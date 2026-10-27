// 后台「自动封禁」模块：/autoban/v1/*（源端 AutobanConfigController + AutobanConfigService）。
//
// 源端是「AutobanFactory 枚举默认值 + autoban_config 表覆盖」二合一：
//   points/expire_time 为 NULL = 用枚举默认；disabled=1 停用该类检测；
//   changePoints/changeExpireTime = 是否写覆盖（false 时清回默认）。
// 这里把 20 个枚举的默认值/中文名原样内置（AutobanFactory + i18n autoban.name.*），
// 表只存覆盖行。秒↔毫秒换算与源端一致（DTO 用秒、库里毫秒、-1 表永不过期）。
//
// 注：单机版还没有外挂检测引擎，本模块先把「配置面」迁完，检测生效是另一条线。
// 约束：纯 C#，不引用 UnityEngine。
using System;
using System.Collections.Generic;

public static class AdminAutoban
{
    sealed class Def
    {
        public string Type, Name;
        public int DefaultPoints;
        public long DefaultExpireMs;     // -1 = 永不过期
        public Def(string type, string name, int points, long expireMs)
        { Type = type; Name = name; DefaultPoints = points; DefaultExpireMs = expireMs; }
    }

    // AutobanFactory 枚举顺序 + i18n 中文名 + (maximum points, expire ms)
    static readonly Def[] Defs =
    {
        new Def("MOB_COUNT", "怪物数量异常", 1, -1),
        new Def("GENERAL", "通用检测", 1, -1),
        new Def("FIX_DAMAGE", "固定伤害", 1, -1),
        new Def("DAMAGE_HACK", "伤害外挂", 15, 60000),
        new Def("DISTANCE_HACK", "距离外挂", 10, 120000),
        new Def("PORTAL_DISTANCE", "传送门距离异常", 5, 30000),
        new Def("PACKET_EDIT", "数据包篡改", 1, -1),
        new Def("ACC_HACK", "命中率外挂", 1, -1),
        new Def("CREATION_GENERATOR", "创建生成器", 1, -1),
        new Def("HIGH_HP_HEALING", "高血量恢复", 1, -1),
        new Def("FAST_HP_HEALING", "快速血量恢复", 15, -1),
        new Def("FAST_MP_HEALING", "快速蓝量恢复", 20, 30000),
        new Def("GACHA_EXP", "扭蛋经验异常", 1, -1),
        new Def("TUBI", "快速拾取", 20, 15000),
        new Def("SHORT_ITEM_VAC", "短距离吸物", 1, -1),
        new Def("ITEM_VAC", "吸物", 1, -1),
        new Def("FAST_ITEM_PICKUP", "快速拾取物品", 5, 30000),
        new Def("FAST_ATTACK", "快速攻击", 10, 30000),
        new Def("MPCON", "蓝量消耗异常", 25, 30000),
        new Def("ATTACK_INTERVAL", "攻击间隔异常", 60, 60000),
    };

    public static void Register(AdminApi api)
    {
        api.Get("/autoban/v1/getConfigList", ctx => AdminMainThread.Run(() =>
        {
            var db = AdminBackend.Db;
            var result = new List<object>();
            foreach (var def in Defs)
            {
                DbRow row = null;
                if (db != null && db.HasTable("autoban_config"))
                    row = db.Table("autoban_config").Find(DbStore.Eq("type", def.Type));

                var dto = AdminJson.Obj(
                    "id", row == null ? 0L : DbStore.Long(row, "id"),
                    "type", def.Type,
                    "name", def.Name,
                    "defaultPoints", (long)def.DefaultPoints,
                    "defaultExpireTimeSeconds", def.DefaultExpireMs < 0 ? -1L : def.DefaultExpireMs / 1000);
                dto["disabled"] = row != null && DbStore.Long(row, "disabled") != 0;

                bool hasPoints = row != null && row.ContainsKey("points") && row["points"] != null;
                bool hasExpire = row != null && row.ContainsKey("expire_time") && row["expire_time"] != null;
                dto["points"] = hasPoints ? DbStore.Long(row, "points") : (object)null;
                dto["expireTimeSeconds"] = hasExpire
                    ? (DbStore.Long(row, "expire_time") < 0 ? -1L : DbStore.Long(row, "expire_time") / 1000)
                    : (object)null;
                dto["description"] = row == null ? "" : DbStore.Str(row, "description");
                dto["changePoints"] = hasPoints;
                dto["changeExpireTime"] = hasExpire;
                result.Add(dto);
            }
            return (object)result;
        }));

        api.Post("/autoban/v1/updateConfig", ctx =>
        {
            var form = AdminJson.AsDict(ctx.Body);
            if (form == null) throw new AdminApiException("缺少配置数据");
            string type = AdminJson.GetString(form, "type");
            if (string.IsNullOrEmpty(type)) throw new AdminApiException("封禁类型不能为空");
            Def def = null;
            foreach (var d in Defs) if (d.Type == type) { def = d; break; }
            if (def == null) throw new AdminApiException("未知的封禁类型: " + type);

            bool changePoints = AdminJson.GetBool(form, "changePoints");
            bool changeExpire = AdminJson.GetBool(form, "changeExpireTime");
            long? points = changePoints && AdminJson.Get(form, "points") != null
                ? AdminJson.GetLong(form, "points") : (long?)null;
            long? expireMs = null;
            if (changeExpire && AdminJson.Get(form, "expireTimeSeconds") != null)
            {
                long sec = AdminJson.GetLong(form, "expireTimeSeconds");
                expireMs = sec < 0 ? -1L : sec * 1000;
            }
            bool disabled = AdminJson.GetBool(form, "disabled");
            string description = AdminJson.GetString(form, "description");

            return AdminMainThread.Run<object>(() =>
            {
                var db = AdminBackend.RequireDb();
                if (!db.HasTable("autoban_config")) throw new AdminApiException("autoban_config 表尚未初始化（请先升级存档）");
                var table = db.Table("autoban_config");
                var row = table.Find(DbStore.Eq("type", type));

                // 源端：没有任何覆盖且未禁用且无描述 → 不落库（用默认值）
                if (row == null && points == null && expireMs == null && !disabled && string.IsNullOrEmpty(description))
                    return null;

                var values = new DbRow
                {
                    ["disabled"] = disabled ? 1L : 0L,
                    ["points"] = points,                       // null = 回退枚举默认
                    ["expire_time"] = expireMs,
                    ["description"] = description,
                    ["update_time"] = DbStore.NowText(),
                };
                if (row == null)
                {
                    values["type"] = type;
                    values["create_time"] = DbStore.NowText();
                    table.Insert(values);
                }
                else
                {
                    table.Update(DbStore.Eq("type", type), values);
                }
                db.Save();
                return null;
            });
        });
    }
}
