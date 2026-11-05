using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Real window -> transport -> DB flow, isolated from player saves.
public static class EnhancementGrowthChecks
{
    static readonly BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static T Field<T>(string name)=>(T)typeof(NativeEnhancementWindow).GetField(name,Flags).GetValue(null);
    public static void Run()
    {
        string path=Path.Combine(Path.GetTempPath(),"enhance-growth-"+Guid.NewGuid().ToString("N")+".db");
        int exit=0;
        try
        {
            var db=DbStore.Create(path);
            var login=LoginService.Login(db,"growthwindow","pw123456");
            var made=CharFactory.CreateCharacter(db,login.accountId,0,"GrowthWindow",CharFactory.JobGroupAdventurer,
                20000,30300,0,0,1040002,1060002,1072001,1302000,0);
            int cid=made.characterId;
            db.Table("characters").Find(DbStore.Eq("id",(long)cid))["level"]=200L;
            typeof(NativeInventoryService).GetMethod("AddItems",Flags).Invoke(null,new object[]{db,CharactersDao.FindById(db,cid),1302134,1,-1L,-1});
            typeof(NativeInventoryService).GetMethod("AddItems",Flags).Invoke(null,new object[]{db,CharactersDao.FindById(db,cid),2048716,2,-1L,-1});
            typeof(NativeInventoryService).GetMethod("AddItems",Flags).Invoke(null,new object[]{db,CharactersDao.FindById(db,cid),2048753,1,-1L,-1});
            var server=new GameServer();
            typeof(GameServer).GetProperty("Db").SetValue(server,db);
            typeof(GameSession).GetProperty("Net").SetValue(null,new InProcessTransport(server));
            typeof(GameSession).GetProperty("CharacterId").SetValue(null,cid);
            typeof(GameSession).GetProperty("InGame").SetValue(null,true);
            typeof(GameSession).GetProperty("Character").SetValue(null,CharService.Load(db,cid));
            typeof(Profile).GetField("data",Flags).SetValue(null,new PlayerProfileData());
            typeof(Profile).GetField("debugOverride",Flags).SetValue(null,true);
            InventoryEntry Find(int id)=>GameSession.Character.inventory.Find(x=>x.itemId==id);
            NativeEnhancementWindow.Open(4);
            Require(NativeEnhancementWindow.Select(Find(1302134))!=null,"missing material has an explanation");
            Require(NativeEnhancementWindow.Select(Find(2048716))==null&&Field<FlameResponse>("flame").ok,"flame selection enables confirmation");
            Require(NativeEnhancementWindow.Confirm()==null&&Find(1302134).exGradeOption>0,"flame confirm settles inventory");
            Require(NativeEnhancementWindow.Select(Find(2048753))==null,"select black flame");
            Require(NativeEnhancementWindow.Confirm()==null&&Field<FlameResponse>("flame").pending,"black flame awaits choice");
            NativeEnhancementWindow.Close();NativeEnhancementWindow.Open(5);
            Require(Field<int>("page")==4&&Field<FlameResponse>("flame").pending,"reopen recovers consumed black flame");
            Require(Field<FlameResponse>("flame").candidate.Exists(x=>x.value>0),"recovered candidate has values");
            Require(NativeEnhancementWindow.ChooseFlame(true)==null,"recovered choice commits");
            var weapon=Find(1302134);
            NativeEnhancementWindow.Open(5);
            Require(NativeEnhancementWindow.Select(new InventoryEntry {itemId=2590002})!=null&&Field<InventoryEntry>("material")==null,
                "modern panel explains retired socket material instead of silently accepting it");
            Require(NativeEnhancementWindow.Select(new InventoryEntry {itemId=2591010})!=null&&Field<InventoryEntry>("material")==null,
                "modern panel explains retired soul material instead of silently accepting it");
            NativeEnhancementWindow.Select(weapon);
            typeof(NativeSoulWeaponPanel).GetField("selected",Flags).SetValue(null,weapon);
            bool Send(SoulOperation op)=>(bool)typeof(NativeSoulWeaponPanel).GetMethod("Send",Flags).Invoke(null,new object[]{op,0});
            Require(Send(SoulOperation.ActivateModern),"modern panel activation settles through transport");
            Require(GameSession.Character.soulWeapon.weapon==weapon.itemRowId,"modern panel receives bound state");
            Require(Send(SoulOperation.DeactivateModern)&&GameSession.Character.soulWeapon.weapon==0,"modern panel deactivation settles");
            SkillWindow.PickSkill(80012853);
            Require(SkillWindow.Dragging,"modern skill can be picked outside skill window");
            SkillWindow.CancelSkillDrag();
            Debug.Log("[EnhancementGrowthChecks] PASS flame, black flame recovery, modern soul activation/deactivation");
        }
        catch(Exception e){Debug.LogException(e);exit=1;}
        finally{NativeEnhancementWindow.Close();if(File.Exists(path))File.Delete(path);EditorApplication.Exit(exit);}
    }
}
