// 地形索引缓存。
//
// 背景（实测量化，2026-10-29 离线压测）：
//   BotGroundIndex / BotWalkRegions / BotAirCollision 三份结构**只依赖地图**，
//   与具体假人无关；但以前每个 BotMovementRuntime 都各建一份。
//   1203 个 foothold 的图上，单个假人 3.4ms、120 个假人 364ms —— 全是重复劳动。
//   而且 BotMovementPlayback 每播一帧录制动作就要 new 一次 BotGroundIndex，
//   出生编排（portalenterdrop / turnaroundtoleft）那几秒是逐帧在付这个钱。
//
// 做法：按 MapRuntime 实例缓存一份，所有假人共享。
//   · 用实例而不是 mapId 做键：同一张图重载后是新的 MapRuntime，天然不会读到旧地形；
//     测试里也可能出现「同 mapId、不同 foothold」的合成地图，用 mapId 会串味。
//   · ConditionalWeakTable 在 MapRuntime 被回收时自动释放缓存，不需要手工 Evict，
//     也不会把地图对象永久钉在内存里。
//   · 三份结构在建完后都是只读的（BotWalkRegions 的并查集路径压缩只在构造期发生），
//     所以共享不存在并发/互相污染问题。
using System.Runtime.CompilerServices;
using Server.Field;

namespace Server.Bot.Environment
{
    public sealed class MapGeometry
    {
        public readonly MapRuntime map;
        public readonly BotGroundIndex ground;
        public readonly BotWalkRegions regions;
        public readonly BotAirCollision collision;
        // 构建时的形状指纹：三份结构都是从 footholds+ropes 快照出来的，
        // 地图在缓存之后又被追加了平台/绳索就必须重建（离线自检就是先建图、再加绳索、再重建）。
        readonly int footholdCount, ropeCount;

        public MapGeometry(MapRuntime map)
        {
            this.map = map;
            footholdCount = map.footholds.Count;
            ropeCount = map.ropes.Count;
            ground = new BotGroundIndex(map);
            regions = new BotWalkRegions(map);
            collision = new BotAirCollision(map, ground);
        }

        public bool Stale => map.footholds.Count != footholdCount || map.ropes.Count != ropeCount;
    }

    public static class MapGeometryCache
    {
        static readonly ConditionalWeakTable<MapRuntime, MapGeometry> table =
            new ConditionalWeakTable<MapRuntime, MapGeometry>();

        public static MapGeometry Get(MapRuntime map)
        {
            if (map == null) return null;
            if (table.TryGetValue(map, out var cached) && !cached.Stale) return cached;
            var geometry = new MapGeometry(map);
            // ConditionalWeakTable 没有索引器赋值；先移除再加，单线程主循环里这样做是安全的。
            if (table.TryGetValue(map, out _)) table.Remove(map);
            table.Add(map, geometry);
            return geometry;
        }
    }
}
