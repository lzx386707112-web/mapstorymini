// 地图运行时注册表（纯 C#，无 UnityEngine）。前端进图时注册 MapRuntime；后端按 mapId 查询 foothold 做落点/移动。
// 对齐源端 world/map 的运行时 foothold 访问（安卓后端无 Map 实体，用这份轻量数据代理）。
using System.Collections.Generic;

namespace Server.Field
{
    public static class MapService
    {
        static readonly Dictionary<int, MapRuntime> maps = new Dictionary<int, MapRuntime>();

        public static void Register(MapRuntime rt) { if (rt != null) maps[rt.mapId] = rt; }
        public static void Unregister(int mapId) => maps.Remove(mapId);
        public static bool Has(int mapId) => maps.ContainsKey(mapId);
        public static MapRuntime Get(int mapId) => maps.TryGetValue(mapId, out var m) ? m : null;

        // 落点：对齐 PlatformSpawner.snapToGround(map, raw)。返回脚下 y-1；地图未加载或无 foothold 时返回原 y。
        public static float SnapToGround(int mapId, float x, float y)
        {
            var rt = Get(mapId);
            if (rt == null || rt.footholds.Count == 0) return y;
            float? g = rt.SnapToGroundRaw(x, y);
            return g.HasValue ? g.Value - 1f : y;
        }
    }
}
