using UnityEngine;

// 武器目录：金币购买，每个武器改玩家的普攻属性（伤害/攻速/攻击距离），
// 同时决定手持道具的视觉形状（kind）。起始武器 w_sword_basic 免费拥有。
public enum WeaponKind { sword, dagger, bow, staff, mace, greatsword }

[System.Serializable]
public struct WeaponDef
{
    public string id;
    public string name;
    public int price;
    public WeaponKind kind;
    public Color color;
    public float dmgAdd;      // 直接加成的伤害
    public float dmgMul;      // 伤害倍率
    public float intervalMul; // 攻速倍率（<1 更快）
    public float reachAdd;    // 攻击距离加成
}

public static class WeaponDefs
{
    static readonly WeaponDef[] All = new WeaponDef[]
    {
        W("w_sword_basic",  "铁剑",     0,   WeaponKind.sword,      new Color(0.80f,0.82f,0.88f), 0f,   1.00f, 1.00f, 0.0f),
        W("w_dagger",       "匕首",     120, WeaponKind.dagger,     new Color(0.82f,0.82f,0.86f), 0f,   0.80f, 0.60f, -0.2f),
        W("w_bow",          "短弓",     150, WeaponKind.bow,        new Color(0.70f,0.85f,0.50f), 0f,   1.10f, 0.90f, 0.6f),
        W("w_mace",         "钝器",     160, WeaponKind.mace,       new Color(0.80f,0.60f,0.40f), 4f,   1.20f, 1.15f, 0.0f),
        W("w_staff",        "法杖",     180, WeaponKind.staff,      new Color(0.60f,0.80f,1.00f), 6f,   1.25f, 1.10f, 0.3f),
        W("w_sword_flame",  "烈焰剑",   260, WeaponKind.sword,      new Color(1.00f,0.50f,0.20f), 8f,   1.40f, 1.00f, 0.2f),
        W("w_greatsword",   "巨剑",     520, WeaponKind.greatsword, new Color(0.90f,0.90f,0.95f), 14f,  1.90f, 1.40f, 0.4f),
    };

    static WeaponDef W(string id, string name, int price, WeaponKind k, Color c, float dmgAdd, float dmgMul, float intervalMul, float reachAdd)
        => new WeaponDef { id = id, name = name, price = price, kind = k, color = c,
                           dmgAdd = dmgAdd, dmgMul = dmgMul, intervalMul = intervalMul, reachAdd = reachAdd };

    public static WeaponDef Get(string id)
    {
        foreach (var d in All) if (d.id == id) return d;
        return All[0];
    }

    public static int Count => All.Length;
    public static WeaponDef At(int i) => All[i];
}
