using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Edge = Server.Bot.Environment.BotNavigationGraph.Edge;

namespace Server.Bot.Environment
{
    // BotNavigationManager.runSearch：搜索状态包含落点和portal来源，不能仅按region去重。
    public static class BotPathSearch
    {
        readonly struct State : IEquatable<State>
        {
            public readonly int region, x, y;
            public readonly bool viaPortal;
            public Point Point => new Point(x, y);
            public State(int region, Point point, bool viaPortal) { this.region = region; x = point.x; y = point.y; this.viaPortal = viaPortal; }
            public bool Equals(State other) => region == other.region && x == other.x && y == other.y && viaPortal == other.viaPortal;
            public override bool Equals(object obj) => obj is State state && Equals(state);
            public override int GetHashCode() => unchecked(((region * 31 + x) * 31 + y) * 31 + (viaPortal ? 1 : 0));
        }
        readonly struct Node
        {
            public readonly State state;
            public readonly int cost, score;
            public Node(State state, int cost, int score) { this.state = state; this.cost = cost; this.score = score; }
        }
        // Java PriorityQueue的二叉堆比较只看score，保留相等score的插入/移除行为。
        sealed class Open
        {
            readonly List<Node> nodes = new List<Node>();
            public int Count => nodes.Count;
            public void Add(Node node)
            {
                int at = nodes.Count; nodes.Add(node);
                while (at > 0) { int p = (at - 1) >> 1; if (node.score >= nodes[p].score) break; nodes[at] = nodes[p]; at = p; }
                nodes[at] = node;
            }
            public Node Pop()
            {
                var result = nodes[0]; var last = nodes[nodes.Count - 1]; nodes.RemoveAt(nodes.Count - 1);
                if (nodes.Count == 0) return result;
                int at = 0, half = nodes.Count >> 1;
                while (at < half)
                {
                    int child = at * 2 + 1, right = child + 1;
                    if (right < nodes.Count && nodes[child].score > nodes[right].score) child = right;
                    if (last.score <= nodes[child].score) break;
                    nodes[at] = nodes[child]; at = child;
                }
                nodes[at] = last; return result;
            }
        }
        public sealed class Outcome
        {
            public readonly List<Edge> path;
            public readonly int cost, expanded;
            public readonly bool usesPortal;
            public Outcome(List<Edge> path, int cost, int expanded)
            { this.path = path; this.cost = cost; this.expanded = expanded; foreach (var edge in path) if (edge.type == BotNavigationGraph.EdgeType.Portal) usesPortal = true; }
        }
        public static Outcome Run(BotNavigationGraph graph, MapRuntime map, Point start, int startRegion, int targetRegion,
            Point target, bool zeroHeuristic = true, long routeSeed = 0)
        {
            bool randomized = routeSeed != 0;
            double epsilon = randomized ? 1 + HashFraction(routeSeed, 15290385) * .15 : 0;
            var open = new Open(); var scores = new Dictionary<State, int>(); var previous = new Dictionary<State, State>();
            var via = new Dictionary<State, Edge>(); var initial = new State(startRegion, start, false);
            State? best = null; int bestCost = int.MaxValue, expanded = 0, relaxations = 0;
            scores[initial] = 0; open.Add(new Node(initial, 0, Heuristic(graph, start, target, zeroHeuristic, randomized, epsilon)));
            while (open.Count > 0 && relaxations < 200000)
            {
                var current = open.Pop();
                if (current.cost != scores[current.state]) continue;
                if (best.HasValue && current.score >= bestCost) break;
                expanded++;
                if (current.state.region == targetRegion)
                {
                    int goalCost = unchecked(current.cost + TravelCost(graph, current.state.region, current.state.Point, target));
                    if (goalCost < bestCost) { bestCost = goalCost; best = current.state; }
                }
                foreach (var edge in graph.Outgoing(current.state.region))
                {
                    bool portal = edge.type == BotNavigationGraph.EdgeType.Portal;
                    if (portal && !PortalEnabled(map, edge.portalId)) continue;
                    bool throughExit = current.state.viaPortal && SamePoint(current.state.Point, edge.startPoint);
                    int edgeCost = portal && throughExit ? 250 : edge.cost;
                    bool straightDrop = edge.type == BotNavigationGraph.EdgeType.Drop && edge.launchStepX == 0;
                    var approach = straightDrop ? edge.NearestLaunch(current.state.x) : edge.startPoint;
                    var landing = straightDrop ? new Point(approach.x, edge.endPoint.y) : edge.endPoint;
                    int stepCost = unchecked(TravelCost(graph, current.state.region, current.state.Point, approach) + edgeCost);
                    if (randomized) stepCost = unchecked(stepCost + BotGroundPhysics.JavaRound(stepCost * .55 * HashFraction(routeSeed, EdgeKey(edge))));
                    int tentative = unchecked(current.cost + stepCost);
                    var next = new State(edge.toRegionId, landing, portal);
                    if (scores.TryGetValue(next, out int prior) && tentative >= prior) continue;
                    relaxations++; scores[next] = tentative; previous[next] = current.state; via[next] = edge;
                    int f = unchecked(tentative + Heuristic(graph, edge.endPoint, target, zeroHeuristic, randomized, epsilon));
                    open.Add(new Node(next, tentative, f));
                    if (relaxations >= 200000) break;
                }
            }
            var path = new List<Edge>();
            if (best.HasValue && via.ContainsKey(best.Value))
            {
                var at = best.Value;
                while (!at.Equals(initial))
                {
                    if (!via.TryGetValue(at, out var edge) || !previous.TryGetValue(at, out var prev)) { path.Clear(); break; }
                    path.Add(edge); at = prev;
                }
                path.Reverse();
            }
            return new Outcome(path, bestCost, expanded);
        }
        static bool PortalEnabled(MapRuntime map, int id)
        { foreach (var p in map.portals) if (p.id == id) return p.enabled; return false; }
        static bool SamePoint(Point a, Point b) => a.x == b.x && a.y == b.y;
        static int TravelCost(BotNavigationGraph graph, int region, Point from, Point to)
        {
            bool rope = graph.GetRegion(region)?.isRopeRegion ?? false;
            return Math.Max(0, BotGroundPhysics.JavaRound(Math.Abs(rope ? to.y - from.y : to.x - from.x) * 1000.0 /
                Math.Max(1, rope ? 100 : graph.profile.WalkVelocity(125))));
        }
        static int Heuristic(BotNavigationGraph graph, Point from, Point target, bool zero, bool random, double epsilon)
        {
            int h = Math.Max(0, BotGroundPhysics.JavaRound(Math.Abs(target.x - from.x) * 1000.0 / Math.Max(1, graph.profile.WalkVelocity(125))));
            return random ? BotGroundPhysics.JavaRound(epsilon * h) : zero ? 0 : h;
        }
        public static long RouteSeed(int botId) => Mix(botId) | 1;
        static long EdgeKey(Edge edge) => unchecked((((((long)edge.toRegionId * 31 + edge.startPoint.x) * 31 + edge.startPoint.y) * 31) + (int)edge.type));
        static double HashFraction(long seed, long key) => ((ulong)Mix(unchecked(seed ^ (key * -7046029254386353131L))) >> 11) * 1.1102230246251565E-16;
        static long Mix(long value)
        {
            long a = unchecked((value ^ (long)((ulong)value >> 30)) * -4658895280553007687L);
            long b = unchecked((a ^ (long)((ulong)a >> 27)) * -7723592293110705685L);
            return b ^ (long)((ulong)b >> 31);
        }
    }
}
