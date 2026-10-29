using System;
using System.Collections.Generic;
using Server.Bot.Decorator;
using Server.Bot.Environment;
using Server.Bot.Trade;

namespace Server.Bot
{
    public enum MarketPurchaseResult { Success, Unavailable, InvalidQuantity, InsufficientMeso, InventoryFull }

    // 地图商店权威集合。店主mock角色不加入BotRegistry；可见摊主走原角色生成路径。
    public sealed class MarketShopService : IDisposable
    {
        static readonly int[] permits = {5030000,5030001,5030002,5030004,5030008};
        readonly DbStore db;
        readonly BotTradeService trades;
        readonly Func<int, Field.MapRuntime> map;
        readonly Random random;
        readonly Dictionary<int, MarketShop> shops = new Dictionary<int, MarketShop>();
        readonly Dictionary<int, int> counts = new Dictionary<int, int>();
        readonly Dictionary<int, int> visits = new Dictionary<int, int>();
        readonly List<BotTasks.Handle> pending = new List<BotTasks.Handle>();
        MarketShopGenerator generator;
        bool disposed, populated;
        int ownerId = 800001;
        public MarketShopService(DbStore db, BotTradeService trades, Func<int, Field.MapRuntime> map, Random random = null)
        { this.db = db; this.trades = trades; this.map = map; this.random = random ?? new Random(); }
        MarketShopGenerator Generator
        {
            get
            {
                if (generator != null) return generator;
                var resources = BotResources.Load(); var catalog = BotItemCatalog.Shared;
                var pool = new MarketItemPool(resources, random);
                var equipment = new MarketEquipmentPool(pool, catalog, new EquipMetadataCache(catalog, EquipMetadataCache.LoadPools()), db, random);
                equipment.LoadDesirable(resources);
                var descriptions = new MarketShopDescriptions(resources, catalog, random);
                return generator = new MarketShopGenerator(pool, equipment, new MarketEconomy(random), random) {Description = descriptions.Generate};
            }
        }
        public List<MarketShop> OnMap(int world, int channel, int mapId)
        {
            var result = new List<MarketShop>();
            foreach (var shop in shops.Values) if (shop.open && shop.world == world && shop.channel == channel && shop.mapId == mapId) result.Add(shop);
            return result;
        }
        public MarketShop Get(int id) => shops.TryGetValue(id, out var shop) ? shop : null;
        bool Reserve(int mapId)
        {
            int count = counts.TryGetValue(mapId, out var existing) ? existing : 0;
            if (count >= Math.Max(BotConfig.MarketShopMax(db), 24)) return false;
            counts[mapId] = count + 1; return true;
        }
        public void PopulateRooms()
        {
            if (disposed || populated || !BotConfig.FmRegionFillEnabled(db)) return;
            populated = true;
            for (int id = 910000001; id <= 910000012; id++)
            {
                int room = id; var spots = MarketShopSpots.ForRoom(room);
                for (int i = spots.Length - 1; i > 0; i--) { int j = random.Next(i + 1); var tmp = spots[i]; spots[i] = spots[j]; spots[j] = tmp; }
                foreach (var point in spots) { var position = point; BotEnvironment.Post(() => Spawn(room, position, false)); }
                for (int n = 0; n < 2; n++) { var point = spots[random.Next(spots.Length)]; BotEnvironment.Post(() => Spawn(room, point, true)); }
            }
        }
        void Spawn(int mapId, BotPoint point, bool playerStore)
        {
            if (disposed || !Reserve(mapId)) return;
            try
            {
                if (map(mapId) == null) { counts[mapId]--; return; }
                var position = PlatformSpawner.SnapToGround(mapId, point);
                if (playerStore)
                {
                    var actor = BotGeneration.CreateBotPollReadiness(position.x, position.y, mapId, true);
                    if (actor == null) { counts[mapId]--; return; }
                    pending.Add(BotTasks.Schedule(() =>
                    {
                        if (disposed) return;
                        Open(actor.id, actor.name, mapId, (int)actor.x, (int)actor.y, BotFurniture.RandomStorePermit(random), true);
                        if (random.NextDouble() < .5) actor.facing = -actor.facing;
                        if (random.NextDouble() < .4) pending.Add(BotTasks.Schedule(() => { if (!disposed) actor.chair = BotFurniture.RandomChair(random); }, 500));
                    }, 1200));
                }
                else Open(ownerId++, BotGeneration.GetRandomShopOwnerIGN(), mapId, position.x, position.y, permits[random.Next(permits.Length)], false);
            }
            catch { counts[mapId]--; throw; }
        }
        void Open(int owner, string name, int mapId, int x, int y, int permit, bool playerStore)
        {
            var client = BotClient.GetBotClient();
            var shop = new MarketShop {ownerId = owner, ownerName = name, mapId = mapId, x = x, y = y,
                world = client.world, channel = client.channel, permitId = permit, playerStore = playerStore};
            Generator.Generate(shop); shop.open = true; shops.Add(owner, shop);
        }
        public MarketShop Visit(BotActor actor, int owner)
        {
            Leave(actor.id); var shop = Get(owner);
            if (shop == null || !Matches(actor, shop) || actor.trade != null || actor.id == owner) return null;
            int count = 0; foreach (int visiting in visits.Values) if (visiting == owner) count++;
            if (count >= 3) return null;
            visits[actor.id] = owner; return shop;
        }
        static bool Matches(BotActor actor, MarketShop shop) => actor != null && actor.online && shop.open
            && actor.world == shop.world && actor.channel == shop.channel && actor.mapId == shop.mapId;
        public void Leave(int actor) => visits.Remove(actor);
        public MarketPurchaseResult Buy(BotActor actor, int owner, int index, int quantity)
        {
            var shop = Get(owner);
            if (shop == null || !Matches(actor, shop) || !visits.TryGetValue(actor.id, out int visiting) || visiting != owner
                || index < 0 || index >= shop.items.Count || actor.trade != null) return MarketPurchaseResult.Unavailable;
            var stock = shop.items[index];
            if (quantity < 1 || quantity > short.MaxValue || !stock.exists || quantity > stock.bundles) return MarketPurchaseResult.InvalidQuantity;
            var item = stock.item.Copy(); item.quantity = unchecked((short)(item.quantity * quantity));
            if (item.quantity <= 0 || item.isEquip && item.quantity > 1 || stock.price < 0) return MarketPurchaseResult.InvalidQuantity;
            int price = MarketEconomy.JavaInt(Math.Min((float)stock.price * quantity, int.MaxValue));
            int balance = actor is BotCharacter bot ? bot.meso : actor.character?.record.meso ?? 0;
            if (balance < price) return MarketPurchaseResult.InsufficientMeso;
            TradeItemRules.ApplyKarma(item);
            if (!trades.Purchase(actor, item, price)) return MarketPurchaseResult.InventoryFull;
            stock.bundles -= quantity; if (stock.bundles < 1) stock.exists = false;
            return MarketPurchaseResult.Success;
        }
        public void Dispose()
        {
            disposed = true; foreach (var task in pending) task.Cancel(); pending.Clear();
            foreach (var shop in shops.Values) shop.open = false;
            shops.Clear(); counts.Clear(); visits.Clear();
        }
    }
}
