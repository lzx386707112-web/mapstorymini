// 心脏卷轴回归：282 的 2616004~2616010 / 2643008 / 2643009~2643010 砸心脏。
// 覆盖：数据落地（info/req/reqScope）、适用范围（req 白名单 + reqScope 段口径）、
// 只能砸心脏、tuc 消耗与 level 递增、100% 成功、卷轴消耗。
using System;
using System.IO;

static class ScrollChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 心脏卷轴 ==");
        var catalog = Server.Bot.BotItemCatalog.Shared;

        // —— 数据层：10 张卷都随 0261.img / 0264.img 进来了 ——
        int[] scrolls = { 2616004, 2616005, 2616006, 2616007, 2616008, 2616009, 2616010, 2643008, 2643009, 2643010 };
        int missing = 0;
        foreach (int id in scrolls)
            if (catalog.Info(id) == null || string.IsNullOrEmpty(catalog.Name(id))) missing++;
        Check(missing == 0, "10 张心脏卷都有 info 与名字，缺 " + missing);

        Check(catalog.Name(2643009) == "卡勒馬特殊心臟攻擊力卷軸 100%", "2643009 名字来自 282 String");
        Check(catalog.Name(2616009) == "未來心臟專用攻擊力卷軸", "2616009 名字来自 282 String");
        Check(Server.Bot.BotItemCatalog.Int(catalog.Info(2643009), "success") == 100, "2643009 成功率 100");
        Check(Server.Bot.BotItemCatalog.Int(catalog.Info(2616010), "incMAD") == 10, "2616010 魔力 +10");
        Check(Server.Bot.BotItemCatalog.Int(catalog.Info(2643009), "incPAD") == 9, "2643009 攻击 +9");

        var futureReq = catalog.RequiredEquips(2616009);
        Check(futureReq != null && futureReq.Length == 6 && Array.IndexOf(futureReq, 1672045) >= 0,
            "2616009 的 req 是未來心臟六款");
        Check(Array.IndexOf(futureReq ?? Array.Empty<int>(), 1672003) < 0, "金之心不在未來心臟 req 里");
        Check(catalog.RequiredEquips(2643009) == null, "2643009 没有 req（走 reqScope）");
        Check(catalog.ScopedTo(2643009, 1672003) && catalog.ScopedTo(2643009, 1672069),
            "2643009 的 reqScope 覆盖整段 167xxxx");

        string path = Path.Combine(Path.GetTempPath(), "mapledash_scroll_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "scroll_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "ScrollGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            // 未來心臟专用卷：能砸 1672045（水晶未來），砸不上金之心（req 白名单）
            long heart = Wear(db, cid, 1672045, Check);
            long scroll = InventoryDao.InsertItem(db, cid, 2616009, 2, 1, 1);
            var ok = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = heart, scrollId = 2616009, scrollPosition = 1 }, new Random(7));
            Check(ok.ok && ok.scrollResult == "success", "2616009 砸水晶未來心臟成功" + (ok.ok ? "" : "（" + ok.error + "）"));
            var eq = InventoryDao.EquipmentOf(db, heart);
            Check(DbStore.Int(eq, "upgradeslots") == 5, "tuc 6 → 5，实际 " + DbStore.Int(eq, "upgradeslots"));
            Check(DbStore.Int(eq, "level") == 1, "砸卷次数 level 0 → 1，实际 " + DbStore.Int(eq, "level"));
            Check(DbStore.Int(eq, "watk") == 10, "攻击力 +10，实际 " + DbStore.Int(eq, "watk"));
            Check(RowCount(db, cid, 2616009) == 0, "卷轴被消耗");

            long gold = Wear(db, cid, 1672003, Check);
            var goldRow = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == gold);
            Check(goldRow != null && DbStore.Int(goldRow, "position") < 0,
                "金之心落在穿着位（position=" + (goldRow == null ? "行丢失" : DbStore.Int(goldRow, "position").ToString()) + "）");
            InventoryDao.InsertItem(db, cid, 2616004, 2, 2, 1);
            var wrongHeart = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = gold, scrollId = 2616004, scrollPosition = 2 }, new Random(7));
            Check(!wrongHeart.ok && wrongHeart.error == "这件心脏不符合卷轴要求",
                "2616004 砸金之心被 req 白名单拒绝（" + wrongHeart.error + "）");

            // 卡勒馬：reqScope 整段 167xxxx，任何心脏都吃
            InventoryDao.InsertItem(db, cid, 2643009, 2, 3, 2);
            var anyHeart = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = gold, scrollId = 2643009, scrollPosition = 3 }, new Random(7));
            Check(anyHeart.ok && anyHeart.scrollResult == "success", "2643009 砸金之心成功（reqScope 全段）");
            var goldEq = InventoryDao.EquipmentOf(db, gold);
            Check(DbStore.Int(goldEq, "watk") == 9 && DbStore.Int(goldEq, "str") == 3 && DbStore.Int(goldEq, "dex") == 3,
                "2643009 给到 全属3 / 攻击9");
            Check(DbStore.Int(goldEq, "upgradeslots") == 3, "金之心 tuc 4 → 3");
            Check(RowCount(db, cid, 2643009) == 1, "2643009 买 2 张，砸 1 次剩 1 张");

            // GM 口径：客户端没传 scrollRequirements 时服务端仍按 Items.json 的 req 卡
            InventoryDao.InsertItem(db, cid, 2616008, 2, 6, 1);
            var noClientReq = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = gold, scrollId = 2616008, scrollPosition = 6 }, new Random(7));
            Check(!noClientReq.ok && noClientReq.error == "这件心脏不符合卷轴要求",
                "客户端不传 req 时服务端自己卡（" + noClientReq.error + "）");

            // 只能砸心脏：武器（穿在身上，才轮到种类校验而不是匠人之魂提示）
            long weapon = InventoryDao.InsertItem(db, cid, 1302000, 1, 2, 1);
            InventoryDao.InsertEquipment(db, weapon);
            NativeInventoryService.Handle(db, new InventoryChangeRequest
            { characterId = cid, itemId = 1302000, equip = true, quantity = 1, equipSlot = -11, sourcePosition = 0 });
            var notHeart = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = weapon, scrollId = 2643009, scrollPosition = 3 }, new Random(7));
            Check(!notHeart.ok && notHeart.error == "卷轴与装备种类不符",
                "2643009 砸武器被拒绝（" + notHeart.error + "）");

            // 心脏也不吃防具/武器卷（Category 靠 167 段配对，2040/2043 都不匹配）
            InventoryDao.InsertItem(db, cid, 2040000, 2, 4, 1);
            var armorScroll = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = gold, scrollId = 2040000, scrollPosition = 4 }, new Random(7));
            Check(!armorScroll.ok && armorScroll.error == "卷轴与装备种类不符",
                "防具卷砸心脏被种类校验拒绝（" + armorScroll.error + "）");

            // —— 真实玩家流程：脚本 cm.gainItem 发下来的心臟（ChangeInventory，不是 InsertEquipment+Wear）——
            // 源端 Equip 实例自带 WZ 的 tuc，所以建行这一刻就要补种；否则新装备看着「0 次可升级」。
            var gained = NativeInventoryService.Handle(db, new InventoryChangeRequest
            { characterId = cid, itemId = 1672046, quantity = 1 });
            Check(gained.ok, "gainItem 發一顆鋰未來心臟" + (gained.ok ? "" : "（" + gained.error + "）"));
            long freshHeart = 0;
            foreach (var row in db.Table("inventoryitems").rows)
                if (DbStore.Int(row, "characterid") == cid && DbStore.Int(row, "itemid") == 1672046)
                    freshHeart = DbStore.Long(row, "inventoryitemid");
            Check(freshHeart > 0, "鋰未來心臟入包");
            var freshEq = InventoryDao.EquipmentOf(db, freshHeart);
            Check(DbStore.Int(freshEq, "upgradeslots") == 8,
                "入包即按 tuc 补种 8 次，实际 " + DbStore.Int(freshEq, "upgradeslots"));
            var worn = NativeInventoryService.Handle(db, new InventoryChangeRequest
            { characterId = cid, itemId = 1672046, equip = true, quantity = 1, equipSlot = -54 });
            Check(worn.ok, "穿上新發的心臟" + (worn.ok ? "" : "（" + worn.error + "）"));
            InventoryDao.InsertItem(db, cid, 2616004, 2, 7, 1);
            var freshScroll = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = freshHeart, scrollId = 2616004, scrollPosition = 7 }, new Random(7));
            Check(freshScroll.ok, "新發的心臟能砸 2616004（" + (freshScroll.ok ? freshScroll.scrollResult : freshScroll.error) + "）");

            // 老存档行：建了 inventoryequipment 但全 0，还被 forceUpdateItem 盖过（nativeStats=1）。
            // 这种「一次没砸过却说次数不足」必须按 tuc 补种后继续，不能直接拒。
            long legacy = InventoryDao.InsertItem(db, cid, 1672047, 1, 1, 1);
            InventoryDao.InsertEquipment(db, legacy);
            InventoryDao.EquipmentOf(db, legacy)["nativeStats"] = 1L;
            NativeInventoryService.Handle(db, new InventoryChangeRequest
            { characterId = cid, itemId = 1672047, equip = true, quantity = 1, equipSlot = -54, sourcePosition = 0 });
            InventoryDao.InsertItem(db, cid, 2616006, 2, 8, 1);
            var legacyScroll = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = legacy, scrollId = 2616006, scrollPosition = 8 }, new Random(7));
            Check(legacyScroll.ok, "老存档空行（nativeStats=1）也能砸（" +
                (legacyScroll.ok ? legacyScroll.scrollResult : legacyScroll.error) + "）");
            Check(DbStore.Int(InventoryDao.EquipmentOf(db, legacy), "upgradeslots") == 9,
                "补种后 tuc 10 → 9，实际 " + DbStore.Int(InventoryDao.EquipmentOf(db, legacy), "upgradeslots"));

            // 提示必须能自己说明白：哪件装备、还剩几次、上限几次（不然「没砸过却说不足」无从查）
            long steel = Wear(db, cid, 1672043, Check);   // 鋼鐵未來心臟 tuc2
            for (int round = 0; round < 2; round++)
            {
                InventoryDao.InsertItem(db, cid, 2616004, 2, 9 + round, 1);
                NativeScrollService.Apply(db, new EquipPatchRequest
                { characterId = cid, inventoryItemId = steel, scrollId = 2616004, scrollPosition = 9 + round }, new Random(7));
            }
            InventoryDao.InsertItem(db, cid, 2616004, 2, 11, 1);
            var drained = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = steel, scrollId = 2616004, scrollPosition = 11 }, new Random(7));
            Check(!drained.ok && drained.error.Contains("鋼鐵未來心臟") && drained.error.Contains("剩 0 次"),
                "次数用完的提示带装备名与次数（" + drained.error + "）");

            // tuc=0 的心脏（銀之心在数据里没有 tuc 字段）砸不上去，且提示要说清是哪件、为什么
            long blank = Wear(db, cid, 1672000, Check);
            InventoryDao.InsertItem(db, cid, 2643010, 2, 5, 1);
            var noSlot = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = blank, scrollId = 2643010, scrollPosition = 5 }, new Random(7));
            Check(!noSlot.ok && noSlot.error == "銀之心 没有可升级次数（数据里 tuc=0），砸不了卷",
                "銀之心 tuc=0 → 提示说清是数据里 0 次（" + noSlot.error + "）");
        }
        finally
        {
            try { db.Save(); } catch { }
            try { File.Delete(path); } catch { }
        }
    }

    static long Wear(DbStore db, int cid, int itemId, Action<bool, string> Check)
    {
        long rowId = InventoryDao.InsertItem(db, cid, itemId, 1, 1, 1);
        InventoryDao.InsertEquipment(db, rowId);
        var res = NativeInventoryService.Handle(db, new InventoryChangeRequest
        { characterId = cid, itemId = itemId, equip = true, quantity = 1, equipSlot = -54, sourcePosition = 0 });
        Check(res.ok, "穿上心脏 " + itemId + (res.ok ? "" : "（" + res.error + "）"));
        return rowId;
    }

    static int RowCount(DbStore db, int cid, int itemId)
    {
        int total = 0;
        foreach (var row in db.Table("inventoryitems").rows)
            if (DbStore.Int(row, "characterid") == cid && DbStore.Int(row, "itemid") == itemId)
                total += DbStore.Int(row, "quantity");
        return total;
    }
}
