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
    static readonly Dictionary<string,int> Entries = new Dictionary<string,int> {
        {"ZAKUM",3},{"CHAOS_ZAKUM",5},{"HORNTAIL",3},{"CHAOS_HORNTAIL",5},
        {"CYGNUS",1},{"AKAYRUM",1},{"LUCID",1},{"PINKBEAN",3},{"SCARGA",1},{"PAPULATUS",2} };
    public NativeBossService(DbStore db) { this.db = db; }
    public BossResponse Handle(BossRequest r)
    {
        lock (db)
        {
            if (CharactersDao.FindById(db,r.characterId) == null) return new BossResponse { ok=false,error="角色不存在" };
            var result = new BossResponse();
            if (r.operation == BossOperation.RecordKill && Growths.TryGetValue(r.mobId,out var growth))
            {
                if (string.IsNullOrEmpty(r.encounter)) return new BossResponse { ok=false,error="缺少击杀会话" };
                string unique = r.characterId + ":" + r.encounter;
                if (recorded.Add(unique))
                {
                    var row = db.Table("boss_growth").Find(x => DbStore.Int(x,"characterid") == r.characterId && DbStore.Str(x,"boss") == growth.key);
                    if (row == null) db.Table("boss_growth").Insert(new DbRow { ["characterid"]=(long)r.characterId,["boss"]=growth.key,["kills"]=1L });
                    else row["kills"] = Math.Min(int.MaxValue,DbStore.Long(row,"kills") + 1);
                    cache.Remove(r.characterId);
                    db.Save();
                }
            }
            if ((r.operation == BossOperation.CheckEntry || r.operation == BossOperation.Enter) &&
                GameConfigDao.GetBool(db,"use_enable_daily_expeditions") && Entries.TryGetValue(r.boss ?? "",out int maximum))
            {
                long start = new DateTimeOffset(Clock().Date,Clock().Offset).ToUnixTimeMilliseconds();
                var table = db.Table("bosslog_daily");
                table.Delete(x => DbStore.Long(x,"attempttime") <= start);
                int count = table.Select(x => DbStore.Int(x,"characterid") == r.characterId && DbStore.Str(x,"bosstype") == r.boss).Count;
                if (r.channel < 0 || count >= maximum) { result.ok=false; result.error="已达到该 Boss 的挑战次数限制"; }
                else if (r.operation == BossOperation.Enter)
                {
                    table.Insert(new DbRow { ["characterid"]=(long)r.characterId,["bosstype"]=r.boss,["attempttime"]=Clock().ToUnixTimeMilliseconds() });
                    db.Save();
                }
            }
            result.multiplier = Multiplier(r.characterId);
            return result;
        }
    }
    public static double Bonus(int quests, double bossPercent, int cards)
        => Math.Min(100,Math.Min(1,Math.Max(0,quests)/1000.0)*30 + Math.Min(40,Math.Max(0,bossPercent)) + Math.Min(1,Math.Max(0,cards)/300.0)*30);
    double Multiplier(int cid)
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
                var row = db.Table("boss_growth").Find(x => DbStore.Int(x,"characterid") == cid && DbStore.Str(x,"boss") == growth.key);
                bonus += Math.Min(1,Math.Max(0,DbStore.Long(row,"kills"))/10.0)*growth.maximum;
            }
            cache[cid] = (now+300000,bonus);
        }
        return 1 + Bonus(quests,bonus,cards)/100;
    }
    public void DeleteCharacter(int cid)
    {
        db.Table("boss_growth").Delete(x => DbStore.Int(x,"characterid") == cid);
        db.Table("bosslog_daily").Delete(x => DbStore.Int(x,"characterid") == cid);
        cache.Remove(cid);
    }
}
