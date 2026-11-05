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
    // 282 的「没有固定攻击框」有三档标记：
    //   info/skillType=3  —— 天怒VI(2341010)、暴风雪VI(2241004)、火流星VI(2140013)、终极攻击VI…
    //   info/areaAttack=1 —— 范围攻击（日月力量、暗灵冲击、火炎神之怒号、巨鲨狂浪…）
    //   五六转技能        —— V/HEXA 全库没有一条写 lt/rb，定义层只给标记不给框
    // 三者都按相机可见范围判定。退回「身前 200px」的后果不是保守而是错：15 个目标的大招
    // 只能打到贴脸的一只怪。判据与清单见 docs/tms282-skill/20-v-hexa-panel.md §7.15。
    public readonly bool FullScreen;
    // 源端「没有固定攻击框但确实是大范围攻击」：skillType=3 / info/areaAttack / 五六转。
    public readonly bool AreaAttack;

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
        bool vMatrix = skill["vMatrix"] is bool vm && vm;
        // 只对攻击型生效：召唤物有自己的一套射程（NativeSummon.AttackRange），
        // 让它吃「可见范围」会把毒领域/炮击艇的召唤物变成全屏炮台。
        // range 优先级在它之上：四转技能用 range 给圆形半径，不能被「可见范围」顶掉。
        // 四转技能不套这条（它们要么有 lt/rb、要么有 range，剩下的是 200px 口径），
        // 只有五六转 —— 282 的 V/HEXA 全库没有一条写 lt/rb —— 才按可见范围判。
        AreaAttack = Kind == "Attack" && ((int)Art.Num(skill, "skillType") == 3 || vMatrix);
        FullScreen = !HasBounds && !level.ContainsKey("range") && AreaAttack;
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
