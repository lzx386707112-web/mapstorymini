using System.Collections.Generic;
using UnityEngine;

public enum WeaponKind { sword, dagger, bow, staff, mace, greatsword, axe, wand, greataxe, greatmace, spear, polearm, crossbow, claw, knuckle, gun }
public struct WeaponDef
{
    public string id, name;
    public int price;
    public WeaponKind kind;
    public Color color;
    public double multiplier;
    public Dictionary<string, object> stats;
}
public sealed class NativeWeaponShop
{
    public int id;
    public string name;
    public readonly List<WeaponDef> items = new List<WeaponDef>();
}

public static class WeaponDefs
{
    static readonly List<WeaponDef> all = new List<WeaponDef>();
    static readonly List<NativeWeaponShop> shops = new List<NativeWeaponShop>();
    static bool loaded;
    // 一次性旧原型存档迁移；烈焰剑无原物品，保留旧 ID 到档案审计字段后折为同类基础剑。
    public static string Canonical(string id)
    {
        switch (id)
        {
            case "w_sword_basic": return "1302000";
            case "w_dagger": return "1332000";
            case "w_bow": return "1452000";
            case "w_mace": return "1322000";
            case "w_staff": return "1382000";
            case "w_sword_flame": return "1302007";
            case "w_greatsword": return "1402000";
            default: return id;
        }
    }
    static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        foreach (Dictionary<string, object> item in (List<object>)NativeCombatData.Root["items"])
            all.Add(new WeaponDef { id = item["id"].ToString(), name = (string)item["name"],
                kind = (WeaponKind)System.Enum.Parse(typeof(WeaponKind), (string)item["kind"]),
                multiplier = System.Convert.ToDouble(item["multiplier"]), stats = (Dictionary<string, object>)item["stats"],
                color = new Color(.6f,.8f,1f), price = -1 });
        foreach (Dictionary<string, object> shop in (List<object>)NativeCombatData.Root["shops"])
        {
            var s = new NativeWeaponShop { id = NativeCombatData.Int(shop,"id"), name = (string)shop["name"] };
            foreach (Dictionary<string, object> row in (List<object>)shop["rows"])
            {
                var item = all.Find(w => w.id == row["item"].ToString());
                item.price = NativeCombatData.Int(row,"price");
                s.items.Add(item);
                int at = all.FindIndex(w => w.id == item.id);
                if (all[at].price < 0) all[at] = item;
            }
            shops.Add(s);
        }
    }
    public static IReadOnlyList<NativeWeaponShop> Shops { get { Ensure(); return shops.AsReadOnly(); } }
    public static bool TryGet(string id, out WeaponDef weapon)
    {
        Ensure(); id = Canonical(id);
        foreach (var item in all) if (item.id == id) { weapon = item; return true; }
        // Starter-town shop export is not a complete weapon catalog. Read the
        // original IMG for other weapons (including guns used by Rapid Fire).
        if (int.TryParse(id,out int itemId))
        {
            int category=itemId/10000;
            var kinds=new Dictionary<int,(WeaponKind kind,double multiplier)> {
                {130,(WeaponKind.sword,4)},{131,(WeaponKind.axe,4.4)},{132,(WeaponKind.mace,4.4)},
                {133,(WeaponKind.dagger,4)},{137,(WeaponKind.wand,3.6)},{138,(WeaponKind.staff,3.6)},
                {140,(WeaponKind.greatsword,4.6)},{141,(WeaponKind.greataxe,4.8)},{142,(WeaponKind.greatmace,4.8)},
                {143,(WeaponKind.spear,5)},{144,(WeaponKind.polearm,5)},{145,(WeaponKind.bow,3.4)},
                {146,(WeaponKind.crossbow,3.6)},{147,(WeaponKind.claw,3.6)},
                {148,(WeaponKind.knuckle,4.8)},{149,(WeaponKind.gun,3.6)} };
            if(kinds.TryGetValue(category,out var type))
            {
                var info=EquipCatalog.Info(itemId);
                if(info!=null)
                {
                    var stats=new Dictionary<string,object>();
                    foreach(var node in info.Children)
                        if(node.Value is int || node.Value is long || node.Value is double || node.Value is float || node.Value is string)
                            stats[node.Name]=node.Value;
                    weapon=new WeaponDef {id=id,name=WzNames.Item(itemId),kind=type.kind,multiplier=type.multiplier,
                        stats=stats,color=new Color(.6f,.8f,1f),price=-1};
                    all.Add(weapon);return true;
                }
            }
        }
        weapon = default; return false;
    }
    public static WeaponDef Get(string id) => TryGet(id, out var item) ? item : Get("1302000");
    public static int Count { get { Ensure(); return all.Count; } }
    public static WeaponDef At(int i) { Ensure(); return all[i]; }
    /// <summary>旧签名：cls 字符串 → job id（旧存档迁移/兼容调用方）。</summary>
    public static bool CanEquip(WeaponDef w, CharacterAttributes a, string cls)
        => CanEquip(w, a, JobDefs.JobIdOf(cls));

    /// <summary>
    /// 装备门槛。reqJob 是位掩码（源端 EquipmentCatalogService.matchesJob）：
    ///   0 = 全职业；1 战士 / 2 法师 / 4 弓手 / 8 飞侠 / 16 海盗，组合值按位与。
    /// 之前只映射了 0/1/2/4，飞侠(8) 和海盗(16) 全被当成战士判可穿，这里按 jobId 归位。
    /// </summary>
    public static bool CanEquip(WeaponDef w, CharacterAttributes a, int jobId)
    {
        var d = w.stats;
        int reqJob = NativeCombatData.Int(d, "reqJob");
        if (reqJob != 0 && (reqJob & JobDefs.JobBit(jobId)) == 0) return false;
        return a.level >= NativeCombatData.Int(d, "reqLevel") &&
            a.str >= NativeCombatData.Int(d, "reqSTR") && a.dex >= NativeCombatData.Int(d, "reqDEX") &&
            a.intelligence >= NativeCombatData.Int(d, "reqINT") && a.luk >= NativeCombatData.Int(d, "reqLUK");
    }
}
