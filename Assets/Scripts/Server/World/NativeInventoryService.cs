using System;
using System.Collections.Generic;
public sealed class InventoryChangeRequest { public int characterId, itemId, quantity, equipSlot; public bool equip; }
public sealed class InventoryChangeResponse { public bool ok = true; public string error = ""; public List<InventoryEntry> inventory; }
public static class NativeInventoryService {
    // Existing gameplay is client-authoritative; persistence still runs inside transport.
    public static InventoryChangeResponse Handle(DbStore db, InventoryChangeRequest r) {
        var c = CharactersDao.FindById(db, r.characterId);
        if (c == null || r.itemId <= 0) return new InventoryChangeResponse { ok = false, error = "角色或道具不存在" };
        var items = db.Table("inventoryitems");
        if (r.equip) {
            if (r.itemId / 10000 != 114 || r.equipSlot != -49) return new InventoryChangeResponse { ok = false, error = "无效勋章槽位" };
            var medal = items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "itemid") == r.itemId);
            if (medal == null) return new InventoryChangeResponse { ok = false, error = "没有持有这枚勋章" };
            int oldPos = DbStore.Int(medal, "position");
            if (r.quantity == 0) { int free = Free(db, c, 1); if (free == 0) return new InventoryChangeResponse { ok = false, error = "装备栏已满" }; medal["position"] = (long)free; }
            else { var prior = items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "position") == -49); if (prior != null) prior["position"] = (long)oldPos; medal["position"] = -49L; }
        } else if (r.quantity > 0) {
            int type = r.itemId / 1000000;
            var existing = type == 1 ? null : items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "itemid") == r.itemId && DbStore.Int(x, "position") > 0 && DbStore.Long(x, "expiration") < 0);
            if (existing != null) existing["quantity"] = (long)(DbStore.Int(existing, "quantity") + r.quantity);
            else {
                int position = Free(db, c, type); if (position == 0) return new InventoryChangeResponse { ok = false, error = "道具栏已满" };
                long id = InventoryDao.InsertItem(db, r.characterId, r.itemId, type, position, r.quantity);
                if (type == 1) InventoryDao.InsertEquipment(db, id);
            }
        } else if (r.quantity < 0) {
            int total = InventoryDao.CountOf(db, r.characterId, r.itemId); if (total < -r.quantity) return new InventoryChangeResponse { ok = false, error = "持有数量不足" };
            InventoryDao.ConsumeItem(db, r.characterId, r.itemId, -r.quantity);
        }
        db.Save(); return new InventoryChangeResponse { inventory = CharService.Load(db, r.characterId).inventory };
    }
    static int Free(DbStore db, CharRecord c, int type) {
        int max = type == 1 ? c.equipslots : type == 2 ? c.useslots : type == 3 ? c.setupslots : type == 4 ? c.etcslots : 96;
        var used = new HashSet<int>(); foreach (var row in InventoryDao.ItemsByCharacter(db, (int)c.id)) if (DbStore.Int(row, "inventorytype") == type) used.Add(DbStore.Int(row, "position"));
        for (int i = 1; i <= max; i++) if (!used.Contains(i)) return i; return 0;
    }
}
