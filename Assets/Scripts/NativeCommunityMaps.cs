using MapleDash.Wz;
public static class NativeCommunityMaps {
    static ImgNode Info(int id) => WzAssets.Node("Map/Map/Map" + id / 100000000 + "/" + id.ToString("D9") + ".img/info");
    public static bool CanEnterCash(int map) {
        var info = Info(map); return info != null && (info.Int("fieldLimit") & 0x10) == 0;
    }
    public static bool CanTravel(int from, int to) {
        var source = Info(from); var destination = Info(to); if (source == null || destination == null) return false;
        return (source.Int("fieldLimit") & 0x10) == 0 && (destination.Int("fieldLimit") & 0x40) == 0 &&
            (destination.Int("forcedReturn", 999999999) == 999999999 || to / 100000000 == 0);
    }
}
