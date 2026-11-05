// 后台「掉落」模块：/drop/v1/*（源端 DropController + DropService）。
//
// 普通掉落与全局掉落是两套 URL + 两张表（drop_data / drop_data_global），字段同构。
// 名称类字段（dropperName/itemName/questName）源端查 WZ，这里走 AdminNames（运行期注入）。
// 写操作之后必须 DropStore.Invalidate()：游戏内 FieldLoot 是从 DropStore 缓存掉东西的，
// 不清缓存就会出现「后台改了、游戏里没变」（对应源端 MonsterInformationProvider.clearDrops）。
//
// 与源端的唯一差别在「数据从哪来」：
//   源端 drop_data 里就是全量掉落（MySQL 迁移灌进去的），后台直接 SELECT 该表；
//   本项目把源端全量掉落导成只读基线 Assets/Resources/Field/FieldDropTable.txt
//   （2.6 万行，见 tools/export_field_drops.py），drop_data 只存「后台改过的覆盖行」。
// 所以这里的查询**必须读 DropStore 的合并视图**（基线 + DB 覆盖），不能像以前那样
// 直接遍历 drop_data —— 真机存档里那张表是 0 行，页面永远是空列表（2026-11-04 用户报障）。
// 写操作也按覆盖层语义落库：改基线行 = 写一条覆盖行；删基线行 = 写 chance=0 墓碑；
// 删纯 DB 行才是真删。
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

    // 列表的一行：owner = 怪物 id（普通）/ 大区 id（全局），e 是合并后的掉落条目。
    sealed class ViewRow
    {
        public int owner;
        public DropEntry e;
        public long dbId => e.id;          // >0 = 已有 DB 覆盖行；0 = 纯基线行
        public bool global;
    }

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
            var rows = View(global);

            var mobNames = new Dictionary<int, string>();
            var itemNames = new Dictionary<int, string>();
            string MobName(int id)
            {
                if (!mobNames.TryGetValue(id, out var n)) mobNames[id] = n = AdminNames.MobName(id);
                return n;
            }
            string ItemName(int id)
            {
                if (!itemNames.TryGetValue(id, out var n)) itemNames[id] = n = AdminNames.ItemName(id);
                return n;
            }

            var matched = new List<ViewRow>();
            foreach (var row in rows)
            {
                if (!global && dropperId > 0 && row.owner != dropperId) continue;
                if (global && continent != -2 && row.owner != continent) continue;
                if (itemId >= 0 && row.e.item != itemId) continue;
                if (questId >= 0 && row.e.quest != questId) continue;
                if (!string.IsNullOrEmpty(dropperName) && !global &&
                    MobName(row.owner).IndexOf(dropperName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrEmpty(itemName) &&
                    ItemName(row.e.item).IndexOf(itemName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                matched.Add(row);
            }

            // 源端按 id 升序；合并视图里基线行没有 id，按 (owner, item, 覆盖行 id) 排，
            // 保证同一只怪的掉落聚在一起、覆盖行固定位置。
            matched.Sort((a, b) =>
            {
                int c = a.owner.CompareTo(b.owner);
                if (c != 0) return c;
                c = a.e.item.CompareTo(b.e.item);
                if (c != 0) return c;
                return a.dbId.CompareTo(b.dbId);
            });

            return AdminDbUtil.Page(matched, pageNo, pageSize, onlyTotal, notPage,
                row => Dto(row, global));
        });
    }

    // 合并视图 = DropStore（基线 + DB 覆盖，游戏内真正生效的那份）。
    // 直接复用运行时缓存，后台看到的就是「打怪会掉什么」，不是另一套口径。
    static List<ViewRow> View(bool global)
    {
        var list = new List<ViewRow>();
        if (global)
        {
            foreach (var e in DropStore.Global())
                list.Add(new ViewRow { owner = (int)e.continent, e = e, global = true });
            return list;
        }
        foreach (var kv in DropStore.All())
            foreach (var e in kv.Value)
                list.Add(new ViewRow { owner = kv.Key, e = e, global = false });
        return list;
    }

    static Dictionary<string, object> Dto(ViewRow v, bool global)
    {
        long itemId = v.e.item;
        return AdminJson.Obj(
            "id", RowKey(v),
            "dbId", v.dbId,
            "source", v.dbId > 0 ? "override" : "baseline",
            "dropperId", global ? 0L : (long)v.owner,
            "dropperName", global ? "" : AdminNames.MobName(v.owner),
            "continent", global ? (long)v.owner : -1L,
            "itemId", itemId,
            "itemName", AdminNames.ItemName((int)itemId),
            "itemCategory", AdminNames.ItemCategory((int)itemId),
            "minimumQuantity", (long)v.e.min,
            "maximumQuantity", (long)v.e.max,
            "questId", (long)v.e.quest,
            "questName", AdminNames.QuestName(v.e.quest),
            "chance", (long)v.e.chance,
            "comments", v.e.comments ?? "");
    }

    // 行主键（前端 row-key / 编辑态开关）：
    //   有 DB 覆盖行 → 直接用行 id；纯基线行 → 负数合成 id（owner 24 位 + item 24 位），
    //   保证同一行每次查询都是同一个值、且永远不撞 DB id（正数）。
    // 0 保留给「新增但还没保存」的行（前端 data.id === 0 判新增）。
    static long RowKey(ViewRow v)
    {
        if (v.dbId > 0) return v.dbId;
        long owner = v.owner & 0xFFFFFFL;
        long item = v.e.item & 0xFFFFFFL;
        return -(((owner << 24) | item) + 1);
    }

    // —————————— 新增 / 更新 ——————————
    // 源端 modifyDropData(data, isGlobal, isDelete) 是同一个入口；add 时把 id 置 null。
    // 这里统一成 upsert：按 (怪物, 道具) / (大区, 道具) 找现有 DB 行，有就改、没有就插。
    // 不能纯 insert——同一 (怪, 道具) 出现两行时 DropStore 按顺序覆盖，结果取决于行序。
    static object Save(AdminCtx ctx, bool global, bool isAdd)
    {
        var form = AdminJson.AsDict(ctx.Body);
        if (form == null) throw new AdminApiException("缺少掉落数据");
        long id = AdminJson.GetLong(form, "id");
        long dropperId = global ? 0 : AdminJson.GetLong(form, "dropperId");
        long continent = global ? AdminJson.GetLong(form, "continent", -1) : 0;
        long itemId = AdminJson.GetLong(form, "itemId");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            if (!db.HasTable(Table(global))) throw new AdminApiException("掉落表 " + Table(global) + " 尚未初始化（请先升级存档）");
            var table = db.Table(Table(global));

            DbRow existing = null;
            if (id > 0) existing = table.Find(DbStore.Eq("id", id));
            if (existing == null) existing = table.Find(KeyFilter(global, global ? continent : dropperId, itemId));

            var values = new DbRow();
            if (!global) AdminDbUtil.Set(values, "dropperid", dropperId);
            if (global) AdminDbUtil.Set(values, "continent", continent);
            AdminDbUtil.Set(values, "itemid", itemId);
            AdminDbUtil.Set(values, "minimum_quantity", AdminJson.GetLong(form, "minimumQuantity", 1));
            AdminDbUtil.Set(values, "maximum_quantity", AdminJson.GetLong(form, "maximumQuantity", 1));
            AdminDbUtil.Set(values, "questid", AdminJson.GetLong(form, "questId"));
            AdminDbUtil.Set(values, "chance", AdminJson.GetLong(form, "chance"));
            if (table.Col("comments") != null) AdminDbUtil.Set(values, "comments", AdminJson.GetString(form, "comments"));

            long newId;
            if (existing != null)
            {
                table.Update(r => ReferenceEquals(r, existing), values);
                newId = DbStore.Long(existing, "id");
            }
            else
            {
                newId = table.Insert(values);
            }
            db.Save();
            DropStore.Invalidate();
            return (object)newId;
        });
    }

    // —————————— 删除 ——————————
    // 语义对齐源端「这条掉落没了」：
    //   · 纯 DB 行（基线里没有这道具）→ 真删；
    //   · 基线自带的掉落 → 写 chance=0 墓碑（DropStore 会把 chance<=0 当删除），
    //     否则删掉覆盖行等于回退成基线，怪照样掉，看着像「删了没生效」。
    // 前端 DELETE 只带得动 id（基线行是负数合成 id），所以额外接受 dropperId/itemId/continent
    // 查询参数来定位 (owner, item)。
    static object Delete(AdminCtx ctx, bool global)
    {
        long id = ctx.ParamLong("id");
        long dropperId = ctx.QueryLong("dropperId", -1);
        long itemId = ctx.QueryLong("itemId", -1);
        long continent = ctx.QueryLong("continent", -2);

        return AdminMainThread.Run<object>(() =>
        {
            var db = AdminBackend.RequireDb();
            if (!db.HasTable(Table(global))) return null;
            var table = db.Table(Table(global));

            DbRow row = null;
            if (id > 0) row = table.Find(DbStore.Eq("id", id));
            if (row == null && itemId >= 0 && (global ? continent != -2 : dropperId > 0))
                row = table.Find(KeyFilter(global, global ? continent : dropperId, itemId));

            long owner = row != null
                ? (global ? DbStore.Long(row, "continent", -1) : DbStore.Long(row, "dropperid"))
                : (global ? continent : dropperId);
            long item = row != null ? DbStore.Long(row, "itemid") : itemId;
            if (item < 0) return null;                              // 定位不到目标行
            if (!global && owner <= 0) return null;                 // 大区可以是 -1（全大区），怪物 id 不行

            var baseline = DropStore.BaselineEntry(global, (int)owner, (int)item);
            if (row == null && baseline == null) return null;       // 本来就不存在
            if (baseline == null && row != null)
            {
                table.Delete(r => ReferenceEquals(r, row));
            }
            else if (row != null)
            {
                table.Update(r => ReferenceEquals(r, row), new DbRow { ["chance"] = 0L });
            }
            else
            {
                // 纯基线行：插一条 chance=0 墓碑（数量/任务条件照抄基线，避免被当成新掉落）
                var dead = new DbRow();
                if (!global) AdminDbUtil.Set(dead, "dropperid", owner);
                if (global) AdminDbUtil.Set(dead, "continent", owner);
                AdminDbUtil.Set(dead, "itemid", item);
                AdminDbUtil.Set(dead, "minimum_quantity", (long)baseline.min);
                AdminDbUtil.Set(dead, "maximum_quantity", (long)baseline.max);
                AdminDbUtil.Set(dead, "questid", (long)baseline.quest);
                AdminDbUtil.Set(dead, "chance", 0L);
                if (table.Col("comments") != null) AdminDbUtil.Set(dead, "comments", "后台删除（墓碑）");
                table.Insert(dead);
            }
            db.Save();
            DropStore.Invalidate();
            return null;
        });
    }

    static Func<DbRow, bool> KeyFilter(bool global, long owner, long item)
    {
        return global
            ? (Func<DbRow, bool>)(r => DbStore.Long(r, "continent", -1) == owner && DbStore.Long(r, "itemid") == item)
            : (Func<DbRow, bool>)(r => DbStore.Long(r, "dropperid") == owner && DbStore.Long(r, "itemid") == item);
    }
}
