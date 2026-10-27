// 假人纯逻辑助手。对齐源端 soloMapling.ArtificialPlayer.BotLogic 的不依赖 packet 部分：
//   距离/价格/范围判断、地面物品查询（readItemOnFloor/filterDroppedItems/checkForItemsOnFloor）、
//   赌场押注字符串。玩家/物品范围查询走 BotRegistry 与 MapItemService 运行时数据。
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Bot
{
    public static class BotLogic
    {
        // —— 距离判断（对齐源端 isCharNear/isPointNear/isPointNearSameY）——
        public static bool IsCharNear(int x1, int y1, int x2, int y2) => IsPointNear(x1, y1, x2, y2, 25);
        public static bool IsPointNear(int x1, int y1, int x2, int y2, double maxDist)
            => BotHelpers.Distance(x1, y1, x2, y2) <= maxDist;
        public static bool IsPointNearSameY(int x1, int y1, int x2, int y2, double maxX, int yThresh)
        {
            if (Math.Abs(y1 - y2) > yThresh) return false;
            return Math.Abs(x1 - x2) <= maxX;
        }

        public static int GetFurthestPointFromOrigin(int bx, int by, List<(int x, int y)> pts)
        {
            if (pts == null || pts.Count == 0) return -1;
            int best = -1; double max = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                int dx = pts[i].x - bx, dy = pts[i].y - by;
                double d = dx * dx + dy * dy;
                if (d > max) { max = d; best = i; }
            }
            return best;
        }

        // —— 价格容忍（默认 80%，对齐源端）——
        public static bool IsWithinPriceRange(double current, double market, double tol = 0.80)
        {
            if (tol < 0 || tol > 1) throw new ArgumentException("tolerance must be 0..1");
            double lo = market * (1 - tol), hi = market * (1 + tol);
            return current >= lo && current <= hi;
        }

        // —— 自由市场地图常量（对齐源端 FreeMarketValues）——
        const int FM_ENTRANCE = 910000000;
        const int FM_ROOM_1 = 910000001;
        const int FM_ROOM_22 = 910000022;
        public static bool IsInsideFMRooms(int map) => map >= FM_ROOM_1 && map <= FM_ROOM_22;
        public static bool IsAtFMEntrance(int map) => map == FM_ENTRANCE;
        public static bool IsInsideFM(int map) => IsInsideFMRooms(map) || IsAtFMEntrance(map);

        // —— 依赖地图运行时的部分（真人玩家由 BotRuntime 注入 OnlinePlayers；地面物品走 MapItemService）——
        internal static Func<IEnumerable<BotActor>> OnlinePlayers;
        static List<BotActor> PlayersInRange(BotCharacter fake, int w, int h, bool realOnly)
        {
            var found = new List<BotActor>();
            if (fake == null || w <= 0 || h <= 0) return found;
            // BotHelpers.createRectangle uses integer half sizes and a 20% downward offset.
            // java.awt.Rectangle.contains includes left/top and excludes right/bottom.
            int left = (int)fake.x - w / 2;
            int top = (int)fake.y - h / 2 + (int)(h * .2) - h / 2;
            void Add(BotActor actor)
            {
                if (actor.id != fake.id && actor.online && actor.mapId == fake.mapId &&
                    actor.world == fake.world && actor.channel == fake.channel &&
                    (int)actor.x >= left && (int)actor.x < left + w &&
                    (int)actor.y >= top && (int)actor.y < top + h) found.Add(actor);
            }
            if (OnlinePlayers != null) foreach (var actor in OnlinePlayers()) Add(actor);
            if (!realOnly) foreach (var actor in BotRegistry.OnMap(fake.mapId)) Add(actor);
            found.Sort((a,b) => a.mapEntryOrder.CompareTo(b.mapEntryOrder));
            return found;
        }
        // The source's waitForPlayerInRange can return another bot; only the second API excludes bots.
        public static BotActor WaitForPlayerInRange(BotCharacter fake, int w, int h)
        { var actors = PlayersInRange(fake,w,h,false); return actors.Count == 0 ? null : actors[0]; }
        public static List<BotActor> GetRealPlayersInRange(BotCharacter fake, int w, int h)
            => PlayersInRange(fake,w,h,true);
        public static bool IsNpcPresent(int mapId, int npcId)
            => Field.MapService.Get(mapId)?.npcs.Exists(npc => npc.id == npcId) ?? false;

        // —— 造干净物品（对齐源端 generateCleanItem*）——
        // generateCleanItemEquip：装备走 generateCleanEquip（getEquipById 全属性），其它走 generateCleanItem
        public static InventoryEntry GenerateCleanItemEquip(int id) => BotItemCatalog.Shared.CleanItem(id);
        public static InventoryEntry GenerateCleanEquip(int itemId) => BotItemCatalog.Shared.CleanEquip(itemId);
        public static InventoryEntry GenerateCleanItem(int itemId) => GenerateCleanItemWithQty(itemId, 1);
        public static InventoryEntry GenerateCleanItemWithQty(int id, int quantity)
            => new InventoryEntry { itemId = id, inventoryType = id / 1000000, quantity = unchecked((short)quantity) };

        // —— 地面物品查询（对齐源端 readItemOnFloor / filterDroppedItems / checkForItemsOnFloor）——
        /// <summary>源端 checkForItemsOnFloor(chr, location, itemIds)：默认范围 5000（距离平方）。</summary>
        public static List<Field.MapItem> CheckForItemsOnFloor(int mapId, int x, int y, int[] itemIds)
            => CheckForItemsOnFloor(mapId, x, y, 5000, itemIds);

        /// <summary>源端 checkForItemsOnFloor(chr, location, range, itemIds)：按 itemId 过滤地面物品。</summary>
        public static List<Field.MapItem> CheckForItemsOnFloor(int mapId, int x, int y, double rangeSq, int[] itemIds)
            => FilterDroppedItems(ReadItemOnFloor(mapId, x, y, rangeSq), itemIds);

        /// <summary>源端 checkForItemOnFloor(chr, location, itemId)。</summary>
        public static List<Field.MapItem> CheckForItemOnFloor(int mapId, int x, int y, int itemId)
            => CheckForItemsOnFloor(mapId, x, y, new[] { itemId });

        /// <summary>源端 readItemOnFloor：取范围内全部地面物品（源端原方法只读不改，这里保持只读）。</summary>
        public static List<Field.MapItem> ReadItemOnFloor(int mapId, int x, int y, double rangeSq)
            => Field.MapItemService.ItemsInRange(mapId, x, y, rangeSq);

        /// <summary>源端 filterDroppedItems：原地过滤，只保留 filterList 里的 itemId（源端用 Iterator.remove 原地改）。</summary>
        public static List<Field.MapItem> FilterDroppedItems(List<Field.MapItem> items, int[] filterList)
        {
            if (items == null) return new List<Field.MapItem>();
            var keep = new HashSet<int>(filterList ?? new int[0]);
            items.RemoveAll(m => !keep.Contains(m.ItemId));
            return items;
        }

        /// <summary>源端 filterPlayersDroppedItems：原地过滤，只保留归属 player 的掉落。</summary>
        public static List<Field.MapItem> FilterPlayersDroppedItems(List<Field.MapItem> items, BotActor player)
        {
            if (items == null) return new List<Field.MapItem>();
            items.RemoveAll(m => m.ownerCharacterId != player.id);
            return items;
        }

        /// <summary>源端 checkIfItemsOnFloorStill：检查 itemsToCheck 是否仍在地面（itemsAtPlayer 是最新读到的）。</summary>
        public static bool CheckIfItemsOnFloorStill(List<Field.MapItem> itemsToCheck, List<Field.MapItem> itemsAtPlayer)
            => BotHelpers.CheckSecondListInsideFirstList(itemsAtPlayer, itemsToCheck);

        // —— 赌场押注字符串（源端标注 casino bot only，但接口是通用的）——
        /// <summary>源端 cleanUpBetsString：把地面物品拼成押注描述，并去掉 Stamp 等后缀。</summary>
        public static StringBuilder CleanUpBetsString(List<Field.MapItem> items, StringBuilder betString)
        {
            foreach (var mapItem in items)
            {
                string itemName = BotHelpers.ConvertItemIdToName(mapItem.ItemId);
                betString.Append(mapItem.Quantity).Append("x ").Append(itemName).Append(", ");
            }
            var s = betString.ToString()
                .Replace("'s Stamp", "")
                .Replace(" Stamp", "")
                .Replace(" the Really Old", "")
                .Replace(" Pierce", "");
            return new StringBuilder(s);
        }

        /// <summary>源端 announceBetString：「&lt;name&gt;'s Bet: …」。</summary>
        public static string AnnounceBetString(BotActor better, List<Field.MapItem> items)
        {
            var betString = new StringBuilder();
            betString.Append(better.name).Append("'s Bet: ");
            return CleanUpBetsString(items, betString).ToString();
        }

        /// <summary>源端 readPlayersBetsStamps(player, range)：脚下范围内的押注图章。</summary>
        public static List<Field.MapItem> ReadPlayersBetsStamps(BotActor player, double rangeSq)
            => ReadPlayersBetsStamps(player, (int)player.x, (int)player.y, rangeSq);

        // 源端 stamps：4002000..4002003、4031558..4031561
        static readonly int[] BetStamps = { 4002000, 4002001, 4002002, 4002003, 4031558, 4031559, 4031560, 4031561 };

        /// <summary>源端 readPlayersBetsStamps(player, location, range)：只认 player 自己扔的图章。</summary>
        public static List<Field.MapItem> ReadPlayersBetsStamps(BotActor player, int x, int y, double rangeSq)
            => FilterDroppedItems(FilterPlayersDroppedItems(ReadItemOnFloor(player.mapId, x, y, rangeSq), player), BetStamps);
    }
}
