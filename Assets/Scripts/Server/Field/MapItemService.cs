// 权威地面掉落注册表。对齐源端 org.gms.server.maps.MapleMap 的掉落/拾取/落点算法：
//   spawnItemDrop / spawnItemDropNoExpire / spawnMesoDrop / disappearingItemDrop / disappearingMesoDrop
//   makeDisappearItemFromMap / pickItemDrop / getMapObjectsInRange / instantiateItemDrop
//   calcPointBelow / calcDropPos / calcPlayerDropPos / calcItemDropPos / bsearchDropPos / generateMapDropRangeCache
// 纯 C#，不引用 UnityEngine；渲染由前端订阅 Spawned/Removed，不在这里画。
// 时钟沿用 MapSpawner.Clock 范式：由前端每帧注入毫秒时钟，禁止 UnityEngine.Time。
using System;
using System.Collections.Generic;

namespace Server.Field
{
    public static class MapItemService
    {
        /// <summary>毫秒时钟，由外部注入（对齐 MapSpawner.Clock / BotScheduler.Clock 范式）。</summary>
        public static Func<long> Clock = () => 0;

        /// <summary>源端 GameConfig item_limit_on_map 默认 200：地图掉落物上限，超出逐出最早的一个。</summary>
        public static int ItemLimitOnMap = 200;
        public static long ExpireMilliseconds=180000;

        /// <summary>归属窗口：源端 MapItem.hasExpiredOwnershipTime 用 15 秒。</summary>
        public const long OwnershipWindowMs = 15_000;

        /// <summary>源端 FieldLimit.DROP_LIMIT(0x400000)：这些地图掉落物只播消失动画，不落地图。</summary>
        public const int DropLimitBit = 0x400000;

        static readonly Dictionary<int, List<MapItem>> byMap = new Dictionary<int, List<MapItem>>();
        // 源端 registeredDrops（弱引用 FIFO）：instantiateItemDrop 满员时从最早注册的开始逐出
        static readonly Dictionary<int, List<MapItem>> registered = new Dictionary<int, List<MapItem>>();
        static int nextObjectId = 1000000;

        /// <summary>前端渲染钩子：新掉落物进入地图。</summary>
        public static event Action<MapItem> Spawned;
        /// <summary>前端渲染钩子：掉落物离开地图（reason：0=消失/过期，2=被拾取）。</summary>
        public static event Action<MapItem, int> Removed;

        // —— 真人队伍解析（由 BotRuntime 注入，避免 Server.Field 依赖 Server.Bot）——
        /// <summary>characterId -> partyId；-1 表示无队伍。</summary>
        public static Func<int, int> PartyOf;
        /// <summary>characterId 是否与 ownerId 同队（源端 Character.isPartyMember）。</summary>
        public static Func<int, int, bool> IsPartyMember;

        public static long Now() => Clock != null ? Clock() : 0;

        // ——————————————————— 落点算法（源端 MapleMap）———————————————————

        // 源端 calcPointBelow(initial)：找 initial 正下方最近 foothold 的 y。
        // MapRuntime.PointBelow 的线性插值与源端三角公式等价（s5 = |dy|*|dx|/|dx_total|）。
        public static bool CalcPointBelow(int mapId, float x, float y, out float outY)
        {
            outY = 0f;
            var map = MapService.Get(mapId);
            if (map == null) return false;
            float? below = map.PointBelow(x, y);
            if (below == null) return false;
            outY = below.Value;
            return true;
        }

        // 源端 generateMapDropRangeCache 的 xLimits：可掉落的左右边界（mapArea 边界再内收 14px）。
        static readonly Dictionary<int, float[]> dropLimits = new Dictionary<int, float[]>();

        static float[] DropLimits(int mapId, MapRuntime map)
        {
            if (dropLimits.TryGetValue(mapId, out var cached)) return cached;
            float fallbackX = (map.left + map.right) / 2f;
            float topY = map.top;
            float lp = BsearchDropPos(mapId, map.left, topY, fallbackX, topY).Item1;
            float rp = BsearchDropPos(mapId, map.right, topY, fallbackX, topY).Item1;
            var limits = new[] { lp + 14f, rp - 14f };
            dropLimits[mapId] = limits;
            return limits;
        }

        // 源端 bsearchDropPos：在 homex..awayx 二分找有 foothold 的落点，找不到回退 fallback。
        static (float, float) BsearchDropPos(int mapId, float homex, float y, float awayx, float fallbackY)
        {
            float dropX = homex, dropY = fallbackY;
            float searchY = y - 85f;
            do
            {
                float distx = awayx - homex;
                float dx = distx / 2f;
                float searchx = homex + dx;
                if (CalcPointBelow(mapId, searchx, searchY, out float foundY))
                {
                    awayx = searchx;
                    dropX = searchx;
                    dropY = foundY;
                }
                else homex = searchx;
            } while (Math.Abs(homex - awayx) > 5f);
            return (dropX, dropY);
        }

