using System.Collections.Generic;
using UnityEngine;

// 原版技能窗：UIWindow.img/Skill (175×289)，右接 SkillMacro (207×289)。
// 坐标依据 BeiDou.exe 0x8AAC69 / 0x8AAD3C / 0x8A8504 / 0x8A8926，
// 以及原端新手截图；所有窗框、页签和控件直接读取原 IMG，不生成近似贴图。
public static class SkillWindow
{
    public static bool Visible;

    static float S => BottomBar.S;
    static Vector2 pos = new Vector2(-1f, -1f);
    static int tab;
    static int selected = -1;   // -1 = 未选中任何技能行（原版打开时没有高亮行）
    static int scroll;

    const int WinW = 175, WinH = 289;

    // 页签条：backgrnd 的白区 y=22..41，页签底板是 Basic.img/Tab2 的左右边和1px平铺中段
    const int TabY = 23, TabH = 19, TabX = 3, TabW = 34;
    const int TabGlyphY = 4;

    // 书条：橙框 y=56..86，左侧是职业书图标 Skill/<job>.img/info/icon（26x30）
    const int BookX = 10, BookY = 55, BookW = 26, BookH = 30;
    const int BookTitleX = 45, BookTitleY = 61, BookTitleW = 123, BookTitleH = 22;

    const int RowX = 7, RowY = 99, RowStep = 40, RowW = 141, RowH = 35, Rows = 4;
    const int RowHitW = 154, RowHitH = 38;
    const int IconOffX = 1, IconOffY = 1, IconSize = 32;
    const int NameX = 45, NameY = 0, NameW = 100, NameH = 14;
    const int LevelX = 45, LevelY = 20, LevelW = 60, LevelH = 12;
    const int SpUpX = 131, SpUpY = 20, SpUpSize = 12;

    // 列表右侧滚动条（Basic.img/VScr4，15 宽）
    const int ScrollX = 153, ScrollW = 15, ScrollTop = 99, ScrollBottom = 254;

    // 底栏：白色数字框 (57,265)-(115,282)，右侧是「宏指令」按钮
    const int FooterX = 58, FooterY = 265, FooterW = 52, FooterH = 17;
    const int MacroX = 120, MacroY = 265, MacroW = 48, MacroH = 18;

    const int RecOffX = 36, RecW = 105, RecH = 35;

    static readonly Color HeaderText = Color.white;
    static readonly Color NameOn = new Color(0.10f, 0.11f, 0.15f);
    static readonly Color NameOff = new Color(0.10f, 0.11f, 0.15f);
    static readonly Color LevelText = new Color(0.10f, 0.11f, 0.15f);
    static readonly Color SpNumber = new Color(0.10f, 0.23f, 0.72f);
    static readonly Color SpZero = new Color(0.10f, 0.11f, 0.15f);

    static Texture2D Tex(string name)
        => Wz("UI/UIWindow.img/Skill/" + (name.StartsWith("spup")
            ? "BtSpUp/" + (name == "spup" ? "normal" : name.Substring(5)) + "/0"
            : name.StartsWith("cool_") ? "CoolTime/" + name.Substring(5)
            : name == "recommend" ? "recommend/0" : name));

    // 页签底板 / 滚动条 / 宏指令按钮都直接用原 IMG 节点，不再单独导出 PNG
    static Texture2D Wz(string node)
        => Art.Tex("wz://" + node);

    static Texture2D SkillIcon(SkillData d)
        => d == null || string.IsNullOrEmpty(d.icon) ? null : Art.Tex(d.icon);

    static Rect At(Rect win, float x, float y, float w, float h)
        => new Rect(win.x + x * S, win.y + y * S, w * S, h * S);

