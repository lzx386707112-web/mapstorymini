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
//   Tab/enabled|disabled  0/I/II/III/IV/V 页签
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

    static float S => UiKit.H / 600f;
    static Vector2 pos = new Vector2(-1f, -1f);
    static int tab;
    static int selected;
    static int scroll;

    const int WinW = 175, WinH = 289;

    const int TabX = 30, TabY = 24, TabStep = 20, TabHitY = 20, TabHitH = 22;

    const int RowX = 7, RowY = 99, RowStep = 40, RowW = 141, RowH = 35, Rows = 4;
    const int RowHitW = 154, RowHitH = 38;
    const int IconOffX = 3, IconOffY = 3, IconSize = 32;
    const int SpUpX = 149, SpUpY = 12, SpUpSize = 12;

    const int FooterX = 61, FooterY = 265, FooterW = 51, FooterH = 16;

    const int RecOffX = 36, RecW = 105, RecH = 35;

    static readonly Color HeaderText = new Color(1f, 0.96f, 0.78f);
    static readonly Color NameOn = new Color(0.10f, 0.11f, 0.15f);
    static readonly Color NameOff = new Color(0.36f, 0.38f, 0.44f);
    static readonly Color LevelText = new Color(0.16f, 0.27f, 0.56f);
    static readonly Color SpNumber = new Color(0.10f, 0.23f, 0.72f);
    static readonly Color SpZero = new Color(0.38f, 0.42f, 0.50f);

    static Texture2D Tex(string name)
        => Art.Tex("ui/skillwin/" + name + ".png");

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

    // 职业 id → 转职阶段：100/200/300 = I，x10 = II，x11 = III，x12 = IV
    static int StageOf(int job)
    {
        if (job < 100) return 0;
        if (job % 100 == 0) return 1;
        int d = job % 10;
        if (d == 0) return 2;
        if (d == 1) return 3;
        return 4;
    }

    static void ClampState(List<int> list)
    {
        if (list.Count == 0) { selected = 0; scroll = 0; return; }
        scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, list.Count - Rows));
        selected = Mathf.Clamp(selected, 0, list.Count - 1);
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
        ClampState(list);

        int hover = RowAt(win, list);
        HandleRows(win, list);
        HandleHints(win, list, hover);

        DrawBackground(win);
        DrawTabs(win, tabs);
        DrawHeader(win, list, hover);
        DrawRows(win, list, hover);
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
    static void HandleTabInput(Rect win, List<Dictionary<string, object>> tabs)
    {
        if (Event.current.type != EventType.Repaint || !Pointer.Pressed) return;
        for (int i = 0; i < tabs.Count && i < 6; i++)
        {
            int stage = StageOf(Art.Int(tabs[i], "job", 0));
            if (stage < 0 || stage > 5) continue;
            var hit = At(win, TabX + TabStep * stage, TabHitY, TabStep, TabHitH);
            if (!hit.Contains(Pointer.Pos)) continue;
            if (tab != i)
            {
                tab = i;
                selected = 0;
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

    static void DrawTabs(Rect win, List<Dictionary<string, object>> tabs)
    {
        for (int i = 0; i < tabs.Count && i < 6; i++)
        {
            int stage = StageOf(Art.Int(tabs[i], "job", 0));
            if (stage < 0 || stage > 5) continue;
            var t = Tex("tab_" + stage + (i == tab ? "" : "_off"));
            if (t == null) continue;
            float tx = TabX + TabStep * stage + (TabStep - t.width) * 0.5f;
            GUI.DrawTexture(new Rect(win.x + tx * S, win.y + TabY * S, t.width * S, t.height * S),
                t, ScaleMode.StretchToFill);
        }
    }

    // —— 顶部选中技能名 ——
    static void DrawHeader(Rect win, List<int> list, int hover)
    {
        int index = hover >= 0 ? hover : selected;
        if (index < 0 || index >= list.Count) return;
        var d = SkillBook.Get(list[index]);
        if (d == null || string.IsNullOrEmpty(d.name)) return;

        var rect = At(win, 7f, 61f, 161f, 19f);
        var shadow = Style(11f, new Color(0f, 0f, 0f, 0.55f), TextAnchor.MiddleCenter, false, true);
        var main = Style(11f, HeaderText, TextAnchor.MiddleCenter, false, true);
        GUI.Label(new Rect(rect.x + S, rect.y + S, rect.width, rect.height), d.name, shadow);
        GUI.Label(rect, d.name, main);
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
            else UiKit.Fill(row, on ? new Color(0.85f, 0.86f, 0.84f) : new Color(0.92f, 0.93f, 0.95f));

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
            var nameStyle = Style(10f, lv > 0 ? NameOn : NameOff, TextAnchor.MiddleLeft, false, true);
            var lvStyle = Style(9f, LevelText, TextAnchor.MiddleLeft, false, true);
            GUI.Label(At(win, RowX + 38f, y + 1f, 104f, 16f), d.name, nameStyle);
            GUI.Label(At(win, RowX + 38f, y + 18f, 104f, 15f), "Lv. " + lv, lvStyle);

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

    // —— 底部技能点 ——
    static void DrawFooter(Rect win)
    {
        int sp = SkillProgress.AvailableSp;
        var box = At(win, FooterX + 1f, FooterY, FooterW - 2f, FooterH);
        var st = Style(10f, sp > 0 ? SpNumber : SpZero, TextAnchor.MiddleCenter);
        GUI.Label(box, sp.ToString(), st);
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

        UiKit.Fill(new Rect(x + 2f, y + 2f, w, h), new Color(0f, 0f, 0f, 0.30f));
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

        UiKit.Fill(new Rect(x + 2f, y + 2f, w, h), new Color(0f, 0f, 0f, 0.35f));
        UiKit.Fill(new Rect(x, y, w, h), new Color(0.055f, 0.075f, 0.125f, 0.96f));
        UiKit.Fill(new Rect(x, y, w, 1.5f), new Color(0.50f, 0.60f, 0.82f, 0.85f));
        UiKit.Fill(new Rect(x, y + h - 1.5f, w, 1.5f), new Color(0.24f, 0.30f, 0.44f, 0.85f));

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
            UiKit.Fill(new Rect(x + pad, cy - 2f * S, inner, 1f), new Color(0.30f, 0.38f, 0.55f, 0.7f));
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
