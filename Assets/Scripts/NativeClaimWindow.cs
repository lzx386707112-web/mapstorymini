using UnityEngine;

// 举报窗（源端 SUE_CHARACTER / CUIReport；素材 UI/UIWindow.img/Claim）。
// 布局（1x，窗 268x345，底图标签已是中文端素材）：
//   举报原因 → 下拉框 box1(157x19) @(107,155)；举报角色名 → 输入框 box2(137x17) @(107,176)；
//   举报情况说明 → 多行输入 @(107,197)-(265,262)（底图自带白框）；
//   底部两个类型按钮（各 122 宽槽居中）：BtCClaim=对话举报(type1) / BtPClaim=非法程序申报(type0)，
//   与 notice 页两个板块一一对应；
//   notice 页（268x429）= 类型确认页：列出可告发事由，底部 BtClaim(检举)/BtCancel(返回)。
// 服务端语义见 NativeReportService（ReportHandler.java）：次数 10/会话、费用 300 金币、
//   成功无回包（客户端本地显示「已成功登记」，源端字符串池 3380）。
// 入口：角色右键菜单 ContextMenu/BtClaim（源端同款），受害者名预填。
public static class NativeClaimWindow
{
    const string Reasons = "辱骂/淫秽词语/泄露个人信息|广告：业务目的的网站宣传|欺诈：非法网站宣传及诱导玩家登陆|现金交易：物品、帐号、现金等交易情况|冒充运营人员";

    public static bool Visible { get; private set; }

    static float S => BottomBar.S;
    static Vector2 pos = new Vector2(-1f, -1f);
    static bool notice;                 // false = 填单页 / true = notice 确认页
    static int type = 1;                // 1 对话举报 / 0 非法程序
    static int reason;                  // 对话举报理由 0..4
    static bool reasonOpen;
    static string victim = "";
    static string description = "";

    const int WinW = 268, WinH = 345;

    static Texture2D Wz(string node) => Art.Tex("wz://UI/UIWindow.img/Claim/" + node);

    static Rect At(float x, float y, float w, float h)
        => new Rect((pos.x + x) * S, (pos.y + y) * S, w * S, h * S);

    static GUIStyle Style(int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
    {
        var st = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(7, Mathf.RoundToInt(size * S)),
            alignment = anchor,
            wordWrap = wrap,
        };
        st.normal.textColor = color;
        return st;
    }

