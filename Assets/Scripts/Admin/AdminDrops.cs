// 后台「掉落」模块：/drop/v1/*（源端 DropController + DropService）。
//
// 普通掉落与全局掉落是两套 URL + 两张表（drop_data / drop_data_global），字段同构。
// 名称类字段（dropperName/itemName/questName）源端查 WZ，这里走 AdminNames（运行期注入）。
// 写操作之后必须 DropStore.Invalidate()：游戏内 FieldLoot 是从 DropStore 缓存掉东西的，
// 不清缓存就会出现「后台改了、游戏里没变」（对应源端 MonsterInformationProvider.clearDrops）。
//
// 约束：纯 C#，不引用 UnityEngine。
using System;
using System.Collections.Generic;

public static class AdminDrops
{
    public static void Register(AdminApi api)
    {
        api.Post("/drop/v1/getDropList", ctx => Query(ctx, false));
        api.Post("/drop/v1/getGlobalDropList", ctx => Query(ctx, true));

        api.Put("/drop/v1/addDropData", ctx => Save(ctx, false, true));
        api.Put("/drop/v1/addGlobalDropData", ctx => Save(ctx, true, true));

        api.Post("/drop/v1/updateDropData", ctx => Save(ctx, false, false));
        api.Post("/drop/v1/updateGlobalDropData", ctx => Save(ctx, true, false));

        api.Delete("/drop/v1/deleteDropData/{id}", ctx => Delete(ctx, false));
        api.Delete("/drop/v1/deleteGlobalDropData/{id}", ctx => Delete(ctx, true));
    }

    static string Table(bool global) => global ? "drop_data_global" : "drop_data";

    // —————————— 查询 ——————————
    // 源端 DropSearchReqDTO：dropperId/dropperName（仅普通）/continent（仅全局）/
    // itemId/itemName/questId + BasePageDTO(pageNo/pageSize/onlyTotal/notPage)。
    static object Query(AdminCtx ctx, bool global)
    {
        long dropperId = ctx.BodyLong("dropperId");
        string dropperName = ctx.BodyString("dropperName");
        long continent = ctx.BodyLong("continent", -2);   // -2 = 未传
        long itemId = ctx.BodyLong("itemId", -1);         // -1 = 未传（0 是金币，合法值）
        string itemName = ctx.BodyString("itemName");
        long questId = ctx.BodyLong("questId", -1);
        int pageNo = ctx.BodyInt("pageNo", 1);
        int pageSize = ctx.BodyInt("pageSize", 20);
        bool onlyTotal = ctx.BodyBool("onlyTotal");
        bool notPage = ctx.BodyBool("notPage");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.Db;
            var matched = new List<DbRow>();
            if (db != null && db.HasTable(Table(global)))
            {
                foreach (var row in db.Table(Table(global)).rows)
                {
                    if (dropperId > 0 && !global && DbStore.Long(row, "dropperid") != dropperId) continue;
                    if (continent != -2 && global && DbStore.Long(row, "continent", -1) != continent) continue;
                    if (itemId >= 0 && DbStore.Long(row, "itemid") != itemId) continue;
                    if (questId >= 0 && DbStore.Long(row, "questid") != questId) continue;
                    if (!string.IsNullOrEmpty(dropperName) && !global &&
                        AdminNames.MobName((int)DbStore.Long(row, "dropperid"))
                            .IndexOf(dropperName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!string.IsNullOrEmpty(itemName) &&
                        AdminNames.ItemName((int)DbStore.Long(row, "itemid"))
                            .IndexOf(itemName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    matched.Add(row);
                }
            }

            // 源端按 id 升序返回（SQL 默认主键序）
            matched.Sort((a, b) => DbStore.Long(a, "id").CompareTo(DbStore.Long(b, "id")));

            return AdminDbUtil.Page(matched, pageNo, pageSize, onlyTotal, notPage,
                row => Dto(row, global));
        });
    }

    static Dictionary<string, object> Dto(DbRow row, bool global)
    {
        long dropperId = DbStore.Long(row, "dropperid");
        long itemId = DbStore.Long(row, "itemid");
        return AdminJson.Obj(
            "id", DbStore.Long(row, "id"),
            "dropperId", global ? 0L : dropperId,
            "dropperName", global ? "" : AdminNames.MobName((int)dropperId),
            "continent", DbStore.Long(row, "continent", -1),
            "itemId", itemId,
            "itemName", AdminNames.ItemName((int)itemId),
            "itemCategory", AdminNames.ItemCategory((int)itemId),
            "minimumQuantity", DbStore.Long(row, "minimum_quantity", 1),
            "maximumQuantity", DbStore.Long(row, "maximum_quantity", 1),
            "questId", DbStore.Long(row, "questid"),
            "questName", AdminNames.QuestName((int)DbStore.Long(row, "questid")),
            "chance", DbStore.Long(row, "chance"),
            "comments", DbStore.Str(row, "comments"));
    }

    // —————————— 新增 / 更新 ——————————
    // 源端 modifyDropData(data, isGlobal, isDelete) 是同一个入口；add 时把 id 置 null。
    static object Save(AdminCtx ctx, bool global, bool isAdd)
    {
        var form = AdminJson.AsDict(ctx.Body);
        if (form == null) throw new AdminApiException("缺少掉落数据");
        long id = AdminJson.GetLong(form, "id");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            if (!db.HasTable(Table(global))) throw new AdminApiException("掉落表 " + Table(global) + " 尚未初始化（请先升级存档）");
            var table = db.Table(Table(global));

            var values = new DbRow();
            if (!global) AdminDbUtil.Set(values, "dropperid", AdminJson.GetLong(form, "dropperId"));
            if (global) AdminDbUtil.Set(values, "continent", AdminJson.GetLong(form, "continent", -1));
            AdminDbUtil.Set(values, "itemid", AdminJson.GetLong(form, "itemId"));
            AdminDbUtil.Set(values, "minimum_quantity", AdminJson.GetLong(form, "minimumQuantity", 1));
            AdminDbUtil.Set(values, "maximum_quantity", AdminJson.GetLong(form, "maximumQuantity", 1));
            AdminDbUtil.Set(values, "questid", AdminJson.GetLong(form, "questId"));
            AdminDbUtil.Set(values, "chance", AdminJson.GetLong(form, "chance"));
            if (table.Col("comments") != null) AdminDbUtil.Set(values, "comments", AdminJson.GetString(form, "comments"));

            long newId;
            if (isAdd || id <= 0)
            {
                newId = table.Insert(values);
            }
            else
            {
                if (table.Find(DbStore.Eq("id", id)) == null) throw new AdminApiException("掉落记录不存在: " + id);
                table.Update(DbStore.Eq("id", id), values);
                newId = id;
            }
            db.Save();
            DropStore.Invalidate();
            return (object)newId;
        });
    }

    static object Delete(AdminCtx ctx, bool global)
    {
        long id = ctx.ParamLong("id");
        return AdminMainThread.Run<object>(() =>
        {
            var db = AdminBackend.RequireDb();
            if (db.HasTable(Table(global)))
            {
                db.Table(Table(global)).Delete(DbStore.Eq("id", id));
                db.Save();
                DropStore.Invalidate();
            }
            return null;
        });
    }
}
