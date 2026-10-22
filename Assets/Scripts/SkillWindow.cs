using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 技能窗（原版 K 键）。
//
// 底板与条目全部来自 UI.wz/UIWindow.img/Skill：
//   backgrnd 175x289   窗口底板（标题「技能」、上排页签位、中间暗条、底部「技能点」都是画在图里的）
//   skill0/skill1 141x35  未选中/选中的技能条目
//   Tab/enabled/N      罗马数字页签 0/I/II/III/IV/V（= 转职阶段）
//   BtSpUp 12x12       SP 加点
//
// 条目内容是原版数据：图标 Skill.wz/<job>.img/skill/<id>/icon 32x32，
// 名字 String.wz/Skill.img/<id>/name，等级上限与逐级数值 Skill.wz 的 level 节点。
//
// 交互：
//   点页签切换职业阶段 / 点条目选中 / 点「+」加一点 SP
//   从图标上按下并拖到底部快捷格松开 = 把技能绑到那个快捷格的键盘键
// ————————————————————————————————————————————————————————————

public static class SkillWindow
{
    public static bool Visible;

    static float S => UiKit.H / 600f;
    static Vector2 pos = new Vector2(-1f, -1f);   // 原图像素坐标，*S 使用
    static int tab;
    static int selected;
    static float scroll;

    // 原图内的排版（像素）
    const int WinW = 175, WinH = 289;
    const int TabY = 24, TabX = 30, TabStep = 20;
    // ListY 必须落在 y62..80 那条「选中技能名」暗条的下面，否则第一条会被暗条压掉半行
    const int ListX = 14, ListY = 82, RowW = 141, RowH = 35, Rows = 5;
    const int IconPad = 2, IconSize = 32;

    static Texture2D Tex(string name)
    {
        return Art.Tex("ui/skillwin/" + name + (name.EndsWith(".png") ? "" : ".png"));
    }

    static Texture2D SkillIcon(SkillData d)
        => d == null || string.IsNullOrEmpty(d.icon) ? null : Art.Tex(d.icon);

    public static void Toggle()
    {
        Visible = !Visible;
        if (Visible) { StatWindow.Visible = false; }
    }

    static Dictionary<string, object> Line => SkillBook.LineFor(Profile.cls);
    static List<Dictionary<string, object>> Tabs => SkillBook.Tabs(Line);

    static List<int> TabSkills()
    {
        var tabs = Tabs;
        if (tabs.Count == 0) return new List<int>();
        tab = Mathf.Clamp(tab, 0, tabs.Count - 1);
        var list = new List<int>();
        if (tabs[tab].TryGetValue("skills", out var raw))
            foreach (var o in (List<object>)raw) list.Add(int.Parse(o.ToString()));
        return list;
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

        float w = WinW * S, h = WinH * S;
        var r = new Rect(pos.x * S, pos.y * S, w, h);

        // 拖动窗口：按住标题栏
        var title = new Rect(r.x, r.y, r.width, 22f * S);
        if (Pointer.Pressed && title.Contains(Pointer.Pos))
        {
            dragging = true;
            dragOffset = Pointer.Pos / S - pos;
        }
        if (dragging && Pointer.Down) DragTo();
        if (!Pointer.Down) dragging = false;

        var bg = Tex("backgrnd");
        if (bg != null) GUI.DrawTexture(r, bg, ScaleMode.StretchToFill);

        DrawTabs(r);
        DrawHeader(r);
        DrawList(r);
        DrawFooter(r);
        DrawDragGhost();
    }

    static bool dragging;
    static Vector2 dragOffset;

    static void DragTo()
    {
        // Pointer.Pos 是虚拟坐标；窗口位置按原图像素存，所以要除回 S
        pos = Pointer.Pos / S - dragOffset;
        pos.x = Mathf.Clamp(pos.x, -40f, UiKit.W / S - 60f);
        pos.y = Mathf.Clamp(pos.y, 0f, UiKit.H / S - 80f);
    }

    static void DrawTabs(Rect r)
    {
        var tabs = Tabs;
        float x = r.x + TabX * S;
        for (int i = 0; i < tabs.Count && i < 6; i++)
        {
            var t = Tex("tab_" + i + (i == tab ? "" : "_off"));
            if (t == null) continue;
            float tw = t.width * S * 1.1f, th = t.height * S * 1.1f;
            var tr = new Rect(x, r.y + TabY * S, tw, th);
            GUI.DrawTexture(tr, t, ScaleMode.StretchToFill);
            var hit = new Rect(tr.x - 2, tr.y - 2, tr.width + 4, tr.height + 6);
            if (Pointer.Pressed && hit.Contains(Pointer.Pos))
            {
                tab = i; scroll = 0f;
            }
            x += Mathf.Max(TabStep * S, tw + 6f * S);
        }
    }

