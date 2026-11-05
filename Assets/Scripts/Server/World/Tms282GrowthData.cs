using System;
using System.Collections.Generic;

// TMS 282 角色成长系统的权威静态数据。
//
// 数据来源（逐项核对过，迁移时不要改数）：
//   徽章      TMS282.5 .../client/character/items/EquipSymbol.java（等级/经验/inc/费用）
//             String.wz/Eqp.img → Eqp/ArcaneForce|AuthenticForce/<id>/name（道具名）
//   极限属性  data/wz282/Skill.wz/Skill/800004.img.xml（17 条技能、maxLevel、逐级数值）
//             点数与费用 constants/SkillConstants.java:2734-2760
//   内在能力  data/wz282/Skill.wz/Skill/7000.img.xml（70000000-70000062 的 common 公式）
//             String.wz/Skill.img 的 name/h（展示文案）
//   星火      data/protocol/tms282.3/flame-rules.tsv + data/resources/tms282/enhance/flame-items.tsv
//             Equip.applyFlameLines / getATTBonus / FlameStat（词条枚举与 exGrade）
//   灵魂      ItemConstants.getSoulOptionFromSoul（option 代码与加值写在源码注释里）
public enum SymbolKind { Arcane = 0, Authentic = 1, Grand = 2 }

public sealed class SymbolInfo
{
    public int itemId;
    public string name;
    public SymbolKind kind;
    public int maxLevel;
    // 新获得的徽章：等级 1、经验 0、inc = 30（ARC）/ 10（AUT、Grand）。
    // 282 Char.java:3060 与 ItemHandler 的两处 setInc(30|10) 同口径。
    public int initialInc;
    // 每次升级给主属性：ARC +100、AUT/Grand +200（ItemHandler 2674 / 2775）。
    public int mainStatPerLevel;
    // 恶魔复仇者每次升级 +HP、Xenon 每次升级 +三围（ItemHandler 2667-2672 / 2768-2773）。
    public int demonAvengerHpPerLevel, xenonPerLevel;

    public bool IsArcane => kind == SymbolKind.Arcane;
    // 282 EquipSymbol.getGrowthCost：ARC lv²+11，AUT/Grand 9lv²+20lv。
    public int GrowthCost(int level) => IsArcane ? level * level + 11 : 9 * level * level + 20 * level;
    // 282 EquipSymbol.setInc 的两处上限：ARC min(220, 10lv+20)，AUT/Grand min(110, 10lv)。
    public int IncAt(int level) => IsArcane ? Math.Min(220, 10 * level + 20) : Math.Min(110, 10 * level);
    // 282 EquipSymbol.canLevelUp：level ∈ [1, max)，且 exp ≥ 本级的 growthCost。
    public bool CanLevelUp(int level, int exp) => level >= 1 && level < maxLevel && exp >= GrowthCost(level);
}

public static class SymbolCatalog
{
    // 槽位沿用本项目「负数=身上」的口径，给三类符文字段独立的槽段，避免和普通装备位冲突：
    //   ARC 1712000..06 → -201..-207，AUT 1713000..05 → -211..-216，Grand 1714000..01 → -221..-222。
    public const int ArcSlotBase = -201;
    public const int AutSlotBase = -211;
    public const int GrandSlotBase = -221;

    static readonly SymbolInfo[] all =
    {
        new SymbolInfo { itemId = 1712000, name = "祕法符文", kind = SymbolKind.Arcane, maxLevel = 20, initialInc = 30, mainStatPerLevel = 100, demonAvengerHpPerLevel = 2100, xenonPerLevel = 48 },
        new SymbolInfo { itemId = 1712001, name = "祕法符文：消逝的旅途", kind = SymbolKind.Arcane, maxLevel = 20, initialInc = 30, mainStatPerLevel = 100, demonAvengerHpPerLevel = 2100, xenonPerLevel = 48 },
        new SymbolInfo { itemId = 1712002, name = "祕法符文：啾啾艾爾蘭", kind = SymbolKind.Arcane, maxLevel = 20, initialInc = 30, mainStatPerLevel = 100, demonAvengerHpPerLevel = 2100, xenonPerLevel = 48 },
        new SymbolInfo { itemId = 1712003, name = "祕法符文：拉契爾恩", kind = SymbolKind.Arcane, maxLevel = 20, initialInc = 30, mainStatPerLevel = 100, demonAvengerHpPerLevel = 2100, xenonPerLevel = 48 },
        new SymbolInfo { itemId = 1712004, name = "祕法符文：阿爾卡娜", kind = SymbolKind.Arcane, maxLevel = 20, initialInc = 30, mainStatPerLevel = 100, demonAvengerHpPerLevel = 2100, xenonPerLevel = 48 },
        new SymbolInfo { itemId = 1712005, name = "祕法符文：魔菈斯", kind = SymbolKind.Arcane, maxLevel = 20, initialInc = 30, mainStatPerLevel = 100, demonAvengerHpPerLevel = 2100, xenonPerLevel = 48 },
        new SymbolInfo { itemId = 1712006, name = "祕法符文：艾斯佩拉", kind = SymbolKind.Arcane, maxLevel = 20, initialInc = 30, mainStatPerLevel = 100, demonAvengerHpPerLevel = 2100, xenonPerLevel = 48 },
        new SymbolInfo { itemId = 1713000, name = "真實符文：賽爾尼溫", kind = SymbolKind.Authentic, maxLevel = 11, initialInc = 10, mainStatPerLevel = 200, demonAvengerHpPerLevel = 4200, xenonPerLevel = 96 },
        new SymbolInfo { itemId = 1713001, name = "真實符文：阿爾克斯", kind = SymbolKind.Authentic, maxLevel = 11, initialInc = 10, mainStatPerLevel = 200, demonAvengerHpPerLevel = 4200, xenonPerLevel = 96 },
        new SymbolInfo { itemId = 1713002, name = "真實符文：奧迪溫", kind = SymbolKind.Authentic, maxLevel = 11, initialInc = 10, mainStatPerLevel = 200, demonAvengerHpPerLevel = 4200, xenonPerLevel = 96 },
        new SymbolInfo { itemId = 1713003, name = "真實符文：桃源境", kind = SymbolKind.Authentic, maxLevel = 11, initialInc = 10, mainStatPerLevel = 200, demonAvengerHpPerLevel = 4200, xenonPerLevel = 96 },
        new SymbolInfo { itemId = 1713004, name = "真實符文：阿爾特利亞", kind = SymbolKind.Authentic, maxLevel = 11, initialInc = 10, mainStatPerLevel = 200, demonAvengerHpPerLevel = 4200, xenonPerLevel = 96 },
        new SymbolInfo { itemId = 1713005, name = "真實符文：卡爾西溫", kind = SymbolKind.Authentic, maxLevel = 11, initialInc = 10, mainStatPerLevel = 200, demonAvengerHpPerLevel = 4200, xenonPerLevel = 96 },
        new SymbolInfo { itemId = 1714000, name = "豪華真實符文：塔拉哈特", kind = SymbolKind.Grand, maxLevel = 11, initialInc = 10, mainStatPerLevel = 200, demonAvengerHpPerLevel = 4200, xenonPerLevel = 96 },
        new SymbolInfo { itemId = 1714001, name = "豪華真實符文：基爾德拉", kind = SymbolKind.Grand, maxLevel = 11, initialInc = 10, mainStatPerLevel = 200, demonAvengerHpPerLevel = 4200, xenonPerLevel = 96 },
    };

