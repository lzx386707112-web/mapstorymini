using System;
using System.Collections.Generic;

// 雇佣商店（HiredMerchant）—— 对照 gms-server：
//   server/maps/HiredMerchant.java（visitShop / addItem / buy / takeItemBack / withdrawMesos /
//                                  clearInexistentItems / closeShop / forceClose / saveItems）
//   net/server/channel/handlers/PlayerInteractionHandler.java:236-815（CREATE / ADD_ITEM /
//                                  TAKE_ITEM_BACK / MERCHANT_MESO / MERCHANT_ORGANIZE /
//                                  CLOSE_MERCHANT / MAINTENANCE_OFF / BUY / REMOVE_ITEM）
//   net/server/channel/handlers/HiredMerchantRequest.java（自由市场按商店盒）
//   client/processor/npc/FredrickProcessor.java（寄存取回 + 每日提醒 + 100 天清理）
//   net/server/world/World.java:1653 runHiredMerchantSchedule（hired_merchants_exist_time 小时级计时）
//   constants/inventory/ItemConstants.java（isHiredMerchant: itemId/10000 == 503）
//
// 单机语义：没有其他真人顾客，「访客 / 聊天 / 黑名单」只在有假人买家时才有内容。
// 假人通过 Server.Bot 的市场系统进店买东西（NativeMerchantMirror 把玩家商店挂进市场），
// 成交金额按源端规则扣手续费后进 characters.MerchantMesos。
public enum MerchantOperation
{
    Box, Create, AddItem, TakeItemBack, RemoveItem, Buy, WithdrawMesos, Organize,
    Close, MaintenanceOff, SetOpen, AddBlacklist, RemoveBlacklist, Retrieve, Tick, Snapshot
}

public sealed class MerchantRequest
{
    public int characterId;
    public MerchantOperation operation;
    public int slot, quantity, bundles, price, inventoryType, itemPosition;
    public string description = "", blacklistName = "";
}

public sealed class MerchantSlotData
{
    public int slot, bundles, price;
    public InventoryEntry item;
}

public sealed class MerchantSnapshot
{
    public int ownerId, mapId, permitId;
    public string ownerName = "", description = "";
    public bool open, published, visitor, mine;
    public long merchantMeso;
    public long startedAt;
    public readonly List<MerchantSlotData> items = new List<MerchantSlotData>();
    public readonly List<string> blacklist = new List<string>();
}

public sealed class MerchantResponse
{
    public bool ok = true;
    public string error = "";
    public byte code;                 // MiniRoomError / Fredrick 返回码
    public bool box;                  // 自由市场商店盒是否可开
    public MerchantSnapshot shop;
    public List<InventoryEntry> inventory;
    public long meso = -1, merchantMeso = -1;
}

public static class NativeMerchantService
{
    // HiredMerchant.addItem 的上限
    public const int MaxSlots = 16;
    // PlayerInteractionHandler：perBundle * bundles > 2000 直接拒绝
    public const int MaxTotalPerSlot = 2000;
    // FredrickProcessor.dailyReminders
    static readonly int[] DailyReminders = { 2, 5, 10, 15, 30, 60, 90, int.MaxValue };
    const int StorageCharacterId = -2;    // 店内存货行的「无主」标记（快递用 -1）

    // MiniRoomError 里本功能用到的几个
    public const byte ErrorNotFreeMarket = 15, ErrorNoPermit = 6, ErrorAlreadyOpen = 13, ErrorClosed = 18;
    // Fredrick 返回码
    public const byte FredrickNoSpace = 0x20, FredrickNotEnoughMeso = 0x21, FredrickUnique = 0x22, FredrickSuccess = 0x1E, FredrickMesoFull = 0x1F;

    static bool FreeMarket(int mapId) => mapId / 1000000 == 910 && mapId > 910000000;

    public static bool IsHiredMerchantPermit(int itemId) => itemId / 10000 == 503;

    // ————————————————— 对外入口 —————————————————

    public static MerchantResponse Handle(DbStore db, MerchantRequest r)
    {
        var c = CharactersDao.FindById(db, r.characterId);
        if (c == null) return new MerchantResponse { ok = false, error = "角色不存在" };
        var res = Dispatch(db, c, r);
        // 会改动店铺状态的操作，同步给假人市场；纯读操作（Box/Snapshot）不动。
        switch (r.operation)
        {
            case MerchantOperation.Create:
            case MerchantOperation.AddItem:
            case MerchantOperation.TakeItemBack:
            case MerchantOperation.RemoveItem:
            case MerchantOperation.Organize:
            case MerchantOperation.Close:
            case MerchantOperation.MaintenanceOff:
            case MerchantOperation.SetOpen:
            case MerchantOperation.Retrieve:
            case MerchantOperation.Tick:
                SyncMarket(db, (int)c.id);
                break;
        }
        return res;
    }

