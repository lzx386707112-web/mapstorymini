// 假人控制（源端 BotControlController + BotControlService，URL 与 DTO 字段对齐）。
//
// 配置全部落在 game_config 的 solo_mapling_* 行（与源端同一批键）；本项目的 BotConfig
// 每次调用都实时读库（无缓存），所以 update 落库即生效。market/appearance 两组键与源端
// MARKET_CODES / APPEARANCE_CODES 完全一致，其余 solo_mapling_* 键按字母序进 environment，
// party 恒为空（源端 List.of()）。market/start 走 BotEnvironment（≡ EnvironmentManager）
// 的三态状态机；FM 房间 hired-merchant 填充与 21点/OPQ 行为类尚未搬（见 BotEnvironment 文件头）。
using System;
using System.Collections.Generic;
using System.Globalization;

public static class AdminBots
{
    // 源端 BotControlService.MARKET_CODES（顺序保持一致）
    static readonly string[] MarketCodes =
    {
        "solo_mapling_market_auto_start",
        "solo_mapling_feature_fm_bots_enabled",
        "solo_mapling_feature_fm_merchants_enabled",
        "solo_mapling_feature_fm_region_fill_enabled",
        "solo_mapling_market_bot_max",
        "solo_mapling_market_shop_max",
        "solo_mapling_market_wander_enabled",
        "solo_mapling_market_hawk_enabled",
        "solo_mapling_market_browse_shops_enabled",
        "solo_mapling_market_idle_when_empty",
        "solo_mapling_market_tick_ms",
        "solo_mapling_market_speech_gap_ms",
        "solo_mapling_market_pathfind_per_tick",
    };

    // 源端 BotControlService.APPEARANCE_CODES
    static readonly string[] AppearanceCodes =
    {
        "solo_mapling_bot_random_chinese_name",
        "solo_mapling_bot_random_skill_enabled",
        "solo_mapling_bot_random_chair_enabled",
        "solo_mapling_bot_random_body_enabled",
        "solo_mapling_bot_normal_equips_enabled",
        "solo_mapling_bot_full_equips_enabled",
        "solo_mapling_bot_nx_equips_enabled",
        "solo_mapling_bot_deferred_decoration_enabled",
    };

    // 组队伴侣（消耗品 2431159「基友集合」）。源端 BotControlConfigDTO 的 party 恒为 List.of()，
    // 那几个参数写死在 PartyGrindCompat；本工程把它们落库成 solo_mapling_party_* 并由这个分组管理。
    static readonly string[] PartyCodes =
    {
        "solo_mapling_party_companion_enabled",
        "solo_mapling_party_companion_require_party",
        "solo_mapling_party_companion_count",
        "solo_mapling_party_companion_minutes",
        "solo_mapling_party_companion_level_jitter",
    };

    /// <summary>一支正在生效的伴侣队伍（由 AdminHost 从 BotPartyGrindService 采集）。</summary>
    public sealed class PartyTeamInfo
    {
        public long characterId;
        public string name;
        public int mapId, companions;
        public long remainingSec;
    }

    // 与 ActiveBots 同理用钩子：AdminBots 不直接引用 Server.Bot，离线自检才编得过
    public static Func<List<PartyTeamInfo>> PartyTeams;
    /// <summary>召唤：返回 null 表示成功，否则是给前端看的失败原因。</summary>
    public static Func<long, string> PartySummon;
    public static Func<long, bool> PartyDismiss;

    public static void Register(AdminApi api)
    {
        api.Get("/bot-control/v1/state", ctx => State());
        api.Get("/bot-control/v1/config", ctx => Config());
        api.Put("/bot-control/v1/config", Update);
        api.Post("/bot-control/v1/market/start", StartMarket);
        api.Get("/bot-control/v1/party", ctx => Party());
        api.Put("/bot-control/v1/party", UpdateParty);
        api.Post("/bot-control/v1/party/summon", SummonParty);
        api.Post("/bot-control/v1/party/dismiss", DismissParty);
    }

    // ≡ BotControlService.startMarket：EnvironmentManager 三态状态机（already/running/started）。
    // 与 ActiveBots 同理用钩子：真实实现 BotEnvironment 在 AdminHost 注入，admin-check 不拖 Bot 链。
    public static Func<bool> MarketStartupComplete;
    public static Func<bool> MarketStartupRunning;
    public static Action RequestMarketStartup;

    static object StartMarket(AdminCtx ctx)
    {
        if (MarketStartupComplete != null && MarketStartupComplete()) return "already";
        if (MarketStartupRunning != null && MarketStartupRunning()) return "running";
        if (RequestMarketStartup == null) throw new AdminApiException("服务端未启动（请先进入游戏）");
        AdminMainThread.Run(() => { RequestMarketStartup(); return (long)1; });
        return "started";
    }

    // 活跃假人计数（由 AdminHost 注入 BotRegistry.Count()；离线自检/未启动时为 0。
    // 用钩子而不是直接引用 Server.Bot.BotRegistry，是为了让 admin-check 不用拖整条 Bot 链）
    public static Func<int> ActiveBots;

