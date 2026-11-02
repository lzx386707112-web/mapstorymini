using UnityEngine;

// 原端 CUtilDlgEx type3（普通宽）输入框：0x9ACD54 / 0x9A3CAE / 0x9A3DC1 核实
//   窗口 529×206，文本区最低 120；文本起点 (157,28)；
//   输入控件 (159,153) 宽 213 高 14；OK (475,180)，关闭 (9,180)。
// 边框沿用源端 FillRect 画法（1px 深色框 + 白底），与项目里其它 CUtilDlgEx 输入一致。
public static class NativeDlgInput
{
    const int W = 529, H = 206;
    const string OwnerKey = "NativeDlgInput";

    static string prompt, value;
    static int max;
    static System.Action<string> done;

    public static bool Visible => done != null;

    public static void Open(string text, string initial, int limit, System.Action<string> callback)
    {
        // 切到另一个输入框前先收掉旧键盘，避免旧文本被 Sync 灌进新框。
        NativeEditKeyboard.Close();
        prompt = text; value = initial ?? ""; max = Mathf.Max(1, limit); done = callback;
    }

    public static void Close() { done = null; prompt = value = null; max = 0; NativeEditKeyboard.Close(); }

    static void Style(out GUIStyle body)
    {
        body = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.RoundToInt(12 * BottomBar.S),
            wordWrap = true,
            padding = new RectOffset(),
        };
        body.normal.textColor = new Color(0.08f, 0.08f, 0.10f);
    }

    public static void Draw()
    {
        if (!Visible) return;
        // 每个 GUI 事件先读键盘快照，再绘制输入框/消费 Enter（Android 最终合成可能与 Done 同帧）。
        var keyboardSignal = NativeEditKeyboard.Sync(OwnerKey, ref value);
        float s = BottomBar.S;
        var rect = new Rect((UiKit.W - W * s) / 2f, (UiKit.H - H * s) / 2f, W * s, H * s);
        NativeUi.DialogPanel(rect);
        Style(out var body);

        GUI.Label(new Rect(rect.x + 157 * s, rect.y + 28 * s, 340 * s, 120 * s), prompt ?? "", body);

        // 输入框：(159,153) 213×14
        var field = new Rect(rect.x + 159 * s, rect.y + 153 * s, 213 * s, 14 * s);
        UiKit.Fill(field, new Color(0.25f, 0.27f, 0.32f));
        UiKit.Fill(new Rect(field.x + s, field.y + s, field.width - 2 * s, field.height - 2 * s), Color.white);
        var edit = new GUIStyle(body) { wordWrap = false };
        value = GUI.TextField(new Rect(field.x + 2 * s, field.y + s, field.width - 4 * s, field.height - 2 * s), value ?? "", max, edit);
        // 点击输入框拉起系统键盘（文本用普通键盘，上限与 GUI.TextField 一致）。
        // field 已经是屏幕像素坐标，直接用 Pointer.Pos 命中；同框已开着不重开。
        if (Event.current.type == EventType.Repaint && GUI.enabled && Pointer.Clicked && field.Contains(Pointer.Pos))
        {
            Pointer.ConsumeClick();
            NativeEditKeyboard.Open(OwnerKey, value, TouchScreenKeyboardType.Default, max);
        }

        bool submit = NativeUi.Button(new Rect(rect.x + 475 * s, rect.y + 180 * s, 43 * s, 19 * s), "UI/UIWindow.img/UtilDlgEx/BtOK");
        bool cancel = NativeUi.Button(new Rect(rect.x + 9 * s, rect.y + 180 * s, 43 * s, 19 * s), "UI/UIWindow.img/UtilDlgEx/BtClose");

        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { cancel = true; e.Use(); }
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { submit = true; e.Use(); }
        // 输入法 Done 与回车等价；同帧一起到达也只提交一次。Canceled 只收键盘，窗口保留。
        if (keyboardSignal == NativeEditKeyboard.Signal.Done) submit = true;

        if (cancel) { Close(); return; }
        if (!submit) return;
        string result = (value ?? "").Trim();
        if (result.Length == 0) return;
        var callback = done;
        Close();
        callback?.Invoke(result);
    }
}
