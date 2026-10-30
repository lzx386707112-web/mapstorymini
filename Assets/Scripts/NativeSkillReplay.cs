using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Schedules exported from the original Java handlers, not inferred from MCV length.
public sealed class NativeReplayStage
{
    public int Skill, Interval;
    public int[] Times;
    public bool Fixed, LiveFacing, OriginalFirst, OriginalBounds, Warrior, Indexed, Total;
}

public static class NativeSkillReplay
{
    static Dictionary<int, List<NativeReplayStage>> schedules;
    public static IReadOnlyList<NativeReplayStage> Get(int skill)
    {
        if (schedules == null)
        {
            schedules = new Dictionary<int, List<NativeReplayStage>>();
            var asset = Resources.Load<TextAsset>("NativeSkillReplays");
            if (asset == null) throw new InvalidOperationException("Missing original replay schedules");
            foreach (var entry in (Dictionary<string, object>)MiniJson.Parse(asset.text))
            {
                var stages = new List<NativeReplayStage>();
                foreach (Dictionary<string, object> row in (List<object>)entry.Value)
                {
                    var times = (List<object>)row["times"];
                    var stage = new NativeReplayStage { Skill = Convert.ToInt32(row["skill"]),
                        Interval = Convert.ToInt32(row["interval"]), Times = new int[times.Count],
                        Fixed = (string)row["origin"] == "fixed", OriginalFirst = (bool)row["originalFirst"],
                        OriginalBounds = (bool)row["originalBounds"], Warrior = (bool)row["warrior"], Indexed = (bool)row["indexed"],
                        Total = row.TryGetValue("total", out var total) && (bool)total,
                        LiveFacing = row.TryGetValue("liveFacing", out var live) && (bool)live };
                    for (int i = 0; i < times.Count; i++) stage.Times[i] = Convert.ToInt32(times[i]);
                    stages.Add(stage);
                }
                schedules.Add(int.Parse(entry.Key), stages);
            }
        }
        return schedules.TryGetValue(skill, out var result) ? result : null;
    }

    // Java Math.round for nonnegative damage; preserve each captured critical flag.
    public static DamageResult[] Adapt(IReadOnlyList<DamageResult> source, int count, double fromPercent, double toPercent,int damageCap=CombatRules.InitialDamageCap)
    {
        if (source.Count == 0) return Array.Empty<DamageResult>();
        var result = new DamageResult[Math.Max(1, Math.Min(15, count))];
        for (int i = 0; i < result.Length; i++)
        {
            var value = source[i % source.Count];
            double scaled = fromPercent > 0 ? Math.Floor(value.Amount * toPercent / fromPercent + .5) : value.Amount;
            result[i] = new DamageResult((int)Math.Max(0, Math.Min(damageCap, scaled)), value.Critical);
        }
        return result;
    }
}

public partial class SkillSystem
{
    sealed class CapturedReplayHit
    {
        public Component Target;
        public DamageResult[] Hits;
    }

    readonly NativeLightningSpearState spearCombo = new NativeLightningSpearState();

