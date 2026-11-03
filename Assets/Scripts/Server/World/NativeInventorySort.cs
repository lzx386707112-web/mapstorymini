using System;
using System.Collections.Generic;

// 背包整理（ITEM_SORT2）/ 合并（ITEM_SORT）—— 对照 gms-server：
//   net/server/channel/handlers/InventorySortHandler.java  （PairedQuicksort + 二次排序）
//   net/server/channel/handlers/InventoryMergeHandler.java （RonanLana 的槽位合并 + 紧排）
//   client/inventory/Inventory.java:264-303             （move：同 id 合并 / 否则 swap）
//   client/inventory/manipulator/InventoryManipulator.java:489-529（同 owner、非充能才合并）
//
// 两条硬约束：
//  1. 排序必须照抄 PairedQuicksort（不稳定快排）。换成 OrderBy 会让相同 itemId 的多个
//     堆叠保持原相对次序，与源端不一致。
//  2. 合并走 move 的完整分支，不是「同 id 就相加」：owner 不同或可充能弹药会 swap 槽位。
public static class NativeInventorySort
{
    const int Equip = 1, Cash = 5;

    sealed class Slot
    {
        public int Index;
        public DbRow Row;          // inventoryitems 行，写 position 用
        public InventoryEntry Item;
    }

    // 源端 Inventory.getSlotLimit：装备/消耗/设置/其他/特殊 各自容量。
    static int SlotLimit(DbStore db, CharRecord c, int type)
        => type == 1 ? c.equipslots : type == 2 ? c.useslots : type == 3 ? c.setupslots : type == 4 ? c.etcslots : 96;

    static bool Rechargeable(int itemId) => itemId / 10000 == 207;

    // 源端 ItemInformationProvider.getWatkForProjectile：info/incPAD。
    static int ProjectileWatk(int itemId)
    {
        var info = Server.Bot.BotItemCatalog.Shared.Info(itemId);
        return info == null ? 0 : Server.Bot.BotItemCatalog.Int(info, "incPAD");
    }

