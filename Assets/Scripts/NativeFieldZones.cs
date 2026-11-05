using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 282 六转的「周期领域」：母技能施放后挂上 invisible 子状态，状态期间按 subTime
// 周期结算同族的追加攻击伤害体。过去这 4 条状态只是计时器（NativeSkillStates 记个
// 到期时间就完事），30/60 秒里的十几轮追加攻击整段丢失。
//
// 数值全部来自 Assets/Resources/NativeFieldZones.json（tools/ms282/export_field_zones.py），
// 每条的 duration / interval / damage / attackCount / mobCount 都在 282 母技能的 h
// 与 level 行里逐级自证过；C# 不复制任何伤害常量。
//
//   2241504 殛凍領域  每 s2=2 秒（状态 subTime=2000ms）魔力迸发 2241501（q2% × q3 段 × s 名）
//   3141501 箭影紛飛  攻击命中时启动，每 s=3 秒一次、最多 q2=7 次，伤害体 3141502
//   4141502 禁咒之符  每 1.9 秒 z=5 个巨型飞镖，优先追踪 maxHP 最高的 BOSS，伤害体 4141501
//   14141021 闇黑天魔VI 暗影蝙蝠状态下使用技能 → 弱化蝙蝠群 14141020（60 秒 / 1470ms）
public partial class SkillSystem
{
    sealed class FieldZone
    {
        public int State, Parent, Body;
        public int DurationSec, IntervalMs, MobCount, Projectiles, MaxActivations;
        public string Trigger, TargetMode;
    }

    static Dictionary<string, object> fieldZoneData;
    static List<FieldZone> fieldZones;
    readonly Dictionary<int, long> fieldZoneGeneration = new Dictionary<int, long>();
    readonly Dictionary<int, double> fieldZoneUntil = new Dictionary<int, double>();

    static int ZoneInt(Dictionary<string, object> row, string key, int fallback = 0)
        => row != null && row.TryGetValue(key, out var raw) ? Convert.ToInt32(raw) : fallback;

    static string ZoneStr(Dictionary<string, object> row, string key)
        => row != null && row.TryGetValue(key, out var raw) ? Convert.ToString(raw) : "";

    static List<FieldZone> Zones
    {
        get
        {
            if (fieldZones != null) return fieldZones;
            fieldZones = new List<FieldZone>();
            if (fieldZoneData == null)
            {
                var text = Resources.Load<TextAsset>("NativeFieldZones")?.text;
                fieldZoneData = string.IsNullOrEmpty(text)
                    ? new Dictionary<string, object>()
                    : (Dictionary<string, object>)MiniJson.Parse(text);
            }
            if (!fieldZoneData.TryGetValue("zones", out var raw)) return fieldZones;
            if (!(raw is Dictionary<string, object> table)) return fieldZones;
            foreach (var pair in table)
            {
                if (!(pair.Value is Dictionary<string, object> row)) continue;
                var bodies = row.TryGetValue("bodies", out var listRaw) ? listRaw as List<object> : null;
                if (bodies == null || bodies.Count == 0) continue;
                fieldZones.Add(new FieldZone
                {
                    State = ZoneInt(row, "state"),
                    Parent = ZoneInt(row, "parent"),
                    Body = Convert.ToInt32(bodies[0]),
                    DurationSec = ZoneInt(row, "durationSec"),
                    IntervalMs = ZoneInt(row, "intervalMs"),
                    MobCount = ZoneInt(row, "mobCount", 1),
                    Projectiles = ZoneInt(row, "projectiles", 1),
                    MaxActivations = ZoneInt(row, "maxActivations"),
                    Trigger = ZoneStr(row, "trigger"),
                    TargetMode = ZoneStr(row, "targetMode"),
                });
            }
            return fieldZones;
        }
    }

    /// <summary>母技能施放：立刻启动 onCast 型领域（殛冻领域 / 禁咒之符）。</summary>
    void StartFieldZones(SkillDef cast, Vector2 origin, int facing)
    {
        if (cast == null || cast.native == null) return;
        foreach (var zone in Zones)
            if (zone.Parent == cast.native.Id && zone.Trigger == "onCast")
                BeginFieldZone(zone, cast.native.Level, origin, facing);
    }

