using UnityEngine;

// ————————————————————————————————————————————————————————————
// 客户端设置窗：源端 UI/UIWindow.img 的 GameOpt / SysOpt / Channel 三张底图。
//
// 逆向结论（BeiDou.exe SHA256 1b91cf5b…34b2，字符串池 id 见括号）：
//   2224 GameMenu/backgrnd、2208..2211 GameMenu/BtChannel..BtQuit —— 源端有按钮，
//        但四项（频道变更/场景变更/游戏设置/系统设置）的绘制与点击逻辑都不存在，
//        所以 GameMenu 里它们恒为 disabled（源端截图同样是灰的）。
//   2225 GameOpt/backgrnd、2226 SysOpt/backgrnd、2212..2214 Channel 三片 ——
//        路径只在资源预载表里被引用，代码段没有任何窗口实现。
// 因此这三张窗口的控件布局不存在"源端坐标"可抄；本项目按用户要求用源端底图补实现，
// 控件贴图仍然全部取自源端 IMG：
//   滑块    UI/Basic.img/Slider/{left,mid,right,thumbNormal,thumbPressed,thumbMouseOver,thumbDisabled}
//   复选框  UI/Basic.img/CheckBox/{0,1}（12×12，0=空框，1=红勾）
//   关闭    UI/Basic.img/BtClose
//   按钮    UI/UIWindow.img/Channel/{BtChange,BtCancel}
// 底图里已经印死的标签位置是量出来的（2× 原始 PNG 逐像素）：
//   GameOpt 158×284：12 行，首行 y=32、行距 18、高 16，右侧状态格 x=90 宽 60；
//   SysOpt  299×365：10 行，首行 y=32、行距 30、高 28，控件区 x=65..291；
//   滑块轨道落在标签之间的空白里：普通行 x=100..248，音乐/音效行 x=96..212。
// 手机端没有意义的项（画质调整 / 鼠标速度调整 / 怪物情报 / 画面模式）保持禁用外观 +
// 点击提示，不编造行为。
// ————————————————————————————————————————————————————————————

// 客户端设置存档。全部走 PlayerPrefs，和 AudioManager 的音量同一个存储域。
public static class ClientSettings
{
    const string PrefInvite = "client.invite";      // 12 位邀请权限位图，1=同意
    const string PrefHpAlarm = "client.hpalarm";     // 0=没有，1..100=百分比
    const string PrefMpAlarm = "client.mpalarm";
    const string PrefScreenFx = "client.screenfx";   // 画面效果（震屏/闪白）

    public const int InviteCount = 12;

    // GameOpt 的 12 行，顺序 = 底图从上到下，只用于提示文案与可读性。
    public static readonly string[] InviteNames =
    {
        "悄悄话", "好友聊天", "聊天邀请", "交易申请", "组队邀请", "寻找组队",
        "游戏邀请", "家族聊天", "家族邀请", "联盟聊天", "联盟邀请", "学院招待",
    };

    static int InviteMask => PlayerPrefs.GetInt(PrefInvite, (1 << InviteCount) - 1);

    public static bool Invite(int index)
        => index >= 0 && index < InviteCount && (InviteMask & (1 << index)) != 0;

    public static void SetInvite(int index, bool on)
    {
        if (index < 0 || index >= InviteCount) return;
        int mask = InviteMask;
        mask = on ? (mask | (1 << index)) : (mask & ~(1 << index));
        PlayerPrefs.SetInt(PrefInvite, mask);
        PlayerPrefs.Save();
    }

    public static int HpAlarm
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(PrefHpAlarm, 0), 0, 100); }
        set { PlayerPrefs.SetInt(PrefHpAlarm, Mathf.Clamp(value, 0, 100)); }
    }

    public static int MpAlarm
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(PrefMpAlarm, 0), 0, 100); }
        set { PlayerPrefs.SetInt(PrefMpAlarm, Mathf.Clamp(value, 0, 100)); }
    }

    public static bool ScreenEffects
    {
        get { return PlayerPrefs.GetInt(PrefScreenFx, 1) != 0; }
        set { PlayerPrefs.SetInt(PrefScreenFx, value ? 1 : 0); PlayerPrefs.Save(); }
    }
}

