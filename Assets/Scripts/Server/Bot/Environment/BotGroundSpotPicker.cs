using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Region = Server.Bot.Environment.BotNavigationGraph.Region;

namespace Server.Bot.Environment
{
    // BotGrindSystem.BotSpotPicker：可达ledge按宽度加权，8次30px间距重试。
    public sealed class BotGroundSpotPicker
    {
        readonly Random random;
        public BotGroundSpotPicker(Random random = null) { this.random = random ?? new Random(); }
        sealed class Candidate
        {
            public Region region;
            public int lo, hi;
            public int Weight => Math.Max(1, hi - lo + 1);
        }
        List<Candidate> Eligible(MapRuntime map, Point from, int x1, int x2)
        {
            var result = new List<Candidate>(); if (map == null) return result;
            var graph = BotMovementService.Graphs.Get(map);
            var reachable = BotMovementService.Reachable(graph, MapGeometryCache.Get(map).ground, from);
            int lo = Math.Min(x1, x2), hi = Math.Max(x1, x2);
            foreach (var region in graph.regions)
                if (!region.isRopeRegion && !region.isLadder && (reachable.Count == 0 || reachable.Contains(region.id)))
                {
                    int min = Math.Max(region.minX, lo), max = Math.Min(region.maxX, hi);
                    if (max >= min) result.Add(new Candidate { region = region, lo = min, hi = max });
                }
            return result;
        }
        Candidate Weighted(List<Candidate> candidates)
        {
            if (candidates.Count == 1) return candidates[0];
            long total = 0; foreach (var candidate in candidates) total += candidate.Weight;
            long choice = (long)(random.NextDouble() * total), cumulative = 0;
            foreach (var candidate in candidates) { cumulative += candidate.Weight; if (choice < cumulative) return candidate; }
            return candidates[candidates.Count - 1];
        }
        int InSpan(int lo, int hi) => hi <= lo ? lo : random.Next(lo, hi + 1);
        public Point? Pick(MapRuntime map, Point from, int lo = int.MinValue, int hi = int.MaxValue)
        {
            var eligible = Eligible(map, from, lo, hi); if (eligible.Count == 0) return null;
            var candidate = Weighted(eligible); return candidate.region.PointAt(InSpan(candidate.lo, candidate.hi));
        }
        public List<Point> PickMany(MapRuntime map, Point from, int count, int lo = int.MinValue, int hi = int.MaxValue)
        {
            var result = new List<Point>(); var eligible = Eligible(map, from, lo, hi);
            if (eligible.Count == 0 || count <= 0) return result;
            var occupied = new Dictionary<int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                var candidate = Weighted(eligible);
                if (!occupied.TryGetValue(candidate.region.id, out var taken)) occupied[candidate.region.id] = taken = new List<int>();
                int x = candidate.lo;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    x = InSpan(candidate.lo, candidate.hi); bool clear = true;
                    foreach (int prior in taken) if (Math.Abs(prior - x) < 30) { clear = false; break; }
                    if (clear) break;
                }
                taken.Add(x); result.Add(candidate.region.PointAt(x));
            }
            return result;
        }
    }
}