    /// <summary>
    /// 攻击命中：启动 onHit 型（箭影纷飞「#time秒內攻擊命中時」）与
    /// onSkillHitWhileState 型（暗黑天魔VI「套用暗影蝙蝠期間使用技能時」）。
    /// 两者都要求状态仍在，且同一状态窗口内只启动一次。
    /// </summary>
    void TriggerFieldZonesOnHit(SkillDef cast, Vector2 origin, int facing)
    {
        if (p == null || cast == null || cast.native == null || cast.native.Kind != "Attack") return;
        var states = NativeSkillStates.For(p);
        double now = Time.realtimeSinceStartupAsDouble;
        foreach (var zone in Zones)
        {
            if (zone.Trigger != "onHit" && zone.Trigger != "onSkillHitWhileState") continue;
            if (!states.Has(zone.State)) continue;
            // 母技能自己那一下不算「狀態期間使用技能」（暗黑天魔VI 的弱化蝙蝠要后续技能触发）。
            if (zone.Trigger == "onSkillHitWhileState" && cast.native.Id == zone.Parent) continue;
            if (fieldZoneUntil.TryGetValue(zone.State, out double until) && now < until) continue;
            BeginFieldZone(zone, Mathf.Max(1, SkillProgress.LevelOf(zone.Parent)), origin, facing);
        }
    }

    void BeginFieldZone(FieldZone zone, int level, Vector2 origin, int facing)
    {
        if (zone.IntervalMs <= 0 || zone.DurationSec <= 0 || zone.Body == 0) return;
        int parentLevel = Mathf.Clamp(level, 1, Mathf.Max(1, SkillBook.MaxLevelOf(zone.Parent)));
        int bodyLevel = Mathf.Clamp(level, 1, Mathf.Max(1, SkillBook.MaxLevelOf(zone.Body)));
        var parent = SkillBook.Native(zone.Parent, parentLevel);
        var body = SkillBook.Native(zone.Body, bodyLevel);
        if (parent == null || body == null || body.DamagePercent <= 0) return;
        // 领域属于母技能：范围取母技能的 lt/rb，命中特效取伤害体自己的 id。
        // 不能沿用触发它的那个技能（onHit 型可能是普攻或其它技能），否则 bounds 会串。
        var host = new SkillDef(new ClassSkill { name = "", art = "", icon = "" }, parent);

        fieldZoneGeneration.TryGetValue(zone.State, out long generation);
        generation++;
        fieldZoneGeneration[zone.State] = generation;
        fieldZoneUntil[zone.State] = Time.realtimeSinceStartupAsDouble + zone.DurationSec;
        var field = FindObjectOfType<FieldDirector>();
        var map = field != null ? field.Map : null;
        StartCoroutine(RunFieldZone(zone, generation, parent, body, host, origin, facing, field, map));
    }

    IEnumerator RunFieldZone(FieldZone zone, long generation, NativeSkillDefinition parent,
        NativeSkillDefinition body, SkillDef host, Vector2 origin, int facing,
        FieldDirector field, FieldMap map)
    {
        double start = Time.realtimeSinceStartupAsDouble;
        double deadline = start + zone.DurationSec;
        double period = zone.IntervalMs / 1000.0;
        int limit = zone.MaxActivations > 0 ? zone.MaxActivations : int.MaxValue;
        for (int tick = 1; tick <= limit; tick++)
        {
            double due = start + tick * period;
            // 施法瞬间的伤害由 BeginReplay 结算，第一个周期末才是第一次追加攻击。
            while (Time.realtimeSinceStartupAsDouble < due) yield return null;
            if (due > deadline || !ReplayAlive(field, map)) yield break;
            if (!fieldZoneGeneration.TryGetValue(zone.State, out long current) || current != generation)
                yield break;

            var targets = zone.TargetMode == "boss"
                ? SeekingTargets(host, origin, facing, false)
                : ReplayTargets(parent, origin, facing, Mathf.Clamp(zone.MobCount, 1, 15), false);
            if (targets.Count == 0) continue;
            PlayNativeReplayEffect(zone.Body, origin, facing);
            // 与 proc 同一条换算管线：母技能 damage% 的面板行 → 追加体 damage%。
            var source = p != null ? p.ReplayFallback(parent, 1, true) : Array.Empty<DamageResult>();
            if (source.Length == 0) continue;
            // boss 模式用 projectiles 个飞镖各追一个目标；area 模式按 mobCount 铺开。
            int shots = zone.TargetMode == "boss"
                ? Mathf.Min(Mathf.Max(1, zone.Projectiles), targets.Count)
                : Mathf.Min(Mathf.Max(1, zone.MobCount), targets.Count);
            for (int i = 0; i < shots; i++)
            {
                var target = targets[i % targets.Count];
                if (!TargetAlive(target)) continue;
                FireProc(host, body, source, parent.DamagePercent, target, origin, facing,
                    body.DamagePercent);
            }
        }
    }

    /// <summary>换图 / 死亡：让还在跑的领域协程全部失效，不能带进下一张图。</summary>
    void ClearFieldZones()
    {
        var keys = new List<int>(fieldZoneGeneration.Keys);
        foreach (int id in keys) fieldZoneGeneration[id]++;
        fieldZoneUntil.Clear();
    }
}
