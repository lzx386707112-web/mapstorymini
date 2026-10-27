using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Environment
{
    // 原导航图的区域构建阶段；不将未生成跳跃/攀爬边的区域表冒充完整导航图。
    public sealed class BotWalkRegions
    {
        public readonly List<BotNavigationGraph.Region> regions = new List<BotNavigationGraph.Region>();
        public readonly Dictionary<int, BotNavigationGraph.Region> byId = new Dictionary<int, BotNavigationGraph.Region>();
        public readonly Dictionary<int, int> byFoothold = new Dictionary<int, int>();
        public readonly Dictionary<int, FootholdSegment> footholds = new Dictionary<int, FootholdSegment>();
        public readonly HashSet<int> fromBelow, walls;
        readonly Dictionary<int, int> parent = new Dictionary<int, int>();
        readonly Dictionary<int, int> rank = new Dictionary<int, int>();
        public BotWalkRegions(MapRuntime map)
        {
            fromBelow = BotFootholdCollision.FromBelow(map.footholds); walls = BotFootholdCollision.Walls(map.footholds);
            foreach (var f in map.footholds) { footholds[f.id] = f; if (!f.Wall) { parent[f.id] = f.id; rank[f.id] = 0; } }
            foreach (var f in map.footholds) if (!f.Wall)
            {
                if (footholds.TryGetValue(f.previous, out var prev)) Union(f, prev);
                if (footholds.TryGetValue(f.next, out var next)) Union(f, next);
            }
            var grouped = new Dictionary<int, List<FootholdSegment>>();
            foreach (var f in map.footholds) if (!f.Wall)
            {
                int root = Find(f.id);
                if (!grouped.TryGetValue(root, out var group)) grouped[root] = group = new List<FootholdSegment>();
                group.Add(f);
            }
            var groups = new List<List<FootholdSegment>>(grouped.Values);
            // List.Sort不是稳定排序，使用原键的稳定插入排序。
            for (int i = 1; i < groups.Count; i++)
            {
                var key = groups[i]; int at = i;
                while (at > 0 && CompareGroup(groups[at - 1], key) > 0) { groups[at] = groups[at - 1]; at--; }
                groups[at] = key;
            }
            int id = 1;
            foreach (var group in groups)
            {
                group.Sort((a, b) => {
                    int c = Math.Min(a.x1, a.x2).CompareTo(Math.Min(b.x1, b.x2));
                    if (c == 0) c = Math.Min(a.y1, a.y2).CompareTo(Math.Min(b.y1, b.y2));
                    return c == 0 ? a.id.CompareTo(b.id) : c;
                });
                var segments = new List<BotNavigationGraph.Segment>();
                foreach (var f in group) { segments.Add(new BotNavigationGraph.Segment(f, fromBelow.Contains(f.id))); byFoothold[f.id] = id; }
                Add(new BotNavigationGraph.Region(id++, segments));
            }
            foreach (var rope in map.ropes) Add(new BotNavigationGraph.Region(id++, rope.x, rope.Top, rope.Bottom, rope.ladder));
        }
        void Add(BotNavigationGraph.Region region) { regions.Add(region); byId[region.id] = region; }
        int Find(int id)
        { int root = id; while (parent[root] != root) root = parent[root]; while (parent[id] != id) { int next = parent[id]; parent[id] = root; id = next; } return root; }
        void Union(FootholdSegment a, FootholdSegment b)
        {
            if (!BotGroundIndex.CanWalkAcross(a, b)) return;
            int ra = Find(a.id), rb = Find(b.id); if (ra == rb) return;
            if (rank[ra] < rank[rb]) parent[ra] = rb;
            else if (rank[ra] > rank[rb]) parent[rb] = ra;
            else { parent[rb] = ra; rank[ra]++; }
        }
        static int CompareGroup(List<FootholdSegment> a, List<FootholdSegment> b)
        {
            float ay = float.MaxValue, ax = float.MaxValue, by = float.MaxValue, bx = float.MaxValue;
            foreach (var f in a) { ay = Math.Min(ay, Math.Min(f.y1, f.y2)); ax = Math.Min(ax, Math.Min(f.x1, f.x2)); }
            foreach (var f in b) { by = Math.Min(by, Math.Min(f.y1, f.y2)); bx = Math.Min(bx, Math.Min(f.x1, f.x2)); }
            int c = ay.CompareTo(by); return c == 0 ? ax.CompareTo(bx) : c;
        }
        public readonly struct Sample
        {
            public readonly BotNavigationGraph.Point point;
            public readonly FootholdSegment foothold;
            public Sample(BotNavigationGraph.Point point, FootholdSegment foothold) { this.point = point; this.foothold = foothold; }
        }
        public Sample? GroundSample(FootholdSegment footing, int x, int referenceY)
        {
            if (footing == null || !byFoothold.TryGetValue(footing.id, out int id)) return null;
            var region = byId[id]; bool containing = false;
            foreach (var s in region.segments) if (s.ContainsX(x)) { containing = true; break; }
            Sample? best = null; int bestScore = int.MaxValue;
            foreach (var s in region.segments)
            {
                int dx = Math.Max(s.minX - x, Math.Max(0, x - s.maxX));
                if (!s.ContainsX(x) && (containing || dx > 2)) continue;
                var p = s.PointAt(x); int dy = p.y - referenceY;
                if (dy > 16 || dy < -26) continue;
                int score = dx * 1000 + Math.Abs(dy);
                if (!best.HasValue || score < bestScore || (score == bestScore && p.y > best.Value.point.y))
                { best = new Sample(p, footholds[s.footholdId]); bestScore = score; }
            }
            return best;
        }
    }
}
