using UnityEngine;

// ————————————————————————————————————————————————————————————
// 虚拟按键编辑器 —— 右侧编辑面板
//   页 2 编辑：基础属性（缩放/旋转/纵横比）+ 外观样式（不透明度/样式设置/形状…）+ 按键映射
//   页 3 样式设置：描边宽度、光环宽度、默认/按下状态的颜色
//   页 4 选择形状：圆形/矩形/多边形/LT/LB/RT/RB/无
//   页 5 颜色：HSV + 透明度
//
//   字号按参考图放大后，一屏放不下所有行，所以内容区支持上下拖动（右侧有滚动条提示）；
//   页头永远钉在顶部，内容滚上去会被页头盖住，看起来就是正常裁切。
// ————————————————————————————————————————————————————————————
public static partial class VirtualControlsEditor
{
    static readonly Color PnlBg = new Color(0.031f, 0.031f, 0.035f, 0.985f);
    static readonly Color PanelEdge = new Color(1f, 1f, 1f, 0.10f);
    static readonly Color CardBg = new Color(0.110f, 0.110f, 0.118f, 1f);
    static readonly Color Glyph = new Color(0.64f, 0.65f, 0.67f, 1f);
    static readonly Color TxtMain = new Color(0.91f, 0.91f, 0.93f, 1f);
    static readonly Color TxtSub = new Color(0.60f, 0.60f, 0.63f, 1f);
    static readonly Color TrackBg = new Color(0.25f, 0.25f, 0.26f, 1f);
    static readonly Color RowLineCol = new Color(1f, 1f, 1f, 0.055f);
    static readonly Color ChipBg = new Color(0.165f, 0.165f, 0.175f, 1f);

    const float HeadH = 76f;     // 页头高度
    const float RH = 80f;        // 滑块行
    const float RHNav = 58f;     // 跳转行 / 颜色行
    const float SecH = 26f;      // 小标题占位高
    const float GapY = 8f;       // 卡片之间的间距
    const float InsetX = 20f;    // 卡片左右内缩

    static int dragSlider = -1;  // 正在拖的滑块
    static bool dirty;

    static float panelX = -1f;        // 面板左边缘；-1 = 默认贴右
    static bool panelMoving;          // 正在拖面板
    static float panelMoveLast;

    static float panelScroll, panelScrollMax;
    static bool scrollArm;
    static Vector2 scrollArmPos, scrollLast;

    // 内容区可见范围：滚出这个范围的整行不再响应点击（否则会点到底下那一行）
    static float bodyTop, bodyBottom;

    static float PanelW => Mathf.Min(520f, UiKit.W - 24f);

    static Rect PanelRect()
    {
        float w = PanelW;
        float def = UiKit.W - w - 12f;
        if (panelX < 0f) panelX = def;
        panelX = Mathf.Clamp(panelX, 12f, Mathf.Max(12f, UiKit.W - w - 12f));
        return new Rect(panelX, 8f, w, UiKit.H - 16f);
    }

    static float Ease(float t) => t * t * (3f - 2f * t);

    static void SidePanel(bool act)
    {
        var r = PanelRect();
        r.x += (1f - Ease(Mathf.Clamp01(pageT))) * 60f;   // 切换页：整块面板从右侧滑入

        // ① 标题栏横向拖动 = 移动面板（把要摆的按键露出来）
        var grip = new Rect(r.x, r.y, r.width, HeadH);
        if (act && aPressed && grip.Contains(aPos)) { panelMoving = true; panelMoveLast = aPos.x; }
        if (panelMoving)
        {
            if (aDown)
            {
                float dx = aPos.x - panelMoveLast;
                panelMoveLast = aPos.x;
                if (Mathf.Abs(dx) > 0.4f)
                    panelX = Mathf.Clamp(panelX + dx, 12f, Mathf.Max(12f, UiKit.W - r.width - 12f));
            }
            else panelMoving = false;
        }

        // ② 内容区纵向拖动 = 滚动（滑块正在拖的时候不滚）
        if (act)
        {
            if (aPressed && !grip.Contains(aPos) && r.Contains(aPos))
            { scrollArm = true; scrollArmPos = scrollLast = aPos; }
            if (scrollArm)
            {
                if (!aDown) scrollArm = false;
                else
                {
                    float dy = aPos.y - scrollLast.y;
                    scrollLast = aPos;
                    float moved = aPos.y - scrollArmPos.y;
                    if (dragSlider < 0 && Mathf.Abs(moved) > 10f && Mathf.Abs(moved) > Mathf.Abs(aPos.x - scrollArmPos.x))
                        panelScroll = Mathf.Clamp(panelScroll - dy, 0f, panelScrollMax);
                }
            }
        }

        Rounded(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), PanelEdge);
        Rounded(r, PnlBg);