    static readonly Dictionary<int, SymbolInfo> byId = Build();
    static Dictionary<int, SymbolInfo> Build()
    {
        var map = new Dictionary<int, SymbolInfo>();
        foreach (var info in all) map[info.itemId] = info;
        return map;
    }

    public static IReadOnlyList<SymbolInfo> All => all;
    public static SymbolInfo Of(int itemId) => byId.TryGetValue(itemId, out var info) ? info : null;
    public static bool IsSymbol(int itemId) => itemId / 1000 == 1712 || itemId / 1000 == 1713 || itemId / 1000 == 1714;
    // 282 ItemConstants.isArcaneSymbol / isSacredSymbol：1712000-06 是祕法，1713/1714 走圣徽分支。
    public static bool IsArcaneSymbol(int itemId) => itemId / 1000 == 1712;
    public static bool IsSacredSymbol(int itemId) => itemId / 1000 == 1713 || itemId / 1000 == 1714;
    // 1712000 是「通用祕法符文」，282 的合并与升级两个 handler 都显式把它挡掉。
    public static bool IsMergeable(int itemId) => IsSymbol(itemId) && itemId != 1712000;
    public static bool IsMergeMaterial(int level, int exp) => level == 1 && (exp == 0 || exp == 1);

    public static int SlotOf(int itemId)
    {
        var info = Of(itemId);
        if (info == null) return 0;
        switch (info.kind)
        {
            case SymbolKind.Arcane: return ArcSlotBase - (itemId - 1712000);
            case SymbolKind.Authentic: return AutSlotBase - (itemId - 1713000);
            default: return GrandSlotBase - (itemId - 1714000);
        }
    }

    // 用户批准采用 282 套件 grand-authentic.tsv（公告/Wiki表），不是客户端等级公式。
    public static int GrandBonus(int itemId, int level, string stat)
    {
        var info = Of(itemId);
        if (info == null || info.kind != SymbolKind.Grand || level < 1 || level > info.maxLevel) return 0;
        switch (stat)
        {
            case "ExpRate": return 10 + (level - 1) * 4;
            case "MesoRate": case "DropRate": return 5 + level - 1;
            default: return 0;
        }
    }

    public static bool IsSymbolSlot(int slot)
        => slot <= ArcSlotBase && slot > ArcSlotBase - 16 || slot <= AutSlotBase && slot > AutSlotBase - 16 || slot <= GrandSlotBase && slot > GrandSlotBase - 16;

    // 282 客户端属性表（EquipSymbol.tms282MesoCost）。服务端 encode() 用的是 265 老公式，
    // 只在 1712001/1713000 与之相等；这里统一按 282 表算，显示与扣费同源。
    public static long MesoCost(int itemId, int level)
    {
        long baseValue, perLevel;
        if (itemId >= 1712001 && itemId <= 1712006)
        {
            baseValue = 80000L + (itemId - 1712001) * 20000L;
            perLevel = 1000L;
        }
        else if (itemId >= 1713000 && itemId <= 1713005)
        {
            baseValue = 1320000L + (itemId - 1713000) * 180000L;
            perLevel = -60000L;
        }
        else if (itemId == 1714000 || itemId == 1714001)
        {
            baseValue = itemId == 1714000 ? 3980000L : 4880000L;
            perLevel = -60000L;
        }
        else return 0;
        bool arcane = itemId / 1000 == 1712;
        long growth = arcane ? (long)level * level + 11 : 9L * level * level + 20L * level;
        long unit = arcane ? 10000L : 100000L;
        long cost = growth * (baseValue + level * perLevel);
        return Math.Max(0, cost / unit * unit);
    }
}

// ———————————————————————————————————————————————————————————————
// 极限属性：17 条可加点技能（80000400-80000422 去掉 407/408/411/415/417/418）。
// Value = PerLevel * slv，除非该技能带 Levels 表（非线性的那 6 条）。
// ———————————————————————————————————————————————————————————————
public sealed class HyperStatInfo
{
    public int skillId;
    public string name;
    public int maxLevel;
    public string key;          // 落在 CharacterAttributes / setItemBonus 上的键
    public int perLevel;
    public int[] levels;        // 非空时按等级查表
    public bool percent;        // 百分号类（伤害/BOSS/无视/爆伤…）
    public bool attack;         // PAD/MAD：直接加到 watk/matk
    public int Value(int level)
    {
        if (level <= 0) return 0;
        if (levels != null && levels.Length > 0) return levels[Math.Min(level, levels.Length) - 1];
        return perLevel * level;
    }
}

