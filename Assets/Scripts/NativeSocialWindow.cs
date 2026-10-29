using UnityEngine;

// User-list and messenger entries share the server's social state, not key-local state.
public static class NativeSocialWindow
{
    public static bool Visible { get; private set; }
    static GameAction action;
    static SocialResponse state;
    static string target = "", group = "Default Group", minimum = "1", maximum = "30";
    static Vector2 scroll;
    static double nextRefresh;
    static int partyInvitation, messengerInvitation;
    public static SocialResponse Request(SocialOperation operation, string name = "", string text = "",
        NativeChatChannel channel = NativeChatChannel.All, int partyId = 0, int messengerRoom = 0)
    {
        if (!GameSession.InGame || GameSession.Net == null)
            return new SocialResponse { ok = false, error = "请先进入角色" };
        // Party level validation must see current gameplay progress.
        if (operation == SocialOperation.CreateParty || operation == SocialOperation.SearchParty) GameSession.SaveToDb();
        int.TryParse(minimum, out int min); int.TryParse(maximum, out int max);
        state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId,
            operation = operation, target = name, group = group, partyId = partyId, messengerRoom = messengerRoom, text = text, channel = channel, minLevel = min, maxLevel = max });
        GameSession.Character.record.party = state.partyId;
        return state;
    }
    static void Run(SocialOperation operation, string name = "")
    {
        var result = Request(operation, name);
        // 源端组队/好友这类失败一律 serverNotice(5, …)：聊天窗粉色行，不是浮层提示。
        if (!result.ok) NativeSystemMessage.Drop(5, result.error);
    }
    public static void Toggle(GameAction next)
    {
        if (Visible && action == next) { Close(); return; }
        action = next; Visible = true; scroll = Vector2.zero;
        state = Request(SocialOperation.Read);
        if (next == GameAction.Messenger) Run(SocialOperation.OpenMessenger);
        minimum = Mathf.Max(1, Profile.level - 15).ToString();
        maximum = (Mathf.Max(1, Profile.level - 15) + 30).ToString();
    }
    public static void Reset()
    { Visible = false; state = null; target = ""; partyInvitation = messengerInvitation = 0; nextRefresh = 0; NativeMessengerWindow.Reset(); }
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
        string folder = action == GameAction.Messenger ? "Messenger" : action == GameAction.PartySearch ? "PartySearch" : "UserList";
        var background = WzAssets.Texture("wz://UI/UIWindow.img/" + folder + "/backgrnd");
        float s = BottomBar.S;
        var r = new Rect(Mathf.Max(8, (UiKit.W - background.width * s) / 2), 35, background.width * s, background.height * s);
        GUI.DrawTexture(r, background);
        var title = NativeKeyActions.Of(action)?.name ?? "好友";
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * s), wordWrap = true };
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(r.x + 12 * s, r.y + 5 * s, r.width - 40 * s, 24 * s), title, style);
        if (NativeUi.Button(new Rect(r.xMax - 20 * s, r.y + 5 * s, 12 * s, 12 * s), "UI/Basic.img/BtClose"))
        { Close(); return; }
        GUILayout.BeginArea(new Rect(r.x + 15 * s, r.y + 48 * s, r.width - 30 * s, r.height - 66 * s));
        var oldFont = GUI.skin.font; GUI.skin.font = UiKit.Font;
        scroll = GUILayout.BeginScrollView(scroll);
        if (action == GameAction.Buddy) DrawBuddies();
        else if (action == GameAction.Party || action == GameAction.PartySearch) DrawParty();
        GUILayout.EndScrollView(); GUI.skin.font = oldFont;
        GUILayout.EndArea();
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
    static void DrawParty()
    {
        string leaderName = "";
        foreach (var member in state.partyMembers) if (member.id == state.partyLeader) leaderName = member.name;
        GUILayout.Label(state.party ? "队长：" + leaderName + "  （" + state.partyMembers.Count + " / 6）" : "尚未加入组队");
        if (GUILayout.Button(state.party ? (state.partyLeader == GameSession.CharacterId ? "解散组队" : "离开组队") : "创建组队")) Run(state.party ? SocialOperation.LeaveParty : SocialOperation.CreateParty);
        target = GUILayout.TextField(target, 13);
        if (GUILayout.Button("邀请队员")) Run(SocialOperation.InviteParty, target);
        if (state.party && GUILayout.Button("组队聊天")) BottomBar.FocusChat(NativeChatChannel.Party);
        if (action != GameAction.PartySearch) return;
        GUILayout.Label("搜索等级范围（最多相差30级）");
        GUILayout.BeginHorizontal(); minimum = GUILayout.TextField(minimum, 3); maximum = GUILayout.TextField(maximum, 3); GUILayout.EndHorizontal();
        GUILayout.Label(state.searching ? "正在寻找符合条件的在线队员" : "搜索已停止");
        if (GUILayout.Button(state.searching ? "停止搜索" : "开始搜索")) Run(state.searching ? SocialOperation.StopSearch : SocialOperation.SearchParty);
    }
}
