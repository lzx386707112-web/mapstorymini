using System;
using System.Collections.Generic;
using MapleDash.Wz;

public static class NativeItemSource {
    public static ImgNode Node(int id) {
        if (id / 1000000 == 1) { string dir = EquipCatalog.DirOf(id); string file = "Character/" + dir + "/" + id.ToString("D8") + ".img"; return dir == null || !WzAssets.HasFile(file) ? null : WzAssets.FileRoot(file); }
        if (id / 10000 == 500) { string file = "Item/Pet/" + id.ToString("D8") + ".img"; return WzAssets.HasFile(file) ? WzAssets.FileRoot(file) : null; }
        string folder = id / 1000000 == 2 ? "Consume" : id / 1000000 == 3 ? "Install" : id / 1000000 == 4 ? "Etc" : id / 1000000 == 5 ? "Cash" : "Special";
        string path = "Item/" + folder + "/" + (id / 10000).ToString("D4") + ".img";
        return WzAssets.HasFile(path) ? WzAssets.FileRoot(path).Get(id.ToString("D8")) : null;
    }
    public static UnityEngine.Texture2D Icon(int id) { var node = Node(id)?.Get("info/icon"); return node == null ? null : WzAssets.Texture(node); }
}
public sealed class NativeCashCatalog : INativeCashCatalog {
    readonly Dictionary<int, CashCommodity> items = new Dictionary<int, CashCommodity>();
    readonly Dictionary<int, IList<int>> packages = new Dictionary<int, IList<int>>();
    public NativeCashCatalog() {
        foreach (var n in WzAssets.FileRoot("Etc/Commodity.img").Children) {
            if (n.Get("SN") == null || n.Get("ItemId") == null) continue;
            int period = n.Int("Period", 1); int sn = n.Int("SN");
            items[sn] = new CashCommodity { sn = sn, itemId = n.Int("ItemId"), price = n.Int("Price"), count = n.Int("Count", 1), period = period == 0 ? 90 : period, onSale = n.Int("OnSale"), gender = n.Int("Gender", 2) };
        }
        foreach (var n in WzAssets.FileRoot("Etc/CashPackage.img").Children) {
            if (!int.TryParse(n.Name, out int id)) continue;
            var values = new List<int>(); var sns = n.Get("SN"); if (sns != null) foreach (var sn in sns.Children) if (sn.Value != null) values.Add(Convert.ToInt32(sn.Value)); packages[id] = values;
        }
    }
    public IEnumerable<CashCommodity> All => items.Values;
    public CashCommodity Find(int sn) => items.TryGetValue(sn, out var item) ? item : null;
    public IList<int> Package(int id) => packages.TryGetValue(id, out var package) ? package : null;
    public bool Exists(int id) => Package(id) != null || NativeItemSource.Node(id) != null;
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