public static class HyperStatCatalog
{
    public const int UnlockLevel = 140;
    public const int Presets = 3;
    public const long ResetCost = 10000000;
    public const long SwitchCost = 2000000;
    // SkillConstants.getNeededSpForHyperStatSkill：1,2,4,8,10,15,20,25,30,35,50,65,80,95,110
    public static readonly int[] PointCost = { 1, 2, 4, 8, 10, 15, 20, 25, 30, 35, 50, 65, 80, 95, 110 };

    static readonly int[] CritChance = { 1, 2, 3, 4, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25 };
    static readonly int[] Boss = { 3, 6, 9, 12, 15, 19, 23, 27, 31, 35, 39, 43, 47, 51, 55 };
    static readonly int[] Resist = { 1, 2, 3, 4, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25 };
    static readonly int[] Exp = { 50, 100, 150, 200, 250, 300, 350, 400, 450, 500, 600, 700, 800, 900, 1000 };
    static readonly int[] Arc = { 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100 };
    static readonly int[] Normal = { 3, 6, 9, 12, 15, 19, 23, 27, 31, 35, 39, 43, 47, 51, 55 };

    static readonly HyperStatInfo[] all =
    {
        new HyperStatInfo { skillId = 80000400, name = "STR", maxLevel = 15, key = "STR", perLevel = 30 },
        new HyperStatInfo { skillId = 80000401, name = "DEX", maxLevel = 15, key = "DEX", perLevel = 30 },
        new HyperStatInfo { skillId = 80000402, name = "INT", maxLevel = 15, key = "INT", perLevel = 30 },
        new HyperStatInfo { skillId = 80000403, name = "LUK", maxLevel = 15, key = "LUK", perLevel = 30 },
        new HyperStatInfo { skillId = 80000404, name = "最大HP", maxLevel = 15, key = "HPR", perLevel = 2, percent = true },
        new HyperStatInfo { skillId = 80000405, name = "最大MP", maxLevel = 15, key = "MPR", perLevel = 2, percent = true },
        // 80000406 = 惡魔力量/時間之力（DF/TF）；本项目没有 DF 资源，保留条目但不结算。
        new HyperStatInfo { skillId = 80000406, name = "惡魔力量/時間之力", maxLevel = 10, key = "", perLevel = 10 },
        new HyperStatInfo { skillId = 80000409, name = "爆擊機率", maxLevel = 15, key = "CriticalChance", levels = CritChance },
        new HyperStatInfo { skillId = 80000410, name = "爆擊傷害", maxLevel = 15, key = "CriticalDamage", perLevel = 1, percent = true },
        new HyperStatInfo { skillId = 80000412, name = "無視防禦率", maxLevel = 15, key = "IgnoreDefense", perLevel = 3, percent = true },
        new HyperStatInfo { skillId = 80000413, name = "傷害", maxLevel = 15, key = "Damage", perLevel = 3, percent = true },
        new HyperStatInfo { skillId = 80000414, name = "Boss傷害", maxLevel = 15, key = "BossDamage", levels = Boss, percent = true },
        new HyperStatInfo { skillId = 80000416, name = "異常狀態耐性", maxLevel = 15, key = "StatusResist", levels = Resist, percent = true },
        new HyperStatInfo { skillId = 80000419, name = "攻擊力／魔力", maxLevel = 15, key = "PAD", perLevel = 3, attack = true },
        new HyperStatInfo { skillId = 80000420, name = "獲得經驗值", maxLevel = 15, key = "ExpRate", levels = Exp, percent = true },
        new HyperStatInfo { skillId = 80000421, name = "神祕力量", maxLevel = 15, key = "Arcane", levels = Arc },
        new HyperStatInfo { skillId = 80000422, name = "一般傷害", maxLevel = 15, key = "NormalDamage", levels = Normal, percent = true },
    };

    static readonly Dictionary<int, HyperStatInfo> byId = Build();
    static Dictionary<int, HyperStatInfo> Build()
    {
        var map = new Dictionary<int, HyperStatInfo>();
        foreach (var info in all) map[info.skillId] = info;
        return map;
    }

    public static IReadOnlyList<HyperStatInfo> All => all;
    public static HyperStatInfo Of(int skillId) => byId.TryGetValue(skillId, out var info) ? info : null;

    // SkillConstants.getHyperStatSpByLv：3 + (level-140)/10，逐级累加。
    public static int SpForLevel(int level) => level < UnlockLevel ? 0 : 3 + (level - UnlockLevel) / 10;
    public static int TotalSp(int level)
    {
        int sp = 0;
        for (int lv = UnlockLevel; lv <= level; lv++) sp += SpForLevel(lv);
        return sp;
    }
    public static int NeededSp(int level) => level >= 1 && level <= PointCost.Length ? PointCost[level - 1] : 0;
}

// ———————————————————————————————————————————————————————————————
// 内在能力：63 条池。公式串来自 7000.img 的 common（x = 本条等级）。
// 支持 u(a) 向上取整、d(a) 向下取整、四则运算；解析在 GrowthFormula。
// ———————————————————————————————————————————————————————————————
public sealed class AbilityInfo
{
    public int skillId;
    public string name;
    public int maxLevel;
    public string[] keys;       // 与 formulas 一一对应
    public string[] formulas;
}

