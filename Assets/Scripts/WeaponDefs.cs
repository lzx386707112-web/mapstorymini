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
        weapon = default; return false;
    }
    public static WeaponDef Get(string id) => TryGet(id, out var item) ? item : Get("1302000");
    public static int Count { get { Ensure(); return all.Count; } }
    public static WeaponDef At(int i) { Ensure(); return all[i]; }
    public static bool CanEquip(WeaponDef w, CharacterAttributes a, string cls)
    {
        var d = w.stats;
        int mask = cls == "beginner" ? 0 : cls == "mage" || cls == "priest" ? 2 : cls == "archer" ? 4 : 1;
        int job = NativeCombatData.Int(d,"reqJob");
        return (job == 0 || (job > 0 && (job & mask) != 0)) && a.level >= NativeCombatData.Int(d,"reqLevel") &&
            a.str >= NativeCombatData.Int(d,"reqSTR") && a.dex >= NativeCombatData.Int(d,"reqDEX") &&
            a.intelligence >= NativeCombatData.Int(d,"reqINT") && a.luk >= NativeCombatData.Int(d,"reqLUK");
    }
}
