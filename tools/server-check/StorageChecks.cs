// 仓库回归：对照源端 Storage.java / StorageProcessor.java / StorageInventory.java。
// 覆盖等级门槛、存取费用（trunkPut/trunkGet）、仓库满/背包满/金币不足/唯一道具错误码、
// 可充能整组入仓、婚戒禁入、karma 标记转不可交易、金币存取、仓库整理、槽位升级。
// 注意建号自带 4161001（Etc pos=1），测试道具统一用 pos>=20 避开。
using System;
using System.Collections.Generic;
using System.IO;

static class StorageChecks
{
    static void SetLevel(DbStore db, int cid, int level)
    {
        var rec = CharactersDao.FindById(db, cid);
        rec.level = level;
        CharactersDao.Save(db, rec);
    }

    static StorageResponse Call(DbStore db, int cid, StorageOperation op, int npc = 9030100,
        int invType = 0, int invPos = 0, int index = -1, int quantity = 0, int itemId = 0, long meso = 0, int slots = 0)
    {
        var res = NativeStorageService.Handle(db, new StorageRequest {
            characterId = cid, operation = op, npcId = npc, inventoryType = invType, itemPosition = invPos,
            storageIndex = index, quantity = quantity, itemId = itemId, meso = meso, slots = slots });
        if (!res.ok) Console.WriteLine("   [失败 op=" + op + " code=" + res.code + " err=" + res.error + "]");
        return res;
    }

