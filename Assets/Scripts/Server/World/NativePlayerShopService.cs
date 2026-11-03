using System;
using System.Collections.Generic;

// 个人商店（PlayerShop，514xxxx 营业执照）—— 对照 gms-server：
//   server/maps/PlayerShop.java、server/maps/PlayerShopItem.java
//   net/server/channel/handlers/PlayerInteractionHandler.java（CREATE 4/5、ADD_ITEM、REMOVE_ITEM、
//       OPEN_STORE、BUY、BAN_PLAYER、CHAT、EXIT）
//   client/Character.java closePlayerShop()（关店把货退回背包）
//   constants/inventory/ItemConstants.java（isPlayerShop: itemId / 10000 == 514）
//
// 与雇佣商店的三处关键差异（别照抄 NativeMerchantService）：
//   1. **纯内存对象，不落库**：源端 PlayerShop 没有数据库表，掉线 / 换图 / 关窗即关店，
//      剩下的货直接回背包（不进 Fredrick 寄存，没有过期与衰减）。
//   2. 上架不消耗许可；许可在「开张营业」（OPEN_STORE）时才按
//      use_erase_permit_on_open_shop 扣 1 个（默认为 true，扣失败静默忽略）。
//   3. 成交金额**直接进店主身上的金币**（`owner.gainMeso`），不走 MerchantMesos；
//      全部卖光时自动关店（boughtNumber == items.size()）。
//
// 单机语义：没有第二个真人顾客，访客/聊天/黑名单只为窗口回显保留。买家是假人
// （Server.Bot 的市场系统），走 Mirror 挂进 MarketShopService 的 playerSell 回调。
public enum PlayerShopOperation
{
    Snapshot, Create, AddItem, TakeItemBack, Open, Close, Ban, Chat
}

public sealed class PlayerShopRequest
{
    public int characterId;
    public PlayerShopOperation operation;
    public int x, y;                         // 玩家脚点（WZ 口径），canPlaceStore 用
    public int permitId;                     // Create：514xxxx 营业执照
    public string description = "";
    public int inventoryType, itemPosition;  // AddItem：背包类型 + 格号
    public int perBundle, bundles, price;    // AddItem：每份数量 / 份数 / 每份单价
    public int slot;                         // TakeItemBack：货架下标（源端就是列表下标）
    public string name = "";                 // Ban
    public string text = "";                 // Chat
}

public sealed class PlayerShopSlotData
{
    public int slot, bundles, price;
    public bool exists;
    public InventoryEntry item;
}

public sealed class PlayerShopSoldData
{
    public string buyer = "";
    public int itemId, quantity;
    public long meso;
}

public sealed class PlayerShopInfo
{
    public int ownerId, mapId, permitId;
    public string ownerName = "", description = "";
    public bool open;
    public int boughtNumber;
    public readonly List<PlayerShopSlotData> items = new List<PlayerShopSlotData>();
    public readonly List<PlayerShopSoldData> sold = new List<PlayerShopSoldData>();
    public readonly List<string> banned = new List<string>();
    public readonly List<string> chat = new List<string>();
}

public sealed class PlayerShopResponse
{
    public bool ok = true;
    public string error = "";
    public byte code;                     // MiniRoomError（PacketCreator.getMiniRoomError）
    public PlayerShopInfo shop;
    public List<InventoryEntry> inventory;
    public long meso = -1;
}

public static class NativePlayerShopService
{
    // PlayerShop.addItem / PlayerInteractionHandler ADD_ITEM
    public const int MaxSlots = 16;
    public const int MaxTotalPerSlot = 2000;
    public const int MaxVisitors = 3;
    public const int MaxChatLog = 25;

    // MiniRoomError 里本功能用到的几个
    public const byte ErrorNotAlive = 4, ErrorInEvent = 5, ErrorNoPermit = 6,
        ErrorNearPortal = 10, ErrorBlocked = 13, ErrorNotFreeMarket = 15;

    // ItemConstants：flag 位
    const int FlagUntradeable = 0x08, FlagKarmaUse = 0x02, FlagKarmaEqp = 0x10;