    bool BeginReplay(SkillDef cast, Vector2 origin, int facing)
    {
        var stages = NativeSkillReplay.Get(cast.native.Id);
        bool frenzy=cast.native.Id==4121023&&fourSeasonsFrenzy;
        if(frenzy){stages=NativeSkillReplay.Get(4121024);fourSeasonsFrenzy=false;}
        if (stages == null) return false;
        var captured = new List<CapturedReplayHit>();
        foreach (var target in ReplayTargets(cast.native, origin, facing, Mathf.Clamp(cast.native.TargetCount, 1, 15), false, false))
        {
            var hits = new DamageResult[Mathf.Clamp(cast.native.HitCount, 1, 15)];
            var stats = target is EnemyUnit e ? e.CombatStats : ((BossUnit)target).CombatStats;
            for (int i = 0; i < hits.Length; i++) hits[i] = p.RollAttack(stats, cast.native);
            captured.Add(new CapturedReplayHit { Target = target, Hits = hits });
        }
        if (!cast.native.Magic)
        {
            var buffs=p.GetComponent<NativeBuffs>();
            if (IsRangedReplay(cast.native.Id)) buffs?.OnRangedAttack(captured.Count);
            else buffs?.OnCloseAttack();
        }
        DamageResult[] template = captured.Count > 0 ? captured[0].Hits : Array.Empty<DamageResult>();
        var field = FindObjectOfType<FieldDirector>();
        var map = field != null ? field.Map : null;
        if(!frenzy)TriggerReactiveSkills(cast,captured,origin,facing);
        StartSeekingSkills(cast,template,origin,facing,field,map);
        if (cast.native.Id == 15121002)
        {
            if (!spearCombo.TryPress((long)(Time.realtimeSinceStartupAsDouble * 1000))) return true;
            int visual=15121022+spearCombo.PressCount-1;
            p.PlayNativeSkill(visual);
            PlayNativeReplayEffect(visual,origin,facing);
            actionUntil=Time.unscaledTime+.180f;
            var numberMode = new NativeReplayStage { Total = true, Interval = 120 };
            foreach (var hit in captured)
                ApplyReplayHit(cast,cast.native,hit.Target,hit.Hits,origin,facing,numberMode);
            var thunder = SkillBook.Native(15121009, Mathf.Clamp(cast.native.Level,1,SkillBook.MaxLevelOf(15121009)));
            var thunderSource = template.Length > 0 ? template : p.ReplayFallback(cast.native,Mathf.Clamp(thunder.HitCount,1,15),true);
            var thunderHits = NativeSkillReplay.Adapt(thunderSource,thunder.HitCount,cast.native.DamagePercent,thunder.DamagePercent,GameSession.Character?.record.damageCap ?? CombatRules.InitialDamageCap);
            int triggered = 0;
            foreach (var hit in captured)
            {
                if (hit.Target == null || (hit.Target is EnemyUnit e && e.dead) || (hit.Target is BossUnit b && b.dead)) continue;
                PlayNativeReplayEffect(15121009,p.FeetPos,facing);
                ApplyReplayHit(cast,thunder,hit.Target,thunderHits,origin,facing,numberMode);
                if (++triggered == 3) break;
            }
            if (!spearCombo.Finishing) return true;
        }
        // Capture once. Later stages never roll a fresh attack against a new monster.
        foreach (var stage in stages)
        {
            var replay = SkillBook.Native(stage.Skill, Mathf.Clamp(cast.native.Level, 1, SkillBook.MaxLevelOf(stage.Skill)));
            if (replay == null) throw new InvalidOperationException("Missing replay WZ level: " + stage.Skill);
            var source = template;
            int count = stage.Warrior && stage.OriginalFirst ? cast.native.HitCount : replay.HitCount;
            count = Mathf.Clamp(count,1,15);
            bool positive = false; foreach(var hit in source) positive |= hit.Amount>0;
            bool fallback = source.Length==0 || (IsRangedReplay(cast.native.Id) && !positive);
            DamageResult[] adapted;
            if (fallback && stage.Warrior)
                adapted = p.ReplayFallback(cast.native,count,true);
            else
            {
                if (fallback && (cast.native.Magic || IsRangedReplay(cast.native.Id)))
                    source = p.ReplayFallback(cast.native,1,false);
                float percent = stage.Warrior && stage.OriginalFirst ? cast.native.DamagePercent : replay.DamagePercent;
                adapted = NativeSkillReplay.Adapt(source,count,cast.native.DamagePercent,percent,GameSession.Character?.record.damageCap ?? CombatRules.InitialDamageCap);
            }
            StartCoroutine(ReplayStage(cast, replay, stage, captured, adapted, origin, facing, field, map));
        }
        return true;
    }

    static bool IsRangedReplay(int id)
    {
        int job = id / 10000;
        return job == 312 || job == 322 || job == 412 || job == 522 || job == 1312 || job == 1412;
    }

    List<Component> ReplayTargets(NativeSkillDefinition bounds, Vector2 origin, int facing, int count, bool fistFinish, bool unbounded = true, bool fuma = false)
    {
        var targets = new List<Component>();
        bool Contains(Component t)
        {
            if (fuma)
            {
                var delta=(Vector2)t.transform.position-origin;
                return delta.x>=-140/Art.PPU && delta.x<140/Art.PPU && delta.y>-140/Art.PPU && delta.y<=140/Art.PPU;
            }
            if (unbounded && !fistFinish && !bounds.HasBounds) return true;
            if (!fistFinish) return InRange(t.transform.position, bounds, origin, facing);
            var d = (Vector2)t.transform.position - origin;
            return d.x >= -1000 / Art.PPU && d.x < 1000 / Art.PPU && d.y > -200 / Art.PPU && d.y <= 450 / Art.PPU;
        }
        foreach (var b in FindObjectsOfType<BossUnit>()) if (!b.dead && Contains(b)) targets.Add(b);
        foreach (var e in EnemyUnit.All) if (!e.dead && Contains(e)) targets.Add(e);
        targets.Sort((a,b) => {
            bool ab = a is BossUnit || (a is EnemyUnit ae && NativeCombatData.Int(ae.CombatStats,"boss") != 0);
            bool bb = b is BossUnit || (b is EnemyUnit be && NativeCombatData.Int(be.CombatStats,"boss") != 0);
            if (ab != bb) return ab ? -1 : 1;
            int distance = ((Vector2)a.transform.position-origin).sqrMagnitude.CompareTo(((Vector2)b.transform.position-origin).sqrMagnitude);
            return distance != 0 ? distance : a.GetInstanceID().CompareTo(b.GetInstanceID());
        });
        if (targets.Count > count) targets.RemoveRange(count, targets.Count-count);
        return targets;
    }

