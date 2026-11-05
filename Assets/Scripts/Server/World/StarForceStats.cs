using System;
using System.Collections.Generic;

// Equip.recalcEnchantmentStats / GameConstants.getEnchantmentValByChuc.
// Returns a derived layer: callers must never write these values into base equipment stats.
public static class StarForceStats
{
    static readonly string[] Keys = { "str", "dex", "int", "luk", "hp", "mp", "watk", "matk", "wdef", "mdef", "acc", "avoid", "speed", "jump" };
    static readonly int[,] Attack = {
        {6,7,8,9,12}, {7,8,9,9,13}, {7,8,9,10,14}, {8,9,10,11,14},
        {9,10,11,12,15}, {11,11,12,13,16}, {12,12,13,14,17},
        {17,17,18,19,21}, {19,19,20,21,23}, {21,21,22,23,25},
        {23,23,24,25,27}, {25,25,26,27,29}, {27,27,28,29,31},
        {29,29,30,31,33}, {31,31,32,33,35}
    };
    static int Base(Dictionary<string, int> stats, string key) => stats.TryGetValue(key, out int n) ? n : 0;
    public static Dictionary<string, int> Calculate(int itemId, int reqLevel, int stars, bool superior,
        Dictionary<string, int> baseStats, bool eligible = true)
    {
        if (baseStats == null || stars < 0 || stars > (superior ? 15 : 30) || reqLevel < 0)
            throw new ArgumentException("无效的星力属性参数");
        var result = new Dictionary<string, int>();
        if (!eligible) return result;
        int category = itemId / 10000;
        bool weapon = (itemId >= 1210000 && itemId < 1600000 || category == 170) && category != 135;
        int bracket = reqLevel <= 137 ? 0 : reqLevel <= 149 ? 1 : reqLevel <= 159 ? 2 : reqLevel <= 199 ? 3 : 4;
        foreach (string key in Keys)
        {
            int value = Base(baseStats, key);
            bool attack = key == "watk" || key == "matk", defense = key == "wdef" || key == "mdef";
            if (value <= 0 && !attack && !defense) continue;
            int total = 0;
            for (int star = 0; star < stars; star++)
            {
                int delta = 0;
                if (defense) delta = (int)(value * (category == 105 ? .10 : .05));
                else if (superior)
                {
                    if (key == "str" || key == "dex" || key == "int" || key == "luk")
                        delta = SuperiorStat(reqLevel, star);
                    else if (attack) delta = SuperiorAttack(reqLevel, star);
                }
                else
                {
                    if (key == "hp" || key == "mp")
                        delta = star <= 2 ? 5 : star <= 4 ? 10 : star <= 6 ? 15 : star <= 8 ? 20 : star <= 14 ? 25 : 0;
                    else if (key == "str" || key == "dex" || key == "int" || key == "luk")
                        delta = star <= 4 ? 2 : star <= 14 ? 3 : star <= 21 ? new[] {7,9,11,13,15}[bracket] : 0;
                    else if (attack)
                    {
                        if (star >= 15) delta += Attack[star - 15, bracket];
                        if (weapon)
                        {
                            // 用户指定：低攻击新手武器每星至少 +1；不增加原本为零的属性。
                            if (star <= 14) delta += value > 0 ? Math.Max(1, (int)(value * .02)) : 0;
                            else
                            {
                                delta += star == 22 ? 13 : star == 23 ? 12 : star == 24 ? 11 : star == 25 ? 10
                                    : star == 27 ? 9 : star == 28 ? 8 : star == 29 ? 7 : 0;
                                if (reqLevel == 200 && star == 15) delta++;
                            }
                        }
                        if (category == 108 && (star <= 10 && star % 2 == 0 || star >= 12 && star <= 14)) delta++;
                    }
                    else if (category == 107 && (key == "speed" || key == "jump") && star <= 4) delta++;
                }
                total = checked(total + delta);
            }
            result[key] = total;
        }
        return result;
    }
    static int SuperiorStat(int level, int star)
    {
        switch (star)
        {
            case 0: return level < 110 ? 2 : level < 149 ? 9 : 19;
            case 1: return level < 110 ? 3 : level < 149 ? 10 : 20;
            case 2: return level < 110 ? 5 : level < 149 ? 12 : 22;
            case 3: return level < 149 ? 15 : 25;
            case 4: return level < 149 ? 19 : 29;
            default: return 0;
        }
    }
    static int SuperiorAttack(int level, int star)
        => star == 5 ? level < 150 ? 5 : 9 : star == 6 ? level < 150 ? 6 : 10 : star == 7 ? level < 150 ? 7 : 11
            : star == 8 ? 12 : star == 9 ? 13 : star == 10 ? 15 : star == 11 ? 17 : star == 12 ? 19 : star == 13 ? 21 : star == 14 ? 23 : 0;
}
