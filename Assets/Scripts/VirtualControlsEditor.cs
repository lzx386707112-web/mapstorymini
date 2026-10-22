using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 虚拟按键编辑器 —— 对齐 Winlator Controls Editor：
//
//   · 全屏暗色网格背景；选中元素画红色贯穿十字线 + 蓝圈
//   · 顶部居中悬浮工具条：配置文件名 + ＋ 添加 + 垃圾桶 + 齿轮 + ✓
//   · 右侧白色属性卡：类型 / 形状 / 大小 / 透明度 / 按键 + 复制 / 删除
//   · 「按键」一栏打开全键盘选择器：A-Z、0-9、F1-F12、编辑键、修饰键、
//     方向键、符号键全都能绑（不再局限于固定游戏动作列表）
//
// 输入统一策略：
//   · 桌面端：全部走 Pointer（== 真实鼠标，与其余 HUD 按钮一致）
//   · 真机端：绝对触点（Pointer 是轨迹板语义，只有绝对触点能"按哪个拖哪个"）
// ————————————————————————————————————————————————————————————
public static class VirtualControlsEditor
{
    public static bool Visible { get; private set; }

    // —— 统一输入（桌面=Pointer/鼠标，真机=绝对触点） ——
    static bool aDown, aPressed, aReleased;
    static Vector2 aPos, aPressPos;
    static int menuOpenedFrame = -1;

    static VCElem selected;
    static bool dragging;
    static Vector2 dragOff;
    static bool panelHidden;      // 属性卡收起（元素被挡住时用）

    // 弹层：0 无 · 1 齿轮 · 2 类型 · 3 形状 · 5 全键盘按键选择
    static int menu;
    static Vector2 menuAnchor;

    static readonly Color Teal = new Color(0.06f, 0.66f, 0.60f);
    static readonly Color PanelBg = new Color(0.97f, 0.97f, 0.97f);
    static readonly Color FieldBg = new Color(0.95f, 0.95f, 0.95f);
    static readonly Color Line = new Color(0.72f, 0.72f, 0.72f);
    static readonly Color TextDark = new Color(0.12f, 0.12f, 0.12f);
    static readonly Color TextGray = new Color(0.42f, 0.42f, 0.42f);
    static readonly Color ToolBg = new Color(0.10f, 0.10f, 0.11f, 0.94f);

    public static void Toggle()
    {
        if (Visible) Close(); else Open();
    }

    static void Open()
    {
        VirtualControls.Ensure();
        Visible = true;
        selected = null;
        menu = 0;
        InputHub.ReleaseAllVirtual();
        TouchInput.Reset();
    }

    static void Close()
    {
        Visible = false;
        menu = 0;
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
        if (aPressed) aPressPos = aPos;
    }

