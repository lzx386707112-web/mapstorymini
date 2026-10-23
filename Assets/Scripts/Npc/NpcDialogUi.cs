using UnityEngine;

// 对话窗口 + NPC 任务图标。图片全部来自原版 UI/UIWindow.img/UtilDlgEx。
// 交互语义严格按源端：继续(1,1) / 返回(0,1) / 确定(-1,0) / 是(1,0) / 否(0,0) / 选项(1,1,index)。
public static class NpcDialogUi
{
    const string BtnRoot = "UI/UIWindow.img/UtilDlgEx/";

    static Vector2 scroll;
    static GUIStyle body, title, rowStyle, hint;

    public static void Draw()
    {
        NpcSession.Tick();
        DrawMarkers();
        DrawPlayerQuestAlert();
        HandleNpcClick();
        var session = NpcSession.Active;
        if (session != null && session.Dialog.Visible) DrawDialog(session);
    }

    // ——————————————— NPC 头顶任务图标 ———————————————
    static void DrawMarkers()
    {
        var map = NpcField.Map;
        var cam = Camera.main;
        if (map == null || cam == null) return;
        EnsureStyles();

        for (int i = 0; i < map.npcs.Count; i++)
        {
            var npc = map.npcs[i];
            if (npc.hide != 0) continue;
            var marker = NpcField.MarkerOf(npc.id);
            if (!marker.start && !marker.complete) continue;

            float wx = map.WorldX(npc.x);
            float wy = map.WorldY(npc.y) + 80f / Art.PPU;      // 原生像素高度，避免标记压在 NPC 身体内
            var sp = cam.WorldToScreenPoint(new Vector3(wx, wy, 0f));
            if (sp.z < 0f) continue;
            var gui = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale);
            if (gui.x < -40f || gui.x > UiKit.W + 40f || gui.y < -40f || gui.y > UiKit.H + 40f) continue;

            DrawQuestIcon("UI/UIWindow.img/QuestIcon/" + (marker.complete ? "2" : "0"), gui);
        }
    }

    static Rect DrawQuestIcon(string path, Vector2 anchor)
    {
        var frames = WzAssets.Frames(WzAssets.Node(path));
        if (frames.Count == 0) return new Rect();
        float total = 0f;
        foreach (var frame in frames) total += Mathf.Max(1, frame.Int("delay", 120));
        float time = Mathf.Repeat(Time.unscaledTime * 1000f, total);
        var current = frames[0];
        foreach (var frame in frames)
        {
            current = frame;
            time -= Mathf.Max(1, frame.Int("delay", 120));
            if (time < 0f) break;
        }
        var tex = WzAssets.Texture(current);
        var origin = WzAssets.Origin(current);
        float scale = Screen.height / 768f / UiKit.Scale;
        var rect = new Rect(anchor.x - origin.x * scale, anchor.y - origin.y * scale,
            tex.width * scale, tex.height * scale);
        GUI.DrawTexture(rect, tex);
        return rect;
    }

    static bool CanClickField => !GameWindows.AnyVisible && !ShopWindow.Visible &&
        !GameSideMenu.Blocking && !VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer &&
        NpcSession.Active == null && GameLoop.I != null && !GameLoop.I.gameOver &&
        GameLoop.I.field != null && !PlayerCtrl.InputLocked;

    static void HandleNpcClick()
    {
        if (Event.current.type != EventType.Repaint || !Pointer.Clicked || !CanClickField) return;
        if (Pointer.Pos.y >= UiKit.H - BottomBar.Height) return;
        var npc = NpcField.Pick(Pointer.Pos);
        if (npc != null) InteractNearby(npc);
    }

    static void InteractNearby(FieldNpc npc)
    {
        var player = GameLoop.I.player;
        if (player == null) return;
        var map = NpcField.Map;
        if (Mathf.Abs(player.FeetPos.x - map.WorldX(npc.x)) > 110f / Art.PPU ||
            Mathf.Abs(player.FeetPos.y - map.WorldY(npc.y)) > 110f / Art.PPU)
        { BottomBar.Toast("请靠近「" + WzNames.Npc(npc.id) + "」后再对话"); return; }
        NpcField.Interact(npc.id);
    }

    static void DrawPlayerQuestAlert()
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        var map = NpcField.Map;
        var cam = Camera.main;
        if (player == null || map == null || cam == null) return;
        // 当前地图确有可接任务时才提示，点击仍走同一套任务条件与源端脚本。
        FieldNpc target = null;
        foreach (var npc in map.npcs)
            if (npc.hide == 0 && NpcField.MarkerOf(npc.id).start) { target = npc; break; }
        if (target == null) return;
        var sp = cam.WorldToScreenPoint(new Vector3(player.FeetPos.x, player.FeetPos.y + 1.3f, 0f));
        if (sp.z < 0f) return;
        var anchor = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale);
        var rect = DrawQuestIcon("Effect/BasicEff.img/QuestAlert/Default", anchor);
        if (Event.current.type == EventType.Repaint && Pointer.Clicked && CanClickField && rect.Contains(Pointer.Pos))
            InteractNearby(target);
    }

    // ——————————————— 对话框 ———————————————
    static void DrawDialog(NpcSession session)
    {
        EnsureStyles();
        float w = Mathf.Min(600f, UiKit.W - 36f);
        float h = Mathf.Min(330f, UiKit.H - BottomBar.Height - 60f);
        var rect = new Rect((UiKit.W - w) * 0.5f, UiKit.H - BottomBar.Height - h - 12f, w, h);

        NativeUi.Panel(rect);

        // 标题（NPC 名）
        string npcName = WzNames.Npc(session.NpcId);
        title.normal.textColor = new Color(0.98f, 0.90f, 0.55f);
        GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, rect.width - 90f, 24f), npcName, title);

        var closeSize = BtnSize("BtClose");
        var closeRect = new Rect(rect.xMax - closeSize.x - 12f, rect.y + 10f, closeSize.x, closeSize.y);
        if (NativeUi.Button(closeRect, BtnRoot + "BtClose"))
        {
            session.Close();
            return;
        }

        float buttonsH = BtnSize("BtOK").y + 10f;
        var view = new Rect(rect.x + 18f, rect.y + 40f, rect.width - 36f, rect.height - 40f - buttonsH);
        float contentW = view.width - 18f;

        var content = session.Dialog.Content;
        float textH = body.CalcHeight(new GUIContent(content.Body), contentW);
        int count = content.Selections.Count;
        float listH = count * 26f;
        float totalH = Mathf.Max(view.height, textH + listH + 6f);

        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, contentW + 18f, totalH));
        GUI.Label(new Rect(0f, 0f, contentW, textH), content.Body, body);

        float y = textH + 6f;
        for (int i = 0; i < count; i++)
        {
            var sel = content.Selections[i];
            var row = new Rect(0f, y, contentW, 24f);
            if (SelectionRow(row, sel.Text))
            {
                GUI.EndScrollView();
                session.Respond(1, 1, sel.Index);
                return;
            }
            y += 26f;
        }
        GUI.EndScrollView();

        // 底部按钮
        float bx = rect.xMax - 18f;
        float by = rect.yMax - buttonsH + 2f;

        switch (session.Dialog.Mode)
        {
            case NpcDialogMode.Next:
                if (Button(ref bx, by, "BtNext")) session.Respond(1, 1, -1);
                break;

            case NpcDialogMode.Prev:
                if (Button(ref bx, by, "BtPrev")) session.Respond(0, 1, -1);
                break;

            case NpcDialogMode.NextPrev:
                if (Button(ref bx, by, "BtNext")) session.Respond(1, 1, -1);
                if (Button(ref bx, by, "BtPrev")) session.Respond(0, 1, -1);
                break;

            case NpcDialogMode.Ok:
                if (Button(ref bx, by, "BtOK")) session.Respond(-1, 0, -1);
                break;

            case NpcDialogMode.YesNo:
                if (Button(ref bx, by, "BtNo")) session.Respond(0, 0, -1);
                if (Button(ref bx, by, "BtYes")) session.Respond(1, 0, -1);
                break;

            case NpcDialogMode.AcceptDecline:
                if (Button(ref bx, by, "BtQNo")) session.Respond(0, 0, -1);
                if (Button(ref bx, by, "BtQYes")) session.Respond(1, 0, -1);
                break;

            case NpcDialogMode.GetText:
            case NpcDialogMode.GetNumber:
                DrawInput(session, rect, by);
                break;

            case NpcDialogMode.Simple:
                if (Button(ref bx, by, "BtClose")) session.Respond(-1, 0, -1);
                break;
        }
    }

    static void DrawInput(NpcSession session, Rect rect, float by)
    {
        var inputRect = new Rect(rect.x + 18f, by - 2f, rect.width - 140f, 24f);
        GUI.SetNextControlName("npcInput");
        string value;
        if (session.Dialog.Mode == NpcDialogMode.GetNumber)
        {
            value = GUI.TextField(inputRect, session.Dialog.Input);
            value = FilterDigits(value, session.Dialog.NumberMin, session.Dialog.NumberMax);
        }
        else value = GUI.TextField(inputRect, session.Dialog.Input, 120);

        session.Dialog.Input = value;
        var e = Event.current;
        bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

        var okRect = new Rect(rect.xMax - 130f, by - 2f, 60f, 24f);
        if (NativeUi.Button(okRect, BtnRoot + "BtOK") || enter)
            session.Respond(1, 0, 0);
    }

    static string FilterDigits(string s, int min, int max)
    {
        string digits = "";
        for (int i = 0; i < s.Length && digits.Length < 9; i++) if (char.IsDigit(s[i])) digits += s[i];
        int v;
        if (!int.TryParse(digits, out v)) return "";
        if (v < min) v = min;
        if (v > max) v = max;
        return v.ToString();
    }

    // 右对齐排按钮：每次往左挪
    static bool Button(ref float right, float y, string node)
    {
        var size = BtnSize(node);
        var r = new Rect(right - size.x, y, size.x, size.y);
        right -= size.x + 6f;
        return NativeUi.Button(r, BtnRoot + node);
    }

    static Vector2 BtnSize(string node)
    {
        var tex = Art.Tex("wz://" + BtnRoot + node + "/normal/0");
        return tex != null ? new Vector2(tex.width, tex.height) : new Vector2(58f, 22f);
    }

    static bool SelectionRow(Rect rect, string text)
    {
        bool hover = rect.Contains(Pointer.Pos);
        var tex = Art.Tex("wz://UI/UIWindow.img/UtilDlgEx/" + (hover ? "list1" : "list0"));
        if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
        else UiKit.Fill(rect, hover ? new Color(0.20f, 0.26f, 0.36f, 0.9f) : new Color(0.14f, 0.16f, 0.20f, 0.9f));
        rowStyle.normal.textColor = hover ? new Color(1f, 0.92f, 0.6f) : new Color(0.92f, 0.94f, 1f);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 2f, rect.width - 14f, rect.height), text, rowStyle);
        return Event.current.type == EventType.Repaint && Pointer.Released && hover;
    }

    static void EnsureStyles()
    {
        if (body != null) return;
        body = new GUIStyle { fontSize = 13, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft };
        body.normal.textColor = new Color(0.12f, 0.12f, 0.14f);

        title = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold };
        rowStyle = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        hint = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleCenter };
    }

    // 底部提示：靠近 NPC 时显示「↑ 对话」
    public static void DrawHint()
    {
        if (NpcSession.Active != null && NpcSession.Active.Dialog.Visible) return;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player == null) return;
        var npc = NpcField.Nearest(player.FeetPos);
        if (npc == null) return;
        EnsureStyles();
        hint.normal.textColor = new Color(1f, 0.95f, 0.7f);
        GUI.Label(new Rect(UiKit.W * 0.5f - 120f, UiKit.H - BottomBar.Height - 54f, 240f, 24f),
                  "↑ / 点击 与 " + WzNames.Npc(npc.id) + " 对话", hint);
    }
}
