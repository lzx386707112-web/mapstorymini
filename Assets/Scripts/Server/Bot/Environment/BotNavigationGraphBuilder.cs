using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Region = Server.Bot.Environment.BotNavigationGraph.Region;
using Edge = Server.Bot.Environment.BotNavigationGraph.Edge;
using EdgeType = Server.Bot.Environment.BotNavigationGraph.EdgeType;

namespace Server.Bot.Environment
{
    public sealed partial class BotNavigationGraphBuilder
    {
        readonly MapRuntime map;
        readonly BotMovementProfile profile;
        readonly BotWalkRegions regions;
        readonly BotGroundIndex ground;
        readonly BotGroundPhysics physics = new BotGroundPhysics();
        readonly BotAirCollision collision;
        readonly BotGroundMotion motion;
        readonly BotTrajectory trajectory;
        readonly Dictionary<int, List<Point>> anchors;
        readonly Dictionary<int, List<Edge>> outgoing = new Dictionary<int, List<Edge>>();
        readonly HashSet<string> edgeKeys = new HashSet<string>();
        readonly Dictionary<(int, int, int), BotTrajectory.StableLanding> jumps = new Dictionary<(int, int, int), BotTrajectory.StableLanding>();
        public BotNavigationGraphBuilder(MapRuntime map, BotMovementProfile profile = null)
        {
            this.map = map; this.profile = profile ?? BotMovementProfile.Base;
            regions = new BotWalkRegions(map); ground = new BotGroundIndex(map); collision = new BotAirCollision(map, ground);
            motion = new BotGroundMotion(map, ground, regions, collision, physics); trajectory = new BotTrajectory(map, collision, this.profile);
            anchors = BotNavigationAnchors.Build(map, regions, ground, physics, this.profile);
        }
        public BotNavigationGraph Build()
        {
            foreach (var f in map.footholds) if (!f.Wall)
            {
                if (regions.footholds.TryGetValue(f.previous, out var previous)) Walk(f, previous);
                if (regions.footholds.TryGetValue(f.next, out var next)) Walk(f, next);
            }
            foreach (var region in regions.regions) if (!region.isRopeRegion)
            { DirectionalDrop(region, -1); DirectionalDrop(region, 1); DownJumps(region); }
            foreach (var region in regions.regions) if (!region.isRopeRegion) Jumps(region);
            foreach (var region in regions.regions) if (region.isRopeRegion) RopeEntries(region);
            foreach (var region in regions.regions) if (region.isRopeRegion) RopeExits(region);
            foreach (var portal in map.portals) Portal(portal);
            return new BotNavigationGraph(map.mapId, 61, profile, regions.regions, regions.byId, regions.byFoothold, outgoing, regions.walls, regions.fromBelow);
        }
        void Add(int from, int to, EdgeType type, Point start, Point end, int min, int max, int step, int portal, int cost,
            int ropeX = 0, int ropeTop = 0, int ropeBottom = 0)
        {
            string key = from + ":" + to + ":" + type + ":" + start.x + ":" + start.y + ":" + end.x + ":" + end.y + ":" + step + ":" + portal +
                ":" + ropeX + ":" + ropeTop + ":" + ropeBottom + ":" + min + ":" + max;
            if (!edgeKeys.Add(key)) return;
            if (!outgoing.TryGetValue(from, out var edges)) outgoing[from] = edges = new List<Edge>();
            edges.Add(new Edge(from, to, type, start, end, min, max, step, portal, ropeX, ropeTop, ropeBottom, cost));
        }
        int BelowRegion(Point point)
        { var f = ground.Below(point.x, point.y); return f != null && regions.byFoothold.TryGetValue(f.id, out int id) ? id : -1; }
        int FootholdRegion(FootholdSegment f) => f != null && regions.byFoothold.TryGetValue(f.id, out int id) ? id : -1;
        int TravelCost(int dx) => Math.Max(0, BotGroundPhysics.JavaRound(dx * 1000.0 / Math.Max(1, profile.WalkVelocity(125))));
        void Walk(FootholdSegment from, FootholdSegment to)
        {
            if (to.Wall) return;
            int a = FootholdRegion(from), b = FootholdRegion(to); if (a < 0 || b < 0 || a == b) return;
            int best = int.MaxValue; Point start = default, end = default;
            for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
            {
                var p = new Point((int)(i == 0 ? from.x1 : from.x2), (int)(i == 0 ? from.y1 : from.y2));
                var q = new Point((int)(j == 0 ? to.x1 : to.x2), (int)(j == 0 ? to.y1 : to.y2));
                int distance = Math.Abs(q.x - p.x) + Math.Abs(q.y - p.y);
                if (distance < best) { best = distance; start = p; end = q; }
            }
            if (!BotGroundPhysics.WalkableEndpoint(Math.Abs(end.x - start.x), end.y - start.y)) return;
            start = regions.byId[a].PointAt(start.x); end = regions.byId[b].PointAt(end.x); int cost = TravelCost(Math.Abs(end.x - start.x));
            Add(a, b, EdgeType.Walk, start, end, start.x, start.x, 0, 0, cost);
            Add(b, a, EdgeType.Walk, end, start, end.x, end.x, 0, 0, cost);
        }
        void Portal(MapPortal portal)
        {
            if (portal.targetMap != map.mapId) return;
            MapPortal target = null;
            foreach (var p in map.portals) if (p.name == portal.targetName) { target = p; break; }
            if (target == null || target.id == portal.id) return;
            int a = BelowRegion(new Point(portal.x, portal.y - 16)), b = BelowRegion(new Point(target.x, target.y - 16));
            if (a < 0 || b < 0) return;
            var start = regions.byId[a].PointAt(portal.x); var end = regions.byId[b].PointAt(target.x);
            Add(a, b, EdgeType.Portal, start, end, start.x, start.x, 0, portal.id, 0);
        }
        void DirectionalDrop(Region from, int direction)
        {
            var endpoint = direction < 0 ? from.Left : from.Right;
            if (from.ForbidFallDownAt(endpoint.x)) return;
            int runway = physics.LaunchRunway(map.footholdSpeed, profile);
            int x = direction < 0 ? Math.Min(from.maxX, endpoint.x + runway) : Math.Max(from.minX, endpoint.x - runway);
            int actual = Math.Abs(x - endpoint.x); if (actual < Math.Min(runway, 20)) return;
            var start = from.PointAt(x);
            if (collision.Wall(start, endpoint, true).kind == BotAirCollision.Kind.Wall) return;
            int step = physics.WalkStep(profile) * direction;
            var landing = trajectory.Fall(endpoint, step); if (landing == null) return;
            int target = FootholdRegion(landing.foothold);
            if (target < 0 || target == from.id || landing.point.y <= endpoint.y + 4 || landing.point.y - endpoint.y > 300) return;
            Add(from.id, target, EdgeType.Drop, start, landing.point, start.x, start.x, step, 0, landing.TimeMs + TravelCost(actual));
        }
        int DropStep(Region from, Point point)
        {
            var left = from.Left; var right = from.Right;
            if (Math.Abs(point.x - left.x) <= 10 && Math.Abs(point.y - left.y) <= 12) return -physics.WalkStep(profile);
            if (Math.Abs(point.x - right.x) <= 10 && Math.Abs(point.y - right.y) <= 12) return physics.WalkStep(profile);
            return 0;
        }
        bool WallBoundary(Point launch)
        {
            foreach (var f in map.footholds) if (f.Wall && f.x1 == launch.x && launch.y > Math.Min(f.y1, f.y2) && launch.y <= Math.Max(f.y1, f.y2)) return true;
            return false;
        }
        bool Approachable(Region from, int x)
        {
            if (WallBoundary(from.PointAt(x))) return false;
            if (x <= from.minX || !CanWalkTo(from, x - 1, x)) return x < from.maxX && CanWalkTo(from, x + 1, x);
            return true;
        }
        bool CanWalkTo(Region from, int x, int launchX)
        {
            var a = from.PointAt(x); var b = from.PointAt(launchX);
            return (a.x != b.x || a.y != b.y) && motion.CanStep(a, b.x - a.x);
        }
        BotTrajectory.Landing ValidDown(Region from, int x, int requiredTarget = int.MinValue)
        {
            var launch = from.PointAt(x); var f = ground.Ground(launch.x, launch.y);
            if (WallBoundary(launch) || f == null || f.forbidFallDown || from.ForbidFallDownAt(x) || DropStep(from, launch) != 0) return null;
            var landing = trajectory.DownJump(launch);
            if (landing == null || landing.point.y <= launch.y + 4 || landing.point.y - launch.y > 200) return null;
            int target = FootholdRegion(landing.foothold);
            if (target < 0 || target == from.id || (requiredTarget != int.MinValue && target != requiredTarget)) return null;
            return landing;
        }
        static int Boundary(Region from, int start, bool left, Func<int, bool> valid)
        {
            int limit = left ? from.minX : from.maxX, good = start, bad = start;
            for (int step = 1; ; step *= 2)
            {
                int probe = left ? Math.Max(limit, start - step) : Math.Min(limit, start + step);
                if (probe == good) break;
                if (!valid(probe)) { bad = probe; break; }
                good = probe; if (probe == limit) return probe;
            }
            while (Math.Abs(good - bad) > 1) { int probe = (good + bad) / 2; if (valid(probe)) good = probe; else bad = probe; }
            return good;
        }
        void DownJumps(Region from)
        {
            foreach (var anchor in anchors[from.id])
            {
                if (DropStep(from, anchor) != 0) continue;
                var landing = ValidDown(from, anchor.x); if (landing == null) continue;
                int target = FootholdRegion(landing.foothold);
                Func<int, bool> valid = x => ValidDown(from, x, target) != null;
                int min = Boundary(from, anchor.x, true, valid), max = Boundary(from, anchor.x, false, valid);
                var start = from.PointAt((min + max) / 2); landing = ValidDown(from, start.x, target); if (landing == null) continue;
                int below = BelowRegion(landing.point); if (below < 0 || below == from.id) continue;
                Add(from.id, below, EdgeType.Drop, start, landing.point, min, max, 0, 0, landing.TimeMs + 2000);
            }
        }
        BotTrajectory.StableLanding Jump(Point start, int step)
        {
            var key = (start.x, start.y, step);
            if (!jumps.TryGetValue(key, out var result))
            { result = trajectory.PostLanding(trajectory.Jump(start, step), Math.Sign(step), motion, physics); jumps[key] = result; }
            return result;
        }
        bool ValidJump(Region from, int x, int step, int target)
        {
            if (!Approachable(from, x)) return false;
            var result = Jump(from.PointAt(x), step);
            return result != null && !result.lostGround && FootholdRegion(result.foothold) == target;
        }
        void Jumps(Region from)
        {
            int jumpStep = physics.WalkStep(profile);
            foreach (var anchor in anchors[from.id]) foreach (int step in new[] { -jumpStep, 0, jumpStep })
            {
                var result = Jump(anchor, step); if (result == null || result.lostGround) continue;
                int target = FootholdRegion(result.foothold); var landing = result.landing;
                if (target < 0 || target == from.id || (Math.Abs(landing.point.x - anchor.x) < 6 && Math.Abs(landing.point.y - anchor.y) < 6) || !ValidJump(from, anchor.x, step, target)) continue;
                Func<int, bool> valid = x => ValidJump(from, x, step, target);
                int min = Boundary(from, anchor.x, true, valid), max = Boundary(from, anchor.x, false, valid);
                var start = from.PointAt((min + max) / 2); result = Jump(start, step);
                if (result == null || result.lostGround || FootholdRegion(result.foothold) != target) continue;
                Add(from.id, target, EdgeType.Jump, start, result.landing.point, min, max, step, 0, result.landing.TimeMs);
            }
        }
    }
}
