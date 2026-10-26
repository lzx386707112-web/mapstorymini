using System;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 游戏内快捷入口：四边吸附悬浮球 + 侧面菜单面板。
//
//   悬浮球
//     · 可拖动；松手吸附最近的一条边，半隐藏在边上
//     · 吸附 1 秒后渐隐为半透明
//     · 第一下点击：从边上拉出来，完整显示并恢复不透明
//     · 第二下点击：打开侧面面板
//     · 一段时间不操作：自动回到吸附 + 半隐藏 + 半透明
//
//   面板
//     · 从球所在的一侧滑入（上下边的球按左右位置选边）
//     · 关闭：点空白、右上角 ×、向外滑动、Esc
//
//   输入：球/面板都用原始触摸与 Pointer 并行处理；按住球期间冻结游戏输入，
//         避免拖球时角色继续跑动。
// ————————————————————————————————————————————————————————————
public static class GameSideMenu
{
    public static bool Visible { get; private set; }

    // 面板打开/收起、按住悬浮球时，都挡住下面的游戏输入与 Pointer
    public static bool Blocking => Visible || slide > 0.001f || swallow || ballPressing;

    // —————————— 面板布局 ——————————
    const float PanelMaxW = 360f;
    const float HeaderH = 96f;
    const float SectionH = 34f;
    const float EntryH = 88f;
    const float EntryGap = 12f;
    const float Inset = 18f;

    const float CloseDrag = 64f;
    const float TapSlop = 10f;
    const float OpenTime = 0.16f;
    const float CloseTime = 0.13f;

    // —————————— 悬浮球 ——————————
    const float BallR = 32f;
    const float BallHitR = 44f;
    const float BallFullGapDp = 20f;      // 拉出后离屏幕边至少 20dp
    const float BallIdleAlpha = 0.85f;
    const float BallFadeDelay = 1f;
    const float BallIdleReturn = 5f;
    const float BallTapSlop = 10f;

    enum BallEdge { Left, Right, Top, Bottom }
    enum BallState { Snapped, Full, Dragging }

    static readonly Color PanelBg = new Color(0.045f, 0.050f, 0.064f, 0.975f);
    static readonly Color PanelHead = new Color(0.070f, 0.078f, 0.100f, 0.985f);
    static readonly Color CardBg = new Color(0.095f, 0.105f, 0.135f, 0.98f);
    static readonly Color CardHot = new Color(0.125f, 0.165f, 0.225f, 1f);
    static readonly Color CardOff = new Color(0.070f, 0.075f, 0.090f, 0.98f);
    static readonly Color IconBg = new Color(0.075f, 0.245f, 0.44f, 1f);
    static readonly Color Accent = new Color(0.20f, 0.72f, 1f, 1f);
    static readonly Color TxtMain = new Color(0.93f, 0.95f, 0.98f, 1f);
    static readonly Color TxtDim = new Color(0.62f, 0.66f, 0.74f, 1f);
    static readonly Color BallBody = new Color(1f, 0.32f, 0.045f, 1f);
    static readonly Color BallRing = new Color(1f, 0.94f, 0.78f, 1f);
    static readonly Color BallIcon = Color.white;

    sealed class MenuEntry
    {
        public string glyph;
        public string glyphOff;      // 开关项关闭时的图标
        public string title;
        public string desc;          // 普通项的描述
        public bool gameplayOnly;
        public Action run;

        // —— 开关项：state 不为 null 就是「点一下改状态」的条目 ——
        // 这类条目不执行 run 里的"打开窗口"，而是取反后原地刷新，面板保持打开，
        // 用户能立刻看到右侧滑块与描述文字变了。
        public Func<bool> state;
        public string descOn;
        public string descOff;

        public bool IsSwitch => state != null;
        public string DescNow => IsSwitch ? (state() ? descOn : descOff) : desc;
        public string GlyphNow => IsSwitch && !state() && !string.IsNullOrEmpty(glyphOff) ? glyphOff : glyph;
    }

    static readonly MenuEntry[] items =
    {
        new MenuEntry
        {
            glyph = "显",
            glyphOff = "隐",
            title = "虚拟按键",
            descOn = "显示中 · 点击隐藏",
            descOff = "已隐藏 · 点击显示",
            gameplayOnly = false,
            state = () => VirtualControls.OverlayVisible,
            run = ToggleVirtualControls,
        },
        new MenuEntry
        {
            glyph = "键",
            title = "按键布局",
            desc = "调整位置、大小与样式",
            gameplayOnly = false,
            run = OpenVirtualEditor,
        },
    };

