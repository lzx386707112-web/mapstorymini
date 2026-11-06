using System;
using System.Collections.Generic;
using UnityEngine;

// 282 强化核心（VMatrix type=1）对既有技能的加成。
//
// 商业端的结构：强化核心本身不施放任何技能，它连的是 `400004xxx` 包装技能，
// 包装技能 `info/type=50`、`common` 只有四个字段、`psdSkill` 列出被强化的既有技能：
//     damR_5th        最终伤害 +%          2*x / 3*x / 5*x
//     targetPlus_5th  攻击目标数 +N        log20(x)
//     ignoreMobpdpR   无视防御 +%          log40(x)*20
//     bdR             Boss 伤害 +%         log70(x)*20
// 等级读 `VMatrixLevels`（它按技能 id 记核心等级，包装技能的 id 就是键）。
//
// 此前项目只在 V 矩阵面板里说「强化核心只提升伤害、不能绑快捷栏」，
// 但这条提升从未结算（代码里搜不到 damR_5th）⇒ 306 个核心装了等于没装。
// 数值全部来自 Assets/Resources/NativeSkillEnhance.json
// （tools/ms282/export_enhance_cores.py 从 VMatrix.json + psdSkill + common 导出）。
public static class NativeSkillEnhance
{
    sealed class Spec
    {
        public int Wrapper;
        public int MaxLevel;
        // 等级来源：0 = V 矩阵强化核心（vmatrix_cores / V 面板快照），
        //           1 = HEXA 强化核心（hexa_skills / HEXA 面板快照）。
        // 两者的等级最终都镜像进 VMatrixLevels，见 LevelOf()。
        public int Source;
        // 每级 [damR, targetPlus, ignorePdp, boss]，顺序与 tools/ms282/export_enhance_cores.py 的
        // LEVEL_KEYS 一致；用数组而不是 Dictionary<string,Row>，371 核 x 60 级才不至于把
        // TextAsset 撑到 1.7MB（导入慢、MiniJson 解析也慢）。
        public List<int[]> Levels = new List<int[]>();
    }

    static bool ready;
    static readonly Dictionary<int, List<Spec>> bySkill = new Dictionary<int, List<Spec>>();

