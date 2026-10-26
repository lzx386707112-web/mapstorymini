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
public sealed class NativeCashCatalog : INativeCashCatalog {
    readonly Dictionary<int, CashCommodity> items = new Dictionary<int, CashCommodity>();
    readonly Dictionary<int, IList<int>> packages = new Dictionary<int, IList<int>>();
    // BeiDou.exe 0047FFA2 起逐字段解析 Etc/Commodity.img；这里只写 IMG 里真实存在的 13 个字段
    // （Count/ItemId/SN/OnSale/Period/Gender/Price/Priority/Class/PbCash/PbGift/PbPoint/Limit），
    // Bonus/MaplePoint/Meso/ForPremiumUser/PackageSN 在本版 WZ 里没有，留给数据库覆盖层。
    public NativeCashCatalog() {
        foreach (var n in WzAssets.FileRoot("Etc/Commodity.img").Children) {
            if (n.Get("SN") == null || n.Get("ItemId") == null) continue;
            int period = n.Int("Period", 1); int sn = n.Int("SN");
            items[sn] = new CashCommodity {
                sn = sn, itemId = n.Int("ItemId"), price = n.Int("Price"),
                count = n.Int("Count", 1), period = period == 0 ? 90 : period,
                onSale = n.Int("OnSale"), gender = n.Int("Gender", 2),
                priority = n.Int("Priority"), clz = n.Int("Class"), limit = IntOf(n, "Limit"),
                pbCash = n.Int("PbCash"), pbPoint = n.Int("PbPoint"), pbGift = n.Int("PbGift")
            };
        }
        foreach (var n in WzAssets.FileRoot("Etc/CashPackage.img").Children) {
            if (!int.TryParse(n.Name, out int id)) continue;
            var values = new List<int>(); var sns = n.Get("SN"); if (sns != null) foreach (var sn in sns.Children) if (sn.Value != null) values.Add(Convert.ToInt32(sn.Value)); packages[id] = values;
        }
    }
    // 004804A4 起客户端把 Limit 当 int 读（缺省 0），但本版 Commodity.img 有 92 行 Limit 是字符串
    // （源端 CashShop.java:159 把 getInteger("Limit") 整行注释掉就是因为这个）。
    // 按原端 property->getInt(default)「类型不符取默认值」的语义解析，不能让整张表解析失败。
    static int IntOf(ImgNode node, string key, int fallback = 0) {
        var value = node.Get(key)?.Value;
        if (value == null) return fallback;
        if (value is int number) return number;
        if (value is string text && int.TryParse(text, out int parsed)) return parsed;
        try { return Convert.ToInt32(value); } catch { return fallback; }
    }
    public IEnumerable<CashCommodity> All => items.Values;
    public CashCommodity Find(int sn) => items.TryGetValue(sn, out var item) ? item : null;
    public IList<int> Package(int id) => packages.TryGetValue(id, out var package) ? package : null;
    public bool Exists(int id) => Package(id) != null || NativeItemSource.Node(id) != null;
    // 商品是否"完整"：客户端既有名字（String.wz / Item.wz/Special）又有图标节点。
    // 缺少任一项的商品不进商城列表（源端这类行在客户端会画出空名字/空图标的格子）。
    readonly Dictionary<int, bool> complete = new Dictionary<int, bool>();
    public bool Complete(int id) {
        bool result;
        if (complete.TryGetValue(id, out result)) return result;
        result = WzNames.HasItem(id) && (Package(id) != null || NativeItemSource.IconNode(id) != null);
        complete[id] = result;
        return result;
    }
    public bool IsCash(int id) => id / 1000000 == 5 || NativeItemSource.Node(id)?.Int("info/cash") == 1;
    public int EquipSlot(int id) => global::EquipSlot.SlotOf(id);
    public string CanEquip(int id, CharRecord c) {
        var n = EquipCatalog.Info(id); if (n == null) return "装备数据缺失";
        int gender = id / 1000 % 10; if (gender < 2 && gender != c.gender) return "装备性别不符";
        int job = n.Int("reqJob"); if (job != 0 && (job & JobDefs.JobBit(c.job)) == 0) return "职业不符";
        if (c.level < n.Int("reqLevel") || c.str < n.Int("reqSTR") || c.dex < n.Int("reqDEX") || c.int_ < n.Int("reqINT") || c.luk < n.Int("reqLUK")) return "未达到装备要求";
        return null;
    }
    public DbRow Equipment(int id) {
        var n = EquipCatalog.Info(id); if (n == null) return null;
        string[] columns = { "str", "dex", "int", "luk", "hp", "mp", "watk", "matk", "wdef", "mdef", "acc", "avoid", "hands", "speed", "jump", "upgradeslots" };
        string[] fields = { "incSTR", "incDEX", "incINT", "incLUK", "incMHP", "incMMP", "incPAD", "incMAD", "incPDD", "incMDD", "incACC", "incEVA", "incCraft", "incSpeed", "incJump", "tuc" };
        var row = new DbRow(); for (int i = 0; i < fields.Length; i++) row[columns[i]] = (long)n.Int(fields[i]); return row;
    }
}
