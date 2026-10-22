using UnityEngine;

// ————————————————————————————————————————————————————————————
// 底部栏按钮背后的各个窗口。
//
// 说明（也写进了 docs 的适配表）：
//   技能窗  —— 用 UI.wz/UIWindow.img/Skill 的原版底板与条目，完整移植，见 SkillWindow.cs
//   底部栏  —— 用 UI.wz/StatusBar.img 的原版按钮，见 BottomBar.cs
//   其余窗口（商城/装备/背包/能力值/快捷键/帮助/菜单）这一轮先做出可用的面板骨架，
//   贴图没有逐张从 WZ 里取，等对应系统真正移植时再换成原版对话框。
// ————————————————————————————————————————————————————————————

public static class WindowShell
{
    public static readonly Color Back = new Color(0.10f, 0.11f, 0.14f, 0.94f);
    public static readonly Color Edge = new Color(0.55f, 0.62f, 0.72f, 0.9f);
    public static readonly Color Title = new Color(0.92f, 0.95f, 1f);

    public static Rect Frame(string title, float x, float y, float w, float h, ref bool visible)
    {
        var r = new Rect(x, y, w, h);
        UiKit.Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0f, 0f, 0f, 0.55f));
        UiKit.Fill(r, Back);
        UiKit.Fill(new Rect(r.x, r.y, r.width, 1.5f), Edge);
        UiKit.Fill(new Rect(r.x, r.yMax - 1.5f, r.width, 1.5f), Edge);
        UiKit.Fill(new Rect(r.x, r.y, 1.5f, r.height), Edge);
        UiKit.Fill(new Rect(r.xMax - 1.5f, r.y, 1.5f, r.height), Edge);

        var ts = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold };
        ts.normal.textColor = Title;
        GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 40, 20), title, ts);

        var close = new Rect(r.xMax - 22, r.y + 3, 18, 16);
        UiKit.Fill(close, new Color(0.35f, 0.12f, 0.14f, 0.95f));
        var cs = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        cs.normal.textColor = new Color(1f, 0.85f, 0.85f);
        GUI.Label(close, "×", cs);
        if (Pointer.Pressed && close.Contains(Pointer.Pos)) visible = false;
        return r;
    }

    public static bool Button(Rect r, string label, bool on = false)
    {
        bool hover = r.Contains(Pointer.Pos);
        UiKit.Fill(r, on ? new Color(0.30f, 0.42f, 0.58f, 0.96f)
                         : hover ? new Color(0.22f, 0.28f, 0.38f, 0.96f)
                                 : new Color(0.15f, 0.18f, 0.24f, 0.96f));
        var st = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(0.92f, 0.95f, 1f);
        GUI.Label(r, label, st);
        return Pointer.Pressed && hover;
    }

    public static void Label(Rect r, string text, int size = 12, Color? color = null)
    {
        var st = new GUIStyle { fontSize = size };
        st.normal.textColor = color ?? new Color(0.86f, 0.9f, 0.96f);
        GUI.Label(r, text, st);
    }
}

public static class StatWindow
{
    public static bool Visible;
    static Rect rect = new Rect(220, 120, 260, 250);
    public static void Toggle() { Visible = !Visible; SkillWindow.Visible = false; }

