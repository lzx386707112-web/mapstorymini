using System;
using System.Collections.Generic;
using UnityEngine;

// Source contract: StatEffect.loadFromData / Character.registerEffect.
// Keep suppressed sources so expiry/cancellation can reveal the next best effect.
public sealed class NativeBuffs : MonoBehaviour
{
    public sealed class Effect
    {
        public int Id, Level;
        public double Until, Started, NextRecovery, NextDragonBlood, NextBeholderHeal, NextBeholderBuff;
        // 秘术超越 400001021：每 y 秒掉一阶段，阶段值直接写在 FinalDamage 上。
        public double NextArcaneDecay;
        public int DurationMs;
        public bool NoShadow;
        public readonly Dictionary<string,int> Stats = new Dictionary<string,int>();
        public int RemainingMs(double now) => (int)Math.Min(int.MaxValue,Math.Max(0,Math.Ceiling((Until-now)*1000)));
        public int Shadow(double now) => DurationMs < 16 ? 0 : Mathf.Clamp(RemainingMs(now)/(DurationMs/16),0,15);
    }
    static Dictionary<string,object> contract;
    readonly List<Effect> active = new List<Effect>();
    readonly Dictionary<string,Effect> winners = new Dictionary<string,Effect>();
    readonly List<Effect> visible = new List<Effect>();
    public event Action<int> Removed;
    static readonly HashSet<string> shared = new HashSet<string> {
        "COUPON_EXP1","COUPON_EXP2","COUPON_EXP3","COUPON_EXP4","COUPON_DRP1","COUPON_DRP2","COUPON_DRP3",
        "MESO_UP_BY_ITEM","ITEM_UP_BY_ITEM","RESPECT_PIMMUNE","RESPECT_MIMMUNE","DEFENSE_ATT","DEFENSE_STATE",
        "WATK","WDEF","MATK","MDEF","ACC","AVOID","SPEED","JUMP",
        // 技能增益（NativeSkillEffects）：这些是「按占比叠加」的属性，不属于
        // applyBuffEffect 的 singleton 语义。不声明成 shared，后放的同名 buff 会把
        // 先放的整个顶掉（Register 的 !shared 分支），两个 +伤害的技能只能剩一个。
        "Damage","NormalDamage","FinalDamage","IgnoreDefense","CriticalChance","ExpRate","DropRate",
        "HP","HPPct","PAD","MAD","PADPct","MADPct","PDDPct","STRPct","DEXPct","INTPct","LUKPct",
        // 爆击强化 400031023 的注册标记（消费方：PlayerCtrl.RefreshBuffAttributes）。
        "CRIT_REINFORCE",
        // 强化核心接管的 summon（暗影瞬移 400041095）：本身没有属性字段，
        // 靠这个标记让 ApplySkill 能注册，NativeSummon 才认 HasSource 而存活。
        "SUMMON_DURATION",
        // 实用的战斗命令 400001004：技能等级 +x，本身不产出属性，
        // 靠这个标记承载生效期与 buff 图标（NativeCombatOrders 读它）。
        "SKILL_ORDER",
        // 状态异常耐性（400011066「#time秒内提升状态异常抗性#indieAsrR」）：
        // 可叠加，且与永久被动的 ASR 相加（消费方 Field/NativeMobDebuffs）。
        "ASR",
        // 普力特的祝福 400001024 的 3/5/6 层：flat 全属性、Boss 伤害%、无敌，
        // 以及承载叠层数的标记。消费方分别是 PlayerCtrl.RefreshAttributes /
        // PlayerCtrl.cs:1731 / PlayerCtrl.cs:1406。新键必须进 shared，否则
        // Register 会把先放的同名 buff 整个顶掉。
        "STR", "DEX", "INT", "LUK", "BossDamage", "INVINCIBLE", "STACK_BLESSING"
    };
    static bool ConfigBool(string key,bool fallback=false) => GameServer.Local == null ? fallback : GameConfigDao.GetBool(GameServer.Local.Db,key,fallback);
    static string Config(string key,string fallback) => GameServer.Local == null ? fallback : GameConfigDao.Get(GameServer.Local.Db,key,fallback);
    public static int SkillDuration(int id,int level)
    {
        // 500004073 强化暗影瞬移：持续时间改由核心自己的 time 字段决定（46..55 秒），
        // 没学核心时原样用技能自己的 time（30..45 秒）。见 NativeSkillOverrides。
        int duration=NativeSkillOverrides.DurationSeconds(id,Art.Int(SkillBook.Get(id)?.Level(level),"time"))*1000;
        if (!ConfigBool("use_buff_everlasting")) return duration;
        var custom=MiniJson.Parse(Config("job_buff_custom_time_list","{\"4121006\":1800000,\"2121004\":60000,\"2221004\":60000,\"2321004\":60000}")) as Dictionary<string,object>;
        if (custom != null && custom.TryGetValue(id.ToString(),out var value) && Convert.ToInt32(value)>0) return Convert.ToInt32(value);
        bool own=SkillProgress.LevelOf(id)>0;
        if (Array.Exists(Config("job_special_buff_time_list","3121002,3221002").Split(','),v=>v.Trim()==id.ToString())) return own ? int.MaxValue : duration;
        if (Array.Exists(Config("job_buff_original_time_list","2321005,3121002,3221002,4221006,4211005").Split(','),v=>v.Trim()==id.ToString())) return duration;
        return own ? int.MaxValue : 1800000;
    }
    public static double Now => Time.realtimeSinceStartupAsDouble;
    public static NativeBuffs For(PlayerCtrl player) => player.GetComponent<NativeBuffs>() ?? player.gameObject.AddComponent<NativeBuffs>();
    public IReadOnlyList<Effect> Visible { get { Tick(Now); return visible; } }
    public int Value(string stat) { Tick(Now); return winners.TryGetValue(stat,out var effect) ? effect.Stats[stat] : 0; }
    // Hero's Will 同族。服务端的 isHerosWill 表来自 GMS083 的 StatEffect.isHerosWill，
    // 那里没有 282 的 V 技能，所以 400001009 艾尔达斯的意志只能在这里补。
    // 判据不是名字，是数值形状：十二职业的 1121011/1221012/…/5121008 全是
    // time=1 + cooltime=360 + 单一资源消耗（mpCon=30）；400001009 是 time=1 +
    // cooltime=330 + hpCon=100，结构一致，只差消耗的是 HP。
    // 另外 1121011 的 h 同样只写「消耗MP #mpCon，再次使用冷却时间 #cooltime秒」——
    // 这套技能的 h 从来不写「解除异常」，所以「h 没有效果描述」不构成拒绝理由。
    public static bool IsHerosWill(int id) => id == 400001009 ||
        ((List<object>)Contract["herosWill"]).Exists(v=>Convert.ToInt32(v)==id);
    static Dictionary<string,object> Contract
    {
        get
        {
            if (contract == null) contract = (Dictionary<string,object>)MiniJson.Parse(Resources.Load<TextAsset>("NativeBuffContract").text);
            return contract;
        }
    }
    static bool OverTime(int id,MapleDash.Wz.ImgNode root)
    {
        bool overtime = root.Get("skillType") != null ? root.Int("skillType") == 2 :
            root.Get("effect") != null && root.Get("hit") == null && root.Get("ball") == null || root.Get("action/0")?.Value?.ToString() == "alert2";
        if (root.Get("skillType") == null && ((Dictionary<string,object>)Contract["forcedOverTime"]).TryGetValue(id.ToString(),out var forced)) overtime = (bool)forced;
        return overtime;
    }
    public static Dictionary<string,int> SkillStats(int id, int level)
    {
        var root = WzAssets.Node("Skill/" + (id/10000).ToString("D3") + ".img/skill/" + id.ToString("D7"));
        var row = SkillBook.Get(id)?.Level(level);
        var result = new Dictionary<string,int>();
        // 暗影瞬移这类由强化核心接管时长的 summon 没有任何属性字段，
        // 但必须让 ApplySkill 注册成功，否则 NativeSummon.HasSource 为假、影子立刻消失。
        // 放在最前面：这种 id 可能连 WZ 的 effect 节点都没有，下面的 root==null 会提前 return。
        if (NativeSkillOverrides.IsDurationTarget(id)) result["SUMMON_DURATION"]=1;
        // 子状态 id（11141501 银河星爆）自己没有 effect/skillType，OverTime 判 false，
        // 也没有 GMS083 的 WZ 节点可查。它由母技能施放时挂上，增益字段全在
        // NativeSkillEffects 的 alias 里（y → FinalDamage），不需要走下面那套 statups。
        if (NativeSkillStates.IsTimedEffect(id))
        {
            if (row != null) NativeSkillEffects.AddBuff(result, id, row);
            // 剧毒炼狱 2141502：元素吸收锁满 10 层 ⇒ FinalDamage +30（表内直给值，
            // 没有「字段 → stat」映射可走 alias）。见 NativeSkillMechanics.AddTimedEffect。
            NativeSkillMechanics.AddTimedEffect(result, id);
            return result;
        }
        if (root == null || row == null) return result;
        bool overtime=OverTime(id,root);
        bool summon = ((List<object>)Contract["summons"]).Exists(v => Convert.ToInt32(v) == id);
        string[] fields = {"pad","pdd","mad","mdd","acc","eva","speed","jump","expBuff"};
        string[] keys = {"WATK","WDEF","MATK","MDEF","ACC","AVOID","SPEED","JUMP","EXP_BUFF"};
        if (overtime && !summon)
            for (int i=0;i<fields.Length;i++) { int value=i<8 ? (short)Art.Int(row,fields[i]) : Art.Int(row,fields[i]); if (value != 0) result[keys[i]]=value; }
        if (overtime && !summon && ConfigBool("use_ultra_nimble_feet") && ((List<object>)Contract["nimbleFeet"]).Exists(v=>Convert.ToInt32(v)==id))
        { int speed=Art.Int(row,"speed"); result["SPEED"]=speed*15; result["JUMP"]=speed*4; }
        int barrier=Art.Int(row,"barrier"); if (barrier != 0) result["AURA"]=barrier;
        if (((Dictionary<string,object>)Contract["statups"]).TryGetValue(id.ToString(),out var raw))
            foreach (List<object> pair in (List<object>)raw)
            {
                string expr=(string)pair[1];
                int value=expr=="sourceid" ? id : expr=="duration" ? Art.Int(row,"time")*1000 :
                    expr=="iprop" ? Art.Int(row,"prop",100) : expr=="x << 8 | y" ? Art.Int(row,"x")<<8|Art.Int(row,"y") :
                    int.TryParse(expr,out var constant) ? constant : Art.Int(row,expr);
                result[(string)pair[0]]=value;
            }
        int morph=Art.Int(row,"morph"); if (morph>0) result["MORPH"]=morph;
        // 秘术超越 400001021：h「將潛藏的魔力開放至#x階段，每1階段魔力，最終傷害增加#z%，
        // 每#y秒開放階段減少」。满阶段 = x × z，之后每 y 秒掉 z（见 TickPeriodic）。
        // 单字母字段只在这一条上有这个含义，所以就地判定，不进全局映射表。
        if (id == 400001021)
        {
            int stages = Art.Int(row,"x"), perStage = Art.Int(row,"z");
            if (stages > 0 && perStage > 0) result["FinalDamage"] = stages * perStage;
        }
        // 爆击强化 400031023：唯一的数值字段是 x（「依照爆击几率的#x%增加爆击的伤害」），
        // 但这个 x 是「暴击率超出部分的折算比」，不是某个属性本身 —— 直接写进 stat 表
        // 会变成「暴击率 +50%」这种东西。所以只放一个标记让 ApplySkill 能注册（否则
        // stats 为空会直接 return false，连图标都没有），真正的换算在
        // PlayerCtrl.RefreshBuffAttributes 里读 x 完成。
        if (id == 400031023) result["CRIT_REINFORCE"] = 1;
        // 实用的战斗命令 400001004：h「在#time秒内所有技能等级+1」，时效侧一条 stat 都没有，
        // 所以同样只放注册标记（值 = 加成级数 x）。读取方 NativeCombatOrders.ActiveBonus
        // → SkillProgress.LevelOf；被动 asrR 走 NativeSkillEffects 的 ASR 通道。
        if (NativeSkillMechanics.Str(id, "kind") == "levelBoost")
        {
            int order = Art.Int(row, NativeSkillMechanics.Str(id, "bonusField"));
            if (order > 0) result["SKILL_ORDER"] = order;
        }
        if (((List<object>)Contract["mapChairs"]).Exists(v=>Convert.ToInt32(v)==id)) result["MAP_CHAIR"]=1;
        if (result.ContainsKey("RECOVERY") && ConfigBool("use_ultra_recovery")) result["RECOVERY"]*=10;
        // 282 的百分比增益（indieDamR / indieIgnoreMobpdpR / cr / nbdR / indieMadR …）：
        // 上面那批只认 pad/mad 这类 flat 字段，所以这些技能以前 stats 为空 ⇒ ApplySkill 直接
        // return false（按得下去、不生效、连图标都没有）。字段语义与映射表在
        // tools/ms282/export_skill_effects.py 生成，见 NativeSkillEffects。
        NativeSkillEffects.AddBuff(result,id,row);
        // 纯无敌型（虚无型态 400021060）：「在#time间进入无敌」，没有别的效果字段。
        // 持续时间照旧由 time 决定（SkillDuration），这里只补一个满额 INVINCIBLE。
        NativeShields.BuffInvincible(result,id);
        return result;
    }
    public bool ApplySkill(int id, int level, double now)
    {
        var root=WzAssets.Node("Skill/"+(id/10000).ToString("D3")+".img/skill/"+id.ToString("D7"));
        bool stateEffect=NativeSkillStates.IsTimedEffect(id);
        bool overridden=NativeSkillOverrides.IsDurationTarget(id);
        bool summon=((List<object>)Contract["summons"]).Exists(v=>Convert.ToInt32(v)==id) || overridden;
        // timedEffect 子状态没有 info/effect 节点，别拿 OverTime 把它挡掉；
        // 真正的注册入口是 NativeSkillStates.ActivateState（母技能施放时）。
        // overridden 的 summon 时长来自强化核心，不依赖 WZ 的 effect 节点，同样放行。
        if (!stateEffect && !overridden && (root==null || (!OverTime(id,root) && !summon))) return false;
        var stats=SkillStats(id,level);
        if (stats.Count==0) return false; // Monster-only effects must not become player icons.
        int duration=SkillDuration(id,level);
        if (duration<=0) return false;
        var effect=new Effect { Id=id,Level=level,DurationMs=duration,Started=now,Until=now+duration/1000.0,NextRecovery=now+(ConfigBool("use_ultra_recovery") ? 2 : 5) };
        effect.NextDragonBlood=now+4;
        effect.NextBeholderHeal=now+Art.Int(LearnedRow(1320008),"x");
        effect.NextBeholderBuff=now+Art.Int(LearnedRow(1320009),"x");
        effect.NextArcaneDecay=now+Mathf.Max(1,Art.Int(SkillBook.Get(id)?.Level(level),"y",10));
        foreach (var pair in stats) effect.Stats.Add(pair.Key,pair.Value);
        Register(effect);
        return true;
    }
    void Register(Effect effect)
    {
        var stats=effect.Stats; int id=effect.Id;
        var removed=new List<int>();
        active.RemoveAll(e=>e.Id==id);
        // applyBuffEffect cancels the previous singleton source as a whole.
        // Shared stats retain at most max_monitored_buff_stats dormant values.
        foreach (var pair in stats)
        {
            if (!shared.Contains(pair.Key))
            {
                for (int i=active.Count-1;i>=0;i--)
                    if (active[i].Stats.ContainsKey(pair.Key)) { removed.Add(active[i].Id); active.RemoveAt(i); }
                break;
            }
        }
        int limit=GameServer.Local == null ? 5 : GameConfigDao.GetInt(GameServer.Local.Db,"max_monitored_buff_stats",5);
        foreach (var pair in stats)
        {
            int count=0; Effect weakest=null;
            foreach (var previous in active)
                if (previous.Stats.TryGetValue(pair.Key,out var value))
                { count++; if (weakest==null || value<weakest.Stats[pair.Key]) weakest=previous; }
            if (count>=Math.Max(1,limit) && weakest!=null)
            { weakest.Stats.Remove(pair.Key); if (weakest.Stats.Count==0) { removed.Add(weakest.Id); active.Remove(weakest); } }
        }
        active.Add(effect);
        Rebuild();
        foreach (int previous in removed) Removed?.Invoke(previous);
        GetComponent<PlayerCtrl>()?.RefreshBuffAttributes();
    }
    public void ApplySoulWeapon(int id,int level,int durationMs)
    {
        double now=Now;
        var effect=new Effect {Id=id,Level=level,DurationMs=durationMs,Started=now,Until=now+durationMs/1000.0};
        // Actual percentages are settled once, in NativeGrowthStats.
        effect.Stats["SOUL_WEAPON_ACTIVE"]=1;Register(effect);
    }
    public void ApplyItem(int itemId,Dictionary<string,int> stats,int durationMs,double now)
    {
        var effect=new Effect { Id=-itemId,DurationMs=durationMs,Started=now,Until=now+durationMs/1000.0,
            NoShadow=NativeItemSource.Node(itemId)?.Int("info/noShadow") != 0 };
        foreach (var pair in stats) effect.Stats.Add(pair.Key,pair.Value);
        var spec=NativeItemSource.Node(itemId)?.Get("spec");
        int interval=spec?.Int("mmpRRate")>0 ? spec.Int("mmpRRate") : spec?.Int("mhpRRate") ?? 0;
        effect.NextRecovery=now+(short)(interval*100)/1000.0;
        Register(effect);
    }
    /// <summary>
    /// 282 可叠层主动 buff（普力特的祝福 400001024）：每次施放按当前层数重写
    /// 同一 skillId 的 Effect。Register 会先移除同 id 的旧 Effect，所以
    /// 「叠一层 = 保留前面所有效果 + 新增一层」正好对应逐层累加的 stat 集合。
    /// </summary>
    public void ApplyStack(int skillId,int level,Dictionary<string,int> stats,int durationMs,double now)
    {
        var effect=new Effect { Id=skillId,Level=level,DurationMs=durationMs,Started=now,Until=now+durationMs/1000.0 };
        foreach(var pair in stats) effect.Stats.Add(pair.Key,pair.Value);
        Register(effect);
    }
    public void Tick(double now)
    {
        bool changed=false;
        for (int i=active.Count-1;i>=0;i--)
            if (active[i].Until<=now) { int id=active[i].Id; active.RemoveAt(i); changed=true; Removed?.Invoke(id); }
        if (changed)
        {
            Rebuild();
            GetComponent<PlayerCtrl>()?.RefreshBuffAttributes();
        }
    }
    void Rebuild()
    {
        winners.Clear(); visible.Clear();
        bool strongest=ConfigBool("use_buff_most_significant",true);
        foreach (var effect in active)
            foreach (var pair in effect.Stats)
                if (!strongest || !winners.TryGetValue(pair.Key,out var previous) || pair.Value>previous.Stats[pair.Key] ||
                    pair.Value==previous.Stats[pair.Key] && effect.Stats.Count>=previous.Stats.Count)
                    winners[pair.Key]=effect;
        foreach (var effect in active)
            foreach (var winner in winners.Values)
                if (winner==effect) { if (effect.Id!=5211006 && effect.Id!=5220011 && effect.Id!=22151002) visible.Add(effect); break; }
    }
    public bool Cancel(int id)
    {
        if (active.RemoveAll(e=>e.Id==id)==0) return false;
        Rebuild(); Removed?.Invoke(id); GetComponent<PlayerCtrl>()?.RefreshBuffAttributes(); return true;
    }
    public bool HasSource(int id) { Tick(Now); return active.Exists(e=>e.Id==id); }
    /// <summary>
    /// 实用的战斗命令（400001004）生效中的技能等级加成；0 = 没有。
    /// 刻意不调 Tick：SkillProgress.LevelOf 在每次伤害结算里都会走这条路，加 Tick 会让
    /// 一条读等级的链路顺手重建 buff 表（并触发 RefreshBuffAttributes），既慢又容易递归。
    /// 过期由 NativeBuffs.Update 的 Tick 清掉，这里只多判一次 Until。
    /// </summary>
    public int SkillLevelBoost
    {
        get
        {
            if (!winners.TryGetValue("SKILL_ORDER", out var effect) || effect.Until <= Now) return 0;
            return effect.Stats.TryGetValue("SKILL_ORDER", out int value) ? value : 0;
        }
    }
    static Dictionary<string,object> LearnedRow(int id)
    {
        int level=SkillProgress.LevelOf(id);
        return level>0 ? SkillBook.Get(id)?.Level(level) : null;
    }
    // CUserLocal -> 0x765066: signed WZ x values add to weapon attackSpeed.
    public static int AttackSpeed(int weaponSpeed,int booster,int infusion,int skill=0)
        => Math.Max(2,Math.Min(10,weaponSpeed+booster+infusion-(skill==4001334 ? 2 : 0)));
    public int AttackSpeed(int weaponSpeed,int skill=0) => AttackSpeed(weaponSpeed,Value("BOOSTER"),Value("SPEED_INFUSION"),skill);
    public void Dispel()
    {
        if (ConfigBool("use_undispel_holy_shield") && HasSource(2321005)) return;
        var ids=new List<int>();
        foreach (var effect in active) if (effect.Id>0 && effect.Id!=21000000) ids.Add(effect.Id);
        foreach (int id in ids) Cancel(id);
    }
    public bool HasStat(string stat) { Tick(Now); return winners.ContainsKey(stat); }
    // 94B38D -> 4F0D45/4F0D64 -> 678CCF: hidden bodies skip ordinary
    // contact candidates, except 9400594 or fields with fieldLimit bit 1.
    public bool SuppressesContact(int mobId,int fieldLimit)
        => (fieldLimit&2)==0 && mobId!=9400594 && (Value("DARKSIGHT")!=0 || Value("WIND_WALK")!=0);

