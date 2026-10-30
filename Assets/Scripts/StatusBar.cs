using UnityEngine;

// Original StatusBar IMG canvas crops and native numeric glyphs; dynamic identity text is rendered at runtime.
public static class StatusBar
{
    // —— 原图像素常量 ——
    const int PanelW = 570, PanelH = 35;
    const int GaugeX = 216, GaugeY = 2;        // gauge/bar 在面板内的左上角
    const int LabelH = 13, BarH = 18;          // 上排文字 / 下排槽体
    const int LevelX = 42, LevelY = 11;         // 数字紧跟LV.，不随位数居中
    const int NameX = 88;                      // 名字/职业的左边距

    static readonly int[] GaugeW = { 109, 108, 123 };
    // 槽宽里 HP/MP/EXP 字样本身占的像素（label 贴图的非透明包围盒宽度），
    // 数值紧接在它后面画——和原版截图一致。
    static readonly float[] LabelTextW = { 18f, 20f, 29f };
    static readonly string[] GaugeName = { "hp", "mp", "exp" };
    static readonly string[] LabelRel =
    {
        "ui/statusbar/gauge_hp_label.bytes", "ui/statusbar/gauge_mp_label.bytes", "ui/statusbar/gauge_exp_label.bytes"
    };
    static readonly string[] FillRel =
    {
        "ui/statusbar/gauge_hp_fill.bytes", "ui/statusbar/gauge_mp_fill.bytes", "ui/statusbar/gauge_exp_fill.bytes"
    };
    static readonly string[] FrameRel =
    {
        "ui/statusbar/gauge_hp_frame.bytes", "ui/statusbar/gauge_mp_frame.bytes", "ui/statusbar/gauge_exp_frame.bytes"
    };

    static bool loaded;
    static Texture2D panel, gray, digitSlash, digitPercent, digitLb, digitRb;
    static Texture2D[] labels = new Texture2D[3], fills = new Texture2D[3], frames = new Texture2D[3];
    static Texture2D[] digits = new Texture2D[10], levelNo = new Texture2D[10];
    static Font font;
    static bool fontTried;

    static readonly GUIStyle textStyle = new GUIStyle();

    // 整块面板当前所在的位置与缩放（虚拟像素）。换分辨率时每帧重算。
    public static float S { get; private set; } = 1f;
    public static Rect Panel { get; private set; }

    // 与整组底栏共用像素倍率；不再按720/600重复放大状态区。
    public static float ComputeScale()
    {
        return BottomBar.S;
    }

    public static void Draw()
    {
        Load();
        if (panel == null) return;

        S = ComputeScale();
        float w = PanelW * S, h = PanelH * S;
        // 原端 backgrnd2 画在 (2,507)：底板左缘从 x=2 起，不再贴屏幕边
        Panel = new Rect(BottomBar.PlateX * S, UiKit.H - h, w, h);
        GUI.DrawTexture(Panel, panel, ScaleMode.StretchToFill);

        DrawLevelTag();
        DrawIdentity();
        DrawGauges();
    }

    // —— LV. 铭牌里的橙色数字（Basic.img/LevelNo，11x13）——
    static void DrawLevelTag()
    {
        int lv = Mathf.Max(1, Profile.level);
        string s = lv.ToString();
        float x0 = LevelX;
        float y = LevelY;
        for (int i = 0; i < s.Length; i++)
        {
            var tex = Digit(levelNo, s[i]);
            if (tex == null) continue;
            GUI.DrawTexture(At(x0 + i * 11f, y, 11f, 13f), tex, ScaleMode.StretchToFill);
        }
    }

    // —— 职业（上）+ 名字（下）——
    // 原版截图里就是这两行的顺序：LV. 铭牌右侧先职业后名字。
    static void DrawIdentity()
    {
        string name = string.IsNullOrEmpty(Profile.playerName) ? Profile.DefaultName : Profile.playerName;
        string job = ClassDefs.Get(Profile.cls).name;

        // 参考图：两行均为浅灰常规字，字号一致，共用左对齐线。
        var identity = Style(12, new Color32(192, 198, 200, 255), TextAnchor.UpperLeft, FontStyle.Normal);
        Shadow(At(NameX, 3f, 116f, 15f), job, identity);
        Shadow(At(NameX, 17f, 116f, 15f), name, identity);
    }

