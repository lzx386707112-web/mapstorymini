// 商城管理（源端 CashShopController + CashShopService，URL 与 DTO 逐字段对齐）。
//
// 数据源：WZ Etc/Commodity.img 是基线（13 个本版真实存在的字段，解析语义与
// NativeCashCatalog 一致），modified_cash_item 表是覆盖层（列级 null 保留，
// 等价于源端 insertSelective + setDbItemValue）。游戏内 NativeCashService.Commodity
// 每次都实时读表，所以后台改完立即生效，无需缓存失效。
using System;
using System.Collections.Generic;
using System.Globalization;
using MapleDash.Wz;

public static class AdminCashShop
{
    // 源端 CategoryType 枚举（en 资源就是这些大写名；id 是 sn 的首位数字）
    static readonly Dictionary<int, string> CategoryNames = new Dictionary<int, string>
    {
        { 8, "MAIN" }, { 1, "EVENT" }, { 2, "EQUIP" }, { 3, "USE" },
        { 4, "SET-UP" }, { 5, "ETC" }, { 6, "PET" }, { 7, "PACKAGE" },
    };

    public static void Register(AdminApi api)
    {
        api.Get("/cashShop/v1/getAllCategoryList", ctx => Categories());
        api.Post("/cashShop/v1/getCommodityByCategory", CommodityPage);
        api.Post("/cashShop/v1/onSale", ctx => ChangeOnSale(ctx, 1));
        api.Post("/cashShop/v1/offSale", ctx => ChangeOnSale(ctx, 0));
        api.Post("/cashShop/v1/batchOnSale", BatchOnSale);
    }

    // —————————— 分类 ——————————
    // Etc/Category.img 每个子节点：Category(大类id) / CategorySub(子类id) / Name(子类名)。
    static object Categories()
    {
        var list = new List<object>();
        var root = WzAssets.FileRoot("Etc/Category.img");
        foreach (var n in root.Children)
        {
            int id = n.Int("Category", -1);
            if (id < 0) continue;
            list.Add(AdminJson.Obj(
                "id", (long)id,
                "name", CategoryNames.TryGetValue(id, out var name) ? name : "",
                "subId", (long)n.Int("CategorySub", 0),
                "subName", n.Text("Name", "")));
        }
        return list;
    }

    // ——————————— 商品基线（Commodity.img，与 NativeCashCatalog 同字段集） ———————————
    sealed class Baseline
    {
        public int sn, itemId, price, count = 1, period = 1, onSale, gender = 2;
        public int priority, bonus, maplePoint, meso, forPremiumUser, clz, limit;
        public int pbCash, pbPoint, pbGift, packageSn;
    }

    static Dictionary<int, Baseline> baselineCache;

    static Dictionary<int, Baseline> Baselines()
    {
        if (baselineCache != null) return baselineCache;
        var map = new Dictionary<int, Baseline>();
        var root = WzAssets.FileRoot("Etc/Commodity.img");
        foreach (var n in root.Children)
        {
            if (n.Get("SN") == null || n.Get("ItemId") == null) continue;
            int period = n.Int("Period", 1);
            var item = new Baseline
            {
                sn = n.Int("SN"),
                itemId = n.Int("ItemId"),
                price = n.Int("Price"),
                count = n.Int("Count", 1),
                period = period == 0 ? 90 : period,
                onSale = n.Int("OnSale"),
                gender = n.Int("Gender", 2),
                priority = n.Int("Priority"),
                clz = n.Int("Class"),
                limit = IntOf(n, "Limit"),
                pbCash = n.Int("PbCash"),
                pbPoint = n.Int("PbPoint"),
                pbGift = n.Int("PbGift"),
            };
            map[item.sn] = item;
        }
        baselineCache = map;
        return map;
    }

    // 本版 Commodity.img 的 Limit 有 92 行是字符串（源端 CashShop.java 注释掉了
    // getInteger("Limit")），按「类型不符取默认值」语义解析。
    static int IntOf(ImgNode node, string key, int fallback = 0)
    {
        var value = node.Get(key)?.Value;
        if (value == null) return fallback;
        if (value is int number) return number;
        if (value is string text && int.TryParse(text, out int parsed)) return parsed;
        try { return Convert.ToInt32(value); } catch { return fallback; }
    }

