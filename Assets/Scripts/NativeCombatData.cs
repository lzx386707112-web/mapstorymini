using System.Collections.Generic;
using UnityEngine;

// WZ 与 SQL 的只读快照，导出入口 tools/export_combat_data.py。
public static class NativeCombatData
{
    static Dictionary<string, object> root;
    public static Dictionary<string, object> Root => root ?? (root =
        (Dictionary<string, object>)MiniJson.Parse(Resources.Load<TextAsset>("GameplayCombat").text));
    public static Dictionary<string, object> Mob(string key)
    {
        foreach (Dictionary<string, object> mob in (List<object>)Root["mobs"])
            if ((string)mob["key"] == key) return (Dictionary<string, object>)mob["stats"];
        return null;
    }
    public static int Int(Dictionary<string, object> d, string key, int fallback = 0)
        => d != null && d.TryGetValue(key, out var v) ? System.Convert.ToInt32(v) : fallback;
}

[System.Serializable]
public sealed class CharacterAttributes
{
    // Character.getDefault：未有练级/AP 分配记录时不伪造高级角色属性。
    public int level = 1, str = 12, dex = 5, intelligence = 4, luk = 4;
    public int maxHp = 50, maxMp = 5;
    public int weaponDefense, magicDefense, accuracy, avoidability;
    public float mastery = 0.1f, criticalChance, criticalBonus;

    public CharacterAttributes Copy() => (CharacterAttributes)MemberwiseClone();

    public CharacterAttributes WithWeapon(WeaponDef weapon, int attackBuff, out int watk, out int matk)
    {
        var result = (CharacterAttributes)MemberwiseClone();
        var d = weapon.stats;
        result.str += NativeCombatData.Int(d,"incSTR"); result.dex += NativeCombatData.Int(d,"incDEX");
        result.intelligence += NativeCombatData.Int(d,"incINT"); result.luk += NativeCombatData.Int(d,"incLUK");
        result.maxHp += NativeCombatData.Int(d,"incMHP"); result.maxMp += NativeCombatData.Int(d,"incMMP");
        result.weaponDefense += NativeCombatData.Int(d,"incPDD"); result.magicDefense += NativeCombatData.Int(d,"incMDD");
        result.accuracy += NativeCombatData.Int(d,"incACC"); result.avoidability += NativeCombatData.Int(d,"incEVA");
        watk = NativeCombatData.Int(d,"incPAD") + attackBuff;
        // Character.reapplyLocalStats：INT 计入总魔攻，装备 INT 不重复累加。
        matk = System.Math.Min(2000, result.intelligence + NativeCombatData.Int(d,"incMAD"));
        result.maxHp = System.Math.Max(50, result.maxHp); result.maxMp = System.Math.Max(5, result.maxMp);
        return result;
    }
}
