using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Environment
{
    // BotPhysicsEngine.findBelowIndexed：64px桶，使用Foothold.compareTo的稳定插入排序。
    // 该比较器不是按minY/id排序，重叠斜坡的原列表顺序也参与结果。
    public sealed class BotGroundIndex
    {
        readonly Dictionary<int, List<FootholdSegment>> buckets = new Dictionary<int, List<FootholdSegment>>();
        readonly int minX;
        public BotGroundIndex(MapRuntime map)
        {
            minX = int.MaxValue;
            foreach (var f in map.footholds) if (!f.Wall) minX = Math.Min(minX, (int)Math.Min(f.x1, f.x2));
            if (minX == int.MaxValue) { minX = 0; return; }
            foreach (var f in map.footholds)
            {
                if (f.Wall) continue;
                int lo = ((int)Math.Min(f.x1, f.x2) - minX) >> 6, hi = ((int)Math.Max(f.x1, f.x2) - minX) >> 6;
                for (int b = lo; b <= hi; b++)
                {
                    if (!buckets.TryGetValue(b, out var bucket)) buckets[b] = bucket = new List<FootholdSegment>();
                    int at = bucket.Count;
                    bucket.Add(f);
                    while (at > 0 && Compare(bucket[at - 1], f) > 0) { bucket[at] = bucket[at - 1]; at--; }
                    bucket[at] = f;
                }
            }
        }
        static int Compare(FootholdSegment a, FootholdSegment b) => a.y2 < b.y1 ? -1 : a.y1 > b.y2 ? 1 : 0;
        public static int YAt(FootholdSegment f, int x)
        {
            if (f.y1 == f.y2) return (int)f.y1;
            double s1 = Math.Abs(f.y2 - f.y1), s2 = Math.Abs(f.x2 - f.x1), s4 = Math.Abs(x - f.x1);
            double offset = Math.Cos(Math.Atan(s2 / s1)) * (s4 / Math.Cos(Math.Atan(s1 / s2)));
            return f.y2 < f.y1 ? (int)f.y1 - (int)offset : (int)f.y1 + (int)offset;
        }
        public FootholdSegment Below(int x, int y)
        {
            if (buckets.TryGetValue((x - minX) >> 6, out var bucket))
                foreach (var f in bucket) if (f.x1 <= x && f.x2 >= x && YAt(f, x) >= y) return f;
            return null;
        }
        public BotNavigationGraph.Point? PointBelow(int x, int y)
        { var f = Below(x, y); return f == null ? (BotNavigationGraph.Point?)null : new BotNavigationGraph.Point(x, YAt(f, x)); }
        public BotNavigationGraph.Point? GroundAbove(int x, int y, int maxRise)
        {
            if (buckets.TryGetValue((x - minX) >> 6, out var bucket)) foreach (var f in bucket)
            {
                if (f.x1 > x || f.x2 < x) continue;
                int fy = YAt(f, x);
                if (fy < y && fy >= y - maxRise) return new BotNavigationGraph.Point(x, fy);
            }
            return null;
        }
        public FootholdSegment Ground(int x, int y)
        {
            var exact = Below(x, y); var offset = Below(x, y - 26);
            if (exact == null) return offset;
            if (offset == null) return exact;
            return Math.Abs(YAt(offset, x) - y) < Math.Abs(YAt(exact, x) - y) ? offset : exact;
        }
        public BotNavigationGraph.Point? GroundPoint(int x, int y)
        { var f = Ground(x, y); return f == null ? (BotNavigationGraph.Point?)null : new BotNavigationGraph.Point(x, YAt(f, x)); }
        public static bool CanWalkAcross(FootholdSegment from, FootholdSegment to)
        {
            if (from == null || to == null || from.Wall || to.Wall) return false;
            int best = int.MaxValue, dx = 0, dy = 0;
            for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
            {
                int x = (int)((j == 0 ? to.x1 : to.x2) - (i == 0 ? from.x1 : from.x2));
                int y = (int)((j == 0 ? to.y1 : to.y2) - (i == 0 ? from.y1 : from.y2));
                int distance = Math.Abs(x) + Math.Abs(y);
                if (distance < best) { best = distance; dx = Math.Abs(x); dy = y; }
            }
            return best <= 2 && BotGroundPhysics.WalkableEndpoint(dx, dy);
        }
    }
}