    sealed class Slot
    {
        public InventoryEntry item;
        public int bundles, price;
        public bool exists = true;
    }

    sealed class Shop
    {
        public int ownerId, mapId, permitId;
        public string ownerName = "", description = "";
        public bool open;
        public int boughtNumber;
        public readonly List<Slot> items = new List<Slot>();
        public readonly List<PlayerShopSoldData> sold = new List<PlayerShopSoldData>();
        public readonly List<string> banned = new List<string>();
        public readonly List<string> chat = new List<string>();
    }

    // 源端店铺对象挂在 MapleMap 上，随角色生命周期存在 → 这里用内存表，不建表。
    static readonly Dictionary<int, Shop> shops = new Dictionary<int, Shop>();

    public static bool IsPlayerShopPermit(int itemId) => itemId / 10000 == 514;

    // GameConstants.isFreeMarketRoom：910xxxxxx 且大于自由市场入口图。
    static bool FreeMarket(int mapId) => mapId / 1000000 == 910 && mapId > 910000000;

    /// <summary>回归 / 换角色时清空内存店铺表（源端没有持久化，重启即空）。</summary>
    public static void Reset() => shops.Clear();

    public static bool Has(int characterId) => shops.ContainsKey(characterId);

    // ————————————————— 对外入口 —————————————————

    public static Func<Server.Bot.MarketShopService> MarketProvider;

    public static PlayerShopResponse Handle(DbStore db, PlayerShopRequest r)
    {
        var c = CharactersDao.FindById(db, r.characterId);
        if (c == null) return new PlayerShopResponse { ok = false, error = "角色不存在" };

        // changeMap → closePlayerInteractions → closePlayerShop：换图即关店并把货退回背包。
        if (shops.TryGetValue((int)c.id, out var stale) && stale.mapId != c.map)
        {
            if (ReturnItems(db, c, stale) == 0) shops.Remove((int)c.id);
            // 装不下的留在店里（源端是掉在地上），并把店铺挂到新地图，免得每次进图都重试
            else { stale.open = false; stale.mapId = c.map; }
            db.Save();
        }

        var res = Dispatch(db, c, r);
        switch (r.operation)
        {
            case PlayerShopOperation.Create:
            case PlayerShopOperation.AddItem:
            case PlayerShopOperation.TakeItemBack:
            case PlayerShopOperation.Open:
            case PlayerShopOperation.Close:
            case PlayerShopOperation.Ban:
                SyncMarket(db, (int)c.id);
                break;
        }
        return res;
    }

    static PlayerShopResponse Dispatch(DbStore db, CharRecord c, PlayerShopRequest r)
    {
        switch (r.operation)
        {
            case PlayerShopOperation.Create: return Create(db, c, r);
            case PlayerShopOperation.AddItem: return AddItem(db, c, r);
            case PlayerShopOperation.TakeItemBack: return TakeItemBack(db, c, r);
            case PlayerShopOperation.Open: return Open(db, c);
            case PlayerShopOperation.Close: return CloseRequest(db, c);
            case PlayerShopOperation.Ban: return Ban(db, c, r.name);
            case PlayerShopOperation.Chat: return Chat(db, c, r.text);
            default: return Snapshot(db, c);
        }
    }

    // ————————————————— 开店 —————————————————

