using System;
using UnityEngine;

// 便签窗（源端 CUI Memo；素材 UI/UIWindow.img/Memo，窗 295x240）。
// 素材不带布局参数（WZ 全部 origin=0,0），按美术几何推导布局：
//   backgrnd3(295x168) = 便签列表区（y=26 起），底部 46px = 按钮行；
//   innerTop/innerCenter/innerBottom(260 宽) = 写便签的输入区背景；check0/check1 = 行复选框。
// 服务端语义见 NativeNoteService（NoteService.java + NoteActionHandler.java + MEMO_RESULT）：
//   sub 3 = 未读便签列表（登录/收礼提醒时下发）、sub 4 = 发送成功回执；
//   回复送礼便签 sendWithFame（收件人名望 +1）消耗一次 availableNotes（收礼领取时累计）；
//   丢弃便签按每条的 fame 累计加名望（源端读库结算）。
// 入口：进入游戏时有未读便签自动弹出（PlayerLoggedinHandler noteService.show 的等价物）；
//   窗内 BtSave 可回复（availableNotes > 0 时）。源端 509 离线消息道具入口因 GMS083 素材缺失不接。
public static class NativeMemoWindow
{
    public static bool Visible { get; private set; }

    enum Mode { Read, Detail, Write }
    static Mode mode = Mode.Read;
    static MemoNote[] notes = Array.Empty<MemoNote>();
    static bool[] checkedList = Array.Empty<bool>();
    static MemoNote detail;                 // Detail 模式当前查看的便签
    static string target = "";              // Write 模式收件人
    static string message = "";             // Write 模署正文
    static bool busy;
    static string hint = "";

    const int WinW = 295, WinH = 240;
    const int ListY = 26, ListH = 168, RowH = 28;
    static Vector2 pos = new Vector2(-1f, -1f);
    static bool dragging;
    static Vector2 dragOff;

    static float S => BottomBar.S;
    static Texture2D Wz(string node) => Art.Tex("wz://UI/UIWindow.img/Memo/" + node);
    static Rect At(float x, float y, float w, float h)
        => new Rect((pos.x + x) * S, (pos.y + y) * S, w * S, h * S);

