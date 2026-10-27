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

    // ————— 原端底栏几何（BeiDou.exe StatusBar::OnCreate 0x008D2FA6 起） —————
    // 客户区 1281×578（快捷栏 815+466=1281 正好顶到右缘），底栏 817×71 贴底：
    //   base/backgrnd   817×71 画在 (0,507)
    //   base/backgrnd2  570×71 画在 (2,507)   ← 左移 2px，白条与底板都跟着偏 2
    //   大按钮 54×34  x=573+56i  y=543 → 底栏内 y36
    //   小按钮 28×20  x=618+30i  y=515 → 底栏内 y8
    //   通知槽 base/box 42×19 at (573,515)，左槽叠 BtClaim 20×19
    //   快捷栏 466×80  at (815, 579-80=499)
    const int BarH = 71;
    // backgrnd2（含白条与深色状态底板）在原端画在 x=2，状态栏底板必须跟着偏 2，
    // 否则 LV 铭牌、三条槽会和底图错开 2px。
    public const int PlateX = 2;
    const int BigW = 54, BigH = 34, SmallW = 28, SmallH = 20;
    const int BigTop = 35, SmallTop = 63;          // 578-543 / 578-515
    const int BoxX = 573, BoxW = 42, BoxH = 19;    // base/box 两个白槽
    const int ClaimW = 20, ClaimH = 19;            // BtClaim 压左槽
    const int MemoX = 599, MemoY = 519;            // 右槽里的 iconMemo
    const int QuickW = 466, QuickH = 80;
    // DLL 0x10036108: 13 columns, 35px pitch; native icon/hit size 32x32.

    // 底图 base/quickSlot 的格子网格是逐像素量出来的：列 x = 10 + 35i，行 y = 10 / 44，格子 27×24。
    // 图标 32×32 比格子大，按原端居中压上去（左上 = 格心 − 16 → x 7.5、y 6 / 40）。
    static readonly int[] CellY = { 10, 44 };
    const int CellW = 27, CellH = 24;     // 底图里白格的实际尺寸
    const int IconW = 32, IconH = 32;     // 原端图标/命中尺寸

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
        => new Rect((573 + index * 56) * S, UiKit.H - BigTop * S, BigW * S, BigH * S);

    public static Rect SmallButtonRect(int index)
        => new Rect((618 + index * 30) * S, UiKit.H - SmallTop * S, SmallW * S, SmallH * S);

    // 两个白色通知槽（base/box 42×19）在 (573,515)：有未读/可领取时才出现，默认不画。
    public static Rect NotifyBoxRect()
        => new Rect(BoxX * S, UiKit.H - SmallTop * S, BoxW * S, BoxH * S);

    // BtClaim 压在左槽上，只有存在可领取道具时原端才显示。
    public static Rect ClaimRect()
        => new Rect(BoxX * S, UiKit.H - SmallTop * S, ClaimW * S, ClaimH * S);

    public static Rect QuickPanelRect()
        => new Rect(815 * S, UiKit.H - 79 * S, QuickW * S, QuickH * S);

    // 图标/命中框：32×32，居中压在 27×24 的白格上（所以左上比格子各多出 2.5 / 4 px）
    public static Rect SlotRect(int i)
    {
        if (collapsed || i < 0 || i >= QuickSlots.Count) return Rect.zero;
        var panel = QuickPanelRect();
        return new Rect(panel.x + (7.5f + i % 13 * 35) * S,
            panel.y + (CellY[i / 13] - 4) * S, IconW * S, IconH * S);
    }

    // 白格本体（27×24）：键名字形贴的就是这个矩形的左上角，不能贴到图标框上，
    // 否则键名会比原端偏左上 2~3px（源端截图实测偏移量）。
    public static Rect CellRect(int i)
    {
        var panel = QuickPanelRect();
        return new Rect(panel.x + (10 + i % 13 * 35) * S,
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
        // 通知槽 (573,515)：两个白槽常驻，左槽是领取按钮，右槽是便签图标
        DrawNotifyBox();
        DrawBigButtons();
        if (!collapsed) DrawQuickSlots();
        // 小图标为独立入口，放在聊天条右侧，不与展开的快捷格重叠。
        DrawSmallButtons();
        DrawClaim();
    }

    static void DrawPlate()
    {
        var col = T("plate_col");
        if (col == null) return;
        // 左段 570 由 StatusBar 自己那张底板画（它上面还有 LV 铭牌的渐变），
        // 这里只铺右边剩下的部分，避免同一段画两遍出现接缝。
        var full = T("backgrnd");
        if (full != null) GUI.DrawTexture(new Rect(0, UiKit.H - BarH * S, 817 * S, BarH * S), full);
        if (UiKit.W > 817 * S) GUI.DrawTexture(new Rect(817 * S, UiKit.H - BarH * S, UiKit.W - 817 * S, BarH * S), col);
        // backgrnd2 在原端是画在 (2,507)：白条与深色底板整体右移 2px，
        // 右缘落在 572，正好让 (573,515) 的通知槽和 (573,543) 的商城按钮接上。
        var left = T("backgrnd2_full");
        if (left != null) GUI.DrawTexture(new Rect(PlateX * S, UiKit.H - BarH * S, 570 * S, BarH * S), left);
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

    // —————— 聊天窗消息 ——————
    // 源端聊天窗不是「一串纯白字符串」：每条消息带类型，按类型上色（见 NativeSystemMessage）。
    // 颜色证据：type5 粉色取自 10-27 截图实测字形像素（R:G:B ≈ 255:209:207）；
    // type0/6 的浅蓝没有截图，按服务端 PacketCreator.serverNotice 的类型名 Lightblue 取值，待核实。
    public struct ChatLine
    {
        public string text;
        public Color color;
    }
    public static readonly Color ChatWhite = Color.white;
    public static readonly Color ChatPink = new Color32(255, 208, 208, 255);
    public static readonly Color ChatLightBlue = new Color32(170, 215, 255, 255);

    static readonly List<ChatLine> chatLines = new List<ChatLine>();
    public static IReadOnlyList<ChatLine> ChatHistory => chatLines;

    public static void AddChat(string text, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        chatLines.Add(new ChatLine { text = text, color = color });
        if (chatLines.Count > 200) chatLines.RemoveAt(0);
    }

    public static bool SubmitChat(string text, NativeChatChannel channel, string target = "")
    {
        var result = NativeSocialWindow.Request(SocialOperation.SendChat, target, text, channel);
        // 源端发送失败走 serverNotice(5, …)：聊天窗粉色，不是浮层提示。
        if (result.ok) AddChat("[" + ChatNames[(int)channel] + "] " + Profile.playerName + " : " + text, ChatWhite);
        else AddChat(result.error, ChatPink);
        return result.ok;
    }

    static void DrawChat()
    {
        // 原版聊天行有两种状态，源端两张截图各拍到一种：
        //   收起态：白条被一层深色底压住（实测 RGB≈(124,117,116)，等于白底上约 55% 黑），
        //           最后一条聊天记录用浅色字压在上面；**没有**频道按钮。点条即进入输入态。
        //   输入态：频道按钮 base/chatTarget(81×20) 贴左边 → 右边是白色输入行 → 行下沿压一条 base/chat 凹线。
        // 聊天记录不铺任何底板，白字黑描边直接压在画面上（原版就是这样，不做“聊天框”这种东西）。
        float y = UiKit.H - 64 * S;
        var e = Event.current;
        if (!NpcSession.Blocking && !ChatFocused && e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)
        { ChatFocused = true; focusChat = true; e.Use(); }

        var strip = new Rect(2 * S, y, 566 * S, 20 * S);
        var targetRect = new Rect(2 * S, y, 81 * S, 20 * S);
        // 频道下拉展开时 ChatFocused 会被置 false，但按钮必须留着，所以按 channelOpen 一起判断。
        if (ChatFocused || channelOpen) DrawChatInput(y, targetRect, strip);
        else DrawCollapsedChat(strip);

        // 输入行下沿：base/chat 的 566×5 凹线，紧贴白色条的下边缘（底板内 y28）
        var groove = T("chat_full");
        if (groove != null) GUI.DrawTexture(new Rect(2 * S, UiKit.H - 43 * S, 566 * S, 5 * S), groove);

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

        // 聊天记录：原版没有聊天框，就是几行带黑描边的彩色字压在画面上，颜色跟着消息类型走。
        int shown = Mathf.Min(5, chatLines.Count);
        if (shown > 0)
        {
            float lineH = 14f * S;
            float top = UiKit.H - (74 + shown * 14) * S;
            for (int i = 0; i < shown; i++)
            {
                var line = chatLines[chatLines.Count - shown + i];
                ShadowLabel(new Rect(3 * S, top + i * lineH, 540 * S, lineH), line.text,
                    Style(11, line.color, TextAnchor.UpperLeft));
            }
        }
        DrawChannelList(targetRect);
    }

    // 收起态：深色底 + 最后一条聊天记录（浅色字），左边没有频道按钮。
    // 底色的 0.51 黑是从源端截图反推的：白条 253 → 实测 124，253×(1-0.51)≈124。
    static void DrawCollapsedChat(Rect strip)
    {
        UiKit.Fill(strip, new Color(0f, 0f, 0f, 0.51f));
        if (chatLines.Count > 0)
        {
            var last = chatLines[chatLines.Count - 1];
            var text = Style(11, last.color, TextAnchor.MiddleLeft);
            GUI.Label(new Rect(strip.x + 4f * S, strip.y, strip.width - 8f * S, strip.height),
                last.text, text);
        }
        if (!NpcSession.Blocking && Event.current.type == EventType.Repaint && Pointer.Clicked && strip.Contains(Pointer.Pos))
            FocusChat();
    }

    // 输入态：频道按钮 + 白色输入行（白条是 backgrnd2 自带的，这里只画字）。
    static void DrawChatInput(float y, Rect targetRect, Rect strip)
    {
        // 频道按钮就是原版 base/chatTarget（81×20）——客户端 exe 的字符串池里直接引用的就是这个路径，
        // 是专门给聊天目标做的按钮皮肤。展开时按钮本身换成 ComboBox2/pressed 表示“按下”。
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

        // 输入行必须完全透明：白条是 backgrnd2 自带的，任何一态的默认背景都会把它盖成深灰。
        // 不能从 GUI.skin.textField 复制——那是 Unity 深色皮肤的 #2A2A2A 圆角九宫格（实测条内正好 42,42,42），
        // 复制过来后即使把 8 个状态的 background 置空，皮肤态仍会回落出深色底。
        // 直接新建空 GUIStyle：任何状态都没有底图，只留字体与颜色。
        var style = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(8, Mathf.RoundToInt(11 * S)),
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0),
            border = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };
        style.normal.textColor = Color.black;
        style.focused.textColor = Color.black;
        style.hover.textColor = Color.black;
        style.active.textColor = Color.black;
        // 白条内的可用宽度：白条本体在底板内 x2..565（backgrnd2 的白区 564 宽），
        // 频道按钮占 x2..83，输入行从 x86 一直排到白条右端，不再提前 34px 收尾。
        float inputX = 86f, inputW = 479f;
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
        if (focusChat && Event.current.type == EventType.Repaint)
        { GUI.FocusControl("NativeChat"); focusChat = false; }
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

    // ————— (573,515) 两个白色通知槽 —————
    // 原端：base/box（42×19，两枚 19×19 白色圆角槽，中缝 2px）常驻画在这里；
    // 左槽叠 BtClaim（20×19，红圆顶+黄底座），右槽放便签图标 base/iconMemo（14×11）于 (599,519)。
    // 源端截图逐像素核对：左槽图标 = BtClaim/normal/0，右槽图标 = iconMemo，两者都常驻。
    static void DrawNotifyBox()
    {
        var box = T("box_full");
        if (box != null) GUI.DrawTexture(NotifyBoxRect(), box, ScaleMode.StretchToFill);

        // 便签图标贴在右槽（595..613）里：14×11 画在 (599,519)，正好落在白槽内。
        // 未读数用的 FontMemo 点阵在原端不是这个绘制函数画的（该函数只有 box 与 iconMemo 两次绘制），
        // 位置未经核实，这里不编造，只按已核实坐标画图标。
        var icon = Art.Tex("ui/statusbar/iconMemo.bytes");
        if (icon == null) return;
        GUI.DrawTexture(new Rect(MemoX * S, UiKit.H - (578 - MemoY) * S, icon.width * S, icon.height * S),
            icon, ScaleMode.StretchToFill);
    }

    // 领取按钮：原端创建在 (573,515) 20×19，四态素材齐全，压在左槽上常驻显示。
    static void DrawClaim()
    {
        var r = ClaimRect();
        bool hover = r.Contains(Pointer.Pos);
        string state = Pointer.Down && hover ? "pressed" : hover ? "mouseOver" : "normal";
        var t = T("BtClaim_" + state) ?? T("BtClaim_normal");
        if (t != null) GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
        if (!NpcSession.Blocking && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer &&
            Event.current.type == EventType.Repaint && Pointer.Released && hover)
        {
            AudioManager.Play("UI/BtMouseClick", 0.7f, false);
            Toast("暂无可领取的道具");
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
            // 空格子在原端就是纯白底 + 左上角键名，不画任何功能图标。
            // 之前这里把 KeyConfig 的功能图标（"攻击"等文字贴图）铺进空格，
            // 结果 Ctrl 格上叠了一行红字，源端没有这个元素。
            // 键名必须跟随实际绑定：贴原版字形（KeyConfig/key/<扫描码>），压在格子左上角。
            // 原版键盘底图里所有键名都贴左上角，这里保持同一套参数；没有对应字形的键不画。
            var nameTex = NativeKeyLayout.NameTexture(key);
            if (nameTex != null)
            {
                var cell = CellRect(i);
                GUI.DrawTexture(new Rect(cell.x, cell.y,
                    nameTex.width * S, nameTex.height * S), nameTex);
            }
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