    // ——————————— 商品分页（源端 getCommodityByCategory） ———————————
    static object CommodityPage(AdminCtx ctx)
    {
        var body = ctx.Body;
        int id = AdminJson.GetInt(body, "id");
        int subId = AdminJson.GetInt(body, "subId");
        if (id <= 0) throw new AdminApiException("参数不能为空: id");
        if (subId <= 0 && AdminJson.Get(body, "subId") == null) throw new AdminApiException("参数不能为空: subId");
        // 与客户端保持一致，固定每页 10 条（源端强制 pageSize=10）
        int pageNo = AdminJson.GetInt(body, "pageNo", 1);
        bool onSaleFilter = AdminJson.Get(body, "onSale") != null && AdminJson.GetBool(body, "onSale");
        long itemIdFilter = AdminJson.GetLong(body, "itemId");

        string prefix = id.ToString() + subId.ToString("D2");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            var baselines = Baselines();

            // 覆盖层：sn -> 行（列级 null 保留）
            var overlay = new Dictionary<int, DbRow>();
            foreach (var r in db.Table("modified_cash_item").rows)
                overlay[DbStore.Int(r, "sn")] = r;

            string categoryName = CategoryNames.TryGetValue(id, out var cn) ? cn : "";
            string subName = "";
            foreach (var n in WzAssets.FileRoot("Etc/Category.img").Children)
                if (n.Int("Category", -1) == id && n.Int("CategorySub", -1) == subId) { subName = n.Text("Name", ""); break; }

            var matched = new List<Dictionary<string, object>>();
            foreach (var b in baselines.Values)
            {
                if (!b.sn.ToString(CultureInfo.InvariantCulture).StartsWith(prefix, StringComparison.Ordinal)) continue;
                var dto = ToDto(b, overlay.TryGetValue(b.sn, out var row) ? row : null, id, categoryName, subId, subName);
                if (AdminJson.Get(body, "onSale") != null && (dto["onSale"] is int onSale ? onSale == 1 : false) != onSaleFilter) continue;
                if (itemIdFilter > 0 && (long)dto["itemId"] != itemIdFilter) continue;
                matched.Add(dto);
            }

            // priority 降序 → itemId 升序（源端 Comparator 链）
            matched.Sort((a, b2) =>
            {
                int r = ((int)b2["priority"]).CompareTo((int)a["priority"]);
                return r != 0 ? r : ((long)a["itemId"]).CompareTo((long)b2["itemId"]);
            });

