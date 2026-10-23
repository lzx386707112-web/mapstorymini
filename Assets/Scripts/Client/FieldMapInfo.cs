// 客户端地图信息：把 FieldCatalog 的地图数据适配成服务端的 IMapInfo。
// 服务端（Server/Death/DeathService）只认这个接口，不直接读 WZ。
using UnityEngine;

public sealed class FieldMapInfo : IMapInfo
{
    public bool IsTown(int mapId)
    {
        FieldCatalog.Ensure();
        return FieldCatalog.Maps.TryGetValue(mapId, out var m) && m.town;
    }

    public int ReturnMap(int mapId)
    {
        FieldCatalog.Ensure();
        return FieldCatalog.Maps.TryGetValue(mapId, out var m) ? m.returnMap : DeathService.MapNone;
    }

    public bool NoExpDecrease(int mapId)
    {
        FieldCatalog.Ensure();
        if (!FieldCatalog.Maps.TryGetValue(mapId, out var m)) return false;
        // 源端 FieldLimit.NO_EXP_DECREASE = 0x10（第 5 位）
        return (m.fieldLimit & 0x10) != 0;
    }
}
