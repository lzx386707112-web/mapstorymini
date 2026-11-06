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
            Check(!wrongHeart.ok && wrongHeart.error.StartsWith("这件心脏不符合卷轴要求") && wrongHeart.error.Contains("水晶未來心臟（6 槽）"),
                "2616004 砸金之心被 req 白名单拒绝，提示带适用清单（" + wrongHeart.error + "）");

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
            Check(!noClientReq.ok && noClientReq.error.StartsWith("这件心脏不符合卷轴要求") && noClientReq.error.Contains("这张卷轴适用于"),
                "客户端不传 req 时服务端自己卡，提示带适用清单（" + noClientReq.error + "）");

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

            // 属性已经被星力/星火种上、槽位仍是 0（星力只写 base 属性，从不写 upgradeslots）；
            // 砸卷必须只补槽位，不能把已有属性重置回模板，更不能以「0 槽」拒绝。
            // 用户实测：武器上完星力，提示里的「可升级次数」变成 0，且砸不上卷。
            long starred = InventoryDao.InsertItem(db, cid, 1302000, 1, 40, 1);
            InventoryDao.InsertEquipment(db, starred);
            NativeInventoryService.Handle(db, new InventoryChangeRequest
            { characterId = cid, itemId = 1302000, equip = true, quantity = 1, equipSlot = -11, sourcePosition = 40 });
            var starredEq = InventoryDao.EquipmentOf(db, starred);
            starredEq["watk"] = 205L; starredEq["starLevel"] = 12L;
            starredEq["upgradeslots"] = 0L; starredEq["level"] = 0L;
            starredEq["nativeStats"] = 1L; starredEq["enhancementVersion"] = 282L;
            InventoryDao.InsertItem(db, cid, 2043000, 2, 41, 1);
            var starredScroll = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = starred, scrollId = 2043000, scrollPosition = 41 }, new Random(7));
            Check(starredScroll.ok, "上过星力、槽位 0 的武器能砸卷（" +
                (starredScroll.ok ? starredScroll.scrollResult : starredScroll.error) + "）");
            var starredAfter = InventoryDao.EquipmentOf(db, starred);
            Check(DbStore.Int(starredAfter, "upgradeslots") == 6,
                "补种 7 次后砸掉 1 次，实际 " + DbStore.Int(starredAfter, "upgradeslots"));
            Check(DbStore.Int(starredAfter, "watk") == 206,
                "补槽位不能重置已有属性（205+1），实际 " + DbStore.Int(starredAfter, "watk"));

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

            // tuc=0 的心脏砸不上去：提示要说清是哪件、为什么，req 白名单卷还要列出能砸哪些心脏
            long blank = Wear(db, cid, 1672000, Check);
            InventoryDao.InsertItem(db, cid, 2643010, 2, 5, 1);
            var noSlot = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = blank, scrollId = 2643010, scrollPosition = 5 }, new Random(7));
            Check(!noSlot.ok && noSlot.error == "銀之心 是 0 槽心脏，砸不了卷（数据里 tuc=0）",
                "銀之心 tuc=0 + 卡勒馬卷（无 req 清单）→ 通用文案（" + noSlot.error + "）");

            // 用户实测场景：2616004 力量卷砸銀未來心臟（1672042，六款里唯一 0 槽）→
            // 提示 0 槽 + 逐颗列出六款心脏的槽数，玩家知道该换鋼鐵以上
            long silver = Wear(db, cid, 1672042, Check);
            InventoryDao.InsertItem(db, cid, 2616004, 2, 12, 1);
            var silverScroll = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = silver, scrollId = 2616004, scrollPosition = 12 }, new Random(7));
            Check(!silverScroll.ok && silverScroll.error.StartsWith("銀未來心臟 是 0 槽心脏，砸不了卷，这张卷轴适用于：")
                && silverScroll.error.Contains("鋼鐵未來心臟（2 槽）") && silverScroll.error.Contains("鈦未來心臟（10 槽）")
                && silverScroll.error.Contains("銀未來心臟（0 槽）"),
                "2616004 砸銀未來心臟 → 0 槽提示 + 六款槽数清单（" + silverScroll.error + "）");

            // —— GM 完美卷轴不再无限砸卷（用户「装备砸卷没有上限」）——
            // 源端 use_perfect_gm_scroll 默认 true：GM 必成功且不扣升级次数。本项目默认 admin 号
            // gm=6，等于无限砸。本地只保留「必成功」，成功照样扣次数，砸满 tuc 即停。
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["gm"] = 6L });
            long gmWeapon = WearWeapon(db, cid, 1302000, Check);
            var gmBefore = InventoryDao.EquipmentOf(db, gmWeapon);
            int gmSlots = DbStore.Int(gmBefore, "upgradeslots"), gmAttack = DbStore.Int(gmBefore, "watk");
            Check(gmSlots == 7, "GM 武器按 tuc 补种 7 次，实际 " + gmSlots);
            int gmSuccess = 0;
            EquipPatchResponse gmLast = null;
            for (int round = 0; round < 9; round++)
            {
                InventoryDao.InsertItem(db, cid, 2043000, 2, 70 + round, 1);
                var r = NativeScrollService.Apply(db, new EquipPatchRequest
                { characterId = cid, inventoryItemId = gmWeapon, scrollId = 2043000, scrollPosition = 70 + round }, new Random(7));
                gmLast = r;
                if (r.ok && r.scrollResult == "success") gmSuccess++;
            }
            var gmAfter = InventoryDao.EquipmentOf(db, gmWeapon);
            Check(gmSuccess == 7, "GM 必成功但只砸得动 7 次（tuc），实际 " + gmSuccess);
            Check(DbStore.Int(gmAfter, "upgradeslots") == 0 && DbStore.Int(gmAfter, "level") == 7,
                "GM 砸满后槽 0 / level 7，实际 " + DbStore.Int(gmAfter, "upgradeslots") + " / " + DbStore.Int(gmAfter, "level"));
            Check(DbStore.Int(gmAfter, "watk") == gmAttack + 7,
                "GM 攻击力只涨 7 次，实际 +" + (DbStore.Int(gmAfter, "watk") - gmAttack));
            Check(gmLast != null && !gmLast.ok && gmLast.error.Contains("剩 0 次"),
                "第 8 次起砸卷被拒（" + (gmLast == null ? "无结果" : gmLast.error) + "）");

            // 恢复类卷轴统一按 tuc + vicious 封顶：良医卷 2049920 源端是「无条件 +1 槽」。
            long restoreWeapon = WearWeapon(db, cid, 1302000, Check);
            var restoreEq = InventoryDao.EquipmentOf(db, restoreWeapon);
            restoreEq["upgradeslots"] = 5L;   // 模拟失败损失 2 次：slots+level=5 < tuc=7
            restoreEq["level"] = 0L;
            for (int round = 0; round < 2; round++)
            {
                InventoryDao.InsertItem(db, cid, 2049920, 2, 100 + round, 1);
                var r = NativeScrollService.Apply(db, new EquipPatchRequest
                { characterId = cid, inventoryItemId = restoreWeapon, scrollId = 2049920, scrollPosition = 100 + round }, new Random(7));
                Check(r.ok && DbStore.Int(InventoryDao.EquipmentOf(db, restoreWeapon), "upgradeslots") == 6 + round,
                    "良医卷把损失的次数补回第 " + (round + 1) + " 次（" + (r.ok ? r.scrollResult : r.error) + "）");
            }
            InventoryDao.InsertItem(db, cid, 2049920, 2, 102, 1);
            var over = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = restoreWeapon, scrollId = 2049920, scrollPosition = 102 }, new Random(7));
            Check(!over.ok && over.error.Contains("没有可恢复的升级次数") && over.error.Contains("上限 7"),
                "良医卷不能超过 tuc（" + over.error + "）");
            InventoryDao.InsertItem(db, cid, 2049000, 2, 103, 1);
            var cleanOver = NativeScrollService.Apply(db, new EquipPatchRequest
            { characterId = cid, inventoryItemId = restoreWeapon, scrollId = 2049000, scrollPosition = 103 }, new Random(7));
            Check(!cleanOver.ok && cleanOver.error.Contains("没有可恢复的升级次数") && cleanOver.error.Contains("上限 7"),
                "白卷同样不能超过 tuc（" + cleanOver.error + "）");
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

    // 必须走真实发装备路径：建行时按 tuc 补种 upgradeslots。手工 InsertEquipment 会留下
    // 全 0 行，砸第一张卷时 Pristine 补种会把属性重置成模板值，基线就不准了。
    static long WearWeapon(DbStore db, int cid, int itemId, Action<bool, string> Check)
    {
        var gain = NativeInventoryService.Handle(db, new InventoryChangeRequest
        { characterId = cid, itemId = itemId, quantity = 1 });
        Check(gain.ok, "发一件武器 " + itemId + (gain.ok ? "" : "（" + gain.error + "）"));
        long rowId = 0; int bagPosition = 0;
        foreach (var row in db.Table("inventoryitems").rows)
            if (DbStore.Int(row, "characterid") == cid && DbStore.Int(row, "itemid") == itemId
                && DbStore.Int(row, "position") > 0 && DbStore.Long(row, "inventoryitemid") > rowId)
            { rowId = DbStore.Long(row, "inventoryitemid"); bagPosition = DbStore.Int(row, "position"); }
        Check(rowId > 0 && bagPosition > 0, "找到新发的武器行");
        var res = NativeInventoryService.Handle(db, new InventoryChangeRequest
        { characterId = cid, itemId = itemId, equip = true, quantity = 1, equipSlot = -11, sourcePosition = bagPosition });
        Check(res.ok, "穿上武器 " + itemId + (res.ok ? "" : "（" + res.error + "）"));
        var worn = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == rowId);
        Check(worn != null && DbStore.Int(worn, "position") < 0, "武器落在穿着位 -11");
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
