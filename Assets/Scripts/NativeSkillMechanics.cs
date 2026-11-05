using System;
using System.Collections.Generic;
using UnityEngine;

// 282 剩余 6 条「buff 无 stat / 只挂计时器」的真实机制。
//
// 它们不是属性 buff，所以 NativeBuffs.SkillStats 永远返回空、按下去什么都没有。
// 查 282 原始节点后确认每条都有独立实体：资源计数、追加方向、符咒、技能替换、
// 元素吸收堆叠、隐身回避。数值全部来自 Assets/Resources/NativeSkillMechanics.json
// （tools/ms282/export_skill_mechanics.py 逐级自证 + h 原文证据），C# 不写死伤害常量。
//
//   5141011 海龙石VI     每次发动海龙技能 +1 石（上限 u=5），攒满消耗 5 个进入 15 秒
//                        突击状态；状态内命中按 5141013/5141014 结算；被动 damPlus 给
//                        海龙之怒VI/爆裂VI 追加一段同面板行伤害。
//   400041001 散式投擲   期间投掷技能命中 → 三飞闪追加 4 方向 u%、四飞闪追加 3 方向 damage%。
//   400041061 飛閃起爆符 主动给 x=46 个符咒（60 秒），被动每 subTime=10 秒攒 1 个；
//                        主/被动互斥；每次投掷技能用 s..w 个符咒各结算一次爆炸体 400041062。
//   5141001 超級閃‧連殺 海龙怒击发动 → 10 秒内把 5141000 替换成 5141002，命中后追加
//                        冲击波 5141003。
//   2141502 劇毒煉獄     元素吸收上限锁满 10 层 ⇒ FinalDamage +30（走 NativeBuffs 注册）。
//   4241005 黑暗面       隐身期间回避「按最大HP 比率」的伤害，解除后按次数叠隐身术冷却。
public static class NativeSkillMechanics
{
    static Dictionary<string, object> table;

    static Dictionary<string, object> Table
    {
        get
        {
            if (table == null)
            {
                var text = Resources.Load<TextAsset>("NativeSkillMechanics")?.text;
                table = string.IsNullOrEmpty(text)
                    ? new Dictionary<string, object>()
                    : (Dictionary<string, object>)MiniJson.Parse(text);
            }
            return table;
        }
    }

    static Dictionary<string, object> Mechanics
        => Table.TryGetValue("mechanics", out var raw) ? raw as Dictionary<string, object> : null;

    public static Dictionary<string, object> Spec(int id)
    {
        var all = Mechanics;
        if (all == null || !all.TryGetValue(id.ToString(), out var raw)) return null;
        return raw as Dictionary<string, object>;
    }

    public static int Int(int id, string key, int fallback = 0)
    {
        var spec = Spec(id);
        return spec != null && spec.TryGetValue(key, out var raw) ? Convert.ToInt32(raw) : fallback;
    }

    public static string Str(int id, string key)
    {
        var spec = Spec(id);
        return spec != null && spec.TryGetValue(key, out var raw) ? Convert.ToString(raw) : "";
    }

    public static int[] Ints(int id, string key)
    {
        var spec = Spec(id);
        if (spec == null || !spec.TryGetValue(key, out var raw) || !(raw is List<object> list))
            return new int[0];
        var result = new int[list.Count];
        for (int i = 0; i < list.Count; i++) result[i] = Convert.ToInt32(list[i]);
        return result;
    }

    public static List<Dictionary<string, object>> Variants(int id)
    {
        var result = new List<Dictionary<string, object>>();
        var spec = Spec(id);
        if (spec == null || !spec.TryGetValue("variants", out var raw) || !(raw is List<object> list))
            return result;
        foreach (var item in list)
            if (item is Dictionary<string, object> row) result.Add(row);
        return result;
    }

    public static bool InList(int id, string key, int value)
    {
        foreach (int candidate in Ints(id, key)) if (candidate == value) return true;
        return false;
    }

    public static bool InList(Dictionary<string, object> row, string key, int value)
    {
        if (row == null || !row.TryGetValue(key, out var raw) || !(raw is List<object> list)) return false;
        foreach (var item in list) if (Convert.ToInt32(item) == value) return true;
        return false;
    }

    public static int[] AllIds()
    {
        var all = Mechanics;
        if (all == null) return new int[0];
        var result = new List<int>();
        foreach (var key in all.Keys) if (int.TryParse(key, out int id)) result.Add(id);
        return result.ToArray();
    }

