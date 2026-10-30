using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class InventoryPortalChecks
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static void Require(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Session(string key, object value) => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null,new[] { value });
    public static void Run()
    {
        int exit = 0;
        string path = Path.Combine(Path.GetTempPath(), "inventory-portal-"+Guid.NewGuid()+".json");
        var oldData = Profile.Data;
        var profileField = typeof(Profile).GetField("data",Flags);
        var saving = typeof(Profile).GetField("debugOverride",Flags);
        object previousSaving = saving.GetValue(null);
        var keys = KeyMap.ExportCharacter();
        var oldLoop = GameLoop.I;
        int oldMap=Profile.LastMapId;
        GameObject go = null, mapGo = null;
        try
        {
            profileField.SetValue(null,new PlayerProfileData()); saving.SetValue(null,true);
            var db = DbStore.Create(path); var server = GameServer.StartWith(db);
            var character = new CharRecord { accountid=1,name="InventoryAudit",level=200,face=20000,hair=30000,hp=500,maxhp=500,maxmp=500,equipslots=96,useslots=96,setupslots=96,etcslots=96 };
            character.id=CharactersDao.Insert(db,character);
            Session("Net",server.Transport); Session("CharacterId",(int)character.id); Session("InGame",true);
            foreach (int item in new[] {1122017,1189999,1010000,1112000,1112001,2029001,2431158})
            {
                int type=item/1000000;
                var result=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=(int)character.id,itemId=item,quantity=item==2431158?2:1});
                Require(result.ok,"Cannot seed item "+item);
            }
            Session("Character",CharService.Load(db,(int)character.id));
            Profile.Data.level=200; Profile.Data.jobId=0;
            GameSession.ApplyInventoryToProfile();
            var attrs=new CharacterAttributes {level=200,str=999,dex=999,intelligence=999,luk=999};
            foreach (int item in new[] {1122017,1189999,1010000,1112000,1112001})
                Require(EquipLoadout.Equip(item,attrs)==null,"Equip rejected "+item);
            Require(Profile.EquipOfSlot(-17)==1122017,"Pendant disappeared from profile mirror");
            Require(Profile.EquipOfSlot(-53)==1189999,"Frenzy must use totem slot, not badge");
            Require(Profile.EquipOfSlot(-12)==1112000 && Profile.EquipOfSlot(-13)==1112001,"Rings replaced each other");
            Require(SkillProgress.LevelOf(1016)==1,"Frenzy equipment did not grant native beginner skill");
            EquipLoadout.Unequip(-17); Require(Profile.EquipOfSlot(-17)==0 && Profile.ItemCount(1122017)==1,"Pendant unequip lost item");
            var binding=new List<KeymapEntry> { new KeymapEntry { key=NativeKeyLayout.ToScanCode(KeyCode.F1),type=2,action=2029001 } };
            KeyMap.LoadCharacter(binding);
            Require(QuickSlots.Id(0)==-2029001,"Item keymap read as skill");
            var exported=KeyMap.ExportCharacter();
            Require(exported.Exists(k=>k.type==2 && k.action==2029001 && k.key==binding[0].key),"Item keymap lost type on export");
            KeyConfigWindow.Toggle();
            Pointer.Pressed=false; NativeInventoryActions.Pick(2431158); NativeInventoryActions.CarryPointerStep();
            Require(NativeInventoryActions.Carrying,"Item must remain carried after initial press/release");
            Pointer.Pos=KeyConfigWindow.KeyCenter(KeyCode.F2); Pointer.Pressed=true;
            NativeInventoryActions.CarryPointerStep();
            Require(!NativeInventoryActions.Carrying && !Pointer.Pressed,"Item keyboard drop leaked same press");
            Pointer.Released=Pointer.Clicked=true; NativeInventoryActions.CarryPointerStep();
            Require(!Pointer.Clicked && !Pointer.Released,"Binding click must not use the item on release");
            Require(KeyConfigWindow.DraftUsers(KeyCode.F2)==1,"Item drop must claim one key");
            KeyConfigWindow.Toggle();
            go=new GameObject("InventoryPortalPlayer"); GameLoop.I=null;
            var loop=go.AddComponent<GameLoop>(); GameLoop.I=loop;
            var player=go.AddComponent<PlayerCtrl>(); loop.player=player;
            typeof(PlayerCtrl).GetMethod("Awake",Flags).Invoke(player,null);
            player.hp=player.hpMax=500; player.mpMax=500;
            loop.field=go.AddComponent<FieldDirector>();
            typeof(FieldDirector).GetField("<Map>k__BackingField",Flags).SetValue(loop.field,new FieldMap {id=104000000});
            Require(NativeInventoryActions.TryUse(2029001)==null,"Shortcut menu failed");
            Require(NpcSession.Active!=null && NpcSession.Active.LastError==null && NpcSession.Blocking,"Shortcut menu did not open original NPC");
            Require(Profile.ItemCount(2029001)==1,"Reusable shortcut menu consumed"); NpcSession.CloseActive();
            Require(NativeInventoryActions.TryUse(2431158)==null,"Vac script entry rejected");
            Require(NpcSession.Active?.LastError==null && player.GetComponent<NativeMonsterVac>()?.Active==true,"Vac script host failed");
            Require(Profile.ItemCount(2431158)==1,"Vac script did not consume exactly one"); NpcSession.CloseActive();
            NativeInventoryActions.TryUse(2431158); NpcSession.CloseActive();
            Require(Profile.ItemCount(2431158)==1,"Stacking vacuum consumed another item");
            mapGo=new GameObject("LithHiddenPortal"); var view=mapGo.AddComponent<NativeMapView>(); view.Show(104000000);
            var portal=mapGo.transform.Find("Portal_30");
            Require(portal!=null && !portal.GetComponent<SpriteRenderer>().enabled,"Hidden portal should start hidden");
            player.PlaceAt(-572f/Art.PPU,-191f/Art.PPU);
            typeof(NativeMapView).GetMethod("TickHiddenPortals",Flags).Invoke(view,null);
            Require(mapGo.transform.Find("Portal_30_start").GetComponent<SpriteRenderer>().enabled,"Ship hidden portal does not appear nearby");
            FieldCatalog.LoadMob(100100); FieldCatalog.LoadMob(8800000);
            var mobGo=new GameObject("VacNormal"); mobGo.transform.SetParent(go.transform);
            var mob=mobGo.AddComponent<EnemyUnit>(); mob.fieldMode=true; mob.fieldMobId=100100; mob.hp=mob.hpMax=100;
            mob.transform.position=new Vector3(10,0,0);
            var bossGo=new GameObject("VacBoss"); bossGo.transform.SetParent(go.transform);
            var boss=bossGo.AddComponent<EnemyUnit>(); boss.fieldMode=true; boss.fieldMobId=8800000; boss.hp=boss.hpMax=100;
            boss.transform.position=new Vector3(10,0,0);
            // Batch edit mode does not invoke MonoBehaviour.OnEnable automatically.
            if(!EnemyUnit.All.Contains(mob)) EnemyUnit.All.Add(mob);
            if(!EnemyUnit.All.Contains(boss)) EnemyUnit.All.Add(boss);
            var vac=player.GetComponent<NativeMonsterVac>();
            typeof(NativeMonsterVac).GetField("nextPull",Flags).SetValue(vac,0d);
            typeof(NativeMonsterVac).GetMethod("Update",Flags).Invoke(vac,null);
            Require(mob.transform.position.x<10 && mob.VacuumLocked,"Vacuum did not pull/lock a normal monster");
            Require(boss.transform.position.x==10 && !boss.VacuumLocked,"Vacuum pulled a boss");
            loop.field.GetType().GetField("<Map>k__BackingField",Flags).SetValue(loop.field,new FieldMap {id=100000000});
            typeof(NativeMonsterVac).GetMethod("Update",Flags).Invoke(player.GetComponent<NativeMonsterVac>(),null);
            Require(!player.GetComponent<NativeMonsterVac>().Active && !mob.VacuumLocked,"Map change retained vacuum/monster lock");
            checks+=InventoryItemFunctionChecks.Check(db,(int)character.id,player,loop.field,server.Bots);
            Debug.Log("[InventoryPortalChecks] PASS "+checks);
        }
        catch(Exception e) { exit=1; Debug.LogException(e); }
        finally
        {
            NpcSession.CloseActive();
            if(KeyConfigWindow.Visible) KeyConfigWindow.Toggle();
            Session("InGame",false); Session("Character",null); Session("CharacterId",0); Session("Net",null);
            GameServer.Stop();
            if(mapGo!=null) UnityEngine.Object.DestroyImmediate(mapGo);
            if(go!=null) UnityEngine.Object.DestroyImmediate(go);
            GameLoop.I=oldLoop; profileField.SetValue(null,oldData); saving.SetValue(null,previousSaving); KeyMap.LoadCharacter(keys);
            Profile.LastMapId=oldMap;
            if(File.Exists(path)) File.Delete(path);
        }
        if(Application.isBatchMode) EditorApplication.Exit(exit);
    }
}