            var records = new List<object>();
            int start = (Math.Max(1, pageNo) - 1) * 10;
            for (int i = start; i < matched.Count && i < start + 10; i++) records.Add(matched[i]);
            return AdminJson.Page(records, pageNo, 10, matched.Count);
        });
    }

    // 源端 fromCashItem + setDbItemValue：default* 是 WZ 原值，当前值被覆盖层替换
    static Dictionary<string, object> ToDto(Baseline b, DbRow row, int categoryId, string categoryName, int subId, string subName)
    {
        int Ov(string col, int dflt) => row != null && row[col] != null ? DbStore.Int(row, col) : dflt;
        return AdminJson.Obj(
            "categoryId", (long)categoryId,
            "categoryName", categoryName,
            "subcategoryId", (long)subId,
            "subcategoryName", subName,
            "sn", (long)b.sn,
            "itemId", (long)Ov("item_id", b.itemId),
            "itemName", AdminNames.ItemName(Ov("item_id", b.itemId)),
            "price", (long)Ov("price", b.price),
            "defaultPrice", (long)b.price,
            "count", (long)Ov("count", b.count),
            "defaultCount", (long)b.count,
            "period", (long)Ov("period", b.period),
            "defaultPeriod", (long)b.period,
            "priority", (long)Ov("priority", b.priority),
            "defaultPriority", (long)b.priority,
            "onSale", (long)Ov("on_sale", b.onSale),
            "defaultOnSale", (long)b.onSale,
            "bonus", (long)Ov("bonus", b.bonus),
            "defaultBonus", (long)b.bonus,
            "maplePoint", (long)Ov("maple_point", b.maplePoint),
            "defaultMaplePoint", (long)b.maplePoint,
            "meso", (long)Ov("meso", b.meso),
            "defaultMeso", (long)b.meso,
            "forPremiumUser", (long)Ov("for_premium_user", b.forPremiumUser),
            "defaultForPremiumUser", (long)b.forPremiumUser,
            "gender", (long)Ov("commodity_gender", b.gender),
            "defaultGender", (long)b.gender,
            "clz", (long)Ov("class", b.clz),
            "defaultClz", (long)b.clz,
            "limit", (long)Ov("limit", b.limit),
            "defaultLimit", (long)b.limit,
            "pbCash", (long)Ov("pb_cash", b.pbCash),
            "defaultPBCash", (long)b.pbCash,
            "pbPoint", (long)Ov("pb_point", b.pbPoint),
            "defaultPBPoint", (long)b.pbPoint,
            "pbGift", (long)Ov("pb_gift", b.pbGift),
            "defaultPBGift", (long)b.pbGift,
            "packageSn", (long)Ov("package_sn", b.packageSn),
            "defaultPackageSn", (long)b.packageSn);
    }

    // ——————————— 上架 / 下架（源端 changeOnSale） ———————————
    // 覆盖层只存与 WZ 基线不同的列（insertSelective 语义），下架只写 on_sale=0。
    static object ChangeOnSale(AdminCtx ctx, int onSale) => ChangeOnSaleCore(ctx.Body, onSale);

    static object ChangeOnSaleCore(object body, int onSale)
    {
        long sn = AdminJson.GetLong(body, "sn");
        if (sn <= 0) throw new AdminApiException("参数不能为空: sn");
        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            if (!Baselines().TryGetValue((int)sn, out var wz))
                throw new AdminApiException("未知参数值: sn=" + sn);
            var table = db.Table("modified_cash_item");
            table.Delete(r => DbStore.Int(r, "sn") == (int)sn);

            if (onSale != 1)
            {
                // 下架：只有 WZ 里原本在卖的才需要覆盖
                if (wz.onSale == 1)
                    table.Insert(new DbRow { ["sn"] = (long)sn, ["on_sale"] = 0L });
                return AdminJson.Ok();
            }

            var row = new DbRow { ["sn"] = (long)sn };
            PutIfChanged(row, "item_id", AdminJson.Get(body, "itemId"), wz.itemId);
            PutIfChanged(row, "price", AdminJson.Get(body, "price"), wz.price);
            PutIfChanged(row, "period", AdminJson.Get(body, "period"), wz.period);
            PutIfChanged(row, "priority", AdminJson.Get(body, "priority"), wz.priority);
            PutIfChanged(row, "count", AdminJson.Get(body, "count"), wz.count);
            PutIfChanged(row, "on_sale", AdminJson.Get(body, "onSale"), wz.onSale);
            PutIf(row, "bonus", AdminJson.Get(body, "bonus"));
            PutIf(row, "maple_point", AdminJson.Get(body, "maplePoint"));
            PutIf(row, "meso", AdminJson.Get(body, "meso"));
            PutIf(row, "for_premium_user", AdminJson.Get(body, "forPremiumUser"));
            PutIf(row, "commodity_gender", AdminJson.Get(body, "commodityGender"));
            PutIf(row, "class", AdminJson.Get(body, "class"));
            PutIf(row, "limit", AdminJson.Get(body, "limit"));
            PutIf(row, "pb_cash", AdminJson.Get(body, "pbCash"));
            PutIf(row, "pb_point", AdminJson.Get(body, "pbPoint"));
            PutIf(row, "pb_gift", AdminJson.Get(body, "pbGift"));
            PutIf(row, "package_sn", AdminJson.Get(body, "packageSn"));
            table.Insert(row);
            return AdminJson.Ok();
        });
    }

    // 请求里没带的列不写（selective）；与 WZ 基线相同的值也不写（省空间，语义同源端置 null）
    static void PutIfChanged(DbRow row, string col, object value, int baseline)
    {
        if (value == null) return;
        int number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (number == baseline) return;
        row[col] = (long)number;
    }

    static void PutIf(DbRow row, string col, object value)
    {
        if (value == null) return;
        row[col] = (long)Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    // ——————————— 批量上架（源端 batchChangeOnSale） ———————————
    // type: "价格"/"数量"/"有效期"（源端中文 switch，前端也发中文）；全部强制 onSale=1。
    static object BatchOnSale(AdminCtx ctx)
    {
        var body = ctx.Body;
        var data = AdminJson.GetList(body, "data");
        string type = AdminJson.GetString(body, "type");
        long value = AdminJson.GetLong(body, "value");
        if (data == null || data.Count == 0) throw new AdminApiException("参数不能为空: data");
        foreach (var item in data)
        {
            var form = AdminJson.AsDict(item);
            if (form == null) continue;
            form["onSale"] = 1;
            if (type == "价格") form["price"] = value;
            else if (type == "数量") form["count"] = value;
            else if (type == "有效期") form["period"] = value;
            ChangeOnSaleCore(form, 1);
        }
        return AdminJson.Ok();
    }
}