    // ——————————— 状态 ———————————
    static object State()
    {
        long current = 0;
        try { current = AdminBackend.CurrentCharacterId != null ? AdminBackend.CurrentCharacterId() : 0; } catch { }
        int bots = 0;
        try { bots = ActiveBots != null ? ActiveBots() : 0; } catch { }
        return AdminMainThread.Run(() => AdminJson.Obj(
            "activeBots", (long)Math.Max(0, bots),
            "marketDirectorBots", 0L,
            "marketEntranceBots", 0L,
            "onlinePlayers", current > 0 ? 1L : 0L,
            "marketReady", false,
            "marketStarting", false));
    }

    // ——————————— 配置 ———————————
    // 源端只列库里的行；本工程 game_config 不预种 solo_mapling_*，这里按「键清单 + 当前行
    // （没有就给默认值）」生成字段，id 为 0 表示尚未落库（update 时补种）。
    static object Config()
    {
        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            var rows = new Dictionary<string, DbRow>();
            foreach (var r in db.Table("game_config").rows)
            {
                string code = DbStore.Str(r, "config_code");
                if (code.StartsWith("solo_mapling", StringComparison.Ordinal)) rows[code] = r;
            }

            var used = new HashSet<string>();
            var market = Fields(MarketCodes, rows, used);
            var appearance = Fields(AppearanceCodes, rows, used);
            var party = Fields(PartyCodes, rows, used);
            var leftover = new List<string>(rows.Keys);
            foreach (var code in used) leftover.Remove(code);
            leftover.Sort(StringComparer.Ordinal);
            var environment = Fields(leftover, rows, null);

