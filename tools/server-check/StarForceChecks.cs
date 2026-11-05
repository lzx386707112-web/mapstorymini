using System;
using System.IO;
using Server.Bot;

static class StarForceChecks
{
    sealed class Draw : Random { readonly int value; public Draw(int value) {this.value=value;} public override int Next(int max) => value; }
    public static void Run(Action<bool,string> check)
    {
        var starterStats = new System.Collections.Generic.Dictionary<string,int>{{"watk",17},{"matk",0}};
        var starter = StarForceStats.Calculate(1302000,0,5,false,starterStats);
        check(starter["watk"]==5 && starter["matk"]==0 && starterStats["watk"]==17,
            "新手武器满五星累计攻击+5，不增加零魔法力且不修改基础值");
        var staff = StarForceStats.Calculate(1372005,0,5,false,
            new System.Collections.Generic.Dictionary<string,int>{{"watk",13},{"matk",23}});
        check(staff["watk"]==5 && staff["matk"]==5,"低攻击法杖的非零物攻及魔攻每星至少+1");
        var normal = StarForceStats.Calculate(1302000,150,15,false,
            new System.Collections.Generic.Dictionary<string,int>{{"watk",100}});
        check(normal["watk"]==30,"原本每星至少+1的武器保留原2%截断规则");
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
            var refreshed=success.inventory.Find(x=>x.itemRowId==id);
            var beforeEntry=drop.inventory.Find(x=>x.itemRowId==id);
            var displayedBefore=StarForceStats.Calculate(beforeEntry.itemId,150,beforeEntry.starLevel,false,
                new System.Collections.Generic.Dictionary<string,int>{{"str",beforeEntry.str},{"watk",beforeEntry.watk}});
            var displayedAfter=StarForceStats.Calculate(refreshed.itemId,150,refreshed.starLevel,false,
                new System.Collections.Generic.Dictionary<string,int>{{"str",refreshed.str},{"watk",refreshed.watk}});
            check(refreshed.starLevel==17 && refreshed.watk==100 && refreshed.str==10 &&
                Convert.ToInt32(refreshed.equipmentData["enhancementVersion"])==282 &&
                displayedAfter["watk"]==success.derivedStats["watk"] &&
                refreshed.watk+displayedAfter["watk"]>beforeEntry.watk+displayedBefore["watk"],
                "成功回包保留基础值，详情用新星级派生攻击并与正式结算一致");
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
        CheckMissingInstances(check);
        CheckLevelCap(check);
        CheckEquipmentTypes(check);
        CheckTransportMirror(check);
    }
    // 282 ItemConstants.isUpgradable 是 BodyPart 白名单，不是"只有武器"。
    static void CheckEquipmentTypes(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash_sf_types_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var db=DbStore.Create(path);
            var rules=new StarForceRules(File.ReadAllText("Assets/Resources/Enhancement/StarForce.json"));
            var catalog=new BotItemCatalog("{\"names\":{},\"items\":{"
                +"\"1003561\":{\"info\":{\"reqLevel\":100,\"tuc\":7,\"incSTR\":10,\"incPDD\":100}},"
                +"\"1050000\":{\"info\":{\"reqLevel\":35,\"tuc\":10,\"incSTR\":5,\"incPDD\":50}},"
                +"\"1122000\":{\"info\":{\"reqLevel\":120,\"tuc\":3,\"incSTR\":15,\"incMDD\":80}},"
                +"\"1132000\":{\"info\":{\"reqLevel\":25,\"tuc\":3,\"incSTR\":3,\"incPDD\":10}},"
                +"\"1152052\":{\"info\":{\"reqLevel\":15,\"tuc\":2,\"incSTR\":2,\"incPDD\":5}},"
                +"\"1182016\":{\"info\":{\"reqLevel\":10,\"tuc\":1,\"incPAD\":5,\"incPDD\":10}},"
                +"\"1672001\":{\"info\":{\"reqLevel\":30,\"tuc\":2,\"incMHP\":50,\"incPAD\":5}},"
                +"\"1302000\":{\"info\":{\"reqLevel\":150,\"tuc\":7,\"incPAD\":100}},"
                +"\"1142000\":{\"info\":{\"reqLevel\":10,\"tuc\":3,\"incSTR\":5}},"
                +"\"1190000\":{\"info\":{\"reqLevel\":10,\"tuc\":3,\"incPAD\":5}},"
                +"\"1162000\":{\"info\":{\"reqLevel\":10,\"tuc\":3,\"incSTR\":5}},"
                +"\"1352000\":{\"info\":{\"reqLevel\":10,\"tuc\":3,\"incPAD\":5}}"
                +"}}");
            int cid=(int)db.Table("characters").Insert(new DbRow {["name"]="SfTypes",["meso"]=1000000000L,["hp"]=100L,["equipslots"]=24L});
            int slot=10;
            StarForceResponse Preview(int itemId)
            {
                long id=InventoryDao.InsertItem(db,cid,itemId,1,slot++,1);
                InventoryDao.InsertEquipment(db,id,str:5,watk:5);
                return NativeStarForceService.Handle(db,new StarForceRequest {characterId=cid,inventoryItemId=id,commit=false},rules,catalog,new Draw(0));
            }
            foreach(int itemId in new[]{1003561,1050000,1122000,1132000,1152052,1182016,1672001,1302000})
            {
                var response=Preview(itemId);
                check(response.ok&&response.cap==rules.Cap(itemId,BotItemCatalog.Int(catalog.Info(itemId),"reqLevel"),false),
                    "282 可强化类型放行："+itemId+"（上限随等级档位）");
            }
            foreach(int itemId in new[]{1142000,1190000,1162000,1352000})
            {
                var response=Preview(itemId);
                check(!response.ok&&response.error=="这件装备不能进行星力强化",
                    "282 排除类型仍拒绝："+itemId);
            }
            // 出厂道具表（Assets/Resources/Bot/Items.json）里的真实防具模板也要能过闸：
            // 帽子/套服/腰带在 282 各有独立 BodyPart，tuc>0 才允许强化。
            string shipped=PlatformRes.LoadText("Bot/Items");
            if(!string.IsNullOrEmpty(shipped))
            {
                var full=new BotItemCatalog(shipped);
                foreach(int itemId in new[]{1003561,1050000,1132000})
                {
                    long id=InventoryDao.InsertItem(db,cid,itemId,1,slot++,1);
                    InventoryDao.InsertEquipment(db,id,str:5,watk:5);
                    var response=NativeStarForceService.Handle(db,new StarForceRequest {characterId=cid,inventoryItemId=id,commit=false},rules,full,new Draw(0));
                    check(response.ok&&response.cap>0,"出厂防具模板可强化："+itemId);
                }
            }
        }
        finally {if(File.Exists(path))File.Delete(path);}
    }
    static void CheckMissingInstances(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash_sf_missing_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var db=DbStore.Create(path);
            int cid=(int)db.Table("characters").Insert(new DbRow {["name"]="SfMissing",["meso"]=1000000000L,["hp"]=100L,["equipslots"]=24L});
            var rules=new StarForceRules(File.ReadAllText("Assets/Resources/Enhancement/StarForce.json"));
            var catalog=new BotItemCatalog("{\"names\":{},\"items\":{\"1302000\":{\"info\":{\"reqLevel\":150,\"tuc\":7,\"incSTR\":10,\"incPAD\":100}}}}");
            long id=InventoryDao.InsertItem(db,cid,1302000,1,1,1);
            var preview=NativeStarForceService.Handle(db,new StarForceRequest {characterId=cid,inventoryItemId=id},rules,catalog);
            var eq=InventoryDao.EquipmentOf(db,id);
            check(preview.ok && eq!=null && DbStore.Int(eq,"watk")==100 && DbStore.Int(eq,"upgradeslots")==7 &&
                CharactersDao.FindById(db,cid).meso==1000000000L,
                "缺属性行装备按模板补齐原ID实例，预览不扣款");
            var reopened=DbStore.Open(path);
            check(DbStore.Int(InventoryDao.EquipmentOf(reopened,id),"watk")==100,
                "实例修复独立持久化，不需要先强化成功");
            var success=NativeStarForceService.Handle(db,new StarForceRequest {characterId=cid,inventoryItemId=id,commit=true},rules,catalog,new Draw(0));
            check(success.ok && success.result.stars==1 && DbStore.Int(eq,"watk")==100 && success.derivedStats["watk"]>0,
                "补齐实例可直接强化，星力加成不烘焙基础属性");
            eq["watk"]=777L;eq["upgradeslots"]=2L;
            InventoryDao.EnsureEquipment(db,id,catalog);
            CharService.Load(db,cid);
            check(DbStore.Int(eq,"watk")==777 && DbStore.Int(eq,"upgradeslots")==2 && DbStore.Int(eq,"starLevel")==1,
                "已有自定义属性、卷位、星数不被读档或修复覆盖");
            long legacy=InventoryDao.InsertItem(db,cid,1002000,1,2,1);
            var loaded=CharService.Load(db,cid);
            check(InventoryDao.EquipmentOf(db,legacy)!=null && loaded.inventory.Find(x=>x.itemRowId==legacy)?.equipmentData!=null,
                "读档补齐旧新手和发放装备的属性行并同步背包");
            long added=CharService.AddItem(db,cid,1002000,1,3,1);
            check(InventoryDao.EquipmentOf(db,added)!=null,
                "通用新增装备即创建实例，不再依赖可选equipStats标记");
        }
        finally {if(File.Exists(path))File.Delete(path);}
    }
    static void CheckLevelCap(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash_sf_cap_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var db=DbStore.Create(path);
            var rules=new StarForceRules(File.ReadAllText("Assets/Resources/Enhancement/StarForce.json"));
            var catalog=new BotItemCatalog("{\"names\":{},\"items\":{\"1302000\":{\"info\":{\"reqLevel\":94,\"tuc\":7,\"incPAD\":10}}}}");
            int cid=(int)db.Table("characters").Insert(new DbRow {["name"]="SfCap",["meso"]=1000000000L,["hp"]=100L,["equipslots"]=24L});
            long id=InventoryDao.InsertItem(db,cid,1302000,1,1,1);
            InventoryDao.InsertEquipment(db,id,watk:10);
            var eq=InventoryDao.EquipmentOf(db,id);
            StarForceResponse Send(bool commit,int revision=0) => NativeStarForceService.Handle(db,
                new StarForceRequest {characterId=cid,inventoryItemId=id,commit=commit,revision=revision},rules,catalog,new Draw(0));
            int pristineCap=rules.Cap(1302000,94,false);
            var first=Send(true);
            check(first.ok && first.result.stars==1 && DbStore.Int(eq,"maxStar")==pristineCap && pristineCap==5,
                "94级新装备首星前后均为5个星位，不再跳到30");
            eq["starLevel"]=4L;
            var last=Send(true,1);
            long money=last.meso;
            check(last.ok && last.result.stars==5 && DbStore.Int(eq,"maxStar")==5,
                "低等级装备最后一次合法强化到5星");
            check(!Send(false,2).ok && !Send(true,2).ok && CharactersDao.FindById(db,cid).meso==money && DbStore.Int(eq,"starLevel")==5,
                "达到等级上限后预览及直接提交均拒绝且不扣款");
            eq["iIncReq"]=1L;
            var raised=Send(true,2);
            check(raised.ok && raised.result.stars==6 && DbStore.Int(eq,"maxStar")==8,
                "94加iIncReq1按95级分档，显示和结算统一8星上限");
        }
        finally {if(File.Exists(path))File.Delete(path);}
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
