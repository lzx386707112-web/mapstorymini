using System.Collections.Generic;
using UnityEngine;

// 原 WZ 的等级参数只读快照；不能把剩余冷却写到配置中。
public sealed class NativeSkillDefinition
{
    public readonly int Id, Level, MpCost, HitCount, TargetCount;
    public readonly float Cooldown, DamagePercent, MagicAttack, Duration, Probability, AttackBuff, SummonInterval;
    public readonly Vector2 Min, Max;
    public readonly string Kind, Element;
    public readonly bool Magic, HasBounds;

    public NativeSkillDefinition(Dictionary<string, object> skill, Dictionary<string, object> level)
    {
        Id = (int)Art.Num(skill, "id"); Level = (int)Art.Num(level, "level", 1);
        Element = skill.TryGetValue("element",out var element) ? element as string : "";
        Kind = skill["kind"] as string; Magic = skill["magic"] is bool b && b;
        SummonInterval = Mathf.Max(0.1f, Art.Num(skill, "summonInterval", 1f));
        MpCost = (int)Art.Num(level, "mpCon"); Cooldown = Art.Num(level, "cooltime");
        HitCount = (int)Art.Num(level, "attackCount", 1);
        TargetCount = (int)Art.Num(level, "mobCount", 1);
        DamagePercent = Art.Num(level, "damage"); MagicAttack = Art.Num(level, "mad");
        Duration = Art.Num(level, "time"); Probability = Art.Num(level, "prop", 100) / 100f;
        AttackBuff = Art.Num(level, "pad");
        HasBounds = level.ContainsKey("lt") && level.ContainsKey("rb");
        Min = Point(level, "lt", new Vector2(-Art.Num(level, "range", 200), -100));
        Max = Point(level, "rb", new Vector2(0, 100));
    }

    static Vector2 Point(Dictionary<string, object> d, string key, Vector2 fallback)
    {
        if (!d.TryGetValue(key, out var v) || !(v is List<object> list) || list.Count < 2) return fallback / Art.PPU;
        return new Vector2(System.Convert.ToSingle(list[0]), System.Convert.ToSingle(list[1])) / Art.PPU;
    }
}

public static class NativeSkillCatalog
{
    static readonly Dictionary<string, List<NativeSkillDefinition>> skills = new Dictionary<string, List<NativeSkillDefinition>>();
    static bool loaded;
    public static NativeSkillDefinition Get(string key, int level = 1)
    {
        if (!loaded)
        {
            loaded = true;
            var asset = Resources.Load<TextAsset>("GameplaySkills");
            if (asset == null) { Debug.LogError("[Skill] 缺少 GameplaySkills 数据"); return null; }
            var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
            foreach (Dictionary<string, object> entry in (List<object>)root["skills"])
            {
                var definitions = new List<NativeSkillDefinition>();
                foreach (Dictionary<string, object> lv in (List<object>)entry["levels"])
                    definitions.Add(new NativeSkillDefinition(entry, lv));
                skills.Add((string)entry["key"], definitions);
            }
        }
        if (!skills.TryGetValue(key, out var values)) return null;
        return values.Find(d => d.Level == level);
    }
}