    public static void Draw()
    {
        if (!Visible || GameLoop.I == null || GameLoop.I.player == null) return;
        var r = WindowShell.Frame("能力值", rect.x, rect.y, rect.width, rect.height, ref Visible);
        var p = GameLoop.I.player;
        float y = r.y + 30;
        WindowShell.Label(new Rect(r.x + 12, y, 240, 18), "等级   Lv." + Profile.level + "    " + ClassDefs.Get(Profile.cls).name);
        y += 20;
        WindowShell.Label(new Rect(r.x + 12, y, 240, 18), "HP     " + Mathf.CeilToInt(p.hp) + " / " + Mathf.CeilToInt(p.hpMax));
        y += 18;
        WindowShell.Label(new Rect(r.x + 12, y, 240, 18), "MP     " + Mathf.CeilToInt(p.mp) + " / " + Mathf.CeilToInt(p.mpMax));
        y += 18;
        WindowShell.Label(new Rect(r.x + 12, y, 240, 18), "经验   " + Profile.experience + " / " + Profile.ExpNeeded);
        y += 18;
        WindowShell.Label(new Rect(r.x + 12, y, 240, 18), "技能点 " + SkillProgress.AvailableSp + " / " + SkillProgress.TotalSp);
        y += 18;
        WindowShell.Label(new Rect(r.x + 12, y, 240, 18), "金币   " + Profile.gold);
        y += 26;
        WindowShell.Label(new Rect(r.x + 12, y, 240, 18), "AP 加点面板尚未移植", 11, new Color(0.7f, 0.75f, 0.82f));
    }
}

public static class EquipWindow
{
    public static bool Visible;
    static Rect rect = new Rect(200, 100, 300, 220);
    public static void Toggle() { Visible = !Visible; }

    public static void Draw()
    {
        if (!Visible) return;
        var r = WindowShell.Frame("装备", rect.x, rect.y, rect.width, rect.height, ref Visible);
        WindowShell.Label(new Rect(r.x + 12, r.y + 30, 280, 18), "当前武器：" + WeaponDefs.Get(Profile.equipWeapon).name);
        WindowShell.Label(new Rect(r.x + 12, r.y + 58, 280, 18), "背包装备 " + Profile.OwnedWeapons.Count + " 件");
        WindowShell.Label(new Rect(r.x + 12, r.y + 96, 280, 40), "装备栏/换装界面尚未移植，\n换装仍在大厅进行。", 11, new Color(0.7f, 0.75f, 0.82f));
    }
}

public static class InvenWindow
{
    public static bool Visible;
    static Rect rect = new Rect(200, 100, 320, 260);
    public static void Toggle() { Visible = !Visible; }

    public static void Draw()
    {
        if (!Visible) return;
        var r = WindowShell.Frame("背包", rect.x, rect.y, rect.width, rect.height, ref Visible);
        WindowShell.Label(new Rect(r.x + 12, r.y + 30, 300, 18), "金币：" + Profile.gold);
        WindowShell.Label(new Rect(r.x + 12, r.y + 56, 300, 18), "消耗/其他栏尚未移植（掉落会进存档）");
    }
}

public static class ShopWindow
{
    public static bool Visible;
    static Rect rect = new Rect(220, 110, 320, 220);
    public static void Toggle() { Visible = !Visible; }

    public static void Draw()
    {
        if (!Visible) return;
        var r = WindowShell.Frame("商城", rect.x, rect.y, rect.width, rect.height, ref Visible);
        WindowShell.Label(new Rect(r.x + 12, r.y + 34, 300, 18), "商城（CashShop）尚未移植。");
        WindowShell.Label(new Rect(r.x + 12, r.y + 60, 300, 40), "按钮版式已按原版摆好，\n商品数据要接服务端 CashShop 表。", 11, new Color(0.7f, 0.75f, 0.82f));
    }
}

// 原版“界面”菜单；未迁移的系统显示原生 disabled 态。
public static class ShortcutWindow
{
    public static bool Visible { get => BottomBar.InterfaceVisible; set => BottomBar.InterfaceVisible = value; }
    public static void Toggle() { Visible = !Visible; }
    public static void Draw()
    {
        if (!Visible) return;
        var anchor = BottomBar.BigButtonRect(3);
        NativeMenu.Draw("shortcut", new Vector2(anchor.xMax - 93 * BottomBar.S, anchor.y - 245 * BottomBar.S),
            new[] { "BtItem", "BtEquip", "BtStat", "BtSkill", "BtParty", "BtQuest", "BtMessenger", "BtGuild", "BtComm", "BtMobbook", "BtRanking" },
            245, i => i < 4, i => {
                if (i == 0) InvenWindow.Toggle();
                if (i == 1) EquipWindow.Toggle();
                if (i == 2) StatWindow.Toggle();
                if (i == 3) SkillWindow.Toggle();
                Visible = false;
            });
    }
}

