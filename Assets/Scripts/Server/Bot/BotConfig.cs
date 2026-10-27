// SoloMapling 假人系统配置。逐条对齐源端 soloMapling.SoloMaplingConfig：
//   所有键名与默认值原样照搬；源端从 GameConfig.getServerString/Int 读取，
//   安卓后端 GameConfigDao 需要 DbStore 上下文，故方法签名接收 db（与 CharFactory 一致）。
using System;

// 键前缀 solo_mapling_* 与源端完全一致。

public static class BotConfig
{
    // —— 自动环境 / 人数上限 ——
    public static bool AutoEnvironmentEnabled(DbStore db) => GetBool(db, "solo_mapling_auto_environment", false);
    public static int EnvironmentBotMax(DbStore db) => GetInt(db, "solo_mapling_environment_bot_max", 30);
    public static int MarketBotMax(DbStore db) => GetInt(db, "solo_mapling_market_bot_max", 10);
    public static int MarketShopMax(DbStore db) => GetInt(db, "solo_mapling_market_shop_max", 10);

    // —— 自动地图 bot（氛围路人）——
    public static bool AutoMapBotsEnabled(DbStore db) => GetBool(db, "solo_mapling_auto_map_bots_enabled", true);
    public static int AutoMapBotsMin(DbStore db) => GetInt(db, "solo_mapling_auto_map_bots_min", 2);
    public static int AutoMapBotsMax(DbStore db) => GetInt(db, "solo_mapling_auto_map_bots_max", 4);
    public static int AutoMapBotsRadius(DbStore db) => GetInt(db, "solo_mapling_auto_map_bots_radius", 350);
    public static bool AutoMapBotsRandomPositionEnabled(DbStore db) => GetBool(db, "solo_mapling_auto_map_bots_random_position", true);
    public static bool AmbientBehaviorEnabled(DbStore db) => GetBool(db, "solo_mapling_auto_map_bots_ambient_enabled", true);
    public static bool AmbientMoveEnabled(DbStore db) => GetBool(db, "solo_mapling_auto_map_bots_move_enabled", true);
    public static bool AmbientChatEnabled(DbStore db) => GetBool(db, "solo_mapling_auto_map_bots_chat_enabled", true);
    public static bool AmbientEmoteEnabled(DbStore db) => GetBool(db, "solo_mapling_auto_map_bots_emote_enabled", true);
    public static bool AmbientFacePlayerEnabled(DbStore db) => GetBool(db, "solo_mapling_auto_map_bots_face_player_enabled", true);
    public static bool AmbientHasAnyActionEnabled(DbStore db) =>
        AmbientMoveEnabled(db) || AmbientChatEnabled(db) || AmbientEmoteEnabled(db) || AmbientFacePlayerEnabled(db);
    public static long AmbientActionMinMs(DbStore db) => GetInt(db, "solo_mapling_auto_map_bots_action_min_ms", 2000);
    public static long AmbientActionMaxMs(DbStore db)
    {
        long min = AmbientActionMinMs(db);
        long max = GetInt(db, "solo_mapling_auto_map_bots_action_max_ms", 5000);
        return Math.Max(min, max);
    }

    // —— 外观 / 装饰 ——
    public static bool RandomChineseNameEnabled(DbStore db) => GetBool(db, "solo_mapling_bot_random_chinese_name", true);
    public static bool RandomSkillEnabled(DbStore db) => GetBool(db, "solo_mapling_bot_random_skill_enabled", true);
    public static bool RandomChairEnabled(DbStore db) => GetBool(db, "solo_mapling_bot_random_chair_enabled", true);
    public static bool RandomBodyEnabled(DbStore db) => GetBool(db, "solo_mapling_bot_random_body_enabled", true);
    public static bool NormalEquipsEnabled(DbStore db) => GetBool(db, "solo_mapling_bot_normal_equips_enabled", true);
    public static bool FullEquipsEnabled(DbStore db) => GetBool(db, "solo_mapling_bot_full_equips_enabled", true);
    public static bool NxEquipsEnabled(DbStore db) => GetBool(db, "solo_mapling_bot_nx_equips_enabled", true);
    public static bool AnyOutfitFeatureEnabled(DbStore db) => NormalEquipsEnabled(db) || NxEquipsEnabled(db);
    public static bool DeferredDecorationEnabled(DbStore db) => GetBool(db, "solo_mapling_bot_deferred_decoration_enabled", false);

