// 图鉴模块路由表：物品 / 装备 / 怪物 / 技能图鉴 + 怪物掉落 + 图标 PNG。
//
// URL 与源端 gms-server 控制器一致（返回形状对齐前端 AdminUi/src/api/*.ts）：
//   GET    /setItem/v1/item/catalog            物品图鉴（分页 + 分类分面）
//   GET    /setItem/v1/equipment/catalog       装备图鉴（分页 + 分类/武器种类/现金分面）
//   GET    /mob/v1/catalog                     怪物图鉴
//   GET    /mob/v1/{mobId}/drops               怪物掉落列表
//   PUT    /mob/v1/{mobId}/drops               新增掉落
//   DELETE /mob/v1/drops/{dropId}              删除掉落
//   GET    /skill/v1/catalog                   技能图鉴（分页 + 职业分面）
//   GET    /skill/v1/detail/{skillId}          技能详情
//   GET    /skill/v1/jobs                      技能职业分类
//   POST   /skill/v1/reload                    重建技能缓存
//   GET    /assets/item-icons/{id}.png         图标 PNG（源端是 atlas 切图，本项目从 WZ 画布编码）
//   GET    /assets/equipment-icons/{id}.png
//   GET    /assets/mob-icons/{id}.png
//   GET    /assets/skill-icons/{id}.png
//
// 数据源是运行时直读 WZ（见 AdminCatalogData 的注释），不是源端的预生成 catalog.json。
// catalog 分页形状是 records/pageNo/pageSize/total（catalog 类，不是 mybatis-flex 的
// pageNumber/totalRow）。图标路由走 AdminHttpResponse.Bytes 直出 PNG，且必须 auth:false
// ——前端 <img src> 不带 Authorization 头。
//
// 纯 C#、不直接引用 UnityEngine（WZ 解析与主线程调度经 AdminCatalogData / AdminMainThread）。
using System;
using System.Collections.Generic;
using System.Globalization;

public static class AdminCatalog
{
    public static void Register(AdminApi api)
    {
        RegisterItemCatalog(api);
        RegisterEquipmentCatalog(api);
        RegisterMobCatalog(api);
        RegisterMobDrops(api);
        RegisterSkillCatalog(api);
        RegisterIcons(api);
    }

    // query 可空整数：缺参/空串 = 不过滤（区别于 QueryInt 的 0 兜底）
    static int? QueryNullableInt(AdminCtx ctx, string key)
    {
        string raw = ctx.Query(key);
        if (string.IsNullOrEmpty(raw)) return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
    }