    // —————————— 面板状态 ——————————
    static float slide;               // 0 关 · 1 全开
    static float openedAt;
    static bool fromRight;
    static bool swallow;

    static bool pressing;
    static bool pressMouse;
    static int pressFinger = -1;
    static Vector2 pressStart;
    static Vector2 pressPos;
    static bool pressOutside;
    static bool pressDragged;
    static int hotEntry = -1;

    // —————————— 悬浮球状态 ——————————
    static bool ballPlaced;
    static BallEdge ballEdge = BallEdge.Right;
    static BallState ballState = BallState.Snapped;
    static Vector2 ballPos;
    static Vector2 ballTarget;
    static float ballOpacity = 1f;
    static float ballSnapAt;
    static float ballLastTouch;

    static bool ballPressing;
    static bool ballDragging;
    static bool ballWasFull;
    static bool ballMouse;
    static int ballFinger = -1;
    static Vector2 ballPressPos;
    static Vector2 ballGrab;

    static bool editorWasVisible;
    static GameLoop.GameState lastState;
    static bool stateKnown;

    static float PanelW => Mathf.Min(PanelMaxW, Mathf.Max(220f, UiKit.W * 0.46f));

    static float BallFullGap
    {
        get
        {
            float dp = Screen.dpi > 1f ? Screen.dpi / 160f : 1f;
            return Mathf.Max(6f, BallFullGapDp * dp / Mathf.Max(0.01f, UiKit.Scale));
        }
    }

    // —————————— 每帧（InputBridge.Update，早于 Pointer.Tick） ——————————
    public static void Tick()
    {
        var g = GameLoop.I;
        if (g == null || g.state != GameLoop.GameState.Playing)
        {
            ResetAll();
            return;
        }

        if (!stateKnown || lastState != g.state)
        {
            stateKnown = true;
            lastState = g.state;
            if (Visible) Close(true);
            ResetBallPress();
        }

        if (VirtualControlsEditor.Visible)
        {
            HideForEditor();
            return;
        }
        if (editorWasVisible)
        {
            editorWasVisible = false;
            RevealBall();
        }
        if (g.state == GameLoop.GameState.Playing && KeyConfigWindow.Visible)
        {
            ResetBallPress();
            AndroidBallGesture.Clear();
            Pointer.Suppressed = false;
            return;
        }

        EnsureBall();

        if (!Application.isFocused)
        {
            ResetBallPress();
            Pointer.Suppressed = Blocking;
            return;
        }

        if (Visible)
        {
            // 开面板的那次点击可能还留着一次 Esc/返回，稍等再接受关闭
            if (Input.GetKeyDown(KeyCode.Escape) && Time.unscaledTime - openedAt > 0.4f)
                Close(false);
            else
                PollPanel();
        }
        else if (slide <= 0.001f)
        {
            PollBall();

            if (g.state == GameLoop.GameState.Playing && !Visible
                && !KeyConfigWindow.Visible && Input.GetKeyDown(KeyCode.F8))
                OpenVirtualEditor();

            if (!Visible && !ballPressing && ballState == BallState.Full
                && Time.unscaledTime - ballLastTouch >= BallIdleReturn)
                SnapBall();
        }

        float speed = 1f / (Visible ? OpenTime : CloseTime);
        slide = Mathf.MoveTowards(slide, Visible ? 1f : 0f, Time.unscaledDeltaTime * speed);
        UpdateBall();

        if (swallow && !RawPointerDown()) swallow = false;
        Pointer.Suppressed = Blocking;
    }

    static void ResetAll()
    {
        Visible = false;
        slide = 0f;
        swallow = false;
        fromRight = false;
        openedAt = 0f;
        ResetPress();

        ballPlaced = false;
        ballPressing = false;
        ballDragging = false;
        ballWasFull = false;
        ballMouse = false;
        ballFinger = -1;
        editorWasVisible = false;
        stateKnown = false;

        AndroidBallGesture.Clear();
        Pointer.Suppressed = false;
    }

    static void HideForEditor()
    {
        editorWasVisible = true;
        if (Visible) { Visible = false; slide = 0f; ResetPress(); }
        ResetBallPress();
        swallow = false;
        AndroidBallGesture.Clear();
        Pointer.Suppressed = false;
    }

