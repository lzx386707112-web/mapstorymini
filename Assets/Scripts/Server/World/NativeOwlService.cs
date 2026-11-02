using System;
using System.Collections.Generic;

// 物品搜索（密涅瓦的猫头鹰，523xxxx）—— 对照 gms-server：
//   net/server/channel/handlers/UseCashItemHandler.java:428-444（itemType == 523 的搜索主流程）
//   net/server/channel/handlers/UseOwlOfMinervaHandler.java（打开搜索框 → 推荐列表）
//   net/server/channel/handlers/OwlWarpHandler.java（点搜索结果跳店）
//   net/server/world/World.java:1337-1365（addOwlItemSearch / getOwlSearchedItems）
//   net/server/world/World.java:1917-1940（getAvailableItemBundles 扫全部在售店铺）
//   util/PacketCreator.java（owlOfMinerva / getOwlOpen / getOwlMessage）
//   constants/id/ItemId.java:392（getOwlItems 固定推荐 11 件）
//   game_config: use_enforce_item_suggestion（默认 false）
//
// 三个照抄的源端行为：
//   1. **搜到东西才扣猫头鹰**：`if (!hmsAvailable.isEmpty()) remove(...)`——空手而归不消耗。
//   2. 结果按 **单价升序**，最多 200 条；「组合」列是份数（bundles），「数量」列是每份数量。
//   3. 打开时的推荐位：搜索过的**种类数** < 5 时给固定 11 件；否则按次数降序取前 10。
//      `use_enforce_item_suggestion` 为真时既不记热度（World.getOwlSearchedItems 直接返回空表），
//      推荐位也恒空。
//
// 单机语义：源端扫的是 getActiveMerchants() + getActivePlayerShops()（世界级，跨频道）。
// 这里的等价面是 MarketShopService 的店铺表——假人开的店、玩家本人的雇佣商店镜像、
// 玩家个人商店镜像都在里面（NativeMerchantService / NativePlayerShopService 的 SyncMarket），
// 所以一次扫描就覆盖了源端那两个集合。
public enum OwlOperation { Open, Search, Warp }

public sealed class OwlRequest
{
    public int characterId;
    public OwlOperation operation;
    public int itemId;                 // Search：要搜的物品 id
    public int ownerId, mapId;         // Warp：目标店铺与来源搜索结果里的地图
}

public sealed class OwlHit
{
    public int ownerId, mapId, bundles, quantity, price;
    public string ownerName = "", shopName = "";
    public byte channel;               // 源端写 channel-1
    public bool equip;                 // 装备才带 addItemInfo（属性）
    public InventoryEntry item;
}

public sealed class OwlResponse
{
    public bool ok = true;
    public string error = "";
    public byte code;                  // SHOP_LINK_RESULT 消息码
    public bool charged;               // 搜到结果 → 客户端扣 1 个猫头鹰
    public List<int> suggestions;      // Open：推荐物品
    public List<OwlHit> hits;          // Search
    public int map = -1;               // Warp：目标地图（-1 表示不动）
}

public static class NativeOwlService
{
    public const int MaxResults = 200;
    public const int TopSearched = 10;
    public const int MinSearchedKinds = 5;

    // SHOP_LINK_RESULT 的消息码（PacketCreator.getOwlMessage 注释）
    public const byte ErrorNoShop = 1, ErrorFull = 2, ErrorMismatch = 3, ErrorNotAllowed = 17, ErrorMaintenance = 18;

    // ItemId.getOwlItems()：搜索种类不足 5 时的固定推荐位
    public static readonly int[] DefaultSuggestions =
    {
        1082002, 2070005, 2070006, 1022047, 1102041, 2044705,
        2340000, 2340001, 2040017, 1092030, 2040804
    };

    // GameServer 启动时注入（同 NativeMerchantService.MarketProvider）。
    public static Func<Server.Bot.MarketShopService> MarketProvider;

    // 源端 World.owlSearched：物品 id → 被搜索次数。进程级，重启即空。
    static readonly Dictionary<int, int> searched = new Dictionary<int, int>();
    public static void Reset() => searched.Clear();

    /// <summary>回归断言用：当前热度表快照。</summary>
    public static Dictionary<int, int> Searched => new Dictionary<int, int>(searched);

