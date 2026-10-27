using System;
using System.Collections.Generic;

namespace Server.Bot.Decorator
{
    public sealed class NXItemPool
    {
        sealed class Item { public int id, gender; }
        readonly Dictionary<string, List<Item>> pools = new Dictionary<string, List<Item>>();
        readonly Random random;
        static readonly HashSet<string> body = new HashSet<string> { "caps", "tops", "bottoms", "overalls", "shoes", "gloves", "capes" };
        static readonly Dictionary<string, string[]> types = new Dictionary<string, string[]> {
            ["caps"] = new[] { "CAP" }, ["tops"] = new[] { "COAT" }, ["bottoms"] = new[] { "PANTS" },
            ["overalls"] = new[] { "LONGCOAT" }, ["shoes"] = new[] { "SHOES" }, ["gloves"] = new[] { "GLOVES" },
            ["capes"] = new[] { "CAPE" }, ["earrings"] = new[] { "EARRING" }, ["face"] = new[] { "FACE" },
            ["eye"] = new[] { "ACCESSORY" }, ["rings"] = new[] { "RING" },
            ["weapons"] = new[] { "SWORD", "AXE", "MACE", "DAGGER", "WAND", "STAFF", "SWORD_2H", "AXE_2H", "MACE_2H", "SPEAR", "POLEARM", "BOW", "CROSSBOW", "CLAW", "KNUCKLER", "PISTOL" }
        };
        public NXItemPool(BotResources resources, EquipMetadataCache cache, Dictionary<string, object> data, Random random = null)
        { this.random = random ?? new Random(); Reload(resources, cache, data); }
        public void Reload(BotResources resources, EquipMetadataCache cache, Dictionary<string, object> data)
        {
            pools.Clear();
            var yaml = resources.ReadData("soloMapling/ArtificialPlayer/BotDecoratorSystem/NXItemPool.yaml") as Dictionary<string, object>;
            if (yaml != null) foreach (var pair in yaml)
            {
                if (!(pair.Value is List<object> entries)) continue;
                var list = new List<Item>(); pools[pair.Key] = list;
                foreach (var raw in entries)
                {
                    if (raw == null) continue;
                    int id; int? gender = null;
                    if (raw is Dictionary<string, object> map)
                    {
                        id = map.TryGetValue("id", out var value) ? Convert.ToInt32(value) : 0;
                        if (map.TryGetValue("gender", out var g)) gender = ParseGender(g);
                    }
                    else if (raw is string || raw is long || raw is int || raw is double) id = Convert.ToInt32(raw);
                    else continue;
                    list.Add(new Item { id = id, gender = Gender(id, pair.Key, gender) });
                }
            }
            foreach (var pair in (Dictionary<string, object>)data["fashion"])
            {
                var ids = (List<object>)pair.Value;
                if (ids.Count == 0) continue;
                pools[pair.Key] = new List<Item>();
                foreach (var id in ids) pools[pair.Key].Add(new Item { id = Convert.ToInt32(id), gender = 2 });
            }
            foreach (var pair in types)
            {
                if (pools.TryGetValue(pair.Key, out var list) && list.Count > 0) continue;
                list = new List<Item>(); pools[pair.Key] = list;
                foreach (string type in pair.Value) foreach (var entry in cache.ByType(type, true))
                    list.Add(new Item { id = entry.Id, gender = Gender(entry.Id, pair.Key, null) });
            }
            foreach (var pair in (Dictionary<string, object>)data["fallback"])
            {
                if (pools.TryGetValue(pair.Key, out var list) && list.Count > 0) continue;
                list = new List<Item>(); pools[pair.Key] = list;
                foreach (var id in (List<object>)pair.Value) list.Add(new Item { id = Convert.ToInt32(id), gender = 2 });
            }
            if (!pools.TryGetValue("rings", out var rings)) pools["rings"] = rings = new List<Item>();
            var existing = new HashSet<int>(); foreach (var ring in rings) existing.Add(ring.id);
            foreach (List<object> range in (List<object>)data["showcaseRingRanges"])
                for (int id = Convert.ToInt32(range[0]), end = Convert.ToInt32(range[1]); id <= end; id++)
                    if (existing.Add(id)) rings.Add(new Item { id = id, gender = 2 });
        }
        static int? ParseGender(object raw) => Convert.ToString(raw)?.ToLowerInvariant() switch
        { "male" => 0, "0" => 0, "female" => 1, "1" => 1, "unisex" => 2, "2" => 2, _ => null };
        static int Gender(int id, string category, int? value)
        { int digit = id / 1000 % 10; return value ?? (body.Contains(category) && digit <= 1 ? digit : 2); }
        public int? Pick(string category, int gender)
        {
            if (!pools.TryGetValue(category, out var list)) return null;
            var eligible = new List<Item>(); foreach (var item in list) if (item.gender == 2 || item.gender == gender) eligible.Add(item);
            return eligible.Count == 0 ? (int?)null : eligible[random.Next(eligible.Count)].id;
        }
    }
}
