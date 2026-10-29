// 天气管理（源端 WeatherController + WeatherConfigService，URL 与 DTO 字段对齐）。
//
// 服务端运行时是 NativeWeatherService（一份实例：游戏 NativeWeatherField 与本模块共用，
// 见 NativeWeatherHost）。配置持久化在 game_config（admin_weather_config / admin_weather_region_*），
// 每个服务实例只应用一次；region 的 forced/tint/palette/weights 直接改服务里的对象，
// 立刻通过每帧 Sync 广播到客户端（无需额外 broadcast 包）。
using System;
using System.Collections.Generic;
using System.Globalization;

public static class AdminWeather
{
    // 天空档位与前端 WEATHER_PROFILES 同序（NativeWeatherState.Set 的 sky 0..8）
    static readonly string[] Profiles = { "clear", "rain", "snow", "overcast", "storm", "blizzard", "leaves", "blossom", "sandstorm" };

    const string ConfigCode = "admin_weather_config";

    static NativeWeatherService service;
    static object configuredFor;   // 已应用持久化配置的服务实例（换实例/重启后重应用）

    public static void Register(AdminApi api)
    {
        api.Get("/weather/v1/state", ctx => State());
        api.Get("/weather/v1/config", ctx => Config());
        api.Put("/weather/v1/config", UpdateConfig);
        api.Get("/weather/v1/regions", ctx => Regions());
        api.Put("/weather/v1/regions/{region}", UpdateRegion);
        api.Post("/weather/v1/override", Override);
        api.Delete("/weather/v1/override", ctx =>
            AdminMainThread.Run(() => { Service().ClearOverride(NativeWeatherHost.NowMs()); return AdminJson.Ok(); }));
        api.Post("/weather/v1/broadcast", ctx => AdminJson.Obj("count", 1L)); // 每帧 Sync 自动生效
        api.Post("/weather/v1/wind", ctx =>
            AdminMainThread.Run(() => { Service().RerollWind(); return AdminJson.Ok(); }));
    }

    static NativeWeatherService Service()
    {
        var s = NativeWeatherHost.Service;
        AdminMainThread.Run<object>(() => { ApplyPersisted(s); return null; });
        return s;
    }

    // 持久化配置只应用一次（直接改服务的公开字段；区域覆盖单独按 name 存）
    static void ApplyPersisted(NativeWeatherService s)
    {
        if (ReferenceEquals(configuredFor, s)) return;
        configuredFor = s;
        try
        {
            var db = AdminBackend.Db;
            if (db == null) return;
            string json = GameConfigDao.Get(db, ConfigCode, "");
            var cfg = string.IsNullOrEmpty(json) ? null : AdminJson.AsDict(AdminJson.Parse(json));
            if (cfg != null)
            {
                s.Enabled = AdminJson.GetBool(cfg, "enabled", s.Enabled);
                s.DayLength = AdminJson.GetLong(cfg, "dayLengthMs", s.DayLength);
                s.ChangeInterval = AdminJson.GetLong(cfg, "changeIntervalMs", s.ChangeInterval);
                s.OverrideHold = AdminJson.GetLong(cfg, "overrideHoldMs", s.OverrideHold);
                s.RainbowDuration = AdminJson.GetInt(cfg, "rainbowDurationSec", s.RainbowDuration);
                s.InjectSky = AdminJson.GetBool(cfg, "injectSky", s.InjectSky);
                s.SeasonDrift = AdminJson.GetBool(cfg, "seasonDrift", s.SeasonDrift);
            }
            if (db.HasTable("game_config"))
                foreach (var r in db.Table("game_config").rows)
                {
                    string code = DbStore.Str(r, "config_code");
                    if (!code.StartsWith("admin_weather_region_", StringComparison.Ordinal)) continue;
                    ApplyRegion(s, code.Substring("admin_weather_region_".Length),
                        AdminJson.AsDict(AdminJson.Parse(DbStore.Str(r, "config_value"))));
                }
        }
        catch { /* 无库/无配置时用默认 */ }
    }

