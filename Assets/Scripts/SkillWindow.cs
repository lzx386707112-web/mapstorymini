using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 技能窗（原版 K 键）。
//
// 全部布局取自 UI.wz/UIWindow.img/Skill 的原始像素坐标（175x289）：
//   backgrnd 175x289      窗框、标题、页签条、选中名暗条、底部「技能点」
//   skill0/skill1 141x35  未选中/高亮条目，条目内图标格 35 宽、y=17 是分隔线
//   line 141x1            条目下方的分隔线（y=rowY+37）
//   BtSpUp 12x12          加点按钮，四态；在条目右侧留白列（x=149）
//   Tab/enabled|disabled  0/N 页签字形（铺在 Basic.img/Tab2 的页签底板上）
//   Tab/enabled|disabled  0..N 页签字形
//   recommend 105x35      推荐技能橙色条，正好盖住条目文本区
//   CoolTime 32x32 ×16    冷却遮罩，按剩余比例取帧
//   tip0..2 193x102       首次操作提示气泡
//
// 源端命中矩形（来自 BeiDou.exe 0x8ad946）：
//   整行 (7,rowY)-(161,rowY+38)，图标 (10,rowY+3)-(42,rowY+35)
//   行 y=99/139/179/219，共 4 行；列表无可见滚动条，滚轮翻行。
// ————————————————————————————————————————————————————————————

public static class SkillWindow
{
    public static bool Visible;

    static float S => BottomBar.S;
    static Vector2 pos = new Vector2(-1f, -1f);
    static int tab;
    static int selected = -1;   // -1 = 未选中任何技能行（原版打开时没有高亮行）
    static int scroll;

    const int WinW = 175, WinH = 289;

    // 页签条：backgrnd 的白区 y=22..41，页签底板是 Basic.img/Tab2 的三切片（4/8/4 × 19）
    const int TabY = 22, TabH = 19, TabX = 2, TabW = 25;
    const int TabGlyphY = 4;

    // 书条：橙框 y=56..78，左侧是职业书图标 Skill/<job>.img/info/icon（26x30）
    const int BookX = 5, BookY = 53, BookW = 24, BookH = 28;
    const int BookTitleX = 32, BookTitleY = 57, BookTitleW = 138, BookTitleH = 20;

    const int RowX = 7, RowY = 99, RowStep = 40, RowW = 141, RowH = 35, Rows = 4;
    const int RowHitW = 154, RowHitH = 38;
    const int IconOffX = 3, IconOffY = 3, IconSize = 32;
    const int NameX = 45, NameY = 4, NameW = 80, NameH = 14;
    const int LevelX = 45, LevelY = 20, LevelW = 60, LevelH = 12;
    const int SpUpX = 131, SpUpY = 20, SpUpSize = 12;

    // 列表右侧滚动条（Basic.img/VScr，12 宽）
    const int ScrollX = 161, ScrollW = 12, ScrollTop = 99, ScrollBottom = 254;

    // 底栏：白色数字框 (57,265)-(115,282)，右侧是「宏指令」按钮
    const int FooterX = 58, FooterY = 266, FooterW = 50, FooterH = 15;
    const int MacroX = 121, MacroY = 264, MacroW = 48, MacroH = 18;

    const int RecOffX = 36, RecW = 105, RecH = 35;

    static readonly Color HeaderText = new Color(1f, 0.96f, 0.78f);
    static readonly Color NameOn = new Color(0.10f, 0.11f, 0.15f);
    static readonly Color NameOff = new Color(0.30f, 0.33f, 0.40f);
    static readonly Color LevelText = new Color(0.16f, 0.27f, 0.56f);
    static readonly Color SpNumber = new Color(0.10f, 0.23f, 0.72f);
    static readonly Color SpZero = new Color(0.38f, 0.42f, 0.50f);

    static Texture2D Tex(string name)
        => Art.Tex("ui/skillwin/" + name + ".png");

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
        if (!Visible || GameLoop.I == null || GameLoop.I.player == null) return;
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
        HandleRows(win, list);
        HandleHints(win, list, hover);

        DrawBackground(win);
        if (NativeUi.Button(At(win, 156, 5, 12, 12), "UI/Basic.img/BtClose")) { Visible = false; return; }
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

    // —— 页签 ——
    // 原版页签条：底板是客户端 GDI 画的圆角块（WZ 里没有贴图，运行时生成），
    // 上面的字形才是 Skill/Tab/enabled|disabled/N。
    // 单页（新手）时底板左对齐，多页时沿页签条均匀铺开。
    static float TabLeft(int i, int n)
        => n <= 1 ? TabX : TabX + i * (167f - TabW) / (n - 1);

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

    // 页签底板：原客户端是 GDI 画的圆角块（选中粉红、未选中浅蓝白），
    // WZ 里没有对应贴图，这里按原图配色在运行时生成一张 25x19 的。
    static Texture2D tabOnTex, tabOffTex;

