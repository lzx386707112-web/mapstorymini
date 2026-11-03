// 背包格到格的移动回归：源端「点起道具再点另一格」走 ITEM_MOVE(0x47) 的 action>0 分支
//   ItemMoveHandler:54 → InventoryManipulator.move:489 → Inventory.move:264
// 语义（照抄，不换实现）：
//   - dst 空 → 源格改 position
//   - 同 id 且非 207 充能且 owner 相同 → 相加；超 slotMax 时 dst 填满、源留余量
//   - 其余（不同 id / 装备 / 现金 / 充能 / owner 不同）→ 两个槽位互换
//   - src==dst、越界、源格空 → 拒绝且不动背包
using System;
using System.IO;

static class InventoryMoveChecks
{
    static InventoryEntry At(System.Collections.Generic.List<InventoryEntry> inv, int type, int pos)
        => inv.Find(i => i.inventoryType == type && i.position == pos);

    static int ItemIdAt(System.Collections.Generic.List<InventoryEntry> inv, int type, int pos)
    {
        var e = At(inv, type, pos);
        return e == null ? 0 : e.itemId;
    }

    static int QtyAt(System.Collections.Generic.List<InventoryEntry> inv, int type, int pos)
    {
        var e = At(inv, type, pos);
        return e == null ? 0 : e.quantity;
    }

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 背包格移动 / 互换（ITEM_MOVE action>0）==");
        string path = Path.Combine(Path.GetTempPath(), "mapledash_move_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "movetest", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "MoveHero", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            Check(made.code == 0, "建号成功");
            int cid = made.characterId;
            var rec = CharactersDao.FindById(db, cid);
            var actor = new Server.Bot.BotActor { id = cid, job = rec.job, character = CharService.Load(db, cid) };
            var rules = new Server.Bot.Trade.TradeItemRules(db, Server.Bot.BotItemCatalog.Shared);

            void Clear(int type)
            {
                var ids = new System.Collections.Generic.HashSet<long>();
                foreach (var r in db.Table("inventoryitems").Select(x => DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "inventorytype") == type))
                    ids.Add(DbStore.Long(r, "inventoryitemid"));
                db.Table("inventoryitems").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
                db.Table("inventoryequipment").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
            }

            InventoryChangeResponse MoveTo(int type, int from, int to)
                => NativeInventoryService.Handle(db, new InventoryChangeRequest
                {
                    characterId = cid, moveFrom = from, moveTo = to, moveInventoryType = type,
                });

