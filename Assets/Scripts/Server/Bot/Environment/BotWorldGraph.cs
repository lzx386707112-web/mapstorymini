using System;
using System.Collections.Generic;

namespace Server.Bot.Environment
{
    public sealed class BotWorldGraph
    {
        readonly Dictionary<int, int[]> edges = new Dictionary<int, int[]>();
        static readonly int[,] taxis = { {104000000,1002007}, {100000000,1012000}, {102000000,1022001},
            {101000000,1032000}, {103000000,1052016}, {120000000,1092014} };
        public BotWorldGraph(string mapIndex)
        {
            var root = MiniJsonDb.Parse(mapIndex) as Dictionary<string, object>;
            var neighbors = root?["neighbors"] as Dictionary<string, object> ?? throw new ArgumentException("Original world graph is missing");
            foreach (var pair in neighbors)
            {
                var ids = (List<object>)pair.Value; var array = new int[ids.Count];
                for (int i = 0; i < ids.Count; i++) array[i] = Convert.ToInt32(ids[i]);
                edges[int.Parse(pair.Key)] = array;
            }
        }
        public static BotWorldGraph Load() => new BotWorldGraph(PlatformRes.LoadText("Bot/Maps"));
        public int MapCount => edges.Count;
        public IReadOnlyList<int> PortalNeighbors(int id) => edges.TryGetValue(id, out var neighbors) ? neighbors : Array.Empty<int>();
        public static int TaxiNpc(int from, int to)
        {
            int npc = 0; bool destination = false;
            for (int i = 0; i < taxis.GetLength(0); i++)
            { if (taxis[i, 0] == from) npc = taxis[i, 1]; if (taxis[i, 0] == to) destination = true; }
            return from != to && destination ? npc : 0;
        }
        public IEnumerable<int> Neighbors(int id)
        {
            foreach (int destination in PortalNeighbors(id)) yield return destination;
            for (int i = 0; i < taxis.GetLength(0); i++) if (taxis[i, 0] == id)
                for (int j = 0; j < taxis.GetLength(0); j++) if (i != j) yield return taxis[j, 0];
            // 原BotScriptedWarp.destinations本身为空，不添加虚构的脚本传送边。
        }
        public List<int> Route(int from, int to, int maxHops)
        {
            if (from == to) return new List<int>();
            if (maxHops <= 0) return null;
            var cameFrom = new Dictionary<int, int> { [from] = from };
            var frontier = new Queue<int>(); frontier.Enqueue(from);
            int depth = 0;
            while (frontier.Count > 0 && depth < maxHops)
            {
                depth++;
                for (int level = frontier.Count; level > 0; level--)
                {
                    int current = frontier.Dequeue();
                    foreach (int next in Neighbors(current))
                    {
                        if (cameFrom.ContainsKey(next)) continue;
                        cameFrom[next] = current;
                        if (next == to)
                        {
                            var route = new List<int>();
                            for (int at = to; at != from; at = cameFrom[at]) route.Add(at);
                            route.Reverse(); return route;
                        }
                        frontier.Enqueue(next);
                    }
                }
            }
            return null;
        }
    }
}
