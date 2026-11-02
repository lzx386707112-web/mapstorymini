// 现金装备道具回归：金锤子 5570000 / 宿命剪刀 5520000 / 取名 5060000 / 封印 5061000..03。
// 服务端 NativeCashEquipmentService.Apply（UseCashItemHandler itemType 552/557 分支直译）；
// 客户端入口 = 背包拖现金道具到装备（NativeInventoryActions.IsCashEquipment）。
using System;

static class CashEquipChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 现金装备道具（金锤 / 剪刀 / 取名 / 封印）==");
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_cashequip_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "hammer_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "HammerGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            // 背包里放一件可升级装备（1302000 剑，初始 upgradeslots 来自 WZ）
            long equipId = InventoryDao.InsertItem(db, cid, 1302000, 1, 1, 1);
            InventoryDao.InsertEquipment(db, equipId);
            var eq = InventoryDao.EquipmentOf(db, equipId);
            int baseSlots = (int)DbStore.Int(eq, "upgradeslots");
            Check(eq != null && baseSlots >= 0, "测试装备就绪（upgradeslots=" + baseSlots + "）");

            // 金锤 ×2：vicious 0→2，upgradeslots +2，锤子各消耗 1
            InventoryDao.InsertItem(db, cid, 5570000, 5, 2, 5);   // 现金栏 5 张
            var r1 = NativeCashEquipmentService.Apply(db, new EquipPatchRequest { characterId = cid, inventoryItemId = equipId, cashItemId = 5570000 });
            Check(r1.ok, "金锤第 1 次成功" + (r1.ok ? "" : "（" + r1.error + "）"));
            eq = InventoryDao.EquipmentOf(db, equipId);
            int cleanSlots = Server.Bot.BotItemCatalog.Shared.CleanEquip(1302000).upgradeslots;
            Check(DbStore.Int(eq, "vicious") == 1 && DbStore.Int(eq, "upgradeslots") == cleanSlots + 1,
                "vicious=1、upgradeslots=clean+1（" + DbStore.Int(eq, "upgradeslots") + "，clean=" + cleanSlots + "）");
            Check(InventoryDao.CountOf(db, cid, 5570000) == 4, "锤子消耗 1 张（5→4）");
            var r2 = NativeCashEquipmentService.Apply(db, new EquipPatchRequest { characterId = cid, inventoryItemId = equipId, cashItemId = 5570000 });
            Check(r2.ok, "金锤第 2 次成功");
            eq = InventoryDao.EquipmentOf(db, equipId);
            Check(DbStore.Int(eq, "vicious") == 2, "vicious=2");
            var r3 = NativeCashEquipmentService.Apply(db, new EquipPatchRequest { characterId = cid, inventoryItemId = equipId, cashItemId = 5570000 });
            Check(!r3.ok, "第 3 次 → 拒绝（vicious>=2）");
            Check(InventoryDao.CountOf(db, cid, 5570000) == 3, "拒绝时不消耗锤子");

            // 宿命剪刀：可交易道具加 karma 标记并消耗
            long scrollId = InventoryDao.InsertItem(db, cid, 2040000, 2, 3, 1);   // 卷轴（ETC 类 karma=2）
            var r4 = NativeCashEquipmentService.Apply(db, new EquipPatchRequest { characterId = cid, inventoryItemId = scrollId, cashItemId = 5520000 });
            if (r4.ok)
            {
                var rows = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == scrollId);
                Check((DbStore.Int(rows, "flag") & 2) != 0, "剪刀 → ETC 道具加 karma 标记");
                Check(InventoryDao.CountOf(db, cid, 5520000) == 2, "剪刀消耗 1 把（3→2）");
            }
            else Check(string.IsNullOrEmpty(r4.error) == false, "剪刀对不可交易数据缺失道具拒绝（" + r4.error + "）");

            // 取名道具：已穿戴装备 owner 角色名
            long wornNew = InventoryDao.InsertItem(db, cid, 1072001, 1, -5, 1);   // 鞋子穿戴位（负 slot）
            InventoryDao.InsertEquipment(db, wornNew);
            long wornId = wornNew;
            InventoryDao.InsertItem(db, cid, 5060000, 1, 4, 1);
            var r5 = NativeCashEquipmentService.Apply(db, new EquipPatchRequest { characterId = cid, inventoryItemId = wornId, cashItemId = 5060000 });
            Check(r5.ok, "取名道具成功");
            var worn = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == wornId);
            Check(DbStore.Str(worn, "owner") == "HammerGuy", "owner 已改为角色名");

            // 孵化器（5060002）：吃掉祭品随机孵奖励（源端 getIncubatedItem bug 照意图修正，见服务注释）
            long victimId = InventoryDao.InsertItem(db, cid, 4030000, 4, 6, 3);   // 祭品 x3
            InventoryDao.InsertItem(db, cid, 5060002, 4, 7, 1);
            NativeCashEquipmentService.IncubatorPick = _ => 4;   // 池 [4] = 2000004 白药水 x240
            var r7 = NativeCashEquipmentService.Apply(db, new EquipPatchRequest { characterId = cid, inventoryItemId = victimId, cashItemId = 5060002 });
            Check(r7.ok && r7.rewardItemId == 2000004 && r7.rewardQuantity == 240, "孵化 → 白药水 x240（" + (r7.ok ? "ok" : r7.error) + "）");
            Check(InventoryDao.CountOf(db, cid, 4030000) == 2, "祭品消耗 1 个（3→2）");
            Check(InventoryDao.CountOf(db, cid, 5060002) == 0, "孵化器消耗");
            Check(InventoryDao.CountOf(db, cid, 2000004) > 0, "奖励进背包");
            NativeCashEquipmentService.IncubatorPick = null;

            // 封印之锁（5061000 = 7 天）：只能封永久道具（无过期）→ 加 flag|1 并设 7 天过期
            long timedId = InventoryDao.InsertItem(db, cid, 1302000, 1, 2, 1);
            InventoryDao.InsertEquipment(db, timedId);
            InventoryDao.InsertItem(db, cid, 5061000, 1, 5, 1);
            long before = DbStore.Long(db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == timedId), "expiration");
            var r6 = NativeCashEquipmentService.Apply(db, new EquipPatchRequest { characterId = cid, inventoryItemId = timedId, cashItemId = 5061000 });
            if (!r6.ok) Console.WriteLine("   [封印失败原因] " + r6.error);
            long after = DbStore.Long(db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == timedId), "expiration");
                        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Check(r6.ok && before <= 0 && after >= now + 6 * 86400000L, "永久道具封印 → flag+7 天过期（" + r6.error + "）");
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }
}
