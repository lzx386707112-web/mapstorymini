// 后台「命令库」模块：/command/v1/*（源端 CommandController + CommandService）。
//
// 表 command_info：syntax（指令）/level（当前等级，可改）/enabled/clazz（后端类名）/
// default_level（默认等级，不可改）。源端 description 不落库，是反射实例化
// org.gms.client.command.commands.gm{defaultLevel}.{clazz} 后取 i18n（<类名>.message1）；
// 单机版没有 Java 反射，这里把 186 个类的中文描述内置成表（与 i18n 值一致）。
//
// 种子数据 = 源端 V1.5.1（179 条）+ V1.8.4（中文别名 10 条）+ V1.9.9（fishdrop）
// + V2.0.9（reloadhunt）+ V2.1.0（soloMapling 9 条）。首启灌进 command_info，
// 之后以表为准（level/enabled 的修改就是本模块的编辑功能）。
//
// 三个 reload 按钮：源端复用 GM 命令重载服务端脚本/传送点/地图。单机版对应
// 「清 NPC/任务脚本缓存」「清地图解析缓存（WZ 改过之后）」——不是假装成功，是真的清缓存。
//
// 约束：纯 C#，不引用 UnityEngine（重载动作走 AdminCommands.Reload* 钩子，由 AdminHost 注入）。
using System;
using System.Collections.Generic;

public static class AdminCommands
{
    /// <summary>重载 NPC/任务脚本缓存（≡ !reloadevents）。由 AdminHost 注入 ScriptSource.ClearCache。</summary>
    public static Action ReloadScripts;
    /// <summary>重载地图/传送点解析缓存（≡ !reloadportals / !reloadmap）。由 AdminHost 注入 FieldCatalog 失效。</summary>
    public static Action ReloadMaps;

    // 种子：syntax|clazz|level（level == default_level，源端种子两列恒等）
    const string Seed = NativeCommandCatalog.Seed;
    const string Descriptions = NativeCommandCatalog.Descriptions;

    static Dictionary<string, string> descMap;

