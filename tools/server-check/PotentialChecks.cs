using System;
using System.IO;
using Server.Bot;

static class PotentialChecks
{
    public static void Run(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash_potential_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var db=DbStore.Create(path);
            var catalog=new BotItemCatalog("{\"names\":{},\"items\":{\"1302000\":{\"info\":{\"reqLevel\":150,\"tuc\":7}}}}");
            var roller=new PotentialRoller(new PotentialCatalog(File.ReadAllText("Assets/Resources/Enhancement/ItemOption.json")),
                new CubeRateTable(File.ReadAllText("Assets/Resources/Enhancement/CubeRates.json")));
            int cid=(int)db.Table("characters").Insert(new DbRow {["name"]="CubeTest",["meso"]=1000000L,["hp"]=100L});
            long target=InventoryDao.InsertItem(db,cid,1302000,1,1,1);
            InventoryDao.InsertEquipment(db,target,watk:100);
            var eq=InventoryDao.EquipmentOf(db,target);
            eq["potentialGrade"]=3L;eq["potentialOption1"]=30041L;eq["potentialOption2"]=20041L;
            long cube=InventoryDao.InsertItem(db,cid,5062030,5,1,2);
            PotentialResponse Send(bool commit,int revision,bool choose=false,bool keep=false)
                =>NativePotentialService.Handle(db,new PotentialRequest {characterId=cid,inventoryItemId=target,cubeInventoryItemId=cube,commit=commit,revision=revision,choose=choose,keepNew=keep},roller,catalog,new Random(17));
            var quote=Send(false,0);
            var staleCharacter=CharService.Load(db,cid);
            check(quote.ok&&quote.cost==0&&InventoryDao.CountOf(db,cid,5062030)==2,"现金主魔方免费预览不扣道具");
            var roll=Send(true,0);
            check(roll.ok&&roll.pending&&InventoryDao.CountOf(db,cid,5062030)==1,"比较魔方恰好扣一件并生成候选");
            check(DbStore.Int(eq,"potentialOption1")==30041&&DbStore.Int(eq,"watk")==100,"候选未选择前不改潜能与基础属性");
            check(!Send(true,0).ok&&InventoryDao.CountOf(db,cid,5062030)==1,"旧revision重复请求不二次扣物");
            check(!Send(true,1).ok,"待选结果阻止再次洗魔方");
            check(NativePotentialService.BlocksTransfer(db,CharService.Load(db,cid).inventory.Find(x=>x.itemRowId==target)),"比较目标禁止转移");
            db.Table("hiredmerchants").Insert(new DbRow {["ownerid"]=(long)cid,["open"]=0L});
            MerchantResponse Stock()=>NativeMerchantService.Handle(db,new MerchantRequest {
                characterId=cid,operation=MerchantOperation.AddItem,inventoryType=1,itemPosition=1,quantity=1,bundles=1,price=100});
            var rejectedStock=Stock();
            check(!rejectedStock.ok&&db.Table("merchantitems").rows.Count==0&&InventoryDao.EquipmentOf(db,target)!=null,
                "雇佣商店上架不能绕过魔方待选保护或复制候选装备");
            var npcShop=new NativeNpcShopService(db);
            var openedShop=npcShop.Handle(new NpcShopRequest {characterId=cid,operation=NpcShopOperation.OpenShop,id=11000});
            var sellPending=npcShop.Handle(new NpcShopRequest {characterId=cid,operation=NpcShopOperation.Sell,inventoryType=1,position=1,quantity=1});
            check(openedShop.ok&&!sellPending.ok&&sellPending.error.Contains("待选")&&InventoryDao.EquipmentOf(db,target)!=null
                &&CharactersDao.FindById(db,cid).meso==1000000,"NPC商店出售待选装备返回明确拒绝且不改变金币或装备");
            string choice=DbStore.Str(eq,"potentialChoice");
            eq["potentialChoice"]="";eq["starForceVestige"]=1L;
            check(!Stock().ok&&db.Table("merchantitems").rows.Count==0,"雇佣商店不能上架装备痕迹");
            var sellVestige=npcShop.Handle(new NpcShopRequest {characterId=cid,operation=NpcShopOperation.Sell,inventoryType=1,position=1,quantity=1});
            check(!sellVestige.ok&&sellVestige.error.Contains("痕迹")&&InventoryDao.EquipmentOf(db,target)!=null,"NPC商店不能出售装备痕迹");
            eq["potentialChoice"]=choice;eq["starForceVestige"]=0L;
            db.Table("hiredmerchants").Delete(x=>DbStore.Int(x,"ownerid")==cid);
            var character=CharactersDao.FindById(db,cid);
            check(!NativeInventorySort.Sort(db,character,1).ok&&!NativeInventorySort.Merge(db,character,1).ok,
                "待选装备禁止整理与合并且位置保持不变");
            check(!NativeInventoryService.Handle(db,new InventoryChangeRequest {characterId=cid,itemId=1302000,removeAll=true}).ok
                &&InventoryDao.EquipmentOf(db,target)!=null,"移除全部不能删除待选装备");
            // Keep this test's cube count/money current: it isolates stale equipment mirror persistence.
            var refreshed=CharService.Load(db,cid);
            check(db.Table("inventoryequipment").Col("enhancementVersion")!=null,
                "测试使用含282强化列的当前数据库结构");
            refreshed.inventory.Find(x=>x.itemRowId==target).equipmentData=staleCharacter.inventory.Find(x=>x.itemRowId==target).equipmentData;
            new Server.Bot.Trade.BotTradeService(db,catalog).Persist(new BotActor {id=cid,character=refreshed});
            db.Save();eq=InventoryDao.EquipmentOf(db,target);
            check(NativePotentialService.IsReserved(db,target)&&DbStore.Int(eq,"potentialRevision")==1,
                "旧装备镜像持久化不能覆盖权威候选及revision：version="+DbStore.Int(eq,"enhancementVersion")+", revision="+DbStore.Int(eq,"potentialRevision")+", pending="+NativePotentialService.IsReserved(db,target));
            check(!NativeScrollService.Apply(db,new EquipPatchRequest {characterId=cid,inventoryItemId=target,scrollId=2040000}).ok
                &&!NativeCashEquipmentService.Apply(db,new EquipPatchRequest {characterId=cid,inventoryItemId=target,cashItemId=5570000}).ok,
                "直接调用卷轴与金锤服务也不能修改待选装备");
            db=DbStore.Open(path);eq=InventoryDao.EquipmentOf(db,target);
            var pending=Send(false,1,true);
            check(pending.ok&&pending.pending&&pending.candidate[0]==roll.candidate[0],"候选重启后仍可恢复");
            var picked=Send(true,1,true,true);
            check(picked.ok&&!picked.pending&&DbStore.Int(eq,"potentialOption1")==roll.candidate[0],"选择新结果后写入精确候选");
            check(!NativePotentialService.IsReserved(db,target)&&InventoryDao.CountOf(db,cid,5062030)==1,"选择解除预留且不额外扣物");
            check(!Send(true,1,true,true).ok,"重复确认已选候选不改状态");
            var second=Send(true,2);
            int before=DbStore.Int(eq,"potentialOption1");
            var old=Send(true,3,true,false);
            check(second.ok&&old.ok&&DbStore.Int(eq,"potentialOption1")==before,"保留旧结果仍消耗本次魔方且不覆盖旧潜能");
            eq["bonusGrade"]=2L;eq["bonusOption1"]=22041L;
            long additional=InventoryDao.InsertItem(db,cid,5062031,5,2,1);
            var request=new PotentialRequest {characterId=cid,inventoryItemId=target,cubeInventoryItemId=additional,revision=4,commit=true};
            var add=NativePotentialService.Handle(db,request,roller,catalog,new Random(1));
            check(add.ok&&add.cost==450000&&add.meso==550000,"附加魔方按装备等级扣鉴定费");
            check(DbStore.Int(eq,"potentialOption1")==before&&DbStore.Int(eq,"bonusOption1")==22041,"附加候选保留原主附潜能直到选择");
            check(!NativePotentialService.Handle(db,new PotentialRequest {characterId=cid+1,inventoryItemId=target},roller,catalog).ok,"潜能接口校验装备归属");
        }
        finally {if(File.Exists(path))File.Delete(path);}
        CheckTransportMirror(check);
        CheckScrollRates(check);
    }
    sealed class ScrollDraw : Random
    {
        readonly int draw;bool first=true;
        public ScrollDraw(int draw){this.draw=draw;}
        public override int Next(int maximum){if(first){first=false;return draw;}return 0;}
    }
    static void CheckScrollRates(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash_potential_scroll_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var db=DbStore.Create(path);
            var catalog=new BotItemCatalog("{\"names\":{},\"items\":{\"1302000\":{\"info\":{\"reqLevel\":150,\"tuc\":7}}}}");
            var roller=new PotentialRoller(new PotentialCatalog(File.ReadAllText("Assets/Resources/Enhancement/ItemOption.json")),
                new CubeRateTable(File.ReadAllText("Assets/Resources/Enhancement/CubeRates.json")));
            int cid=(int)db.Table("characters").Insert(new DbRow {["name"]="ScrollTest",["meso"]=1000000L});
            long target=InventoryDao.InsertItem(db,cid,1302000,1,1,1);InventoryDao.InsertEquipment(db,target,watk:100);
            var eq=InventoryDao.EquipmentOf(db,target);
            foreach(int id in new[]{2049400,2049401})
            {
                long scroll=InventoryDao.InsertItem(db,cid,id,2,1,100);
                var request=new PotentialRequest {characterId=cid,inventoryItemId=target,cubeInventoryItemId=scroll};
                check(NativePotentialService.Handle(db,request,roller,catalog).ok&&InventoryDao.CountOf(db,cid,id)==100,"潜能卷轴预览不扣物："+id);
                int successes=0;bool exact=true;
                for(int draw=0;draw<100;draw++)
                {
                    eq["potentialGrade"]=0L;for(int i=1;i<=3;i++)eq["potentialOption"+i]=0L;
                    request.commit=true;request.revision=DbStore.Int(eq,"potentialRevision");
                    var result=NativePotentialService.Handle(db,request,roller,catalog,new ScrollDraw(draw));
                    if(result.ok&&result.scrollResult=="success")successes++;
                    exact&=result.ok&&DbStore.Int(eq,"watk")==100&&CharactersDao.FindById(db,cid).meso==1000000
                        &&InventoryDao.CountOf(db,cid,id)==99-draw
                        &&(result.scrollResult=="success"?DbStore.Int(eq,"potentialOption1")>0:DbStore.Int(eq,"potentialOption1")==0);
                    if(draw==0)
                        check(!NativePotentialService.Handle(db,request,roller,catalog,new ScrollDraw(0)).ok&&InventoryDao.CountOf(db,cid,id)==99,
                            "潜能卷轴重复revision不二次扣物："+id);
                }
                check(successes==(id==2049400?90:70)&&exact,"遍历100种抽签："+id+"精确成功"+successes+"次，成功失败均只扣一张且保留基础属性和金币");
            }
        }
        finally {if(File.Exists(path))File.Delete(path);}
    }
    static void CheckTransportMirror(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),"mapledash_cube_mirror_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var db=DbStore.Create(path);
            int cid=(int)db.Table("characters").Insert(new DbRow {["name"]="CubeMirror",["meso"]=1000000L});
            long target=InventoryDao.InsertItem(db,cid,1302134,1,1,1);
            InventoryDao.InsertEquipment(db,target,watk:100);
            var eq=InventoryDao.EquipmentOf(db,target);eq["bonusGrade"]=2L;eq["bonusOption1"]=22041L;
            long cube=InventoryDao.InsertItem(db,cid,5062031,5,1,2);
            var server=GameServer.StartWith(db);
            var player=server.Bots.SynchronizePlayer(CharService.Load(db,cid),910000000,0,0);
            var stale=CharService.Load(db,cid);
            var response=server.Transport.Potential(new PotentialRequest {
                characterId=cid,inventoryItemId=target,cubeInventoryItemId=cube,revision=0,commit=true});
            check(response.ok&&response.cost==450000&&player.character.record.meso==550000
                &&player.character.inventory.Find(x=>x.itemRowId==cube).quantity==1&&player.character.tradeRevision==1,
                "正式潜能接口同步在线金币与魔方镜像并推进快照修订");
            new Server.Bot.Trade.BotTradeService(db,BotItemCatalog.Shared).Persist(player);
            check(InventoryDao.CountOf(db,cid,5062031)==1&&CharactersDao.FindById(db,cid).meso==550000,
                "强化后拾取持久化不会补回已消耗魔方或金币");
            stale.record.exp=123;
            server.Transport.SaveCharacter(new SaveCharacterRequest {characterId=cid,character=stale});
            check(InventoryDao.CountOf(db,cid,5062031)==1&&CharactersDao.FindById(db,cid).meso==550000
                &&CharactersDao.FindById(db,cid).exp==123&&NativePotentialService.IsReserved(db,target),
                "强化前客户端存档不能退还消耗且保留正常经验进度与待选结果");
        }
        finally {GameServer.Stop();if(File.Exists(path))File.Delete(path);}
    }
}