        // 内容整体上移 panelScroll；超出面板的部分靠页头/底部遮罩盖掉
        bodyTop = r.y + HeadH;
        bodyBottom = r.yMax;
        var body = new Rect(r.x, bodyTop + 6f - panelScroll, r.width, r.height);

        float bottom;
        switch (page)
        {
            case P_Edit: bottom = EditPage(body, act); break;
            case P_Style: bottom = StylePage(body, act); break;
            case P_Shape: bottom = ShapePage(body, act); break;
            default: bottom = ColorPage(body, act); break;
        }

        // 内容总高 → 可滚动范围（画完才知道，晚一帧收敛没关系）
        float contentH = bottom + 14f - (bodyTop + 6f);
        panelScrollMax = Mathf.Max(0f, contentH - (r.height - HeadH - 6f));
        panelScroll = Mathf.Clamp(panelScroll, 0f, panelScrollMax);

        // 滚动条
        if (panelScrollMax > 0.5f)
        {
            float vh = r.height - HeadH;
            float th = Mathf.Max(48f, vh * (vh / (vh + panelScrollMax)));
            float ty = r.y + HeadH + (vh - th) * Mathf.Clamp01(panelScroll / panelScrollMax);
            Rounded(new Rect(r.xMax - 7f, ty, 5f, th), new Color(1f, 1f, 1f, 0.22f));
        }

        // 底部遮罩：把滚出面板下沿的那一点点裁掉
        UiKit.Fill(new Rect(r.x, r.yMax - 1f, r.width, UiKit.H - r.yMax + 1f), Color.black);

        // 页头最后画：天然挡住被滚上去的内容
        HeaderMask(r, act);