    static MerchantResponse Dispatch(DbStore db, CharRecord c, MerchantRequest r)
    {
        switch (r.operation)
        {
            case MerchantOperation.Box: return Box(db, c);
            case MerchantOperation.Create: return Create(db, c, r);
            case MerchantOperation.AddItem: return AddItem(db, c, r);
            case MerchantOperation.TakeItemBack:
            case MerchantOperation.RemoveItem: return TakeItemBack(db, c, r);
            case MerchantOperation.Buy: return new MerchantResponse { ok = false, error = "单机没有其他真人顾客，店内商品由假人购买" };
            case MerchantOperation.WithdrawMesos: return WithdrawMesos(db, c);
            case MerchantOperation.Organize: return Organize(db, c);
            case MerchantOperation.Close: return Close(db, c, false);
            case MerchantOperation.MaintenanceOff: return Maintenance(db, c);
            case MerchantOperation.SetOpen: return SetOpen(db, c, r.quantity != 0);
            case MerchantOperation.AddBlacklist: return Blacklist(db, c, r.blacklistName, true);
            case MerchantOperation.RemoveBlacklist: return Blacklist(db, c, r.blacklistName, false);
            case MerchantOperation.Retrieve: return Retrieve(db, c);
            case MerchantOperation.Tick: return Tick(db, c);
            default: return Snapshot(db, c);
        }
    }

    // 市场系统的挂载点：GameServer 启动时把 BotRuntime 的 MarketShopService 塞进来，
    // 之后每次调用后把「玩家本人的雇佣商店」同步成假人可访问的店铺。
    public static Func<Server.Bot.MarketShopService> MarketProvider;

    static void SyncMarket(DbStore db, int ownerId)
    {
        var service = MarketProvider?.Invoke();
        if (service == null) return;
        var shop = Load(db, ownerId);
        if (shop == null || !shop.open) { service.RemovePlayerStore(ownerId); return; }
        var client = Server.Bot.BotClient.GetBotClient();
        var mirror = new Server.Bot.MarketShop {
            ownerId = ownerId, ownerName = shop.ownerName, description = shop.description,
            mapId = shop.mapId, world = client.world, channel = client.channel, permitId = shop.permitId,
            open = true, playerStore = true };
        foreach (var slot in shop.items)
        {
            if (slot.item == null || slot.bundles < 1) continue;
            mirror.items.Add(new Server.Bot.MarketShopStock {
                item = slot.item.Copy(), bundles = slot.bundles, price = slot.price,
                exists = true, merchantSlot = slot.slot });
        }
        if (mirror.items.Count == 0) { service.RemovePlayerStore(ownerId); return; }
        mirror.playerSell = (merchantSlot, quantity) =>
            BotPurchase(db, ownerId, merchantSlot, quantity, "假人", out int gross, out _)
                ? gross - Server.Bot.Trade.BotTradeService.Fee(gross) : 0;
        service.UpsertPlayerStore(mirror);
    }

    // ————————————————— 商店盒（自由市场） —————————————————

    // HiredMerchantRequest：自由市场里点「开商店」→ 无寄存开盒，有寄存先去 Fredrick。
    static MerchantResponse Box(DbStore db, CharRecord c)
    {
        int map = c.map;
        if (!FreeMarket(map))
            return new MerchantResponse { ok = false, code = ErrorNotFreeMarket, error = "不能在这里开雇佣商店" };
        if (HasMerchantFlag(db, c))
            return new MerchantResponse { ok = false, error = "你已经有店铺了" };
        bool stored = StoredItemRows(db, (int)c.id).Count > 0;
        if (!stored && DbStore.Long(CharactersRow(db, c), "MerchantMesos", 0) == 0)
            return new MerchantResponse { ok = true, box = true };
        return new MerchantResponse { ok = true, box = false, error = "店铺寄存还有东西，请到自由市场入口找弗利德里取回" };
    }

