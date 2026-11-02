using System.Collections.Generic;
using UnityEngine;

// 原端「技能点洗点」窗：cashType24 → 0xA0C6DD（USE_CASH 跳转表 0xA0EAD4[12]）。
// 三页流程（0xD4 列表窗 → 0xCC 列表窗 → 0xA0 确认页，共享例程 0x4EDAEF/0x4EDBA1 逐页确认）：
//   第 1 页选「想恢复的技能」(SPFrom)，第 2 页选「再分配目标」(SPTo)，确认页后发请求。
// 发包序（0xA0CA71..0xA0CA82）：先写第 2 页选中值(SPTo) 再写第 1 页(SPFrom)，
// 与源端 UseCashItemHandler(175..235) 的 readInt 顺序（先 SPTo 后 SPFrom）一致；
// 服务端 canSPAssign 只校验职业树（AssignSPProcessor.java:43-59），转数范围由道具语义约束：
// 5050001=1转 / 5050002≤2转 / 5050003≤3转 / 5050004≤4转（String.wz/Cash.img 官方中文描述）。
// 资源（字符串池 2934..2943）：Reset/SP/{backgrnd,backgrnd2 175x289; backgrnd3 175x230;
//   skill0/skill1 141x35、line 141x1（UOL→Skill/*）；Tab/enabled|disabled/0..3 = 罗马数字 I..IV}
// 控件（0xD4 OnCreate 0x8b2cc4 的 push 常量对，EXE 1b91cf5b…134b2）：
//   行按钮 BtSpUp 12x12 id=2002..2006 @(131, 119+40k)；0x8b2f6d 把 id-0x7DA 当行索引交给
//   0x8b3005 选中（加滚动偏移 [obj+0x38] 后写入 [obj+0xCC]）——只有点行上的小按钮才算选中。
//   滚动条 id=2001 @(153,99) 高 155（VScr5，15 宽，与技能窗 VScr4 同构）。
//   行 141x35：与技能窗同构（SkillWindow 0x8AAC69 已核实 RowX=7/RowY=99/SpUpX=131/SpUpY=20，
//   行按钮 y=行顶+20 → 行顶 99+40k，可见 5 行：99/139/179/219/259，第 5 行底部被窗框裁切）。
//   Tab 页签底板 Basic.img/Tab2 + Reset/SP/Tab 图案；tab 的 x/y 未从汇编解出，
//   按底图灰带实测 y≈57、x=3 起（同技能窗 TabX=3/TabW=34），属推断，待实拍核对。
// 确认页（0xA0 OnCreate 0x8b5889）：BtOK2 id=1 @(87,207)、BtCancel2 id=2 @(129,207)、
//   BtClose id=2 @(155,6)。87/129 与 47 宽按钮只差 42 会重叠 5px，与资源图矛盾，
//   坐标按汇编保留，待实拍核对后修正。摘要两组（像素实测）：灰条+红线（组1 y=91、组2 y=155）
//   +图标块 (8,组顶+1,35,28)+文字区 (44..168)；灰条上的原端文字未解，留空。
// 边界：源端来源技能归 0 时清理技能宏（UseCashItemHandler 199..227）——本工程无技能宏，不适用；
//   req 前置失效照源端不校验；新手（job 0/1000，虚拟 SP 体系）与 autoLearn 技能不进列表。
public static class NativeSpResetWindow
{
    const string Root = "UI/UIWindow.img/Reset/SP/";
    const int WinW = 175, WinH = 289, ConfirmH = 230;

    // 行布局：与技能窗同构（见文件头）。第 1/2 页同构（0xCC OnCreate 0x8b4127 未逐条解，
    // 按同窗布局处理，待核实）。
    const int RowX = 7, RowY = 99, RowStep = 40, RowW = 141, RowH = 35, Rows = 5;
    const int IconOffX = 1, IconOffY = 1, IconSize = 32;
    const int NameX = 45, NameY = 0, NameW = 84, NameH = 14;
    const int LevelX = 45, LevelY = 20, LevelW = 60, LevelH = 12;
    const int SpUpX = 131, SpUpY = 20, SpUpSize = 12;
    const int ScrollX = 153, ScrollW = 15, ScrollTop = 99, ScrollBottom = 254;

