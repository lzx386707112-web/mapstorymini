using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 282「施放上诅咒 → 命中被诅咒目标时生成冰塊」：400011121 極冰風暴。
//
// 上一轮把这条判成「common 没有 damage/attackCount/psdSkill，放行就是 79 帧特效 + 0 伤害」。
// 回商业端看才知道整条链是三个技能，只是我们没接线：
//   400011121 母技能   施放时上诅咒；effect 23 帧 + special 4 组（就是那 79 帧）
//   400011123 光芒状态 invisible=1，time=20 / subTime=5000 / mobCount=5
//   400011122 冰塊伤害体 invisible=1，damage=640+25*x / attackCount=4 / mobCount=15 /
//             cooltimeMS=400 / lt=[-750,-750] rb=[750,400]，special/special0/hit
//
// 所有数值来自 Assets/Resources/NativeSkillCurses.json
// （tools/ms282/export_skill_curses.py 从商业端三条 common + h 逐级交叉校验导出）；
// 冰塊伤害/段数由 SkillBook.Native(400011122, level) 从 IMG 展开，本文件不写死常量。
//
// 关键：母技能 common 没有 damage/attackCount/time/prop，SkillBook.Get() 按字段猜会
// 留在 passive ⇒ 按下去只回「被动技能无需施放」。所以 IsCurseParent() 把它登记成
// 主动 Buff（Cast 协程在 Buff 分支 yield break，不会凭空多打一发）。
public partial class SkillSystem
{
    sealed class CurseMark
    {
        public double startedAt, until;
        public int stacks;
    }

    sealed class CurseSpec
    {
        public int Parent, State, Body;
        public Dictionary<string, object> Row;
    }

    static Dictionary<string, object> curseData;
    static Dictionary<int, bool> curseParents;
    readonly Dictionary<Component, CurseMark> curseMarks = new Dictionary<Component, CurseMark>();
    static readonly Dictionary<int, CurseSpec> curseSpecs = new Dictionary<int, CurseSpec>();
    double curseActiveUntil, iceNext;
    int curseLevel;
    long curseGeneration;
    CurseSpec curseActive;

    static void EnsureCurses()
    {
        if (curseData != null) return;
        var text = Resources.Load<TextAsset>("NativeSkillCurses")?.text;
        curseData = string.IsNullOrEmpty(text)
            ? new Dictionary<string, object>()
            : (Dictionary<string, object>)MiniJson.Parse(text);
        curseParents = new Dictionary<int, bool>();
        if (!curseData.TryGetValue("curses", out var raw) || !(raw is Dictionary<string, object> table)) return;
        foreach (var pair in table)
            if (int.TryParse(pair.Key, out int id)) curseParents[id] = true;
    }

    /// <summary>SkillBook 分类用：这些是主动上 debuff 的技能，不能判 passive。</summary>
    public static bool IsCurseParent(int id)
    {
        EnsureCurses();
        return curseParents.ContainsKey(id);
    }

    static int CurseInt(Dictionary<string, object> row, string key, int fallback = 0)
        => row != null && row.TryGetValue(key, out var raw) ? Convert.ToInt32(raw) : fallback;

    static CurseSpec CurseFor(int parentId, int level)
    {
        EnsureCurses();
        int key = parentId * 100 + Mathf.Clamp(level, 1, 30);
        if (curseSpecs.TryGetValue(key, out var cached)) return cached;
        if (!curseData.TryGetValue("curses", out var raw) || !(raw is Dictionary<string, object> table)) return null;
        if (!table.TryGetValue(parentId.ToString(), out var entry) || !(entry is Dictionary<string, object> spec)) return null;
        var levels = spec.TryGetValue("levels", out var lv) ? lv as Dictionary<string, object> : null;
        if (levels == null || !levels.TryGetValue(Mathf.Clamp(level, 1, 30).ToString(), out var rowObj)
            || !(rowObj is Dictionary<string, object> row)) return null;
        var result = new CurseSpec
        {
            Parent = parentId,
            State = CurseInt(spec, "state"),
            Body = CurseInt(spec, "body"),
            Row = row,
        };
        curseSpecs[key] = result;
        return result;
    }