    // query 可空布尔：缺参 = 不过滤；true/1 → true，false/0 → false
    static bool? QueryNullableBool(AdminCtx ctx, string key)
    {
        string raw = ctx.Query(key);
        if (string.IsNullOrEmpty(raw)) return null;
        if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    // —————————— 物品图鉴（源端 ItemCatalogService） ——————————
    static void RegisterItemCatalog(AdminApi api)
    {
        api.Get("/setItem/v1/item/catalog", ctx => AdminCatalogData.ItemCatalog(
            ctx.Query("keyword"), ctx.Query("category"),
            ctx.QueryInt("pageNo", 1), ctx.QueryInt("pageSize", 60)));
    }

    // —————————— 装备图鉴（源端 EquipmentCatalogService） ——————————
    static void RegisterEquipmentCatalog(AdminApi api)
    {
        api.Get("/setItem/v1/equipment/catalog", ctx => AdminCatalogData.EquipmentCatalog(
            ctx.Query("keyword"), ctx.Query("category"),
            ctx.QueryInt("pageNo", 1), ctx.QueryInt("pageSize", 60),
            QueryNullableBool(ctx, "cash"), ctx.Query("weaponType"),
            QueryNullableInt(ctx, "job"), QueryNullableInt(ctx, "minLevel"), QueryNullableInt(ctx, "maxLevel")));
    }

    // —————————— 怪物图鉴（源端 MobCatalogService） ——————————
    static void RegisterMobCatalog(AdminApi api)
    {
        api.Get("/mob/v1/catalog", ctx => AdminCatalogData.MobCatalog(
            ctx.Query("keyword"),
            ctx.QueryInt("pageNo", 1), ctx.QueryInt("pageSize", 60),
            QueryNullableInt(ctx, "minLevel"), QueryNullableInt(ctx, "maxLevel")));
    }

    // —————————— 技能图鉴（源端 SkillCatalogService） ——————————
    static void RegisterSkillCatalog(AdminApi api)
    {
        api.Get("/skill/v1/catalog", ctx => AdminCatalogData.SkillCatalog(
            ctx.Query("keyword"), QueryNullableInt(ctx, "jobId"), QueryNullableInt(ctx, "advancement"),
            ctx.QueryInt("pageNo", 1), ctx.QueryInt("pageSize", 120)));

        api.Get("/skill/v1/detail/{skillId}", ctx => AdminCatalogData.SkillDetail(ctx.ParamLong("skillId")));

        api.Get("/skill/v1/jobs", ctx => AdminCatalogData.SkillJobs());

        // 重建技能缓存，返回技能总数（源端 reload 返回 allSkills.size()）
        api.Post("/skill/v1/reload", ctx => AdminCatalogData.SkillReload());
    }

    // —————————— 图标 PNG ——————————
    // AdminApi 的 {x} 占位只支持整段匹配，URL 尾部的 .png 拼在占位里拿不到，
    // 所以注册成 {file}（如 "1002000.png"），handler 里自己剥后缀解析 id。
    static void RegisterIcons(AdminApi api)
    {
        api.Get("/assets/item-icons/{file}", ctx => IconResponse(ctx, AdminCatalogData.ItemIconPng), auth: false);
        api.Get("/assets/equipment-icons/{file}", ctx => IconResponse(ctx, AdminCatalogData.EquipmentIconPng), auth: false);
        api.Get("/assets/mob-icons/{file}", ctx => IconResponse(ctx, AdminCatalogData.MobIconPng), auth: false);
        api.Get("/assets/skill-icons/{file}", ctx => IconResponse(ctx, AdminCatalogData.SkillIconPng), auth: false);
    }

    static AdminHttpResponse IconResponse(AdminCtx ctx, Func<long, byte[]> encode)
    {
        string file = ctx.Param("file") ?? "";
        if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            file = file.Substring(0, file.Length - 4);
        if (!long.TryParse(file, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return NotFound();
        byte[] png = encode(id);
        return png == null || png.Length == 0
            ? NotFound()
            : AdminHttpResponse.Bytes(png, "image/png");
    }

    static AdminHttpResponse NotFound()
        => AdminHttpResponse.Json(AdminJson.Fail("not found", 404), 404);

    // —————————— 怪物掉落（源端 DropController / MobController） ——————————
    // 表 drop_data：dropperid/itemid/minimum_quantity/maximum_quantity/questid/chance，
    // 自增 id。源端是 MySQL + MonsterInformationProvider 内存缓存；本项目落单机
    // DbStore（GameServer.Db）。DropStore（Server/Field/DropStore.cs）已落地，是
    // 游戏内 FieldLoot 的运行时缓存：这里的写操作（PUT/DELETE）写完必须
    // DropStore.Invalidate()，否则「后台改了、游戏里没变」。GET 直读表（无缓存，
    // 后台管理页永远看到最新数据），与 DropStore 缓存不冲突。
    static void RegisterMobDrops(AdminApi api)
    {
        api.Get("/mob/v1/{mobId}/drops", ctx =>
        {
            long mobId = ctx.ParamLong("mobId");
            return AdminMainThread.Run(() =>
            {
                var list = new List<object>();
                var db = Db;
                if (db == null || !db.HasTable("drop_data")) return list; // 表还没建：空表，不报错
                var rows = db.Table("drop_data").Select(DbStore.Eq("dropperid", mobId));
                rows.Sort((a, b) => DbStore.Long(a, "id").CompareTo(DbStore.Long(b, "id")));
                foreach (var row in rows) list.Add(DropDto(row));
                return list;
            });
        });

        api.Put("/mob/v1/{mobId}/drops", ctx =>
        {
            long mobId = ctx.ParamLong("mobId");
            // 前端 axios.put(url, { data }) 会再包一层 data，AdminApi.UnwrapSubmit 只拆
            // requestId/data，所以这里还剩 { data: {...} }，再拆一层才拿到字段。
            var form = UnwrapInner(AdminJson.AsDict(ctx.Body));
            if (form == null) throw new AdminApiException("缺少掉落数据");
            long itemId = AdminJson.GetLong(form, "itemId");
            return AdminMainThread.Run(() =>
            {
                var db = RequireDb();
                if (!db.HasTable("drop_data")) throw new AdminApiException("掉落表 drop_data 尚未初始化（请先升级存档）");
                var table = db.Table("drop_data");
                var row = new DbRow
                {
                    ["dropperid"] = mobId,
                    ["itemid"] = itemId,
                    ["minimum_quantity"] = AdminJson.GetLong(form, "minimumQuantity"),
                    ["maximum_quantity"] = AdminJson.GetLong(form, "maximumQuantity"),
                    ["questid"] = AdminJson.GetLong(form, "questId"),
                    ["chance"] = AdminJson.GetLong(form, "chance"),
                };
                // comments 是全局掉落表的字段，单怪掉落表若建了这一列也一并存（列不存在则忽略）
                if (table.Col("comments") != null) row["comments"] = AdminJson.GetString(form, "comments");
                long id = table.Insert(row);
                db.Save();
                DropStore.Invalidate(); // 游戏内 FieldLoot 走 DropStore 缓存，写后必须清（同 AdminDrops）
                return id; // 源端返回新建行 id（ResultBody<Long>）
            });
        });

        api.Delete("/mob/v1/drops/{dropId}", ctx =>
        {
            long dropId = ctx.ParamLong("dropId");
            return AdminMainThread.Run<object>(() =>
            {
                var db = RequireDb();
                if (db.HasTable("drop_data"))
                {
                    db.Table("drop_data").Delete(DbStore.Eq("id", dropId));
                    db.Save();
                    DropStore.Invalidate(); // 同上：写后清运行时缓存
                }
                return null;
            });
        });
    }

    // body 兼容两种形状：{data:{...}}（前端标准）或裸 {...}（手工调试）
    static Dictionary<string, object> UnwrapInner(Dictionary<string, object> body)
    {
        if (body == null) return null;
        if (body.TryGetValue("data", out var inner) && inner is Dictionary<string, object> dict) return dict;
        return body;
    }

    // 掉落行 → 前端 MobDropItem（字段与 AdminUi/src/api/drop.ts 逐字一致）。
    // 名称走 WzNames（String.wz，带缓存）；类别按 id 首位映射成源端的中文枚举。
    static Dictionary<string, object> DropDto(DbRow row)
    {
        long itemId = DbStore.Long(row, "itemid");
        long questId = DbStore.Long(row, "questid");
        return AdminJson.Obj(
            "id", DbStore.Long(row, "id"),
            "dropperId", DbStore.Long(row, "dropperid"),
            "dropperName", WzNames.Mob((int)DbStore.Long(row, "dropperid")),
            "itemId", itemId,
            "itemName", itemId > 0 ? WzNames.Item((int)itemId) : "",
            "itemCategory", ItemCategoryOf(itemId),
            "minimumQuantity", DbStore.Long(row, "minimum_quantity"),
            "maximumQuantity", DbStore.Long(row, "maximum_quantity"),
            "questId", questId,
            "questName", AdminCatalogData.QuestName(questId),
            "chance", DbStore.Long(row, "chance"),
            "comments", DbStore.Str(row, "comments"));
    }

    // 源端 ItemConstants.getInventoryType：按 id 首位，金币(0) 与未知值单独兜底
    static string ItemCategoryOf(long itemId)
    {
        switch (itemId / 1000000)
        {
            case 0: return "金币";
            case 1: return "装备";
            case 2: return "消耗";
            case 3: return "设置";
            case 4: return "其他";
            case 5: return "特殊";
            default: return "未知";
        }
    }

    // —————————— 数据库访问（照抄 AdminBackend 的 Db 模式） ——————————
    // 离线自检（tools/admin-check）注入 DbOverride；运行期取 GameServer.Local.Db。
    static DbStore Db
    {
        get
        {
            if (AdminBackend.DbOverride != null) return AdminBackend.DbOverride;
#if UNITY_EDITOR || UNITY_5_3_OR_NEWER
            return GameServer.Local?.Db;
#else
            return null;
#endif
        }
    }

    static DbStore RequireDb()
    {
        var db = Db;
        if (db == null) throw new AdminApiException("服务端未启动（请先进入游戏）");
        return db;
    }
}
