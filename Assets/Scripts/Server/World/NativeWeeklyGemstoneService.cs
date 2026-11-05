using System;
using System.Collections.Generic;
using System.Globalization;

public enum WeeklyGemstoneOperation { Read, Start, Claim }
public sealed class WeeklyGemstoneRequest
{
    public int characterId;
    public WeeklyGemstoneOperation operation;
}
public sealed class WeeklyGemstoneResponse
{
    public bool ok = true, eligible, claimed, started;
    public string error = "";
    public int clear;
    public readonly List<int> resetQuests = new List<int>();
}

// 用户授权重建：原脚本缺失。每周四00:00（周三结束，TMS UTC+8）重置。
// 复用现有日调查；34129的旧映射-31407只是引导，绝不直接计次。
public static class NativeWeeklyGemstoneService
{
    // extend_type=31 保留给服务端任务账本；脚本键值API只能写11..13/21..23。
    const int LedgerType = 31;
    const string LedgerName = "tms282.weeklyGemstone";
    sealed class State
    {
        public string week = "", day = "", creditedDay = "";
        public int clear;
        public bool claimed, started;
        public HashSet<int> tasks = new HashSet<int>();
    }
    public static DateTimeOffset LocalTime(DateTimeOffset now) => now.ToOffset(TimeSpan.FromHours(8));
    public static string Week(DateTimeOffset now)
    {
        var date = LocalTime(now).Date;
        return date.AddDays(-((7 + (int)date.DayOfWeek - (int)DayOfWeek.Thursday) % 7)).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }
    static string Day(DateTimeOffset now) => LocalTime(now).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    static int Canonical(int quest) => quest < 0 ? quest + 65536 : quest;
    public static bool IsDaily(int quest) => Array.IndexOf(WeeklyGemstoneData.DailyIds, Canonical(quest)) >= 0;
    static DbRow Row(DbStore db, int cid) => db.Table("extend_value").Find(r =>
        DbStore.Int(r,"extend_type") == LedgerType && DbStore.Str(r,"extend_id") == cid.ToString(CultureInfo.InvariantCulture)
        && DbStore.Str(r,"extend_name") == LedgerName);
    static State Load(DbRow row)
    {
        var s = new State();
        var parts = (row == null ? "" : DbStore.Str(row,"extend_value")).Split('|');
        if (parts.Length != 7) return s;
        s.week=parts[0]; s.day=parts[1]; s.creditedDay=parts[2];
        int.TryParse(parts[3],out s.clear); s.claimed=parts[4]=="1"; s.started=parts[5]=="1";
        foreach(string text in parts[6].Split(',')) if(int.TryParse(text,out int id)) s.tasks.Add(id);
        return s;
    }
    static bool Store(DbStore db, int cid, State s, DateTimeOffset now)
    {
        string value = string.Join("|",s.week,s.day,s.creditedDay,s.clear.ToString(CultureInfo.InvariantCulture),
            s.claimed ? "1" : "0",s.started ? "1" : "0",string.Join(",",s.tasks));
        var row=Row(db,cid);
        if(row!=null && DbStore.Str(row,"extend_value")==value) return false;
        if(row==null) db.Table("extend_value").Insert(new DbRow {
            ["extend_type"]=(long)LedgerType,["extend_id"]=cid.ToString(CultureInfo.InvariantCulture),
            ["extend_name"]=LedgerName,["extend_value"]=value,["create_time"]=now.ToUnixTimeMilliseconds(),["update_time"]=0L });
        else { row["extend_value"]=value; row["update_time"]=now.ToUnixTimeMilliseconds(); }
        return true;
    }
    static bool Eligible(DbStore db, int cid)
    {
        var c=CharactersDao.FindById(db,cid);
        return c!=null && c.level>=WeeklyGemstoneData.Level &&
            (QuestStatusDao.StatusOf(db,cid,WeeklyGemstoneData.Prerequisite)==2 ||
             QuestStatusDao.StatusOf(db,cid,WeeklyGemstoneData.Prerequisite-65536)==2);
    }
    static void RollWeek(State s, DateTimeOffset now)
    {
        string week=Week(now);
        if(s.week==week) return;
        s.week=week; s.clear=0; s.claimed=false; s.started=false;
        // 同一日不会跨周；日进度由Read重置，以便同时清除客户端镜像。
    }
    public static WeeklyGemstoneResponse Handle(DbStore db, WeeklyGemstoneRequest req, DateTimeOffset? instant=null)
    {
        var response=new WeeklyGemstoneResponse();
        if(db==null || req==null || CharactersDao.FindById(db,req.characterId)==null)
        { response.ok=false; response.error="角色不存在"; return response; }
        lock(db)
        {
            var now=instant ?? DateTimeOffset.UtcNow;
            var state=Load(Row(db,req.characterId));
            RollWeek(state,now);
            string day=Day(now);
            if(state.day!=day)
            {
                if(state.day!="")
                    foreach(var r in QuestStatusDao.ByCharacter(db,req.characterId))
                    {
                        int quest=DbStore.Int(r,"quest");
                        if(!IsDaily(quest)) continue;
                        r["status"]=0L; r["completed"]=0L;
                        response.resetQuests.Add(quest);
                    }
                state.day=day; state.tasks.Clear();
            }
            response.eligible=Eligible(db,req.characterId);
            if(req.operation!=WeeklyGemstoneOperation.Read && !response.eligible)
            { response.ok=false; response.error="需达到等级并完成消逝的旅途前置任务"; }
            else if(req.operation==WeeklyGemstoneOperation.Start) state.started=true;
            else if(req.operation==WeeklyGemstoneOperation.Claim)
            {
                if(state.claimed) { response.ok=false; response.error="本周奖励已经领取"; }
                else if(state.clear<WeeklyGemstoneData.Required) { response.ok=false; response.error="本周调查次数不足"; }
                else
                {
                    var c=CharactersDao.FindById(db,req.characterId);
                    // 发放和领奖标记同一事务；满包异常回滚，保留可领奖状态。
                    db.BeginTransaction();
                    try
                    {
                        NativeInventoryService.AddItems(db,c,WeeklyGemstoneData.RewardItem,WeeklyGemstoneData.RewardCount,-1);
                        state.claimed=true; state.started=true;
                        Store(db,req.characterId,state,now);
                        db.Save(); db.Commit();
                    }
                    catch(InvalidOperationException)
                    { db.Rollback(); state.claimed=false; response.ok=false; response.error="消耗栏空间不足，请整理背包后重试"; }
                    catch { db.Rollback(); throw; }
                }
            }
            if(Store(db,req.characterId,state,now) || response.resetQuests.Count>0) db.Save();
            response.clear=state.clear; response.claimed=state.claimed; response.started=state.started;
            return response;
        }
    }
    // QuestStatusDao仅在真实“未完成→完成”时调用；重复保存/重复脚本回调不计次。
    public static void OnCompleted(DbStore db,int cid,int quest,DateTimeOffset? instant=null)
    {
        if(!IsDaily(quest) || !Eligible(db,cid)) return;
        var now=instant ?? DateTimeOffset.UtcNow;
        var state=Load(Row(db,cid)); RollWeek(state,now);
        string day=Day(now);
        if(state.day!=day) { state.day=day; state.tasks.Clear(); }
        state.tasks.Add(Canonical(quest));
        if(state.tasks.Count>=WeeklyGemstoneData.DailyRequired && state.creditedDay!=day)
        {
            state.clear=Math.Min(WeeklyGemstoneData.Required,state.clear+1);
            state.creditedDay=day; state.started=true;
        }
        Store(db,cid,state,now);
    }
}