    // —— 三条槽 ——
    static void DrawGauges()
    {
        var p = GameLoop.I != null ? GameLoop.I.player : null;

        float hp = p != null ? p.hp : Profile.Attributes.maxHp;
        float hpMax = p != null ? Mathf.Max(1f, p.hpMax) : Profile.Attributes.maxHp;
        float mp = p != null ? p.mp : Profile.Attributes.maxMp;
        float mpMax = p != null ? Mathf.Max(1f, p.mpMax) : Profile.Attributes.maxMp;
        float[] ratio = { Mathf.Clamp01(hp / hpMax), Mathf.Clamp01(mp / mpMax), Profile.ExpRatio };

        TrackGauges(ratio);

        float x = GaugeX;
        for (int i = 0; i < 3; i++)
        {
            int gw = GaugeW[i];

            // 上排：原版 HP / MP / EXP 字样
            if (labels[i] != null)
                GUI.DrawTexture(At(x, GaugeY, gw, LabelH), labels[i], ScaleMode.StretchToFill);

            // 下排三层，顺序和原版一致：空槽底色 → 按比例裁剪的填充 → 外框+刻度
            float barY = GaugeY + LabelH;
            // gauge/gray 是 1x16 的 #CCCCCC 纯色条：没填满的那一段是浅灰，不是面板底色。
            // 少了这一层，空槽会直接露出深色面板，和原版差得很明显。
            if (gray != null)
                GUI.DrawTexture(At(x, barY + 1f, gw, BarH - 2f), gray, ScaleMode.StretchToFill);
            DrawClippedFill(At(x, barY, gw * GaugeRatio(i, Time.unscaledTime), BarH), fills[i], gw);
            if (frames[i] != null)
                GUI.DrawTexture(At(x, barY, gw, BarH), frames[i], ScaleMode.StretchToFill);

            // 原版受击反馈：hpFlash / mpFlash 是 5 帧（0/1 交替，109×18），叠在槽体上
            if (i < 2) DrawFlash(i, x, barY);
            // Native tempExp is banked Gacha EXP, not a timed recent-gain flash.
            if (i == 2) DrawTemporaryExp(x, barY);

            // 数值：紧跟在 HP/MP/EXP 字样后面。原版就是这个排法（截图里是 HP[384/435]），
            // 不是右对齐到槽尾——槽尾那一段是留给填充余量的。
            string value = i == 0 ? Number(hp, hpMax)
                         : i == 1 ? Number(mp, mpMax)
                         : Mathf.FloorToInt(ratio[2] * 100f) + "%";
            DrawNumber(Panel.x + (x + LabelTextW[i] + 1f) * S, Panel.y + (GaugeY + 1f) * S, value);

            x += gw;
        }
    }

    // ————— 原版受击闪烁 / 经验增量 —————
    // 掉血掉蓝：客户端里 hpFlash、mpFlash 各 5 帧（引用 0/1 两张图交替），
    // 每帧 100ms，走完 0.5 秒收工。只在数值下降时触发，升级/回血不闪。
    const float FlashFrame = 0.1f;
    const int FlashFrames = 5;
    static float hpFlashAt = -99f, mpFlashAt = -99f;
    static float lastHp = 1f, lastMp = 1f;
    static bool gaugeInit;
    // CUIStatusBar::SetNumberValue 8D8BAC/8D932C: HP/MP vectors move over 700ms.
    const float GaugeDuration = .7f;
    static readonly float[] gaugeFrom = new float[3], gaugeTo = new float[3], gaugeAt = new float[3];
    static float GaugeRatio(int index, float now) => Mathf.Lerp(gaugeFrom[index], gaugeTo[index],
        Mathf.Clamp01((now - gaugeAt[index]) / GaugeDuration));

    static void TrackGauges(float[] ratio)
    {
        float now = Time.unscaledTime;
        if (!gaugeInit)
        {
            gaugeInit = true; lastHp = ratio[0]; lastMp = ratio[1];
            for (int i = 0; i < 3; i++) gaugeFrom[i] = gaugeTo[i] = ratio[i];
            return;
        }
        for (int i = 0; i < 2; i++)
            if (ratio[i] != gaugeTo[i])
            { gaugeFrom[i] = GaugeRatio(i, now); gaugeTo[i] = ratio[i]; gaugeAt[i] = now; }
        // EXP's base fill updates immediately; temporary EXP is a separate stat.
        gaugeFrom[2] = gaugeTo[2] = ratio[2];
        if (ratio[0] < lastHp - 0.0005f) hpFlashAt = now;
        if (ratio[1] < lastMp - 0.0005f) mpFlashAt = now;
        lastHp = ratio[0]; lastMp = ratio[1];
    }

    static void DrawFlash(int index, float x, float barY)
    {
        float since = Time.unscaledTime - (index == 0 ? hpFlashAt : mpFlashAt);
        if (since < 0f || since >= FlashFrame * FlashFrames) return;
        int frame = Mathf.Clamp(Mathf.FloorToInt(since / FlashFrame), 0, FlashFrames - 1) % 2;
        var tex = Art.Tex("ui/statusbar/" + (index == 0 ? "hp" : "mp") + "flash_" + frame + ".bytes");
        if (tex == null) return;
        GUI.DrawTexture(At(x, barY, tex.width, tex.height), tex, ScaleMode.StretchToFill);
    }

