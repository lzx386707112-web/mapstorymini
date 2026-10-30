using System;
using System.Reflection;
using Server.Field;
using UnityEngine;

public static class InventoryItemFunctionChecks
{
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
    static int checks;
    static void Require(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
    public static int Check(DbStore db,int cid,PlayerCtrl player,FieldDirector field,Server.Bot.BotRuntime bots)
    {
        checks=0;
        foreach(int id in new[] {2029002,2030000,2030001,2380000,2100000,2280003,2043000,2040000,1302000})
        {
            var result=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=id,quantity=1});
            Require(result.ok,"Item fixture seed failed "+id);
            GameSession.Character.inventory=result.inventory;
        }
        GameSession.ApplyInventoryToProfile();
        Require(NativeInventoryActions.TryUse(2029002)==null,"Timed coupon grant failed");
        var coupon=GameSession.Character.inventory.Find(i=>i.itemId==5360901);
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Require(coupon!=null && coupon.expiration>now+1790000 && coupon.expiration<=now+1800000,"Timed coupon expiration is not 30 minutes");
        Require(Profile.ItemCount(2029002)==0,"Timed coupon source not consumed");
        var row=db.Table("inventoryitems").Find(r=>DbStore.Long(r,"inventoryitemid")==coupon.itemRowId);
        row["expiration"]=now-1;
        Require(!CharService.Load(db,cid).inventory.Exists(i=>i.itemRowId==coupon.itemRowId),"Expired item survived reload");
        Require(NativeInventoryActions.TryUse(2380000)==null && Profile.ItemCount(2380000)==0,"Monster card use failed");
        Require(GameSession.Net.Social(new SocialRequest {characterId=cid,operation=SocialOperation.Read}).cards.ContainsKey(2380000),"Monster book did not persist card");
        Profile.Data.jobId=112;
        Require(NativeInventoryActions.TryUse(2280003)==null,"100% skill book rejected correct job");
        Require(GameSession.Character.skills.Exists(s=>s.skillId==1121000 && s.masterLevel==10),"Skill book did not persist master level");
        GameSession.SaveToDb();
        Require(CharService.Load(db,cid).skills.Exists(s=>s.skillId==1121000 && s.masterLevel==10 && s.skillLevel==0),"Unlearned master level was lost by save");
        Profile.Data.jobId=0;
        var weapon=GameSession.Character.inventory.Find(i=>i.itemId==1302000 && i.position>0);
        Require(EquipLoadout.Equip(1302000,new CharacterAttributes {level=200,str=999,dex=999,intelligence=999,luk=999},weapon.position)==null,"Scroll fixture weapon equip failed");
        var bad=NativeScrollService.Apply(db,new EquipPatchRequest {characterId=cid,inventoryItemId=weapon.itemRowId,scrollId=2040000});
        Require(!bad.ok && InventoryDao.CountOf(db,cid,2040000)==1,"Incompatible scroll consumed item");
        var good=NativeScrollService.Apply(db,new EquipPatchRequest {characterId=cid,inventoryItemId=weapon.itemRowId,scrollId=2043000});
        Require(good.ok && good.scrollResult=="success","100% weapon scroll failed");
        GameSession.Character.inventory=good.inventory; GameSession.ApplyInventoryToProfile();
        var upgraded=good.inventory.Find(i=>i.itemRowId==weapon.itemRowId);
        var clean=Server.Bot.BotItemCatalog.Shared.CleanEquip(1302000);
        Require(upgraded.watk==clean.watk+1 && upgraded.upgradeslots==clean.upgradeslots-1,"Scroll changed wrong stats/slots");
        int attack=0,magic=0;
        EquipCatalog.Apply(1302000,new CharacterAttributes(),ref attack,ref magic,-11);
        Require(attack==upgraded.watk,"Combat ignored scrolled weapon instance");
        Require(InventoryDao.CountOf(db,cid,2043000)==0,"Successful scroll not consumed");
        Require(EquipLoadout.Equip(1122017,new CharacterAttributes {level=200,str=999,dex=999,intelligence=999,luk=999})==null,"Pendant fixture equip failed");
        var effects=player.GetComponent<NativeEquipmentEffects>();
        Require(effects.PendantStage(NativeBuffs.Now)==0,"Pendant starts above zero");
        typeof(NativeEquipmentEffects).GetField("equippedAt",Flags).SetValue(effects,NativeBuffs.Now-7201);
        Require(NativeEquipmentEffects.PendantBonus(109)==20,"Pendant rounding/stage differs from gain/10 * stage");
        EquipLoadout.Unequip(-17);
        Require(NativeEquipmentEffects.PendantBonus(109)==0,"Pendant removal retained EXP bonus");
        var spawner=new MapSpawner(); long clock=1000; spawner.Clock=()=>clock;
        var original=spawner.AddMonsterSpawn(new SpawnPointDef {mob=100100,mobTime=0}); original.MarkSpawned(clock);
        spawner.AddMonsterSpawn(new SpawnPointDef {mob=100100,mobTime=-1});
        var extra=spawner.StartFrenzy();
        Require(extra.Count==1 && spawner.SpawnPointCount==3 && extra[0].ShouldSpawn(clock),"Frenzy duplicate occupied or copied one-shot point");
        original.OnKilled(1000,clock);
        Require(!original.ShouldSpawn(clock+499) && original.ShouldSpawn(clock+500),"Frenzy did not halve respawn delay");
        spawner.StopFrenzy(); Require(spawner.SpawnPointCount==2 && !spawner.FrenzyTotemActive,"Frenzy points not removed");
        original.OnKilled(1000,clock);
        Require(!original.ShouldSpawn(clock+999) && original.ShouldSpawn(clock+1000),"Frenzy cleanup did not restore delay");
        typeof(FieldDirector).GetField("<Map>k__BackingField",Flags).SetValue(field,new FieldMap {id=100000000,returnMap=104000000});
        Require(NativeInventoryActions.TryUse(2030000)==null && Profile.ItemCount(2030000)==0,"Return scroll failed");
        Require((int)typeof(FieldDirector).GetField("pendingWarpMap",Flags).GetValue(field)==104000000,"Return scroll selected wrong town");
        Require(NativeDamageCapService.Resolve(19999999,300000,true)==20299999,"Damage cap success increment differs");
        Require(NativeDamageCapService.Resolve(19999999,300000,false)==19999999,"Failed stone increased cap");
        Require(NativeDamageCapService.Resolve(int.MaxValue-1,300000,true)==int.MaxValue-1,"Damage cap overflow changed cap");
        var stone=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2431152,quantity=1});
        GameSession.Character.inventory=stone.inventory; GameSession.ApplyInventoryToProfile();
        Require(NpcSession.UseItem(2431152)!=null,"Original breakthrough item script did not start");
        Require(Profile.ItemCount(2431152)==0,"Original breakthrough script did not consume stone");
        NpcSession.CloseActive();
        InventoryDao.InsertItem(db,cid,5211000,5,90,1);
        var activeRates=new CommunityResponse();
        NativeCouponRates.Apply(db,cid,activeRates,new DateTime(2026,10,30,18,0,0));
        Require(activeRates.expRate==2,"Scheduled EXP coupon inactive during source window");
        var inactiveRates=new CommunityResponse();
        NativeCouponRates.Apply(db,cid,inactiveRates,new DateTime(2026,10,30,20,0,0));
        Require(inactiveRates.expRate==1,"Scheduled EXP coupon active at exclusive end hour");
        var capacityCheck=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,checkItems=new[] {2000000},checkQuantities=new[] {1}});
        Require(capacityCheck.ok,"Capacity query rejected available potion space");
        int beforeQuery=InventoryDao.CountOf(db,cid,2000000);
        NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,checkItems=new[] {2000000},checkQuantities=new[] {1}});
        Require(InventoryDao.CountOf(db,cid,2000000)==beforeQuery,"Capacity simulation granted an item");
        var invalidCapacity=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,checkItems=new[] {999999999},checkQuantities=new[] {1}});
        Require(!invalidCapacity.ok,"Capacity check accepted missing original item");
        NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5570000,quantity=1});
        var hammerBad=NativeCashEquipmentService.Apply(db,new EquipPatchRequest {characterId=cid,inventoryItemId=weapon.itemRowId,cashItemId=5570000});
        Require(!hammerBad.ok && InventoryDao.CountOf(db,cid,5570000)==1,"Hammer accepted worn equipment or consumed on rejection");
        NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=weapon.itemId,equip=true,equipSlot=-11,quantity=0});
        int slotsBefore=DbStore.Int(InventoryDao.EquipmentOf(db,weapon.itemRowId),"upgradeslots");
        var hammerGood=NativeCashEquipmentService.Apply(db,new EquipPatchRequest {characterId=cid,inventoryItemId=weapon.itemRowId,cashItemId=5570000});
        Require(hammerGood.ok && DbStore.Int(InventoryDao.EquipmentOf(db,weapon.itemRowId),"vicious")==1 && DbStore.Int(InventoryDao.EquipmentOf(db,weapon.itemRowId),"upgradeslots")==slotsBefore+1,"Hammer did not add original slot/vicious count");
        Require(InventoryDao.CountOf(db,cid,5570000)==0,"Successful hammer did not consume item");
        foreach(int id in new[] {2430681,4000227,4000136})
        {
            var seed=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=id,quantity=id==4000227 ? 888 : id==4000136 ? 111 : 1});
            Require(seed.ok,"World quest fixture grant failed"); GameSession.Character.inventory=seed.inventory;
        }
        GameSession.ApplyInventoryToProfile();
        var worldQuest=NpcSession.UseItem(2430681);
        Require(worldQuest!=null && worldQuest.Dialog.RawText.Contains("世界任务1"),"World quest did not resolve original item context");
        int goldBefore=Profile.gold;
        worldQuest.Reply(1);
        Require(string.IsNullOrEmpty(worldQuest.LastError),"WorldQuest failed after reward: "+worldQuest.LastError);
        Require(Profile.IsQuestCompleted(200001),"Original WorldQuest did not persist completion");
        Require(Profile.ItemCount(2430681)==0 && Profile.ItemCount(4000227)==0 && Profile.ItemCount(4000136)==0,"WorldQuest did not consume original material quantities");
        Require(Profile.gold==goldBefore+10000000 && Profile.ItemCount(4032170)==10,"WorldQuest player host did not grant original rewards");
        NpcSession.CloseActive();
        foreach(int id in new[] {2430033,2430674,2370000})
        {
            var seed=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=id,quantity=1});
            Require(seed.ok,"Script fixture grant failed"); GameSession.Character.inventory=seed.inventory;
        }
        GameSession.ApplyInventoryToProfile();
        var star=NpcSession.UseItem(2430033);
        Require(star!=null && star.Dialog.Mode==NpcDialogMode.GetNumber && string.IsNullOrEmpty(star.LastError),"Original star exchange input did not open");
        star.Reply(1,1);
        Require(star.Dialog.Mode==NpcDialogMode.YesNo && string.IsNullOrEmpty(star.LastError),"Star exchange original confirmation failed");
        star.Reply(1);
        Require(string.IsNullOrEmpty(star.LastError) && Profile.ItemCount(2430033)==0 && Profile.ItemCount(5451000)==1,"Original star exchange reward branch failed");
        NpcSession.CloseActive();
        var steal=NpcSession.UseItem(2430674);
        Require(steal!=null && string.IsNullOrEmpty(steal.LastError) && Profile.ItemCount(2430674)==0,"Original stolen skill point script failed");
        NpcSession.CloseActive();
        Require(NativeInventoryActions.TryUse(2370000)!=null && Profile.ItemCount(2370000)==1,"Solomon book ignored original maxLevel");
        Profile.Data.level=40; GameSession.SaveToDb();
        int bankBefore=GameSession.Character.record.gachaexp;
        int bankGrant=NativeItemSource.Node(2370000).Int("spec/exp");
        Require(NativeInventoryActions.TryUse(2370000)==null && GameSession.Character.record.gachaexp==bankBefore+bankGrant && Profile.ItemCount(2370000)==0,"Solomon book did not bank original EXP");
        var morphClip=WzAssets.MorphClip("1","walk");
        Require(morphClip!=null && morphClip.frames.Length==3 && morphClip.delays[0]>.0f,"Morph original animation not loaded");
        var morphGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2210000,quantity=1});
        GameSession.Character.inventory=morphGrant.inventory; GameSession.ApplyInventoryToProfile();
        Require(NativeInventoryActions.TryUse(2210000)==null && NativeBuffs.For(player).Value("MORPH")==1,"Original morph potion did not apply MORPH");
        NativeBuffs.For(player).Cancel(-2210000);
        var catchGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2270000,quantity=1});
        GameSession.Character.inventory=catchGrant.inventory; GameSession.ApplyInventoryToProfile();
        var catchGo=new GameObject("CaptureFixture");
        var capture=catchGo.AddComponent<EnemyUnit>();
        capture.fieldMode=true;capture.fieldMobId=9300101;capture.hp=capture.hpMax=100;
        capture.transform.position=player.FeetPos;
        EnemyUnit.All.Add(capture);
        int hogBefore=Profile.ItemCount(1902000);
        Require(NativeInventoryActions.TryUse(2270000)==null && Profile.ItemCount(2270000)==0 && Profile.ItemCount(1902000)==hogBefore+1,"Pheromone did not capture original hog/reward");
        EnemyUnit.All.Remove(capture);
        if(catchGo!=null) UnityEngine.Object.DestroyImmediate(catchGo);
        var lockGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5061000,quantity=1});
        var seal=NativeCashEquipmentService.Apply(db,new EquipPatchRequest {characterId=cid,inventoryItemId=weapon.itemRowId,cashItemId=5061000});
        var sealedWeapon=seal.inventory?.Find(i=>i.itemRowId==weapon.itemRowId);
        Require(seal.ok && sealedWeapon!=null && (sealedWeapon.flag&1)!=0 && sealedWeapon.expiration>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+6*86400000L,"Seven-day seal did not preserve original flag/expiration");
        Require(InventoryDao.CountOf(db,cid,5061000)==0,"Successful seal did not consume cash item");
        InventoryDao.ExpireItems(db,cid,sealedWeapon.expiration+1);
        var unlocked=CharService.Load(db,cid).inventory.Find(i=>i.itemRowId==weapon.itemRowId);
        Require(unlocked!=null && (unlocked.flag&1)==0 && unlocked.expiration==-1,"Expired seal destroyed equipment instead of unlocking it");
        var stackActor=new Server.Bot.BotActor {id=cid,job=Profile.JobId,character=CharService.Load(db,cid)};
        int stackMax=new Server.Bot.Trade.TradeItemRules(db,Server.Bot.BotItemCatalog.Shared).SlotMax(2000000,stackActor);
        var stackGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2000000,quantity=stackMax+1});
        var potionStacks=stackGrant.inventory.FindAll(i=>i.itemId==2000000 && i.position>0);
        Require(stackGrant.ok && potionStacks.Count==2 && potionStacks.TrueForAll(i=>i.quantity<=stackMax),"Normal item grant ignored original stack limit");
        Require(activeRates.couponBuffs.Contains(5211000) && !inactiveRates.couponBuffs.Contains(5211000),"Coupon status icons not bound to active schedule");
        var boxGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2022309,quantity=1});
        GameSession.Character.inventory=boxGrant.inventory;GameSession.ApplyInventoryToProfile();
        var randomState=UnityEngine.Random.state;
        int boxSeed=0;
        for(;boxSeed<10000;boxSeed++) { UnityEngine.Random.InitState(boxSeed); if(UnityEngine.Random.Range(0,100)==0) break; }
        Require(boxSeed<10000,"Could not seed deterministic original reward roll");
        UnityEngine.Random.InitState(boxSeed);
        int prizeBefore=Profile.ItemCount(4001126);
        Require(NativeInventoryActions.TryUse(2022309)==null && Profile.ItemCount(2022309)==0 && Profile.ItemCount(4001126)==prizeBefore+500,"Reward box did not grant first original row / consume source");
        UnityEngine.Random.state=randomState;
        var petGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5000000,quantity=1});
        Require(petGrant.ok,"Pet fixture grant failed");
        var petItem=petGrant.inventory.Find(i=>i.itemId==5000000);
        Require(petItem!=null && petItem.petId>=0 && petItem.petData!=null,"Pet grant did not create original pets row");
        var petRow=db.Table("inventoryitems").Find(r=>DbStore.Long(r,"inventoryitemid")==petItem.itemRowId);
        petRow["expiration"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-1;
        var waterGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5180000,quantity=1});
        GameSession.Character.inventory=waterGrant.inventory;GameSession.ApplyInventoryToProfile();
        Require(GameSession.Character.inventory.Exists(i=>i.itemRowId==petItem.itemRowId && i.expiration==-1),"Expired permanent pet was destroyed instead of becoming a doll");
        Require(NativeInventoryActions.TryUse(5180000)==null,"Water of life original NPC route rejected");
        var water=NpcSession.Active;
        Require(water!=null && string.IsNullOrEmpty(water.LastError) && water.Dialog.Mode==NpcDialogMode.YesNo,"Water of life did not enumerate dried pets");
        water.Reply(1);water.Reply(1,0);
        Require(string.IsNullOrEmpty(water.LastError) && Profile.ItemCount(5180000)==0,"Original water of life script failed to consume water");
        var revived=CharService.Load(db,cid).inventory.Find(i=>i.itemRowId==petItem.itemRowId);
        Require(revived!=null && revived.expiration>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+89*86400000L,"Water of life did not persist original ninety-day expiration");
        NpcSession.CloseActive();
        var foodGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2120000,quantity=2});
        GameSession.Character.inventory=foodGrant.inventory;GameSession.ApplyInventoryToProfile();
        Require(NativeInventoryActions.TryUse(2120000)==null && Profile.ItemCount(2120000)==2,"Pet food consumed without summoned pet");
        var petState=db.Table("pets").Find(r=>DbStore.Int(r,"petid")==petItem.petId);
        petState["summoned"]=1L;petState["fullness"]=50L;
        var cashFoodGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5240004,quantity=1});
        GameSession.Character.inventory=cashFoodGrant.inventory;GameSession.ApplyInventoryToProfile();
        Require(NativeInventoryActions.TryUse(5240004)==null && Profile.ItemCount(5240004)==0,"Matching original cash pet food rejected");
        Require(DbStore.Int(petState,"fullness")==100 && DbStore.Int(petState,"closeness")>0,"Cash pet food did not restore fullness / intimacy");
        foreach(int id in new[] {2430014,2430015,2431159})
        {
            var grant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=id,quantity=1});
            GameSession.Character.inventory=grant.inventory;
        }
        GameSession.ApplyInventoryToProfile();
        var noParty=NpcSession.UseItem(2431159);
        Require(noParty!=null && string.IsNullOrEmpty(noParty.LastError) && Profile.ItemCount(2431159)==1,"Party companion item did not preserve original no-party rejection");
        NpcSession.CloseActive();
        Profile.LastMapId=106020300;
        var mushroomMap=FieldCatalog.GetMap(106020300);
        var obstacle=mushroomMap.Portal("obstacle");
        Require(obstacle!=null,"Original mushroom barrier portal missing");
        player.worldX=(obstacle.x+500)/Art.PPU; player.terrainY=-obstacle.y/Art.PPU-player.DepthOffset; player.jumpY=0;
        var farBarrier=NpcSession.UseItem(2430014);
        Require(farBarrier!=null && string.IsNullOrEmpty(farBarrier.LastError) && Profile.ItemCount(2430014)==1,"Barrier item ignored original portal distance");
        NpcSession.CloseActive();
        player.worldX=obstacle.x/Art.PPU; player.terrainY=-obstacle.y/Art.PPU-player.DepthOffset; player.jumpY=0;
        var nearBarrier=NpcSession.UseItem(2430014);
        Require(nearBarrier!=null && string.IsNullOrEmpty(nearBarrier.LastError) && Profile.ItemCount(2430014)==0 && Profile.IsQuestStarted(100202),"Original mushroom barrier success branch failed");
        NpcSession.CloseActive();
        Profile.LastMapId=106020500;Profile.StartQuest(2324);
        var thorn=FieldCatalog.GetMap(106020500).Portal("investigate2");
        Require(thorn!=null,"Original thorn investigation portal missing");
        player.worldX=thorn.x/Art.PPU; player.terrainY=-thorn.y/Art.PPU-player.DepthOffset; player.jumpY=0;
        var removeThorns=NpcSession.UseItem(2430015);
        Require(removeThorns!=null && string.IsNullOrEmpty(removeThorns.LastError) && Profile.ItemCount(2430015)==0 && Profile.IsQuestCompleted(2324),"Original thorn quest / item success branch failed");
        NpcSession.CloseActive();
        foreach(int id in new[] {5610000,2043002})
        {
            var grant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=id,quantity=1});
            Require(grant.ok,"Vega fixture seed failed");GameSession.Character.inventory=grant.inventory;
        }
        GameSession.ApplyInventoryToProfile();
        NativeVegaWindow.Open(5610000);
        var vegaEquip=GameSession.Character.inventory.Find(i=>i.itemRowId==weapon.itemRowId);
        var vegaScroll=GameSession.Character.inventory.Find(i=>i.itemId==2043002);
        Require(NativeVegaWindow.Visible && NativeVegaWindow.Select(vegaEquip)==null,"Vega rejected backpack equipment");
        Require(NativeVegaWindow.Select(GameSession.Character.inventory.Find(i=>i.itemId==2040000))!=null,"Vega accepted 100 percent scroll");
        Require(NativeVegaWindow.Select(vegaScroll)==null,"Vega rejected original 10 percent whitelist scroll");
        Require(Profile.ItemCount(5610000)==1 && Profile.ItemCount(2043002)==1,"Vega selection consumed items");
        Require(NativeVegaWindow.Start()==null && NativeVegaWindow.Busy,"Vega start failed");
        Require(Profile.ItemCount(5610000)==0 && Profile.ItemCount(2043002)==0,"Vega authoritative settlement not published immediately");
        Require(NativeVegaWindow.Start()!=null,"Vega accepted duplicate submit during animation");
        GameSession.SaveToDb();
        Require(InventoryDao.CountOf(db,cid,5610000)==0 && InventoryDao.CountOf(db,cid,2043002)==0,"Save during Vega animation restored consumed items");
        NativeVegaWindow.Close();
        foreach(int id in new[] {2022225,2022226,2022227,2022268})
        {
            var grant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=id,quantity=1});
            GameSession.Character.inventory=grant.inventory;GameSession.ApplyInventoryToProfile();
            NativeMobDebuffs.For(player).Clear();
            Require(NativeInventoryActions.TryUse(id)==null && Profile.ItemCount(id)==0,"specEx item did not consume/apply "+id);
            int disease=id==2022225 ? 125 : id==2022226 ? 124 : id==2022227 ? 122 : 132;
            Require(NativeMobDebuffs.For(player).Has(disease)==(id!=2022268),"specEx target or disease differs from original "+id);
        }
        NativeMobDebuffs.For(player).Clear();
        foreach(int id in new[] {5100000,5160000})
        {
            var grant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=id,quantity=1});
            GameSession.Character.inventory=grant.inventory;GameSession.ApplyInventoryToProfile();
            Require(NativeInventoryActions.TryUse(id)==null,"Cash original entry rejected "+id);
            Require(Profile.ItemCount(id)==(id==5160000 ? 1 : 0),"Cash entry consumption differs from original "+id);
        }
        GameSession.Character.inventory=CharService.Load(db,cid).inventory;GameSession.ApplyInventoryToProfile();
        Require(NativeInventoryActions.TryUse(5000000,revived.position)==null,"Pet toggle rejected revived pet");
        Require(DbStore.Int(petState,"summoned")==0,"Already summoned pet did not return");
        Require(NativeInventoryActions.TryUse(5000000,revived.position)==null && DbStore.Int(petState,"summoned")==1,"Pet toggle failed to persist summon");
        Require(Profile.ItemCount(5000000)==1,"Pet summon consumed pet item");
        petRow["expiration"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-1;
        Require(NativeInventoryActions.TryUse(5000000,revived.position)!=null,"Expired pet could be summoned");
        var appearanceHost=NpcApi.Build(NpcSession.Conversation(1012103,"",(session,mode)=>{}),1012103,0,false);
        foreach(var appearance in new[] {new[] {0,40070},new[] {1,20005},new[] {2,3}})
        {
            string method=appearance[0]==0 ? "setHair" : appearance[0]==1 ? "setFace" : "setSkin";
            ((MapleDash.Js.JsFunction)appearanceHost.Get(method)).Native(new MapleDash.Js.JsCall {
                Args=new MapleDash.Js.JsValue[] {new MapleDash.Js.JsNumber(appearance[1])}});
            var saved=CharactersDao.FindById(db,cid);
            Require((appearance[0]==0 ? saved.hair : appearance[0]==1 ? saved.face : saved.skincolor)==appearance[1],
                "NPC cosmetic setter did not preserve original appearance ID: "+method);
        }
        NpcSession.CloseActive();
        GameSession.Character.record.gender=0;
        var salon=NpcSession.Talk(1012103);
        Require(salon.LastError==null && salon.Dialog.Mode==NpcDialogMode.Style && salon.Dialog.MessageType==7 &&
            salon.Dialog.Styles.Length==12 && salon.Dialog.Styles[0]==40070,"Original salon style array / packet type was not preserved");
        NativeNpcStyleWindow.Move(salon.Dialog,-1);
        Require(salon.Dialog.StyleIndex==11,"Native style previous did not wrap");
        NativeNpcStyleWindow.Move(salon.Dialog,1);
        Require(salon.Dialog.StyleIndex==0 && NativeNpcStyleWindow.PreviewIds(salon.Dialog)[3]=="00040070","Native hair preview changed ID / did not wrap");
        salon.Reply(1,1);
        Require(salon.LastError==null && salon.Dialog.Mode==NpcDialogMode.Style && salon.Dialog.Styles.Length==8 &&
            salon.Dialog.Styles[3]==40083,"Original salon color branch did not receive selection index");
        salon.Reply(1,3);
        Require(salon.LastError==null && CharactersDao.FindById(db,cid).hair==40083,"Original salon script did not persist exact selected hair/color");
        NpcSession.CloseActive();
        var fatigueGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2260000,quantity=2});
        GameSession.Character.inventory=fatigueGrant.inventory;GameSession.ApplyInventoryToProfile();
        GameSession.Character.record.mounttiredness=40;
        Require(NativeInventoryActions.TryUse(2260000)==null && CharactersDao.FindById(db,cid).mounttiredness==10 &&
            Profile.ItemCount(2260000)==1,"Mount fatigue potion did not consume / persist original -30 effect");
        Require(NativeInventoryActions.TryUse(2260000)==null && CharactersDao.FindById(db,cid).mounttiredness==0 &&
            Profile.ItemCount(2260000)==0,"Mount fatigue potion failed lower clamp");
        var drawSeed=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2430680,quantity=1});
        GameSession.Character.inventory=drawSeed.inventory;GameSession.ApplyInventoryToProfile();
        int worldItemsBefore=0;foreach(var held in GameSession.Character.inventory) if(held.itemId>=2430681 && held.itemId<=2430705) worldItemsBefore+=held.quantity;
        Require(NativeInventoryActions.TryUse(2430680)==null && Profile.ItemCount(2430680)==0 && !NpcSession.Blocking,
            "World quest starter bypassed original server special branch / opened reward JS");
        int worldItemsAfter=0;foreach(var held in GameSession.Character.inventory) if(held.itemId>=2430681 && held.itemId<=2430705) worldItemsAfter+=held.quantity;
        Require(worldItemsAfter==worldItemsBefore+1,"World quest starter did not grant exactly one original range item");
        var drawAgain=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2430680,worldQuestDraw=true});
        Require(!drawAgain.ok && drawAgain.error.Contains("冷却"),"World quest starter omitted original one-second cooldown");
        var hungerCharacter=CharactersDao.FindById(db,cid);int priorGm=hungerCharacter.gm;hungerCharacter.gm=0;CharactersDao.Save(db,hungerCharacter);
        petRow["expiration"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+900000;
        petState["summoned"]=1L;petState["fullness"]=6L;
        long hungerNow=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        NativePetItems.Tick(db,cid,hungerNow);
        Require(DbStore.Int(petState,"fullness")==6 && DbStore.Int(petState,"summoned")==1,"Pet hunger tick ran before original minute boundary");
        NativePetItems.Tick(db,cid,hungerNow+61000);
        Require(DbStore.Int(petState,"fullness")==15 && DbStore.Int(petState,"summoned")==0 && InventoryDao.CountOf(db,cid,5000000)==1,
            "Hungry pet did not return at <=5 / reset fullness15 without consuming pet");
        hungerCharacter=CharactersDao.FindById(db,cid);hungerCharacter.gm=priorGm;CharactersDao.Save(db,hungerCharacter);
        petState["summoned"]=1L;petState["fullness"]=50L;
        NativePetItems.EndSession(db,cid);
        long foodRow=InventoryDao.InsertItem(db,cid,2120000,2,95,2);
        int foodBefore=InventoryDao.CountOf(db,cid,2120000);
        var wrongFood=NativePetItems.Feed(db,new InventoryChangeRequest {characterId=cid,itemId=2120000,sourcePosition=94});
        Require(!wrongFood.ok && InventoryDao.CountOf(db,cid,2120000)==foodBefore,"Pet food accepted mismatching source slot");
        var selectedFood=NativePetItems.Feed(db,new InventoryChangeRequest {characterId=cid,itemId=2120000,sourcePosition=95});
        Require(selectedFood.ok && DbStore.Int(db.Table("inventoryitems").Find(r=>DbStore.Long(r,"inventoryitemid")==foodRow),"quantity")==1 &&
            InventoryDao.CountOf(db,cid,2120000)==foodBefore-1 && DbStore.Int(petState,"fullness")==80,"Pet food did not consume selected stack / apply original fullness");
        SkillsDao.Upsert(db,cid,8,1,1,-1);
        long secondPetRow=InventoryDao.InsertItem(db,cid,5000000,5,94,1);
        var secondPetItem=db.Table("inventoryitems").Find(r=>DbStore.Long(r,"inventoryitemid")==secondPetRow);
        secondPetItem["expiration"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+900000;
        var secondPetState=db.Table("pets").Find(r=>DbStore.Int(r,"petid")==DbStore.Int(secondPetItem,"petid"));
        Require(NativePetItems.Toggle(db,new InventoryChangeRequest {characterId=cid,itemId=5000000,sourcePosition=94}).ok,
            "Second pet failed original multi-pet skill gate");
        // Moving the lead pet behind the later summon must not reorder pet slots.
        petRow["position"]=95L;petState["fullness"]=50L;secondPetState["fullness"]=50L;
        NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5240004,quantity=1});
        Require(NativePetItems.Feed(db,new InventoryChangeRequest {characterId=cid,itemId=5240004}).ok &&
            DbStore.Int(petState,"fullness")==100 && DbStore.Int(secondPetState,"fullness")==50,
            "Cash food changed lead pet after inventory reordering");
        secondPetState["summoned"]=0L;
        petState["summoned"]=0L;
        NativePetItems.EndSession(db,cid);
        var dragonGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5000029,quantity=1});
        var dragon=dragonGrant.inventory.Find(i=>i.itemId==5000029);
        var dragonState=db.Table("pets").Find(r=>DbStore.Int(r,"petid")==dragon.petId);
        dragonState["summoned"]=1L;dragonState["level"]=15L;dragonState["closeness"]=1800L;
        dragonState["fullness"]=73L;dragonState["name"]="测试龙";
        var evolveSeed=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5380000,quantity=1});
        GameSession.Character.inventory=evolveSeed.inventory;GameSession.ApplyInventoryToProfile();
        var evolution=NpcSession.Talk(1032102);
        evolution.Reply(1);
        Require(string.IsNullOrEmpty(evolution.LastError),"Original pet evolution script failed: "+evolution.LastError);
        var evolved=CharService.Load(db,cid).inventory.Find(i=>i.itemId>=5000030 && i.itemId<=5000033);
        Require(evolved!=null && InventoryDao.CountOf(db,cid,5000029)==0 && InventoryDao.CountOf(db,cid,5380000)==0,
            "Original evolution did not replace dragon / consume stone");
        Require(evolved.petId!=dragon.petId && Convert.ToInt32(evolved.petData["level"])==15 &&
            Convert.ToInt32(evolved.petData["closeness"])==1800 && Convert.ToInt32(evolved.petData["fullness"])==73 &&
            evolved.petData["name"].ToString()=="测试龙" && evolved.expiration>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+89L*86400000,
            "Evolved pet lost inherited data or ninety-day expiration");
        var secondCharacter=new CharRecord {accountid=1,name="PetClockAudit",level=1,hp=50,maxhp=50};
        int secondCid=(int)CharactersDao.Insert(db,secondCharacter);
        var clockPetGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=secondCid,itemId=5000000,quantity=1});
        var clockPet=clockPetGrant.inventory.Find(i=>i.itemId==5000000);
        var clockPetState=db.Table("pets").Find(r=>DbStore.Int(r,"petid")==clockPet.petId);
        clockPetState["summoned"]=1L;clockPetState["fullness"]=50L;
        NativePetItems.EndSession(db,cid);NativePetItems.EndSession(db,secondCid);
        long sharedNow=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        NativePetItems.Tick(db,cid,sharedNow);NativePetItems.Tick(db,secondCid,sharedNow);
        NativePetItems.Tick(db,cid,sharedNow+61000);NativePetItems.Tick(db,secondCid,sharedNow+61000);
        Require(DbStore.Int(clockPetState,"fullness")==48,"First character stole shared hunger tick from second character");
        NativePetItems.Tick(db,secondCid,sharedNow+61001);
        Require(DbStore.Int(clockPetState,"fullness")==48,"Second character processed the same hunger minute twice");
        NativePetItems.EndSession(db,secondCid);
        var requirement=new QuestRequirement {petTameness=1800};requirement.pets.Add(evolved.itemId);
        var questCheck=typeof(QuestLogic).GetMethod("Check",BindingFlags.NonPublic|BindingFlags.Static);
        object[] questArgs={requirement,0,0,null};
        Require((bool)questCheck.Invoke(null,questArgs),"Summoned evolved pet could not satisfy original quest requirements");
        var evolvedState=db.Table("pets").Find(r=>DbStore.Int(r,"petid")==evolved.petId);
        evolvedState["summoned"]=0L;
        Require(!(bool)questCheck.Invoke(null,questArgs),"Pet in cash inventory satisfied summoned-pet quest requirements");
        evolvedState["summoned"]=1L;
        NativePetItems.GetPet(db,cid,0);
        secondPetState["summoned"]=1L;
        int followerBefore=DbStore.Int(secondPetState,"closeness");
        QuestLogic.Apply(new QuestActions {petTameness=7},0);
        Require(DbStore.Int(evolvedState,"closeness")==1807 && DbStore.Int(secondPetState,"closeness")==followerBefore,
            "Quest pettameness action did not limit reward to lead pet");
        secondPetState["summoned"]=0L;


        NpcSession.CloseActive();
        foreach(int ticket in new[] {5150000,5220000,5251000,5380000,5420000,5460000,5490000,5190000})
        {
            var ticketSeed=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=ticket,quantity=1});
            Require(ticketSeed.ok,"Ticket fixture seed failed: "+ticket+" "+ticketSeed.error);
            GameSession.Character.inventory=ticketSeed.inventory;GameSession.ApplyInventoryToProfile();
            int count=Profile.ItemCount(ticket);
            Require(NativeInventoryActions.TryUse(ticket)==null && Profile.ItemCount(ticket)==count && !NpcSession.Blocking,
                "Native NPC/quest-only ticket double-click consumed or opened unrelated flow: "+ticket);
        }
        var snackSeed=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5460000,quantity=1});
        GameSession.Character.inventory=snackSeed.inventory;GameSession.ApplyInventoryToProfile();
        Profile.StartQuest(4647);
        var petSnack=NpcSession.TalkQuest(QuestCatalog.Get(4647),QuestCatalog.Get(4647).completeReq.npc,true);
        Require(petSnack!=null && string.IsNullOrEmpty(petSnack.LastError) && Profile.ItemCount(5460000)==1 &&
            Profile.IsQuestCompleted(4647) && SkillProgress.LevelOf(8)==1,"Original pet snack quest failed to consume / teach multi-pet skill");
        NpcSession.CloseActive();
        var petApi=NpcApi.Build(NpcSession.Conversation(1032102,"",(session,mode)=>{}),1032102,0,false);
        ((MapleDash.Js.JsFunction)petApi.Get("gainTameness")).Native(new MapleDash.Js.JsCall {Args=new MapleDash.Js.JsValue[] {new MapleDash.Js.JsNumber(5)}});
        Require(Convert.ToInt32(CharService.Load(db,cid).inventory.Find(i=>i.petId==evolved.petId).petData["closeness"])==1812,
            "NPC gainTameness remained a no-op for a summoned full pet");
        ((MapleDash.Js.JsFunction)petApi.Get("gainTameness")).Native(new MapleDash.Js.JsCall {Args=new MapleDash.Js.JsValue[] {new MapleDash.Js.JsNumber(-5)}});
        Require(Convert.ToInt32(CharService.Load(db,cid).inventory.Find(i=>i.petId==evolved.petId).petData["closeness"])==1812,
            "NPC negative tameness changed source no-op semantics");

        NpcSession.CloseActive();

        bots.SynchronizePlayer(GameSession.Character,104000000,0,0);
        Require(GameSession.Net.Social(new SocialRequest {characterId=cid,operation=SocialOperation.CreateParty}).ok,
            "Companion script fixture party creation failed");
        var companions=NpcSession.UseItem(2431159);
        Require(companions!=null && companions.LastError==null && Profile.ItemCount(2431159)==0,
            "Original companion script did not consume on successful summon: "+companions?.LastError);
        var grind=GameSession.Net.PartyGrind(new Server.Bot.PartyGrindRequest {characterId=cid});
        Require(grind.active && grind.companions.Count==5,"Companion item did not summon original five party members");
        var extraCompanion=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=2431159,quantity=1});
        GameSession.Character.inventory=extraCompanion.inventory;GameSession.ApplyInventoryToProfile();
        companions=NpcSession.UseItem(2431159);
        Require(companions!=null && companions.LastError==null && Profile.ItemCount(2431159)==1,
            "Repeated active companion script consumed another item");
        GameSession.Net.PartyGrind(new Server.Bot.PartyGrindRequest {characterId=cid,operation=Server.Bot.PartyGrindOperation.Stop});
        GameSession.Net.Social(new SocialRequest {characterId=cid,operation=SocialOperation.LeaveParty});
        NpcSession.CloseActive();
        foreach(int id in new[] {2431151,2435719})
        {
            var warpState=typeof(FieldDirector).GetField("warpState",Flags);
            warpState.SetValue(field,Enum.ToObject(warpState.FieldType,0));
            var seed=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=id,quantity=1});
            Require(seed.ok,"Original item script fixture seed failed "+id);
            GameSession.Character.inventory=seed.inventory;GameSession.ApplyInventoryToProfile();
            var originalItemSession=NpcSession.UseItem(id);
            Require(originalItemSession!=null && originalItemSession.LastError==null,"Original item script failed "+id+": "+originalItemSession?.LastError);
            Require(Profile.ItemCount(id)==(id==2431151 ? 0 : 1),"Original item script consumption differs "+id);
            if(id==2431151) Require((int)typeof(FieldDirector).GetField("pendingWarpMap",Flags).GetValue(field)==105040300,
                "Original warp item followed stale comment instead of actual JS destination");
            NpcSession.CloseActive();
        }
        var wheelGrant=NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=5510000,quantity=1});
        Require(wheelGrant.ok,"Wheel fixture grant failed");
        var dead=CharactersDao.FindById(db,cid);dead.map=104000000;dead.hp=0;dead.mp=7;CharactersDao.Save(db,dead);
        var wheelMap=FieldCatalog.GetMap(dead.map);var spawn=wheelMap.SpawnPortal;
        Require(DeathFlow.WheelAllowed(0,104000000) && !DeathFlow.WheelAllowed(0,910000000) && !DeathFlow.WheelAllowed(3,104000000),"Wheel special-field gate differs from native helper");
        Require(WzAssets.Texture(WzAssets.Node("UI/UIWindow.img/Notice/2")).width==286 &&
            WzAssets.Texture(WzAssets.Node("UI/Basic.img/BtYes2/normal/0"))!=null,"Wheel original dialog artwork missing");
        var wheel=GameSession.Net.ChangeMap(new ChangeMapRequest {characterId=cid,fromDying=true,wheel=true,deathX=(int)spawn.x,deathY=(int)spawn.y});
        Require(wheel.ok && wheel.wheelUsed && wheel.mapId==104000000 && wheel.hp==50 && wheel.mp==7,"Wheel did not revive at same map with original HP / unchanged MP");
        Require(InventoryDao.CountOf(db,cid,5510000)==0 && wheel.inventory!=null,"Wheel was not consumed / authoritative inventory missing");
        Require(wheel.spawnPoint==wheelMap.portals.IndexOf(spawn),"Wheel did not choose closest player spawnpoint");
        GameSession.Character.inventory=wheel.inventory;GameSession.ApplyInventoryToProfile();
        return checks;
    }
}
