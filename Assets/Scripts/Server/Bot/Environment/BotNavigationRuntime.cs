using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Edge = Server.Bot.Environment.BotNavigationGraph.Edge;
using EdgeType = Server.Bot.Environment.BotNavigationGraph.EdgeType;
using Region = Server.Bot.Environment.BotNavigationGraph.Region;

namespace Server.Bot.Environment
{
    // BotNavigationManager：运行时边提交、起跳窗口、绳梯入口/出口与portal时序。
    public sealed class BotNavigationRuntime
    {
        readonly BotMovementRuntime motion;
        readonly BotNavigationGraph graph;
        readonly Random random;
        readonly BotTrajectory trajectory;
        readonly BotGroundMotion groundMotion;
        int targetRegion = -1, selectedLaunchX = int.MinValue, launchDelay = int.MinValue;
        Edge selectedLaunchEdge;
        int portalReadyTicks = -1, blockedTicks, blockedGiveUp;
        Point blockedPosition;
        long portalCooldown;
        string blockedReason;
        public Func<MapPortal, bool> UsePortal;
        public string LastDecision { get; private set; }
        public BotNavigationRuntime(BotMovementRuntime motion, BotNavigationGraph graph, Random random = null)
        {
            this.motion = motion; this.graph = graph; this.random = random ?? new Random();
            var collision = new BotAirCollision(motion.map, motion.ground);
            trajectory = new BotTrajectory(motion.map, collision, motion.profile);
            groundMotion = new BotGroundMotion(motion.map, motion.ground, motion.regions, collision, motion.physics);
        }
        public void Clear()
        {
            motion.ClearNavigation(); targetRegion = -1; selectedLaunchEdge = null;
            selectedLaunchX = launchDelay = int.MinValue; blockedTicks = 0;
        }
        public int CurrentRegion()
        {
            if (motion.Climbing || motion.actor.stance == 14 || motion.actor.stance == 16)
            { int rope = graph.RopeRegion(motion.Position); if (rope >= 0) return rope; }
            return motion.InAir ? -1 : graph.FindRegionId(motion.ground, motion.Position);
        }
        public int TargetRegion(Point point)
        {
            int rope = graph.RopeRegion(point);
            var ground = motion.ground.GroundPoint(point.x, point.y);
            return rope >= 0 && (!ground.HasValue || ground.Value.y - point.y > 16) ? rope : graph.FindRegionId(motion.ground, point);
        }
        public bool Tick(Point? rawTarget, long now, bool runAi = true, bool precise = false)
        {
            Point? target = Resolve(rawTarget, now, runAi, out bool consumed);
            if (!consumed)
            {
                if (precise && motion.navEdge == null) motion.preciseTarget = true;
                motion.Tick(target, now, runAi);
                if (!motion.InAir && !motion.Climbing && motion.navEdge != null)
                {
                    var edge = Reuse(CurrentRegion(), targetRegion);
                    if (edge == null) Clear();
                    else if (Execute(edge, now, true)) { LastDecision = "exec"; motion.Publish(now); }
                }
            }
            else motion.Publish(now);
            return consumed;
        }
        public Point? Resolve(Point? rawTarget, long now, bool runAi, out bool consumed)
        {
            consumed = false;
            if (motion.map.swim || !rawTarget.HasValue) { Clear(); return rawTarget; }
            int start = CurrentRegion(), goal = TargetRegion(rawTarget.Value);
            Point pathTarget = rawTarget.Value;
            var goalRegion = graph.GetRegion(goal);
            if (motion.grinding && goalRegion != null && !goalRegion.isRopeRegion && goalRegion.minX + 40 < goalRegion.maxX - 40)
                pathTarget = goalRegion.PointAt(Clamp(pathTarget.x, goalRegion.minX + 40, goalRegion.maxX - 40));
            if (runAi && motion.navEdge != null && blockedTicks > 0 && blockedTicks >= blockedGiveUp) Clear();
            var edge = Reuse(start, goal); bool reused = edge != null;
            if (reused && runAi && start >= 0 && goal >= 0 && start != goal)
            {
                bool refresh = !motion.InAir && (!motion.Climbing || edge.type == EdgeType.Climb && edge.launchStepX != 0 && !CanClimbExit(edge));
                if (refresh)
                {
                    var best = FindNext(start, goal, pathTarget);
                    bool retain = !motion.Climbing && best != null && edge.fromRegionId == best.fromRegionId && edge.toRegionId == best.toRegionId && edge.type != EdgeType.Walk && best.type != EdgeType.Walk;
                    if (best != null && !Same(edge, best) && !retain) { edge = best; motion.navEdge = best; targetRegion = goal; }
                }
            }
            if (edge == null && runAi && start >= 0 && goal >= 0)
            { edge = FindNext(start, goal, pathTarget); if (edge != null) { motion.navEdge = edge; targetRegion = goal; } }
            if (edge == null) { LastDecision = !runAi ? "no-ai" : start < 0 || goal < 0 ? "no-region" : start == goal ? "same-region" : "no-path"; Clear(); return rawTarget; }
            if (Execute(edge, now, runAi)) { LastDecision = "exec"; blockedTicks = 0; consumed = true; return rawTarget; }
            bool blocked = reused && blockedReason != null && blockedReason.EndsWith("-pos", StringComparison.Ordinal);
            if (!blocked) blockedTicks = 0;
            else
            {
                if (blockedTicks == 0 || !Equal(blockedPosition, motion.Position))
                { blockedTicks = 0; blockedGiveUp = 6 + random.Next(5); blockedPosition = motion.Position; }
                blockedTicks++;
            }
            motion.preciseTarget = Precise(edge);
            motion.navigationTarget = Waypoint(edge);
            LastDecision = reused ? "reuse" : "new";
            return motion.navigationTarget;
        }
        Edge Reuse(int start, int goal)
        {
            var edge = motion.navEdge;
            if (edge == null || goal < 0) return null;
            int previous = targetRegion; targetRegion = goal;
            if (edge.type == EdgeType.Portal && Portal(edge.portalId)?.enabled != true) return null;
            if (motion.Climbing && edge.type == EdgeType.Climb && graph.GetRegion(edge.fromRegionId)?.isRopeRegion == false && graph.GetRegion(edge.toRegionId)?.isRopeRegion == true) return null;
            if (start == edge.toRegionId && !motion.InAir && !motion.Climbing && edge.fromRegionId != edge.toRegionId) return null;
            if (!motion.InAir && !motion.Climbing && start >= 0 && start == goal && edge.toRegionId != start && previous != goal) return null;
            if (start == edge.fromRegionId)
            {
                if (!motion.InAir && !motion.Climbing && previous >= 0 && previous != goal && edge.toRegionId != goal) return null;
                return edge;
            }
            if (motion.Climbing && (start < 0 || start != edge.toRegionId)) return edge;
            if (motion.InAir && (start < 0 || start == edge.toRegionId) && (edge.type == EdgeType.Drop || edge.type == EdgeType.Jump)) return edge;
            return motion.InAir && edge.type == EdgeType.Climb && edge.launchStepX != 0 ? edge : null;
        }
        Edge FindNext(int start, int goal, Point target)
        {
            var path = BotPathSearch.Run(graph, motion.map, motion.Position, start, goal, target,
                routeSeed: BotPathSearch.RouteSeed(motion.actor.id)).path;
            if (path.Count == 0) return null;
            var first = path[0]; if (first.type != EdgeType.Walk || !NoMovement(first)) return first;
            int at = 0, cost = 0;
            while (at < path.Count && path[at].type == EdgeType.Walk && NoMovement(path[at])) cost += path[at++].cost;
            if (at == path.Count) return null;
            var next = path[at];
            return new Edge(first.fromRegionId, next.toRegionId, next.type, next.startPoint, next.endPoint, next.launchMinX,
                next.launchMaxX, next.launchStepX, next.portalId, next.ropeX, next.ropeTopY, next.ropeBottomY, cost + next.cost);
        }
        bool Execute(Edge edge, long now, bool runAi)
        {
            if (!runAi) return false;
            Point pos = motion.Position;
            switch (edge.type)
            {
                case EdgeType.Jump:
                    if (motion.InAir || motion.Climbing) return false;
                    if (!CanJump(edge))
                    {
                        var rope = Rope(graph.GetRegion(edge.fromRegionId));
                        if (edge.startPoint.y > pos.y && rope != null && BotRopeMotion.CanGrab(pos, rope)) { motion.Attach(rope, pos.y); return true; }
                        blockedReason = "jump-pos"; return false;
                    }
                    if (Deepen(edge)) { blockedReason = "jump-delay"; return false; }
                    if (edge.launchStepX == 0 && motion.physics.SlipScale(motion.map.footholdSpeed, motion.profile) < 1 && BotGroundPhysics.JavaRound(motion.travel.Speed * 50 / 8.0) != 0)
                    { blockedReason = "jump-slide"; return false; }
                    SetExecution(edge); motion.Jump(edge.launchStepX); selectedLaunchEdge = null; selectedLaunchX = launchDelay = int.MinValue; return true;
                case EdgeType.Drop:
                    if (motion.InAir || motion.Climbing || motion.downJumpPending || edge.launchStepX != 0) return false;
                    if (!WithinWindow(edge)) { blockedReason = "drop-pos"; return false; }
                    SetExecution(edge); motion.QueueDownJump(); motion.navigationTarget = edge.endPoint; return true;
                case EdgeType.Climb:
                    if (motion.InAir || motion.downJumpPending) return false;
                    if (motion.Climbing)
                    {
                        if (!CanClimbExit(edge)) return false;
                        var to = graph.GetRegion(edge.toRegionId);
                        if (to != null && to.isRopeRegion)
                        {
                            var rope = Rope(to);
                            if (rope == null || SameRope(motion.climb.rope, rope)) return false;
                            motion.RopeJump(edge.launchStepX, true); return true;
                        }
                        if (edge.launchStepX == 0) return false;
                        var source = Rope(graph.GetRegion(edge.fromRegionId));
                        if (TopJumpReady(source, pos, edge) && pos.y != edge.startPoint.y) motion.Attach(source, edge.startPoint.y);
                        motion.RopeJump(edge.launchStepX, false); return true;
                    }
                    var targetRope = Rope(graph.GetRegion(edge.toRegionId));
                    if (!CanClimbEntry(edge, targetRope)) { blockedReason = "climb-pos"; return false; }
                    if (BotRopeMotion.CanGrab(pos, targetRope)) motion.Attach(targetRope, pos.y);
                    else if (TopAttach(edge, targetRope)) motion.Attach(targetRope, edge.endPoint.y);
                    else if (TopGrab(edge, targetRope)) motion.QueueRopeEntry(targetRope, edge.endPoint.y);
                    else if (GroundRopeJump(edge)) motion.Jump(edge.launchStepX, climbIntent: true);
                    else { blockedReason = "climb-reach"; return false; }
                    blockedReason = null; return true;
                case EdgeType.Portal:
                    if (motion.InAir || Math.Abs(pos.x - edge.startPoint.x) > 14 || Math.Abs(pos.y - edge.startPoint.y) > 60) { portalReadyTicks = -1; return false; }
                    if (portalReadyTicks < 0) portalReadyTicks = random.Next(4);
                    if (portalReadyTicks > 0) { portalReadyTicks--; return false; }
                    portalReadyTicks = -1;
                    var portal = Portal(edge.portalId);
                    if (now < portalCooldown || portal == null || !portal.enabled || UsePortal == null || !UsePortal(portal)) return false;
                    portalCooldown = now + 250; Clear(); motion.Reset(); return true;
                default: return false;
            }
        }
        void SetExecution(Edge edge) { blockedReason = null; motion.preciseTarget = false; motion.navigationTarget = edge.endPoint; }
        bool WithinWindow(Edge edge)
        {
            var from = graph.GetRegion(edge.fromRegionId);
            return edge.ContainsLaunchX(motion.Position.x) && from != null && (edge.type != EdgeType.Drop || !from.isRopeRegion)
                && Math.Abs(motion.Position.y - from.PointAt(motion.Position.x).y) <= 30;
        }
        bool CanJump(Edge edge) => WithinWindow(edge) && Math.Abs(motion.Position.x - SelectedLaunch(edge)) <= motion.physics.WalkStep(motion.profile);
        int SelectedLaunch(Edge edge)
        {
            var from = graph.GetRegion(edge.fromRegionId);
            if (from == null || from.isRopeRegion) return edge.startPoint.x;
            if (Same(selectedLaunchEdge, edge) && edge.ContainsLaunchX(selectedLaunchX)) return selectedLaunchX;
            int min = Math.Max(edge.launchMinX, from.minX), max = Math.Min(edge.launchMaxX, from.maxX);
            if (min > max) { min = edge.launchMinX; max = edge.launchMaxX; }
            int margin = Math.Min((max - min) / 2, Math.Max(1, motion.physics.WalkStep(motion.profile) * 2));
            selectedLaunchX = min + margin >= max - margin ? min + margin : random.Next(min + margin, max - margin + 1);
            selectedLaunchEdge = edge; return selectedLaunchX;
        }
        bool Deepen(Edge edge)
        {
            int dir = Math.Sign(edge.launchStepX);
            if (selectedLaunchX == int.MinValue || dir == 0) return false;
            if (launchDelay == int.MinValue) launchDelay = random.Next(3);
            if (launchDelay <= 0) return false;
            int deeper = selectedLaunchX + dir * motion.physics.WalkStep(motion.profile);
            var from = graph.GetRegion(edge.fromRegionId);
            if (!edge.ContainsLaunchX(deeper) || from == null || from.isRopeRegion || deeper < from.minX || deeper > from.maxX) { launchDelay = 0; return false; }
            launchDelay--; selectedLaunchX = deeper; return true;
        }
        bool Precise(Edge edge)
        {
            if (motion.InAir) return false;
            switch (edge.type)
            {
                case EdgeType.Jump: return !CanJump(edge);
                case EdgeType.Drop: return edge.launchStepX == 0 && !WithinWindow(edge);
                case EdgeType.Climb: return motion.Climbing ? edge.launchStepX != 0 && !CanClimbExit(edge) : !CanClimbEntry(edge, Rope(graph.GetRegion(edge.toRegionId)));
                case EdgeType.Portal: return Math.Abs(motion.Position.x - edge.startPoint.x) > 14 || Math.Abs(motion.Position.y - edge.startPoint.y) > 60 || portalReadyTicks > 0;
                default: return !NoMovement(edge);
            }
        }
        Point Waypoint(Edge edge)
        {
            Point pos = motion.Position;
            switch (edge.type)
            {
                case EdgeType.Jump:
                    if (motion.InAir) return edge.endPoint;
                    var from = graph.GetRegion(edge.fromRegionId);
                    return from == null || from.isRopeRegion ? edge.startPoint : from.PointAt(SelectedLaunch(edge));
                case EdgeType.Drop:
                    if (motion.InAir) return edge.endPoint;
                    var region = graph.GetRegion(edge.fromRegionId);
                    if (edge.launchStepX == 0)
                        return region == null || region.isRopeRegion ? edge.startPoint : region.PointAt(edge.ContainsLaunchX(pos.x) ? pos.x : SteerWindow(edge, pos.x));
                    if (edge.launchStepX > 0 ? pos.x >= edge.startPoint.x : pos.x <= edge.startPoint.x) return edge.endPoint;
                    if (region == null || region.isRopeRegion || MatchesWalkOff(edge)) return edge.endPoint;
                    return edge.startPoint;
                case EdgeType.Climb:
                    if (motion.InAir) return edge.endPoint;
                    if (motion.Climbing && edge.launchStepX != 0) return CanClimbExit(edge) ? pos : edge.startPoint;
                    if (motion.Climbing) return new Point(motion.climb.rope.x, edge.endPoint.y);
                    return edge.launchStepX != 0 && edge.launchMaxX > edge.launchMinX ? new Point(SteerWindow(edge, pos.x), edge.startPoint.y) : edge.startPoint;
                case EdgeType.Portal: return edge.startPoint;
                default: return edge.endPoint;
            }
        }
        bool MatchesWalkOff(Edge edge)
        {
            var point = motion.Position; var foothold = motion.ground.Ground(point.x, point.y);
            var travel = motion.travel; if (foothold == null) return false;
            for (int i = 0; i < 256; i++)
            {
                var step = groundMotion.Step(point, foothold, Math.Sign(edge.launchStepX), travel, motion.profile);
                if (step.lostGround)
                {
                    var landing = step.stepX == 0 ? null : trajectory.Fall(step.point, step.stepX);
                    return landing != null && graph.regionByFoothold.TryGetValue(landing.foothold.id, out int id) && id == edge.toRegionId
                        && Math.Abs(landing.point.x - edge.endPoint.x) <= Math.Max(6, Math.Abs(edge.launchStepX) + 2) && Math.Abs(landing.point.y - edge.endPoint.y) <= 60;
                }
                point = step.point; foothold = step.foothold; travel = step.travel;
            }
            return false;
        }
        bool CanClimbEntry(Edge edge, MapRope rope) => rope != null && (BotRopeMotion.CanGrab(motion.Position, rope) || TopAttach(edge, rope) || TopGrab(edge, rope) || GroundRopeJump(edge));
        bool GroundRopeJump(Edge edge) => edge.ContainsLaunchX(motion.Position.x) && Math.Abs(motion.Position.y - edge.startPoint.y) <= 60;
        bool TopAttach(Edge edge, MapRope rope) => Math.Abs(motion.Position.x - rope.x) <= 8 && edge.endPoint.y == BotRopeMotion.FirstClimbableY(rope)
            && motion.Position.y < rope.Top && rope.Top - motion.Position.y <= 16;
        bool TopGrab(Edge edge, MapRope rope) => edge.startPoint.y <= rope.Top + 30 && Math.Abs(motion.Position.x - rope.x) <= 8;
        bool CanClimbExit(Edge edge)
        {
            var pos = motion.Position; var rope = Rope(graph.GetRegion(edge.fromRegionId));
            if (edge.launchStepX != 0 && pos.y != edge.startPoint.y && !TopJumpReady(rope, pos, edge)) return false;
            if (graph.GetRegion(edge.toRegionId)?.isRopeRegion == true || edge.launchStepX != 0) return Math.Abs(pos.y - edge.startPoint.y) <= 60;
            return rope != null && edge.startPoint.y == rope.Top && Math.Abs(edge.endPoint.y - rope.Top) <= 60 && pos.y <= rope.Top + 60;
        }
        static bool TopJumpReady(MapRope rope, Point pos, Edge edge) => rope != null && edge.launchStepX != 0 && edge.startPoint.x == rope.x
            && edge.startPoint.y == BotRopeMotion.FirstClimbableY(rope) && pos.x == rope.x && pos.y >= BotRopeMotion.FirstClimbableY(rope) && pos.y <= BotRopeMotion.FirstClimbableY(rope) + 7;
        MapRope Rope(Region region)
        {
            if (region == null || !region.isRopeRegion) return null;
            foreach (var rope in motion.map.ropes) if (rope.x == region.minX && rope.Top == region.minY && rope.Bottom == region.maxY && rope.ladder == region.isLadder) return rope;
            return null;
        }
        MapPortal Portal(int id) => motion.map.portals.Find(p => p.id == id);
        static bool SameRope(MapRope a, MapRope b) => a != null && b != null && a.x == b.x && a.Top == b.Top && a.Bottom == b.Bottom && a.ladder == b.ladder;
        static bool Equal(Point a, Point b) => a.x == b.x && a.y == b.y;
        static bool NoMovement(Edge edge) => Math.Abs(edge.endPoint.x - edge.startPoint.x) <= 4 && Math.Abs(edge.endPoint.y - edge.startPoint.y) <= 4;
        static int SteerWindow(Edge edge, int x) { int inset = Math.Min((edge.launchMaxX - edge.launchMinX) / 2, 4); return Clamp(x, edge.launchMinX + inset, edge.launchMaxX - inset); }
        static int Clamp(int x, int min, int max) => Math.Max(min, Math.Min(max, x));
        static bool Same(Edge a, Edge b) => ReferenceEquals(a, b) || a != null && b != null && a.fromRegionId == b.fromRegionId && a.toRegionId == b.toRegionId && a.type == b.type
            && a.launchMinX == b.launchMinX && a.launchMaxX == b.launchMaxX && a.launchStepX == b.launchStepX && a.portalId == b.portalId
            && a.ropeX == b.ropeX && a.ropeTopY == b.ropeTopY && a.ropeBottomY == b.ropeBottomY && Equal(a.startPoint, b.startPoint) && Equal(a.endPoint, b.endPoint);
    }
}
