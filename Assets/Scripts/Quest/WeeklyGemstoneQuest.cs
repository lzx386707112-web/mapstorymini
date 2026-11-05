using System;
using System.Collections.Generic;
using UnityEngine;

// 2026-11-04 用户授权重建；原IMG文字，复用标准任务/NPC对话框。
public static class WeeklyGemstoneQuest
{
    [Serializable] sealed class Data
    {
        public int id,npc,level,prerequisite,required,dailyRequired,rewardItem,rewardCount;
        public string name,demand,reward;
        public string[] info;
    }
    static QuestDef definition;
    static WeeklyGemstoneResponse cached;
    static int owner;
    static DateTime until;
    public static void Register(Dictionary<int,QuestDef> cache,List<int> ids,Dictionary<int,List<int>> byNpc)
    {
        var asset=Resources.Load<TextAsset>("Quests/weekly-gemstone");
        if(asset==null) throw new InvalidOperationException("Missing 282 weekly gemstone quest data");
        var d=JsonUtility.FromJson<Data>(asset.text);
        if(ids.Contains(d.id)) throw new InvalidOperationException("282 weekly quest conflicts with WZ: "+d.id);
        var q=new QuestDef { id=d.id,name=d.name,area=207,info=d.info,autoStart=true,autoComplete=true,
            summary=d.info[0],demandSummary="[每日任務] 調查消逝的旅途 0 / "+d.required,rewardSummary=d.reward };
        for(int i=0;i<d.info.Length;i++) q.stateInfo[i]=d.info[i];
        q.startReq.npc=q.completeReq.npc=d.npc;
        q.startReq.level=d.level;
        // 先决条件/计数/奖励由服务端权威检查，不能把奖励再放进completeAct重复发放。
        definition=q;
        cache[q.id]=q; ids.Add(q.id);
        if(!byNpc.TryGetValue(d.npc,out var list)) byNpc[d.npc]=list=new List<int>();
        list.Add(q.id);
    }
    public static void Invalidate() { cached=null; }
    public static WeeklyGemstoneResponse Read(bool force=false)
    {
        if(!GameSession.InGame) return new WeeklyGemstoneResponse();
        if(!force && cached!=null && owner==GameSession.CharacterId && DateTime.UtcNow<until) return cached;
        owner=GameSession.CharacterId;
        cached=GameSession.Net.WeeklyGemstone(new WeeklyGemstoneRequest {characterId=owner});
        until=DateTime.UtcNow.AddSeconds(1);
        foreach(int id in cached.resetQuests)
        {
            Profile.Data.ResetQuest(id);
            var entry=GameSession.Character.quests.Find(q=>q.quest==id);
            if(entry!=null) {entry.status=0;entry.completed=0;}
        }
        int status=cached.claimed ? PlayerProfileData.QuestCompletedState : cached.started ? PlayerProfileData.QuestStartedState : 0;
        if(Profile.Data.QuestStatusOf(WeeklyGemstoneData.Id)!=status)
        {
            Profile.Data.ResetQuest(WeeklyGemstoneData.Id);
            if(cached.claimed) Profile.Data.SetQuestCompleted(WeeklyGemstoneData.Id);
            else if(cached.started) Profile.Data.SetQuestStarted(WeeklyGemstoneData.Id);
        }
        if(definition!=null) definition.demandSummary="[每日任務] 調查消逝的旅途 "+cached.clear+" / "+WeeklyGemstoneData.Required;
        Profile.Data.SetQuestProgress(WeeklyGemstoneData.Id,"clear",cached.clear.ToString());
        return cached;
    }
    public static int Status()
    {
        var r=Read();
        return r.claimed ? PlayerProfileData.QuestCompletedState : r.started ? PlayerProfileData.QuestStartedState : 0;
    }
    public static bool Can(bool complete,int npc,out string reason)
    {
        var r=Read(); reason=null;
        if(!r.eligible) reason="需达到 Lv."+WeeklyGemstoneData.Level+" 并完成消逝的旅途前置任务";
        else if(npc>0 && npc!=WeeklyGemstoneData.Npc) reason="请找萝娜领取";
        else if(r.claimed) reason="本周奖励已经领取";
        else if(complete && r.clear<WeeklyGemstoneData.Required) reason="本周调查进度 "+r.clear+" / "+WeeklyGemstoneData.Required;
        else if(!complete && r.started) reason="任务已在进行中";
        return reason==null;
    }
    public static bool Run(bool claim)
    {
        // 先落盘其他任务/背包，再做原子发奖，防旧快照覆盖新奖励。
        Profile.Save();
        var r=GameSession.Net.WeeklyGemstone(new WeeklyGemstoneRequest {characterId=GameSession.CharacterId,
            operation=claim ? WeeklyGemstoneOperation.Claim : WeeklyGemstoneOperation.Start});
        if(!r.ok) { BottomBar.Toast(r.error); Invalidate(); return false; }
        GameSession.ReloadAfterCommand(); Invalidate(); Read(true);
        return true;
    }
}
