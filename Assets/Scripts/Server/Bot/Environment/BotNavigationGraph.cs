using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Environment
{
    public sealed class BotNavigationGraph
    {
        public enum EdgeType { Walk, Jump, Drop, Climb, Portal }
        public readonly struct Point
        {
            public readonly int x, y;
            public Point(int x, int y) { this.x = x; this.y = y; }
        }
        public sealed class Segment
        {
            public readonly int footholdId, x1, y1, x2, y2, minX, maxX;
            public readonly bool forbidFallDown, collidableFromBelow;
            public Segment(FootholdSegment foothold, bool collidableFromBelow = false)
            {
                footholdId = foothold.id; x1 = (int)foothold.x1; y1 = (int)foothold.y1; x2 = (int)foothold.x2; y2 = (int)foothold.y2;
                minX = Math.Min(x1, x2); maxX = Math.Max(x1, x2); forbidFallDown = foothold.forbidFallDown;
                this.collidableFromBelow = collidableFromBelow;
            }
            public bool ContainsX(int x) => x >= minX && x <= maxX;
            public int ClampX(int x) => Math.Max(minX, Math.Min(maxX, x));
            public Point PointAt(int x)
            {
                int clamped = ClampX(x);
                if (x1 == x2) return new Point(clamped, Math.Min(y1, y2));
                // 源Java表达式两操作数均int，先整数除法再赋double；不得悄悄改为线性浮点插值。
                double ratio = (clamped - x1) / (x2 - x1);
                return new Point(clamped, BotGroundPhysics.JavaRound(y1 + (y2 - y1) * ratio));
            }
        }
        public sealed class Region
        {
            public readonly int id, minX, maxX, minY, maxY;
            public readonly List<Segment> segments;
            public readonly bool isRopeRegion, isLadder;
            public Region(int id, List<Segment> segments)
            {
                if (segments.Count == 0) throw new ArgumentException("Bot nav region requires at least one segment");
                this.id = id; this.segments = new List<Segment>(segments);
                minX = minY = int.MaxValue; maxX = maxY = int.MinValue;
                foreach (var s in segments)
                {
                    minX = Math.Min(minX, s.minX); maxX = Math.Max(maxX, s.maxX);
                    minY = Math.Min(minY, Math.Min(s.y1, s.y2)); maxY = Math.Max(maxY, Math.Max(s.y1, s.y2));
                }
            }
            public Region(int id, int ropeX, int topY, int bottomY, bool ladder)
            {
                this.id = id; minX = maxX = ropeX; minY = topY; maxY = bottomY;
                isRopeRegion = true; isLadder = ladder; segments = new List<Segment>();
            }
            public int Width => Math.Max(0, maxX - minX);
            public int Height => Math.Max(0, maxY - minY);
            public Point Left => PointAt(minX);
            public Point Right => PointAt(maxX);
            public Point Center => isRopeRegion ? new Point(minX, minY + Height / 2) : PointAt(minX + Width / 2);
            public Point PointAt(int x) => isRopeRegion ? new Point(minX, minY + Height / 2) : BestSegment(x).PointAt(x);
            public bool ForbidFallDownAt(int x) => !isRopeRegion && segments.Count > 0 && BestSegment(x).forbidFallDown;
            Segment BestSegment(int x)
            {
                var best = segments[0]; int bestDistance = Distance(best, x);
                for (int i = 1; i < segments.Count; i++)
                { int distance = Distance(segments[i], x); if (distance < bestDistance) { best = segments[i]; bestDistance = distance; } }
                return best;
            }
            static int Distance(Segment s, int x) => s.ContainsX(x) ? 0 : x < s.minX ? s.minX - x : x - s.maxX;
        }
        public sealed class Edge
        {
            public readonly int fromRegionId, toRegionId, launchMinX, launchMaxX, launchStepX, portalId, ropeX, ropeTopY, ropeBottomY, cost;
            public readonly EdgeType type;
            public readonly Point startPoint, endPoint;
            public Edge(int from, int to, EdgeType type, Point start, Point end, int launchMin, int launchMax,
                int step, int portalId, int ropeX, int ropeTop, int ropeBottom, int cost)
            {
                fromRegionId = from; toRegionId = to; this.type = type; startPoint = start; endPoint = end;
                launchMinX = Math.Min(launchMin, launchMax); launchMaxX = Math.Max(launchMin, launchMax); launchStepX = step;
                this.portalId = portalId; this.ropeX = ropeX; ropeTopY = ropeTop; ropeBottomY = ropeBottom; this.cost = cost;
            }
            public Edge(int from, int to, EdgeType type, Point start, Point end, int step, int portalId, int ropeX, int ropeTop, int ropeBottom, int cost)
                : this(from, to, type, start, end, start.x, start.x, step, portalId, ropeX, ropeTop, ropeBottom, cost) { }
            public bool ContainsLaunchX(int x, int tolerance = 0) => x >= launchMinX - tolerance && x <= launchMaxX + tolerance;
            public Point NearestLaunch(int x) => new Point(Math.Max(launchMinX, Math.Min(launchMaxX, x)), startPoint.y);
        }
        public readonly int mapId, version;
        public readonly BotMovementProfile profile;
        public readonly List<Region> regions;
        public readonly Dictionary<int, Region> regionsById;
        public readonly Dictionary<int, int> regionByFoothold;
        public readonly Dictionary<int, List<Edge>> outgoing;
        public readonly HashSet<int> collidableWalls, collidableFromBelow;
        public BotNavigationGraph(int mapId, int version, BotMovementProfile profile, List<Region> regions,
            Dictionary<int, Region> byId, Dictionary<int, int> byFoothold, Dictionary<int, List<Edge>> outgoing,
            HashSet<int> walls, HashSet<int> fromBelow = null)
        {
            this.mapId = mapId; this.version = version; this.profile = profile;
            this.regions = new List<Region>(regions); regionsById = new Dictionary<int, Region>(byId);
            regionByFoothold = new Dictionary<int, int>(byFoothold); this.outgoing = new Dictionary<int, List<Edge>>();
            foreach (var pair in outgoing) this.outgoing[pair.Key] = new List<Edge>(pair.Value);
            collidableWalls = new HashSet<int>(walls); collidableFromBelow = fromBelow == null ? new HashSet<int>() : new HashSet<int>(fromBelow);
        }
        public Region GetRegion(int id) => regionsById.TryGetValue(id, out var region) ? region : null;
        public IReadOnlyList<Edge> Outgoing(int id) => outgoing.TryGetValue(id, out var edges) ? edges : Array.Empty<Edge>();
        public bool HasInterRegionEdge(int from, int to)
        { foreach (var edge in Outgoing(from)) if (edge.fromRegionId != edge.toRegionId && edge.toRegionId == to) return true; return false; }
        public HashSet<int> MutualAdjacent(int id)
        {
            var adjacent = new HashSet<int>();
            foreach (var edge in Outgoing(id)) if (edge.fromRegionId != edge.toRegionId && HasInterRegionEdge(edge.toRegionId, id)) adjacent.Add(edge.toRegionId);
            return adjacent;
        }
        public int RopeRegion(Point position)
        {
            foreach (var r in regions) if (r.isRopeRegion && Math.Abs(position.x - r.minX) <= 8 && position.y >= r.minY && position.y <= r.maxY) return r.id;
            return -1;
        }
        public int FindRegionId(BotGroundIndex ground, Point position)
        {
            if (ground == null) return -1;
            var foothold = ground.Ground(position.x, position.y);
            if (foothold != null && regionByFoothold.TryGetValue(foothold.id, out int id) && id >= 0) return id;
            return RopeRegion(position);
        }
    }
}
