using System.Collections.Generic;
using UnityEngine;

// ——— 282 的「技能间伤害强化」———
//
// 五六转技能里有一批 h 的**方括号被动段**写的是「学了我，另一个技能的伤害增加 #X%p」：
//     1141002 愤怒爆发VI  h「[被动效果：狂暴攻击VI的伤害增加#damPlus%p]」
//     5141015 勾拳爆破VI  h「[被动效果：闪‧连杀VI伤害增加#u%p、超級閃‧連殺伤害增加
//                            #damPlus%p、超級閃‧連殺衝擊波伤害增加#v%p]」
// 这些字段既不是属性 stat（进不了 setItemBonus 的四维/倍率结算），也不产生伤害体
// （不是 proc），所以过去没有任何消费方 —— 12 条五六转技能的被动强化等于没做，
// 表现为「点了被动，另一个技能伤害一点没变」。
//
// 表由 tools/ms282/export_skill_boosts.py 生成：pct 逐级从技能表读，目标 id 有 h 原文
// + psdSkill 双重自证。这里只负责「来源技能学没学、学到几级」，不写任何常量。
//
// 口径：(100+Σpct)/100 直接乘在目标技能的 damage 上 —— 282 的 damPlus 是百分点，
// 与「XX 技能伤害增加 N%p」同义，不是独立乘区。多个来源强化同一个目标时相加。
public static class NativeSkillBoosts
{
    sealed class Entry
    {
        public int Source;
        public int[] Pct;
    }

    static Dictionary<int, List<Entry>> byTarget;
    static bool tried;

    static void Ensure()
    {
        if (tried) return;
        tried = true;
        byTarget = new Dictionary<int, List<Entry>>();
        var asset = Resources.Load<TextAsset>("NativeSkillBoosts");
        if (asset == null) return;
        if (!(MiniJson.Parse(asset.text) is Dictionary<string, object> root)) return;
        if (!(root.TryGetValue("boosts", out var raw) && raw is Dictionary<string, object> table)) return;
        foreach (var pair in table)
        {
            // pair.Value = [ {target, field, pct:[…], …}, … ]
            if (!(pair.Value is List<object> rows)) continue;
            foreach (var item in rows)
            {
                if (!(item is Dictionary<string, object> row)) continue;
                int target = (int)Art.Num(row, "target");
                if (target <= 0) continue;
                if (!(row.TryGetValue("pct", out var pctRaw) && pctRaw is List<object> pctList)) continue;
                var values = new int[pctList.Count];
                // MiniJson 的数字一律是 double；pct 都是整数百分点，直接取整。
                for (int i = 0; i < pctList.Count; i++) values[i] = System.Convert.ToInt32(pctList[i]);
                if (values.Length == 0) continue;
                if (!byTarget.TryGetValue(target, out var list))
                    byTarget[target] = list = new List<Entry>();
                list.Add(new Entry { Source = int.Parse(pair.Key), Pct = values });
            }
        }
    }

    /// <summary>目标技能当前的伤害强化百分点（Σ 已学来源在该等级下的 pct）。</summary>
    public static int Pct(int targetId)
    {
        Ensure();
        if (byTarget == null || !byTarget.TryGetValue(targetId, out var list)) return 0;
        int total = 0;
        foreach (var entry in list)
        {
            // 来源没学（核心没解锁 / 没加点）就整条不计；等级越过表长时钳到末项。
            int level = SkillProgress.LevelOf(entry.Source);
            if (level <= 0) continue;
            total += entry.Pct[Mathf.Clamp(level - 1, 0, entry.Pct.Length - 1)];
        }
        return total;
    }
}
