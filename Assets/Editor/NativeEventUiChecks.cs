using MapleDash.Wz;
using UnityEditor;
using UnityEngine;

// 事件倒计时 / 屏幕特效的「无渲染断言」。
//
// 交接文档要求：UI 优先写无渲染断言（LoginFlowChecks 那种：new 组件 + 反射调真实方法），
// 不要指望截图脚本 —— batchmode 下 WaitForEndOfFrame 不回调，截图协程会卡住。
// 这里验证的是「素材路径和帧时序确实对得上原 IMG」，画面观感仍要实机看。
//
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod NativeEventUiChecks.Run -logFile /tmp/event_ui.log
public static class NativeEventUiChecks
{
    static int pass, fail;
    static void Require(bool ok, string what)
    {
        if (ok) { pass++; return; }
        fail++;
        Debug.LogError("[EventUI] 失败：" + what);
    }
    static void Step(string what) => Debug.Log("[EventUI] 通过：" + what);

    public static void Run()
    {
        pass = fail = 0;
        try
        {
            CheckClockGlyphs();
            CheckClockState();
            CheckClearEffectFrames();
            CheckScreenEffectPlayback();
        }
        catch (System.Exception error)
        {
            fail++;
            Debug.LogError("[EventUI] 异常：" + error);
        }
        Debug.Log("[EventUI] 通过 " + pass + " 项，失败 " + fail + " 项");
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    // 倒计时字体：Map/Obj/etc.img/clock/fontTime（0-9 26x35、comma 17x35）。
    static void CheckClockGlyphs()
    {
        const string root = "Map/Obj/etc.img/clock/fontTime/";
        Require(WzAssets.HasFile("Map/Obj/etc.img"), "Map/Obj/etc.img 在 Data 目录里");
        for (int digit = 0; digit <= 9; digit++)
        {
            var node = WzAssets.Node(root + digit);
            var canvas = node == null ? null : WzAssets.Canvas(node);
            Require(canvas != null && canvas.Width == 26 && canvas.Height == 35,
                "fontTime/" + digit + " 是 26x35，实际 " + (canvas == null ? "缺失" : canvas.Width + "x" + canvas.Height));
        }
        var comma = WzAssets.Node(root + "comma");
        var commaCanvas = comma == null ? null : WzAssets.Canvas(comma);
        Require(commaCanvas != null && commaCanvas.Width == 17 && commaCanvas.Height == 35,
            "fontTime/comma（冒号）是 17x35，实际 " + (commaCanvas == null ? "缺失" : commaCanvas.Width + "x" + commaCanvas.Height));
        Step("倒计时字体：0-9 + 冒号共 11 个图块，尺寸与原 IMG 一致");
    }

    // 倒计时状态机：startEventTimer 开始、stopEventTimer 清零。
    static void CheckClockState()
    {
        NativeEventClock.Hide();
        Require(!NativeEventClock.Visible, "初始不显示倒计时");
        NativeEventClock.Show(125);
        Require(NativeEventClock.Visible, "Show(125) 之后可见");
        NativeEventClock.Hide();
        Require(!NativeEventClock.Visible, "Hide() 之后不可见");
        NativeEventClock.Show(0);
        Require(!NativeEventClock.Visible, "Show(0) 等同于隐藏（源端没有 0 秒计时器）");
        Step("倒计时状态机：Show/Hide/0 秒");
    }

    // clear 特效：Map/Effect.img/quest/party/clear 的 4 帧与各自 delay。
    static void CheckClearEffectFrames()
    {
        var node = WzAssets.Node("Map/Effect.img/quest/party/clear");
        Require(node != null, "quest/party/clear 存在");
        var frames = node == null ? null : WzAssets.Frames(node);
        Require(frames != null && frames.Count == 4, "clear 有 4 帧，实际 " + (frames == null ? "缺失" : frames.Count.ToString()));
        if (frames == null || frames.Count != 4) return;
        int[] delays = { 130, 130, 130, 2000 };
        for (int i = 0; i < 4; i++)
        {
            int delay = frames[i].Int("delay", -1);
            Require(delay == delays[i], "clear 第 " + i + " 帧 delay=" + delays[i] + "，实际 " + delay);
            var texture = WzAssets.Texture(frames[i]);
            Require(texture != null && texture.width > 0, "clear 第 " + i + " 帧能解出纹理");
        }
        // 每帧自带 origin，播放时按锚点对齐；这里确认它们不是全 0（否则说明读错了节点）。
        var origin = WzAssets.Origin(frames[0]);
        Require(origin.x > 0f && origin.y > 0f, "clear 第 0 帧 origin 非零，实际 " + origin);
        Step("clear 特效：4 帧 / 130,130,130,2000ms / origin 可用");
    }

    static void CheckScreenEffectPlayback()
    {
        NativeScreenEffect.Clear();
        Require(NativeScreenEffect.ActiveCount == 0, "清空后没有播放中的特效");
        NativeScreenEffect.Play("Map/Effect.img/quest/party/clear");
        Require(NativeScreenEffect.ActiveCount == 1, "Play 之后登记了 1 个特效，实际 " + NativeScreenEffect.ActiveCount);
        NativeScreenEffect.Play("Map/Effect.img/quest/party/clear");
        Require(NativeScreenEffect.ActiveCount == 2, "连续两次 clear 会叠两段（未清场时重复触发不丢）");
        NativeScreenEffect.Clear();
        Require(NativeScreenEffect.ActiveCount == 0, "Clear 之后清空");
        // 路径不存在时静默跳过，不能抛（事件脚本不该因为缺素材中断）。
        NativeScreenEffect.Play("Map/Effect.img/quest/party/not-a-real-effect");
        Require(NativeScreenEffect.ActiveCount == 0, "不存在的特效路径被静默忽略");
        Step("屏幕特效播放队列：登记 / 叠加 / 清空 / 缺素材不抛");
    }
}