    public static void Open(string victimName)
    {
        Visible = true;
        notice = false;
        type = 1;
        reason = 0;
        reasonOpen = false;
        victim = victimName ?? "";
        description = "";
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, notice ? 429 : WinH), S);
    }

    public static void Close()
    {
        Visible = false;
        reasonOpen = false;
    }

    public static void Reset() => Close();

    public static void Draw()
    {
        if (!Visible) return;
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        NativeUi.RegisterWindow("NativeClaimWindow",
            new Rect(pos.x * S, pos.y * S, WinW * S, (notice ? 429 : WinH) * S), () => Visible);

        var bg = Wz(notice ? "notice" : "backgrnd");
        if (bg != null) GUI.DrawTexture(new Rect(pos.x * S, pos.y * S, bg.width * S, bg.height * S), bg);
        // 无自带关闭钮：右上自画 ×（项目对这类窗的统一口径）
        if (NativeUi.Button(At(WinW - 17, 3, 12, 12), "UI/Basic.img/BtClose")) { Close(); return; }

        if (notice) DrawNotice();
        else DrawForm();

        // 拖动（标题条 y 0..18）
        HandleDrag();
    }

    static void HandleDrag()
    {
        float h = notice ? 429 : WinH;
        if (Event.current.type == EventType.Repaint && Pointer.Pressed
            && new Rect(pos.x * S, pos.y * S, WinW * S, 18 * S).Contains(Pointer.Pos))
        {
            dragging = true;
            dragOff = Pointer.Pos / S - pos;
        }
        if (dragging && Pointer.Down)
        {
            pos = Pointer.Pos / S - dragOff;
            pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, h), S);
        }
        if (!Pointer.Down) dragging = false;
    }
    static bool dragging;
    static Vector2 dragOff;

    static void DrawForm()
    {
        if (reasonOpen) DrawReasonList();
        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;

        // 举报原因下拉框（box1 157x19）
        var dd = At(107, 155, 157, 19);
        var box1 = Wz("box1");
        if (box1 != null) GUI.DrawTexture(dd, box1);
        GUI.Label(At(112, 157, 147, 16), Reasons.Split('|')[reason], Style(12, new Color(0.10f, 0.11f, 0.15f)));
        if (press && dd.Contains(Pointer.Pos)) reasonOpen = !reasonOpen;

        // 举报角色名输入框（box2 137x17）
        var nameRect = At(107, 176, 137, 17);
        var box2 = Wz("box2");
        if (box2 != null) GUI.DrawTexture(nameRect, box2);
        var nameStyle = Style(12, new Color(0.10f, 0.11f, 0.15f));
        string n = GUI.TextField(nameRect, victim, 12, nameStyle);
        if (n != victim) victim = n;

        // 举报情况说明（底图自带白框 @(107,197)-(265,262)）
        var descRect = At(110, 200, 152, 60);
        var descStyle = Style(12, new Color(0.10f, 0.11f, 0.15f), TextAnchor.UpperLeft, wrap: true);
        string d = GUI.TextArea(descRect, description, 200, descStyle);
        if (d != description) description = d;

        // 底部类型按钮：对话举报 / 非法程序申报（各 122 宽槽内居中，108x17）
        if (NativeUi.Button(At(7, 313, 108, 17), "UI/UIWindow.img/Claim/BtCClaim")) { type = 1; notice = true; }
        if (NativeUi.Button(At(141, 313, 108, 17), "UI/UIWindow.img/Claim/BtPClaim")) { type = 0; notice = true; }
    }

    static void DrawReasonList()
    {
        var items = Reasons.Split('|');
        float y = 174f;
        var listRect = At(107, y, 157, items.Length * 17f + 2);
        UiKit.Fill(listRect, new Color(1f, 1f, 1f));
        // 1px 边框（UiKit 没有 Frame，用四条填充拼）
        UiKit.Fill(new Rect(listRect.x, listRect.y, listRect.width, 1), new Color(0.55f, 0.58f, 0.65f));
        UiKit.Fill(new Rect(listRect.x, listRect.yMax - 1, listRect.width, 1), new Color(0.55f, 0.58f, 0.65f));
        UiKit.Fill(new Rect(listRect.x, listRect.y, 1, listRect.height), new Color(0.55f, 0.58f, 0.65f));
        UiKit.Fill(new Rect(listRect.xMax - 1, listRect.y, 1, listRect.height), new Color(0.55f, 0.58f, 0.65f));
        for (int i = 0; i < items.Length; i++)
        {
            var row = At(108, y + 1 + i * 17, 155, 17);
            bool hover = row.Contains(Pointer.Pos);
            if (hover) UiKit.Fill(row, new Color(0.85f, 0.88f, 0.95f));
            GUI.Label(row, items[i], Style(12, new Color(0.10f, 0.11f, 0.15f)));
            if (hover && Event.current.type == EventType.Repaint && Pointer.Pressed)
            {
                reason = i;
                reasonOpen = false;
                Pointer.ConsumeClick();
            }
        }
    }

    static void DrawNotice()
    {
        // notice 底图已列出可告发事由；底部 BtClaim(检举)/BtCancel(返回) 居中一对
        if (NativeUi.Button(At(73, 406, 59, 17), "UI/UIWindow.img/Claim/BtClaim")) Submit();
        if (NativeUi.Button(At(136, 406, 61, 17), "UI/UIWindow.img/Claim/BtCancel")) notice = false;
    }

    static void Submit()
    {
        if (string.IsNullOrEmpty(victim))
        {
            BottomBar.Toast("请填写要举报的角色名。");
            return;
        }
        if (type == 0) reason = 0;   // 非法程序申报不带 reason（源端 type 0 分支不读 reason 之后的字段照收）
        var res = GameSession.Net.Report(new ReportRequest
        {
            characterId = GameSession.CharacterId,
            type = type,
            victim = victim,
            reason = reason,
            description = description,
            // 源端对话举报附带客户端聊天记录（供 GM 审计）
            chatlog = type == 1 ? ChatLogSnapshot() : "",
        });
        if (res.ok)
        {
            BottomBar.AddChat("举报已成功登记。", BottomBar.ChatYellow);
            Close();
            return;
        }
        // 源端 SUE_CHARACTER_RESULT：2 次数用尽 / 4 金币不足（字符串池 3390 / 3388）
        BottomBar.AddChat(res.error, BottomBar.ChatPink);
        if (res.code == 2 || res.code == 4) Close();
    }

    // 最近 30 行聊天记录（源端 type 1 的 chatlog 字段）
    static string ChatLogSnapshot()
    {
        var history = BottomBar.ChatHistory;
        int from = Mathf.Max(0, history.Count - 30);
        var sb = new System.Text.StringBuilder();
        for (int i = from; i < history.Count; i++)
        {
            sb.AppendLine(history[i].text);
        }
        return sb.ToString();
    }
}
