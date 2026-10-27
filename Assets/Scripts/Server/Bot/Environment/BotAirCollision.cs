using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    public sealed class BotAirCollision
    {
        public enum Kind { None, Wall, Ceiling, Land }
        public readonly struct Hit
        {
            public readonly Kind kind;
            public readonly Point point;
            public readonly FootholdSegment foothold;
            public readonly double progress;
            public Hit(Kind kind, Point point, FootholdSegment foothold, double progress)
            { this.kind = kind; this.point = point; this.foothold = foothold; this.progress = progress; }
            public static Hit None => new Hit(Kind.None, default, null, double.PositiveInfinity);
        }
        readonly MapRuntime map;
        readonly BotGroundIndex ground;
        readonly List<FootholdSegment> walls = new List<FootholdSegment>(), ceilings = new List<FootholdSegment>();
        public BotAirCollision(MapRuntime map, BotGroundIndex ground)
        {
            this.map = map; this.ground = ground;
            var below = BotFootholdCollision.FromBelow(map.footholds);
            foreach (var f in map.footholds) { if (f.Wall) walls.Add(f); if (below.Contains(f.id)) ceilings.Add(f); }
        }
        public Hit Resolve(Point from, Point to)
        {
            var best = Wall(from, to); var ceiling = Ceiling(from, to); var landing = Landing(from, to);
            if (ceiling.kind != Kind.None && ceiling.progress < best.progress) best = ceiling;
            if (landing.kind != Kind.None && landing.progress < best.progress) best = landing;
            return best;
        }
        public Hit Wall(Point from, Point to, bool allowWalkableEndpoint = false)
        {
            if (from.x == to.x) return Hit.None;
            var best = Boundary(from, to);
            foreach (var f in walls)
            {
                // 原Java源码在赋double前执行整数除法，与Segment.pointAt相同。
                double progress = ((int)f.x1 - from.x) / (to.x - from.x);
                if (progress <= 0 || progress > 1) continue;
                double y = from.y + (to.y - from.y) * progress;
                int minY = (int)Math.Min(f.y1, f.y2), maxY = (int)Math.Max(f.y1, f.y2);
                if (y < minY || y > maxY) continue;
                if (allowWalkableEndpoint && (Math.Abs(y - minY) < .001 || (Math.Abs(y - maxY) < .001 && maxY - minY <= 26))) continue;
                if (progress < best.progress) best = new Hit(Kind.Wall,
                    new Point((int)f.x1 - Math.Sign(to.x - from.x), BotGroundPhysics.JavaRound(y)), f, progress);
            }
            return best;
        }
        Hit Boundary(Point from, Point to)
        {
            int width = map.right - map.left, height = map.bottom - map.top;
            if (width <= 0 || height <= 0 || from.x == to.x) return Hit.None;
            int minX = (int)map.minX, maxX = (int)map.maxX, dir = Math.Sign(to.x - from.x);
            bool usable = minX < maxX, synthetic = width >= 262144 && height >= 262144;
            int left = usable ? synthetic ? minX : Math.Min(map.left, minX) : map.left;
            int right = usable ? synthetic ? maxX : Math.Max(map.right, maxX) : map.right;
            int x = dir > 0 ? right : left;
            if (dir > 0 && (from.x > x || to.x <= x)) return Hit.None;
            if (dir < 0 && (from.x < x || to.x >= x)) return Hit.None;
            double progress = (x - from.x) / (to.x - from.x);
            if (progress < 0 || progress > 1) return Hit.None;
            return new Hit(Kind.Wall, new Point(x, BotGroundPhysics.JavaRound(from.y + (to.y - from.y) * progress)), null, progress);
        }
        public Hit Ceiling(Point from, Point to)
        {
            if (to.y >= from.y) return Hit.None;
            var best = Hit.None;
            foreach (var f in ceilings)
            {
                int y = (int)f.y1;
                if (f.y1 != f.y2 || y > from.y || y < to.y) continue;
                double progress = (y - from.y) / (to.y - from.y);
                if (progress <= 0 || progress > 1) continue;
                double x = from.x + (to.x - from.x) * progress;
                if (x < Math.Min(f.x1, f.x2) || x > Math.Max(f.x1, f.x2)) continue;
                if (progress < best.progress) best = new Hit(Kind.Ceiling, new Point(BotGroundPhysics.JavaRound(x), y + 1), f, progress);
            }
            return best;
        }
        public Hit Landing(Point from, Point to)
        {
            if (to.y < from.y) return Hit.None;
            int dir = Math.Sign(to.x - from.x);
            if (dir == 0) return LandingAtX(from, to, to.x, 1);
            int steps = Math.Abs(to.x - from.x);
            for (int i = 0; i <= steps; i++)
            {
                double progress = i / steps;
                var hit = LandingAtX(from, to, from.x + dir * i, progress);
                if (hit.kind == Kind.Land) return hit;
            }
            return Hit.None;
        }
        Hit LandingAtX(Point from, Point to, int x, double progress)
        {
            int y = BotGroundPhysics.JavaRound(from.y + (to.y - from.y) * progress);
            var landing = LandingAtProbe(from, x, y, progress, from.y + 1, false);
            if (landing.kind == Kind.Land) return landing;
            return x != from.x ? LandingAtProbe(from, x, y, progress, from.y, true) : Hit.None;
        }
        Hit LandingAtProbe(Point from, int x, int y, double progress, int probeY, bool tangent)
        {
            var floor = ground.PointBelow(x, probeY);
            if (!floor.HasValue || floor.Value.y < Math.Min(from.y, y) || floor.Value.y > Math.Max(from.y, y) ||
                (tangent && floor.Value.y != from.y)) return Hit.None;
            var f = ground.Below(x, probeY);
            return f == null ? Hit.None : new Hit(Kind.Land, floor.Value, f, progress);
        }
    }
}