    /// <summary>
    /// 剧毒炼狱状态：元素吸收的满层最终伤害加成。
    /// 数值在导出期由「状态 x=10 层 × 2100009.common.x=3%/层」算出，写进
    /// NativeSkillStates.json 的 effectKey/effectPct/elementAbsorbCap，这里只注入。
    /// NativeBuffs.SkillStats 的 timedEffect 分支调用。
    /// </summary>
    public static void AddTimedEffect(Dictionary<string, int> stats, int stateId)
    {
        if (stats == null) return;
        var spec = NativeSkillStates.Spec(stateId);
        if (spec == null || !spec.TryGetValue("effectKey", out var rawKey)) return;
        int pct = NativeSkillStates.Int(stateId, "effectPct");
        int cap = NativeSkillStates.Int(stateId, "elementAbsorbCap");
        if (pct <= 0 || cap <= 0) return;
        string key = Convert.ToString(rawKey);
        if (string.IsNullOrEmpty(key)) return;
        int had;
        stats.TryGetValue(key, out had);
        stats[key] = Mathf.Max(had, pct);
    }
}

public partial class SkillSystem
{
    readonly Dictionary<int, int> mechResources = new Dictionary<int, int>();
    readonly Dictionary<int, double> mechUntil = new Dictionary<int, double>();
    readonly Dictionary<int, double> mechNextPassive = new Dictionary<int, double>();
    readonly Dictionary<int, int> mechBursts = new Dictionary<int, int>();
    double darkSightBlockedUntil;
    int darkSightEvades;
    bool darkSightWasActive;
    bool mechReplaceShock;
    int mechReplaceLevel;

    static int MechanicId(string kind)
    {
        foreach (int id in NativeSkillMechanics.AllIds())
            if (NativeSkillMechanics.Str(id, "kind") == kind) return id;
        return 0;
    }

    static Dictionary<string, object> MechRow(int id)
        => SkillBook.Get(id)?.Level(Mathf.Max(1, SkillProgress.LevelOf(id)));

    /// <summary>读表登记的字段名 → 从该技能当前等级行取数值。</summary>
    static int MechField(int id, string fieldKey)
    {
        string field = NativeSkillMechanics.Str(id, fieldKey);
        var row = MechRow(id);
        return string.IsNullOrEmpty(field) || row == null ? 0 : Art.Int(row, field);
    }

    int Resource(int id)
    {
        int value;
        return mechResources.TryGetValue(id, out value) ? value : 0;
    }

    bool MechanicActive(int id, double now)
    {
        double until;
        return mechUntil.TryGetValue(id, out until) && now < until;
    }

    // ——— 施放前：资源门 / 隐身冷却 / 技能替换 ———

    string MechanicCastGate(int id, int level)
    {
        SettleDarkSight();
        int dark = MechanicId("darkSight");
        if (dark > 0 && id == NativeSkillMechanics.Int(dark, "parent")
            && NativeBuffs.Now < darkSightBlockedUntil)
            return "隐身术冷却中（黑暗面回避）";
        int burst = MechanicId("resourceBurst");
        if (burst > 0 && id == burst)
        {
            int cost = MechField(burst, "costField");
            if (cost > 0 && Resource(burst) < cost)
                return "海龙石不足（需要 " + cost + " 个）";
        }
        return null;
    }

    SkillDef ResolveMechanicReplacement(SkillDef s)
    {
        if (p == null || s == null || s.native == null) return s;
        int mid = MechanicId("replace");
        if (mid <= 0 || s.native.Id != NativeSkillMechanics.Int(mid, "source")) return s;
        var states = NativeSkillStates.For(p);
        if (!states.Has(NativeSkillMechanics.Int(mid, "state"))) return s;
        int[] bodies = NativeSkillMechanics.Ints(mid, "bodies");
        if (bodies.Length == 0) return s;
        var def = SkillBook.Native(bodies[0],
            Mathf.Clamp(s.native.Level, 1, Mathf.Max(1, SkillBook.MaxLevelOf(bodies[0]))));
        if (def == null || def.DamagePercent <= 0) return s;
        mechReplaceShock = true;
        mechReplaceLevel = s.native.Level;
        return new SkillDef(new ClassSkill { name = s.name, art = s.art, icon = s.icon }, def);
    }

