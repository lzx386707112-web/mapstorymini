using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Environment
{
    // 主线程图缓存。容量兜底逐条淘汰，保留按地图的容错查询所需的其他缓存图。
    public sealed class BotNavigationGraphs
    {
        readonly Dictionary<(int, int, int, bool), BotNavigationGraph> graphs = new Dictionary<(int, int, int, bool), BotNavigationGraph>();
        readonly LinkedList<(int, int, int, bool)> lru = new LinkedList<(int, int, int, bool)>();
        readonly Dictionary<(int, int, int, bool), LinkedListNode<(int, int, int, bool)>> nodes = new Dictionary<(int, int, int, bool), LinkedListNode<(int, int, int, bool)>>();
        public int Count => graphs.Count;
        static BotMovementProfile Canonical(MapRuntime map, BotMovementProfile profile)
        {
            profile = profile ?? BotMovementProfile.Base;
            return profile.snowShoes && !(map.footholdSpeed > 0 && map.footholdSpeed < 1)
                ? new BotMovementProfile(profile.speed, profile.jump) : profile;
        }
        static (int, int, int, bool) Key(MapRuntime map, BotMovementProfile profile)
        { var p = Canonical(map, profile); return (map.mapId, p.speed, p.jump, p.snowShoes); }
        void Touch((int, int, int, bool) key)
        {
            if (nodes.TryGetValue(key, out var node)) { if (lru.Last == node) return; lru.Remove(node); lru.AddLast(node); }
            else nodes[key] = lru.AddLast(key);
        }
        void Cache((int, int, int, bool) key, BotNavigationGraph graph)
        {
            graphs[key] = graph; Touch(key);
            while (graphs.Count > 512)
            {
                var oldest = lru.First; if (oldest == null) return;
                lru.RemoveFirst(); nodes.Remove(oldest.Value); graphs.Remove(oldest.Value);
            }
        }
        public BotNavigationGraph Get(MapRuntime map, BotMovementProfile profile = null)
        {
            if (map == null) return null;
            var key = Key(map, profile);
            if (graphs.TryGetValue(key, out var cached)) { Touch(key); return cached; }
            var graph = new BotNavigationGraphBuilder(map, Canonical(map, profile)).Build(); Cache(key, graph); return graph;
        }
        public BotNavigationGraph Rebuild(MapRuntime map, BotMovementProfile profile = null)
        {
            var graph = new BotNavigationGraphBuilder(map, profile).Build();
            var p = profile ?? BotMovementProfile.Base; Cache((map.mapId, p.speed, p.jump, p.snowShoes), graph); return graph;
        }
        public BotNavigationGraph Peek(MapRuntime map, BotMovementProfile profile)
        {
            if (map == null) return null;
            var key = Key(map, profile); if (!graphs.TryGetValue(key, out var graph)) return null;
            Touch(key); return graph;
        }
        public BotNavigationGraph PeekAny(MapRuntime map)
        {
            if (map == null) return null;
            foreach (var pair in graphs) if (pair.Key.Item1 == map.mapId) { Touch(pair.Key); return pair.Value; }
            return null;
        }
        public BotNavigationGraph PeekClosest(MapRuntime map, BotMovementProfile profile = null)
        {
            if (map == null) return null;
            var requested = Canonical(map, profile); BotNavigationGraph best = null; int distance = int.MaxValue;
            (int, int, int, bool) bestKey = default;
            foreach (var pair in graphs) if (pair.Key.Item1 == map.mapId)
            {
                int d = Math.Abs(pair.Key.Item2 - requested.speed) + Math.Abs(pair.Key.Item3 - requested.jump) + (pair.Key.Item4 != requested.snowShoes ? 1000 : 0);
                if (best == null || d < distance) { best = pair.Value; bestKey = pair.Key; distance = d; }
            }
            if (best != null) Touch(bestKey); return best;
        }
        public BotNavigationGraph PeekBest(MapRuntime map, BotMovementProfile profile = null) => Peek(map, profile) ?? PeekClosest(map, profile);
    }
}