    // PlayerInteractionHandler CREATE(4/5)：自由市场 + 存活 + 不在事件副本 + canPlaceStore +
    // CASH 背包里有这个营业执照（**不消耗**）。
    static PlayerShopResponse Create(DbStore db, CharRecord c, PlayerShopRequest r)
    {
        if (!FreeMarket(c.map))
            return Fail(ErrorNotFreeMarket, "只能在自由市场里开个人商店");
        if (shops.ContainsKey((int)c.id))
            return Fail(ErrorBlocked, "你已经有店铺了");
        // 存活 / 棋盘 / 事件副本：establishMiniroomStatus（单机没有棋盘与副本实例，只留接口位）
        if (!IsPlayerShopPermit(r.permitId))
            return Fail(ErrorNoPermit, "需要营业执照才能开店");
        if (InventoryDao.CountOf(db, (int)c.id, r.permitId) < 1)
            return Fail(ErrorNoPermit, "背包里没有这个营业执照");
        var place = CanPlaceStore(db, c, r.x, r.y);
        if (place != 0) return Fail(place, place == ErrorNearPortal ? "离传送门太近，换个地方摆摊" : "这里已经有店铺了");

        var shop = new Shop {
            ownerId = (int)c.id, mapId = c.map, permitId = r.permitId,
            ownerName = c.name, description = Trim(r.description, 40) };
        shops[shop.ownerId] = shop;
        return Snapshot(db, c);
    }

    // canPlaceStore：23000 范围内不能有别的摊位/雇佣商店；最近的传送门 120 内不行。
    static byte CanPlaceStore(DbStore db, CharRecord c, int x, int y)
    {
        foreach (var other in shops.Values)
            if (other.ownerId != (int)c.id && other.mapId == c.map) return ErrorBlocked;
        foreach (var row in db.Table("hiredmerchants").rows)
            if (DbStore.Int(row, "mapid") == c.map) return ErrorBlocked;

        var map = Server.Field.MapService.Get(c.map);
        if (map == null) return 0;
        double best = double.MaxValue;
        int bx = 0, by = 0;
        foreach (var portal in map.portals)
        {
            // MapleMap.findClosestTeleportPortal：type == TELEPORT_PORTAL(1) 且 targetMap 有效
            if (portal.type != 1 || portal.targetMap <= 0) continue;
            double dx = portal.x - x, dy = portal.y - y, distance = dx * dx + dy * dy;
            if (distance < best) { best = distance; bx = portal.x; by = portal.y; }
        }
        if (best == double.MaxValue) return 0;
        double ddx = bx - x, ddy = by - y;
        return Math.Sqrt(ddx * ddx + ddy * ddy) < 120.0 ? ErrorNearPortal : (byte)0;
    }

    // PlayerInteractionHandler OPEN_STORE(0x0B)：开张营业。
    // use_erase_permit_on_open_shop 为真时消耗 1 个营业执照（removeById 失败静默忽略）。
    static PlayerShopResponse Open(DbStore db, CharRecord c)
    {
        var shop = ShopOf(c);
        if (shop == null) return Fail(ErrorBlocked, "还没有开店");
        if (GameConfigDao.GetBool(db, "use_erase_permit_on_open_shop", true))
            InventoryDao.ConsumeItem(db, (int)c.id, shop.permitId, 1);
        shop.open = true;
        db.Save();
        return Snapshot(db, c);
    }

    // ————————————————— 上架 / 取回 —————————————————

    // PlayerInteractionHandler ADD_ITEM(0x16) / PUT_ITEM(0x21)
    static PlayerShopResponse AddItem(DbStore db, CharRecord c, PlayerShopRequest r)
    {
        var shop = ShopOf(c);
        if (shop == null) return Fail(ErrorBlocked, "还没有开店");
        if (shop.open) return Fail(0, "营业中不能上架道具");
        if (shop.items.Count >= MaxSlots) return Fail(0, "货架已满（最多 16 格）");

        int cid = (int)c.id;
        var row = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid
            && DbStore.Int(x, "inventorytype") == r.inventoryType
            && DbStore.Int(x, "position") == r.itemPosition
            && InventoryDao.IsBagPosition(DbStore.Int(x, "position")));
        if (row == null) return Fail(0, "背包里没有这件道具");

        var item = CharService.ToInventoryEntry(db, row);
        if (Untradeable(db, item)) return Fail(0, "这件道具无法交易");
        if (Unmerchable(db, item.itemId))
            return Fail(0, item.itemId / 1000 == 5000 ? "宠物不能摆摊" : "这件现金道具不能摆摊");