public static class AbilityCatalog
{
    public const int BaseId = 70000000;
    public const int MaxId = 70000062;
    public const int Presets = 3;
    public const int Lines = 3;
    // 行解锁等级（任务 12394/12395/12396 的等价物）
    public const int Line2Level = 30;
    public const int Line3Level = 50;
    // 荣誉值洗练费用（UserHandler.handleUserRequestCharacterPotentialSkillRandSetUi）：
    // 按当前 grade 取（rare 100 / epic 200 / unique 1500 / legendary 8000），再叠加锁行费用。
    // 注意别写成「{0,100,200,1500,8000} 按 grade 索引」——那样 rare 会变成免费、epic 变成 100。
    public static int ResetCostOf(int grade) => grade >= 3 ? 8000 : grade == 2 ? 1500 : grade == 1 ? 200 : 100;
    public const int LockFirstCost = 3000;
    public const int LockSecondCost = 5000;
    // 一次洗练要花多少名聲值 = 按档位的基础费用 + 每锁一行加价。
    // Preview 和真正扣费都走这里，窗口显示的数和实际扣的数不可能对不上。
    public static int CostOf(int grade, int lockedCount)
    {
        int cost = ResetCostOf(grade);
        if (lockedCount >= 1) cost += LockFirstCost;
        if (lockedCount >= 2) cost += LockSecondCost;
        return cost;
    }
    // grade 升降概率（GameConstants.BASE_CHAR_*_POT_*_RATE）：稀有 5/0、史诗 3/3、唯一 2/15、传说 0/50。
    public static int GradeUpRate(int grade) => grade >= 3 ? 0 : grade == 2 ? 2 : grade == 1 ? 3 : 5;
    public static int GradeDownRate(int grade) => grade >= 3 ? 50 : grade == 2 ? 15 : grade == 1 ? 3 : 0;

    static AbilityInfo Make(int id, string name, int max, params string[] pairs)
    {
        var info = new AbilityInfo { skillId = id, name = name, maxLevel = max, keys = new string[pairs.Length / 2], formulas = new string[pairs.Length / 2] };
        for (int i = 0; i < info.keys.Length; i++) { info.keys[i] = pairs[i * 2]; info.formulas[i] = pairs[i * 2 + 1]; }
        return info;
    }

    static readonly AbilityInfo[] all =
    {
        Make(70000000, "力量增加", 40, "STR", "x"),
        Make(70000001, "敏捷增加", 40, "DEX", "x"),
        Make(70000002, "智力增加", 40, "INT", "x"),
        Make(70000003, "幸運增加", 40, "LUK", "x"),
        Make(70000004, "跳躍力增加", 30, "Jump", "2*u(x/3)"),
        Make(70000005, "移動速度增加", 30, "Speed", "2*u(x/3)"),
        Make(70000006, "防禦力增加", 40, "PDD", "10*x"),
        Make(70000007, "防禦力增加", 40, "PDD", "10*x"),
        Make(70000008, "最大HP增加", 40, "MHP", "x*15"),
        Make(70000009, "最大MP增加", 40, "MMP", "x*15"),
        Make(70000010, "跳躍力增加", 30, "Jump", "2*u(x/3)"),
        Make(70000011, "移動速度增加", 30, "Speed", "2*u(x/3)"),
        Make(70000012, "攻擊力增加", 30, "PAD", "3*u(x/3)"),
        Make(70000013, "魔力增加", 30, "MAD", "3*u(x/3)"),
        Make(70000014, "爆擊機率增加", 30, "CriticalChance", "x"),
        Make(70000015, "所有能力值增加", 40, "STR", "x", "DEX", "x", "INT", "x", "LUK", "x"),
        Make(70000016, "攻擊速度增加", 10, "AttackSpeed", "-1"),
        Make(70000017, "STR增加", 40, "STR", "x"),
        Make(70000018, "DEX增加", 40, "DEX", "x"),
        Make(70000019, "INT增加", 40, "INT", "x"),
        Make(70000020, "LUK增加", 40, "LUK", "x"),
        // 21-24 是四维互转（str2dex…），需要「转换量 = 其它来源的 1/4」，本项目没有独立来源分层，暂不结算。
        Make(70000021, "力量轉換為敏捷", 40, "STR2DEX", "u(x/4)"),
        Make(70000022, "敏捷轉換為力量", 40, "DEX2STR", "u(x/4)"),
        Make(70000023, "智力轉換為幸運", 40, "INT2LUK", "u(x/4)"),
        Make(70000024, "幸運轉換為敏捷", 40, "LUK2DEX", "u(x/4)"),
        // 25/26/40/42 是「每一定等级」的 pad/mad，依赖角色等级，暂不结算。
        Make(70000025, "每一定等級攻擊力增加", 10, "LV2PAD", "20-2*d(x/2)"),
        Make(70000026, "每一定等級魔力增加", 10, "LV2MAD", "20-2*d(x/2)"),
        Make(70000027, "最大HP固定比率增加", 20, "MHPR", "x"),
        Make(70000028, "最大MP固定比率增加", 20, "MMPR", "x"),
        Make(70000029, "防禦力固定比率增加", 20, "PDDR", "x"),
        Make(70000030, "防禦力固定比率增加", 20, "PDDR", "x"),
        Make(70000031, "最大HP增加一定比率", 20, "MHPR", "x"),
        Make(70000032, "最大MP增加一定比率", 20, "MMPR", "x"),
        Make(70000033, "楓幣獲得量增加", 40, "MesoRate", "u(x/2)"),
        Make(70000034, "增加迴避率", 20, "EVA", "u(x/2)"),
        Make(70000035, "攻擊Boss怪物時增加傷害", 20, "BossDamage", "x"),
        Make(70000036, "攻擊一般怪物時增加傷害", 40, "NormalDamage", "u(x/4)"),
        Make(70000037, "攻擊塔時增加傷害", 30, "TowerDamage", "2*u(x/3)"),
        Make(70000038, "補給模式秒殺", 40, "MINION", "u(x/4)"),
        Make(70000039, "狀態異常追加傷害", 40, "STATUSDMG", "u(x/4)"),
        Make(70000040, "每一定等級攻擊力增加", 10, "LV2PAD", "20-2*d(x/2)"),
        Make(70000041, "防禦力轉換為傷害", 20, "PDD2DAM", "x*2+u(x/2)"),
        Make(70000042, "每一定等級魔力增加", 10, "LV2MAD", "20-2*d(x/2)"),
        Make(70000043, "爆擊機率增加", 30, "CriticalChance", "x"),
        Make(70000044, "所有能力值增加", 40, "STR", "x", "DEX", "x", "INT", "x", "LUK", "x"),
        Make(70000045, "一定機率不套用冷卻", 20, "NOCOOL", "x"),
        Make(70000046, "被動技能等級增加", 10, "PASSIVE", "1"),
        Make(70000047, "增加攻擊技能目標", 10, "TARGET", "1"),
        Make(70000048, "增加加持持續時間", 40, "BuffTime", "x+u(x/4)"),
        Make(70000049, "增加道具掉落率", 40, "DropRate", "u(x/2)"),
        Make(70000050, "增加楓幣獲得量", 40, "MesoRate", "u(x/2)"),
        Make(70000051, "增加力量、敏捷", 40, "STR", "x", "DEX", "u(x/2)"),
        Make(70000052, "增加力量、智力", 40, "STR", "x", "INT", "u(x/2)"),
        Make(70000053, "增加力量、幸運", 40, "STR", "x", "LUK", "u(x/2)"),
        Make(70000054, "增加敏捷、智力", 40, "DEX", "x", "INT", "u(x/2)"),
        Make(70000055, "增加敏捷、幸運", 40, "DEX", "x", "LUK", "u(x/2)"),
        Make(70000056, "增加智力、幸運", 40, "INT", "x", "LUK", "u(x/2)"),
        Make(70000057, "增加敏捷、力量", 40, "DEX", "x", "STR", "u(x/2)"),
        Make(70000058, "增加智力、力量", 40, "INT", "x", "STR", "u(x/2)"),
        Make(70000059, "增加幸運、力量", 40, "LUK", "x", "STR", "u(x/2)"),
        Make(70000060, "增加智力、敏捷", 40, "INT", "x", "DEX", "u(x/2)"),
        Make(70000061, "增加幸運、敏捷", 40, "LUK", "x", "DEX", "u(x/2)"),
        Make(70000062, "增加幸運、智力", 40, "LUK", "x", "INT", "u(x/2)"),
    };

