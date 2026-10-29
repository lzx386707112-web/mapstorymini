using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot
{
    // PartyGrindFloors.java: cluster ledges by the running rounded mean, not pairwise distance.
    public static class BotPartyGrindFloors
    {
        public const int Band = 80;
        public readonly struct Floor
        {
            public readonly int centerY, minX, maxX;
            public Floor(int centerY, int minX, int maxX)
            { this.centerY = centerY; this.minX = minX; this.maxX = maxX; }
            public bool ContainsY(int y) => Math.Abs(y-centerY) <= Band;
            public int ClampX(int x) => Math.Max(minX,Math.Min(maxX,x));
        }
        public static int IndexFor(int slot, int count) => count <= 0 ? 0 : (slot % count + count) % count;
        public static List<Floor> Cluster(IEnumerable<Floor> samples)
        {
            var ordered = samples == null ? new List<Floor>() : new List<Floor>(samples);
            // Java's ordered.sort is stable.
            for (int i = 1; i < ordered.Count; i++)
            {
                var sample = ordered[i]; int at = i;
                while (at > 0 && ordered[at-1].centerY > sample.centerY)
                { ordered[at] = ordered[at-1]; at--; }
                ordered[at] = sample;
            }
            var result = new List<Floor>();
            if (ordered.Count == 0) return result;
            int sum = ordered[0].centerY, n = 1, min = ordered[0].minX, max = ordered[0].maxX;
            for (int i = 1; i < ordered.Count; i++)
            {
                var sample = ordered[i];
                if (Math.Abs(sample.centerY - Mean(sum,n)) <= Band)
                { sum = unchecked(sum + sample.centerY); n++; min = Math.Min(min,sample.minX); max = Math.Max(max,sample.maxX); }
                else
                { result.Add(new Floor(Mean(sum,n),min,max)); sum = sample.centerY; n = 1; min = sample.minX; max = sample.maxX; }
            }
            result.Add(new Floor(Mean(sum,n),min,max)); return result;
        }
        static int Mean(int sum, int count) => (int)Math.Floor((float)sum/count + .5f);
        public static List<Floor> FromMap(MapRuntime map, Point fallback)
        {
            var samples = new List<Floor>();
            if (map != null)
                foreach (var region in BotMovementService.Graphs.Get(map,BotMovementProfile.Base).regions)
                    if (!region.isRopeRegion && !region.isLadder && region.Width >= 40)
                        samples.Add(new Floor(region.Center.y,region.minX,region.maxX));
            var floors = Cluster(samples);
            if (floors.Count == 0) floors.Add(new Floor(fallback.y,fallback.x-400,fallback.x+400));
            return floors;
        }
        public static Point Station(MapRuntime map, Floor floor, int desiredX)
        {
            int x = floor.ClampX(desiredX);
            return map == null ? new Point(x,floor.centerY) :
                MapGeometryCache.Get(map).ground.GroundPoint(x,floor.centerY) ?? new Point(x,floor.centerY);
        }
    }
}
