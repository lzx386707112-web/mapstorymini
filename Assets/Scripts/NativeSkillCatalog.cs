using System.Collections.Generic;
using UnityEngine;

// 原 WZ 的等级参数只读快照；不能把剩余冷却写到配置中。
public sealed class NativeSkillDefinition
{
    public readonly int Id, Level, MpCost, HpCost, HitCount, TargetCount, FixedDamage;
    public readonly float Cooldown, DamagePercent, MagicAttack, Duration, Probability, AttackBuff, SummonInterval;
    // 连射/按住型技能的「每发间隔」（毫秒，源端 attackDelay）。
    // 之前统一按 100ms 打：间隔 150ms 的会多打一半、间隔 60ms 的会少打一半，
    // 伤害直接差一个乘区。0 表示源端没写，按调用方的默认值走。
    public readonly int AttackDelay;
    public readonly Vector2 Min, Max;
    public readonly string Kind, Element;
    public readonly bool Magic, HasBounds, ForwardBox;
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
        // 技能号 4 亿级：走 Art.Id（long）避免 float 把 400011088 舍成 400011072。
        Id = Art.Id(skill, "id"); Level = Art.Id(level, "level", 1);
        Element = skill.TryGetValue("element",out var element) ? element as string : "";
        // 这份结构同时给两条数据用：SkillBook.Native() 的 282 运行期字典（字段齐全），
        // 以及 Resources/GameplaySkills.json 的 15 条旧四转技能（没有 vMatrix / 部分字段）。
        // 之前直接 skill["vMatrix"] 取键，旧表一律 KeyNotFoundException ⇒ SkillSystem.Awake
        // 在建定义表时抛异常，真机上表现为「技能特效出来了但整条数值链没建起来」。
        Kind = skill.TryGetValue("kind", out var k) ? k as string : "Attack";
        Magic = skill["magic"] is bool b && b;
        SummonInterval = Mathf.Max(0.1f, Art.Num(skill, "summonInterval", 1f));
        MpCost = (int)Art.Num(level, "mpCon");
        // 282 有 8 条技能把冷却只写在 cooltimeMS（毫秒），与 cooltime（秒）互斥、从不并存：
        // 400011131 雷神战槌 2500 / 400021086 神之惩罚 1500 / 400051042 海龙冲锋 500 …
        // 只读 cooltime 会让它们变成 0 冷却 ⇒ 按一下放一次、可以无限连按，
        // 每次重播三四十张 500px 画布，表现就是「这技能放起来特别卡」。
        float cooldownSeconds = Art.Num(level, "cooltime");
        if (cooldownSeconds <= 0) cooldownSeconds = Art.Num(level, "cooltimeMS") / 1000f;
        Cooldown = cooldownSeconds;
        AttackDelay = (int)Art.Num(level, "attackDelay");
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
        // 282 的多段攻击会把第二/三/四段的判定框写成 lt2/rb2、lt3/rb3、lt4/rb4。
        // 400051024 ICBM 就只有 lt2=[-800,-400] rb2=[-250,80]，主 lt/rb 是空的 ——
        // 只认 lt/rb 会得到 HasBounds=false，InRange 退回 0 宽的默认框，
        // 表现是「技能放得出来、怪一点不掉血」。逐级回退，只认第一条存在的。
        string bounds = "";
        foreach (var suffix in new[] { "", "2", "3", "4" })
        {
            if (!level.ContainsKey("lt" + suffix) || !level.ContainsKey("rb" + suffix)) continue;
            // 282 的 ltN/rbN 不全是判定框，也可能是「一个点 / 一条线」：
            // 400021092 火蜥蜴的恶作剧 lt2=[65,-42] rb2=[67,-40] 只有 2x2px，
            // 真正的范围写在 lt3/rb3=[-1300,-750]..[1300,750]；
            // 400001008 / 400041038 的 lt2/rb2 是 0 宽竖线。
            // 导出链补全 lt2..lt4 之后若不过滤，这些技能会从「可见范围」退化成
            // 几像素的框 ⇒ 放得出来却一只怪都打不到。只过滤备用框，主 lt/rb 保持原口径。
            if (suffix.Length > 0)
            {
                var lo = Point(level, "lt" + suffix, Vector2.zero);
                var hi = Point(level, "rb" + suffix, Vector2.zero);
                float minSpan = 20f / Art.PPU;
                if (hi.x - lo.x < minSpan || hi.y - lo.y < minSpan) continue;
            }
            bounds = suffix; break;
        }
        HasBounds = bounds.Length > 0 || level.ContainsKey("lt") && level.ContainsKey("rb");
        Min = Point(level, "lt" + bounds, new Vector2(-Art.Num(level, "range", 200), -100));
        Max = Point(level, "rb" + bounds, new Vector2(0, 100));
        // 全库 962 个有框技能里 958 个是「负框」（lt.x<0、rb.x<=0），源端按左朝向写，
        // `InRange` 统一取 -max.x..-min.x 镜像到身前是对的。剩下 4 个是**正框**
        // （400051018/019/020 聚光灯、5101002 回马，lt.x=0、rb.x>0），继续镜像会把它们
        // 判到身后：运行时 `[Hit] id=400051018 框=(0.00,0.00)~(31.25,31.25) ⇒ 选中=0`
        // 就是这么来的。正框保持原方向（x = delta.x*facing 下 [Min.x,Max.x] 即身前）。
        // ⚠️ 未逆到 EXE 的框选函数，这是「4 条特例 + 958 条反例」推出的显式本地适配，
        //    不推广到其它框；若源端证实正框另有语义，回滚这三处即可。
        ForwardBox = bounds.Length == 0 && HasBounds && Min.x >= -0.0001f && Max.x > Min.x;
        // 按住型光束的「看得见 ≠ 打得到」：400011072 聖十字架 的 common 写的是
        //   lt=[-210,-100] rb=[210,30]   ← 每发判定框只有 ±210px
        //   w=1080                       ← 光束的视觉长度（px）
        // 照 lt/rb 结算，画面上一道光束贯穿半个屏幕，实际只有贴身的怪会掉血。
        // w 就是源端给的光束长度，按住期间沿朝向把它接到判定框上（只放长前向，不动后向/高度）。
        // 只对「按住连续攻击」的技能做（RapidAttack），普通技能 w 的语义不是光束长度。
        int beamWidth = (int)Art.Num(level, "w");
        if (beamWidth > 0 && HasBounds && Kind == "Attack" && NativeSkillVfx.RapidAttack(Id))
        {
            float forward = beamWidth / Art.PPU;
            if (forward > -Min.x) Min = new Vector2(-forward, Min.y);
        }
        bool vMatrix = skill.TryGetValue("vMatrix", out var matrixFlag) && matrixFlag is bool vm && vm;
        // 只对攻击型生效：召唤物有自己的一套射程（NativeSummon.AttackRange），
        // 让它吃「可见范围」会把毒领域/炮击艇的召唤物变成全屏炮台。
        // range 优先级在它之上：四转技能用 range 给圆形半径，不能被「可见范围」顶掉。
        // 四转技能不套这条（它们要么有 lt/rb、要么有 range，剩下的是 200px 口径）。
        // 五六转里确实有大量技能没写 lt/rb（旧导出表把 lt2/rb2 也丢了，看起来像
        // 「全库都没有」），但商业端 400011072 聖十字架 明写 lt/rb 四组框，
        // 所以这里只能是「没有框时按可见范围」，不能当作源端口径的结论。
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