        static bool InMapArea(MapRuntime map, float x, float y)
            => x >= map.left && x <= map.right && y >= map.top && y <= map.bottom;

        /// <summary>源端 calcDropPos(initial, fallback)：把掉落物吸附到下方平台，并夹在可掉落范围内。</summary>
        public static void CalcDropPos(int mapId, float x, float y, float fallbackX, float fallbackY, out float outX, out float outY)
        {
            outX = fallbackX; outY = fallbackY;
            var map = MapService.Get(mapId);
            if (map == null) return;
            var limits = DropLimits(mapId, map);
            float ix = x;
            if (ix < limits[0]) ix = limits[0];
            else if (ix > limits[1]) ix = limits[1];

            if (!CalcPointBelow(mapId, ix, y - 85f, out float retY))
            {
                (ix, retY) = BsearchDropPos(mapId, ix, y, fallbackX, fallbackY);
            }

            if (!InMapArea(map, ix, retY)) return;   // 源端：落点跑出地图 → 用 fallback
            outX = ix; outY = retY;
        }

        /// <summary>源端 calcPlayerDropPos：FM 入口玩家掉落用更贴近脚下的 -6 采样。</summary>
        public static void CalcPlayerDropPos(int mapId, float x, float y, float fallbackX, float fallbackY, out float outX, out float outY)
        {
            outX = fallbackX; outY = fallbackY;
            var map = MapService.Get(mapId);
            if (map == null) return;
            var limits = DropLimits(mapId, map);
            float sx = x;
            if (sx < limits[0]) sx = limits[0];
            else if (sx > limits[1]) sx = limits[1];

            float rx, ry;
            if (!CalcPointBelow(mapId, sx, y - 6f, out ry)) CalcDropPos(mapId, sx, y, fallbackX, fallbackY, out rx, out ry);
            else rx = sx;

            if (!InMapArea(map, rx, ry)) return;
            outX = rx; outY = ry;
        }

        /// <summary>源端 calcItemDropPos：playerDrop 且地图是 FM 入口 910000000 时走 calcPlayerDropPos。</summary>
        public const int FmEntranceMapId = 910000000;
        public static void CalcItemDropPos(int mapId, float x, float y, bool playerDrop, out float outX, out float outY)
        {
            if (playerDrop && mapId == FmEntranceMapId) CalcPlayerDropPos(mapId, x, y, x, y, out outX, out outY);
            else CalcDropPos(mapId, x, y, x, y, out outX, out outY);
        }

        // ——————————————————— 生成 / 消失 ——————————————————

        static bool DropLimited(int mapId)
        {
            var map = MapService.Get(mapId);
            return map != null && (map.fieldLimit & DropLimitBit) != 0;
        }

        static MapItem NewItem(int mapId, InventoryEntry item, int dropperId, int ownerId, int partyId, byte dropType, bool playerDrop, float x, float y)
        {
            return new MapItem
            {
                objectId = nextObjectId++,
                mapId = mapId,
                item = item,
                meso = 0,
                dropperId = dropperId,
                type = dropType,
                playerDrop = playerDrop,
                x = x,
                y = y,
                dropTime = Now(),
                partyDrop = partyId != -1,
                partyOwnerId = partyId,
                ownerCharacterId = ownerId,
            };
        }

        static MapItem NewMeso(int mapId, int meso, int dropperId, int ownerId, int partyId, byte dropType, bool playerDrop, float x, float y)
        {
            return new MapItem
            {
                objectId = nextObjectId++,
                mapId = mapId,
                item = null,
                meso = meso,
                dropperId = dropperId,
                type = dropType,
                playerDrop = playerDrop,
                x = x,
                y = y,
                dropTime = Now(),
                partyDrop = partyId != -1,
                partyOwnerId = partyId,
                ownerCharacterId = ownerId,
            };
        }

        static void Register(int mapId, MapItem mdrop, bool evictable)
        {
            if (!byMap.TryGetValue(mapId, out var list)) byMap[mapId] = list = new List<MapItem>();
            list.Add(mdrop);
            if (!evictable) return;
            if (MapService.Get(mapId)?.everlast!=true) mdrop.expiresAt=Now()+ExpireMilliseconds;

            // 源端 instantiateItemDrop：满 item_limit_on_map 时从最早注册的逐出
            if (!registered.TryGetValue(mapId, out var reg)) registered[mapId] = reg = new List<MapItem>();
            while (ItemLimitOnMap > 0 && reg.Count >= ItemLimitOnMap)
            {
                var oldest = reg[0];
                reg.RemoveAt(0);
                MakeDisappear(mapId, oldest);
            }
            reg.Add(mdrop);
        }