// 三个设置窗共用的窗体外壳：底图 + 标题栏拖动 + 右上角关闭。
static class SettingsShell
{
    public static bool Begin(string background, ref Vector2 pos, int w, int h, float s,
                            ref bool dragging, ref Vector2 grab, out Rect rect)
    {
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(w, h), s);
        rect = new Rect(pos.x * s, pos.y * s, w * s, h * s);
        if (Event.current.type == EventType.Repaint && Pointer.Pressed &&
            new Rect(rect.x, rect.y, rect.width - 24f * s, 22f * s).Contains(Pointer.Pos))
        {
            dragging = true;
            grab = Pointer.Pos / s - pos;
        }
        if (dragging && Pointer.Down)
        {
            pos = NativeUi.ClampWindowPosition(Pointer.Pos / s - grab, new Vector2(w, h), s);
            rect.position = pos * s;
        }
        if (!Pointer.Down) dragging = false;

        var tex = Art.Tex(background);
        if (tex != null) GUI.DrawTexture(rect, tex);

        return !NativeUi.Button(new Rect(rect.x + (w - 19) * s, rect.y + 5 * s, 12 * s, 12 * s),
                                "UI/Basic.img/BtClose");
    }
}

// ——————————————— 游戏设置（GameOpt 158×284，12 个同意/拒绝） ———————————————
// 底图右侧状态格里印死的是「同意」；切到「拒绝」只能先抹掉再重写（一张底图一种状态）。
// 抹色取底图非文字区的中位色（221,221,204），文字色取底图字形实测（34,68,102）。
public static class GameOptWindow
{
    public static bool Visible;
    public static void Toggle() { Visible = !Visible; }
    public static void Close() { Visible = false; }

    const int W = 158, H = 284;
    const int RowTop = 32, RowPitch = 18, BoxX = 90, BoxW = 60, BoxH = 16;

    static Vector2 pos = new Vector2(238, 132);
    static bool dragging;
    static Vector2 grab;
    static float S { get { return BottomBar.S; } }

    static readonly Color BoxFill = new Color32(221, 221, 204, 255);
    static readonly Color BoxText = new Color32(34, 68, 102, 255);

    static Rect At(Rect r, float x, float y, float w, float h)
        => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);

    static void Text(Rect r, string value, Color color)
    {
        var style = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(8, Mathf.RoundToInt(11 * S)),
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0),
        };
        style.normal.textColor = color;
        GUI.Label(r, value, style);
    }

    public static void Draw()
    {
        if (!Visible) return;
        if (!SettingsShell.Begin("wz://UI/UIWindow.img/GameOpt/backgrnd", ref pos, W, H, S,
                                 ref dragging, ref grab, out var r))
        {
            Visible = false;
            return;
        }

        for (int i = 0; i < ClientSettings.InviteCount; i++)
        {
            float y = RowTop + i * RowPitch;
            bool agree = ClientSettings.Invite(i);
            if (!agree)
            {
                UiKit.Fill(At(r, BoxX + 1, y + 1, BoxW - 2, BoxH - 2), BoxFill);
                Text(At(r, BoxX, y, BoxW, BoxH), "拒绝", BoxText);
            }
            // 整行（粉色标签 + 状态格）都能点，和源端这类列表窗的命中习惯一致
            var row = At(r, 3, y - 1, W - 6, BoxH + 2);
            if (Event.current.type == EventType.Repaint && Pointer.Clicked && row.Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick();
                AudioManager.Play("UI/BtMouseClick", 0.7f, false);
                ClientSettings.SetInvite(i, !agree);
            }
        }
    }
}

// ——————————————— 系统设置（SysOpt 299×365） ———————————————
// 行序 = 底图：0 画质调整 1 背景音乐 2 效果音响 3 截图保存位置 4 鼠标速度调整
//             5 HP 警报 6 MP 警报 7 画面效果 8 怪物情报 9 画面模式
public static class SysOptWindow
{
    public static bool Visible;
    public static void Toggle() { Visible = !Visible; }
    public static void Close() { Visible = false; }

