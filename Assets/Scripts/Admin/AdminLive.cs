// 后台操作 → 正在运行的游戏。
//
// 单机版只有一个在线角色，后台改的是数据库，但游戏里的角色状态是进游戏时读进内存的
// （GameSession.Character / Profile），退出时才整份写回。所以只改库会出现两种结果：
//   · 当前角色：界面不变，而且退出时 SaveToDb 会把后台改动覆盖掉；
//   · 其他角色：下次选角时生效（这类不用特殊处理，落库就够）。
//
// 本文件只负责「当前角色」的实时同步：传送、改名、背包增删改、封停踢下线。
// 所有方法都在主线程执行（调用方 AdminBackend 已经走 AdminMainThread.Run）。
using System;

public static class AdminLive
{
    // —————————— 判定 ——————————
    public static bool IsCurrentCharacter(long characterId)
        => GameSession.InGame && characterId > 0 && GameSession.CharacterId == (int)characterId;

    public static bool IsCurrentAccount(long accountId)
        => GameSession.InGame && accountId > 0 && GameSession.AccountId == (int)accountId;

    // —————————— 传送 ——————————
    // 返回 null = 不需要实时处理（不是当前角色）；否则返回给前端的提示文案。
    public static string WarpToHenesys(long characterId) => WarpTo(characterId, AdminBackend.HenesysMap);

    public static string WarpTo(long characterId, long mapId)
    {
        if (!IsCurrentCharacter(characterId)) return null;

        var loop = GameLoop.I;
        if (loop == null || loop.field == null)
            return "已写入数据库；当前不在原版地图场景，重新进入地图后生效";

        // 和传送门同一条路径（黑场过渡），不走瞬间切图
        loop.field.WarpTo((int)mapId);
        // WarpTo 是异步的，先改本地记录，避免这期间存档把地图写回去
        Profile.LastMapId = (int)mapId;
        if (GameSession.Character != null && GameSession.Character.record != null)
            GameSession.Character.record.map = (int)mapId;
        return "当前角色已传送到地图 " + mapId;
    }

    // —————————— 改名 ——————————
    public static string Rename(long characterId, string newName)
    {
        if (!IsCurrentCharacter(characterId)) return null;
        if (GameSession.Character != null && GameSession.Character.record != null)
            GameSession.Character.record.name = newName;
        Profile.Data.playerName = newName;
        return "当前角色已改名，界面立即生效";
    }

    // —————————— 背包 ——————————
    // deleted=true 从内存移除；否则按库里的行重建那一条（数量/位置/属性都跟着变）。
    public static string SyncInventory(DbStore db, long characterId, long itemRowId, bool deleted)
    {
        if (!IsCurrentCharacter(characterId)) return null;
        var inv = GameSession.Character != null ? GameSession.Character.inventory : null;
        if (inv == null || itemRowId <= 0) return null;

        int index = inv.FindIndex(e => e.itemRowId == itemRowId);
        if (deleted)
        {
            if (index >= 0) inv.RemoveAt(index);
            return "已从当前角色背包移除该道具";
        }

        var row = db.Table("inventoryitems").Find(DbStore.Eq("inventoryitemid", itemRowId));
        if (row == null)
        {
            if (index >= 0) inv.RemoveAt(index);
            return null;
        }

        var entry = CharService.ToInventoryEntry(db, row);
        if (index >= 0) inv[index] = entry;
        else inv.Add(entry);
        return "当前角色背包已同步";
    }

    // —————————— 封停踢下线 ——————————
    public static string KickAccount(long accountId, string reason)
    {
        if (!IsCurrentAccount(accountId)) return null;

        string message = string.IsNullOrEmpty(reason) ? "账号已被管理员封停" : "账号已被封停：" + reason;
        try
        {
            var loop = GameLoop.I;
            if (loop != null)
            {
                // 先走正常退场（存档 → 拆场景 → 回登录态），此时账号还登录着，
                // 选角屏能正常加载；之后再清账号态并切回登录页。
                loop.ExitToCharSelect();
            }
            GameSession.Logout();
            var flow = LoginFlow.I;
            if (flow != null) flow.ForceLogout(message);
            else BottomBar.Toast(message);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[Admin] 踢下线失败: " + e.Message);
        }
        return message;
    }
}
