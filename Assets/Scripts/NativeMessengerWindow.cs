using System;
using System.Collections.Generic;
using UnityEngine;

// 聊天窗（UI/UIWindow.img/Messenger，源端 CUIMessenger）。
// 全部几何取自 BeiDou.exe（SHA256 1b91cf5b…34b2）：
//   0x850264 SetMode 尺寸表   0x8505e2 控件创建（坐标按 cdecl 压栈顺序 x,y,…）
//   0x84f44b 座位/名字条      0x84fff8 座位状态
// 迁移边界：只还原 mode 0（295x364 完整态）为验收目标；mode 1/2 的坐标同样照抄，
// 但 mode 1 的 392px 逻辑宽配 295px 底图是源端原样（CWnd::Draw 0x9E0502 不拉伸），
// 未做二次修图。
public static class NativeMessengerWindow
{
    // —————— 源端常量 ——————
    public const int LineHeight = 13;        // 日志行高（0x8505e2：13px 行距）
    public const int LogLineWidth = 250;     // [ctrl+0x34]=0xfa（0x85096d / 0x850b83）
    public const int SeatPitch = 94;         // 0x84f49d imul eax,eax,0x5e
    public const int SeatFirst = 54;         // 0x84f4a7 add eax,0x36
    public const int SeatAvatarY = 118;      // 0x84f4a5 push 0x76（脚点）
    public const int NameBarX = 9;           // 名字条左沿
    public const int NameBarY = 122;         // 名字条上沿（与底图空位灰条下缘对齐）
    public const int NameBarWidth = 89;      // Messenger/NameBar/%d 画布尺寸
    public const int NameBarHeight = 19;

    static readonly Vector2[] Sizes =
    {
        new Vector2(295, 364),   // 0 完整态 0x85032a：0x127 x 0x16c
        new Vector2(392, 240),   // 1 宽态   0x85030d：0x188 x 0x0f0
        new Vector2(184, 22),    // 2 收起态 0x8502f0：0x0b8 x 0x016
    };

    public static Vector2 SizeOf(int mode) => Sizes[Mathf.Clamp(mode, 0, 2)];

    // 标题栏三键：关闭 276、最小化 248、最大化 262（0x8506b3 id1000→0x114=276、
    // 0x850759 id1001→0xf8=248、0x850814 id1002→0x106=262）；收起态各减 110 → 166/138/152。
    // y=6（0x85060f：add eax,5，mode!=2 时再 +1），收起态 5。三键同尺寸 12x12，间距 14。
    public static Rect TitleButton(int index, int mode)
    {
        bool collapsed = mode == 2;
        float closeX = collapsed ? 166 : 276;
        float x = index == 0 ? closeX : index == 1 ? closeX - 28 : closeX - 14;
        return new Rect(x, collapsed ? 5 : 6, 12, 12);
    }
    public static Rect TitleClose(int mode) => TitleButton(0, mode);
    public static Rect TitleMin(int mode) => TitleButton(1, mode);
    public static Rect TitleMax(int mode) => TitleButton(2, mode);

    // 滚动条：mode 0 → (273,148,15,154)；mode 1 → (273,55,15,125)（0x850b4c / 0x850943）
    public static Rect Scroll(int mode) =>
        new Rect(273, mode == 0 ? 148 : 55, 15, mode == 0 ? 154 : 125);

    // 输入框：mode 0 → (9,318,207,15)；mode 1 → (9,194,207,15)（0x850bcf / 0x8509c6）
    public static Rect Edit(int mode) =>
        new Rect(9, mode == 0 ? 318 : 194, 207, 15);

    // ENTER：mode 0 → (249,317,39,17)；mode 1 → (249,193,39,17)（0x850cfe / 0x850af5）
    // 起点 249 在白色输入框（底图 8..246）右侧的灰条里，不是压在框上。
    public static Rect Enter(int mode) =>
        new Rect(249, mode == 0 ? 317 : 193, 39, 17);

    public static int Lines(int mode) => mode == 0 ? 11 : 9;   // [this+0x70]：0xb / 0x9

    // 座位：小人脚点 (54+94i,118)，名字条 (9+94i,122,89,19)
    public static Vector2 SeatAnchor(int seat) =>
        new Vector2(SeatFirst + SeatPitch * seat, SeatAvatarY);
    public static Rect SeatNameBar(int seat) =>
        new Rect(NameBarX + SeatPitch * seat, NameBarY, NameBarWidth, NameBarHeight);

    static int mode, knownCount;
    static Vector2 position = new Vector2(100, 40), grab;
    static bool dragging;
    static string input = "";
    static float scroll;
    static long sequence;
    static readonly List<string> messages = new List<string>();
    public static void Reset() { mode = 0; input = ""; scroll = 0; sequence = 0; knownCount = 0; messages.Clear(); }

