using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Edge = Server.Bot.Environment.BotNavigationGraph.Edge;

namespace Server.Bot.Environment
{
    // GCMovement/GCMovementDriver主线程调度适配。移动与FSM调度分离。
    public static class BotMovementService
    {
        sealed class Entry
        {
            public BotMovementRuntime motion;
            public BotNavigationRuntime navigation;
            public BotTasks.Handle task;
            public Point? target;
            public Action arrival;
            public long progressAt, profileAt, portalDropAt;
            public int bestDistance = int.MaxValue, aiAccumulator, stuckMs, cooldown, airStuckTicks;
            public Point? stuckAt, airStuckAt;
            public bool coarse;
            public List<Edge> coarseEdges;
            public long coarseStarted, coarseDuration;
        }
        static readonly Random random = new Random();
        static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        public static readonly BotNavigationGraphs Graphs = new BotNavigationGraphs();
        public static Func<int, bool> IsObserved;
        public static Action<BotCharacter, byte[]> Broadcast;
        public static Action<BotCharacter, float> ApplyFallDamage;
        public static Func<BotCharacter, MapPortal, bool> EnterPortal;
        public static Func<BotCharacter, MapRuntime, BotMovementProfile> ResolveProfile;
        public static bool Observed(int mapId) => IsObserved != null && IsObserved(mapId);
        static BotMovementProfile Profile(BotCharacter actor, MapRuntime map) => ResolveProfile != null ? ResolveProfile(actor, map) :
            BotMovementProfile.FromCharacter(actor, (map.fieldLimit & 2) != 0, snowShoes: HasSnowShoes(actor));
        internal static bool HasSnowShoes(BotCharacter actor)
        {
            return actor.equipment.TryGetValue(-7, out var entry) && entry != null &&
                BotItemCatalog.Int(BotItemCatalog.Shared.Info(entry.itemId), "fs") >= 1;
        }
        public static BotMovementRuntime Get(BotCharacter actor) => actor != null && entries.TryGetValue(actor.id, out var e) ? e.motion : null;
        public static bool IsMoving(BotCharacter actor) => actor != null && entries.TryGetValue(actor.id, out var e) &&
            (e.target.HasValue || e.motion.InAir || e.motion.Climbing || e.motion.navEdge != null || e.portalDropAt > 0);
        public static BotMovementRuntime Enable(BotCharacter actor)
        {
            if (actor == null) return null;
            if (entries.TryGetValue(actor.id, out var existing)) return existing.motion;
            var map = MapService.Get(actor.mapId); if (map == null) return null;
            var e = Create(actor, map); entries[actor.id] = e;
            var ground = e.motion.ground.GroundPoint((int)actor.x, (int)actor.y - 1);
            if (ground.HasValue) e.motion.Teleport(ground.Value);
            Schedule(e, 0); return e.motion;
        }
        static Entry Create(BotCharacter actor, MapRuntime map)
        {
            var motion = new BotMovementRuntime(actor, map, Profile(actor, map));
            motion.IsObserved = Observed;
            motion.Broadcast = (bot, bytes) => Broadcast?.Invoke(bot, bytes);
            motion.ApplyFallDamage = (bot, distance) => ApplyFallDamage?.Invoke(bot, distance);
            var nav = new BotNavigationRuntime(motion, Graphs.Get(map, motion.profile));
            nav.UsePortal = portal => EnterPortal != null && EnterPortal(actor, portal);
            return new Entry { motion = motion, navigation = nav, profileAt = BotScheduler.Now() };
        }
        static void Schedule(Entry e, long delay)
        {
            e.task = BotTasks.Schedule(() =>
            {
                if (!entries.TryGetValue(e.motion.actor.id, out var current) || !ReferenceEquals(current, e)) return;
                try { Tick(e, BotScheduler.Now()); }
                finally
                {
                    if (entries.TryGetValue(e.motion.actor.id, out current) && ReferenceEquals(current, e))
                        Schedule(e, Observed(e.motion.actor.mapId) ? 50 : IsMoving(e.motion.actor) ? 500 : 2500);
                }
            }, delay);
        }
        public static void SetGrinding(BotCharacter actor, bool grinding)
        { var motion=Enable(actor); if (motion!=null) motion.grinding=grinding; }
        public static void Teleport(BotCharacter actor, Point point)
        { var motion=Enable(actor); if (motion==null) return; Stop(actor); motion.Teleport(point); }
        public static void Move(BotCharacter actor, int x, int y, Action arrival = null)
        {
            if (Enable(actor) == null) return;
            var e = entries[actor.id]; e.target = new Point(x, y); e.arrival = arrival;
            e.bestDistance = int.MaxValue; e.progressAt = BotScheduler.Now();
            e.coarseEdges = null;
        }
        public static void Stop(BotCharacter actor)
        {
            if (actor == null || !entries.TryGetValue(actor.id, out var e)) return;
            e.target = null; e.arrival = null; e.coarseEdges = null; e.navigation.Clear();
        }
        public static void Disable(BotCharacter actor)
        {
            if (actor == null || !entries.TryGetValue(actor.id, out var e)) return;
            e.task?.Cancel(); entries.Remove(actor.id);
        }
        public static void Shutdown()
        { foreach (var e in entries.Values) e.task?.Cancel(); entries.Clear(); }
        static void Tick(Entry e, long now)
        {
            var actor = e.motion.actor;
            if (!ReferenceEquals(BotRegistry.Get(actor.id), actor)) { Disable(actor); return; }
            if (actor.mapId != e.motion.map.mapId)
            {
                var map = MapService.Get(actor.mapId); if (map == null) { Disable(actor); return; }
                var changed = Create(actor, map); changed.target = e.target; changed.arrival = e.arrival;
                var ground = changed.motion.ground.GroundPoint((int)actor.x, (int)actor.y - 1);
                if (ground.HasValue && Observed(actor.mapId))
                {
                    changed.motion.Teleport(new Point((int)actor.x, Math.Min((int)actor.y, ground.Value.y - 60)));
                    changed.portalDropAt = now + 1500 + random.Next(601);
                }
                else changed.motion.Teleport(ground ?? changed.motion.Position);
                entries[actor.id] = changed; Schedule(changed, 0); return;
            }
            if (now - e.profileAt >= 20000)
            {
                e.profileAt = now;
                if (!Profile(actor, e.motion.map).Equals(e.motion.profile))
                {
                    var refreshed = Create(actor, e.motion.map); refreshed.target = e.target; refreshed.arrival = e.arrival;
                    refreshed.progressAt = e.progressAt; entries[actor.id] = refreshed; Schedule(refreshed, 0); return;
                }
            }
            if (actor.chair > 0) return;
            bool observed = Observed(actor.mapId);
            if (!observed && !IsMoving(actor)) return;
            if (e.coarse && observed)
            {
                e.coarse = false; e.coarseEdges = null;
                var ground = e.motion.ground.GroundPoint((int)actor.x, (int)actor.y - 1);
                e.motion.Teleport(ground ?? e.motion.Position); e.navigation.Clear();
            }
            if (e.motion.resting)
            {
                if (!e.motion.Climbing) e.motion.Idle();
                e.motion.Publish(now); return;
            }
            if (e.portalDropAt > 0)
            {
                if (now < e.portalDropAt)
                { e.motion.Idle(); e.motion.Fall(0); e.motion.Publish(now); return; }
                e.portalDropAt = 0; e.motion.Fall(0); e.motion.Publish(now); return;
            }
            if (e.target.HasValue && !e.motion.InAir && !e.motion.Climbing)
            {
                int distance = Math.Abs((int)actor.x - e.target.Value.x) + Math.Abs((int)actor.y - e.target.Value.y);
                if (e.progressAt == 0) e.progressAt = now;
                if (distance < e.bestDistance - 16) { e.bestDistance = distance; e.progressAt = now; }
                else if (now - e.progressAt > 8000) { Stop(actor); e.motion.Idle(); e.motion.Publish(now); return; }
            }
            if (!e.target.HasValue && !e.motion.InAir && !e.motion.Climbing && e.motion.navEdge == null)
            { e.motion.Idle(); e.motion.Publish(now); return; }
            if (!observed && e.target.HasValue && !e.motion.InAir && !e.motion.Climbing) { AdvanceCoarse(e, now); return; }
            e.aiAccumulator += 50; bool ai = e.aiAccumulator >= 100; if (ai) e.aiAccumulator -= 100;
            e.navigation.Tick(e.target ?? e.motion.Position, now, ai, true);
            DetectStuck(e, now);
            if (e.target.HasValue && Math.Abs(actor.x - e.target.Value.x) <= 8 && Math.Abs(actor.y - e.target.Value.y) <= 8) Arrive(e);
        }
        static void Arrive(Entry e)
        {
            e.target = null; e.bestDistance = int.MaxValue; var callback = e.arrival; e.arrival = null; callback?.Invoke();
        }
        static void AdvanceCoarse(Entry e, long now)
        {
            Point target = e.target.Value, pos = e.motion.Position;
            if (e.coarseEdges == null && Math.Abs(pos.x - target.x) <= 12 && Math.Abs(pos.y - target.y) <= 12)
            { e.motion.Teleport(target); e.navigation.Clear(); Arrive(e); return; }
            if (e.coarseEdges == null)
            {
                var graph = Graphs.Get(e.motion.map, e.motion.profile);
                int start = graph.FindRegionId(e.motion.ground, pos), goal = graph.FindRegionId(e.motion.ground, target);
                var path = start < 0 || goal < 0 ? null : BotPathSearch.Run(graph, e.motion.map, pos, start, goal, target).path;
                if (path == null || path.Count == 0) { e.motion.Teleport(target); e.navigation.Clear(); Arrive(e); return; }
                e.coarseEdges = path; e.coarseDuration = 0; foreach (var edge in path) e.coarseDuration += Math.Max(0, edge.cost);
                e.coarseStarted = now;
            }
            e.coarse = true; long elapsed = Math.Max(0, now - e.coarseStarted);
            if (elapsed >= e.coarseDuration)
            { e.motion.actor.x = target.x; e.motion.actor.y = target.y; e.coarseEdges = null; e.navigation.Clear(); Arrive(e); return; }
            long startMs = 0;
            foreach (var edge in e.coarseEdges)
            {
                long duration = Math.Max(0, edge.cost);
                if (elapsed < startMs + duration)
                {
                    // MovementPlan.positionAt原Java为long整数除法，不自行改为浮点插值。
                    double t = Math.Min(1.0, (elapsed - startMs) / Math.Max(1, duration));
                    e.motion.actor.x = BotGroundPhysics.JavaRound(edge.startPoint.x + (edge.endPoint.x - edge.startPoint.x) * t);
                    e.motion.actor.y = BotGroundPhysics.JavaRound(edge.startPoint.y + (edge.endPoint.y - edge.startPoint.y) * t);
                    break;
                }
                startMs += duration;
            }
        }
        static void DetectStuck(Entry e, long now)
        {
            var m = e.motion; var pos = m.Position; e.cooldown = BotMovementRuntime.TickDown(e.cooldown);
            if (m.InAir && !m.Climbing)
            {
                if (!e.airStuckAt.HasValue || pos.x != e.airStuckAt.Value.x || pos.y != e.airStuckAt.Value.y)
                { e.airStuckTicks = 0; e.airStuckAt = pos; }
                else if (++e.airStuckTicks >= 30)
                { Recover(e, e.target ?? m.navigationTarget ?? pos); e.airStuckTicks = 0; e.airStuckAt = null; }
            }
            else { e.airStuckTicks = 0; e.airStuckAt = null; }
            var map = m.map;
            if (map.right > map.left && map.bottom > map.top && (pos.y > map.bottom + 400 || pos.x < map.left - 400 || pos.x > map.right + 400))
            {
                Recover(e, e.target ?? m.navigationTarget ?? new Point(Math.Max(map.left, Math.Min(map.right, pos.x)), map.top));
                e.target = null;
            }
            if (m.InAir || m.Climbing || m.navEdge == null && !e.target.HasValue) { e.stuckMs = 0; e.stuckAt = null; return; }
            if (!e.stuckAt.HasValue) { e.stuckAt = pos; return; }
            if (Math.Abs(pos.x - e.stuckAt.Value.x) > 8 || Math.Abs(pos.y - e.stuckAt.Value.y) > 8) { e.stuckMs = 0; e.stuckAt = pos; }
            else e.stuckMs += 50;
            if (e.stuckMs >= 500 && e.cooldown == 0)
            { e.stuckMs = 0; e.stuckAt = null; m.Jump(random.Next(2) == 0 ? -1 : 1); e.navigation.Clear(); e.cooldown = 4950; m.Publish(now); }
        }
        static void Recover(Entry e, Point target)
        { e.motion.Teleport(e.motion.ground.GroundPoint(target.x, target.y - 1) ?? target); e.navigation.Clear(); }
        public static HashSet<int> Reachable(BotNavigationGraph graph, BotGroundIndex ground, Point from)
        {
            var seen = new HashSet<int>(); int start = graph.FindRegionId(ground, from); if (start < 0) return seen;
            var queue = new Queue<int>(); seen.Add(start); queue.Enqueue(start);
            while (queue.Count > 0) foreach (var edge in graph.Outgoing(queue.Dequeue())) if (seen.Add(edge.toRegionId)) queue.Enqueue(edge.toRegionId);
            return seen;
        }
    }
}