            // 1. 目标格为空 → 直接移动（Inventory.move 的 target==null 分支）
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 1, 3);
            var moved = MoveTo(InventoryDao.Etc, 1, 5);
            Check(moved.ok, "空格移动回包 ok");
            Check(ItemIdAt(moved.inventory, InventoryDao.Etc, 5) == 4000000 && QtyAt(moved.inventory, InventoryDao.Etc, 5) == 3,
                "道具整格搬到目标槽位（含数量）");
            Check(At(moved.inventory, InventoryDao.Etc, 1) == null, "源槽位被清空");

            // 2. 不同 id → 两个槽位互换
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000001, InventoryDao.Etc, 1, 1);
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 2, 1);
            var swapped = MoveTo(InventoryDao.Etc, 1, 2);
            Check(swapped.ok, "互换回包 ok");
            Check(ItemIdAt(swapped.inventory, InventoryDao.Etc, 1) == 4000002 && ItemIdAt(swapped.inventory, InventoryDao.Etc, 2) == 4000001,
                "两格物品互换位置");

            // 3. 同 id 可叠加 → 相加并删掉源格
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 1, 5);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 2, 7);
            var merged = MoveTo(InventoryDao.Etc, 1, 2);
            Check(merged.ok, "合并回包 ok");
            Check(QtyAt(merged.inventory, InventoryDao.Etc, 2) == 12 && At(merged.inventory, InventoryDao.Etc, 1) == null,
                "同 id 叠到目标格并删除源格");

            // 4. 超过 slotMax → 目标填满、源格留余量
            Clear(InventoryDao.Etc);
            int slotMax = rules.SlotMax(4000000, actor);
            Check(slotMax > 5, "测试道具 slotMax > 5（前置）");
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 1, 5);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 2, slotMax - 2);
            var overflow = MoveTo(InventoryDao.Etc, 1, 2);
            Check(overflow.ok, "超上限回包 ok");
            Check(QtyAt(overflow.inventory, InventoryDao.Etc, 2) == slotMax && QtyAt(overflow.inventory, InventoryDao.Etc, 1) == 3,
                "超 slotMax 时目标填满、源格留下余量");

            // 5. 装备栏同 id 不合并 → 互换槽位（Inventory.move 对 EQUIP 走 swap）
            Clear(InventoryDao.Equip);
            db.Table("inventoryequipment").Delete(x => false);
            long first = InventoryDao.InsertItem(db, cid, 1302000, InventoryDao.Equip, 1, 1);
            long second = InventoryDao.InsertItem(db, cid, 1302000, InventoryDao.Equip, 2, 1);
            InventoryDao.InsertEquipment(db, first);
            InventoryDao.InsertEquipment(db, second);
            db.Table("inventoryequipment").Find(x => DbStore.Long(x, "inventoryitemid", -1) == first)["level"] = 9L;
            db.Table("inventoryequipment").Find(x => DbStore.Long(x, "inventoryitemid", -1) == second)["level"] = 1L;
            db.Save();
            var equipSwap = MoveTo(InventoryDao.Equip, 1, 2);
            Check(equipSwap.ok, "装备互换回包 ok");
            var slot1 = At(equipSwap.inventory, InventoryDao.Equip, 1);
            var slot2 = At(equipSwap.inventory, InventoryDao.Equip, 2);
            Check(slot1 != null && slot2 != null && slot1.equipLevel == 1 && slot2.equipLevel == 9,
                "装备栏同 id 仍互换（按实例 level 可区分），不合并");

            // 6. 充能道具同 id → 互换而不是相加（isRechargeable 走 swap）
            int star = 2070000;
            Check(Server.Bot.Trade.TradeItemRules.Rechargeable(star), "2070000 判定为充能道具（前置）");
            if (Server.Bot.Trade.TradeItemRules.Rechargeable(star) && Server.Bot.BotItemCatalog.Shared.Info(star) != null)
            {
                Clear(InventoryDao.Use);
                InventoryDao.InsertItem(db, cid, star, InventoryDao.Use, 1, 500);
                InventoryDao.InsertItem(db, cid, star, InventoryDao.Use, 2, 800);
                var charged = MoveTo(InventoryDao.Use, 1, 2);
                Check(charged.ok, "充能道具移动回包 ok");
                Check(QtyAt(charged.inventory, InventoryDao.Use, 1) == 800 && QtyAt(charged.inventory, InventoryDao.Use, 2) == 500,
                    "充能道具同 id 不叠加，互换槽位");
            }

            // 7. src == dst → 拒绝且不动背包
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 3, 1);
            var same = MoveTo(InventoryDao.Etc, 3, 3);
            Check(!same.ok && ItemIdAt(CharService.Load(db, cid).inventory, InventoryDao.Etc, 3) == 4000002,
                "源格与目标格相同拒绝");

            // 8. 源格为空 → 拒绝
            var empty = MoveTo(InventoryDao.Etc, 1, 4);
            Check(!empty.ok, "源格没有物品拒绝");

            // 9. 越界 / 页号非法 → 拒绝
            Check(!MoveTo(InventoryDao.Etc, 3, 999).ok, "目标槽位越界拒绝");
            Check(!MoveTo(9, 1, 2).ok, "背包页号非法拒绝");
        }
        finally
        {
            db.Save();
            File.Delete(path);
        }
    }
}
