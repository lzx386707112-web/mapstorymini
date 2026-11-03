using System;
using System.Collections.Generic;

// 仓库（Trunk / Storage）—— 对照 gms-server：
//   server/Storage.java（loadOrCreateFromDB / store / takeOut / arrangeItems / gainSlots /
//                        getStoreFee=info/trunkPut 默认100 / getTakeOutFee=info/trunkGet 默认0 / isFull）
//   client/processor/npc/StorageProcessor.java（storageAction：mode 4 取出 / 5 存入 / 6 整理 /
//                        7 金币 / 8 关闭；15 级限制；0x0A 背包满 / 0x0B 金币不足 / 0x0C 唯一道具 / 0x11 仓库满）
//   server/StorageInventory.java（mergeItems + sortItems，use_storage_item_sort / use_item_sort_by_name）
//   net/server/channel/handlers/CashOperationHandler.java action 0x07（现金买仓库格 4/8 个，上限 48）
//   util/PacketCreator.java getStorage(0x16)（npcId + slots + meso + 全量物品）
//   KarmaManipulator.toggleKarmaFlagToUntradeable（存取都调用：带 karma 标记的道具进仓库变不可交易）
//   ItemId.isWeddingRing/isWeddingToken（婚戒 1112803/06/07/09、订婚盒 4031357..4031364 禁止入仓）
//
// 与源端的刻意差异：
//   1. 仓库物品行放在 inventoryitems 里，characterid = -3（仓库标记）+ storageaccountid 列按账号隔离
//      （源端是独立 storageitems 表；快递 -1 / 雇佣商店寄存 -2 已占用其它标记值）。
//   2. 金币是 long：源端 mode 7 里 Integer.MIN/MAX 溢出钳制两个分支在 long 下不可能触发，直接省略。
//   3. GM 限制（minimum_gm_level_to_use_storage）单机无意义，不做。
public enum StorageOperation { Open, Store, TakeOut, Meso, Arrange, Close, IncreaseSlots }

public sealed class StorageRequest
{
    public int characterId;
    public StorageOperation operation;
    public int npcId;
    public int inventoryType, itemPosition;  // Store：背包类型 + 格号（1 基）
    public int storageIndex;                 // TakeOut：同类型列表里的下标（0 基，源端 slot）
    public int quantity;
    public long meso;                        // Meso：>0 取出 / <0 存入（源端口径）
    public int slots;                        // IncreaseSlots
    public int itemId;                       // Store：背包里的道具 id（源端第二个 int）
}

public sealed class StorageSlotData
{
    public int index;
    public InventoryEntry item;
}

public sealed class StorageSnapshot
{
    public int npcId, slots;
    public long meso;
    public readonly List<StorageSlotData> items = new List<StorageSlotData>();
}

public sealed class StorageResponse
{
    public bool ok = true;
    public string error = "";
    public byte code;                     // getStorageError：0x0A/0x0B/0x0C/0x11
    public StorageSnapshot storage;
    public List<InventoryEntry> inventory;
    public long meso = -1;
}

public static class NativeStorageService
{
    public const int MaxSlots = 48;           // Storage.canGainSlots
    public const int InitialSlots = 4;        // INSERT INTO storages (... 4, 0)
    public const int MinLevel = 15;           // sendStorage / storageAction 的等级门槛
    public const byte ErrorInvFull = 0x0A, ErrorNoMeso = 0x0B, ErrorUnique = 0x0C, ErrorStorageFull = 0x11;
    const int StorageMarker = -3;             // inventoryitems.characterid 仓库标记（回归用 StorageMarkerValue 读）
    /// <summary>仓库物品行的 inventoryitems.characterid 标记值（快递 -1 / 雇佣商店寄存 -2）。</summary>
    public const int StorageMarkerValue = StorageMarker;
    public const int DefaultPutFee = 100, DefaultTakeFee = 0;

    // Storage.getStoreFee/getTakeOutFee：Npc.img info/trunkPut|trunkGet（导出表见 tools/export_trunk_fees.py）
    public static Func<string> FeeProvider;
    static Dictionary<int, int[]> fees;

    public static void Reset()
    {
        fees = null;
    }