    // ——————————— 状态 ———————————
    static object State()
    {
        var s = Service();
        long now = NativeWeatherHost.NowMs();
        bool overridden = s.OverrideSky >= 0 || s.OverrideMinute >= 0;
        int sky = s.RegionCount > 0 ? s.SkyOf(s.RegionCount - 1) : 0;
        long current = 0;
        try { current = AdminBackend.CurrentCharacterId != null ? AdminBackend.CurrentCharacterId() : 0; } catch { }
        return AdminJson.Obj(
            "enabled", s.Enabled,
            "minuteOfDay", (long)s.MinuteOfDay(now),
            "nightLevel", (double)s.NightLevel(now),
            "phase", sky >= 0 && sky < Profiles.Length ? Profiles[sky] : "clear",
            "weatherOverridden", overridden,
            "overrideProfile", s.OverrideSky >= 0 && s.OverrideSky < Profiles.Length ? Profiles[s.OverrideSky] : null,
            "timeFrozen", s.OverrideMinute >= 0,
            "overrideRemainingSec", (long)Math.Max(0, (s.OverrideUntil - now) / 1000),
            "nextRollInSec", (long)Math.Max(0, (s.NextRollAt - now) / 1000),
            "onlinePlayers", current > 0 ? 1L : 0L,
            "msPerGameMinute", (long)Math.Max(1, s.DayLength / 1440));
    }

    // ——————————— 配置 ———————————
    static object Config()
    {
        var s = Service();
        return AdminJson.Obj(
            "enabled", s.Enabled,
            "dayLengthMs", s.DayLength,
            "changeIntervalMs", s.ChangeInterval,
            "overrideHoldMs", s.OverrideHold,
            "rainbowDurationSec", (long)s.RainbowDuration,
            "injectSky", s.InjectSky,
            "seasonDrift", s.SeasonDrift);
    }

    static object UpdateConfig(AdminCtx ctx)
    {
        var body = ctx.Body;
        return AdminMainThread.Run(() =>
        {
            var s = Service();
            if (AdminJson.Get(body, "enabled") != null) s.Enabled = AdminJson.GetBool(body, "enabled", s.Enabled);
            if (AdminJson.Get(body, "dayLengthMs") != null) s.DayLength = Math.Max(60000L, AdminJson.GetLong(body, "dayLengthMs", s.DayLength));
            if (AdminJson.Get(body, "changeIntervalMs") != null) s.ChangeInterval = Math.Max(10000L, AdminJson.GetLong(body, "changeIntervalMs", s.ChangeInterval));
            if (AdminJson.Get(body, "overrideHoldMs") != null) s.OverrideHold = Math.Max(60000L, AdminJson.GetLong(body, "overrideHoldMs", s.OverrideHold));
            if (AdminJson.Get(body, "rainbowDurationSec") != null) s.RainbowDuration = Math.Max(0, AdminJson.GetInt(body, "rainbowDurationSec", s.RainbowDuration));
            if (AdminJson.Get(body, "injectSky") != null) s.InjectSky = AdminJson.GetBool(body, "injectSky", s.InjectSky);
            if (AdminJson.Get(body, "seasonDrift") != null) s.SeasonDrift = AdminJson.GetBool(body, "seasonDrift", s.SeasonDrift);

            // 立刻通知客户端重新 Sync（否则要等 60s 周期，看着像没生效）
            s.Touch();

            var db = AdminBackend.RequireDb();
            Upsert(db, ConfigCode, AdminJson.Write(Config()), "server", "Weather", "weather");
            return (long)1;
        });
    }

    // ——————————— 区域 ———————————
    static object Regions()
    {
        var s = Service();
        var list = new List<object>();
        for (int i = 0; i < s.RegionCount; i++)
        {
            var r = s.Region(i);
            int sky = s.SkyOf(i);
            list.Add(AdminJson.Obj(
                "region", r.Name,
                "label", r.Name,
                "currentProfile", sky >= 0 && sky < Profiles.Length ? Profiles[sky] : "clear",
                "forcedProfile", r.Forced >= 0 && r.Forced < Profiles.Length ? Profiles[r.Forced] : null,
                "weights", ToDoubles(r.Weights),
                "nightTint", (long)r.Tint,
                "paletteId", (long)r.Palette));
        }
        return list;
    }

