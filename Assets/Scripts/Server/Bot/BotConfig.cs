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

    // —— 外观 ——
    // 假人外观模板数。>0 时把「每次运行全空间随机」收敛到固定模板池：同屏假人共享外观，
    // 纸娃娃缓存才能命中（否则每个假人每个动作帧都要 CPU 合成一次）。0 = 关闭，回退全随机。
    public static int AppearanceTemplateCount(DbStore db)
        => GetInt(db, "solo_mapling_appearance_templates", Server.Bot.BotAppearanceTemplates.DefaultCount);

    // —— 假人档案册（对齐联机账号表）——
    // 预置多少条固定档案（名字 + 外观模板都固定，见 BotDirectory）。
    // 这是「库里有多少个假人账号」，不是「同时在场多少人」；在场人数仍由
    // market_bot_max / environment_bot_max / 城镇配置决定。
    // 0 = 关闭档案（回退到每次现随机，仅供排查用）；这里单独读，因为 0 是有效值。
    public static int DirectorySize(DbStore db)
    {
        string value = db == null ? "" : GameConfigDao.Get(db, "solo_mapling_directory_size");
        if (int.TryParse(value, out int size)) return Math.Max(0, size);
        return Server.Bot.BotDirectory.DefaultSize;
    }

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

    // —— 组队伴侣（消耗品 2431159「基友集合」，后台「假人控制 → 组队」）——
    // 源端这套参数是写死在 PartyGrindCompat 常量里的，后台 party 分组恒为空；
    // 这里改成落库可配，玩法数值仍与原版一致（5 个 / 30 分钟 / 等级 ±3）。
    public static bool PartyCompanionEnabled(DbStore db) => GetBool(db, "solo_mapling_party_companion_enabled", true);
    // 是否必须先自己开组队（原版 canSummonFive 要求 partySize>=1）
    public static bool PartyCompanionRequireParty(DbStore db) => GetBool(db, "solo_mapling_party_companion_require_party", true);
    // 召唤数量：队伍上限 6 人，队长自己占 1，所以最多 5
    public static int PartyCompanionCount(DbStore db) => Math.Max(1, Math.Min(5, GetInt(db, "solo_mapling_party_companion_count", 5)));
    public static int PartyCompanionMinutes(DbStore db) => Math.Max(1, Math.Min(720, GetInt(db, "solo_mapling_party_companion_minutes", 30)));
    // 伴侣等级 = 队长等级 + [-jitter, jitter]，0 是有效值（完全同等级），不能走 GetInt 的 >0 兜底
    public static int PartyCompanionLevelJitter(DbStore db)
    {
        string value = db == null ? "" : GameConfigDao.Get(db, "solo_mapling_party_companion_level_jitter");
        return int.TryParse(value, out int jitter) ? Math.Max(0, Math.Min(10, jitter)) : 3;
    }
    public static long PartyCompanionDurationMs(DbStore db) => PartyCompanionMinutes(db) * 60000L;

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
