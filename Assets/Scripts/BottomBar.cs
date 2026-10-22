using System.Collections.Generic;
using UnityEngine;

// Expanded client: fixed left groups, bottom anchored, 26 display keys.
// Coordinates verified against BeiDou.exe and ijl15.dll; see docs/original-hud-port.md.
public static class BottomBar
{
    public const float NativeWidth = 1281f;
    // 本项目按视口整体适配；允许高分辨率放大，内部仍使用原端坐标。
    public static float ScaleFor(float width, float height) => Mathf.Min(width / NativeWidth, height / 600f);
    public static float S => ScaleFor(Screen.width, Screen.height) / UiKit.Scale;
    public static float Height => 35f * S;

    // 原图像素常量
    const int BigW = 54, BigH = 34, SmallW = 28, SmallH = 20;
    const int QuickW = 466, QuickH = 80;
    // DLL 0x10036108: 13 columns, 35px pitch; native icon/hit size 32x32.

    static readonly int[] CellY = { 8, 41 };
    const int CellW = 32, CellH = 32;

    static readonly Dictionary<string, Texture2D> tex = new Dictionary<string, Texture2D>();
    static bool collapsed;
    public static bool InterfaceVisible;

    static Texture2D T(string name)
    {
        if (tex.TryGetValue(name, out var t)) return t;
        t = Art.Tex("ui/bar/" + name + ".bytes");
        tex[name] = t;
        return t;
    }

    static string toast;
    static float toastUntil;
    public static void Toast(string text) { toast = text; toastUntil = Time.unscaledTime + 1.8f; }
    public static string ActiveToast => Time.unscaledTime < toastUntil ? toast : null;

    // —————————— 几何 ——————————
    public static Rect BigButtonRect(int index)
        => new Rect((573 + index * 56) * S, UiKit.H - BigH * S, BigW * S, BigH * S);

    public static Rect SmallButtonRect(int index)
        => new Rect((618 + index * 30) * S, UiKit.H - 62 * S, SmallW * S, SmallH * S);

    public static Rect QuickPanelRect()
        => new Rect(815 * S, UiKit.H - 79 * S, QuickW * S, QuickH * S);

    public static Rect SlotRect(int i)
    {
        if (collapsed || i < 0 || i >= QuickSlots.Count) return Rect.zero;
        var panel = QuickPanelRect();
        return new Rect(panel.x + (7 + i % 13 * 35) * S,
            panel.y + CellY[i / 13] * S, CellW * S, CellH * S);
    }

    public static bool QuickCollapsed
    {
        get { return collapsed; }
        set { collapsed = value; }
    }

    // —————————— 绘制 ——————————
    public static void Draw()
    {
        if (GameLoop.I == null || GameLoop.I.player == null) return;
        if (GameLoop.I.state != GameLoop.GameState.Playing) return;

        DrawPlate();
        // 左段（LV/名字/HP/MP/EXP）画在底板之上；顺序反了会被整条底板盖掉
        StatusBar.Draw();
        DrawBigButtons();
        if (!collapsed) DrawQuickSlots();
        // 小图标为独立入口，放在聊天条右侧，不与展开的快捷格重叠。
        DrawSmallButtons();
        DrawToast();
    }

    static void DrawPlate()
    {
        var col = T("plate_col");
        if (col == null) return;
        // 左段 570 由 StatusBar 自己那张底板画（它上面还有 LV 铭牌的渐变），
        // 这里只铺右边剩下的部分，避免同一段画两遍出现接缝。
        var full = T("backgrnd");
        if (full != null) GUI.DrawTexture(new Rect(0, UiKit.H - 71 * S, 817 * S, 71 * S), full);
        if (UiKit.W > 817 * S) GUI.DrawTexture(new Rect(817 * S, UiKit.H - 71 * S, UiKit.W - 817 * S, 71 * S), col);
        var left = T("backgrnd2_full");
        if (left != null) GUI.DrawTexture(new Rect(0, UiKit.H - 71 * S, 570 * S, 71 * S), left);
        DrawChat();
    }