    // 8DA0A9..8DA1C2: floor(115 * min(gachaExp, maxExp-exp) / maxExp).
    static Rect TemporaryExpRect(long exp, long maximum, long temporary)
    {
        if (maximum <= 0) return Rect.zero;
        exp = System.Math.Max(0, System.Math.Min(maximum, exp));
        temporary = System.Math.Max(0, System.Math.Min(maximum - exp, temporary));
        int from = (int)(115 * exp / maximum);
        int width = (int)(115 * temporary / maximum);
        // Native x=441..556, relative to the EXP frame at x=435.
        return new Rect(6 + from, 1, width, 15);
    }

    static void DrawTemporaryExp(float x, float barY)
    {
        long temporary = GameSession.Character?.record?.gachaexp ?? 0;
        var r = TemporaryExpRect(Profile.experience, Profile.ExpNeeded, temporary);
        if (r.width <= 0) return;
        var tex = Art.Tex("wz://UI/StatusBar.img/gauge/tempExp");
        if (tex == null) return;
        GUI.DrawTexture(At(x + r.x, barY + r.y, r.width, r.height),
            tex, ScaleMode.StretchToFill);
    }

    static string Number(float current, float max)
        => "[" + Mathf.CeilToInt(current) + "/" + Mathf.CeilToInt(max) + "]";

    // 填充层按比例裁剪：用 GUI 组做硬裁剪，避免缩放采样把像素糊掉
    static void DrawClippedFill(Rect clipped, Texture2D fill, int fullWidth)
    {
        if (fill == null || clipped.width <= 0.5f) return;
        GUI.BeginGroup(clipped);
        GUI.DrawTexture(new Rect(0f, 0f, fullWidth * S, BarH * S), fill, ScaleMode.StretchToFill);
        GUI.EndGroup();
    }

    // —— 原版点阵字形（StatusBar.img/number：数字 5x7、斜杠 7x7、方括号 2x9）——
    // 从 leftX 往右摆；所有字形按 9px 的 band 垂直居中对齐（方括号 9 高、数字 7 高）。
    static void DrawNumber(float leftX, float topY, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        float x = leftX;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            var tex = Glyph(c);
            if (tex != null)
            {
                float y = topY + (9f - tex.height) * 0.5f * S;
                GUI.DrawTexture(new Rect(x, y, tex.width * S, tex.height * S), tex, ScaleMode.StretchToFill);
            }
            x += Advance(c) * S;
        }
    }

    static Texture2D Glyph(char c)
    {
        switch (c)
        {
            case '/': return digitSlash;
            case '%': return digitPercent;
            case '[': return digitLb;
            case ']': return digitRb;
            default: return Digit(digits, c);
        }
    }

    static float Advance(char c)
    {
        switch (c)
        {
            case '/':
            case '%': return 8f;
            case '[':
            case ']': return 3f;
            default: return 6f;
        }
    }

    static Texture2D Digit(Texture2D[] set, char c)
    {
        if (c < '0' || c > '9' || set == null) return null;
        return set[c - '0'];
    }

    // —————————— 布局 & 资源 ——————————

    // 原图像素矩形 → 屏幕虚拟矩形（相对面板原点）
    static Rect At(float ox, float oy, float w, float h)
        => new Rect(Panel.x + ox * S, Panel.y + oy * S, w * S, h * S);

    static GUIStyle Style(int size, Color color, TextAnchor anchor, FontStyle style)
    {
        textStyle.font = Font();
        textStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S));
        textStyle.alignment = anchor;
        textStyle.fontStyle = style;
        textStyle.normal.textColor = color;
        textStyle.wordWrap = false;
        textStyle.clipping = TextClipping.Clip;
        return textStyle;
    }

    // 原版是带描边的白字；GUI 只画一层阴影也能在原画背景上读清
    static void Shadow(Rect r, string value, GUIStyle style)
    {
        var old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(r.x + 0.5f * S, r.y + 0.5f * S, r.width, r.height), value, style);
        style.normal.textColor = old;
        GUI.Label(r, value, style);
    }

    static Font Font()
    {
        if (fontTried) return font;
        fontTried = true;
        font = Resources.Load<Font>("Fonts/ui");
        return font;
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;

        panel = Art.Tex("ui/statusbar/bar_lv.bytes");
        for (int i = 0; i < 3; i++)
        {
            labels[i] = Art.Tex(LabelRel[i]);
            fills[i] = Art.Tex(FillRel[i]);
            frames[i] = Art.Tex(FrameRel[i]);
        }
        for (int i = 0; i < 10; i++)
        {
            digits[i] = Art.Tex("ui/statusbar/num_" + i + ".bytes");
            levelNo[i] = Art.Tex("ui/level/lv_" + i + ".bytes");
        }
        gray = Art.Tex("ui/statusbar/gauge_gray.bytes");
        digitSlash = Art.Tex("ui/statusbar/num_slash.bytes");
        digitPercent = Art.Tex("ui/statusbar/num_percent.bytes");
        digitLb = Art.Tex("ui/statusbar/num_lb.bytes");
        digitRb = Art.Tex("ui/statusbar/num_rb.bytes");
    }
}