        if (act && aReleased)
        {
            if (dirty) { VirtualControls.SaveActive(); dirty = false; }
            dragSlider = -1;
        }
    }

    // —————————— 页头 ——————————
    static void HeaderMask(Rect r, bool act)
    {
        UiKit.Fill(new Rect(r.x, r.y, r.width, HeadH), PnlBg);
        UiKit.Fill(new Rect(r.x + InsetX, r.y + HeadH - 1f, r.width - InsetX * 2f, 1f), RowLineCol);

        string title = page == P_Style ? "样式设置"
                     : page == P_Shape ? "选择形状"
                     : page == P_Color ? "颜色"
                     : selected == null ? "编辑"
                     : selected.type == VCElemType.Stick ? "编辑 摇杆"
                     : selected.type == VCElemType.Dpad ? "编辑 方向键"
                     : "编辑 按钮";
        bool back = page != P_Edit;
        int backTo = page == P_Color ? P_Style : P_Edit;

        float cy = r.y + HeadH * 0.5f;
        float tx = r.x + InsetX;

        if (back)
        {
            var br = new Rect(r.x + 8f, cy - 22f, 46f, 44f);
            ArrowBack(new Vector2(br.center.x, br.center.y), br.Contains(aPos) ? Color.white : Ink);
            if (Tap(br, act)) OpenPage(backTo);
            tx = r.x + 62f;
        }

        Text(new Rect(tx, r.y, r.width - 150f, HeadH), title, 27, TxtMain, TextAnchor.MiddleLeft);

        var xr = new Rect(r.xMax - 56f, cy - 22f, 46f, 44f);
        XIcon(new Vector2(xr.center.x, xr.center.y),
              xr.Contains(aPos) ? Color.white : new Color(1f, 1f, 1f, 0.72f), 11f);
        if (Tap(xr, act))
        {
            if (dirty) { VirtualControls.SaveActive(); dirty = false; }
            page = P_None;
        }
    }

    static void SectionLabel(Rect r, float y, string s)
        => Text(new Rect(r.x + InsetX, y, r.width - InsetX * 2f, SecH - 4f), s, 19, TxtSub, TextAnchor.MiddleLeft);

    // 卡片：先铺一层 1px 的极淡轮廓光，再压主体色 —— 纯色块在深色面板上会"糊"成一片
    static void Card(Rect r)
    {
        Rounded(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(1f, 1f, 1f, 0.05f));
        Rounded(r, CardBg);
    }

    static void RowLine(Rect card, float y)
        => UiKit.Fill(new Rect(card.x + InsetX, y, card.width - InsetX * 2f, 1f), RowLineCol);

    // 只在可见区域里的行才响应点击
    static bool PTap(Rect r, bool act)
        => r.y >= bodyTop - 0.5f && r.yMax <= bodyBottom + 0.5f && Tap(r, act);

    static bool PVisible(Rect r) => r.y >= bodyTop - 0.5f && r.yMax <= bodyBottom + 0.5f;

    // —————————— 行控件 ——————————
    static float SliderRow(Rect row, string title, string value, float v, float min, float max, int id, bool act)
    {
        Text(new Rect(row.x + InsetX, row.y + 12f, row.width - 170f, 30f), title, 24, TxtMain, TextAnchor.MiddleLeft);
        Text(new Rect(row.x, row.y + 12f, row.width - InsetX, 30f), value, 21, TxtSub, TextAnchor.MiddleRight);
        var track = new Rect(row.x + InsetX, row.y + 62f, row.width - InsetX * 2f, 11f);
        return Slider(track, v, min, max, id, act);
    }

    static bool NavRow(Rect row, string title, string value, bool act)
    {
        Text(new Rect(row.x + InsetX, row.y, row.width - 210f, row.height), title, 24, TxtMain, TextAnchor.MiddleLeft);
        if (!string.IsNullOrEmpty(value))
            Text(new Rect(row.x, row.y, row.width - 48f, row.height), value, 21, TxtSub, TextAnchor.MiddleRight);
        ChevronRight(new Vector2(row.xMax - 26f, row.center.y), new Color(1f, 1f, 1f, 0.45f), 8f);
        return PTap(row, act);
    }

    static bool ColorRow(Rect row, string title, Color c, bool act)
    {
        Text(new Rect(row.x + InsetX, row.y, row.width - 260f, row.height), title, 24, TxtMain, TextAnchor.MiddleLeft);

        var chip = new Rect(row.xMax - 200f, row.y + (row.height - 40f) * 0.5f, 180f, 40f);
        Rounded(chip, ChipBg);
        // 半透明颜色的底色（棋盘格简化成浅灰）
        Disc(new Vector2(chip.x + 26f, chip.center.y), 13f, new Color(0.55f, 0.55f, 0.57f, 1f));
        Disc(new Vector2(chip.x + 26f, chip.center.y), 13f, c);
        Text(new Rect(chip.x + 48f, chip.y, chip.width - 58f, chip.height),
             VirtualControls.Hex(c), 20, new Color(0.78f, 0.78f, 0.81f, 1f), TextAnchor.MiddleLeft);
        return PTap(row, act);
    }

    // 注意：这个还用在「选择按键」弹窗里，那里没有面板的可见区概念，所以用普通 Tap
    static bool ToggleChip(Rect r, string label, bool on, bool act)
    {
        Rounded(r, on ? Accent : ChipBg);
        Text(r, label, 21, on ? Color.white : TxtSub, TextAnchor.MiddleCenter);
        return Tap(r, act);
    }

    // —————————— 滑块 ——————————
    static float Slider(Rect track, float v, float min, float max, int id, bool act)
    {
        float k = Mathf.InverseLerp(min, max, v);
        var knob = new Vector2(track.x + track.width * k, track.center.y);

        Rounded(track, TrackBg);
        Disc(knob, 17f, Color.white);

        var hit = new Rect(track.x - 16f, track.y - 22f, track.width + 32f, track.height + 44f);
        if (act && aPressed && aPos.y >= bodyTop && aPos.y <= bodyBottom && hit.Contains(aPos))
        {
            dragSlider = id;
            PushHistory();
        }
        if (dragSlider == id)
        {
            if (aDown)
            {
                float nv = Mathf.Lerp(min, max, Mathf.Clamp01((aPos.x - track.x) / Mathf.Max(1f, track.width)));
                if (!Mathf.Approximately(nv, v)) dirty = true;
                v = nv;
            }
            else dragSlider = -1;
        }
        return v;
    }

    // —————————— 页 2：编辑 ——————————
    static float EditPage(Rect b, bool act)
    {
        var e = selected;
        if (e == null) { page = P_None; return b.y; }

        // 摇杆 / 十字键是另一套属性（没有形状、旋转对这些控件没意义），单独一页
        if (e.type != VCElemType.Button) return StickPage(b, e, act);

        float x = b.x + InsetX, w = b.width - InsetX * 2f;
        float y = b.y;

        // —— 基础属性 ——
        SectionLabel(b, y, "基础属性"); y += SecH;
        var card = new Rect(x, y, w, RH * 3f + 2f);
        Card(card);

        float ry = card.y;
        e.scale = SliderRow(new Rect(x, ry, w, RH), "整体缩放", e.scale.ToString("0.0"), e.scale, 0.4f, 3f, 1, act); ry += RH;
        RowLine(card, ry);
        e.rotation = SliderRow(new Rect(x, ry, w, RH), "旋转角度", Mathf.RoundToInt(e.rotation) + "°", e.rotation, -180f, 180f, 2, act); ry += RH;
        RowLine(card, ry);
        e.aspect = SliderRow(new Rect(x, ry, w, RH), "纵横比例", e.aspect.ToString("0.0"), e.aspect, 0.4f, 2.5f, 3, act);
        y = card.yMax + GapY;

        // —— 外观样式 ——
        // 说明：初版这里还有「图标设置」「按键模式」两个占位行（点了没反应）。
        // 字号整体放大后一屏放不下九行，先把两个占位收起来，等真做功能再加回来。
        SectionLabel(b, y, "外观样式"); y += SecH;
        card = new Rect(x, y, w, RH + RHNav * 2f + 4f);
        Card(card);

        ry = card.y;
        e.opacity = SliderRow(new Rect(x, ry, w, RH), "不透明度", Mathf.RoundToInt(e.opacity * 100f) + "%", e.opacity, 0.05f, 1f, 4, act);
        ry += RH; RowLine(card, ry);
        if (NavRow(new Rect(x, ry, w, RHNav), "样式设置", "", act)) OpenPage(P_Style); ry += RHNav;
        RowLine(card, ry);
        if (NavRow(new Rect(x, ry, w, RHNav), "形状", ShapeName(e.shape), act)) OpenPage(P_Shape);
        y = card.yMax + GapY;

        // —— 按键映射 ——
        SectionLabel(b, y, "按键映射"); y += SecH;
        card = new Rect(x, y, w, RHNav);
        Card(card);
        string bind = e.key == KeyCode.None ? "未绑定" : (e.shiftMod ? "Shift + " : "") + VirtualControls.KeyLabel(e.key);
        if (NavRow(new Rect(x, card.y, w, RHNav), "选择按键", bind, act))
        {
            keyTarget = 0;                     // 按钮页落的是主键
            OpenPage(P_Keys);
        }
        y = card.yMax + GapY;

        // —— 技能绑定 ——
        // 原版是「技能窗把技能拖到快捷格」，这里是同一语义的屏上版本：
        // 游戏里把技能图标拖到这个按键上即绑定（面板只负责看和解除）。
        SectionLabel(b, y, "技能"); y += SecH;
        card = new Rect(x, y, w, RHNav);
        Card(card);
        var bound = e.skillId > 0 ? SkillBook.Get(e.skillId) : null;
        string boundName = bound != null ? bound.name : "从技能窗拖入";
        if (NavRow(new Rect(x, card.y, w, RHNav), "绑定技能", boundName, act) && e.skillId > 0)
        {
            VirtualControls.UnbindSkill(e);
            BottomBar.Toast("已解除技能绑定");
        }

        return card.yMax;
    }

    static readonly string[] ShapeNames = { "圆形", "矩形", "多边形", "LT", "LB", "RT", "RB", "无" };
    static string ShapeName(int s) => s >= 0 && s < ShapeNames.Length ? ShapeNames[s] : "圆形";

    // —————————— 页 2b：摇杆 / 十字键 ——————————
    // 参考图只保留「输入模式」和「按键映射」；动态范围、闲置隐藏、触摸即触发、
    // 左右摇杆这些高级项都不做。摇杆唯一真正需要调的是尺寸，所以下面留了整体缩放。
    static readonly string[] DirNames = { "上", "下", "左", "右" };
    static readonly int[] DirRot = { 3, 1, 2, 0 };          // 基准朝右，顺时针转 90° 的次数

    static float StickPage(Rect b, VCElem e, bool act)
    {
        float x = b.x + InsetX, w = b.width - InsetX * 2f;
        float y = b.y;

        // —— 输入模式 ——
        SectionLabel(b, y, "输入模式"); y += SecH;
        var seg = new Rect(x, y, w, 66f);
        int mode = Seg2(seg, "标准输入", "自定义", e.stickStd ? 0 : 1, act);
        if (mode == 0 && !e.stickStd)
        {
            PushHistory();
            VirtualControls.ReleaseStickKeys(e);   // 残留在按下的方向键先抬起来，别把键带进新模式
            e.stickStd = true;
            dirty = true;
        }
        else if (mode == 1 && e.stickStd)
        {
            PushHistory();
            e.stickStd = false;
            dirty = true;
        }
        y = seg.yMax + 12f;

        Text(new Rect(x, y, w, 30f),
             e.stickStd ? "直接输出上下左右移动，不用绑键盘。"
                        : "推向某个方向时，同时按下该方向绑的键盘键。",
             18, TxtSub, TextAnchor.MiddleLeft);
        y += 40f;

        // 自定义但一个方向都没绑：说清楚"为什么摇杆没反应"，免得以为是坏了
        if (!e.stickStd && !e.HasDirKey)
        {
            Text(new Rect(x, y, w, 28f), "四个方向都还没绑键，摇杆目前不会输出。",
                 17, new Color(1f, 0.72f, 0.30f, 1f), TextAnchor.MiddleLeft);
            y += 32f;
        }

        // —— 按键映射（只有自定义模式才有意义） ——
        if (!e.stickStd)
        {
            SectionLabel(b, y, "按键映射"); y += SecH;
            var mcard = new Rect(x, y, w, RHNav * 4f + 3f);
            Card(mcard);
            for (int d = 0; d < 4; d++)
            {
                var row = new Rect(x, mcard.y + RHNav * d, w, RHNav);
                if (DirRow(row, d, e.DirKey(d), act))
                {
                    keyTarget = d + 1;              // 1~4 = 上/下/左/右，「选择按键」落键时用
                    OpenPage(P_Keys);
                }
                if (d < 3) RowLine(mcard, mcard.y + RHNav * (d + 1));
            }
            y = mcard.yMax + GapY;
        }

        // —— 基础属性：摇杆只需要大小 ——
        SectionLabel(b, y, "基础属性"); y += SecH;
        var scard = new Rect(x, y, w, RH + 1f);
        Card(scard);
        e.scale = SliderRow(new Rect(x, scard.y, w, RH), "整体缩放",
                            e.scale.ToString("0.0"), e.scale, 0.4f, 3f, 1, act);

        return scard.yMax;
    }

    // 两段分段控件（标准输入 / 自定义）
    static int Seg2(Rect r, string left, string right, int cur, bool act)
    {
        Rounded(r, ChipBg);
        float half = r.width * 0.5f;
        for (int i = 0; i < 2; i++)
        {
            var cell = new Rect(r.x + half * i, r.y, half, r.height);
            if (i == cur) Rounded(new Rect(cell.x + 4f, cell.y + 4f, cell.width - 8f, cell.height - 8f), Accent);
            Text(cell, i == 0 ? left : right, 23, i == cur ? Color.white : TxtSub, TextAnchor.MiddleCenter);
            if (PTap(cell, act)) return i;
        }
        return cur;
    }

    // 「上 / 下 / 左 / 右 → 当前绑定键」一行
    static bool DirRow(Rect row, int dir, KeyCode k, bool act)
    {
        ChevronDir(new Vector2(row.x + InsetX + 15f, row.center.y), dir,
                   new Color(0.82f, 0.84f, 0.87f, 1f), 9f);

        Text(new Rect(row.x + InsetX + 42f, row.y, row.width - 230f, row.height),
             DirNames[dir], 24, TxtMain, TextAnchor.MiddleLeft);

        bool none = k == KeyCode.None;
        Text(new Rect(row.x, row.y, row.width - 58f, row.height),
             none ? "无" : VirtualControls.KeyLabel(k), 22,
             none ? new Color(0.46f, 0.46f, 0.49f, 1f) : TxtMain, TextAnchor.MiddleRight);

        ChevronRight(new Vector2(row.xMax - 28f, row.center.y), new Color(1f, 1f, 1f, 0.42f), 8f);
        return PTap(row, act);
    }

    static void ChevronDir(Vector2 c, int dir, Color col, float s)
    {
        int rot = (dir >= 0 && dir < 4) ? DirRot[dir] : 0;
        var a = Rot90(new Vector2(-s * 0.6f, -s), rot);
        var b = Rot90(new Vector2(s * 0.6f, 0f), rot);
        var d = Rot90(new Vector2(-s * 0.6f, s), rot);
        VirtualControls.DrawLine(c + a, c + b, col, 2.6f);
        VirtualControls.DrawLine(c + b, c + d, col, 2.6f);
    }

    static Vector2 Rot90(Vector2 v, int times)
    {
        for (int i = 0; i < times; i++) v = new Vector2(-v.y, v.x);
        return v;
    }

    // —————————— 页 3：样式设置 ——————————
    static float StylePage(Rect b, bool act)
    {
        var e = selected;
        if (e == null) { page = P_None; return b.y; }

        float x = b.x + InsetX, w = b.width - InsetX * 2f;
        float y = b.y;

        SectionLabel(b, y, "基础设置"); y += SecH;
        var card = new Rect(x, y, w, RH * 2f + 1f);
        Card(card);

        float ry = card.y;
        e.borderW = SliderRow(new Rect(x, ry, w, RH), "描边宽度", e.borderW.ToString("0.0") + "dp", e.borderW, 0f, 8f, 11, act);
        ry += RH; RowLine(card, ry);
        e.halo = SliderRow(new Rect(x, ry, w, RH), "光环宽度", Mathf.RoundToInt(e.halo * 100f) + "%", e.halo, 0f, 1f, 12, act);
        y = card.yMax + GapY;

        SectionLabel(b, y, "默认状态"); y += SecH;
        card = new Rect(x, y, w, RHNav * 2f + 1f);
        Card(card);
        if (ColorRow(new Rect(x, card.y, w, RHNav), "背景颜色", e.bg, act)) { colorTarget = 0; OpenColor(e.bg); }
        RowLine(card, card.y + RHNav);
        if (ColorRow(new Rect(x, card.y + RHNav, w, RHNav), "描边颜色", e.line, act)) { colorTarget = 1; OpenColor(e.line); }
        y = card.yMax + GapY;

        SectionLabel(b, y, "按下状态"); y += SecH;
        card = new Rect(x, y, w, RHNav * 2f + 1f);
        Card(card);
        if (ColorRow(new Rect(x, card.y, w, RHNav), "背景颜色", e.bgDown, act)) { colorTarget = 2; OpenColor(e.bgDown); }
        RowLine(card, card.y + RHNav);
        if (ColorRow(new Rect(x, card.y + RHNav, w, RHNav), "描边颜色", e.lineDown, act)) { colorTarget = 3; OpenColor(e.lineDown); }

        return card.yMax;
    }

    // —————————— 页 4：选择形状 ——————————
    static float ShapePage(Rect b, bool act)
    {
        var e = selected;
        if (e == null) { page = P_None; return b.y; }

        float x = b.x + InsetX, w = b.width - InsetX * 2f;
        float y = b.y;

        const int cols = 3;
        float gap = 14f, cardW = (w - gap * (cols - 1)) / cols, cardH = 150f;

        for (int i = 0; i < 8; i++)
        {
            int c = i % cols, row = i / cols;
            var cell = new Rect(x + c * (cardW + gap), y + row * (cardH + gap), cardW, cardH);
            bool on = e.shape == i;

            var cellBg = on ? new Color(0.06f, 0.20f, 0.29f, 1f) : CardBg;
            Rounded(cell, cellBg);
            if (on) StrokeRect(cell, SelectRing, cellBg, 2.4f);

            var cc = new Vector2(cell.center.x, cell.y + 58f);
            var col = new Color(1f, 1f, 1f, 0.92f);
            if (i < 7)
            {
                var m = GUI.matrix;
                GUI.matrix = m * Matrix4x4.TRS(new Vector3(cc.x, cc.y, 0f), Quaternion.identity, Vector3.one);
                VirtualControls.DrawShape(i, 36f, new Color(1f, 1f, 1f, 0.10f), col, 2.6f, 0f);
                GUI.matrix = m;
            }
            Text(new Rect(cell.x, cell.yMax - 52f, cell.width, 34f), ShapeNames[i], 21,
                 i == 7 ? TxtSub : TxtMain, TextAnchor.MiddleCenter);

            if (PTap(cell, act))
            {
                if (e.shape != i) { PushHistory(); e.shape = i; dirty = true; }
                OpenPage(P_Edit);
            }
        }

        return y + 3f * cardH + gap * 2f - 4f;
    }
    // —————————— 页 5：颜色 ——————————
    static float ch, cs, cv, ca;
    static Color colorOrigin;

    static void OpenColor(Color c)
    {
        RgbToHsv(c, out ch, out cs, out cv);
        ca = c.a;
        colorOrigin = c;
        OpenPage(P_Color);
    }

    static Color CurrentColor() => HsvToRgb(ch, cs, cv, ca);

    static float ColorPage(Rect b, bool act)
    {
        var e = selected;
        if (e == null) { page = P_None; return b.y; }

        float x = b.x + InsetX, w = b.width - InsetX * 2f;
        float y = b.y;

        // 预览
        var prev = new Rect(x, y, w, 140f);
        Rounded(prev, ChipBg);
        Rounded(new Rect(prev.x + 10f, prev.y + 10f, prev.width - 20f, prev.height - 20f), CurrentColor());

        // 纯色预览条（不看透明度）
        var solid = HsvToRgb(ch, cs, cv, 1f);
        var bar = new Rect(prev.x + 12f, prev.yMax - 40f, 64f, 26f);
        Rounded(bar, solid);
        Text(new Rect(bar.xMax + 14f, bar.y - 4f, prev.width - 110f, 32f),
             VirtualControls.Hex(CurrentColor()), 20, new Color(0.82f, 0.82f, 0.85f, 1f), TextAnchor.MiddleLeft);
        y = prev.yMax + 16f;

        var card = new Rect(x, y, w, RH * 4f + 3f);
        Card(card);

        float ry = card.y;
        ch = SliderRow(new Rect(x, ry, w, RH), "色相", Mathf.RoundToInt(ch * 360f) + "°", ch, 0f, 1f, 21, act); ry += RH;
        RowLine(card, ry);
        cs = SliderRow(new Rect(x, ry, w, RH), "饱和度", Mathf.RoundToInt(cs * 100f) + "%", cs, 0f, 1f, 22, act); ry += RH;
        RowLine(card, ry);
        cv = SliderRow(new Rect(x, ry, w, RH), "亮度", Mathf.RoundToInt(cv * 100f) + "%", cv, 0f, 1f, 23, act); ry += RH;
        RowLine(card, ry);
        ca = SliderRow(new Rect(x, ry, w, RH), "透明度", Mathf.RoundToInt(ca * 100f) + "%", ca, 0f, 1f, 24, act);

        // 实时写回元素
        var nc = CurrentColor();
        if (!SameColor(nc, TargetColor(e))) { SetTargetColor(e, nc); dirty = true; }

        return card.yMax;
    }

    static bool SameColor(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f
        && Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;

    static Color TargetColor(VCElem e)
    {
        switch (colorTarget)
        {
            case 0: return e.bg;
            case 1: return e.line;
            case 2: return e.bgDown;
            default: return e.lineDown;
        }
    }

    static void SetTargetColor(VCElem e, Color c)
    {
        switch (colorTarget)
        {
            case 0: e.bg = c; break;
            case 1: e.line = c; break;
            case 2: e.bgDown = c; break;
            default: e.lineDown = c; break;
        }
    }

    // —————————— HSV ——————————
    static void RgbToHsv(Color c, out float h, out float s, out float v)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        v = max;
        float d = max - min;
        s = max <= 0.0001f ? 0f : d / max;
        if (d <= 0.0001f) { h = 0f; return; }
        if (max == c.r) h = Mathf.Repeat((c.g - c.b) / d, 6f);
        else if (max == c.g) h = (c.b - c.r) / d + 2f;
        else h = (c.r - c.g) / d + 4f;
        h /= 6f;
    }

    static Color HsvToRgb(float h, float s, float v, float a)
    {
        h = Mathf.Repeat(h, 1f);
        float i = Mathf.Floor(h * 6f);
        float f = h * 6f - i;
        float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
        switch ((int)i % 6)
        {
            case 0: return new Color(v, t, p, a);
            case 1: return new Color(q, v, p, a);
            case 2: return new Color(p, v, t, a);
            case 3: return new Color(p, q, v, a);
            case 4: return new Color(t, p, v, a);
            default: return new Color(v, p, q, a);
        }
    }
}
