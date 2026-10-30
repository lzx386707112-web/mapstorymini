using System.Collections.Generic;
using UnityEngine;

// 原版「家族」窗口 = UIWindow.img/UserList 主窗（312×389）的 tab2（家族页）。
//
// 逆向依据（BeiDou.exe，SHA256 1b91cf5b…）：
//   0x919B73 主窗构建：tab 控件 0x919D34 → (6,23) 宽 270；标签位图取 UserList/Tab/enabled|disabled/0..4，
//            0..4 = 好友/组员/家族/家族联盟/拒绝列表；
//   0x91D99D SetTab：0→backgrnd3、1/4→backgrnd、2/3→backgrnd2（家族页用 backgrnd2）；
//   0x90E8E8 GuildInfo 精灵表：+4 guildinfo0 在线组标题、+8 guildinfo1 离线组标题、+0xc guildinfo2 列头、
//            +0x10 guildinfo3 行底、+0x14 guildinfo4 底线、+0x18 guildinfo5 竖线、+0x1c guildinfo6 公告底、
//            +0x20 guildinfo7 列头变体、+0x24 guildmark 徽章底图；默认职位名 Master/Jr.Master/Member；
//   0x90F17C 列表绘制：组标题 27、列头 +31、成员行 +49 步长 18、空组仍留一行、底线 1px、两组间距 10px；
//            文字列 x=10/100/170/210（名字/职业/等级/地位），选中行填充 0xFF244768；
//   0x90F3F9 未入家族分支：以 306 宽居中绘制字符串 350「没有参加的家族」；
//   0x91CF4F 按钮分派：跳转表 index = id-2010（字节表 0x91D44E / 表体 0x91D372），
//            家族段 id 2030..2041 的完整 id→节点→桩→处理函数见本文件「底部按钮」节的表。
// 素材全部直读原端 IMG；窗口内坐标就是原端窗口坐标（312×389），不做二次设计。
public static class NativeGuildWindow
{
    public static bool Visible;

    const int WinW = 312, WinH = 389;

    const string Root = "UI/UIWindow.img/UserList/";
    const string Info = Root + "Guild/GuildInfo/";

    // tab 条：控件 (6,23) 宽 270；标签就是 Tab/enabled|disabled 位图本身（当前页用 enabled）
    const int TabX = 6, TabY = 23, TabW = 270, TabGap = 13, TabH = 23;
    static readonly string[] TabNames = { "好友", "组员", "家族", "家族联盟", "拒绝列表" };

    // ————————————————— 家族页的两个子画布 —————————————————
    // 0x90F17C 把家族页画在两张子画布上，坐标全是画布局部坐标，必须加上画布原点才是窗口坐标：
    //   名条画布 [ebp+8]：306 宽，原点 (3,53)。画 徽章 (left,1) 20×20、家族名 (left+26,3)、
    //     未入家族提示 ((306-w)/2, 3)、公告底 guildinfo6 (0,25) 282×20、公告文字 (8,30)；
    //     left = (306 - (名字宽 + 26)) / 2。
    //   列表画布 [ebp+0xc]：281×192 @(6,113)，列 x = 10/100/170/210（见 0x90F880 起的四列绘制）。
    // 背景 backgrnd2 里 y=52/76 是名条内嵌框的上下深线、53..75 是可用区，20 高徽章正好落在 54..74；
    // 之前把局部坐标当窗口坐标，徽章画到 y=46 正好压在背景烘死的红线上（用户截图里的错位就是这个）。
    const int ContentX = 3, ContentY = 53;
    const int BarW = 306;                       // 名条画布宽 = 312 - 左右各 3
    const int MarkOffsetY = 1, MarkSize = 20;   // 徽章：画布 y+1，20×20
    const int NameGap = 26;                     // 名字 x = left + 0x1a（徽章 20 + 6）
    const int NameOffsetY = 3, NameTextH = 16;  // 名字 y = 画布 y+3
    const int MarkY = ContentY + MarkOffsetY, NameTextY = ContentY + NameOffsetY;