    static Texture2D TabPlateTex(bool on)
    {
        if (on && tabOnTex != null) return tabOnTex;
        if (!on && tabOffTex != null) return tabOffTex;

        const int w = TabW, h = TabH, r = 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = on ? "skillTabOn" : "skillTabOff",
        };
        var px = new Color32[w * h];
        var top = on ? new Color(1.00f, 0.88f, 0.94f) : new Color(0.95f, 0.97f, 1.00f);
        var bot = on ? new Color(0.79f, 0.20f, 0.35f) : new Color(0.70f, 0.76f, 0.88f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(r - x, x - (w - 1 - r)));
                float dy = Mathf.Max(0f, Mathf.Max(r - y, y - (h - 1 - r)));
                bool inside = dx * dx + dy * dy <= r * r + 0.5f;
                var c = Color.Lerp(bot, top, (float)y / (h - 1));
                if (!inside) c.a = 0f;
                px[y * w + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply();
        if (on) tabOnTex = tex; else tabOffTex = tex;
        return tex;
    }

    static void DrawTabPlate(Rect r, bool on)
    {
        var plate = TabPlateTex(on);
        if (plate != null) GUI.DrawTexture(r, plate, ScaleMode.StretchToFill);
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
        if (Event.current.type != EventType.Repaint || !Pointer.Pressed) return;

        for (int i = 0; i < Rows; i++)
        {
            int index = scroll + i;
            if (index >= list.Count) break;
            var d = SkillBook.Get(list[index]);
            if (d == null) continue;

            float y = RowY + i * RowStep;
            var hit = At(win, RowX, y, RowHitW, RowHitH);
            if (!hit.Contains(Pointer.Pos)) continue;

            var plus = At(win, SpUpX, y + SpUpY, SpUpSize, SpUpSize);
            var icon = At(win, RowX + IconOffX, y + IconOffY, IconSize, IconSize);
            selected = index;

            if (plus.Contains(Pointer.Pos))
            {
                CancelDrag();
                SkillProgress.Raise(d);
                return;
            }
            if (icon.Contains(Pointer.Pos) && SkillProgress.LevelOf(d.id) > 0)
            {
                dragId = d.id;
                dragStarted = false;
                lastPress = Pointer.Pos;
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
            bool on = index == selected || index == hover;
            var bg = Tex(on ? "skill1" : "skill0");
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
            var lvStyle = Style(10f, LevelText, TextAnchor.MiddleLeft, false, true);
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
        var st = Style(11f, sp > 0 ? SpNumber : SpZero, TextAnchor.MiddleRight);
        GUI.Label(At(win, FooterX, FooterY, FooterW, FooterH), sp.ToString(), st);

        if (NativeUi.Button(At(win, MacroX, MacroY, MacroW, MacroH), "UI/Basic.img/BtMacro"))
            BottomBar.Toast("技能组合栏暂未开放");
    }

    // —— 右侧滚动条（Basic.img/VScr）——
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
        var baseTex = Wz("UI/Basic.img/VScr/" + state + "/base");
        var prev = Wz("UI/Basic.img/VScr/" + state + (on ? "/prev0" : "/prev"));
        var next = Wz("UI/Basic.img/VScr/" + state + (on ? "/next0" : "/next"));
        var thumb = Wz("UI/Basic.img/VScr/" + state + (on ? "/thumb0" : "/thumb"));

        var track = ScrollTrackRect(win);
        if (baseTex != null) GUI.DrawTexture(track, baseTex, ScaleMode.StretchToFill);
        if (prev != null) GUI.DrawTexture(ScrollPrevRect(win), prev, ScaleMode.StretchToFill);
        if (next != null) GUI.DrawTexture(ScrollNextRect(win), next, ScaleMode.StretchToFill);
        if (thumb != null) GUI.DrawTexture(ScrollThumbRect(win, list), thumb, ScaleMode.StretchToFill);
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
        if (hover < 0 || hover >= list.Count || dragStarted) return;
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

    // —— 拖拽到快捷格 ——
    static int dragId;
    static bool dragStarted;
    static Vector2 lastPress;

    static void CancelDrag()
    {
        dragId = 0;
        dragStarted = false;
    }

    static void DrawDragGhost()
    {
        if (dragId == 0) { dragStarted = false; return; }
        if (!dragStarted && Pointer.Down)
        {
            if ((Pointer.Pos - lastPress).sqrMagnitude > 16f) dragStarted = true;
        }
        if (dragStarted && Pointer.Down)
        {
            var d = SkillBook.Get(dragId);
            var icon = SkillIcon(d);
            if (icon != null)
                GUI.DrawTexture(new Rect(Pointer.Pos.x - 16f * S, Pointer.Pos.y - 16f * S, 32f * S, 32f * S),
                    icon, ScaleMode.ScaleToFit);
        }
        if (!Pointer.Down)
        {
            if (dragStarted) DropOnSlot();
            CancelDrag();
        }
    }

    static void DropOnSlot()
    {
        if (KeyConfigWindow.DropSkill(Pointer.Pos, dragId)) return;
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            if (!BottomBar.SlotRect(i).Contains(Pointer.Pos)) continue;
            if (!QuickBarKeys.BindSkill(i, dragId)) return;
            BottomBar.QuickCollapsed = false;
            BottomBar.Toast("已绑定到快捷格 " + (i + 1) + "（键 " + KeyMap.Label(QuickBarKeys.Of(i)) + "）");
            return;
        }
    }
}
