using System;
using UnityEditor;
using UnityEngine;

// 聊天窗（Messenger）几何断言：无渲染，batchmode 可跑。
// 期望值全部来自 BeiDou.exe（1b91cf5b…34b2）：0x850264 尺寸表、0x8505e2 控件创建、
// 0x84f44b 座位/名字条。改了 NativeMessengerWindow 的常量而没同步这里，会直接失败。
//
//   /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -quit \
//     -projectPath <proj> -executeMethod MessengerLayoutChecks.Run -logFile /tmp/msgcheck.log
public static class MessengerLayoutChecks
{
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Rect_(Rect actual, Rect expected, string name)
    {
        Require(actual == expected, name + " expected " + expected + " got " + actual);
    }
    public static void Run()
    {
        int exit = 0;
        try
        {
            // 1) 三种模式窗口尺寸（0x85032a / 0x85030d / 0x8502f0）
            Require(NativeMessengerWindow.SizeOf(0) == new Vector2(295, 364), "mode 0 size");
            Require(NativeMessengerWindow.SizeOf(1) == new Vector2(392, 240), "mode 1 size");
            Require(NativeMessengerWindow.SizeOf(2) == new Vector2(184, 22), "mode 2 size");

            // 2) 标题栏三键：顺序 关闭/最小化/最大化 = 276/248/262，y=6；收起态 166/138/152，y=5
            Rect_(NativeMessengerWindow.TitleClose(0), new Rect(276, 6, 12, 12), "close (mode 0)");
            Rect_(NativeMessengerWindow.TitleMin(0), new Rect(248, 6, 12, 12), "min (mode 0)");
            Rect_(NativeMessengerWindow.TitleMax(0), new Rect(262, 6, 12, 12), "max (mode 0)");
            Rect_(NativeMessengerWindow.TitleClose(2), new Rect(166, 5, 12, 12), "close (mode 2)");
            Rect_(NativeMessengerWindow.TitleMin(2), new Rect(138, 5, 12, 12), "min (mode 2)");
            Rect_(NativeMessengerWindow.TitleMax(2), new Rect(152, 5, 12, 12), "max (mode 2)");
            foreach (int mode in new[] { 0, 2 })
            {
                var a = NativeMessengerWindow.TitleMin(mode); var b = NativeMessengerWindow.TitleMax(mode); var c = NativeMessengerWindow.TitleClose(mode);
                Require(a.yMin == b.yMin && b.yMin == c.yMin, "title buttons not on one baseline (mode " + mode + ")");
                Require(a.xMax <= b.xMin && b.xMax <= c.xMin, "title buttons overlap (mode " + mode + ")");
                Require(c.xMax <= NativeMessengerWindow.SizeOf(mode).x - 5, "close button outside frame (mode " + mode + ")");
            }

            // 3) 滚动条与日志区（0x850b4c / 0x850943）
            Rect_(NativeMessengerWindow.Scroll(0), new Rect(273, 148, 15, 154), "scroll (mode 0)");
            Rect_(NativeMessengerWindow.Scroll(1), new Rect(273, 55, 15, 125), "scroll (mode 1)");
            Require(NativeMessengerWindow.Lines(0) == 11 && NativeMessengerWindow.Lines(1) == 9, "visible line count");
            Require(NativeMessengerWindow.Lines(0) * NativeMessengerWindow.LineHeight <= 152, "11 lines overflow the log box");
            Require(NativeMessengerWindow.Scroll(0).yMax == 302, "scrollbar bottom");

            // 4) 输入行：输入框 9..216，ENTER 249..288（不得压住输入框/白条）
            Rect_(NativeMessengerWindow.Edit(0), new Rect(9, 318, 207, 15), "edit (mode 0)");
            Rect_(NativeMessengerWindow.Enter(0), new Rect(249, 317, 39, 17), "enter (mode 0)");
            Rect_(NativeMessengerWindow.Enter(1), new Rect(249, 193, 39, 17), "enter (mode 1)");
            var edit = NativeMessengerWindow.Edit(0); var enter = NativeMessengerWindow.Enter(0);
            Require(edit.xMax <= enter.xMin, "ENTER overlaps the input field");
            Require(enter.xMin >= 246, "ENTER covers the white input box border (art box ends at 246)");
            Require(enter.xMax <= 288, "ENTER crosses the window frame");
            Require(Mathf.Abs(enter.yMin - 317) <= 1 && Mathf.Abs(edit.yMin - 318) <= 1, "ENTER/edit row misaligned");

            // 5) 三个座位：小人脚点 54/148/242 @118，名字条 9/103/197 @122，89x19
            for (int seat = 0; seat < 3; seat++)
            {
                var anchor = NativeMessengerWindow.SeatAnchor(seat);
                Require(anchor == new Vector2(54 + 94 * seat, 118), "seat anchor " + seat);
                Rect_(NativeMessengerWindow.SeatNameBar(seat), new Rect(9 + 94 * seat, 122, 89, 19), "name bar " + seat);
                var bar = NativeMessengerWindow.SeatNameBar(seat);
                Require(Mathf.Abs(anchor.x - bar.center.x) <= 1.5f, "avatar not centred on name bar " + seat);
                Require(anchor.y <= bar.yMin, "avatar feet sink into the name bar " + seat);
                Require(bar.xMax <= 288, "name bar outside frame " + seat);
            }
            for (int seat = 1; seat < 3; seat++)
                Require(NativeMessengerWindow.SeatNameBar(seat - 1).xMax < NativeMessengerWindow.SeatNameBar(seat).xMin,
                    "name bars overlap at seat " + seat);

            Debug.Log("[MessengerLayoutChecks] PASS 3 modes / title buttons y=6 / scroll 273,148,15,154 / enter 249,317 / 3 seats @94");
        }
        catch (Exception e) { Debug.LogException(e); exit = 1; }
        EditorApplication.Exit(exit);
    }
}