    // —— 各功能开关 ——
    public static bool HenesysBotsChangeMapsEnabled(DbStore db) => GetBool(db, "solo_mapling_henesys_bots_change_maps", false);
    public static bool FmBotsEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_fm_bots_enabled", true);
    public static bool FmMerchantsEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_fm_merchants_enabled", true);
    public static bool FmRegionFillEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_fm_region_fill_enabled", true);
    public static bool HenesysCrowdEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_henesys_crowd_enabled", true);
    public static bool HenesysMarketCrowdEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_henesys_market_crowd_enabled", true);
    public static bool HenesysParkCrowdEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_henesys_park_crowd_enabled", true);
    public static bool HenesysPotionShopCrowdEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_henesys_potion_shop_crowd_enabled", true);
    public static bool HenesysGameZoneCrowdEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_henesys_game_zone_crowd_enabled", true);
    public static bool GachaBotsEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_gacha_bots_enabled", true);
    public static bool OpqLobbyBotsEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_opq_lobby_bots_enabled", true);
    public static bool BlackjackTablesEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_blackjack_tables_enabled", true);
    public static bool CasinoNpcEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_casino_npc_enabled", true);
    public static bool ConversationEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_conversation_enabled", true);
    public static bool HotPotatoEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_hot_potato_enabled", true);
    public static bool TutorialBotEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_tutorial_bot_enabled", true);
    public static bool PetParkJqBotsEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_pet_park_jq_bots_enabled", true);
    public static bool PetParkSocialBotsEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_pet_park_social_bots_enabled", true);
    public static bool GameZoneHostBotsEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_game_zone_host_bots_enabled", true);
    public static bool PotionShopDropGameEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_potion_shop_drop_game_enabled", true);
    public static bool ScrollBotsEnabled(DbStore db) => GetBool(db, "solo_mapling_feature_scroll_bots_enabled", true);

    // —— 自由市场循环 ——
    public static bool MarketWanderEnabled(DbStore db) => GetBool(db, "solo_mapling_market_wander_enabled", true);
    public static bool MarketHawkEnabled(DbStore db) => GetBool(db, "solo_mapling_market_hawk_enabled", true);
    public static bool MarketBrowseShopsEnabled(DbStore db) => GetBool(db, "solo_mapling_market_browse_shops_enabled", true);
    public static bool MarketIdleWhenEmpty(DbStore db) => GetBool(db, "solo_mapling_market_idle_when_empty", true);
    public static long MarketTickMs(DbStore db) => Math.Max(150, GetInt(db, "solo_mapling_market_tick_ms", 250));
    public static long MarketSpeechGapMs(DbStore db) => Math.Max(250, GetInt(db, "solo_mapling_market_speech_gap_ms", 500));
    public static int MarketPathfindPerTick(DbStore db) => GetInt(db, "solo_mapling_market_pathfind_per_tick", 3);
    public static bool MarketAutoStartEnabled(DbStore db) => GetBool(db, "solo_mapling_market_auto_start", true);

    static bool GetBool(DbStore db, string key, bool def)
    {
        string value = db == null ? "" : GameConfigDao.Get(db, key);
        return string.IsNullOrEmpty(value) ? def : value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
    static int GetInt(DbStore db, string key, int def)
    {
        int value = db == null ? 0 : GameConfigDao.GetInt(db, key);
        return value > 0 ? value : def;
    }
}