    /// <summary>施放成功（MP/冷却已扣）后：消耗资源、开状态窗口。</summary>
    void OnMechanicCast(SkillDef s, Vector2 origin, int facing)
    {
        if (p == null || s == null || s.native == null) return;
        double now = NativeBuffs.Now;
        int id = s.native.Id;
        foreach (int mid in NativeSkillMechanics.AllIds())
        {
            string kind = NativeSkillMechanics.Str(mid, "kind");
            if (kind == "darkSight")
            {
                // 隐身术按键本身就开启回避窗口；状态由 NativeSkillStates.Activate 挂上。
                // desc「再次使用隐身术技能键即可解除隐身术状态」：重复按键等于提前解除，
                // 这时要先按已回避次数结算冷却，否则计数会被下面的重置吃掉。
                if (id == NativeSkillMechanics.Int(mid, "parent"))
                {
                    if (darkSightEvades > 0) ApplyDarkSightCooldown();
                    darkSightWasActive = true;
                    darkSightEvades = 0;
                }
                continue;
            }
            if (id != mid) continue;
            switch (kind)
            {
                case "resourceBurst":
                    mechResources[mid] = Mathf.Max(0, Resource(mid) - MechField(mid, "costField"));
                    mechUntil[mid] = now + MechField(mid, "durationField");
                    mechBursts[mid] = 0;
                    break;
                case "spread":
                    mechUntil[mid] = now + MechField(mid, "durationField");
                    break;
                case "charm":
                    mechResources[mid] = MechField(mid, "capField");
                    mechUntil[mid] = now + MechField(mid, "durationField");
                    // 主动效果期间不攒被动符咒；主动结束后再等一个完整周期。
                    mechNextPassive[mid] = now + MechField(mid, "durationField")
                        + MechField(mid, "passiveIntervalField") / 1000.0;
                    break;
            }
        }
    }

    // ——— 命中：全部追加结算 ———

    void OnMechanicHit(SkillDef cast, List<CapturedReplayHit> captured, Vector2 origin, int facing)
    {
        if (p == null || cast == null || cast.native == null || captured == null || captured.Count == 0) return;
        double now = NativeBuffs.Now;
        int cid = cast.native.Id;
        if (mechReplaceShock)
        {
            mechReplaceShock = false;
            FireReplacementShock(cast, captured, origin, facing);
        }
        foreach (int mid in NativeSkillMechanics.AllIds())
        {
            switch (NativeSkillMechanics.Str(mid, "kind"))
            {
                case "resourceBurst":
                    if (NativeSkillMechanics.InList(mid, "gainSkills", cid))
                    {
                        int cap = MechField(mid, "capField");
                        mechResources[mid] = Mathf.Min(Mathf.Max(1, cap), Resource(mid) + 1);
                    }
                    if (NativeSkillMechanics.InList(mid, "bonusTargets", cid))
                        FireDragonBonus(mid, cast, captured, origin, facing);
                    if (MechanicActive(mid, now) && NativeSkillMechanics.InList(mid, "gainSkills", cid))
                        FireDragonBurst(mid, cast, captured, origin, facing);
                    break;
                case "spread":
                    if (MechanicActive(mid, now) && NativeSkillMechanics.InList(mid, "throwSkills", cid))
                        FireSpread(mid, cast, captured, origin, facing);
                    break;
                case "charm":
                    AccumulateCharms(mid, now);
                    if (NativeSkillMechanics.InList(mid, "throwSkills", cid))
                        FireCharms(mid, cast, captured, origin, facing);
                    break;
                case "replace":
                    if (NativeSkillMechanics.InList(mid, "triggerSkills", cid))
                        NativeSkillStates.For(p).ActivateState(
                            NativeSkillMechanics.Int(mid, "state"),
                            Mathf.Max(1, SkillProgress.LevelOf(NativeSkillMechanics.Int(mid, "source"))),
                            MechField(mid, "durationField"));
                    break;
            }
        }
        SettleDarkSight();
    }

