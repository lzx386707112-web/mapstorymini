using System;
using System.Collections.Generic;

namespace Server.Bot.Decorator
{
    public sealed class EquipSelection
    {
        readonly EquipMetadataCache cache;
        readonly Random random;
        public EquipSelection(EquipMetadataCache cache, Random random = null)
        { this.cache = cache; this.random = random ?? new Random(); }
        public static int JobStyle(BotCharacter bot)
        {
            int type = bot.job / 100;
            if (type == 1 || type == 11 || type == 21) return 100;
            if (type == 2 || type == 12 || type == 22) return 200;
            if (type == 3 || type == 13) return bot.job / 10 == 32 ? 320 : 300;
            if (type == 4 || type == 14) return 400;
            if (type == 5 || type == 15) return bot.str > bot.dex ? 510 : 520;
            return 0;
        }
        public int? ForWearing(string type, BotCharacter bot)
        {
            int req = JobStyle(bot) switch { 100 => 1, 200 => 2, 300 => 4, 400 => 8, 500 => 16, _ => 0 };
            return Pick(type, bot.level, req, bot.gender) ?? Pick(type, bot.level, 0, bot.gender);
        }
        public int? Pick(string type, int maxLevel, int requiredJob, int gender)
        {
            int minLevel = maxLevel - Math.Max((int)(maxLevel * .25), 10);
            var ids = new List<int>();
            foreach (var e in cache.ByType(type))
                if (!e.Cash && (e.Gender == 2 || e.Gender == gender) && e.RequiredLevel >= minLevel
                    && e.RequiredLevel <= maxLevel && e.RequiredJob == requiredJob) ids.Add(e.Id);
            return Weighted(ids);
        }
        public int? Weighted(IReadOnlyList<int> ids, int intervalPercentage = 20)
        {
            if (ids == null || ids.Count == 0) return null;
            if (intervalPercentage < 1 || intervalPercentage > 100) throw new ArgumentOutOfRangeException(nameof(intervalPercentage));
            int size = Math.Max(1, ids.Count * intervalPercentage / 100), count = (ids.Count + size - 1) / size;
            double total = (double)count * (count + 1) / 2, roll = random.NextDouble() * total, accumulated = 0;
            int selected = 0;
            for (int i = 0; i < count; i++) { accumulated += count - i; if (roll <= accumulated) { selected = i; break; } }
            int start = selected * size, end = Math.Min(start + size, ids.Count);
            return ids[start + random.Next(end - start)];
        }
    }
}
