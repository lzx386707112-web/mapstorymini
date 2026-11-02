using UnityEngine;

// 事件倒计时（源端 SendOpcode.CLOCK / STOP_CLOCK）。
//
// 源端：EventInstanceManager.startEventTimer（:317-334）给每个玩家发
// PacketCreator.getClock(秒) —— CLOCK 包，首字节 2 表示「倒计时秒数」；
// dismissEventTimer（:357-365）发 removeClock（STOP_CLOCK）。
// restartEventTimer = stop + start，所以中途加时也会整包重发，客户端只需要
// 「收到就按这个秒数重新本地倒数」。
//
// —— 版面（BeiDou.exe 0x536246..0x5362B7 建层 + 0x49A180..0x49A89C 绘制）——
//   底图 Map/Obj/etc.img/timer/backgrnd(258×58) / backgrndhour(258×58)
//   数字 Map/Obj/etc.img/clock/fontTime/{0..9} 各 26×35（comma 17×35 只有当前时刻用）
//   CreateLayer(x=0x10F, y=0x1E, w=0x102, h=0x3A)：
//     0x5362A9 push 0x3A / 0x5362AB push 0x102 / 0x5362B0 push 0x1E / 0x5362B2 push 0x10F
//   x=271 正好是 (800-258)/2 —— 原版按 800 宽居中；北斗把客户区放宽到 1281 宽后
//   这条常量没有跟着改，因此这里按原设计意图居中（与 NativeBossHpBar 的 800 空间口径一致）。
//   底图在图层内画在 (0,0)（0x49A2AF/0x49A2B2 两次 push 0）。
//   四个数字槽 x 存在图层 +0x8C/+0x90/+0x94/+0x98，来自描述符 +0x1C..+0x28
//   （0x52BFE5 mov [esi+0x1C],0x56 / +0x20=0x70 / +0x24=0xB3 / +0x28=0xCD），
//   即 86/112/179/205：分十位 86、分个位 112、秒十位 179、秒个位 205。
//   分/秒这两个字是印在底图上的（「分」墨迹 152..162、「秒」237..246），所以槽位是固定坐标。
//   数字 y = 0x0C = 12（0x49A6FF/0x49A789/0x49A813/0x49A89C 的 push 0xC），
//   35 高的字在 58 高的底图里上下各留 12/11 px，与底图上「分」的墨迹底边对齐。
//   前导零：十位为 0 不画（0x49A30C test esi,esi / je、小时格式 0x49A6AF 同款），个位与后两位始终画。
//   满 1 小时换 hour 底图（0x49A196 cmp eax,0xE10 / jge 0x49A53A），显示 HH 小时 MM 分。
public static class NativeEventClock
{
    const string PanelRoot = "Map/Obj/etc.img/timer/";
    const string FontRoot  = "Map/Obj/etc.img/clock/fontTime/";

    const int PanelX = 0x10F;      // 271 —— 800 宽画布的居中位
    const int PanelY = 0x1E;       // 30
    const int PanelW = 0x102;      // 258
    const int PanelH = 0x3A;       // 58
    const int DigitY = 0x0C;       // 12
    static readonly int[] SlotX = { 0x56, 0x70, 0xB3, 0xCD };   // 86 / 112 / 179 / 205

    static int totalSeconds;
    static float shownAt;
    static bool visible;

    // 素材按路径缓存：每帧不该重新解一遍 IMG 路径。
    static readonly Texture2D[] digits = new Texture2D[10];
    static Texture2D backgrnd, backgrndHour;
    static bool texturesLoaded;

    public static bool Visible => visible;

    // eim.startEventTimer / restartEventTimer → CLOCK type 2。
    public static void Show(int seconds)
    {
        if (seconds <= 0) { Hide(); return; }
        totalSeconds = seconds;
        shownAt = Time.unscaledTime;
        visible = true;
    }

    // eim.stopEventTimer → STOP_CLOCK。
    public static void Hide()
    {
        visible = false;
        totalSeconds = 0;
    }

    public static void Draw()
    {
        if (!visible) return;
        int left = totalSeconds - Mathf.FloorToInt(Time.unscaledTime - shownAt);
        if (left < 0) left = 0;
        DrawTimer(left);
        // 源端计时器到点是服务端 dismissEventTimer 发 STOP_CLOCK；这里本地数到 0 就自己收，
        // 免得脚本没发 next packet 时数字停在 00:00。
        if (left == 0) Hide();
    }

    static void DrawTimer(int left)
    {
        // 源端 >= 3600 秒走 hour 分支：换底图，显示「HH 小时 MM 分」。
        bool hour = left >= 3600;
        int high = hour ? left / 3600 : left / 60;
        int low = hour ? left % 3600 / 60 : left % 60;
        // 只有两个数字槽：极端长的事件（>99 小时）按 99 封顶，避免越界。
        if (high > 99) high = 99;

        LoadTextures();
        float s = BottomBar.S;
        float x = (UiKit.W - 800f * s) * .5f + PanelX * s;
        float y = PanelY * s;
        var panel = hour ? backgrndHour : backgrnd;
        if (panel == null) { DrawFallback(x, y, high, low, s); return; }
        GUI.DrawTexture(new Rect(x, y, PanelW * s, PanelH * s), panel);

        // 前导零不画，其余三位始终画（源端 0x49A30C / 0x49A3A4 / 0x49A42E / 0x49A4B8）。
        if (high / 10 != 0) Digit(digits[high / 10], SlotX[0], x, y, s);
        Digit(digits[high % 10], SlotX[1], x, y, s);
        Digit(digits[low / 10], SlotX[2], x, y, s);
        Digit(digits[low % 10], SlotX[3], x, y, s);
    }

    static void Digit(Texture2D texture, int slotX, float x, float y, float s)
    {
        if (texture == null) return;
        GUI.DrawTexture(new Rect(x + slotX * s, y + DigitY * s, texture.width * s, texture.height * s), texture);
    }

    static void LoadTextures()
    {
        if (texturesLoaded) return;
        texturesLoaded = true;
        // Data 目录可能是直读外部缓存（direct-img-runtime），缺文件时不能每帧抛异常。
        if (!WzAssets.HasFile("Map/Obj/etc.img")) return;
        backgrnd = Texture(PanelRoot + "backgrnd");
        backgrndHour = Texture(PanelRoot + "backgrndhour");
        for (int i = 0; i < 10; i++) digits[i] = Texture(FontRoot + i);
    }

    static Texture2D Texture(string path)
    {
        var node = WzAssets.Node(path);
        return node == null ? null : WzAssets.Texture(node);
    }

    // 素材没打进包时至少别让计时器消失：退回纯文字，位置仍是原端图层位置。
    static void DrawFallback(float x, float y, int high, int low, float s)
    {
        string text = high.ToString("00") + ":" + low.ToString("00");
        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(26 * s) };
        style.normal.textColor = new Color(1f, 0.94f, 0.55f);
        GUI.Label(new Rect(x, y, PanelW * s, PanelH * s), text, style);
    }
}