    static readonly Dictionary<int, AbilityInfo> byId = Build();
    static Dictionary<int, AbilityInfo> Build()
    {
        var map = new Dictionary<int, AbilityInfo>();
        foreach (var info in all) map[info.skillId] = info;
        return map;
    }

    public static IReadOnlyList<AbilityInfo> All => all;
    public static AbilityInfo Of(int skillId) => byId.TryGetValue(skillId, out var info) ? info : null;

    // 等级段：base = grade*10，slv ∈ [base+1, base+10]，再夹到该条目的 maxLevel。
    // grade 0 稀有 / 1 英雄(rare->epic 段) / 2 唯一 / 3 传说。
    public static int MinLevelForGrade(int grade) => grade * 10 + 1;
    public static int MaxLevelForGrade(int grade) => (grade + 1) * 10;
    // 顺序很关键：先按 grade 段夹，再被 maxLevel 封顶。
    // 反过来的话「每一定等級攻擊力增加」(maxLevel=10) 在传说段会算出 31 级——
    // 那是段下限，不是这条技能能到的等级。282 里这类技能无论摇到哪档，实际值都是它的满级。
    public static int ClampLevel(AbilityInfo info, int grade, int level)
        => Math.Min(info.maxLevel, Math.Max(MinLevelForGrade(grade), Math.Min(MaxLevelForGrade(grade), level)));
    // grade 由三行里最高等级的段推导（0..3）
    public static int GradeOfLevel(int level) => Math.Max(0, Math.Min(3, (level - 1) / 10));
}

// ———————————————————————————————————————————————————————————————
// 星火规则（282）：
//   createType 6/7/12/20/21/0/8 → 行数/阶数权重
//   道具覆盖：ct20/ct21 的阶数表由 flame-items.tsv 覆盖
//   黑星火/深渊星火走前后二选一确认窗
//   词条 0..19 可摇（LevelReduction=20 摇不出），Boss 装备固定 4 行且阶数 +2
// ———————————————————————————————————————————————————————————————
public sealed class FlameRule
{
    public int createType;
    public string kind;             // POWERFUL / ETERNAL / ABYSSAL
    public string lineWeights;
    public string tierWeights;
}

public static class FlameCatalog
{
    public const int BossLines = 4;
    public const int BossTierShift = 2;
    public const int Divider = 40;
    public const int DividerExtended = 20;
    public static readonly double[] WeaponMultiplier = { 1.0, 2.2, 3.65, 5.35, 7.3, 8.8, 10.25 };
    public static readonly double[] BossWeaponMultiplier = { 1.0, 1.0, 3.0, 4.4, 6.05, 8.0, 10.25 };

    // ItemConstants.canEquipHaveFlame 的装备类别（按 itemId/10000 段）
    static readonly int[] FlamePrefixes =
    {
        // 吊坠 112 / 腰带 113 / 帽 100 / 脸饰 101 / 眼饰 102 / 套服 105 / 上衣 104 / 下装 106
        112, 113, 100, 101, 102, 105, 104, 106,
        // 鞋 107 / 耳环 103 / 手套 108 / 披风 110 / 口袋 116
        107, 103, 108, 110, 116,
        // 武器 121..160（副武器 135 与盾 109 不算）
        121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 136, 137, 138, 139, 140,
        141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160,
    };

