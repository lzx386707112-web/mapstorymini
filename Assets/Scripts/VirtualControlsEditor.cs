using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 虚拟按键编辑器（深色版）—— 外壳
//
//   工具条：← 返回 | 图层 1 ▾ | 默认布局 |（＋ 或 ↶ ↷ 🗑 ⧉ ✏️）
//     · 没有选中控件时显示 ＋（添加控件）
//     · 选中控件后 ＋ 收起，换成 垃圾桶 / 复制 / 铅笔
//   画面：纯黑；控件按「真实样式」渲染（背景色/描边/光环/形状/旋转），
//         选中项加一圈蓝色选框，拖动时才显示贯穿全屏的虚线坐标
//   面板：右侧编辑面板见 VirtualControlsEditor.Panel.cs
//         选择按键弹窗见 VirtualControlsEditor.Keys.cs
//
//   ※ 尺寸：整块界面按 1920×869 的参考图折算过一轮，比初版放大约 1.4 倍
//     （UiKit 虚拟高仍是 720，所以这里的数值就是虚拟像素）
// ————————————————————————————————————————————————————————————
public static partial class VirtualControlsEditor
{
    public static bool Visible { get; private set; }

    // 弹层：互斥，同一时刻只开一个
    const int P_None = 0, P_Add = 1, P_Edit = 2, P_Style = 3, P_Shape = 4, P_Color = 5, P_Keys = 6;

    static int page = P_None;
    static int pageOpenedFrame = -1;
    static float pageT = 1f;               // 面板滑入动画 0→1

    static VCElem selected;
    static bool dragging, dragMoved;
    static Vector2 dragOff;
    static string dragSnapshot = "";

    // 拖摇杆四角改缩放
    static bool resizing, resizeMoved;
    static float resizeStartDist, resizeStartScale;
    static string resizeSnapshot = "";

    // —— 输入 ——
    static bool aDown, aPressed, aReleased;
    static Vector2 aPos, aPressPos;
    static float pressDist;                // 本次按下累计位移（区分点击/拖动）
    static Vector2 lastPos;

    // —— 颜色编辑目标：0 默认背景 1 默认描边 2 按下背景 3 按下描边 ——
    static int colorTarget;

    static readonly List<string> undoStack = new List<string>();
    static readonly List<string> redoStack = new List<string>();
    const int HistoryMax = 60;

    // —————————— 配色 ——————————
    static readonly Color BarBg = new Color(0.052f, 0.052f, 0.058f, 0.96f);
    static readonly Color BarEdge = new Color(1f, 1f, 1f, 0.16f);
    static readonly Color Ink = new Color(0.94f, 0.95f, 0.96f, 1f);
    static readonly Color InkDim = new Color(1f, 1f, 1f, 0.26f);
    static readonly Color DividerCol = new Color(1f, 1f, 1f, 0.13f);
    static readonly Color Accent = new Color(0.04f, 0.52f, 1f, 1f);       // #0A84FF
    static readonly Color SelectRing = new Color(0.16f, 0.72f, 0.95f, 0.95f);

    public static void Toggle()
    {
        if (Visible) Close(); else Open();
    }

    static void Open()
    {
        VirtualControls.Ensure();
        Visible = true;
        selected = null;
        dragging = false;
        page = P_None;
        panelX = -1f;              // 每次打开都回到贴右的默认位置
        panelMoving = false;
        panelScroll = 0f;
        undoStack.Clear();
        redoStack.Clear();
        InputHub.ReleaseAllVirtual();
        TouchInput.Reset();
    }

    static void Close()
    {
        Visible = false;
        page = P_None;
        VirtualControls.SaveActive();
        InputHub.ReleaseAllVirtual();
        TouchInput.Reset();
    }

    // ——————————— 输入采集 ———————————
    static bool AbsTouch => Application.isMobilePlatform;

    static Vector2 ToGui(Vector2 screen)
        => new Vector2(screen.x / UiKit.Scale, (Screen.height - screen.y) / UiKit.Scale);

    static void Poll()
    {
        aPressed = aReleased = false;
        if (AbsTouch && Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            aPos = ToGui(t.position);
            aDown = t.phase == TouchPhase.Began || t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
            aPressed = t.phase == TouchPhase.Began;
            aReleased = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
        }
        else
        {
            aPos = Pointer.Pos;
            aDown = Pointer.Down;
            aPressed = Pointer.Pressed;
            aReleased = Pointer.Released;
        }
        if (aPressed) { aPressPos = aPos; pressDist = 0f; }
        else if (aDown) pressDist += (aPos - lastPos).magnitude;
        lastPos = aPos;
    }

