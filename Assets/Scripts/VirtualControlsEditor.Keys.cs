using UnityEngine;

// ————————————————————————————————————————————————————————————
// 虚拟按键编辑器 —— 「选择按键」全屏弹窗
//   左上：← 返回 + 标题；右上：主键盘 / 鼠标按键 / 游戏手柄
//   主键盘：默认 / Shift+ 两段切换；右侧 控制键 / 数字键 切换
//   这里只做单键绑定（Shift+ 时同时记 shiftMod，运行时会连 LeftShift 一起发）
// ————————————————————————————————————————————————————————————
public static partial class VirtualControlsEditor
{
    static int keyTab;        // 0 主键盘 · 1 鼠标 · 2 手柄
    static bool keyShift;     // 默认 / Shift+
    static int padTab;        // 0 控制键 · 1 数字键

    // 弹窗这次要落的键：0 = 控件主键；1~4 = 摇杆的 上/下/左/右
    static int keyTarget;

    static KeyCode CurKey()
    {
        if (selected == null) return KeyCode.None;
        if (keyTarget <= 0 || selected.type == VCElemType.Button) return selected.key;
        return selected.DirKey(keyTarget - 1);
    }

    static readonly Color KeyBg = new Color(0.110f, 0.110f, 0.118f, 1f);
    static readonly Color KeyBgOn = new Color(0.10f, 0.22f, 0.34f, 1f);
    static readonly Color KeyTxt = new Color(0.86f, 0.87f, 0.89f, 1f);
    static readonly Color KeyTxtDim = new Color(0.44f, 0.44f, 0.47f, 1f);
    static readonly Color SheetBg = new Color(0.043f, 0.043f, 0.047f, 1f);

    static void KeySheet(bool act)
    {
        UiKit.Fill(new Rect(0f, 0f, UiKit.W, UiKit.H), SheetBg);

        float margin = 54f;
        float rightW = 400f;
        float gap = 28f;
        float kw = Mathf.Max(620f, UiKit.W - margin * 2f - gap - rightW);
        float x0 = (UiKit.W - (kw + gap + rightW)) * 0.5f;

        // —— 页头 ——
        var br = new Rect(x0 - 6f, 16f, 48f, 48f);
        ArrowBack(new Vector2(br.center.x + 2f, br.center.y), br.Contains(aPos) ? Color.white : Ink);
        if (Tap(br, act)) OpenPage(P_Edit);
        string sheetTitle = keyTarget > 0 && keyTarget <= 4 ? "选择按键 · " + DirNames[keyTarget - 1] : "选择按键";
        Text(new Rect(x0 + 52f, 16f, 400f, 48f), sheetTitle, 31, TxtMain, TextAnchor.MiddleLeft);

        // —— 右上：输入设备切换 ——
        float tw = 152f;
        float tx = UiKit.W - margin - tw * 3f - 26f;
        Tab(new Rect(tx, 16f, tw, 50f), "主键盘", 0, act);
        Tab(new Rect(tx + tw + 12f, 16f, tw, 50f), "鼠标按键", 1, act);
        Tab(new Rect(tx + (tw + 12f) * 2f, 16f, tw, 50f), "游戏手柄", 2, act);

        if (keyTab == 0) KeyboardPage(act, x0, kw, gap, rightW);
        else if (keyTab == 1) MousePage(act);
        else PadPage(act);
    }

    static void Tab(Rect r, string label, int id, bool act)
    {
        bool on = keyTab == id;
        Rounded(r, on ? Accent : KeyBg);
        GlyphDevice(new Vector2(r.x + 25f, r.center.y), on ? Color.white : TxtSub, id);
        Text(new Rect(r.x + 44f, r.y, r.width - 50f, r.height), label, 20, on ? Color.white : TxtSub, TextAnchor.MiddleLeft);
        if (Tap(r, act)) { keyTab = id; }
    }