    static object UpdateRegion(AdminCtx ctx)
    {
        string region = ctx.Param("region");
        var body = ctx.Body;
        return AdminMainThread.Run(() =>
        {
            var s = Service();
            int index = -1;
            for (int i = 0; i < s.RegionCount; i++)
                if (string.Equals(s.Region(i).Name, region, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
            if (index < 0) throw new AdminApiException("天气区域不存在: " + region);

            var r = s.Region(index);
            int oldForced = r.Forced, oldSky = s.SkyOf(index);
            if (AdminJson.Get(body, "forcedProfile") != null)
            {
                string forced = AdminJson.GetString(body, "forcedProfile");
                r.Forced = string.IsNullOrEmpty(forced) ? -1 : Array.IndexOf(Profiles, forced);
                if (r.Forced < 0 && !string.IsNullOrEmpty(forced))
                    throw new AdminApiException("未知天气: " + forced);
            }
            if (AdminJson.Get(body, "weights") is List<object> weights)
            {
                var w = new double[weights.Count];
                for (int i = 0; i < w.Length; i++) w[i] = Convert.ToDouble(weights[i], CultureInfo.InvariantCulture);
                r.Weights = w;
            }
            if (AdminJson.Get(body, "nightTint") != null) r.Tint = AdminJson.GetInt(body, "nightTint", r.Tint);
            if (AdminJson.Get(body, "paletteId") != null) r.Palette = AdminJson.GetInt(body, "paletteId", r.Palette);

            // forced 变了要让该区域立即落到新值（源端 override(region, profile) 的行为）
            s.ApplyForced(index, s.OverrideSky >= 0 ? s.OverrideSky : (oldForced >= 0 ? oldForced : oldSky), NativeWeatherHost.NowMs());

            var db = AdminBackend.RequireDb();
            Upsert(db, "admin_weather_region_" + r.Name, AdminJson.Write(AdminJson.Obj(
                "forced", (long)r.Forced,
                "weights", ToDoubles(r.Weights),
                "tint", (long)r.Tint,
                "paletteId", (long)r.Palette)), "server", "Weather", "weather region");
            return (long)1;
        });
    }

    static void ApplyRegion(NativeWeatherService s, string name, Dictionary<string, object> cfg)
    {
        if (cfg == null) return;
        for (int i = 0; i < s.RegionCount; i++)
        {
            if (!string.Equals(s.Region(i).Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            var r = s.Region(i);
            r.Forced = AdminJson.GetInt(cfg, "forced", r.Forced);
            if (AdminJson.Get(cfg, "weights") is List<object> weights)
            {
                var w = new double[weights.Count];
                for (int k = 0; k < w.Length; k++) w[k] = Convert.ToDouble(weights[k], CultureInfo.InvariantCulture);
                r.Weights = w;
            }
            r.Tint = AdminJson.GetInt(cfg, "tint", r.Tint);
            r.Palette = AdminJson.GetInt(cfg, "paletteId", r.Palette);
        }
    }

    // ——————————— 强制天气 / 时段 ———————————
    static object Override(AdminCtx ctx)
    {
        var body = ctx.Body;
        return AdminMainThread.Run(() =>
        {
            var s = Service();
            long now = NativeWeatherHost.NowMs();
            int? sky = null, minute = null;
            string profile = AdminJson.GetString(body, "profile");
            if (!string.IsNullOrEmpty(profile))
            {
                int idx = Array.IndexOf(Profiles, profile);
                if (idx < 0) throw new AdminApiException("未知天气: " + profile);
                sky = idx;
            }
            if (AdminJson.Get(body, "minuteOfDay") != null)
            {
                minute = Math.Max(0, Math.Min(1439, AdminJson.GetInt(body, "minuteOfDay", 720)));
            }
            if (sky == null && minute == null) throw new AdminApiException("天气或时段必填其一");
            long duration = AdminJson.Get(body, "durationMinutes") != null
                ? Math.Max(1, AdminJson.GetLong(body, "durationMinutes", 60)) * 60000L
                : s.OverrideHold;
            s.Override(sky, minute, duration, now);
            return (long)1;
        });
    }

    // ——————————— 小工具 ———————————
    static List<object> ToDoubles(double[] w)
    {
        var list = new List<object>();
        foreach (var v in w) list.Add(v);
        return list;
    }

    static void Upsert(DbStore db, string code, string value, string type, string subType, string desc)
    {
        var table = db.Table("game_config");
        var row = table.Find(r => DbStore.Str(r, "config_code") == code);
        if (row != null)
            table.Update(r => ReferenceEquals(r, row), new DbRow { ["config_value"] = value, ["update_time"] = Now() });
        else
            table.Insert(new DbRow
            {
                ["config_type"] = type,
                ["config_sub_type"] = subType,
                ["config_clazz"] = "java.lang.String",
                ["config_code"] = code,
                ["config_value"] = value,
                ["config_desc"] = desc,
                ["update_time"] = Now(),
            });
    }

    static string Now() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
