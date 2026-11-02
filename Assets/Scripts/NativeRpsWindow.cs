using UnityEngine;

// 猜拳小游戏窗（源端 CUIRockPaperScissor；素材 UI/UIWindow.img/RpsGame，窗 310x358）。
// 素材不带布局参数（WZ 全部 origin=0,0），按钮坐标在 BeiDou.exe 二进制里；
// 这里按美术几何推导：两个气泡 = 出手区（左我 / 右 NPC，F* 是朝左翻转的 NPC 手），
// 中台 = 胜负台（charWin/charLose 64x34 小标），底部白条 = 按钮槽，记录在
// skill references/minigame-rps.md。
// 服务端语义见 NativeRpsService（RockPaperScissor.java + RPSActionHandler.java）：
//   开局/再来一局 1000 金币、10 轮、平局可重出、赢着离场发证书 4031332+胜场。
// 入口：NPC 9000019（源端 GM 召唤 NPC，WZ 无常规出生点 → 项目补在自由市场 910000000，
//   见 NativeMapView.ExtraNpcs），脚本 openRPSNPC 经 NpcApi.ApplyScriptPacket 打开本窗。
public static class NativeRpsWindow
{
    public static bool Visible { get; private set; }

    enum Phase { Idle, Choosing, Result }
    static Phase phase = Phase.Idle;
    static int round = 1;               // 当前轮（1..10，显示用）
    static int myHand = -1;             // 0 石 / 1 剪 / 2 布
    static int npcHand = -1;            // 服务端 selection
    static int banner;                  // 0 无 / 1 win / 2 lose / 3 draw / 4 timeover
    static bool busy;                   // 请求在途
    static string roundText = "";       // 底部白条文字

    const int WinW = 310, WinH = 358;
    static Vector2 pos = new Vector2(-1f, -1f);
    static bool dragging;
    static Vector2 dragOff;

    static float S => BottomBar.S;
    static Texture2D Wz(string node) => Art.Tex("wz://UI/UIWindow.img/RpsGame/" + node);
    static Rect At(float x, float y, float w, float h)
        => new Rect((pos.x + x) * S, (pos.y + y) * S, w * S, h * S);