    public static bool ChatFocused;
    static bool focusChat;
    static string chatInput = "";
    static readonly List<string> chatLines = new List<string>();

    static void DrawChat()
    {
        float y = UiKit.H - 64 * S;
        var e = Event.current;
        if (!ChatFocused && e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)
        { ChatFocused = true; focusChat = true; e.Use(); }
        var target = T("chatTarget_full");
        if (ChatFocused && target != null) GUI.DrawTexture(new Rect(2 * S, y, 81 * S, 20 * S), target);
        var style = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(11 * S) };
        style.normal.background = null;
        style.focused.background = null;
        style.normal.textColor = Color.black;
        GUI.SetNextControlName("NativeChat");
        if (ChatFocused) chatInput = GUI.TextField(new Rect(86 * S, y, 448 * S, 20 * S), chatInput, 160, style);
        if (focusChat && Event.current.type == EventType.Repaint)
        { GUI.FocusControl("NativeChat"); focusChat = false; }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)
        {
            if (ChatFocused)
            {
                if (!string.IsNullOrWhiteSpace(chatInput))
                {
                    chatLines.Add(Profile.playerName + " : " + chatInput);
                    if (chatLines.Count > 5) chatLines.RemoveAt(0);
                }
                chatInput = "";
                GUI.FocusControl(null);
                ChatFocused = false;
            }
            else { GUI.FocusControl("NativeChat"); ChatFocused = true; }
            e.Use();
        }
        if (ChatFocused && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        { GUI.FocusControl(null); ChatFocused = false; e.Use(); }
        if (chatLines.Count == 0) return;
        var r = new Rect(3 * S, UiKit.H - (74 + chatLines.Count * 14) * S, 540 * S, chatLines.Count * 14 * S);
        NativeUi.Panel(r);
        var text = new GUIStyle { fontSize = Mathf.RoundToInt(11 * S) };
        text.normal.textColor = Color.black;
        for (int i = 0; i < chatLines.Count; i++)
            GUI.Label(new Rect(r.x + 3 * S, r.y + i * 14 * S, r.width, 14 * S), chatLines[i], text);
    }

    static void DrawBigButtons()
    {
        string[] keys = { "BtShop", "BtNPT", "BtMenu", "BtShort" };
        for (int i = 0; i < keys.Length; i++)
        {
            var r = BigButtonRect(i);
            bool hover = r.Contains(Pointer.Pos);
            string state = Pointer.Down && hover ? "pressed" : hover ? "mouseOver" : "normal";
            var t = T(keys[i] + "_" + state) ?? T(keys[i] + "_normal");
            if (t != null) GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
            if (!KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Event.current.type == EventType.Repaint && Pointer.Released && hover) PressBig(i);
        }
    }

    static void PressBig(int i)
    {
        switch (i)
        {
            case 0: ShopWindow.Toggle(); break;
            case 1: HelpWindow.Toggle(); break;
            case 2: GameMenuWindow.Toggle(); break;
            case 3: InterfaceVisible = !InterfaceVisible; break;
        }
    }

    static void DrawSmallButtons()
    {
        string[] keys = { "EquipKey", "InvenKey", "StatKey", "SkillKey", "KeySet", collapsed ? "QuickSlot" : "QuickSlotD" };
        for (int i = 0; i < keys.Length; i++)
        {
            var r = SmallButtonRect(i);
            bool hover = r.Contains(Pointer.Pos);
            string state = Pointer.Down && hover ? "pressed" : hover ? "mouseOver" : "normal";
            var t = T(keys[i] + "_" + state) ?? T(keys[i] + "_normal");
            if (t != null) GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
            if (!KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Event.current.type == EventType.Repaint && Pointer.Released && hover) { PressSmall(i); }
        }
    }

    static void PressSmall(int i)
    {
        switch (i)
        {
            case 0: EquipWindow.Toggle(); break;
            case 1: InvenWindow.Toggle(); break;
            case 2: StatWindow.Toggle(); break;
            case 3: SkillWindow.Toggle(); break;
            case 4: KeyConfigWindow.Toggle(); break;
            case 5: collapsed = !collapsed; break;
        }
    }

    // Display keys query the same key-to-function map as physical input.

    static void DrawQuickSlots()
    {
        var panel = QuickPanelRect();
        var bg = T("quickSlot26");
        if (bg != null) GUI.DrawTexture(panel, bg, ScaleMode.StretchToFill);

        for (int i = 0; i < QuickSlots.Count; i++)
        {
            var r = SlotRect(i);
            var key = QuickBarKeys.Of(i);
            int skillSlot = QuickBarKeys.SkillSlot(key);
            int id = skillSlot >= 0 ? QuickSlots.Id(skillSlot) : 0;
            var data = id > 0 ? SkillBook.Get(id) : null;
            if (data != null)
            {
                var icon = string.IsNullOrEmpty(data.icon) ? null : Art.Tex(data.icon);
                if (icon != null) GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
                int lv = SkillProgress.LevelOf(id);
                var sys = GameLoop.I.player != null ? GameLoop.I.player.GetComponent<SkillSystem>() : null;
                float left = sys != null ? sys.RemainingById(id) : 0f;
                if (left > 0f)
                {
                    float k = Mathf.Clamp01(left / Mathf.Max(0.01f, sys.CooldownOf(id)));
                    var cool = Art.Tex("ui/skillwin/cool_" + Mathf.Clamp(Mathf.FloorToInt(k * 16), 0, 15) + ".png");
                    if (cool != null) GUI.DrawTexture(r, cool);
                }
                if (lv > 0)
                {
                    var st = new GUIStyle { fontSize = Mathf.RoundToInt(9f * S), alignment = TextAnchor.LowerRight };
                    st.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
                    GUI.Label(new Rect(r.x, r.y, r.width - 1, r.height - 1), lv.ToString(), st);
                }
            }
            // 键名必须跟随实际绑定；标签靠上沿，不能盖在技能图标正中央。

            var label = new GUIStyle { fontSize = Mathf.Max(8, Mathf.RoundToInt(8f * S)), alignment = TextAnchor.UpperLeft };
            label.normal.textColor = new Color(0.15f, 0.17f, 0.22f);
            GUI.Label(new Rect(r.x, r.y - 2 * S, r.width, 12 * S), KeyMap.Label(key), label);
            if (id == 0 && key != KeyCode.None)
            {
                var action = QuickBarKeys.Function(key);
                var attack = action.HasValue ? KeyConfigWindow.Icon(action.Value) : null;
                if (attack != null) GUI.DrawTexture(r, attack, ScaleMode.ScaleToFit);
            }
            if (!KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Event.current.type == EventType.Repaint && Pointer.RightPressed && r.Contains(Pointer.Pos))
                { if (skillSlot >= 0) QuickSlots.Clear(skillSlot); }
            if (!KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Event.current.type == EventType.Repaint && Pointer.Clicked && r.Contains(Pointer.Pos))
            {
                if (id > 0) QuickSlots.Cast(skillSlot);
                else if (key != KeyCode.None && key == KeyMap.Of(GameAction.Attack)) GameLoop.I.player.RequestAttack();
                else if (key != KeyCode.None) { InputHub.SetVirtual(key, true); InputHub.SetVirtual(key, false); }
            }
        }
    }

    static void DrawToast()
    {
        string text = ActiveToast;
        if (string.IsNullOrEmpty(text)) return;
        var st = new GUIStyle { fontSize = Mathf.RoundToInt(13f * S), alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(1f, 0.95f, 0.72f);
        var r = new Rect(UiKit.W * 0.18f, UiKit.H - Height - 30f * S, UiKit.W * 0.64f, 22f * S);
        NativeUi.Panel(new Rect(r.x - 6, r.y - 2, r.width + 12, r.height + 4));
        GUI.Label(r, text, st);
    }
}
