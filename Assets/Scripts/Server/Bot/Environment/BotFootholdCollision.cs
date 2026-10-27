using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Environment
{
    // BotNavigationGraphProvider.classifyCollidableFromBelowFootholds及闭环实体/孔洞分类。
    public static class BotFootholdCollision
    {
        sealed class Loop
        {
            public readonly List<FootholdSegment> chain;
            public double minX = double.PositiveInfinity, maxX = double.NegativeInfinity,
                minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            public bool solid;
            public double Area => (maxX - minX) * (maxY - minY);
            public Loop(List<FootholdSegment> chain)
            {
                this.chain = chain;
                foreach (var f in chain)
                {
                    minX = Math.Min(minX, Math.Min(f.x1, f.x2)); maxX = Math.Max(maxX, Math.Max(f.x1, f.x2));
                    minY = Math.Min(minY, Math.Min(f.y1, f.y2)); maxY = Math.Max(maxY, Math.Max(f.y1, f.y2));
                }
            }
            public bool Contains(double x, double y)
            {
                bool inside = false;
                for (int i = 0, j = chain.Count - 1; i < chain.Count; j = i++)
                {
                    var a = chain[i]; var b = chain[j];
                    if ((a.y1 > y) != (b.y1 > y) && x < (b.x1 - a.x1) * (y - a.y1) / (b.y1 - a.y1) + a.x1)
                        inside = !inside;
                }
                return inside;
            }
            public bool Contains(Loop inner) => minX <= inner.minX && maxX >= inner.maxX && minY <= inner.minY && maxY >= inner.maxY &&
                Contains(inner.chain[0].x1, inner.chain[0].y1);
            public bool ThinShell(Loop inner) => inner.minX - minX <= 8 && maxX - inner.maxX <= 8 && inner.minY - minY <= 8 && maxY - inner.maxY <= 8;
        }
        public static HashSet<int> FromBelow(IReadOnlyList<FootholdSegment> footholds)
        {
            var byId = new Dictionary<int, FootholdSegment>();
            foreach (var f in footholds) byId[f.id] = f;
            var visited = new HashSet<int>(); var loops = new List<Loop>();
            foreach (var start in footholds)
            {
                if (visited.Contains(start.id)) continue;
                var chain = new List<FootholdSegment>(); var seen = new HashSet<int>();
                var current = start; bool closed = false;
                while (current != null && seen.Add(current.id) && chain.Count <= byId.Count)
                {
                    chain.Add(current);
                    byId.TryGetValue(current.next, out var next);
                    if (next != null && next.id == start.id) { closed = true; break; }
                    current = next;
                }
                if (!closed || chain.Count < 3) continue;
                bool connected = true;
                for (int i = 0; i < chain.Count; i++)
                {
                    var a = chain[i]; var b = chain[(i + 1) % chain.Count];
                    if (a.x2 != b.x1 || a.y2 != b.y1) { connected = false; break; }
                }
                if (!connected) continue;
                visited.UnionWith(seen); loops.Add(new Loop(chain));
            }
            // 稳定降序：相同包围面积保留原闭环遍历次序。
            for (int i = 1; i < loops.Count; i++)
            {
                var key = loops[i]; int at = i;
                while (at > 0 && loops[at - 1].Area < key.Area) { loops[at] = loops[at - 1]; at--; }
                loops[at] = key;
            }
            var result = new HashSet<int>();
            for (int i = 0; i < loops.Count; i++)
            {
                var loop = loops[i]; Loop parent = null; double area = double.PositiveInfinity;
                for (int j = 0; j < i; j++) if (loops[j].Contains(loop) && loops[j].Area < area)
                { parent = loops[j]; area = parent.Area; }
                loop.solid = parent == null || (parent.ThinShell(loop) ? parent.solid : !parent.solid);
                if (!loop.solid) continue;
                foreach (var f in loop.chain) if (!f.Wall && f.y1 == f.y2)
                {
                    double x = (f.x1 + f.x2) / 2.0;
                    if (loop.Contains(x, f.y1 - 1) && !loop.Contains(x, f.y1 + 1)) result.Add(f.id);
                }
            }
            return result;
        }
        public static HashSet<int> Walls(IReadOnlyList<FootholdSegment> footholds)
        {
            var ids = new HashSet<int>();
            foreach (var f in footholds) if (f.Wall) ids.Add(f.id);
            return ids;
        }
    }
}
