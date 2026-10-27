// 后台「商店」模块：/shop/v1/*（源端 ShopController + ShopService）。
//
// 表 shops(shopid,npcid) + shopitems(shopitemid,shopid,itemid,price,pitch,position)。
// 源端 getShopList 是「SQL join 过滤 + 内存按名字模糊 + 去重 + 内存分页」，这里照抄；
// npcName/itemName/itemDesc 由 AdminNames 注入（运行期查 WZ）。
// 注：游戏内 NPC 商店界面（openShopNPC）尚未接入，本模块先维护数据，进商店买/卖是另一条线。
//
// 约束：纯 C#，不引用 UnityEngine。
using System;
using System.Collections.Generic;

public static class AdminShop
{
    public static void Register(AdminApi api)
    {
        api.Post("/shop/v1/getShopList", GetShopList);
        api.Post("/shop/v1/getShopItemList", GetShopItemList);
        api.Get("/shop/v1/getShopItem/{id}", ctx =>
        {
            long id = ctx.ParamLong("id");
            return AdminMainThread.Run(() =>
            {
                var db = AdminBackend.Db;
                var row = db != null && db.HasTable("shopitems")
                    ? db.Table("shopitems").Find(DbStore.Eq("shopitemid", id))
                    : null;
                return row == null ? null : ItemDto(row);
            });
        });
        api.Put("/shop/v1/addShopItem", ctx => SaveItem(ctx, true));
        api.Post("/shop/v1/updateShopItem", ctx => SaveItem(ctx, false));
        api.Delete("/shop/v1/deleteShopItem/{id}", ctx =>
        {
            long id = ctx.ParamLong("id");
            return AdminMainThread.Run<object>(() =>
            {
                var db = AdminBackend.RequireDb();
                if (db.HasTable("shopitems"))
                {
                    db.Table("shopitems").Delete(DbStore.Eq("shopitemid", id));
                    db.Save();
                }
                return null;
            });
        });
    }

    // 商店列表：一店一行（同 shopid 的商品行折叠成一条）
    static object GetShopList(AdminCtx ctx)
    {
        long shopId = ctx.BodyLong("shopId", -1);
        long npcId = ctx.BodyLong("npcId", -1);
        string npcName = ctx.BodyString("npcName");
        long itemId = ctx.BodyLong("itemId", -1);
        string itemName = ctx.BodyString("itemName");
        int pageNo = ctx.BodyInt("pageNo", 1);
        int pageSize = ctx.BodyInt("pageSize", 20);
        bool onlyTotal = ctx.BodyBool("onlyTotal");
        bool notPage = ctx.BodyBool("notPage");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.Db;
            var shops = new Dictionary<long, long>();     // shopId -> npcId
            if (db != null && db.HasTable("shops"))
                foreach (var row in db.Table("shops").rows)
                    shops[DbStore.Long(row, "shopid")] = DbStore.Long(row, "npcid");