    static void OpenPanel()
    {
        bool right = ballEdge == BallEdge.Right
                  || (ballEdge != BallEdge.Left && ballPos.x >= UiKit.W * 0.5f);
        Open(right);
    }

    static void Open(bool right)
    {
        Visible = true;
        fromRight = right;
        openedAt = Time.unscaledTime;
        slide = Mathf.Max(slide, 0.001f);
        swallow = true;
        ResetPress();
        AndroidBallGesture.Clear();
    }

    static void Close(bool immediate)
    {
        Visible = false;
        ResetPress();
        ResetBallPress();
        if (immediate) slide = 0f;
        swallow = RawPointerDown();

        // 面板关闭后球先完整显示，5 秒无操作再自动收回边上
        if (ballPlaced)
        {
            ballState = BallState.Full;
            ballTarget = FullPos(ballEdge, ballPos);
            ballLastTouch = Time.unscaledTime;
        }
        AndroidBallGesture.Clear();
    }

    static void OpenVirtualEditor()
    {
        if (GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Playing) return;
        Close(true);
        if (KeyConfigWindow.Visible) KeyConfigWindow.Toggle();
        if (!VirtualControlsEditor.Visible) VirtualControlsEditor.Toggle();
    }

    // 显示/隐藏整层虚拟按键：只切 OverlayVisible，游戏逻辑、键位映射、布局存档都不动。
    // 悬浮球属于本层，隐藏后仍在，随时能从同一个入口调回来。
    static void ToggleVirtualControls()
    {
        VirtualControls.OverlayVisible = !VirtualControls.OverlayVisible;
        BottomBar.Toast(VirtualControls.OverlayVisible ? "虚拟按键已显示" : "虚拟按键已隐藏");
    }

    static bool RawPointerDown()
        => Input.touchCount > 0 || Input.GetMouseButton(0);

    // —————————— 悬浮球：初始化 / 动画 / 吸附 ——————————
    static void EnsureBall()
    {
        if (ballPlaced) return;
        ballPlaced = true;
        ballEdge = BallEdge.Right;
        ballPos = ballTarget = SnappedPos(ballEdge, new Vector2(UiKit.W, UiKit.H * 0.18f));
        ballState = BallState.Snapped;
        ballOpacity = 1f;
        ballSnapAt = ballLastTouch = Time.unscaledTime;
    }

    static void UpdateBall()
    {
        if (!ballPlaced) return;

        if (ballState != BallState.Dragging)
        {
            float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            ballPos = Vector2.Lerp(ballPos, ballTarget, k);
            if ((ballPos - ballTarget).sqrMagnitude < 0.04f) ballPos = ballTarget;
        }

        float targetAlpha = 1f;
        if (ballState == BallState.Snapped && Time.unscaledTime - ballSnapAt >= BallFadeDelay)
            targetAlpha = BallIdleAlpha;
        ballOpacity = Mathf.MoveTowards(ballOpacity, targetAlpha, Time.unscaledDeltaTime * 3f);

        if (!Visible && slide <= 0.001f && !ballPressing && !ballDragging
            && (ballPos - ballTarget).sqrMagnitude < 1f)
            AndroidBallGesture.Update(ballPos, BallHitR);
    }

    static void RevealBall()
    {
        EnsureBall();
        ballState = BallState.Full;
        ballTarget = FullPos(ballEdge, ballPos);
        ballLastTouch = Time.unscaledTime;
    }

    static void SnapBall()
    {
        EnsureBall();
        ballEdge = NearestEdge(ballPos);
        ballTarget = SnappedPos(ballEdge, ballPos);
        ballState = BallState.Snapped;
        ballSnapAt = Time.unscaledTime;
        ballLastTouch = Time.unscaledTime;
    }

    static BallEdge NearestEdge(Vector2 p)
    {
        float l = p.x, r = UiKit.W - p.x, t = p.y, b = UiKit.H - p.y;
        float m = Mathf.Min(Mathf.Min(l, r), Mathf.Min(t, b));
        if (Mathf.Approximately(m, l)) return BallEdge.Left;
        if (Mathf.Approximately(m, r)) return BallEdge.Right;
        if (Mathf.Approximately(m, t)) return BallEdge.Top;
        return BallEdge.Bottom;
    }

