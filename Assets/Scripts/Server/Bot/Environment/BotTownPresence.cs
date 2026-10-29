using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Region = Server.Bot.Environment.BotNavigationGraph.Region;

namespace Server.Bot.Environment
{
    public sealed class BotTownOverrides
    {
        public readonly struct Zone
        {
            public readonly int x1, y1, x2, y2;
            public readonly double multiplier;
            public Zone(int x1, int y1, int x2, int y2, double multiplier)
            { this.x1 = x1; this.y1 = y1; this.x2 = x2; this.y2 = y2; this.multiplier = multiplier; }
            public bool Contains(int x, int y) => x >= Math.Min(x1, x2) && x <= Math.Max(x1, x2) && y >= Math.Min(y1, y2) && y <= Math.Max(y1, y2);
        }
        public readonly List<Zone> ban = new List<Zone>(), boost = new List<Zone>();
        public readonly List<Point> pins = new List<Point>();
        public bool IsBanned(int x, int y) { foreach (var z in ban) if (z.Contains(x, y)) return true; return false; }
        public double Boost(int x, int y) { double m = 1; foreach (var z in boost) if (z.Contains(x, y)) m *= z.multiplier; return m; }
    }

    // TownPresenceSampler：NPC与portal高斯吸引、宽度/高度衰减、9%均匀尾部。
    public sealed class BotTownPresenceSampler
    {
        public readonly struct Anchor
        {
            public readonly int x, y;
            public readonly double strength;
            public Anchor(int x, int y, double strength) { this.x = x; this.y = y; this.strength = strength; }
        }
        readonly Random random;
        public BotTownPresenceSampler(Random random = null) { this.random = random ?? new Random(); }
        public List<Point> Sample(MapRuntime map, Point anchor, int count, IEnumerable<Point> npcs, BotTownOverrides overrides = null)
        {
            var result = new List<Point>(); if (map == null || count <= 0) return result;
            var ov = overrides ?? new BotTownOverrides(); var ground = MapGeometryCache.Get(map).ground;
            foreach (var pin in ov.pins)
            {
                if (result.Count >= count) return result;
                result.Add(ground.GroundPoint(pin.x, pin.y) ?? pin);
            }
            var graph = BotMovementService.Graphs.Get(map);
            var all = graph.regions.FindAll(r => !r.isRopeRegion && !r.isLadder);
            var reachable = BotMovementService.Reachable(graph, ground, anchor);
            var ledges = all.FindAll(r => reachable.Contains(r.id));
            if (ledges.Count == 0) ledges = all;
            if (ledges.Count == 0) return result;
            var anchors = new List<Anchor>();
            if (npcs != null) foreach (var npc in npcs) anchors.Add(new Anchor(npc.x, npc.y, 1));
            foreach (var portal in map.portals) anchors.Add(new Anchor(portal.x, portal.y, .45));
            int maxSpan = 1; foreach (var l in ledges) maxSpan = Math.Max(maxSpan, l.Width);
            int band = int.MinValue; foreach (var l in ledges) if (l.Width >= maxSpan / 2) band = Math.Max(band, l.Center.y);
            if (band == int.MinValue) band = ledges[0].Center.y;
            var weights = new double[ledges.Count]; double total = 0;
            for (int i = 0; i < ledges.Count; i++)
            {
                var l = ledges[i]; var c = l.Center; double pull = 0;
                foreach (var a in anchors) pull += Pull(a, Math.Max(l.minX, Math.Min(l.maxX, a.x)), c.y);
                weights[i] = ov.IsBanned(c.x, c.y) ? 0 : Math.Max(1, l.Width) * Math.Exp(-Math.Max(0, band - c.y) / 320.0) * (.15 + pull) * ov.Boost(c.x, c.y);
                total += weights[i];
            }
            var occupied = new Dictionary<int, List<int>>();
            while (result.Count < count)
            {
                Region ledge;
                if (random.NextDouble() < .09 || total <= 0) ledge = ledges[random.Next(ledges.Count)];
                else
                {
                    double roll = random.NextDouble() * total, sum = 0; ledge = ledges[ledges.Count - 1];
                    for (int i = 0; i < ledges.Count; i++) { sum += weights[i]; if (roll < sum) { ledge = ledges[i]; break; } }
                }
                if (!occupied.TryGetValue(ledge.id, out var taken)) occupied[ledge.id] = taken = new List<int>();
                int best = ledge.minX; double bestScore = double.MinValue;
                if (ledge.maxX > ledge.minX) for (int i = 0; i < 7; i++)
                {
                    int x = random.Next(ledge.minX, ledge.maxX + 1); if (ov.IsBanned(x, ledge.Center.y)) continue;
                    double pull = 0, penalty = 0;
                    foreach (var a in anchors) pull += Pull(a, x, ledge.Center.y);
                    foreach (int t in taken) if (Math.Abs(t - x) < 30) penalty += 30 - Math.Abs(t - x);
                    double score = pull * ov.Boost(x, ledge.Center.y) - penalty;
                    if (score > bestScore) { bestScore = score; best = x; }
                }
                taken.Add(best); result.Add(ledge.PointAt(best));
            }
            return result;
        }
        static double Pull(Anchor a, int x, int y)
        {
            double dx = (double)a.x - x, dy = (double)a.y - y;
            return a.strength * Math.Exp(-dx * dx / 135200.0) * Math.Exp(-dy * dy / 33800.0);
        }
    }
}
