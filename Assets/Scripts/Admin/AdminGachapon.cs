// 后台「扭蛋机」模块：/gachapon/v1/*（源端 GachaponController + GachaponService）。
//
// 表 gachapon_reward_pool（奖池）/ gachapon_reward（奖品）。源端还有旧表 gachapon（硬编码
// 奖池机制），后台不走它，这里也不建。realProb 的算法照抄 setRealProb：
//   公共池 realProb = prob * 100；私有池按权重分 (1000000 - 100*Σprob)。
// 名称（gachaponName=扭蛋机 NPC 名、itemName）经 AdminNames 注入。
//
// 约束：纯 C#，不引用 UnityEngine。
using System;
using System.Collections.Generic;

public static class AdminGachapon
{
    public static void Register(AdminApi api)
    {
        api.Post("/gachapon/v1/getPools", GetPools);
        api.Post("/gachapon/v1/updatePool", UpdatePool);
        api.Post("/gachapon/v1/deletePool", ctx =>
        {
            long id = AdminJson.GetLong(AdminJson.AsDict(ctx.Body), "id");
            return AdminMainThread.Run<object>(() =>
            {
                var db = AdminBackend.RequireDb();
                if (db.HasTable("gachapon_reward_pool"))
                    db.Table("gachapon_reward_pool").Delete(DbStore.Eq("id", id));
                if (db.HasTable("gachapon_reward"))
                    db.Table("gachapon_reward").Delete(DbStore.Eq("pool_id", id));   // 源端级联删奖品
                db.Save();
                return null;
            });
        });
        api.Post("/gachapon/v1/getRewards", GetRewards);
        api.Post("/gachapon/v1/updateReward", UpdateReward);
        api.Post("/gachapon/v1/deleteReward", ctx =>
        {
            long id = AdminJson.GetLong(AdminJson.AsDict(ctx.Body), "id");
            return AdminMainThread.Run<object>(() =>
            {
                var db = AdminBackend.RequireDb();
                if (db.HasTable("gachapon_reward"))
                {
                    db.Table("gachapon_reward").Delete(DbStore.Eq("id", id));
                    db.Save();
                }
                return null;
            });
        });
    }

    // —————————— 奖池 ——————————
    static object GetPools(AdminCtx ctx)
    {
        long? gachaponId = ctx.BodyLong("gachaponId", long.MinValue) == long.MinValue
            ? (long?)null : ctx.BodyLong("gachaponId");
        int pageNo = ctx.BodyInt("pageNo", 1);
        int pageSize = ctx.BodyInt("pageSize", 20);
        bool onlyTotal = ctx.BodyBool("onlyTotal");
        bool notPage = ctx.BodyBool("notPage");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.Db;
            var rows = new List<DbRow>();
            if (db != null && db.HasTable("gachapon_reward_pool"))
                foreach (var row in db.Table("gachapon_reward_pool").rows)
                {
                    // 源端：gachapon_id = X OR is_public = 1
                    if (gachaponId.HasValue &&
                        DbStore.Long(row, "gachapon_id") != gachaponId.Value &&
                        DbStore.Long(row, "is_public") == 0) continue;
                    rows.Add(row);
                }
            rows.Sort((a, b) =>
            {
                int byPublic = DbStore.Long(b, "is_public").CompareTo(DbStore.Long(a, "is_public"));  // is_public 降序
                return byPublic != 0 ? byPublic : DbStore.Long(a, "id").CompareTo(DbStore.Long(b, "id"));
            });

            var dtos = new List<Dictionary<string, object>>();
            foreach (var row in rows) dtos.Add(PoolDto(row, 0));
            if (gachaponId.HasValue && gachaponId.Value != -1) SetRealProb(dtos);

