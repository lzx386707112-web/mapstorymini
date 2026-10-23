using UnityEngine;

// User-list and messenger entries share the server's social state, not key-local state.
public static class NativeSocialWindow
{
    public static bool Visible { get; private set; }
    static GameAction action;
    static SocialResponse state;
    static string target = "", group = "Default Group", minimum = "1", maximum = "30";
    static Vector2 scroll;
    public static SocialResponse Request(SocialOperation operation, string name = "", string text = "",
        NativeChatChannel channel = NativeChatChannel.All)
    {
        if (!GameSession.InGame || GameSession.Net == null)
            return new SocialResponse { ok = false, error = "请先进入角色" };
        // Party level validation must see current gameplay progress.
        if (operation == SocialOperation.CreateParty || operation == SocialOperation.SearchParty) GameSession.SaveToDb();
        int.TryParse(minimum, out int min); int.TryParse(maximum, out int max);
        state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId,
            operation = operation, target = name, group = group, text = text, channel = channel, minLevel = min, maxLevel = max });
        GameSession.Character.record.party = state.party ? GameSession.CharacterId : 0;
        return state;
    }
    static void Run(SocialOperation operation, string name = "")
    {
        var result = Request(operation, name);
        if (!result.ok) BottomBar.Toast(result.error);
    }
    public static void Toggle(GameAction next)
    {
        if (Visible && action == next) { Visible = false; return; }
        action = next; Visible = true; scroll = Vector2.zero;
        state = Request(SocialOperation.Read);
        if (next == GameAction.Messenger) Run(SocialOperation.OpenMessenger);
        minimum = Mathf.Max(1, Profile.level - 15).ToString();
        maximum = (Mathf.Max(1, Profile.level - 15) + 30).ToString();
    }
    public static void Reset()
    { Visible = false; state = null; target = ""; }
    public static void Draw()
    {
        if (!Visible || state == null) return;
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
        { if (action == GameAction.Messenger) Run(SocialOperation.CloseMessenger); Visible = false; return; }
        GUILayout.BeginArea(new Rect(r.x + 15 * s, r.y + 48 * s, r.width - 30 * s, r.height - 66 * s));
        var oldFont = GUI.skin.font; GUI.skin.font = UiKit.Font;
        scroll = GUILayout.BeginScrollView(scroll);
        if (action == GameAction.Buddy) DrawBuddies();
        else if (action == GameAction.Party || action == GameAction.PartySearch) DrawParty();
        else if (action == GameAction.Messenger)
        {
            GUILayout.Label("讯息成员（1 / 3）"); GUILayout.Label(Profile.playerName);
            target = GUILayout.TextField(target, 13);
            if (GUILayout.Button("邀请")) Run(SocialOperation.InviteMessenger, target);
            if (GUILayout.Button("聊天")) BottomBar.FocusChat(NativeChatChannel.Messenger);
        }
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
        GUILayout.Label(state.party ? "队长：" + Profile.playerName + "  （1 / 6）" : "尚未加入组队");
        if (GUILayout.Button(state.party ? "解散组队" : "创建组队")) Run(state.party ? SocialOperation.LeaveParty : SocialOperation.CreateParty);
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