    static void OpenPage(int p)
    {
        // 只要不是进「选择按键」，就清掉"这次落哪个方向"的标记，免得下次误落摇杆方向
        if (p != P_Keys) keyTarget = 0;
        if (page != p) panelScroll = 0f;
        page = p;
        pageOpenedFrame = Time.frameCount;
        pageT = 0f;
    }

    // —————————— 主绘制 ——————————
    public static void Draw()
    {
        if (!Visible) return;
        VirtualControls.Ensure();
        Poll();
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F8))
        {
            if (page == P_Edit || page == P_Add) { page = P_None; return; }
            if (page != P_None) { OpenPage(P_Edit); return; }
            Close();
            return;
        }

        bool act = Event.current.type == EventType.Repaint;
        pageT = Mathf.Min(1f, pageT + Time.unscaledDeltaTime * 7f);

        UiKit.Fill(new Rect(0f, 0f, UiKit.W, UiKit.H), Color.black);

        var list = VirtualControls.elems;
        for (int i = 0; i < list.Count; i++) VirtualControls.DrawElement(list[i]);

        if (selected != null) DrawSelection(selected);
        if (dragging && selected != null) Crosshair(CenterOf(selected));

        bool side = page == P_Edit || page == P_Style || page == P_Shape || page == P_Color;
        if (act && (page == P_None || side)) HandleScene(side);

        if (page != P_Keys && BarFits()) Toolbar(act);

        if (page == P_Add) AddPanel(act);
        else if (page != P_None && page != P_Keys) SidePanel(act);

        if (page == P_Keys) KeySheet(act);
    }

    static Vector2 CenterOf(VCElem e) => VirtualControls.Center(e);

    // 选中：蓝色虚线框（摇杆/十字键带四角圆点）；拖动时才叠加坐标虚线
    static void DrawSelection(VCElem e)
    {
        var c = CenterOf(e);
        var col = SelectRing;

        if (e.type != VCElemType.Button)
        {
            // 摇杆没有"形状"可言，用方框圈住整体，四角给抓手点
            float hw = e.HalfW + 18f, hh = e.R + 18f;
            var box = new Rect(c.x - hw, c.y - hh, hw * 2f, hh * 2f);
            DashRect(box, col);
            DrawHandle(new Vector2(box.x, box.y));
            DrawHandle(new Vector2(box.xMax, box.y));
            DrawHandle(new Vector2(box.x, box.yMax));
            DrawHandle(new Vector2(box.xMax, box.yMax));
            return;
        }

        if (e.shape == 0)
        {
            Ring(c, e.R + 9f, col, 2.6f);
        }
        else
        {
            var r = new Rect(c.x - e.HalfW - 9f, c.y - e.R - 9f, e.HalfW * 2f + 18f, e.R * 2f + 18f);
            Outline(r, col, 2.6f);
        }
    }

    static void DrawHandle(Vector2 p)
    {
        Disc(p, 7.5f, Color.white);
        Ring(p, 7.5f, SelectRing, 2.2f);
    }

    // 四角手柄的命中：两个轴都靠近框角，就当作抓到了手柄
    const float HandleGrab = 15f;

    static bool NearCorner(VCElem e, Vector2 p)
    {
        var c = CenterOf(e);
        float hw = e.HalfW + 18f, hh = e.R + 18f;
        return Mathf.Abs(Mathf.Abs(p.x - c.x) - hw) < HandleGrab
            && Mathf.Abs(Mathf.Abs(p.y - c.y) - hh) < HandleGrab;
    }

    static void DashRect(Rect r, Color col)
    {
        const float dash = 10f, gap = 8f, t = 2.4f;
        for (float x = r.x; x < r.xMax; x += dash + gap)
        {
            float w = Mathf.Min(dash, r.xMax - x);
            UiKit.Fill(new Rect(x, r.y - t * 0.5f, w, t), col);
            UiKit.Fill(new Rect(x, r.yMax - t * 0.5f, w, t), col);
        }
        for (float y = r.y; y < r.yMax; y += dash + gap)
        {
            float h = Mathf.Min(dash, r.yMax - y);
            UiKit.Fill(new Rect(r.x - t * 0.5f, y, t, h), col);
            UiKit.Fill(new Rect(r.xMax - t * 0.5f, y, t, h), col);
        }
    }

    static void Crosshair(Vector2 c)
    {
        var col = new Color(1f, 1f, 1f, 0.38f);
        const float dash = 11f, gap = 11f, t = 2f;
        for (float x = 0f; x < UiKit.W; x += dash + gap)
            UiKit.Fill(new Rect(x, c.y - t * 0.5f, Mathf.Min(dash, UiKit.W - x), t), col);
        for (float y = 0f; y < UiKit.H; y += dash + gap)
            UiKit.Fill(new Rect(c.x - t * 0.5f, y, t, Mathf.Min(dash, UiKit.H - y)), col);
    }

    // —————————— 场景交互 ——————————
    // 面板打开时点击空白保留选中；点击其他控件则切换到它的基础编辑页。
    static void HandleScene(bool keepSelectionOnEmpty)
    {
        // 摇杆四角手柄：拖它 = 改整体缩放（比滑到面板里再调更直觉）
        if (resizing)
        {
            if (aDown && selected != null)
            {
                float d = (aPos - CenterOf(selected)).magnitude;
                if (!resizeMoved && Mathf.Abs(d - resizeStartDist) > 2.5f)
                {
                    resizeMoved = true;
                    PushHistory(resizeSnapshot);
                }
                if (resizeMoved)
                {
                    selected.scale = Mathf.Clamp(resizeStartScale * d / Mathf.Max(8f, resizeStartDist), 0.4f, 3f);
                    dirty = true;
                }
                return;
            }
            if (resizeMoved) VirtualControls.SaveActive();
            resizing = false;
        }

        if (!aDown) dragging = false;

        // 按在选中摇杆的四角上 → 进入缩放拖拽（优先于移动）
        if (aPressed && selected != null && selected.type != VCElemType.Button &&
            !OverUi() && NearCorner(selected, aPos))
        {
            resizing = true;
            resizeMoved = false;
            resizeStartScale = selected.scale;
            resizeStartDist = Mathf.Max(8f, (aPos - CenterOf(selected)).magnitude);
            resizeSnapshot = VirtualControls.ToJson();
            dragging = false;
            return;
        }

        if (dragging && selected != null && aDown)
        {
            var p = aPos - dragOff;
            float ncx = Mathf.Clamp(p.x / UiKit.W, 0.02f, 0.98f);
            float ncy = Mathf.Clamp(p.y / UiKit.H, 0.02f, 0.98f);
            if (!dragMoved && (Mathf.Abs(ncx - selected.cx) > 0.001f || Mathf.Abs(ncy - selected.cy) > 0.001f))
            {
                dragMoved = true;
                PushHistory(dragSnapshot);
            }
            if (dragMoved) { selected.cx = ncx; selected.cy = ncy; }
            return;
        }

        if (!aPressed) return;
        if (OverUi()) return;

        VCElem hit = null;
        var list = VirtualControls.elems;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].Hit(aPos)) { hit = list[i]; break; }

        if (keepSelectionOnEmpty)
        {
            if (hit == null) return;
            if (hit != selected) OpenPage(P_Edit);
        }

        selected = hit;
        dragging = hit != null;
        dragMoved = false;
        if (hit != null)
        {
            dragSnapshot = VirtualControls.ToJson();
            dragOff = aPos - CenterOf(hit);
        }
    }

    static bool OverUi()
    {
        if (ToolbarRect().Contains(aPos)) return true;
        if (page == P_Add && AddPanelRect().Contains(aPos)) return true;
        if (page != P_None && page != P_Add && page != P_Keys && PanelRect().Contains(aPos)) return true;
        return false;
    }

    // —————————— 顶部工具条 ——————————
    const float BarH = 84f;
    const float BarY = 16f;
    const float PadX = 26f, GapIn = 12f, GapGroup = 30f, DivW = 2f;

    // 各按钮宽度
    const float W_BACK = 64f, W_LAYER = 140f, W_DEFAULT = 140f, W_PLUS = 72f, W_ICON = 62f;

    // 严格跟着 Toolbar() 的排布算，避免两边对不上导致工具条被裁掉一截
    static float BarWidth()
    {
        float x = PadX;
        x += W_BACK + GapGroup + DivW + GapGroup;
        x += W_LAYER + GapGroup + DivW + GapGroup;
        x += W_DEFAULT + GapGroup + DivW + GapGroup;
        if (selected == null) x += W_PLUS + GapGroup + DivW + GapGroup;
        x += W_ICON + GapIn + W_ICON;
        if (selected != null) x += GapIn + W_ICON + GapIn + W_ICON + GapIn + W_ICON;
        return x + PadX;
    }

    static Rect ToolbarRect()
    {
        float w = Mathf.Min(BarWidth(), UiKit.W - 32f);
        // 右侧面板打开时，工具条在"面板左侧的空白区"里居中，别被面板盖住
        bool side = page == P_Edit || page == P_Style || page == P_Shape || page == P_Color;
        float right = side ? PanelRect().x - 10f : UiKit.W;
        return new Rect(Mathf.Max(14f, (right - w) * 0.5f), BarY, w, BarH);
    }

    static Rect plusRect;

    static void Toolbar(bool act)
    {
        var bar = ToolbarRect();
        Pill(bar, BarEdge);
        Pill(new Rect(bar.x + 1.6f, bar.y + 1.6f, bar.width - 3.2f, bar.height - 3.2f), BarBg);

        float ih = W_ICON;
        float iy = bar.y + (bar.height - ih) * 0.5f;
        float cy = bar.y + bar.height * 0.5f;
        float x = bar.x + PadX;

        // ← 返回
        var backR = new Rect(x, iy, W_BACK, ih);
        HoverCircle(backR, act);
        ArrowBack(new Vector2(backR.center.x, cy), backR.Contains(aPos) ? Color.white : Ink);
        if (BarTap(backR, act)) { Close(); return; }
        x += W_BACK + GapGroup;
        Divider(x, cy); x += DivW + GapGroup;

        // 图层 1 ▾（本阶段只显示）
        LayerChip(new Rect(x, iy, W_LAYER, ih), cy);
        x += W_LAYER + GapGroup;
        Divider(x, cy); x += DivW + GapGroup;

        // 默认布局：复用现有工具条样式，点击立即套用并保存。
        var defaultR = new Rect(x, iy, W_DEFAULT, ih);
        Pill(defaultR, defaultR.Contains(aPos) ? Accent : BarBg);
        Text(defaultR, "默认布局", 24, Ink, TextAnchor.MiddleCenter);
        if (BarTap(defaultR, act))
        {
            PushHistory();
            foreach (var e in VirtualControls.elems) VirtualControls.ReleaseForEdit(e);
            VirtualControls.RestoreDefaultLayout();
            selected = null;
            dragging = resizing = false;
            panelMoving = scrollArm = false;
            dragSlider = -1;
            dirty = false;
            page = P_None;
            BottomBar.Toast("已应用默认布局");
            return;
        }
        x += W_DEFAULT + GapGroup;
        Divider(x, cy); x += DivW + GapGroup;

        if (selected == null)
        {
            plusRect = new Rect(x, iy, W_PLUS, ih);
            HoverCircle(plusRect, act);
            Plus(new Vector2(plusRect.center.x, cy),
                 page == P_Add || plusRect.Contains(aPos) ? Color.white : Ink);
            if (BarTap(plusRect, act)) OpenPage(page == P_Add ? P_None : P_Add);
            x += W_PLUS + GapGroup;
            Divider(x, cy); x += DivW + GapGroup;
        }

        // ↶ 撤回
        var undR = new Rect(x, iy, W_ICON, ih);
        bool canUndo = undoStack.Count > 0;
        if (canUndo) HoverCircle(undR, act);
        UndoIcon(new Vector2(undR.center.x, cy), canUndo ? (undR.Contains(aPos) ? Color.white : Ink) : InkDim);
        if (canUndo && BarTap(undR, act)) Undo();
        x += W_ICON + GapIn;

        // ↷ 还原
        var redR = new Rect(x, iy, W_ICON, ih);
        bool canRedo = redoStack.Count > 0;
        if (canRedo) HoverCircle(redR, act);
        RedoIcon(new Vector2(redR.center.x, cy), canRedo ? (redR.Contains(aPos) ? Color.white : Ink) : InkDim);
        if (canRedo && BarTap(redR, act)) Redo();
        x += W_ICON;

        if (selected == null) return;

        // 🗑 删除
        x += GapIn;
        var delR = new Rect(x, iy, W_ICON, ih);
        HoverCircle(delR, act);
        Trash(new Vector2(delR.center.x, cy), delR.Contains(aPos) ? Color.white : Ink);
        if (BarTap(delR, act)) DeleteSelected();
        x += W_ICON + GapIn;

        // ⧉ 复制
        var copyR = new Rect(x, iy, W_ICON, ih);
        HoverCircle(copyR, act);
        CopyIcon(new Vector2(copyR.center.x, cy), copyR.Contains(aPos) ? Color.white : Ink);
        if (BarTap(copyR, act)) CopySelected();
        x += W_ICON + GapIn;

        // ✏️ 编辑
        var editR = new Rect(x, iy, W_ICON, ih);
        HoverCircle(editR, act);
        bool editOn = page == P_Edit || page == P_Style || page == P_Shape || page == P_Color || page == P_Keys;
        Pencil(new Vector2(editR.center.x, cy), editOn || editR.Contains(aPos) ? Color.white : Ink);
        if (BarTap(editR, act)) OpenPage(page == P_Edit ? P_None : P_Edit);
    }

    // 面板被拖到太靠左时，工具条塞不进左边的空白区 → 整条先藏起来（别露半截）
    static bool BarFits()
    {
        bool side = page == P_Edit || page == P_Style || page == P_Shape || page == P_Color;
        if (!side) return true;
        return PanelRect().x - 10f >= BarWidth() + 20f;
    }

    // 面板盖住的那一段工具条不再响应点击（否则会同时触发面板里的控件）
    static bool BarTap(Rect r, bool act)
    {
        if (!Tap(r, act)) return false;
        bool side = page == P_Edit || page == P_Style || page == P_Shape || page == P_Color;
        return !(side && PanelRect().Contains(r.center));
    }

    static void Divider(float x, float cy)
        => UiKit.Fill(new Rect(x, cy - 19f, DivW, 38f), DividerCol);

    static void LayerChip(Rect r, float cy)
    {
        Text(new Rect(r.x, r.y, r.width - 30f, r.height), "图层 1", 25, Ink, TextAnchor.MiddleLeft);
        var t = new Vector2(r.x + r.width - 20f, cy);
        var col = new Color(1f, 1f, 1f, 0.62f);
        VirtualControls.DrawLine(t + new Vector2(-7f, -3.5f), t + new Vector2(0f, 3.5f), col, 2.6f);
        VirtualControls.DrawLine(t + new Vector2(0f, 3.5f), t + new Vector2(7f, -3.5f), col, 2.6f);
    }

    // —————————— 选中项的 删除 / 复制 ——————————
    static void DeleteSelected()
    {
        if (selected == null) return;
        PushHistory();
        VirtualControls.ReleaseForEdit(selected);
        VirtualControls.elems.Remove(selected);
        selected = null;
        page = P_None;
        VirtualControls.SaveActive();
    }

    static void CopySelected()
    {
        if (selected == null) return;
        PushHistory();
        var c = selected.Clone();
        c.cx = Mathf.Clamp(c.cx + 0.06f, 0.02f, 0.98f);
        c.cy = Mathf.Clamp(c.cy + 0.06f, 0.02f, 0.98f);
        VirtualControls.elems.Add(c);
        selected = c;
        VirtualControls.SaveActive();
    }

    // —————————— 撤回 / 还原 ——————————
    static void PushHistory() => PushHistory(VirtualControls.ToJson());

    static void PushHistory(string snapshot)
    {
        if (string.IsNullOrEmpty(snapshot)) snapshot = VirtualControls.ToJson();
        undoStack.Add(snapshot);
        if (undoStack.Count > HistoryMax) undoStack.RemoveAt(0);
        redoStack.Clear();
    }

    static void Restore(string json)
    {
        VirtualControls.FromJson(json);
        VirtualControls.SaveActive();
        selected = null;
        dragging = false;
        page = P_None;
    }

    static void Undo()
    {
        if (undoStack.Count == 0) return;
        redoStack.Add(VirtualControls.ToJson());
        int last = undoStack.Count - 1;
        var s = undoStack[last];
        undoStack.RemoveAt(last);
        Restore(s);
    }

    static void Redo()
    {
        if (redoStack.Count == 0) return;
        undoStack.Add(VirtualControls.ToJson());
        int last = redoStack.Count - 1;
        var s = redoStack[last];
        redoStack.RemoveAt(last);
        Restore(s);
    }

    // —————————— 「添加控件」面板（锚在 ＋ 下方） ——————————
    enum CardKind { Button, Stick, Trackpad, Dpad, KeyGroup }

    const float AddPad = 18f, AddGap = 14f, AddCardH = 156f, AddLabH = 34f, AddHead = 62f;

    static Rect AddPanelRect()
    {
        float w = Mathf.Min(680f, UiKit.W - 32f);
        float rowH = AddCardH + AddLabH;
        float h = AddHead + rowH * 2f + AddGap + 20f;
        float anchor = plusRect.width > 0f ? plusRect.center.x : UiKit.W * 0.5f;
        float x = Mathf.Clamp(anchor - w * 0.5f, 14f, Mathf.Max(14f, UiKit.W - w - 14f));
        return new Rect(x, ToolbarRect().yMax + 6f, w, h);
    }

    static void AddPanel(bool act)
    {
        var panel = AddPanelRect();
        Rounded(new Rect(panel.x - 1.5f, panel.y - 1.5f, panel.width + 3f, panel.height + 3f), PanelEdge);
        Rounded(panel, PnlBg);

        float cardW = (panel.width - AddPad * 2f - AddGap * 2f) / 3f;
        float rowH = AddCardH + AddLabH;
        float top = panel.y + AddHead;

        Text(new Rect(panel.x + AddPad, panel.y + 6f, panel.width - AddPad * 2f, 46f),
             "添加控件", 27, new Color(0.90f, 0.91f, 0.92f, 1f), TextAnchor.MiddleLeft);

        for (int i = 0; i < 5; i++)
        {
            int col = i % 3, row = i / 3;
            var card = new Rect(panel.x + AddPad + col * (cardW + AddGap), top + row * (rowH + AddGap), cardW, AddCardH);
            var lab = new Rect(card.x, card.yMax + 2f, card.width, AddLabH - 4f);

            switch (i)
            {
                case 0: AddCard(card, lab, CardKind.Button, "按钮", true, act, () => AddElem(VCElemType.Button)); break;
                case 1: AddCard(card, lab, CardKind.Stick, "摇杆", true, act, () => AddElem(VCElemType.Stick)); break;
                case 2: AddCard(card, lab, CardKind.Trackpad, "触控板", false, act, null); break;
                case 3: AddCard(card, lab, CardKind.Dpad, "方向键", true, act, () => AddElem(VCElemType.Dpad)); break;
                default: AddCard(card, lab, CardKind.KeyGroup, "按键组", false, act, null); break;
            }
        }

        if (act && aPressed && Time.frameCount != pageOpenedFrame &&
            !panel.Contains(aPos) && !ToolbarRect().Contains(aPos))
            page = P_None;
    }

    static void AddCard(Rect card, Rect lab, CardKind kind, string label, bool enabled, bool act, System.Action click)
    {
        bool hover = enabled && card.Contains(aPos);
        Rounded(card, hover ? new Color(0.145f, 0.145f, 0.158f, 1f) : CardBg);
        if (hover) Rounded(card, new Color(1f, 1f, 1f, 0.10f));

        var g = enabled ? Glyph : new Color(Glyph.r, Glyph.g, Glyph.b, 0.30f);
        var c = new Vector2(card.center.x, card.y + card.height * 0.46f);

        switch (kind)
        {
            case CardKind.Button:
                Disc(c, 38f, new Color(0.16f, 0.16f, 0.17f, 1f));
                Ring(c, 38f, g, 2.2f);
                Text(new Rect(c.x - 40f, c.y - 40f, 80f, 80f), "A", 36,
                     enabled ? g : new Color(g.r, g.g, g.b, 0.55f), TextAnchor.MiddleCenter);
                break;
            case CardKind.Stick:
                // 缩略图按真实摇杆的分层画：底盘 / 外环 / 内圈 / 球钮
                Disc(c, 40f, new Color(0.075f, 0.10f, 0.135f, 1f));
                Ring(c, 40f, g, 2.6f);
                Ring(c, 31f, new Color(g.r, g.g, g.b, 0.45f), 1.6f);
                Disc(c, 16f, g);
                break;
            case CardKind.Trackpad:
                var rp = new Rect(c.x - 40f, c.y - 28f, 80f, 56f);
                Rounded(rp, new Color(0.14f, 0.14f, 0.15f, 1f));
                Outline(rp, g, 2.2f);
                break;
            case CardKind.Dpad:
                foreach (var d in DpadDirs)
                    Rounded(new Rect(c.x + d.x - 11.5f, c.y + d.y - 11.5f, 23f, 23f), g);
                break;
            case CardKind.KeyGroup:
                for (int i = 0; i < 3; i++)
                {
                    var kb = new Rect(c.x - 44f + i * 30f, c.y - 26f, 27f, 52f);
                    Rounded(kb, g);
                    Text(kb, "ABC"[i].ToString(), 21, new Color(0.10f, 0.10f, 0.11f, enabled ? 0.85f : 0.45f), TextAnchor.MiddleCenter);
                }
                break;
        }

        Text(lab, label, 23, enabled ? Ink : InkDim, TextAnchor.MiddleCenter);
        if (enabled && click != null && Tap(card, act)) click();
    }

    static readonly Vector2[] DpadDirs =
    {
        new Vector2(0f, -18f), new Vector2(0f, 18f), new Vector2(-18f, 0f), new Vector2(18f, 0f)
    };

    static void AddElem(VCElemType t)
    {
        PushHistory();
        var e = new VCElem { type = t, cx = 0.5f, cy = 0.5f };
        if (t == VCElemType.Button)
        {
            e.action = GameAction.Jump;
            e.key = KeyCode.None;          // 不预绑键：绑了就等于按了那个物理键
            e.size = 0.18f;
        }
        else if (t == VCElemType.Stick)
        {
            e.size = 0.34f;
            VirtualControls.ApplyStickSkin(e);   // 蓝环 + 深色底盘，和参考图一致
        }
        else e.size = 0.34f;
        e.opacity = 1f;
        VirtualControls.elems.Add(e);
        VirtualControls.SaveActive();
        selected = e;
        OpenPage(P_Edit);                  // 添加完直接进编辑，少点一次铅笔
    }

    // —————————— 图标 ——————————
    // 统一用 GS 放大：以后调图标大小只改这一个数
    const float GS = 1.42f;

    static void Seg(Vector2 c, float x0, float y0, float x1, float y1, Color col, float w)
        => VirtualControls.DrawLine(c + new Vector2(x0, y0) * GS,
                                    c + new Vector2(x1, y1) * GS, col, w * GS);

    static void ArrowBack(Vector2 c, Color col)
    {
        Seg(c, 10f, 0f, -8f, 0f, col, 2.2f);
        Seg(c, -8f, 0f, 0f, -8f, col, 2.2f);
        Seg(c, -8f, 0f, 0f, 8f, col, 2.2f);
    }

    static void Plus(Vector2 c, Color col)
    {
        Seg(c, -10f, 0f, 10f, 0f, col, 2.4f);
        Seg(c, 0f, -10f, 0f, 10f, col, 2.4f);
    }

    static void Gear(Vector2 c, Color col)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f + Mathf.PI / 8f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            VirtualControls.DrawLine(c + d * (6.5f * GS), c + d * (12f * GS), col, 4.4f * GS);
        }
        Disc(c, 9f * GS, col);
        Disc(c, 3.6f * GS, new Color(0.055f, 0.055f, 0.06f, 1f));
    }

    static void UndoIcon(Vector2 c, Color col) => TurnArrow(c, col, false);
    static void RedoIcon(Vector2 c, Color col) => TurnArrow(c, col, true);

    static void TurnArrow(Vector2 c, Color col, bool mirror)
    {
        float sx = mirror ? -1f : 1f;
        float rad = 8.5f * GS;
        const int seg = 26;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI;
            float a1 = (i + 1) / (float)seg * Mathf.PI;
            var p0 = c + new Vector2(Mathf.Cos(a0) * sx, -Mathf.Sin(a0)) * rad;
            var p1 = c + new Vector2(Mathf.Cos(a1) * sx, -Mathf.Sin(a1)) * rad;
            VirtualControls.DrawLine(p0, p1, col, 2.2f * GS);
        }
        var tip = c + new Vector2(-rad * sx, 0f);
        VirtualControls.DrawLine(tip, tip + new Vector2(5.5f, -6f) * GS, col, 2.2f * GS);
        VirtualControls.DrawLine(tip, tip + new Vector2(5.5f, 6f) * GS, col, 2.2f * GS);
    }

    static void Trash(Vector2 c, Color col)
    {
        Seg(c, -11f, -8f, 11f, -8f, col, 2.2f);
        Seg(c, -4f, -8f, -4f, -12f, col, 2f);
        Seg(c, 4f, -8f, 4f, -12f, col, 2f);
        Seg(c, -4f, -12f, 4f, -12f, col, 2f);
        Seg(c, -8f, -8f, -6.5f, 12f, col, 2.2f);
        Seg(c, 8f, -8f, 6.5f, 12f, col, 2.2f);
        Seg(c, -6.5f, 12f, 6.5f, 12f, col, 2.2f);
        Seg(c, -2f, -3f, -1.6f, 8f, col, 1.8f);
        Seg(c, 2f, -3f, 1.6f, 8f, col, 1.8f);
    }

    static void CopyIcon(Vector2 c, Color col)
    {
        Outline(new Rect(c.x - 11f * GS, c.y - 11f * GS, 15f * GS, 15f * GS), col, 2f * GS);
        Outline(new Rect(c.x - 3f * GS, c.y - 3f * GS, 15f * GS, 15f * GS), col, 2f * GS);
    }

    static void Pencil(Vector2 c, Color col)
    {
        var a = c + new Vector2(-8f, 7f) * GS;
        var b = c + new Vector2(8f, -9f) * GS;
        var dir = (b - a).normalized;
        var nrm = new Vector2(-dir.y, dir.x) * (3.2f * GS);
        float w = 1.8f * GS;
        VirtualControls.DrawLine(a + nrm, b + nrm, col, w);
        VirtualControls.DrawLine(a - nrm, b - nrm, col, w);
        VirtualControls.DrawLine(b - nrm, b + nrm, col, w);
        VirtualControls.DrawLine(a + nrm, a - nrm, col, w);
        // 笔尖
        var tip = a - dir * (4f * GS);
        VirtualControls.DrawLine(a + nrm, tip, col, w);
        VirtualControls.DrawLine(a - nrm, tip, col, w);
    }

    static void XIcon(Vector2 c, Color col, float s = 11f)
    {
        VirtualControls.DrawLine(c + new Vector2(-s, -s), c + new Vector2(s, s), col, 3f);
        VirtualControls.DrawLine(c + new Vector2(s, -s), c + new Vector2(-s, s), col, 3f);
    }

    static void ChevronRight(Vector2 c, Color col, float s = 7f)
    {
        VirtualControls.DrawLine(c + new Vector2(-s * 0.6f, -s), c + new Vector2(s * 0.6f, 0f), col, 2.8f);
        VirtualControls.DrawLine(c + new Vector2(s * 0.6f, 0f), c + new Vector2(-s * 0.6f, s), col, 2.8f);
    }

    // —————————— 基础图形 ——————————
    static void HoverCircle(Rect r, bool act)
    {
        if (act && r.Contains(aPos))
            Disc(new Vector2(r.center.x, r.center.y), 26f, new Color(1f, 1f, 1f, 0.07f));
    }

    static void Disc(Vector2 c, float r, Color col)
    {
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), VirtualControls.DiscTex());
        GUI.color = old;
    }

    static void Ring(Vector2 c, float r, Color col, float w)
        => VirtualControls.DrawAnnulus(c, Mathf.Max(0f, r - w * 0.5f), r + w * 0.5f, col, false);

    // 圆角矩形描边（外圈一个色 + 内圈"擦"回底色，视觉上是一条等宽描边；IMGUI 没有擦除混合，
    // 只能靠 2 层遮盖实现，好处是四角圆润、边缘有现成的抗锯齿）
    static void StrokeRect(Rect r, Color line, Color fill, float t)
    {
        Rounded(new Rect(r.x - t * 0.5f, r.y - t * 0.5f, r.width + t, r.height + t), line);
        Rounded(r, fill);
    }

    static void Rounded(Rect r, Color col)
    {
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(r, VirtualControls.SoftTex());
        GUI.color = old;
    }

    static void Outline(Rect r, Color col, float t)
    {
        UiKit.Fill(new Rect(r.x, r.y, r.width, t), col);
        UiKit.Fill(new Rect(r.x, r.yMax - t, r.width, t), col);
        UiKit.Fill(new Rect(r.x, r.y, t, r.height), col);
        UiKit.Fill(new Rect(r.xMax - t, r.y, t, r.height), col);
    }

    static void Pill(Rect r, Color col)
    {
        float rad = r.height * 0.5f;
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(r.x + rad, r.y, Mathf.Max(0f, r.width - rad * 2f), r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, rad * 2f, r.height), VirtualControls.DiscTex());
        GUI.DrawTexture(new Rect(r.xMax - rad * 2f, r.y, rad * 2f, r.height), VirtualControls.DiscTex());
        GUI.color = old;
    }

    // 一次点击：按下与抬起都在同一块区域内、且几乎没有移动
    static bool Tap(Rect r, bool act)
        // 切页后不能复用打开页面的抬手事件，否则会点穿到新页面同位置的控件。
        => act && Time.frameCount != pageOpenedFrame && aReleased && pressDist < 12f
            && r.Contains(aPos) && r.Contains(aPressPos);

    // 统一走 VirtualControls.Label：带伪粗（项目字体只有一个字重），
    // 面板/工具条的字因此比 GUI.Label 默认的细身字更有分量。
    static void Text(Rect r, string s, int size, Color col, TextAnchor align, int weight = 1)
        => VirtualControls.Label(r, s, size, col, align, weight);
}