    IEnumerator ReplayStage(SkillDef cast, NativeSkillDefinition replay, NativeReplayStage stage,
        List<CapturedReplayHit> captured, DamageResult[] template, Vector2 origin, int facing, FieldDirector field, FieldMap map)
    {
        double started = Time.realtimeSinceStartupAsDouble;
        for (int index = 0; index < stage.Times.Length; index++)
        {
            double due = started + stage.Times[index] / 1000.0;
            while (Time.realtimeSinceStartupAsDouble < due) yield return null;
            if (p == null || p.hp <= 0 || GameLoop.I == null || GameLoop.I.gameOver ||
                GameLoop.I.state != GameLoop.GameState.Playing || (field != null && !ReferenceEquals(field.Map,map))) yield break;
            bool original = index == 0 && stage.OriginalFirst && !stage.Warrior && !stage.LiveFacing;
            if (original && captured.Count > 0)
            {
                foreach (var hit in captured) ApplyReplayHit(cast,cast.native,hit.Target,hit.Hits,origin,facing,stage);
                continue;
            }
            Vector2 current = stage.Fixed ? origin : p.FeetPos;
            if(cast.native.Id==15121002)PlayNativeReplayEffect(replay.Id,current,facing);
            int direction = stage.LiveFacing ? p.facing : facing;
            if (replay.Id==4121011) current.x += facing*(600*Mathf.Clamp(stage.Times[index]-420,0,360)/360)/Art.PPU;
            var bounds = stage.OriginalBounds ? cast.native : replay;
            int limit = stage.OriginalBounds ? cast.native.TargetCount : replay.TargetCount;
            if (cast.native.Id == 1121020) limit = Mathf.Clamp(limit,1,8); // SWORD_ILLUSION
            if (cast.native.Magic && !bounds.HasBounds)
            {
                // MagicDamageHandler.collectAnimatedTargets falls back to the original
                // captured per-monster lines when there is no replay bounding box.
                foreach(var hit in captured) ApplyReplayHit(cast,replay,hit.Target,hit.Hits,current,direction,stage);
                continue;
            }
            foreach (var target in ReplayTargets(bounds,current,direction,Mathf.Clamp(limit,1,15),replay.Id == 5121016,true,replay.Id==4121011))
                ApplyReplayHit(cast,replay,target,template,current,direction,stage);
        }
        if (cast.native.Id == 15121002 && replay.Id == 15121011) spearCombo.Reset();
    }

    void ApplyReplayHit(SkillDef cast, NativeSkillDefinition replay, Component target, DamageResult[] hits,
        Vector2 origin, int facing, NativeReplayStage stage)
    {
        if (target == null) return;
        var e = target as EnemyUnit; var b = target as BossUnit;
        if ((e != null && e.dead) || (b != null && b.dead)) return;
        int direction = target.transform.position.x >= origin.x ? 1 : -1;
        if (e != null) e.TakeReplayHit(hits,direction,replay.Magic,stage.Indexed,stage.Interval,stage.Total);
        else if (b != null) b.TakeReplayHit(hits,direction,stage.Indexed,stage.Interval,stage.Total);
        bool landed = false;
        foreach (var hit in hits) landed |= hit.Amount > 0;
        Impact(cast,replay,target,direction,landed);
    }

    void PlayNativeReplayEffect(int id,Vector2 origin,int facing)
    {
        string key=NativeSkillVfx.Key(id,Mathf.Max(1,SkillProgress.LevelOf(id)));
        foreach(var clip in NativeSkillVfx.Parts(key,"effect"))
            Visual(key,clip.path,origin,facing,NativeSkillVfx.Number(key,clip.path,"fix")!=0?null:p.transform,
                false,p.SortingOrder+(NativeSkillVfx.Number(key,clip.path,"z")<0?-1:1));
    }
}
