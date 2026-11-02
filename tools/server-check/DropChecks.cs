// 背包丢弃回归：源端没有独立的 ITEM_DROP 包，丢弃是 ITEM_MOVE(0x47) 的 action==0 分支
// （ItemMoveHandler:44 → InventoryManipulator.drop）。这里验证那条服务器的语义：
//   - quantity < 堆叠数 → 拆一份出去，原格数量减少
//   - quantity >= 堆叠数 → 整格移除（装备连 inventoryequipment 一起删）
//   - 数量非法（<=0 / 超出堆叠）→ 拒绝且不动背包
//   - 落地一律 spawnItemDrop(dropper=owner=本人, playerDrop=true, ffaDrop=true) → dropType 2
using System;
using System.Collections.Generic;
using System.IO;
using Server.Field;

static class DropChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 背包丢弃（ITEM_MOVE action=0 → InventoryManipulator.drop）==");
        string path = Path.Combine(Path.GetTempPath(), "mapledash_drop_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        var spawned = new List<MapItem>();
        Action<MapItem> sink = item => spawned.Add(item);
        MapItemService.Spawned += sink;
        try
        {
            var login = LoginService.Login(db, "droptest", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "DropHero", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            Check(made.code == 0, "建号成功");
            int cid = made.characterId;

            InventoryDao.InsertItem(db, cid, 2000000, InventoryDao.Use, 1, 10);
            Check(InventoryDao.CountOf(db, cid, 2000000) == 10, "背包里有 10 个药水");

            InventoryChangeResponse Drop(int itemId, int quantity, int position = 0)
                => NativeInventoryService.Handle(db, new InventoryChangeRequest
                {
                    characterId = cid, itemId = itemId, sourcePosition = position,
                    dropToField = true, dropQuantity = quantity, dropMapId = 10000, dropX = 120, dropY = 300,
                });

            // 1. 拆堆丢弃：源端 source.quantity(10) > quantity(4) → 拆一份出去
            var split = Drop(2000000, 4);
            Check(split.ok, "拆堆丢弃成功");
            Check(InventoryDao.CountOf(db, cid, 2000000) == 6, "原格剩 6 个");
            Check(spawned.Count == 1 && spawned[0].ItemId == 2000000 && spawned[0].Quantity == 4, "地面掉出 4 个药水");
            Check(spawned.Count == 1 && spawned[0].type == 2 && spawned[0].playerDrop, "ffaDrop/playerDrop → dropType 2");
            Check(spawned.Count == 1 && spawned[0].dropperId == cid && spawned[0].ownerCharacterId == cid, "掉落者与归属者都是本人");
            Check(spawned.Count == 1 && spawned[0].mapId == 10000 && spawned[0].IsMeso == false, "落在当前地图且算道具掉落");
            Check(split.inventory != null && split.inventory.Exists(x => x.itemId == 2000000 && x.quantity == 6), "回包带最新背包");

            // 2. 数量非法：源端 quantity > source.quantity 直接 return，不动背包
            var tooMany = Drop(2000000, 99);
            Check(!tooMany.ok && InventoryDao.CountOf(db, cid, 2000000) == 6 && spawned.Count == 1, "超出堆叠数拒绝且不扣");

            var zero = Drop(2000000, 0);
            Check(!zero.ok && InventoryDao.CountOf(db, cid, 2000000) == 6, "数量 0 拒绝且不扣");
            var negative = Drop(2000000, -3);
            Check(!negative.ok && InventoryDao.CountOf(db, cid, 2000000) == 6, "负数数量拒绝且不扣");

            // 3. 整堆丢弃：quantity == 堆叠数 → 整格移除
            int remain = InventoryDao.CountOf(db, cid, 2000000);
            var whole = Drop(2000000, remain);
            Check(whole.ok && InventoryDao.CountOf(db, cid, 2000000) == 0, "整堆丢弃后该格消失");
            Check(spawned.Count == 2 && spawned[1].Quantity == remain, "地面掉出整堆");

            // 4. 装备：整格移除同时删 inventoryequipment 行
            long spareRow = InventoryDao.InsertItem(db, cid, 1040002, InventoryDao.Equip, 1, 1);
            InventoryDao.InsertEquipment(db, spareRow, upgradeslots: 7);
            db.Save();
            var equipRow = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid &&
                DbStore.Int(x, "itemid") == 1040002 && DbStore.Int(x, "position") > 0);
            Check(equipRow != null, "背包里有上衣 1040002");
            int equipPos = DbStore.Int(equipRow, "position");
            var equipDrop = Drop(1040002, 1, equipPos);
            Check(equipDrop.ok && InventoryDao.CountOf(db, cid, 1040002) == 0, "丢弃装备后背包格消失");
            Check(db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == spareRow) == null &&
                db.Table("inventoryequipment").Find(x => DbStore.Long(x, "inventoryitemid") == spareRow) == null,
                "背包行与 inventoryequipment 行一并删除");
            Check(spawned.Count == 3 && spawned[2].ItemId == 1040002 && spawned[2].Quantity == 1, "地面掉出装备");

            // 5. 不存在的格
            var missing = Drop(2000000, 1);
            Check(!missing.ok, "没有这件物品时拒绝");
        }
        finally
        {
            MapItemService.Spawned -= sink;
            MapItemService.ClearAll();
            db.Save();
            File.Delete(path);
        }
    }
}
