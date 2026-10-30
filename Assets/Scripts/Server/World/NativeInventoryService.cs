using System;
using System.Collections.Generic;
public sealed class InventoryChangeRequest { public int characterId, itemId, quantity, equipSlot, sourcePosition; public bool equip, projectile; }
public sealed class InventoryChangeResponse { public bool ok = true; public string error = ""; public List<InventoryEntry> inventory; }
public static class NativeInventoryService {
    // Existing gameplay is client-authoritative; persistence still runs inside transport.
    public static InventoryChangeResponse Handle(DbStore db, InventoryChangeRequest r) {
        var c = CharactersDao.FindById(db, r.characterId);
        if (c == null || r.itemId <= 0) return new InventoryChangeResponse { ok = false, error = "角色或道具不存在" };
        var items = db.Table("inventoryitems");
        if (r.equip) {
            int slot = EquipmentSlotOf(r.itemId);
            if (slot == -12 && (r.equipSlot == -12 || r.equipSlot == -13 || r.equipSlot == -15 || r.equipSlot == -16)) slot = r.equipSlot;
            if (slot == 0 || slot != r.equipSlot) return new InventoryChangeResponse { ok = false, error = "无效装备槽位" };
            var source = items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "itemid") == r.itemId &&
                (r.quantity == 0 ? DbStore.Int(x, "position") == slot : DbStore.Int(x, "position") > 0 && (r.sourcePosition == 0 || DbStore.Int(x, "position") == r.sourcePosition)));
            if (source == null) return new InventoryChangeResponse { ok = false, error = "没有持有这件装备" };
            int oldPos = DbStore.Int(source, "position");
            if (r.quantity == 0) {
                int free = Free(db, c, 1);
                if (free == 0) return new InventoryChangeResponse { ok = false, error = "装备栏已满" };
                source["position"] = (long)free; source["inventorytype"] = 1L;
            } else {
                // InventoryManipulator.equip: overall/pants and two-handed weapon/shield conflicts move to a free slot.
                int conflictSlot = slot == -6 ? -5 : slot == -5 && r.itemId / 10000 == 105 ? -6 :
                    slot == -10 ? -11 : slot == -11 && TwoHanded(r.itemId) ? -10 : 0;
                var conflict = conflictSlot == 0 ? null : items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "position") == conflictSlot && DbStore.Int(x, "inventorytype") == InventoryDao.Equipped);
                if (conflict != null && (slot != -6 || DbStore.Int(conflict, "itemid") / 10000 == 105) && (slot != -10 || TwoHanded(DbStore.Int(conflict, "itemid")))) {
                    int free = Free(db, c, 1);
                    if (free == 0) return new InventoryChangeResponse { ok = false, error = "装备栏已满" };
                    conflict["position"] = (long)free; conflict["inventorytype"] = 1L;
                }
                var prior = items.Find(x => DbStore.Int(x, "characterid") == r.characterId && DbStore.Int(x, "position") == slot && DbStore.Int(x, "inventorytype") == InventoryDao.Equipped);
                if (prior != null) { prior["position"] = (long)oldPos; prior["inventorytype"] = 1L; }
                source["position"] = (long)slot; source["inventorytype"] = (long)InventoryDao.Equipped;
            }
        } else if (r.projectile) {
            bool rechargeable=r.itemId/10000==207 || r.itemId/10000==233;
            bool arrow=r.itemId/1000==2060 || r.itemId/1000==2061;
            var source=items.Find(x=>DbStore.Int(x,"characterid")==r.characterId && DbStore.Int(x,"itemid")==r.itemId &&
                DbStore.Int(x,"inventorytype")==2 && DbStore.Int(x,"position")==r.sourcePosition && r.sourcePosition>0);
            if ((!rechargeable && !arrow) || source==null || r.quantity<short.MinValue || r.quantity>short.MaxValue)
                return new InventoryChangeResponse { ok=false,error="弹药槽位或数量无效" };
            long remaining=Math.Max(0,(int)unchecked((short)(DbStore.Int(source,"quantity")+r.quantity)));
            if (remaining>0 || rechargeable) source["quantity"]=remaining;
            else { long id=DbStore.Long(source,"inventoryitemid"); items.Delete(x=>DbStore.Long(x,"inventoryitemid")==id); }
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
            if (r.sourcePosition!=0) {
                var source=items.Find(x=>DbStore.Int(x,"characterid")==r.characterId && DbStore.Int(x,"itemid")==r.itemId &&
                    DbStore.Int(x,"inventorytype")==r.itemId/1000000 && DbStore.Int(x,"position")==r.sourcePosition && r.sourcePosition>0);
                if (source==null || r.quantity==int.MinValue || DbStore.Int(source,"quantity") < -r.quantity)
                    return new InventoryChangeResponse { ok=false,error="指定槽位数量不足" };
                int remaining=DbStore.Int(source,"quantity")+r.quantity;
                if (remaining>0) source["quantity"]=(long)remaining;
                else { long id=DbStore.Long(source,"inventoryitemid"); items.Delete(x=>DbStore.Long(x,"inventoryitemid")==id);
                    db.Table("inventoryequipment").Delete(x=>DbStore.Long(x,"inventoryitemid")==id); }
            } else {
            int total = InventoryDao.CountOf(db, r.characterId, r.itemId); if (total < -r.quantity) return new InventoryChangeResponse { ok = false, error = "持有数量不足" };
            InventoryDao.ConsumeItem(db, r.characterId, r.itemId, -r.quantity);
            }
        }
        db.Save(); return new InventoryChangeResponse { inventory = CharService.Load(db, r.characterId).inventory };
    }
    // Pure server mapping: script harness must not depend on Unity's equipment presentation.
    public static int EquipmentSlotOf(int itemId)
    {
        return -Server.Bot.Decorator.BotCustomization.EquipSlotType(itemId);
    }

    static bool TwoHanded(int id) => id / 10000 >= 140 && id / 10000 <= 149;
    static int Free(DbStore db, CharRecord c, int type) {
        int max = type == 1 ? c.equipslots : type == 2 ? c.useslots : type == 3 ? c.setupslots : type == 4 ? c.etcslots : 96;
        var used = new HashSet<int>(); foreach (var row in InventoryDao.ItemsByCharacter(db, (int)c.id)) if (DbStore.Int(row, "inventorytype") == type) used.Add(DbStore.Int(row, "position"));
        for (int i = 1; i <= max; i++) if (!used.Contains(i)) return i; return 0;
    }
}
