// 魔幻箭筒VI 的箭镞模式存档。
//
// 282 的箭筒是纯客户端结算（服务端 TMS282.5 里 grep Quiver / 箭筒 / 箭簇 零命中），
// 所以这个「服务」只做一件事：把当前箭镞跟着角色存进 character_growth.quiverMode。
//
// 不需要新增协议：CharService.Full 已经把整个 GrowthState 下发给客户端
// （GameSession.Character.growth），换角色/重登会自动带回存档值；
// 客户端切换箭镞时调 Set 立即落库（成长数据不是退出时统一保存的）。
public static class NativeArrowModeService
{
    // 与客户端 NativeArrowMode 的 Off/Magic/Drain 保持同值。
    public const int Off = 0, Magic = 1, Drain = 2;

    public static int Get(DbStore db, int characterId)
    {
        if (db == null || characterId <= 0) return Off;
        int mode = GrowthDao.Load(db, characterId).quiverMode;
        return mode < Off || mode > Drain ? Off : mode;
    }

    // 整行覆盖是安全的：Load 已经把 honorExp / 预设 / 灵魂能量全读出来了，
    // 这里只改 quiverMode 再写回，不会把别的成长字段冲成默认值。
    public static bool Set(DbStore db, int characterId, int mode)
    {
        if (db == null || characterId <= 0) return false;
        if (mode < Off || mode > Drain) return false;
        lock (db)
        {
            var state = GrowthDao.Load(db, characterId);
            if (state.quiverMode == mode) return true;
            state.quiverMode = mode;
            GrowthDao.Save(db, characterId, state);
            return true;
        }
    }
}