            // itemId / itemName 过滤：先找命中的商品所在商店
            if (itemId >= 0 || !string.IsNullOrEmpty(itemName))
            {
                var keep = new HashSet<long>();
                if (db != null && db.HasTable("shopitems"))
                    foreach (var row in db.Table("shopitems").rows)
                    {
                        long item = DbStore.Long(row, "itemid");
                        if (itemId >= 0 && item != itemId) continue;
                        if (!string.IsNullOrEmpty(itemName) &&
                            AdminNames.ItemName((int)item).IndexOf(itemName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        keep.Add(DbStore.Long(row, "shopid"));
                    }
                var filtered = new Dictionary<long, long>();
                foreach (var kv in shops) if (keep.Contains(kv.Key)) filtered[kv.Key] = kv.Value;
                shops = filtered;
            }

            var list = new List<KeyValuePair<long, long>>();
            foreach (var kv in shops)
            {
                if (shopId >= 0 && kv.Key != shopId) continue;
                if (npcId >= 0 && kv.Value != npcId) continue;
                if (!string.IsNullOrEmpty(npcName) &&
                    AdminNames.NpcName((int)kv.Value).IndexOf(npcName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(kv);
            }
            list.Sort((a, b) => a.Key.CompareTo(b.Key));

            return AdminDbUtil.Page(list, pageNo, pageSize, onlyTotal, notPage,
                kv => AdminJson.Obj("shopId", kv.Key, "npcId", kv.Value, "npcName", AdminNames.NpcName((int)kv.Value)));
        });
    }

    // 商品列表：源端只按 shopId 过滤（itemId/itemName 不参与），直接分页
    static object GetShopItemList(AdminCtx ctx)
    {
        long shopId = ctx.BodyLong("shopId", -1);
        int pageNo = ctx.BodyInt("pageNo", 1);
        int pageSize = ctx.BodyInt("pageSize", 20);
        bool onlyTotal = ctx.BodyBool("onlyTotal");
        bool notPage = ctx.BodyBool("notPage");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.Db;
            var rows = new List<DbRow>();
            if (db != null && db.HasTable("shopitems"))
                foreach (var row in db.Table("shopitems").rows)
                    if (shopId < 0 || DbStore.Long(row, "shopid") == shopId)
                        rows.Add(row);
            rows.Sort((a, b) =>
            {
                int byShop = DbStore.Long(a, "shopid").CompareTo(DbStore.Long(b, "shopid"));
                return byShop != 0 ? byShop : DbStore.Long(a, "position").CompareTo(DbStore.Long(b, "position"));
            });
            return AdminDbUtil.Page(rows, pageNo, pageSize, onlyTotal, notPage, ItemDto);
        });
    }

    static Dictionary<string, object> ItemDto(DbRow row)
    {
        long itemId = DbStore.Long(row, "itemid");
        return AdminJson.Obj(
            "id", DbStore.Long(row, "shopitemid"),
            "shopId", DbStore.Long(row, "shopid"),
            "itemId", itemId,
            "price", DbStore.Long(row, "price"),
            "pitch", DbStore.Long(row, "pitch"),
            "position", DbStore.Long(row, "position"),
            "itemName", AdminNames.ItemName((int)itemId),
            "itemDesc", AdminNames.ItemDescription((int)itemId));
    }

    static object SaveItem(AdminCtx ctx, bool isAdd)
    {
        var form = AdminJson.AsDict(ctx.Body);
        if (form == null) throw new AdminApiException("缺少商品数据");
        long id = AdminJson.GetLong(form, "id");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            if (!db.HasTable("shopitems")) throw new AdminApiException("商店表尚未初始化（请先升级存档）");
            var table = db.Table("shopitems");

            long shopId = AdminJson.GetLong(form, "shopId");
            if (shopId <= 0) throw new AdminApiException("商店 ID 不能为空");
            EnsureShop(db, shopId, AdminJson.GetLong(form, "npcId"));

            var values = new DbRow();
            AdminDbUtil.Set(values, "shopid", shopId);
            AdminDbUtil.Set(values, "itemid", AdminJson.GetLong(form, "itemId"));
            AdminDbUtil.Set(values, "price", AdminJson.GetLong(form, "price"));
            AdminDbUtil.Set(values, "pitch", AdminJson.GetLong(form, "pitch", 0));
            AdminDbUtil.Set(values, "position", AdminJson.GetLong(form, "position", 0));

            long newId;
            if (isAdd || id <= 0) newId = table.Insert(values);
            else
            {
                if (table.Find(DbStore.Eq("shopitemid", id)) == null) throw new AdminApiException("商品不存在: " + id);
                table.Update(DbStore.Eq("shopitemid", id), values);
                newId = id;
            }
            db.Save();
            return (object)newId;
        });
    }

    static void EnsureShop(DbStore db, long shopId, long npcId)
    {
        if (!db.HasTable("shops")) return;
        var shops = db.Table("shops");
        if (shops.Find(DbStore.Eq("shopid", shopId)) != null) return;
        shops.Insert(new DbRow { ["shopid"] = shopId, ["npcid"] = npcId > 0 ? npcId : 0L });
    }
}