            return AdminJson.Obj("market", market, "appearance", appearance, "environment", environment, "party", party);
        });
    }

    static List<object> Fields(string[] codes, Dictionary<string, DbRow> rows, HashSet<string> used)
    {
        var list = new List<object>();
        foreach (var code in codes)
        {
            if (used != null) used.Add(code);
            if (!rows.TryGetValue(code, out var row))
            {
                // 未落库的键给 BotConfig 默认值，clazz 由键名推断（_enabled 结尾是布尔，其余整数/字符串）
                list.Add(AdminJson.Obj(
                    "id", 0L, "code", code, "clazz", ClazzOf(code),
                    "value", DefaultOf(code), "desc", code));
                continue;
            }
            list.Add(AdminJson.Obj(
                "id", DbStore.Long(row, "id"),
                "code", code,
                "clazz", string.IsNullOrEmpty(DbStore.Str(row, "config_clazz")) ? ClazzOf(code) : DbStore.Str(row, "config_clazz"),
                "value", DbStore.Str(row, "config_value"),
                "desc", code));
        }
        return list;
    }

    static List<object> Fields(List<string> codes, Dictionary<string, DbRow> rows, HashSet<string> used)
    {
        return Fields(codes.ToArray(), rows, used);
    }

    static string ClazzOf(string code)
    {
        if (code.EndsWith("_enabled", StringComparison.Ordinal) || code.EndsWith("_auto_start", StringComparison.Ordinal)
            || code.EndsWith("_require_party", StringComparison.Ordinal))
            return "java.lang.Boolean";
        if (code.EndsWith("_max", StringComparison.Ordinal) || code.EndsWith("_ms", StringComparison.Ordinal)
            || code.EndsWith("_per_tick", StringComparison.Ordinal) || code.EndsWith("_min", StringComparison.Ordinal)
            || code.EndsWith("_count", StringComparison.Ordinal) || code.EndsWith("_minutes", StringComparison.Ordinal)
            || code.EndsWith("_jitter", StringComparison.Ordinal))
            return "java.lang.Integer";
        return "java.lang.String";
    }

    // 未落库键的默认值：与 BotConfig 里的 fallback 一致（改 BotConfig 记得同步这里）
    static string DefaultOf(string code)
    {
        switch (code)
        {
            case "solo_mapling_market_auto_start": return "true";
            case "solo_mapling_market_bot_max": return "10";
            case "solo_mapling_market_shop_max": return "10";
            case "solo_mapling_market_tick_ms": return "250";
            case "solo_mapling_market_speech_gap_ms": return "500";
            case "solo_mapling_market_pathfind_per_tick": return "3";
            case "solo_mapling_bot_deferred_decoration_enabled": return "false";
            case "solo_mapling_party_companion_enabled": return "true";
            case "solo_mapling_party_companion_require_party": return "true";
            case "solo_mapling_party_companion_count": return "5";
            case "solo_mapling_party_companion_minutes": return "30";
            case "solo_mapling_party_companion_level_jitter": return "3";
            default: return code.EndsWith("_enabled", StringComparison.Ordinal) ? "true" : "";
        }
    }

    // ——————————— 更新（源端 update：只接受 solo_mapling_*，逐行 upsert） ———————————
    static object Update(AdminCtx ctx)
    {
        var fields = AdminJson.GetList(ctx.Body, "fields");
        if (fields == null || fields.Count == 0) throw new AdminApiException("参数不能为空: fields");
        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            var table = db.Table("game_config");
            int updated = 0;
            foreach (var f in fields)
            {
                var field = AdminJson.AsDict(f);
                string code = AdminJson.GetString(field, "code");
                if (string.IsNullOrEmpty(code) || !code.StartsWith("solo_mapling_", StringComparison.Ordinal)) continue;
                string value = AdminJson.GetString(field, "value");

                var row = table.Find(r => DbStore.Str(r, "config_code") == code);
                if (row != null)
                {
                    table.Update(r => ReferenceEquals(r, row), new DbRow
                    {
                        ["config_value"] = value,
                        ["config_clazz"] = ClazzOf(code),
                        ["update_time"] = Now(),
                    });
                }
                else
                {
                    table.Insert(new DbRow
                    {
                        ["config_type"] = "server",
                        ["config_sub_type"] = "SoloMapling",
                        ["config_clazz"] = ClazzOf(code),
                        ["config_code"] = code,
                        ["config_value"] = value,
                        ["config_desc"] = code,
                        ["update_time"] = Now(),
                    });
                }
                updated++;
            }
            // BotConfig 全部实时读库，无需刷新；调度 throttle 因子自适应，tick 间隔下一拍生效
            return (long)updated;
        });
    }

    // ——————————— 组队伴侣 ———————————
    // 面板 = 配置（solo_mapling_party_*）+ 实时状态（正在生效的几支队、还剩多久）。
    static object Party()
    {
        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            var rows = new Dictionary<string, DbRow>();
            foreach (var r in db.Table("game_config").rows)
            {
                string code = DbStore.Str(r, "config_code");
                if (code.StartsWith("solo_mapling", StringComparison.Ordinal)) rows[code] = r;
            }
            return AdminJson.Obj("config", Fields(PartyCodes, rows, null), "state", PartyState());
        });
    }

    static object PartyState()
    {
        var teams = new List<object>();
        var list = PartyTeams != null ? PartyTeams() : null;
        if (list != null)
            foreach (var team in list)
                teams.Add(AdminJson.Obj(
                    "characterId", team.characterId,
                    "name", team.name ?? "",
                    "mapId", (long)team.mapId,
                    "companions", (long)team.companions,
                    "remainingSec", Math.Max(0L, team.remainingSec)));
        long current = 0;
        try { current = AdminBackend.CurrentCharacterId != null ? AdminBackend.CurrentCharacterId() : 0; } catch { }
        return AdminJson.Obj("teams", teams, "activeTeams", (long)teams.Count, "characterId", current);
    }

    static object UpdateParty(AdminCtx ctx)
    {
        var fields = AdminJson.GetList(ctx.Body, "fields");
        if (fields == null || fields.Count == 0) throw new AdminApiException("参数不能为空: fields");
        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            var table = db.Table("game_config");
            var allowed = new HashSet<string>(PartyCodes);
            int updated = 0;
            foreach (var f in fields)
            {
                var field = AdminJson.AsDict(f);
                string code = AdminJson.GetString(field, "code");
                if (string.IsNullOrEmpty(code) || !allowed.Contains(code)) continue;
                string value = AdminJson.GetString(field, "value");
                var row = table.Find(r => DbStore.Str(r, "config_code") == code);
                if (row != null)
                    table.Update(r => ReferenceEquals(r, row), new DbRow
                    {
                        ["config_value"] = value,
                        ["config_clazz"] = ClazzOf(code),
                        ["update_time"] = Now(),
                    });
                else
                    table.Insert(new DbRow
                    {
                        ["config_type"] = "server",
                        ["config_sub_type"] = "SoloMapling",
                        ["config_clazz"] = ClazzOf(code),
                        ["config_code"] = code,
                        ["config_value"] = value,
                        ["config_desc"] = code,
                        ["update_time"] = Now(),
                    });
                updated++;
            }
            return (long)updated;
        });
    }

    // 召唤 / 解散：服务端没起来（还没进游戏）就没法调，直接给前端明确原因。
    static object SummonParty(AdminCtx ctx)
    {
        long characterId = PartyTarget(ctx);
        return AdminMainThread.Run(() =>
        {
            if (PartySummon == null) throw new AdminApiException("服务端未启动（请先进入游戏）");
            string error = PartySummon(characterId);
            if (!string.IsNullOrEmpty(error)) throw new AdminApiException(error);
            return PartyState();
        });
    }

    static object DismissParty(AdminCtx ctx)
    {
        long characterId = PartyTarget(ctx);
        return AdminMainThread.Run(() =>
        {
            if (PartyDismiss == null) throw new AdminApiException("服务端未启动（请先进入游戏）");
            PartyDismiss(characterId);
            return PartyState();
        });
    }

    static long PartyTarget(AdminCtx ctx)
    {
        long id = AdminJson.GetLong(ctx.Body, "characterId", 0);
        if (id <= 0)
            try { id = AdminBackend.CurrentCharacterId != null ? AdminBackend.CurrentCharacterId() : 0; } catch { }
        if (id <= 0) throw new AdminApiException("没有在线角色（请先进入游戏）");
        return id;
    }

    static string Now() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
