using System;
using System.Collections.Generic;

// CashShop.CashItemFactory / ModifiedCashItemDO / CashOperationHandler.
// 字段与源端 ModifiedCashItemDO 对齐：Etc/Commodity.img 只提供其中一部分，
// 其余字段由 modified_cash_item 表覆盖（等价于 CashItemFactory.getItem 的逐字段 null 保留）。
public sealed class CashCommodity {
    public int sn, itemId, count = 1, price, period = 1, onSale, gender = 2;
    public int priority, bonus, maplePoint, meso, forPremiumUser, clz, limit;
    public int pbCash, pbPoint, pbGift, packageSn;
    public CashCommodity Copy() => (CashCommodity)MemberwiseClone();
}
public interface INativeCashCatalog {
    IEnumerable<CashCommodity> All { get; }
    CashCommodity Find(int sn);
    IList<int> Package(int itemId);
    bool Exists(int itemId);
    // 商品在客户端是否完整（既有名字又有图标）；残缺商品不入库、不展示。
    bool Complete(int itemId);
    bool IsCash(int itemId);
    DbRow Equipment(int itemId);
    int EquipSlot(int itemId);
    string CanEquip(int itemId, CharRecord character);
}
public enum CashOperation { Open, Close, Read, Buy, Gift, ClaimGifts, Withdraw, Deposit, WishList, ExpandInventory, ExpandCharacters, Equip, Unequip, AdjustBalance, RedeemCode }
public sealed class CashRequest {
    public int characterId, sn, currency = 1, birthday, inventoryType, amount;
    public long itemId;
    public string target = "", message = "";
    public int[] wishes;
    public CashOperation operation;
}
public sealed class CashStock {
    public long id, expiration;
    public int itemId, quantity, sn, characterId;
    public string giftFrom;
}
public sealed class CashResponse {
    public bool ok = true, opened;
    public string error = "";
    public int credit, points, prepaid, gifts;
    public List<CashCommodity> catalog = new List<CashCommodity>();
    public List<CashStock> stock = new List<CashStock>();
    public List<int> wishes = new List<int>();
    public FullCharacter character;
}
public sealed class NativeCashService {
    readonly DbStore db;
    readonly HashSet<int> opened = new HashSet<int>();
    public INativeCashCatalog Catalog { get; set; }
    public Func<int, bool> CanEnter;
    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public NativeCashService(DbStore db) { this.db = db; }
    public void EndSession(int id) { opened.Remove(id); }
    int Group(CharRecord c) => GameConfigDao.GetBool(db, "use_joint_cash_shop_inventory", false) ? 7 : c.job / 1000 == 1 ? 4 : c.job / 1000 == 2 ? 5 : 3;
    bool Own(DbRow row, CharRecord c) => DbStore.Int(row, "accountid") == c.accountid && DbStore.Int(row, "world") == c.world && DbStore.Int(row, "jobgroup") == Group(c);
    List<DbRow> Stock(CharRecord c) => db.Table("cash_items").Select(r => Own(r, c) && DbStore.Int(r, "characterid") == 0);
    static string Currency(int type) => type == 1 ? "nxCredit" : type == 2 ? "maplePoint" : type == 4 ? "nxPrepaid" : null;
    // 与 CashShop.CashItemFactory.getItem 同构：WZ 记录为基线，modified_cash_item 的列为覆盖
    // （列为空 = 保留原值）。表里单独存在的 SN 也能独立定义一个商品。
    public CashCommodity Commodity(int sn) => Commodity(db.Table("modified_cash_item").Find(r => DbStore.Int(r, "sn") == sn), sn);
    // 列表整表遍历时按行走，避免每行再线性 Find 一次（表上千行时是 O(n²)）。
    public CashCommodity Commodity(DbRow row, int sn) {
        var item = Catalog?.Find(sn)?.Copy();
        if (row == null) return item;
        if (item == null) item = new CashCommodity { sn = sn };
        item.itemId = Override(row, "item_id", item.itemId);
        item.price = Override(row, "price", item.price);
        item.count = Override(row, "count", item.count);
        item.period = Override(row, "period", item.period);
        item.onSale = Override(row, "on_sale", item.onSale);
        item.gender = Override(row, "commodity_gender", item.gender);
        item.priority = Override(row, "priority", item.priority);
        item.bonus = Override(row, "bonus", item.bonus);
        item.maplePoint = Override(row, "maple_point", item.maplePoint);
        item.meso = Override(row, "meso", item.meso);
        item.forPremiumUser = Override(row, "for_premium_user", item.forPremiumUser);
        item.clz = Override(row, "class", item.clz);
        item.limit = Override(row, "limit", item.limit);
        item.pbCash = Override(row, "pb_cash", item.pbCash);
        item.pbPoint = Override(row, "pb_point", item.pbPoint);
        item.pbGift = Override(row, "pb_gift", item.pbGift);
        item.packageSn = Override(row, "package_sn", item.packageSn);
        return item;
    }
    static int Override(DbRow row, string key, int fallback) => row.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : fallback;
    // 商品信息存数据库：首次进入商城时把 WZ 里「完整且上架」的商品写进 modified_cash_item，
    // 之后商城一律以表为准（改价/上下架改表即可，和原端后台面板改这张表是一样的）。
    // 残缺商品（客户端取不到名字或图标）不入库，也就不会出现在列表里。
    bool imported;
    public void ImportCommodities() {
        if (imported || Catalog == null) return;
        imported = true;
        var table = db.Table("modified_cash_item");
        var existing = new HashSet<int>();
        foreach (var r in table.Select(x => true)) existing.Add(DbStore.Int(r, "sn"));
        int added = 0;
        foreach (var entry in Catalog.All) {
            if (entry.onSale != 1 || !Catalog.Complete(entry.itemId)) continue;
            if (existing.Contains(entry.sn)) continue;
            table.Insert(new DbRow {
                ["sn"] = (long)entry.sn, ["item_id"] = (long)entry.itemId,
                ["count"] = (long)entry.count, ["price"] = (long)entry.price,
                ["period"] = (long)entry.period, ["on_sale"] = (long)entry.onSale,
                ["commodity_gender"] = (long)entry.gender, ["priority"] = (long)entry.priority,
                ["class"] = (long)entry.clz, ["limit"] = (long)entry.limit,
                ["pb_cash"] = (long)entry.pbCash, ["pb_point"] = (long)entry.pbPoint,
                ["pb_gift"] = (long)entry.pbGift
            });
            added++;
        }
        if (added > 0) db.Save();
    }
    public CashResponse Handle(CashRequest r) {
        lock (db) {
            var c = CharactersDao.FindById(db, r.characterId); if (c == null) return new CashResponse { ok = false, error = "角色不存在" };
            ImportCommodities();
            Sweep();
            string error; db.BeginTransaction();
            try { error = Apply(c, r); if (error == null) db.Commit(); else db.Rollback(); }
            catch { db.Rollback(); throw; }
            db.Save(); var result = Read(CharactersDao.FindById(db, r.characterId)); result.ok = error == null; result.error = error ?? ""; return result;
        }
    }
    public void Sweep() {
        foreach (var owned in db.Table("cash_items").Select(x => DbStore.Int(x, "characterid") > 0)) {
            var inventory = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == DbStore.Long(owned, "inventoryitemid"));
            if (inventory == null) db.Table("cash_items").Delete(x => DbStore.Long(x, "id") == DbStore.Long(owned, "id"));
            else
            {
                owned["quantity"] = (long)DbStore.Int(inventory, "quantity");
                owned["expiration"]=DbStore.Long(inventory,"expiration",-1);
                owned["petid"]=DbStore.Long(inventory,"petid",-1);
            }
        }
        foreach (var row in db.Table("cash_items").Select(x => DbStore.Long(x, "expiration") > 0 && DbStore.Long(x, "expiration") <= Now)) {
            int itemId=DbStore.Int(row,"itemid"),petId=DbStore.Int(row,"petid",-1);
            if(itemId/1000==5000)
            {
                var pet=db.Table("pets").Find(x=>DbStore.Int(x,"petid")==petId);
                if(pet!=null) pet["summoned"]=0L;
                if(itemId!=5000054 && !GameConfigDao.GetBool(db,"use_erase_pet_on_expiration",false))
                {
                    row["expiration"]=-1L;
                    long heldId=DbStore.Long(row,"inventoryitemid");
                    var held=db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==heldId);
                    if(held!=null) held["expiration"]=-1L;
                    continue;
                }
                db.Table("petignores").Delete(x=>DbStore.Int(x,"petid")==petId);
                db.Table("pets").Delete(x=>DbStore.Int(x,"petid")==petId);
            }
            long inv = DbStore.Long(row, "inventoryitemid"); if (inv > 0) CharService.RemoveItem(db, inv);
            db.Table("cash_items").Delete(x => DbStore.Long(x, "id") == DbStore.Long(row, "id"));
        }
    }
    string Apply(CharRecord c, CashRequest r) {
        int cid = (int)c.id;
        if (r.operation == CashOperation.Open) { if (Catalog == null) return "商城目录资源未加载"; if (c.hp <= 0 || CanEnter != null && !CanEnter(c.map)) return "当前状态或地图不能进入商城"; opened.Add(cid); return null; }
        if (r.operation == CashOperation.Close) { opened.Remove(cid); return null; }
        if (r.operation == CashOperation.Read) return null;
        // Trusted original NPC scripts award/spend account cash outside the shop.
        if (r.operation == CashOperation.AdjustBalance) {
            var acc = AccountsDao.FindById(db, c.accountid); string col = Currency(r.currency);
            if (acc == null || col == null) return "无效账号或余额类型";
            long total = (long)DbStore.Int(acc, col) + r.amount;
            if (total < 0 || total > int.MaxValue) return "余额不足或超出上限";
            acc[col] = total; return null;
        }
        if (!opened.Contains(cid)) return "请先进入商城";
        if (r.operation == CashOperation.RedeemCode)
            return new Server.Bot.BotNXCodes(db).Redeem(c, r.message, Now);
        if (Catalog == null) return "商城目录资源未加载";
        var account = AccountsDao.FindById(db, c.accountid); if (account == null) return "账号不存在";
        var item = Commodity(r.sn);
        switch (r.operation) {
            case CashOperation.Buy:
                string invalid = CanBuy(c, item, account, r.currency); if (invalid != null) return invalid;
                var package = Expand(item); if (package == null || package.Count == 0) return "礼包目录不完整";
                if (Stock(c).Count + package.Count > 1000) return "现金仓库已满（最多1000件）";
                foreach (var part in package) { string why = ValidItem(c, part); if (why != null) return why; }
                Charge(account, r.currency, item.price);
                foreach (var part in package) AddStock(c, part, ""); return null;
            case CashOperation.Gift:
                invalid = CanBuy(c, item, account, 4); if (invalid != null) return invalid;
                if (string.IsNullOrEmpty(r.message) || r.message.Length > 73) return "赠言须为1～73个字符";
                string birthday = DbStore.Str(account, "birthday").Replace("-", ""); if (!int.TryParse(birthday, out int date) || date != r.birthday) return "生日验证失败";
                var targetRow = db.Table("characters").Find(x => string.Equals(DbStore.Str(x, "name"), r.target, StringComparison.OrdinalIgnoreCase));
                if (targetRow == null) return "收件角色不存在";
                if (DbStore.Int(targetRow, "accountid") == c.accountid) return "不能向同账号角色赠送";
                if (Expand(item) == null) return "礼包目录不完整";
                Charge(account, 4, item.price);
                db.Table("cash_gifts").Insert(new DbRow { ["characterid"] = (long)DbStore.Int(targetRow, "id"), ["sn"] = (long)item.sn, ["sender"] = c.name, ["message"] = r.message }); return null;
            case CashOperation.ClaimGifts:
                var gifts = db.Table("cash_gifts").Select(x => DbStore.Int(x, "characterid") == c.id);
                var claim = new List<KeyValuePair<CashCommodity, string>>();
                foreach (var gift in gifts) { var parts = Expand(Commodity(DbStore.Int(gift, "sn"))); if (parts == null) return "礼物目录缺失"; foreach (var p in parts) { if (!Catalog.Exists(p.itemId)) return "礼物道具数据缺失"; claim.Add(new KeyValuePair<CashCommodity, string>(p, DbStore.Str(gift, "sender"))); } }
                if (Stock(c).Count + claim.Count > 1000) return "现金仓库没有足够空间";
                foreach (var p in claim) AddStock(c, p.Key, p.Value);
                db.Table("cash_gifts").Delete(x => DbStore.Int(x, "characterid") == cid); return null;
            case CashOperation.Withdraw:
                var row = db.Table("cash_items").Find(x => DbStore.Long(x, "id") == r.itemId && Own(x, c) && DbStore.Int(x, "characterid") == 0);
                if (row == null) return "现金道具不存在或不属于此账号职业仓库";
                int id = DbStore.Int(row, "itemid"), type = id / 1000000; if (type == 1) type = 1;
                int limit = Limit(c, type), slot = FreeSlot(cid, type, limit); if (slot < 1) return "对应道具栏已满";
                long inventoryId = InventoryDao.InsertItem(db, cid, id, type, slot, DbStore.Int(row, "quantity"),DbStore.Int(row,"petid",-1));
                var inventory = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == inventoryId);
                inventory["expiration"] = DbStore.Long(row, "expiration"); inventory["giftFrom"] = DbStore.Str(row, "giftFrom"); inventory["accountid"] = (long)c.accountid;
                row["petid"]=DbStore.Long(inventory,"petid",-1);
                if (type == 1) { var stats = Catalog.Equipment(id) ?? new DbRow(); stats["inventoryitemid"] = inventoryId; db.Table("inventoryequipment").Insert(stats); }
                row["characterid"] = c.id; row["inventoryitemid"] = inventoryId; return null;
            case CashOperation.Deposit:
                row = db.Table("cash_items").Find(x => DbStore.Long(x, "id") == r.itemId && Own(x, c) && DbStore.Int(x, "characterid") == cid);
                if (row == null) return "只能存入当前角色持有的现金道具";
                var held = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == DbStore.Long(row, "inventoryitemid") && DbStore.Int(x, "characterid") == cid);
                if (held == null || DbStore.Int(held, "position") <= 0) return "请先卸下该现金装备或宠物";
                if (Stock(c).Count >= 1000) return "现金仓库已满";
                row["quantity"] = (long)DbStore.Int(held, "quantity");row["petid"]=DbStore.Long(held,"petid",-1);row["expiration"]=DbStore.Long(held,"expiration",-1);
                CharService.RemoveItem(db, DbStore.Long(row, "inventoryitemid")); row["inventoryitemid"] = 0L; row["characterid"] = 0L; return null;
            case CashOperation.Equip:
            case CashOperation.Unequip:
                row = db.Table("cash_items").Find(x => DbStore.Long(x, "id") == r.itemId && Own(x, c) && DbStore.Int(x, "characterid") == cid);
                if (row == null) return "装备不属于当前角色";
                held = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == DbStore.Long(row, "inventoryitemid"));
                if (held == null || DbStore.Int(held, "inventorytype") != 1) return "这不是装备";
                int equipId = DbStore.Int(held, "itemid");
                if (r.operation == CashOperation.Equip) {
                    string why = Catalog.CanEquip(equipId, c); if (why != null) return why;
                    int destination = Catalog.EquipSlot(equipId); if (destination == 0) return "此类装备没有可用槽位";
                    int oldPosition = DbStore.Int(held, "position"); if (oldPosition < 1) return "已经装备";
                    var previous = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "inventorytype") == 1 && DbStore.Int(x, "position") == destination);
                    if (previous != null) previous["position"] = (long)oldPosition;
                    held["position"] = (long)destination;
                } else {
                    if (DbStore.Int(held, "position") > 0) return "该装备没有穿戴";
                    int free = FreeSlot(cid, 1, c.equipslots); if (free == 0) return "装备栏已满";
                    held["position"] = (long)free;
                }
                return null;
            case CashOperation.WishList:
                if (r.wishes == null || r.wishes.Length > 10) return "愿望清单最多10项";
                foreach (int sn in r.wishes) if (Commodity(sn)?.onSale != 1) return "愿望清单包含未出售商品";
                db.Table("cash_wishlist").Delete(x => DbStore.Int(x, "characterid") == cid);
                foreach (int sn in r.wishes) db.Table("cash_wishlist").Insert(new DbRow { ["characterid"] = c.id, ["sn"] = (long)sn }); return null;
            case CashOperation.ExpandInventory:
                string currency = Currency(r.currency); if (currency == null) return "无效余额类型";
                if (r.inventoryType < 1 || r.inventoryType > 4) return "不能扩充此道具栏";
                int current = Limit(c, r.inventoryType); if (current + 4 > 96) return "道具栏已达上限";
                if (DbStore.Int(account, currency) < 4000) return "余额不足，扩充4格需要4000点";
                Charge(account, r.currency, 4000);
                string col = r.inventoryType == 1 ? "equipslots" : r.inventoryType == 2 ? "useslots" : r.inventoryType == 3 ? "setupslots" : "etcslots";
                db.Table("characters").Update(x => DbStore.Int(x, "id") == cid, new DbRow { [col] = (long)(current + 4) }); return null;
            case CashOperation.ExpandCharacters:
                invalid = CanBuy(c, item, account, r.currency); if (invalid != null) return invalid;
                if (item.itemId != 5430000) return "该商品不是角色栏扩充券";
                if (DbStore.Int(account, "characterslots") >= 15) return "角色栏已达上限";
                Charge(account, r.currency, item.price); account["characterslots"] = (long)(DbStore.Int(account, "characterslots") + 1); return null;
        }
        return "无效商城操作";
    }
    string CanBuy(CharRecord c, CashCommodity item, DbRow account, int currency) {
        string col = Currency(currency); if (col == null) return "无效余额类型";
        if (item == null || item.onSale != 1 || item.price < 0 || item.count < 1 || item.period < -1) return "商品未出售或数据无效";
        if (DbStore.Int(account, col) < item.price) return "余额不足";
        return ValidItem(c, item);
    }
    string ValidItem(CharRecord c, CashCommodity item) {
        if (item == null || !Catalog.Exists(item.itemId)) return "道具资源缺失";
        int category = item.itemId / 10000;
        if ((category == 503 || category == 514) && c.level < 16) return "达到16级才能购买商店道具";
        if ((item.itemId / 1000 == 5211 || item.itemId / 1000 == 5360) && !GameConfigDao.GetBool(db, "use_supply_rate_coupons")) return "当前服务器不出售倍率券";
        if (category == 543 && item.itemId != 5430000 && c.level < 30) return "达到30级才能购买此道具";
        return null;
    }
    List<CashCommodity> Expand(CashCommodity item) {
        if (item == null) return null;
        var package = Catalog.Package(item.itemId); if (package == null) return item.itemId / 10000 == 910 ? null : new List<CashCommodity> { item };
        var result = new List<CashCommodity>(); foreach (int sn in package) { var part = Commodity(sn); if (part == null || Catalog.Package(part.itemId) != null) return null; result.Add(part); } return result;
    }
    static void Charge(DbRow account, int currency, int price) { string col = Currency(currency); account[col] = (long)(DbStore.Int(account, col) - price); }
    void AddStock(CharRecord c, CashCommodity item, string sender) {
        db.Table("cash_items").Insert(new DbRow { ["accountid"] = (long)c.accountid, ["world"] = (long)c.world, ["jobgroup"] = (long)Group(c), ["sn"] = (long)item.sn, ["itemid"] = (long)item.itemId, ["quantity"] = (long)item.count, ["expiration"] = Expiration(item, Now), ["giftFrom"] = sender });
    }
    public static long Expiration(CashCommodity item, long now) {
        if (item.period == -1) return -1;
        if (item.period == 1) {
            if (item.itemId == 5211048 || item.itemId == 5360042) return now + 4 * 3600000L;
            if (item.itemId == 5211901 || item.itemId == 5360901 || item.itemId == 5211902 || item.itemId == 5360902) return now + 30 * 60000L;
            if (item.itemId == 5211060) return now + 2 * 3600000L;
        }
        return now + (item.period == 0 ? 90 : item.period) * 86400000L;
    }
    int Limit(CharRecord c, int type) => type == 1 ? c.equipslots : type == 2 ? c.useslots : type == 3 ? c.setupslots : type == 4 ? c.etcslots : 96;
    int FreeSlot(int cid, int type, int max) { var used = new HashSet<int>(); foreach (var row in InventoryDao.ItemsByCharacter(db, cid)) if (DbStore.Int(row, "inventorytype") == type) used.Add(DbStore.Int(row, "position")); for (int i = 1; i <= max; i++) if (!used.Contains(i)) return i; return 0; }
    CashResponse Read(CharRecord c) {
        var account = AccountsDao.FindById(db, c.accountid);
        var result = new CashResponse { opened = opened.Contains((int)c.id), credit = DbStore.Int(account, "nxCredit"), points = DbStore.Int(account, "maplePoint"), prepaid = DbStore.Int(account, "nxPrepaid"), character = CharService.Load(db, (int)c.id) };
        // 商城列表读 modified_cash_item（数据库），不直接遍历 WZ；残缺商品不展示。
        if (Catalog != null) foreach (var row in db.Table("modified_cash_item").Select(x => true)) {
            var item = Commodity(row, DbStore.Int(row, "sn"));
            if (item != null && item.itemId != 0 && item.onSale == 1 && Catalog.Complete(item.itemId)) result.catalog.Add(item);
        }
        result.catalog.Sort((a, b) => a.sn.CompareTo(b.sn));
        foreach (var row in db.Table("cash_items").Select(x => Own(x, c) && (DbStore.Int(x, "characterid") == 0 || DbStore.Int(x, "characterid") == c.id)))
            result.stock.Add(new CashStock { id = DbStore.Long(row, "id"), sn = DbStore.Int(row, "sn"), itemId = DbStore.Int(row, "itemid"), quantity = DbStore.Int(row, "quantity"), expiration = DbStore.Long(row, "expiration"), characterId = DbStore.Int(row, "characterid"), giftFrom = DbStore.Str(row, "giftFrom") });
        foreach (var row in db.Table("cash_wishlist").Select(x => DbStore.Int(x, "characterid") == c.id)) result.wishes.Add(DbStore.Int(row, "sn"));
        result.gifts = db.Table("cash_gifts").Select(x => DbStore.Int(x, "characterid") == c.id).Count; return result;
    }
}
