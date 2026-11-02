// 背包整理 / 合并回归：对照源端
//   InventorySortHandler.java（ITEM_SORT2）与 InventoryMergeHandler.java（ITEM_SORT）。
// 断言的是服务端数据层的最终槽位与数量；客户端显示受 Profile 扁平投影限制，不在本文件覆盖。
using System;
using System.IO;

static class InventoryOrganizeChecks
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
        Console.WriteLine("== 背包整理 / 合并（ITEM_SORT2 / ITEM_SORT）==");
        string path = Path.Combine(Path.GetTempPath(), "mapledash_sort_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "sorttest", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "SortHero", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            Check(made.code == 0, "建号成功");
            int cid = made.characterId;
            var rec = CharactersDao.FindById(db, cid);

            bool catalogOk = Server.Bot.BotItemCatalog.Shared.Info(2000000) != null;
            Check(catalogOk, "道具目录可读（slotMax 依赖，前置）");

            void Clear(int type)
            {
                var ids = new System.Collections.Generic.HashSet<long>();
                foreach (var r in db.Table("inventoryitems").Select(x => DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "inventorytype") == type))
                    ids.Add(DbStore.Long(r, "inventoryitemid"));
                db.Table("inventoryitems").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
                db.Table("inventoryequipment").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
            }

            void SetConfig(string code, string value)
            {
                var row = db.Table("game_config").Find(x => (string)x["config_code"] == code);
                if (row == null)
                    db.Table("game_config").Insert(new DbRow {
                        ["config_type"] = "server", ["config_sub_type"] = "Game Mechanics",
                        ["config_clazz"] = "java.lang.Boolean", ["config_code"] = code, ["config_value"] = value });
                else row["config_value"] = value;
                db.Save();
            }

            InventoryChangeResponse Sort(int type)
                => NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = cid, sortItems = true, sortInventoryType = type });

            InventoryChangeResponse Merge(int type)
                => NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = cid, mergeItems = true, sortInventoryType = type });

            // 1. 主键 itemId 升序：三个道具乱序放 → 排完按 id 递增占 1/2/3
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 1, 1);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 2, 1);
            InventoryDao.InsertItem(db, cid, 4000001, InventoryDao.Etc, 3, 1);
            var sorted = Sort(InventoryDao.Etc);
            Check(sorted.ok, "排序回包 ok");
            Check(ItemIdAt(sorted.inventory, InventoryDao.Etc, 1) == 4000000
                  && ItemIdAt(sorted.inventory, InventoryDao.Etc, 2) == 4000001
                  && ItemIdAt(sorted.inventory, InventoryDao.Etc, 3) == 4000002,
                "主键按 itemId 升序重排");

            // 2. 同 itemId 分组后按数量（副键 1）升序
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 1, 5);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 2, 3);
            sorted = Sort(InventoryDao.Etc);
            Check(QtyAt(sorted.inventory, InventoryDao.Etc, 1) == 3 && QtyAt(sorted.inventory, InventoryDao.Etc, 2) == 5,
                "同 itemId 组内按数量升序（副键 1）");

            // 3. 装备副键 = inventoryequipment.level（Equip.getLevel），不是需求等级
            Clear(InventoryDao.Equip);
            db.Table("inventoryequipment").Delete(x => false);
            long high = InventoryDao.InsertItem(db, cid, 1302000, InventoryDao.Equip, 1, 1);
            long low = InventoryDao.InsertItem(db, cid, 1302000, InventoryDao.Equip, 2, 1);
            InventoryDao.InsertEquipment(db, high);
            InventoryDao.InsertEquipment(db, low);
            db.Table("inventoryequipment").Find(x => DbStore.Long(x, "inventoryitemid", -1) == high)["level"] = 5L;
            db.Table("inventoryequipment").Find(x => DbStore.Long(x, "inventoryitemid", -1) == low)["level"] = 0L;
            db.Save();
            sorted = Sort(InventoryDao.Equip);
            var first = At(sorted.inventory, InventoryDao.Equip, 1);
            Check(first != null && first.equipLevel == 0, "装备按 level 升序（副键 3）");

            // 4. 合并：同 id 两格并成一格，数量相加
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 1, 1);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 3, 1);
            var merged = Merge(InventoryDao.Etc);
            Check(merged.ok, "合并回包 ok");
            Check(QtyAt(merged.inventory, InventoryDao.Etc, 1) == 2 && At(merged.inventory, InventoryDao.Etc, 3) == null,
                "同 id 合并到 dst 并删掉源格");

            // 5. 合并超上限：dst 填满，余量留在源格（Inventory.move 的 slotMax 分支）
            Clear(InventoryDao.Etc);
            var actor = new Server.Bot.BotActor { id = cid, job = rec.job, character = CharService.Load(db, cid) };
            int slotMax = new Server.Bot.Trade.TradeItemRules(db, Server.Bot.BotItemCatalog.Shared).SlotMax(4000000, actor);
            Check(slotMax > 5, "测试道具 slotMax > 5（前置）");
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 1, slotMax - 1);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 2, 5);
            merged = Merge(InventoryDao.Etc);
            Check(QtyAt(merged.inventory, InventoryDao.Etc, 1) == slotMax && QtyAt(merged.inventory, InventoryDao.Etc, 2) == 4,
                "超过 slotMax 时 dst 填满、源格留余量");

            // 6. 合并第二段：填掉空洞，物品前移紧排
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000001, InventoryDao.Etc, 1, 1);
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 5, 1);
            merged = Merge(InventoryDao.Etc);
            Check(ItemIdAt(merged.inventory, InventoryDao.Etc, 1) == 4000001 && ItemIdAt(merged.inventory, InventoryDao.Etc, 2) == 4000002,
                "合并后空洞被填，物品前移紧排");

            // 7. 装备栏不合并（Inventory.move 对 EQUIP 走 swap 分支）
            Clear(InventoryDao.Equip);
            db.Table("inventoryequipment").Delete(x => false);
            long e1 = InventoryDao.InsertItem(db, cid, 1302000, InventoryDao.Equip, 1, 1);
            long e2 = InventoryDao.InsertItem(db, cid, 1302000, InventoryDao.Equip, 2, 1);
            InventoryDao.InsertEquipment(db, e1);
            InventoryDao.InsertEquipment(db, e2);
            db.Save();
            merged = Merge(InventoryDao.Equip);
            Check(At(merged.inventory, InventoryDao.Equip, 1) != null && At(merged.inventory, InventoryDao.Equip, 2) != null,
                "装备栏同 id 不合并（EQUIP 走 swap）");

            // 8. use_item_sort 关闭时原样返回
            Clear(InventoryDao.Etc);
            InventoryDao.InsertItem(db, cid, 4000002, InventoryDao.Etc, 1, 1);
            InventoryDao.InsertItem(db, cid, 4000000, InventoryDao.Etc, 2, 1);
            SetConfig("use_item_sort", "false");
            var off = Sort(InventoryDao.Etc);
            Check(off.ok && ItemIdAt(off.inventory, InventoryDao.Etc, 1) == 4000002, "use_item_sort=false 时排序不动背包");
            SetConfig("use_item_sort", "true");

            // 9. 非法页拒绝
            var bad = Sort(9);
            Check(!bad.ok, "背包页号越界拒绝");
        }
        finally
        {
            db.Save();
            File.Delete(path);
        }
    }
}
