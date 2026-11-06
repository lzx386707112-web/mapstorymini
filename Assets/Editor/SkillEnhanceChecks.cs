using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 强化核心加成（NativeSkillEnhance）的验证：
//   ① 映表本身：包装技能 400004008 必须连到 1121008 / 1120017 / 1141000 / 1141001；
//   ② 未装核心：加成为 0（不能影响既有伤害口径）；
//   ③ 核心 Lv60：最终伤害 +120%、目标数 +1、无视防御 +20%（数据来自包装技能 common）；
//   ④ Play Mode 实测：同一条技能在「无核心 / 有核心」两种状态下的总伤害必须显著变大。
//
// 命令行：
//   Unity -batchmode -projectPath . -executeMethod SkillEnhanceChecks.Run -quit -logFile /tmp/enhance.log
//   Unity -batchmode -projectPath . -executeMethod SkillEnhanceChecks.RunPlay -logFile /tmp/enhance_play.log
public static class SkillEnhanceChecks
{
    const int Wrapper = 400004008;
    const int BaseSkill = 1121008;
    const int CoreId = 20010000;

    static readonly List<string> report = new List<string>();

    [MenuItem("Tools/技能诊断/强化核心加成")]
    public static void Run()
    {
        report.Clear();
        bool ok = true;

        var targets = NativeSkillEnhance.TargetsOf(Wrapper);
        targets.Sort();
        report.Add("① 映射：" + Wrapper + " → " + string.Join(",", targets));
        if (targets.Count == 0) { ok = false; report.Add("  ✗ 没有任何被强化技能"); }

        NativeSkillEnhance.Bonus(BaseSkill, out int f0, out int t0, out int i0, out int b0);
        report.Add("② 未装核心：" + f0 + "/" + t0 + "/" + i0 + "/" + b0);
        if (f0 != 0 || t0 != 0 || i0 != 0 || b0 != 0) { ok = false; report.Add("  ✗ 未装核心时加成不为 0"); }

        // 用面板快照注入核心等级（和真实登录走同一条 VMatrixLevels.Capture）
        var response = new VMatrixResponse();
        response.cores.Add(new VMatrixCoreView
        {
            coreId = CoreId, level = 60, maxLevel = 60,
            skills = new[] { Wrapper }, name = "狂暴攻擊強化", desc = "",
        });
        VMatrixLevels.Capture(response, 990001);
        int wrapperLevel = NativeSkillEnhance.WrapperLevel(Wrapper);
        NativeSkillEnhance.Bonus(BaseSkill, out int f1, out int t1, out int i1, out int b1);
        report.Add("③ 核心 Lv" + wrapperLevel + "：最终伤害 +" + f1 + "% 目标数 +" + t1 +
                   " 无视防御 +" + i1 + "% Boss +" + b1 + "%");
        if (wrapperLevel != 60 || f1 != 120 || t1 != 1 || i1 != 20) { ok = false; report.Add("  ✗ 与 common 公式不符"); }

        // 另一条被强化的技能也必须吃到同一份加成
        NativeSkillEnhance.Bonus(1141000, out int f2, out _, out _, out _);
        report.Add("   对照 1141000 最终伤害 +" + f2 + "%");
        if (f2 != f1) { ok = false; report.Add("  ✗ psdSkill 其它目标没有同步加成"); }

        VMatrixLevels.Clear();
        NativeSkillEnhance.Bonus(BaseSkill, out int f3, out _, out _, out _);
        report.Add("④ 清空核心后：" + f3);
        if (f3 != 0) { ok = false; report.Add("  ✗ 清空后仍有加成"); }

        report.Insert(0, ok ? "强化核心加成：全部通过" : "强化核心加成：有失败项");
        string text = string.Join("\n", report.ToArray());
        Debug.Log("[Enhance]\n" + text);
        try { System.IO.File.WriteAllText("/tmp/skill_enhance.txt", text); } catch { }
        EditorApplication.Exit(ok ? 0 : 1);
    }