    const int W = 299, H = 365;
    const int RowTop = 32, RowPitch = 30, RowH = 28;
    const int RowQuality = 0, RowBgm = 1, RowSfx = 2, RowShot = 3, RowMouse = 4,
              RowHp = 5, RowMp = 6, RowFx = 7, RowMobInfo = 8, RowScreenMode = 9;

    // 轨道横向范围：标签是底图印死的，只能落在它们之间的空白里
    const float TrackX = 100f, TrackW = 148f;        // 画质 / 鼠标 / HP / MP
    const float TrackAudioX = 96f, TrackAudioW = 116f; // 背景音乐 / 效果音响（小..大）
    const float MuteX = 240f;                         // 静音复选框（大与静音之间）
    const float CheckX = 262f;                        // 画面效果 / 怪物情报复选框

    static Vector2 pos = new Vector2(196, 92);
    static bool dragging;
    static Vector2 grab;
    static float S { get { return BottomBar.S; } }
    static int sliderDrag = -1;

    static Rect At(Rect r, float x, float y, float w, float h)
        => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);

    public static void Draw()
    {
        if (!Visible) return;
        if (!SettingsShell.Begin("wz://UI/UIWindow.img/SysOpt/backgrnd", ref pos, W, H, S,
                                 ref dragging, ref grab, out var r))
        {
            Visible = false;
            return;
        }

        // —— 画质调整：手机端由设备/引擎决定，这里只画禁用滑块 ——
        Slider(r, RowQuality, TrackX, TrackW, 0.5f, 100, false);
        HintRow(r, RowQuality);

        // —— 背景音乐 / 效果音响：直接接 AudioManager ——
        float bgm = Slider(r, RowBgm, TrackAudioX, TrackAudioW, AudioManager.BgmVolume, 101, !AudioManager.BgmMuted);
        if (Mathf.Abs(bgm - AudioManager.BgmVolume) > 0.0005f) AudioManager.BgmVolume = bgm;
        Checkbox(r, RowBgm, MuteX, AudioManager.BgmMuted, "音乐已静音", "音乐已恢复",
                 () => AudioManager.BgmMuted = !AudioManager.BgmMuted);

        float sfx = Slider(r, RowSfx, TrackAudioX, TrackAudioW, AudioManager.SfxVolume, 102, !AudioManager.SfxMuted);
        if (Mathf.Abs(sfx - AudioManager.SfxVolume) > 0.0005f) AudioManager.SfxVolume = sfx;
        Checkbox(r, RowSfx, MuteX, AudioManager.SfxMuted, "音效已静音", "音效已恢复",
                 () => AudioManager.SfxMuted = !AudioManager.SfxMuted);

        // —— 截图保存位置：手机端固定写系统相册/下载目录，键帽只是说明位 ——
        RowHit(r, RowShot, () => BottomBar.Toast("手机端截图直接保存到系统相册的 MapleDash 目录"));

        // —— 鼠标速度调整 ——
        Slider(r, RowMouse, TrackX, TrackW, 0.5f, 103, false);
        HintRow(r, RowMouse);

        // —— HP / MP 警报：低于阈值时血条/蓝条按 1 秒周期闪 ——
        int hp = Mathf.RoundToInt(Slider(r, RowHp, TrackX, TrackW, ClientSettings.HpAlarm / 100f, 104, true) * 100f);
        if (hp != ClientSettings.HpAlarm) { ClientSettings.HpAlarm = hp; }
        int mp = Mathf.RoundToInt(Slider(r, RowMp, TrackX, TrackW, ClientSettings.MpAlarm / 100f, 105, true) * 100f);
        if (mp != ClientSettings.MpAlarm) { ClientSettings.MpAlarm = mp; }

        // —— 画面效果：震屏/闪白总开关 ——
        Checkbox(r, RowFx, CheckX, ClientSettings.ScreenEffects, "", "",
                 () => ClientSettings.ScreenEffects = !ClientSettings.ScreenEffects);

        // —— 怪物情报：手机端未开放 ——
        Checkbox(r, RowMobInfo, CheckX, false, "", "", null);

        // —— 画面模式：底图自带说明文字，手机端没有窗口化/全屏切换 ——
        RowHit(r, RowScreenMode, () => BottomBar.Toast("手机端固定全屏，窗口化模式不适用"));
    }

    // 禁用行：整行可点，点了给一句为什么不能用
    static void HintRow(Rect r, int row)
    {
        RowHit(r, row, () => BottomBar.Toast(row == RowQuality ? "画质由手机性能档位决定，此处不适用"
                                                               : "手机端没有鼠标，速度调整不适用"));
    }

    static void RowHit(Rect r, int row, System.Action onClick)
    {
        var hit = At(r, 65, RowTop + row * RowPitch, 226, RowH);
        if (Event.current.type == EventType.Repaint && Pointer.Clicked && hit.Contains(Pointer.Pos))
        {
            Pointer.ConsumeClick();
            onClick();
        }
    }

    // 滑块：轨道 8 高（left 3 + 平铺 mid + right 3），滑块头 22×14。
    // 按下即开始拖，拖动期间 Pointer 的纵坐标不参与计算（和原生同类控件一致）。
    static float Slider(Rect r, int row, float x0, float w, float value, int id, bool enabled)
    {
        float top = RowTop + row * RowPitch;
        var track = At(r, x0, top + 10f, w, 8f);
        var left = Art.Tex("wz://UI/Basic.img/Slider/left");
        var mid = Art.Tex("wz://UI/Basic.img/Slider/mid");
        var right = Art.Tex("wz://UI/Basic.img/Slider/right");
        if (mid != null)
            GUI.DrawTexture(new Rect(track.x + 3f * S, track.y, Mathf.Max(0f, track.width - 6f * S), track.height),
                mid, ScaleMode.StretchToFill);
        if (left != null) GUI.DrawTexture(new Rect(track.x, track.y, 3f * S, track.height), left);
        if (right != null) GUI.DrawTexture(new Rect(track.xMax - 3f * S, track.y, 3f * S, track.height), right);

        float thumbW = 22f * S, thumbH = 14f * S;
        var hit = new Rect(track.x - 4f * S, track.y - 5f * S, track.width + 8f * S, thumbH + 4f * S);
        bool hover = hit.Contains(Pointer.Pos);

        if (Event.current.type == EventType.Repaint)
        {
            if (enabled && Pointer.Pressed && hit.Contains(Pointer.Pos))
            {
                sliderDrag = id;
                Pointer.ConsumeClick();
            }
            if (sliderDrag == id && (!Pointer.Down || !enabled)) sliderDrag = -1;
        }
        if (enabled && sliderDrag == id && Pointer.Down)
        {
            float travel = Mathf.Max(1f, track.width - thumbW);
            value = Mathf.Clamp01((Pointer.Pos.x - track.x - thumbW * 0.5f) / travel);
        }

        string state = !enabled ? "thumbDisabled"
                     : sliderDrag == id ? "thumbPressed"
                     : hover ? "thumbMouseOver" : "thumbNormal";
        var thumb = Art.Tex("wz://UI/Basic.img/Slider/" + state);
        float tx = track.x + Mathf.Max(0f, track.width - thumbW) * Mathf.Clamp01(value);
        if (thumb != null) GUI.DrawTexture(new Rect(tx, track.y + track.height * 0.5f - thumbH * 0.5f, thumbW, thumbH), thumb);
        return Mathf.Clamp01(value);
    }

    static void Checkbox(Rect r, int row, float x, bool on, string toastOn, string toastOff,
                         System.Action toggle)
    {
        var box = At(r, x, RowTop + row * RowPitch + 8f, 12f, 12f);
        var tex = Art.Tex("wz://UI/Basic.img/CheckBox/" + (on ? 1 : 0));
        if (tex != null) GUI.DrawTexture(box, tex);
        if (Event.current.type != EventType.Repaint || !Pointer.Clicked) return;
        if (!box.Contains(Pointer.Pos)) return;
        Pointer.ConsumeClick();
        if (toggle == null) { BottomBar.Toast("手机端未开放"); return; }
        AudioManager.Play("UI/BtMouseClick", 0.7f, false);
        toggle();
        bool now = !on;
        string text = now ? toastOn : toastOff;
        if (!string.IsNullOrEmpty(text)) BottomBar.Toast(text);
    }
}