    static void Ensure()
    {
        if (ready) return;
        ready = true;
        var asset = Resources.Load<TextAsset>("NativeSkillEnhance");
        if (asset == null) return;
        if (!(MiniJson.Parse(asset.text) is Dictionary<string, object> root)) return;
        if (!root.TryGetValue("cores", out var raw) || !(raw is Dictionary<string, object> cores)) return;
        foreach (var pair in cores)
        {
            if (!(pair.Value is Dictionary<string, object> core)) continue;
            // id 类字段必须走 Art.Id（long 精确转换）：Art.Int 内部是 Mathf.RoundToInt(float)，
            // 400004008 超过 2^28 后 float 步长是 32，会被舍成 400004000 ⇒ 和 VMatrixLevels
            // 的键对不上、加成永远为 0。这是 IRON-RULES §7.1 那条老坑。
            var spec = new Spec
            {
                Wrapper = Art.Id(core, "w"),
                MaxLevel = Mathf.Max(1, Art.Id(core, "max", 60)),
                Source = Art.Id(core, "s"),
            };
            if (!core.TryGetValue("lv", out var rawLevels) || !(rawLevels is List<object> levels))
            {
                Debug.LogWarning("[SkillEnhance] " + pair.Key + " 没有等级数组，跳过");
                continue;
            }
            foreach (var item in levels)
            {
                if (!(item is List<object> row))
                {
                    spec.Levels.Add(new[] { 0, 0, 0, 0 });
                    continue;
                }
                spec.Levels.Add(new[]
                {
                    row.Count > 0 ? Convert.ToInt32(row[0]) : 0,
                    row.Count > 1 ? Convert.ToInt32(row[1]) : 0,
                    row.Count > 2 ? Convert.ToInt32(row[2]) : 0,
                    row.Count > 3 ? Convert.ToInt32(row[3]) : 0,
                });
            }
            if (!core.TryGetValue("t", out var rawTargets) || !(rawTargets is List<object> targets)) continue;
            foreach (var item in targets)
            {
                int skill = (int)Art.Long(item);
                if (skill <= 0) continue;
                if (!bySkill.TryGetValue(skill, out var list)) bySkill[skill] = list = new List<Spec>();
                list.Add(spec);
            }
        }
        int linked = 0;
        foreach (var list in bySkill.Values) linked += list.Count;
        if (bySkill.Count == 0)
            Debug.LogWarning("[SkillEnhance] 表里没有被强化技能：强化核心加成不会生效");
        else if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[SkillEnhance] 载入 " + cores.Count + " 个核心 / " + bySkill.Count +
                      " 条被强化技能 / " + linked + " 条连线");
    }

    /// <summary>
    /// 包装技能当前的生效等级。两条来源合成一条链：
    /// ① `VMatrixLevels` —— V 面板快照 + HEXA 面板快照（CaptureHexa）都镜像到这里，
    ///    刚升完级还没重拉角色数据时也能立刻生效；
    /// ② 存档技能表 —— 服务端 `NativeMatrixSkills.Sync` 把两类核心等级都写进 skills 表
    ///    （HEXA 的 `core.skills` 就是 `500004xxx`，所以这条链对 HEXA 同样成立）。
    /// 不用 `SkillProgress.LevelOf`：它内部先过 `SkillBook.Get`，包装技能不在 JobTree 里，
    /// 取不到定义就直接返回 0，会让 65 条 HEXA 强化静默失效。
    /// </summary>
    static int LevelOf(int wrapper)
    {
        if (VMatrixLevels.TryGet(wrapper, out int level) && level > 0) return level;
        return Profile.Data != null ? Profile.Data.SkillLevelOf(wrapper) : 0;
    }

    /// <summary>当前角色身上该技能的强化核心合计：最终伤害%、目标数、无视防御%、Boss 伤害%。</summary>
    public static void Bonus(int skillId, out int finalPct, out int targetPlus, out int ignorePdp, out int bossPct)
    {
        finalPct = 0; targetPlus = 0; ignorePdp = 0; bossPct = 0;
        if (skillId <= 0) return;
        Ensure();
        if (!bySkill.TryGetValue(skillId, out var specs)) return;
        foreach (var spec in specs)
        {
            int level = LevelOf(spec.Wrapper);
            if (level <= 0) continue;
            int index = Mathf.Clamp(level, 1, Mathf.Min(spec.MaxLevel, spec.Levels.Count)) - 1;
            if (index < 0 || index >= spec.Levels.Count) continue;
            var row = spec.Levels[index];
            finalPct += row[0];
            targetPlus += row[1];
            ignorePdp += row[2];
            bossPct += row[3];
        }
    }

    public static int FinalPct(int skillId) { Bonus(skillId, out int v, out _, out _, out _); return v; }
    public static int TargetPlus(int skillId) { Bonus(skillId, out _, out int v, out _, out _); return v; }
    public static int IgnorePdp(int skillId) { Bonus(skillId, out _, out _, out int v, out _); return v; }
    public static int BossPct(int skillId) { Bonus(skillId, out _, out _, out _, out int v); return v; }

    /// <summary>调试/探针用：某个核心包装技能当前等级。</summary>
    public static int WrapperLevel(int wrapper) => LevelOf(wrapper);

    /// <summary>被某个包装技能强化的技能列表（探针断言用）。</summary>
    public static List<int> TargetsOf(int wrapper)
    {
        Ensure();
        var result = new List<int>();
        foreach (var pair in bySkill)
            foreach (var spec in pair.Value)
                if (spec.Wrapper == wrapper && !result.Contains(pair.Key)) result.Add(pair.Key);
        return result;
    }
}
