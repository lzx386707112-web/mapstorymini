using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Stateful branches from CloseRangeDamageHandler and RangedAttackHandler.
public partial class SkillSystem
{
    static Dictionary<string,object> followupData;
    static Dictionary<string,object> FollowupData=>followupData??(followupData=(Dictionary<string,object>)MiniJson.Parse(Resources.Load<TextAsset>("NativeSkillFollowups").text));
    static int FollowupInt(string key)=>Convert.ToInt32(FollowupData[key]);
    static int[] FollowupTimes(string key)
    {var a=(List<object>)FollowupData[key];var result=new int[a.Count];for(int i=0;i<a.Count;i++)result[i]=Convert.ToInt32(a[i]);return result;}
    bool fourSeasonsFrenzy;
    int dualBladeHits;
    double arrowRainExpires,arrowRainNext;
    readonly Dictionary<Component,long> windDots=new Dictionary<Component,long>();
    long windGeneration;
    readonly List<NativeImgAnimation> nativeReplayEffects=new List<NativeImgAnimation>();
    bool ReplayAlive(FieldDirector field,FieldMap map)=>p!=null&&p.hp>0&&p.isActiveAndEnabled&&GameLoop.I!=null&&!GameLoop.I.gameOver&&GameLoop.I.state==GameLoop.GameState.Playing&&(field==null||ReferenceEquals(field.Map,map));
    static bool TargetAlive(Component t)=>t!=null&&((t is EnemyUnit e&&!e.dead)||(t is BossUnit b&&!b.dead));
    NativeSkillDefinition ReplayDefinition(int id,int level)=>SkillBook.Native(id,Mathf.Clamp(level,1,SkillBook.MaxLevelOf(id)));
    void ExtraStage(SkillDef cast,int id,int[] times,DamageResult[] source,List<CapturedReplayHit> captured,
        Vector2 origin,int facing,FieldDirector field,FieldMap map,bool fixedOrigin=false,int level=0)
    {
        var replay=ReplayDefinition(id,level>0?level:cast.native.Level);if(replay==null)return;
        bool positive=false;foreach(var hit in source)positive|=hit.Amount>0;
        if(IsRangedReplay(cast.native.Id)&&!positive)source=p.ReplayFallback(cast.native,1,false);
        var adapted=NativeSkillReplay.Adapt(source,replay.HitCount,cast.native.DamagePercent,replay.DamagePercent);
        var stage=new NativeReplayStage{Skill=id,Times=times,Interval=120,Fixed=fixedOrigin};
        StartCoroutine(ReplayStage(cast,replay,stage,captured,adapted,origin,facing,field,map));
    }
    void TriggerReactiveSkills(SkillDef cast,List<CapturedReplayHit> captured,Vector2 origin,int facing)
    {
        var field=FindObjectOfType<FieldDirector>();var map=field!=null?field.Map:null;
        var source=captured.Count>0?captured[0].Hits:Array.Empty<DamageResult>();
        int id=cast.native.Id;double now=Time.realtimeSinceStartupAsDouble;
        if(Profile.JobId==422&&captured.Count>0)
        {
            bool haunted=(id==4221020||id==4221018)&&State(4221013).TryStart(now,12);
            if(haunted)
            {
                if(id==4221020)
                {
                    ExtraStage(cast,4221014,FollowupTimes("HAUNTED_EDGE_ASURA_TIMES_MS"),source,captured,origin,facing,field,map);
                    ExtraStage(cast,4221015,FollowupTimes("HAUNTED_EDGE_PROJECTILE_TIMES_MS"),source,captured,origin,facing,field,map);
                }
                else ExtraStage(cast,4221016,FollowupTimes("HAUNTED_EDGE_RAKSHASA_TIMES_MS"),source,captured,origin,facing,field,map);
            }
            dualBladeHits=Math.Min(FollowupInt("SUDDEN_RAID_MAX_STORED_HITS"),dualBladeHits+1);
            int threshold=FollowupInt("SUDDEN_RAID_TRIGGER_HITS");
            if(dualBladeHits>=threshold&&State(4221017).TryStart(now,1))
            {dualBladeHits-=threshold;ExtraStage(cast,4221017,FollowupTimes("SUDDEN_RAID_FINISHER_TIMES_MS"),source,captured,origin,facing,field,map);}
        }
        if(id==4121023)fourSeasonsFrenzy=captured.Count>0;
        if(!IsRangedReplay(id)||id==3121033)return;
        if(id==3121010){arrowRainExpires=now+FollowupInt("ARROW_RAIN_DURATION_MS")/1000.0;arrowRainNext=now;}
        if(now>=arrowRainExpires || (id!=3121010&&captured.Count==0) || now<arrowRainNext)return;
        arrowRainNext=now+FollowupInt("ARROW_RAIN_FIELD_COOLDOWN_MS")/1000.0;
        ExtraStage(cast,3121033,FollowupTimes("ARROW_RAIN_FIELD_TIMES_MS"),source,captured,origin,facing,field,map,true,Math.Max(1,SkillProgress.LevelOf(3121010)));
    }
    // A normal ranged attack (skill=0 in the original packet) also consumes the field gate.
    public void NativeBasicRangedHit(Component target,DamageResult hit,int facing)
    {
        double now=Time.realtimeSinceStartupAsDouble;
        if(target==null||now>=arrowRainExpires||now<arrowRainNext)return;
        var replay=ReplayDefinition(3121033,Math.Max(1,SkillProgress.LevelOf(3121010)));if(replay==null)return;
        arrowRainNext=now+FollowupInt("ARROW_RAIN_FIELD_COOLDOWN_MS")/1000.0;
        var field=FindObjectOfType<FieldDirector>();var map=field!=null?field.Map:null;
        var cast=new SkillDef(new ClassSkill{name="",art="",icon=""},replay);
        var template=NativeSkillReplay.Adapt(new[]{hit},replay.HitCount,100,replay.DamagePercent);
        var stage=new NativeReplayStage{Skill=3121033,Times=FollowupTimes("ARROW_RAIN_FIELD_TIMES_MS"),Fixed=true,Interval=120};
        StartCoroutine(ReplayStage(cast,replay,stage,new List<CapturedReplayHit>(),template,p.FeetPos,facing,field,map));
    }
    void StartSeekingSkills(SkillDef cast,DamageResult[] template,Vector2 origin,int facing,FieldDirector field,FieldMap map)
    {
        if(cast.native.Id!=13121003&&cast.native.Id!=13121013)return;
        if(template.Length==0)template=p.ReplayFallback(cast.native,1,false);
        double now=Time.realtimeSinceStartupAsDouble;
        if(cast.native.Id==13121003)
        {
            var replay=ReplayDefinition(13121018,cast.native.Level);
            var hits=NativeSkillReplay.Adapt(template,replay.HitCount,cast.native.DamagePercent,replay.DamagePercent);
            var dot=NativeSkillReplay.Adapt(template,1,cast.native.DamagePercent,FollowupInt("MERCILESS_WINDS_DOT_PERCENT"));
            var counts=new Dictionary<Component,int>();int index=0;
            double deadline=now+FollowupInt("MERCILESS_WINDS_TRACKING_DURATION_MS")/1000.0;
            foreach(int at in FollowupTimes("MERCILESS_WINDS_TIMES_MS"))
                StartCoroutine(SeekingProjectile(cast,replay,hits,dot,origin,facing,field,map,index++,now+at/1000.0,deadline,counts,false));
        }
        else
        {
            int[] counts=FollowupTimes("MISTRAL_SPIRIT_COUNTS"),lives=FollowupTimes("MISTRAL_SPIRIT_LIFETIMES_MS"),delays=FollowupTimes("MISTRAL_SPIRIT_ENABLE_DELAYS_MS");
            int wave=0;
            for(int offset=0;offset<FollowupInt("MISTRAL_SPIRIT_DURATION_MS");offset+=FollowupInt("MISTRAL_SPIRIT_INTERVAL_MS"))
            {
                int type=wave++%3;var replay=ReplayDefinition(13121015+type,cast.native.Level);
                var hits=NativeSkillReplay.Adapt(template,replay.HitCount,cast.native.DamagePercent,replay.DamagePercent);
                double start=now+(FollowupInt("MISTRAL_SPIRIT_START_TIME_MS")+offset)/1000.0;
                double deadline=Math.Min(start+lives[type]/1000.0,now+FollowupInt("MISTRAL_SPIRIT_TOTAL_END_TIME_MS")/1000.0);
                for(int index=0;index<counts[type];index++)
                    StartCoroutine(SeekingProjectile(cast,replay,hits,null,origin,facing,field,map,index,start+delays[type]/1000.0,deadline,null,true));
            }
        }
    }
    List<Component> SeekingTargets(SkillDef cast,Vector2 origin,int facing,bool mistral)
    {
        var targets=ReplayTargets(cast.native,origin,facing,int.MaxValue,false);
        if(mistral&&!cast.native.HasBounds)targets.RemoveAll(t=>Mathf.Abs(t.transform.position.x-origin.x)>1200/Art.PPU||Mathf.Abs(t.transform.position.y-origin.y)>800/Art.PPU);
        // Original ordering: bosses first, bosses' maxHP descending, then distance/OID.
        targets.Sort((a,b)=>
        {
            bool ab=a is BossUnit||(a is EnemyUnit ae&&NativeCombatData.Int(ae.CombatStats,"boss")!=0);
            bool bb=b is BossUnit||(b is EnemyUnit be&&NativeCombatData.Int(be.CombatStats,"boss")!=0);
            if(ab!=bb)return ab?-1:1;
            if(ab){var sa=a is EnemyUnit ea?ea.CombatStats:((BossUnit)a).CombatStats;var sb=b is EnemyUnit eb?eb.CombatStats:((BossUnit)b).CombatStats;int hp=Art.Num(sb,"maxHP").CompareTo(Art.Num(sa,"maxHP"));if(hp!=0)return hp;}
            int distance=(a.transform.position-(Vector3)origin).sqrMagnitude.CompareTo((b.transform.position-(Vector3)origin).sqrMagnitude);
            return distance!=0?distance:a.GetInstanceID().CompareTo(b.GetInstanceID());
        });return targets;
    }
    IEnumerator SeekingProjectile(SkillDef cast,NativeSkillDefinition replay,DamageResult[] hits,DamageResult[] dot,
        Vector2 origin,int facing,FieldDirector field,FieldMap map,int index,double start,double deadline,Dictionary<Component,int> counts,bool mistral)
    {
        while(Time.realtimeSinceStartupAsDouble<start)yield return null;
        while(ReplayAlive(field,map)&&Time.realtimeSinceStartupAsDouble<deadline)
        {
            var targets=SeekingTargets(cast,origin,facing,mistral);
            if(targets.Count>0)
            {
                var target=targets[index%targets.Count];var damage=hits;
                if(counts!=null)
                {
                    counts.TryGetValue(target,out int previous);
                    if(previous>0)damage=NativeSkillReplay.Adapt(hits,hits.Length,100,FollowupInt("MERCILESS_WINDS_REPEATED_TARGET_PERCENT"));
                    counts[target]=previous+1;
                }
                ApplyReplayHit(cast,replay,target,damage,origin,facing,new NativeReplayStage{Indexed=mistral,Total=!mistral,Interval=120});
                if(dot!=null&&TargetAlive(target))
                {
                    long generation=++windGeneration;windDots[target]=generation;
                    StartCoroutine(WindDot(target,dot[0].Amount,generation,field,map));
                }
                yield break;
            }
            double next=Time.realtimeSinceStartupAsDouble+FollowupInt(mistral?"MISTRAL_SPIRIT_SEARCH_INTERVAL_MS":"MERCILESS_WINDS_SEARCH_INTERVAL_MS")/1000.0;
            while(Time.realtimeSinceStartupAsDouble<next)yield return null;
        }
    }
    IEnumerator WindDot(Component target,int amount,long generation,FieldDirector field,FieldMap map)
    {
        double start=Time.realtimeSinceStartupAsDouble;
        for(int tick=1;tick<=FollowupInt("MERCILESS_WINDS_DOT_TICKS");tick++)
        {
            double due=start+tick*FollowupInt("MERCILESS_WINDS_DOT_INTERVAL_MS")/1000.0;
            while(Time.realtimeSinceStartupAsDouble<due)yield return null;
            if(!ReplayAlive(field,map)||!TargetAlive(target)||!windDots.TryGetValue(target,out long current)||current!=generation)yield break;
            if(target is EnemyUnit e)e.TakeReplayDot(amount);else ((BossUnit)target).TakeReplayDot(amount);
        }
        if(windDots.TryGetValue(target,out long last)&&last==generation)windDots.Remove(target);
    }
    void ClearNativeReplayEffects()
    {
        foreach(var a in nativeReplayEffects)if(a!=null)a.Remove();nativeReplayEffects.Clear();windDots.Clear();
    }
}