    static int BagCount(DbStore db, int cid, int itemId)
    {
        int total = 0;
        foreach (var row in db.Table("inventoryitems").Select(x =>
                     DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "itemid") == itemId))
            total += DbStore.Int(row, "quantity");
        return total;
    }

    static int StoredCount(DbStore db, int accountId, int itemId)
    {
        int total = 0;
        foreach (var row in db.Table("inventoryitems").Select(x =>
                     DbStore.Int(x, "characterid") == NativeStorageService.StorageMarkerValue
                     && DbStore.Int(x, "storageaccountid") == accountId && DbStore.Int(x, "itemid") == itemId))
            total += DbStore.Int(row, "quantity");
        return total;
    }

    static void ClearBag(DbStore db, int cid, int inventoryType)
    {
        var ids = new HashSet<long>();
        foreach (var row in db.Table("inventoryitems").Select(x =>
                     DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "inventorytype") == inventoryType))
            ids.Add(DbStore.Long(row, "inventoryitemid"));
        db.Table("inventoryitems").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
        db.Table("inventoryequipment").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
        db.Save();
    }

    static void ClearStorage(DbStore db, int accountId)
    {
        var ids = new HashSet<long>();
        foreach (var row in db.Table("inventoryitems").Select(x =>
                     DbStore.Int(x, "characterid") == NativeStorageService.StorageMarkerValue
                     && DbStore.Int(x, "storageaccountid") == accountId))
            ids.Add(DbStore.Long(row, "inventoryitemid"));
        db.Table("inventoryitems").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
        db.Table("inventoryequipment").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
        db.Save();
    }

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 仓库 Trunk（Storage / StorageProcessor）==");
        NativeStorageService.Reset();
        NativeStorageService.FeeProvider = () =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Storage/TrunkFees.txt"));

        string path = Path.Combine(Path.GetTempPath(), "mapledash_storage_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "storagetest", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "StorageHero", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;
            int accountId = login.accountId;

            // 费用表：9030100 = 500/1000（Npc.img 实测），1002005 用默认 100/0
            Check(NativeStorageService.PutFee(9030100) == 500 && NativeStorageService.TakeFee(9030100) == 1000,
                "9030100（自由市场仓库）trunkPut=500 trunkGet=1000");
            Check(NativeStorageService.PutFee(1002005) == 100 && NativeStorageService.TakeFee(1002005) == 0,
                "1002005 走默认 trunkPut=100 trunkGet=0");

            SetLevel(db, cid, 10);
            var denied = Call(db, cid, StorageOperation.Open);
            Check(!denied.ok && denied.error.Contains("15级"), "15 级门槛：低等级拒绝");

            SetLevel(db, cid, 15);
            var res = Call(db, cid, StorageOperation.Open);
            Check(res.ok && res.storage != null && res.storage.slots == NativeStorageService.InitialSlots && res.storage.meso == 0,
                "首次打开自动建仓：4 格 0 金币");

            // —— 存入 ——
            SetLevel(db, cid, 15);
            var c0 = CharactersDao.FindById(db, cid); c0.meso = 100000; CharactersDao.Save(db, c0);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 20, 30);
            var store = Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 20, quantity: 10, itemId: 4000000);
            Check(store.ok && StoredCount(db, accountId, 4000000) == 10, "存入 10 个进仓库");
            Check(BagCount(db, cid, 4000000) == 20, "背包扣掉 10 个");
            Check(store.meso == 100000 - 500, "存入扣 trunkPut 费 500（还 " + store.meso + "）");
            Check(store.storage != null && store.storage.items.Count == 1, "快照带回仓库列表");

            Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 20, quantity: 5, itemId: 4000000);
            Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 20, quantity: 5, itemId: 4000000);
            Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 20, quantity: 5, itemId: 4000000);
            var full = Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 20, quantity: 1, itemId: 4000000);
            Check(!full.ok && full.code == NativeStorageService.ErrorStorageFull, "仓库 4 格满 → 0x11");

            // —— 取出（趁仓库还有货）——
            long mesoBefore = CharactersDao.FindById(db, cid).meso;
            var take = Call(db, cid, StorageOperation.TakeOut, invType: InventoryDao.Etc, index: 0);
            Check(take.ok && BagCount(db, cid, 4000000) == 15 && StoredCount(db, accountId, 4000000) == 15,
                "取回第一格 10 个（背包 5+10，仓库剩 3 格各 5）");
            long afterTake = CharactersDao.FindById(db, cid).meso;
            Check(afterTake == mesoBefore - 1000, "取回扣 trunkGet 费 1000");

            // 金币不足
            var poor = CharactersDao.FindById(db, cid); poor.meso = 100; CharactersDao.Save(db, poor);
            ClearStorage(db, accountId);
            InventoryDao.InsertItem(db, cid, 4000001, InventoryDao.Etc, 21, 5);
            var broke = Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 21, quantity: 1, itemId: 4000001);
            Check(!broke.ok && broke.code == NativeStorageService.ErrorNoMeso, "金币不足付保管费 → 0x0B");
            Check(StoredCount(db, accountId, 4000001) == 0 && BagCount(db, cid, 4000001) == 5, "失败后背包没扣");

            // 可充能整组入仓
            var rich = CharactersDao.FindById(db, cid); rich.meso = 1000000; CharactersDao.Save(db, rich);
            InventoryDao.InsertItem(db, cid, 2070005, InventoryDao.Use, 1, 800);
            var recharge = Call(db, cid, StorageOperation.Store, invType: InventoryDao.Use, invPos: 1, quantity: 5, itemId: 2070005);
            Check(recharge.ok && StoredCount(db, accountId, 2070005) == 800,
                "飞刀（可充能）必须整组 800 入仓，quantity 参数被忽略");

            // 婚戒禁入
            InventoryDao.InsertItem(db, cid, 1112803, InventoryDao.Equip, 1, 1);
            var wedding = Call(db, cid, StorageOperation.Store, invType: InventoryDao.Equip, invPos: 1, quantity: 1, itemId: 1112803);
            Check(!wedding.ok && StoredCount(db, accountId, 1112803) == 0, "结婚戒指禁止入仓");

            // karma 标记 → 不可交易
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 22, 3);
            foreach (var row in db.Table("inventoryitems").Select(x =>
                         DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "itemid") == 4000002))
                row["flag"] = 0x02L;   // KARMA_USE
            db.Save();
            Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 22, quantity: 3, itemId: 4000002);
            int storedFlag = 0;
            foreach (var row in db.Table("inventoryitems").Select(x =>
                         DbStore.Int(x, "characterid") == NativeStorageService.StorageMarkerValue && DbStore.Int(x, "itemid") == 4000002))
                storedFlag = DbStore.Int(row, "flag");
            Check(storedFlag == 0x08, "带 karma 的道具入仓 → KARMA_USE(0x02) 换成 UNTRADEABLE(0x08)");

            // 唯一道具：身上已有一件再取同 id → 0x0C（4000381 是 Items.json 里 only:1 的真实道具）
            InventoryDao.InsertItem(db, cid, 4000381, InventoryDao.Etc, 23, 1);
            Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 23, quantity: 1, itemId: 4000381);
            InventoryDao.InsertItem(db, cid, 4000381, InventoryDao.Etc, 24, 1);   // 身上再拿一件
            // Etc 类型里 4000002（karma 测试）在前、4000381 在后 → 取下标 1
            var unique = Call(db, cid, StorageOperation.TakeOut, invType: InventoryDao.Etc, index: 1);
            Check(!unique.ok && unique.code == NativeStorageService.ErrorUnique, "唯一道具身上已有一件 → 0x0C");

            // 背包满 → 0x0A：清空 Etc 背包 → 存一件 → 把 Etc 塞满 24 格 → 取
            ClearBag(db, cid, InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000004, InventoryDao.Etc, 1, 1);
            Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 1, quantity: 1, itemId: 4000004);
            for (int slot = 1; slot <= 24; slot++) InventoryDao.InsertItem(db, cid, 4000005, InventoryDao.Etc, slot, 1);
            var invFull = Call(db, cid, StorageOperation.TakeOut, invType: InventoryDao.Etc, index: StoredCount(db, accountId, 4000004) - 1);
            Check(!invFull.ok && invFull.code == NativeStorageService.ErrorInvFull, "背包满 → 0x0A");

            // —— 金币 ——
            ClearBag(db, cid, InventoryDao.Etc);
            ClearStorage(db, accountId);
            var putM = Call(db, cid, StorageOperation.Meso, meso: -50000);
            Check(putM.ok && putM.storage.meso == 50000, "存入 5 万到仓库");
            Check(putM.meso == CharactersDao.FindById(db, cid).meso, "身上金币同步扣");
            var takeM = Call(db, cid, StorageOperation.Meso, meso: 20000);
            Check(takeM.ok && takeM.storage.meso == 30000, "取出 2 万，仓库剩 3 万");
            var tooMuch = Call(db, cid, StorageOperation.Meso, meso: 99999999);
            Check(tooMuch.ok && tooMuch.storage.meso == 30000, "取出超过库存 → 无操作");

            // —— 整理 ——
            db.Table("game_config").Insert(new DbRow { ["config_type"] = "server", ["config_code"] = "use_storage_item_sort", ["config_value"] = "true" });
            InventoryDao.InsertItem(db, cid, 4000010, InventoryDao.Etc, 1, 5);
            Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 1, quantity: 5, itemId: 4000010);
            InventoryDao.InsertItem(db, cid, 4000010, InventoryDao.Etc, 2, 7);
            Call(db, cid, StorageOperation.Store, invType: InventoryDao.Etc, invPos: 2, quantity: 7, itemId: 4000010);
            var arrange = Call(db, cid, StorageOperation.Arrange);
            Check(arrange.ok && StoredCount(db, accountId, 4000010) == 12, "整理合并同名道具（5+7=12）");
            int sorted = 1;
            var items = arrange.storage.items;
            for (int i = 1; i < items.Count; i++)
                if (items[i].item.inventoryType < items[i - 1].item.inventoryType) sorted = 0;
            Check(sorted == 1, "整理后按 inventoryType 升序");
            db.Table("game_config").Delete(x => (string)x["config_code"] == "use_storage_item_sort");
            db.Save();
            var arrangeOff = Call(db, cid, StorageOperation.Arrange);
            Check(arrangeOff.ok, "use_storage_item_sort 关闭时整理是无操作");

            // —— 持久化往返（回归 2026-11：storageaccountid 未声明进 Schema，落盘即丢，重启后仓库清空）——
            // 此时仓库里有整理合并后的 4000010 ×12、金币 30000。
            db.Save();
            db = DbStore.Open(path);
            Check(StoredCount(db, accountId, 4000010) == 12,
                "落盘重开后仓库物品仍在（storageaccountid 持久化）");
            var reopened = Call(db, cid, StorageOperation.Open);
            Check(reopened.ok && reopened.storage.meso == 30000, "落盘重开后仓库金币仍在");
            var reopenTake = Call(db, cid, StorageOperation.TakeOut, invType: InventoryDao.Etc, index: 0);
            Check(reopenTake.ok, "落盘重开后能正常取出仓库物品");

            // —— 槽位升级 ——
            var grow = Call(db, cid, StorageOperation.IncreaseSlots, slots: 8);
            Check(grow.ok && grow.storage.slots == NativeStorageService.InitialSlots + 8, "现金买 8 格 → 12 格");
            var capped = Call(db, cid, StorageOperation.IncreaseSlots, slots: 48);
            Check(!capped.ok, "超出 48 格上限拒绝");
        }
        finally { NativeStorageService.Reset(); db.Save(); }
    }
}