        /// <summary>
        /// 源端 spawnItemDrop(dropper, owner, item, pos, ffaDrop, playerDrop)。
        /// DROP_LIMIT 地图只播消失动画（disappearingItemDrop），不落地图。
        /// </summary>
        public static MapItem SpawnItemDrop(int mapId, int dropperId, int ownerId, int partyId,
            InventoryEntry item, float x, float y, bool ffaDrop, bool playerDrop)
            => SpawnItemDrop(mapId, dropperId, ownerId, partyId, item, x, y, (byte)(ffaDrop ? 2 : 0), playerDrop);

        /// <summary>源端 spawnItemDrop(dropper, owner, item, pos, dropType, playerDrop)。</summary>
        public static MapItem SpawnItemDrop(int mapId, int dropperId, int ownerId, int partyId,
            InventoryEntry item, float x, float y, byte dropType, bool playerDrop)
        {
            if (item == null) return null;
            if (DropLimited(mapId))
            {
                DisappearingItemDrop(mapId, dropperId, ownerId, partyId, item, x, y);
                return null;
            }
            CalcItemDropPos(mapId, x, y, playerDrop, out float dx, out float dy);
            var mdrop = NewItem(mapId, item, dropperId, ownerId, partyId, dropType, playerDrop, dx, dy);
            Register(mapId, mdrop, true);
            Spawned?.Invoke(mdrop);
            return mdrop;
        }

        /// <summary>
        /// 源端 spawnItemDropNoExpire：不进 registeredDrops（不会被 item_limit 逐出），
        /// 过期由调用方（DropCommands.botDropItemWithExpiry）自行安排。
        /// </summary>
        public static MapItem SpawnItemDropNoExpire(int mapId, int dropperId, int ownerId, int partyId,
            InventoryEntry item, float x, float y, bool ffaDrop, bool playerDrop)
        {
            if (item == null) return null;
            if (DropLimited(mapId))
            {
                DisappearingItemDrop(mapId, dropperId, ownerId, partyId, item, x, y);
                return null;
            }
            CalcItemDropPos(mapId, x, y, playerDrop, out float dx, out float dy);
            var mdrop = NewItem(mapId, item, dropperId, ownerId, partyId, (byte)(ffaDrop ? 2 : 0), playerDrop, dx, dy);
            Register(mapId, mdrop, false);
            Spawned?.Invoke(mdrop);
            return mdrop;
        }

        /// <summary>源端 spawnMesoDrop(meso, position, dropper, owner, playerDrop, droptype)。</summary>
        public static MapItem SpawnMesoDrop(int mapId, int meso, float x, float y,
            int dropperId, int ownerId, int partyId, bool playerDrop, byte dropType)
        {
            if (meso <= 0) return null;
            CalcDropPos(mapId, x, y, x, y, out float dx, out float dy);
            var mdrop = NewMeso(mapId, meso, dropperId, ownerId, partyId, dropType, playerDrop, dx, dy);
            Register(mapId, mdrop, true);
            Spawned?.Invoke(mdrop);
            return mdrop;
        }

        /// <summary>源端 disappearingItemDrop：DROP_LIMIT 地图上的道具只播一次性消失动画，不落地图。</summary>
        public static void DisappearingItemDrop(int mapId, int dropperId, int ownerId, int partyId,
            InventoryEntry item, float x, float y)
        {
            CalcDropPos(mapId, x, y, x, y, out float dx, out float dy);
            var mdrop = NewItem(mapId, item, dropperId, ownerId, partyId, 1, false, dx, dy);
            Spawned?.Invoke(mdrop);      // 源端 broadcastItemDropMessage(..., (byte)3) 一次性动画
            Removed?.Invoke(mdrop, 3);
        }

        /// <summary>源端 disappearingMesoDrop。</summary>
        public static void DisappearingMesoDrop(int mapId, int meso, float x, float y,
            int dropperId, int ownerId, int partyId)
        {
            CalcDropPos(mapId, x, y, x, y, out float dx, out float dy);
            var mdrop = NewMeso(mapId, meso, dropperId, ownerId, partyId, 1, false, dx, dy);
            Spawned?.Invoke(mdrop);
            Removed?.Invoke(mdrop, 3);
        }

        // ——————————————————— 拾取 / 消失 ——————————————————

        /// <summary>
        /// 源端 PacketCreator.removeItemFromMap(objectId, 0, 0) 的**只广播不摘除**：
        /// 客户端先看不见，服务端实体仍在（DropCommands 的限时掉落宽限期就靠它）。
        /// </summary>
        public static void NotifyRemoved(MapItem mdrop, int reason) => Removed?.Invoke(mdrop, reason);