    // 设备小图标：键盘 / 鼠标 / 手柄（坐标按 GS 放大，和工具条同一比例）
    static void GlyphDevice(Vector2 c, Color col, int kind)
    {
        float s = GS;
        if (kind == 0)
        {
            var r = new Rect(c.x - 8f * s, c.y - 6f * s, 16f * s, 12f * s);
            Outline(r, col, 1.4f * s);
            UiKit.Fill(new Rect(r.x + 3f * s, r.y + 3f * s, 2.4f * s, 2.4f * s), col);
            UiKit.Fill(new Rect(r.x + 7f * s, r.y + 3f * s, 2.4f * s, 2.4f * s), col);
            UiKit.Fill(new Rect(r.x + 11f * s, r.y + 3f * s, 2.4f * s, 2.4f * s), col);
            UiKit.Fill(new Rect(r.x + 4f * s, r.y + 8f * s, 8f * s, 2f * s), col);
        }
        else if (kind == 1)
        {
            var r = new Rect(c.x - 6f * s, c.y - 8f * s, 12f * s, 16f * s);
            Rounded(r, col);
            UiKit.Fill(new Rect(c.x - 0.9f * s, r.y + 2f * s, 1.8f * s, 5f * s), new Color(0.05f, 0.05f, 0.05f, 1f));
        }
        else
        {
            var r = new Rect(c.x - 9f * s, c.y - 5f * s, 18f * s, 10f * s);
            Rounded(r, col);
            UiKit.Fill(new Rect(r.x + 3f * s, c.y - 2f * s, 3f * s, 3f * s), new Color(0.05f, 0.05f, 0.05f, 1f));
            UiKit.Fill(new Rect(r.xMax - 6f * s, c.y - 2f * s, 3f * s, 3f * s), new Color(0.05f, 0.05f, 0.05f, 1f));
        }
    }

    // —————————— 主键盘 ——————————
    static void KeyboardPage(bool act, float x0, float kw, float gap, float rightW)
    {
        // 默认 / Shift+
        float half = kw * 0.5f;
        var seg = new Rect(x0, 80f, kw, 50f);
        Rounded(seg, KeyBg);
        if (!keyShift) Rounded(new Rect(seg.x, seg.y, half, seg.height), Accent);
        else Rounded(new Rect(seg.x + half, seg.y, half, seg.height), Accent);
        Text(new Rect(seg.x, seg.y, half, seg.height), "默认", 22, keyShift ? TxtSub : Color.white, TextAnchor.MiddleCenter);
        Text(new Rect(seg.x + half, seg.y, half, seg.height), "Shift +", 22, keyShift ? Color.white : TxtSub, TextAnchor.MiddleCenter);
        if (Tap(new Rect(seg.x, seg.y, half, seg.height), act)) keyShift = false;
        if (Tap(new Rect(seg.x + half, seg.y, half, seg.height), act)) keyShift = true;

        float top0 = 148f;
        float avail = UiKit.H - top0 - 30f;
        const int rows = 6;
        float rowGap = 8f;
        float unit = kw / 15f;

        // 行高反过来由"键宽"决定：早先按可用高度均分，6 行会把键帽拉成 43×84 的竖条，
        // 和参考图里接近方形的键帽完全不是一个东西。
        float rh = Mathf.Clamp(unit * 1.02f, 40f, 76f);
        float totalH = rows * rh + rowGap * (rows - 1);
        float top = top0 + Mathf.Max(0f, (avail - totalH) * 0.5f);

        // F 键行：Esc 1.5u + F1~F12 各 1.125u = 15u
        float fy = top;
        KeyRow(RowFuncs, x0, fy, unit, rh, act);

        float y = fy + rh + rowGap;
        KeyRow(RowDigits, x0, y, unit, rh, act); y += rh + rowGap;
        KeyRow(RowQwerty, x0, y, unit, rh, act); y += rh + rowGap;
        KeyRow(RowHome, x0, y, unit, rh, act); y += rh + rowGap;
        KeyRow(RowBottom, x0, y, unit, rh, act); y += rh + rowGap;
        KeyRow(RowSpace, x0, y, unit, rh, act);

        // —— 右侧：控制键 / 数字键 ——
        float rx = x0 + kw + gap;
        float pw = (rightW - 10f) * 0.5f;
        if (ToggleChip(new Rect(rx, 80f, pw, 50f), "控制键", padTab == 0, act)) padTab = 0;
        if (ToggleChip(new Rect(rx + pw + 10f, 80f, pw, 50f), "数字键", padTab == 1, act)) padTab = 1;

        if (padTab == 0) ControlPad(rx, top, rightW, rh, rowGap, act);
        else NumPad(rx, top, rightW, rh, rowGap, act);
    }