    static GUIStyle Style(int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        var st = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(7, Mathf.RoundToInt(size * S)),
            alignment = anchor,
            clipping = TextClipping.Clip,
        };
        st.normal.textColor = color;
        return st;
    }

    static GUIStyle Field() => new GUIStyle
    {
        font = UiKit.Font, fontSize = 12, clipping = TextClipping.Clip, alignment = TextAnchor.UpperLeft,
        normal = { textColor = Color.black },
    };

    public static void Open()
    {
        Visible = true;
        mode = Mode.Read;
        busy = false;
        hint = "";
        target = "";
        message = "";
        detail = null;
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, WinH), S);
        Refresh();
    }

    // 拉取未读便签（源端 ShowNotes：服务端只推给收件人）
    public static void Refresh()
    {
        if (!GameSession.InGame) return;
        var res = GameSession.Net.Memo(new MemoRequest { characterId = GameSession.CharacterId, action = 2 });
        notes = res?.notes ?? Array.Empty<MemoNote>();
        checkedList = new bool[notes.Length];
    }

    public static void Close() => Visible = false;
    public static void Reset() => Close();

    public static void Draw()
    {
        if (!Visible) return;
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        NativeUi.RegisterWindow("NativeMemoWindow",
            new Rect(pos.x * S, pos.y * S, WinW * S, WinH * S), () => Visible);

        var bg = Wz("backgrnd");
        if (bg != null) GUI.DrawTexture(new Rect(pos.x * S, pos.y * S, bg.width * S, bg.height * S), bg);
        var bg2 = Wz("backgrnd2");
        if (bg2 != null) GUI.DrawTexture(new Rect(pos.x * S, pos.y * S, bg2.width * S, bg2.height * S), bg2);
        // 无自带关闭钮：右上自画 ×（项目统一口径）
        if (NativeUi.Button(At(WinW - 17, 3, 12, 12), "UI/Basic.img/BtClose")) { Close(); return; }

        switch (mode)
        {
            case Mode.Read: DrawRead(); break;
            case Mode.Detail: DrawDetail(); break;
            case Mode.Write: DrawWrite(); break;
        }

        if (!string.IsNullOrEmpty(hint))
            GUI.Label(At(18, WinH - 40, WinW - 36, 14), hint, Style(11, new Color(0.55f, 0.15f, 0.15f)));

        if (Event.current.type == EventType.Repaint && Pointer.Pressed
            && new Rect(pos.x * S, pos.y * S, WinW * S, 18 * S).Contains(Pointer.Pos))
        {
            dragging = true;
            dragOff = Pointer.Pos / S - pos;
        }
        if (dragging && Pointer.Down)
        {
            pos = Pointer.Pos / S - dragOff;
            pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, WinH), S);
        }
        if (!Pointer.Down) dragging = false;
    }

    // 列表区：backgrnd3 底 + 每行 check + 发件人/时间 + 正文截断；点击行勾选
    static void DrawRead()
    {
        var listBg = Wz("backgrnd3");
        if (listBg != null) GUI.DrawTexture(At(0, ListY, listBg.width, listBg.height), listBg);
        GUI.Label(At(18, ListY + 4, WinW - 36, 14),
            notes.Length == 0 ? "没有未读的便签。" : "共 " + notes.Length + " 张未读便签（点击勾选）",
            Style(11, new Color(0.35f, 0.3f, 0.2f)));

        for (int i = 0; i < notes.Length; i++)
        {
            float y = ListY + 22 + i * RowH;
            if (y + RowH > ListY + ListH) break;
            var n = notes[i];
            var line = new Rect(0, y, WinW, RowH);
            bool clicked = Event.current.type == EventType.Repaint && Pointer.Clicked
                && At(line.x, line.y, line.width, line.height).Contains(Pointer.Pos);
            if (clicked) { Pointer.ConsumeClick(); checkedList[i] = !checkedList[i]; }

            var check = Wz(checkedList[i] ? "check1" : "check0");
            if (check != null) GUI.DrawTexture(At(20, y + 8, check.width, check.height), check);
            var rule = Wz("line");
            if (rule != null) GUI.DrawTexture(At(38, y + RowH - 1, rule.width, rule.height), rule);
            GUI.Label(At(38, y + 2, 120, 13), n.from, Style(12, new Color(0.15f, 0.2f, 0.5f)));
            GUI.Label(At(158, y + 2, 120, 13), TimeText(n.timestamp), Style(11, new Color(0.45f, 0.42f, 0.35f)));
            GUI.Label(At(38, y + 14, 240, 12), n.message, Style(11, new Color(0.25f, 0.22f, 0.16f)));
        }

        // 底部按钮行：查看(BtOpen) / 丢弃(BtDelete) / 回复写便签(BtSave，需可用次数)
        if (NativeUi.Button(At(28, WinH - 26, 64, 19), "UI/UIWindow.img/Memo/BtOpen") && !busy)
        {
            int sel = Selected();
            if (sel < 0) hint = "先勾选一张便签。";
            else { detail = notes[sel]; mode = Mode.Detail; hint = ""; }
        }
        if (NativeUi.Button(At(104, WinH - 26, 39, 19), "UI/UIWindow.img/Memo/BtDelete") && !busy)
        {
            var ids = CheckedIds();
            if (ids.Length == 0) hint = "先勾选要丢弃的便签。";
            else { busy = true; Discard(ids); }
        }
        bool canReply = NativeNoteService.AvailableNotes > 0;
        var save = Wz("BtSave/disabled/0");
        GUI.enabled = canReply && !busy;
        if (NativeUi.Button(At(WinW - 70, WinH - 25, 38, 18), "UI/UIWindow.img/Memo/BtSave") && canReply)
        {
            target = "";
            message = "";
            mode = Mode.Write;
            hint = "";
        }
        GUI.enabled = true;
        if (canReply)
            GUI.Label(At(WinW - 120, WinH - 25, 48, 14), "×" + NativeNoteService.AvailableNotes,
                Style(11, new Color(0.3f, 0.3f, 0.3f), TextAnchor.MiddleRight));
    }

    static void DrawDetail()
    {
        var listBg = Wz("backgrnd3");
        if (listBg != null) GUI.DrawTexture(At(0, ListY, listBg.width, listBg.height), listBg);
        if (detail == null) { mode = Mode.Read; return; }
        GUI.Label(At(20, ListY + 8, 150, 14), "发件人：" + detail.from, Style(12, new Color(0.15f, 0.2f, 0.5f)));
        GUI.Label(At(20, ListY + 24, 250, 14), TimeText(detail.timestamp), Style(11, new Color(0.45f, 0.42f, 0.35f)));
        GUI.Label(At(20, ListY + 44, 255, 110), detail.message,
            Style(12, new Color(0.2f, 0.18f, 0.12f), TextAnchor.UpperLeft));
        if (NativeUi.Button(At((WinW - 64) / 2f, WinH - 26, 64, 19), "UI/UIWindow.img/Memo/BtOpen"))
        {
            mode = Mode.Read;
            Refresh();
        }
    }

    // 写便签（回复送礼）：innerTop/innerCenter/innerBottom 拼 260 宽输入区
    static void DrawWrite()
    {
        float x = (WinW - 260) / 2f;
        var top = Wz("innerTop");
        if (top != null) GUI.DrawTexture(At(x, ListY, 260, 24), top);
        var center = Wz("innerCenter");
        if (center != null) for (float y = ListY + 24; y < ListY + 100; y += 18) GUI.DrawTexture(At(x, y, 260, 18), center);
        var bottom = Wz("innerBottom");
        if (bottom != null) GUI.DrawTexture(At(x, ListY + 100, 260, 8), bottom);

        GUI.Label(At(x + 6, ListY + 3, 40, 14), "收件人", Style(10, new Color(0.4f, 0.36f, 0.28f)));
        target = GUI.TextField(At(x + 52, ListY + 4, 200, 15), target ?? "", 13, Field());
        GUI.Label(At(x + 6, ListY + 30, 60, 14), "正文(73字)", Style(10, new Color(0.4f, 0.36f, 0.28f)));
        message = GUI.TextField(At(x + 52, ListY + 30, 200, 60), message ?? "", 73, Field());

        if (NativeUi.Button(At(x + 92, ListY + 112, 38, 18), "UI/UIWindow.img/Memo/BtSave") && !busy)
        {
            if (string.IsNullOrEmpty(target.Trim())) hint = "请输入收件角色名。";
            else if (string.IsNullOrEmpty(message) || message.Length > 73) hint = "便签须为1～73个字符。";
            else { busy = true; SendReply(); }
        }
        if (NativeUi.Button(At(x + 140, ListY + 112, 38, 18), "UI/Basic.img/BtCancel2"))
        {
            mode = Mode.Read;
            hint = "";
        }
    }

    static int Selected()
    {
        for (int i = 0; i < checkedList.Length; i++) if (checkedList[i]) return i;
        return -1;
    }

    static int[] CheckedIds()
    {
        var ids = new System.Collections.Generic.List<int>();
        for (int i = 0; i < checkedList.Length && i < notes.Length; i++) if (checkedList[i]) ids.Add(notes[i].id);
        return ids.ToArray();
    }

    static void Discard(int[] ids)
    {
        var res = GameSession.Net.Memo(new MemoRequest { characterId = GameSession.CharacterId, action = 1, discardIds = ids });
        busy = false;
        if (res == null || !res.ok) { hint = res?.error ?? "丢弃失败"; return; }
        if (res.fameGain > 0)
        {
            BottomBar.AddChat("丢弃便签，名望 +" + res.fameGain, BottomBar.ChatYellow);
            GameSession.ReloadAfterCommand();
        }
        mode = Mode.Read;
        Refresh();
    }

    // NoteActionHandler action 0：回复送礼便签（sendWithFame + decreaseNotes）
    static void SendReply()
    {
        var res = GameSession.Net.Memo(new MemoRequest { characterId = GameSession.CharacterId, action = 0, target = target, message = message });
        busy = false;
        if (res == null || !res.ok) { hint = res?.error ?? "发送失败"; return; }
        BottomBar.AddChat("便签已寄出。", BottomBar.ChatYellow);
        mode = Mode.Read;
        Refresh();
    }

    // 时间戳 → 本地时间文本（素材 FontTime 小字模省略：字模只有数字/冒号，日期排版成本高）
    static string TimeText(long unixMs)
    {
        try { return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("MM-dd HH:mm"); }
        catch { return ""; }
    }
}
