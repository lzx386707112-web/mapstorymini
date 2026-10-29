// WZ 名字查询（String.wz）。带缓存，避免每帧读 IMG。
// 从 DialogText.cs 拆出为独立文件：Admin（AdminCatalog）/NativeCashCatalog 也要用，
// 而离线工具链（tools/admin-check）只编纯 C#，WZ 读取走 #if 双路径。
using System;
using System.Collections.Generic;
using MapleDash.Wz;

// WZ 名字查询（String.wz）。带缓存，避免每帧读 IMG。
public static class WzNames
{
    static readonly Dictionary<int, string> npc = new Dictionary<int, string>();
    static readonly Dictionary<int, string> map = new Dictionary<int, string>();
    static readonly Dictionary<int, string> item = new Dictionary<int, string>();
    static readonly Dictionary<int, string> mob = new Dictionary<int, string>();

    public static string Npc(int id)
    {
        if (npc.TryGetValue(id, out var v)) return v;
        v = NpcOrNull(id) ?? "NPC " + id;
        npc[id] = v;
        return v;
    }

    // 只认 String/Npc.img 里真有的 name 字段。Npc() 会兜底成「NPC xxxxxxx」，
    // 名字条这种面向玩家的地方要用这个判断「这个 NPC 到底有没有名字」。
    public static string NpcOrNull(int id)
    {
        if (npcNamed.TryGetValue(id, out var v)) return v;
        try { v = WzAssets.FileRoot("String/Npc.img").Get(id.ToString())?.Text("name"); }
        catch (Exception) { v = null; }
        if (string.IsNullOrEmpty(v)) v = null;
        npcNamed[id] = v;
        return v;
    }
    static readonly Dictionary<int, string> npcNamed = new Dictionary<int, string>();

    public static string Mob(int id)
    {
        if (mob.TryGetValue(id, out var v)) return v;
        v = WzAssets.FileRoot("String/Mob.img").Get(id.ToString())?.Text("name");
        if (string.IsNullOrEmpty(v)) v = "怪物 " + id;
        mob[id] = v;
        return v;
    }

    public static string Map(int id)
    {
        if (map.TryGetValue(id, out var v)) return v;
        v = null;
        var root = WzAssets.FileRoot("String/Map.img");
        foreach (var region in root.Children)
        {
            var entry = region.Get(id.ToString());
            if (entry == null) continue;
            v = entry.Text("mapName");
            if (!string.IsNullOrEmpty(v)) break;
        }
        if (string.IsNullOrEmpty(v)) v = "地图 " + id;
        map[id] = v;
        return v;
    }

    public static string Item(int id)
    {
        if (item.TryGetValue(id, out var v)) return v;
        v = ItemUncached(id);
        if (string.IsNullOrEmpty(v)) v = "道具 " + id;
        item[id] = v;
        return v;
    }

    // 是否真有名字。Item() 对查不到的 id 会兜底成「道具 xxxxx」，
    // 判定商品数据是否完整时必须用这个，不能用 Item() 的返回值非空来判断。
    public static bool HasItem(int id)
    {
        if (named.TryGetValue(id, out var has)) return has;
        has = !string.IsNullOrEmpty(ItemUncached(id));
        named[id] = has;
        return has;
    }
    static readonly Dictionary<int, bool> named = new Dictionary<int, bool>();

    static string ItemUncached(int id)
    {
        int cat = id / 1000000;
        try
        {
            switch (cat)
            {
                case 1: return EquipItem(id);
                case 2: return FlatItem("String/Consume.img", id);
                case 3: return FlatItem("String/Ins.img", id);
                // String/Etc.img 多包一层同名节点：Etc/Etc/<id>/name
                case 4: return FlatItem("String/Etc.img", "Etc", id);
                // 宠物名不在 Cash.img，在 String/Pet.img/<id>/name
                case 5: return FlatItem("String/Cash.img", id) ?? FlatItem("String/Pet.img", id);
                // 910xxxx 礼包不在 String.wz，Item.wz/Special 自带 name
                case 9: return SpecialItem(id);
                default: return null;
            }
        }
        catch { return null; }
    }

    // String/Eqp.img 的真实层级是 Eqp/Eqp/<类别>/<id>/name，类别名随版本变化，按层遍历。
    static string EquipItem(int id)
    {
        var root = WzAssets.FileRoot("String/Eqp.img");
        foreach (var group in (root.Get("Eqp") ?? root).Children)
        {
            var n = group.Get(id.ToString())?.Text("name");
            if (!string.IsNullOrEmpty(n)) return n;
        }
        return null;
    }

    // Consume / Ins / Cash / Pet 的 <id> 节点直接在根层；Etc 要先下钻一层同名节点。
    static string FlatItem(string file, int id) => FlatItem(file, null, id);

    static string FlatItem(string file, string inner, int id)
    {
        var root = WzAssets.FileRoot(file);
        if (inner != null) root = root.Get(inner) ?? root;
        var name = root.Get(id.ToString())?.Text("name");
        return string.IsNullOrEmpty(name) ? null : name;
    }

    // Item.wz/Special/0910.img 里 910xxxx 的节点名是 7 位（不带补零），且 name 直接挂在节点下。
    static string SpecialItem(int id)
    {
        string file = "Item/Special/" + (id / 10000).ToString("D4") + ".img";
        if (!WzAssets.HasFile(file)) return null;
        var root = WzAssets.FileRoot(file);
        foreach (string key in new[] { id.ToString(), id.ToString("D8") })
        {
            var name = root.Get(key)?.Text("name");
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return null;
    }
}