    // —————————— 主绘制 ——————————
    public static void Draw()
    {
        if (!Visible) return;
        VirtualControls.Ensure();
        Poll();
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F8)) { Close(); return; }

        bool act = Event.current.type == EventType.Repaint;

        DrawBackdrop();

        if (act && menu == 0) HandleScene();
        foreach (var e in VirtualControls.elems) VirtualControls.DrawElement(e);
        if (selected != null) DrawSelection(selected);

        Toolbar(act);
        // 拖动元素时不画属性卡：否则它会把拖到右侧的元素盖住
        if (selected != null && !dragging)
        {
            if (panelHidden) PanelTab(act);
            else PropsPanel(act);
        }
        else if (selected == null) Hint();

        if (act) DrawMenuOverlay(act);
    }

    // —————————— 背景 ——————————
    static void DrawBackdrop()
    {
        UiKit.Fill(new Rect(0f, 0f, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.86f));
        var minor = new Color(1f, 1f, 1f, 0.05f);
        var major = new Color(1f, 1f, 1f, 0.10f);
        const float step = 32f, big = 160f;
        for (float x = 0f; x <= UiKit.W; x += step)
            VirtualControls.DrawLine(new Vector2(x, 0f), new Vector2(x, UiKit.H),
                Mathf.Approximately(x % big, 0f) ? major : minor, 1f);
        for (float y = 0f; y <= UiKit.H; y += step)
            VirtualControls.DrawLine(new Vector2(0f, y), new Vector2(UiKit.W, y),
                Mathf.Approximately(y % big, 0f) ? major : minor, 1f);
    }

    // —————————— 选中：红色贯穿十字线 + 蓝圈（文字只由元素自己画一次，避免重叠） ——————————
    static void DrawSelection(VCElem e)
    {
        var c = new Vector2(e.cx * UiKit.W, e.cy * UiKit.H);
        var red = new Color(0.85f, 0.15f, 0.15f, 0.55f);
        VirtualControls.DrawLine(new Vector2(0f, c.y), new Vector2(UiKit.W, c.y), red, 2f);
        VirtualControls.DrawLine(new Vector2(c.x, 0f), new Vector2(c.x, UiKit.H), red, 2f);
        var blue = new Color(0.25f, 0.5f, 0.95f, 0.95f);
        int seg = 40;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
            VirtualControls.DrawLine(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * (e.R + 6f),
                                     c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * (e.R + 6f), blue, 3f);
        }
    }

    static void Hint()
    {
        var st = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
        st.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        GUI.Label(new Rect(0f, UiKit.H - 54f, UiKit.W, 30f),
            "点击画面上的按键进行编辑   ·   顶部 ＋ 添加新按键", st);
    }

    // —————————— 场景交互：点选 / 直接拖动 ——————————
    static void HandleScene()
    {
        if (!aDown) dragging = false;
        if (dragging && selected != null)
        {
            var p = aPos - dragOff;
            selected.cx = Mathf.Clamp(p.x / UiKit.W, 0.02f, 0.98f);
            selected.cy = Mathf.Clamp(p.y / UiKit.H, 0.02f, 0.98f);
            return;
        }

        if (!aPressed) return;
        if (OverUi()) return;

        VCElem hit = null;
        for (int i = VirtualControls.elems.Count - 1; i >= 0; i--)
            if (VirtualControls.elems[i].Hit(aPos)) { hit = VirtualControls.elems[i]; break; }

        selected = hit;
        dragging = hit != null;
        if (hit != null)
            dragOff = aPos - new Vector2(hit.cx * UiKit.W, hit.cy * UiKit.H);
        if (hit == null) menu = 0;
    }

    static Rect ToolbarRect()
    {
        float w = Mathf.Min(520f, UiKit.W - 24f), h = 72f;
        return new Rect(UiKit.W * 0.5f - w * 0.5f, 14f, w, h);
    }

    static float PropsHeight(VCElem e)
    {
        float h = 96f;                                   // 标题 + 上边距
        h += 82f;                                        // 类型
        if (e.type == VCElemType.Button) h += 82f;        // 形状
        h += 78f + 78f;                                   // 大小 + 透明度
        if (e.type == VCElemType.Button) h += 82f + 82f;  // 动作 + 按键
        h += 66f;                                         // 复制 / 删除
        return h;
    }

    static Rect PropsRect()
    {
        float h = selected != null ? PropsHeight(selected) : 300f;
        float w = Mathf.Min(312f, UiKit.W - 24f);
        // 选中元素落在右侧时，面板自动换到左边，避免把自己的按钮盖住
        float rightX = Mathf.Max(12f, UiKit.W - w - 20f);
        bool cover = selected != null && selected.cx * UiKit.W > rightX - 24f;
        return new Rect(cover ? 20f : rightX, 100f, w, h);
    }

    static bool OverUi()
    {
        if (ToolbarRect().Contains(aPos)) return true;
        if (selected != null && !panelHidden && !dragging && PropsRect().Contains(aPos)) return true;
        return false;
    }

    // 收起后的入口：右上角一个小标签，点一下重新展开属性卡
    static void PanelTab(bool act)
    {
        var r = new Rect(UiKit.W - 56f, 104f, 44f, 44f);
        bool hover = r.Contains(aPos);
        SoftFill(new Rect(r.x + 4f, r.y + 5f, r.width, r.height), new Color(0f, 0f, 0f, 0.35f));
        SoftFill(r, hover ? new Color(0.90f, 0.96f, 0.95f) : PanelBg);
        Border(r, hover ? Teal : Line, 2f);
        for (int i = 0; i < 3; i++)
            UiKit.Fill(new Rect(r.x + 12f, r.y + 13f + i * 7f, 20f, 3f), TextDark);
        if (act && aPressed && hover) { panelHidden = false; }
    }

    // —————————— 顶部工具条 ——————————
    static void Toolbar(bool act)
    {
        var bar = ToolbarRect();
        SoftFill(bar, ToolBg);

        var prof = new Rect(bar.x + 18f, bar.y + 8f, 190f, bar.height - 16f);
        if (act && aPressed && prof.Contains(aPos)) CycleProfile(1);
        Label(new Rect(prof.x, prof.y + 4f, prof.width, 20f), "配置文件", 14, TextGray);
        var names = VirtualControls.profileNames;
        Label(new Rect(prof.x, prof.y + 26f, prof.width, 28f),
            names.Contains(VirtualControls.activeName) ? VirtualControls.activeName : "", 20, Teal);
        UiKit.Fill(new Rect(prof.xMax + 8f, bar.y + 14f, 2f, bar.height - 28f), new Color(1f, 1f, 1f, 0.22f));

        float y = bar.y + (bar.height - 48f) * 0.5f, s = 48f;
        float x = prof.xMax + 24f;
        if (IconBtn(new Rect(x, y, s, s), Icon.Plus, Teal, act)) AddButton(); x += s + 14f;
        if (IconBtn(new Rect(x, y, s, s), Icon.Trash, Color.white, act)) DeleteSelected(); x += s + 14f;
        if (IconBtn(new Rect(x, y, s, s), Icon.Gear, Color.white, act))
        {
            bool open = menu != 1;
            OpenMenu(open ? 1 : 0, new Vector2(x - 8f, bar.yMax + 4f));
        } x += s + 14f;
        if (IconBtn(new Rect(x, y, s, s), Icon.Check, new Color(0.2f, 0.85f, 0.4f), act)) Close();
    }

    static string TypeName(VCElemType t)
        => t == VCElemType.Button ? "BUTTON" : t == VCElemType.Dpad ? "DPAD" : "STICK";

    static void OpenMenu(int m, Vector2 anchor)
    {
        menu = m;
        menuAnchor = anchor;
        menuOpenedFrame = Time.frameCount;
    }

    static void CycleProfile(int dir)
    {
        var names = VirtualControls.profileNames;
        if (names.Count == 0) return;
        VirtualControls.SaveActive();
        int i = names.IndexOf(VirtualControls.activeName);
        if (i < 0) i = 0;
        i = (i + dir + names.Count) % names.Count;
        VirtualControls.activeName = names[i];
        if (!VirtualControls.LoadFromFile(names[i]))
        {
            VirtualControls.elems.Clear();
            VirtualControls.RestoreDefaultLayout();
        }
        selected = null;
    }

    static void AddButton()
    {
        var e = new VCElem
        {
            type = VCElemType.Button,
            action = GameAction.Jump,
            key = KeyMap.Of(GameAction.Jump),
            size = 0.18f,
            opacity = 0.75f,
            cx = 0.5f,
            cy = 0.42f
        };
        VirtualControls.elems.Add(e);
        selected = e;
        menu = 0;
    }

    static void DeleteSelected()
    {
        if (selected == null) return;
        VirtualControls.ReleaseForEdit(selected);
        VirtualControls.elems.Remove(selected);
        selected = null;
        menu = 0;
    }

    // —————————— 右侧属性卡（Winlator 白色卡片） ——————————
    static void PropsPanel(bool act)
    {
        var e = selected;
        var r = PropsRect();

        // 阴影 + 白卡
        SoftFill(new Rect(r.x + 5f, r.y + 6f, r.width, r.height), new Color(0f, 0f, 0f, 0.35f));
        SoftFill(r, PanelBg);

        float x = r.x + 22f, w = r.width - 44f, y = r.y + 18f;

        // 标题（右侧「收起」按钮：面板挡住元素时可以收起来）
        Label(new Rect(x, y, w - 90f, 26f), "属性", 21, TextDark);
        LabelR(new Rect(x, y, w - 34f, 26f), TypeName(e.type), 15, TextGray);
        if (CollapseBtn(new Rect(r.xMax - 44f, y - 2f, 30f, 30f), act)) panelHidden = true;
        y += 34f;
        UiKit.Fill(new Rect(x, y, w, 1.5f), new Color(0.86f, 0.86f, 0.86f));
        y += 14f;

        // 类型
        FieldLabel(x, y, w, "类型", TypeName(e.type));
        if (Dropdown(new Rect(x, y + 26f, w, 44f), TypeName(e.type), act))
            OpenMenu(2, new Vector2(x, y + 72f));
        y += 82f;

        // 形状
        if (e.type == VCElemType.Button)
        {
            string sh = e.shape == 0 ? "CIRCLE" : "ROUNDED";
            FieldLabel(x, y, w, "形状", sh);
            if (Dropdown(new Rect(x, y + 26f, w, 44f), sh, act))
                OpenMenu(3, new Vector2(x, y + 72f));
            y += 82f;
        }

        // 大小
        float pct = e.size / 0.18f * 100f;
        FieldLabel(x, y, w, "大小", Mathf.RoundToInt(pct) + "%");
        e.size = Slider(new Rect(x, y + 30f, w, 12f), e.size, 0.08f, 0.45f, act);
        y += 78f;

        // 透明度
        FieldLabel(x, y, w, "透明度", Mathf.RoundToInt(e.opacity * 100f) + "%");
        e.opacity = Slider(new Rect(x, y + 30f, w, 12f), e.opacity, 0.15f, 1f, act);
        y += 78f;

        // 动作（真正触发什么：攻击/跳跃/技能格… 与键位表无关）
        if (e.type == VCElemType.Button)
        {
            FieldLabel(x, y, w, "动作", VirtualControls.ActionDisplayName(e.action));
            if (Dropdown(new Rect(x, y + 26f, w, 44f), VirtualControls.ActionDisplayName(e.action), act))
                OpenMenu(6, Vector2.zero);
            y += 82f;
        }

        // 按键（全键盘）
        if (e.type == VCElemType.Button)
        {
            FieldLabel(x, y, w, "按键", BindSummary(e.Key, e.action));
            if (Dropdown(new Rect(x, y + 26f, w, 44f), VirtualControls.ActionName(e), act))
                OpenMenu(5, Vector2.zero);
            y += 82f;
        }

        // 复制 / 删除
        float bw = (w - 14f) * 0.5f;
        if (ChipBtn(new Rect(x, y, bw, 46f), "复制", TextDark, FieldBg, act))
        {
            var c = e.Clone();
            c.cx = Mathf.Clamp(c.cx + 0.05f, 0.02f, 0.98f);
            c.cy = Mathf.Clamp(c.cy + 0.05f, 0.02f, 0.98f);
            VirtualControls.elems.Add(c);
            selected = c;
        }
        if (ChipBtn(new Rect(x + bw + 14f, y, bw, 46f), "删除", new Color(0.82f, 0.22f, 0.22f), FieldBg, act))
            DeleteSelected();
    }

    // 标签 + 右侧当前值
    static void FieldLabel(float x, float y, float w, string title, string value)
    {
        Label(new Rect(x, y, w * 0.5f, 22f), title, 16, TextGray);
        LabelR(new Rect(x, y, w, 22f), value, 15, TextDark);
    }

    // 按键摘要：这个键在游戏里指向哪个动作；没指向就说明会按「动作」触发
    static string BindSummary(KeyCode k, GameAction action)
    {
        if (k == KeyCode.None) return "无键 · 按动作触发";
        var a = VirtualControls.ActionOfKey(k);
        return a.HasValue ? "键映射 · " + VirtualControls.ActionDisplayName(a.Value) : "键未映射 · 按动作";
    }

    // —————————— 弹层 ——————————
    static void DrawMenuOverlay(bool act)
    {
        if (menu == 0) return;

        if (menu == 5) { KeyPicker(act); return; }
        if (menu == 6) { ActionPicker(act); return; }

        if (menu == 1)
        {
            var items = new[] { "新建方案", "删除当前方案", "恢复默认布局" };
            PickList(menuAnchor, items, -1, i =>
            {
                if (i == 0) { VirtualControls.NewProfile("方案" + (VirtualControls.profileNames.Count + 1)); selected = null; }
                if (i == 1 && VirtualControls.profileNames.Count > 1) { VirtualControls.DeleteProfile(VirtualControls.activeName); selected = null; }
                if (i == 2) { VirtualControls.RestoreDefaultLayout(); selected = null; }
                menu = 0;
            });
            return;
        }

        string[] list;
        int cur = 0;
        System.Action<int> apply;
        if (menu == 2)
        {
            list = new[] { "BUTTON", "DPAD", "STICK" };
            cur = (int)selected.type;
            apply = i =>
            {
                var nt = (VCElemType)i;
                if (nt != selected.type)
                {
                    selected.type = nt;
                    if (nt != VCElemType.Button) selected.size = Mathf.Max(selected.size, 0.32f);
                }
            };
        }
        else if (menu == 3)
        {
            list = new[] { "CIRCLE", "ROUNDED" };
            cur = selected.shape;
            apply = i => selected.shape = i;
        }
        else
        {
            list = new[] { "CIRCLE", "ROUNDED" };
            cur = selected.shape;
            apply = i => selected.shape = i;
        }
        PickList(menuAnchor, list, cur, i => { apply(i); menu = 0; });
    }

    // —————————— 动作选择：网格面板（攻击/跳跃/技能格…，不依赖键盘键位） ——————————
    static void ApplyAction(GameAction a)
    {
        if (selected == null) { menu = 0; return; }
        selected.action = a;
        // 当前键若指向别的动作，会与新动作打架 → 跟着新动作走；
        // 键本身没映射（例如自己设的 C）就保留，当"别名键"显示。
        var ka = VirtualControls.ActionOfKey(selected.key);
        if (selected.key != KeyCode.None && ka.HasValue && ka.Value != a)
            selected.key = KeyMap.Of(a);
        VirtualControls.SaveActive();
        menu = 0;
    }

    static void ActionPicker(bool act)
    {
        UiKit.Fill(new Rect(0f, 0f, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.5f));

        var acts = VirtualControls.BindableActions;
        const int cols = 3;
        int rows = Mathf.CeilToInt(acts.Length / (float)cols);
        float pw = Mathf.Min(UiKit.W - 28f, 660f);
        float ph = Mathf.Min(UiKit.H - 20f, 96f + rows * 56f + 18f);
        var panel = new Rect((UiKit.W - pw) * 0.5f, (UiKit.H - ph) * 0.5f, pw, ph);
        SoftFill(new Rect(panel.x + 6f, panel.y + 8f, panel.width, panel.height), new Color(0f, 0f, 0f, 0.4f));
        SoftFill(panel, PanelBg);

        Label(new Rect(panel.x + 24f, panel.y + 14f, pw * 0.6f, 30f), "选择动作", 22, TextDark);
        Label(new Rect(panel.x + 24f, panel.y + 44f, pw - 220f, 20f),
            "当前：" + VirtualControls.ActionDisplayName(selected != null ? selected.action : GameAction.Jump) +
            "    ·    动作与键盘键位无关，点了就一定触发", 13, TextGray);
        if (CloseBtn(new Rect(panel.xMax - 58f, panel.y + 16f, 40f, 40f), act))
            menu = 0;

        float pad = 22f, gap = 10f, rowH = 46f;
        float cw = (panel.width - pad * 2f - gap * (cols - 1)) / cols;
        float top = panel.y + 80f;

        for (int i = 0; i < acts.Length; i++)
        {
            int c = i % cols, r = i / cols;
            var rr = new Rect(panel.x + pad + c * (cw + gap), top + r * (rowH + gap), cw, rowH);
            if (rr.yMax > panel.yMax - 10f) break;
            bool sel = selected != null && selected.action == acts[i];
            bool hover = rr.Contains(aPos);
            SoftFill(rr, sel ? Teal : hover ? new Color(0.93f, 0.93f, 0.93f) : new Color(0.97f, 0.97f, 0.97f));
            Border(rr, sel ? Teal : Line, 1.5f);
            LabelC(rr, VirtualControls.ActionDisplayName(acts[i]), 17, sel ? Color.white : TextDark);
            if (act && aPressed && hover) ApplyAction(acts[i]);
        }
    }

    // 小下拉列表（类型/形状/齿轮菜单）
    static void PickList(Vector2 at, string[] items, int current, System.Action<int> pick)
    {
        UiKit.Fill(new Rect(0f, 0f, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.3f));
        const float rowH = 52f;
        float h = Mathf.Min(items.Length * rowH + 20f, UiKit.H - at.y - 20f);
        var box = new Rect(Mathf.Min(at.x, UiKit.W - 332f), at.y, 300f, h);
        SoftFill(box, PanelBg);

        bool consumed = false;
        for (int i = 0; i < items.Length; i++)
        {
            var rr = new Rect(box.x + 8f, box.y + 10f + i * rowH, box.width - 16f, rowH);
            if (rr.yMax > box.yMax) break;
            bool hover = rr.Contains(aPos);
            if (hover) SoftFill(rr, new Color(0.84f, 0.92f, 0.91f));
            Label(new Rect(rr.x + 16f, rr.y, rr.width - 32f, rowH), items[i], 18,
                i == current ? Teal : TextDark);
            if (aPressed && hover) { pick(i); consumed = true; }
        }
        if (aPressed && !consumed && !box.Contains(aPos) && Time.frameCount != menuOpenedFrame) menu = 0;
    }

    // —————————— 全键盘按键选择器（按真实键盘排版：QWERTY + 功能键 + 导航键） ——————————
    // 每行用「键宽单位」描述（1u = 一个字母键），总宽 15u；空格 8.5u。
    static readonly (KeyCode k, float w)[] KbDigits =
    {
        (KeyCode.Alpha1,1),(KeyCode.Alpha2,1),(KeyCode.Alpha3,1),(KeyCode.Alpha4,1),(KeyCode.Alpha5,1),
        (KeyCode.Alpha6,1),(KeyCode.Alpha7,1),(KeyCode.Alpha8,1),(KeyCode.Alpha9,1),(KeyCode.Alpha0,1),
        (KeyCode.Minus,1),(KeyCode.Equals,1),(KeyCode.Backspace,3),
    };
    static readonly (KeyCode k, float w)[] KbQwerty =
    {
        (KeyCode.Tab,1.5f),
        (KeyCode.Q,1),(KeyCode.W,1),(KeyCode.E,1),(KeyCode.R,1),(KeyCode.T,1),
        (KeyCode.Y,1),(KeyCode.U,1),(KeyCode.I,1),(KeyCode.O,1),(KeyCode.P,1),
        (KeyCode.LeftBracket,1),(KeyCode.RightBracket,1),(KeyCode.Backslash,1.5f),
    };
    static readonly (KeyCode k, float w)[] KbHome =
    {
        (KeyCode.CapsLock,1.75f),
        (KeyCode.A,1),(KeyCode.S,1),(KeyCode.D,1),(KeyCode.F,1),(KeyCode.G,1),
        (KeyCode.H,1),(KeyCode.J,1),(KeyCode.K,1),(KeyCode.L,1),
        (KeyCode.Semicolon,1),(KeyCode.Quote,1),(KeyCode.Return,2.25f),
    };
    static readonly (KeyCode k, float w)[] KbBottom =
    {
        (KeyCode.LeftShift,2.25f),
        (KeyCode.Z,1),(KeyCode.X,1),(KeyCode.C,1),(KeyCode.V,1),(KeyCode.B,1),
        (KeyCode.N,1),(KeyCode.M,1),
        (KeyCode.Comma,1),(KeyCode.Period,1),(KeyCode.Slash,1),
        (KeyCode.RightShift,2.75f),
    };
    static readonly (KeyCode k, float w)[] KbSpace =
    {
        (KeyCode.LeftControl,1.75f),(KeyCode.LeftAlt,1.5f),(KeyCode.Space,8.5f),
        (KeyCode.RightAlt,1.5f),(KeyCode.RightControl,1.75f),
    };
    static readonly KeyCode[] KbFunc = BuildKeys(KeyCode.F1, 12);
    static readonly KeyCode[] KbNav =
    {
        KeyCode.Escape, KeyCode.BackQuote, KeyCode.Insert, KeyCode.Delete,
        KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
        KeyCode.LeftArrow, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.RightArrow,
    };

    static KeyCode[] BuildKeys(KeyCode start, int n)
    {
        var a = new KeyCode[n];
        for (int i = 0; i < n; i++) a[i] = (KeyCode)((int)start + i);
        return a;
    }

    static void KeyPicker(bool act)
    {
        UiKit.Fill(new Rect(0f, 0f, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.5f));

        float pw = Mathf.Min(UiKit.W - 28f, 780f);
        float ph = Mathf.Min(UiKit.H - 20f, 412f);
        var panel = new Rect((UiKit.W - pw) * 0.5f, (UiKit.H - ph) * 0.5f, pw, ph);
        SoftFill(new Rect(panel.x + 6f, panel.y + 8f, panel.width, panel.height), new Color(0f, 0f, 0f, 0.4f));
        SoftFill(panel, PanelBg);

        // 标题
        Label(new Rect(panel.x + 24f, panel.y + 14f, pw * 0.5f, 30f), "选择按键", 22, TextDark);
        Label(new Rect(panel.x + 24f, panel.y + 44f, pw - 250f, 20f),
            "当前：" + VirtualControls.KeyLabel(selected != null ? selected.Key : KeyCode.None) +
            "    ·    青色 = 该键已绑定游戏动作", 13, TextGray);

        if (ChipBtn(new Rect(panel.xMax - 190f, panel.y + 16f, 118f, 40f), "清除绑定", TextDark, FieldBg, act))
            ApplyKey(KeyCode.None);
        if (CloseBtn(new Rect(panel.xMax - 58f, panel.y + 16f, 40f, 40f), act))
            menu = 0;

        float pad = 22f;
        float kw = panel.width - pad * 2f;
        float x0 = panel.x + pad;
        const float rowH = 42f, rowGap = 5f, keyGap = 4f;

        // —— 功能键行：F1-F4 / F5-F8 / F9-F12 三组 ——
        float fy = panel.y + 74f, fh = 32f, fgap = 6f, fgroup = 14f;
        float fw = (kw - fgap * 11f - fgroup * 2f) / 12f;
        float fx = x0;
        for (int i = 0; i < KbFunc.Length; i++)
        {
            if (i == 4 || i == 8) fx += fgroup;
            KeyCap(new Rect(fx, fy, fw, fh), KbFunc[i], act);
            fx += fw + fgap;
        }

        // —— 主键盘 5 行 ——
        float ky = fy + fh + 10f;
        float unit = kw / 15f;
        DrawKeyRow(KbDigits, x0, ky, unit, rowH, keyGap, act);
        DrawKeyRow(KbQwerty, x0, ky + (rowH + rowGap), unit, rowH, keyGap, act);
        DrawKeyRow(KbHome, x0, ky + (rowH + rowGap) * 2f, unit, rowH, keyGap, act);
        DrawKeyRow(KbBottom, x0, ky + (rowH + rowGap) * 3f, unit, rowH, keyGap, act);
        DrawKeyRow(KbSpace, x0, ky + (rowH + rowGap) * 4f, unit, rowH, keyGap, act);

        // —— 导航/编辑/方向键一行 ——
        float ny = ky + (rowH + rowGap) * 5f + 4f, nh = 36f;
        float nw = (kw - keyGap * (KbNav.Length - 1)) / KbNav.Length;
        for (int i = 0; i < KbNav.Length; i++)
            KeyCap(new Rect(x0 + i * (nw + keyGap), ny, nw, nh), KbNav[i], act);
    }

    static void DrawKeyRow((KeyCode k, float w)[] row, float x, float y, float unit, float h, float gap, bool act)
    {
        foreach (var item in row)
        {
            KeyCap(new Rect(x, y, Mathf.Max(10f, item.w * unit - gap), h), item.k, act);
            x += item.w * unit;
        }
    }

    static void KeyCap(Rect r, KeyCode k, bool act)
    {
        bool sel = selected != null && selected.Key == k;
        var mapped = VirtualControls.ActionOfKey(k);
        bool hover = r.Contains(aPos);

        Color bg = sel ? Teal
                 : mapped.HasValue ? new Color(0.87f, 0.95f, 0.94f)
                 : hover ? new Color(0.93f, 0.93f, 0.93f)
                 : new Color(0.97f, 0.97f, 0.97f);
        SoftFill(r, bg);
        Border(r, sel ? Teal : mapped.HasValue ? new Color(0.45f, 0.80f, 0.76f) : new Color(0.80f, 0.80f, 0.80f), 1.5f);

        var txt = sel ? Color.white : TextDark;
        string name = VirtualControls.KeyLabel(k);
        int fs = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.36f), 10, 16);

        // 键名过长（Bksp/Shift 等）时缩小字号，避免溢出键帽
        float need = name.Length * fs * 0.62f;
        if (need > r.width - 8f) fs = Mathf.Max(9, Mathf.FloorToInt(fs * (r.width - 8f) / need));

        if (mapped.HasValue && r.width >= 46f)
        {
            LabelC(new Rect(r.x, r.y + r.height * 0.08f, r.width, r.height * 0.56f), name, fs, txt);
            LabelC(new Rect(r.x + 1f, r.y + r.height * 0.58f, r.width - 2f, r.height * 0.36f),
                VirtualControls.ActionDisplayName(mapped.Value), Mathf.Max(9, fs - 5),
                sel ? new Color(1f, 1f, 1f, 0.88f) : Teal);
        }
        else
        {
            LabelC(r, name, fs, txt);
            // 窄键放不下动作名，用右上角小圆点表示"该键已绑动作"
            if (mapped.HasValue)
            {
                var d = VirtualControls.DiscTex();
                var old = GUI.color;
                GUI.color = sel ? Color.white : Teal;
                GUI.DrawTexture(new Rect(r.xMax - 10f, r.y + 4f, 6f, 6f), d);
                GUI.color = old;
            }
        }

        if (act && aPressed && hover) ApplyKey(k);
    }

    static void ApplyKey(KeyCode k)
    {
        if (selected == null) { menu = 0; return; }
        selected.key = k;
        var a = VirtualControls.ActionOfKey(k);
        if (a.HasValue) selected.action = a.Value;
        VirtualControls.SaveActive();     // 改完立刻落盘：中途退出也不会丢绑定
        menu = 0;
    }

    // —————————— 控件 ——————————
    static bool Dropdown(Rect r, string text, bool act)
    {
        bool hover = r.Contains(aPos);
        SoftFill(r, hover ? new Color(0.90f, 0.96f, 0.95f) : FieldBg);
        Border(r, hover ? new Color(0.55f, 0.78f, 0.75f) : Line, 2f);
        var tri = new Vector2(r.xMax - 26f, r.y + r.height * 0.5f);
        VirtualControls.DrawLine(tri + new Vector2(-9f, -4f), tri + new Vector2(0f, 4f), TextGray, 2.5f);
        VirtualControls.DrawLine(tri + new Vector2(0f, 4f), tri + new Vector2(9f, -4f), TextGray, 2.5f);
        Label(new Rect(r.x + 16f, r.y, r.width - 56f, r.height), text, 18, TextDark);
        return act && aPressed && hover;
    }

    static float Slider(Rect r, float v, float min, float max, bool act)
    {
        float k = Mathf.Clamp01((v - min) / (max - min));
        SoftFill(new Rect(r.x, r.y, r.width, r.height), new Color(0.78f, 0.80f, 0.80f));
        SoftFill(new Rect(r.x, r.y, r.width * k, r.height), Teal);
        var knob = new Vector2(r.x + r.width * k, r.y + r.height * 0.5f);
        var tex = VirtualControls.DiscTex();
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.18f);
        GUI.DrawTexture(new Rect(knob.x - 14f, knob.y - 12f, 28f, 30f), tex);
        GUI.color = Teal;
        GUI.DrawTexture(new Rect(knob.x - 12f, knob.y - 14f, 24f, 24f), tex);
        GUI.color = old;

        var grab = new Rect(r.x - 14f, r.y - 22f, r.width + 28f, r.height + 44f);
        if (act && aPressed && grab.Contains(aPos)) sliding = true;
        if (sliding)
        {
            if (aDown) v = Mathf.Lerp(min, max, Mathf.Clamp01((aPos.x - r.x) / Mathf.Max(1f, r.width)));
            else sliding = false;
        }
        return v;
    }

    static bool sliding;

    static bool ChipBtn(Rect r, string label, Color txt, Color bg, bool act)
    {
        bool hover = r.Contains(aPos);
        SoftFill(r, hover && aDown ? new Color(0.85f, 0.93f, 0.92f) : bg);
        Border(r, Line, 2f);
        LabelC(r, label, 17, txt);
        return act && aReleased && hover && r.Contains(aPressPos);
    }

    static bool CollapseBtn(Rect r, bool act)
    {
        bool hover = r.Contains(aPos);
        SoftFill(r, hover ? new Color(0.90f, 0.90f, 0.90f) : FieldBg);
        Border(r, Line, 1.5f);
        var c = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
        VirtualControls.DrawLine(c + new Vector2(-6f, -7f), c + new Vector2(3f, 0f), TextDark, 2.5f);
        VirtualControls.DrawLine(c + new Vector2(3f, 0f), c + new Vector2(-6f, 7f), TextDark, 2.5f);
        return act && aReleased && hover && r.Contains(aPressPos);
    }

    static bool CloseBtn(Rect r, bool act)
    {
        bool hover = r.Contains(aPos);
        SoftFill(r, hover ? new Color(0.90f, 0.90f, 0.90f) : FieldBg);
        Border(r, Line, 2f);
        var c = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
        VirtualControls.DrawLine(c + new Vector2(-8f, -8f), c + new Vector2(8f, 8f), TextDark, 3f);
        VirtualControls.DrawLine(c + new Vector2(8f, -8f), c + new Vector2(-8f, 8f), TextDark, 3f);
        return act && aReleased && hover && r.Contains(aPressPos);
    }

    enum Icon { Plus, Trash, Gear, Check }

    static bool IconBtn(Rect r, Icon icon, Color col, bool act)
    {
        bool hover = r.Contains(aPos);
        bool down = hover && aDown;
        SoftFill(r, down ? new Color(1f, 1f, 1f, 0.18f) : hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.04f));
        var c = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

        switch (icon)
        {
            case Icon.Plus:
                UiKit.Fill(new Rect(c.x - 16f, c.y - 3.5f, 32f, 7f), col);
                UiKit.Fill(new Rect(c.x - 3.5f, c.y - 16f, 7f, 32f), col);
                break;
            case Icon.Trash:
                UiKit.Fill(new Rect(c.x - 13f, c.y - 11f, 26f, 4f), col);
                UiKit.Fill(new Rect(c.x - 3f, c.y - 16f, 6f, 4f), col);
                UiKit.Fill(new Rect(c.x - 10f, c.y - 6f, 20f, 19f), col);
                var dark = new Color(0.10f, 0.10f, 0.11f, 0.94f);
                UiKit.Fill(new Rect(c.x - 5f, c.y - 3f, 3f, 13f), dark);
                UiKit.Fill(new Rect(c.x + 2f, c.y - 3f, 3f, 13f), dark);
                break;
            case Icon.Gear:
                var tex = VirtualControls.DiscTex();
                var old = GUI.color;
                GUI.color = col;
                GUI.DrawTexture(new Rect(c.x - 13f, c.y - 13f, 26f, 26f), tex);
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * Mathf.PI * 2f;
                    var tooth = new Rect(c.x + Mathf.Cos(a) * 15f - 3f, c.y + Mathf.Sin(a) * 15f - 3f, 6f, 6f);
                    GUI.DrawTexture(tooth, Texture2D.whiteTexture);
                }
                GUI.color = new Color(0.10f, 0.10f, 0.11f, 0.94f);
                GUI.DrawTexture(new Rect(c.x - 5f, c.y - 5f, 10f, 10f), tex);
                GUI.color = old;
                break;
            case Icon.Check:
                VirtualControls.DrawLine(c + new Vector2(-12f, 0f), c + new Vector2(-3f, 10f), col, 5f);
                VirtualControls.DrawLine(c + new Vector2(-3f, 10f), c + new Vector2(13f, -10f), col, 5f);
                break;
        }
        return act && aReleased && hover && r.Contains(aPressPos) && !sliding;
    }

    // —————————— 绘制小工具 ——————————
    static void SoftFill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, VirtualControls.SoftTex());
        GUI.color = old;
    }

    static void Border(Rect r, Color c, float t)
    {
        UiKit.Fill(new Rect(r.x, r.y, r.width, t), c);
        UiKit.Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        UiKit.Fill(new Rect(r.x, r.y, t, r.height), c);
        UiKit.Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    static void Label(Rect r, string text, int size, Color col)
    {
        var st = new GUIStyle { alignment = TextAnchor.MiddleLeft, fontSize = size };
        st.normal.textColor = col;
        GUI.Label(r, text, st);
    }

    static void LabelR(Rect r, string text, int size, Color col)
    {
        var st = new GUIStyle { alignment = TextAnchor.MiddleRight, fontSize = size };
        st.normal.textColor = col;
        GUI.Label(r, text, st);
    }

    static void LabelC(Rect r, string text, int size, Color col)
    {
        var st = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = size };
        st.normal.textColor = col;
        GUI.Label(r, text, st);
    }
}