    static MerchantResponse Create(DbStore db, CharRecord c, MerchantRequest r)
    {
        if (!FreeMarket(c.map))
            return new MerchantResponse { ok = false, code = ErrorNotFreeMarket, error = "不能在这里开雇佣商店" };
        if (HasMerchantFlag(db, c) || ShopRow(db, (int)c.id) != null)
            return new MerchantResponse { ok = false, code = ErrorAlreadyOpen, error = "你已经有店铺了" };
        if (!IsHiredMerchantPermit(r.price > 0 ? r.price : r.inventoryType))
            return new MerchantResponse { ok = false, code = ErrorNoPermit, error = "需要雇佣商人许可" };
        int permit = r.price > 0 ? r.price : r.inventoryType;
        if (InventoryDao.CountOf(db, (int)c.id, permit) < 1)
            return new MerchantResponse { ok = false, code = ErrorNoPermit, error = "背包里没有这个许可" };
        // 源端不消耗许可（PlayerInteractionHandler 只做 countById >= 1 校验）。
        db.Table("hiredmerchants").Insert(new DbRow {
            ["ownerid"] = c.id, ["mapid"] = (long)c.map, ["x"] = 0L, ["y"] = 0L, ["permitid"] = (long)permit,
            ["description"] = r.description ?? "", ["open"] = 0L, ["published"] = 0L,
            ["starttime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        SetMerchantFlag(db, c, true);
        db.Save();
        return Snapshot(db, c);
    }

    static MerchantResponse Snapshot(DbStore db, CharRecord c)
    {
        var shop = Load(db, (int)c.id);
        var res = new MerchantResponse { shop = shop, inventory = CharService.Load(db, (int)c.id).inventory,
            meso = c.meso, merchantMeso = MerchantMeso(db, c) };
        return res;
    }

    // ————————————————— 摆货 / 收货 —————————————————

    // PlayerInteractionHandler ADD_ITEM：营业中不能上架；现金道具上架过就不能再摆。
    static MerchantResponse AddItem(DbStore db, CharRecord c, MerchantRequest r)
    {
        var shopRow = ShopRow(db, (int)c.id);
        if (shopRow == null) return new MerchantResponse { ok = false, error = "还没有开店" };
        int cid = (int)c.id;
        var items = db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == cid);
        if (DbStore.Int(shopRow, "open") == 1)
            return new MerchantResponse { ok = false, error = "营业中不能上架道具" };
        if (DbStore.Int(shopRow, "published") == 1 && r.inventoryType == 5)
            return new MerchantResponse { ok = false, error = "已经开过张的商店不能再上架现金道具" };
        if (items.Count >= MaxSlots) return new MerchantResponse { ok = false, error = "货架已满（最多 16 格）" };

        var row = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid
            && DbStore.Int(x, "inventorytype") == r.inventoryType && DbStore.Int(x, "position") == r.itemPosition
            && InventoryDao.IsBagPosition(DbStore.Int(x, "position")));
        if (row == null) return new MerchantResponse { ok = false, error = "背包里没有这件道具" };

        var item = CharService.ToInventoryEntry(db, row);
        if (NativePotentialService.BlocksTransfer(db, item))
            return new MerchantResponse { ok = false, error = "潜能结果待选装备或装备痕迹不能上架" };
        bool rechargeable = Server.Bot.Trade.TradeItemRules.Rechargeable(item.itemId);
        int perBundle = r.bundles > 0 ? r.quantity : r.quantity;   // 请求里的 quantity = 每份数量
        int bundles = r.bundles > 0 ? r.bundles : 1;
        if (rechargeable) { perBundle = 1; bundles = 1; }
        if (!rechargeable && item.quantity < perBundle * bundles)
            return new MerchantResponse { ok = false, error = "数量不足" };
        int price = r.price;
        if (perBundle <= 0 || perBundle * bundles > MaxTotalPerSlot || bundles <= 0 || price <= 0)
            return new MerchantResponse { ok = false, error = "上架参数不合法" };

        int take = rechargeable ? item.quantity : perBundle * bundles;
        var stored = item.Copy();
        stored.quantity = rechargeable ? item.quantity : perBundle;
        int slot = NextSlot(items);
        long payload = WriteEntry(db, StorageCharacterId, stored, 1, stored.quantity);
        db.Table("merchantitems").Insert(new DbRow {
            ["ownerid"] = c.id, ["slot"] = (long)slot, ["inventoryitemid"] = payload,
            ["bundles"] = (long)bundles, ["price"] = (long)price });
        ConsumeFromInventory(db, row, item, take);
        db.Save();
        return Snapshot(db, c);
    }

    // PlayerInteractionHandler TAKE_ITEM_BACK：只有关店状态能拿回，数量 = 份数 × 每份数量。
    static MerchantResponse TakeItemBack(DbStore db, CharRecord c, MerchantRequest r)
    {
        var shopRow = ShopRow(db, (int)c.id);
        if (shopRow == null) return new MerchantResponse { ok = false, error = "还没有开店" };
        if (DbStore.Int(shopRow, "open") == 1)
            return new MerchantResponse { ok = false, error = "营业中不能取回道具" };
        int cid = (int)c.id;
        var entry = db.Table("merchantitems").Find(x => DbStore.Int(x, "ownerid") == cid && DbStore.Int(x, "slot") == r.slot);
        if (entry == null) return new MerchantResponse { ok = false, error = "这个格子是空的" };
        var stored = LoadSlotItem(db, entry);
        int bundles = DbStore.Int(entry, "bundles");
        int quantity = stored != null ? stored.quantity * Math.Max(0, bundles) : 0;
        if (stored != null && quantity > 0)
        {
            var give = stored.Copy();
            give.quantity = quantity;
            var full = CharService.Load(db, cid);
            int position = Server.Bot.Trade.TradeItemRules.FreeSlot(Actor(c, full), full.inventory, give.inventoryType);
            if (position <= 0)
                return new MerchantResponse { ok = false, error = "背包没有空位放回这件道具" };
            WriteEntry(db, cid, give, position, quantity);
        }
        DeleteSlot(db, entry);
        db.Save();
        return Snapshot(db, c);
    }

    // ————————————————— 买卖 —————————————————

    // HiredMerchant.buy：价格 × 份数，卖价先扣 Trade.getFee 再进店主 MerchantMesos。
    // 单机里唯一的顾客是假人（Server.Bot 的市场系统进店购买），所以这里接收「买家已扣款」的调用，
    // 只负责店内存货、成交记录与店主金币。真人之间的相互购买在本移植里不存在。
    public static bool BotPurchase(DbStore db, int ownerId, int slot, int quantity, string buyer, out int price, out MerchantSlotData sold)
    {
        price = 0; sold = null;
        var entry = db.Table("merchantitems").Find(x => DbStore.Int(x, "ownerid") == ownerId && DbStore.Int(x, "slot") == slot);
        if (entry == null || quantity < 1) return false;
        var shopRow = ShopRow(db, ownerId);
        if (shopRow == null || DbStore.Int(shopRow, "open") == 0) return false;
        int bundles = DbStore.Int(entry, "bundles");
        if (bundles < quantity) return false;
        var stored = LoadSlotItem(db, entry);
        if (stored == null) return false;
        price = MarketEconomyClamp(Math.Min((float)DbStore.Int(entry, "price") * quantity, int.MaxValue));
        long proceeds = price - Server.Bot.Trade.BotTradeService.Fee(price);
        CreditMerchantMeso(db, ownerId, proceeds);
        EntryAddSold(db, ownerId, buyer, stored.itemId, stored.quantity, quantity, proceeds);
        bundles -= quantity;
        if (bundles < 1) DeleteSlot(db, entry); else entry["bundles"] = (long)bundles;
        db.Save();
        sold = new MerchantSlotData { slot = slot, price = price, bundles = quantity, item = stored };
        return true;
    }

    // 成交记录（源端 HiredMerchant.sold；每行只留买家/物品/份数/实收）。
    static void EntryAddSold(DbStore db, int ownerId, string buyer, int itemId, int perBundle, int quantity, long meso)
        => db.Table("merchantsold").Insert(new DbRow {
            ["ownerid"] = (long)ownerId, ["buyer"] = buyer ?? "", ["itemid"] = (long)itemId,
            ["quantity"] = (long)(perBundle * quantity), ["meso"] = meso,
            ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

    static int MarketEconomyClamp(double value)
        => double.IsNaN(value) ? 0 : value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;

    // ————————————————— 金币 —————————————————

    // Character.withdrawMerchantMesos：净额 = merchantmeso × (100 - 寄存天数)/100，考虑金币上限。
    static MerchantResponse WithdrawMesos(DbStore db, CharRecord c)
    {
        int cid = (int)c.id;
        long net = NetMerchantMeso(db, c);
        long merchantMeso = MerchantMeso(db, c);
        if (net > 0)
        {
            long possible = int.MaxValue - c.meso;
            if (possible > 0)
            {
                long gain = Math.Min(possible, net);
                c.meso += gain;
                CharactersDao.Save(db, c);
                SetMerchantMeso(db, cid, merchantMeso - gain);
            }
        }
        else if (merchantMeso < 0)
        {
            if (c.meso + merchantMeso < 0)
            {
                SetMerchantMeso(db, cid, merchantMeso + c.meso);
                c.meso = 0;
            }
            else
            {
                c.meso += merchantMeso;
                SetMerchantMeso(db, cid, 0);
            }
            CharactersDao.Save(db, c);
        }
        db.Save();
        return new MerchantResponse { ok = true, meso = c.meso, merchantMeso = MerchantMeso(db, c), shop = Load(db, cid) };
    }

    // ————————————————— 整理 / 关店 / 营业 —————————————————

    // PlayerInteractionHandler MERCHANT_ORGANIZE：先提金币、清掉卖空的格子，空了就关店。
    static MerchantResponse Organize(DbStore db, CharRecord c)
    {
        WithdrawMesos(db, c);
        ClearEmptySlots(db, (int)c.id);
        int cid = (int)c.id;
        if (db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == cid).Count == 0)
            return Close(db, c, false);
        db.Save();
        return Snapshot(db, c);
    }

    // HiredMerchant.closeShop：有空间就把货退回背包，剩下连同金币记进 Fredrick 寄存。
    static MerchantResponse Close(DbStore db, CharRecord c, bool expire)
    {
        int cid = (int)c.id;
        var shopRow = ShopRow(db, cid);
        if (shopRow == null) return new MerchantResponse { ok = false, error = "还没有开店" };
        var entries = db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == cid);
        if (!expire)
        {
            var full = CharService.Load(db, cid);
            var actor = Actor(c, full);
            // 先把整店的退货算一遍空位（同一格不能塞两件），算不下就整店留在寄存里。
            var used = new HashSet<int>();
            var give = new List<KeyValuePair<DbRow, InventoryEntry>>();
            bool room = true;
            foreach (var entry in entries)
            {
                var stored = LoadSlotItem(db, entry);
                if (stored == null) continue;
                var item = stored.Copy();
                item.quantity = stored.quantity * Math.Max(1, DbStore.Int(entry, "bundles"));
                if (Reserve(actor, full.inventory, used, item.inventoryType) <= 0) { room = false; break; }
                give.Add(new KeyValuePair<DbRow, InventoryEntry>(entry, item));
            }
            if (room)
            {
                foreach (var pair in give)
                {
                    int position = Reserve(actor, full.inventory, used, pair.Value.inventoryType);
                    if (position <= 0) continue;
                    WriteEntry(db, cid, pair.Value, position, pair.Value.quantity);
                    DeleteSlot(db, pair.Key);
                }
            }
        }
        bool leftovers = db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == cid).Count > 0;
        if (leftovers) TouchFredrickLog(db, cid); else RemoveFredrickLog(db, cid);
        db.Table("hiredmerchants").Delete(x => DbStore.Int(x, "ownerid") == cid);
        SetMerchantFlag(db, c, false);
        db.Save();
        var res = new MerchantResponse { ok = true, inventory = CharService.Load(db, cid).inventory, meso = c.meso,
            merchantMeso = MerchantMeso(db, c) };
        res.shop = null;
        return res;
    }

