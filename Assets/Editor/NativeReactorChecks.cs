using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Server.Bot;
using Server.Field;
using UnityEditor;
using UnityEngine;

// Real IMG -> player swing -> original JS -> authoritative drops -> pickup -> quest.
public static class NativeReactorChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool ok,string label) { if (!ok) throw new Exception(label); }
    static object Call(object obj,string method,params object[] args)
        => obj.GetType().GetMethod(method,Private).Invoke(obj,args);
    static void Session(string name,object value)
        => typeof(GameSession).GetProperty(name).GetSetMethod(true).Invoke(null,new[] { value });

    public static void Run()
    {
        string path=Path.GetFullPath("Library/reactor-check-"+Guid.NewGuid()+".json");
        var host=new GameObject("ReactorCheck");host.SetActive(false);
        GameObject poolObject=null;
        try
        {
            typeof(Profile).GetField("data",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,new PlayerProfileData());
            typeof(Profile).GetField("debugOverride",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,true);
            var db=DbStore.Create(path);
            var record=new CharRecord { accountid=1,name="ReactorCheck",level=1,face=20000,hair=30000,
                equipslots=24,useslots=24,setupslots=24,etcslots=24 };
            int id=(int)CharactersDao.Insert(db,record);
            long weapon=InventoryDao.InsertItem(db,id,1302000,InventoryDao.Equipped,-11,1);
            InventoryDao.InsertEquipment(db,weapon);
            var character=CharService.Load(db,id);
            var server=GameServer.StartWith(db);
            Session("Server",server);Session("Net",server.Transport);Session("Character",character);
            Session("CharacterId",id);Session("InGame",true);
            GameSession.ApplyInventoryToProfile();
            var runtime=server.Bots;
            var map=runtime.GetMap(1000000);
            Require(map.reactors.Count==5,"Maple Island must have five original boxes");
            var actor=runtime.SynchronizePlayer(character,map.mapId,560,274);
            var game=host.AddComponent<GameLoop>();GameLoop.I=game;game.state=GameLoop.GameState.Playing;
            var field=host.AddComponent<FieldDirector>();game.field=field;
            typeof(FieldDirector).GetProperty("Map").GetSetMethod(true).Invoke(field,new object[] { new FieldMap { id=map.mapId } });
            var playerObject=new GameObject("ReactorPlayer");playerObject.transform.SetParent(host.transform);
            var player=playerObject.AddComponent<PlayerCtrl>();Call(player,"Awake");game.player=player;
            PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-100, -274/Art.PPU,100,-274/Art.PPU) });
            player.PlaceAt(560/Art.PPU,-274/Art.PPU);player.facing=1;
            var visual=(SpriteAnim)typeof(PlayerCtrl).GetField("vis",Private).GetValue(player);
            poolObject=new GameObject("ReactorPool");
            var pool=poolObject.AddComponent<NativeReactorView>();pool.Bind(map.mapId);
            long now=1000;
            MapReactorService.Clock=()=>now;
            MapItemService.Clock=()=>now;
            NativeReactorDrops.Random=new System.Random(42);
            NativeReactorDrops.Rates=_=>(1,1);
            NativeReactorDrops.NeedsQuestItem=(_,quest,item)=>QuestLogic.NeedQuestItem(quest,item);
            Exception scriptError=null;
            MapReactorService.ScriptSource=ScriptSource.LoadKey;
            MapReactorService.ScriptError=error=>scriptError=error;
            var bridgeObject=new GameObject("ReactorBridge");bridgeObject.transform.SetParent(host.transform);
            var bridge=bridgeObject.AddComponent<GroundItemBridge>();bridge.Bind(field,runtime);
            pool.Tick(now);
            Require(poolObject.GetComponentsInChildren<SpriteRenderer>().Length==5,"Five original box sprites missing");
            foreach (var renderer in poolObject.GetComponentsInChildren<SpriteRenderer>())
                Require(renderer.sprite!=null && renderer.sprite.rect.width==64 && renderer.sprite.rect.height==45,
                    "Box did not decode the original 64x45 IMG Canvas");
            foreach (int state in new[] { 1,2 })
            {
                var clip=Call(pool,"Load",2001,state,true);
                Require(clip!=null && (int)clip.GetType().GetField("duration").GetValue(clip)==300,
                    "Intermediate hit UOL did not resolve original two 150ms frames");
            }
            var box=MapReactorService.Get(map.mapId)[0];
            Require(!QuestLogic.NeedQuestItem(1008,4031161),"Quest drops visible before accepting Pio quest");
            Profile.Data.SetQuestStarted(1008);
            Require(QuestLogic.NeedQuestItem(1008,4031161),"Started quest must need original board");
            var attack=AfterimageFx.AttackBounds(WzCharacter.Afterimage(visual.key),WzCharacter.AttackPoseName(visual.key));
            Require(attack.HasValue,"Equipped weapon's original attack rectangle missing");
            var prone=AfterimageFx.AttackBounds("swordOL","proneStab");
            Require(prone.HasValue && prone.Value==Rect.MinMaxRect(-74,-11,-15,1),
                "Crouched attack must read its own original IMG bounds");
            Require(NativeReactorView.FindTarget(map.mapId,NativeTargetGeometry.World(attack.Value,player.FeetPos,false))==null,
                "Facing away hit the box");
            player.PlaceAt(0,-274/Art.PPU);
            Require(!(bool)Call(player,"StrikeReactor"),"Distant box hit by invented radius");
            player.PlaceAt(560/Art.PPU,-274/Art.PPU);
            for (int hit=1;hit<=4;hit++)
            {
                Require((bool)Call(player,"StrikeReactor"),"Real basic attack did not select box "+hit);
                Require(box.state==hit-1,"Box state advanced before windup settlement");
                Call(player,"TickPendingHit",10f);pool.Tick(now);
                Require(box.state==hit,"Box failed original state transition "+hit);
                if (hit<4) Require(MapItemService.DroppedItems(map.mapId).Count==0,"Box dropped items before fourth hit");
                now+=1000;
            }
            Require(!box.alive && scriptError==null,"Box script failed: "+scriptError);
            Require(!MapReactorService.HitPlayer(map.mapId,box.objectId,id),"Broken box accepted duplicate attack");
            long destroyedAt=box.lastHitAt;
            NativeReactorDrops.Tick(destroyedAt);
            Require(MapItemService.DroppedItems(map.mapId).Count==1,"Drop spray did not start at zero delay");
            NativeReactorDrops.Tick(destroyedAt+199);
            Require(MapItemService.DroppedItems(map.mapId).Count==1,"Drop spray ran before 200ms interval");
            NativeReactorDrops.Tick(destroyedAt+200);
            Require(MapItemService.DroppedItems(map.mapId).Count==2,"Drop spray missed 200ms interval");
            MapReactorService.Tick(now+5000);
            var drops=MapItemService.DroppedItems(map.mapId);
            var board=drops.Find(d=>!d.IsMeso && d.ItemId==4031161);
            var screw=drops.Find(d=>!d.IsMeso && d.ItemId==4031162);
            Require(board!=null && screw!=null && board.questId==1008 && screw.questId==1008,
                "Original script/SQL did not generate both quest-tagged items");
            Require(board.item.inventoryType==4 && board.Quantity==1,"Task item not a real ETC inventory entry");
            foreach (var item in new[] { board,screw })
            {
                item.dropTime=(long)(Time.realtimeSinceStartup*1000)-1000;
                Require((bool)Call(bridge,"TryPickup",item),"Real pickup bridge rejected task item");
                Require(!(bool)Call(bridge,"TryPickup",item),"Duplicate pickup granted another task item");
            }
            Require(Profile.ItemCount(4031161)==1 && Profile.ItemCount(4031162)==1,"Pickup did not synchronize Profile mirror");
            var saved=CharService.Load(db,id);
            Require(saved.inventory.Exists(i=>i.itemId==4031161 && i.quantity==1) &&
                saved.inventory.Exists(i=>i.itemId==4031162 && i.quantity==1),"Pickup did not persist both quest items");
            Require(!QuestLogic.NeedQuestItem(1008,4031161),"Quest item cap failed after collection");
            var surplus=MapItemService.DropFromReactor(map.mapId,box.objectId,id,
                BotLogic.GenerateCleanItem(4031161),box.x,box.y,box.x,box.y,1008);
            surplus.dropTime=(long)(Time.realtimeSinceStartup*1000)-1000;
            Require(!(bool)Call(bridge,"TryPickup",surplus) && !surplus.pickedUp,
                "Surplus task item bypassed quest cap in authoritative pickup");
            var quest=QuestCatalog.Get(1008);
            Require(QuestLogic.CanComplete(quest,10000),"Pio quest cannot be handed in after real pickup");
            Require(QuestLogic.Complete(quest,10000,true) && Profile.QuestStatus(1008)==2,
                "Original Quest.wz hand-in actions did not finish Pio quest");
            Require(!QuestLogic.NeedQuestItem(1008,4031161),"Completed quest still accepts boards");
            now=destroyedAt+90000-1;MapReactorService.Tick(now);
            Require(!box.alive,"Box respawned before original 90 second boundary");
            now++;MapReactorService.Tick(now);pool.Tick(now);
            Require(box.alive && box.state==0,"Box did not reset at original 90 second boundary");
            Debug.Log("[NativeReactorChecks] PASS: five IMG boxes; original weapon attack bounds; wrong facing/distance rejected; real player windup/four hits; original JS and SQL drops; duplicate guards; pickup persistence and Profile sync; Pio Quest.wz hand-in; 90s respawn");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error);EditorApplication.Exit(1); }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);GameLoop.I=null;GameServer.Stop();
            if (poolObject!=null) UnityEngine.Object.DestroyImmediate(poolObject);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