    // 页签（推断位）：底板 y=57 高 19，图案垂直居中
    const int TabX = 3, TabY = 57, TabW = 34, TabH = 19;

    // 确认页摘要组（像素实测）：红线 y=91/155，内容区在其下 29 高
    static readonly int[] GroupLine = { 91, 155 };

    static int itemId, itemPosition, stage, tab, scroll;
    static int fromId, toId;
    static Vector2 pos, grab;
    static bool dragging, centered, scrollDrag;
    static float scrollGrab;

    public static bool Visible => stage != 0;
    static float S => BottomBar.S;
    static Vector2 Size => new Vector2(WinW, stage == 3 ? ConfirmH : WinH);
    static Rect Window => new Rect(pos.x * S, pos.y * S, Size.x * S, Size.y * S);
    static Rect At(Rect r, float x, float y, float w, float h)
        => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);

    static Texture2D Wz(string node) => Art.Tex("wz://" + node);

    static void Image(Rect r, string node, float x, float y)
    {
        var tex = Wz(node);
        if (tex != null) GUI.DrawTexture(At(r, x, y, tex.width, tex.height), tex);
    }

    // 道具 → 可洗的最高转数。5050001..5050004（源端 handler 只要求 itemId > AP_RESET）。
    public static int MaxStageOf(int id) => Mathf.Clamp(id - 5050000, 1, 4);

    // jobId → 转数：100/1100 → 1转；110/1110（%100==10）→ 2转；111/1111 → 3转；112/1112 → 4转。
    // 验证：100→1、110→2、111→3、112→4、510→2、1100→1、1110→2、1111→3、1112→4。
    public static int StageOfJob(int job)
    {
        int b = job % 100;
        if (b == 0) return 1;
        if (b < 10) return 2;
        return b % 10 == 1 ? 3 : 4;
    }

    // 当前职业线里属于某转数的技能（排除新手：job 0 = 新手虚拟线、job 1000 = 新手特殊技能组；
    // 普通职业 100/110/… 与骑士团 1100/1110/… 都要保留）。
    static List<SkillData> StageSkills(int stage)
    {
        var result = new List<SkillData>();
        var line = SkillBook.LineForJob(Profile.JobId);
        foreach (var tabDef in SkillBook.Tabs(line))
        {
            if (!tabDef.TryGetValue("skills", out var raw)) continue;
            foreach (var o in (List<object>)raw)
            {
                var d = SkillBook.Get(int.Parse(o.ToString()));
                if (d == null || d.autoLearn || d.job == 0 || d.job == 1000) continue;
                if (StageOfJob(d.job) == stage) result.Add(d);
            }
        }
        return result;
    }

    public static void Open(int id, int inventoryPosition = 0)
    {
        int maxStage = MaxStageOf(id);
        if (maxStage < 1) return;
        itemId = id; itemPosition = inventoryPosition;
        stage = 1; fromId = 0; toId = 0; tab = 0; scroll = 0;
        dragging = false; centered = false; scrollDrag = false;
        pos = new Vector2(300, 120);
        Center();
    }

    public static void Close()
    {
        itemId = 0; stage = 0; fromId = 0; toId = 0; scroll = 0;
        dragging = false; centered = false; scrollDrag = false;
    }

    // 共享创建例程 0x4EDAEF 把窗口按 (800-w)/2,(600-h)/2 居中；S 没量准前等绘制帧再算。
    static void Center()
    {
        if (S < 0.3f) return;
        pos = NativeUi.ClampWindowPosition(
            new Vector2((UiKit.W / S - Size.x) / 2, (UiKit.H / S - Size.y) / 2), Size, S);
        centered = true;
    }

    static GUIStyle Style(int size, Color color, TextAnchor anchor)
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

    static readonly Color Ink = new Color(0.10f, 0.11f, 0.15f);

    public static void Draw()
    {
        if (!Visible) return;
        if (!centered) Center();
        pos = NativeUi.ClampWindowPosition(pos, Size, S);
        var r = Window;
        NativeUi.RegisterWindow("NativeSpResetWindow", r, () => NativeSpResetWindow.Visible);

        if (Event.current.type == EventType.Repaint && Pointer.Pressed &&
            At(r, 0, 0, WinW, 22).Contains(Pointer.Pos) && stage != 3)
        { dragging = true; centered = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down)
        {
            pos = NativeUi.ClampWindowPosition(Pointer.Pos / S - grab, Size, S);
            r = Window;
        }
        if (!Pointer.Down) dragging = false;

        if (stage == 3) { DrawConfirm(r); return; }
        DrawList(r, stage);
    }

    // —— 第 1/2 页：页签 + 技能列表 ——
    static void DrawList(Rect r, int page)
    {
        Image(r, page == 1 ? Root + "backgrnd" : Root + "backgrnd2", 0, 0);
        if (NativeUi.Button(At(r, 155, 6, 12, 12), "UI/Basic.img/BtClose")) { Close(); return; }

        // 页签：I..IV，超出道具转数范围的画 disabled 且不可点
        int maxStage = MaxStageOf(itemId);
        for (int i = 0; i < 4; i++)
        {
            bool on = i == tab;
            bool allowed = (i + 1) <= maxStage;
            var plate = At(r, TabX + i * TabW, TabY, TabW, TabH);
            DrawTabPlate(plate, on);
            var glyph = Wz(Root + "Tab/" + (on ? "enabled" : "disabled") + "/" + i);
            if (glyph != null)
                GUI.DrawTexture(new Rect(plate.x + (plate.width - glyph.width * S) * 0.5f,
                    plate.y + (TabH * S - glyph.height * S) * 0.5f,
                    glyph.width * S, glyph.height * S), glyph);
            if (allowed && Event.current.type == EventType.Repaint && Pointer.Pressed &&
                plate.Contains(Pointer.Pos) && tab != i)
            {
                tab = i; scroll = 0;
            }
        }

        var skills = PageSkills(page);
        scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, skills.Count - Rows));

        DrawRows(r, skills, page);
        DrawScrollbar(r, skills);

        if (skills.Count == 0)
            GUI.Label(At(r, 12, RowY + 10, 140, 18),
                page == 1 ? "这个转数还没有已加点的技能" : "这个转数没有可再分配的技能",
                Style(11, new Color(0.35f, 0.38f, 0.45f), TextAnchor.MiddleCenter));
    }

    // 页 1 = 来源（已加点）；页 2 = 目标（未满级）。当前页签 = tab+1 转。
    static List<SkillData> PageSkills(int page)
    {
        var all = StageSkills(tab + 1);
        var result = new List<SkillData>();
        foreach (var d in all)
        {
            int lv = SkillProgress.LevelOf(d.id);
            int cap = SkillBook.MasterCapOf(d.id);
            if (page == 1 && lv > 0) result.Add(d);
            if (page == 2 && lv < cap && d.id != fromId) result.Add(d);
        }
        return result;
    }

    static void DrawRows(Rect r, List<SkillData> skills, int page)
    {
        for (int i = 0; i < Rows; i++)
        {
            int index = scroll + i;
            if (index >= skills.Count) break;
            var d = skills[index];
            float y = RowY + i * RowStep;

            var bg = Wz(Root + "skill1");   // 原端普通行 = 蓝色 skill1（技能窗同口径）
            if (bg != null) GUI.DrawTexture(At(r, RowX, y, RowW, RowH), bg, ScaleMode.StretchToFill);

            var icon = string.IsNullOrEmpty(d.icon) ? null : Art.Tex(d.icon);
            if (icon != null) GUI.DrawTexture(At(r, RowX + IconOffX, y + IconOffY, IconSize, IconSize), icon, ScaleMode.StretchToFill);

            int lv = SkillProgress.LevelOf(d.id);
            GUI.Label(At(r, NameX, y + NameY, NameW, NameH), d.name, Style(12, Ink, TextAnchor.MiddleLeft));
            int cap = SkillBook.MasterCapOf(d.id);
            GUI.Label(At(r, LevelX, y + LevelY, LevelW, LevelH),
                page == 1 ? lv + " / " + cap : lv + " / " + cap, Style(12, Ink, TextAnchor.MiddleLeft));

            var line = Wz(Root + "line");
            if (line != null) GUI.DrawTexture(At(r, RowX, y + 37, RowW, 1), line, ScaleMode.StretchToFill);

            // 只有行上的 BtSpUp 按钮才算选中（0x8b2f6d 只处理按钮 id，点行无效）。
            var plus = At(r, SpUpX, y + SpUpY, SpUpSize, SpUpSize);
            bool over = plus.Contains(Pointer.Pos);
            string state = over ? (Pointer.Down ? "pressed" : "mouseOver") : "normal";
            var up = Wz("UI/UIWindow.img/Skill/BtSpUp/" + state + "/0");
            if (up != null) GUI.DrawTexture(plus, up, ScaleMode.StretchToFill);
            if (over && Event.current.type == EventType.Repaint && Pointer.Pressed)
            {
                if (page == 1) { fromId = d.id; toId = 0; scroll = 0; stage = 2; }
                else { toId = d.id; stage = 3; }
                return;
            }
        }
    }

    static Rect ScrollTrack(Rect r) => At(r, ScrollX, ScrollTop + ScrollW, ScrollW,
        Mathf.Max(0f, ScrollBottom - ScrollTop - ScrollW * 2));

    static Rect ScrollThumb(Rect r, List<SkillData> skills)
    {
        var track = ScrollTrack(r);
        if (skills.Count <= Rows) return track;
        float h = Mathf.Max(24f * S, track.height * (float)Rows / skills.Count);
        int max = Mathf.Max(1, skills.Count - Rows);
        float t = Mathf.Clamp01((float)scroll / max);
        return new Rect(track.x, track.y + (track.height - h) * t, track.width, h);
    }

    static void DrawScrollbar(Rect r, List<SkillData> skills)
    {
        bool on = skills.Count > Rows;
        string st = on ? "enabled" : "disabled";
        var prev = At(r, ScrollX, ScrollTop, ScrollW, ScrollW);
        var next = At(r, ScrollX, ScrollBottom - ScrollW, ScrollW, ScrollW);
        var track = ScrollTrack(r);
        var baseTex = Wz("UI/Basic.img/VScr5/" + st + "/base");
        if (baseTex != null) GUI.DrawTexture(track, baseTex, ScaleMode.StretchToFill);
        var prevTex = Wz("UI/Basic.img/VScr5/" + st + (on ? "/prev0" : "/prev"));
        var nextTex = Wz("UI/Basic.img/VScr5/" + st + (on ? "/next0" : "/next"));
        if (prevTex != null) GUI.DrawTexture(prev, prevTex, ScaleMode.StretchToFill);
        if (nextTex != null) GUI.DrawTexture(next, nextTex, ScaleMode.StretchToFill);
        if (on)
        {
            var thumb = Wz("UI/Basic.img/VScr5/enabled/thumb0");
            if (thumb != null) GUI.DrawTexture(ScrollThumb(r, skills), thumb, ScaleMode.StretchToFill);
        }

        if (Event.current.type != EventType.Repaint) return;
        if (on && Pointer.Pressed)
        {
            if (prev.Contains(Pointer.Pos)) { scroll = Mathf.Max(0, scroll - 1); return; }
            if (next.Contains(Pointer.Pos)) { scroll = Mathf.Min(skills.Count - Rows, scroll + 1); return; }
            var th = ScrollThumb(r, skills);
            if (th.Contains(Pointer.Pos)) { scrollDrag = true; scrollGrab = Pointer.Pos.y - th.y; return; }
        }
        if (scrollDrag && Pointer.Down)
        {
            var tr = ScrollTrack(r);
            float h = ScrollThumb(r, skills).height;
            float span = Mathf.Max(1f, tr.height - h);
            float t = Mathf.Clamp01((Pointer.Pos.y - scrollGrab - tr.y) / span);
            scroll = Mathf.RoundToInt(t * Mathf.Max(0, skills.Count - Rows));
        }
        if (!Pointer.Down) scrollDrag = false;

        // 列表区滚轮
        var listRect = At(r, RowX, RowY, RowW + ScrollW, Rows * RowStep);
        if (listRect.Contains(Pointer.Pos) && skills.Count > Rows)
        {
            float delta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) > 0.01f)
                scroll = Mathf.Clamp(scroll - (int)Mathf.Sign(delta), 0, skills.Count - Rows);
        }
    }

    static void DrawTabPlate(Rect rect, bool on)
    {
        string state = on ? "1" : "0";
        var left = Wz("UI/Basic.img/Tab2/left" + state);
        var fill = Wz("UI/Basic.img/Tab2/fill" + state);
        var right = Wz("UI/Basic.img/Tab2/right" + state);
        float edge = 4f * S;
        if (fill != null) GUI.DrawTexture(new Rect(rect.x + edge, rect.y, rect.width - edge * 2, rect.height), fill);
        if (left != null) GUI.DrawTexture(new Rect(rect.x, rect.y, edge, rect.height), left);
        if (right != null) GUI.DrawTexture(new Rect(rect.xMax - edge, rect.y, edge, rect.height), right);
    }

    // —— 确认页：backgrnd3 175x230，两组摘要（来源/目标）+ 底部 OK/Cancel ——
    static void DrawConfirm(Rect r)
    {
        Image(r, Root + "backgrnd3", 0, 0);
        if (NativeUi.Button(At(r, 155, 6, 12, 12), "UI/Basic.img/BtClose")) { Close(); return; }

        var from = SkillBook.Get(fromId);
        var to = SkillBook.Get(toId);
        DrawGroup(r, 0, from, SkillProgress.LevelOf(fromId), SkillProgress.LevelOf(fromId) - 1);
        DrawGroup(r, 1, to, SkillProgress.LevelOf(toId), SkillProgress.LevelOf(toId) + 1);

        // 0x8b5889 汇编常量是 BtOK2 @(87,207)、BtCancel2 @(129,207)，但 47 宽按钮只差 42 会重叠 5px
        // （实拍证实两钮叠成一个橙块）。按 AP 确认页同构间距（BtOK2 70 / BtCancel2 119，差 49）修正，
        // y=207 保留汇编值；x 的原端真实值待进一步逆向（可能在写回时另有偏移）。
        if (NativeUi.Button(At(r, 70, 207, 47, 19), "UI/Basic.img/BtOK2")) Confirm();
        if (NativeUi.Button(At(r, 119, 207, 47, 19), "UI/Basic.img/BtCancel2")) { Close(); return; }
    }

    static void DrawGroup(Rect r, int group, SkillData d, int lvBefore, int lvAfter)
    {
        if (d == null) return;
        int top = GroupLine[group] + 1;   // 红线下内容区
        var icon = string.IsNullOrEmpty(d.icon) ? null : Art.Tex(d.icon);
        if (icon != null)
            GUI.DrawTexture(At(r, 9, top + 1, 34, 28), icon, ScaleMode.ScaleToFit);
        GUI.Label(At(r, 46, top + 1, 122, 14), d.name, Style(12, Ink, TextAnchor.MiddleLeft));
        GUI.Label(At(r, 46, top + 15, 122, 14),
            "Lv." + lvBefore + " → Lv." + lvAfter, Style(12, Ink, TextAnchor.MiddleLeft));
    }

    // 确认：服务端语义（UseCashItemHandler 175..235 SP 分支）——先 SPTo 后 SPFrom 读包，
    // canSPAssign(SPTo)、目标 curLevel<maxLevel、来源 curLevel>0，双向各 ±1，最后才扣道具。
    static void Confirm()
    {
        int source = fromId, target = toId, position = itemPosition, id = itemId;
        Close();
        string error = Profile.ResetSp(source, target);
        if (error != null) { BottomBar.Toast(error); return; }
        Profile.RemoveItem(id, 1, position);
    }
}
