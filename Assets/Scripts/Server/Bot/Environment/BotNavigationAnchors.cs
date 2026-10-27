using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Region = Server.Bot.Environment.BotNavigationGraph.Region;

namespace Server.Bot.Environment
{
    public static class BotNavigationAnchors
    {
        public static Dictionary<int, List<Point>> Build(MapRuntime map, BotWalkRegions regions, BotGroundIndex ground,
            BotGroundPhysics physics, BotMovementProfile profile)
        {
            var features = new Dictionary<int, HashSet<int>>();
            foreach (var rope in map.ropes)
            {
                Feature(features, regions, ground, rope.x, rope.Bottom - 1);
                Feature(features, regions, ground, rope.x, rope.Top - 1);
                Feature(features, regions, ground, rope.x, rope.Top - 60);
            }
            foreach (var portal in map.portals)
            {
                Feature(features, regions, ground, portal.x, portal.y - 16);
                if (portal.targetMap == map.mapId) foreach (var target in map.portals) if (target.name == portal.targetName)
                { Feature(features, regions, ground, target.x, target.y - 16); break; }
            }
            foreach (var region in regions.regions) if (!region.isRopeRegion)
            {
                var left = region.Left; var right = region.Right;
                Feature(features, regions, ground, left.x, left.y + 1);
                Feature(features, regions, ground, right.x, right.y + 1);
                if (region.Width <= 64) { var center = region.Center; Feature(features, regions, ground, center.x, center.y + 1); }
            }
            var result = new Dictionary<int, List<Point>>();
            foreach (var region in regions.regions) if (!region.isRopeRegion)
            {
                var xs = features.TryGetValue(region.id, out var values) ? new List<int>(values) : new List<int>(); xs.Sort();
                result[region.id] = Points(region, xs, physics, profile);
            }
            return result;
        }
        static void Feature(Dictionary<int, HashSet<int>> features, BotWalkRegions regions, BotGroundIndex ground, int x, int y)
        {
            var f = ground.Below(x, y);
            if (f == null || !regions.byFoothold.TryGetValue(f.id, out int id)) return;
            if (!features.TryGetValue(id, out var xs)) features[id] = xs = new HashSet<int>(); xs.Add(x);
        }
        static List<Point> Points(Region region, List<int> features, BotGroundPhysics physics, BotMovementProfile profile)
        {
            var points = new List<Point>(); Add(points, region.Left);
            int inset = Math.Max(8, BotGroundPhysics.JavaRound(profile.WalkVelocity(125) * 50 / 1000));
            if (region.Width > inset * 2)
            { Add(points, region.PointAt(region.minX + inset), 10); Add(points, region.PointAt(region.maxX - inset), 10); }
            int apexTicks = Math.Max(1, (int)Math.Ceiling(profile.JumpSpeed(555) * .05f / Math.Max(.001f, 2000 * .05f * .05f)));
            int jumpInset = Math.Max(inset * 3, physics.WalkStep(profile) * apexTicks);
            if (region.Width > jumpInset * 2)
            { Add(points, region.PointAt(region.minX + jumpInset), 10); Add(points, region.PointAt(region.maxX - jumpInset), 10); }
            if (region.Width > inset * 2) for (int x = region.minX + inset; x <= region.maxX - inset; x += Math.Max(physics.WalkStep(profile), 1))
                Add(points, region.PointAt(x));
            foreach (var segment in region.segments)
            { Add(points, new Point(segment.x1, segment.y1), 10); Add(points, new Point(segment.x2, segment.y2), 10); }
            if (region.Width >= 160) Add(points, region.Center);
            if (region.Width >= 320)
            { Add(points, region.PointAt(region.minX + region.Width / 3)); Add(points, region.PointAt(region.maxX - region.Width / 3)); }
            Add(points, region.Right);
            foreach (int x in features) if (x >= region.minX && x <= region.maxX) Add(points, region.PointAt(x));
            points.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            return points;
        }
        static void Add(List<Point> points, Point point, int spacing = 0)
        {
            foreach (var existing in points)
                if ((existing.x == point.x && existing.y == point.y) ||
                    (spacing > 0 && Math.Abs(existing.x - point.x) <= spacing && Math.Abs(existing.y - point.y) <= 8)) return;
            points.Add(point);
        }
    }
}
