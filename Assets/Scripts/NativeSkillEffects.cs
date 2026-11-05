using System.Collections.Generic;
using UnityEngine;

// 282 技能的「属性被动」与「时效增益」。
//
// 两类东西的形状完全不同，过去被混在一起当 buff 处理，于是两头都失效：
//
//   ① [被动效果：…]  —— 学会即永久生效，与持续时间无关。
//      例：400001001 实用的时空门「[被动效果：全属性增加#strX]」、400011066 钢铁之躯
//      「[被动效果：力量增加#strX、最大HP增加#mhpX]」。全库 65 条带这个括号，其中 27 条
//      的字段语义能自证（其余是「给指定技能加伤害」的 damPlus 或单字母，见导出脚本 skipped）。
//      过去没有任何管道结算它们（mhpR/basicStatUp/strX 在 C# 侧零命中），等于白写。
//
//   ② 时效增益 —— 「#time秒内 … #indieDamR%」这类，写在 indie* / ignoreMobpdpR /
//      cr / nbdR 上。它们不是 NativeBuffs 认识的 pad/mad flat 属性，所以 SkillStats
//      返回空、ApplySkill 直接 return false：按得下去、不生效、连图标都没有。
//
// 字段语义不在这里猜：tools/ms282/export_skill_effects.py 从 String.wz 的 h 文案里
// 用「[被动效果：…]」括号把两者切开，并逐条给出字段 → setItemBonus 键的映射。
// 本文件只是执行器，表改了不用动代码。
//
// 为什么全部写进 setItemBonus：EquipLoadout 已经在按 (base+flat)×(100+pct)/100 结算这张表，
// CombatRules 也已经从中读 Damage%/BossDamage%/NormalDamage%/FinalDamage%/IgnoreDefense%。
// 走同一条路，伤害管线不需要为技能增益再开分支。
public static class NativeSkillEffects
{
    static Dictionary<string, object> table;

    static Dictionary<string, object> Table
    {
        get
        {
            if (table != null) return table;
            var text = Resources.Load<TextAsset>("NativeSkillEffects")?.text;
            // 缺表时退化为「没有被动」，不影响既有玩法；守卫脚本会报出来。
            table = string.IsNullOrEmpty(text)
                ? new Dictionary<string, object>()
                : (Dictionary<string, object>)MiniJson.Parse(text);
            return table;
        }
    }

    static Dictionary<string, object> Section(string name)
        => Table.TryGetValue(name, out var value) ? value as Dictionary<string, object> : null;

    /// <summary>字段 → setItemBonus 键数组（导出期生成，含 h 原文依据）。</summary>
    static Dictionary<string, object> Fields => Section("fields");

    /// <summary>技能 id → { setItemBonus 键: 逐级数值数组 }，只收 [被动效果] 括号里的字段。</summary>
    static Dictionary<string, object> Passives => Section("passive");

    /// <summary>技能 id → 被动词段用到的字段名（时效增益要把这些字段排除掉）。</summary>
    static Dictionary<string, object> PassiveFieldMap => Section("passiveFields");

    public static bool Ready => Fields != null;

    /// <summary>该技能被动词段用到的字段名（时效增益要把这些字段排除掉）。</summary>
    static HashSet<string> PassiveFieldNames(int id)
    {
        var result = new HashSet<string>();
        var map = PassiveFieldMap;
        if (map == null || !map.TryGetValue(id.ToString(), out var raw)) return result;
        foreach (var name in (List<object>)raw) result.Add((string)name);
        return result;
    }

    /// <summary>
    /// 把已学技能的永久被动并进差量表（EquipLoadout.Apply / CurrentSetBonus 调用）。
    /// 数值在导出期就按 1..maxLevel 展开了（level/N 优先、缺失回退 common 的公式），
    /// 所以这里只按下标取值，运行时不需要公式求值器。
    /// 只遍历表里的 27 条，不扫全部已学技能 —— 每次攻击结算都会走到这里。
    /// </summary>
    public static void AddPassive(Dictionary<string, int> set)
    {
        var map = Passives;
        if (map == null || set == null) return;
        foreach (var entry in map)
        {
            if (!int.TryParse(entry.Key, out int id)) continue;
            int level = SkillProgress.LevelOf(id);
            if (level <= 0) continue;                       // 没学就不参与
            foreach (var binding in (Dictionary<string, object>)entry.Value)
            {
                if (!(binding.Value is List<object> values) || values.Count == 0) continue;
                int index = Mathf.Clamp(level - 1, 0, values.Count - 1);
                int value = System.Convert.ToInt32(values[index]);
                if (value == 0) continue;
                string key = binding.Key;
                set[key] = (set.TryGetValue(key, out int had) ? had : 0) + value;
            }
        }
    }

    /// <summary>技能 id → { 字段名: [setItemBonus 键…] }。单字母字段只能逐条登记，见导出脚本。</summary>
    static Dictionary<string, object> Aliases => Section("alias");

    /// <summary>
    /// 把一条技能的时效增益并进 buff 的 Stats（NativeBuffs.SkillStats 调用）。
    /// 先查该技能的逐条登记（alias），再查全局字段表；单字母 x/y/z/q/u/v/w 一律不进全局表
    /// —— 同名字段跨技能含义不同（400001002 的 x 是爆击几率、400001003 的 x 是最大HP%），
    /// 猜错比不做更糟。
    /// </summary>
    public static void AddBuff(Dictionary<string, int> stats, int id, Dictionary<string, object> row)
    {
        if (row == null || stats == null) return;
        var passive = PassiveFieldNames(id);

        var aliases = Aliases;
        if (aliases != null && aliases.TryGetValue(id.ToString(), out var aliasRaw))
            foreach (var entry in (Dictionary<string, object>)aliasRaw)
            {
                if (passive.Contains(entry.Key)) continue;
                Add(stats, Art.Int(row, entry.Key), entry.Value);
            }

        var fields = Fields;
        if (fields == null) return;
        foreach (var entry in fields)
        {
            string field = entry.Key;
            if (passive.Contains(field)) continue;          // 这是永久被动，不是时效增益
            Add(stats, Art.Int(row, field), entry.Value);
        }
    }

    static void Add(Dictionary<string, int> stats, int value, object keys)
    {
        if (value == 0) return;
        foreach (var key in (List<object>)keys)
        {
            string name = (string)key;
            stats[name] = (stats.TryGetValue(name, out int had) ? had : 0) + value;
        }
    }
}