// ——————————————— 频道变更（Channel 436 宽三片窗口） ———————————————
// 三片素材：标题 t 436×67 / 中段 c 436×10 平铺 / 底部 s 436×36。
// 本机服务器只有 1 个频道，所以「变更」按钮保持禁用，只显示当前频道并说明原因。
public static class ChannelWindow
{
    public static bool Visible;
    public static void Toggle() { Visible = !Visible; }
    public static void Close() { Visible = false; }

    const int W = 436, H = 143;     // 67 + 4×10 + 36
    const int BodyTop = 67, BodyH = 40, BottomH = 36;
    // t 片里那块列表框：逐像素量 t 的 2× 原图，蓝色渐变内区在逻辑坐标
    // x 8..426 / y 28..53（高 25）。行素材 68×20，垂直居中 → y=30。
    const int ListX = 16, ListY = 30;

    static Vector2 pos = new Vector2(220, 140);
    static bool dragging;
    static Vector2 grab;
    static float S { get { return BottomBar.S; } }

    static Rect At(Rect r, float x, float y, float w, float h)
        => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);

    static void Text(Rect rect, string value, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        var style = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(8, Mathf.RoundToInt(11 * S)),
            alignment = anchor,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0),
        };
        style.normal.textColor = color;
        GUI.Label(rect, value, style);
    }

    public static void Draw()
    {
        if (!Visible) return;

        pos = NativeUi.ClampWindowPosition(pos, new Vector2(W, H), S);
        var r = new Rect(pos.x * S, pos.y * S, W * S, H * S);
        if (Event.current.type == EventType.Repaint && Pointer.Pressed &&
            At(r, 0, 0, W, 26).Contains(Pointer.Pos))
        {
            dragging = true;
            grab = Pointer.Pos / S - pos;
        }
        if (dragging && Pointer.Down)
        {
            pos = NativeUi.ClampWindowPosition(Pointer.Pos / S - grab, new Vector2(W, H), S);
            r.position = pos * S;
        }
        if (!Pointer.Down) dragging = false;

        var top = Art.Tex("wz://UI/UIWindow.img/Channel/t");
        var mid = Art.Tex("wz://UI/UIWindow.img/Channel/c");
        var bottom = Art.Tex("wz://UI/UIWindow.img/Channel/s");
        if (top != null) GUI.DrawTexture(At(r, 0, 0, W, 67), top);
        if (mid != null)
            for (int y = BodyTop; y < BodyTop + BodyH; y += 10)
                GUI.DrawTexture(At(r, 0, y, W, Mathf.Min(10, BodyTop + BodyH - y)), mid);
        if (bottom != null) GUI.DrawTexture(At(r, 0, H - BottomH, W, BottomH), bottom);

        // 频道列表：源端用 channel0/1/2（68×20 行底：普通 / 灰 / 蓝选中，行底自带左侧小三角）
        // ＋ ch/0..9（20×10 的「CH.N」，ch/0 才是 CH.1）拼出来。本机只有一个频道，只画当前这一行。
        const int RowW = 68, RowH = 20;
        var row = At(r, ListX, ListY, RowW, RowH);
        var rowBg = Art.Tex("wz://UI/UIWindow.img/Channel/channel2");
        if (rowBg != null) GUI.DrawTexture(row, rowBg);
        var label = Art.Tex("wz://UI/UIWindow.img/Channel/ch/0");
        if (label != null) GUI.DrawTexture(At(r, ListX + 14, ListY + 5, 20, 10), label);

        // 正文：说明为什么切不了（当前频道已由列表行 CH.1 表示，不再重复一行）
        Text(At(r, 18, BodyTop + 2, 400, 14), "本机服务器只开放 1 个频道，无法变更。", Color.black);

        // 按钮：BtCancel 47×19、BtChange 60×19，居中排在底部片
        float bx = (W - (47 + 10 + 60)) * 0.5f, by = H - BottomH + 9;
        if (NativeUi.Button(At(r, bx, by, 47, 19), "UI/UIWindow.img/Channel/BtCancel"))
        {
            Visible = false;
            return;
        }
        var disabled = Art.Tex("wz://UI/UIWindow.img/Channel/BtChange/disabled/0");
        if (disabled != null) GUI.DrawTexture(At(r, bx + 57, by, 60, 19), disabled);
    }
}