    static GUIStyle Text(bool center = false)
    {
        // 全新实例：任何状态都没有底图，白条/输入框只显示底图本身
        var style = new GUIStyle {font = UiKit.Font,fontSize = 12,padding = new RectOffset(),clipping = TextClipping.Clip,
            alignment = center ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft};
        style.normal.textColor = Color.black; return style;
    }
    static void Avatar(string[] ids,float x,float y)
    {
        if (ids == null) return;
        var sprite = WzCharacter.Appearance("messenger_"+string.Join("_",ids),ids,WzCharacter.AppearanceFrame(ids,Time.unscaledTime));
        if (sprite != null) GUI.DrawTexture(new Rect(x-sprite.pivot.x,y-sprite.rect.height+sprite.pivot.y,sprite.rect.width,sprite.rect.height),sprite.texture);
    }
    public static void Draw(SocialResponse state)
    {
        foreach (var message in state.messengerMessages)
            if (message.sequence > sequence)
            { sequence = message.sequence; if (!message.typing) messages.Add(message.sender+" : "+message.text); }
        if (messages.Count > 200) messages.RemoveRange(0,messages.Count-200);
        float scale = BottomBar.S;
        Vector2 size = SizeOf(mode);
        position = NativeUi.ClampWindowPosition(position,size,scale);
        var pointer = Pointer.Pos / scale;
        var matrix = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale,scale,1));
        try
        {
            if (Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x,position.y,size.x-50,22).Contains(pointer))
            { dragging = true; grab = pointer-position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer-grab,size,scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x,position.y,size.x,size.y));
            try
            {
                var p = pointer-position;
                var background = Art.Tex("wz://UI/UIWindow.img/Messenger/"+(mode==0?"backgrnd":mode==1?"backgrnd2":"backgrnd3"));
                // CWnd::Draw (0x9E0502) copies the canvas at its native size.
                // Mode 1 has a 392px logical window but a 295px background; it is not stretched.
                if (background != null) GUI.DrawTexture(new Rect(0,0,background.width,background.height),background);
                GUI.enabled = enabled && !NativeUtilPrompt.Visible;
                if (NativeUi.Button(TitleClose(mode),"UI/Basic.img/BtClose",p)) { NativeSocialWindow.Close(); return; }
                bool active = GUI.enabled;
                GUI.enabled = active && mode != 2;
                if (NativeUi.Button(TitleMin(mode),"UI/Basic.img/BtMin",p)) { mode = Math.Min(2,mode+1); return; }
                // 源端 mode 0 时把最大化键置灰：0x850824 cmp [this+0x6c],0 → Enable(false)
                GUI.enabled = active && mode != 0;
                if (NativeUi.Button(TitleMax(mode),"UI/Basic.img/BtMax",p)) { mode = Math.Max(0,mode-1); return; }
                GUI.enabled = active;
                if (mode == 2) return;
                for (int i = 0; i < Math.Min(3,state.messengerMembers.Count); i++)
                {
                    var member = state.messengerMembers[i];
                    int seat = Mathf.Clamp(member.messengerPosition, 0, 2);
                    if (mode == 0)
                    {
                        var ids = member.id == GameSession.CharacterId ? WzCharacter.CharacterIds(GameSession.Character.record,GameSession.Character.inventory) : member.appearance;
                        var anchor = SeatAnchor(seat);
                        Avatar(ids,anchor.x,anchor.y);
                    }
                    var bar = SeatNameBar(seat);
                    var texture = Art.Tex("wz://UI/UIWindow.img/Messenger/NameBar/"+(seat+1));
                    if (texture != null) GUI.DrawTexture(bar,texture);
                    GUI.Label(bar,member.name,Text(true));
                }
                int lines = Lines(mode);
                var scrollBar = Scroll(mode);
                int top = mode == 0 ? 155 : 60;
                int maximum = Math.Max(0,messages.Count-lines);
                if (knownCount != messages.Count) { knownCount = messages.Count; scroll = maximum; }
                scroll = NativeUi.Scroll4(scrollBar,new Rect(9,top,LogLineWidth,lines*LineHeight),scroll,maximum,1,p);
                for (int i = (int)scroll; i < Math.Min(messages.Count,(int)scroll+lines); i++)
                    GUI.Label(new Rect(14,top+(i-(int)scroll)*LineHeight,LogLineWidth,LineHeight),messages[i],Text());
                var edit = Edit(mode);
                GUI.SetNextControlName("nativeMessengerChat");
                input = GUI.TextField(edit,input,127,Text());
                bool send = NativeUi.Button(Enter(mode),"UI/UIWindow.img/Messenger/BtEnter",p);
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl()=="nativeMessengerChat")
                { send = true; Event.current.Use(); }
                if (send && !string.IsNullOrWhiteSpace(input))
                {
                    var response = NativeSocialWindow.Request(SocialOperation.SendChat,text:input,channel:NativeChatChannel.Messenger);
                    if (response.ok) { messages.Add(GameSession.Character.record.name+" : "+input); input = ""; }
                    else NativeSystemMessage.Drop(5,response.error);
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.enabled = enabled; GUI.matrix = matrix; }
    }
}
