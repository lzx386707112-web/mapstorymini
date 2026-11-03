// 装备面板双击脱下报「无效装备槽位」复现。
// 链路 = 客户端 EquipLoadout.Equip/Unequip 发出的 InventoryChangeRequest 形状：
//   穿上: equip=true, quantity=1, equipSlot=客户端算出的槽位, sourcePosition=背包格
//   脱下: equip=true, quantity=0, equipSlot=当前穿着位（装备面板格 = WornSlotOf(cellSlot)）
using System;
using System.Collections.Generic;
using System.Linq;

static class EquipUnequipChecks
{
    static int passed, failed;

    public static int Run()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_equipunequip_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "equip_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "EquipGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            Console.WriteLine("-- 新号初始穿着装备：双击脱下（武器除外，客户端拦）--");
            foreach (int slot in new[] { -5, -6, -7 })
                UnequipAt(db, cid, slot, "初始穿着位 " + slot);

            Console.WriteLine("-- 漂移数据（穿着位 ≠ islot 规范位）：源端 unequip 按客户端指定格子放行 --");
            // 现金上衣（1040001 cash=1，规范位 -105）坐在普通位 -5：旧数据/导入会这样
            DriftUnequip(db, cid, 1040001, -5, "现金上衣坐在普通位 -5");
            // 普通上衣坐在现金位 -105
            DriftUnequip(db, cid, 1040002, -105, "普通上衣坐在现金位 -105");
            // 消耗品坐在宠物药水包格 -24（双击宠物面板药水包格）
            DriftUnequip(db, cid, 2000001, -24, "药水坐在宠物药水包格 -24");
            // 穿上方向仍严格：上衣指定 -6 必须拒绝
            long rowId = InventoryDao.InsertItem(db, cid, 1040002, 1, 1, 1);
            InventoryDao.InsertEquipment(db, rowId);
            var bad = NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = cid, itemId = 1040002, equip = true, quantity = 1, equipSlot = -6 });
            Check(!bad.ok && bad.error == "无效装备槽位", "穿上时槽位仍严格（上衣指定 -6 拒绝）");

            Console.WriteLine("-- 全槽位单件穿脱轮扫（道具目录按 islot 分组）--");
            var catalog = Server.Bot.BotItemCatalog.Shared;
            var bySlot = new Dictionary<int, List<int>>();
            foreach (int id in catalog.ItemIds)
            {
                int slot = NativeInventoryService.EquipmentSlotOf(id);
                if (slot == 0) continue;
                if (!bySlot.TryGetValue(slot, out var list)) bySlot[slot] = list = new List<int>();
                list.Add(id);
            }
            foreach (var kv in bySlot.OrderBy(k => k.Key))
                foreach (int id in kv.Value.Take(3))
                    if (Roundtrip(db, cid, id)) break;

            Console.WriteLine("-- 状态场景 --");
            int cashWeapon = FirstOf(catalog, -111);
            Check(cashWeapon > 0, "目录含现金武器");
            if (cashWeapon > 0)
            {
                EquipAt(db, cid, 1302000, -11, "普通武器作为现金覆盖的底层");
                EquipAt(db, cid, cashWeapon, -111, "现金武器覆盖");
                int destination = NativeInventoryService.Free(db, CharactersDao.FindById(db, cid), 1);
                var moved = NativeInventoryService.Handle(db, new InventoryChangeRequest {
                    characterId=cid,itemId=cashWeapon,equip=true,quantity=0,equipSlot=-111,moveTo=destination });
                Check(moved.ok && !Worn(db,cid,-111) && Worn(db,cid,-11) &&
                    moved.inventory.Exists(x=>x.itemId==cashWeapon && x.inventoryType==1 && x.position==destination),
                    "现金武器卸到指定空格，底层普通武器保留");
                // 穿回同一条实例，再验证占用格/越界拒绝且保留原穿着。
                var worn = NativeInventoryService.Handle(db, new InventoryChangeRequest {
                    characterId=cid,itemId=cashWeapon,equip=true,quantity=1,equipSlot=-111,sourcePosition=destination });
                Check(worn.ok, "现金武器可重新穿戴");
                var occupied = InventoryDao.ItemsByCharacter(db,cid).Find(x=>DbStore.Int(x,"inventorytype")==1 && DbStore.Int(x,"position")>0);
                Check(occupied != null, "存在已占用背包格供拒绝测试");
                if (occupied != null)
                {
                    var rejected = NativeInventoryService.Handle(db, new InventoryChangeRequest {
                        characterId=cid,itemId=cashWeapon,equip=true,quantity=0,equipSlot=-111,moveTo=DbStore.Int(occupied,"position") });
                    Check(!rejected.ok && Worn(db,cid,-111), "放到已占用格拒绝，不卸装不覆盖");
                }
                var outside = NativeInventoryService.Handle(db, new InventoryChangeRequest {
                    characterId=cid,itemId=cashWeapon,equip=true,quantity=0,equipSlot=-111,moveTo=int.MaxValue });
                Check(!outside.ok && Worn(db,cid,-111), "越界背包格拒绝且不丢装备");
                UnequipAt(db,cid,-111,"现金武器双击式卸下（自动空格）");
            }
            // 两枚普通戒指：第一枚 -12，第二枚客户端挑 -13；再从面板格 -13 双击脱下
            int ring1 = FirstOf(catalog, -12), ring2 = SecondOf(catalog, -12);
            if (ring1 > 0 && ring2 > 0)
            {
                EquipAt(db, cid, ring1, -12, "戒指1");
                EquipAt(db, cid, ring2, -13, "戒指2（客户端挑第一个空槽）");
                UnequipAt(db, cid, -13, "双击面板格 -13 脱下戒指2");
                UnequipAt(db, cid, -12, "双击面板格 -12 脱下戒指1");
            }
            // 两枚现金戒指：-112 → -113
            int cashRing1 = FirstOf(catalog, -112), cashRing2 = SecondOf(catalog, -112);
            if (cashRing1 > 0 && cashRing2 > 0)
            {
                EquipAt(db, cid, cashRing1, -112, "现金戒指1");
                EquipAt(db, cid, cashRing2, -113, "现金戒指2（客户端挑第一个空槽）");
                UnequipAt(db, cid, -113, "双击面板格 -12（现金盖显示）脱下现金戒指2");
                UnequipAt(db, cid, -112, "双击脱下现金戒指1");
            }
            // 已占用槽位互换（源端 prior 分支）：再穿一件同槽
            int hat = FirstOf(catalog, -1);
            if (hat > 0)
            {
                EquipAt(db, cid, hat, -1, "帽子（-1 已被初始装备占用，应互换）");
                UnequipAt(db, cid, -1, "脱下互换后的帽子");
            }
            Console.WriteLine($"通过 {passed} / 失败 {failed}");
            return failed == 0 ? 0 : 1;
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }

    static int FirstOf(Server.Bot.BotItemCatalog catalog, int slot)
    {
        foreach (int id in catalog.ItemIds) if (NativeInventoryService.EquipmentSlotOf(id) == slot) return id;
        return 0;
    }

    static int SecondOf(Server.Bot.BotItemCatalog catalog, int slot)
    {
        bool seen = false;
        foreach (int id in catalog.ItemIds)
            if (NativeInventoryService.EquipmentSlotOf(id) == slot)
            { if (seen) return id; seen = true; }
        return 0;
    }

    // 返回 true 表示该件测过（含失败），调用方换下一件
    static bool Roundtrip(DbStore db, int cid, int itemId)
    {
        string tag = itemId + "（" + Server.Bot.BotItemCatalog.Shared.Name(itemId) + "，目标槽 " + NativeInventoryService.EquipmentSlotOf(itemId) + "）";
        int target = NativeInventoryService.EquipmentSlotOf(itemId);
        if (target == -12 || target == -112)
        {
            bool cash = target == -112;
            int[] group = cash ? new[] { -112, -113, -115, -116 } : new[] { -12, -13, -15, -16 };
            foreach (int ringSlot in group)
                if (!Worn(db, cid, ringSlot)) { target = ringSlot; break; }
        }
        if (!EquipAt(db, cid, itemId, target, tag)) return true;
        int wornPos = WornPosition(db, cid, itemId);
        Check(wornPos == target, tag + " 落位 " + wornPos + "（期望 " + target + "）");
        bool ok = UnequipAt(db, cid, wornPos, "脱下 " + tag);
        Cleanup(db, cid, itemId);
        return !ok; // 失败也换下一件，避免坏数据卡死整组
    }

    static void DriftUnequip(DbStore db, int cid, int itemId, int wornSlot, string tag)
    {
        long rowId = InventoryDao.InsertItem(db, cid, itemId, 1, wornSlot, 1);
        InventoryDao.InsertEquipment(db, rowId);
        var row = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == rowId);
        row["inventorytype"] = (long)InventoryDao.Equipped;
        var unequip = NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = cid, itemId = itemId, equip = true, quantity = 0, equipSlot = wornSlot });
        Check(unequip.ok, tag + "（equipSlot=" + wornSlot + "）" + (unequip.ok ? "" : " → " + unequip.error));
        Cleanup(db, cid, itemId);
    }

    static bool EquipAt(DbStore db, int cid, int itemId, int slot, string tag)
    {
        long rowId = InventoryDao.InsertItem(db, cid, itemId, 1, 1, 1);
        InventoryDao.InsertEquipment(db, rowId);
        var equip = NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = cid, itemId = itemId, equip = true, quantity = 1, equipSlot = slot, sourcePosition = 0 });
        Check(equip.ok, "穿上 " + tag + " → 槽 " + slot + (equip.ok ? "" : "（" + equip.error + "）"));
        return equip.ok;
    }

    static bool UnequipAt(DbStore db, int cid, int slot, string tag)
    {
        int itemId = 0;
        var row = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid &&
            DbStore.Int(x, "position") == slot && DbStore.Int(x, "inventorytype") == InventoryDao.Equipped);
        if (row != null) itemId = DbStore.Int(row, "itemid");
        if (itemId <= 0) { Check(false, tag + "：该槽位没有穿着的装备"); return false; }
        // 客户端 Unequip 发的是 Profile 里的槽位与道具，quantity=0
        var unequip = NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = cid, itemId = itemId, equip = true, quantity = 0, equipSlot = slot });
        Check(unequip.ok, tag + "（equipSlot=" + slot + "）" + (unequip.ok ? "" : " → " + unequip.error));
        return unequip.ok;
    }

    static void Cleanup(DbStore db, int cid, int itemId)
    {
        var stale = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid &&
            DbStore.Int(x, "itemid") == itemId && DbStore.Int(x, "inventorytype") == InventoryDao.Equipped);
        if (stale != null)
        {
            long rowId = DbStore.Long(stale, "inventoryitemid");
            db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid") == rowId);
            db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid") == rowId);
        }
    }

    static bool Worn(DbStore db, int cid, int slot)
    {
        return db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid &&
            DbStore.Int(x, "position") == slot && DbStore.Int(x, "inventorytype") == InventoryDao.Equipped) != null;
    }

    static int WornPosition(DbStore db, int cid, int itemId)
    {
        var row = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid &&
            DbStore.Int(x, "itemid") == itemId && DbStore.Int(x, "position") < 0);
        return row == null ? 0 : DbStore.Int(row, "position");
    }

    static void Check(bool cond, string what)
    {
        if (cond) { passed++; Console.WriteLine("  ✓ " + what); }
        else { failed++; Console.WriteLine("  ✗ " + what); }
    }
}
