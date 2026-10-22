using UnityEngine;

// Window artwork is loaded directly from the original UI IMG files.

public static class WindowShell
{
    public static readonly Color Back = new Color(0.10f, 0.11f, 0.14f, 0.94f);
    public static readonly Color Edge = new Color(0.55f, 0.62f, 0.72f, 0.9f);
    public static readonly Color Title = new Color(0.92f, 0.95f, 1f);

    public static Rect Frame(string title, float x, float y, float w, float h, ref bool visible)
    {
        string folder = title == "能力值" ? "Stat" : title == "装备" ? "Equip" : "Item";
        var bg = Art.Tex("wz://UI/UIWindow.img/" + folder + "/backgrnd");
        var r = new Rect(x, y, bg.width * BottomBar.S, bg.height * BottomBar.S);
        r.x = Mathf.Clamp(r.x, 0, Mathf.Max(0, UiKit.W - r.width));
        r.y = Mathf.Clamp(r.y, 0, Mathf.Max(0, UiKit.H - BottomBar.Height - r.height));
        GUI.DrawTexture(r, bg);
        var close = new Rect(r.xMax - 19 * BottomBar.S, r.y + 5 * BottomBar.S, 12 * BottomBar.S, 12 * BottomBar.S);
        if (NativeUi.Button(close, "UI/Basic.img/BtClose")) visible = false;
        return r;
    }

    public static bool Button(Rect r, string label, bool on = false)
    {
        bool clicked = NativeUi.Button(r, "UI/Basic.img/BtOK");
        return clicked;
    }

    public static void Label(Rect r, string text, int size = 12, Color? color = null)
    {
        var st = new GUIStyle { fontSize = size };
        st.normal.textColor = color ?? Color.black;
        GUI.Label(r, text, st);
    }
}

public static class StatWindow
{
    public static bool Visible;
    static Rect rect = new Rect(220, 120, 260, 250);
    static bool detail;
    public static void Toggle() { Visible = !Visible; SkillWindow.Visible = false; }

    public static void Draw()
    {
        if (!Visible || GameLoop.I == null || GameLoop.I.player == null) return;
        var r = WindowShell.Frame("能力值", rect.x, rect.y, rect.width, rect.height, ref Visible);
        var p = GameLoop.I.player;
        float scale = BottomBar.S;
        var attrs = Profile.Attributes;
        string[] values = { Profile.playerName, "新手", Profile.level.ToString(), "", Mathf.CeilToInt(p.hp) + " / " + Mathf.CeilToInt(p.hpMax), Mathf.CeilToInt(p.mp) + " / " + Mathf.CeilToInt(p.mpMax), Profile.experience.ToString(), "0" };
        int[] textY = { 33, 55, 79, 96, 115, 133, 151, 169 };
        for (int i = 0; i < values.Length; i++)
            WindowShell.Label(new Rect(r.x + 61 * scale, r.y + textY[i] * scale, 140 * scale, 18 * scale), values[i], Mathf.RoundToInt(11 * scale));
        WindowShell.Label(new Rect(r.x + 64 * scale, r.y + 213 * scale, 80 * scale, 18 * scale), Profile.ap.ToString());
        // CUIStat::OnCreate / SetButtons: original button locations, including title controls.
        int[] upY = { 117, 135, 247, 265, 283, 301 };
        foreach (int y in upY)
        {
            var disabled = Art.Tex("wz://UI/UIWindow.img/Stat/BtApUp/disabled/0");
            if (disabled != null) GUI.DrawTexture(new Rect(r.x + 153 * scale, r.y + y * scale, disabled.width * scale, disabled.height * scale), disabled);
        }
        if (NativeUi.Button(new Rect(r.x + 124 * scale, r.y + 324 * scale, 75 * scale, 18 * scale), "UI/UIWindow.img/Stat/BtDetail")) detail = !detail;
        if (detail)
        {
            var bg = Art.Tex("wz://UI/UIWindow.img/Stat/backgrnd2");
            float dx = r.xMax + 4 * scale;
            if (dx + bg.width * scale > UiKit.W) dx = Mathf.Max(0, r.x - (bg.width + 4) * scale);
            var dr = new Rect(dx, r.y, bg.width * scale, bg.height * scale);
            GUI.DrawTexture(dr, bg);
            int watk, matk;
            var combat = attrs.WithWeapon(WeaponDefs.Get(Profile.equipWeapon), 0, out watk, out matk);
            string[] lines = { watk.ToString(), combat.weaponDefense.ToString(), matk.ToString(), combat.magicDefense.ToString(), combat.accuracy.ToString(), combat.avoidability.ToString() };
            for (int i = 0; i < lines.Length; i++) WindowShell.Label(new Rect(dr.x + 84 * scale, dr.y + (7 + i * 18) * scale, 125 * scale, 16 * scale), lines[i]);
        }
        int[] stats = { attrs.str, attrs.dex, attrs.intelligence, attrs.luk };
        for (int i = 0; i < stats.Length; i++) WindowShell.Label(new Rect(r.x + 61 * scale, r.y + (244 + i * 18) * scale, 130 * scale, 18 * scale), stats[i].ToString());
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
        string[] parts = { "Coat/01040002", "Pants/01060000", "Shoes/01070000", "Weapon/" + int.Parse(Profile.equipWeapon).ToString("D8") };
        Vector2[] slots = { new Vector2(35, 133), new Vector2(35, 167), new Vector2(69, 201), new Vector2(103, 133) };
        for (int i = 0; i < parts.Length; i++)
        {
            var icon = Art.Tex("wz://Character/" + parts[i] + ".img/info/icon");
            if (icon != null) GUI.DrawTexture(new Rect(r.x + slots[i].x * BottomBar.S, r.y + slots[i].y * BottomBar.S, 30 * BottomBar.S, 30 * BottomBar.S), icon, ScaleMode.ScaleToFit);
        }
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
        WindowShell.Label(new Rect(r.x + 25 * BottomBar.S, r.y + 268 * BottomBar.S, 116 * BottomBar.S, 16 * BottomBar.S), Profile.gold.ToString());
        int slot = 0;
        foreach (string id in Profile.OwnedWeapons)
        {
            var icon = Art.Tex("wz://Character/Weapon/" + int.Parse(id).ToString("D8") + ".img/info/icon");
            var cell = new Rect(r.x + (10 + slot % 4 * 36) * BottomBar.S, r.y + (51 + slot / 4 * 34) * BottomBar.S, 30 * BottomBar.S, 30 * BottomBar.S);
            if (icon != null) GUI.DrawTexture(cell, icon, ScaleMode.ScaleToFit);
            if (Event.current.type == EventType.Repaint && Pointer.Released && cell.Contains(Pointer.Pos)) Profile.EquipWeapon(id);
            if (++slot >= 24) break;
        }
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
        Visible = false;
        BottomBar.Toast("商城功能尚未接入");
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
            if (!KeyConfigWindow.Visible && !VirtualControlsEditor.Visible && enabled(i) && Event.current.type == EventType.Repaint && Pointer.Released && hover) click(i);
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
        Visible = false;
        if (!KeyConfigWindow.Visible) KeyConfigWindow.Toggle();
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
                if (i == 4) Application.Quit();
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