    static void KeyRow((KeyCode k, string l, string s, float w)[] row, float x, float y, float unit, float h, bool act)
    {
        foreach (var it in row)
        {
            KeyCap(new Rect(x, y, Mathf.Max(18f, it.w * unit - 7f), h), it.k, it.l, it.s, act);
            x += it.w * unit;
        }
    }

    static void KeyCap(Rect r, KeyCode k, string label, string shiftLabel, bool act)
    {
        bool shiftMode = keyShift;
        string txt = label;
        bool dim = false;
        if (shiftMode)
        {
            if (!string.IsNullOrEmpty(shiftLabel)) txt = shiftLabel;
            else dim = true;
        }

        var ck = CurKey();
        bool cur = ck != KeyCode.None && ck == k && (!shiftMode || selected == null || selected.shiftMod);
        Rounded(r, cur ? KeyBgOn : KeyBg);
        if (cur) StrokeRect(r, SelectRing, cur ? KeyBgOn : KeyBg, 2f);

        // 字号跟着键帽高度走，但长标签（L Shift / PrtScr）要按宽度收回来，别溢出
        int fs = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.40f), 12, 30);
        if (!string.IsNullOrEmpty(txt))
        {
            float avail = r.width - 10f;
            int fit = Mathf.FloorToInt(avail / (txt.Length * 0.56f));
            fs = Mathf.Clamp(Mathf.Min(fs, fit), 11, 30);
        }
        Text(r, txt, fs, dim ? KeyTxtDim : KeyTxt, TextAnchor.MiddleCenter);

        if (Tap(r, act)) ApplyKey(k);
    }

    static void ControlPad(float rx, float y, float w, float rh, float gap, bool act)
    {
        float cw = (w - gap * 2f) / 3f;
        var cells = new (KeyCode k, string l)[]
        {
            (KeyCode.Print, "PrtScr"), (KeyCode.ScrollLock, "ScrLK"), (KeyCode.Pause, "Pause"),
            (KeyCode.Insert, "Ins"), (KeyCode.Home, "Home"), (KeyCode.PageUp, "PgUp"),
            (KeyCode.Delete, "Del"), (KeyCode.End, "End"), (KeyCode.PageDown, "PgDn"),
        };
        for (int i = 0; i < cells.Length; i++)
        {
            int c = i % 3, row = i / 3;
            KeyCap(new Rect(rx + c * (cw + gap), y + row * (rh + gap), cw, rh), cells[i].k, cells[i].l, null, act);
        }

        float ay = y + 3f * (rh + gap) + gap;
        KeyCap(new Rect(rx + cw + gap, ay, cw, rh), KeyCode.UpArrow, "↑", null, act);
        KeyCap(new Rect(rx, ay + rh + gap, cw, rh), KeyCode.LeftArrow, "←", null, act);
        KeyCap(new Rect(rx + cw + gap, ay + rh + gap, cw, rh), KeyCode.DownArrow, "↓", null, act);
        KeyCap(new Rect(rx + (cw + gap) * 2f, ay + rh + gap, cw, rh), KeyCode.RightArrow, "→", null, act);
    }

    static void NumPad(float rx, float y, float w, float rh, float gap, bool act)
    {
        float cw = (w - gap * 3f) / 4f;
        var cells = new (KeyCode k, string l)[]
        {
            (KeyCode.Numlock, "Num"), (KeyCode.KeypadDivide, "/"), (KeyCode.KeypadMultiply, "*"), (KeyCode.KeypadMinus, "-"),
            (KeyCode.Keypad7, "7"), (KeyCode.Keypad8, "8"), (KeyCode.Keypad9, "9"), (KeyCode.KeypadPlus, "+"),
            (KeyCode.Keypad4, "4"), (KeyCode.Keypad5, "5"), (KeyCode.Keypad6, "6"), (KeyCode.None, ""),
            (KeyCode.Keypad1, "1"), (KeyCode.Keypad2, "2"), (KeyCode.Keypad3, "3"), (KeyCode.KeypadEnter, "Ent"),
            (KeyCode.Keypad0, "0"), (KeyCode.KeypadPeriod, "."), (KeyCode.None, ""), (KeyCode.None, ""),
        };
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i].k == KeyCode.None) continue;
            int c = i % 4, row = i / 4;
            KeyCap(new Rect(rx + c * (cw + gap), y + row * (rh + gap), cw, rh), cells[i].k, cells[i].l, null, act);
        }
    }

    // —————————— 鼠标 ——————————
    static void MousePage(bool act)
    {
        var items = new (KeyCode k, string l, int icon)[]
        {
            (KeyCode.Mouse0, "左键", 0), (KeyCode.Mouse1, "右键", 1), (KeyCode.Mouse2, "中键", 2),
            (KeyCode.Mouse3, "上侧键", 3), (KeyCode.None, "上滚轮", 4),
            (KeyCode.None, "下滚轮", 5), (KeyCode.Mouse4, "下侧键", 6),
            (KeyCode.None, "鼠标上移", 7), (KeyCode.None, "鼠标下移", 8), (KeyCode.None, "鼠标左移", 9),
            (KeyCode.None, "鼠标右移", 10),
        };

        float margin = 54f;
        const int cols = 5;
        float gap = 16f;
        float cw = (UiKit.W - margin * 2f - gap * (cols - 1)) / cols;
        float ch = 112f;
        float top = 128f;

        for (int i = 0; i < items.Length; i++)
        {
            int c = i % cols, row = i / cols;
            var cell = new Rect(margin + c * (cw + gap), top + row * (ch + gap), cw, ch);
            bool usable = items[i].k != KeyCode.None;
            bool cur = usable && CurKey() == items[i].k;

            Rounded(cell, cur ? KeyBgOn : KeyBg);
            if (cur) Outline(cell, SelectRing, 2f);

            var col = usable ? KeyTxt : KeyTxtDim;
            MouseGlyph(new Vector2(cell.center.x, cell.y + 38f), col, items[i].icon);
            Text(new Rect(cell.x, cell.yMax - 44f, cell.width, 30f), items[i].l, 19, col, TextAnchor.MiddleCenter);

            if (usable && Tap(cell, act)) ApplyKey(items[i].k);
        }
    }

    // 0 左 1 右 2 中 3 上侧 4 上滚 5 下滚 6 下侧 7 上 8 下 9 左 10 右
    static void MouseGlyph(Vector2 c, Color col, int kind)
    {
        float s = GS;
        if (kind >= 7)
        {
            var a = kind == 7 ? new Vector2(0f, -9f) : kind == 8 ? new Vector2(0f, 9f) : kind == 9 ? new Vector2(-9f, 0f) : new Vector2(9f, 0f);
            a *= s;
            VirtualControls.DrawLine(c + a * 0.2f, c + a, col, 2.4f * s);
            var n = new Vector2(-a.y, a.x).normalized * (4f * s);
            VirtualControls.DrawLine(c + a, c + a - a.normalized * (4f * s) + n, col, 2.4f * s);
            VirtualControls.DrawLine(c + a, c + a - a.normalized * (4f * s) - n, col, 2.4f * s);
            return;
        }

        var body = new Rect(c.x - 9f * s, c.y - 12f * s, 18f * s, 24f * s);
        Rounded(body, new Color(col.r, col.g, col.b, col.a * 0.30f));
        Outline(body, col, 1.6f * s);

        if (kind == 0) UiKit.Fill(new Rect(body.x + 1.4f, body.y + 1.4f, 7f * s, 9f * s), col);
        else if (kind == 1) UiKit.Fill(new Rect(body.center.x + 0.6f, body.y + 1.4f, 7f * s, 9f * s), col);
        else if (kind == 2) UiKit.Fill(new Rect(body.center.x - 2f * s, body.y + 1.4f, 4f * s, 7f * s), col);
        else if (kind == 3) UiKit.Fill(new Rect(body.x, body.y + 3f * s, 4f * s, 8f * s), col);
        else if (kind == 6) UiKit.Fill(new Rect(body.xMax - 4f * s, body.y + 3f * s, 4f * s, 8f * s), col);
        else
        {
            // 滚轮：上下箭头
            float dy = kind == 4 ? -1f : 1f;
            VirtualControls.DrawLine(c + new Vector2(-3f, dy * 2f) * s, c + new Vector2(0f, dy * 6f) * s, col, 2f * s);
            VirtualControls.DrawLine(c + new Vector2(0f, dy * 6f) * s, c + new Vector2(3f, dy * 2f) * s, col, 2f * s);
        }
    }

    // —————————— 手柄 ——————————
    static void PadPage(bool act)
    {
        var items = new (KeyCode k, string l)[]
        {
            (KeyCode.JoystickButton0, "A"), (KeyCode.JoystickButton1, "B"),
            (KeyCode.JoystickButton2, "X"), (KeyCode.JoystickButton3, "Y"),
            (KeyCode.JoystickButton4, "LB"), (KeyCode.JoystickButton5, "RB"),
            (KeyCode.JoystickButton6, "LT"), (KeyCode.JoystickButton7, "RT"),
            (KeyCode.JoystickButton8, "Back"), (KeyCode.JoystickButton9, "Start"),
            (KeyCode.JoystickButton10, "LS"), (KeyCode.JoystickButton11, "RS"),
        };

        float margin = 54f;
        const int cols = 4;
        float gap = 16f;
        float cw = Mathf.Min(300f, (UiKit.W - margin * 2f - gap * (cols - 1)) / cols);
        float ch = 112f;
        float top = 150f;
        float totalW = cw * cols + gap * (cols - 1);
        float x0 = (UiKit.W - totalW) * 0.5f;

        for (int i = 0; i < items.Length; i++)
        {
            int c = i % cols, row = i / cols;
            var cell = new Rect(x0 + c * (cw + gap), top + row * (ch + gap), cw, ch);
            bool cur = selected != null && CurKey() == items[i].k;
            Rounded(cell, cur ? KeyBgOn : KeyBg);
            if (cur) Outline(cell, SelectRing, 2f);

            var cc = new Vector2(cell.center.x, cell.y + 36f);
            var col = KeyTxt;
            if (items[i].l.Length == 1)
            {
                Disc(cc, 20f, col);
                Text(new Rect(cc.x - 20f, cc.y - 17f, 40f, 34f), items[i].l, 21,
                     new Color(0.10f, 0.10f, 0.11f, 1f), TextAnchor.MiddleCenter);
            }
            else
            {
                Rounded(new Rect(cc.x - 32f, cc.y - 15f, 64f, 30f), col);
                Text(new Rect(cc.x - 32f, cc.y - 15f, 64f, 30f), items[i].l, 18,
                     new Color(0.10f, 0.10f, 0.11f, 1f), TextAnchor.MiddleCenter);
            }
            Text(new Rect(cell.x, cell.yMax - 40f, cell.width, 28f), items[i].l, 18, TxtSub, TextAnchor.MiddleCenter);

            if (Tap(cell, act)) ApplyKey(items[i].k);
        }
    }

    // —————————— 落键 ——————————
    static void ApplyKey(KeyCode k)
    {
        if (selected == null) { page = P_Edit; return; }

        // 摇杆方向映射：只动那一个方向，不碰元素自己的动作/主键
        if (keyTarget > 0 && selected.type != VCElemType.Button)
        {
            PushHistory();
            VirtualControls.ReleaseStickKeys(selected);   // 换键前先把旧键抬起来
            selected.SetDirKey(keyTarget - 1, k);
            dirty = true;
            VirtualControls.SaveActive();
            keyTarget = 0;
            OpenPage(P_Edit);
            return;
        }

        PushHistory();
        selected.key = k;
        selected.shiftMod = keyShift;
        var a = VirtualControls.ActionOfKey(k);
        if (a.HasValue) selected.action = a.Value;
        dirty = true;
        VirtualControls.SaveActive();
        OpenPage(P_Edit);
    }

    // —————————— 键位表 ——————————
    static readonly (KeyCode k, string l, string s, float w)[] RowFuncs =
    {
        (KeyCode.Escape, "Esc", null, 1.5f),
        (KeyCode.F1, "F1", null, 1.125f), (KeyCode.F2, "F2", null, 1.125f), (KeyCode.F3, "F3", null, 1.125f), (KeyCode.F4, "F4", null, 1.125f),
        (KeyCode.F5, "F5", null, 1.125f), (KeyCode.F6, "F6", null, 1.125f), (KeyCode.F7, "F7", null, 1.125f), (KeyCode.F8, "F8", null, 1.125f),
        (KeyCode.F9, "F9", null, 1.125f), (KeyCode.F10, "F10", null, 1.125f), (KeyCode.F11, "F11", null, 1.125f), (KeyCode.F12, "F12", null, 1.125f),
    };

    static readonly (KeyCode k, string l, string s, float w)[] RowDigits =
    {
        (KeyCode.BackQuote, "`", "~", 1f),
        (KeyCode.Alpha1, "1", "!", 1f), (KeyCode.Alpha2, "2", "@", 1f), (KeyCode.Alpha3, "3", "#", 1f),
        (KeyCode.Alpha4, "4", "$", 1f), (KeyCode.Alpha5, "5", "%", 1f), (KeyCode.Alpha6, "6", "^", 1f),
        (KeyCode.Alpha7, "7", "&", 1f), (KeyCode.Alpha8, "8", "*", 1f), (KeyCode.Alpha9, "9", "(", 1f),
        (KeyCode.Alpha0, "0", ")", 1f),
        (KeyCode.Minus, "-", "_", 1f), (KeyCode.Equals, "=", "+", 1f),
        (KeyCode.Backspace, "←", null, 2f),
    };

    static readonly (KeyCode k, string l, string s, float w)[] RowQwerty =
    {
        (KeyCode.Tab, "Tab", null, 1.5f),
        (KeyCode.Q, "Q", null, 1f), (KeyCode.W, "W", null, 1f), (KeyCode.E, "E", null, 1f), (KeyCode.R, "R", null, 1f),
        (KeyCode.T, "T", null, 1f), (KeyCode.Y, "Y", null, 1f), (KeyCode.U, "U", null, 1f), (KeyCode.I, "I", null, 1f),
        (KeyCode.O, "O", null, 1f), (KeyCode.P, "P", null, 1f),
        (KeyCode.LeftBracket, "[", "{", 1f), (KeyCode.RightBracket, "]", "}", 1f),
        (KeyCode.Backslash, "\\", "|", 1.5f),
    };

    static readonly (KeyCode k, string l, string s, float w)[] RowHome =
    {
        (KeyCode.CapsLock, "Caps", null, 1.75f),
        (KeyCode.A, "A", null, 1f), (KeyCode.S, "S", null, 1f), (KeyCode.D, "D", null, 1f), (KeyCode.F, "F", null, 1f),
        (KeyCode.G, "G", null, 1f), (KeyCode.H, "H", null, 1f), (KeyCode.J, "J", null, 1f), (KeyCode.K, "K", null, 1f),
        (KeyCode.L, "L", null, 1f),
        (KeyCode.Semicolon, ";", ":", 1f), (KeyCode.Quote, "'", "\"", 1f),
        (KeyCode.Return, "Enter", null, 2.25f),
    };

    static readonly (KeyCode k, string l, string s, float w)[] RowBottom =
    {
        (KeyCode.LeftShift, "L Shift", null, 2.25f),
        (KeyCode.Z, "Z", null, 1f), (KeyCode.X, "X", null, 1f), (KeyCode.C, "C", null, 1f), (KeyCode.V, "V", null, 1f),
        (KeyCode.B, "B", null, 1f), (KeyCode.N, "N", null, 1f), (KeyCode.M, "M", null, 1f),
        (KeyCode.Comma, ",", "<", 1f), (KeyCode.Period, ".", ">", 1f), (KeyCode.Slash, "/", "?", 1f),
        (KeyCode.RightShift, "R Shift", null, 2.75f),
    };

    static readonly (KeyCode k, string l, string s, float w)[] RowSpace =
    {
        (KeyCode.LeftControl, "L Ctrl", null, 1.25f),
        (KeyCode.LeftWindows, "L Win", null, 1.25f),
        (KeyCode.LeftAlt, "L Alt", null, 1.25f),
        (KeyCode.Space, "Space", null, 6.25f),
        (KeyCode.RightAlt, "R Alt", null, 1.25f),
        (KeyCode.RightWindows, "R Win", null, 1.25f),
        (KeyCode.Menu, "Menu", null, 1.25f),
        (KeyCode.RightControl, "R Ctrl", null, 1.25f),
    };
}