    static GUIStyle Style(float size, Color color, TextAnchor anchor, bool wrap = false, bool clip = false)
    {
        var st = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(7, Mathf.RoundToInt(size * S)),
            alignment = anchor,
            wordWrap = wrap,
            clipping = clip ? TextClipping.Clip : TextClipping.Overflow,
        };
        st.normal.textColor = color;
        return st;
    }

    public static void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            StatWindow.Visible = false;
        }
        else
        {
            CancelDrag();
        }
    }

    static Dictionary<string, object> Line => SkillBook.LineFor(Profile.cls);
    static List<Dictionary<string, object>> Tabs => SkillBook.Tabs(Line);

    static List<int> TabSkills()
    {
        var tabs = Tabs;
        if (tabs.Count == 0)
        {
            tab = 0;
            return new List<int>();
        }
        tab = Mathf.Clamp(tab, 0, tabs.Count - 1);
        var list = new List<int>();
        if (tabs[tab].TryGetValue("skills", out var raw))
            foreach (var o in (List<object>)raw) list.Add(int.Parse(o.ToString()));
        return list;
    }

    static void ClampState(List<int> list)
    {
        if (list.Count == 0) { selected = -1; scroll = 0; return; }
        scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, list.Count - Rows));
        if (selected >= list.Count) selected = list.Count - 1;
    }

    public static void Draw()
    {
        // 截图/调试：?skill=1 或 -smokeSkill 1 直接展开技能窗，只影响本次运行
        if (!Visible && PlayerPrefs.GetInt("debug_skillwin", 0) == 1)
        {
            PlayerPrefs.DeleteKey("debug_skillwin");
            Visible = true;
        }
        if (!Visible) { CancelDrag(); return; }      // 任何关窗路径都要把手上的技能收回去
        if (GameLoop.I == null || GameLoop.I.player == null) return;
        if (pos.x < 0f) pos = new Vector2(18f, 70f);

        var win = new Rect(pos.x * S, pos.y * S, WinW * S, WinH * S);

        HandleWindowDrag(win);

        var tabs = Tabs;
        var list = TabSkills();

        HandleTabInput(win, tabs);
        list = TabSkills();

        HandleScroll(win, list);
        HandleScrollbar(win, list);
        ClampState(list);

        int hover = RowAt(win, list);
        // 手上有技能时，这次按下按「放下」处理：落到目标就结束；
        // 没落到目标只是取消，还要继续走单击拾起 / 双击施放，否则双击会被吃掉。
        if (Dragging && Pointer.Pressed)
        {
            bool placed = DropOnSlot(dragId);
            CancelDrag();
            if (placed) return;
        }
        HandleRows(win, list);
        HandleHints(win, list, hover);
        UpdateCursor(win, list, hover);

        DrawMacro();
        DrawBackground(win);
        if (NativeUi.Button(At(win, 156, 5, 12, 12), "UI/Basic.img/BtClose")) { Visible = false; CancelDrag(); return; }
        DrawTabs(win, tabs);
        DrawHeader(win);
        DrawRows(win, list, hover);
        DrawScrollbar(win, list);
        DrawFooter(win);
        DrawTooltip(win, list, hover);
        DrawTip();
        DrawDragGhost();
    }

    // —— 窗口拖动 ——
    static bool dragging;
    static Vector2 dragOffset;

    static void HandleWindowDrag(Rect win)
    {
        var title = At(win, 0f, 0f, WinW, 22f);
        if (Event.current.type == EventType.Repaint && Pointer.Pressed && title.Contains(Pointer.Pos))
        {
            dragging = true;
            dragOffset = Pointer.Pos / S - pos;
        }
        if (dragging && Pointer.Down)
        {
            pos = Pointer.Pos / S - dragOffset;
            pos.x = Mathf.Clamp(pos.x, -40f, UiKit.W / S - 60f);
            pos.y = Mathf.Clamp(pos.y, 0f, UiKit.H / S - 80f);
        }
        if (!Pointer.Down) dragging = false;
    }

    // 原端 CCtrlTab：x=3、y=23、单页宽34；页签连续排列，保留六阶页签空间。
    static float TabLeft(int i, int n) => TabX + i * Mathf.Min(TabW, 168f / Mathf.Max(1, n));

    static void HandleTabInput(Rect win, List<Dictionary<string, object>> tabs)
    {
        if (Event.current.type != EventType.Repaint || !Pointer.Pressed) return;
        for (int i = 0; i < tabs.Count && i < 6; i++)
        {
            var hit = At(win, TabLeft(i, tabs.Count), TabY, TabW, TabH);
            if (!hit.Contains(Pointer.Pos)) continue;
            if (tab != i)
            {
                tab = i;
                selected = -1;
                scroll = 0;
                CancelDrag();
            }
            return;
        }
    }

    static void DrawBackground(Rect win)
    {
        var bg = Tex("backgrnd");
        if (bg != null) GUI.DrawTexture(win, bg, ScaleMode.StretchToFill);
    }

    static void DrawTabPlate(Rect r, bool on)
    {
        string state = on ? "1" : "0";
        var left = Wz("UI/Basic.img/Tab2/left" + state);
        var fill = Wz("UI/Basic.img/Tab2/fill" + state);
        var right = Wz("UI/Basic.img/Tab2/right" + state);
        float edge = 4f * S;
        if (fill != null) GUI.DrawTexture(new Rect(r.x + edge, r.y, r.width - edge * 2, r.height), fill);
        if (left != null) GUI.DrawTexture(new Rect(r.x, r.y, edge, r.height), left);
        if (right != null) GUI.DrawTexture(new Rect(r.xMax - edge, r.y, edge, r.height), right);
    }

    static void DrawTabs(Rect win, List<Dictionary<string, object>> tabs)
    {
        for (int i = 0; i < tabs.Count && i < 6; i++)
        {
            bool on = i == tab;
            var plate = At(win, TabLeft(i, tabs.Count), TabY, TabW, TabH);
            DrawTabPlate(plate, on);

            var glyph = Wz("UI/UIWindow.img/Skill/Tab/" + (on ? "enabled" : "disabled") + "/" + i);
            if (glyph == null) continue;
            GUI.DrawTexture(new Rect(plate.x + (plate.width - glyph.width * S) * 0.5f,
                                     plate.y + TabGlyphY * S, glyph.width * S, glyph.height * S),
                glyph, ScaleMode.StretchToFill);
        }
    }

    // —— 书条：职业书图标 + 技能页书名（String.wz 的 bookName）——
    static void DrawHeader(Rect win)
    {
        var tabs = Tabs;
        if (tab < 0 || tab >= tabs.Count) return;
        var t = tabs[tab];

        // 原版这里的书就是 Skill.wz/<job>.img/info/icon（26x30）
        int job = Art.Int(t, "job", 0);
        var book = Art.Tex("wz://Skill/" + job.ToString("D3") + ".img/info/icon");
        if (book != null)
            GUI.DrawTexture(At(win, BookX, BookY, BookW, BookH), book, ScaleMode.StretchToFill);

        string name = t.TryGetValue("book", out var b) ? b as string : null;
        if (string.IsNullOrEmpty(name))
            name = t.TryGetValue("name", out var n) ? n as string : "";
        GUI.Label(At(win, BookTitleX, BookTitleY, BookTitleW, BookTitleH), name,
            Style(12, HeaderText, TextAnchor.MiddleCenter, false, true));
    }

    // —— 列表 ——
    static int RowAt(Rect win, List<int> list)
    {
        for (int i = 0; i < Rows; i++)
        {
            int index = scroll + i;
            if (index >= list.Count) break;
            float y = RowY + i * RowStep;
            var hit = At(win, RowX, y, RowHitW, RowHitH);
            if (hit.Contains(Pointer.Pos)) return index;
        }
        return -1;
    }

    // 光标：悬停在能拿起的技能行上 = 张开手（原图 Cursor/11），手上拿着 = 握拳（Cursor/12）。
    // 和键位窗同一套：源端 DragStart 之后图标跟着光标，直到下一次点击落下。
    static void UpdateCursor(Rect win, List<int> list, int hover)
    {
        if (carrying && dragId != 0) { NativeCursor.Request(NativeCursor.StateHold); return; }
        if (Pointer.Down || Pointer.RightDown) return;
        if (hover < 0 || hover >= list.Count) return;
        var d = SkillBook.Get(list[hover]);
        if (d == null || SkillProgress.LevelOf(d.id) <= 0) return;
        NativeCursor.Request(NativeCursor.StateGrab);
    }

    static void HandleScroll(Rect win, List<int> list)
    {
        if (Event.current.type != EventType.Repaint) return;
        var listRect = At(win, RowX, 87f, 161f, 169f);
        if (!listRect.Contains(Pointer.Pos)) return;
        float delta = Input.mouseScrollDelta.y;
        if (Mathf.Abs(delta) < 0.01f) return;
        int max = Mathf.Max(0, list.Count - Rows);
        scroll = Mathf.Clamp(scroll - (int)Mathf.Sign(delta), 0, max);
    }

    static void HandleRows(Rect win, List<int> list)
    {
        if (Event.current.type != EventType.Repaint) return;

        // 双击 = 直接施放（原版技能窗里双击技能就是这么用的）。
        // 必须放在单击处理之前：双击的第二下也会产生 Pressed，先判 DoubleClicked
        // 才能既不重新起拖、也不会把第二下当成"拾起"。
        if (Pointer.DoubleClicked)
        {
            for (int i = 0; i < Rows; i++)
            {
                int index = scroll + i;
                if (index >= list.Count) break;
                float y = RowY + i * RowStep;
                if (!At(win, RowX, y, RowHitW, RowHitH).Contains(Pointer.Pos)) continue;
                var hit = SkillBook.Get(list[index]);
                selected = index;
                CancelDrag();
                if (hit == null) return;
                // 连点「+」是连续加点，不能被当成双击施放
                if (At(win, SpUpX, y + SpUpY, SpUpSize, SpUpSize).Contains(Pointer.Pos))
                {
                    SkillProgress.Raise(hit);
                    return;
                }
                string why;
                SkillCast.TryCast(hit.id, out why);
                return;
            }
            CancelDrag();
            return;
        }

        if (!Pointer.Pressed) return;

        for (int i = 0; i < Rows; i++)
        {
            int index = scroll + i;
            if (index >= list.Count) break;
            var d = SkillBook.Get(list[index]);
            if (d == null) continue;

            float y = RowY + i * RowStep;
            var hitRect = At(win, RowX, y, RowHitW, RowHitH);
            if (!hitRect.Contains(Pointer.Pos)) continue;

            var plus = At(win, SpUpX, y + SpUpY, SpUpSize, SpUpSize);
            selected = index;

            if (plus.Contains(Pointer.Pos))
            {
                CancelDrag();
                SkillProgress.Raise(d);
                return;
            }
            // 整行都能拿起（原版是拖图标，但手机上 32px 图标太难点中）。
            // 按下即 DragStart，不等位移阈值、也不等松手——源端没有松开分支。
            if (SkillProgress.LevelOf(d.id) > 0)
            {
                dragId = d.id;
                carrying = true;
            }
            return;
        }
    }

    static void HandleHints(Rect win, List<int> list, int hover)
    {
        if (!string.IsNullOrEmpty(tipName) && Time.unscaledTime < tipUntil) return;

        if (At(win, FooterX, FooterY, FooterW, FooterH).Contains(Pointer.Pos))
        {
            ShowTip("tip2");
            return;
        }

        int i = hover - scroll;
        if (i < 0 || i >= Rows || hover >= list.Count) return;
        float y = RowY + i * RowStep;
        if (At(win, RowX + IconOffX, y + IconOffY, IconSize, IconSize).Contains(Pointer.Pos))
            ShowTip("tip0");
        else if (At(win, SpUpX, y + SpUpY, SpUpSize, SpUpSize).Contains(Pointer.Pos))
            ShowTip("tip1");
    }

    static void DrawRows(Rect win, List<int> list, int hover)
    {
        var sys = GameLoop.I.player != null ? GameLoop.I.player.GetComponent<SkillSystem>() : null;
        int recId = RecommendedId();

        for (int i = 0; i < Rows; i++)
        {
            int index = scroll + i;
            if (index >= list.Count) break;
            var d = SkillBook.Get(list[index]);
            if (d == null) continue;

            float y = RowY + i * RowStep;
            var row = At(win, RowX, y, RowW, RowH);
            // 原端新手四行均为蓝色 skill1；skill0 不是普通行/悬停态。
            var bg = Tex("skill1");
            if (bg != null) GUI.DrawTexture(row, bg, ScaleMode.StretchToFill);

            if (recId == d.id)
            {
                var rec = Tex("recommend");
                if (rec != null)
                    GUI.DrawTexture(At(win, RowX + RecOffX, y, RecW, RecH), rec, ScaleMode.StretchToFill);
            }

            var iconRect = At(win, RowX + IconOffX, y + IconOffY, IconSize, IconSize);
            var icon = SkillIcon(d);
            if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.StretchToFill);

            if (sys != null)
            {
                float left = sys.RemainingById(d.id);
                if (left > 0.001f)
                {
                    float total = Mathf.Max(0.01f, sys.CooldownOf(d.id));
                    int frame = Mathf.Clamp(Mathf.RoundToInt((1f - Mathf.Clamp01(left / total)) * 15f), 0, 15);
                    var cool = Tex("cool_" + frame);
                    if (cool != null) GUI.DrawTexture(iconRect, cool, ScaleMode.StretchToFill);
                }
            }

            int lv = SkillProgress.LevelOf(d.id);
            // 原版行内只有「技能名 + 等级数字」，没有 "Lv." 前缀
            var nameStyle = Style(12f, lv > 0 ? NameOn : NameOff, TextAnchor.MiddleLeft, false, true);
            var lvStyle = Style(12f, LevelText, TextAnchor.MiddleLeft, false, true);
            GUI.Label(At(win, NameX, y + NameY, NameW, NameH), d.name, nameStyle);
            GUI.Label(At(win, LevelX, y + LevelY, LevelW, LevelH), lv.ToString(), lvStyle);

            var plusRect = At(win, SpUpX, y + SpUpY, SpUpSize, SpUpSize);
            bool can = SkillProgress.CanRaise(d);
            bool over = plusRect.Contains(Pointer.Pos);
            bool down = over && Pointer.Down;
            string state = !can ? "spup_disabled" : down ? "spup_pressed" : over ? "spup_mouseOver" : "spup";
            var up = Tex(state);
            if (up != null) GUI.DrawTexture(plusRect, up, ScaleMode.StretchToFill);

            var line = Tex("line");
            if (line != null)
                GUI.DrawTexture(At(win, RowX, y + 37f, RowW, 1f), line, ScaleMode.StretchToFill);
        }

        if (list.Count == 0)
            WindowShell.Label(At(win, 12f, 96f, 151f, 18f), "这条职业线没有技能数据", 11,
                new Color(0.35f, 0.38f, 0.45f));
    }

    // —— 底栏：技能点 + 宏指令 ——
    static void DrawFooter(Rect win)
    {
        int sp = SkillProgress.AvailableSp;
        var st = Style(12f, sp > 0 ? SpNumber : SpZero, TextAnchor.MiddleRight);
        GUI.Label(At(win, FooterX, FooterY, FooterW, FooterH), sp.ToString(), st);

        if (NativeUi.Button(At(win, MacroX, MacroY, MacroW, MacroH), "UI/Basic.img/BtMacro"))
            macroVisible = !macroVisible;
    }

    // —— 技能组合栏：原端 0x8A8504 控件坐标、0x8A8926 绘制坐标 ——
    // 右窗在左窗下面绘制，重叠2px，左窗边框盖住右窗左侧阴影。
    static bool macroVisible = true;
    static int macroScroll, macroSelected = -1;
    static bool macroLoaded, macroDirty;
    static readonly int[,] macroSkills = new int[5, 3];
    static readonly string[] macroNames = new string[5];
    static readonly bool[] macroShowName = new bool[5];
    static Rect MacroRect => new Rect((pos.x + WinW - 2) * S, pos.y * S, 207 * S, WinH * S);

    static void MacroImage(Rect win, string node, float x, float y)
    {
        var tex = Wz("UI/UIWindow.img/SkillMacro/" + node);
        if (tex != null) GUI.DrawTexture(At(win, x, y, tex.width, tex.height), tex);
    }

    static void LoadMacros()
    {
        if (macroLoaded) return;
        macroLoaded = true;
        for (int i = 0; i < 5; i++)
        {
            macroNames[i] = PlayerPrefs.GetString("mxd.macro." + i + ".name", "");
            macroShowName[i] = PlayerPrefs.GetInt("mxd.macro." + i + ".show", 0) != 0;
            for (int j = 0; j < 3; j++)
                macroSkills[i, j] = PlayerPrefs.GetInt("mxd.macro." + i + ".skill." + j, 0);
        }
    }

    static void DrawMacro()
    {
        if (!macroVisible) return;
        LoadMacros();
        var win = MacroRect;
        MacroImage(win, "backgrnd", 0, 0);
        MacroImage(win, "line01", 17, 49);
        MacroImage(win, "line02", 17, 188);
        MacroImage(win, "macroname", 17, 195);
        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;
        for (int i = 0; i < 3; i++)
        {
            int index = macroScroll + i;
            int y = 53 + i * 45;
            if (macroSelected == index) MacroImage(win, "macroslot2", 15, y - 2);
            MacroImage(win, "macroslot", 17, y);
            MacroImage(win, "macroslot1", 143, y);
            MacroImage(win, "Macroicon/" + index + "/icon", 146, y + 3);
            for (int j = 0; j < 3; j++)
            {
                var rect = At(win, 20 + j * 37, y + 3, 32, 32);
                var icon = SkillIcon(SkillBook.Get(macroSkills[index, j]));
                if (icon != null) GUI.DrawTexture(rect, icon);
                if (Event.current.type == EventType.Repaint && Pointer.RightPressed && rect.Contains(Pointer.Pos))
                {
                    macroSkills[index, j] = 0;
                    macroSelected = index;
                    macroDirty = true;
                }
            }
            if (press && At(win, 17, y, 164, 38).Contains(Pointer.Pos)) macroSelected = index;
        }

        // 原端主题3使用 VScr4，滚动范围5组、可见3组。
        var prev = At(win, 185, 54, 15, 15);
        var next = At(win, 185, 164, 15, 15);
        var track = At(win, 185, 69, 15, 95);
        var thumb = At(win, 185, 69 + macroScroll * 35, 15, 25);
        string root = "UI/Basic.img/VScr4/enabled/";
        GUI.DrawTexture(track, Wz(root + "base"));
        GUI.DrawTexture(thumb, Wz(root + "thumb0"));
        GUI.DrawTexture(prev, Wz(root + (prev.Contains(Pointer.Pos) ? Pointer.Down ? "prev2" : "prev1" : "prev0")));
        GUI.DrawTexture(next, Wz(root + (next.Contains(Pointer.Pos) ? Pointer.Down ? "next2" : "next1" : "next0")));
        if (press && prev.Contains(Pointer.Pos)) macroScroll = Mathf.Max(0, macroScroll - 1);
        if (press && next.Contains(Pointer.Pos)) macroScroll = Mathf.Min(2, macroScroll + 1);
        if (Event.current.type == EventType.Repaint && At(win, 17, 53, 183, 126).Contains(Pointer.Pos))
        {
            float delta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) > 0.01f) macroScroll = Mathf.Clamp(macroScroll - (int)Mathf.Sign(delta), 0, 2);
            if (press && track.Contains(Pointer.Pos))
                macroScroll = Mathf.Clamp(Mathf.RoundToInt((Pointer.Pos.y / S - pos.y - 81.5f) / 35f), 0, 2);
        }

        var nameRect = At(win, 59, 215, 117, 16);
        // 输入框本身是原端控件绘制的白底，不覆盖 macroname 内的原版文字。
        GUI.DrawTexture(nameRect, Texture2D.whiteTexture);
        if (macroSelected >= 0)
        {
            MacroImage(win, "Macroicon/" + macroSelected + "/icon", 20, 198);
            var style = Style(12, Color.black, TextAnchor.MiddleLeft, false, true);
            string name = GUI.TextField(nameRect, macroNames[macroSelected], 12, style);
            if (name != macroNames[macroSelected]) { macroNames[macroSelected] = name; macroDirty = true; }
        }
        var check = At(win, 169, 243, 12, 12);
        bool show = macroSelected >= 0 && macroShowName[macroSelected];
        GUI.DrawTexture(check, Wz("UI/Basic.img/CheckBox/" + (show ? "1" : "0")));
        if (press && macroSelected >= 0 && check.Contains(Pointer.Pos))
        {
            macroShowName[macroSelected] = !show;
            macroDirty = true;
        }
        var ok = At(win, 154, 265, 47, 19);
        if (!macroDirty) GUI.DrawTexture(ok, Wz("UI/Basic.img/BtOK2/disabled/0"));
        else if (NativeUi.Button(ok, "UI/Basic.img/BtOK2"))
        {
            for (int i = 0; i < 5; i++)
            {
                PlayerPrefs.SetString("mxd.macro." + i + ".name", macroNames[i]);
                PlayerPrefs.SetInt("mxd.macro." + i + ".show", macroShowName[i] ? 1 : 0);
                for (int j = 0; j < 3; j++)
                    PlayerPrefs.SetInt("mxd.macro." + i + ".skill." + j, macroSkills[i, j]);
            }
            PlayerPrefs.Save();
            macroDirty = false;
        }
    }

    static bool DropOnMacro(int id)
    {
        if (!macroVisible) return false;
        var d = SkillBook.Get(id);
        if (d == null || d.type == "passive" || SkillProgress.LevelOf(id) <= 0) return false;
        LoadMacros();
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (At(MacroRect, 20 + j * 37, 56 + i * 45, 32, 32).Contains(Pointer.Pos))
                {
                    macroSelected = macroScroll + i;
                    macroSkills[macroSelected, j] = id;
                    macroDirty = true;
                    return true;
                }
        return false;
    }

    // —— 右侧滚动条（Basic.img/VScr4）——
    static bool scrollDrag;
    static float scrollDragGrab;

    static Rect ScrollPrevRect(Rect win) => At(win, ScrollX, ScrollTop, ScrollW, ScrollW);
    static Rect ScrollNextRect(Rect win) => At(win, ScrollX, ScrollBottom - ScrollW, ScrollW, ScrollW);
    static Rect ScrollTrackRect(Rect win)
        => At(win, ScrollX, ScrollTop + ScrollW, ScrollW, Mathf.Max(0f, ScrollBottom - ScrollTop - ScrollW * 2));

    static Rect ScrollThumbRect(Rect win, List<int> list)
    {
        var track = ScrollTrackRect(win);
        if (list.Count <= Rows) return track;
        float h = Mathf.Max(24f * S, track.height * (float)Rows / list.Count);
        float maxScroll = Mathf.Max(1, list.Count - Rows);
        float t = Mathf.Clamp01((float)scroll / maxScroll);
        return new Rect(track.x, track.y + (track.height - h) * t, track.width, h);
    }

    static void HandleScrollbar(Rect win, List<int> list)
    {
        if (list.Count <= Rows) { scrollDrag = false; return; }
        int max = Mathf.Max(0, list.Count - Rows);

        if (Event.current.type == EventType.Repaint && Pointer.Pressed)
        {
            if (ScrollPrevRect(win).Contains(Pointer.Pos)) { scroll = Mathf.Max(0, scroll - 1); return; }
            if (ScrollNextRect(win).Contains(Pointer.Pos)) { scroll = Mathf.Min(max, scroll + 1); return; }
            var thumb = ScrollThumbRect(win, list);
            if (thumb.Contains(Pointer.Pos))
            {
                scrollDrag = true;
                scrollDragGrab = Pointer.Pos.y - thumb.y;
                return;
            }
        }

        if (scrollDrag && Pointer.Down)
        {
            var track = ScrollTrackRect(win);
            float h = ScrollThumbRect(win, list).height;
            float span = Mathf.Max(1f, track.height - h);
            float t = Mathf.Clamp01((Pointer.Pos.y - scrollDragGrab - track.y) / span);
            scroll = Mathf.RoundToInt(t * max);
        }
        if (!Pointer.Down) scrollDrag = false;
    }

    static void DrawScrollbar(Rect win, List<int> list)
    {
        bool on = list.Count > Rows;
        string state = on ? "enabled" : "disabled";
        var baseTex = Wz("UI/Basic.img/VScr4/" + state + "/base");
        var prev = Wz("UI/Basic.img/VScr4/" + state + (on ? "/prev0" : "/prev"));
        var next = Wz("UI/Basic.img/VScr4/" + state + (on ? "/next0" : "/next"));
        var thumb = on ? Wz("UI/Basic.img/VScr4/enabled/thumb0") : null;

        var track = ScrollTrackRect(win);
        if (baseTex != null) GUI.DrawTexture(track, baseTex, ScaleMode.StretchToFill);
        if (prev != null) GUI.DrawTexture(ScrollPrevRect(win), prev, ScaleMode.StretchToFill);
        if (next != null) GUI.DrawTexture(ScrollNextRect(win), next, ScaleMode.StretchToFill);
        if (on && thumb != null) GUI.DrawTexture(ScrollThumbRect(win, list), thumb, ScaleMode.StretchToFill);
    }

    // —— 首次操作提示（tip0..2，各自只出现一次） ——
    static string tipName;
    static float tipUntil;

    static void ShowTip(string name)
    {
        if (!string.IsNullOrEmpty(tipName) && Time.unscaledTime < tipUntil) return;
        if (PlayerPrefs.GetInt("mxd.skill." + name, 0) == 1) return;
        PlayerPrefs.SetInt("mxd.skill." + name, 1);
        PlayerPrefs.Save();
        tipName = name;
        tipUntil = Time.unscaledTime + 4f;
    }

    static void DrawTip()
    {
        if (string.IsNullOrEmpty(tipName)) return;
        if (Time.unscaledTime >= tipUntil) { tipName = null; return; }
        var tex = Tex(tipName);
        if (tex == null) { tipName = null; return; }

        float w = tex.width * S, h = tex.height * S;
        float x = Pointer.Pos.x + 12f * S;
        float y = Pointer.Pos.y + 14f * S;
        if (x + w > UiKit.W - 4f) x = Pointer.Pos.x - w - 12f * S;
        if (y + h > UiKit.H - 4f) y = Pointer.Pos.y - h - 14f * S;
        x = Mathf.Clamp(x, 4f, Mathf.Max(4f, UiKit.W - w - 4f));
        y = Mathf.Clamp(y, 4f, Mathf.Max(4f, UiKit.H - h - 4f));

        GUI.DrawTexture(new Rect(x, y, w, h), tex, ScaleMode.StretchToFill);
    }

    // —— 悬停说明 ——
    static void DrawTooltip(Rect win, List<int> list, int hover)
    {
        // 手上有技能时不弹说明：图标正跟着光标走，说明框会挡住落点
        if (hover < 0 || hover >= list.Count || carrying) return;
        if (tipName == "tip0" && Time.unscaledTime < tipUntil) return;

        int i = hover - scroll;
        if (i < 0 || i >= Rows) return;
        var iconRect = At(win, RowX + IconOffX, RowY + i * RowStep + IconOffY, IconSize, IconSize);
        if (!iconRect.Contains(Pointer.Pos)) return;

        var d = SkillBook.Get(list[hover]);
        if (d == null) return;
        DrawSkillData(d, Pointer.Pos);
    }

    static void DrawSkillData(SkillData d, Vector2 mouse)
    {
        int lv = SkillProgress.LevelOf(d.id);
        var row = d.Level(Mathf.Max(1, lv));
        string desc = CleanDesc(d.desc);
        var stats = StatLines(row);

        float w = 176f * S;
        float pad = 8f * S;
        float inner = w - pad * 2f;

        var titleStyle = Style(12f, new Color(1f, 0.93f, 0.62f), TextAnchor.UpperLeft, false, false);
        var levelStyle = Style(9f, new Color(0.62f, 0.78f, 1f), TextAnchor.UpperLeft);
        var bodyStyle = Style(10f, new Color(0.84f, 0.87f, 0.94f), TextAnchor.UpperLeft, true);
        var statStyle = Style(9f, new Color(0.76f, 0.82f, 0.92f), TextAnchor.UpperLeft);

        float titleH = 16f * S;
        float lvH = 13f * S;
        float descH = string.IsNullOrEmpty(desc) ? 0f : bodyStyle.CalcHeight(new GUIContent(desc), inner);
        float statH = stats.Count * 13f * S;
        float h = pad + titleH + lvH + (descH > 0f ? 5f * S + descH : 0f) +
                  (stats.Count > 0 ? 5f * S + statH : 0f) + pad;

        float x = mouse.x + 16f * S;
        float y = mouse.y + 14f * S;
        if (x + w > UiKit.W - 4f) x = mouse.x - w - 12f * S;
        if (y + h > UiKit.H - 4f) y = mouse.y - h - 12f * S;
        x = Mathf.Clamp(x, 4f, Mathf.Max(4f, UiKit.W - w - 4f));
        y = Mathf.Clamp(y, 4f, Mathf.Max(4f, UiKit.H - h - 4f));


        NativeUi.Panel(new Rect(x, y, w, h));
        titleStyle.normal.textColor = levelStyle.normal.textColor = bodyStyle.normal.textColor = statStyle.normal.textColor = Color.black;
        float cy = y + pad;
        GUI.Label(new Rect(x + pad, cy, inner, titleH), d.name, titleStyle);
        cy += titleH;
        string lvLine = lv > 0 ? "技能等级 : " + lv + " / " + d.maxLevel : "最高等级 : " + d.maxLevel;
        GUI.Label(new Rect(x + pad, cy, inner, lvH), lvLine, levelStyle);
        cy += lvH;

        if (descH > 0f)
        {
            cy += 5f * S;
            GUI.Label(new Rect(x + pad, cy, inner, descH), desc, bodyStyle);
            cy += descH;
        }

        if (stats.Count > 0)
        {
            cy += 5f * S;
            cy += 2f * S;
            for (int k = 0; k < stats.Count; k++)
            {
                GUI.Label(new Rect(x + pad, cy, inner, 13f * S), stats[k], statStyle);
                cy += 13f * S;
            }
        }
    }

    static string CleanDesc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // 技能 JSON 里的换行是字面量 \n，不是真实换行；不还原会把整段描述当成一行标题丢掉
        s = s.Replace("\\n", "\n").Replace("#c", "").Replace("#", "");
        var lines = s.Split('\n');
        int start = 0;
        while (start < lines.Length && lines[start].TrimStart().StartsWith("[最高等级"))
            start++;
        var kept = new List<string>();
        for (int i = start; i < lines.Length; i++) kept.Add(lines[i]);
        return string.Join("\n", kept).Trim();
    }

    static List<string> StatLines(Dictionary<string, object> row)
    {
        var lines = new List<string>();
        if (row == null) return lines;
        AddStat(lines, row, "mpCon", "消耗 MP", "");
        AddStat(lines, row, "damage", "伤害", "%");
        AddStat(lines, row, "attackCount", "攻击次数", " 次");
        AddStat(lines, row, "mobCount", "对象数量", " 只");
        AddStat(lines, row, "time", "持续时间", " 秒");
        AddStat(lines, row, "cooltime", "冷却时间", " 秒");
        AddStat(lines, row, "pad", "物理攻击力", "");
        AddStat(lines, row, "mad", "魔法攻击力", "");
        AddStat(lines, row, "pdd", "物理防御力", "");
        AddStat(lines, row, "mdd", "魔法防御力", "");
        AddStat(lines, row, "acc", "命中率", "");
        AddStat(lines, row, "eva", "回避率", "");
        AddStat(lines, row, "speed", "移动速度", "");
        AddStat(lines, row, "jump", "跳跃力", "");
        AddStat(lines, row, "hp", "HP", "");
        AddStat(lines, row, "mp", "MP", "");
        AddStat(lines, row, "prop", "发动概率", "%");
        AddStat(lines, row, "mastery", "熟练度", "%");
        return lines;
    }

    static void AddStat(List<string> lines, Dictionary<string, object> row, string key, string label, string suffix)
    {
        if (!row.TryGetValue(key, out var raw) || raw == null) return;
        float n;
        if (!float.TryParse(raw.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out n)) return;
        string value = Mathf.Approximately(n, Mathf.Round(n)) ? Mathf.RoundToInt(n).ToString() : n.ToString("0.#");
        lines.Add(label + " : " + value + suffix);
    }

    // —— 推荐技能（Etc.wz/RecommendSkill.img，按根职业与等级阈值取一条） ——
    static Dictionary<string, object> recommend;
    static bool recommendTried;

    static int RecommendedId()
    {
        if (!recommendTried)
        {
            recommendTried = true;
            var ta = Resources.Load<TextAsset>("UI/RecommendSkill");
            if (ta != null) recommend = MiniJson.Parse(ta.text) as Dictionary<string, object>;
        }
        if (recommend == null) return 0;

        var tabs = Tabs;
        if (tabs.Count == 0) return 0;
        int job = Art.Int(tabs[0], "job", 0);
        int root = job / 100 * 100;
        if (!recommend.TryGetValue(root.ToString(), out var node)) return 0;
        var table = node as Dictionary<string, object>;
        if (table == null) return 0;

        int bestAt = -1, id = 0;
        foreach (var pair in table)
        {
            int at;
            if (!int.TryParse(pair.Key, out at) || at > Profile.level || at < bestAt) continue;
            bestAt = at;
            int.TryParse(pair.Value.ToString(), out id);
        }
        return id;
    }

    // —— 把技能放到快捷格 / 宏 / 键位 ——
    // 原端（BeiDou.exe 0x8ABA6B 技能窗、0x8A98FE 组合栏）在 WM_LBUTTONDOWN(0x201) 里就发
    // "DragStart"（字符串池 1847），两个窗口都**没有 WM_LBUTTONUP(0x202) 分支**；
    // 双击走 0x203。也就是说源端是「点一下拿起、再点一下放下」，不是按住拖。
    static int dragId;
    static bool carrying;          // 手上有技能（源端 DragStart 之后的状态）

    // 给虚拟按键层用：手上有没有技能、是哪个
    public static bool Dragging => dragId != 0 && carrying;
    public static int DragSkillId => dragId;

    static void CancelDrag()
    {
        dragId = 0;
        carrying = false;
    }

    // 手上有技能时右键 / 关窗都要能收回去
    public static void CancelSkillDrag() => CancelDrag();

    // 供自检用：手上有技能时，下一次按下 = 放下（落不到任何目标就当作取消）
    public static bool HandleDrop()
    {
        if (!Dragging || !Pointer.Pressed) return false;
        bool placed = DropOnSlot(dragId);
        CancelDrag();
        return placed;
    }

    // 自检用：不经过 IMGUI 直接把某个技能拿到手上
    public static void PickForTest(int id) { dragId = id; carrying = id != 0; }

    // 自检用：把手上的技能放到指定屏幕坐标（原端「再点一下放下」）
    public static bool DropAt(Vector2 point)
    {
        if (!Dragging) return false;
        var saved = Pointer.Pos;
        Pointer.Pos = point;
        bool placed = DropOnSlot(dragId);
        Pointer.Pos = saved;
        CancelDrag();
        return placed;
    }

    static void DrawDragGhost()
    {
        if (!Dragging) return;
        var d = SkillBook.Get(dragId);
        var icon = SkillIcon(d);
        if (icon != null)
            GUI.DrawTexture(new Rect(Pointer.Pos.x - 16f * S, Pointer.Pos.y - 16f * S, 32f * S, 32f * S),
                icon, ScaleMode.ScaleToFit);
    }

    static bool DropOnSlot(int id)
    {
        if (DropOnMacro(id)) return true;
        // 放到屏上虚拟按键：把这个按键直接变成该技能键（原版"技能拖进快捷格"的屏上等价物）。
        // 键盘设定窗口盖住按键时，落在键帽上的优先算键位绑定——视觉在上面的那个先收到。
        // 但键位窗自己也拿着功能时让位给它，一次按下只做一件事。
        bool overKeycap = KeyConfigWindow.Visible && !KeyConfigWindow.Carrying
                          && KeyConfigWindow.HitKey(Pointer.Pos) != KeyCode.None;
        if (!overKeycap && VirtualControls.TryBindSkill(Pointer.Pos, id)) return true;
        if (overKeycap && KeyConfigWindow.DropSkill(Pointer.Pos, id)) return true;
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            if (!BottomBar.SlotRect(i).Contains(Pointer.Pos)) continue;
            if (!QuickBarKeys.BindSkill(i, id)) return true;
            BottomBar.QuickCollapsed = false;
            BottomBar.Toast("已绑定到快捷格 " + (i + 1) + "（键 " + KeyMap.Label(QuickBarKeys.Of(i)) + "）");
            return true;
        }
        return false;
    }
}
