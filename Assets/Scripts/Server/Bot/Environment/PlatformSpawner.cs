using System;
using System.Collections.Generic;
using System.Linq;
using Server.Field;

namespace Server.Bot.Environment
{
    // Environment/PlatformSpawner.java：保留间距30、按空隙/隔离度加权和30%偏移。
    public sealed class PlatformSpawner
    {
        readonly Random random;
        public PlatformSpawner(Random random = null) { this.random = random ?? new Random(); }
        public static BotPoint SnapToGround(int mapId, BotPoint raw)
            => new BotPoint(raw.x, (int)MapService.SnapToGround(mapId, raw.x, raw.y));

        public BotPoint FindUnoccupiedPoint(Platform platform, List<BotPoint> occupied)
            => platform.IsSloped ? FindSloped(platform, occupied) : FindFlat(platform, occupied);

        public List<BotPoint> FindUnoccupiedPoints(Platform platform, List<BotPoint> occupied, int count)
        {
            var result = new List<BotPoint>();
            var all = occupied == null ? new List<BotPoint>() : new List<BotPoint>(occupied);
            for (int i = 0; i < count; i++) { var p = FindUnoccupiedPoint(platform, all); result.Add(p); all.Add(p); }
            return result;
        }
        BotPoint RandomFlat(Platform p) => new BotPoint(p.minX + random.Next(Math.Max(1, p.Width)), p.baseY);
        BotPoint FindFlat(Platform platform, List<BotPoint> occupied)
        {
            if (occupied == null || occupied.Count == 0) return RandomFlat(platform);
            var sorted = occupied.OrderBy(p => p.x).ToList();
            var gaps = new List<(int start, int end)>();
            if (sorted[0].x - platform.minX > 30) gaps.Add((platform.minX, sorted[0].x));
            for (int i = 0; i < sorted.Count - 1; i++)
                if (sorted[i + 1].x - sorted[i].x > 30) gaps.Add((sorted[i].x, sorted[i + 1].x));
            if (platform.maxX - sorted[sorted.Count - 1].x > 30) gaps.Add((sorted[sorted.Count - 1].x, platform.maxX));
            if (gaps.Count == 0) return RandomFlat(platform);
            var gap = gaps[0];
            if (gaps.Count > 1)
            {
                int value = random.Next(gaps.Sum(g => g.end - g.start)), sum = 0;
                foreach (var g in gaps) { sum += g.end - g.start; if (value < sum) { gap = g; break; } }
            }
            int size = gap.end - gap.start;
            int maxOffset = (int)(size / 2 * .3);
            int offset = maxOffset > 0 ? random.Next(maxOffset * 2) - maxOffset : 0;
            int padding = Math.Min(15, size / 4);
            int x = Math.Min(gap.end - padding, Math.Max(gap.start + padding, gap.start + size / 2 + offset));
            return new BotPoint(x, platform.baseY);
        }
        BotPoint FindSloped(Platform platform, List<BotPoint> occupied)
        {
            var refs = platform.sortedPoints;
            if (refs.Count == 0) return new BotPoint(platform.minX, platform.baseY);
            if (occupied == null || occupied.Count == 0) return refs[random.Next(refs.Count)];
            var available = refs.Where(p => !occupied.Any(o => Math.Abs(p.x - o.x) < 30)).ToList();
            if (available.Count == 0) return refs[random.Next(refs.Count)];
            if (available.Count == 1) return available[0];
            var scores = available.Select(p => (int)Math.Sqrt(occupied.Min(o => Math.Abs(p.x - o.x)))).ToList();
            int total = scores.Sum();
            if (total == 0) return available[random.Next(available.Count)];
            int value = random.Next(total), sum = 0;
            for (int i = 0; i < available.Count; i++) { sum += scores[i]; if (value < sum) return available[i]; }
            return available[available.Count - 1];
        }
    }
}
