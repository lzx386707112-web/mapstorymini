// 假人纯逻辑助手。对齐源端 soloMapling.ArtificialPlayer.BotLogic 的不依赖 packet 部分：
//   距离/价格/范围判断。依赖地图运行时（玩家/物品范围查询）的部分在阶段C+ 才有地图数据，
//   阶段A 先留纯函数与占位签名。
using System;
using System.Collections.Generic;

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

        // —— 依赖地图运行时的部分（阶段C+ 实现，阶段A 占位）——
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
        public static List<int> CheckForItemsOnFloor(int mapId, int x, int y, int[] itemIds) => new List<int>();
        public static bool IsNpcPresent(int mapId, int npcId)
            => Field.MapService.Get(mapId)?.npcs.Exists(npc => npc.id == npcId) ?? false;
        public static InventoryEntry GenerateCleanItemEquip(int id) => BotItemCatalog.Shared.CleanItem(id);
        public static InventoryEntry GenerateCleanItemWithQty(int id, int quantity)
            => new InventoryEntry { itemId = id, inventoryType = id / 1000000, quantity = unchecked((short)quantity) };
    }
}
