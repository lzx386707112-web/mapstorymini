using System.Collections.Generic;
using MapleDash.Wz;
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
        OnlineTime.Tick();
        DrawNpcNames();
        DrawMarkers();
        DrawPlayerQuestAlert();
        HandleNpcClick();
        var session = NpcSession.Active;
        if (session != null && session.Dialog.Visible) DrawDialog(session);
    }

    // NPC 名字牌（原客户端默认开启「显示 NPC 名字」）：半透明深绿底 + 亮绿 1px 边框 + 黄绿字。
    // 锚点固定为 NPC 的脚点（life/y），与 NPC 图高无关——所有 NPC 名字都在脚下同一条线上。
    // 以前按 stand 首帧 origin.y 贴头顶，结果巨型雕像/矮 NPC 名字高低全不一样，是错的。
    // 任务图标（!/?）仍挂在头顶，和名字分开。
    //
    // 字号是「逻辑像素」：GUI 已经按 UiKit.Scale 缩放过，这里不能再乘 Screen.height。
    // 原来写的是 13 * Screen.height / 768，1080p 下等于 18，比源端（字高约 12 逻辑像素）大了一半。
    public const int NameFontSize = 12;
    public const float NamePadX = 3f;      // 源端牌子左右各留约 2px，很紧
    public const float NamePadY = 2f;      // 上下各 2px
    public const float NameGapPx = 1f;     // 脚点往下 1px，牌子顶边贴着脚底
    static GUIStyle nameStyle;

    // 配色取自源端截图（名字牌内部主色 #33460F、边框 #6BAE4A、字 #D9EB38）
    static readonly Color NameBg = new Color(0.20f, 0.275f, 0.06f, 0.88f);
    static readonly Color NameBorder = new Color(0.42f, 0.68f, 0.29f, 0.95f);
    static readonly Color NameText = new Color(0.85f, 0.92f, 0.22f);
    static readonly Color NameOutline = new Color(0.03f, 0.08f, 0f, 0.95f);

    // 牌子与文字的位置：牌宽 = 文字宽 + NamePadX*2、牌高 = 文字高 + NamePadY*2，水平居中于脚点，牌顶 = 脚点 + NameGapPx。
    // 单独抽出来是为了让 Editor 自检能直接验几何，不必起 GUI。
    public static void NamePlateRects(Vector2 anchor, Vector2 textSize, out Rect plate, out Rect text)
    {
        float w = textSize.x + NamePadX * 2f, h = textSize.y + NamePadY * 2f;
        plate = new Rect(anchor.x - w * .5f, anchor.y + NameGapPx, w, h);
        text = new Rect(plate.x + NamePadX, plate.y + NamePadY, textSize.x, textSize.y);
    }

    static void DrawNpcNames()
    {
        var map = NpcField.Map;
        var cam = Camera.main;
        if (map == null || cam == null) return;
        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label)
            {
                font = UiKit.Font,
                fontSize = NameFontSize,
                alignment = TextAnchor.UpperCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
        }
        for (int i = 0; i < map.npcs.Count; i++)
        {
            var npc = map.npcs[i];
            if (npc.hide != 0) continue;
            string name = WzNames.Npc(npc.id);
            if (string.IsNullOrEmpty(name) || npc.id <= 0) continue;
            float wx = map.WorldX(npc.x);
            float wy = map.WorldY(npc.y);                                  // 脚点，不做任何高度补偿
            var sp = cam.WorldToScreenPoint(new Vector3(wx, wy, 0f));
            if (sp.z < 0f) continue;
            var gui = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale);
            if (gui.x < -60f || gui.x > UiKit.W + 60f || gui.y < -30f || gui.y > UiKit.H + 60f) continue;

            var size = nameStyle.CalcSize(new GUIContent(name));
            NamePlateRects(gui, size, out var plate, out var text);
            UiKit.Fill(plate, NameBorder);                                    // 外框
            UiKit.Fill(new Rect(plate.x + 1f, plate.y + 1f,
                plate.width - 2f, plate.height - 2f), NameBg);                // 内底，四周留 1px 边框

            nameStyle.normal.textColor = NameOutline;                         // 深色描边：四向偏移各画一次
            foreach (var d in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
                GUI.Label(new Rect(text.x + d.x, text.y + d.y, text.width, text.height), name, nameStyle);
            nameStyle.normal.textColor = NameText;
            GUI.Label(text, name, nameStyle);
        }
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
            float wy = map.WorldY(npc.y) + (npc.head + 26f) / Art.PPU;   // 名字行（约 14px）上方，避免图标压在名字上
            var sp = cam.WorldToScreenPoint(new Vector3(wx, wy, 0f));
            if (sp.z < 0f) continue;
            var gui = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale);
            if (gui.x < -40f || gui.x > UiKit.W + 40f || gui.y < -40f || gui.y > UiKit.H + 40f) continue;

            DrawQuestIcon("UI/UIWindow.img/QuestIcon/" + (marker.complete ? "2" : "0"), gui);
        }
    }

    // elapsedMs >= 0 时按「这段动画自己走过的毫秒」取帧——出现动画必须从第 0 帧起播，
    // 不能沿用全局时钟取模（那样会从随机相位开始，灯泡像是闪一下就到了中段）。
    static Rect DrawQuestIcon(string path, Vector2 anchor, float elapsedMs = -1f)
    {
        var frames = WzAssets.Frames(WzAssets.Node(path));
        if (frames.Count == 0) return new Rect();
        float total = 0f;
        foreach (var frame in frames) total += Mathf.Max(1, frame.Int("delay", 120));
        float time = elapsedMs >= 0f ? Mathf.Repeat(elapsedMs, total) : Mathf.Repeat(Time.unscaledTime * 1000f, total);
        var shown = FrameAt(frames, time);
        var rect = FrameRect(shown, anchor);
        GUI.DrawTexture(rect, WzAssets.Texture(shown));
        return rect;
    }

    static ImgNode FrameAt(List<ImgNode> frames, float time)
    {
        var current = frames[0];
        foreach (var frame in frames)
        {
            current = frame;
            time -= Mathf.Max(1, frame.Int("delay", 120));
            if (time < 0f) break;
        }
        return current;
    }

    // 只算矩形不画：点击热区要跟 Default 那颗灯泡的固定大小走。
    // Appear 是很大的闪光（最大 197×178），热区跟着它抖会出现「明明点中了却没反应」。
    static Rect QuestIconRect(string path, Vector2 anchor, float elapsedMs = -1f)
    {
        var frames = WzAssets.Frames(WzAssets.Node(path));
        if (frames.Count == 0) return new Rect();
        float total = 0f;
        foreach (var frame in frames) total += Mathf.Max(1, frame.Int("delay", 120));
        float time = elapsedMs >= 0f ? Mathf.Repeat(elapsedMs, total) : Mathf.Repeat(Time.unscaledTime * 1000f, total);
        return FrameRect(FrameAt(frames, time), anchor);
    }

    static Rect FrameRect(ImgNode frame, Vector2 anchor)
    {
        var tex = WzAssets.Texture(frame);
        var origin = WzAssets.Origin(frame);
        float scale = Screen.height / 768f / UiKit.Scale;
        return new Rect(anchor.x - origin.x * scale, anchor.y - origin.y * scale,
            tex.width * scale, tex.height * scale);
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
        // 灯泡刚冒出来时先播一次 Appear（QuestAlert/Appear），播完转 Default 常驻呼吸
        if (!NpcField.AlertAnim(out string art, out float elapsed)) return;
        var sp = cam.WorldToScreenPoint(new Vector3(player.FeetPos.x, player.FeetPos.y + 1.3f, 0f));
        if (sp.z < 0f) return;
        var anchor = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale);
        DrawQuestIcon(art, anchor, elapsed);
        var rect = QuestIconRect(NpcField.AlertDefault, anchor, 0f);   // 恒定第 0 帧，热区不抖
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
