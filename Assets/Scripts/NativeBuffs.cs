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
        "WATK","WDEF","MATK","MDEF","ACC","AVOID","SPEED","JUMP"
    };
    static bool ConfigBool(string key,bool fallback=false) => GameServer.Local == null ? fallback : GameConfigDao.GetBool(GameServer.Local.Db,key,fallback);
    static string Config(string key,string fallback) => GameServer.Local == null ? fallback : GameConfigDao.Get(GameServer.Local.Db,key,fallback);
    public static int SkillDuration(int id,int level)
    {
        int duration=Art.Int(SkillBook.Get(id)?.Level(level),"time")*1000;
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
    public static bool IsHerosWill(int id) => ((List<object>)Contract["herosWill"]).Exists(v=>Convert.ToInt32(v)==id);
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
        if (((List<object>)Contract["mapChairs"]).Exists(v=>Convert.ToInt32(v)==id)) result["MAP_CHAIR"]=1;
        if (result.ContainsKey("RECOVERY") && ConfigBool("use_ultra_recovery")) result["RECOVERY"]*=10;
        return result;
    }
    public bool ApplySkill(int id, int level, double now)
    {
        var root=WzAssets.Node("Skill/"+(id/10000).ToString("D3")+".img/skill/"+id.ToString("D7"));
        if (root==null || !OverTime(id,root) && !((List<object>)Contract["summons"]).Exists(v=>Convert.ToInt32(v)==id)) return false;
        var stats=SkillStats(id,level);
        if (stats.Count==0) return false; // Monster-only effects must not become player icons.
        int duration=SkillDuration(id,level);
        if (duration<=0) return false;
        var effect=new Effect { Id=id,Level=level,DurationMs=duration,Started=now,Until=now+duration/1000.0,NextRecovery=now+(ConfigBool("use_ultra_recovery") ? 2 : 5) };
        effect.NextDragonBlood=now+4;
        effect.NextBeholderHeal=now+Art.Int(LearnedRow(1320008),"x");
        effect.NextBeholderBuff=now+Art.Int(LearnedRow(1320009),"x");
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
    public void ApplyItem(int itemId,Dictionary<string,int> stats,int durationMs,double now)
    {
        var effect=new Effect { Id=-itemId,DurationMs=durationMs,Started=now,Until=now+durationMs/1000.0,
            NoShadow=NativeItemSource.Node(itemId)?.Int("info/noShadow") != 0 };
        foreach (var pair in stats) effect.Stats.Add(pair.Key,pair.Value);
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
    public static void GuardLoss(int damage,int availableMp,int availableMeso,int magicGuard,int mesoGuard,
        bool hasMagicGuard,bool hasMesoGuard,int mpAttack,out int hpLoss,out int mpLoss,out int mesoLoss,out bool cancelMeso)
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
            int cost=(int)(hpLoss*(mesoGuard/100.0));
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
        if (winners.TryGetValue("DRAGONBLOOD",out var blood) && now>=blood.NextDragonBlood)
        {
            blood.NextDragonBlood=now+4;
            player.hp=Mathf.Clamp(player.hp-blood.Stats["DRAGONBLOOD"],0,player.hpMax);
            if (player.hp<=0) { Clear(); GameLoop.I?.OnPlayerDead(); return; }
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