    /// <summary>母技能施放成功：给范围内敌人上诅咒，并启动 periodic 光芒补层。</summary>
    public void StartCurse(SkillDef cast)
    {
        if (cast == null || cast.native == null || p == null) return;
        var spec = CurseFor(cast.native.Id, cast.native.Level);
        if (spec == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        curseActive = spec;
        curseLevel = cast.native.Level;
        curseActiveUntil = now + CurseInt(spec.Row, "lightDurationSec", 20);
        iceNext = 0;
        long generation = ++curseGeneration;
        Vector2 origin = p.FeetPos;
        int facing = p.facing;
        ApplyCurse(spec, origin, facing, now);
        if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[Curse] cast parent=" + spec.Parent + " lv=" + curseLevel +
                      " marks=" + curseMarks.Count + " until=+" +
                      CurseInt(spec.Row, "lightDurationSec", 20) + "s");
        var field = FindObjectOfType<FieldDirector>();
        var map = field != null ? field.Map : null;
        StartCoroutine(CurseLights(spec, generation, origin, facing, field, map));
    }

    void ApplyCurse(CurseSpec spec, Vector2 origin, int facing, double now)
    {
        var targets = ReplayTargets(CurseBody(spec), origin, facing,
            Mathf.Clamp(CurseInt(spec.Row, "curseTargets", 10), 1, 15), false);
        int duration = CurseInt(spec.Row, "curseDurationSec", 15);
        int cap = CurseInt(spec.Row, "refreshCapSec", 20);
        int maxStacks = Mathf.Max(1, CurseInt(spec.Row, "maxStacks", 6));
        int applied = 0;
        foreach (var target in targets)
        {
            if (!TargetAlive(target)) continue;
            if (!curseMarks.TryGetValue(target, out var mark))
            {
                mark = new CurseMark { startedAt = now, stacks = 0 };
                curseMarks[target] = mark;
            }
            mark.stacks = Mathf.Min(maxStacks, mark.stacks + 1);
            mark.until = Mathf.Min((float)(mark.startedAt + cap), (float)(now + duration));
            applied++;
        }
        if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[Curse] apply targets=" + targets.Count + " applied=" + applied +
                      " marks=" + curseMarks.Count + " duration=" + duration + "s");
    }

    NativeSkillDefinition CurseBody(CurseSpec spec)
        => SkillBook.Native(spec.Parent, curseLevel);

    // 光芒状态 400011123 自带自己的框（lt/rb=-550,-350..550,200）与 mobCount=5，
    // 和母技能 400011121 的框不是一套；拿母技能的框选光芒目标会多/少人。
    NativeSkillDefinition CurseState(CurseSpec spec)
        => SkillBook.Native(spec.State, curseLevel) ?? SkillBook.Native(spec.Parent, curseLevel);

    // 400011123：20 秒内每 subTime=5000ms 一次光芒，每次给最多 5 名敌人叠一层。
    IEnumerator CurseLights(CurseSpec spec, long generation, Vector2 origin, int facing,
        FieldDirector field, FieldMap map)
    {
        double period = CurseInt(spec.Row, "lightIntervalMs", 5000) / 1000.0;
        int targets = CurseInt(spec.Row, "lightTargets", 5);
        int maxStacks = Mathf.Max(1, CurseInt(spec.Row, "maxStacks", 6));
        for (int tick = 1; ; tick++)
        {
            double due = curseActiveUntil - CurseInt(spec.Row, "lightDurationSec", 20) + tick * period;
            while (Time.realtimeSinceStartupAsDouble < due) yield return null;
            if (due > curseActiveUntil || curseGeneration != generation || !ReplayAlive(field, map)) yield break;
            // 光芒用的是状态 400011123 的判定框与 mobCount，这里只按条数取样补层。
            var list = ReplayTargets(CurseState(spec), p.FeetPos, p.facing,
                Mathf.Clamp(targets, 1, 15), false);
            double now = Time.realtimeSinceStartupAsDouble;
            int duration = CurseInt(spec.Row, "curseDurationSec", 15);
            int cap = CurseInt(spec.Row, "refreshCapSec", 20);
            foreach (var target in list)
            {
                if (!TargetAlive(target)) continue;
                if (!curseMarks.TryGetValue(target, out var mark))
                {
                    mark = new CurseMark { startedAt = now, stacks = 0 };
                    curseMarks[target] = mark;
                }
                mark.stacks = Mathf.Min(maxStacks, mark.stacks + 1);
                mark.until = Mathf.Min((float)(mark.startedAt + cap), (float)(now + duration));
            }
            PlayNativeReplayEffect(spec.State, p.FeetPos, p.facing);
        }
    }

    /// <summary>由 TriggerReactiveSkills 在每次真实命中后调用。</summary>
    void TriggerCurseOnHit(SkillDef cast, List<CapturedReplayHit> captured, Vector2 origin, int facing)
    {
        bool debug = PlayerPrefs.GetInt("debug_hitlog", 0) != 0;
        if (curseActive == null || cast == null || cast.native == null)
        {
            if (debug && cast != null && cast.native != null && cast.native.Id == 400011121)
                Debug.Log("[Curse] skip: no active curse");
            return;
        }
        double now = Time.realtimeSinceStartupAsDouble;
        if (now > curseActiveUntil || cast.native.Kind != "Attack" || cast.native.Id == curseActive.Body)
            return;
        if (captured == null || captured.Count == 0)
        {
            if (debug) Debug.Log("[Curse] skip " + cast.native.Id + ": 无命中记录");
            return;
        }
        // 必须命中「被诅咒」的目标才生成冰塊（h「以直接攻擊的技能命中被施加詛咒的敵人時」）。
        bool cursed = false;
        int stacks = 1;
        foreach (var hit in captured)
        {
            if (hit.Target == null) continue;
            if (!curseMarks.TryGetValue(hit.Target, out var mark))
            {
                if (debug) Debug.Log("[Curse] skip " + cast.native.Id + ": 目标未被诅咒 marks=" + curseMarks.Count);
                continue;
            }
            if (now > mark.until) continue;
            cursed = true;
            stacks = Mathf.Max(stacks, mark.stacks);
            // 命中会把诅咒持续时间刷新，但不超过 h 的 q2 上限。
            mark.until = Mathf.Min((float)(mark.startedAt + CurseInt(curseActive.Row, "refreshCapSec", 20)),
                                   (float)(now + CurseInt(curseActive.Row, "curseDurationSec", 15)));
        }
        if (!cursed || now < iceNext) return;
        iceNext = now + CurseInt(curseActive.Row, "iceCooldownMs", 400) / 1000.0;

        var bodyDef = SkillBook.Native(curseActive.Body, curseLevel);
        if (bodyDef == null || bodyDef.DamagePercent <= 0) return;
        PlayNativeReplayEffect(curseActive.Body, origin, facing);
        var source = captured.Count > 0 ? captured[0].Hits : Array.Empty<DamageResult>();
        if (source.Length == 0 && p != null) source = p.ReplayFallback(cast.native, 1, true);
        if (source.Length == 0) return;
        // 冰塊是范围攻击：按 400011122 的 lt/rb 取最多 iceTargets 名敌人。
        var targets = ReplayTargets(bodyDef, origin, facing,
            Mathf.Clamp(CurseInt(curseActive.Row, "iceTargets", 15), 1, 15), false);
        int bonus = CurseInt(curseActive.Row, "bonusHitsPerStack", 1);
        int hits = Mathf.Max(1, bodyDef.HitCount + Mathf.Max(0, stacks - 1) * bonus);
        foreach (var target in targets)
        {
            if (!TargetAlive(target)) continue;
            FireIce(bodyDef, source, cast.native.DamagePercent, target, origin, facing, bodyDef.DamagePercent, hits);
        }
        if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[Curse] ice body=" + curseActive.Body + " stacks=" + stacks +
                      " hits=" + hits + " targets=" + targets.Count);
    }

    // 同 NativeProcSkills.FireProc，但段数要按叠层动态给，所以自己走一次 Adapt。
    // ApplyReplayHit 不经过 HitAt ⇒ 不会递归触发 TriggerReactiveSkills。
    void FireIce(NativeSkillDefinition body, DamageResult[] source, double fromPercent,
        Component target, Vector2 origin, int facing, double percent, int hits)
    {
        var lines = NativeSkillReplay.Adapt(source, hits, fromPercent, percent,
            GameSession.Character?.record.damageCap ?? CombatRules.InitialDamageCap);
        var cast = new SkillDef(new ClassSkill { name = body.Id.ToString(), art = "", icon = "" }, body);
        ApplyReplayHit(cast, body, target, lines, origin, facing,
            new NativeReplayStage { Total = true, Interval = 120 });
    }

    /// <summary>换图 / 死亡：状态与协程全部失效。</summary>
    void ClearCurses()
    {
        curseGeneration++;
        curseActiveUntil = 0;
        curseActive = null;
        curseMarks.Clear();
    }
}
