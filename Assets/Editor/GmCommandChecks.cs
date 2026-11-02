using System;
using System.IO;
using System.Reflection;
using UnityEngine;

// Real chat -> transport -> permission dispatcher -> Unity host, isolated from player saves.
public static class GmCommandSceneChecks
{
    static void Require(bool ok,string message) {if(!ok)throw new Exception(message);}
    static void Set(string name,object value)=>typeof(GameSession).GetProperty(name).SetValue(null,value);
    public static void Run()
    {
        string path=Path.Combine(Path.GetTempPath(),"mxd-gm-scene-"+Guid.NewGuid().ToString("N")+".db");
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        var oldProfile=typeof(Profile).GetField("data",flags).GetValue(null);
        var oldOverride=typeof(Profile).GetField("debugOverride",flags).GetValue(null);
        var priorLoop=GameLoop.I;
        var go=new GameObject("GmSceneChecks");
        try
        {
            typeof(Profile).GetField("data",flags).SetValue(null,new PlayerProfileData());
            typeof(Profile).GetField("debugOverride",flags).SetValue(null,true);
            var db=DbStore.Open(path);var login=LoginService.Login(db,"admin","isolated-test");
            var created=CharFactory.CreateCharacter(db,login.accountId,0,"GM场景",1,20000,30300,0,0,1040002,1060002,1072001,1302000,0);
            var server=GameServer.StartWith(db);server.Social.Commands.Host=NativeGmCommandHost.Execute;
            Set("Server",server);Set("Net",server.Transport);Set("AccountId",login.accountId);Set("CharacterId",created.characterId);Set("Character",CharService.Load(db,created.characterId));Set("InGame",true);
            var loop=go.AddComponent<GameLoop>();GameLoop.I=loop;
            var playerObject=new GameObject("GMPlayer");playerObject.transform.SetParent(go.transform);
            loop.player=playerObject.AddComponent<PlayerCtrl>();loop.field=go.AddComponent<FieldDirector>();
            typeof(FieldDirector).GetProperty("Map").SetValue(loop.field,FieldCatalog.GetMap(100000005));
            GameSession.ApplyToProfile();loop.player.ApplyProgression();loop.player.hp=loop.player.hpMax;loop.player.mp=loop.player.mpMax;
            Profile.LastMapId=100000005;
            var identity=GameSession.Character;
            Require(BottomBar.SubmitChat("!ap 88",NativeChatChannel.All),"chat AP dispatch");
            Require(ReferenceEquals(identity,GameSession.Character),"GM reload preserves map/event session identity");
            Require(Profile.ap==88 && GameSession.Character.record.ap==88,"DB changes applied to live profile");
            GameSession.SaveToDb();Require(CharactersDao.FindById(db,created.characterId).ap==88,"next save cannot overwrite command");
            Require(BottomBar.SubmitChat("!item 2000000 12",NativeChatChannel.All) && Profile.ItemCount(2000000)==12,"item visible immediately through actual chat");
            Require(BottomBar.SubmitChat("!setstat 200",NativeChatChannel.All) && Profile.Data.attributes.str==200,"stat applies to live combat profile");
            Require(BottomBar.SubmitChat("!job 100",NativeChatChannel.All) && Profile.JobId==100,"job command actual host");
            Require(BottomBar.SubmitChat("!spawn 6130101 1",NativeChatChannel.All),"boss spawn command accepted");
            EnemyUnit boss=null;foreach(var mob in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())if(mob.fieldMobId==6130101)boss=mob;
            Require(boss!=null && boss.NativeController.MaximumHp>0,"spawn creates original boss with authoritative HP");
            if(!EnemyUnit.All.Contains(boss))EnemyUnit.All.Add(boss);
            Require(BottomBar.SubmitChat("!bosshp",NativeChatChannel.All),"boss HP query through host");
            Require(BottomBar.SubmitChat("!buff 1001003",NativeChatChannel.All) && NativeBuffs.For(loop.player).HasSource(1001003),"buff changes real temporary stats");
            Require(BottomBar.SubmitChat("!startquest 1000",NativeChatChannel.All) && Profile.IsQuestStarted(1000),"quest force-start state");
            Require(BottomBar.SubmitChat("!completequest 1000",NativeChatChannel.All) && Profile.IsQuestCompleted(1000),"quest force-complete state");
            Require(BottomBar.SubmitChat("!resetquest 1000",NativeChatChannel.All) && Profile.QuestStatus(1000)==0,"quest reset state");
            // 输入容错：玩家常把指令表里的 @job 敲成 @//job/ 100，必须仍然按 job 解析。
            Require(BottomBar.SubmitChat("@//job/ 200",NativeChatChannel.All) && Profile.JobId==200,"slash tolerant command syntax");
            // 技能全满不再依赖 String/Skill.img（真机直读索引里可能没有这一层）。
            Require(BottomBar.SubmitChat("!maxskill",NativeChatChannel.All),"maxskill through host");
            bool learned=false;foreach(var job in JobDefs.All){if(job.id!=200||job.allSkills==null)continue;
                foreach(int skill in job.allSkills)if(SkillProgress.LevelOf(skill)>0)learned=true;}
            Require(learned,"maxskill actually raises job skills");
            Require(BottomBar.SubmitChat("!resetskill",NativeChatChannel.All),"resetskill through host");
            // 三转/四转 id：ClsOf 以前只认一转/二转代表 id，121 之类会落到 "beginner"，
            // 于是职业真的改了、状态栏却还写着「新手」（玩家看到的就是「指令没生效」）。
            Require(BottomBar.SubmitChat("@//job/ 121",NativeChatChannel.All) && Profile.JobId==121,"three-tier job id through slash tolerant syntax");
            Require(Profile.cls=="warrior","three-tier job keeps its art class instead of beginner");
            Require(JobDefs.NameOf(Profile.JobId)=="骑士","status bar label uses the original Job.img name");
            Require(GameSession.Character.record.job==121,"job reaches the DB record");
            Require(BottomBar.SubmitChat("@job 1112",NativeChatChannel.All) && Profile.JobId==1112 && Profile.cls=="warrior","cygnus job borrows the warrior art class");
            // 权限：普通账号（gm=0）跑 GM2 指令必须被明确拒绝，而不是静默「没生效」。
            // 注意 admin 账号会被 AccountsDao.DefaultGmLevel 抬到 6，所以这里直接打纯服务端分发器。
            var anon=CharactersDao.FindById(db,created.characterId);anon.gm=0;
            var denied=server.Social.Commands.Execute(anon,"@//job/ 121");
            Require(!denied.ok && denied.error.Contains("权限") && Profile.JobId==1112,"gm0 player gets an explicit permission denial");
            Require(BottomBar.SubmitChat("!healmap",NativeChatChannel.All),"heal map through host");
            Require(BottomBar.SubmitChat("!timer 5",NativeChatChannel.All),"timer command through host");
            Debug.Log("GM_COMMAND_SCENE_PASS chat profile_persistence inventory stats job boss_spawn boss_hp buff quest");
        }
        finally
        {
            foreach(var mob in UnityEngine.Object.FindObjectsOfType<EnemyUnit>()) {EnemyUnit.All.Remove(mob);UnityEngine.Object.DestroyImmediate(mob.gameObject);}
            UnityEngine.Object.DestroyImmediate(go);GameLoop.I=priorLoop;Set("InGame",false);GameServer.Stop();
            typeof(Profile).GetField("data",flags).SetValue(null,oldProfile);typeof(Profile).GetField("debugOverride",flags).SetValue(null,oldOverride);
            if(File.Exists(path))File.Delete(path);
        }
    }
}
