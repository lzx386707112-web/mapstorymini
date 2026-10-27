using System;
using System.Collections.Generic;
using System.Globalization;

namespace Server.Bot
{
    public sealed class MarketItem
    {
        public int itemId, price, quantity;
        public MarketItem(int itemId, int price = -1, int quantity = 1)
        { this.itemId = itemId; this.price = price; this.quantity = quantity; }
    }

    // ItemSelector / ItemDatabase / QuantitySelector。JSON只承载原YAML；标量按十进制读取。
    public sealed class MarketItemPool
    {
        const string Directory = "soloMapling/itemPool/itemConfig/";
        static readonly string[] databasePools = { "thief.yaml", "common.yaml", "etc.yaml", "scrolls.yaml", "darkscrolls.yaml", "useables.yaml" };
        static readonly string[] scrolls = { "Earring", "Overall", "Gloves", "Claw", "Dagger", "Shoes", "Gloves", "Cape", "ETC" };
        static readonly string[] darkScrolls = { "Earring", "Overall", "Shoes", "Gloves", "Cape", "Hat", "Top", "Bottom", "Shield", "ETC", "Special" };
        readonly BotResources resources;
        readonly Random random;
        readonly Dictionary<string, Dictionary<string, object>> pools = new Dictionary<string, Dictionary<string, object>>();
        readonly Dictionary<int, Dictionary<string, object>> database = new Dictionary<int, Dictionary<string, object>>();
        bool databaseLoaded;
        public int Version = 55; // MapleVersionManager.itemPoolVersion
        public MarketItemPool(BotResources resources, Random random = null)
        { this.resources = resources; this.random = random ?? new Random(); }
        static int Number(object value) => int.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        static Dictionary<string, object> Map(object value) => value as Dictionary<string, object>;
        static object Get(Dictionary<string, object> map, string key) => map != null && map.TryGetValue(key, out var value) ? value : null;
        static List<object> List(object value) => value as List<object>;
        public static object ValueForVersion(Dictionary<string, object> values, int version)
        {
            object result = null; int highest = -1;
            if (values != null) foreach (var pair in values)
            {
                int start = Number(pair.Key);
                if (start <= version && start > highest) { highest = start; result = pair.Value; }
            }
            return result;
        }
        Dictionary<string, object> Load(string file)
        {
            file = file.Replace('\\', '/'); file = file.Substring(file.LastIndexOf('/') + 1);
            if (pools.TryGetValue(file, out var pool)) return pool;
            try { pool = Map(resources.ReadData(Directory + file)); }
            catch (Exception) { pool = null; }
            pools[file] = pool; return pool;
        }
        public Dictionary<string, object> Pick(string file, string type, string tier)
        {
            var entries = List(Get(Load(file), type)); if (entries == null) return null;
            var matches = new List<Dictionary<string, object>>();
            foreach (var raw in entries)
            {
                var item = Map(raw);
                if (Convert.ToString(ValueForVersion(Map(Get(item, "tier")), Version), CultureInfo.InvariantCulture) == tier) matches.Add(item);
            }
            return matches.Count == 0 ? null : matches[random.Next(matches.Count)];
        }
        public int? Variant(Dictionary<string, object> item)
        {
            var ids = List(Get(item, "variant_id"));
            return ids == null || ids.Count == 0 ? (int?)null : Number(ids[random.Next(ids.Count)]);
        }
        public int CurrentPrice(Dictionary<string, object> item)
        { var value = ValueForVersion(Map(Get(item, "price")), Version); return value == null ? -1 : Number(value); }
        public Dictionary<string, object> Item(int id)
        {
            if (!databaseLoaded)
            {
                databaseLoaded = true;
                foreach (string file in databasePools)
                {
                    var pool = Load(file); if (pool == null) break;
                    foreach (var category in pool.Values)
                        if (category is List<object> entries) foreach (var raw in entries)
                        {
                            var row = Map(raw); var variants = List(Get(row, "variant_id"));
                            if (variants != null) foreach (var variant in variants) database[Number(variant)] = row;
                        }
                }
            }
            return database.TryGetValue(id, out var item) ? item : null;
        }
        public int? Price(int id)
        { var row = Item(id); var value = ValueForVersion(Map(Get(row, "price")), Version); return value == null ? (int?)null : Number(value); }
        public string Tier(int id) => Convert.ToString(ValueForVersion(Map(Get(Item(id), "tier")), Version), CultureInfo.InvariantCulture);
        public bool Exists(int id)
        {
            var tiers = Map(Get(Item(id), "tier"));
            if (tiers == null) return false;
            foreach (string key in tiers.Keys) if (Version >= Number(key)) return true;
            return false;
        }
        public int Quantity(string type, string tier)
        {
            var root = Load("itemQuantities.yaml");
            var types = Map(Get(root, "itemQuantities"));
            var itemType = Map(Get(types, type));
            var range = Map(Get(Map(Get(itemType, "tiers")), tier));
            if (range == null) return 5;
            int min = Number(Get(range, "min")), max = Number(Get(range, "max"));
            return random.Next(max - min + 1) + min;
        }
        public string DistributedTier(string tier)
        {
            int roll = random.Next(100);
            switch (tier)
            {
                case "S": return roll < 60 ? "S" : roll < 98 ? "A" : "B";
                case "A": return roll < 70 ? "A" : roll < 80 ? "S" : "B";
                case "B": return roll < 68 ? "B" : roll < 98 ? "A" : "S";
                default: return roll < 20 ? "S" : roll < 60 ? "A" : "B";
            }
        }
        public List<MarketItem> Scrolls(string tier, bool dark = false)
            => dark && Version < 39 ? new List<MarketItem>() : Generate(dark ? "darkscrolls.yaml" : "scrolls.yaml", dark ? darkScrolls : scrolls, "Scroll", tier, 4);
        public List<MarketItem> Potions(string tier) => Generate("useables.yaml", new[] { "Potions" }, "Potion", tier, 3);
        public List<MarketItem> Etc(string tier) => Generate("etc.yaml", new[] { "CrystalRefined", "CrystalOre", "MineralRefined", "MineralOre", "JewelRefined", "JewelOre", "SkillRocks", "BossItems", "RandomETC" }, "ETC", tier, 3);
        public List<MarketItem> MasteryBooks(string tier) => Generate("masteryBook.yaml", new[] { "MasteryBooks" }, "Mastery", tier, 3);
        public List<MarketItem> Chairs(string tier) => Generate("chair.yaml", new[] { "Chairs" }, null, tier, 1);
        public List<MarketItem> Stars(string tier)
        {
            var result = new List<MarketItem>();
            for (int i = 0; i < 2; i++)
            {
                var item = Pick("thief.yaml", "Stars", DistributedTier(tier)); var id = Variant(item);
                if (id.HasValue) result.Add(new MarketItem(id.Value, CurrentPrice(item)));
            }
            return result; // 原Stars不去重，且每次从传入tier重新抽。
        }
        List<MarketItem> Generate(string file, string[] categories, string quantityType, string tier, int count)
        {
            var result = new List<MarketItem>(); var seen = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                string category = categories[random.Next(categories.Length)];
                tier = DistributedTier(tier); // 原循环会将新tier传给下一轮，不是独立抽样。
                var item = Pick(file, category, tier); if (item == null) continue;
                var id = Variant(item); if (!id.HasValue) continue;
                int price = CurrentPrice(item), quantity = quantityType == null ? 1 : Quantity(quantityType, tier);
                if (seen.Add(id.Value)) result.Add(new MarketItem(id.Value, price, quantity));
            }
            return result;
        }
    }
}
