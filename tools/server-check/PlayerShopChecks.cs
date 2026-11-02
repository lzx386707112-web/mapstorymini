// 个人商店（PlayerShop，514xxxx 营业执照）回归：对照源端 PlayerShop.java / PlayerShopItem.java /
// PlayerInteractionHandler.java（CREATE 4/5、ADD_ITEM、REMOVE_ITEM、OPEN_STORE、EXIT）/
// Character.closePlayerShop()。
// 覆盖：非自由市场 / 无许可 / 重复开店、上架三类拒绝、营业中锁定、开张扣许可、
//       假人成交（手续费 + 直接进人物金币）、卖空自动关店、取回数量与列表前移、
//       关店退道具、背包满时拒绝关店、换图关店。
using System;
using System.Collections.Generic;
using System.IO;

static class PlayerShopChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 个人商店 PlayerShop ==");
        string path = Path.Combine(Path.GetTempPath(), "mapledash_playershop_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        NativePlayerShopService.Reset();
        try
        {
            var login = LoginService.Login(db, "playershoptest", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "ShopHero", CharFactory.JobGroupAdventurer,
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

            void Clear(int type)
            {
                var ids = new HashSet<long>();
                foreach (var r in db.Table("inventoryitems").Select(x => DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "inventorytype") == type))
                    ids.Add(DbStore.Long(r, "inventoryitemid"));
                db.Table("inventoryitems").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
                db.Table("inventoryequipment").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
                db.Save();
            }

            PlayerShopResponse Call(PlayerShopOperation operation, int slot = -1, int invType = 0, int invPos = 0,
                int perBundle = 0, int bundles = 0, int price = 0, int permit = 0, string description = "", string name = "")
                => NativePlayerShopService.Handle(db, new PlayerShopRequest {
                    characterId = cid, operation = operation, slot = slot, inventoryType = invType,
                    itemPosition = invPos, perBundle = perBundle, bundles = bundles, price = price,
                    permitId = permit, description = description, name = name, x = 0, y = 0 });

            SetConfig(db, "use_erase_permit_on_open_shop", "true");
            SetMeso(10000000);

            // ——— 开店门槛 ———
            SetMap(100000000);
            InventoryDao.InsertItem(db, cid, 5140000, InventoryDao.Cash, 1, 1);
            var wrongMap = Call(PlayerShopOperation.Create, permit: 5140000);
            Check(!wrongMap.ok && wrongMap.code == NativePlayerShopService.ErrorNotFreeMarket, "非自由市场不能开个人商店");

            SetMap(910000001);
            Clear(InventoryDao.Cash);
            var noPermit = Call(PlayerShopOperation.Create, permit: 5140000);
            Check(!noPermit.ok && noPermit.code == NativePlayerShopService.ErrorNoPermit, "没有营业执照不能开店");

            InventoryDao.InsertItem(db, cid, 5140000, InventoryDao.Cash, 1, 1);
            var created = Call(PlayerShopOperation.Create, permit: 5140000, description: "街边小摊");
            Check(created.ok && created.shop != null, "开店成功");
            Check(created.shop != null && !created.shop.open && created.shop.description == "街边小摊", "新店处于准备中并带店名");
            Check(InventoryDao.CountOf(db, cid, 5140000) == 1, "开店本身不消耗营业执照（源端只在开张时扣）");
            Check(!Call(PlayerShopOperation.Create, permit: 5140000).ok, "已有店铺不能重复开店");

            // ——— 上架校验 ———
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 1, 30);
            Check(!Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 1, 10, 4, 100).ok, "数量不足时拒绝上架");
            Check(!Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 1, 1, 1, 0).ok, "单价 0 拒绝上架");
            Check(!Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 1, 2000, 2, 100).ok, "每份×份数超过 2000 拒绝上架");

            var stocked = Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 1, 10, 3, 500);
            Check(stocked.ok, "上架成功");
            Check(stocked.shop != null && stocked.shop.items.Count == 1 && stocked.shop.items[0].bundles == 3
                  && stocked.shop.items[0].price == 500 && stocked.shop.items[0].item.quantity == 10,
                "货架记录每份数量/份数/单价");
            Check(CharService.Load(db, cid).inventory.Find(i => i.itemId == 4000000) == null, "上架的 30 个全部从背包扣走");

            // ——— 不可交易道具 ———
            InventoryDao.InsertItem(db, cid, 4000001, InventoryDao.Etc, 2, 5);
            db.Table("inventoryitems").Update(x => DbStore.Int(x, "characterid") == cid
                && DbStore.Int(x, "itemid") == 4000001, new DbRow { ["flag"] = 8L });
            db.Save();
            Check(!Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 2, 1, 1, 100).ok, "不可交易（flag 0x08）道具拒绝上架");

            // ——— 开张营业：扣 1 个执照 ———
            var opened = Call(PlayerShopOperation.Open);
            Check(opened.ok && opened.shop != null && opened.shop.open, "开张营业");
            Check(InventoryDao.CountOf(db, cid, 5140000) == 0, "use_erase_permit_on_open_shop 开张时扣掉营业执照");
            Check(!Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 2, 1, 1, 100).ok, "营业中不能上架");
            Check(!Call(PlayerShopOperation.TakeItemBack, 0).ok, "营业中不能取回");

            // ——— 假人成交：金币直接进人物身上，扣手续费 ———
            long beforeSale = Meso();
            long earned = NativePlayerShopService.BotBuy(db, cid, 0, 1, "假人甲");
            long expected = 500 - Server.Bot.Trade.BotTradeService.Fee(500);
            Check(earned == expected, "店主实收 = 单价 × 份数 - 手续费");
            Check(Meso() == beforeSale + expected, "成交金额直接进店主身上（不走 MerchantMesos）");
            Check(NativePlayerShopService.Handle(db, new PlayerShopRequest { characterId = cid, operation = PlayerShopOperation.Snapshot })
                .shop.items[0].bundles == 2, "成交后份数递减");

            // ——— 买空 → 自动关店 ———
            long again = NativePlayerShopService.BotBuy(db, cid, 0, 2, "假人乙");
            Check(again > 0, "假人买走剩下 2 份");
            Check(!NativePlayerShopService.Has(cid), "全部卖光自动关店（boughtNumber == items.size）");
            Check(NativePlayerShopService.BotBuy(db, cid, 0, 1, "假人丙") == 0, "关店后再买失败");

            // ——— 重新开店：取回数量与列表前移 ———
            NativePlayerShopService.Reset();
            InventoryDao.InsertItem(db, cid, 5140001, InventoryDao.Cash, 1, 1);
            Check(Call(PlayerShopOperation.Create, permit: 5140001).ok, "再开一家店");
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 3, 6);
            InventoryDao.InsertItem(db, cid, 4000003, InventoryDao.Etc, 4, 9);
            Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 3, 2, 2, 300);
            Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 4, 3, 3, 700);
            var snapshot = Call(PlayerShopOperation.Snapshot);
            Check(snapshot.shop.items.Count == 2, "两件分别在 0/1 号货架");
            var back = Call(PlayerShopOperation.TakeItemBack, 0);
            Check(back.ok, "取回成功");
            int returned = 0;
            foreach (var entry in CharService.Load(db, cid).inventory) if (entry.itemId == 4000002) returned += entry.quantity;
            Check(returned == 6, "取回数量 = 每份 2 × 份数 2（背包原有 2 个 + 取回 4 个）");
            var shifted = Call(PlayerShopOperation.Snapshot);
            Check(shifted.shop.items.Count == 1 && shifted.shop.items[0].item.itemId == 4000003,
                "取回后这一格从列表删除，后面的格子前移（源端 items.remove）");

            // ——— 关店把货退回背包 ———
            var closed = Call(PlayerShopOperation.Close);
            Check(closed.ok && closed.shop == null, "关店成功");
            Check(!NativePlayerShopService.Has(cid), "关店后内存店铺表里没有这条");
            int leftInBag = 0;
            foreach (var entry in CharService.Load(db, cid).inventory) if (entry.itemId == 4000003) leftInBag += entry.quantity;
            Check(leftInBag == 9, "关店把剩下的 9 个退回背包");

            // ——— 背包满时关店被拒，货留在店里 ———
            NativePlayerShopService.Reset();
            InventoryDao.InsertItem(db, cid, 5140002, InventoryDao.Cash, 1, 1);
            Call(PlayerShopOperation.Create, permit: 5140002);
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000004, InventoryDao.Etc, 1, 5);
            Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 1, 5, 1, 900);
            var actor = new Server.Bot.BotActor { id = cid, job = CharactersDao.FindById(db, cid).job, character = CharService.Load(db, cid) };
            int slotMax = new Server.Bot.Trade.TradeItemRules(db, Server.Bot.BotItemCatalog.Shared).SlotMax(4000004, actor);
            int etcSlots = CharactersDao.FindById(db, cid).etcslots;
            for (int i = 1; i <= etcSlots; i++) InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, i, slotMax);
            var refused = Call(PlayerShopOperation.Close);
            Check(!refused.ok && refused.error.Contains("留在店里"), "背包满时拒绝关店，提示货还在店里");
            Check(NativePlayerShopService.Has(cid), "被拒后店铺还在（不丢货）");

            // ——— 换图关店：货退回背包 ———
            Clear(InventoryDao.Etc);
            SetMap(910000002);
            var warped = Call(PlayerShopOperation.Snapshot);
            Check(warped.ok && warped.shop == null, "换图后店铺自动关闭");
            int warpedBack = 0;
            foreach (var entry in CharService.Load(db, cid).inventory) if (entry.itemId == 4000004) warpedBack += entry.quantity;
            Check(warpedBack == 5, "换图关店把货退回背包");

            // ——— 黑名单与聊天只留在店里 ———
            SetMap(910000003);
            InventoryDao.InsertItem(db, cid, 5140003, InventoryDao.Cash, 1, 1);
            Call(PlayerShopOperation.Create, permit: 5140003);
            var banned = Call(PlayerShopOperation.Ban, name: "坏人");
            Check(banned.ok && banned.shop != null && banned.shop.banned.Contains("坏人"), "拉黑写入店铺黑名单");
            Check(Call(PlayerShopOperation.Ban, name: "  ").error.Length > 0, "空名字不能拉黑");
            var chatted = NativePlayerShopService.Handle(db, new PlayerShopRequest {
                characterId = cid, operation = PlayerShopOperation.Chat, text = "欢迎光临" });
            Check(chatted.ok && chatted.shop != null && chatted.shop.chat.Count > 0, "店铺聊天留在店内");
            Call(PlayerShopOperation.Close);

            // ——— 掉线：Logout 把货退回背包 ———
            NativePlayerShopService.Reset();
            InventoryDao.InsertItem(db, cid, 5140004, InventoryDao.Cash, 1, 1);
            Call(PlayerShopOperation.Create, permit: 5140004);
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000005, InventoryDao.Etc, 1, 7);
            Call(PlayerShopOperation.AddItem, 0, InventoryDao.Etc, 1, 7, 1, 100);
            NativePlayerShopService.Logout(db, cid);
            Check(!NativePlayerShopService.Has(cid), "掉线后店铺消失（源端 PlayerShop 不落库）");
            int logoutBack = 0;
            foreach (var entry in CharService.Load(db, cid).inventory) if (entry.itemId == 4000005) logoutBack += entry.quantity;
            Check(logoutBack == 7, "掉线时把货退回背包");
        }
        finally
        {
            NativePlayerShopService.Reset();
            db.Save();
            File.Delete(path);
        }
    }

    static void SetConfig(DbStore db, string code, string value)
    {
        var table = db.Table("game_config");
        var row = table.Find(x => (string)x["config_code"] == code);
        if (row == null)
            table.Insert(new DbRow { ["config_type"] = "server", ["config_code"] = code, ["config_value"] = value });
        else row["config_value"] = value;
        db.Save();
    }
}
