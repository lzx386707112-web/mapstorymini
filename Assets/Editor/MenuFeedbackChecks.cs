using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Real host, fresh character per route; no Mock APIs and no live player save.
public static class MenuFeedbackChecks
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    [Serializable] public sealed class Row { public string script, route, error, text; public int map; }
    [Serializable] public sealed class Report
    {
        public string scope="Bounded NPC dialogue routes; does not certify combat, portals, rewards, or all catalogue purchases.";
        public int pendingRoutes, deferredCatalogues;
        public List<Row> rows = new List<Row>();
    }
    sealed class Route { public string script; public int npc, map; public List<int> replies = new List<int>(); }
    static void Set(string key, object value) => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null, new[] {value});
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Feedback regression: " + message);
    }
    static void CheckEquipmentAndCash(DbStore db, int account)
    {
        int cid = GameSession.CharacterId;
        Require(MapleDash.Js.JsConvert.ToInt32(GameSession.Net.Script(new ScriptRequest {
            characterId=cid,operation=ScriptOperation.GetMaxClassLevel }).value)==255,"class level default");
        var itemEngine=new MapleDash.Js.JsEngine("function check(){var Item=Java.type('org.gms.client.inventory.Item');var a=new Item(4001000,0,3);a.setOwner('tower');a.setExpiration(123456);var b=a.copy();b.setQuantity(2);return a.getQuantity()===3 && b.getQuantity()===2 && b.getItemId()===4001000 && b.getOwner()==='tower' && b.getExpiration()===123456 && b.getPetId()===-1;}","detached-item");
        itemEngine.Interp.ResolveHostType=GameSession.Server.Scripts.ResolveHostType;
        itemEngine.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(itemEngine.Call("check")),"detached event item state and independent copy");
        var statEngine=new MapleDash.Js.JsEngine("function check(){var s=Java.type('org.gms.client.Stat');return s.PET.getValue()===1572872 && s.getByValue(64)===s.STR && s.getByString('INT')===s.INT && s.getBy5ByteEncoding(512)===s.LUK && s.getBy5ByteEncoding(1024)===null && s.getByString('invalid')===null && s.values().length===20 && s.LEVEL.ordinal()===3;}","stat-enum");
        statEngine.Interp.ResolveHostType=GameSession.Server.Scripts.ResolveHostType;statEngine.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(statEngine.Call("check")),"original stat enum masks and lookup identity");
        long eventNow=1000;int clearAwards=0;
        var events=new NativeEventScriptService(key=>"function monsterKilled(m,e,k){e.setProperty('lastKiller',String(k));} function allMonstersDead(e,k){e.setProperty('empty',String(k));} function delayed(e){e.setProperty('delayed','yes');} function setup(d,l){var e=em.newInstance('test'+l);e.setIntProperty('difficulty',d);e.setIntProperty('lobby',l);return e;} function playerEntry(e,p){e.setIntProperty('entered',p.getId());} function playerExit(e,p){e.setProperty('exited','yes');}",()=>eventNow);
        events.AwardClearQuestPoints=player=>clearAwards++;
        var manager=events.GetManager("feedback");
        var eventEngine=new MapleDash.Js.JsEngine("function check(){if(!em.startInstance(-1,p,p,4))return false;var e=p.getEventInstance();return true;}","event-overload");
        // Use the service's own membership in this isolated test, not the scene singleton.
        var eventPlayer=new MapleDash.Js.JsHostObject(null);
        eventPlayer.Set("getId",MapleDash.Js.JsFunction.FromNative("getId",call=>new MapleDash.Js.JsNumber(cid)));
        eventPlayer.Set("getEventInstance",MapleDash.Js.JsFunction.FromNative("getEventInstance",call=>events.GetInstance(cid).Host));
        eventEngine.Bind("em",manager.Host);eventEngine.Bind("p",eventPlayer);eventEngine.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(eventEngine.Call("check")),"event character/difficulty overload");
        var instance=events.GetInstance(cid);
        var verify=new MapleDash.Js.JsEngine("function check(){if(e.getIntProperty('difficulty')!==4 || e.getIntProperty('lobby')!==0 || e.getIntProperty('entered')!==p.getId())return false;if(em.startInstance(0,p))return false;e.exitPlayer(p);return e.getProperty('exited')==='yes' && e.getPlayerCount()===0;}","event-lifecycle");
        verify.Bind("e",instance.Host);verify.Bind("p",eventPlayer);verify.Bind("em",manager.Host);verify.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(verify.Call("check")) && events.GetInstance(cid)==null,"occupied lobby and event exit callback");
        var cleared=manager.Start(eventPlayer,1);
        var a=new MapleDash.Js.JsHostObject(null);var b=new MapleDash.Js.JsHostObject(null);
        cleared.RegisterMonster(a);cleared.RegisterMonster(b);
        cleared.MonsterKilled(a,true);
        Require(cleared.Host.Get("getProperty") is MapleDash.Js.JsFunction,"event property host");
        var lifecycle=new MapleDash.Js.JsEngine("function before(){return e.getProperty('empty')===null && e.isEventTeamLackingNow(true,1,p) && !e.isEventCleared();} function after(){return e.getProperty('empty')==='false' && e.getProperty('lastKiller')==='false';} function clear(){e.schedule('delayed',2000);e.setEventCleared();return e.isEventCleared() && !e.isDisposed() && e.isEventTeamLackingNow(true,1,p) && !e.isEventTeamLackingNow(false,1,p);} function delayed(){return e.getProperty('delayed')==='yes';}","event-clear-and-monsters");
        lifecycle.Bind("e",cleared.Host);lifecycle.Bind("p",eventPlayer);lifecycle.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(lifecycle.Call("before")),"registered monsters do not clear early");
        cleared.MonsterKilled(b,false);
        Require(MapleDash.Js.JsConvert.ToBoolean(lifecycle.Call("after")),"last monster callback carries no-killer flag");
        Require(MapleDash.Js.JsConvert.ToBoolean(lifecycle.Call("clear")) && clearAwards==1,"clear awards points without cancelling instance callbacks");
        eventNow=3000;events.Tick();
        Require(MapleDash.Js.JsConvert.ToBoolean(lifecycle.Call("delayed")),"cleared voyage can still run its delayed finish");
        eventNow=11000;events.Tick();
        Require(manager.Start(eventPlayer,1)!=null,"cleared lobby released after configured delay");
        events.Clear();
        var mapEvents=new NativeEventScriptService(key=>"function init(){}",()=>1000);
        var mapManager=mapEvents.GetManager("map-isolation");
        var mapInstanceA=mapManager.NewInstance("a");var mapInstanceB=mapManager.NewInstance("b");
        var mapA=new NativeEventMap(mapInstanceA,953030000);var mapB=new NativeEventMap(mapInstanceB,953030000);
        try
        {
            mapA.Reset();mapB.Reset();
            Require(mapA.Scope!=mapB.Scope && mapA.Id==mapB.Id && mapA.Count>0 && mapA.Count==mapB.Count,"private park maps have separate population with original template id");
            Require(!ReferenceEquals(mapA.Geometry,mapB.Geometry) && !ReferenceEquals(mapA.Geometry.portals[0],mapB.Geometry.portals[0]),"private map portal mutable state isolation");
            var privateEngine=new MapleDash.Js.JsEngine("function check(){var n=b.countMonsters();a.killAllMonsters();return a.countMonsters()===0 && b.countMonsters()===n;}","private-map-mobs");
            privateEngine.Bind("a",mapA.Host);privateEngine.Bind("b",mapB.Host);privateEngine.Run();
            Require(MapleDash.Js.JsConvert.ToBoolean(privateEngine.Call("check")),"clearing one park instance preserves the other");
            var privateReactor=Server.Field.MapReactorService.Spawn(mapA.Scope,2002000,0,0);
            Require(Server.Field.MapReactorService.Get(mapB.Scope).TrueForAll(r=>r.id!=2002000),"private reactor pools isolated");
            Server.Field.MapReactorService.ForceHit(mapA.Scope,privateReactor.objectId);
            mapA.Reset();
            Require(ReferenceEquals(Server.Field.MapReactorService.Find(mapA.Scope,privateReactor.objectId),privateReactor) &&
                privateReactor.alive && privateReactor.state==0,"PQ reset preserves reactor identity and resets state");
            Server.Field.MapItemService.SpawnItemDrop(mapA.Scope,0,cid,-1,new InventoryEntry {itemId=4000000,quantity=1},0,0,false,false);
            Require(Server.Field.MapItemService.ItemsInRect(mapB.Scope,-1000,-1000,2000,2000).Count==0,"private map drops isolated");
        }
        finally {mapA.Dispose();mapB.Dispose();mapEvents.Clear();}
        Require(!Server.Field.MapService.Has(mapA.Scope) && !Server.Field.MapService.Has(mapB.Scope),"private geometry released on disposal");
        CheckEventRewardsAndFame(db,cid);
        var name=NativeNameTag.Prepare("UI/NameTag.img/3",31);
        Require(name!=null && name.width==46 && name.height==18 && name.textX==7 && name.textY==2,"original name tag three-piece layout");
        var medal=NativeNameTag.Prepare("UI/NameTag.img/medal/9",31);
        Require(medal!=null && medal.width==51 && medal.height==17 && medal.textX==10 && medal.textY==1,"original medal layout");
        var bubble=NativeChatBalloon.Prepare(174,31,1);
        Require(bubble!=null && bubble.texture.width==97 && bubble.texture.height==81 && bubble.anchorX==50,"asymmetric original chat ring layout: "+(bubble==null?"missing":bubble.texture.width+"x"+bubble.texture.height+" anchor="+bubble.anchorX));
        string fixtures=Path.Combine(Application.dataPath,"../docs/evidence/menu-feedback-2026-11-01");
        File.WriteAllBytes(Path.Combine(fixtures,"name-tag-3.png"),name.texture.EncodeToPNG());
        File.WriteAllBytes(Path.Combine(fixtures,"medal-9.png"),medal.texture.EncodeToPNG());
        File.WriteAllBytes(Path.Combine(fixtures,"chat-ring-174.png"),bubble.texture.EncodeToPNG());
        var engine = new MapleDash.Js.JsEngine("function check() { var c=Java.type('org.gms.constants.inventory.ItemConstants'); return c.isNewHair(30300) && !c.isNewHair(30308) && c.isAccessory(1112100) && !c.isAccessory(1302000); }", "feedback");
        engine.Bind("Java",NpcApi.BuildJava());engine.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(engine.Call("check")),"original hair catalog and accessory classification");
        var limits=new MapleDash.Js.JsEngine("function check(){var f=Java.type('org.gms.server.maps.FieldLimit');return f.DROP_LIMIT.getValue()===4194304 && f.DROP_LIMIT.check(4194305) && !f.DROP_LIMIT.check(1) && f.JUMP.check(4194305) && !f.CANNOTMIGRATE.check(4194305);}","field-limits");
        limits.Bind("Java",NpcApi.BuildJava());limits.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(limits.Call("check")),"original independent field restriction masks");
        FieldCatalog.Ensure();
        db.Table("drop_data").Insert(new DbRow { ["dropperid"]=9999999L,["itemid"]=4001000L,
            ["chance"]=321L,["minimum_quantity"]=2L,["maximum_quantity"]=3L,["questid"]=123L });
        DropStore.Invalidate();
        var drops=new MapleDash.Js.JsEngine("function check(){var a=Java.type('org.gms.server.life.MonsterInformationProvider').getInstance().retrieveDrop(9999999);return a.size()===1 && a.get(0).itemId===4001000 && a.get(0).Minimum===2 && a.get(0).Maximum===3 && a.get(0).questid===123 && a.filter(function(x){return x.chance===321;}).length===1 && a.iterator().hasNext();}","drop-query");
        drops.Bind("Java",NpcApi.BuildJava());drops.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(drops.Call("check")),"monster drops query authoritative database fields");
        db.Table("drop_data").Delete(x=>DbStore.Int(x,"dropperid")==9999999);DropStore.Invalidate();
        Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=4010000,quantity=10}).ok,"extra storage ore grant");
        InventoryChangeResponse Extra(ExtraStorageOperation action,int quantity=0,int category=1)
            =>GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=4010000,quantity=quantity,
                extraStorageType=category,extraStorage=action});
        Require(Extra(ExtraStorageOperation.Store,7).ok && Extra(ExtraStorageOperation.Read).extraStorageQuantity==7 &&
            InventoryDao.CountOf(db,cid,4010000)==3 && Extra(ExtraStorageOperation.Read,0,2).extraStorageQuantity==0,"extra storage deposit/category isolation");
        Require(Extra(ExtraStorageOperation.Withdraw,3).ok && InventoryDao.CountOf(db,cid,4010000)==6 &&
            Extra(ExtraStorageOperation.Read).extraStorageQuantity==4,"extra storage withdrawal");
        Require(!Extra(ExtraStorageOperation.Withdraw,5).ok && Extra(ExtraStorageOperation.Read).extraStorageQuantity==4,"extra storage insufficient quantity is unchanged");
        Require(Extra(ExtraStorageOperation.Store,6).ok,"extra storage fill fixture");
        var capacity=CharactersDao.FindById(db,cid);capacity.etcslots=1;CharactersDao.Save(db,capacity);
        Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=4000000,quantity=1}).ok,"fill etc bag");
        Require(!Extra(ExtraStorageOperation.Withdraw,1).ok && Extra(ExtraStorageOperation.Read).extraStorageQuantity==10 &&
            InventoryDao.CountOf(db,cid,4010000)==0,"full bag preserves storage and inventory");
        capacity=CharactersDao.FindById(db,cid);capacity.etcslots=96;CharactersDao.Save(db,capacity);
        foreach (var pair in new[] { (1302000, -11), (1702000, -111), (1902000, -18), (1912000, -19), (1112100, -112),
            (1112101,-113),(1112200,-115),(1112201,-116) })
        {
            var add = GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=pair.Item1,quantity=1});
            Require(add.ok, "grant " + pair.Item1 + ":" + add.error);
            var equip = GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=pair.Item1,quantity=1,equip=true,equipSlot=pair.Item2});
            Require(equip.ok, "equip " + pair.Item1 + ":" + equip.error);
            GameSession.Character.inventory = equip.inventory;
            Require(equip.inventory.Exists(x=>x.itemId==pair.Item1 && x.position==pair.Item2 && x.inventoryType==InventoryDao.Equipped), "authoritative slot " + pair.Item1);
        }
        GameSession.ApplyInventoryToProfile();
        Require(NativeNameTag.RingStyle("nameTag")==3 && NativeNameTag.RingStyle("chatBalloon")==3,"native ring order preserves independent nonzero name/chat styles");
        var ids = WzCharacter.CharacterIds(GameSession.Character.record,GameSession.Character.inventory);
        Require(Array.IndexOf(ids,"01702000")>=0 && ids[ids.Length-1]=="01302000", "cash cover and real weapon coexist");
        Require(WzCharacter.Appearance("feedback-cash-weapon",ids)!=null,"cash weapon composition");
        var pet = GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=5000000,createPet=true});
        Require(pet.ok && pet.createdPetId>0,"pet identity");
        long expiration=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+90L*24*60*60*1000;
        var grant = GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=5000000,quantity=1,petId=pet.createdPetId,itemExpiration=expiration});
        Require(grant.ok && grant.inventory.Exists(x=>x.itemId==5000000 && x.petId==pet.createdPetId && x.expiration==expiration),"pet grant retains identity and 90-day expiration");
        var record=CharactersDao.FindById(db,cid);record.equipslots=24;CharactersDao.Save(db,record);
        var cash=GameSession.Server.Cash;cash.Catalog=new NativeCashCatalog();
        Require(cash.Handle(new CashRequest {characterId=cid,operation=CashOperation.Open}).ok,"open cash shop");
        int balance=DbStore.Int(AccountsDao.FindById(db,account),"nxCredit");
        var expand=cash.Handle(new CashRequest {characterId=cid,operation=CashOperation.ExpandInventory,inventoryType=1});
        Require(expand.ok && expand.character.record.equipslots==28 && expand.credit==balance-2000,"expansion +4 for 2000 credit");
        var storageExpansion=cash.Handle(new CashRequest {characterId=cid,operation=CashOperation.ExpandStorage});
        Require(storageExpansion.ok && storageExpansion.storageSlots==8 && storageExpansion.credit==balance-4000,"storage expansion shares source 2000 price and adds four slots");
        var storageRow=db.Table("storages").Find(x=>DbStore.Int(x,"accountid")==account && DbStore.Int(x,"world")==GameSession.Character.record.world);
        Require(storageRow!=null && DbStore.Int(storageRow,"slots")==8,"storage expansion persists account/world capacity");
        storageRow["slots"]=48L;
        var storageFull=cash.Handle(new CashRequest {characterId=cid,operation=CashOperation.ExpandStorage});
        Require(!storageFull.ok && storageFull.storageSlots==48 && storageFull.credit==balance-4000,"full storage does not charge");
        AccountsDao.FindById(db,account)["nxCredit"]=0L;
        var failed=cash.Handle(new CashRequest {characterId=cid,operation=CashOperation.ExpandInventory,inventoryType=1});
        Require(!failed.ok && failed.character.record.equipslots==28 && failed.credit==0,"insufficient funds do not expand");
        storageRow=db.Table("storages").Find(x=>DbStore.Int(x,"accountid")==account && DbStore.Int(x,"world")==GameSession.Character.record.world);
        storageRow["slots"]=8L;
        var storagePoor=cash.Handle(new CashRequest {characterId=cid,operation=CashOperation.ExpandStorage});
        Require(!storagePoor.ok && storagePoor.storageSlots==8 && storagePoor.credit==0,"insufficient storage funds preserve capacity");
        cash.Handle(new CashRequest {characterId=cid,operation=CashOperation.Close});
        AccountsDao.FindById(db,account)["nxCredit"]=1000000L;
        db.Table("inventoryitems").Delete(x=>DbStore.Int(x,"characterid")==cid);
        Set("Character",CharService.Load(db,cid));GameSession.ApplyToProfile();
        CheckCubes(db,cid);
        CheckDatabase(db,cid);
        CheckFashionTransfer(db,cid);
        CheckHunts(db,cid);
        var boss=GameSession.Net.Boss(new BossRequest {characterId=cid,mobId=8500002,operation=BossOperation.RecordKill,encounter="feedback-boss"});
        Require(Math.Abs(boss.bossBonusPercent-0.1)<1e-9 && Math.Abs(boss.multiplier-1.001)<1e-9,"boss kill growth applied to combat transport");
        Require(GameSession.Net.ScriptValue(new ScriptValueRequest {characterId=cid,name="boss_growth_kill_papulatus"}).value=="1","boss kill is visible to original script extend key");
        GameSession.Net.Boss(new BossRequest {characterId=cid,mobId=8500002,operation=BossOperation.RecordKill,encounter="feedback-boss"});
        Require(GameSession.Net.ScriptValue(new ScriptValueRequest {characterId=cid,name="boss_growth_kill_papulatus"}).value=="1","repeated kill notification is deduplicated");
        var growthSession=NpcSession.Talk(9900001,"Boss成长系统");
        Require(growthSession.LastError==null && growthSession.Dialog.RawText.Contains("+0.1%"),"original boss growth menu reads authoritative progress");
        growthSession.Reply(1,1);
        Require(growthSession.LastError==null && growthSession.Dialog.RawText.Contains("帕普拉图斯(1次)"),"boss conquest detail agrees with combat progress");
        NpcSession.CloseActive();
        db.Table("inventoryitems").Delete(x=>DbStore.Int(x,"characterid")==cid);
        Set("Character",CharService.Load(db,cid));GameSession.ApplyToProfile();
        Debug.Log("[MenuFeedback] equipment/cash/pet regression passed");
    }
    static void CheckEventRewardsAndFame(DbStore db,int cid)
    {
        var context=(NpcSession)typeof(NpcSession).GetMethod("MapContext",Flags).Invoke(null,null);
        var player=NpcApi.BuildPlayer(context);
        var service=new NativeEventScriptService(key=>"function init(){}",()=>1000);
        var instance=service.GetManager("reward-regression").NewInstance("rewards");
        var engine=new MapleDash.Js.JsEngine("function check(){e.setEventRewards(1,[4010000],[2]);e.setExclusiveItems([4010000]);e.registerPlayer(p,false);if(!e.giveEventReward(p))return false;return p.getItemQuantity(4010000,false)===2;} function leave(){e.unregisterPlayer(p);return p.getItemQuantity(4010000,false)===0 && e.getPlayerCount()===0;} function fame(){cm.gainFame(40000);if(cm.getFame()!==30000)return false;cm.gainFame(-70000);if(cm.getFame()!==-30000)return false;cm.gainFame(30000);return cm.getFame()===0;}","event-reward-and-fame");
        engine.Bind("e",instance.Host);engine.Bind("p",player);engine.Bind("cm",NpcApi.Build(context,0,0,false));engine.Run();
        try
        {
            Require(MapleDash.Js.JsConvert.ToBoolean(engine.Call("check")) && InventoryDao.CountOf(db,cid,4010000)==2,"event item reward uses real inventory transport");
            Require(MapleDash.Js.JsConvert.ToBoolean(engine.Call("leave")) && InventoryDao.CountOf(db,cid,4010000)==0,"exclusive event items removed on unregister");
            Require(MapleDash.Js.JsConvert.ToBoolean(engine.Call("fame")) && CharactersDao.FindById(db,cid).fame==0,"real fame gain clamps and persists both limits");
        }
        finally {service.Clear();}
        var oldRequired=GameConfigDao.Get(db,"quest_point_requirement",null);
        var oldAward=GameConfigDao.Get(db,"quest_point_per_event_clear",null);
        void Config(string key,string value)
        {
            var table=db.Table("game_config");table.Delete(row=>DbStore.Str(row,"config_code")==key);
            if(value!=null)table.Insert(new DbRow {["config_code"]=key,["config_value"]=value});
        }
        try
        {
            Config("quest_point_requirement","10");Config("quest_point_per_event_clear","7");
            GameSession.Net.Script(new ScriptRequest {characterId=cid,operation=ScriptOperation.AwardEventQuestPoints});
            Require(CharactersDao.FindById(db,cid).fquest==7 && CharactersDao.FindById(db,cid).fame==0,"event clear retains partial quest fame");
            GameSession.Net.Script(new ScriptRequest {characterId=cid,operation=ScriptOperation.AwardEventQuestPoints});
            Require(CharactersDao.FindById(db,cid).fquest==4 && CharactersDao.FindById(db,cid).fame==1,"event clear converts quotient to fame and saves remainder");
        }
        finally
        {
            Config("quest_point_requirement",oldRequired);Config("quest_point_per_event_clear",oldAward);
            var character=CharactersDao.FindById(db,cid);character.fame=0;character.fquest=0;CharactersDao.Save(db,character);
        }
    }
    static void CheckHunts(DbStore db,int cid)
    {
        var hunts=GameSession.Server.Scripts.Hunts;
        Require(hunts.Gold==10000000 && hunts.Exp==5000000 && hunts.Cash==2000 && hunts.Rewards.Count==2,"original hunt JSON reward configuration");
        var clock=hunts.Clock;long now=1000000;hunts.Clock=()=>now;hunts.Choose=_=>0;hunts.Roll=()=>1;hunts.Reload();
        Require(hunts.Current().itemId==4000082 && hunts.Current().needCount==10,"hunt publishes configured collection target");
        Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=4000082,quantity=10}).ok,"hunt collection fixture");
        Set("Character",CharService.Load(db,cid));GameSession.ApplyToProfile();
        long meso=Profile.gold;int cash=DbStore.Int(AccountsDao.FindById(db,GameSession.AccountId),"nxCredit");
        var session=NpcSession.Talk(9900001,"任务/狩猎");
        Require(session.LastError==null && session.Dialog.RawText.Contains("提交任务"),"original hunt menu exposes eligible submission: "+session.LastError+" / "+session.Dialog.RawText);
        session.Reply(1,1);
        Require(session.LastError==null && session.Dialog.RawText.Contains("狩猎任务完成") && hunts.Current().completed && InventoryDao.CountOf(db,cid,4000082)==0,"original hunt submission consumes actual materials and marks shared task");
        Require(Profile.gold==meso && DbStore.Int(AccountsDao.FindById(db,GameSession.AccountId),"nxCredit")==cash,"source collection path only marks completion; do not invent displayed rewards");
        NpcSession.CloseActive();
        hunts.Choose=_=>2;hunts.Reload();
        NativeHuntRuntime.MonsterKilled(3220000);
        Require(!hunts.Current().completed && Profile.gold==meso,"non-target hunt kill cannot claim");
        NativeHuntRuntime.MonsterKilled(2220000);
        Require(hunts.Current().completed && Profile.gold==meso+10000000 && DbStore.Int(AccountsDao.FindById(db,GameSession.AccountId),"nxCredit")==cash+2000,"target hunt kill grants original money and cash");
        NativeHuntRuntime.MonsterKilled(2220000);
        Require(Profile.gold==meso+10000000 && DbStore.Int(AccountsDao.FindById(db,GameSession.AccountId),"nxCredit")==cash+2000,"hunt boss reward is claimed once");
        now+=3600000;hunts.Choose=_=>1;hunts.Tick();
        Require(!hunts.Current().completed && hunts.Current().itemId==4000179 && hunts.Current().start==now,"hunt interval publishes next independent task");
        hunts.Clock=clock;hunts.Choose=null;hunts.Roll=null;hunts.Reload();
    }
    static void CheckFashionTransfer(DbStore db,int cid)
    {
        db.Table("inventoryitems").Delete(x=>DbStore.Int(x,"characterid")==cid);
        foreach(int id in new[]{1112100,1112101,4000313})
            Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=id,quantity=id==4000313?10:1}).ok,"fashion transfer fixture");
        Set("Character",CharService.Load(db,cid));GameSession.ApplyToProfile();
        var src=GameSession.Character.inventory.Find(x=>x.itemId==1112100);
        var dst=GameSession.Character.inventory.Find(x=>x.itemId==1112101);
        Require(src.position==1 && dst.position==2,"fashion transfer original fixed slots");
        Require(GameSession.Net.UpdateEquip(new EquipPatchRequest {characterId=cid,inventoryItemId=src.itemRowId,str=37,hp=123,watk=5}).ok,"fashion source stats");
        Require(GameSession.Net.UpdateEquip(new EquipPatchRequest {characterId=cid,inventoryItemId=dst.itemRowId,str=3,hp=7,watk=1}).ok,"fashion destination stats");
        Set("Character",CharService.Load(db,cid));GameSession.ApplyToProfile();
        var frozen=new MapleDash.Js.JsEngine("function check(){var o=Object.freeze({str:37,child:{x:1}});o.str=9;o.other=1;o.child.x=2;return o.str===37 && o.other===undefined && o.child.x===2 && Object.isFrozen(o);}","frozen-transfer-snapshot");
        frozen.Run();Require(MapleDash.Js.JsConvert.ToBoolean(frozen.Call("check")),"transfer snapshot is shallow immutable");
        var copy=new MapleDash.Js.JsEngine("function check(){var M=Java.type('org.gms.client.inventory.ModifyInventory');var C=Java.type('java.util.Collections');var i=p.getInventory(1).getItem(1);var m=new M(0,i,8);i.setStr(38);if(m.getItem().getStr()!==37 || m.getOldPosition()!==8 || m.getInventoryType()!==1)return false;var l=C.unmodifiableList([m]);try{l.clear();return false;}catch(e){}return l.size()===1;}","inventory-packet-copy");
        copy.Bind("Java",NpcApi.BuildJava());copy.Bind("p",NpcApi.BuildPlayer((NpcSession)typeof(NpcSession).GetMethod("MapContext",Flags).Invoke(null,null)));copy.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(copy.Call("check")),"inventory packets copy equipment and readonly list refuses writes");
        // Persist the source fixture changed through the real item host before the script starts.
        var flush=NpcSession.Talk(9900001,"时装属性转移");
        Require(flush.LastError==null && flush.Dialog.Mode==NpcDialogMode.YesNo,"original fashion transfer rules");
        flush.Reply(1,-1);Require(flush.LastError==null && flush.Dialog.RawText.Contains("确认将以下"),"original fashion transfer eligibility");
        flush.Reply(1,-1);Require(flush.LastError==null && flush.Dialog.RawText.Contains("属性转移成功"),"original fashion transfer completes");
        var saved=CharService.Load(db,cid);
        src=saved.inventory.Find(x=>x.itemId==1112100);dst=saved.inventory.Find(x=>x.itemId==1112101);
        Require(src.str==0 && src.hp==0 && src.watk==0 && dst.str==38 && dst.hp==123 && dst.watk==5 && InventoryDao.CountOf(db,cid,4000313)==0,"fashion transfer clears source, replaces destination, consumes exactly ten, and persists");
        NpcSession.CloseActive();
    }
    static void CheckCubes(DbStore db,int cid)
    {
        var equip=new InventoryEntry {itemId=1302000,str=10,watk=20,starLevel=7,owner="7★"};
        var roll=NativeEquipmentCube.CreateRoll(equip,4007000,_=>0);
        Require(roll.grade==2 && roll.rankedUp && !roll.canKeepOld,"cube upgrade boundary and grade");
        NativeEquipmentCube.Apply(equip,roll);
        Require(equip.str==12 && equip.dex==2 && equip.watk==20 && equip.starLevel==7 && equip.owner=="7★","cube bonuses preserve unrelated upgrades");
        NativeEquipmentCube.Apply(equip,roll);
        Require(equip.str==12 && equip.dex==2,"cube replaces previous bonuses without accumulation");
        var saved=equip.Copy();
        var black=NativeEquipmentCube.CreateRoll(equip,4007007,n=>n-1);
        Require(black.grade==2 && !black.rankedUp && black.canKeepOld && NativeEquipmentCube.Raw(saved)==NativeEquipmentCube.Raw(equip),"black cube preview does not mutate old bonuses");
        var malformed=new InventoryEntry {itemId=1302000,equipmentData=new Dictionary<string,object>{{"expandAttribute4","{\"v\":2,\"cube\":4007000,\"grade\":4,\"lines\":[]}"}}};
        Require(!NativeEquipmentCube.CanRoll(malformed),"malformed cube data rejected");
        var overflow=new InventoryEntry {itemId=1302000,str=short.MaxValue};
        bool refused=false;try{NativeEquipmentCube.Apply(overflow,roll);}catch(InvalidOperationException){refused=true;}
        Require(refused && overflow.str==short.MaxValue && overflow.dex==0 && NativeEquipmentCube.Raw(overflow)=="","cube overflow is atomic");
        Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=1302000,quantity=1}).ok,"cube target grant");
        var grant=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=4007000,quantity=1});
        Require(grant.ok,"cube grant");GameSession.Character.inventory=grant.inventory;GameSession.ApplyInventoryToProfile();
        var session=NpcSession.Talk(9900001,"魔方洗练");
        Require(session.LastError==null && session.Dialog.RawText.Contains("当前词条"),"original cube script menu");
        session.Reply(1,4007000);
        Require(session.LastError==null && session.Dialog.RawText.Contains("新词条") && !session.Dialog.RawText.Contains("失败"),"original cube script roll/apply");
        NpcSession.CloseActive();
        var loaded=CharService.Load(db,cid);
        var target=loaded.inventory.Find(x=>x.itemId==1302000);
        Require(target!=null && NativeEquipmentCube.Raw(target).Contains("\"v\":2") && InventoryDao.CountOf(db,cid,4007000)==0,"cube result persists and consumes exactly one cube");
        string old=NativeEquipmentCube.Raw(target);
        grant=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=4007007,quantity=1});
        GameSession.Character.inventory=grant.inventory;GameSession.ApplyInventoryToProfile();
        session=NpcSession.Talk(9900001,"魔方洗练");session.Reply(1,4007007);
        Require(session.LastError==null && session.Dialog.RawText.Contains("保留原词条"),"black cube original selection");
        session.Reply(1,1);NpcSession.CloseActive();
        target=CharService.Load(db,cid).inventory.Find(x=>x.itemId==1302000);
        Require(NativeEquipmentCube.Raw(target)==old && InventoryDao.CountOf(db,cid,4007007)==0,"black cube keep consumes cube and retains old result");
    }
    static void CheckDatabase(DbStore db,int cid)
    {
        var equip=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=cid,itemId=1302000,quantity=1,equip=true,equipSlot=-11});
        Require(equip.ok,"SQL equipment fixture equip");
        var item=equip.inventory.Find(x=>x.position==-11 && x.itemId==1302000);
        var row=db.Table("inventoryequipment").Find(x=>DbStore.Long(x,"inventoryitemid")==item.itemRowId);
        foreach(string stat in new[]{"str","dex","int","luk","watk","matk","wdef","mdef"})row[stat]=0L;
        row["str"]=12L;row["watk"]=20L;
        Set("Character",CharService.Load(db,cid));GameSession.ApplyInventoryToProfile();
        var session=NpcSession.Talk(9900001,"战力系统");
        Require(session.LastError==null && session.Dialog.RawText.Contains("1120"),"original power menu reflects equipment: "+session.LastError+" / "+session.ScriptName+" / "+session.Dialog.RawText+" / stats="+GameSession.Character.inventory.Find(x=>x.position==-11)?.str);
        session.Reply(1,997);
        Require(session.LastError==null && session.Dialog.RawText.Contains("1120") && session.Dialog.RawText.Contains("Review0"),"original SQL aggregate ranking");
        session.Reply(1,0);
        Require(session.LastError==null && session.Dialog.RawText.Contains("1120"),"original SQL character equipment joins");NpcSession.CloseActive();
        db.Table("drop_data").Insert(new DbRow {["dropperid"]=9999999L,["itemid"]=4001000L,["chance"]=21L,["questid"]=123L});
        db.Table("drop_data").Insert(new DbRow {["dropperid"]=9999998L,["itemid"]=4001000L,["chance"]=42L,["questid"]=0L});
        var engine=new MapleDash.Js.JsEngine("function check(){var c=Java.type('org.gms.util.DatabaseConnection').getConnection();var p=c.prepareStatement('SELECT dropperid, chance, questid FROM drop_data WHERE itemid = ? ORDER BY chance DESC LIMIT 30');p.setInt(1,4001000);var r=p.executeQuery();if(!r.next() || r.getInt(1)!==9999998 || r.getLong('chance')!==42 || !r.next() || r.getInt('questid')!==123)return false;r.close();p.close();c.close();var rejected=false;try{p.executeQuery();}catch(e){rejected=true;}return rejected && c.isClosed();}","jdbc-query");
        engine.Interp.ResolveHostType=GameSession.Server.Scripts.ResolveHostType;engine.Run();
        Require(MapleDash.Js.JsConvert.ToBoolean(engine.Call("check")),"JDBC parameters/order/columns/closed resource semantics");
        db.Table("drop_data").Delete(x=>DbStore.Int(x,"dropperid")>=9999998);DropStore.Invalidate();
    }
    static bool focused;
    public static void RunFocused() { focused = true; Run(); }
    public static void Run()
    {
        var report = new Report();
        string path = Path.Combine(Path.GetTempPath(), "menu-feedback-" + Guid.NewGuid() + ".json");
        var old = Profile.Data;
        var data = typeof(Profile).GetField("data", Flags);
        var saving = typeof(Profile).GetField("debugOverride", Flags);
        object oldSaving = saving.GetValue(null);
        int oldMap = Profile.LastMapId;
        var errors = new List<string>();
        Application.LogCallback log = (text, stack, type) => { if (type == LogType.Error || type == LogType.Exception) errors.Add(text); };
        try
        {
            saving.SetValue(null, true);
            var db = DbStore.Create(path);
            int account = AccountsDao.AutoRegister(db, "menureview", "isolated");
            AccountsDao.FindById(db, account)["nxCredit"] = 1000000L;
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", account); Set("InGame", true);
            ScriptSource.ClearCache();
            var queue = new Queue<Route>();
            foreach (var seed in new[] {
                new Route { npc=9900001, map=1000000 },
                new Route { npc=9390220, map=865000001 }, new Route { npc=9390221, map=865000001 },
                new Route { npc=2540000, map=992000000 },
                new Route { npc=9071000, map=951000000, script="mPark_in00" },
                new Route { npc=9071000, map=951000000, script="mPark_in01" },
                new Route { npc=9071000, map=951000000, script="mPark_in02" },
                new Route { npc=9900001, map=910000000, script="时装暖暖" },
                new Route { npc=9900001, map=910000000, script="宠物" } })
                if (!focused || seed.script == "时装暖暖" || seed.script == "宠物") queue.Enqueue(seed);
            var seen = new HashSet<string>();
            Application.logMessageReceived += log;
            while (queue.Count > 0 && report.rows.Count < 3000)
            {
                var route = queue.Dequeue();
                data.SetValue(null, new PlayerProfileData());
                var character = new CharRecord { accountid=account, name="Review"+report.rows.Count, level=200,
                    face=20000, hair=30300, hp=500, maxhp=500, maxmp=500,
                    equipslots=96, useslots=96, setupslots=96, etcslots=96, meso=100000000 };
                character.id=CharactersDao.Insert(db,character);
                Set("CharacterId",(int)character.id); Set("Character",CharService.Load(db,(int)character.id));
                GameSession.ApplyToProfile(); Profile.LastMapId=route.map;
                if (report.rows.Count == 0) CheckEquipmentAndCash(db, account);
                errors.Clear();
                var session=NpcSession.Talk(route.npc,route.script);
                foreach (int reply in route.replies)
                {
                    var active=NpcSession.Active;
                    if(active==null || active.LastError!=null || !active.Dialog.Visible) break;
                    active.Reply(1,reply);
                }
                var current=NpcSession.Active ?? session;
                var row=new Row {script=route.script ?? route.npc.ToString(),route=string.Join(",",route.replies),
                    map=Profile.LastMapId,error=string.Join("\n",errors),text=current?.Dialog.RawText ?? ""};
                if(current?.LastError!=null && !row.error.Contains(current.LastError)) row.error+="\n"+current.LastError;
                // Original scripts sometimes catch a host exception and turn it into
                // a dialogue. It is still a failed route, even without an Error log.
                if(row.text.Contains("系统错误：") || row.text.Contains("系统错误:"))row.error+="\nScript reported an error in its dialogue";
                report.rows.Add(row);
                // Explore each visible option, not only the first. Fingerprints stop loops/catalog explosions.
                if(row.error.Length==0 && current!=null && !current.Closed && !current.Disposed && current.Dialog.Visible && route.replies.Count<3)
                {
                    string fingerprint=row.script+"|"+current.ScriptName+"|"+row.text;
                    if(seen.Add(fingerprint))
                    {
                        var options=DialogFlow.Links(current.Dialog.Content);
                        if(options.Count==0 && current.Dialog.Mode!=NpcDialogMode.Simple && current.Dialog.Mode!=NpcDialogMode.Style)
                            options.Add(current.Dialog.Mode==NpcDialogMode.GetNumber ? current.Dialog.NumberDef : -1);
                        // Every menu option; large item catalogs get separate static resource coverage.
                        if(options.Count<=40)
                            foreach(int option in options)
                            {
                                var next=new Route {npc=route.npc,map=route.map,script=route.script,replies=new List<int>(route.replies)};
                                next.replies.Add(option);queue.Enqueue(next);
                            }
                        else report.deferredCatalogues++;
                    }
                }
                NpcSession.CloseActive();
            }
            report.pendingRoutes=queue.Count;
        }
        catch(Exception e) {report.rows.Add(new Row {script="HOST",error=e.ToString()});}
        finally
        {
            Application.logMessageReceived-=log; NpcSession.CloseActive();
            foreach(var host in UnityEngine.Object.FindObjectsOfType<NativeEventScripts>()) UnityEngine.Object.DestroyImmediate(host.gameObject);
            GameServer.Stop(); Set("InGame",false);
            data.SetValue(null,old);saving.SetValue(null,oldSaving);Profile.LastMapId=oldMap;
            if(File.Exists(path)) File.Delete(path);
        }
        File.WriteAllText(Path.Combine(Application.dataPath,focused ? "../docs/menu-feedback-focused-2026-11-01.json" : "../docs/menu-feedback-audit-2026-11-01.json"),JsonUtility.ToJson(report,true));
        Debug.Log("[MenuFeedback] routes="+report.rows.Count+" errors="+report.rows.FindAll(r=>!string.IsNullOrEmpty(r.error)).Count);
        if(Application.isBatchMode) EditorApplication.Exit(report.pendingRoutes>0 || report.rows.Exists(r=>!string.IsNullOrEmpty(r.error)) ? 1 : 0);
    }
}