    static Vector2 SnappedPos(BallEdge edge, Vector2 p)
    {
        float x = Mathf.Clamp(p.x, BallR, UiKit.W - BallR);
        float y = Mathf.Clamp(p.y, BallR, UiKit.H - BallR);
        switch (edge)
        {
            case BallEdge.Left: x = 0f; break;
            case BallEdge.Right: x = UiKit.W; break;
            case BallEdge.Top: y = 0f; break;
            case BallEdge.Bottom: y = UiKit.H; break;
        }
        return new Vector2(x, y);
    }

    static Vector2 FullPos(BallEdge edge, Vector2 p)
    {
        float gap = BallFullGap;
        float x = Mathf.Clamp(p.x, BallR + 8f, UiKit.W - BallR - 8f);
        float y = Mathf.Clamp(p.y, BallR + 8f, UiKit.H - BallR - 8f);
        switch (edge)
        {
            case BallEdge.Left: x = BallR + gap; break;
            case BallEdge.Right: x = UiKit.W - BallR - gap; break;
            case BallEdge.Top: y = BallR + gap; break;
            case BallEdge.Bottom: y = UiKit.H - BallR - gap; break;
        }
        return new Vector2(x, y);
    }

    // —————————— 悬浮球输入 ——————————
    static void PollBall()
    {
        if (Input.touchCount > 0)
        {
            if (ballPressing && ballMouse) ResetBallPress();

            if (ballPressing)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.fingerId != ballFinger) continue;
                    if (t.phase == TouchPhase.Canceled) { CancelBallPress(); return; }
                    var gui = ToGui(t.position);
                    if (t.phase == TouchPhase.Ended) { UpdateBallPress(gui); FinishBallPress(); return; }
                    UpdateBallPress(gui);
                    return;
                }
                CancelBallPress();
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Began) continue;
                var gui = ToGui(t.position);
                if (!BallContains(gui)) continue;
                BeginBallPress(gui, t.fingerId, false);
                return;
            }
            return;
        }

        if (ballPressing && ballMouse)
        {
            var gui = MouseGui();
            UpdateBallPress(gui);
            if (!Input.GetMouseButton(0)) FinishBallPress();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            var gui = MouseGui();
            if (BallContains(gui)) BeginBallPress(gui, -1, true);
        }
    }

    static bool BallContains(Vector2 p)
        => (p - ballPos).sqrMagnitude <= BallHitR * BallHitR;

    static void BeginBallPress(Vector2 gui, int finger, bool mouse)
    {
        ballPressing = true;
        ballMouse = mouse;
        ballFinger = finger;
        ballPressPos = gui;
        ballDragging = false;
        ballWasFull = ballState == BallState.Full;
        ballGrab = gui - ballPos;
        ballLastTouch = Time.unscaledTime;

        if (!ballWasFull)
        {
            ballState = BallState.Full;
            ballTarget = FullPos(ballEdge, ballPos);
        }
    }

    static void UpdateBallPress(Vector2 gui)
    {
        var d = gui - ballPressPos;
        if (!ballDragging && d.sqrMagnitude > BallTapSlop * BallTapSlop)
        {
            ballDragging = true;
            ballState = BallState.Dragging;
            ballGrab = gui - ballPos;
        }

        if (!ballDragging) return;

        ballPos = new Vector2(
            Mathf.Clamp(gui.x - ballGrab.x, BallR, UiKit.W - BallR),
            Mathf.Clamp(gui.y - ballGrab.y, BallR, UiKit.H - BallR));
        ballTarget = ballPos;
        ballLastTouch = Time.unscaledTime;
    }

    static void FinishBallPress()
    {
        if (ballDragging)
        {
            SnapBall();
        }
        else if (ballWasFull)
        {
            OpenPanel();
        }
        else
        {
            ballState = BallState.Full;
            ballTarget = FullPos(ballEdge, ballPos);
            ballLastTouch = Time.unscaledTime;
        }

        ResetBallPress();
        swallow = RawPointerDown();
    }

    static void CancelBallPress()
    {
        if (ballDragging) SnapBall();
        ResetBallPress();
        swallow = RawPointerDown();
    }

    static void ResetBallPress()
    {
        ballPressing = false;
        ballMouse = false;
        ballFinger = -1;
        ballDragging = false;
        ballWasFull = false;
    }

    // —————————— 面板输入 ——————————
    static void PollPanel()
    {
        hotEntry = -1;

        if (Input.touchCount > 0)
        {
            if (pressing && pressMouse) ResetPress();

            if (pressing)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.fingerId != pressFinger) continue;
                    if (t.phase == TouchPhase.Canceled) { ResetPress(); return; }
                    pressPos = ToGui(t.position);
                    if (t.phase == TouchPhase.Ended) FinishPress();
                    else UpdateDrag();
                    return;
                }
                ResetPress();
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Began) continue;
                BeginPress(ToGui(t.position), t.fingerId, false);
                return;
            }
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            BeginPress(MouseGui(), -1, true);
            return;
        }
        if (!pressing || !pressMouse) return;

        if (Input.GetMouseButton(0))
        {
            pressPos = MouseGui();
            UpdateDrag();
        }
        else
        {
            FinishPress();
        }
    }

    static void BeginPress(Vector2 gui, int finger, bool mouse)
    {
        pressing = true;
        pressMouse = mouse;
        pressFinger = finger;
        pressStart = pressPos = gui;
        pressOutside = !PanelRect().Contains(gui);
        pressDragged = false;
        hotEntry = pressOutside ? -1 : EntryAt(gui);
    }

    static void UpdateDrag()
    {
        var d = pressPos - pressStart;
        if (!pressDragged && d.sqrMagnitude > TapSlop * TapSlop) pressDragged = true;

        float outward = fromRight ? d.x : -d.x;
        if (!pressOutside && outward >= CloseDrag && Mathf.Abs(d.x) >= Mathf.Abs(d.y) * 1.15f)
        {
            Close(false);
            return;
        }
        hotEntry = !pressOutside && !pressDragged ? EntryAt(pressPos) : -1;
    }

    static void FinishPress()
    {
        if (pressOutside)
        {
            if (!pressDragged) Close(false);
            else ResetPress();
            return;
        }

        if (!pressDragged)
        {
            if (CloseRect().Contains(pressStart) && CloseRect().Contains(pressPos))
            {
                Close(false);
                return;
            }
            int hit = EntryAt(pressStart);
            if (hit >= 0 && hit == EntryAt(pressPos) && EntryEnabled(hit))
            {
                // 开关项：原地切换，面板留着 —— 关掉面板就看不到状态怎么变的
                if (items[hit].IsSwitch)
                {
                    var toggle = items[hit].run;
                    ResetPress();
                    toggle?.Invoke();
                    return;
                }

                var run = items[hit].run;
                Close(true);
                run?.Invoke();
                return;
            }
        }
        ResetPress();
    }

    static void ResetPress()
    {
        pressing = false;
        pressMouse = false;
        pressFinger = -1;
        pressDragged = false;
        pressOutside = false;
        hotEntry = -1;
    }

    static Vector2 ToGui(Vector2 screen)
        => new Vector2(screen.x / UiKit.Scale, (Screen.height - screen.y) / UiKit.Scale);

    static Vector2 MouseGui()
        => new Vector2(Input.mousePosition.x / UiKit.Scale,
                       (Screen.height - Input.mousePosition.y) / UiKit.Scale);

    // —————————— 面板布局 ——————————
    static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    static float PanelX()
    {
        float k = Ease(slide);
        return fromRight ? UiKit.W - PanelW * k : -(1f - k) * PanelW;
    }

    static Rect PanelRect() => new Rect(PanelX(), 0f, PanelW, UiKit.H);

    static Rect CloseRect()
        => new Rect(PanelX() + PanelW - 58f, 15f, 42f, 42f);

    static Rect EntryRect(int index)
    {
        float y = HeaderH + SectionH + index * (EntryH + EntryGap);
        return new Rect(PanelX() + Inset, y, PanelW - Inset * 2f, EntryH);
    }

    static int EntryAt(Vector2 p)
    {
        for (int i = 0; i < items.Length; i++)
            if (EntryRect(i).Contains(p)) return i;
        return -1;
    }

    static bool EntryEnabled(int index)
        => !items[index].gameplayOnly
        || (GameLoop.I != null && GameLoop.I.state == GameLoop.GameState.Playing);

    static Color A(Color c, float a)
        => new Color(c.r, c.g, c.b, c.a * a);

    static void Disc(Vector2 center, float r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(center.x - r, center.y - r, r * 2f, r * 2f),
            VirtualControls.DiscTex());
        GUI.color = old;
    }

    static void Round(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, VirtualControls.SoftTex());
        GUI.color = old;
    }

    // 胶囊：两端圆 + 中间矩形。SoftTex 的圆角是按边长比例算的，压成细长条后会变形，
    // 开关底座必须是标准的半圆头，所以这里直接拼几何。
    static void Pill(Rect r, Color c)
    {
        float rad = r.height * 0.5f;
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x + rad, r.y, Mathf.Max(0f, r.width - rad * 2f), r.height),
            Texture2D.whiteTexture);
        GUI.color = old;
        Disc(new Vector2(r.x + rad, r.center.y), rad, c);
        Disc(new Vector2(r.xMax - rad, r.center.y), rad, c);
    }

    // —————————— 绘制（GameHUD.OnGUI，UiKit.Begin 之后） ——————————
    public static void Draw()
    {
        var g = GameLoop.I;
        if (g == null) return;
        if (g.state != GameLoop.GameState.Playing) return;
        if (VirtualControlsEditor.Visible
            || (g.state == GameLoop.GameState.Playing && KeyConfigWindow.Visible)) return;
        if (!ballPlaced) return;

        if (slide > 0.0001f)
        {
            DrawPanel();
            return;
        }
        DrawBall();
    }

    static void DrawBall()
    {
        float a = ballOpacity;
        if (a <= 0.01f) return;

        var c = ballPos;
        Disc(c + new Vector2(2f, 3f), BallR + 2f, new Color(0f, 0f, 0f, 0.28f * a));
        Disc(c, BallR + 6f, new Color(1f, 0.40f, 0.06f, 0.18f * a));
        Disc(c, BallR, A(BallBody, a));
        VirtualControls.DrawAnnulus(c, BallR - 1.4f, BallR + 2.4f, A(BallRing, a), false);

        float l = BallR * 0.38f;
        for (int i = -1; i <= 1; i++)
            VirtualControls.DrawLine(new Vector2(c.x - l, c.y + i * 9f),
                new Vector2(c.x + l, c.y + i * 9f), A(BallIcon, a), 3.6f);
    }

    static void DrawPanel()
    {
        float k = Ease(slide);
        var r = PanelRect();

        UiKit.Fill(new Rect(0f, 0f, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.46f * k));
        for (int i = 1; i <= 6; i++)
        {
            float a = 0.10f * (1f - (i - 1) / 6f) * k;
            float sx = fromRight ? r.xMin - i * 4f : r.xMax + (i - 1) * 4f;
            UiKit.Fill(new Rect(sx, 0f, 4f, UiKit.H), new Color(0f, 0f, 0f, a));
        }

        UiKit.Fill(r, PanelBg);
        UiKit.Fill(new Rect(r.x, 0f, r.width, HeaderH), PanelHead);
        UiKit.Fill(new Rect(fromRight ? r.x : r.xMax - 2f, 0f, 2f, UiKit.H),
            new Color(0.32f, 0.66f, 1f, 0.45f * k));
        UiKit.Fill(new Rect(r.x, HeaderH - 1f, r.width, 1f), new Color(1f, 1f, 1f, 0.07f));

        VirtualControls.Label(new Rect(r.x + 22f, 22f, r.width - 90f, 34f),
            "快捷菜单", 26, TxtMain, TextAnchor.MiddleLeft, 2);
        VirtualControls.Label(new Rect(r.x + 22f, 58f, r.width - 90f, 22f),
            "随时调整游戏操作", 12, TxtDim, TextAnchor.MiddleLeft, 0);

        VirtualControls.Label(new Rect(r.x + Inset + 2f, HeaderH + 6f, 120f, 22f),
            "功能", 12, new Color(1f, 1f, 1f, 0.38f), TextAnchor.MiddleLeft, 0);
        UiKit.Fill(new Rect(r.x + Inset + 46f, HeaderH + 17f, PanelW - Inset * 2f - 46f, 1f),
            new Color(1f, 1f, 1f, 0.05f));

        for (int i = 0; i < items.Length; i++)
        {
            bool enabled = EntryEnabled(i);
            bool isSwitch = items[i].IsSwitch;
            bool on = isSwitch && items[i].state();
            var card = EntryRect(i);
            bool down = enabled && pressing && !pressOutside && !pressDragged && hotEntry == i;
            bool hover = enabled && !Application.isMobilePlatform && !pressing && card.Contains(MouseGui());
            Round(card, !enabled ? CardOff : down || hover ? CardHot : CardBg);
            if (down || hover)
                UiKit.Fill(new Rect(card.x, card.y + 12f, 3f, card.height - 24f), Accent);

            var icon = new Rect(card.x + 14f, card.y + (card.height - 52f) * 0.5f, 52f, 52f);
            Round(icon, !enabled ? new Color(0.10f, 0.12f, 0.15f, 0.9f)
                                 : isSwitch && !on ? new Color(0.115f, 0.135f, 0.17f, 0.95f) : IconBg);
            VirtualControls.Label(icon, items[i].GlyphNow, 24,
                enabled ? (isSwitch && !on ? new Color(0.66f, 0.72f, 0.80f) : new Color(0.88f, 0.96f, 1f))
                        : new Color(0.58f, 0.62f, 0.68f),
                TextAnchor.MiddleCenter, 1);

            float tx = icon.xMax + 14f;
            float tw = card.xMax - tx - (isSwitch ? 70f : 42f);
            VirtualControls.Label(new Rect(tx, card.y + 18f, tw, 26f),
                items[i].title, 19, enabled ? TxtMain : TxtDim, TextAnchor.MiddleLeft, 1);
            VirtualControls.Label(new Rect(tx, card.y + 46f, tw, 20f),
                enabled ? items[i].DescNow : "进入游戏后可编辑", 12,
                enabled ? TxtDim : new Color(0.48f, 0.51f, 0.57f),
                TextAnchor.MiddleLeft, 0);

            if (isSwitch)
            {
                // 开关项右侧画滑块，代替普通项的箭头：一眼看出现在是开还是关
                var sw = new Rect(card.xMax - 60f, card.center.y - 13f, 46f, 26f);
                Pill(sw, !enabled ? new Color(0.14f, 0.15f, 0.18f, 1f)
                        : on ? new Color(0.20f, 0.60f, 1f, 1f)
                             : new Color(0.17f, 0.19f, 0.24f, 1f));
                float kr = sw.height * 0.5f - 3f;
                float kx = on ? sw.xMax - sw.height * 0.5f : sw.x + sw.height * 0.5f;
                Disc(new Vector2(kx, sw.center.y + 1.4f), kr + 1f, new Color(0f, 0f, 0f, 0.22f));
                Disc(new Vector2(kx, sw.center.y), kr, enabled ? Color.white : new Color(0.44f, 0.47f, 0.52f));
            }
            else
            {
                float cx = card.xMax - 24f, cy = card.center.y;
                var chevron = new Color(1f, 1f, 1f, enabled ? 0.36f : 0.12f);
                VirtualControls.DrawLine(new Vector2(cx - 4f, cy - 7f), new Vector2(cx + 3f, cy), chevron, 2f);
                VirtualControls.DrawLine(new Vector2(cx + 3f, cy), new Vector2(cx - 4f, cy + 7f), chevron, 2f);
            }
        }

        var close = CloseRect();
        bool closeDown = pressing && !pressOutside && CloseRect().Contains(pressPos);
        bool closeHover = !Application.isMobilePlatform && !pressing && close.Contains(MouseGui());
        if (closeDown || closeHover)
            Round(new Rect(close.x + 2f, close.y + 2f, close.width - 4f, close.height - 4f),
                new Color(1f, 1f, 1f, 0.10f));
        var cc = close.center;
        var xc = closeDown || closeHover ? Color.white : new Color(1f, 1f, 1f, 0.55f);
        VirtualControls.DrawLine(new Vector2(cc.x - 7f, cc.y - 7f), new Vector2(cc.x + 7f, cc.y + 7f), xc, 2.2f);
        VirtualControls.DrawLine(new Vector2(cc.x + 7f, cc.y - 7f), new Vector2(cc.x - 7f, cc.y + 7f), xc, 2.2f);

        float fy = UiKit.H - 62f;
        UiKit.Fill(new Rect(r.x + Inset, fy - 12f, PanelW - Inset * 2f, 1f), new Color(1f, 1f, 1f, 0.06f));
        VirtualControls.Label(new Rect(r.x + Inset, fy, PanelW - Inset * 2f, 22f),
            fromRight ? "点击空白处或向右滑动关闭" : "点击空白处或向左滑动关闭",
            12, new Color(1f, 1f, 1f, 0.30f), TextAnchor.MiddleCenter, 0);
    }
}