    // 源端 InventorySortHandler 里排序后的落位：全部移除再按新顺序 1..n 重排。
    static InventoryChangeResponse ApplyOrder(DbStore db, List<Slot> ordered, int characterId)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Row["position"] = (long)(i + 1);
            ordered[i].Item.position = i + 1;
        }
        return null;
    }

    static List<Slot> LoadSlots(DbStore db, int characterId, int type)
    {
        var slots = new List<Slot>();
        foreach (var row in InventoryDao.ItemsByCharacter(db, characterId))
        {
            if (DbStore.Int(row, "inventorytype") != type) continue;
            int position = DbStore.Int(row, "position");
            if (position <= 0) continue;              // 已穿戴（position<0）不参与整理
            slots.Add(new Slot { Index = position, Row = row, Item = CharService.ToInventoryEntry(db, row) });
        }
        slots.Sort((a, b) => a.Index.CompareTo(b.Index));
        return slots;
    }

    // ───────────────────────── 整理 ─────────────────────────

    public static InventoryChangeResponse Sort(DbStore db, CharRecord c, int invType)
    {
        int cid = (int)c.id;
        if (!GameConfigDao.GetBool(db, "use_item_sort", true))
            return new InventoryChangeResponse { ok = true, inventory = CharService.Load(db, cid).inventory };
        if (invType < 1 || invType > 5) return new InventoryChangeResponse { ok = false, error = "背包页无效" };

        var slots = LoadSlots(db, cid, invType);
        if (slots.Count == 0)
            return new InventoryChangeResponse { ok = true, inventory = CharService.Load(db, cid).inventory };

        var items = new List<Slot>(slots);
        // InventorySortHandler：主键 = use_item_sort_by_name ? 名称(2) : itemId(0)；
        // 副键 = 装备按等级(3)、其余按数量(1)。
        int primary = GameConfigDao.GetBool(db, "use_item_sort_by_name", false) ? 2 : 0;
        int secondary = invType == Equip ? 3 : 1;
        PairedQuicksort(items, primary, secondary, invType);

        ApplyOrder(db, items, cid);
        db.Save();
        return new InventoryChangeResponse { ok = true, inventory = CharService.Load(db, cid).inventory };
    }

    // 源端 PairedQuicksort 构造函数：先按 primary 全排，USE 栏把 206/207/233 三段按
    // projectile watk 再 partition 一次，最后按 itemId 分组、组内用 secondary 排。
    static void PairedQuicksort(List<Slot> a, int primary, int secondary, int invType)
    {
        if (a.Count == 0) return;
        MapleQuicksort(a, 0, a.Count - 1, primary);

        if (invType == InventoryDao.Use)
        {
            ReverseSortSublist(a, BinarySearchElement(a, 206));   // 箭
            ReverseSortSublist(a, BinarySearchElement(a, 207));   // 飞镖
            ReverseSortSublist(a, BinarySearchElement(a, 233));   // 子弹
        }

        var intersect = new List<int> { 0 };
        for (int i = 1; i < a.Count; i++)
            if (a[i - 1].Item.itemId != a[i].Item.itemId) intersect.Add(i);
        intersect.Add(a.Count);

        for (int i = 0; i < intersect.Count - 1; i++)
            if (intersect[i + 1] > intersect[i])
                MapleQuicksort(a, intersect[i], intersect[i + 1] - 1, secondary);
    }

    static void MapleQuicksort(List<Slot> a, int esq, int dir, int sort)
    {
        int i, j;
        switch (sort)
        {
            case 3: (i, j) = PartitionByLevel(a, esq, dir); break;
            case 2: (i, j) = PartitionByName(a, esq, dir); break;
            case 1: (i, j) = PartitionByQuantity(a, esq, dir); break;
            default: (i, j) = PartitionByItemId(a, esq, dir); break;
        }
        if (esq < j) MapleQuicksort(a, esq, j, sort);
        if (i < dir) MapleQuicksort(a, i, dir, sort);
    }

    static (int, int) PartitionByItemId(List<Slot> a, int esq, int dir)
    {
        int i = esq, j = dir;
        int x = a[(i + j) / 2].Item.itemId;
        do
        {
            while (x > a[i].Item.itemId) i++;
            while (x < a[j].Item.itemId) j--;
            if (i <= j) { var w = a[i]; a[i] = a[j]; a[j] = w; i++; j--; }
        } while (i <= j);
        return (i, j);
    }

    static (int, int) PartitionByQuantity(List<Slot> a, int esq, int dir)
    {
        int i = esq, j = dir;
        int x = a[(i + j) / 2].Item.quantity;
        do
        {
            while (x > a[i].Item.quantity) i++;
            while (x < a[j].Item.quantity) j--;
            if (i <= j) { var w = a[i]; a[i] = a[j]; a[j] = w; i++; j--; }
        } while (i <= j);
        return (i, j);
    }

    // 源端 Equip.getLevel()：inventoryequipment.level（不是需求等级，也不是星级）。
    static int EquipLevel(InventoryEntry e) => e.equipLevel;

    static (int, int) PartitionByLevel(List<Slot> a, int esq, int dir)
    {
        int i = esq, j = dir;
        int x = EquipLevel(a[(i + j) / 2].Item);
        do
        {
            while (x > EquipLevel(a[i].Item)) i++;
            while (x < EquipLevel(a[j].Item)) j--;
            if (i <= j) { var w = a[i]; a[i] = a[j]; a[j] = w; i++; j--; }
        } while (i <= j);
        return (i, j);
    }

    // Java String.compareTo 是 UTF-16 码元序，对应 CompareOrdinal。
    static (int, int) PartitionByName(List<Slot> a, int esq, int dir)
    {
        int i = esq, j = dir;
        string x = Server.Bot.BotItemCatalog.Shared.Name(a[(i + j) / 2].Item.itemId) ?? "";
        do
        {
            while (string.CompareOrdinal(x, Server.Bot.BotItemCatalog.Shared.Name(a[i].Item.itemId) ?? "") > 0) i++;
            while (string.CompareOrdinal(x, Server.Bot.BotItemCatalog.Shared.Name(a[j].Item.itemId) ?? "") < 0) j--;
            if (i <= j) { var w = a[i]; a[i] = a[j]; a[j] = w; i++; j--; }
        } while (i <= j);
        return (i, j);
    }

    static (int, int) PartitionByProjectileAtk(List<Slot> a, int esq, int dir)
    {
        int i = esq, j = dir;
        int watk = ProjectileWatk(a[(i + j) / 2].Item.itemId);
        do
        {
            while (watk < ProjectileWatk(a[i].Item.itemId)) i++;
            while (watk > ProjectileWatk(a[j].Item.itemId)) j--;
            if (i <= j) { var w = a[i]; a[i] = a[j]; a[j] = w; i++; j--; }
        } while (i <= j);
        return (i, j);
    }

    static int Subtype(InventoryEntry e) => e.itemId / 10000;

    // 二分找到 itemId/10000 == rangeId 的整段，找不到返回 null。
    static int[] BinarySearchElement(List<Slot> a, int rangeId)
    {
        int st = 0, en = a.Count - 1, idx = -1;
        while (en >= st)
        {
            idx = (st + en) / 2;
            int mid = Subtype(a[idx].Item);
            if (mid == rangeId) break;
            if (mid < rangeId) st = idx + 1; else en = idx - 1;
        }
        if (en < st) return null;

        st = idx - 1; en = idx + 1;
        while (st >= 0 && Subtype(a[st].Item) == rangeId) st -= 1;
        st += 1;
        while (en < a.Count && Subtype(a[en].Item) == rangeId) en += 1;
        en -= 1;
        return new[] { st, en };
    }

    // 源端只做一次 partition（不递归），这是原端的近似倒序，照抄。
    static void ReverseSortSublist(List<Slot> a, int[] range)
    {
        if (range != null) PartitionByProjectileAtk(a, range[0], range[1]);
    }

    // ───────────────────────── 合并 ─────────────────────────

    public static InventoryChangeResponse Merge(DbStore db, CharRecord c, int invType)
    {
        int cid = (int)c.id;
        if (!GameConfigDao.GetBool(db, "use_item_sort", true))
            return new InventoryChangeResponse { ok = true, inventory = CharService.Load(db, cid).inventory };
        if (invType < 1 || invType > 5) return new InventoryChangeResponse { ok = false, error = "背包页无效" };

        var slots = LoadSlots(db, cid, invType);
        if (slots.Count == 0)
            return new InventoryChangeResponse { ok = true, inventory = CharService.Load(db, cid).inventory };

        int limit = SlotLimit(db, c, invType);
        var actor = new Server.Bot.BotActor { id = cid, job = c.job, character = CharService.Load(db, cid) };
        var rules = new Server.Bot.Trade.TradeItemRules(db, Server.Bot.BotItemCatalog.Shared);

        // position → slot（保持对象引用，move 后引用可能换槽，与源端 Java 的 dictionary 一致）
        var map = new Dictionary<int, Slot>();
        foreach (var s in slots) map[s.Index] = s;

        Slot At(int position) => map.TryGetValue(position, out var v) ? v : null;

        int SlotMaxOf(int itemId) => rules.SlotMax(itemId, actor);

        // 第一段：RonanLana's SLOT MERGER —— dst 从头扫，src 从 dst+1 扫，
        // 同 itemId 且 dst 未满就 move(src→dst)；dst 满了直接 break 换下一个 dst。
        for (int dst = 1; dst <= limit; dst++)
        {
            var dstSlot = At(dst);
            if (dstSlot == null) continue;

            for (int src = dst + 1; src <= limit; src++)
            {
                var srcSlot = At(src);
                if (srcSlot == null) continue;
                if (dstSlot.Item.itemId != srcSlot.Item.itemId) continue;
                // 源端用同一个 dstItem 引用判满；move 可能把它换槽，但 quantity 判断不变。
                if (dstSlot.Item.quantity == SlotMaxOf(dstSlot.Item.itemId)) break;
                Move(map, invType, src, dst, SlotMaxOf(srcSlot.Item.itemId));
            }
        }

        // 第二段：把后面的物品前移，填掉空洞（源端的 while(!sorted) 循环）。
        int free = 1;
        while (true)
        {
            while (free <= limit && At(free) != null) free++;
            if (free > limit) break;
            int itemSlot = -1;
            for (int i = free + 1; i <= limit; i++) if (At(i) != null) { itemSlot = i; break; }
            if (itemSlot <= 0) break;
            Move(map, invType, itemSlot, free, SlotMaxOf(At(itemSlot).Item.itemId));
        }

        // 落库：先按最终槽位写 position，再删掉已并为空的源行。
        Persist(db, slots, map);

        db.Save();
        return new InventoryChangeResponse { ok = true, inventory = CharService.Load(db, cid).inventory };
    }

    // 槽位 → 行/实例 的最终落库：写 position，删掉被并掉的行（源行 position 置 0）。
    static void Persist(DbStore db, List<Slot> slots, Dictionary<int, Slot> map)
    {
        foreach (var pair in map)
        {
            pair.Value.Row["position"] = (long)pair.Key;
            pair.Value.Item.position = pair.Key;
        }
        var alive = new HashSet<long>();
        foreach (var pair in map) alive.Add(pair.Value.Item.itemRowId);
        foreach (var s in slots)
            if (!alive.Contains(s.Item.itemRowId))
            {
                long rowId = s.Item.itemRowId;
                db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == rowId);
                db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == rowId);
            }
    }

    // ───────────────── ITEM_MOVE(action>0)：格到格 ─────────────────
    // 源端 ItemMoveHandler:54（action>0）→ InventoryManipulator.move(c, type, src, dst)
    //   → Inventory.move(src, dst, slotMax)：dst 空 → 改 position；同 id 且非充能、owner
    //   相同 → 相加（超 slotMax 时 dst 填满、源留余量）；其余 → 两个槽位互换。
    // 客户端 0x4F3057 在发请求前已限定「同背包页 + 源格≠目标格」，这里再兜一次。
    public static InventoryChangeResponse MoveItem(DbStore db, CharRecord c, int invType, int from, int to)
    {
        int cid = (int)c.id;
        if (invType < 1 || invType > 5) return new InventoryChangeResponse { ok = false, error = "背包页无效" };
        int limit = SlotLimit(db, c, invType);
        if (from <= 0 || to <= 0 || from == to || from > limit || to > limit)
            return new InventoryChangeResponse { ok = false, error = "槽位无效" };

        var slots = LoadSlots(db, cid, invType);
        var map = new Dictionary<int, Slot>();
        foreach (var s in slots) map[s.Index] = s;
        if (!map.TryGetValue(from, out var source)) return new InventoryChangeResponse { ok = false, error = "该格子没有物品" };

        var actor = new Server.Bot.BotActor { id = cid, job = c.job, character = CharService.Load(db, cid) };
        var rules = new Server.Bot.Trade.TradeItemRules(db, Server.Bot.BotItemCatalog.Shared);
        Move(map, invType, from, to, rules.SlotMax(source.Item.itemId, actor));

        Persist(db, slots, map);
        db.Save();
        return new InventoryChangeResponse { ok = true, inventory = CharService.Load(db, cid).inventory };
    }

    // Inventory.move(sSlot, dSlot, slotMax) 的完整分支（Inventory.java:264-303）。
    static void Move(Dictionary<int, Slot> map, int invType, int src, int dst, int slotMax)
    {
        if (src < 0 || dst < 0) return;
        if (!map.TryGetValue(src, out var source)) return;
        if (!map.TryGetValue(dst, out var target))
        {
            map.Remove(src); map[dst] = source;
            source.Row["position"] = (long)dst; source.Item.position = dst;
            return;
        }

        bool sameStack = target.Item.itemId == source.Item.itemId
                         && !Rechargeable(source.Item.itemId)
                         && string.Equals(target.Item.owner ?? "", source.Item.owner ?? "", StringComparison.Ordinal);
        if (sameStack && invType != Equip && invType != Cash)
        {
            int total = source.Item.quantity + target.Item.quantity;
            if (total > slotMax)
            {
                source.Item.quantity = total - slotMax;
                target.Item.quantity = slotMax;
                source.Row["quantity"] = (long)source.Item.quantity;
                target.Row["quantity"] = (long)target.Item.quantity;
            }
            else
            {
                map.Remove(src);
                target.Item.quantity = total;
                target.Row["quantity"] = (long)total;
                // 源行已被并掉：position 置 0，落库阶段按 alive 集合删除。
                source.Item.position = 0;
            }
            return;
        }

        // swap：两件物品交换槽位。
        map[src] = target; map[dst] = source;
        source.Row["position"] = (long)dst; source.Item.position = dst;
        target.Row["position"] = (long)src; target.Item.position = src;
    }
}
