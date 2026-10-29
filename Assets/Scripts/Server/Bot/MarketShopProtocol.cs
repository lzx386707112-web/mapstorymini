using System;
using System.Collections.Generic;

namespace Server.Bot
{
    public enum MarketOperation { Read, Visit, Buy, Leave }
    public sealed class MarketRequest
    {
        public int characterId, ownerId, itemIndex, quantity;
        public MarketOperation operation;
    }
    public sealed class MarketShopSnapshot
    {
        public int ownerId, permitId, x, y;
        public string ownerName, description;
        public bool playerStore;
        public readonly List<MarketShopStock> items = new List<MarketShopStock>();
        public static MarketShopSnapshot Of(MarketShop shop)
        {
            if (shop == null || !shop.open) return null;
            var copy = new MarketShopSnapshot {ownerId = shop.ownerId, permitId = shop.permitId, x = shop.x, y = shop.y,
                ownerName = shop.ownerName, description = shop.description, playerStore = shop.playerStore};
            foreach (var stock in shop.items) copy.items.Add(new MarketShopStock {item = stock.item.Copy(),
                price = stock.price, bundles = stock.bundles, exists = stock.exists});
            return copy;
        }
    }
    public sealed class MarketResponse
    {
        public bool ok = true;
        public string error;
        public MarketPurchaseResult purchase;
        public MarketShopSnapshot visiting;
        public readonly List<MarketShopSnapshot> shops = new List<MarketShopSnapshot>();
    }
    public sealed class MarketShopProtocol
    {
        readonly MarketShopService service;
        readonly Func<int, BotActor> players;
        public MarketShopProtocol(MarketShopService service, Func<int, BotActor> players)
        { this.service = service; this.players = players; }
        public MarketResponse Handle(MarketRequest request)
        {
            var actor = players(request.characterId);
            if (actor == null || !actor.online || actor.IsBot()) return new MarketResponse {ok = false, error = "角色不在线"};
            var response = new MarketResponse();
            switch (request.operation)
            {
                case MarketOperation.Visit:
                    if (service.Visit(actor, request.ownerId) == null) { response.ok = false; response.error = "无法进入该商店"; }
                    break;
                case MarketOperation.Buy:
                    response.purchase = service.Buy(actor, request.ownerId, request.itemIndex, request.quantity);
                    response.ok = response.purchase == MarketPurchaseResult.Success;
                    if (!response.ok) response.error = response.purchase == MarketPurchaseResult.InsufficientMeso ? "金币不足"
                        : response.purchase == MarketPurchaseResult.InventoryFull ? "背包空间不足"
                        : response.purchase == MarketPurchaseResult.OwnerMesoFull ? "店主无法持有更多金币" : "商品或商店状态已变更";
                    break;
                case MarketOperation.Leave: service.Leave(actor.id); break;
            }
            foreach (var shop in service.OnMap(actor.world, actor.channel, actor.mapId)) response.shops.Add(MarketShopSnapshot.Of(shop));
            response.visiting = MarketShopSnapshot.Of(service.Visiting(actor));
            return response;
        }
    }
}
