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
    public static NativeChatChannel ChatChannel { get; private set; }
    static string whisperTarget = "";
    static readonly string[] ChatNames = { "所有人", "悄悄话", "组队", "好友", "家族", "联盟", "夫妻", "讯息" };
    public static void FocusChat() { ChatFocused = true; focusChat = true; }
    public static void FocusChat(NativeChatChannel channel, string target = "")
    { ChatChannel = channel; whisperTarget = target; FocusChat(); }
    public static void ResetChat()
    { ChatFocused = focusChat = false; chatInput = whisperTarget = ""; chatLines.Clear(); ChatChannel = NativeChatChannel.All; channelOpen = false; }
    static string chatInput = "";
    static readonly List<string> chatLines = new List<string>();
    public static IReadOnlyList<string> ChatHistory => chatLines;
    public static bool SubmitChat(string text, NativeChatChannel channel, string target = "")
    {
        var result = NativeSocialWindow.Request(SocialOperation.SendChat, target, text, channel);
        chatLines.Add(result.ok ? "[" + ChatNames[(int)channel] + "] " + Profile.playerName + " : " + text : result.error);
        if (chatLines.Count > 200) chatLines.RemoveAt(0);
        return result.ok;
    }

    static void DrawChat()
    {
        // 原版聊天区完全落在底板自带的白色条上（backgrnd2 的 y7..28，x2..568，正好 566 宽）：
        //   频道按钮 chatTarget(81×20) 贴左边 → 右边是输入行 → 行下沿压一条 base/chat 凹线。
        // 聊天记录不铺任何底板，白字黑描边直接压在画面上（原版就是这样，不做“聊天框”这种东西）。
        float y = UiKit.H - 64 * S;
        var e = Event.current;
        if (!NpcSession.Blocking && !ChatFocused && e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)
        { ChatFocused = true; focusChat = true; e.Use(); }

        // 频道按钮就是原版 base/chatTarget（81×20）——客户端 exe 的字符串池里直接引用的就是这个路径，
        // 是专门给聊天目标做的按钮皮肤。展开时按钮本身换成 ComboBox2/pressed 表示“按下”。
        var targetRect = new Rect(2 * S, y, 81 * S, 20 * S);
        bool overTarget = targetRect.Contains(Pointer.Pos);
        var comb = channelOpen ? (T("combo_pressed") ?? T("chatTarget_full")) : T("chatTarget_full");
        if (comb != null) GUI.DrawTexture(targetRect, comb, ScaleMode.StretchToFill);
        // 频道名：原版这张深蓝按钮上就是白字，这里用同一套字体居中
        ShadowLabel(targetRect, ChatNames[(int)ChatChannel], Style(11, Color.white, TextAnchor.MiddleCenter));
        if (!NpcSession.Blocking && Event.current.type == EventType.Repaint && Pointer.Clicked && overTarget)
        {
            channelOpen = !channelOpen;
            if (channelOpen) { ChatFocused = false; GUI.FocusControl(null); }
        }

        var style = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(11 * S) };
        style.normal.background = null;
        style.focused.background = null;
        style.normal.textColor = Color.black;
        // 白条内的可用宽度：x86..534（566 宽的白条，右边留 34 给滚动条/边距）
        float inputX = 86f, inputW = 448f;
        // 悄悄话：原版没有额外弹层，就是在同一行输入里先写收件人（To. 名字），后面接正文。
        // 之前把它放在底栏上面一行、黑字压在游戏画面上，既看不清也不是原版排法。
        if (ChatFocused && ChatChannel == NativeChatChannel.Whisper)
        {
            // 不加 padding 的纯文字标签：白条上的黑字，位置按原图坐标走
            var lab = new GUIStyle(GUI.skin.label)
            {
                fontSize = style.fontSize,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0)
            };
            lab.normal.textColor = Color.black;
            GUI.Label(new Rect(inputX * S, y, 18 * S, 20 * S), "To.", lab);
            GUI.SetNextControlName("NativeWhisper");
            whisperTarget = GUI.TextField(new Rect((inputX + 20f) * S, y, 118 * S, 20 * S), whisperTarget, 13, style);
            GUI.Label(new Rect((inputX + 142f) * S, y, 12 * S, 20 * S), ":", lab);
            inputX += 158f;
            inputW -= 158f;
        }
        GUI.SetNextControlName("NativeChat");
        if (ChatFocused) chatInput = GUI.TextField(new Rect(inputX * S, y, inputW * S, 20 * S), chatInput, 127, style);
        // 输入行下沿：base/chat 的 566×5 凹线，紧贴白色条的下边缘（底板内 y28）
        var groove = T("chat_full");
        if (groove != null) GUI.DrawTexture(new Rect(2 * S, UiKit.H - 43 * S, 566 * S, 5 * S), groove);
        if (focusChat && Event.current.type == EventType.Repaint)
        { GUI.FocusControl("NativeChat"); focusChat = false; }
        if (!NpcSession.Blocking && e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)
        {
            if (ChatFocused)
            {
                if (!string.IsNullOrWhiteSpace(chatInput))
                {
                    SubmitChat(chatInput, ChatChannel, whisperTarget);
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

        // 聊天记录：原版没有聊天框，就是几行带黑描边的白字压在画面上。
        int shown = Mathf.Min(5, chatLines.Count);
        if (shown > 0)
        {
            var text = Style(11, Color.white, TextAnchor.UpperLeft);
            float lineH = 14f * S;
            float top = UiKit.H - (74 + shown * 14) * S;
            for (int i = 0; i < shown; i++)
                ShadowLabel(new Rect(3 * S, top + i * lineH, 540 * S, lineH), chatLines[chatLines.Count - shown + i], text);
        }
        DrawChannelList(targetRect);
    }

    static bool channelOpen;
    // 频道下拉自己吃 Esc；InputBridge 靠这个属性让出这一次按键
    public static bool ChannelListOpen => channelOpen;

    // 频道下拉：原版同一张 ComboBox2（81×20）逐行拼成 8 个频道，按钮在下沿，列表往上展开。
    // 当前频道用 pressed 态、悬停用 mouseOver 态；全部是原素材，不上色、不画框。
    static void DrawChannelList(Rect anchor)
    {
        if (!channelOpen) return;
        int n = ChatNames.Length;
        float rowH = 20f * S;
        float top = anchor.y - rowH * n;
        var block = new Rect(anchor.x, top, anchor.width, rowH * n);

        for (int i = 0; i < n; i++)
        {
            var r = new Rect(anchor.x, top + i * rowH, anchor.width, rowH);
            bool over = r.Contains(Pointer.Pos);
            var tex = T("combo_" + (i == (int)ChatChannel ? "pressed" : over ? "mouseOver" : "normal")) ?? T("combo_normal");
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            ShadowLabel(r, ChatNames[i], Style(11, Color.white, TextAnchor.MiddleCenter));
            if (Event.current.type == EventType.Repaint && Pointer.Clicked && over)
            {
                ChatChannel = (NativeChatChannel)i;
                channelOpen = false;
                return;
            }
        }

        var e = Event.current;
        if (e.type == EventType.Repaint && Pointer.Clicked && !block.Contains(Pointer.Pos) && !anchor.Contains(Pointer.Pos))
            channelOpen = false;
        else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        { channelOpen = false; e.Use(); }
    }

    // 聊天区的文字沿用底板同一套字体；GUI.Label 只画字，不画任何底板/边框。
    static readonly GUIStyle chatStyle = new GUIStyle();

    static GUIStyle Style(int size, Color color, TextAnchor anchor)
    {
        chatStyle.font = UiKit.Font;
        chatStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S));
        chatStyle.alignment = anchor;
        chatStyle.normal.textColor = color;
        chatStyle.wordWrap = false;
        chatStyle.clipping = TextClipping.Clip;
        return chatStyle;
    }

    // 原版白字带黑描边：四向偏移各画一次黑字再画本字，不加底框。
    static void ShadowLabel(Rect r, string value, GUIStyle style)
    {
        var old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        float d = Mathf.Max(0.5f, S * 0.5f);
        GUI.Label(new Rect(r.x - d, r.y, r.width, r.height), value, style);
        GUI.Label(new Rect(r.x + d, r.y, r.width, r.height), value, style);
        GUI.Label(new Rect(r.x, r.y - d, r.width, r.height), value, style);
        GUI.Label(new Rect(r.x, r.y + d, r.width, r.height), value, style);
        style.normal.textColor = old;
        GUI.Label(r, value, style);
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
            if (!NpcSession.Blocking && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Event.current.type == EventType.Repaint && Pointer.Released && hover) PressBig(i);
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
            if (!NpcSession.Blocking && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Event.current.type == EventType.Repaint && Pointer.Released && hover) { PressSmall(i); }
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
                if (lv > 0) DrawSlotLevel(lv, r);
            }
            if (id == 0 && key != KeyCode.None)
            {
                var action = QuickBarKeys.Function(key);
                var attack = action.HasValue ? KeyConfigWindow.Icon(action.Value) : null;
                if (attack != null) GUI.DrawTexture(r, attack, ScaleMode.ScaleToFit);
            }
            // 键名必须跟随实际绑定：贴原版字形（KeyConfig/key/<扫描码>），压在格子左上角。
            // 原版键盘底图里所有键名都贴左上角，这里保持同一套参数；没有对应字形的键不画。
            var nameTex = NativeKeyLayout.NameTexture(key);
            if (nameTex != null)
                GUI.DrawTexture(new Rect(r.x + 1f * S, r.y + 1f * S,
                    nameTex.width * S, nameTex.height * S), nameTex);
            if (!NpcSession.Blocking && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Event.current.type == EventType.Repaint && Pointer.RightPressed && r.Contains(Pointer.Pos))
                { if (skillSlot >= 0) QuickSlots.Clear(skillSlot); }
            // 手上拿着技能时点快捷格 = 放下绑定（由 SkillWindow 处理），不再当作施放
            if (SkillWindow.Dragging)
            {
                if (Event.current.type == EventType.Repaint && Pointer.RightPressed && r.Contains(Pointer.Pos))
                    SkillWindow.CancelSkillDrag();
                continue;
            }
            if (!NpcSession.Blocking && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Event.current.type == EventType.Repaint && Pointer.Clicked && r.Contains(Pointer.Pos))
            {
                AudioManager.Play("UI/BtMouseClick", 0.7f, false);
                if (id > 0) QuickSlots.Cast(skillSlot);
                else if (key != KeyCode.None && key == KeyMap.Of(GameAction.Attack)) GameLoop.I.player.RequestAttack();
                else if (key != KeyCode.None) { InputHub.SetVirtual(key, true); InputHub.SetVirtual(key, false); }
            }
        }
    }

    // 技能等级：原版 StatusBar.img/number 的 5×7 点阵数字，右对齐贴格子右下角。
    // 之前这里用 GUI.Label 画文字，缩放后糊成一团，也不属于原版素材。
    static void DrawSlotLevel(int level, Rect cell)
    {
        string s = level.ToString();
        float x = cell.xMax - (s.Length * 6f - 1f) * S;
        float y = cell.yMax - 8f * S;
        for (int i = 0; i < s.Length; i++)
        {
            var t = Art.Tex("ui/statusbar/num_" + s[i] + ".bytes");
            if (t == null) continue;
            GUI.DrawTexture(new Rect(x + i * 6f * S, y, t.width * S, t.height * S), t);
        }
    }

}
