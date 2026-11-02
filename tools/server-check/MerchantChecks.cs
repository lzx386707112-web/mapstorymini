// 雇佣商店回归：对照源端 HiredMerchant.java / PlayerInteractionHandler.java / FredrickProcessor.java。
// 覆盖开店→上架→营业→假人成交→提金币→停业取回→关店寄存→Fredrick 取回→过期整条链路。
// 假人市场的镜像（MarketShopService）不在本文件里跑：那条路径需要整份 BotRuntime。
using System;
using System.Collections.Generic;
using System.IO;

static class MerchantChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 雇佣商店 HiredMerchant ==");
        string path = Path.Combine(Path.GetTempPath(), "mapledash_merchant_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "merchanttest", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "MerchantHero", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            Check(made.code == 0, "建号成功");
            int cid = made.characterId;

            void SetMap(int map)
            {
                var rec = CharactersDao.FindById(db, cid);
                rec.map = map;
                CharactersDao.Save(db, rec);
            }
            void SetMeso(long meso)
            {
                var rec = CharactersDao.FindById(db, cid);
                rec.meso = meso;
                CharactersDao.Save(db, rec);
            }
            long Meso() => CharactersDao.FindById(db, cid).meso;
            long MerchantMeso() => DbStore.Long(db.Table("characters").Find(x => DbStore.Int(x, "id") == cid), "MerchantMesos", 0);
            bool HasFlag() => DbStore.Int(db.Table("characters").Find(x => DbStore.Int(x, "id") == cid), "HasMerchant") != 0;

            void Clear(int type)
            {
                var ids = new HashSet<long>();
                foreach (var r in db.Table("inventoryitems").Select(x => DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "inventorytype") == type))
                    ids.Add(DbStore.Long(r, "inventoryitemid"));
                db.Table("inventoryitems").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
                db.Table("inventoryequipment").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
                db.Save();
            }

            void ResetShop()
            {
                foreach (var row in db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == cid))
                {
                    long payload = DbStore.Long(row, "inventoryitemid");
                    db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == payload);
                    db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == payload);
                }
                db.Table("merchantitems").Delete(x => DbStore.Int(x, "ownerid") == cid);
                db.Table("hiredmerchants").Delete(x => DbStore.Int(x, "ownerid") == cid);
                db.Table("fredstorage").Delete(x => DbStore.Int(x, "cid") == cid);
                var rec = CharactersDao.FindById(db, cid);
                rec.HasMerchant = 0;
                rec.MerchantMesos = 0;
                CharactersDao.Save(db, rec);
                db.Save();
            }

            MerchantResponse Call(MerchantOperation operation, int slot = -1, int quantity = 0, int bundles = 0, int price = 0,
                int invType = 0, int invPos = 0, string description = "")
                => NativeMerchantService.Handle(db, new MerchantRequest {
                    characterId = cid, operation = operation, slot = slot, quantity = quantity, bundles = bundles,
                    price = price, inventoryType = invType, itemPosition = invPos, description = description });

            SetMeso(10000000);
            // 1. 非自由市场不能开店
            SetMap(100000000);
            InventoryDao.InsertItem(db, cid, 5030000, InventoryDao.Cash, 1, 1);
            var wrongMap = Call(MerchantOperation.Create, price: 5030000);
            Check(!wrongMap.ok && wrongMap.code == NativeMerchantService.ErrorNotFreeMarket, "非自由市场不能开雇佣商店");

            // 2. 自由市场 + 无许可
            SetMap(910000001);
            Clear(InventoryDao.Cash);
            var noPermit = Call(MerchantOperation.Create, price: 5030000);
            Check(!noPermit.ok && noPermit.code == NativeMerchantService.ErrorNoPermit, "没有许可道具不能开店");

            // 3. 商店盒：无店无寄存 → 可开盒
            var box = Call(MerchantOperation.Box);
            Check(box.ok && box.box, "自由市场商店盒可打开");

            // 4. 开店
            InventoryDao.InsertItem(db, cid, 5030000, InventoryDao.Cash, 1, 1);
            var created = Call(MerchantOperation.Create, price: 5030000, description: "测试店");
            Check(created.ok && created.shop != null, "开店成功");
            Check(HasFlag(), "开店后 HasMerchant=1");
            Check(created.shop != null && !created.shop.open && created.shop.description == "测试店", "新店处于准备中并带描述");
            Check(InventoryDao.CountOf(db, cid, 5030000) == 1, "许可道具不被消耗（源端只校验持有）");

            // 5. 有店时再开 / 开盒
            Check(!Call(MerchantOperation.Create, price: 5030000).ok, "已有店铺不能重复开店");
            Check(!Call(MerchantOperation.Box).ok, "已有店铺时商店盒不再打开");

            // 6. 上架：参数与数量校验
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 1, 30);
            Check(!Call(MerchantOperation.AddItem, 0, 10, 4, 100, InventoryDao.Etc, 1).ok, "数量不足时拒绝上架");
            Check(!Call(MerchantOperation.AddItem, 0, 1, 1, 0, InventoryDao.Etc, 1).ok, "单价 0 拒绝上架");
            Check(!Call(MerchantOperation.AddItem, 0, 2000, 2, 100, InventoryDao.Etc, 1).ok, "每份×份数超过 2000 拒绝上架");

            var stocked = Call(MerchantOperation.AddItem, 0, 10, 3, 500, InventoryDao.Etc, 1);
            Check(stocked.ok, "上架成功");
            Check(stocked.shop != null && stocked.shop.items.Count == 1 && stocked.shop.items[0].bundles == 3
                  && stocked.shop.items[0].price == 500 && stocked.shop.items[0].item.quantity == 10,
                "货架记录每份数量/份数/单价");
            var left = CharService.Load(db, cid).inventory.Find(i => i.itemId == 4000000);
            Check(left == null, "上架的 30 个全部从背包扣走");

            // 7. 开始营业 → 营业中不能上架、不能取回
            var opened = Call(MerchantOperation.MaintenanceOff);
            Check(opened.ok && opened.shop.open, "开始营业");
            Check(!Call(MerchantOperation.AddItem, 0, 1, 1, 100, InventoryDao.Etc, 1).ok, "营业中不能上架");
            Check(!Call(MerchantOperation.TakeItemBack, 0).ok, "营业中不能取回");

            // 8. 假人成交：扣手续费进 MerchantMesos，卖空删格
            Check(NativeMerchantService.BotPurchase(db, cid, 0, 1, "假人甲", out int price1, out _), "假人买走 1 份");
            Check(price1 == 500, "成交价 = 单价 × 份数");
            long expected = 500 - Server.Bot.Trade.BotTradeService.Fee(500);
            Check(MerchantMeso() == expected, "店主收入 = 成交价 - 手续费");
            Check(NativeMerchantService.BotPurchase(db, cid, 0, 2, "假人乙", out _, out _), "假人买走剩下 2 份");
            Check(NativeMerchantService.Load(db, cid).items.Count == 0, "卖空的格子从货架删除");
            Check(!NativeMerchantService.BotPurchase(db, cid, 0, 1, "假人丙", out _, out _), "卖空后再买失败");

            // 9. 提金币
            long beforeCash = Meso();
            var withdrawn = Call(MerchantOperation.WithdrawMesos);
            Check(withdrawn.ok && Meso() == beforeCash + MerchantMeso() + 0 || MerchantMeso() == 0, "提金币后商店金币清零");
            Check(MerchantMeso() == 0 && Meso() > beforeCash, "金币进入人物身上");

            // 10. 上架一件后停业再取回
            InventoryDao.InsertItem(db, cid, 4000001, InventoryDao.Etc, 2, 6);
            Call(MerchantOperation.SetOpen, 0, 0);
            Check(Call(MerchantOperation.AddItem, 0, 2, 2, 300, InventoryDao.Etc, 2).ok, "停业后可以上架");
            var slot = NativeMerchantService.Load(db, cid).items[0];
            var back = Call(MerchantOperation.TakeItemBack, slot.slot);
            Check(back.ok, "取回成功");
            int returnedTotal = 0;
            foreach (var entry in CharService.Load(db, cid).inventory) if (entry.itemId == 4000001) returnedTotal += entry.quantity;
            Check(returnedTotal == 6, "取回数量 = 每份 × 份数（2×2 加上剩余的 2）");

            // 11. 货架空了以后「开始营业」直接关店（MAINTENANCE_OFF 的空店分支）
            var closed = Call(MerchantOperation.MaintenanceOff);
            Check(closed.ok, "空店开始营业 → 直接关店");
            Check(!HasFlag(), "关店后 HasMerchant=0");
            Check(db.Table("hiredmerchants").Find(x => DbStore.Int(x, "ownerid") == cid) == null, "关店后店铺行删除");

            // 12. 关店时装不下 → 留在 Fredrick 寄存
            ResetShop();
            InventoryDao.InsertItem(db, cid, 5030000, InventoryDao.Cash, 1, 1);
            Call(MerchantOperation.Create, price: 5030000);
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 3, 5);
            Call(MerchantOperation.AddItem, 0, 5, 1, 700, InventoryDao.Etc, 3);
            // 把 Etc 塞满，关店就退不回来
            var actor = new Server.Bot.BotActor { id = cid, job = CharactersDao.FindById(db, cid).job, character = CharService.Load(db, cid) };
            int slotMax = new Server.Bot.Trade.TradeItemRules(db, Server.Bot.BotItemCatalog.Shared).SlotMax(4000000, actor);
            int etcSlots = CharactersDao.FindById(db, cid).etcslots;
            for (int i = 1; i <= etcSlots; i++)
                InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, i, slotMax);
            Call(MerchantOperation.MaintenanceOff);
            NativeMerchantService.BotPurchase(db, cid, 0, 0, "假人丁", out _, out _);   // 先卖出一份制造金币
            long storedMeso = MerchantMeso();
            var closedFull = Call(MerchantOperation.Close);
            Check(closedFull.ok, "背包满也能关店");
            Check(db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == cid).Count > 0, "装不下的货留在寄存");
            Check(db.Table("fredstorage").Find(x => DbStore.Int(x, "cid") == cid) != null, "寄存登记 fredstorage");
            Check(MerchantMeso() == storedMeso, "寄存期间商店金币保留");

            // 13. 商店盒此时应提示去 Fredrick
            var box2 = Call(MerchantOperation.Box);
            Check(box2.ok && !box2.box && !string.IsNullOrEmpty(box2.error), "有寄存时商店盒提示去弗利德里");

            // 14. 清空背包后 Fredrick 取回
            Clear(InventoryDao.Etc);
            var retrieved = Call(MerchantOperation.Retrieve);
            Check(retrieved.ok && retrieved.code == NativeMerchantService.FredrickSuccess, "Fredrick 取回成功");
            Check(db.Table("fredstorage").Find(x => DbStore.Int(x, "cid") == cid) == null, "取回后删除寄存登记");
            Check(MerchantMeso() == 0, "取回后商店金币清零");
            var gotBack = CharService.Load(db, cid).inventory.Find(i => i.itemId == 4000002);
            Check(gotBack != null && gotBack.quantity == 5, "寄存的道具回到背包");

            // 15. 寄存越久 Fredrick 抽成越高（每秒 1%，最多 100 天）
            ResetShop();
            InventoryDao.InsertItem(db, cid, 5030000, InventoryDao.Cash, 1, 1);
            Call(MerchantOperation.Create, price: 5030000);
            db.Table("characters").Update(x => DbStore.Int(x, "id") == cid, new DbRow { ["MerchantMesos"] = 1000L });
            db.Table("fredstorage").Insert(new DbRow { ["cid"] = (long)cid, ["daynotes"] = 0L,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 10L * 24 * 60 * 60 * 1000 });
            db.Save();
            long cashBefore = Meso();
            Call(MerchantOperation.WithdrawMesos);
            Check(Meso() == cashBefore + 900, "寄存 10 天后只提得出 90%（每天 1%）");

            // 16. 过期：hired_merchants_exist_time=1 天
            ResetShop();
            InventoryDao.InsertItem(db, cid, 5030000, InventoryDao.Cash, 1, 1);
            Call(MerchantOperation.Create, price: 5030000);
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 4, 1);
            Call(MerchantOperation.AddItem, 0, 1, 1, 100, InventoryDao.Etc, 4);
            db.Table("hiredmerchants").Update(x => DbStore.Int(x, "ownerid") == cid,
                new DbRow { ["starttime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 2L * 24 * 60 * 60 * 1000 });
            db.Save();
            int expired = NativeMerchantService.Expire(db, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Check(expired >= 1, "超过 hired_merchants_exist_time 天强制关店");
            Check(db.Table("hiredmerchants").Find(x => DbStore.Int(x, "ownerid") == cid) == null, "过期店铺行被删除");
        }
        finally
        {
            db.Save();
            File.Delete(path);
        }
    }
}
