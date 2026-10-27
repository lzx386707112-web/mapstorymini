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
    // 身上套装差量（≡ Character.setItemBonus）。EquipLoadout.Apply 结算时填充，
    // CombatRules.Attack 消费 Damage%/BossDamage%/FinalDamage%；null 表示未结算过。
    public System.Collections.Generic.Dictionary<string, int> setItemBonus;

    public CharacterAttributes Copy() => (CharacterAttributes)MemberwiseClone();

    public CharacterAttributes WithWeapon(WeaponDef weapon, int attackBuff, out int watk, out int matk)
    {
        // 武器与身上防具统一在 EquipLoadout.Apply 里叠加（源端 Character.reapplyLocalStats），
        // 出战 PlayerCtrl 与能力值窗 StatWindow 共用同一条路，避免两套属性不一致。
        return EquipLoadout.Apply(this, weapon, attackBuff, out watk, out matk);
    }
}
