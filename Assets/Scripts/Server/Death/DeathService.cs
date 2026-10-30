// 死亡 / 复活规则。逐条对齐源端：
//   Character.playerDead()   Character.java:7478-7545
//   Character.respawn()      Character.java:7596-7613
//   FULL_HP_RESPAWN_MAPS     Character.java:95-98
//   ChangeMapHandler:99-118  客户端 dying=1 → respawn(map.getReturnMapId())
//
// 地图信息（town / returnMap / NO_EXP_DECREASE）由客户端数据提供，
// 通过 IMapInfo 注入，Server 层不直接读 WZ。
using System;

public interface IMapInfo
{
    bool IsTown(int mapId);
    int ReturnMap(int mapId);          // 地图 info/returnMap；999999999 表示无（源端 MapId.NONE）
    bool NoExpDecrease(int mapId);     // fieldLimit NO_EXP_DECREASE
}

public interface IRespawnMapInfo
{
    int ClosestSpawn(int mapId,int x,int y);
}

public sealed class DeathResult
{
    public int expLost;
    public bool usedSafetyCharm;
    public bool noExpLoss;             // 新手 / 城镇保护 / fieldLimit 命中
}

public static class DeathService
{
    public const int SafetyCharm = 5130000;      // ItemId.SAFETY_CHARM
    public const int EasterBasket = 4031283;
    public const int EasterCharm = 4140903;
    public const int WheelOfFortune = 5510000;

    // 源端：这些地图复活后满血
    static readonly int[] FullHpRespawnMaps =
    {
        262030300, 262031300, 450010100, 221040001,
        450009400, 900000207, 410002060
    };

    public const int MapNone = 999999999;

    /// <summary>
    /// HP 归零时的处罚结算（不含 buff 清理，那在客户端表现层）。
    /// 返回扣多少经验；调用方负责把 exp 写到角色记录。
    /// </summary>
    public static DeathResult PlayerDead(CharRecord chr, int mapId, IMapInfo mapInfo, int safetyCharmCount)
    {
        var res = new DeathResult();

        // 安全护符：消耗 1 个，不扣经验（源端 7497-7511，道场地图除外——本地没有道场）
        if (safetyCharmCount > 0)
        {
            res.usedSafetyCharm = true;
            return res;
        }

        // 新手不扣经验（源端 7512）
        if (chr.job == 0) { res.noExpLoss = true; return res; }

        // 地图 NO_EXP_DECREASE 保护
        if (mapInfo != null && mapInfo.NoExpDecrease(mapId)) { res.noExpLoss = true; return res; }

        // 城镇只扣 1%；野外 LUK<50 扣 10%，否则 20%（源端 7514-7531）
        int dummy = ServerExpTable.GetExpNeededForLevel(chr.level);
        bool town = mapInfo != null && mapInfo.IsTown(mapId);
        if (town) dummy /= 100;
        else dummy = chr.luk < 50 ? dummy / 10 : dummy / 20;

        res.expLost = chr.exp > dummy ? dummy : chr.exp;
        return res;
    }

    /// <summary>
    /// 复活后血量（源端 respawn 7604-7610）：
    /// 特殊图满血 / 用过护符 30% / 其余 50。
    /// </summary>
    public static int RespawnHp(int returnMap, bool usedSafetyCharm, int maxHp)
    {
        foreach (var m in FullHpRespawnMaps) if (m == returnMap) return maxHp;
        if (usedSafetyCharm) return (int)Math.Ceiling(maxHp * 0.3);
        return 50;
    }

    public static int RespawnMp(int returnMap, bool usedSafetyCharm, int maxMp)
    {
        foreach (var m in FullHpRespawnMaps) if (m == returnMap) return maxMp;
        if (usedSafetyCharm) return (int)Math.Ceiling(maxMp * 0.3);
        return maxMp;   // 源端 respawn 只动 HP；MP 保持护符分支的 30%，常规分支不动
    }

    /// <summary>
    /// 死亡确认后要去的目标地图：当前地图的 returnMap；无则原地（源端 getReturnMap）。
    /// </summary>
    public static int RespawnTargetMap(int currentMap, IMapInfo mapInfo)
    {
        if (mapInfo == null) return currentMap;
        int rm = mapInfo.ReturnMap(currentMap);
        return rm == MapNone ? currentMap : rm;
    }
}
