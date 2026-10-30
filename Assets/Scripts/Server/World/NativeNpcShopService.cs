using System;
using System.Collections.Generic;
using Server.Bot;
using Server.Bot.Trade;

public enum NpcShopOperation { OpenNpc, OpenShop, Close, Buy, Sell, Recharge }
public sealed class NpcShopRequest
{
    public NpcShopOperation operation;
    public int characterId, id, index, itemId, quantity = 1, inventoryType, position;
    public bool fallback;
}
public sealed class NpcShopItem
{
    public int itemId, price, pitch;
}
public sealed class NpcShopResponse
{
    public bool ok = true, opened;
    public string error = "";
    public int shopId, npcId, mapId;
    public List<NpcShopItem> items = new List<NpcShopItem>();
    public FullCharacter character;
    public Dictionary<int, int> sellPrices = new Dictionary<int, int>();
}

// NPCTalkHandler -> ShopFactory -> Shop; source JS remains unchanged.
public sealed class NativeNpcShopService
{
    readonly DbStore db;
    readonly Dictionary<int, int> sessions = new Dictionary<int, int>();
    readonly Dictionary<int, int> sessionMaps = new Dictionary<int, int>();
    BotItemCatalog Catalog => BotItemCatalog.Shared;
    bool seeded;
    public Func<int, BotActor> Player;
    public NativeNpcShopService(DbStore db) { this.db = db; }
    public void EndSession(int id) { sessions.Remove(id); sessionMaps.Remove(id); }
    void Seed()
    {
        if (seeded) return;
        const string marker = "native_npc_shops_seed_v1";
        if (GameConfigDao.Get(db, marker) != "1")
        {
            var text = PlatformRes.LoadText("Shop/NpcShops");
            var root = MiniJsonDb.Parse(text) as Dictionary<string, object>;
            if (root == null) throw new InvalidOperationException("Original NPC shop seed is missing");
            db.BeginTransaction();
            try
            {
                // Existing admin stock is authoritative. Import untouched shops only.
                var configured = new HashSet<int>();
                foreach (var row in db.Table("shops").Select(_ => true)) configured.Add(DbStore.Int(row, "shopid"));
                foreach (var row in db.Table("shopitems").Select(_ => true)) configured.Add(DbStore.Int(row, "shopid"));
                foreach (var entry in (List<object>)root["shops"])
                {
                    var row = (Dictionary<string, object>)entry;
                    int shop = Convert.ToInt32(row["shopid"]);
                    if (db.Table("shops").Find(r => DbStore.Int(r, "shopid") == shop) == null)
                        db.Table("shops").Insert(new DbRow { ["shopid"] = row["shopid"], ["npcid"] = row["npcid"] });
                }
                foreach (var entry in (List<object>)root["shopitems"])
                {
                    var row = (Dictionary<string, object>)entry;
                    if (configured.Contains(Convert.ToInt32(row["shopid"]))) continue;
                    var stock = new DbRow(); foreach (var pair in row) if (pair.Key != "shopitemid") stock[pair.Key] = pair.Value;
                    db.Table("shopitems").Insert(stock);
                }
                db.Table("game_config").Insert(new DbRow { ["config_code"] = marker, ["config_value"] = "1" });
                db.Commit(); db.Save();
            }
            catch { db.Rollback(); throw; }
        }
        seeded = true;
    }
    public bool HasNpc(int id) { lock (db) { Seed(); return db.Table("shops").Find(r => DbStore.Int(r, "npcid") == id) != null; } }
    List<NpcShopItem> Stock(int shop)
    {
        var rows = db.Table("shopitems").Select(r => DbStore.Int(r, "shopid") == shop);
        rows.Sort((a, b) => DbStore.Int(b, "position").CompareTo(DbStore.Int(a, "position")));
        var items = new List<NpcShopItem>(); var recharge = new List<int>();
        for (int i = 2070000; i <= 2070016; i++) if (i != 2070014) recharge.Add(i);
        recharge.Add(2331000); recharge.Add(2332000); recharge.Add(2070018);
        for (int i = 2330000; i <= 2330005; i++) recharge.Add(i);
        foreach (var row in rows)
        {
            int id = DbStore.Int(row, "itemid");
            items.Add(new NpcShopItem { itemId = id, price = DbStore.Int(row, "price"), pitch = DbStore.Int(row, "pitch") });
            recharge.Remove(id);
        }
        foreach (int id in recharge) items.Add(new NpcShopItem { itemId = id });
        return items;
    }
    public NpcShopResponse Handle(NpcShopRequest r)
    {
        lock (db)
        {
            Seed(); var c = CharService.Load(db, r.characterId);
            if (c == null) return new NpcShopResponse { ok = false, error = "角色不存在" };
            string error = null;
            var active = Player?.Invoke(r.characterId);
            if (active?.trade != null && r.operation != NpcShopOperation.Close)
                return new NpcShopResponse { ok = false, error = "交易中不能打开商店" };
            if (r.operation == NpcShopOperation.Close) EndSession(r.characterId);
            else if (r.operation == NpcShopOperation.OpenNpc || r.operation == NpcShopOperation.OpenShop)
            {
                var row = db.Table("shops").Find(x => DbStore.Int(x, r.operation == NpcShopOperation.OpenNpc ? "npcid" : "shopid") == r.id);
                if (row == null && r.fallback) row = db.Table("shops").Find(x => DbStore.Int(x, "shopid") == 11000);
                EndSession(r.characterId);
                if (row == null) error = "这位 NPC 没有商店";
                else { sessions[r.characterId] = DbStore.Int(row, "shopid"); sessionMaps[r.characterId] = c.record.map; }
            }
            else if (!sessions.TryGetValue(r.characterId, out int shop) || sessionMaps[r.characterId] != c.record.map)
            { EndSession(r.characterId); error = "请重新打开商店"; }
            else
            {
                var actor = new BotActor { id = r.characterId, job = c.record.job, character = c };
                var trade = new BotTradeService(db, Catalog);
                db.BeginTransaction();
                try
                {
                    error = Apply(actor, trade.Rules, Stock(shop), r);
                    if (error == null)
                    {
                        trade.Persist(actor); db.Commit(); db.Save();
                        if (active?.character != null) { active.character.inventory = c.inventory; active.character.record.meso = c.record.meso; }
                    }
                    else db.Rollback();
                }
                catch { db.Rollback(); throw; }
            }
            var result = new NpcShopResponse { ok = error == null, error = error ?? "", character = CharService.Load(db, r.characterId) };
            if (sessions.TryGetValue(r.characterId, out int current))
            {
                var row = db.Table("shops").Find(x => DbStore.Int(x, "shopid") == current);
                result.opened = true; result.shopId = current; result.npcId = DbStore.Int(row, "npcid");
                result.mapId = sessionMaps[r.characterId]; result.items = Stock(current);
                foreach (var item in result.character.inventory) if (item.position > 0)
                    result.sellPrices[item.inventoryType * 1000 + item.position] = SellPrice(item.itemId, TradeItemRules.Rechargeable(item.itemId) ? item.quantity : 1);
            }
            return result;
        }
    }
    string Apply(BotActor actor, TradeItemRules rules, List<NpcShopItem> stock, NpcShopRequest r)
    {
        var c = actor.character; var inv = c.inventory;
        if (c.record.hp <= 0) return "当前状态不能交易";
        int q = r.quantity;
        if (r.operation == NpcShopOperation.Buy)
        {
            if (r.index < 0 || r.index >= stock.Count || stock[r.index].itemId != r.itemId || q < 1 || q > short.MaxValue) return "商品或数量无效";
            var item = stock[r.index];
            if (Catalog.Info(item.itemId) == null) return "道具资源缺失";
            if (item.itemId / 1000000 == 1 && q != 1) return "装备每次购买一件";
            bool rechargeable = TradeItemRules.Rechargeable(item.itemId);
            long cost = item.price > 0 ? Math.Min((long)((float)item.price * q), int.MaxValue) : Math.Min((long)((float)item.pitch * q), int.MaxValue);
            if (item.price <= 0 && item.pitch <= 0) return "此道具仅提供补充服务";
            if (item.price > 0 && c.record.meso < cost) return "金币不足";
            if (item.price <= 0)
            {
                long held = 0; foreach (var it in inv) if (it.inventoryType == 4 && it.position > 0 && it.itemId == 4310000) held += it.quantity;
                if (held < cost) return "音符数量不足";
            }
            if (!rules.Receive(actor, inv, Catalog.CleanItem(item.itemId, rechargeable ? rules.SlotMax(item.itemId, actor) : q))) return "背包空间不足或道具已达持有上限";
            if (item.price > 0) c.record.meso -= rechargeable ? item.price : (int)cost;
            else
            {
                foreach (var it in new List<InventoryEntry>(inv))
                {
                    if (cost == 0) break;
                    if (it.itemId != 4310000 || it.inventoryType != 4 || it.position <= 0) continue;
                    int take = (int)Math.Min(cost, it.quantity); it.quantity -= take; cost -= take;
                    if (it.quantity == 0) inv.Remove(it);
                }
            }
            return null;
        }
        var heldItem = inv.Find(i => i.inventoryType == r.inventoryType && i.position == r.position && i.position > 0);
        if (heldItem == null) return "该背包槽位没有道具";
        if (r.operation == NpcShopOperation.Recharge)
        {
            if (heldItem.inventoryType != 2 || !TradeItemRules.Rechargeable(heldItem.itemId) || heldItem.quantity < 0) return "不能补充此道具";
            int max = rules.SlotMax(heldItem.itemId, actor);
            int price = (int)Math.Ceiling(UnitPrice(heldItem.itemId) * Math.Max(0, max - heldItem.quantity));
            if (price < 0 || c.record.meso < price) return "金币不足";
            if (heldItem.quantity < max) { heldItem.quantity = max; c.record.meso -= price; }
            return null;
        }
        if (r.operation != NpcShopOperation.Sell || r.inventoryType > 5) return "无效商店操作";
        if (q == 0 || q == 65535) q = 1;
        if (q < 1 || q > short.MaxValue || heldItem.quantity < 0) return "出售数量无效";
        if (TradeItemRules.Rechargeable(heldItem.itemId)) q = heldItem.quantity;
        else if (q > heldItem.quantity) return "道具数量不足";
        int priceReceived = SellPrice(heldItem.itemId, q);
        if (priceReceived > 0 && (long)c.record.meso + priceReceived > int.MaxValue) return "金币已达上限";
        heldItem.quantity -= q; if (heldItem.quantity == 0) inv.Remove(heldItem);
        if (priceReceived > 0) c.record.meso += priceReceived;
        return null;
    }
    public int SellPrice(int id, int quantity)
    {
        int price = BotItemCatalog.Int(Catalog.Info(id), "price", -1);
        return price == -1 ? -1 : TradeItemRules.Rechargeable(id) ? price + (int)Math.Ceiling(quantity * UnitPrice(id)) : unchecked(price * quantity);
    }
    public double UnitPrice(int id)
    {
        var info = Catalog.Info(id);
        if (info == null || !info.TryGetValue("unitPrice", out var raw)) return 0;
        double value = Convert.ToDouble(raw), whole = Math.Floor(value), fraction = value - whole, mask = 0, last, current = 1;
        int i = 1;
        do { last = current; current /= 2; if (fraction == current) break; if (fraction > current) { mask += current; fraction -= current; } i++; } while (i <= 5);
        if (i > 5) { last = current; current = 0; }
        return whole + mask + (Math.Abs(fraction - current) < Math.Abs(fraction - last) ? current : last);
    }
}
