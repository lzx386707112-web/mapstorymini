using System;
using System.Collections.Generic;

// 装备实例改写（源端 Character.forceUpdateItem + Equip 的 setStarLevel/setStarCount/setWatk…）。
//
// 星之力强化脚本（scripts-zh-CN/BeiDouSpecial/装备制作/星级强化.js）改的是内存里的 Equip 对象：
//   addStarStats → item.setStr/setWatk…、item.setStarLevel、item.setStarCount、item.setMaxStar、
//   item.setOwner("<N>★")，最后 forceUpdateItem(item) 让服务端落库并刷新客户端。
// 本工程背包真源在 DB（inventoryitems + inventoryequipment），所以这里做「整行覆盖」：
// 请求带当前星力与全部属性，服务端把 equipment 行盖成这份值，再回最新背包快照。
public sealed class EquipPatchRequest
{
    public int characterId;
    public long inventoryItemId;
    public long? expiration;
    public int starLevel, starCount, maxStar;
    public string owner;
    public int str, dex, int_, luk, hp, mp, watk, matk, wdef, mdef, acc, avoid, hands, speed, jump, upgradeslots;
    public int scrollId, scrollPosition, vegaItemId, cashItemId;
    public bool whiteScroll;
    public int[] scrollRequirements;
}

public sealed class EquipPatchResponse
{
    public bool ok = true;
    public string error = "";
    public string scrollResult;
    public List<InventoryEntry> inventory;
}

// InventoryManipulator.removeFromSlot(Client, type, slot, quantity, fromDrop)：
// 源端按「栏位 + 格号」删。本工程 inventoryitems.position 就是格号（已装备用负位置），
// 所以能照原样实现：够扣就删整行（连同 inventoryequipment），不够扣就减数量。
public sealed class SlotRemoveRequest
{
    public int characterId, inventoryType, slot, quantity;
}

public sealed class SlotRemoveResponse
{
    public bool ok = true;
    public string error = "";
    public List<InventoryEntry> inventory;
}

public static class NativeEquipService
{
    public static EquipPatchResponse Patch(DbStore db, EquipPatchRequest r)
    {
        if(r!=null && r.cashItemId>0) return NativeCashEquipmentService.Apply(db,r);
        if(r!=null && r.scrollId>0) return NativeScrollService.Apply(db,r);
        if (r == null || r.inventoryItemId <= 0) return new EquipPatchResponse { ok = false, error = "无效装备" };
        var row = db.Table("inventoryitems").Find(x =>
            DbStore.Long(x, "inventoryitemid") == r.inventoryItemId &&
            DbStore.Int(x, "characterid") == r.characterId);
        if (row == null) return new EquipPatchResponse { ok = false, error = "找不到这件装备" };
        if(r.expiration.HasValue)
        {
            row["expiration"]=r.expiration.Value;
            db.Save();
            return new EquipPatchResponse {inventory=CharService.Load(db,r.characterId).inventory};
        }

        var eq = db.Table("inventoryequipment").Find(x => DbStore.Long(x, "inventoryitemid") == r.inventoryItemId);
        if (eq == null)
        {
            // 装备行缺失（老存档里只建了 inventoryitems）：补一行再写，避免「强化成功但没落库」。
            InventoryDao.InsertEquipment(db, r.inventoryItemId);
            eq = db.Table("inventoryequipment").Find(x => DbStore.Long(x, "inventoryitemid") == r.inventoryItemId);
            if (eq == null) return new EquipPatchResponse { ok = false, error = "装备行写入失败" };
        }

        eq["upgradeslots"] = (long)r.upgradeslots;
        eq["nativeStats"] = 1L;
        eq["str"] = (long)r.str; eq["dex"] = (long)r.dex;
        eq["int"] = (long)r.int_; eq["luk"] = (long)r.luk;
        eq["hp"] = (long)r.hp; eq["mp"] = (long)r.mp;
        eq["watk"] = (long)r.watk; eq["matk"] = (long)r.matk;
        eq["wdef"] = (long)r.wdef; eq["mdef"] = (long)r.mdef;
        eq["acc"] = (long)r.acc; eq["avoid"] = (long)r.avoid;
        eq["hands"] = (long)r.hands; eq["speed"] = (long)r.speed; eq["jump"] = (long)r.jump;
        eq["starLevel"] = (long)Math.Max(0, r.starLevel);
        eq["starCount"] = (long)Math.Max(0, r.starCount);
        eq["maxStar"] = (long)Math.Max(0, r.maxStar);
        if (r.owner != null) row["owner"] = r.owner;

        db.Save();
        return new EquipPatchResponse { inventory = CharService.Load(db, r.characterId).inventory };
    }

    public static SlotRemoveResponse RemoveSlot(DbStore db, SlotRemoveRequest r)
    {
        if (r == null) return new SlotRemoveResponse { ok = false, error = "无效请求" };
        int want = Math.Max(1, r.quantity);
        var row = db.Table("inventoryitems").Find(x =>
            DbStore.Int(x, "characterid") == r.characterId &&
            DbStore.Int(x, "inventorytype") == r.inventoryType &&
            DbStore.Int(x, "position") == r.slot);
        if (row == null) return new SlotRemoveResponse { ok = false, error = "该格位没有道具" };

        long id = DbStore.Long(row, "inventoryitemid");
        int have = DbStore.Int(row, "quantity", 1);
        if (have <= want)
        {
            db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid") == id);
            db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid") == id);
        }
        else row["quantity"] = (long)(have - want);

        db.Save();
        return new SlotRemoveResponse { inventory = CharService.Load(db, r.characterId).inventory };
    }
}