        bool rechargeable = Server.Bot.Trade.TradeItemRules.Rechargeable(item.itemId);
        int perBundle = r.perBundle, bundles = r.bundles;
        if (rechargeable) { perBundle = 1; bundles = 1; }
        else if (item.quantity < perBundle * bundles) return Fail(0, "数量不足");
        if (perBundle <= 0 || perBundle * bundles > MaxTotalPerSlot || bundles <= 0 || r.price <= 0)
            return Fail(0, "上架参数不合法（每格最多 2000 个）");

        var sell = item.Copy();
        sell.quantity = rechargeable ? item.quantity : perBundle;
        shop.items.Add(new Slot { item = sell, bundles = bundles, price = r.price });
        int take = rechargeable ? item.quantity : perBundle * bundles;
        NativeMerchantService.ConsumeFromInventory(db, row, item, take);
        db.Save();
        return Snapshot(db, c);
    }

    // PlayerInteractionHandler REMOVE_ITEM(0x1B) / PlayerShop.takeItemBack：只有关店状态能取回，
    // 数量 = 每份数量 × 份数，取回后这一格**从列表里删掉**（不是留空位）。
    static PlayerShopResponse TakeItemBack(DbStore db, CharRecord c, PlayerShopRequest r)
    {
        var shop = ShopOf(c);
        if (shop == null) return Fail(ErrorBlocked, "还没有开店");
        if (shop.open) return Fail(0, "营业中不能取回道具");
        if (r.slot < 0 || r.slot >= shop.items.Count) return Fail(0, "这个格子是空的");
        var slot = shop.items[r.slot];
        if (slot.exists && slot.bundles > 0 && slot.item != null)
        {
            var give = slot.item.Copy();
            give.quantity = slot.item.quantity * slot.bundles;
            if (!Give(db, c, give)) return Fail(0, "背包没有空位放回这件道具");
        }
        shop.items.RemoveAt(r.slot);
        db.Save();
        return Snapshot(db, c);
    }

    // ————————————————— 关店 —————————————————

    // PlayerInteractionHandler EXIT(0x0A) → Character.closePlayerShop()
    static PlayerShopResponse CloseRequest(DbStore db, CharRecord c)
    {
        var shop = ShopOf(c);
        if (shop == null) return Fail(ErrorBlocked, "还没有开店");
        int kept = ReturnItems(db, c, shop);
        if (kept > 0)
            return Fail(0, "背包没有空位，还有 " + kept + " 件留在店里，清出空间后再关店");
        shops.Remove(shop.ownerId);
        db.Save();
        var res = new PlayerShopResponse { ok = true, inventory = CharService.Load(db, (int)c.id).inventory, meso = c.meso };
        res.shop = null;
        return res;
    }

    // 关店把货退回背包（源端 closePlayerShop 的 bundles>=2 / bundles==1 && isExist 两分支）。
    // 返回「装不下、还留在店里」的件数。与源端的唯一差异：源端 addFromDrop 装不下就把道具掉在地上，
    // 这里留在店里等玩家清空背包再取，避免静默丢货。
    static int ReturnItems(DbStore db, CharRecord c, Shop shop)
    {
        int kept = 0;
        for (int index = shop.items.Count - 1; index >= 0; index--)
        {
            var slot = shop.items[index];
            if (slot.bundles < 2 && !slot.exists) { shop.items.RemoveAt(index); continue; }   // 已卖光
            var give = slot.item.Copy();
            give.quantity = slot.bundles >= 2 ? slot.item.quantity * slot.bundles : slot.item.quantity;
            if (!Give(db, c, give)) { kept++; continue; }
            shop.items.RemoveAt(index);
        }
        return kept;
    }

    /// <summary>掉线 / 换角色：源端 closePlayerInteractions 会把货退回背包。</summary>
    public static void Logout(DbStore db, int characterId)
    {
        if (!shops.TryGetValue(characterId, out var shop)) return;
        var c = CharactersDao.FindById(db, characterId);
        if (c == null) { shops.Remove(characterId); return; }
        if (ReturnItems(db, c, shop) == 0) shops.Remove(characterId);
        db.Save();
    }

    // ————————————————— 黑名单 / 聊天 —————————————————

    static PlayerShopResponse Ban(DbStore db, CharRecord c, string name)
    {
        var shop = ShopOf(c);
        if (shop == null) return Fail(ErrorBlocked, "还没有开店");
        name = (name ?? "").Trim();
        if (name.Length == 0) return Fail(0, "名字不能为空");
        if (!shop.banned.Contains(name)) shop.banned.Add(name);
        shop.chat.Add(shop.ownerName + " 把 " + name + " 请出了店铺");
        TrimChat(shop);
        return Snapshot(db, c);
    }

    static PlayerShopResponse Chat(DbStore db, CharRecord c, string text)
    {
        var shop = ShopOf(c);
        if (shop == null) return Fail(ErrorBlocked, "还没有开店");
        text = (text ?? "").Trim();
        if (text.Length == 0) return Fail(0, "内容不能为空");
        shop.chat.Add(shop.ownerName + " : " + Trim(text, 80));
        TrimChat(shop);
        return Snapshot(db, c);
    }

    static void TrimChat(Shop shop)
    {
        while (shop.chat.Count > MaxChatLog) shop.chat.RemoveAt(0);
    }

    // ————————————————— 假人成交（PlayerShop.buy） —————————————————

    /// <summary>
    /// 假人顾客买单：假人的钱由 MarketShopService 那边先扣好，这里只处理店内存货、
    /// 成交记录与店主金币，返回店主实收（含手续费扣除）；0 表示这单不成立。
    /// </summary>
    public static long BotBuy(DbStore db, int ownerId, int slot, int quantity, string buyer)
    {
        if (!shops.TryGetValue(ownerId, out var shop) || !shop.open) return 0;
        if (slot < 0 || slot >= shop.items.Count) return 0;
        var entry = shop.items[slot];
        if (quantity < 1 || !entry.exists || entry.bundles < quantity) return 0;
        var owner = CharactersDao.FindById(db, ownerId);
        if (owner == null) return 0;

        int price = (int)Math.Min((float)entry.price * quantity, int.MaxValue);
        // Character.canHoldMeso 用的是「扣手续费之前」的毛价
        if (owner.meso + price > int.MaxValue) return 0;
        long proceeds = price - Server.Bot.Trade.BotTradeService.Fee(price);

        owner.meso += proceeds;
        CharactersDao.Save(db, owner);
        shop.sold.Add(new PlayerShopSoldData {
            buyer = buyer ?? "", itemId = entry.item.itemId, quantity = entry.item.quantity * quantity, meso = proceeds });
        entry.bundles -= quantity;
        if (entry.bundles < 1)
        {
            entry.exists = false;
            if (++shop.boughtNumber >= shop.items.Count)
            {
                // 全部卖光自动关店（源端 boughtnumber == items.size()）
                shop.open = false;
                shops.Remove(ownerId);
            }
        }
        db.Save();
        return proceeds;
    }

    // ————————————————— 窗口快照 —————————————————

    static PlayerShopResponse Snapshot(DbStore db, CharRecord c)
    {
        var shop = ShopOf(c);
        var res = new PlayerShopResponse {
            inventory = CharService.Load(db, (int)c.id).inventory, meso = c.meso };
        if (shop == null) return res;
        var info = new PlayerShopInfo {
            ownerId = shop.ownerId, mapId = shop.mapId, permitId = shop.permitId,
            ownerName = shop.ownerName, description = shop.description, open = shop.open,
            boughtNumber = shop.boughtNumber };
        for (int index = 0; index < shop.items.Count; index++)
        {
            var slot = shop.items[index];
            info.items.Add(new PlayerShopSlotData {
                slot = index, bundles = slot.bundles, price = slot.price, exists = slot.exists,
                item = slot.item == null ? null : slot.item.Copy() });
        }
        foreach (var line in shop.sold) info.sold.Add(line);
        foreach (var name in shop.banned) info.banned.Add(name);
        foreach (var line in shop.chat) info.chat.Add(line);
        res.shop = info;
        return res;
    }

    // ————————————————— 假人市场镜像 —————————————————

    // 把店主自己的摊挂进 MarketShopService，市场里的假人就会来买（与雇佣商店同一套挂载点）。
    static void SyncMarket(DbStore db, int ownerId)
    {
        var service = MarketProvider?.Invoke();
        if (service == null) return;
        if (!shops.TryGetValue(ownerId, out var shop) || !shop.open || shop.items.Count == 0)
        { service.RemovePlayerStore(ownerId); return; }
        var client = Server.Bot.BotClient.GetBotClient();
        var mirror = new Server.Bot.MarketShop {
            ownerId = ownerId, ownerName = shop.ownerName, description = shop.description,
            mapId = shop.mapId, world = client.world, channel = client.channel, permitId = shop.permitId,
            open = true, playerStore = true };
        for (int index = 0; index < shop.items.Count; index++)
        {
            var slot = shop.items[index];
            if (slot.item == null || slot.bundles < 1 || !slot.exists) continue;
            mirror.items.Add(new Server.Bot.MarketShopStock {
                item = slot.item.Copy(), bundles = slot.bundles, price = slot.price,
                exists = true, merchantSlot = index });
        }
        if (mirror.items.Count == 0) { service.RemovePlayerStore(ownerId); return; }
        mirror.playerSell = (slot, quantity) => BotBuy(db, ownerId, slot, quantity, "假人");
        service.UpsertPlayerStore(mirror);
    }

    // ————————————————— 工具 —————————————————

    static Shop ShopOf(CharRecord c) => shops.TryGetValue((int)c.id, out var shop) ? shop : null;

    static PlayerShopResponse Fail(byte code, string error)
        => new PlayerShopResponse { ok = false, code = code, error = error };

    static string Trim(string value, int max)
    {
        value = (value ?? "").Trim();
        return value.Length <= max ? value : value.Substring(0, max);
    }

    // Item.isUntradeable()：UNTRADEABLE 位，或「禁止丢弃且没有因果剪刀标记」
    static bool Untradeable(DbStore db, InventoryEntry item)
    {
        if(NativePotentialService.BlocksTransfer(db,item))return true;
        int karma = item.isEquip ? FlagKarmaEqp : FlagKarmaUse;
        if ((item.flag & FlagUntradeable) == FlagUntradeable) return true;
        var info = Server.Bot.BotItemCatalog.Shared.Info(item.itemId);
        bool restricted = Server.Bot.BotItemCatalog.Int(info, "tradeBlock") == 1
            || Server.Bot.BotItemCatalog.Int(info, "accountSharable") == 1
            || Server.Bot.BotItemCatalog.Int(info, "quest") == 1;
        return restricted && (item.flag & karma) != karma;
    }

    // ItemInformationProvider.isUnmerchable
    static bool Unmerchable(DbStore db, int itemId)
    {
        bool cash = Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(itemId), "cash") == 1;
        bool pet = itemId / 1000 == 5000;
        return GameConfigDao.GetBool(db, "use_enforce_unmerchable_cash", false) && cash
            || GameConfigDao.GetBool(db, "use_enforce_unmerchable_pet", false) && pet;
    }

    // 往背包塞一件（InventoryManipulator.addFromDrop 的单机版：找空位写行）。
    static bool Give(DbStore db, CharRecord c, InventoryEntry item)
    {
        if (item == null || item.quantity <= 0) return true;
        var full = CharService.Load(db, (int)c.id);
        var actor = new Server.Bot.BotActor { id = (int)c.id, job = c.job, character = full };
        int position = Server.Bot.Trade.TradeItemRules.FreeSlot(actor, full.inventory, item.inventoryType);
        if (position <= 0) return false;
        NativeMerchantService.WriteEntry(db, (int)c.id, item, position, item.quantity);
        full.inventory.Add(new InventoryEntry {
            itemId = item.itemId, inventoryType = item.inventoryType, position = position, quantity = item.quantity });
        return true;
    }
}
