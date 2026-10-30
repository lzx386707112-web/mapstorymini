// 客户端地图信息：把 FieldCatalog 的地图数据适配成服务端的 IMapInfo。
// 服务端（Server/Death/DeathService）只认这个接口，不直接读 WZ。
using UnityEngine;

public sealed class FieldMapInfo : IMapInfo, IRespawnMapInfo
{
    public int ClosestSpawn(int mapId,int x,int y)
    {
        var map=FieldCatalog.GetMap(mapId);int chosen=0;double nearest=double.PositiveInfinity;
        if(map==null) return chosen;
        for(int i=0;i<map.portals.Count;i++)
        {
            var portal=map.portals[i];double dx=portal.x-x,dy=portal.y-y,distance=dx*dx+dy*dy;
            if(portal.type>=0 && portal.type<=1 && portal.tm==DeathService.MapNone && distance<nearest)
            {chosen=i;nearest=distance;}
        }
        return chosen;
    }
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
