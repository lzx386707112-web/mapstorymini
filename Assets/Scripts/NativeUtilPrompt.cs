using System;
using UnityEngine;

// CUtilDlg / compact CUtilDlgEx: verified native Notice3/4 slices and edit rectangles.
public static class NativeUtilPrompt
{
    static string text, input;
    static int minimum, maximum;
    static Action<int> number;
    static Action<bool> answer;
    static Action<string> entered;
    public static bool Visible => number != null || answer != null || entered != null;
    public static bool Drawing { get; private set; }
    public static void Number(string prompt, int value, int min, int max, Action<int> done)
    { Clear(); text = prompt; input = value.ToString(); minimum = min; maximum = max; number = done; }
    public static void Confirm(string prompt, Action<bool> done)
    { Clear(); text = prompt; answer = done; }
    public static void Text(string prompt, string value, int min, int max, Action<string> done)
    { Clear(); text = prompt; input = value ?? ""; minimum = min; maximum = max; entered = done; }
    public static void Clear() { number = null; answer = null; entered = null; text = input = null; }
    public static void Cancel() { var callback = answer; Clear(); callback?.Invoke(false); }
    public static void Draw()
    {
        if (!Visible) return;
        Drawing = true;
        float scale = BottomBar.S;
        var matrix = GUI.matrix; var pointer = Pointer.Pos / scale;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            var style = new GUIStyle {font = UiKit.Font, fontSize = 12, wordWrap = true, padding = new RectOffset()};
            style.normal.textColor = Color.black;
            bool numeric = number != null;
            bool edit = numeric || entered != null;
            float textHeight = Mathf.Ceil(style.CalcHeight(new GUIContent(text), edit ? 225 : 200));
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
            GUI.Label(new Rect(x + 20, y + 22, 225, textHeight), text, style);
            bool submit = false;
            if (edit)
            {
                var editRect=numeric ? new Rect(x+19,y+textHeight+24,229,18) : new Rect(x+19,y+height-73,226,15);
                UiKit.Fill(editRect, Color.black);
                UiKit.Fill(new Rect(editRect.x+1,editRect.y+1,editRect.width-2,editRect.height-2), Color.white);
                style.wordWrap = false;
                var contentRect=numeric ? new Rect(x+22,y+textHeight+27,224,14) : new Rect(editRect.x+2,editRect.y+2,editRect.width-4,editRect.height-4);
                string value = GUI.TextField(contentRect, input, numeric ? 10 : maximum, style);
                if(numeric) { input = ""; foreach (char c in value) if (c >= '0' && c <= '9') input += c; }
                else input=value;
                submit = NativeUi.Button(numeric ? new Rect(x + 212,y + height - 26,43,19) : new Rect(x+158,y+height-30,47,19),
                    numeric ? "UI/UIWindow.img/UtilDlgEx/BtOK" : "UI/Basic.img/BtOK2", pointer);
                if (NativeUi.Button(numeric ? new Rect(x + 9,y + height - 26,43,19) : new Rect(x+208,y+height-30,47,19),
                    numeric ? "UI/UIWindow.img/UtilDlgEx/BtClose" : "UI/Basic.img/BtCancel2", pointer)) { Clear(); return; }
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
            if (!submit) return;
            if (numeric)
            {
                if (!int.TryParse(input, out int value) || value < minimum || value > maximum) return;
                var callback = number; Clear(); callback?.Invoke(value);
            }
            else if(entered!=null)
            {
                if(input.Length<minimum || input.Length>maximum) return;
                string value=input;var callback=entered;Clear();callback(value);
            }
            else { var callback = answer; Clear(); callback?.Invoke(true); }
        }
        finally { GUI.matrix = matrix; Drawing = false; }
    }
}
