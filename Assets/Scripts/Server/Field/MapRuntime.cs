// 地图运行时 foothold 数据（纯 C#，无 UnityEngine）。前端 FieldDirector 进图时从 FieldMap.footholds
// 构建并注册到 MapService；后端假人系统（BotWander / BotGeneration 落点）只读这份纯数据，不直接依赖 WZ。
// 对齐源端 MapleMap.getPointBelow / PlatformSpawner.snapToGround 的落地语义。
using System.Collections.Generic;

namespace Server.Field
{
    public sealed class FootholdSegment
    {
        public int id, previous, next, layer, group;
        public bool forbidFallDown;
        public float x1, y1, x2, y2;
        public bool Wall => x1 == x2;
        public double Slope => Wall ? 0 : (y2 - y1) / (double)(x2 - x1);
        public FootholdSegment(float x1, float y1, float x2, float y2)
        {
            this.x1 = x1; this.y1 = y1; this.x2 = x2; this.y2 = y2;
        }
    }

    public sealed class MapRope
    {
        public int id, x, y1, y2;
        public bool ladder, upperExit;
        public int Top => System.Math.Min(y1, y2);
        public int Bottom => System.Math.Max(y1, y2);
    }

    public sealed class MapPortal
    {
        public bool enabled = true;
        public int id, x, y, type, targetMap;
        public string name, targetName, script;
    }

    public sealed class MapNpcPosition
    {
        public int id, x, y;
        public bool dynamic;
    }

    public sealed class MapRuntime
    {
        public int mapId;
        public int returnMap, fieldLimit, left, right, top, bottom;
        public bool swim;
        public bool everlast;
        public float footholdSpeed = 1;
        public readonly List<MapRope> ropes = new List<MapRope>();
        public readonly List<MapPortal> portals = new List<MapPortal>();
        public readonly List<MapNpcPosition> npcs = new List<MapNpcPosition>();
        /// <summary>WZ reactor 节点的静态描述；运行时实例由 MapReactorService 惰性生成（本文件不持逻辑）。</summary>
        public readonly List<MapReactorSpawn> reactors = new List<MapReactorSpawn>();
        public readonly List<FootholdSegment> footholds = new List<FootholdSegment>();
        public float minX = float.MaxValue, maxX = float.MinValue;
        public float minY = float.MaxValue, maxY = float.MinValue;

        public void AddFoothold(float x1, float y1, float x2, float y2)
        {
            footholds.Add(new FootholdSegment(x1, y1, x2, y2));
            minX = System.Math.Min(minX, System.Math.Min(x1, x2));
            maxX = System.Math.Max(maxX, System.Math.Max(x1, x2));
            minY = System.Math.Min(minY, System.Math.Min(y1, y2));
            maxY = System.Math.Max(maxY, System.Math.Max(y1, y2));
        }

        // 对齐 MapleMap.getPointBelow：返回 x 处、y >= yGuess 的最近（最小 y） foothold 高度；无则 null。
        public float? PointBelow(float x, float yGuess)
        {
            float? best = null;
            foreach (var f in footholds)
            {
                if (f.x1 == f.x2) continue; // 源端 FootholdTree.findBelow 跳过墙
                float lo = System.Math.Min(f.x1, f.x2), hi = System.Math.Max(f.x1, f.x2);
                if (x < lo || x > hi) continue;
                float t = (f.x2 == f.x1) ? 0f : (x - f.x1) / (f.x2 - f.x1);
                float yAt = f.y1 + (f.y2 - f.y1) * t;
                if (yAt >= yGuess && (best == null || yAt < best.Value))
                    best = yAt;
            }
            return best;
        }

        // 对齐 PlatformSpawner.snapToGround：先 (x, y-12) 找脚下，否则用 minY 兜底；返回脚下 y（不含 -1 偏移）。
        public float? SnapToGroundRaw(float x, float y)
        {
            float? below = PointBelow(x, y - 12f);
            if (below == null && footholds.Count > 0) below = PointBelow(x, minY);
            return below;
        }
    }
}
