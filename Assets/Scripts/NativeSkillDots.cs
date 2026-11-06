using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 282 技能的持续伤害（DoT / `dot` + `dotTime` + `dotInterval`）。
//
// 商业端把持续伤害写成三条成组的字段：`dot`=每跳伤害%、`dotInterval`=跳间隔秒、
// `dotTime`=总时长秒。项目此前**一条都没结算**（全代码搜不到 dotInterval），
// 30 条持续伤害技能只出首段 `damage`，后面几十跳全丢 —— 对蓝焰恐惧、暴风雪这类
// 技能等于少了一大半输出，而且怪物血条是「一下子掉一段」而不是持续往下走。
//
// 每跳的数值口径：源端每跳按 `dot%` 独立计算。这里不重新 roll，
// 而是把**首段真实伤害的行**按 `dot% / damage%` 等比缩放（`NativeSkillReplay.Adapt`）——
// 这样既保留暴击/无视/加成的整条乘区，也不会因为另起一条 roll 通道而和首段差一截。
//
// 边界（不猜）：
//   · 同技能同目标重复命中 = **刷新**，不是叠加（源端 dot 是覆盖）；
//   · 目标死亡 / 换图 / 回城 → 立即停止（协程每跳检查）；
//   · 只吃 `dotTime` 写明的技能。6 条放置型领域（`1241500` 等）时长由
//     `updatableTime`/`ndTime` 管，在 tools/ms282/export_skill_dots.py 里登记为
//     `unresolved`，没有硬套。
public partial class SkillSystem
{
    sealed class DotSpec
    {
        public int Max;
        // 每级 [每跳伤害%, 持续秒, 间隔秒]；顺序与 export_skill_dots.py 的 lv 数组一致。
        public List<float[]> Levels = new List<float[]>();
    }

    sealed class DotMark
    {
        public int Skill;
        public double Next, Until;
        public float DotPercent, Interval, Seconds;
        public double FromPercent;
        public Vector2 Origin;
        public int Facing;
    }

    static Dictionary<int, DotSpec> dotSpecs;
    static bool dotsLoaded;

    static Dictionary<int, DotSpec> Dots
    {
        get
        {
            if (dotsLoaded) return dotSpecs;
            dotsLoaded = true;
            dotSpecs = new Dictionary<int, DotSpec>();
            var asset = Resources.Load<TextAsset>("NativeSkillDots");
            if (asset == null)
            {
                Debug.LogWarning("[Dot] 缺 Assets/Resources/NativeSkillDots.json，持续伤害不会结算");
                return dotSpecs;
            }
            if (!(MiniJson.Parse(asset.text) is Dictionary<string, object> root) ||
                !root.TryGetValue("skills", out var raw) ||
                !(raw is Dictionary<string, object> skills))
                return dotSpecs;
            foreach (var pair in skills)
            {
                // 技能 id 一律走 long：float 会把 400011072 舍成 400011072 附近的整十数。
                if (!long.TryParse(pair.Key, out long id) || id <= 0 || id > int.MaxValue) continue;
                if (!(pair.Value is Dictionary<string, object> rec)) continue;
                if (!rec.TryGetValue("lv", out var rawLevels) || !(rawLevels is List<object> levels)) continue;
                var spec = new DotSpec { Max = Mathf.Max(1, Art.Int(rec, "max", levels.Count)) };
                foreach (var item in levels)
                {
                    if (!(item is List<object> row) || row.Count < 3) { spec.Levels.Add(null); continue; }
                    spec.Levels.Add(new[]
                    {
                        Convert.ToSingle(row[0]), Convert.ToSingle(row[1]), Convert.ToSingle(row[2]),
                    });
                }
                if (spec.Levels.Count == 0) continue;
                dotSpecs[(int)id] = spec;
            }
            if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
                Debug.Log("[Dot] 载入 " + dotSpecs.Count + " 条持续伤害技能");
            return dotSpecs;
        }
    }

    // 目标 → 各技能的上一次 DoT 协程代号。用代号而不是句柄：重挂时旧协程看到
    // 代号变了就自己退出，不需要 StopCoroutine（Stop 的对象可能已经被销毁）。
    readonly Dictionary<Component, Dictionary<int, int>> dotTokens =
        new Dictionary<Component, Dictionary<int, int>>();
    int dotSerial;