    /// <summary>与 FireProc 同一条换算管线：主攻击面板行 → 追加伤害体 damage%。</summary>
    void FireMechanicHit(SkillDef cast, List<CapturedReplayHit> captured, Vector2 origin, int facing,
        NativeSkillDefinition proc, double fromPercent, double percent, int hits, int targetLimit, int visualId)
    {
        if (percent <= 0 || cast == null || cast.native == null || captured == null) return;
        var targets = ProcTargets(captured, Mathf.Clamp(targetLimit, 1, 15), origin);
        if (targets.Count == 0) return;
        if (visualId > 0) PlayNativeReplayEffect(visualId, origin, facing);
        var replay = proc ?? cast.native;
        foreach (var target in targets)
        {
            var source = ProcSource(captured, target, cast.native);
            var lines = NativeSkillReplay.Adapt(source, Mathf.Clamp(hits, 1, 15), fromPercent, percent,
                GameSession.Character?.record.damageCap ?? CombatRules.InitialDamageCap);
            ApplyReplayHit(cast, replay, target, lines, origin, facing,
                new NativeReplayStage { Total = true, Interval = 120 });
        }
    }

    void FireDragonBurst(int mid, SkillDef cast, List<CapturedReplayHit> captured,
        Vector2 origin, int facing)
    {
        int max = MechField(mid, "maxBurstsField");   // w3=10「對同一名敵人發動10次攻擊時消失」
        int used;
        mechBursts.TryGetValue(mid, out used);
        if (max > 0 && used >= max) return;
        mechBursts[mid] = used + 1;
        int level = Mathf.Max(1, SkillProgress.LevelOf(mid));
        // h「[被動效果：海龍之怒VI傷害增加#damPlus%p]」。282 里 5140004/5141006/5141008
        // 是 passive / invisible 伤害体，本工程没有它们的独立施放链；把 damPlus 落在
        // 同族突击伤害体（5141013/5141014）的倍率上，保证这条被动不是死代码。
        // 若将来那两个体真的作为 cast 出现，FireDragonBonus 会另外再结算一次。
        double passiveBonus = 1.0 + Mathf.Max(0, MechField(mid, "bonusField")) / 100.0;
        foreach (int bodyId in NativeSkillMechanics.Ints(mid, "bodies"))
        {
            var body = SkillBook.Native(bodyId,
                Mathf.Clamp(level, 1, Mathf.Max(1, SkillBook.MaxLevelOf(bodyId))));
            if (body == null || body.DamagePercent <= 0) continue;
            FireMechanicHit(cast, captured, origin, facing, body, cast.native.DamagePercent,
                body.DamagePercent * passiveBonus, body.HitCount, body.TargetCount, bodyId);
        }
    }

    /// <summary>h「[被動效果：海龍之怒VI傷害增加#damPlus%p]」：同一目标同一面板行追加一段。</summary>
    void FireDragonBonus(int mid, SkillDef cast, List<CapturedReplayHit> captured,
        Vector2 origin, int facing)
    {
        double pct = MechField(mid, "bonusField");
        if (pct <= 0) return;
        FireMechanicHit(cast, captured, origin, facing, cast.native, cast.native.DamagePercent,
            pct, 1, Mathf.Clamp(cast.native.TargetCount, 1, 15), 0);
    }

    void FireSpread(int mid, SkillDef cast, List<CapturedReplayHit> captured,
        Vector2 origin, int facing)
    {
        var variants = NativeSkillMechanics.Variants(mid);
        if (variants.Count == 0) return;
        Dictionary<string, object> chosen = null;
        foreach (var variant in variants)
            if (NativeSkillMechanics.InList(variant, "triggers", cast.native.Id)) { chosen = variant; break; }
        // 名单外的投掷类技能按四飞闪的追加方向处理（h 只写两种投掷技能）。
        if (chosen == null) chosen = variants[variants.Count - 1];
        int directions = Convert.ToInt32(chosen["directions"]);
        var row = MechRow(mid);
        if (row == null) return;
        double pct = Art.Int(row, Convert.ToString(chosen["field"]));
        int mobs = Art.Int(row, Convert.ToString(chosen["mobField"]), 1);
        for (int i = 0; i < directions; i++)
            FireMechanicHit(cast, captured, origin, facing, cast.native,
                cast.native.DamagePercent, pct, 1, mobs, mid);
    }

    void AccumulateCharms(int mid, double now)
    {
        // 「主動效果和被動效果不會同時發動」：主动窗口内完全不攒被动符咒。
        if (MechanicActive(mid, now)) return;
        int intervalMs = MechField(mid, "passiveIntervalField");
        if (intervalMs <= 0) return;
        double next;
        if (!mechNextPassive.TryGetValue(mid, out next))
        { mechNextPassive[mid] = now + intervalMs / 1000.0; return; }
        if (now < next) return;
        int ticks = Mathf.Clamp((int)((now - next) * 1000 / intervalMs) + 1, 1, 64);
        int cap = Mathf.Max(1, MechField(mid, "capField"));
        mechResources[mid] = Mathf.Min(cap, Resource(mid) + ticks);
        mechNextPassive[mid] = next + ticks * intervalMs / 1000.0;
    }