    // 公告底 guildinfo6（282×20）在画布 (0,25)，公告文字 (8,30)；
    // 右侧 Btnotice（20×20，origin 0,0）是独立控件，OnCreate 里给的是 (287,80)
    const int NoticeX = ContentX, NoticeY = ContentY + 25, NoticeW = 282, NoticeH = 20;
    const int NoticeTextX = ContentX + 8, NoticeTextY = ContentY + 30;
    const int NoticeButtonX = 287, NoticeButtonY = 80;

    // 列表视口 281×192，起点 (6,113)
    const int ListX = 6, ListTop = 113, ListW = 281, ListH = 192;
    const int GroupTitleH = 27, HeaderY = 31, HeaderH = 18, RowsY = 49, RowH = 18, GroupGap = 10;
    const int ColName = 10, ColClass = 100, ColLevel = 170, ColRank = 210;
    const int ScrollStep = 33;      // 源端 0x90F17C 的滚轮步长

    static int tab = 2;
    static int selectedId;
    static int scroll;
    static double nextRefresh;
    static bool dragging;
    static float dragFromY;
    static int dragFromScroll;

    static float S => BottomBar.S;
    static CommunityResponse State => NativeCommunityWindow.State;

    static Rect At(Rect win, float x, float y, float w, float h)
        => new Rect(win.x + x * S, win.y + y * S, w * S, h * S);

