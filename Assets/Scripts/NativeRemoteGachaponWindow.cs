using UnityEngine;

// 远程扭蛋操作券选择窗（素材 UI/UIWindow.img/RemoteGachapon，246x235；源端 CUIRemoteGachapon）。
// 源端链路：双击 5451000（RemoteGachaponTicket）→ 客户端选村 → RemoteGachaponHandler：
//   gacha 0..7 → NpcId 9100100+gacha；8 → 新叶子 9100109；9 → 海豚 9100117；
//   然后 NPCScriptManager.start(npcId, "gachaponRemote")。
// 项目等价：选村 → NpcSession.Talk(npcId, "gachaponRemote")（脚本校验持票/四栏空位、
//   扣券、cm.doGachapon() 抽奖——NpcApi 已有引擎）。
// 按钮 Bt0..Bt9（103x18）2 列 × 5 行；底图不带坐标，按槽位几何排布。
public static class NativeRemoteGachaponWindow
{
    public static bool Visible { get; private set; }

    // gacha 索引 → (NPC id, 村名)（脚本 mapName + RemoteGachaponHandler 分派）
    static readonly (int npc, string name)[] Machines =
    {
        (9100100, "射手村"), (9100101, "魔法密林"), (9100102, "勇士部落"), (9100103, "废弃都市"),
        (9100104, "林中之城"), (9100105, "蘑菇神社"), (9100106, "昭和澡堂（男）"), (9100107, "昭和澡堂（女）"),
        (9100109, "新叶城"), (9100117, "海豚码头"),
    };

    const int WinW = 246, WinH = 235;
    static Vector2 pos = new Vector2(-1f, -1f);
    static bool dragging;
    static Vector2 dragOff;

    static float S => BottomBar.S;
    static Rect At(float x, float y, float w, float h)
        => new Rect((pos.x + x) * S, (pos.y + y) * S, w * S, h * S);

    public static void Open()
    {
        Visible = true;
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, WinH), S);
    }

    public static void Close() => Visible = false;
    public static void Reset() => Close();

    public static void Draw()
    {
        if (!Visible) return;
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        NativeUi.RegisterWindow("NativeRemoteGachaponWindow",
            new Rect(pos.x * S, pos.y * S, WinW * S, WinH * S), () => Visible);

        var bg = Art.Tex("wz://UI/UIWindow.img/RemoteGachapon/backgrnd");
        if (bg != null) GUI.DrawTexture(new Rect(pos.x * S, pos.y * S, bg.width * S, bg.height * S), bg);
        // 无自带关闭钮：右上自画 ×（项目统一口径）
        if (NativeUi.Button(At(WinW - 17, 3, 12, 12), "UI/Basic.img/BtClose")) { Close(); return; }

        // 2 列 × 5 行（Bt0..Bt9，103x18）；底图槽位 x≈18/125，y≈48 起每行 36
        for (int i = 0; i < Machines.Length; i++)
        {
            float x = i % 2 == 0 ? 18 : 125, y = 48 + i / 2 * 36;
            if (NativeUi.Button(At(x, y, 103, 18), "UI/UIWindow.img/RemoteGachapon/Bt" + i))
            {
                Visible = false;
                NpcSession.Talk(Machines[i].npc, "gachaponRemote");
                return;
            }
        }

        if (Event.current.type == EventType.Repaint && Pointer.Pressed
            && new Rect(pos.x * S, pos.y * S, WinW * S, 22 * S).Contains(Pointer.Pos))
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
}
