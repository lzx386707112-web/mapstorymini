using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 悬浮框布局的无渲染断言（不需要进 Play，batchmode 直接跑）：
//   /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -quit \
//     -projectPath <proj> -executeMethod TooltipLayoutChecks.Run -logFile /tmp/tooltip-layout.log
//
// 起因：套装面板原来贴在装备框「正下方」、放不下就往上翻，八件套的面板比装备框还高，
// 往上翻就把装备框整个盖住，只剩底部几行露出来 —— 看起来像装备面板没画（2026-10-29 报障）。
// 现在改成源端的右侧并排，这里把各种视口/装备框位置都摆一遍，确认两者永不重叠。
public static class TooltipLayoutChecks
{
    // 真实数据：八件套（冒险岛宝石）展开约 516 逻辑高；小套装约 200
    const float SetTall = 516f, SetShort = 200f;
    // 装备卡 UI/UIToolTip.img/Item/Equip/frame/common 的实测宽度
    const float EquipCardWidth = 324f;

    public static void Run()
    {
        int pass = 0, fail = 0;

        // s 取自 BottomBar.S：1280x720 → 1.082，竖屏手机 471x555 → 0.834，800x480 → 0.72
        float[] scales = { 1.082f, 0.834f, 0.72f };
        float[] screensW = { 1280f, 611f, 1111f };
        float[] screensH = { 720f, 720f, 667f };
        float[] heights = { SetTall, SetShort };

        for (int i = 0; i < scales.Length; i++)
        {
            float s = scales[i], W = screensW[i], H = screensH[i];
            // 装备卡 frame/common 是 324 宽；套装面板 282 frame/set 是 298 宽
            //（旧 beidou DLL 的套装层是 236，落点断言必须跟着改）。
            float tooltipW = EquipCardWidth * s;
            // 装备框 tooltip：高 = 166 + 行数×16（帽子 12 行 → 358）
            float tooltipH = (166f + 12 * 16f) * s;

            // 指针从左到右全部扫一遍，包括贴左、居中、贴右
            for (float px = 0; px <= W + 1; px += Mathf.Max(1f, W / 40f))
            {
                float x = Mathf.Clamp(px, 0, Mathf.Max(0, W - tooltipW));
                float y = Mathf.Clamp(px * 0.5f + 20f * s, 0, Mathf.Max(0, H - tooltipH));
                var equip = new Rect(x, y, tooltipW, tooltipH);

                foreach (float h in heights)
                {
                    var set = ItemTooltip.SetPanelRect(equip, h, s, W, H);
                    string tag = "s=" + s + " 屏=" + W + "x" + H + " 面板高=" + h + " 指针x=" + Mathf.Round(px);

                    // 单侧放得下 ⇒ 必须彻底不重叠且完整在屏内。
                    // 两侧都放不下时（298+324 在窄屏上几何上摆不下两栏），重叠不可避免：
                    // 只要求面板被夹在屏内、顶边可见，不再要求零重叠。
                    bool fitsRight = equip.xMax + 4f * s + set.width <= W - 4f;
                    bool fitsLeft = equip.x - 4f * s - set.width >= 4f;
                    bool inside = set.x >= -0.5f && set.xMax <= W + 0.5f && set.y >= -0.5f && set.yMax <= H + 0.5f;
                    if (fitsRight || fitsLeft)
                    {
                        if (set.Overlaps(equip))
                        {
                            fail++; Debug.LogError("[Tooltip] 装得下却压住装备框：" + tag +
                                " equip=" + Fmt(equip) + " set=" + Fmt(set));
                            continue;
                        }
                        if (!inside)
                        {
                            fail++; Debug.LogError("[Tooltip] 装得下却跑到屏外：" + tag +
                                " equip=" + Fmt(equip) + " set=" + Fmt(set));
                            continue;
                        }
                    }
                    else if (!inside)
                    {
                        fail++; Debug.LogError("[Tooltip] 两侧都放不下时未夹进屏幕：" + tag +
                            " equip=" + Fmt(equip) + " set=" + Fmt(set));
                        continue;
                    }
                    // 竖直方向允许溢出：面板本身比屏还高时只保证顶边可见
                    if (set.height > H && set.y > 4.5f)
                    {
                        fail++; Debug.LogError("[Tooltip] 超屏面板没有贴顶：" + tag + " set=" + Fmt(set));
                        continue;
                    }
                    pass++;
                }
            }
        }

        // —— 星级条（BeiDouSetItemCompat.dll 0x65C81949 上限表 / 0x65C81650 名字解析）——
        int[,] caps = { { 0, 5 }, { 1, 5 }, { 94, 5 }, { 95, 8 }, { 107, 8 }, { 108, 10 }, { 117, 10 },
                        { 118, 15 }, { 127, 15 }, { 128, 20 }, { 137, 20 }, { 138, 25 }, { 200, 25 } };
        for (int i = 0; i < caps.GetLength(0); i++)
        {
            int got = ItemTooltip.StarCapacity(caps[i, 0]);
            if (got == caps[i, 1]) pass++;
            else { fail++; Debug.LogError("[Tooltip] 星级上限 Lv." + caps[i, 0] + " 期望 " + caps[i, 1] + " 实得 " + got); }
        }
        string[] named = { "冒险岛宝石贝雷帽", "冒险岛宝石贝雷帽5★", "冒险岛宝石贝雷帽12★", "★", "剑★" };
        int[] want = { 0, 5, 12, 0, 0 };
        for (int i = 0; i < named.Length; i++)
        {
            int got = ItemTooltip.StarFromName(named[i]);
            if (got == want[i]) pass++;
            else { fail++; Debug.LogError("[Tooltip] 星数解析 \"" + named[i] + "\" 期望 " + want[i] + " 实得 " + got); }
        }

        // —— 大额数值的万/亿/兆/京写法（282 单位阶梯：京/兆/億/萬，atkPow 字形只有 0-9 + 万 + 亿）——
        // 每个档位都过一遍：不足万、万、亿、兆、京、long 上限、负数、long.MinValue。
        var compact = new (long value, string text)[]
        {
            (0L, "0"), (1L, "1"), (999L, "999"), (9999L, "9,999"),
            (10000L, "1万"), (10001L, "1万 1"), (12345L, "1万 2345"),
            (2204312L, "220万 4312"), (99999999L, "9999万 9999"),
            (100000000L, "1亿"), (100000001L, "1亿 1"),
            (123456789L, "1亿 2345万 6789"), (503000000L, "5亿 300万"),
            (999999999999L, "9999亿 9999万 9999"),
            (1000000000000L, "1兆"), (1000000000001L, "1兆 1"),
            (1234567890123L, "1兆 2345亿 6789万 123"),
            (10000000000000000L, "1京"),
            (12345678901234567L, "1京 2345兆 6789亿 123万 4567"),
            (long.MaxValue, "922京 3372兆 368亿 5477万 5807"),
            (-1L, "-1"), (-9999L, "-9,999"), (-12345L, "-1万 2345"),
            (-100000000L, "-1亿"), (-1234567890123L, "-1兆 2345亿 6789万 123"),
            (long.MinValue, "-922京 3372兆 368亿 5477万 5807")   // 绝对值溢出按上限夹
        };
        foreach (var c in compact)
        {
            string got = NumberFormat.Compact(c.value);
            if (got == c.text) pass++;
            else { fail++; Debug.LogError("[Tooltip] 数值缩写 " + c.value + " 期望 " + c.text + " 实得 " + got); }
        }

        // —— 战斗力公式（PacketCreator.calculateNameplatePower）——
        (long stats, long attack, long hp, long mp, long power)[] powers =
        {
            (1000L, 10L, 5000L, 100L, 10505L),   // 1000×10 + 500 + 5
            (0L, 0L, 0L, 0L, 0L),                // 属性 0 就是 0
            (5L, 0L, 0L, 0L, 5L),                // max(1, 0) = 1
            (0L, 0L, 12345L, 999L, 1283L),       // 只有 HP/MP：1234 + 49
            (-10L, 5L, -100L, -100L, 0L),        // 负值全按 0
            (1000000L, 100000L, 0L, 0L, 100000000000L),   // 10^11
            (long.MaxValue, 2L, 0L, 0L, long.MaxValue)     // 溢出饱和，不回绕成负数
        };
        foreach (var p in powers)
        {
            long got = CombatPower.Of(p.stats, p.attack, p.hp, p.mp);
            if (got == p.power) pass++;
            else { fail++; Debug.LogError("[Tooltip] 战斗力 " + p.stats + "/" + p.attack + " 期望 " + p.power + " 实得 " + got); }
        }

        // —— 「战斗力提升量」数值的字号退档（209px 槽位，兆/京级不能顶出边框）——
        var powerTexts = new (string text, int size)[]
        {
            ("+ 8,470", 20),
            ("+ 1万 2345", 20),
            ("+ 220万 4312", 16),
            ("+ 1亿 2345万 6789", 16),
            ("+ 9亿 9999万 9999", 16),
            ("+ 922京 3372兆 368亿 5477万 5807", 12),
            ("+ 1234兆 5678亿 9012万 3456", 12)
        };
        foreach (var t in powerTexts)
        {
            int got = ItemTooltip.PowerFontSize(t.text);
            if (got == t.size) pass++;
            else { fail++; Debug.LogError("[Tooltip] 战力字号 \"" + t.text + "\"(" + t.text.Length + "字) 期望 " + t.size + " 实得 " + got); }
        }

        // —— 282 套装数据全量迁入（877 套 / 简体 / Option / activeSkill）——
        void Check(bool ok, string tag)
        {
            if (ok) pass++;
            else { fail++; Debug.LogError("[Tooltip] 282 套装断言失败：" + tag); }
        }

        Check(AdminSetItem.Using282Data && AdminSetItem.BuiltInSetCount == 877,
            "内置套装应为 282 的 877 套，实得 " + AdminSetItem.BuiltInSetCount);
        var worn504 = new List<int> { 1004422, 1052882, 1073030, 1082636, 1102775, 1302333 };
        var panels = AdminSetItem.PanelsFor(1004422, worn504);
        Check(panels.Count > 0 && panels[0].Name == "航海师套装(战士)",
            "504 套装名应为简体「航海师套装(战士)」");
        Check(panels.Count > 0 && panels[0].CompleteCount == 7 && panels[0].EquippedCount == 6,
            "504 件数应为 6 / 7");
        Check(panels.Count > 0 && panels[0].Tiers.Count > 0
              && panels[0].Tiers[0].Options.Count > 0
              && panels[0].Tiers[0].Options[0].Contains("攻击Boss怪物时伤害"),
            "Option 文案应为简体且占位符已替换");
        Check(AdminSetItem.EquipName282(1004422).Contains("航海师"),
            "282 专属道具名应来自 String/Eqp.img");

        // Option incDAMr：boss:1 → BossDamage，否则 → Damage（导出期修，不在运行时兜底）。
        var b504 = AdminSetItem.ComputeBonus(new List<int> { 1004422, 1052882 }, -1);
        Check(b504.TryGetValue("BossDamage", out int bossDmg) && bossDmg == 10,
            "504 的 2 件档应给 BossDamage 10");
        Check(!b504.TryGetValue("Damage", out int plainDmg) || plainDmg == 0,
            "boss:1 的 Option 不能误算成普通 Damage");
        var b996 = AdminSetItem.ComputeBonus(new List<int> { 1112019, 1022234, 1122286 }, -1);
        Check(b996.TryGetValue("Damage", out int dmg996) && dmg996 == 3,
            "996 的非 boss Option 应给 Damage 3");

        // activeSkill 授予 / 移除：set 40 的 2 件档给 80001037 lv1。
        var granted = AdminSetItem.ActiveSkillsFor(new List<int> { 5000084, 5000089 }, -1);
        Check(granted.Exists(g => g.Id == 80001037 && g.Level == 1), "2 件档应授予 80001037 lv1");
        NativeSetItemSkills.Refresh(new List<int> { 5000084, 5000089 }, -1);
        bool registered = NativeSetItemSkills.IsSkill(80001037);
        Check(registered, "授予表应登记 80001037");
        // 数据没装载时不要拿不存在的技能去探 SkillBook.Get（会抛 FileNotFoundException），
        // 上面的 registered 断言已经把「数据没读到」这件事报出来了。
        if (registered)
        {
            Check(SkillProgress.BaseLevelOf(80001037) == 1, "授予表应接到 SkillProgress.BaseLevelOf");
            NativeSetItemSkills.Refresh(new List<int>(), -1);
            // 卸下后 IsSkill 为 false 即等价于 BaseLevelOf 回 0；这里不直接探 BaseLevelOf，
            // 否则会走到 SkillBook.Get(80001037) 去读资源包里不存在的 Skill/8000.img。
            Check(!NativeSetItemSkills.IsSkill(80001037), "卸下装备后应自动移除授予技能");
        }
        Check(AdminSetItem.ActiveSkillsFor(new List<int>(), -1).Count == 0, "空穿着不应授予技能");

        Debug.Log("[Tooltip] 布局断言 " + pass + " 通过 / " + fail + " 失败");
        EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    static string Fmt(Rect r) =>
        "(" + r.x.ToString("0.0") + "," + r.y.ToString("0.0") + "," +
        r.width.ToString("0.0") + "x" + r.height.ToString("0.0") + ")";
}
