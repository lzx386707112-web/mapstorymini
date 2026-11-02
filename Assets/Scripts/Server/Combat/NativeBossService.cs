using System;
using System.Collections.Generic;

public enum BossOperation { ReadGrowth, RecordKill, CheckEntry, Enter }
public sealed class BossRequest
{
    public int characterId, mobId, channel = 1;
    public BossOperation operation;
    public string boss = "", encounter = "";
}
public sealed class BossResponse
{
    public bool ok = true;
    public string error = "";
    public double multiplier = 1;
    public double dropRate = 1;
    public int questCount, monsterCardCount;
    public double questBonusPercent, bossBonusPercent, monsterCardBonusPercent, bonusPercent;
    public string message;
}

// BossDamageGrowth / ExpeditionBossLog, persistence on the local server.
// Encounter identifiers deduplicate repeated kill notifications within a session.
public sealed class NativeBossService
{
    readonly DbStore db;
    readonly HashSet<string> recorded = new HashSet<string>();
    readonly Dictionary<int,(long expires,double multiplier)> cache = new Dictionary<int,(long,double)>();
    public Func<DateTimeOffset> Clock = () => DateTimeOffset.Now;
    sealed class Growth { public string key; public double maximum; public Growth(string key,double maximum) { this.key=key; this.maximum=maximum; } }
    static readonly Dictionary<int,Growth> Growths = new Dictionary<int,Growth> {
        {8500002,new Growth("papulatus",1)}, {9420544,new Growth("scarga",1.2)},
        {9600025,new Growth("yaoseng",1.4)}, {9420522,new Growth("krexel",1.6)},
        {8800002,new Growth("zakum",1.8)}, {9400300,new Growth("showa",2.4)},
        {8810018,new Growth("horntail",2.6)}, {8820001,new Growth("pinkbean",2.8)},
        {9400265,new Growth("tokyo_vergamot",3)}, {9400270,new Growth("tokyo_dunas",3.2)},
        {9400273,new Growth("tokyo_nibergen",3.4)}, {9400266,new Growth("tokyo_nux",3.6)},
        {9400294,new Growth("tokyo_dunas2",3.8)}, {9400289,new Growth("tokyo_aufheben",4)},
        {8840000,new Growth("vonleon",4.2)} };
    static readonly Dictionary<int,string> GrowthNames=new Dictionary<int,string> {
        {8500002,"帕普拉图斯"},{9420544,"狮熊双王"},{9600025,"武林妖僧"},{9420522,"克雷塞尔"},
        {8800002,"扎昆"},{9400300,"昭和大头老板"},{8810018,"暗黑龙王"},{8820001,"品克缤"},
        {9400265,"贝尔加莫特"},{9400270,"都纳斯"},{9400273,"尼贝隆"},{9400266,"努克斯"},
        {9400294,"再生都纳斯"},{9400289,"欧碧拉"},{8840000,"狮子王"} };
    static readonly Dictionary<string,int> Entries = new Dictionary<string,int> {
        {"ZAKUM",3},{"CHAOS_ZAKUM",5},{"HORNTAIL",3},{"CHAOS_HORNTAIL",5},
        {"CYGNUS",1},{"AKAYRUM",1},{"LUCID",1},{"PINKBEAN",3},{"SCARGA",1},{"PAPULATUS",2} };
    public NativeBossService(DbStore db) { this.db = db; }
    public BossResponse Handle(BossRequest r)
    {
        lock (db)
        using (db.DeferSaves())
        {
            var character = CharactersDao.FindById(db,r.characterId);
            if (character == null) return new BossResponse { ok=false,error="角色不存在" };
            var result = new BossResponse();
            var world = db.Table("world_prop").Find(x => DbStore.Int(x,"id") == character.world);
            result.dropRate = DbStore.Dbl(world,"boss_drop_rate",1);
            if (r.operation == BossOperation.RecordKill && Growths.TryGetValue(r.mobId,out var growth))
            {
                if (string.IsNullOrEmpty(r.encounter)) return new BossResponse { ok=false,error="缺少击杀会话" };
                string unique = r.characterId + ":" + r.encounter;
                if (recorded.Add(unique))
                {
                    int previous=KillCount(r.characterId,growth.key);
                    new NativeScriptValues(db).Handle(new ScriptValueRequest {characterId=r.characterId,write=true,
                        name="boss_growth_kill_"+growth.key,value=(previous==int.MaxValue?previous:previous+1).ToString(System.Globalization.CultureInfo.InvariantCulture)});
                    cache.Remove(r.characterId);
                    int effective=Math.Min(10,previous==int.MaxValue?previous:previous+1);
                    result.message="[BOSS记录] 击败"+GrowthNames[r.mobId]+"，进度 "+effective+"/10，BOSS伤害 +"+
                        (effective/10.0*growth.maximum).ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                        "% / +"+growth.maximum.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"%。";
                    db.Save();
                }
            }
            if ((r.operation == BossOperation.CheckEntry || r.operation == BossOperation.Enter) &&
                GameConfigDao.GetBool(db,"use_enable_daily_expeditions") && Entries.TryGetValue(r.boss ?? "",out int maximum))
            {
                var time = Clock();
                long start = new DateTimeOffset(time.Date,time.Offset).ToUnixTimeMilliseconds();
                var table = db.Table("bosslog_daily");
                table.Delete(x => DbStore.Long(x,"attempttime") < start);
                int count = table.Select(x => DbStore.Int(x,"characterid") == r.characterId && DbStore.Str(x,"bosstype") == r.boss).Count;
                if (r.channel < 0 || count >= maximum) { result.ok=false; result.error="已达到该 Boss 的挑战次数限制"; }
                else if (r.operation == BossOperation.Enter)
                {
                    table.Insert(new DbRow { ["characterid"]=(long)r.characterId,["bosstype"]=r.boss,["attempttime"]=Clock().ToUnixTimeMilliseconds() });
                    db.Save();
                }
            }
            FillGrowth(r.characterId,result);
            return result;
        }
    }
    public static double Bonus(int quests, double bossPercent, int cards)
        => Math.Min(100,Math.Min(1,Math.Max(0,quests)/1000.0)*30 + Math.Min(40,Math.Max(0,bossPercent)) + Math.Min(1,Math.Max(0,cards)/300.0)*30);
    int KillCount(int cid,string key)
    {
        var value=new NativeScriptValues(db).Handle(new ScriptValueRequest {characterId=cid,name="boss_growth_kill_"+key}).value;
        if(value!=null)return int.TryParse(value,out int count)?Math.Max(0,count):0;
        // Preserve progress from the earlier local-only table, then use the source extend key.
        var legacy=db.Table("boss_growth").Find(x=>DbStore.Int(x,"characterid")==cid && DbStore.Str(x,"boss")==key);
        int kills=(int)Math.Min(int.MaxValue,Math.Max(0,DbStore.Long(legacy,"kills")));
        if(legacy!=null)new NativeScriptValues(db).Handle(new ScriptValueRequest {characterId=cid,write=true,
            name="boss_growth_kill_"+key,value=kills.ToString(System.Globalization.CultureInfo.InvariantCulture)});
        return kills;
    }
    public void Invalidate(int cid)=>cache.Remove(cid);
    void FillGrowth(int cid,BossResponse result)
    {
        int quests = db.Table("queststatus").Select(x => DbStore.Int(x,"characterid") == cid && DbStore.Int(x,"status") == 2).Count;
        int cards = db.Table("monsterbook").Select(x => DbStore.Int(x,"charid") == cid).Count;
        long now = Clock().ToUnixTimeMilliseconds();
        double bonus;
        if (cache.TryGetValue(cid,out var cached) && cached.expires > now) bonus = cached.multiplier;
        else
        {
            bonus = 0;
            foreach (var growth in Growths.Values)
            {
                bonus += Math.Min(1,KillCount(cid,growth.key)/10.0)*growth.maximum;
            }
            cache[cid] = (now+300000,bonus);
        }
        result.questCount=quests;result.monsterCardCount=cards;
        result.questBonusPercent=Math.Min(1,quests/1000.0)*30;
        result.monsterCardBonusPercent=Math.Min(1,cards/300.0)*30;
        result.bossBonusPercent=Math.Min(40,Math.Max(0,bonus));
        result.bonusPercent=Bonus(quests,bonus,cards);result.multiplier=1+result.bonusPercent/100;
    }
    public void DeleteCharacter(int cid)
    {
        db.Table("boss_growth").Delete(x => DbStore.Int(x,"characterid") == cid);
        db.Table("bosslog_daily").Delete(x => DbStore.Int(x,"characterid") == cid);
        cache.Remove(cid);
    }
}
