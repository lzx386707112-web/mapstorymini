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
            float tooltipW = 236f * s;
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

                    if (set.Overlaps(equip))
                    {
                        fail++; Debug.LogError("[Tooltip] 套装面板压住装备框：" + tag +
                            " equip=" + Fmt(equip) + " set=" + Fmt(set));
                        continue;
                    }
                    // 装得下就必须完整在屏内（两侧都不够时才允许夹取）
                    bool fitsRight = equip.xMax + 4f * s + set.width <= W - 4f;
                    bool fitsLeft = equip.x - 4f * s - set.width >= 4f;
                    bool inside = set.x >= -0.5f && set.xMax <= W + 0.5f && set.y >= -0.5f && set.yMax <= H + 0.5f;
                    if ((fitsRight || fitsLeft) && !inside)
                    {
                        fail++; Debug.LogError("[Tooltip] 装得下却跑到屏外：" + tag +
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

        Debug.Log("[Tooltip] 布局断言 " + pass + " 通过 / " + fail + " 失败");
        EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    static string Fmt(Rect r) =>
        "(" + r.x.ToString("0.0") + "," + r.y.ToString("0.0") + "," +
        r.width.ToString("0.0") + "x" + r.height.ToString("0.0") + ")";
}
