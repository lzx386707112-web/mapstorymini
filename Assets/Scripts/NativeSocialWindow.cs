using System;
using System.Collections.Generic;
using UnityEngine;

// User-list and messenger entries share the server's social state, not key-local state.
//
// UserList 窗口的原端几何（BeiDou.exe，ImageBase 0x400000）：
//   OnCreate 0x919B73；Party 资源 0x90B43D；Party Draw 0x90BDDB；
//   主 Draw 0x91BEAB 的 tab1 → 0x91C39C..0x91C46A → 0x91CCBB 把 party 合成图复制到主窗口 (6,53)，
//   高度上限 278、滚动步长 33。分组规则 0x90B8D0：channel==-2 归离线，否则 mapId 同图/异图。
public static class NativeSocialWindow
{
    // —— 原端几何（单位：窗口像素，窗口 312×389）——
    const int WindowW = 312, WindowH = 389;
    const int ListX = 6, ListY = 53, ListW = 281, ListH = 278;
    const int GroupHeadH = 27;      // party0 / party4，281×27
    const int ColumnHeadY = 31;     // 组内列头 y31，高 18（party5，279×18）
    const int RowTop = 49;          // 成员首行 y49
    const int RowH = 18;            // 行高 18（party1，279×18）
    const int ColNameX = 25, ColJobX = 125, ColLevelX = 205;
    const int NameClip = 67;        // 名字裁剪 67px
    const int GroupLineH = 4;       // party2，279×4
    const int GroupGap = 10;        // 在线组结束后间隔 10
    const string PartyRoot = "UI/UIWindow.img/UserList/Party/";