    static GUIStyle Style(int size, Color color, TextAnchor anchor = TextAnchor.UpperLeft)
    {
        var st = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(7, Mathf.RoundToInt(size * S)),
            alignment = anchor,
            clipping = TextClipping.Clip,
            wordWrap = false,
            padding = new RectOffset(),
        };
        st.normal.textColor = color;
        return st;
    }

    static readonly Color TextOn = new Color(0.08f, 0.08f, 0.10f);
    static readonly Color TextDim = new Color(0.42f, 0.44f, 0.48f);
    static readonly Color TextSelected = new Color(0.93f, 0.94f, 0.96f);
    static readonly Color RowSelected = new Color32(0x24, 0x47, 0x68, 0xFF);

    public static void Toggle()
    {
        if (Visible) { Reset(); return; }
        if (!GameSession.InGame) { BottomBar.Toast("请先选择角色"); return; }
        tab = 2; selectedId = 0; scroll = 0; dragging = false;
        NativeCommunityWindow.Request(CommunityOperation.Read);
        Visible = true;
    }

    public static void Reset() { Visible = false; selectedId = 0; scroll = 0; dragging = false; CloseInput(); NativeGuildGradeWindow.Reset(); }

    static void Tick()
    {
        if (Time.realtimeSinceStartupAsDouble < nextRefresh) return;
        nextRefresh = Time.realtimeSinceStartupAsDouble + 1.0;
        NativeCommunityWindow.Request(CommunityOperation.Read);
    }

    public static void Draw()
    {
        if (!Visible) return;
        var st = State;
        if (st == null) { Visible = false; return; }
        if (!NativeDlgInput.Visible) Tick();
        float s = S;
        var win = new Rect(Mathf.Max(4, (UiKit.W - WinW * s) / 2f), 26, WinW * s, WinH * s);
        string background = tab == 2 || tab == 3 ? "backgrnd2" : tab == 0 ? "backgrnd3" : "backgrnd";
        var texture = Art.Tex("wz://" + Root + background);
        if (texture != null) GUI.DrawTexture(win, texture);

        DrawTabs(win);
        if (tab == 2) DrawGuild(win, st);
        else Centered(win, "该页签尚未迁移", 186);
        NativeDlgInput.Draw();
    }

    // ————————————————— 页签 —————————————————
    static void DrawTabs(Rect win)
    {
        int total = TabGap * (TabNames.Length - 1);
        var widths = new int[TabNames.Length];
        for (int i = 0; i < TabNames.Length; i++) { widths[i] = TabWidth(i); total += widths[i]; }
        int x = TabX + (TabW - total) / 2;
        for (int i = 0; i < TabNames.Length; i++)
        {
            var tex = Art.Tex("wz://" + Root + "Tab/" + (i == tab ? "enabled/" : "disabled/") + i);
            if (tex != null)
                GUI.DrawTexture(At(win, x + (widths[i] - tex.width) / 2f, TabY + (TabH - tex.height) / 2f, tex.width, tex.height), tex);
            var hit = At(win, x, TabY, widths[i], TabH);
            if (!NativeDlgInput.Visible && hit.Contains(Pointer.Pos) && Pointer.Clicked) { Pointer.ConsumeClick(); SwitchTab(i); }
            x += widths[i] + TabGap;
        }
    }

    static int TabWidth(int index)
    {
        var tex = Art.Tex("wz://" + Root + "Tab/enabled/" + index);
        return tex != null ? Mathf.Max(tex.width, 27) : 27;
    }

    static void SwitchTab(int index)
    {
        if (index == tab) return;
        if (index == 0) { Reset(); NativeSocialWindow.Toggle(GameAction.Buddy); return; }
        if (index == 1) { Reset(); NativeSocialWindow.Toggle(GameAction.Party); return; }
        if (index >= 3) { NativeSystemMessage.Drop(5, TabNames[index] + "尚未开放"); return; }
        tab = index; selectedId = 0; scroll = 0;
    }

    // ————————————————— 家族页 —————————————————
    static void DrawGuild(Rect win, CommunityResponse st)
    {
        bool inGuild = st.guildId > 0;
        if (inGuild) DrawNameBar(win, st);
        // 源端 0x90F3F9 未入家族分支：以 306 宽居中画字符串 350「没有参加的家族」，
        // 位置和家族名同一个槽位（画布局部 y=3，也就是名条内嵌框里）。
        else Centered(win, "没有参加的家族", ContentY + NameOffsetY);
        // 公告底在两条分支里都画（0x90F3F9 收尾 jmp 0x90f523 落回 0x90f534 的 guildinfo6 绘制），
        // 只有公告文字和编辑按钮要「有家族」才有意义。
        DrawNoticeBar(win, st, inGuild);
        if (inGuild) DrawList(win, st);
        DrawButtons(win, st, inGuild);
    }

    static void DrawNameBar(Rect win, CommunityResponse st)
    {
        // 源端 0x90F17C：left = (306 - (名字宽 + 0x1a)) / 2，徽章画在 (left, 1)，名字画在 (left+26, 3)。
        // 名字用左上对齐——原端的文字绘制 0x4277ad 就是左上原点，之前用 MiddleLeft 把行盒居中会整体偏低。
        string name = st.guildName ?? "";
        var nameStyle = Style(12, TextOn);
        float textWidth = nameStyle.CalcSize(new GUIContent(name)).x / S;
        float left = ContentX + Mathf.Max(0, (BarW - (textWidth + NameGap)) / 2f);
        var mark = Art.Tex("wz://" + Info + "guildmark");
        if (mark != null) GUI.DrawTexture(At(win, left, MarkY, MarkSize, MarkSize), mark);
        GUI.Label(At(win, left + NameGap, NameTextY, textWidth + 8, NameTextH), name, nameStyle);
    }

    static void DrawNoticeBar(Rect win, CommunityResponse st, bool inGuild)
    {
        var box = Art.Tex("wz://" + Info + "guildinfo6");
        if (box != null) GUI.DrawTexture(At(win, NoticeX, NoticeY, NoticeW, NoticeH), box);
        // 公告文字局部 (8,30)：左内缩 8、距公告底顶边 5，左上对齐
        if (inGuild) GUI.Label(At(win, NoticeTextX, NoticeTextY, NoticeW - 16, 14), st.notice ?? "", Style(10, TextOn));
        bool old = GUI.enabled; GUI.enabled = old && inGuild && !NativeDlgInput.Visible;
        bool clicked = NativeUi.Button(At(win, NoticeButtonX, NoticeButtonY, 20, 20), Info + "Btnotice");
        GUI.enabled = old;
        if (clicked && inGuild)
            NativeDlgInput.Open("请输入家族公告。（最多 100 个字）", st.notice ?? "", 100, value =>
            {
                NativeCommunityWindow.InputText = value;
                NativeCommunityWindow.Request(CommunityOperation.GuildNotice);
            });
    }

    // 视口内自上而下：组标题(27) → 列头(+31) → 成员行(+49, 步长 18) → 底线(1px) → 10px → 下一组
    static void DrawList(Rect win, CommunityResponse st)
    {
        var view = At(win, ListX, ListTop, ListW, ListH);
        var rows = Flatten(st, out int online);
        int content = GroupHeight(online) + GroupGap + GroupHeight(rows.Count - online);
        int maximum = Mathf.Max(0, content - ListH);
        scroll = Mathf.Clamp(scroll, 0, maximum);

        var local = Pointer.Pos - new Vector2(view.x, view.y);
        bool inside = local.x >= 0 && local.y >= 0 && local.x < ListW * S && local.y < ListH * S;
        // 滚轮一格 33px（源端 0x90F17C 的滚动步长），不是一行 18px：原端列表是整段位移
        if (inside && Event.current.type == EventType.Repaint)
            scroll = Mathf.Clamp(scroll - Mathf.RoundToInt(Input.mouseScrollDelta.y) * ScrollStep, 0, maximum);
        // 按住拖动胜过一行 6px 才开始滚，避免「想选人结果滚了一下」
        if (inside && maximum > 0 && Pointer.Pressed) { dragging = true; dragFromY = local.y; dragFromScroll = scroll; }
        if (dragging)
        {
            if (Pointer.Down)
            {
                int delta = Mathf.RoundToInt(local.y - dragFromY);
                if (Mathf.Abs(delta) > 6f * S) scroll = Mathf.Clamp(dragFromScroll - delta, 0, maximum);
            }
            else dragging = false;
        }

        GUI.BeginClip(view);
        float y = -scroll;
        y = DrawGroup(y, rows, 0, online, true);
        y += GroupGap;
        DrawGroup(y, rows, online, rows.Count - online, false);

        // 行选择：指针在裁剪区内 → 换回内容坐标（+scroll）再命中
        if (inside && Pointer.Clicked && !dragging)
        {
            var pointer = local / S;
            int index = HitRow(new Vector2(pointer.x, pointer.y + scroll), rows.Count, online);
            if (index >= 0) { selectedId = rows[index].id; Pointer.ConsumeClick(); }
        }
        GUI.EndClip();
    }

    static int HitRow(Vector2 pointer, int count, int online)
    {
        float y = 0;
        for (int group = 0; group < 2; group++)
        {
            int start = group == 0 ? 0 : online;
            int n = group == 0 ? online : count - online;
            float top = y + RowsY;
            for (int i = 0; i < Mathf.Max(1, n); i++)
            {
                var row = new Rect(0, top + i * RowH, ListW, RowH);
                if (row.Contains(pointer)) return i < n ? start + i : -1;
            }
            y += GroupHeight(n) + GroupGap;
        }
        return -1;
    }

    static int GroupHeight(int count) => GroupTitleH + 4 + HeaderH + RowH * Mathf.Max(1, count) + 1;

    // 在线组在前、离线组在后；组内按职位、同职位按名字
    static List<CommunityMember> Flatten(CommunityResponse st, out int online)
    {
        var list = new List<CommunityMember>();
        online = 0;
        if (st?.guild == null) return list;
        foreach (var m in st.guild) if (m.online) list.Add(m);
        list.Sort(Compare);
        online = list.Count;
        foreach (var m in st.guild) if (!m.online) list.Add(m);
        list.Sort(online, list.Count - online, Comparer<CommunityMember>.Create(Compare));
        return list;
    }

    static int Compare(CommunityMember a, CommunityMember b)
        => a.rank != b.rank ? a.rank.CompareTo(b.rank) : string.CompareOrdinal(a.name ?? "", b.name ?? "");

    static float DrawGroup(float y, List<CommunityMember> rows, int start, int count, bool online)
    {
        var title = Art.Tex("wz://" + Info + (online ? "guildinfo0" : "guildinfo1"));
        if (title != null) GUI.DrawTexture(new Rect(0, y, title.width, title.height), title);
        var header = Art.Tex("wz://" + Info + (online ? "guildinfo2" : "guildinfo7"));
        if (header != null) GUI.DrawTexture(new Rect(0, y + HeaderY, header.width, header.height), header);
        var rowSprite = Art.Tex("wz://" + Info + "guildinfo3");
        var normal = Style(11, TextOn);
        var dim = Style(11, TextDim);
        var picked = Style(11, TextSelected);
        int shown = Mathf.Max(1, count);
        for (int i = 0; i < shown; i++)
        {
            float ry = y + RowsY + i * RowH;
            if (rowSprite != null) GUI.DrawTexture(new Rect(0, ry, rowSprite.width, rowSprite.height), rowSprite);
            if (i >= count) continue;
            var m = rows[start + i];
            bool isSelected = m.id == selectedId && selectedId != 0;
            if (isSelected) UiKit.Fill(new Rect(1, ry, ListW - 2, RowH), RowSelected);
            var style = isSelected ? picked : m.online ? normal : dim;
            GUI.Label(new Rect(ColName - 2, ry + 4, ColClass - ColName - 4, RowH), m.name ?? "", style);
            // 职业列：源端列头就是「名字|职业|等级|地位」（guildinfo2 位图），职业取 showGuildInfo 包里的 jobId。
            // 名字用 JobEnum 的中文表（源端 Job.java 枚举 + message_zh_CN.properties 的 job.name.<id>），
            // 不另造一套职业命名。
            GUI.Label(new Rect(ColClass, ry + 4, ColLevel - ColClass - 6, RowH), JobName(m.job), style);
            GUI.Label(new Rect(ColLevel, ry + 4, ColRank - ColLevel - 6, RowH), m.level.ToString(), style);
            GUI.Label(new Rect(ColRank, ry + 4, 66, RowH), RankName(m.rank), style);
        }
        var line = Art.Tex("wz://" + Info + "guildinfo4");
        float lineY = y + RowsY + shown * RowH;
        if (line != null) GUI.DrawTexture(new Rect(0, lineY, line.width, line.height), line);
        return lineY + 1;
    }

    static string RankName(int rank)
    {
        var titles = State?.titles;
        if (titles != null && rank >= 1 && rank <= titles.Length && !string.IsNullOrEmpty(titles[rank - 1])) return titles[rank - 1];
        return rank == 1 ? "族长" : rank == 2 ? "副族长" : "";
    }

    // 职业中文名：源端 org.gms.client.Job 的 getId() 与 i18n job.name.<id>，工程里已有一份权威表。
    static string JobName(int jobId)
    {
        var job = JobEnum.Of(jobId);
        return job != null ? job.Display : "";
    }

    static void Centered(Rect win, string text, float y)
        => GUI.Label(At(win, ContentX, y, BarW, NameTextH), text, Style(12, TextOn, TextAnchor.MiddleCenter));

    // ————————————————— 底部按钮 —————————————————
    // 全部 (x,y,id) 来自主窗构建函数 0x919B73（家族段 0x91AA5B..0x91B01A）；
    // id → 桩 → 处理函数由分派器 0x91CF4F 的跳转表给出（字节索引表 0x91D44E、表体 0x91D372，index = id-2010）：
    //
    //   id   节点            (x,y)      桩          处理
    //   2030 BtInvite       (9,316)    0x91D0E7    0x9111BE
    //   2031 BtKick         (159,336)  0x91D107    0x9112C0
    //   2032 BtWithdraw     (80,316)   0x91D0F7    0x9112B4
    //   2033 BtUp           (9,336)    0x91D117    0x911341(push 1)
    //   2034 BtDown         (80,336)   0x91D11B    0x911341(push 0)
    //   2035 BtWhere        (159,316)  0x91D160    0x911C5C  ← 成员位置
    //   2036 BtWhisper      (238,316)  0x91D12C    0x9113C0
    //   2037 Btnotice       (287,80)   0x91D188    0x9114F1  ← 公告（弹类 0xBF0B00 窗，0x6E8 字节）
    //   2038 BtChat         (238,336)  0x91D13C    0x911454  ← 家族聊天
    //   2039 BtInfo         (238,363)  0x91D170    vtable+0x38（参数=是否折叠）
    //   2040 BtPartyInvite  (159,363)  0x91D198    0x911687
    //   2041 BtGuildBBS     (80,363)   0x91D14C    0xA05AB8(ecx=0xBE7918, push 11, push -1)
    //
    // 「成员位置」是纯客户端行为，已核实：0x911C5C 先排除「未选中 / 选中自己」，再
    // 0x9118A4 在在线表里取记录并拼 "%s"（0xAF34E4），最后 0x437A0C 往聊天窗追加一行，全程不发 GUILD_OPERATION。
    // 2037/2038 两个函数体形状相近（都走 0x9118A4 → 聊天追加），语义按节点名（改公告 / 家族聊天）落地，
    // 与实机若有出入以实机为准。
    //
    // 绘制口径：(x,y) **就是贴图左上角**，不减画布 origin。证据（2026-10-30 复核）：
    //   ① 12 个锚点恰好排成三行四列的栅格——列 9/80/159/238，行 316/336/363；第三行只有 3 个按钮，
    //      正好落在第 2/3/4 列（80/159/238）。
    //   ② 若按 origin 减：BtInfo/BtGuildBBS 的 origin=(23,9) 是 47×18 的中心，会变成 57..104 / 215..262，
    //      于是第三行「组队邀请(159..212)」和「资料(215..262)」只隔 3px，而 BtPartyInvite(origin 0) 又比
    //      两侧低 9px——用户截图里的错位就是这么来的。BtWhisper/BtChat 的 origin=(0,2) 同理会把整列抬高 2px。
    //   ③ origin 只是美术锚点约定：46×17 的 BtWhisper/BtChat 比 53×18 的兄弟按钮少一行透明边，
    //      所以画布左上是 (0,2) 而不是 (0,0)；绘制时不管它，按钮的可见边框自然对齐。
    //   ④ 不减 origin 时右边缘 238+47=285，与列表视口右边界 287 齐；左边 9 与视口左边 6 齐，
    //      即按钮块本来就贴在列表的横向带里。
    //   ⑤ 同窗口的好友/组员页（NativeSocialWindow）共用这批按钮，一直是锚点=左上角，没有减 origin。
    // 尺寸取自 IMG 节点的 normal/0 画布。
    static readonly string[] ButtonNodes =
    {
        "BtInvite", "BtWithdraw", "BtWhere", "BtWhisper",
        "BtUp", "BtDown", "BtKick", "BtChat",
        "BtGuildBBS", "BtPartyInvite", "BtInfo",
    };
    static readonly int[,] ButtonAt =
    {
        { 9, 316 }, { 80, 316 }, { 159, 316 }, { 238, 316 },
        { 9, 336 }, { 80, 336 }, { 159, 336 }, { 238, 336 },
        { 80, 363 }, { 159, 363 }, { 238, 363 },
    };
    static Rect[] buttonRects;   // 原生像素坐标（不随 S 变），首次绘制时解析一次
    static Vector2[] buttonSize;

    // 绘制矩形＝锚点＋IMG 尺寸，锚点就是左上角（不减 origin，依据见上面「按钮对齐」的 ①..⑤）。
    // 抽成纯函数是为了让断言能用同一份逻辑算期望值，而不是自己再减一遍 origin。
    public static Rect PlaceButton(int index, Vector2 size)
        => new Rect(ButtonAt[index, 0], ButtonAt[index, 1], size.x, size.y);

    static void ResolveButtons()
    {
        if (buttonRects != null) return;
        buttonRects = new Rect[ButtonNodes.Length];
        buttonSize = new Vector2[ButtonNodes.Length];
        for (int i = 0; i < ButtonNodes.Length; i++)
        {
            string node = Info + ButtonNodes[i] + "/normal/0";
            var tex = Art.Tex("wz://" + node);
            float w = tex != null ? tex.width : 53, h = tex != null ? tex.height : 18;
            buttonSize[i] = new Vector2(w, h);
            buttonRects[i] = PlaceButton(i, buttonSize[i]);
        }
    }

    static void DrawButtons(Rect win, CommunityResponse st, bool inGuild)
    {
        ResolveButtons();
        var target = SelectedMember(st);
        var enabled = EnabledRules(st, target);
        for (int i = 0; i < ButtonNodes.Length; i++)
        {
            string node = Info + ButtonNodes[i];
            var r = buttonRects[i];
            var rect = At(win, r.x, r.y, r.width, r.height);
            bool old = GUI.enabled; GUI.enabled = old && enabled[i] && !NativeDlgInput.Visible;
            bool clicked = NativeUi.Button(rect, node);
            GUI.enabled = old;
            if (clicked) Act(i, st, target);
        }
    }

    // 按钮可用性：源端逐按钮在 0x90F17C 的对应分支里判，这里按同一组条件落地。
    // 单独抽出来是为了能无渲染断言（GuildWindowChecks），改动必须同步两处语义。
    public static bool[] EnabledRules(CommunityResponse st, CommunityMember target)
    {
        bool inGuild = st != null && st.guildId > 0;
        var me = Self(st);
        int myRank = me != null ? me.rank : 5;
        bool other = target != null && target.id != GameSession.CharacterId;
        bool reachable = other && target.online;
        int targetRank = target != null ? target.rank : 0;
        return new[]
        {
            // 服务端 InviteGuild 只给 rank<=2（NativeCommunityService）；原端也按职位禁用
            inGuild && myRank <= 2,                                      // 邀请
            inGuild && myRank != 1,                                      // 出去（族长不能离开，只能解散）
            // 成员位置：源端 0x911C5C 一进来就排除「没选中」和「选中自己」，
            // 再要求 0x9118A4 在**在线表**里命中才继续；离线/查不到直接静默返回。
            reachable,                                                   // 成员位置
            reachable,                                                   // 悄悄话
            // 升职传 rank-1：下界 2，且升到 2 只有族长能做
            inGuild && other && myRank <= 2 && targetRank > myRank + 1 &&
                targetRank - 1 >= 2 && (targetRank - 1 != 2 || myRank == 1),   // 地位↑
            // 降职传 rank+1：不能超过最低职位 5
            inGuild && other && myRank <= 2 && targetRank > myRank && targetRank < 5, // 地位↓
            inGuild && other && myRank <= 2 && targetRank > myRank,      // 退出（驱逐）
            inGuild,                                                     // 家族聊天
            false,                                                       // 公告板（GuildBBS 独立窗，尚未迁移）
            reachable,                                                   // 组队邀请
            inGuild,                                                     // 资料
        };
    }

    static void Act(int index, CommunityResponse st, CommunityMember target)
    {
        switch (index)
        {
            case 0:
                // 0x9111BE：邀请输入最多 12 字、最少 4 字
                NativeDlgInput.Open("请输入要邀请的角色名。（4～12 个字）", "", 12, value =>
                {
                    NativeCommunityWindow.InputName = value;
                    NativeCommunityWindow.Request(CommunityOperation.InviteGuild);
                });
                return;
            case 1:
                NativeUtilPrompt.Confirm("确定要退出家族吗？", yes => { if (yes) NativeCommunityWindow.Request(CommunityOperation.LeaveGuild); });
                return;
            case 2:
                // 源端 0x911C5C 是在客户端本地把成员位置拼成一句话打到聊天窗（不走 GUILD_OPERATION），
                // 本工程由服务端 DTO 带 mapId，名字取 String.wz 的 mapName。查不到就退回地图 id。
                if (target != null)
                {
                    string where = NativeMapNames.Of(target.map);
                    NativeSystemMessage.Drop(5, target.name + " 目前在 " + (where != "" ? where : "地图 " + target.map));
                }
                return;
            case 3:
                if (target != null) { Reset(); BottomBar.FocusChat(NativeChatChannel.Whisper, target.name); }
                return;
            case 4:
                NativeCommunityWindow.Request(CommunityOperation.GuildRank, target.id, Mathf.Max(2, target.rank - 1));
                return;
            case 5:
                NativeCommunityWindow.Request(CommunityOperation.GuildRank, target.id, Mathf.Min(5, target.rank + 1));
                return;
            case 6:
                NativeCommunityWindow.Request(CommunityOperation.ExpelGuild, target.id);
                return;
            case 7:
                Reset(); BottomBar.FocusChat(NativeChatChannel.Guild);
                return;
            case 9:
                if (target != null) NativeSocialWindow.Request(SocialOperation.InviteParty, target.name);
                return;
            case 10:
                NativeGuildGradeWindow.Toggle();
                return;
        }
    }

    static CommunityMember Self(CommunityResponse st)
    {
        if (st?.guild == null) return null;
        foreach (var m in st.guild) if (m.id == GameSession.CharacterId) return m;
        return null;
    }

    static CommunityMember SelectedMember(CommunityResponse st)
    {
        if (st?.guild == null || selectedId == 0) return null;
        foreach (var m in st.guild) if (m.id == selectedId) return m;
        return null;
    }

    static void CloseInput() => NativeDlgInput.Close();

    // ————————————————— 只读几何口（给 GuildWindowChecks 的无渲染断言用） —————————————————
    // 只转述常量，不含任何运行时状态；改常量必须让断言跟着改，否则校验会红。
    public static Vector2 SizeOf() => new Vector2(WinW, WinH);
    public static Vector2 TabStrip() => new Vector2(TabX, TabY);
    public static Vector2 ContentOrigin() => new Vector2(ContentX, ContentY);
    // 名条里徽章的位置（源端 left = (306-(名字宽+26))/2，徽章在 (left,1)）
    public static Rect MarkRect(float nameWidth)
        => new Rect(ContentX + Mathf.Max(0, (BarW - (nameWidth + NameGap)) / 2f), MarkY, MarkSize, MarkSize);
    public static float NameTextLeft(float nameWidth) => MarkRect(nameWidth).x + NameGap;
    public static Rect ListViewport() => new Rect(ListX, ListTop, ListW, ListH);
    public static Rect NoticeBar() => new Rect(NoticeX, NoticeY, NoticeW, NoticeH);
    public static Rect NoticeButton() => new Rect(NoticeButtonX, NoticeButtonY, 20, 20);
    public static int GroupHeightOf(int members) => GroupHeight(members);
    public static float RowTop() => RowsY;
    public static int RowStep() => RowH;
    public static int ColumnX(int column) => column == 0 ? ColName : column == 1 ? ColClass : column == 2 ? ColLevel : ColRank;
    public static int NameTextTop() => ContentY + NameOffsetY;
    public static string ButtonNode(int index) => ButtonNodes[index];
    public static Vector2 ButtonAnchor(int index) => new Vector2(ButtonAt[index, 0], ButtonAt[index, 1]);
    public static int ButtonCount => ButtonNodes.Length;
}