        /// <summary>
        /// 源端 pickItemDrop(pickupPacket, mdrop)：广播移除 + 从地图摘除 + 标记已拾取。
        /// 注意源端这里**不入背包**；入背包在 Character.pickupItem（见 MapItemPickup）。
        /// 调用前必须已确认 !isPickedUp（源端注释：mdrop must be already locked and not-pickedup checked）。
        /// </summary>
        public static void PickItemDrop(int mapId, MapItem mdrop, int reason, int pickerId = 0, int petIndex = -1)
        {
            if (mdrop == null) return;
            Detach(mapId, mdrop);
            mdrop.pickedUp = true;
            mdrop.pickupCharacterId = pickerId;
            mdrop.pickupPetIndex = petIndex;
            Removed?.Invoke(mdrop, reason);
        }

        /// <summary>源端 makeDisappearItemFromMap：已拾取直接返回 true，否则按 reason 0 移除。</summary>
        public static bool MakeDisappear(int mapId, MapItem mapitem)
        {
            if (mapitem == null) return false;
            if (!ReferenceEquals(Find(mapId, mapitem.objectId), mapitem)) return false;
            if (mapitem.pickedUp) return true;
            PickItemDrop(mapId, mapitem, 0);
            return true;
        }

        static void Detach(int mapId, MapItem mdrop)
        {
            if (byMap.TryGetValue(mapId, out var list)) list.Remove(mdrop);
            if (registered.TryGetValue(mapId, out var reg)) reg.Remove(mdrop);
        }

        public static MapItem Find(int mapId, int objectId)
        {
            if (!byMap.TryGetValue(mapId, out var list)) return null;
            foreach (var item in list) if (item.objectId == objectId) return item;
            return null;
        }

        public static void Tick(long now)
        {
            var expired=new List<MapItem>();
            foreach (var items in byMap.Values)
                foreach (var item in items) if (item.expiresAt<now) expired.Add(item);
            foreach (var item in expired) MakeDisappear(item.mapId,item);
        }

        // ——————————————————— 查询 ——————————————————

        /// <summary>源端 getDroppedItemCount。</summary>
        public static int DroppedItemCount(int mapId)
            => byMap.TryGetValue(mapId, out var list) ? list.Count : 0;

        /// <summary>源端 getDroppedItems()：当前地图全部地面物品。</summary>
        public static List<MapItem> DroppedItems(int mapId)
            => byMap.TryGetValue(mapId, out var list) ? new List<MapItem>(list) : new List<MapItem>();

        /// <summary>
        /// 源端 getMapObjectsInRange(from, rangeSq, [ITEM])：按**距离平方**比较（参数名就是 rangeSq）。
        /// DropCommands 传 1000/3000/5000/10000 都是平方距离，保持原样不换算。
        /// </summary>
        public static List<MapItem> ItemsInRange(int mapId, float x, float y, double rangeSq)
        {
            var ret = new List<MapItem>();
            if (!byMap.TryGetValue(mapId, out var list)) return ret;
            foreach (var item in list)
            {
                double dx = item.x - x, dy = item.y - y;
                if (dx * dx + dy * dy <= rangeSq) ret.Add(item);
            }
            return ret;
        }

        /// <summary>源端 getMapObjectsInRect(rect, [ITEM])：矩形含左上、不含右下（java.awt.Rectangle.contains）。</summary>
        public static List<MapItem> ItemsInRect(int mapId, int left, int top, int width, int height)
        {
            var ret = new List<MapItem>();
            if (!byMap.TryGetValue(mapId, out var list)) return ret;
            foreach (var item in list)
            {
                int ix = (int)item.x, iy = (int)item.y;
                if (ix >= left && ix < left + width && iy >= top && iy < top + height) ret.Add(item);
            }
            return ret;
        }

        // ——————————————————— 生命周期 ——————————————————

        /// <summary>地图切换/清场：移除该图全部掉落物（前端随 Removed 清理）。</summary>
        public static void ClearMap(int mapId)
        {
            if (byMap.TryGetValue(mapId, out var list))
            {
                foreach (var item in new List<MapItem>(list))
                {
                    item.pickedUp = true;
                    Removed?.Invoke(item, 0);
                }
                list.Clear();
            }
            registered.Remove(mapId);
            dropLimits.Remove(mapId);
        }

        /// <summary>会话结束/换频道：清空全部地图掉落。</summary>
        public static void ClearAll()
        {
            foreach (var mapId in new List<int>(byMap.Keys)) ClearMap(mapId);
            byMap.Clear(); registered.Clear(); dropLimits.Clear();
        }

        /// <summary>地图重载后几何变化，丢弃缓存的可掉落范围。</summary>
        public static void InvalidateMapGeometry(int mapId) => dropLimits.Remove(mapId);
    }
}