    public static bool Visible { get; private set; }
    static GameAction action;
    static SocialResponse state;
    static string target = "", group = "Default Group", minimum = "1", maximum = "30";
    static Vector2 scroll;                  // 仅好友 tab 的 GUILayout 用
    static Vector2 position = new Vector2(60, 40), grab;
    static bool dragging;
    static double nextRefresh;
    static int partyInvitation, messengerInvitation;
    static int selectedId;
    public static SocialResponse Request(SocialOperation operation, string name = "", string text = "",
        NativeChatChannel channel = NativeChatChannel.All, int partyId = 0, int messengerRoom = 0, int memberId = 0)
    {
        if (!GameSession.InGame || GameSession.Net == null)
            return new SocialResponse { ok = false, error = "请先进入角色" };
        // Party level validation must see current gameplay progress.
        if (operation == SocialOperation.CreateParty || operation == SocialOperation.SearchParty) GameSession.SaveToDb();
        int.TryParse(minimum, out int min); int.TryParse(maximum, out int max);
        state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId,
            operation = operation, target = name, group = group, partyId = partyId, messengerRoom = messengerRoom,
            text = text, channel = channel, minLevel = min, maxLevel = max, memberId = memberId });
        GameSession.Character.record.party = state.partyId;
        return state;
    }
    static void Run(SocialOperation operation, string name = "")
    {
        var result = Request(operation, name);
        // 源端组队/好友这类失败一律 serverNotice(5, …)：聊天窗粉色行，不是浮层提示。
        if (!result.ok) NativeSystemMessage.Drop(5, result.error);
    }
    // 逐出/转让队长按 memberId 定位：服务端 PartyOperationHandler 也是按成员 id 处理。
    static void RunMember(SocialOperation operation)
    {
        if (selectedId == 0) return;
        var result = Request(operation, memberId: selectedId);
        if (!result.ok) NativeSystemMessage.Drop(5, result.error);
        selectedId = 0;
    }
    public static void Toggle(GameAction next)
    {
        if (Visible && action == next) { Close(); return; }
        action = next; Visible = true; scroll = Vector2.zero; selectedId = 0;
        state = Request(SocialOperation.Read);
        if (next == GameAction.Messenger) Run(SocialOperation.OpenMessenger);
        minimum = Mathf.Max(1, Profile.level - 15).ToString();
        maximum = (Mathf.Max(1, Profile.level - 15) + 30).ToString();
    }
    public static void Reset()
    {
        Visible = false; state = null; target = ""; selectedId = 0;
        partyInvitation = messengerInvitation = 0; nextRefresh = 0; NativeMessengerWindow.Reset();
    }
    // 关闭按钮 / Esc 共用：信使要先按源端发一条关闭，再收起窗口
    public static void Close()
    {
        if (Visible && action == GameAction.Messenger) Run(SocialOperation.CloseMessenger);
        Visible = false;
    }
    public static void Tick()
    {
        if (!GameSession.InGame || GameSession.Net == null || Time.realtimeSinceStartupAsDouble < nextRefresh) return;
        state = Request(SocialOperation.Read); nextRefresh = Time.realtimeSinceStartupAsDouble + .25;
        if (NativeUtilPrompt.Visible) return;
        if (state.partyInvite != 0 && partyInvitation != state.partyInvite)
        {
            int invite = partyInvitation = state.partyInvite;
            NativeUtilPrompt.Confirm("收到组队邀请。是否接受？",yes =>
            { Request(yes ? SocialOperation.AcceptParty : SocialOperation.DeclineParty,partyId:invite); partyInvitation = 0; });
        }
        else if (state.messengerInvite != 0 && messengerInvitation != state.messengerInvite)
        {
            int invite = messengerInvitation = state.messengerInvite;
            NativeUtilPrompt.Confirm(state.messengerInviter+" 邀请你加入聊天。是否接受？",yes =>
            {
                var response = Request(yes ? SocialOperation.AcceptMessenger : SocialOperation.DeclineMessenger,messengerRoom:invite);
                messengerInvitation = 0;
                if (yes && response.ok) { action = GameAction.Messenger; Visible = true; NativeMessengerWindow.Reset(); }
            });
        }
        if (state.partyInvite == 0) partyInvitation = 0;
        if (state.messengerInvite == 0) messengerInvitation = 0;
    }
    public static void Draw()
    {
        Tick();
        if (!Visible || state == null) return;
        if (action == GameAction.Messenger) { NativeMessengerWindow.Draw(state); return; }
        DrawUserList();
        NativeDlgInput.Draw();
    }

    // ————— 窗口外壳：背景 / 关闭 / tab 条 —————
    static void DrawUserList()
    {
        float scale = BottomBar.S;
        var size = new Vector2(WindowW, WindowH);
        position = NativeUi.ClampWindowPosition(position, size, scale);
        var pointer = Pointer.Pos / scale;
        var matrix = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (Event.current.type == EventType.Repaint && Pointer.Pressed &&
                new Rect(position.x, position.y, size.x - 44, 22).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, size, scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, size.x, size.y));
            try
            {
                var p = pointer - position;
                // 标题「玩家列表 & 家族」和红分隔线烘死在 backgrnd 上，不再自绘文字。
                // 三张同尺寸底图分工不同（2026-10-30 导出核对）：backgrnd 纯列表面板（组员 tab）、
                // backgrnd2 带内嵌列表区（家族 tab）、backgrnd3 带「管理朋友」横幅（好友 tab）。
                string backgroundNode = action == GameAction.Buddy ? "backgrnd3" : "backgrnd";
                var background = Art.Tex("wz://UI/UIWindow.img/UserList/" + backgroundNode);
                if (background != null) GUI.DrawTexture(new Rect(0, 0, WindowW, WindowH), background);
                GUI.enabled = enabled && !NativeUtilPrompt.Visible && !NativeDlgInput.Visible;
                if (NativeUi.Button(new Rect(WindowW - 19, 5, 12, 12), "UI/Basic.img/BtClose", p)) { Close(); return; }
                DrawTabs(p);
                if (action == GameAction.Buddy) DrawBuddyTab(p);
                else if (action == GameAction.PartySearch) DrawSearchTab(p);
                else DrawPartyTab(p);
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.enabled = enabled; GUI.matrix = matrix; }
    }

    // Tab/0 好友、Tab/1 组员、Tab/2 家族、Tab/3-4 拒绝列表。
    // 非等宽分支 0x4DE15F：段宽 = 标签 canvas 宽 − 4，首段 leading 6，段间 13。
    static readonly string[] TabNames = { "好友", "组员", "家族", "拒绝列表" };
    static readonly int[] TabCanvas = { 0, 1, 2, 4 };
    static readonly int[] TabCanvasWidth = { 27, 27, 27, 52 };
    static void DrawTabs(Vector2 p)
    {
        int current = action == GameAction.Buddy ? 0 : 1;
        float x = ListX + 6;
        for (int i = 0; i < TabNames.Length; i++)
        {
            var tex = Art.Tex("wz://UI/UIWindow.img/UserList/Tab/" + (i == current ? "enabled" : "disabled") + "/" + TabCanvas[i]);
            // 段长 = 当前真正画出来的那张标签 canvas 宽 − 4（源端非等宽分支 0x4DE15F）；
            // 不能写死一组宽度：enabled/disabled 的 canvas 宽差 1~2px，写死会让后段整体漂移。
            float w = (tex != null ? tex.width : TabCanvasWidth[i]) - 4;
            var slot = new Rect(x, 23, w, 20);
            if (tex != null)
                GUI.DrawTexture(new Rect(slot.center.x - tex.width / 2f, slot.y + (slot.height - tex.height) / 2f,
                    tex.width, tex.height), tex);
            if (Event.current.type == EventType.Repaint && Pointer.Clicked && slot.Contains(p))
            { Pointer.ConsumeClick(); SwitchTab(i); return; }
            x += w + 13;
        }
    }
    static void SwitchTab(int index)
    {
        selectedId = 0;
        if (index == 0) { action = GameAction.Buddy; state = Request(SocialOperation.Read); return; }
        if (index == 1) { action = GameAction.Party; state = Request(SocialOperation.Read); return; }
        if (index == 2) { Close(); NativeCommunityWindow.Toggle(GameAction.Family); return; }
        BottomBar.Toast("拒绝列表尚未开放");
    }

    // ————— 组员 tab —————
    static SocialMember SelectedMember()
    {
        if (selectedId == 0) return null;
        foreach (var member in state.partyMembers) if (member.id == selectedId) return member;
        return null;
    }
    static GUIStyle RowStyle(bool selected)
    {
        var style = new GUIStyle
        {
            font = UiKit.Font, fontSize = 12, padding = new RectOffset(), clipping = TextClipping.Clip,
            alignment = TextAnchor.UpperLeft,
        };
        style.normal.textColor = selected ? Color.white : Color.black;
        return style;
    }
    static void MemberRow(Rect row, SocialMember member, bool leader, bool selected, Vector2 p)
    {
        var floor = Art.Tex("wz://" + PartyRoot + "party1");
        if (floor != null) GUI.DrawTexture(new Rect(1, row.y, floor.width, floor.height), floor);
        // 选中行原端 FillRect 0xFF244768，宽 281 高 18。
        if (selected) UiKit.Fill(new Rect(0, row.y, 281, RowH), new Color32(0x24, 0x47, 0x68, 0xff));
        if (leader)
        {
            var star = Art.Tex("wz://" + PartyRoot + "icon1");
            if (star != null) GUI.DrawTexture(new Rect(6, row.y + 1, star.width, star.height), star);
        }
        var style = RowStyle(selected);
        GUI.Label(new Rect(ColNameX, row.y + 2, NameClip, 14), member.name, style);
        GUI.Label(new Rect(ColJobX, row.y + 2, 70, 14), JobDefs.NameOf(member.job), style);
        GUI.Label(new Rect(ColLevelX, row.y + 2, 40, 14), member.level.ToString(), style);
        if (Event.current.type == EventType.Repaint && Pointer.Clicked && row.Contains(p))
        { Pointer.ConsumeClick(); selectedId = member.id; }
    }
    // 返回该组绘制结束后的 y（组内相对坐标）。
    static float DrawGroup(List<SocialMember> members, string header, bool online, float y, Vector2 p)
    {
        var title = Art.Tex("wz://" + PartyRoot + header);
        if (title != null) GUI.DrawTexture(new Rect(0, y, title.width, title.height), title);
        y += ColumnHeadY;                                   // 组内列头 y31
        var columns = Art.Tex("wz://" + PartyRoot + "party5");
        if (columns != null) GUI.DrawTexture(new Rect(1, y, columns.width, columns.height), columns);
        y += 18;                                            // 成员首行 y49
        if (members.Count == 0) y += RowH;                  // 空组仍留一行
        foreach (var member in members)
        {
            // 组内坐标：列表已经整体平移过，行矩形不再叠加 (6,53)。
            var row = new Rect(0, y, ListW, RowH);
            MemberRow(row, member, member.id == state.partyLeader, member.id == selectedId, p);
            y += RowH;
        }
        if (online)
        {
            var line = Art.Tex("wz://" + PartyRoot + "party2");
            if (line != null) GUI.DrawTexture(new Rect(1, y, line.width, line.height), line);
            y += GroupLineH + GroupGap;
        }
        else
        {
            var line = Art.Tex("wz://" + PartyRoot + "party3");
            if (line != null) GUI.DrawTexture(new Rect(1, y, line.width, line.height), line);
        }
        return y;
    }
    // 0x90B8D0：channel==-2 离线；否则同图/异图。三组各自按名字比较排序（0x90BA13..0x90BC13）。
    // public 而非 internal：Assets/Editor 编到 Assembly-CSharp-Editor，跨程序集看不到 internal。
    public static void GroupMembers(SocialResponse state, int selfMap,
        out List<SocialMember> online, out List<SocialMember> offline)
    {
        online = new List<SocialMember>(); offline = new List<SocialMember>();
        if (state == null) return;
        foreach (var member in state.partyMembers)
            (member.channel == -2 ? offline : online).Add(member);
        online.Sort((a, b) =>
        {
            int rank = (a.mapId == selfMap ? 0 : 1).CompareTo(b.mapId == selfMap ? 0 : 1);
            return rank != 0 ? rank : string.CompareOrdinal(a.name, b.name);
        });
        offline.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }
    // 列表内容总高：两组各「标题27 + 列头18 + 行×18 + 组线」，在线组再加间隔 10。上限 278。
    public static float PartyContentHeight(int online, int offline)
    {
        float onlineGroup = GroupHeadH + (18 + 4) + Mathf.Max(1, online) * RowH + GroupLineH + GroupGap;
        float offlineGroup = GroupHeadH + (18 + 4) + Mathf.Max(1, offline) * RowH + 1;
        return onlineGroup + offlineGroup;
    }
    static void DrawPartyTab(Vector2 p)
    {
        int selfMap = GameSession.Character != null ? GameSession.Character.record.map : 0;
        GroupMembers(state, selfMap, out var online, out var offline);
        if (SelectedMember() == null) selectedId = 0;

        // 列表内容整体裁在合成区 (6,53) 281×278 内。
        GUI.BeginGroup(new Rect(ListX, ListY, ListW, ListH));
        try
        {
            var inside = p - new Vector2(ListX, ListY);
            float y = DrawGroup(online, "party0", true, 0, inside);
            DrawGroup(offline, "party4", false, y, inside);
        }
        finally { GUI.EndGroup(); }
        DrawPartyButtons(p);
    }

    // 按钮坐标来自 OnCreate 0x919B73 的 CreateButton 调用（id, x, y）；
    // y 是顶边，19 高素材比 18 高的多占 1px，源端按底边对齐（上排 363、下排 383）。
    struct PartyButton { public int id; public string node; public float x, y; }
    static readonly PartyButton[] Buttons = {
        new PartyButton { id = 2200, node = "BtCreate",     x = 13,  y = 355 },
        new PartyButton { id = 2201, node = "BtInvite",     x = 72,  y = 365 },
        new PartyButton { id = 2202, node = "BtKick",       x = 190, y = 344 },
        new PartyButton { id = 2203, node = "BtWithdraw",   x = 190, y = 364 },
        new PartyButton { id = 2204, node = "BtWhisper",    x = 249, y = 345 },
        new PartyButton { id = 2205, node = "BtChat",       x = 249, y = 365 },
        new PartyButton { id = 2206, node = "BtChangeBoss", x = 131, y = 365 },
        new PartyButton { id = 2207, node = "BtHP",         x = 131, y = 345 },
        new PartyButton { id = 2208, node = "BtSearch",     x = 72,  y = 345 },
    };
    public struct PartyButtonInfo { public int id; public string node; public float x, y, w, h; }
    // 供无渲染断言使用（Assets/Editor/PartyPanelChecks.cs）。
    public static PartyButtonInfo[] PartyButtons()
    {
        var list = new List<PartyButtonInfo>();
        foreach (var button in Buttons)
        {
            var tex = Art.Tex("wz://" + PartyRoot + button.node + "/normal/0");
            list.Add(new PartyButtonInfo
            {
                id = button.id, node = button.node, x = button.x, y = button.y,
                w = tex != null ? tex.width : 47, h = tex != null ? tex.height : 19,
            });
        }
        return list.ToArray();
    }
    public static Vector4 PartyListBox() => new Vector4(ListX, ListY, ListW, ListH);
    static void DrawPartyButtons(Vector2 p)
    {
        bool party = state.party;
        bool leader = party && state.partyLeader == GameSession.CharacterId;
        bool other = selectedId != 0 && selectedId != GameSession.CharacterId && SelectedMember() != null;
        bool room = party && state.partyMembers.Count < 6;
        bool restore = GUI.enabled;
        foreach (var button in Buttons)
        {
            var tex = Art.Tex("wz://" + PartyRoot + button.node + "/normal/0");
            float w = tex != null ? tex.width : 47, h = tex != null ? tex.height : 19;
            bool on;
            switch (button.id)
            {
                case 2200: on = !party; break;                                  // 开设
                case 2201: on = leader && room; break;                          // 邀请
                case 2202: on = leader && other; break;                         // 强退
                case 2203: on = party; break;                                   // 退出
                case 2204: on = other; break;                                   // 悄悄话
                case 2205: on = party; break;                                   // 对话
                case 2206: on = leader && other; break;                         // 组队长
                case 2207: on = false; break;                                   // HP 显示：服务端未下发队员 HP
                case 2208: on = leader; break;                                  // 搜索
                default: on = false; break;
            }
            GUI.enabled = on;
            if (NativeUi.Button(new Rect(button.x, button.y, w, h), PartyRoot + button.node, p))
                PartyAction(button.id);
        }
        // 按钮的 enabled 状态是本帧临时的：不恢复会连带把后面绘制的其它窗口一起置灰。
        GUI.enabled = restore;
    }
    static void PartyAction(int id)
    {
        var member = SelectedMember();
        switch (id)
        {
            case 2200: Run(SocialOperation.CreateParty); break;
            case 2201:
                // 原端组队邀请走 CUtilDlgEx type3 输入框（0x9111BE 同类入口，最多 12 字符）。
                NativeDlgInput.Open("请输入要邀请的角色名。", target, 12,
                    name => { target = name; Run(SocialOperation.InviteParty, name); });
                break;
            case 2202: RunMember(SocialOperation.ExpelParty); break;
            case 2203: Run(SocialOperation.LeaveParty); selectedId = 0; break;
            case 2204:
                if (member != null) BottomBar.FocusChat(NativeChatChannel.Whisper, member.name);
                break;
            case 2205: BottomBar.FocusChat(NativeChatChannel.Party); break;
            case 2206: RunMember(SocialOperation.ChangePartyLeader); break;
            case 2208: action = GameAction.PartySearch; break;
        }
    }

    // ————— 好友 tab（尚未按原端 2010..2020 按钮组重排，保留可用的临时列表）—————
    static void DrawBuddyTab(Vector2 p)
    {
        GUI.BeginGroup(new Rect(ListX, ListY, ListW, ListH));
        try
        {
            GUILayout.BeginArea(new Rect(0, 0, ListW, ListH));
            var oldFont = GUI.skin.font; GUI.skin.font = UiKit.Font;
            scroll = GUILayout.BeginScrollView(scroll);
            DrawBuddies();
            GUILayout.EndScrollView(); GUI.skin.font = oldFont;
            GUILayout.EndArea();
        }
        finally { GUI.EndGroup(); }
    }
    static void DrawBuddies()
    {
        int count = 0; foreach (var buddy in state.buddies) if (!buddy.pending) count++;
        GUILayout.Label("好友 " + count + " / " + state.capacity);
        target = GUILayout.TextField(target, 13);
        group = GUILayout.TextField(group, 16);
        if (GUILayout.Button("添加好友 / 修改分组")) Run(SocialOperation.AddBuddy, target);
        foreach (var buddy in state.buddies.ToArray())
        {
            GUILayout.Label(buddy.name + (buddy.pending ? "  申请添加好友" : "  离线 · " + buddy.group));
            GUILayout.BeginHorizontal();
            if (buddy.pending && GUILayout.Button("接受")) Run(SocialOperation.AcceptBuddy, buddy.name);
            if (GUILayout.Button(buddy.pending ? "拒绝" : "删除")) Run(SocialOperation.DeleteBuddy, buddy.name);
            if (!buddy.pending && GUILayout.Button("悄悄话")) BottomBar.FocusChat(NativeChatChannel.Whisper, buddy.name);
            GUILayout.EndHorizontal();
        }
    }

    // ————— 寻找组队（独立窗口 UI/UIWindow.img/PartySearch 305×407 尚未迁移）—————
    static void DrawSearchTab(Vector2 p)
    {
        GUI.BeginGroup(new Rect(ListX, ListY, ListW, 240));
        try
        {
            GUILayout.BeginArea(new Rect(0, 0, ListW, 240));
            var oldFont = GUI.skin.font; GUI.skin.font = UiKit.Font;
            GUILayout.Label("搜索等级范围（最多相差30级）");
            GUILayout.BeginHorizontal(); minimum = GUILayout.TextField(minimum, 3); maximum = GUILayout.TextField(maximum, 3); GUILayout.EndHorizontal();
            GUILayout.Label(state.searching ? "正在寻找符合条件的在线队员" : "搜索已停止");
            if (GUILayout.Button(state.searching ? "停止搜索" : "开始搜索")) Run(state.searching ? SocialOperation.StopSearch : SocialOperation.SearchParty);
            GUI.skin.font = oldFont;
            GUILayout.EndArea();
        }
        finally { GUI.EndGroup(); }
    }
}
