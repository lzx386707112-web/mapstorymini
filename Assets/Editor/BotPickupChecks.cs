using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Server.Bot;
using Server.Field;
using UnityEditor;
using UnityEngine;

public static class BotPickupChecks
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int count;
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); count++; }
    static void Session(string key,object value) => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null,new[] {value});
    static object Field(object target,string key) => target.GetType().GetField(key,Flags).GetValue(target);
    static void Set(object target,string key,object value) => target.GetType().GetField(key,Flags).SetValue(target,value);
    public static void Run()
    {
        string path=Path.Combine(Path.GetTempPath(),"bot-pickup-"+Guid.NewGuid()+".json");
        var oldData=Profile.Data;
        bool oldSaving=(bool)typeof(Profile).GetField("debugOverride",Flags).GetValue(null);
        GameObject game=null,body=null;
        try
        {
            typeof(Profile).GetField("data",Flags).SetValue(null,new PlayerProfileData());
            typeof(Profile).GetField("debugOverride",Flags).SetValue(null,true);
            var db=DbStore.Create(path); var server=GameServer.StartWith(db);
            db.Table("accounts").Insert(new DbRow {["name"]="BotPickupCheck",["nxCredit"]=0L});
            var record=new CharRecord {accountid=1,name="Collector",level=120,hp=500,maxhp=500,
                face=20000,hair=30000,equipslots=96,useslots=96,setupslots=96,etcslots=96};
            record.id=CharactersDao.Insert(db,record);
            Session("Server",server);Session("Net",server.Transport);Session("CharacterId",(int)record.id);
            Session("Character",CharService.Load(db,(int)record.id));Session("InGame",true);
            GameSession.ApplyInventoryToProfile();
            game=new GameObject("BotPickupFixture");
            var loop=game.AddComponent<GameLoop>();loop.enabled=false;GameLoop.I=loop;
            loop.state=GameLoop.GameState.Playing;
            loop.field=game.AddComponent<FieldDirector>();loop.field.enabled=false;
            typeof(FieldDirector).GetField("<Map>k__BackingField",Flags).SetValue(loop.field,new FieldMap {id=100000000});
            var runtime=server.Bots;
            var actor=runtime.SynchronizePlayer(GameSession.Character,100000000,0,0);
            runtime.parties.Create(actor);
            var bot=new BotCharacter {id=987650,name="CollectorBot",mapId=actor.mapId,world=actor.world,channel=actor.channel};
            BotRegistry.Add(bot);runtime.parties.Join(bot,runtime.parties.For(actor.id).id);
            ((Dictionary<int,int>)Field(runtime.partyGrind,"leaders"))[bot.id]=actor.id;
            var bridge=game.AddComponent<GroundItemBridge>();bridge.Bind(loop.field,runtime);
            var botField=game.AddComponent<BotField>();botField.enabled=false;
            body=new GameObject("CollectorBotBody");
            var animation=body.AddComponent<SpriteAnim>();
            var viewType=typeof(BotField).GetNestedType("View",BindingFlags.NonPublic);
            var view=Activator.CreateInstance(viewType,true);
            Set(view,"root",body);Set(view,"animation",animation);
            ((IDictionary)Field(botField,"views"))[bot.id]=view;
            var pickup=new BotGroundPickup();pickup.Bind(runtime);
            var local=GroundItem.Spawn(2000000,3,Vector3.zero,alreadyOnGround:true);
            if(!GroundItem.All.Contains(local)) GroundItem.All.Add(local);
            NativeGainNotice.Clear();
            Check(pickup.PickAny(bot,120,2)==1,"Companion did not pick local drop");
            Check(Profile.ItemCount(2000000)==3 && InventoryDao.CountOf(db,actor.id,2000000)==3 && bot.inventory.Count==0,
                "Local drop must persist to player, not bot");
            Check((bool)Field(local,"picking") && local.gameObject.activeSelf,"Local drop must retain pickup animation");
            Check(((ICollection)typeof(NativeGainNotice).GetField("lines",Flags).GetValue(null)).Count==1,"Player gain notice missing");
            Check(pickup.PickAny(bot,120,2)==0,"Animated drop picked twice");
            body.transform.position=new Vector3(2,1,0);
            var target=(Func<Vector2?>)Field(local,"pickupTarget");
            Check(target().Value==(Vector2)body.transform.position,"Animation must track companion, not player");
            Set(local,"removedAt",Time.realtimeSinceStartupAsDouble-.35);
            typeof(GroundItem).GetMethod("RenderAt",Flags).Invoke(local,new object[] {0});
            Check(local.transform.position.x>0 && local.transform.position.x<2,"Pickup arc must move toward companion");
            UnityEngine.Object.DestroyImmediate(local.gameObject);
            long gold=Profile.gold;
            var coin=GroundItem.Spawn(0,103,Vector3.zero,alreadyOnGround:true);
            if(!GroundItem.All.Contains(coin)) GroundItem.All.Add(coin);
            Check(pickup.PickAny(bot,120,2)==1 && Profile.gold==gold+103 && bot.meso==0,"All local mesos belong to player");
            UnityEngine.Object.DestroyImmediate(coin.gameObject);
            var item=new MapItem {objectId=987651,mapId=actor.mapId,meso=103,ownerCharacterId=actor.id,
                dropTime=(long)(Time.realtimeSinceStartupAsDouble*1000)-1000};
            var bound=GroundItem.Spawn(0,103,Vector3.zero,item,alreadyOnGround:true);
            if(!GroundItem.All.Contains(bound)) GroundItem.All.Add(bound);
            gold=Profile.gold;
            Check(pickup.PickAny(bot,120,2)==1 && item.pickedUp && Profile.gold==gold+103 && bot.meso==0,
                "All authoritative mesos belong to player without party rounding");
            Check((bool)Field(bound,"picking") && ((Func<Vector2?>)Field(bound,"pickupTarget"))().Value==(Vector2)body.transform.position,
                "Authoritative drop must animate toward companion");
            UnityEngine.Object.DestroyImmediate(bound.gameObject);
            var nx=GroundItem.Spawn(4031865,2,Vector3.zero,alreadyOnGround:true);
            if(!GroundItem.All.Contains(nx)) GroundItem.All.Add(nx);
            Check(pickup.PickAny(bot,120,2)==1 && DbStore.Int(AccountsDao.FindById(db,1),"nxCredit")==200 && Profile.ItemCount(4031865)==0,
                "Companion NX pickup must credit player account");
            UnityEngine.Object.DestroyImmediate(nx.gameObject);
            var coupon=GroundItem.Spawn(4001007,2,Vector3.zero,alreadyOnGround:true);
            if(!GroundItem.All.Contains(coupon)) GroundItem.All.Add(coupon);
            int before=Profile.ItemCount(4001007);
            Check(pickup.PickNearby(bot,4001007,120,1)==1 && bot.inventory.Exists(i=>i.itemId==4001007 && i.quantity==1) &&
                Profile.ItemCount(4001007)==before && coupon.amount==1,"PQ coupons must remain with bot and preserve exact count");
            bool partial=false;
            foreach(var visible in Resources.FindObjectsOfTypeAll<GroundItem>())
                partial |= visible.itemId==4001007 && (bool)Field(visible,"picking");
            Check(partial,"Partial PQ pickup lacks animation");
            foreach(var visible in Resources.FindObjectsOfTypeAll<GroundItem>()) UnityEngine.Object.DestroyImmediate(visible.gameObject);
            var reward=new MapItem {objectId=987652,mapId=actor.mapId,ownerCharacterId=actor.id,
                item=new InventoryEntry {itemId=2000000,quantity=2,inventoryType=2},
                dropTime=(long)(Time.realtimeSinceStartupAsDouble*1000)-1000};
            var rewardView=GroundItem.Spawn(2000000,2,Vector3.zero,reward,alreadyOnGround:true);
            if(!GroundItem.All.Contains(rewardView)) GroundItem.All.Add(rewardView);
            Check(pickup.PickAny(bot,120,2)==1 && Profile.ItemCount(2000000)==5 && InventoryDao.CountOf(db,actor.id,2000000)==5,
                "Authoritative item must stack in player inventory and persist");
            UnityEngine.Object.DestroyImmediate(rewardView.gameObject);
            // 玩家背包满时两套掉落都保留，既不播放成功动画也不发获得提示。
            actor.character.record.useslots=1;
            db.Table("characters").Update(DbStore.Eq("id",(long)actor.id),new DbRow {["useslots"]=1L});
            NativeGainNotice.Clear();
            var full=GroundItem.Spawn(2000001,1,Vector3.zero,alreadyOnGround:true);
            if(!GroundItem.All.Contains(full)) GroundItem.All.Add(full);
            Check(pickup.PickAny(bot,120,2)==0 && !(bool)Field(full,"removing") && full.amount==1,
                "Full player inventory must preserve local drop");
            UnityEngine.Object.DestroyImmediate(full.gameObject);
            var refused=new MapItem {objectId=987653,mapId=actor.mapId,ownerCharacterId=actor.id,
                item=new InventoryEntry {itemId=2000001,quantity=1,inventoryType=2},
                dropTime=(long)(Time.realtimeSinceStartupAsDouble*1000)-1000};
            var refusedView=GroundItem.Spawn(2000001,1,Vector3.zero,refused,alreadyOnGround:true);
            if(!GroundItem.All.Contains(refusedView)) GroundItem.All.Add(refusedView);
            Check(pickup.PickAny(bot,120,2)==0 && !refused.pickedUp && !(bool)Field(refusedView,"removing"),
                "Full player inventory must preserve authoritative drop");
            Check(((ICollection)typeof(NativeGainNotice).GetField("lines",Flags).GetValue(null)).Count==0,
                "Failed pickup must not emit gain notice");
            Debug.Log("[BotPickupChecks] PASS "+count);
            EditorApplication.Exit(0);
        }
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
        finally
        {
            foreach(var item in Resources.FindObjectsOfTypeAll<GroundItem>()) if(item!=null) UnityEngine.Object.DestroyImmediate(item.gameObject);
            if(game!=null) UnityEngine.Object.DestroyImmediate(game);
            if(body!=null) UnityEngine.Object.DestroyImmediate(body);
            Session("InGame",false);GameServer.Stop();GameLoop.I=null;BotRegistry.Remove(987650);
            typeof(Profile).GetField("data",Flags).SetValue(null,oldData);
            typeof(Profile).GetField("debugOverride",Flags).SetValue(null,oldSaving);
            if(File.Exists(path)) File.Delete(path);
        }
    }
}