    static void LoadFees()
    {
        fees = new Dictionary<int, int[]>();
        var text = FeeProvider?.Invoke();
        if (text == null) return;
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length != 4 || parts[0] != "F") continue;
            fees[int.Parse(parts[1])] = new[] { int.Parse(parts[2]), int.Parse(parts[3]) };
        }
    }

    static int Fee(int npcId, int which, int fallback)
    {
        if (fees == null) LoadFees();
        return fees.TryGetValue(npcId, out var pair) ? pair[which] : fallback;
    }

    public static int PutFee(int npcId) => Fee(npcId, 0, DefaultPutFee);
    public static int TakeFee(int npcId) => Fee(npcId, 1, DefaultTakeFee);

    // ————————————————— 对外入口 —————————————————

    public static StorageResponse Handle(DbStore db, StorageRequest r)
    {
        var c = CharactersDao.FindById(db, r.characterId);
        if (c == null) return new StorageResponse { ok = false, error = "角色不存在" };
        // sendStorage / storageAction 共用的等级门槛
        if (c.level < MinLevel && r.operation != StorageOperation.Close)
            return new StorageResponse { ok = false, error = "15级以后才可以使用仓库服务" };
        switch (r.operation)
        {
            case StorageOperation.Open: return Open(db, c, r.npcId);
            case StorageOperation.Store: return Store(db, c, r);
            case StorageOperation.TakeOut: return TakeOut(db, c, r);
            case StorageOperation.Meso: return Meso(db, c, r);
            case StorageOperation.Arrange: return Arrange(db, c, r.npcId);
            case StorageOperation.Close: return new StorageResponse { ok = true };
            case StorageOperation.IncreaseSlots: return IncreaseSlots(db, c, r.slots);
        }
        return new StorageResponse { ok = false, error = "未知操作" };
    }

    // ————————————————— 数据 —————————————————

    static DbRow StorageRow(DbStore db, int accountId, bool create)
    {
        var row = db.Table("storages").Find(x => DbStore.Int(x, "accountid") == accountId);
        if (row == null && create)
        {
            db.Table("storages").Insert(new DbRow
                { ["accountid"] = (long)accountId, ["slots"] = (long)InitialSlots, ["meso"] = 0L });
            row = db.Table("storages").Find(x => DbStore.Int(x, "accountid") == accountId);
            db.Save();
        }
        return row;
    }

    static int Slots(DbStore db, int accountId)
    {
        var row = StorageRow(db, accountId, true);
        return DbStore.Int(row, "slots", InitialSlots);
    }

    static long Meso(DbStore db, int accountId)
    {
        var row = StorageRow(db, accountId, true);
        return DbStore.Long(row, "meso");
    }

    // Storage.loadOrCreateFromDB 的物品半边：inventoryitems 行 characterid=-3 + storageaccountid。
    public static List<InventoryEntry> LoadItems(DbStore db, int accountId)
    {
        var list = new List<InventoryEntry>();
        foreach (var row in db.Table("inventoryitems").Select(x =>
                     DbStore.Int(x, "characterid") == StorageMarker && DbStore.Int(x, "storageaccountid") == accountId))
            list.Add(CharService.ToInventoryEntry(db, row));
        return list;
    }

    static void DeleteStorageItem(DbStore db, InventoryEntry entry)
    {
        long id = entry.itemRowId;
        db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == id);
        db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == id);
    }

    // WriteEntry 再补 storageaccountid 列（WriteEntry 只认 characterid）。
    static void WriteStorageEntry(DbStore db, int accountId, InventoryEntry entry)
    {
        long id = NativeMerchantService.WriteEntry(db, StorageMarker, entry, 0, entry.quantity);
        var row = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid", -1) == id);
        row["storageaccountid"] = (long)accountId;
    }

    // ————————————————— 操作 —————————————————

    static StorageResponse Open(DbStore db, CharRecord c, int npcId)
    {
        var items = LoadItems(db, c.accountid);
        // sendStorage：按 inventoryType 稳定排序后一次全量下发
        items.Sort((a, b) => a.inventoryType.CompareTo(b.inventoryType));
        return new StorageResponse { storage = Snapshot(npcId, db, c, items) };
    }

    static StorageSnapshot Snapshot(int npcId, DbStore db, CharRecord c, List<InventoryEntry> items)
    {
        var snap = new StorageSnapshot { npcId = npcId, slots = Slots(db, c.accountid), meso = Meso(db, c.accountid) };
        for (int i = 0; i < items.Count; i++)
            snap.items.Add(new StorageSlotData { index = i, item = items[i] });
        return snap;
    }

    // StorageProcessor case 5（Store）。
    static StorageResponse Store(DbStore db, CharRecord c, StorageRequest r)
    {
        if (r.quantity < 1) return new StorageResponse { ok = true, error = "" };
        var full = CharService.Load(db, (int)c.id);
        var row = full.inventory.Find(x => x.inventoryType == r.inventoryType && x.position == r.itemPosition && InventoryDao.IsBagPosition(x.position));
        if (row == null || row.itemId != r.itemId)
            return new StorageResponse { ok = false, error = "背包里没有这个道具" };
        if (IsWeddingItem(row.itemId))
            return new StorageResponse { ok = false, error = "这个道具不能存进仓库" };
        if(NativePotentialService.BlocksTransfer(db,row))return new StorageResponse {ok=false,error="请先处理装备痕迹或方块候选结果"};

        int slots = Slots(db, c.accountid);
        if (LoadItems(db, c.accountid).Count >= slots)
            return new StorageResponse { ok = false, code = ErrorStorageFull, error = "仓库已满" };
        int fee = PutFee(r.npcId);
        if (c.meso < fee)
            return new StorageResponse { ok = false, code = ErrorNoMeso, error = "金币不足" };

        // 可充能道具（飞刀/子弹）必须整组入仓
        int quantity = r.quantity;
        if (Server.Bot.Trade.TradeItemRules.Rechargeable(row.itemId)) quantity = row.quantity;
        if (quantity > row.quantity) return new StorageResponse { ok = false, error = "数量不合法" };

        var invRow = InventoryRow(db, (int)c.id, row.itemRowId);
        NativeMerchantService.ConsumeFromInventory(db, invRow, row, quantity);

        c.meso -= fee;
        CharactersDao.Save(db, c);
        var stored = row.Copy();
        stored.quantity = quantity;
        KarmaToUntradeable(stored);
        WriteStorageEntry(db, c.accountid, stored);
        db.Save();
        return ResponseAfter(db, c, r.npcId, null, fee, true);
    }

    // StorageProcessor case 4（Take Out）。
    static StorageResponse TakeOut(DbStore db, CharRecord c, StorageRequest r)
    {
        var items = LoadItems(db, c.accountid);
        var typed = items.FindAll(x => x.inventoryType == r.inventoryType);
        if (r.storageIndex < 0 || r.storageIndex >= typed.Count)
            return new StorageResponse { ok = false, error = "仓库里没有这个道具" };
        var entry = typed[r.storageIndex];

        // ii.isPickupRestricted && haveItemWithId → 0x0C（唯一道具身上已有一件）
        if (Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(entry.itemId), "only") == 1)
        {
            var full = CharService.Load(db, (int)c.id);
            foreach (var item in full.inventory)
                if (item.itemId == entry.itemId) return Fail(ErrorUnique, "身上已有一件这个道具");
        }

        int fee = TakeFee(r.npcId);
        if (c.meso < fee) return Fail(ErrorNoMeso, "金币不足");

        // InventoryManipulator.checkSpace → 0x0A
        var probe = CharService.Load(db, (int)c.id);
        var actor = new Server.Bot.BotActor { id = (int)c.id, character = probe };
        if (Server.Bot.Trade.TradeItemRules.FreeSlot(actor, probe.inventory, r.inventoryType) <= 0)
            return Fail(ErrorInvFull, "背包空间不足");

        DeleteStorageItem(db, entry);
        KarmaToUntradeable(entry);
        int position = Server.Bot.Trade.TradeItemRules.FreeSlot(actor, probe.inventory, r.inventoryType);
        NativeMerchantService.WriteEntry(db, (int)c.id, entry, position, entry.quantity);
        c.meso -= fee;
        CharactersDao.Save(db, c);
        db.Save();
        return ResponseAfter(db, c, r.npcId, null, fee, false);
    }

    // StorageProcessor case 7（Mesos）：meso>0 取出 / <0 存入。
    // 源端的 Integer.MIN/MAX 溢出钳制分支在 long 金币下不可达，直接省略。
    static StorageResponse Meso(DbStore db, CharRecord c, StorageRequest r)
    {
        long storageMesos = Meso(db, c.accountid);
        if ((r.meso > 0 && storageMesos >= r.meso) || (r.meso < 0 && c.meso >= -r.meso))
        {
            var row = StorageRow(db, c.accountid, true);
            row["meso"] = storageMesos - r.meso;
            c.meso += r.meso;
            CharactersDao.Save(db, c);
            db.Save();
            return ResponseAfter(db, c, r.npcId, null, 0, false);
        }
        return new StorageResponse { ok = true, storage = Snapshot(r.npcId, db, c, LoadItems(db, c.accountid)) };
    }

    // StorageProcessor case 6 + StorageInventory.mergeItems/sortItems。
    // use_storage_item_sort 关着时整理是无操作（源端 behavior 相同）。
    static StorageResponse Arrange(DbStore db, CharRecord c, int npcId)
    {
        if (!GameConfigDao.GetBool(db, "use_storage_item_sort", false))
            return new StorageResponse { storage = Snapshot(npcId, db, c, LoadItems(db, c.accountid)) };

        var items = LoadItems(db, c.accountid);
        var rules = new Server.Bot.Trade.TradeItemRules(db, Server.Bot.BotItemCatalog.Shared);
        // mergeItems：同 id 同 owner、非充值非唯一才合并；装备/现金换位，普通叠数量（超出上限拆开）
        bool byName = GameConfigDao.GetBool(db, "use_item_sort_by_name", false);
        var merged = new List<InventoryEntry>();
        foreach (var item in items)
        {
            if (TryMergeInto(merged, item, rules.SlotMax(item.itemId, new Server.Bot.BotActor { id = (int)c.id }))) continue;
            merged.Add(item);
        }
        // sortItems：inventoryType 主序，次序 itemId（use_item_sort_by_name=true 时按名字）
        merged.Sort((a, b) =>
        {
            if (a.inventoryType != b.inventoryType) return a.inventoryType.CompareTo(b.inventoryType);
            return byName ? string.CompareOrdinal(NativeItemName(a.itemId), NativeItemName(b.itemId))
                          : a.itemId.CompareTo(b.itemId);
        });

        // 全量重写仓库行（源端 sortItems 重建 inventory 后由 saveToDB 落库）
        foreach (var item in items) DeleteStorageItem(db, item);
        foreach (var item in merged) WriteStorageEntry(db, c.accountid, item);
        db.Save();
        return new StorageResponse { storage = Snapshot(npcId, db, c, LoadItems(db, c.accountid)) };
    }

    static bool TryMergeInto(List<InventoryEntry> merged, InventoryEntry item, int slotMax)
    {
        if (item.isEquip || Server.Bot.Trade.TradeItemRules.Rechargeable(item.itemId)) return false;
        if (Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(item.itemId), "only") == 1) return false;
        foreach (var dst in merged)
        {
            if (dst.isEquip || dst.itemId != item.itemId || dst.owner != item.owner) continue;
            if (Server.Bot.Trade.TradeItemRules.Rechargeable(dst.itemId)) continue;
            if (dst.quantity >= slotMax) continue;
            if (dst.quantity + item.quantity <= slotMax) { dst.quantity += item.quantity; return true; }
            int take = slotMax - dst.quantity;
            dst.quantity = slotMax;
            item.quantity -= take;
        }
        return item.quantity <= 0;
    }

    static string NativeItemName(int id) => Server.Bot.BotItemCatalog.Shared.Name(id) ?? "";

    // CashOperationHandler action 0x07：现金买仓库格（每次 4/8 个），上限 48。
    static StorageResponse IncreaseSlots(DbStore db, CharRecord c, int slots)
    {
        int current = Slots(db, c.accountid);
        if (current + slots > MaxSlots)
            return new StorageResponse { ok = false, error = "仓库格已达上限（48）" };
        StorageRow(db, c.accountid, true)["slots"] = (long)(current + slots);
        db.Save();
        return new StorageResponse { storage = Snapshot(0, db, c, LoadItems(db, c.accountid)) };
    }

    // ————————————————— 辅助 —————————————————

    static StorageResponse ResponseAfter(DbStore db, CharRecord c, int npcId, List<InventoryEntry> _, long __, bool stored)
    {
        var res = new StorageResponse { storage = Snapshot(npcId, db, c, LoadItems(db, c.accountid)), inventory = CharService.Load(db, (int)c.id).inventory, meso = c.meso };
        return res;
    }

    static StorageResponse Fail(byte code, string error)
        => new StorageResponse { ok = false, code = code, error = error };

    static DbRow InventoryRow(DbStore db, int cid, long itemRowId)
        => db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid", -1) == itemRowId);

    // KarmaManipulator.toggleKarmaFlagToUntradeable：KARMA_EQP(0x10)/KARMA_USE(0x02) → UNTRADEABLE(0x08)
    static void KarmaToUntradeable(InventoryEntry item)
    {
        int karma = item.isEquip ? 0x10 : 0x02;
        if ((item.flag & karma) == karma) item.flag = item.flag & ~karma | 0x08;
    }

    // ItemId.isWeddingRing / isWeddingToken
    static bool IsWeddingItem(int itemId)
        => itemId == 1112803 || itemId == 1112806 || itemId == 1112807 || itemId == 1112809
           || (itemId >= 4031357 && itemId <= 4031364);
}