public static class NativeMenu
{
    public static void Draw(string folder, Vector2 pos, string[] buttons, int height,
                            System.Func<int, bool> enabled, System.Action<int> click)
    {
        float s = BottomBar.S;
        var bg = Art.Tex("ui/" + folder + "/backgrnd.bytes");
        if (bg != null) GUI.DrawTexture(new Rect(pos.x, pos.y, 93 * s, height * s), bg);
        float step = (height - 10f) / buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            var r = new Rect(pos.x + 6 * s, pos.y + (5 + i * step) * s, 81 * s, 25 * s);
            bool hover = r.Contains(Pointer.Pos);
            string state = !enabled(i) ? "disabled" : hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal";
            var tex = Art.Tex("ui/" + folder + "/" + buttons[i] + "_" + state + ".bytes");
            if (tex != null) GUI.DrawTexture(r, tex);
            if (!KeyConfigWindow.Visible && enabled(i) && Event.current.type == EventType.Repaint && Pointer.Released && hover) click(i);
        }
    }
}

public static class HelpWindow
{
    public static bool Visible;
    static Rect rect = new Rect(180, 80, 380, 320);
    public static void Toggle() { Visible = !Visible; }

    public static void Draw()
    {
        if (!Visible) return;
        var r = WindowShell.Frame("帮助 · 键位", rect.x, rect.y, rect.width, rect.height, ref Visible);
        float y = r.y + 30;
        string[] lines =
        {
            "← →   移动        ↑ ↓   上下绳/台阶",
            "Alt   跳跃        Ctrl  普攻（点一下即挥）",
            "Z     拾取        ↑     进传送门",
            "",
            "Shift/Ins/Home/PgUp/Ctrl/Del/End/PgDn → 快捷格 1~8",
            "K 技能   S 能力值   E 装备   I 背包",
            "\\ 键位    `  快捷栏   Tab 快捷键",
            "Esc 菜单   F4 商城    F1 帮助",
            "",
            "屏幕左下角的虚拟键盘可以按住不放拖动，",
            "键盘设定中拖动功能图标，确定后保存。",
        };
        foreach (var line in lines)
        {
            WindowShell.Label(new Rect(r.x + 14, y, r.width - 28, 18), line, 12);
            y += 19;
        }
        if (WindowShell.Button(new Rect(r.x + 14, r.yMax - 34, 90, 24), "恢复默认键位"))
            KeyMap.ResetAll();
    }
}

public static class GameMenuWindow
{
    public static bool Visible;
    public static void Toggle() { Visible = !Visible; }
    public static void Draw()
    {
        if (!Visible) return;
        var anchor = BottomBar.BigButtonRect(2);
        NativeMenu.Draw("gamemenu", new Vector2(anchor.xMax - 93 * BottomBar.S, anchor.y - 140 * BottomBar.S),
            new[] { "BtChannel", "BtSkin", "BtGameOpt", "BtSysOpt", "BtQuit" }, 140,
            i => i == 4, i => {
                Visible = false;
                if (i == 2) KeyConfigWindow.Toggle();
                if (i == 3) HelpWindow.Toggle();
                if (i == 4) GameLoop.I.EnterLobby();
            });
    }
}

// 所有浮窗的统一绘制顺序（技能窗在最后，压在虚拟键盘之上）
public static class GameWindows
{
    public static void DrawAll()
    {
        StatWindow.Draw();
        EquipWindow.Draw();
        InvenWindow.Draw();
        ShopWindow.Draw();
        ShortcutWindow.Draw();
        HelpWindow.Draw();
        GameMenuWindow.Draw();
        SkillWindow.Draw();
    }
}