            return AdminDbUtil.Page(dtos, pageNo, pageSize, onlyTotal, notPage, d => d);
        });
    }

    static Dictionary<string, object> PoolDto(DbRow row, long realProb)
    {
        long gachaponId = DbStore.Long(row, "gachapon_id");
        return AdminJson.Obj(
            "id", DbStore.Long(row, "id"),
            "name", DbStore.Str(row, "name"),
            "gachaponId", gachaponId,
            "gachaponName", gachaponId > 0 ? AdminNames.NpcName((int)gachaponId) : "",
            "weight", DbStore.Long(row, "weight"),
            "isPublic", DbStore.Long(row, "is_public") != 0,
            "prob", DbStore.Long(row, "prob"),
            "startTime", DbStore.Str(row, "start_time"),
            "endTime", DbStore.Str(row, "end_time"),
            "notification", DbStore.Long(row, "notification") != 0,
            "comment", DbStore.Str(row, "comment"),
            "realProb", realProb);
    }

    static void SetRealProb(List<Dictionary<string, object>> pools)
    {
        long probTotal = 0, totalWeight = 0;
        foreach (var p in pools)
        {
            probTotal += AdminJson.GetLong(p, "prob");
            totalWeight += AdminJson.GetLong(p, "weight");
        }
        long weightPoint = 1000000 - 100 * probTotal;
        foreach (var p in pools)
        {
            if (AdminJson.GetBool(p, "isPublic")) p["realProb"] = AdminJson.GetLong(p, "prob") * 100;
            else p["realProb"] = totalWeight > 0
                ? (long)Math.Round((double)weightPoint * AdminJson.GetLong(p, "weight") / totalWeight)
                : 0L;
        }
    }

    static object UpdatePool(AdminCtx ctx)
    {
        var form = AdminJson.AsDict(ctx.Body);
        if (form == null) throw new AdminApiException("缺少奖池数据");
        string startTime = AdminJson.GetString(form, "startTime");
        if (string.IsNullOrEmpty(startTime)) throw new AdminApiException("生效时间不能为空");
        bool isPublic = AdminJson.GetBool(form, "isPublic");
        long prob = AdminJson.GetLong(form, "prob");
        long gachaponId = AdminJson.GetLong(form, "gachaponId");
        long weight = AdminJson.GetLong(form, "weight");
        if (isPublic)
        {
            if (form.ContainsKey("prob") == false || AdminJson.Get(form, "prob") == null) throw new AdminApiException("公共奖池中奖率不能为空");
            gachaponId = -1; weight = 0;
        }
        else
        {
            prob = 0;
            if (gachaponId == 0 && AdminJson.Get(form, "gachaponId") == null) throw new AdminApiException("百宝箱ID不能为空");
            if (AdminJson.Get(form, "weight") == null) throw new AdminApiException("奖池权重不能为空");
        }

        long id = AdminJson.GetLong(form, "id");
        return AdminMainThread.Run<object>(() =>
        {
            var db = AdminBackend.RequireDb();
            var table = RequireTable(db, "gachapon_reward_pool");
            var values = new DbRow();
            AdminDbUtil.Set(values, "name", AdminJson.GetString(form, "name"));
            AdminDbUtil.Set(values, "gachapon_id", gachaponId);
            AdminDbUtil.Set(values, "weight", weight);
            AdminDbUtil.Set(values, "is_public", isPublic ? 1L : 0L);
            AdminDbUtil.Set(values, "prob", prob);
            AdminDbUtil.Set(values, "start_time", startTime);
            AdminDbUtil.Set(values, "end_time", AdminJson.GetString(form, "endTime"));
            AdminDbUtil.Set(values, "notification", AdminJson.GetBool(form, "notification") ? 1L : 0L);
            AdminDbUtil.Set(values, "comment", AdminJson.GetString(form, "comment"));

            if (id > 0 && table.Find(DbStore.Eq("id", id)) != null) table.Update(DbStore.Eq("id", id), values);
            else id = table.Insert(values);
            db.Save();
            return (object)id;
        });
    }

    // —————————— 奖品 ——————————
    static object GetRewards(AdminCtx ctx)
    {
        long poolId = AdminJson.GetLong(AdminJson.AsDict(ctx.Body), "id");
        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.Db;
            var list = new List<object>();
            if (db == null || !db.HasTable("gachapon_reward")) return list;
            var rows = db.Table("gachapon_reward").Select(DbStore.Eq("pool_id", poolId));
            rows.Sort((a, b) => DbStore.Long(a, "item_id").CompareTo(DbStore.Long(b, "item_id")));
            foreach (var row in rows)
            {
                long itemId = DbStore.Long(row, "item_id");
                list.Add(AdminJson.Obj(
                    "id", DbStore.Long(row, "id"),
                    "poolId", DbStore.Long(row, "pool_id"),
                    "itemId", itemId,
                    "itemName", AdminNames.ItemName((int)itemId),
                    "quantity", DbStore.Long(row, "quantity", 1),
                    "createTime", DbStore.Str(row, "create_time"),
                    "comment", DbStore.Str(row, "comment")));
            }
            return (object)list;
        });
    }

    static object UpdateReward(AdminCtx ctx)
    {
        var form = AdminJson.AsDict(ctx.Body);
        if (form == null) throw new AdminApiException("缺少奖品数据");
        long itemId = AdminJson.GetLong(form, "itemId");
        if (itemId <= 0) return null;                       // 源端 updateReward 直接忽略非法 itemId

        long id = AdminJson.GetLong(form, "id");
        return AdminMainThread.Run<object>(() =>
        {
            var db = AdminBackend.RequireDb();
            var table = RequireTable(db, "gachapon_reward");
            var values = new DbRow();
            AdminDbUtil.Set(values, "pool_id", AdminJson.GetLong(form, "poolId"));
            AdminDbUtil.Set(values, "item_id", itemId);
            AdminDbUtil.Set(values, "quantity", AdminJson.GetLong(form, "quantity", 1));
            AdminDbUtil.Set(values, "comment", AdminJson.GetString(form, "comment"));
            string createTime = AdminJson.GetString(form, "createTime");
            AdminDbUtil.Set(values, "create_time", string.IsNullOrEmpty(createTime) ? DbStore.NowText() : createTime);

            if (id > 0 && table.Find(DbStore.Eq("id", id)) != null) table.Update(DbStore.Eq("id", id), values);
            else table.Insert(values);
            db.Save();
            return null;
        });
    }

    static DbTable RequireTable(DbStore db, string name)
    {
        if (!db.HasTable(name)) throw new AdminApiException("扭蛋表 " + name + " 尚未初始化（请先升级存档）");
        return db.Table(name);
    }
}