    static Dictionary<string, string> DescMap
    {
        get
        {
            if (descMap != null) return descMap;
            descMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in Descriptions.Split('\n'))
            {
                var t = line.Trim();
                int p = t.IndexOf('|');
                if (p <= 0) continue;
                descMap[t.Substring(0, p)] = t.Substring(p + 1);
            }
            return descMap;
        }
    }

    public static void Register(AdminApi api)
    {
        api.Post("/command/v1/getCommandListFromDB", GetList);
        api.Post("/command/v1/updateCommand", Update);
        api.Get("/command/v1/reloadEventsByGMCommand", ctx =>
        {
            ReloadScripts?.Invoke();
            return null;
        });
        api.Get("/command/v1/reloadPortalsByGMCommand", ctx =>
        {
            ReloadMaps?.Invoke();
            return null;
        });
        api.Get("/command/v1/reloadMapsByGMCommand", ctx =>
        {
            ReloadMaps?.Invoke();
            return null;
        });
    }

    // CommandService.loadCommands: level 0..6, enabled rows, first syntax wins.
    // Reuse the same seed and i18n catalog as the administration endpoint.
    public static List<KeyValuePair<string, string>>[] ScriptCommandDescriptions(DbStore db)
    {
        EnsureSeed(db);
        var groups = new List<KeyValuePair<string, string>>[7];
        var registered = new HashSet<string>(StringComparer.Ordinal);
        for (int level = 0; level < groups.Length; level++)
        {
            groups[level] = new List<KeyValuePair<string, string>>();
            foreach (var row in db.Table("command_info").rows)
            {
                if (DbStore.Int(row, "level") != level || DbStore.Int(row, "enabled") == 0) continue;
                // A missing command class cannot be registered by the Java service.
                if (!DescMap.TryGetValue(DbStore.Str(row, "clazz"), out string description)) continue;
                string syntax = DbStore.Str(row, "syntax").ToLowerInvariant();
                if (!registered.Add(syntax)) continue;
                groups[level].Add(new KeyValuePair<string, string>(syntax, description));
            }
        }
        return groups;
    }

    static void EnsureSeed(DbStore db)
    {
        if (!db.HasTable("command_info")) return;
        var table = db.Table("command_info");
        if (table.rows.Count > 0) return;
        foreach (var line in Seed.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '#') continue;
            var parts = t.Split('|');
            if (parts.Length < 3) continue;
            table.Insert(new DbRow
            {
                ["syntax"] = parts[0],
                ["clazz"] = parts[1],
                ["level"] = long.Parse(parts[2]),
                ["default_level"] = long.Parse(parts[2]),
                ["enabled"] = 1L,
            });
        }
        db.Save();
    }

    static object GetList(AdminCtx ctx)
    {
        var form = AdminJson.AsDict(ctx.Body) ?? new Dictionary<string, object>();
        int pageNo = AdminJson.GetInt(form, "pageNo", 1);
        int pageSize = AdminJson.GetInt(form, "pageSize", 20);
        bool onlyTotal = AdminJson.GetBool(form, "onlyTotal");
        bool notPage = AdminJson.GetBool(form, "notPage");
        string syntax = AdminJson.GetString(form, "syntax");
        var levelList = AdminJson.GetList(form, "levelList");
        var defaultLevelList = AdminJson.GetList(form, "defaultLevelList");
        bool hasEnabled = AdminJson.Get(form, "enabled") != null;
        bool enabled = AdminJson.GetBool(form, "enabled");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            EnsureSeed(db);

            var rows = new List<DbRow>();
            foreach (var row in db.Table("command_info").rows)
            {
                long level = DbStore.Long(row, "level");
                long defaultLevel = DbStore.Long(row, "default_level");
                if (levelList != null && levelList.Count > 0 && !Contains(levelList, level)) continue;
                if (defaultLevelList != null && defaultLevelList.Count > 0 && !Contains(defaultLevelList, defaultLevel)) continue;
                if (!string.IsNullOrEmpty(syntax) &&
                    DbStore.Str(row, "syntax").IndexOf(syntax, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (hasEnabled && (DbStore.Long(row, "enabled") != 0) != enabled) continue;
                rows.Add(row);
            }
            rows.Sort((a, b) =>
            {
                int byLevel = DbStore.Long(a, "default_level").CompareTo(DbStore.Long(b, "default_level"));
                return byLevel != 0 ? byLevel : DbStore.Str(a, "syntax").CompareTo(DbStore.Str(b, "syntax"));
            });

            return AdminDbUtil.Page(rows, pageNo, pageSize, onlyTotal, notPage, Dto);
        });
    }

    static bool Contains(List<object> list, long value)
    {
        foreach (var item in list)
            if (AdminJson.GetLong(new Dictionary<string, object> { ["v"] = item }, "v") == value) return true;
        return false;
    }

    static Dictionary<string, object> Dto(DbRow row)
    {
        string clazz = DbStore.Str(row, "clazz");
        string description;
        if (!DescMap.TryGetValue(clazz, out description)) description = "";
        return AdminJson.Obj(
            "id", DbStore.Long(row, "id"),
            "level", DbStore.Long(row, "level"),
            "syntax", DbStore.Str(row, "syntax"),
            "defaultLevel", DbStore.Long(row, "default_level"),
            "clazz", clazz,
            "description", description,
            "enabled", DbStore.Long(row, "enabled") != 0);
    }

    // 源端 updateCommand 只允许改 level / enabled（syntax/defaultLevel/clazz 不可改）
    static object Update(AdminCtx ctx)
    {
        var form = AdminJson.AsDict(ctx.Body);
        if (form == null) throw new AdminApiException("缺少命令数据");
        long id = AdminJson.GetLong(form, "id");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            EnsureSeed(db);
            var table = db.Table("command_info");
            var row = table.Find(DbStore.Eq("id", id));
            if (row == null) throw new AdminApiException("命令不存在: " + id);

            var values = new DbRow();
            if (AdminJson.Get(form, "level") != null) AdminDbUtil.Set(values, "level", AdminJson.GetLong(form, "level"));
            if (AdminJson.Get(form, "enabled") != null) AdminDbUtil.Set(values, "enabled", AdminJson.GetBool(form, "enabled") ? 1L : 0L);
            table.Update(DbStore.Eq("id", id), values);
            db.Save();
            return (object)Dto(table.Find(DbStore.Eq("id", id)));
        });
    }
}
