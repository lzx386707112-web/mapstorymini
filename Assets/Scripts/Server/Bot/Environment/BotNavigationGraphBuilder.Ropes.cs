using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Region = Server.Bot.Environment.BotNavigationGraph.Region;
using EdgeType = Server.Bot.Environment.BotNavigationGraph.EdgeType;

namespace Server.Bot.Environment
{
    public sealed partial class BotNavigationGraphBuilder
    {
        readonly Dictionary<(int, int, int, int, int, int), BotTrajectory.RopeGrab> ropeGrabs = new Dictionary<(int, int, int, int, int, int), BotTrajectory.RopeGrab>();
        MapRope Rope(Region region)
        {
            foreach (var rope in map.ropes) if (rope.x == region.minX && rope.Top == region.minY && rope.Bottom == region.maxY) return rope;
            return null;
        }
        BotTrajectory.RopeGrab Grab(Point start, int step, MapRope rope)
        {
            var key = (start.x, start.y, step, rope.x, rope.Top, rope.Bottom);
            if (!ropeGrabs.TryGetValue(key, out var result)) { result = trajectory.Grab(start, step, rope); ropeGrabs[key] = result; }
            return result;
        }
        bool ReachableRope(Point from, MapRope rope)
        {
            if (BotRopeMotion.CanGrab(from, rope)) return true;
            if (rope.Top >= from.y) return false;
            float force = profile.JumpSpeed(555) * .05f, gravity = 2000 * .05f * .05f;
            int height = (int)Math.Ceiling(force * force / (2f * gravity));
            float up = force / gravity, apex = force * force / (2f * gravity);
            float down = (float)Math.Sqrt(2f * (apex + Math.Max(0, rope.Bottom - from.y)) / gravity);
            int ticks = Math.Max(1, (int)Math.Ceiling(up + down));
            return rope.Bottom >= from.y - height && Math.Abs(rope.x - from.x) <= physics.WalkStep(profile) * ticks;
        }
        void RopeEntries(Region ropeRegion)
        {
            var rope = Rope(ropeRegion); if (rope == null) return;
            int first = BotRopeMotion.FirstClimbableY(rope), walk = physics.WalkStep(profile);
            foreach (var from in regions.regions) if (!from.isRopeRegion) foreach (var anchor in anchors[from.id])
            {
                bool near = Math.Abs(anchor.x - rope.x) <= 8;
                if (near && anchor.y >= first && anchor.y <= rope.Bottom)
                {
                    var end = new Point(rope.x, Math.Max(first, Math.Min(anchor.y, rope.Bottom)));
                    Add(from.id, ropeRegion.id, EdgeType.Climb, anchor, end, anchor.x, anchor.x, 0, 0, 50); continue;
                }
                if (near && anchor.y < rope.Top && rope.Top - anchor.y <= 16)
                {
                    Add(from.id, ropeRegion.id, EdgeType.Climb, anchor, new Point(rope.x, first), anchor.x, anchor.x, 0, 0, 50); continue;
                }
                if (near && anchor.y <= rope.Top + 30)
                {
                    var down = trajectory.Grab(anchor, 0, rope, false, true);
                    if (down != null) Add(from.id, ropeRegion.id, EdgeType.Climb, anchor, down.point, anchor.x, anchor.x, 0, 0, down.TimeMs);
                }
                if (!ReachableRope(anchor, rope)) continue;
                foreach (int step in new[] { -walk, 0, walk })
                {
                    Func<int, bool> valid = x => Approachable(from, x) && Grab(from.PointAt(x), step, rope) != null;
                    if (!valid(anchor.x)) continue;
                    int min = Boundary(from, anchor.x, true, valid), max = Boundary(from, anchor.x, false, valid);
                    var start = from.PointAt((min + max) / 2); var grab = Grab(start, step, rope);
                    if (grab != null) Add(from.id, ropeRegion.id, EdgeType.Climb, start, grab.point, min, max, step, 0, grab.TimeMs);
                }
            }
        }
        void RopeExits(Region region)
        {
            var rope = Rope(region); if (rope == null) return;
            int first = BotRopeMotion.FirstClimbableY(rope), walk = physics.WalkStep(profile);
            var top = new BotRopeMotion(ground).TopExitLanding(rope);
            if (top.HasValue)
            {
                int target = FootholdRegion(ground.Ground(top.Value.x, top.Value.y));
                if (target >= 0 && !regions.byId[target].isRopeRegion)
                    Add(region.id, target, EdgeType.Climb, new Point(rope.x, rope.Top), top.Value, rope.x, rope.x, 0, 0, 50);
            }
            var ys = new List<int> { first };
            for (int y = rope.Top + 30; y < rope.Bottom; y += 30) if (y > first) ys.Add(y);
            if (ys[ys.Count - 1] != rope.Bottom) ys.Add(rope.Bottom);
            foreach (int y in ys) foreach (int step in new[] { -walk, 0, walk })
            {
                var start = new Point(rope.x, y); var landing = trajectory.RopeJump(start, step);
                if (landing == null) continue;
                int target = FootholdRegion(landing.foothold);
                if (target >= 0 && !regions.byId[target].isRopeRegion)
                    Add(region.id, target, EdgeType.Climb, start, landing.point, start.x, start.x, step, 0, landing.TimeMs);
            }
            ys.Clear();
            for (int y = first; y <= rope.Bottom; y += 5) ys.Add(y);
            if (ys.Count == 0 || ys[ys.Count - 1] != rope.Bottom) ys.Add(rope.Bottom);
            foreach (int y in ys) foreach (var other in regions.regions) if (other.isRopeRegion && other.id != region.id)
            {
                var target = Rope(other); if (target == null || Math.Abs(rope.x - target.x) > walk * 30) continue;
                int step = target.x > rope.x ? walk : -walk;
                var start = new Point(rope.x, y); var grab = trajectory.Grab(start, step, target, true);
                if (grab != null) Add(region.id, other.id, EdgeType.Climb, start, grab.point, start.x, start.x, step, 0, grab.TimeMs);
            }
        }
    }
}
