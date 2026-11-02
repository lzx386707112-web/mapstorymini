using UnityEngine;

// CPQ 房间选择窗 —— 源端 sendCPQMapLists（native）打开房间列表、玩家点选后走独立 CPQ 挑战包，
// 不回脚本 selection；单机只有一个可用 field 组（无真实对手队伍），故六个 field 直接可进：
// field n → 战斗图 980000(1+n)01（isCPQMap 列表 980000101..980000601）。
// 进入即 NativeCarnivalService.Enter（MonsterCarnival 构造：红队玩家 / 蓝队假人模拟）。
// 素材：源端房间列表为专用 UI（无独立窗素材），此处用项目统一面板呈现六行按钮。
public static class NativeCarnivalLobby
{
    static readonly string[] FieldNames = { "第一战场", "第二战场", "第三战场", "第四战场", "第五战场", "第六战场" };

    public static bool Visible { get; private set; }
    static int pendingLobby = -1;   // cpqLobby(selection) 带来的 field（脚本流程），-1 = 自由选择

    const int WinW = 220, WinH = 200;
    static Vector2 pos = new Vector2(-1f, -1f);

    static float S => BottomBar.S;
    static Rect At(float x, float y, float w, float h) => new Rect((pos.x + x) * S, (pos.y + y) * S, w * S, h * S);

    public static void Open(int field = -1)
    {
        Visible = true;
        pendingLobby = field;
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, WinH), S);
    }

    public static void Close() => Visible = false;
    public static void Reset() => Close();

    public static void Draw()
    {
        if (!Visible) return;
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        NativeUi.RegisterWindow("NativeCarnivalLobby", new Rect(pos.x * S, pos.y * S, WinW * S, WinH * S), () => Visible);
        UiKit.Fill(new Rect(pos.x * S, pos.y * S, WinW * S, WinH * S), new Color(0.08f, 0.09f, 0.13f, 0.95f));
        UiKit.Fill(new Rect(pos.x * S, pos.y * S, WinW * S, 22 * S), new Color(0.16f, 0.2f, 0.32f));
        GUI.Label(new Rect((pos.x + 8) * S, pos.y * S + 3 * S, 160 * S, 18 * S), "怪物嘉年华 · 选择战场",
            Label(12, Color.white, TextAnchor.MiddleLeft));
        if (NativeUi.Button(At(WinW - 17, 3, 12, 12), "UI/Basic.img/BtClose")) { Close(); return; }

        float y = 30;
        if (pendingLobby >= 0)
        {
            // 脚本 cpqLobby(selection) 流：直接进对应 field
            GUI.Label(At(10, y, WinW - 20, 30), "已选择 " + FieldNames[Mathf.Clamp(pendingLobby, 0, 5)] + "，点击进入战斗。",
                Label(11, Color.white, TextAnchor.UpperLeft));
            y += 34;
            if (Button(At(40, y, 140, 26), "进入战斗"))
                Enter(pendingLobby);
            return;
        }
        for (int i = 0; i < FieldNames.Length; i++)
        {
            if (Button(At(20, y, WinW - 40, 22), FieldNames[i])) { Enter(i); return; }
            y += 26;
        }
        GUI.Label(At(10, y + 2, WinW - 20, 16), "30~50 级 · 击杀怪物为红队赢取 CP", Label(10, new Color(0.7f, 0.72f, 0.8f), TextAnchor.MiddleLeft));
    }

    static void Enter(int field)
    {
        int mapId = 980000101 + field * 100;
        var res = GameSession.Net.Carnival(new CarnivalRequest { characterId = GameSession.CharacterId, operation = 0, mapId = mapId });
        if (res == null || !res.ok) { BottomBar.Toast(res?.error ?? "无法进入嘉年华"); return; }
        Close();
        NativeCarnivalHud.Reset();
        GameLoop.I?.field?.WarpTo(mapId);
    }

    // 简易整块按钮（窗内自绘，无 WZ 按钮素材）
    static bool Button(Rect r, string label)
    {
        bool hover = r.Contains(Pointer.Pos);
        UiKit.Fill(r, hover ? new Color(0.3f, 0.42f, 0.7f, 0.95f) : new Color(0.2f, 0.28f, 0.5f, 0.9f));
        GUI.Label(r, label, Label(12, Color.white, TextAnchor.MiddleCenter));
        bool clicked = hover && Event.current.type == EventType.Repaint && Pointer.Clicked;
        if (clicked) Pointer.ConsumeClick();
        return clicked;
    }

    static GUIStyle Label(int size, Color color, TextAnchor anchor)
    {
        var st = new GUIStyle { font = UiKit.Font, fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S)), alignment = anchor };
        st.normal.textColor = color;
        return st;
    }
}