    // 伤害通道对比：直接调 CombatRules.Attack（固定 roll/hitRoll，无随机、无未命中），
    // 比在 Play Mode 里打怪稳得多 —— 那边一次未命中就整段 0 伤害，看着像「没加成」。
    public static void RunDamage()
    {
        report.Clear();
        var def = SkillBook.Native(BaseSkill, 30);
        if (def == null) { report.Add("✗ SkillBook.Native(" + BaseSkill + ",30) 取不到定义"); Finish(false); return; }
        var target = new Dictionary<string, object>
        {
            { "level", 1 }, { "eva", 0 }, { "PDRate", 0 }, { "MDRate", 0 }, { "boss", 0 },
        };
        var weapon = new WeaponDef { id = "test", name = "test", kind = WeaponKind.sword, multiplier = 1.34 };
        var attributes = Profile.Attributes;

        VMatrixLevels.Clear();
        long plain = CombatRules.Attack(attributes, weapon, 200, 200, target, def, .5, 1.0, 0.0, long.MaxValue).Amount;

        var response = new VMatrixResponse();
        response.cores.Add(new VMatrixCoreView
        {
            coreId = CoreId, level = 60, maxLevel = 60,
            skills = new[] { Wrapper }, name = "狂暴攻擊強化", desc = "",
        });
        VMatrixLevels.Capture(response, 990001);
        long boosted = CombatRules.Attack(attributes, weapon, 200, 200, target, def, .5, 1.0, 0.0, long.MaxValue).Amount;

        report.Add("无核心：" + plain);
        report.Add("Lv60 核心（damR=+120%）：" + boosted);
        double ratio = plain <= 0 ? 0 : (double)boosted / plain;
        report.Add("倍率：" + ratio.ToString("0.000") + "（期望 ≈ 2.2）");
        bool ok = plain > 0 && ratio > 2.0 && ratio < 2.4;
        report.Insert(0, ok ? "强化核心伤害通道：通过" : "强化核心伤害通道：未通过");
        VMatrixLevels.Clear();
        Finish(ok);
    }

