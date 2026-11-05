using System.Collections.Generic;
using UnityEngine;

// 原 WZ 的等级参数只读快照；不能把剩余冷却写到配置中。
public sealed class NativeSkillDefinition
{
    public readonly int Id, Level, MpCost, HpCost, HitCount, TargetCount, FixedDamage;
    public readonly float Cooldown, DamagePercent, MagicAttack, Duration, Probability, AttackBuff, SummonInterval;
    public readonly Vector2 Min, Max;
    public readonly string Kind, Element;
    public readonly bool Magic, HasBounds;
    // 282 用 info/skillType=3 标记「无固定攻击框」的技能：天怒VI(2341010)、
    // 暴风雪VI(2241004)、火流星VI(2140013)、终极攻击VI、战舰鲸鱼号VI…
    // 这些技能源端本来就没有 lt/rb，判定覆盖整个屏幕，不是身前 200px。
    public readonly bool FullScreen;

    public NativeSkillDefinition(Dictionary<string, object> skill, Dictionary<string, object> level)
    {
        Id = (int)Art.Num(skill, "id"); Level = (int)Art.Num(level, "level", 1);
        Element = skill.TryGetValue("element",out var element) ? element as string : "";
        Kind = skill["kind"] as string; Magic = skill["magic"] is bool b && b;
        SummonInterval = Mathf.Max(0.1f, Art.Num(skill, "summonInterval", 1f));
        MpCost = (int)Art.Num(level, "mpCon"); Cooldown = Art.Num(level, "cooltime");
        HpCost = (int)Art.Num(level, "hpCon");
        FixedDamage = (int)Art.Num(level, "fixdamage", -1);
        // AbstractDealDamageHandler:1103 —— 一次攻击允许的段数 = max(bulletCount, attackCount)。
        // 二连射/双飞斩/三连环光击破/箭扫射/急速射/三连射杀这类旧技能只有 bulletCount，
        // 没有 attackCount；只读 attackCount 会把它们降成 1 段。
        HitCount = Mathf.Max(1, Mathf.Max((int)Art.Num(level, "attackCount"), (int)Art.Num(level, "bulletCount")));
        TargetCount = (int)Art.Num(level, "mobCount", 1);
        DamagePercent = Art.Num(level, "damage"); MagicAttack = Art.Num(level, "mad");
        Duration = Art.Num(level, "time"); Probability = Art.Num(level, "prop", 100) / 100f;
        AttackBuff = Art.Num(level, "pad");
        HasBounds = level.ContainsKey("lt") && level.ContainsKey("rb");
        Min = Point(level, "lt", new Vector2(-Art.Num(level, "range", 200), -100));
        Max = Point(level, "rb", new Vector2(0, 100));
        FullScreen = !HasBounds && (int)Art.Num(skill, "skillType") == 3;
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