    static readonly Dictionary<int, FlameRule> rules = new Dictionary<int, FlameRule>
    {
        { 6,  new FlameRule { createType = 6,  kind = "POWERFUL", lineWeights = "1:40,2:40,3:15,4:5", tierWeights = "1:20,2:30,3:36,4:14" } },
        { 7,  new FlameRule { createType = 7,  kind = "ETERNAL",  lineWeights = "1:40,2:40,3:15,4:5", tierWeights = "2:29,3:45,4:25" } },
        { 12, new FlameRule { createType = 12, kind = "ABYSSAL",  lineWeights = "1:40,2:40,3:15,4:5", tierWeights = "3:1,4:1,5:1" } },
        { 20, new FlameRule { createType = 20, kind = "POWERFUL", lineWeights = "1:40,2:40,3:15,4:5", tierWeights = "3:300,4:550,5:270,6:140,7:50" } },
        { 21, new FlameRule { createType = 21, kind = "POWERFUL", lineWeights = "1:40,2:40,3:15,4:5", tierWeights = "4:550,5:270,6:140,7:50" } },
        { 0,  new FlameRule { createType = 0,  kind = "POWERFUL", lineWeights = "1:40,2:40,3:15,4:5", tierWeights = "1:20,2:30,3:36,4:14" } },
        { 8,  new FlameRule { createType = 8,  kind = "POWERFUL", lineWeights = "1:40,2:40,3:15,4:5", tierWeights = "1:20,2:30,3:36,4:14" } },
    };

    static readonly HashSet<int> Black = new HashSet<int> { 2048753, 2048755, 2048766, 2048767, 2048768, 2048799 };
    static readonly HashSet<int> Abyssal = new HashSet<int> { 2048794, 2048796 };

    // 道具 → createType。来源：282 客户端 Item/Consume/0204.img 的 info/createType（2026-11-04 全表导出）。
    // 2048726/2048738 没有 createType，但有 exNew 1——就是 flame-rules.tsv 里「282 週年武器專用、
    // 无 createType 的 exNew 星火」，按 createType 0 处理。
    static readonly Dictionary<int, int> createTypes = new Dictionary<int, int>
    {
        { 2048716, 6 }, { 2048724, 6 }, { 2048745, 6 }, { 2048751, 6 }, { 2048752, 6 },
        { 2048759, 6 }, { 2048770, 6 }, { 2048780, 6 }, { 2048788, 6 }, { 2048795, 6 },
        { 2048717, 7 }, { 2048721, 7 }, { 2048723, 7 }, { 2048746, 7 }, { 2048747, 7 },
        { 2048753, 7 }, { 2048755, 7 }, { 2048757, 7 }, { 2048758, 7 }, { 2048766, 7 },
        { 2048767, 7 }, { 2048768, 7 }, { 2048769, 7 }, { 2048786, 7 }, { 2048787, 7 },
        { 2048799, 7 },
        { 2048720, 8 },
        { 2048794, 12 }, { 2048796, 12 },
        { 2048761, 20 },
        { 2048760, 21 }, { 2048779, 21 },
        { 2048726, 0 }, { 2048738, 0 },
    };

    public static bool IsFlameItem(int itemId) => createTypes.ContainsKey(itemId);
    public static int CreateTypeOf(int itemId) => createTypes.TryGetValue(itemId, out int type) ? type : -1;

    public static FlameRule Rule(int createType) => rules.TryGetValue(createType, out var rule) ? rule : null;
    public static bool IsChoice(int itemId) => Black.Contains(itemId) || Abyssal.Contains(itemId);
    public static bool IsAbyssal(int itemId) => Abyssal.Contains(itemId);
    public static bool CanFlame(int itemId)
    {
        if (itemId / 1000000 != 1) return false;
        int prefix = itemId / 10000;
        if (prefix == 109 || prefix == 135) return false;          // 盾 / 副武器
        return Array.IndexOf(FlamePrefixes, prefix) >= 0;
    }

    // 每行的实际加值：照 Equip.applyFlameLines 的分支抄，不要自己重排列。
    //   added    = tier * ceil((reqLevel + incReq + 1) / 40)
    //   addedExt = tier * ceil((reqLevel + incReq + 1) / 20)
    // 单属性(0-3)用 addedExt；双属性(4-9)用 added；防御(12)用 addedExt。
    // 攻/魔(10,11)：武器按基础攻击 × 倍率表 × flameLevel（再向上取整），非武器就是 tier。
    // HP/MP(13,14)：(reqLevel+incReq)/10*30*tier。速/跳/全属性/伤害 = tier，BOSS 伤害 = 2*tier，减等级 = 5*tier。
    public static int FlameLevel(int reqLevel, int incReq)
        => (int)Math.Ceiling((reqLevel + incReq + 1.0) / Divider);
    public static int FlameLevelExtended(int reqLevel, int incReq)
        => (int)Math.Ceiling((reqLevel + incReq + 1.0) / DividerExtended);

    public static int Value(int statVal, int tier, int reqLevel, int incReq, bool weapon, bool bossWeapon, int baseAttack)
    {
        int added = tier * FlameLevel(reqLevel, incReq);
        int addedExt = tier * FlameLevelExtended(reqLevel, incReq);
        switch (statVal)
        {
            case 0: case 1: case 2: case 3: return addedExt;
            case 4: case 5: case 6: case 7: case 8: case 9: return added;
            case 10: case 11:
                if (!weapon) return tier;
                var multipliers = bossWeapon ? BossWeaponMultiplier : WeaponMultiplier;
                int index = Math.Min(Math.Max(tier, 1), multipliers.Length) - 1;
                return (int)Math.Ceiling(baseAttack * (multipliers[index] * FlameLevel(reqLevel, incReq)) / 100.0);
            case 12: return addedExt;
            case 13: case 14: return ((reqLevel + incReq) / 10) * 30 * tier;
            case 15: case 16: case 17: case 19: return tier;
            case 18: return tier * 2;
            case 20: return 5 * tier;
            default: return 0;
        }
    }

    // 该词条能不能出现在这件装备上（applyFlameLines 的两条 continue）。
    public static bool Allowed(int statVal, bool weapon, int reqLevel, int incReq)
    {
        if (statVal == 20) return reqLevel + incReq >= 5;   // 等级要求减免
        if (statVal == 18 || statVal == 19) return weapon;   // BOSS 伤害 / 伤害只在武器
        return true;
    }

