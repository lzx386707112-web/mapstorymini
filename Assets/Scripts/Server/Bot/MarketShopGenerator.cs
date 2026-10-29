using System;
using System.Collections.Generic;

namespace Server.Bot
{
    public enum MarketShopType { Warrior, Mage, Bowman, Thief, Common, Pirate, Scroll, DarkScroll, Potion, ETC, Chair, Mastery, Stars }

    public sealed class MarketShopStock
    {
        public InventoryEntry item;
        public int bundles, price;
        public bool exists = true;
    }

    public sealed class MarketShop
    {
        public int ownerId, mapId, world, channel, permitId, x, y;
        public string ownerName, description, tier;
        public bool open, playerStore;
        public MarketShopType primary, secondary;
        public MarketShopType? tertiary;
        public readonly List<MarketShopStock> items = new List<MarketShopStock>();
    }

    // ArtificialFreeMarket + ArtificialShopGenerator。商店数据与地图展示分离。
    public sealed class MarketShopGenerator
    {
        static readonly int[] normal = {12,12,12,18,18,1,5,5,5,5,3,4};
        static readonly int[] hot = {18,18,18,20,16,0,3,2,2,1,1,1};
        static readonly MarketShopType[] secondary = {MarketShopType.Potion, MarketShopType.Scroll, MarketShopType.DarkScroll,
            MarketShopType.Mastery, MarketShopType.Chair, MarketShopType.ETC};
        static readonly int[] secondaryWeights = {40,25,15,5,2,13};
        readonly MarketItemPool pool;
        readonly MarketEquipmentPool equipment;
        readonly MarketEconomy economy;
        readonly Random random;
        public Func<MarketShop, string> Description;
        public MarketShopGenerator(MarketItemPool pool, MarketEquipmentPool equipment, MarketEconomy economy, Random random = null)
        { this.pool = pool; this.equipment = equipment; this.economy = economy; this.random = random ?? new Random(); }

        int Weighted(int[] weights)
        {
            int total = 0; foreach (int weight in weights) total += weight;
            int pick = random.Next(total);
            for (int i = 0; i < weights.Length; i++) { pick -= weights[i]; if (pick < 0) return i; }
            throw new InvalidOperationException();
        }
        public void Generate(MarketShop shop)
        {
            shop.tier = economy.TierForRoom(shop.mapId);
            bool hotRoom = MarketEconomy.IsHotRoom(shop.mapId);
            shop.primary = (MarketShopType)Weighted(hotRoom ? hot : normal);
            Primary(shop, shop.primary);
            if (hotRoom)
            {
                Primary(shop, (MarketShopType)random.Next(5));
                Secondary(shop); Secondary(shop);
                for (int attempt = 0; shop.items.Count < 10 && attempt < 3; attempt++) Primary(shop, (MarketShopType)random.Next(5));
            }
            else
            {
                Secondary(shop);
                if (shop.items.Count < 5) { shop.tertiary = (MarketShopType)random.Next(5); Primary(shop, shop.tertiary.Value); }
            }
            shop.description = Description?.Invoke(shop);
            foreach (var entry in shop.items)
            {
                if (hotRoom && (entry.item.inventoryType == 2 || entry.item.inventoryType == 4))
                    entry.bundles = unchecked((short)(entry.bundles * random.Next(5, 9)));
                if (random.NextDouble() < .09) { entry.bundles = 0; entry.exists = false; }
            }
            ApplySale(shop, random.Next(10000));
            shop.description += "          <" + (shop.mapId - 910000000) + ">";
            EnsureItems(shop);
        }
        public static void ApplySale(MarketShop shop, int roll)
        {
            if (roll >= 800) return;
            if (roll == 0) shop.description = "一金币店";
            else shop.description += roll < 100 ? "清仓" : "低价";
            foreach (var entry in shop.items) entry.price = roll == 0 ? 1 : (int)(entry.price * (roll < 100 ? .7 : .85));
        }
        public static void EnsureItems(MarketShop shop)
        {
            if (shop.items.Count != 0) return;
            foreach (int id in new[] {2000002,2000001}) shop.items.Add(new MarketShopStock
            { item = new InventoryEntry {itemId = id, inventoryType = 2, position = 1, quantity = 100}, bundles = 20, price = 1 });
        }
        void Add(MarketShop shop, InventoryEntry item, int bundles, int price)
        {
            shop.items.Add(new MarketShopStock {item = item,
                bundles = unchecked((short)MarketEconomy.Quantity(bundles, shop.mapId, shop.x, shop.y)),
                price = economy.Price(price, shop.mapId, shop.x, shop.y)});
        }
        void Primary(MarketShop shop, MarketShopType type)
        {
            if ((int)type <= 5)
            {
                int job = type == MarketShopType.Warrior ? 100 : type == MarketShopType.Mage ? 200 :
                    type == MarketShopType.Bowman ? 300 : type == MarketShopType.Thief ? 400 : 0;
                foreach (var entry in equipment.ForJob(shop.tier, job, MarketEconomy.IsHotRoom(shop.mapId)))
                    Add(shop, entry.Item, 1, entry.Price);
                if (job == 400 && random.NextDouble() < (shop.tier == "S" ? .7 : shop.tier == "A" ? .4 : .1))
                    Items(shop, MarketShopType.Stars);
            }
            else for (int i = 0; i < (type == MarketShopType.Chair ? 5 : 3); i++) Items(shop, type);
        }
        void Secondary(MarketShop shop)
        {
            int count = random.Next(2) == 0 ? 1 : 2;
            var selected = new HashSet<MarketShopType>();
            while (selected.Count < count)
            {
                var type = secondary[Weighted(secondaryWeights)];
                if (!selected.Add(type)) continue;
                if (selected.Count == 1) shop.secondary = type;
                Items(shop, type);
            }
        }
        void Items(MarketShop shop, MarketShopType type)
        {
            List<MarketItem> entries;
            switch (type)
            {
                case MarketShopType.Scroll: entries = pool.Scrolls(shop.tier); break;
                case MarketShopType.DarkScroll: entries = pool.Scrolls(shop.tier, true); break;
                case MarketShopType.Potion: entries = pool.Potions(shop.tier); break;
                case MarketShopType.ETC: entries = pool.Etc(shop.tier); break;
                case MarketShopType.Mastery: entries = pool.MasteryBooks(shop.tier); break;
                case MarketShopType.Chair: entries = pool.Chairs(shop.tier); break;
                case MarketShopType.Stars: entries = pool.Stars(shop.tier); break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
            foreach (var entry in entries) Add(shop, new InventoryEntry {itemId = entry.itemId,
                inventoryType = entry.itemId / 1000000, position = 1, quantity = 1}, entry.quantity, entry.price);
        }
    }
}