    static GUIStyle Style(int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var st = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(7, Mathf.RoundToInt(size * S)),
            alignment = anchor,
        };
        st.normal.textColor = color;
        return st;
    }

    static readonly string[] HandNodes = { "rock", "paper", "scissor" };     // 源端胜负表 (0,2)(1,0)(2,1) → 0石 1布 2剪
    static readonly string[] FHandNodes = { "Frock", "Fpaper", "Fscissor" };

    public static void Open()
    {
        Visible = true;
        phase = Phase.Idle;
        round = 1;
        myHand = npcHand = -1;
        banner = 0;
        busy = false;
        roundText = "与 9000019 号管理员猜拳，一局 1000 金币，赢满 10 轮有大奖！";
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, WinH), S);
    }

    public static void Close() => Visible = false;
    public static void Reset() => Close();

    // 关窗时结算在途会话（源端关 UI 即 dispose）
    public static void RequestLeave()
    {
        if (phase == Phase.Idle && round == 1 && banner == 0 && myHand < 0) { Close(); return; }
        Send(RpsOperation.Leave, 0);
    }

    public static void Draw()
    {
        if (!Visible) return;
        if (pos.x < 0f) pos = new Vector2((UiKit.W / S - WinW) / 2f, (UiKit.H / S - WinH) / 2f);
        NativeUi.RegisterWindow("NativeRpsWindow",
            new Rect(pos.x * S, pos.y * S, WinW * S, WinH * S), () => Visible);

        var bg = Wz("backgrnd");
        if (bg != null) GUI.DrawTexture(new Rect(pos.x * S, pos.y * S, bg.width * S, bg.height * S), bg);
        // 无自带关闭钮：右上自画 ×（项目统一口径）；关窗即 dispose
        if (NativeUi.Button(At(WinW - 17, 3, 12, 12), "UI/Basic.img/BtClose")) { RequestLeave(); return; }

        DrawHands();
        DrawBanner();
        DrawButtons();

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

    // 出手区：左气泡=我方手，右气泡=NPC 手；胜负台小标
    static void DrawHands()
    {
        if (myHand >= 0)
        {
            var tex = Wz(HandNodes[myHand]);
            if (tex != null) GUI.DrawTexture(At(24, 42, tex.width, tex.height), tex);
        }
        if (npcHand >= 0)
        {
            var tex = Wz(FHandNodes[npcHand]);
            if (tex != null) GUI.DrawTexture(At(172, 42, tex.width, tex.height), tex);
        }
        // 胜负台小标（赢的一方亮 charWin / 输的亮 charLose）
        if (banner == 1)
        {
            var cw = Wz("charWin");
            if (cw != null) GUI.DrawTexture(At(22, 148, cw.width, cw.height), cw);
        }
        else if (banner == 2)
        {
            var cl = Wz("charLose");
            if (cl != null) GUI.DrawTexture(At(222, 148, cl.width, cl.height), cl);
        }
    }

    static void DrawBanner()
    {
        if (banner == 0) return;
        string node = banner == 1 ? "win" : banner == 2 ? "lose" : banner == 3 ? "draw" : "timeover";
        var tex = Wz(node);
        if (tex != null) GUI.DrawTexture(At((WinW - tex.width) / 2f, 118, tex.width, tex.height), tex);
    }

    static void DrawButtons()
    {
        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;

        // 底部白条文字（轮数/提示）
        if (!string.IsNullOrEmpty(roundText))
            GUI.Label(At(20, 290, 270, 20), roundText, Style(12, new Color(0.25f, 0.22f, 0.16f)));

        switch (phase)
        {
            case Phase.Idle:
                if (NativeUi.Button(At(105, 291, 99, 32), "UI/UIWindow.img/RpsGame/BtStart") && !busy)
                {
                    busy = true;
                    Send(RpsOperation.Start, 0);
                }
                break;

            case Phase.Choosing:
                // 三个出手按钮 33x33（answer 语义：0 石 / 1 剪 / 2 布）
                if (NativeUi.Button(At(45, 291, 33, 33), "UI/UIWindow.img/RpsGame/BtRock") && !busy) { busy = true; Send(RpsOperation.Answer, 0); }
                if (NativeUi.Button(At(138, 291, 33, 33), "UI/UIWindow.img/RpsGame/BtPaper") && !busy) { busy = true; Send(RpsOperation.Answer, 1); }
                if (NativeUi.Button(At(231, 291, 33, 33), "UI/UIWindow.img/RpsGame/BtScissor") && !busy) { busy = true; Send(RpsOperation.Answer, 2); }
                break;

            case Phase.Result:
                if (banner == 1)
                {
                    // 赢 → 继续（round 10 时服务端直接结算关窗）
                    if (NativeUi.Button(At(104, 290, 101, 35), "UI/UIWindow.img/RpsGame/BtContinue") && !busy)
                    {
                        busy = true;
                        Send(RpsOperation.Continue, 0);
                    }
                }
                else
                {
                    // 输/超时 → 再来一局（1000 金币）或离开
                    if (NativeUi.Button(At(60, 290, 101, 35), "UI/UIWindow.img/RpsGame/BtRetry") && !busy)
                    {
                        busy = true;
                        Send(RpsOperation.Retry, 0);
                    }
                    if (NativeUi.Button(At(180, 299, 59, 17), "UI/UIWindow.img/RpsGame/BtExit") && !busy)
                    {
                        busy = true;
                        Send(RpsOperation.Leave, 0);
                    }
                }
                break;
        }
    }

    static void Send(RpsOperation op, int answer)
    {
        if (op == RpsOperation.Answer) LastAnswer = answer;
        var res = GameSession.Net.Rps(new RpsRequest { characterId = GameSession.CharacterId, operation = op, answer = answer });
        busy = false;
        Apply(res);
    }

    // 服务端下行码 → 窗口状态（对齐源端客户端对 RPS_GAME 各 mode 的处理）
    static void Apply(RpsResponse r)
    {
        if (r.rewardItem != 0)
        {
            BottomBar.AddChat("获得了猜拳胜利证书。", BottomBar.ChatYellow);
            GameSession.ReloadAfterCommand();
        }
        else if (r.mode == 9 || r.mode == 14)
        {
            GameSession.ReloadAfterCommand();   // 开局扣了 1000 金币
        }
        if (!r.ok)
        {
            if (!string.IsNullOrEmpty(r.error)) BottomBar.AddChat(r.error, BottomBar.ChatPink);
            if (r.closed) Close();
            return;
        }
        switch (r.mode)
        {
            case 9:
            case 14:
                phase = Phase.Choosing;
                round = 1;
                myHand = npcHand = -1;
                banner = 0;
                roundText = "第 1 轮 —— 出拳吧！";
                break;
            case 0x0B:
                npcHand = r.selection;
                myHand = LastAnswer;
                if (r.roundLabel < 0)
                {
                    banner = 2;
                    phase = Phase.Result;
                    roundText = "第 " + round + " 轮输了……（赢了 " + (round - 1) + " 轮）";
                }
                else if (r.roundLabel == round)
                {
                    banner = 3;
                    roundText = "平局！再出一次。";
                    // 平局：仍在 Choosing，可立刻再出（源端 ableAnswer 保持）
                    myHand = npcHand = -1;
                }
                else
                {
                    round = r.roundLabel;       // 赢：label = round+1
                    banner = 1;
                    phase = Phase.Result;
                    roundText = "第 " + round + " 轮获胜！" + (round >= 10 ? "" : "点「继续」进入下一轮。");
                }
                break;
            case 0x0C:
                phase = Phase.Choosing;
                myHand = npcHand = -1;
                banner = 0;
                roundText = "第 " + round + " 轮 —— 出拳吧！";
                break;
            case 0x0D:
                Close();
                break;
        }
    }

    // 发包前记录自己出的手（0x0B 只回服务器 selection）
    static int LastAnswer;
}