    public static OwlResponse Handle(DbStore db, OwlRequest r)
    {
        var c = CharactersDao.FindById(db, r.characterId);
        if (c == null) return new OwlResponse { ok = false, error = "角色不存在" };
        switch (r.operation)
        {
            case OwlOperation.Open: return Open(db);
            case OwlOperation.Search: return Search(db, r.itemId);
            case OwlOperation.Warp: return Warp(c, r.ownerId, r.mapId, r.itemId);
        }
        return new OwlResponse { ok = false, error = "未知操作" };
    }

    // UseOwlOfMinervaHandler.handlePacket：打开搜索框时给推荐位。
    static OwlResponse Open(DbStore db)
    {
        var list = new List<int>();
        if (!Enforced(db))
        {
            if (searched.Count < MinSearchedKinds)
            {
                foreach (int id in DefaultSuggestions) list.Add(id);
            }
            else
            {
                // PriorityQueue 按次数降序弹前 10；同次数时源端顺序不稳定，这里补 itemId 升序保证可复现。
                var pairs = new List<KeyValuePair<int, int>>(searched);
                pairs.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
                for (int i = 0; i < Math.Min(pairs.Count, TopSearched); i++) list.Add(pairs[i].Key);
            }
        }
        return new OwlResponse { suggestions = list, hits = new List<OwlHit>() };
    }

    // UseCashItemHandler itemType == 523 的主体。
    static OwlResponse Search(DbStore db, int itemId)
    {
        if (itemId <= 0) return new OwlResponse { ok = false, error = "请先选择要搜索的道具" };
        if (!Enforced(db))
        {
            searched.TryGetValue(itemId, out int count);
            searched[itemId] = count + 1;
        }
        var hits = Scan(itemId);
        // 源端只在 hmsAvailable 非空时 remove(c, position, itemId)。
        return new OwlResponse { hits = hits, charged = hits.Count > 0 };
    }

    /// <summary>World.getAvailableItemBundles：扫全部在售店铺里该物品的所有货架。</summary>
    public static List<OwlHit> Scan(int itemId)
    {
        var hits = new List<OwlHit>();
        var service = MarketProvider?.Invoke();
        if (service == null) return hits;
        foreach (var shop in service.AllOpen())
        {
            if (shop.items == null) continue;
            foreach (var stock in shop.items)
            {
                if (stock == null || stock.item == null || !stock.exists || stock.bundles <= 0) continue;
                if (stock.item.itemId != itemId) continue;
                hits.Add(new OwlHit
                {
                    ownerId = shop.ownerId, ownerName = shop.ownerName, shopName = shop.description,
                    mapId = shop.mapId, bundles = stock.bundles, quantity = stock.item.quantity,
                    price = stock.price, channel = unchecked((byte)Math.Max(0, shop.channel - 1)),
                    equip = stock.item.isEquip, item = stock.item.Copy()
                });
            }
        }
        // getAvailableItemBundles：按单价升序 → 截到 200 条。
        hits.Sort((a, b) => a.price != b.price ? a.price.CompareTo(b.price) : a.ownerId.CompareTo(b.ownerId));
        if (hits.Count > MaxResults) hits.RemoveRange(MaxResults, hits.Count - MaxResults);
        return hits;
    }

    // OwlWarpHandler.handlePacket：从搜索结果跳到店铺。单机只保留消息码 1 / 3 / 18
    // （2 = 满员、17 = 禁入 需要第二个真人顾客，恒不触发）。
    static OwlResponse Warp(CharRecord c, int ownerId, int mapId, int itemId)
    {
        if (ownerId == c.id)
            return new OwlResponse { ok = false, code = ErrorNoShop, error = "不能访问自己的店铺" };
        var shop = MarketProvider?.Invoke()?.Get(ownerId);
        if (shop == null)
            return new OwlResponse { ok = false, code = ErrorNoShop, error = "找不到这家店铺了" };
        if (!shop.open)
            return new OwlResponse { ok = false, code = ErrorMaintenance, error = "店主正在整理店铺，请稍后再试" };
        bool has = false;
        if (shop.items != null)
            foreach (var stock in shop.items)
                if (stock != null && stock.item != null && stock.exists && stock.bundles > 0 && stock.item.itemId == itemId)
                { has = true; break; }
        if (shop.mapId != mapId || !has)
            return new OwlResponse { ok = false, code = ErrorMismatch, error = "这家店铺已经不再出售该道具" };
        return new OwlResponse { map = shop.mapId };
    }

    static bool Enforced(DbStore db) => GameConfigDao.GetBool(db, "use_enforce_item_suggestion", false);
}