    // MAINTAINANCE_OFF：还有货就开张营业；没货直接关店。
    static MerchantResponse Maintenance(DbStore db, CharRecord c)
    {
        var shopRow = ShopRow(db, (int)c.id);
        if (shopRow == null) return new MerchantResponse { ok = false, error = "还没有开店" };
        int cid = (int)c.id;
        if (db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == cid).Count == 0)
            return Close(db, c, false);
        shopRow["open"] = 1L;
        shopRow["published"] = 1L;
        db.Save();
        return Snapshot(db, c);
    }

    static MerchantResponse SetOpen(DbStore db, CharRecord c, bool open)
    {
        var shopRow = ShopRow(db, (int)c.id);
        if (shopRow == null) return new MerchantResponse { ok = false, error = "还没有开店" };
        shopRow["open"] = open ? 1L : 0L;
        if (open) shopRow["published"] = 1L;
        db.Save();
        return Snapshot(db, c);
    }

    static MerchantResponse Blacklist(DbStore db, CharRecord c, string name, bool add)
    {
        if (string.IsNullOrEmpty(name)) return new MerchantResponse { ok = false, error = "名字不能为空" };
        // 源端黑名单只存在于内存里（HiredMerchant.blacklist），关店即失效；单机没有真人访客，
        // 这里保留同样的「记录但不落库」语义，仅让窗口能回显。
        var shop = Load(db, (int)c.id);
        if (shop == null) return new MerchantResponse { ok = false, error = "还没有开店" };
        if (add) { if (shop.blacklist.Count < 20 && !shop.blacklist.Contains(name)) shop.blacklist.Add(name); }
        else shop.blacklist.Remove(name);
        return new MerchantResponse { ok = true, shop = shop, inventory = null, meso = -1 };
    }

    // ————————————————— Fredrick 寄存 —————————————————

    // FredrickProcessor.fredrickRetrieveItems
    static MerchantResponse Retrieve(DbStore db, CharRecord c)
    {
        int cid = (int)c.id;
        var entries = db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == cid);
        var full = CharService.Load(db, cid);
        var actor = Actor(c, full);
        var planned = new List<InventoryEntry>();
        foreach (var entry in entries)
        {
            var stored = LoadSlotItem(db, entry);
            if (stored == null) continue;
            var give = stored.Copy();
            give.quantity = stored.quantity * Math.Max(1, DbStore.Int(entry, "bundles"));
            // 先做一次空间试算，任何一件放不下就整体拒绝（源端 checkSpotsAndOwnership 语义）。
            var probe = new List<InventoryEntry>();
            foreach (var item in full.inventory) probe.Add(item.Copy());
            foreach (var item in planned) probe.Add(new InventoryEntry { itemId = item.itemId, inventoryType = item.inventoryType, position = 1, quantity = 0 });
            if (Server.Bot.Trade.TradeItemRules.FreeSlot(actor, probe, give.inventoryType) <= 0)
            {
                bool unique = Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(give.itemId), "only") == 1;
                return new MerchantResponse { ok = false, code = unique ? FredrickUnique : FredrickNoSpace,
                    error = unique ? "背包里已有同一件唯一道具" : "背包空间不足，无法取回" };
            }
            planned.Add(give);
        }
        long net = NetMerchantMeso(db, c);
        if (net > 0 && c.meso + net > int.MaxValue)
            return new MerchantResponse { ok = false, code = FredrickMesoFull, error = "金币已达上限" };
        if (net < 0 && c.meso < -net)
            return new MerchantResponse { ok = false, code = FredrickNotEnoughMeso, error = "金币不足以支付商店欠款" };

        foreach (var give in planned)
        {
            int position = Server.Bot.Trade.TradeItemRules.FreeSlot(actor, full.inventory, give.inventoryType);
            if (position <= 0) continue;
            WriteEntry(db, cid, give, position, give.quantity);
            full.inventory.Add(new InventoryEntry { itemId = give.itemId, inventoryType = give.inventoryType, position = position, quantity = give.quantity });
        }
        foreach (var entry in entries) DeleteSlot(db, entry);
        WithdrawMesos(db, c);
        RemoveFredrickLog(db, cid);
        db.Save();
        return new MerchantResponse { ok = true, code = FredrickSuccess, inventory = CharService.Load(db, cid).inventory, meso = c.meso, merchantMeso = MerchantMeso(db, c) };
    }

    // World.runHiredMerchantSchedule：开店超过 hired_merchants_exist_time 天 → 强制关店，货进寄存。
    public static int Expire(DbStore db, long now)
    {
        int days = Math.Max(1, GameConfigDao.GetInt(db, "hired_merchants_exist_time", 1));
        long limit = (long)days * 24 * 60 * 60 * 1000;
        var stale = db.Table("hiredmerchants").Select(x => now - DbStore.Long(x, "starttime") > limit);
        foreach (var row in stale)
        {
            var c = CharactersDao.FindById(db, DbStore.Int(row, "ownerid"));
            if (c != null)
            {
                var shopRow = ShopRow(db, (int)c.id);
                if (shopRow != null && DbStore.Long(shopRow, "starttime") > now - limit) continue;   // 刚重开
                Close(db, c, true);
            }
            else
            {
                db.Table("hiredmerchants").Delete(x => DbStore.Int(x, "ownerid") == DbStore.Int(row, "ownerid"));
                TouchFredrickLog(db, DbStore.Int(row, "ownerid"));
            }
        }
        if (stale.Count > 0) db.Save();
        return stale.Count;
    }

    static MerchantResponse Tick(DbStore db, CharRecord c)
    {
        Expire(db, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return Snapshot(db, c);
    }

    // ————————————————— 存储层 —————————————————

    static DbRow ShopRow(DbStore db, int ownerId)
        => db.Table("hiredmerchants").Find(x => DbStore.Int(x, "ownerid") == ownerId);

    static DbRow CharactersRow(DbStore db, CharRecord c)
        => db.Table("characters").Find(x => DbStore.Int(x, "id") == (int)c.id);

    static bool HasMerchantFlag(DbStore db, CharRecord c)
    {
        var row = CharactersRow(db, c);
        return row != null && DbStore.Int(row, "HasMerchant") != 0;
    }

    static void SetMerchantFlag(DbStore db, CharRecord c, bool value)
    {
        var row = CharactersRow(db, c);
        if (row == null) return;
        row["HasMerchant"] = value ? 1L : 0L;
        c.HasMerchant = value ? 1 : 0;
    }

    static long MerchantMeso(DbStore db, CharRecord c)
    {
        var row = CharactersRow(db, c);
        return row == null ? 0 : DbStore.Long(row, "MerchantMesos", 0);
    }

    static void SetMerchantMeso(DbStore db, int characterId, long value)
    {
        // Character.setMerchantMeso 把值截到 int 范围。
        value = Math.Max(int.MinValue, Math.Min(int.MaxValue, value));
        db.Table("characters").Update(x => DbStore.Int(x, "id") == characterId, new DbRow { ["MerchantMesos"] = value });
        var c = CharactersDao.FindById(db, characterId);
        if (c != null) c.MerchantMesos = (int)value;
    }

    static void CreditMerchantMeso(DbStore db, int ownerId, long add)
    {
        var owner = CharactersDao.FindById(db, ownerId);
        if (owner == null) return;
        SetMerchantMeso(db, ownerId, MerchantMeso(db, owner) + add);
    }

    // Character.getMerchantNetMeso：寄存越久，Fredrick 抽成越高（每天 1%，最多 100 天）。
    static long NetMerchantMeso(DbStore db, CharRecord c)
    {
        int elapsed = 0;
        var log = db.Table("fredstorage").Find(x => DbStore.Int(x, "cid") == (int)c.id);
        if (log != null)
            elapsed = (int)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - DbStore.Long(log, "timestamp")) / (24L * 60 * 60 * 1000));
        if (elapsed > 100) elapsed = 100;
        if (elapsed < 0) elapsed = 0;
        return MerchantMeso(db, c) * (100 - elapsed) / 100;
    }

    static void TouchFredrickLog(DbStore db, int cid)
    {
        RemoveFredrickLog(db, cid);
        db.Table("fredstorage").Insert(new DbRow { ["cid"] = (long)cid, ["daynotes"] = 0L,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }

    public static void RemoveFredrickLog(DbStore db, int cid)
        => db.Table("fredstorage").Delete(x => DbStore.Int(x, "cid") == cid);

    // FredrickProcessor.runFredrickSchedule：按 2/5/10/15/30/60/90 天提醒，100 天连物品一起清掉。
    // 源端提醒时同时 noteService.sendNormal(msg, "FREDRICK", 角色名)（便签，登录时随 ShowNotes 下发）。
    public static List<int> FredrickDueReminders(DbStore db, long now)
    {
        var due = new List<int>();
        foreach (var log in db.Table("fredstorage").Select(x => true))
        {
            int cid = DbStore.Int(log, "cid");
            int notes = DbStore.Int(log, "daynotes");
            int elapsed = (int)((now - DbStore.Long(log, "timestamp")) / (24L * 60 * 60 * 1000));
            if (elapsed > 100) continue;
            int threshold = notes >= 0 && notes < DailyReminders.Length ? DailyReminders[notes] : int.MaxValue;
            if (elapsed < threshold) continue;
            do { notes++; threshold = notes < DailyReminders.Length ? DailyReminders[notes] : int.MaxValue; } while (elapsed >= threshold);
            log["daynotes"] = (long)notes;
            var chr = db.Table("characters").Find(x => DbStore.Int(x, "id") == cid);
            if (chr != null) NativeNoteService.SendNormal(db, FredrickReminderMessage(notes - 1), "FREDRICK", DbStore.Str(chr, "name"));
            due.Add(cid);
        }
        if (due.Count > 0) db.Save();
        return due;
    }

    // FredrickProcessor.fredrickReminderMessage(daynotes)：dailyReminders 下标文案
    public static string FredrickReminderMessage(int daynotes)
    {
        if (daynotes < 0 || daynotes >= DailyReminders.Length - 1) daynotes = 0;
        int days = DailyReminders[daynotes];
        if (daynotes < 4) return "您好，亲爱的顾客! 我是弗兰德里, 雇佣商人工会的主席. 您的店铺已经关闭" + days + " 天了. 请到自由市场入口找我取回存储的物品.";
        return "您好，亲爱的顾客! 我是弗兰德里, 雇佣商人工会的主席. 您的店铺已经关闭 " + days + " 天了. 在我们撤走之前，请考虑索回这些物品";
    }

    static List<DbRow> StoredItemRows(DbStore db, int ownerId)
        => db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == ownerId);

    static int NextSlot(List<DbRow> items)
    {
        var used = new HashSet<int>();
        foreach (var row in items) used.Add(DbStore.Int(row, "slot"));
        for (int slot = 0; slot < MaxSlots; slot++) if (!used.Contains(slot)) return slot;
        return -1;
    }

    static void ClearEmptySlots(DbStore db, int ownerId)
    {
        foreach (var entry in db.Table("merchantitems").Select(x => DbStore.Int(x, "ownerid") == ownerId))
            if (DbStore.Int(entry, "bundles") < 1) DeleteSlot(db, entry);
    }

    static void DeleteSlot(DbStore db, DbRow entry)
    {
        long payload = DbStore.Long(entry, "inventoryitemid");
        db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == payload);
        db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == payload);
        db.Table("merchantitems").Delete(x => DbStore.Long(x, "id", -1) == DbStore.Long(entry, "id", -2));
    }

    static InventoryEntry LoadSlotItem(DbStore db, DbRow entry)
    {
        long payload = DbStore.Long(entry, "inventoryitemid");
        var row = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid", -1) == payload);
        return row == null ? null : CharService.ToInventoryEntry(db, row);
    }

    // 背包行 → 任意持有者（角色的背包或商店寄存），完整保留 itemData / equipmentData。
    // internal：个人商店（NativePlayerShopService）复用同一套行写入，避免两处口径漂移。
    internal static long WriteEntry(DbStore db, int characterId, InventoryEntry entry, int position, int quantity)
    {
        var row = new DbRow();
        if (entry.itemData != null) foreach (var pair in entry.itemData) row[pair.Key] = pair.Value;
        row.Remove("inventoryitemid");
        row["characterid"] = (long)characterId;
        row["type"] = (long)(entry.itemId / 1000000);
        row["itemid"] = (long)entry.itemId;
        row["inventorytype"] = (long)entry.inventoryType;
        row["position"] = (long)position;
        row["quantity"] = (long)quantity;
        row["owner"] = entry.owner ?? "";
        row["flag"] = (long)entry.flag;
        row["expiration"] = entry.expiration;
        if (entry.petId > 0) row["petid"] = (long)entry.petId;
        long id = db.Table("inventoryitems").Insert(row);
        if (entry.isEquip)
        {
            var equip = new DbRow();
            if (entry.equipmentData != null) foreach (var pair in entry.equipmentData) equip[pair.Key] = pair.Value;
            equip.Remove("inventoryequipmentid");
            equip["inventoryitemid"] = id;
            db.Table("inventoryequipment").Insert(equip);
        }
        return id;
    }

    // 从背包扣掉指定数量（可充能整组扣），扣空就删行与装备行。
    internal static void ConsumeFromInventory(DbStore db, DbRow row, InventoryEntry item, int take)
    {
        if (take >= item.quantity)
        {
            long id = DbStore.Long(row, "inventoryitemid");
            db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == id);
            db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == id);
        }
        else row["quantity"] = (long)(item.quantity - take);
    }

    static Server.Bot.BotActor Actor(CharRecord c, FullCharacter full)
        => new Server.Bot.BotActor { id = (int)c.id, job = c.job, character = full };

    // 在同一份「已占用」集合上连续找空位（退货多件时不能都算到同一个格子）。
    static int Reserve(Server.Bot.BotActor actor, List<InventoryEntry> inventory, HashSet<int> used, int type)
    {
        int limit = Server.Bot.Trade.TradeItemRules.SlotLimit(actor, type);
        var occupied = new HashSet<int>();
        foreach (var item in inventory) if (item.inventoryType == type && InventoryDao.IsBagPosition(item.position)) occupied.Add(item.position);
        for (int slot = 1; slot <= limit; slot++)
            if (!occupied.Contains(slot) && used.Add(slot)) return slot;
        return -1;
    }

    // 读取整间店（窗口与假人镜像都用它）。
    public static MerchantSnapshot Load(DbStore db, int ownerId)
    {
        var row = ShopRow(db, ownerId);
        if (row == null) return null;
        var owner = CharactersDao.FindById(db, ownerId);
        var shop = new MerchantSnapshot {
            ownerId = ownerId, mapId = DbStore.Int(row, "mapid"), permitId = DbStore.Int(row, "permitid"),
            ownerName = owner?.name ?? "", description = Str(row, "description"),
            open = DbStore.Int(row, "open") != 0, published = DbStore.Int(row, "published") != 0,
            merchantMeso = owner == null ? 0 : MerchantMeso(db, owner),
            startedAt = DbStore.Long(row, "starttime") };
        foreach (var entry in StoredItemRows(db, ownerId))
        {
            var stored = LoadSlotItem(db, entry);
            shop.items.Add(new MerchantSlotData {
                slot = DbStore.Int(entry, "slot"), bundles = DbStore.Int(entry, "bundles"),
                price = DbStore.Int(entry, "price"), item = stored });
        }
        shop.items.Sort((a, b) => a.slot.CompareTo(b.slot));
        return shop;
    }

    static string Str(DbRow row, string column)
        => row.TryGetValue(column, out var value) ? value as string ?? "" : "";
}