    void FireCharms(int mid, SkillDef cast, List<CapturedReplayHit> captured,
        Vector2 origin, int facing)
    {
        int available = Resource(mid);
        int min = Mathf.Max(1, MechField(mid, "minPerCastField"));
        int max = Mathf.Max(min, MechField(mid, "maxPerCastField"));
        if (available < min) return;
        int use = Mathf.Min(available, max);
        mechResources[mid] = available - use;
        var row = MechRow(mid);
        if (row == null) return;
        double pct = Art.Int(row, NativeSkillMechanics.Str(mid, "damageField"));
        int hits = Art.Int(row, NativeSkillMechanics.Str(mid, "hitsField"), 1);
        int mobs = Art.Int(row, NativeSkillMechanics.Str(mid, "mobsField"), 1);
        int visual = NativeSkillMechanics.Int(mid, "body");
        for (int i = 0; i < use; i++)
            FireMechanicHit(cast, captured, origin, facing, cast.native,
                cast.native.DamagePercent, pct, hits, mobs, visual);
    }

    void FireReplacementShock(SkillDef cast, List<CapturedReplayHit> captured,
        Vector2 origin, int facing)
    {
        int mid = MechanicId("replace");
        if (mid <= 0) return;
        int[] bodies = NativeSkillMechanics.Ints(mid, "bodies");
        if (bodies.Length < 2) return;
        int shock = bodies[1];
        var def = SkillBook.Native(shock,
            Mathf.Clamp(mechReplaceLevel, 1, Mathf.Max(1, SkillBook.MaxLevelOf(shock))));
        if (def == null || def.DamagePercent <= 0) return;
        FireMechanicHit(cast, captured, origin, facing, def, cast.native.DamagePercent,
            def.DamagePercent, def.HitCount, def.TargetCount, shock);
    }

    // ——— 隐身回避（PlayerCtrl.TakeBossDamage 调用）———

    /// <summary>
    /// 4001003 h「迴避依照最大HP的比率造成傷害的攻擊時」：本工程的 TakeBossDamage
    /// 就是 Boss 百分比/固定伤害的唯一通路，所以挂在这里，物理接触伤害不受影响。
    /// </summary>
    public bool TryEvadeHpRatioDamage()
    {
        if (p == null || p.hp <= 0) return false;
        int mid = MechanicId("darkSight");
        if (mid <= 0) return false;
        if (!NativeSkillStates.For(p).Has(NativeSkillMechanics.Int(mid, "state"))) return false;
        int maxEvades = MechField(mid, "maxEvadesField");
        if (maxEvades > 0 && darkSightEvades >= maxEvades) return false;
        darkSightEvades++;
        darkSightWasActive = true;
        return true;
    }

    void SettleDarkSight()
    {
        int mid = MechanicId("darkSight");
        if (mid <= 0 || p == null) return;
        if (NativeSkillStates.For(p).Has(NativeSkillMechanics.Int(mid, "state")))
        { darkSightWasActive = true; return; }
        if (!darkSightWasActive) return;
        darkSightWasActive = false;
        if (darkSightEvades > 0) ApplyDarkSightCooldown();
    }

    void ApplyDarkSightCooldown()
    {
        int mid = MechanicId("darkSight");
        if (mid <= 0 || darkSightEvades <= 0) return;
        // h「依照迴避的次數套用冷卻時間#cooltime秒，最多可重疊到#y次」
        int per = Mathf.Max(1, NativeSkillMechanics.Int(mid, "cooldownPerEvadeSec", 3));
        int stacks = Mathf.Max(1, NativeSkillMechanics.Int(mid, "maxStacks", 5));
        darkSightBlockedUntil = NativeBuffs.Now + Mathf.Min(darkSightEvades, stacks) * per;
        darkSightEvades = 0;
    }

    void ClearMechanics()
    {
        mechResources.Clear();
        mechUntil.Clear();
        mechNextPassive.Clear();
        mechBursts.Clear();
        darkSightBlockedUntil = 0;
        darkSightEvades = 0;
        darkSightWasActive = false;
        mechReplaceShock = false;
    }
}
