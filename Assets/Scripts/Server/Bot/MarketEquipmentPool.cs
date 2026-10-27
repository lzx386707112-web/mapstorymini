using System;
using System.Collections.Generic;
using Server.Bot.Decorator;

namespace Server.Bot
{
    public sealed class MarketEquipment
    {
        public readonly InventoryEntry Item;
        public readonly int Price;
        public MarketEquipment(InventoryEntry item, int price) { Item = item; Price = price; }
    }
    // EquipListGenerator；市场筛选不同于穿戴筛选，不复用后者的等级窗口。
    public sealed class MarketEquipmentPool
    {
        readonly MarketItemPool pool;
        readonly BotItemCatalog catalog;
        readonly EquipMetadataCache cache;
        readonly MarketScrollPricing scrolls;
        readonly DbStore db;
        readonly Random random;
        readonly EquipSelection selection;
        readonly HashSet<int> desirable = new HashSet<int>();
        public MarketEquipmentPool(MarketItemPool pool, BotItemCatalog catalog, EquipMetadataCache cache,
            DbStore db, Random random = null)
        {
            this.pool = pool; this.catalog = catalog; this.cache = cache; this.db = db;
            this.random = random ?? new Random(); selection = new EquipSelection(cache, this.random);
            scrolls = new MarketScrollPricing(pool, catalog, this.random);
            // DesirableEquipList.load的环境启动调用在后续接线；保留显式加载时机。
        }
        public void LoadDesirable(BotResources resources)
        {
            var data = resources.ReadData("soloMapling/itemPool/itemConfig/desirableEquips.yaml") as Dictionary<string, object>;
            if (data == null) return;
            foreach (var value in data.Values) if (value is List<object> ids)
                foreach (var raw in ids) if (int.TryParse(Convert.ToString(raw).Trim(), out int id)) desirable.Add(id);
        }
        public static int[] Pattern(string tier, bool hotRoom = false)
            => hotRoom && tier.Equals("S", StringComparison.OrdinalIgnoreCase) ? new[] {2,2,1,1}
                : tier.Equals("A", StringComparison.OrdinalIgnoreCase) || tier.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? new[] {2,2} : new[] {2,1,1};
        public List<string> Categories(IReadOnlyList<string> categories, int[] pattern)
        {
            var indices = new List<int>(); for (int i = 0; i < categories.Count; i++) indices.Add(i);
            for (int i = indices.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1), tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp; }
            int count = Math.Min(pattern.Length, indices.Count);
            indices = indices.GetRange(0, count); indices.Sort();
            var result = new List<string>();
            // 原调用方类别数均>=模式长度；不额外扩充类别或去除重复装备。
            for (int i = 0; i < count; i++) for (int n = 0; n < pattern[i]; n++) result.Add(categories[indices[i]]);
            return result;
        }
        public List<MarketEquipment> FromYaml(string file, IReadOnlyList<string> categories, string tier, bool hotRoom = false)
        {
            var result = new List<MarketEquipment>();
            var selected = Categories(categories, Pattern(tier, hotRoom));
            string distributed = pool.DistributedTier(tier);
            foreach (string category in selected)
            {
                var row = pool.Pick(file, category, distributed); if (row == null) continue;
                int? id = pool.Variant(row); if (!id.HasValue) continue;
                var item = catalog.CleanEquip(id.Value); int price = pool.CurrentPrice(row);
                if (scrolls.DefaultScrolls(id.Value) != null)
                { scrolls.ScrollEquip(item, db); price = scrolls.EquipValue(item); }
                result.Add(new MarketEquipment(item, price));
            }
            return result;
        }
        public int Level(string tier) => tier == "S" ? random.Next(80,121) : tier == "A" ? random.Next(60,81)
            : tier == "B" ? random.Next(30,71) : random.Next(0,41);
        public int? Cached(string type, int maxLevel, int jobStyle, string tier)
        {
            int gender = random.NextDouble() < .5 ? 0 : 1;
            int req = jobStyle == 100 ? 1 : jobStyle == 200 ? 2 : jobStyle == 300 ? 4 : jobStyle == 400 ? 8 : 0;
            int min = req != 0 ? Math.Max((int)(maxLevel * .6),15) : Math.Max((int)(maxLevel * .25),10);
            int floor = tier.Equals("S", StringComparison.OrdinalIgnoreCase) ? 100000
                : tier.Equals("A", StringComparison.OrdinalIgnoreCase) ? 50000
                : tier.Equals("B", StringComparison.OrdinalIgnoreCase) ? 20000 : 10000;
            var ids = new List<int>();
            foreach (var e in cache.ByType(type))
            {
                if (e.Untradeable || e.Quest || e.Cash || e.RequiredJob != 0 && e.RequiredJob != req ||
                    req == 0 && e.RequiredJob != 0 || e.Gender != 2 && e.Gender != gender) continue;
                if (desirable.Contains(e.Id) || e.RequiredLevel >= min && e.RequiredLevel <= maxLevel && e.Price >= floor) ids.Add(e.Id);
            }
            ids.Sort(); return selection.Weighted(ids);
        }
        int AboveAverage(int value, int range) => value == 0 ? 0 : unchecked((short)(value + Math.Floor(random.NextDouble() * (range + 1))));
        public MarketEquipment Process(int id, bool scroll = false)
        {
            var info = catalog.Info(id); int req = BotItemCatalog.Int(info,"reqJob");
            int job = req == 1 ? 100 : req == 2 ? 200 : req == 4 ? 300 : req == 8 ? 400 : req == 16 ? 500 : 0;
            int wzPrice = BotItemCatalog.Int(info,"price");
            if (wzPrice <= 50) wzPrice = 5000000;
            int price = MarketEconomy.WzPriceByJob(wzPrice,job);
            var item = catalog.CleanEquip(id);
            if (random.NextDouble() > .5)
            {
                item.str = AboveAverage(item.str,2); item.dex = AboveAverage(item.dex,2);
                item.int_ = AboveAverage(item.int_,2); item.luk = AboveAverage(item.luk,2);
                item.matk = AboveAverage(item.matk,2); item.watk = AboveAverage(item.watk,2);
                item.acc = AboveAverage(item.acc,2); item.avoid = AboveAverage(item.avoid,2); item.jump = AboveAverage(item.jump,2);
                item.wdef = AboveAverage(item.wdef,5); item.mdef = AboveAverage(item.mdef,5);
                item.hp = AboveAverage(item.hp,5); item.mp = AboveAverage(item.mp,5);
                price = MarketEconomy.JavaInt(price * 1.5);
            }
            if (scrolls.DefaultScrolls(id) != null && scroll)
            { scrolls.ScrollEquip(item, db); price = scrolls.EquipValue(item); }
            return new MarketEquipment(item,price);
        }
        public List<MarketEquipment> FromCache(string tier, int jobStyle, bool scroll = false)
        {
            var result = new List<MarketEquipment>(); if (cache == null) return result;
            int max = Level(tier);
            var types = new List<string> {"CAP","COAT","LONGCOAT","PANTS","GLOVES","SHOES"};
            switch (jobStyle)
            {
                case 0: types.AddRange(new[] {"SHIELD","CAPE","EARRING","ACCESSORY"}); break;
                case 100: types.AddRange(new[] {"SHIELD","SWORD","SWORD_2H","AXE","AXE_2H","SPEAR","POLEARM"}); break;
                case 200: types.AddRange(new[] {"SHIELD","WAND","STAFF"}); break;
                case 300: types.AddRange(new[] {"BOW","CROSSBOW"}); break;
                case 400: types.AddRange(new[] {"SHIELD","CLAW","DAGGER"}); break;
            }
            for (int attempt = 0; attempt < 15 && result.Count < 4; attempt++)
            { int? id = Cached(types[random.Next(types.Count)],max,jobStyle,tier); if (id.HasValue) result.Add(Process(id.Value,scroll)); }
            return result;
        }
        public List<MarketEquipment> ForJob(string tier, int jobStyle, bool hotRoom = false)
        {
            string file; string[] categories;
            switch (jobStyle)
            {
                case 0: file = "common.yaml"; categories = new[] {"Earring","Overall","Gloves","Shoes","Cape","Hat","Bottom","Top"}; break;
                case 100: file = "warrior.yaml"; categories = new[] {"Weapon","Hat","Top","Pants"}; break;
                case 200: file = "mage.yaml"; categories = new[] {"Weapon","Hat","Top","Shield"}; break;
                case 300: file = "bowman.yaml"; categories = new[] {"Weapon","Hat","Top","Pants"}; break;
                case 400: file = "thief.yaml"; categories = new[] {"Weapon","Hat","Top","Pants","Shield"}; break;
                default: return new List<MarketEquipment>();
            }
            var result = FromYaml(file,categories,tier,hotRoom);
            result.AddRange(FromCache(tier,jobStyle));
            return result;
        }
    }
}