    /// <summary>命中之后：对每个被命中的目标按源端 dot 参数起一条持续伤害。</summary>
    void ApplyDots(SkillDef cast, List<CapturedReplayHit> captured)
    {
        if (cast?.native == null || captured == null || captured.Count == 0) return;
        bool debug = PlayerPrefs.GetInt("debug_hitlog", 0) != 0;
        if (!Dots.TryGetValue(cast.native.Id, out var spec))
        {
            if (debug) Debug.Log("[DotCall] id=" + cast.native.Id + " 不在 DoT 表里（captured=" + captured.Count + "）");
            return;
        }
        int level = Mathf.Clamp(cast.native.Level, 1, Mathf.Min(spec.Max, spec.Levels.Count));
        var row = level >= 1 && level <= spec.Levels.Count ? spec.Levels[level - 1] : null;
        if (row == null || row[0] <= 0f || row[1] <= 0f || row[2] <= 0f) return;

        var body = SkillBook.Native(cast.native.Id, cast.native.Level);
        if (body == null) return;

        var seen = new HashSet<Component>();
        int alive = 0;
        foreach (var hit in captured)
        {
            var target = hit.Target;
            if (target == null || !TargetAlive(target)) continue;
            alive++;
            if (!seen.Add(target)) continue;              // 同一次结算里同一目标只挂一条

            var mark = new DotMark
            {
                Skill = cast.native.Id,
                DotPercent = row[0],
                Seconds = row[1],
                Interval = row[2],
                FromPercent = cast.native.DamagePercent,
                Origin = p != null ? p.FeetPos : target.transform.position,
                Facing = p != null ? p.facing : 1,
            };
            var source = ProcSource(captured, target, cast.native);
            if (source.Length == 0) continue;

            if (!dotTokens.TryGetValue(target, out var perTarget))
                dotTokens[target] = perTarget = new Dictionary<int, int>();
            perTarget[cast.native.Id] = ++dotSerial;
            // 挂上就记一行：目标可能活不到第一跳（测试场小怪只有几十血），
            // 「有没有挂上 + 参数对不对」必须能单独观察到，否则会被误判成「DoT 没接」。
            if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
                Debug.Log("[DotApply] id=" + cast.native.Id + " 每跳=" + mark.DotPercent +
                          "% 间隔=" + mark.Interval + "s 时长=" + mark.Seconds + "s");
            StartCoroutine(DotTicks(cast, body, target, mark, source, perTarget[cast.native.Id]));
        }
        // 「hit 里有目标但全都已死」是最容易被误读成「DoT 没接」的情形：
        // 测试场小怪只有几十血，首段就死了，源端也不会让 DoT 跳在死目标上。
        if (debug && alive == 0)
            Debug.Log("[DotCall] id=" + cast.native.Id + " 命中目标已全部死亡，不挂 DoT（captured=" + captured.Count + "）");
    }

    IEnumerator DotTicks(SkillDef cast, NativeSkillDefinition body, Component target, DotMark mark,
        DamageResult[] source, int token)
    {
        // 跳数由源端参数算，不按墙钟判：`dotTime / dotInterval` = 应跳次数。
        // 用 `while (now + interval <= end)` 判会在 batchmode / 掉帧时少跳 ——
        // 首跳之后只要卡一帧（换图、spawn），第二跳的条件就不成立，实测只剩 1 跳。
        int planned = Mathf.Max(1, Mathf.RoundToInt(mark.Seconds / Mathf.Max(.05f, mark.Interval)));
        var stage = new NativeReplayStage { Total = true, Interval = 0 };
        long total = 0;
        int ticks = 0;
        for (int i = 0; i < planned; i++)
        {
            yield return new WaitForSeconds(mark.Interval);
            if (p == null || p.hp <= 0 || GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Playing) break;
            if (target == null || !TargetAlive(target)) break;
            // 被同技能重新命中（或换图清了）→ 这条协程作废，交给新的那条。
            if (!dotTokens.TryGetValue(target, out var perTarget) ||
                !perTarget.TryGetValue(mark.Skill, out int current) || current != token) break;

            var lines = NativeSkillReplay.Adapt(source, 1, mark.FromPercent, mark.DotPercent,
                GameSession.Character?.record.damageCap ?? CombatRules.InitialDamageCap);
            if (lines.Length == 0) break;
            ApplyReplayHit(cast, body, target, lines, mark.Origin, mark.Facing, stage);
            total += lines[0].Amount;
            ticks++;
        }
        if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[Dot] id=" + mark.Skill + " 跳=" + ticks + " 每跳=" + mark.DotPercent + "%" +
                      " 间隔=" + mark.Interval + "s 合计=" + total);
    }

    /// <summary>换图 / 死亡：把标记全清掉，旧协程看到 token 不匹配会自己退出。</summary>
    void ClearDots()
    {
        foreach (var perTarget in dotTokens.Values)
            foreach (var skill in new List<int>(perTarget.Keys)) perTarget[skill] = -1;
        dotTokens.Clear();
    }

    /// <summary>探针用：该技能在给定等级的 DoT 参数（没有则 false）。</summary>
    public static bool DotSpecOf(int skillId, int level, out float dmg, out float seconds, out float interval)
    {
        dmg = seconds = interval = 0f;
        if (!Dots.TryGetValue(skillId, out var spec)) return false;
        int index = Mathf.Clamp(level, 1, Mathf.Min(spec.Max, spec.Levels.Count)) - 1;
        if (index < 0 || index >= spec.Levels.Count || spec.Levels[index] == null) return false;
        var row = spec.Levels[index];
        dmg = row[0]; seconds = row[1]; interval = row[2];
        return dmg > 0f && seconds > 0f && interval > 0f;
    }

    public static int DotSkillCount => Dots.Count;
}
