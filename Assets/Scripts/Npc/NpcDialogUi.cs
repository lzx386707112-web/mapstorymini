using UnityEngine;

// 对话窗口 + NPC 任务图标。图片全部来自原版 UI/UIWindow.img/UtilDlgEx。
// 正文排版/滚动在 DialogFlow（#L 选项是行内链接，滚动条用原版 VScr4）。
public static class NpcDialogUi
{
    const string BtnRoot = "UI/UIWindow.img/UtilDlgEx/";

    static float scrollY;
    static GUIStyle title, hint;

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
        !GameSideMenu.Blocking && !VirtualControlsEditor.Visible && !KeyConfigWindow.Visible && !VirtualControls.BlockingPointer &&
        NpcSession.Active == null && GameLoop.I != null && !GameLoop.I.gameOver &&
        GameLoop.I.field != null && !PlayerCtrl.InputLocked;

    static void HandleNpcClick()
    {
        if (Event.current.type != EventType.Repaint || !Pointer.DoubleClicked || !CanClickField) return;
        if (Pointer.Pos.y >= UiKit.H - BottomBar.Height) return;
        var npc = NpcField.Pick(Pointer.Pos);
        if (npc != null) { Pointer.ConsumeClick(); NpcField.Interact(npc.id); }
    }

    static void DrawPlayerQuestAlert()
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        var map = NpcField.Map;
        var cam = Camera.main;
        if (player == null || map == null || cam == null) return;
        if (NpcField.QuestAlerts().Count == 0) return;
        var sp = cam.WorldToScreenPoint(new Vector3(player.FeetPos.x, player.FeetPos.y + 1.3f, 0f));
        if (sp.z < 0f) return;
        var anchor = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale);
        var rect = DrawQuestIcon("Effect/BasicEff.img/QuestAlert/Default", anchor);
        if (Event.current.type == EventType.Repaint && Pointer.Clicked && CanClickField && rect.Contains(Pointer.Pos))
        {
            Pointer.ConsumeClick();
            NpcField.ShowQuestAlerts();
        }
    }

    // ——————————————— 对话框 ———————————————
    static void DrawDialog(NpcSession session)
    {
        EnsureStyles();
        if (session.Dialog.ScrollReset) { scrollY = 0f; session.Dialog.ScrollReset = false; }
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        { session.Cancel(); e.Use(); return; }
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyMap.Of(GameAction.NpcTalk)) &&
            (session.Dialog.Mode == NpcDialogMode.Next || session.Dialog.Mode == NpcDialogMode.NextPrev || session.Dialog.Mode == NpcDialogMode.Ok))
        { session.Reply(1); e.Use(); return; }

        float w = Mathf.Min(529f, UiKit.W - 20f);          // 原端窗口固定 529 宽
        var content = session.Dialog.Content;
        float contentH = DialogFlow.ContentHeight(content, w);
        // 窗口高度跟正文走（原端也是这么自适应），超过一屏才滚动
        float h = Mathf.Clamp(DialogFlow.PaperY + 8f + contentH + 8f + DialogFlow.PaperBottom,
                              220f, UiKit.H - BottomBar.Height - 16f);
        var rect = new Rect((UiKit.W - w) * .5f, (UiKit.H - h) * .5f, w, h);
        NativeUi.DialogPanel(rect);
        DrawPortrait(session.NpcId, rect);

        var closeSize = BtnSize("BtClose");
        if (NativeUi.Button(new Rect(rect.x + 10f, rect.yMax - 27f, closeSize.x, closeSize.y), BtnRoot + "BtClose"))
        { session.Cancel(); return; }

        int picked = DialogFlow.DrawPane(rect, content, ref scrollY);
        if (picked >= 0) { session.Reply(1, picked); return; }

        // 底部按钮：原端放在 s 的蓝灰底板上（窗口底往上 30..6 行）
        float bx = rect.xMax - 18f;
        float by = rect.yMax - 27f;

        switch (session.Dialog.Mode)
        {
            case NpcDialogMode.Next:
                if (Button(ref bx, by, "BtNext")) session.Reply(1);
                break;

            case NpcDialogMode.Prev:
                if (Button(ref bx, by, "BtPrev")) session.Reply(0);
                break;

            case NpcDialogMode.NextPrev:
                if (Button(ref bx, by, "BtNext")) session.Reply(1);
                if (Button(ref bx, by, "BtPrev")) session.Reply(0);
                break;

            case NpcDialogMode.Ok:
                if (Button(ref bx, by, "BtOK")) session.Reply(1);
                break;

            case NpcDialogMode.YesNo:
                if (Button(ref bx, by, "BtNo")) session.Reply(0);
                if (Button(ref bx, by, "BtYes")) session.Reply(1);
                break;

            case NpcDialogMode.AcceptDecline:
                if (Button(ref bx, by, "BtQNo")) session.Reply(0);
                if (Button(ref bx, by, "BtQYes")) session.Reply(1);
                break;

            case NpcDialogMode.GetText:
            case NpcDialogMode.GetNumber:
                DrawInput(session, rect, by);
                break;

            case NpcDialogMode.Simple:
                if (session.Dialog.IsMenu && Button(ref bx, by, "BtNext"))
                {
                    foreach (var selection in content.Selections)
                        if (selection.HeaderArt == null) { session.Reply(1, selection.Index); break; }
                }
                break;
        }
    }

    static void DrawPortrait(int npcId, Rect rect)
    {
        if (npcId <= 0) return;
        int speakerId = npcId;
        var npc = WzAssets.FileRoot("Npc/" + npcId.ToString("D7") + ".img");
        var seen = new System.Collections.Generic.HashSet<int>();
        while (npc.Get("info/link") != null && seen.Add(npcId))
        {
            npcId = npc.Int("info/link");
            npc = WzAssets.FileRoot("Npc/" + npcId.ToString("D7") + ".img");
        }
        var frames = WzAssets.Frames(npc.Get("stand"));
        float feet = rect.y + DialogFlow.PaperY + (rect.height - DialogFlow.PaperY - DialogFlow.PaperBottom) * .5f;
        if (frames.Count > 0)
        {
            float total = 0;
            foreach (var frame in frames) total += Mathf.Max(1, Mathf.Abs(frame.Int("delay", 120)));
            float time = Mathf.Repeat(Time.unscaledTime * 1000, total);
            var current = frames[0];
            foreach (var frame in frames)
            {
                current = frame;
                time -= Mathf.Max(1, Mathf.Abs(frame.Int("delay", 120)));
                if (time < 0) break;
            }
            var tex = WzAssets.Texture(current);
            var origin = WzAssets.Origin(current);
            GUI.DrawTexture(new Rect(rect.x + 78f - origin.x, feet - origin.y, tex.width, tex.height), tex);
        }
        var bar = Art.Tex("wz://" + BtnRoot + "bar");
        var nameRect = new Rect(rect.x + 18f, feet + 2, 121, 19);
        GUI.DrawTexture(nameRect, bar);
        title.fontSize = 12;
        title.alignment = TextAnchor.MiddleCenter;
        title.normal.textColor = Color.white;
        GUI.Label(nameRect, WzNames.Npc(speakerId), title);
    }

    static void DrawInput(NpcSession session, Rect rect, float by)
    {
        var inputRect = new Rect(rect.x + 160f, by - 2f, rect.width - 250f, 24f);
        GUI.SetNextControlName("npcInput");
        string value;
        if (session.Dialog.Mode == NpcDialogMode.GetNumber)
        {
            value = GUI.TextField(inputRect, session.Dialog.Input);
            value = FilterDigits(value);
        }
        else value = GUI.TextField(inputRect, session.Dialog.Input, 120);

        session.Dialog.Input = value;
        var e = Event.current;
        bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

        var okRect = new Rect(rect.xMax - 70f, by - 2f, 44f, 18f);
        if (NativeUi.Button(okRect, BtnRoot + "BtOK") || enter)
        {
            int selection = -1;
            if (session.Dialog.Mode == NpcDialogMode.GetNumber &&
                (!int.TryParse(value, out selection) || selection < session.Dialog.NumberMin || selection > session.Dialog.NumberMax))
            { BottomBar.Toast("请输入 " + session.Dialog.NumberMin + " 到 " + session.Dialog.NumberMax + " 之间的数字"); return; }
            if (enter) e.Use();
            session.Reply(1, selection);
        }
    }

    static string FilterDigits(string s)
    {
        string digits = "";
        for (int i = 0; i < s.Length && digits.Length < 11; i++)
            if (char.IsDigit(s[i]) || (i == 0 && s[i] == '-')) digits += s[i];
        return digits;
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

    static void EnsureStyles()
    {
        if (title != null) return;
        title = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold };
        hint = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleCenter };
    }

    // Proximity is only used to pick a keyboard target, not to gate mouse talk.
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
                  "Space / 双击 与 " + WzNames.Npc(npc.id) + " 对话", hint);
    }
}
