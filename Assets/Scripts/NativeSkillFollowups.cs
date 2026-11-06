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
        // 持续伤害（dot/dotTime/dotInterval）：30 条技能以前只结算首段，后续几十跳全丢。
        // 必须在每次真实命中后挂标记，见 NativeSkillDots.cs。
        ApplyDots(cast,captured);
        // 282 的被动追加攻击（終極攻擊 / 魔法爆發 / 深淵黑暗 / 蝙蝠 / 風暴使者…）：
        // 主攻击命中后按 prop 或命中计数结算一次，见 NativeProcSkills.cs。
        TriggerProcSkills(cast,captured,origin,facing);
        // 子状态驱动的周期领域（箭影纷飞「攻擊命中時」、暗黑天魔VI「狀態期間使用技能時」）
        // 必须由一次真实命中启动，不能只靠计时器自己打。见 NativeFieldZones.cs。
        if(captured!=null&&captured.Count>0)TriggerFieldZonesOnHit(cast,origin,facing);
        // 资源/追加方向/符咒/技能替换/隐身回避：这些都是「命中后」的实体结算，
        // 数值来自 NativeSkillMechanics.json，见 NativeSkillMechanics.cs。
        OnMechanicHit(cast,captured,origin,facing);
        // 極冰風暴：直接攻击命中「被鑽石狼的詛咒」标记的目标 ⇒ 生成冰塊追加攻击
        //（400011122，内置 400ms 冷却）。见 NativeSkillCurses.cs。
        TriggerCurseOnHit(cast,captured,origin,facing);
        // 黑影切断（400041025）的被动段：致命暗殺 / 音速狂襲命中 → 给最大 HP 最高的
        // 那名敌人叠伤口（最多 3 层、10 秒），满层后才允许连携 400041026。
        // 见 NativeSkillSequences.cs。
        NotifySequenceHit(cast,captured);
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
    IEnumerator WindDot(Component target,long amount,long generation,FieldDirector field,FieldMap map)
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
        ClearCurses();     // 换图/死亡：诅咒标记与光芒协程不能带进下一张图
        ClearDots();       // 同上：持续伤害的标记与协程也不能跨图
        ClearStacks();     // 叠层状态同理
    }

    // ——— 282「按一次、结算多次」的两类技能 ———
    //
    // (1) 周期自动攻击：common/time = 持续时间，common/attackDelay = 周期。
    //     祝福之鎚 400011052 的 h 模板:「在#v秒內鐵鎚自動以#q%的傷害攻擊最多#s名敵人#z次」，
    //     子技能 400011053 写死 attackDelay=1800、time=30 ⇒ 30 秒内每 1.8 秒打一次。
    //     以前只在按下瞬间结算一次，剩下 29 秒的段数整段丢失。
    //
    // (2) 多道落雷：common/w = 道数。落雷凝聚 400021030 的 h 模板:
    //     「最多對#q名的敵方使用以#v%傷害值攻擊#s次的閃電#w次」，w=8；
    //     effect 有 16 层、每层 delay=60ms，正好 8 道 × 2 层 ⇒ 每道间隔 120ms。
    //     以前只放 1 道，实际伤害约为原版的 1/8。
    //
    // 两份名单都只登记已经逐字段核实过的技能：282 的 time 字段单位并不统一
    //（同一批 V 技能里 30/5/4000/15000 混用），没核实的不能按秒套，否则会把
    // 一次性技能变成常驻。
    static readonly HashSet<int> RepeatAttackSkills = new HashSet<int> { 400011052 };
    // 282 把「母技能开状态」和「子技能出每一发」拆成两条：
    //   400011052 祝福之鎚 common = mpCon/cooltime/x/y/w/q/z/s/v（没有 damage/time/attackDelay），
    //               info/type=11 + areaAttack=1 ⇒ 范围攻击，不是弹道；顶层只有 special + ball。
    //   400011053（invisible=1、action=5thBlessedHammer）common 才有
    //               damage/attackCount/mobCount/time=30/attackDelay=1800/lt/rb，
    //               特效层是 special(8) + ball/0..2 + effect(7) + effect0(8,z=-1) + hit/0。
    // 只按母技能 vfx 播，后续每一发就只剩「凭空掉血」；必须换成子技能的层。
    static readonly Dictionary<int,int> RepeatAttackBodies = new Dictionary<int,int> { { 400011052, 400011053 } };
    public static bool IsRepeatingSkill(int id) => RepeatAttackSkills.Contains(id);

    void StartRepeatingAttack(SkillDef s)
    {
        if (s.native == null || !RepeatAttackSkills.Contains(s.native.Id)) return;
        var row = SkillBook.Get(s.native.Id)?.Level(s.native.Level);
        int period = Art.Int(row, "attackDelay");
        int seconds = Art.Int(row, "time");
        if (period <= 0 || seconds <= 0) return;
        // 伤害/判定框仍用母技能（VMatrixSkills 已把子技能的 damage/lt/rb 合并进来），
        // 取层换成子技能：BeginVisuals 的 effect/effect0/special、Impact 的 hit 都从它来。
        SkillDef body = s;
        if (RepeatAttackBodies.TryGetValue(s.native.Id, out int childId))
        {
            string childKey = NativeSkillVfx.Key(childId, s.native.Level);
            body = new SkillDef(new ClassSkill { name = s.name, art = s.art, icon = s.icon }, s.native, childKey);
        }
        StartCoroutine(RepeatAttack(body, period / 1000f, seconds));
    }

    IEnumerator RepeatAttack(SkillDef body, float period, float seconds)
    {
        var field = FindObjectOfType<FieldDirector>();
        var map = field != null ? field.Map : null;
        double start = Time.realtimeSinceStartupAsDouble;
        double end = start + seconds;
        int tick = 0;
        // 用真实时间排程而不是 WaitForSeconds：后者受 timeScale 影响，
        // 而 end 是真实时间，两者混用会在慢动作/暂停恢复后整段漂移。
        while (start + (tick + 1) * period <= end)
        {
            double due = start + (++tick) * period;
            while (Time.realtimeSinceStartupAsDouble < due) yield return null;
            if (!ReplayAlive(field, map)) yield break;
            Vector2 origin = p.FeetPos;
            int facing = p.facing;
            // 先打 tick 标记：后面的 [FxPlay]/[Hit] 日志都归到这一发，
            // 批次探针靠日志顺序归因，不靠 batchmode 里会被节流的时间戳。
            if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
                Debug.Log("[Repeat] tick=" + tick + " body=" + body.native.Id + " vfx=" + body.vfx);
            // 一发 = 本体层 + 可见锤体 + 一次范围结算，三者缺一就是
            // 「怪突然掉血但看不到特效」或「只播特效打不到怪」。
            BeginVisuals(body, true);
            StartCoroutine(RepeatingHammer(body, origin, facing, .45f));
            HitAt(body, body.native, origin, facing);
        }
    }

    void StartThunderSeries(SkillDef s, Vector2 origin, int facing)
    {
        if (s.native == null || s.native.Id != 400021030) return;
        var row = SkillBook.Get(s.native.Id)?.Level(s.native.Level);
        int bolts = Art.Int(row, "w");
        if (bolts <= 1) return;
        StartCoroutine(ThunderSeries(s, origin, facing, bolts));
    }

    IEnumerator ThunderSeries(SkillDef s, Vector2 origin, int facing, int bolts)
    {
        const float interval = .12f;   // 16 层 effect × 60ms ÷ 8 道
        for (int index = 1; index < bolts; index++)
        {
            yield return new WaitForSeconds(interval);
            if (p == null || p.hp <= 0 || GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Playing) yield break;
            HitAt(s, s.native, origin, facing);
        }
    }
}
