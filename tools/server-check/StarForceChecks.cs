using System;
using System.IO;
using Server.Bot;

static class StarForceChecks
{
    sealed class Draw : Random { readonly int value; public Draw(int value) {this.value=value;} public override int Next(int max) => value; }
    public static void Run(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash_sf_"+Guid.NewGuid().ToString("N")+".json");
        var db=DbStore.Create(path);
        var rules=new StarForceRules(File.ReadAllText("Assets/Resources/Enhancement/StarForce.json"));
        var catalog=new BotItemCatalog("{\"names\":{},\"items\":{\"1302000\":{\"info\":{\"reqLevel\":150,\"tuc\":7,\"incSTR\":10,\"incPAD\":100}}}}");
        try
        {
            int cid=(int)db.Table("characters").Insert(new DbRow {["name"]="SfTest",["meso"]=10000000000L,["hp"]=100L,["equipslots"]=24L});
            long id=InventoryDao.InsertItem(db,cid,1302000,InventoryDao.Equipped,-11,1);
            InventoryDao.InsertEquipment(db,id,str:10,watk:100);
            var eq=InventoryDao.EquipmentOf(db,id);
            eq["enhancementVersion"]=282L; eq["starLevel"]=17L; eq["starCount"]=1L;
            StarForceResponse Send(bool commit,int revision,int draw=9999,bool safeguard=false)
                => NativeStarForceService.Handle(db,new StarForceRequest {characterId=cid,inventoryItemId=id,commit=commit,revision=revision,safeguard=safeguard},rules,catalog,new Draw(draw));
            long before=CharactersDao.FindById(db,cid).meso;
            var preview=Send(false,0);
            check(preview.ok && CharactersDao.FindById(db,cid).meso==before,"星力预览不扣款");
            var drop=Send(true,0);
            check(drop.ok && drop.result.stars==16 && drop.result.dropStreak==2,"第二次掉星进入Chance Time");
            check(drop.meso==before-preview.quote.cost && DbStore.Int(eq,"watk")==100,"报价与扣款一致，星力不烘焙基础攻击");
            var legacyPatch=NativeEquipService.Patch(db,new EquipPatchRequest {characterId=cid,inventoryItemId=id,starLevel=30,watk=9999});
            check(!legacyPatch.ok && DbStore.Int(eq,"starLevel")==16 && DbStore.Int(eq,"watk")==100,"旧属性接口不能覆盖282权威结算");
            var stale=Send(true,0);
            check(!stale.ok && CharactersDao.FindById(db,cid).meso==drop.meso,"重复提交旧revision不再扣费");
            var chance=Send(false,1);
            check(chance.quote.success==10000 && chance.quote.destroy==0,"Chance Time预览必成且不破坏");
            var success=Send(true,1);
            check(success.ok && success.result.stars==17 && success.result.dropStreak==0,"Chance Time最大随机数仍成功并清计数");
            var protectedQuote=Send(false,2,9999,true);
            check(protectedQuote.ok && protectedQuote.quote.destroy==0 && protectedQuote.quote.cost==rules.Cost(1302000,150,17,false)*3,"保护报价消除破坏率且费用三倍");
            var boom=Send(true,2,3000);
            check(boom.ok && boom.result.vestige && boom.result.stars==17,"破坏保留普通装备星级，生成残骸");
            var item=db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==id);
            check(DbStore.Int(item,"inventorytype")==1 && DbStore.Int(item,"position")>0,"穿戴装备破坏后移入背包空格");
            check(!Send(true,3).ok,"残骸不能继续强化");
            db.Save();
            var reopened=DbStore.Open(path);
            var persisted=InventoryDao.EquipmentOf(reopened,id);
            check(DbStore.Int(persisted,"starForceVestige")==1 && DbStore.Int(persisted,"starForceRevision")==3 && DbStore.Int(persisted,"nativeStats")==1,"重开存档保留残骸、revision和基础属性标记");
            check(CharactersDao.FindById(reopened,cid).meso==boom.meso,"扣款重启后不丢失");
            long donor=InventoryDao.InsertItem(db,cid,1302000,1,2,1);
            InventoryDao.InsertEquipment(db,donor,str:1,watk:1);
            var restore=NativeStarForceService.Handle(db,new StarForceRequest {characterId=cid,inventoryItemId=id,sacrificeInventoryItemId=donor,revision=3,commit=true},rules,catalog);
            check(restore.ok && restore.restored && DbStore.Int(eq,"starForceVestige")==0 && DbStore.Int(eq,"starLevel")==17 && DbStore.Int(eq,"watk")==100,"同ID材料复原残骸，保留原星级及基础属性");
            check(InventoryDao.EquipmentOf(db,donor)==null && db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==donor)==null,"复原恰好消耗一件材料及其装备属性行");
            check(CharactersDao.FindById(db,cid).meso==boom.meso,"源端痕迹复原不收取金币");
            check(!NativeStarForceService.Handle(db,new StarForceRequest {characterId=cid+1,inventoryItemId=id,commit=true},rules,catalog).ok,"不能强化其他角色的装备");
            long potion=InventoryDao.InsertItem(db,cid,2000000,2,1,20);
            int removed=EnhancementEquipmentReset.Apply(db);
            check(removed>0 && db.Table("inventoryequipment").rows.Count==0,"切换清空旧装备及属性行");
            check(db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==potion)!=null,"切换保留消耗品");
            var backup=DbStore.Open(path+".before-equipment-282-reset.bak");
            check(InventoryDao.EquipmentOf(backup,id)!=null,"切换备份保留清空前装备");
            var reset=DbStore.Open(path);
            long fresh=InventoryDao.InsertItem(reset,cid,1302000,1,1,1);
            InventoryDao.InsertEquipment(reset,fresh);
            check(EnhancementEquipmentReset.Apply(reset)==0 && InventoryDao.EquipmentOf(reset,fresh)!=null,"完成标记重启后生效，不删除新装备");
        }
        finally {if(File.Exists(path))File.Delete(path);if(File.Exists(path+".before-equipment-282-reset.bak"))File.Delete(path+".before-equipment-282-reset.bak");}
        CheckTransportMirror(check);
    }
    static void CheckTransportMirror(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash_sf_mirror_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var db=DbStore.Create(path);
            int cid=(int)db.Table("characters").Insert(new DbRow {["name"]="SfMirror",["meso"]=1000000000L,["hp"]=100L});
            long target=InventoryDao.InsertItem(db,cid,1302134,1,1,1);
            InventoryDao.InsertEquipment(db,target,watk:100);
            var eq=InventoryDao.EquipmentOf(db,target);
            eq["enhancementVersion"]=282L;eq["starCount"]=2L;
            var server=GameServer.StartWith(db);
            var player=server.Bots.SynchronizePlayer(CharService.Load(db,cid),910000000,0,0);
            var stale=CharService.Load(db,cid);
            var response=server.Transport.StarForce(new StarForceRequest {
                characterId=cid,inventoryItemId=target,revision=0,commit=true});
            check(response.ok&&response.result.stars==1&&response.meso<1000000000L
                &&player.character.record.meso==response.meso&&player.character.tradeRevision==1,
                "正式星力接口同步在线金币与装备镜像");
            new Server.Bot.Trade.BotTradeService(db,BotItemCatalog.Shared).Persist(player);
            stale.record.exp=321;
            server.Transport.SaveCharacter(new SaveCharacterRequest {characterId=cid,character=stale});
            check(CharactersDao.FindById(db,cid).meso==response.meso&&CharactersDao.FindById(db,cid).exp==321
                &&DbStore.Int(InventoryDao.EquipmentOf(db,target),"starLevel")==1,
                "星力后拾取持久化及旧快照不能退费或回退星数");
        }
        finally {GameServer.Stop();if(File.Exists(path))File.Delete(path);}
    }

}
