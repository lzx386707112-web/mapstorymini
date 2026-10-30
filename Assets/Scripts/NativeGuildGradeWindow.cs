using UnityEngine;

// 原端「家族资料 / 职位」附属窗：家族主窗 BtInfo（按钮 2039 → 0x91E2E0）打开，0x915D7C 创建 233×304 子窗。
// 素材 UserList/Guild/backgrnd（233×304）+ GuildGrade/*；0x9165DD 依次在 x6 / y8、45、91、128、146 复制
// guildgrade3、guildgrade4、guildgrade0、guildgrade1、guildgrade2；
// 五行职位每行 18px，次数 x26、人数 x66、职位名称在 x146 起的 86px 列内居中；修改/保存 (25,268) / (125,268)。
public static class NativeGuildGradeWindow
{
    public static bool Visible;

    const int WinW = 233, WinH = 304;
    const string Root = "UI/UIWindow.img/UserList/Guild/";
    const int ColNumber = 26, ColCount = 66, ColTitle = 146, ColTitleW = 86;
    const int RowsTop = 146, RowStep = 18, Rows = 5;

    static bool editing;
    static string[] typed = new string[Rows];

    static float S => BottomBar.S;
    static CommunityResponse State => NativeCommunityWindow.State;

    static Rect At(Rect win, float x, float y, float w, float h)
        => new Rect(win.x + x * S, win.y + y * S, w * S, h * S);

    static GUIStyle Style(int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
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

    public static void Toggle()
    {
        Visible = !Visible;
        editing = false;
        if (Visible) Snapshot();
    }

    public static void Reset() { Visible = false; editing = false; }

    static void Snapshot()
    {
        var titles = State?.titles;
        for (int i = 0; i < Rows; i++) typed[i] = titles != null && i < titles.Length ? titles[i] ?? "" : "";
    }

    public static void Draw()
    {
        if (!Visible) return;
        var st = State;
        if (st == null) { Visible = false; return; }
        float s = S;
        // 贴在主窗右侧并排（主窗 312 宽、居中），不压主窗的按钮列：
        // 两者都放不下时翻到左侧，最后才夹进屏幕。
        float mainW = 312 * s;
        float mainX = Mathf.Max(4, (UiKit.W - mainW) / 2f);
        float right = mainX + mainW + 4;
        float x = right + WinW * s <= UiKit.W - 4 ? right : mainX - WinW * s - 4;
        x = Mathf.Max(4, Mathf.Min(x, UiKit.W - WinW * s - 4));
        var win = new Rect(x, 26, WinW * s, WinH * s);
        var frame = Art.Tex("wz://" + Root + "backgrnd");
        if (frame != null) GUI.DrawTexture(win, frame);

        Slice(win, "GuildGrade/guildgrade3", 6, 8);
        Slice(win, "GuildGrade/guildgrade4", 6, 45);
        Slice(win, "GuildGrade/guildgrade0", 6, 91);
        Slice(win, "GuildGrade/guildgrade1", 6, 128);
        Slice(win, "GuildGrade/guildgrade2", 6, 146);

        // GP 条（guildgrade4 221×36）：「◆GP」标签烘死在标签行，数值画在下面的空白区
        GUI.Label(At(win, 10, 64, 200, 15), st.gp.ToString(), Style(11, TextOn, TextAnchor.MiddleLeft));

        for (int i = 0; i < Rows; i++)
        {
            float y = RowsTop + i * RowStep;
            GUI.Label(At(win, ColNumber - 6, y + 3, 24, RowStep), (i + 1).ToString(), Style(11, TextOn));
            GUI.Label(At(win, ColCount - 6, y + 3, 30, RowStep), CountOf(st, i + 1).ToString(), Style(11, TextOn));
            if (editing)
            {
                var edit = Style(11, TextOn, TextAnchor.MiddleLeft);
                typed[i] = GUI.TextField(At(win, ColTitle - 4, y + 2, ColTitleW, 14), typed[i] ?? "", 10, edit);
            }
            else
                GUI.Label(At(win, ColTitle - 4, y + 3, ColTitleW, RowStep), typed[i] ?? "", Style(11, TextOn));
        }

        // 修改/保存在 OnCreate 里是两个并存控件，这里都用 enable 区分可用性，不靠「藏掉一个」
        bool old = GUI.enabled;
        GUI.enabled = old && !editing;
        if (NativeUi.Button(At(win, 25, 268, 80, 17), Root + "GuildGrade/BtModf"))
        { editing = true; Snapshot(); }
        GUI.enabled = old && editing;
        if (NativeUi.Button(At(win, 125, 268, 80, 17), Root + "GuildGrade/BtSave"))
        {
            NativeCommunityWindow.InputTitles = (string[])typed.Clone();
            NativeCommunityWindow.Request(CommunityOperation.GuildTitles);
            editing = false;
        }
        GUI.enabled = old;
    }

    static void Slice(Rect win, string node, int x, int y)
    {
        var tex = Art.Tex("wz://" + Root + node);
        if (tex != null) GUI.DrawTexture(At(win, x, y, tex.width, tex.height), tex);
    }

    static int CountOf(CommunityResponse st, int rank)
    {
        if (st?.guild == null) return 0;
        int n = 0;
        foreach (var m in st.guild) if (m.rank == rank) n++;
        return n;
    }

    // 只读几何口（GuildWindowChecks 用）：0x915D7C 建的附属窗 233×304，
    // 五行职位表 top=146、行距 18；改常量要同步断言。
    public static Vector2 SizeOf() => new Vector2(WinW, WinH);
    public static int RowsTopY() => RowsTop;
    public static int RowPitch() => RowStep;
    public static int RowCount() => Rows;
    public static int TitleColumn() => ColTitle;
    public static int TitleColumnWidth() => ColTitleW;
}
