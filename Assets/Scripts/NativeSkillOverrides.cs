using System;
using System.Collections.Generic;
using UnityEngine;

// 282 的「强化核心」：自己不产出任何 stat，而是把**另一个技能**的字段替换掉。
//
// 500004073 h「暗影侍從延伸的最終傷害反映率#x%，暗影瞬移持續時間增加為#time秒」
//   · x    76..105   → 覆盖 400041028 暗影侍從 的 x 46..75（暗影飞镖的追加打击倍率）
//   · time 46..55 秒 → 覆盖 400041095 暗影瞬移 的 time 30..45（影子维持时长）
// 两条都是替换、不是相加；数值与 h 原文在 tools/ms282/export_skill_overrides.py 里
// 逐级自证，C# 不写死任何数值。没学强化核心时原样返回被强化技能自己的值。
public static class NativeSkillOverrides
{
    sealed class Rule
    {
        public int Source, ScaleTarget, DurationTarget;
        public string ScaleField, DurationField;
    }

    static Dictionary<string, object> table;
    static List<Rule> rules;

    static int Int(Dictionary<string, object> row, string key, int fallback = 0)
        => row != null && row.TryGetValue(key, out var raw) ? Convert.ToInt32(raw) : fallback;

    static string Str(Dictionary<string, object> row, string key)
        => row != null && row.TryGetValue(key, out var raw) ? Convert.ToString(raw) : "";

    static List<Rule> Rules
    {
        get
        {
            if (rules != null) return rules;
            rules = new List<Rule>();
            if (table == null)
            {
                var text = Resources.Load<TextAsset>("NativeSkillOverrides")?.text;
                table = string.IsNullOrEmpty(text)
                    ? new Dictionary<string, object>()
                    : (Dictionary<string, object>)MiniJson.Parse(text);
            }
            if (!table.TryGetValue("overrides", out var raw) ||
                !(raw is Dictionary<string, object> all)) return rules;
            foreach (var pair in all)
            {
                if (!(pair.Value is Dictionary<string, object> row)) continue;
                if (!int.TryParse(pair.Key, out int source)) continue;
                rules.Add(new Rule
                {
                    Source = source,
                    ScaleTarget = Int(row, "scaleTarget"),
                    ScaleField = Str(row, "scaleField"),
                    DurationTarget = Int(row, "durationTarget"),
                    DurationField = Str(row, "durationField"),
                });
            }
            return rules;
        }
    }

    /// <summary>该 id 的持续时间是否由某条强化核心接管（同时用于放行 summon 的 buff 注册）。</summary>
    public static bool IsDurationTarget(int id)
    {
        foreach (var rule in Rules) if (rule.DurationTarget == id) return true;
        return false;
    }

    static int Value(Rule rule, string field, int fallback)
    {
        int learned = SkillProgress.LevelOf(rule.Source);
        if (learned <= 0 || string.IsNullOrEmpty(field)) return fallback;
        int level = Mathf.Clamp(learned, 1, Mathf.Max(1, SkillBook.MaxLevelOf(rule.Source)));
        var row = SkillBook.Get(rule.Source)?.Level(level);
        int value = Art.Int(row, field, 0);
        return value > 0 ? value : fallback;
    }

    /// <summary>追加打击倍率：学了强化核心就用核心的 x，否则用技能自己的 x。</summary>
    public static int Scale(int targetId, int fallback)
    {
        foreach (var rule in Rules)
            if (rule.ScaleTarget == targetId) return Value(rule, rule.ScaleField, fallback);
        return fallback;
    }

    /// <summary>持续秒数：学了强化核心就用核心的 time，否则用技能自己的 time。</summary>
    public static int DurationSeconds(int targetId, int fallback)
    {
        foreach (var rule in Rules)
            if (rule.DurationTarget == targetId) return Value(rule, rule.DurationField, fallback);
        return fallback;
    }
}