    // HEXA 强化（damR_6th，65 条 / 138 个被强化技能）的验证。
    // 它和 V 强化核心是同一张表、同一套结算，唯一区别是等级来自 HEXA 核心快照：
    // 核心 30000006 skills=[500004006]，包装技能 500004006 psdSkill=[400011072]，
    // common damR_6th = 10+x+5*log10(x)+5*log20(x)+10*log30(x) ⇒ lv30 = 60%。
    public static void RunHexa()
    {
        report.Clear();
        const int hexaCoreId = 30000006, hexaWrapper = 500004006, hexaSkill = 400011072;

        VMatrixLevels.Clear();
        var targets = NativeSkillEnhance.TargetsOf(hexaWrapper);
        targets.Sort();
        report.Add("① 映射：" + hexaWrapper + " → " + string.Join(",", targets));
        bool ok = targets.Contains(hexaSkill);
        if (!ok) report.Add("  ✗ 没有连到 " + hexaSkill);

        NativeSkillEnhance.Bonus(hexaSkill, out int f0, out int t0, out int i0, out int b0);
        report.Add("② 未装 HEXA：" + f0 + "/" + t0 + "/" + i0 + "/" + b0);
        if (f0 != 0) { ok = false; report.Add("  ✗ 未装时加成不为 0"); }

        var hexa = new HexaResponse();
        hexa.cores.Add(new HexaCoreView
        {
            coreId = hexaCoreId, type = 2, level = 30, maxLevel = 30, activated = true,
            name = "聖十字架", skills = new[] { hexaWrapper }, group = 1,
        });
        VMatrixLevels.CaptureHexa(hexa, 990001);
        int wrapperLevel = NativeSkillEnhance.WrapperLevel(hexaWrapper);
        NativeSkillEnhance.Bonus(hexaSkill, out int f1, out _, out _, out _);
        report.Add("③ HEXA Lv" + wrapperLevel + "：最终伤害 +" + f1 + "%（端 damR_6th lv30 = 60）");
        if (wrapperLevel != 30 || f1 != 60) { ok = false; report.Add("  ✗ 与 common 公式不符"); }

        // 伤害通道：damR 是独立乘区，+60% ⇒ ×1.6
        var def = SkillBook.Native(hexaSkill, 30);
        if (def == null) { ok = false; report.Add("  ✗ SkillBook.Native 取不到 " + hexaSkill); }
        else
        {
            var target = new Dictionary<string, object>
            {
                { "level", 1 }, { "eva", 0 }, { "PDRate", 0 }, { "MDRate", 0 }, { "boss", 0 },
            };
            var weapon = new WeaponDef { id = "test", name = "test", kind = WeaponKind.sword, multiplier = 1.34 };
            var attributes = Profile.Attributes;
            long boosted = CombatRules.Attack(attributes, weapon, 200, 200, target, def, .5, 1.0, 0.0, long.MaxValue).Amount;
            VMatrixLevels.Clear();
            long plain = CombatRules.Attack(attributes, weapon, 200, 200, target, def, .5, 1.0, 0.0, long.MaxValue).Amount;
            double ratio = plain <= 0 ? 0 : (double)boosted / plain;
            report.Add("④ 伤害通道：无 HEXA " + plain + " → Lv30 " + boosted +
                       "，倍率 " + ratio.ToString("0.000") + "（期望 ≈ 1.6）");
            if (!(ratio > 1.5 && ratio < 1.7)) { ok = false; report.Add("  ✗ 倍率不在 1.6 附近"); }
        }

        // 打开 V 面板（Capture 是「整份重建」语义）不能把 HEXA 的等级冲掉。
        // 注意 ④ 里为了算「无 HEXA」的基线调过一次 VMatrixLevels.Clear()，
        // 这里必须先重新灌 HEXA —— 否则测的是「清空后当然是 0」，得不出任何结论。
        VMatrixLevels.CaptureHexa(hexa, 990001);
        var vresp = new VMatrixResponse();
        vresp.cores.Add(new VMatrixCoreView
        {
            coreId = CoreId, level = 60, maxLevel = 60, skills = new[] { Wrapper }, name = "狂暴攻擊強化", desc = "",
        });
        VMatrixLevels.Capture(vresp, 990001);
        int hexaAfterV = NativeSkillEnhance.WrapperLevel(hexaWrapper);
        NativeSkillEnhance.Bonus(hexaSkill, out int f2, out _, out _, out _);
        bool hexaLookup = VMatrixLevels.TryGet(hexaWrapper, out int hexaRaw);
        report.Add("⑤ 再灌一次 V 矩阵快照后：HEXA Lv" + hexaAfterV + "（damR_6th +" + f2 + "%）" +
                   "  [TryGet=" + (hexaLookup ? hexaRaw.ToString() : "false") +
                   "  V=" + VMatrixLevels.Count(false) + " HEXA=" + VMatrixLevels.Count(true) + "]");
        if (hexaAfterV != 30 || f2 != 60) { ok = false; report.Add("  ✗ V 快照把 HEXA 等级冲掉了"); }

        VMatrixLevels.Clear();
        report.Insert(0, ok ? "HEXA 强化（damR_6th）：全部通过" : "HEXA 强化（damR_6th）：有失败项");
        string text = string.Join("\n", report.ToArray());
        Debug.Log("[EnhanceHexa]\n" + text);
        try { System.IO.File.WriteAllText("/tmp/skill_enhance_hexa.txt", text); } catch { }
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static void Finish(bool ok)
    {
        string text = string.Join("\n", report.ToArray());
        Debug.Log("[EnhanceDamage]\n" + text);
        try { System.IO.File.WriteAllText("/tmp/skill_enhance_damage.txt", text); } catch { }
        EditorApplication.Exit(ok ? 0 : 1);
    }
}

