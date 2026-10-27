// 道具节点定位（Node/IconNode）：商城（NativeCashCatalog）、后台图鉴（AdminCatalogData）共用。
// 从 NativeCashCatalog.cs 拆出为独立文件，便于离线工具链单独编译。
using System;
using System.Collections.Generic;
using MapleDash.Wz;

public static class NativeItemSource {
    public static ImgNode Node(int id) {
        if (id / 1000000 == 1) { string dir = EquipCatalog.DirOf(id); string file = "Character/" + dir + "/" + id.ToString("D8") + ".img"; return dir == null || !WzAssets.HasFile(file) ? null : WzAssets.FileRoot(file); }
        // 宠物是一个 IMG 一个宠，文件名是 7 位原始 id（不是 8 位补零），节点就是根。
        if (id / 10000 == 500) { string file = "Item/Pet/" + id + ".img"; return WzAssets.HasFile(file) ? WzAssets.FileRoot(file) : null; }
        string folder = id / 1000000 == 2 ? "Consume" : id / 1000000 == 3 ? "Install" : id / 1000000 == 4 ? "Etc" : id / 1000000 == 5 ? "Cash" : "Special";
        string path = "Item/" + folder + "/" + (id / 10000).ToString("D4") + ".img";
        if (!WzAssets.HasFile(path)) return null;
        // 8 位道具在 IMG 里是补零节点名（02000000）；910xxxx 礼包是原样 7 位节点名。
        var root = WzAssets.FileRoot(path);
        return root.Get(id.ToString("D8")) ?? root.Get(id.ToString());
    }
    public static UnityEngine.Texture2D Icon(int id) {
        var icon = IconNode(id);
        return icon == null ? null : WzAssets.Texture(icon);
    }
    // 只取节点不解码，用于商品完整性校验（逐行解码 2 千多个 canvas 太慢）。
    public static ImgNode IconNode(int id) {
        var node = Node(id);
        // 常规道具图标是 info/icon，Item.wz/Special 的礼包图标直接挂在节点下。
        return node?.Get("info/icon") ?? node?.Get("icon");
    }
}
