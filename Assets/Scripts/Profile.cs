using System.Collections.Generic;
using UnityEngine;

// 跨局持久化的玩家档案：金币、所选职业、已拥有/已装备武器、装扮（头发/服饰）。
// 用 PlayerPrefs 直接存字段（不用 JSON 序列化，避免依赖 MiniJson 的 stringify），
// 这样换机器/重装项目都不会因为解析失败丢档。
public static class Profile
{
    const string K_GOLD   = "mxd.gold";
    const string K_CLASS  = "mxd.cls";
    const string K_OWNED  = "mxd.owned";
    const string K_EQUIP  = "mxd.equip";
    const string K_HAIR   = "mxd.hair";
    const string K_OUTFIT = "mxd.outfit";

    public static int   gold;
    public static string cls       = "knight";
    public static string equipWeapon = "w_sword_basic";
    public static string hair      = "black";
    public static string outfit    = "none";

    static HashSet<string> _owned = new HashSet<string> { "w_sword_basic" };
    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;

        gold    = PlayerPrefs.GetInt(K_GOLD, 0);
        cls     = PlayerPrefs.GetString(K_CLASS, "knight");
        equipWeapon = PlayerPrefs.GetString(K_EQUIP, "w_sword_basic");
        hair    = PlayerPrefs.GetString(K_HAIR, "black");
        outfit  = PlayerPrefs.GetString(K_OUTFIT, "none");

        _owned.Clear();
        string o = PlayerPrefs.GetString(K_OWNED, "w_sword_basic");
        foreach (var p in o.Split(','))
            if (!string.IsNullOrEmpty(p)) _owned.Add(p);
        if (!_owned.Contains("w_sword_basic")) _owned.Add("w_sword_basic");
        if (!_owned.Contains(equipWeapon)) equipWeapon = "w_sword_basic";
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(K_GOLD, gold);
        PlayerPrefs.SetString(K_CLASS, cls);
        PlayerPrefs.SetString(K_EQUIP, equipWeapon);
        PlayerPrefs.SetString(K_HAIR, hair);
        PlayerPrefs.SetString(K_OUTFIT, outfit);
        PlayerPrefs.SetString(K_OWNED, string.Join(",", _owned));
        PlayerPrefs.Save();
    }

    // —— 金币 ——
    public static void AddGold(int n) { gold = Mathf.Max(0, gold + n); }
    public static bool Spend(int n)
    {
        if (gold < n) return false;
        gold -= n; return true;
    }

    // —— 武器 ——
    public static bool OwnsWeapon(string id) => _owned.Contains(id);
    public static void OwnWeapon(string id) => _owned.Add(id);
    public static void EquipWeapon(string id)
    {
        if (!_owned.Contains(id)) _owned.Add(id);
        equipWeapon = id;
    }

    // —— 装扮 ——
    public static void SetClass(string id)  => cls = id;
    public static void SetHair(string id)   => hair = id;
    public static void SetOutfit(string id) => outfit = id;

    public static IReadOnlyCollection<string> OwnedWeapons => _owned;
}