    static void DrawHeader(Rect r)
    {
        // 原图 y40..70 的暗条：原版放当前选中技能的名字
        var list = TabSkills();
        if (list.Count == 0) return;
        selected = Mathf.Clamp(selected, 0, list.Count - 1);
        var d = SkillBook.Get(list[selected]);
        if (d == null) return;
        var st = new GUIStyle { fontSize = Mathf.RoundToInt(11f * S), alignment = TextAnchor.MiddleLeft };
        st.normal.textColor = new Color(0.95f, 0.93f, 0.82f);
        // 底板上的暗条实测在原图 y62..80；上面的 y45..54 是一条浅色内嵌条，
        // 名字画到那儿会「上半截消失在白条里」，所以贴着暗条画。
        GUI.Label(new Rect(r.x + 18f * S, r.y + 61f * S, (RowW + 8) * S, 13f * S), d.name, st);
    }

    static void DrawList(Rect r)
    {
        var list = TabSkills();
        if (list.Count == 0)
        {
            WindowShell.Label(new Rect(r.x + 10 * S, r.y + 90 * S, 150 * S, 20 * S), "这条职业线没有技能数据", 11);
            return;
        }

        // 滚轮
        if (r.Contains(Pointer.Pos))
            scroll = Mathf.Clamp(scroll - Input.mouseScrollDelta.y * 2f, 0f, Mathf.Max(0f, list.Count - Rows));

        int first = Mathf.FloorToInt(scroll);
        for (int i = 0; i < Rows; i++)
        {
            int index = first + i;
            if (index >= list.Count) break;
            var d = SkillBook.Get(list[index]);
            if (d == null) continue;

            var row = new Rect(r.x + ListX * S, r.y + (ListY + i * RowH) * S, RowW * S, RowH * S);
            bool sel = index == selected;
            var bg = Tex(sel ? "skill1" : "skill0");
            if (bg != null) GUI.DrawTexture(row, bg, ScaleMode.StretchToFill);
            else UiKit.Fill(row, sel ? new Color(0.3f, 0.24f, 0.15f, 0.9f) : new Color(0.2f, 0.22f, 0.26f, 0.9f));

            var icon = SkillIcon(d);
            if (icon != null)
                GUI.DrawTexture(new Rect(row.x + IconPad * S, row.y + 2 * S, IconSize * S, IconSize * S), icon, ScaleMode.ScaleToFit);

            int lv = SkillProgress.LevelOf(d.id);
            // skill0/skill1 这两张条目图是浅底的，字必须压深才看得见
            var ns = new GUIStyle { fontSize = Mathf.RoundToInt(10f * S), alignment = TextAnchor.UpperLeft };
            ns.normal.textColor = lv > 0 ? new Color(0.12f, 0.13f, 0.17f) : new Color(0.38f, 0.40f, 0.46f);
            GUI.Label(new Rect(row.x + 38 * S, row.y + 3 * S, 92 * S, 14 * S), d.name, ns);

            var ls = new GUIStyle { fontSize = Mathf.RoundToInt(9f * S), alignment = TextAnchor.UpperLeft };
            ls.normal.textColor = new Color(0.20f, 0.30f, 0.55f);
            GUI.Label(new Rect(row.x + 38 * S, row.y + 17 * S, 92 * S, 13 * S), "Lv." + lv + " / " + d.maxLevel, ls);

            if (SkillProgress.CanRaise(d))
            {
                var plus = new Rect(row.xMax - 16 * S, row.y + 10 * S, 12 * S, 12 * S);
                var sp = Tex("spup");
                if (sp != null) GUI.DrawTexture(plus, sp, ScaleMode.StretchToFill);
                if (Pointer.Pressed && plus.Contains(Pointer.Pos)) SkillProgress.Raise(d);
            }

            // 选中 / 开始拖拽
            if (Pointer.Pressed && row.Contains(Pointer.Pos))
            {
                selected = index;
                if (lv > 0) { dragId = d.id; dragStarted = false; lastPress = Pointer.Pos; }
            }
        }
    }

    static void DrawFooter(Rect r)
    {
        // 原图底栏「技能点」后面就是数值框（x≈70..115, y≈262..282）
        var box = new Rect(r.x + 70 * S, r.y + 262 * S, 46 * S, 18 * S);
        UiKit.Fill(box, new Color(1f, 1f, 1f, 0.92f));
        var st = new GUIStyle { fontSize = Mathf.RoundToInt(10f * S), alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(0.1f, 0.12f, 0.16f);
        GUI.Label(box, SkillProgress.AvailableSp.ToString(), st);
    }

    static int dragId;
    static bool dragStarted;

    static void DrawDragGhost()
    {
        if (dragId == 0) { dragStarted = false; return; }
        // 移动超过 4px 才算拖拽，避免点一下就误绑
        if (!dragStarted && Pointer.Down)
        {
            if ((Pointer.Pos - lastPress).sqrMagnitude > 16f) dragStarted = true;
        }
        if (dragStarted && Pointer.Down)
        {
            var d = SkillBook.Get(dragId);
            var icon = SkillIcon(d);
            if (icon != null)
                GUI.DrawTexture(new Rect(Pointer.Pos.x - 16 * S, Pointer.Pos.y - 16 * S, 32 * S, 32 * S), icon, ScaleMode.ScaleToFit);
        }
        if (!Pointer.Down)
        {
            if (dragStarted) DropOnSlot();
            dragId = 0; dragStarted = false;
        }
    }

    static Vector2 lastPress;

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
