using UnityEngine;

// Keyboard G/F/' routes use the same protocol as the server-side community rules.
public static class NativeCommunityWindow {
    public static bool Visible;
    static GameAction action;
    static CommunityResponse state;
    static string name = "", text = "", rank = "5", logo = "0", logoColor = "0", background = "0", backgroundColor = "0";
    static string[] titles = new string[5];
    static Vector2 scroll;
    static string board = "";
    static int chatChannel;
    public static CommunityResponse Request(CommunityOperation operation, int targetId = 0, int value = 0) {
        if (!GameSession.InGame) return new CommunityResponse { ok = false, error = "请先选择角色" };
        GameSession.SaveToDb();
        int.TryParse(logo, out int l); int.TryParse(logoColor, out int lc); int.TryParse(background, out int b); int.TryParse(backgroundColor, out int bc);
        state = GameSession.Net.Community(new CommunityRequest { characterId = GameSession.CharacterId, operation = operation,
            targetId = targetId, value = value, name = name, text = text, titles = titles, logo = l, logoColor = lc, background = b, backgroundColor = bc });
        if (state.ok) {
            var rec = GameSession.Character.record; rec.guildid = state.guildId; rec.familyId = state.familyId == 0 ? -1 : state.familyId;
            rec.meso = state.meso; Profile.Data.gold = state.meso;
            foreach (var m in state.guild) if (m.id == GameSession.CharacterId) rec.guildrank = m.rank;
            board = state.board; Profile.Save();
        } else BottomBar.Toast(state.error);
        return state;
    }
    public static void Toggle(GameAction next) {
        if (Visible && action == next) { Visible = false; return; }
        action = next; state = Request(CommunityOperation.Read); Visible = true; scroll = Vector2.zero;
        text = next == GameAction.Family ? state.notice : next == GameAction.School ? state.precepts : state.board;
        titles = (string[])state.titles.Clone();
    }
    public static void Reset() { Visible = false; state = null; board = ""; }
    static int Int(string value) { int.TryParse(value, out int n); return n; }
    public static void Draw() {
        if (!Visible || state == null) return;
        float w = Mathf.Min(570, UiKit.W - 24), h = Mathf.Min(490, UiKit.H - BottomBar.Height - 24);
        var rect = new Rect((UiKit.W - w) / 2, 12, w, h);
        GUI.Box(rect, "");
        GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20));
        var oldFont = GUI.skin.font; GUI.skin.font = UiKit.Font;
        GUILayout.BeginHorizontal(); GUILayout.Label(NativeKeyActions.Of(action).name); if (GUILayout.Button("刷新", GUILayout.Width(60))) Request(CommunityOperation.Read); if (GUILayout.Button("关闭", GUILayout.Width(60))) Visible = false; GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll);
        if (action == GameAction.Family) Guild();
        else if (action == GameAction.School) Family();
        else Board();
        GUILayout.EndScrollView(); GUI.skin.font = oldFont; GUILayout.EndArea();
    }
    static void Guild() {
        if (state.guildConfirmation > 0) { GUILayout.Label("收到创建家族的成员确认"); GUILayout.BeginHorizontal(); if (GUILayout.Button("同意创建")) Request(CommunityOperation.ConfirmGuild, state.guildConfirmation, 1); if (GUILayout.Button("拒绝")) Request(CommunityOperation.ConfirmGuild, state.guildConfirmation, 0); GUILayout.EndHorizontal(); }
        if (state.guildId <= 0) {
            GUILayout.Label("尚未加入家族"); name = GUILayout.TextField(name, 12);
            if (GUILayout.Button("发起创建（源端默认6人 / 150万金币）")) Request(CommunityOperation.CreateGuild);
            if (state.guildInvite > 0 && GUILayout.Button("接受家族邀请")) Request(CommunityOperation.AcceptGuild);
            return;
        }
        GUILayout.Label(state.guildName + "    " + state.guild.Count + " / " + state.capacity + "    GP " + state.gp);
        GUILayout.Label("公告：" + state.notice);
        foreach (var m in state.guild.ToArray()) {
            GUILayout.BeginHorizontal(); GUILayout.Label(m.name + "  Lv." + m.level + "  " + state.titles[Mathf.Clamp(m.rank - 1, 0, 4)] + (m.online ? " 在线" : " 离线"));
            if (m.id != GameSession.CharacterId) { if (GUILayout.Button("设职位", GUILayout.Width(70))) Request(CommunityOperation.GuildRank, m.id, Int(rank)); if (GUILayout.Button("驱逐", GUILayout.Width(50))) Request(CommunityOperation.ExpelGuild, m.id); }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("角色名 / 职位等级（2～5）"); name = GUILayout.TextField(name, 13); rank = GUILayout.TextField(rank, 1);
        if (GUILayout.Button("邀请成员")) Request(CommunityOperation.InviteGuild);
        text = GUILayout.TextField(text, 100); if (GUILayout.Button("保存公告")) Request(CommunityOperation.GuildNotice);
        GUILayout.Label("五级职位名称"); for (int i = 0; i < 5; i++) titles[i] = GUILayout.TextField(titles[i] ?? "", 45);
        if (GUILayout.Button("保存职位名称")) Request(CommunityOperation.GuildTitles);
        GUILayout.Label("徽章图案 / 颜色 / 底纹 / 底纹颜色（需在家族总部）");
        GUILayout.BeginHorizontal(); logo = GUILayout.TextField(logo, 5); logoColor = GUILayout.TextField(logoColor, 3); background = GUILayout.TextField(background, 5); backgroundColor = GUILayout.TextField(backgroundColor, 3); GUILayout.EndHorizontal();
        if (GUILayout.Button("修改徽章（源端默认500万金币）")) Request(CommunityOperation.GuildEmblem);
        if (GUILayout.Button("扩充5名成员容量")) Request(CommunityOperation.ExpandGuild);
        GUILayout.BeginHorizontal(); if (GUILayout.Button("退出家族")) Request(CommunityOperation.LeaveGuild); if (GUILayout.Button("解散家族（族长）")) Request(CommunityOperation.DisbandGuild); GUILayout.EndHorizontal();
    }
    static readonly string[] Privileges = { "传送到同学", "召集同学", "本人掉落1.5倍 / 15分", "本人经验1.5倍 / 15分", "学院团结 经验掉落2倍 / 30分", "本人掉落2倍 / 15分", "本人经验2倍 / 15分", "本人掉落2倍 / 30分", "本人经验2倍 / 30分", "组队掉落2倍 / 30分", "组队经验2倍 / 30分" };
    static void Family() {
        GUILayout.Label(state.familyId > 0 ? "学院 " + state.familyId : "尚未加入学院");
        if (state.summonFrom > 0) {
            GUILayout.Label("收到同学召集请求");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("同意召集")) { var reply = Request(CommunityOperation.AnswerSummon, 0, 1); if (reply.ok && reply.map != Profile.LastMapId) { Visible = false; GameLoop.I?.field?.WarpTo(reply.map); } }
            if (GUILayout.Button("拒绝召集")) Request(CommunityOperation.AnswerSummon, 0, 0);
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("声望 " + state.reputation + "   今日 " + state.todayRep + "   累计 " + state.totalRep);
        GUILayout.Label("家训：" + state.precepts);
        name = GUILayout.TextField(name, 13); if (GUILayout.Button("登记直属同学（需对方在线且在同地图）")) Request(CommunityOperation.InviteFamily);
        if (state.familyInvite > 0 && GUILayout.Button("接受学长登记邀请")) Request(CommunityOperation.AcceptFamily);
        foreach (var m in state.family.ToArray()) {
            string senior = "学院首领"; foreach (var s in state.family) if (s.id == m.senior) senior = "学长 " + s.name;
            GUILayout.BeginHorizontal(); GUILayout.Label(m.name + " Lv." + m.level + " · " + senior + (m.online ? " 在线" : " 离线"));
            if (m.senior == GameSession.CharacterId && GUILayout.Button("解除", GUILayout.Width(50))) Request(CommunityOperation.SeparateFamily, m.id); GUILayout.EndHorizontal();
        }
        if (state.familyId > 0) {
            if (GUILayout.Button("解除与学长的关系（按等级差收费）")) Request(CommunityOperation.SeparateFamily);
            text = GUILayout.TextField(text, 200); if (GUILayout.Button("保存家训（学院首领）")) Request(CommunityOperation.FamilyPrecepts);
            GUILayout.Label("特权：召集/传送目标使用上方角色名");
            for (int i = 0; i < Privileges.Length; i++) { int index = i; bool old = GUI.enabled; GUI.enabled = !state.used.Contains(i); if (GUILayout.Button(Privileges[i] + "  声望 " + NativeCommunityService.FamilyCosts[i])) { var result = Request(CommunityOperation.UseFamily, 0, index); if (result.ok && result.map != Profile.LastMapId) { Visible = false; GameLoop.I?.field?.WarpTo(result.map); } } GUI.enabled = old; }
        }
    }
    static void Board() {
        GUILayout.Label("聊天记录");
        foreach (var line in BottomBar.ChatHistory) GUILayout.Label(line.text);
        chatChannel = GUILayout.Toolbar(chatChannel, new[] { "所有人", "悄悄话", "组队", "好友", "家族", "联盟", "夫妻", "讯息" });
        name = GUILayout.TextField(name, 13);
        GUILayout.Label("消息内容 / 悄悄话对象填写在上方");
        text = GUILayout.TextField(text, 127);
        if (GUILayout.Button("发送消息")) { if (BottomBar.SubmitChat(text, (NativeChatChannel)chatChannel, name)) text = ""; }
        GUILayout.Space(12);
        GUILayout.Label("现金黑板（需要持有537类现金道具，使用不消耗）");
        text = GUILayout.TextArea(text, 255, GUILayout.Height(100));
        if (GUILayout.Button("显示黑板")) Request(CommunityOperation.SetBoard);
        if (GUILayout.Button("收起黑板")) Request(CommunityOperation.CloseBoard);
    }
    public static void DrawBoard() {
        if (!GameSession.InGame || string.IsNullOrEmpty(board)) return;
        var player = GameLoop.I != null ? GameLoop.I.player : null; var camera = Camera.main; if (player == null || camera == null) return;
        var point = camera.WorldToScreenPoint(player.transform.position + new Vector3(0, 2.3f, 0));
        var rect = new Rect(point.x - 120, Screen.height - point.y - 48, 240, 48);
        var style = new GUIStyle(GUI.skin.box) { font = UiKit.Font, wordWrap = true, alignment = TextAnchor.MiddleCenter }; GUI.Box(rect, board, style);
    }
}
