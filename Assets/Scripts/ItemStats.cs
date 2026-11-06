using System.Collections.Generic;

// ————————————————————————————————————————————————————————————
// 一件装备实例的「有效属性」（基础 + 星力 + 星火），tooltip 与战斗力预览共用同一份。
// 口径与 ItemTooltip.DrawEquipment 原本内联的那段逐条一致：
//   · 282 实例（equipmentData.enhancementVersion == 282）以实例行为准，行里没种值才回落模板；
//   · 老存档按 max(实例, 模板) 取大，避免早期没写全的实例把属性算少；
//   · 星力只在 282 且不是「痕迹」状态时叠，星火按 DescribeFlame 的行映射到列。
// 列顺序固定为 Keys，下标即属性：0 STR / 1 DEX / 2 INT / 3 LUK / 4 HP / 5 MP /
// 6 watk / 7 matk / 8 wdef / 9 mdef / 10 acc / 11 avoid / 12 hands / 13 speed / 14 jump。
// ————————————————————————————————————————————————————————————
public struct ItemStats
{
    public static readonly string[] Keys =
        { "incSTR", "incDEX", "incINT", "incLUK", "incMHP", "incMMP", "incPAD", "incMAD",
          "incPDD", "incMDD", "incACC", "incEVA", "incCraft", "incSpeed", "incJump" };
    static readonly string[] StatKeys =
        { "str", "dex", "int", "luk", "hp", "mp", "watk", "matk", "wdef", "mdef", "acc", "avoid", "hands", "speed", "jump" };
    // 星火的 statVal → 属性列（与 NativeGrowthStats.ApplyFlameStat 的映射同一张表）。
    static readonly int[][] FlameColumns =
    {
        new[]{0}, new[]{1}, new[]{2}, new[]{3}, new[]{0,1}, new[]{0,2}, new[]{0,3}, new[]{1,2}, new[]{1,3},
        new[]{2,3}, new[]{6}, new[]{7}, new[]{8}, new[]{4}, new[]{5}, new[]{13}, new[]{14}
    };

    public int[] baseStats;
    public int[] starStats;
    public int[] flameStats;
    public bool starForceVestige;

    public int Str => Sum(0);
    public int Dex => Sum(1);
    public int Int => Sum(2);
    public int Luk => Sum(3);
    public int Hp => Sum(4);
    public int Mp => Sum(5);
    public int Watk => Sum(6);
    public int Matk => Sum(7);

    int Sum(int column) => (baseStats == null ? 0 : baseStats[column])
        + (starStats == null ? 0 : starStats[column]) + (flameStats == null ? 0 : flameStats[column]);

    /// <summary>只按模板取值（没有实例时的回落，等价于旧代码 actual == null 那支）。</summary>
    public static ItemStats Template(int itemId)
    {
        var result = new ItemStats
        {
            baseStats = new int[Keys.Length], starStats = new int[Keys.Length], flameStats = new int[Keys.Length]
        };
        var info = EquipCatalog.Info(itemId);
        if (info == null) return result;
        for (int i = 0; i < Keys.Length; i++) result.baseStats[i] = info.Int(Keys[i]);
        return result;
    }

    /// <summary>装备实例（可为空：只看模板）。entry 为空时返回全 0。</summary>
    public static ItemStats Of(InventoryEntry entry)
    {
        var result = new ItemStats
        {
            baseStats = new int[Keys.Length], starStats = new int[Keys.Length], flameStats = new int[Keys.Length]
        };
        if (entry == null || entry.itemId <= 0) return result;
        var info = EquipCatalog.Info(entry.itemId);
        if (info == null) return result;

        bool version282 = entry.equipmentData != null
            && entry.equipmentData.TryGetValue("enhancementVersion", out var version)
            && System.Convert.ToInt32(version) == 282;
        result.starForceVestige = entry.equipmentData != null
            && entry.equipmentData.TryGetValue("starForceVestige", out var vestige)
            && System.Convert.ToInt32(vestige) != 0;
        bool native = NativeScrollService.HasStats(entry);
        int[] actual = entry.isEquip
            ? new[] { entry.str, entry.dex, entry.int_, entry.luk, entry.hp, entry.mp, entry.watk, entry.matk,
                      entry.wdef, entry.mdef, entry.acc, entry.avoid, entry.hands, entry.speed, entry.jump }
            : null;

        for (int i = 0; i < Keys.Length; i++)
        {
            if (version282) result.baseStats[i] = native && actual != null ? actual[i] : info.Int(Keys[i]);
            else if (actual == null) result.baseStats[i] = info.Int(Keys[i]);
            else result.baseStats[i] = System.Math.Max(actual[i], info.Int(Keys[i]));
        }

        if (version282 && !result.starForceVestige)
        {
            var flat = new Dictionary<string, int>();
            for (int i = 0; i < Keys.Length; i++) flat[StatKeys[i]] = result.baseStats[i];
            int level = info.Int("reqLevel");
            if (entry.equipmentData != null && entry.equipmentData.TryGetValue("iIncReq", out var incReq))
                level += System.Convert.ToInt32(incReq);
            var star = StarForceStats.Calculate(entry.itemId, level, entry.starLevel, info.Int("superiorEqp") != 0, flat);
            for (int i = 0; i < Keys.Length; i++)
                if (star.TryGetValue(StatKeys[i], out int delta)) result.starStats[i] = delta;
        }

        foreach (var flame in NativeGrowthStats.DescribeFlame(entry))
        {
            if (flame.statVal < 0 || flame.statVal >= FlameColumns.Length) continue;
            foreach (int column in FlameColumns[flame.statVal]) result.flameStats[column] += flame.value;
        }
        return result;
    }
}
