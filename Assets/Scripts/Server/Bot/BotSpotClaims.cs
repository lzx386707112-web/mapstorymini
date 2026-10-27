using System;
using System.Collections.Generic;

namespace Server.Bot
{
    public sealed class BotSpotClaims
    {
        readonly Dictionary<int, Dictionary<int, Dictionary<int, int>>> claims = new Dictionary<int, Dictionary<int, Dictionary<int, int>>>();
        public int Claim(int mapId, int spotId, int capacity, int botId)
        {
            if (!claims.TryGetValue(mapId, out var spots)) claims[mapId] = spots = new Dictionary<int, Dictionary<int, int>>();
            if (!spots.TryGetValue(spotId, out var slots)) spots[spotId] = slots = new Dictionary<int, int>();
            if (slots.TryGetValue(botId, out int existing)) return existing;
            int cap = Math.Max(1, capacity);
            if (slots.Count >= cap) return -1;
            var used = new HashSet<int>(slots.Values);
            for (int i = 0; i < cap; i++) if (!used.Contains(i)) { slots[botId] = i; return i; }
            return -1;
        }
        public void Release(int mapId, int spotId, int botId)
        {
            if (!claims.TryGetValue(mapId, out var spots) || !spots.TryGetValue(spotId, out var slots)) return;
            slots.Remove(botId);
            if (slots.Count == 0) spots.Remove(spotId);
            if (spots.Count == 0) claims.Remove(mapId);
        }
        public int Holders(int mapId, int spotId)
            => claims.TryGetValue(mapId, out var spots) && spots.TryGetValue(spotId, out var slots) ? slots.Count : 0;
        public static (int minX, int maxX) Section(int minX, int maxX, int slot, int capacity)
        {
            int k = Math.Max(1, capacity), s = Math.Max(0, Math.Min(slot, k - 1)), width = Math.Max(0, maxX - minX);
            return (minX + width * s / k, minX + width * (s + 1) / k);
        }
    }
}
