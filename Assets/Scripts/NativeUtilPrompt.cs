using System;
using UnityEngine;

// CUtilDlg / compact CUtilDlgEx: native Notice3/4 slices and compact controls.
public static class NativeUtilPrompt
{
    const string OwnerKey = "NativeUtilPrompt";
    static string text, input;
    // 数值输入用 long：金币/经验已经超出 21 亿，若还用 int 做上下界，交易窗口就放不进全额金币。
    static long minimum, maximum;
    static int textMin, textMax;
    static Action<long> number;
    static Action<bool> answer;
    static Action<string> entered;
    public static bool Visible => number != null || answer != null || entered != null;
    public static bool Drawing { get; private set; }
    public static void Number(string prompt, int value, int min, int max, Action<int> done)
        => NumberLong(prompt, value, min, max, v => done?.Invoke((int)Math.Max(int.MinValue, Math.Min(int.MaxValue, v))));
    public static void NumberLong(string prompt, long value, long min, long max, Action<long> done)
    { Clear(); text = prompt; input = value.ToString(); minimum = min; maximum = max; number = done; }
    public static void Confirm(string prompt, Action<bool> done)
    { Clear(); text = prompt; answer = done; }
    public static void Text(string prompt, string value, int min, int max, Action<string> done)
    { Clear(); text = prompt; input = value ?? ""; textMin = min; textMax = max; entered = done; }
    public static void Clear() { number = null; answer = null; entered = null; text = input = null; NativeEditKeyboard.Close(); }
    public static void Cancel() { var callback = answer; Clear(); callback?.Invoke(false); }
    public static void Draw()
    {
        if (!Visible) return;
        // 每个 GUI 事件先读键盘快照再绘制/消费 Enter：Android 输入法最终合成可能与 Done/Enter
        // 同帧到达，Done 与回车同帧也只提交一次（沿用 BottomBar.DrawChat 的既有修正）。
        var keyboardSignal = NativeEditKeyboard.Sync(OwnerKey, ref input);
        Drawing = true;
        float scale = BottomBar.S;
        var matrix = GUI.matrix; var pointer = Pointer.Pos / scale;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            var style = new GUIStyle {font = UiKit.Font, fontSize = 12, wordWrap = true, padding = new RectOffset()};
            style.normal.textColor = Color.white;
            bool numeric = number != null;
            bool edit = numeric || entered != null;
            // CUtilDlgEx A4=1: measurement width 225, internal margin 2,
            // default font index 4 (white); the edit control has its own black font.
            var layout = edit ? DialogFlow.Measure(DialogText.Parse("#w" + text), 225, input: true) : null;
            float textHeight = edit ? layout.Height : Mathf.Ceil(style.CalcHeight(new GUIContent(text), 200));
            int lines = Mathf.Max(2, Mathf.FloorToInt(textHeight / 20) + 1);
            float height = edit ? textHeight + 99 : 55 + 21 + lines * 20;
            float x = (UiKit.W / scale - 266) / 2, y = (UiKit.H / scale - height) / 2;
            void Image(string path, float dy, float h)
            { var t = Art.Tex("wz://UI/Basic.img/" + path); if (t != null) GUI.DrawTexture(new Rect(x, y + dy, 266, h), t); }
            string slices = edit ? "Notice3" : "YesNo3";
            Image(slices + "/t", 0, 21);
            float bottom = edit ? 78 : 55;
            for (float dy = 21; dy < height - bottom; dy += 20) Image(slices + "/c", dy, 20);
            Image(edit ? "Notice4/s" : "YesNo3/s", height - bottom, bottom);
            if (edit)
            {
                foreach (var span in layout.Spans)
                {
                    var rect = new Rect(x + 20 + span.Rect.x, y + 22 + span.Rect.y, span.Rect.width, span.Rect.height);
                    if (span.Tex != null) GUI.DrawTexture(rect, span.Tex);
                    else
                    {
                        style.normal.textColor = span.Run.Color;
                        style.fontStyle = span.Run.Bold ? FontStyle.Bold : FontStyle.Normal;
                        GUI.Label(rect, span.Text, style);
                    }
                }
            }
            else GUI.Label(new Rect(x + 20, y + 22, 225, textHeight), text, style);
            bool submit = false;
            if (edit)
            {
                // CUtilDlgEx compact OnCreate (9A8716), not NPC-style 9A83F6.
                var editRect = new Rect(x + 19, y + height - 73, 226, 15);
                if (!numeric)
                {
                    UiKit.Fill(editRect, Color.black);
                    UiKit.Fill(new Rect(editRect.x+1,editRect.y+1,editRect.width-2,editRect.height-2), Color.white);
                }
                style.wordWrap = false;
                style.normal.textColor = Color.black;
                style.fontStyle = FontStyle.Normal;
                var contentRect = numeric ? editRect : new Rect(editRect.x+2,editRect.y+2,editRect.width-4,editRect.height-4);
                string value = GUI.TextField(contentRect, input, numeric ? 19 : textMax, style);
                if(numeric) { input = ""; foreach (char c in value) if (c >= '0' && c <= '9') input += c; }
                else input=value;
                // 点击输入框拉起系统键盘：数值用数字键盘、文本用普通键盘；上限与 GUI.TextField 一致。
                // 同一个输入框已开着键盘时绝不重开（Android 关掉再 Open 真会失败，见 LoginFlow 注释）。
                if (Event.current.type == EventType.Repaint && GUI.enabled && Pointer.Clicked && editRect.Contains(pointer))
                {
                    Pointer.ConsumeClick();
                    NativeEditKeyboard.Open(OwnerKey, input,
                        numeric ? TouchScreenKeyboardType.NumberPad : TouchScreenKeyboardType.Default,
                        numeric ? 19 : textMax);
                }
                submit = NativeUi.Button(new Rect(x + 158,y + height - 30,47,19),
                    "UI/Basic.img/BtOK2", pointer);
                if (NativeUi.Button(new Rect(x + 208,y + height - 30,47,19),
                    "UI/Basic.img/BtCancel2", pointer)) { Clear(); return; }
            }
            else
            {
                submit = NativeUi.Button(new Rect(x + 155,y + height - 30,47,19), "UI/Basic.img/BtOK2", pointer);
                if (NativeUi.Button(new Rect(x + 205,y + height - 30,47,19), "UI/Basic.img/BtCancel2", pointer))
                { Cancel(); return; }
            }
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Cancel(); e.Use(); return; }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { submit = true; e.Use(); }
            // 输入法 Done 与回车等价；同帧一起到达也只提交一次。
            // Canceled 只收键盘（Sync 内已 Close），弹窗保留，用户仍可点 OK/关闭。
            if (keyboardSignal == NativeEditKeyboard.Signal.Done) submit = true;
            if (!submit) return;
            if (numeric)
            {
                if (!long.TryParse(input, out long value) || value < minimum || value > maximum) return;
                var callback = number; Clear(); callback?.Invoke(value);
            }
            else if(entered!=null)
            {
                if(input.Length<textMin || input.Length>textMax) return;
                string value=input;var callback=entered;Clear();callback(value);
            }
            else { var callback = answer; Clear(); callback?.Invoke(true); }
        }
        finally { GUI.matrix = matrix; Drawing = false; }
    }
}