    // 0x958AF1..0x958BC3: field restriction precedes the clamped STANCE roll.
    public static bool ResistsKnockback(int stance,int fieldLimit,int roll)
        => (fieldLimit&2)==0 && roll<System.Math.Max(0,System.Math.Min(100,stance));
    public int SourceOf(string stat) { Tick(Now); return winners.TryGetValue(stat,out var effect) ? effect.Id : 0; }
    public void OnRangedAttack(int targets)
    {
        if (targets<=0) return;
        string stat=SkillProgress.LevelOf(14100005)>0 && HasStat("DARKSIGHT") && SourceOf("DARKSIGHT")!=9101004 ? "DARKSIGHT" :
            SkillProgress.LevelOf(13101006)>0 && HasStat("WIND_WALK") ? "WIND_WALK" : null;
        if (stat==null) return;
        RemoveAttackHidden(stat);
    }
    public void OnCloseAttack()
    {
        string stat=(SkillProgress.LevelOf(14100005)>0 || SkillProgress.LevelOf(4001003)>0) && HasStat("DARKSIGHT") ? "DARKSIGHT" :
            SkillProgress.LevelOf(13101006)>0 && HasStat("WIND_WALK") ? "WIND_WALK" : null;
        if (stat!=null) RemoveAttackHidden(stat);
    }
    void RemoveAttackHidden(string stat)
    {
        // RangedAttackHandler cancels the winner, then removes this stat from
        // suppressed holders so an older hidden buff cannot reappear.
        CancelStat(stat);
        var empty=new List<int>();
        foreach (var effect in active) { effect.Stats.Remove(stat); if (effect.Stats.Count==0) empty.Add(effect.Id); }
        foreach (int id in empty) Cancel(id);
        Rebuild(); GetComponent<PlayerCtrl>()?.RefreshBuffAttributes();
    }
    // TakeDamageHandler: contact Power Guard caps at 10% monster HP, half rate on bosses.
    public static int PowerGuardDamage(int incoming,int percent,long monsterHp,bool boss)
        => (int)Math.Max(0,Math.Min(monsterHp/10,(int)(incoming*(percent/(boss ? 200.0 : 100.0)))));
    public static int PassiveIncoming(int incoming,int barrier,int achilles,int highDefense)
    {
        if (barrier!=0) incoming=(int)(incoming*(barrier/1000.0));
        if (achilles!=0) incoming=(int)(incoming*(achilles/1000.0));
        if (highDefense!=0) incoming=(int)(incoming*Math.Ceiling(highDefense/1000.0));
        return Math.Max(0,incoming);
    }
    public void CancelStat(string stat)
    {
        Tick(Now);
        if (winners.TryGetValue(stat,out var effect)) Cancel(effect.Id);
    }
    // TakeDamageHandler: Magic Guard takes priority; Meso Guard uses integer damage / 2.
    public static void GuardLoss(int damage,int availableMp,long availableMeso,int magicGuard,int mesoGuard,
        bool hasMagicGuard,bool hasMesoGuard,int mpAttack,out int hpLoss,out int mpLoss,out long mesoLoss,out bool cancelMeso)
    {
        hpLoss=damage; mpLoss=mpAttack; mesoLoss=0; cancelMeso=false;
        if (hasMagicGuard && mpAttack==0)
        {
            mpLoss=Math.Min(availableMp,(int)(damage*(magicGuard/100.0)));
            hpLoss=damage-mpLoss;
        }
        else if (hasMesoGuard)
        {
            hpLoss=damage/2;
            long cost=(long)(hpLoss*(mesoGuard/100.0));
            cancelMeso=availableMeso<cost;
            mesoLoss=Math.Min(availableMeso,cost);
        }
    }
    public bool CancelByUser(int id)
    {
        // CUserLocal status cancellation: Aran combo is not manually cancellable.
        if (id==0x14011e || id==5101007 && Value("ENERGY_CHARGE")>0) return false;
        return Cancel(id);
    }
    public void Clear()
    {
        var ids=new List<int>(); foreach (var effect in active) ids.Add(effect.Id);
        active.Clear(); Rebuild(); foreach (int id in ids) Removed?.Invoke(id);
        GetComponent<PlayerCtrl>()?.RefreshBuffAttributes();
    }
    void Update()
    {
        var player=GetComponent<PlayerCtrl>();
        if (player != null && player.hp<=0) { if (active.Count>0) Clear(); return; }
        TickPeriodic(player,Now);
    }
    public void TickPeriodic(PlayerCtrl player,double now)
    {
        Tick(now);
        if (player==null || player.hp<=0) return;
        if (winners.TryGetValue("RECOVERY",out var effect) && now>=effect.NextRecovery)
        {
            effect.NextRecovery=now+(ConfigBool("use_ultra_recovery") ? 2 : 5);
            player.hp=Mathf.Min(player.hpMax,player.hp+(sbyte)effect.Stats["RECOVERY"]);
        }
        Effect extra=null;
        if(winners.TryGetValue("HPREC",out var hpExtra)) extra=hpExtra;
        if(winners.TryGetValue("MPREC",out var mpExtra) && (extra==null || mpExtra.Started>extra.Started)) extra=mpExtra;
        if(extra!=null && now>=extra.NextRecovery)
        {
            var spec=NativeItemSource.Node(-extra.Id)?.Get("spec");
            int interval=spec?.Int("mmpRRate")>0 ? spec.Int("mmpRRate") : spec?.Int("mhpRRate") ?? 0;
            if(interval>0)
            {
                extra.NextRecovery=now+(short)(interval*100)/1000.0;
                player.hp=Mathf.Clamp(player.hp+Value("HPREC"),0,player.hpMax);
                player.mp=Mathf.Clamp(player.mp+Value("MPREC"),0,player.mpMax);
            }
        }
        if (winners.TryGetValue("DRAGONBLOOD",out var blood) && now>=blood.NextDragonBlood)
        {
            blood.NextDragonBlood=now+4;
            player.hp=Mathf.Clamp(player.hp-blood.Stats["DRAGONBLOOD"],0,player.hpMax);
            if (player.hp<=0) { Clear(); GameLoop.I?.OnPlayerDead(); return; }
        }
        // 秘术超越 400001021：「每#y秒開放階段減少」，阶段值就是 FinalDamage 里的 z 的倍数。
        // 30 秒持续、每 10 秒掉一阶段，掉到 0 就等价于满阶段加成消失，不必提前结束 buff。
        // 注意 winners 的键是 stat 名而不是技能 id，这里要按 id 在 active 里找。
        var arcane=active.Find(e=>e.Id==400001021);
        if (arcane!=null && now>=arcane.NextArcaneDecay)
        {
            var row=LearnedRow(400001021);
            int step=Mathf.Max(1,Art.Int(row,"z",1));
            arcane.NextArcaneDecay=now+Mathf.Max(1,Art.Int(row,"y",10));
            if (arcane.Stats.TryGetValue("FinalDamage",out int left) && left>0)
            {
                arcane.Stats["FinalDamage"]=Mathf.Max(0,left-step);
                Rebuild();
                player.RefreshBuffAttributes();
            }
        }
        var beholder=active.Find(e=>e.Id==1321007);
        if (beholder==null) return;
        var healing=LearnedRow(1320008);
        if (healing!=null && now>=beholder.NextBeholderHeal)
        {
            beholder.NextBeholderHeal=now+Art.Int(healing,"x");
            // Character.addHP bypasses ZOMBIFY's skill-healing conversion.
            player.hp=Mathf.Clamp(player.hp+Art.Int(healing,"hp"),0,player.hpMax);
        }
        var hex=LearnedRow(1320009);
        if (hex!=null && now>=beholder.NextBeholderBuff)
        {
            beholder.NextBeholderBuff=now+Art.Int(hex,"x");
            ApplySkill(1320009,SkillProgress.LevelOf(1320009),now);
        }
    }
}