    // WeightedPick.Spec（"1:40,2:40" → 累积权重）
    public sealed class Weights
    {
        public readonly int[] keys;
        public readonly int[] cumulative;
        public readonly int total;
        public Weights(string spec)
        {
            var keyList = new List<int>();
            var sumList = new List<int>();
            int sum = 0;
            foreach (var part in spec.Split(','))
            {
                var kv = part.Split(':');
                int key = int.Parse(kv[0]), weight = int.Parse(kv[1]);
                if (weight <= 0) continue;
                sum += weight;
                keyList.Add(key);
                sumList.Add(sum);
            }
            keys = keyList.ToArray();
            cumulative = sumList.ToArray();
            total = sum;
        }
        public int Pick(int roll)
        {
            for (int i = 0; i < cumulative.Length; i++) if (roll < cumulative[i]) return keys[i];
            return keys[keys.Length - 1];
        }
    }
}

// 词条枚举（FlameStat 的 val 与 exGrade 两列；roll 只用 0..19）
public sealed class FlameStatDef
{
    public int val;
    public int exGrade;
    public string label;
}

public static class FlameStats
{
    public static readonly FlameStatDef[] All =
    {
        new FlameStatDef { val = 0,  exGrade = 0,  label = "STR" },
        new FlameStatDef { val = 1,  exGrade = 1,  label = "DEX" },
        new FlameStatDef { val = 2,  exGrade = 2,  label = "INT" },
        new FlameStatDef { val = 3,  exGrade = 3,  label = "LUK" },
        new FlameStatDef { val = 4,  exGrade = 4,  label = "STR/DEX" },
        new FlameStatDef { val = 5,  exGrade = 5,  label = "STR/INT" },
        new FlameStatDef { val = 6,  exGrade = 6,  label = "STR/LUK" },
        new FlameStatDef { val = 7,  exGrade = 7,  label = "DEX/INT" },
        new FlameStatDef { val = 8,  exGrade = 8,  label = "DEX/LUK" },
        new FlameStatDef { val = 9,  exGrade = 9,  label = "INT/LUK" },
        new FlameStatDef { val = 10, exGrade = 17, label = "攻擊力" },
        new FlameStatDef { val = 11, exGrade = 18, label = "魔力" },
        new FlameStatDef { val = 12, exGrade = 13, label = "防禦力" },
        new FlameStatDef { val = 13, exGrade = 10, label = "最大HP" },
        new FlameStatDef { val = 14, exGrade = 11, label = "最大MP" },
        new FlameStatDef { val = 15, exGrade = 19, label = "移動速度" },
        new FlameStatDef { val = 16, exGrade = 20, label = "跳躍力" },
        new FlameStatDef { val = 17, exGrade = 24, label = "全屬性%" },
        new FlameStatDef { val = 18, exGrade = 21, label = "BOSS傷害%" },
        new FlameStatDef { val = 19, exGrade = 23, label = "傷害%" },
        new FlameStatDef { val = 20, exGrade = 12, label = "等級要求減免" },
    };
    public static FlameStatDef ByVal(int val) => val >= 0 && val < All.Length ? All[val] : null;
    public static FlameStatDef ByExGrade(int exGrade)
    {
        foreach (var def in All) if (def.exGrade == exGrade) return def;
        return null;
    }
    // 可摇范围：Equip.applyFlameLines 用 Util.getRandom(values().length - 1)，而 getRandom(int inclBound)
    // 的上界是**闭区间**，所以 r ∈ [0, 20]，21 类全可摇。等级要求减免另有门槛：
    // 只有 rLevel+iIncReq ≥ 5 才会被采用（<5 直接跳过该次抽取，不占行数）。
    public const int RollableCount = 21;
}

// 灵魂属性：soulOption 代码 → 加值（数值写在 ItemConstants.getSoulOptionFromSoul 的注释里）
public sealed class SoulOption
{
    public int code;
    public string key;     // STR/DEX/INT/LUK/MHP/MMP/PAD/MAD/AllStats/CriticalChance/BossDamage
    public int value;
}

public static class SoulCatalog
{
    public const int EnchanterBase = 2590000;
    public const int SoulBase = 2591000;
    public const int MinLevel = 75;
    public const int MaxSoulMp = 1000;

    // 282 的 getSoulOptionFromSoul 里「名字没有属性前缀」的灵魂（例如 Balrog's Soul Marble 这类 Boss 灵魂珠）
    // 走 soulPotList 随机，不按档位算。这里保留同样的兜底集合。
    // 必须声明在 table 之前：C# 静态字段按文本顺序初始化，Build() 会读它，摆后面就是 null。
    public static readonly int[] FallbackPotList =
    {
        32001, 32002, 32003, 32004, 32005, 32006, 32011, 32012,
        32041, 32042, 32043, 32044, 32045, 32046, 32051, 32052,
    };
    public static int FallbackOption(int roll) => FallbackPotList[Math.Abs(roll) % FallbackPotList.Length];

