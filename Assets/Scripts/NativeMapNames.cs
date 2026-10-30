using System.Collections.Generic;
using MapleDash.Wz;

// 地图名查询：String.wz/Map.img/<大陆>/<mapId>/mapName
//
// 为什么要这个：源端家族面板的「成员位置」按钮（BtWhere，ID 2035，窗口坐标 (159,316)，
// 分派 0x91CF4F → 0x91D160 → 0x911C5C）会在聊天窗打一行「<成员名> 目前在 <地图名>」。
// 源端客户端用的是本地成员表里的 mapId，再查 String.wz 的 mapName 显示
// （0x911C5C → 0x9118A4 命中在线表 → 0x91192F 拷贝记录 → 格式串 "%s"）。
// 本工程没有客户端侧成员表，改由服务端 DTO 下发 mapId，这里按源端同一份资源取名字。
//
// 资源结构在 BeiDou 的 Data/String/Map.img 里是**两层**：第一层是大陆分组（china / maple / …），
// 第二层才是 mapId。所以不能直接 Get(mapId)，必须先找有该 id 子节点的大陆。
// 整棵树只在第一次查询时解析一次（WzAssets 自己也按路径缓存 ImgFile）。
public static class NativeMapNames
{
    const string Source = "String/Map.img";

    static readonly Dictionary<int, string> cache = new Dictionary<int, string>();
    static bool loaded;

    public static string Of(int mapId)
    {
        if (mapId < 0) return "";
        if (cache.TryGetValue(mapId, out string name)) return name;
        name = Lookup(mapId);
        cache[mapId] = name;
        return name;
    }

    // 查到名字就返回名字；查不到返回空串，调用方退化显示地图 id（原端查不到也是静默不显示）。
    static string Lookup(int mapId)
    {
        var root = Root();
        if (root == null) return "";
        string key = mapId.ToString();
        foreach (var continent in root.Children)
        {
            var node = continent.Get(key);
            if (node == null) continue;
            string text = node.Text("mapName");
            if (text != "") return text;
        }
        return "";
    }

    static ImgNode Root()
    {
        if (loaded) return cachedRoot;
        loaded = true;
        try { cachedRoot = WzAssets.FileRoot(Source); }
        catch { cachedRoot = null; }
        return cachedRoot;
    }

    static ImgNode cachedRoot;

    public static void Reset() { cache.Clear(); loaded = false; cachedRoot = null; }
}