    static readonly Dictionary<int, SoulOption> table = Build();
    static Dictionary<int, SoulOption> Build()
    {
        var map = new Dictionary<int, SoulOption>();
        void Add(int code, string key, int value) => map[code] = new SoulOption { code = code, key = key, value = value };
        // +STR/DEX/INT/LUK：tier1 +3、tier2 +5、tier3 +7、tier4 +10、tier5 +12、tier6 +15、tier7 +18、tier8 +20、tier9 +24
        int[] statTiers = { 3, 5, 7, 10, 12, 15, 18, 20, 24 };
        int[] statCodes = { 101, 105, 109, 113, 163, 117, 185, 121, 167 };
        string[] stats = { "STR", "DEX", "INT", "LUK" };
        for (int tier = 0; tier < statTiers.Length; tier++)
            for (int k = 0; k < stats.Length; k++) Add(statCodes[tier] + k, stats[k], statTiers[tier]);
        // +HP/+MP
        Add(131, "MHP", 150); Add(132, "MMP", 150);
        Add(133, "MHP", 200); Add(134, "MMP", 200);
        Add(135, "MHP", 300); Add(136, "MMP", 300);
        Add(137, "MHP", 400); Add(175, "MHP", 500); Add(138, "MHP", 600);
        Add(189, "MHP", 700); Add(139, "MHP", 800); Add(177, "MHP", 960);
        // 全属性
        Add(201, "AllStats", 2); Add(202, "AllStats", 3); Add(203, "AllStats", 5); Add(204, "AllStats", 7);
        Add(210, "AllStats", 8); Add(205, "AllStats", 10); Add(40081, "AllStats", 12); Add(70057, "AllStats", 15);
        // 攻/魔
        Add(2011, "PAD", 3); Add(2012, "MAD", 3);
        Add(153, "PAD", 4); Add(154, "MAD", 4);
        Add(155, "PAD", 5); Add(156, "MAD", 5);
        Add(171, "PAD", 6); Add(172, "MAD", 6);
        // 鋒利/破壞：282 源码只给代码没有数值注释（42292/60009），先保留代码、不结算。
        Add(42292, "", 0);
        Add(60009, "", 0);
        // soulPotList 兜底族（32xxx）：282 对没有属性前缀的 Boss 灵魂珠随机给其中之一。
        // 源码同样只列代码不给数值，保留条目以免镶魂被拒，数值待后续从 ItemOption 表补。
        foreach (int code in FallbackPotList) Add(code, "", 0);
        return map;
    }

    public static SoulOption Of(int code) => table.TryGetValue(code, out var option) ? option : null;
    public static int SoulIdOf(int itemId) => 1 + itemId % SoulBase;

    // 灵魂石 id → 属性代码：由 tools/ms282/export_soul_options.py 离线复刻 282 的名字推导后导出，
    // 运行期只查表，绝不按 id 取模猜（那样会给出完全错误的属性）。
    static Dictionary<int, int> soulOptions;
    static Dictionary<int, int> SoulOptions => soulOptions ??= LoadSoulOptions();
    static Dictionary<int, int> LoadSoulOptions()
    {
        var map = new Dictionary<int, int>();
        string json = PlatformRes.LoadText("Enhancement/SoulOptions");
        if (string.IsNullOrEmpty(json)) return map;
        if (!(MiniJsonDb.Parse(json) is Dictionary<string, object> root)) return map;
        if (!root.TryGetValue("options", out var raw) || !(raw is Dictionary<string, object> options)) return map;
        foreach (var pair in options)
            if (int.TryParse(pair.Key, out int itemId) && pair.Value != null
                && int.TryParse(pair.Value.ToString(), out int code)) map[itemId] = code;
        return map;
    }

    /// <summary>灵魂石对应的属性代码；不在表里返回 0（调用方应当拒绝，或按 FallbackOption 兜底）。</summary>
    public static int OptionFor(int soulItemId) => SoulOptions.TryGetValue(soulItemId, out int code) ? code : 0;
}

// 282 公式串求值：支持 x、整数、+ - * /、u(...) 向上取整、d(...) 向下取整。
// 只用于内在能力/极限属性这些 WZ 里写死的公式，不做通用脚本。
public static class GrowthFormula
{
    public static int Evaluate(string formula, int x)
    {
        int pos = 0;
        double value = ParseExpr(formula, ref pos, x);
        return (int)Math.Floor(value + 1e-9);
    }

    static double ParseExpr(string s, ref int i, int x)
    {
        double value = ParseTerm(s, ref i, x);
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '+') { i++; value += ParseTerm(s, ref i, x); }
            else if (c == '-') { i++; value -= ParseTerm(s, ref i, x); }
            else break;
        }
        return value;
    }

    static double ParseTerm(string s, ref int i, int x)
    {
        double value = ParseFactor(s, ref i, x);
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '*') { i++; value *= ParseFactor(s, ref i, x); }
            else if (c == '/') { i++; value /= ParseFactor(s, ref i, x); }
            else break;
        }
        return value;
    }

    static double ParseFactor(string s, ref int i, int x)
    {
        while (i < s.Length && s[i] == ' ') i++;
        if (i >= s.Length) return 0;
        char c = s[i];
        if (c == '(') { i++; double inner = ParseExpr(s, ref i, x); if (i < s.Length && s[i] == ')') i++; return inner; }
        if (c == '-') { i++; return -ParseFactor(s, ref i, x); }
        if (c == 'x' || c == 'X') { i++; return x; }
        if (c == 'u' || c == 'd' || c == 'U' || c == 'D')
        {
            // u(...) 向上取整，d(...) 向下取整。
            // 语义是从 WZ 数据反推的：70000025「20-2*d(x/2)」的说明是「#lv2pad 等級時增加攻擊力 1」，
            // 只有 d=floor 才能让 x=1 → 20（每 20 级 +1）、x=10 → 10（每 10 级 +1）单调成立。
            // 关键：子表达式必须按 double 求值后再取整，若先转 int 会让 x/2 的小数提前被截断，
            // 两个函数的区别就全丢了（70000036「u(x/4)」在 x=1 时应该是 1% 而不是 0%）。
            bool roundUp = c == 'u' || c == 'U';
            i++;
            while (i < s.Length && s[i] == ' ') i++;
            if (i >= s.Length || s[i] != '(') return 0;
            i++;
            double inner = ParseExpr(s, ref i, x);
            if (i < s.Length && s[i] == ')') i++;
            return roundUp ? Math.Ceiling(inner) : Math.Floor(inner);
        }
        if (char.IsDigit(c))
        {
            int start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            return double.Parse(s.Substring(start, i - start));
        }
        i++;
        return 0;
    }
}
