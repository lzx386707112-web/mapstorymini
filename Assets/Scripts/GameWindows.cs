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
    static Vector2 pos = new Vector2(220, 80);
    static bool detail = true, dragging;
    // Reference client screenshot: all six AP arrows share the right-hand column.
    const int ApArrowX = 185;
    static Vector2 grab;
    static float S => BottomBar.S;
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static Texture2D Tex(string node) => Art.Tex("wz://UI/UIWindow.img/Stat/" + node);
    static void Image(Rect r, string node, int x, int y)
    {
        var tex = Tex(node);
        if (tex != null) GUI.DrawTexture(At(r, x, y, tex.width, tex.height), tex);
    }
    static void Text(Rect r, string text, int x, int y, int width = 135)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(11 * S), clipping = TextClipping.Clip };
        style.normal.textColor = Color.black;
        GUI.Label(At(r, x, y, width, 17), text, style);
    }
    public static void Toggle() { Visible = !Visible; SkillWindow.Visible = false; }

    public static void Draw()
    {
        if (!Visible || GameLoop.I == null || GameLoop.I.player == null) return;
        var r = new Rect(pos.x * S, pos.y * S, 208 * S, 347 * S);
        if (Event.current.type == EventType.Repaint && Pointer.Pressed && At(r, 0, 0, 184, 22).Contains(Pointer.Pos))
        { dragging = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down)
        {
            pos = Pointer.Pos / S - grab;
            pos.x = Mathf.Clamp(pos.x, 0, Mathf.Max(0, UiKit.W / S - 208));
            pos.y = Mathf.Clamp(pos.y, 0, Mathf.Max(0, UiKit.H / S - 347));
            r.position = pos * S;
        }
        if (!Pointer.Down) dragging = false;
        Image(r, "backgrnd", 0, 0);
        if (NativeUi.Button(At(r, 190, 5, 12, 12), "UI/Basic.img/BtClose")) { Visible = false; return; }
        var p = GameLoop.I.player;
        var attrs = Profile.Attributes;
        string[] values = { Profile.playerName, "", Profile.level.ToString(), "-",
            Mathf.CeilToInt(p.hp) + " / " + Mathf.CeilToInt(p.hpMax), Mathf.CeilToInt(p.mp) + " / " + Mathf.CeilToInt(p.mpMax),
            Profile.experience + " (" + (Profile.ExpRatio * 100).ToString("0.##") + "%)", "0" };
        int[] textY = { 33, 55, 79, 96, 115, 133, 151, 169 };
        for (int i = 0; i < values.Length; i++) Text(r, values[i], 61, textY[i]);
        int job = Profile.cls == "knight" ? 112 : Profile.cls == "warrior" ? 132 : Profile.cls == "mage" ? 212 : Profile.cls == "priest" ? 232 : Profile.cls == "archer" ? 312 : 0;
        Image(r, "Job/main/" + job / 100, 61, 51);
        Image(r, "Job/sub/" + job / 100 + "/" + job, 61, 64);

        if (Profile.AutomaticAp) Image(r, "basicStat", 8, 195);
        else
        {
            var apStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(11 * S), alignment = TextAnchor.MiddleRight };
            apStyle.normal.textColor = Color.black;
            GUI.Label(At(r, 61, 213, 26, 16), Profile.ap.ToString(), apStyle);
            // Original 11+ layout: compact automatic-allocation control beside the AP count.
            var auto = Tex("BtAuto/disabled/0");
            if (auto != null) GUI.DrawTexture(At(r, 97, 197, 73, 35), auto);
            int[] stats = { attrs.str, attrs.dex, attrs.intelligence, attrs.luk };
            for (int i = 0; i < stats.Length; i++)
            {
                // Unadvanced beginners use STR/DEX; INT/LUK labels are grey in the client.
                if (Profile.cls == "beginner" && i >= 2)
                    Image(r, "Disabled/" + (i == 2 ? "INT" : "LUK"), 8, 244 + i * 18);
                Text(r, stats[i].ToString(), 61, 244 + i * 18);
                var up = At(r, ApArrowX, 247 + i * 18, 12, 12);
                if (!Profile.CanAssignAp(i)) Image(r, "BtApUp/disabled/0", ApArrowX, 247 + i * 18);
                else if (NativeUi.Button(up, "UI/UIWindow.img/Stat/BtApUp")) Profile.AssignAp(i);
            }
            // HP/MP AP conversion is separate from the four base attributes.
            Image(r, "BtApUp/disabled/0", ApArrowX, 117);
            Image(r, "BtApUp/disabled/0", ApArrowX, 135);
        }
        // 音量控制（音乐/音效分开，原端设置里也是两项）：
        // 拖动即时生效并写 PlayerPrefs；BGM 音量变化同时作用到正在播的曲子。
        var volStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(10 * S) };
        volStyle.normal.textColor = Color.black;
        GUI.Label(At(r, 8, 310, 34, 14), "音乐", volStyle);
        float bgmVol = GUI.HorizontalSlider(At(r, 42, 314, 100, 12), AudioManager.BgmVolume, 0f, 1f);
        if (!Mathf.Approximately(bgmVol, AudioManager.BgmVolume)) AudioManager.BgmVolume = bgmVol;
        GUI.Label(At(r, 8, 328, 34, 14), "音效", volStyle);
        float sfxVol = GUI.HorizontalSlider(At(r, 42, 332, 100, 12), AudioManager.SfxVolume, 0f, 1f);
        if (!Mathf.Approximately(sfxVol, AudioManager.SfxVolume)) AudioManager.SfxVolume = sfxVol;

        if (NativeUi.Button(At(r, 154, 324, 46, 19), "UI/UIWindow.img/Stat/BtDetail")) detail = !detail;
        if (!detail) return;
        // Original detail window bottom-aligns with the main panel (347-203=144).
        float dx = r.xMax - S;
        if (dx + 218 * S > UiKit.W) dx = Mathf.Max(0, r.x - 217 * S);
        var dr = new Rect(dx, r.y + 144 * S, 218 * S, 203 * S);
        Image(dr, "backgrnd2", 0, 0);
        int watk, matk;
        var weapon = WeaponDefs.Get(Profile.equipWeapon);
        var combat = attrs.WithWeapon(weapon, 0, out watk, out matk);
        bool dexWeapon = weapon.kind == WeaponKind.bow || weapon.kind == WeaponKind.crossbow || weapon.kind == WeaponKind.gun;
        int main = weapon.kind == WeaponKind.claw ? combat.luk : dexWeapon ? combat.dex : combat.str;
        int secondary = weapon.kind == WeaponKind.claw ? combat.dex + combat.str : dexWeapon ? combat.str : combat.dex;
        int max = CombatRules.MaxPhysicalDamage(main, secondary, watk, weapon.multiplier);
        int min = (int)System.Math.Floor((main * weapon.multiplier * .9 * combat.mastery + secondary) * watk / 100.0);
        string[] lines = { min + " ~ " + max, combat.weaponDefense.ToString(), matk.ToString(), combat.magicDefense.ToString(),
            Mathf.FloorToInt(combat.accuracy + combat.dex * .8f + combat.luk * .5f).ToString(),
            Mathf.FloorToInt(combat.avoidability + combat.dex * .25f + combat.luk * .5f).ToString(),
            (combat.dex + combat.intelligence + combat.luk).ToString(), "100%", "100%" };
        for (int i = 0; i < lines.Length; i++) Text(dr, lines[i], 81, 7 + i * 18, 125);
    }
}

public static class EquipWindow
{
    public static bool Visible;
    static Rect rect = new Rect(200, 100, 300, 220);
    public static void Toggle() { Visible = !Visible; }

    static float S => BottomBar.S;
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);

    public static void Draw()
    {
        if (!Visible) return;
        var r = WindowShell.Frame("装备", rect.x, rect.y, rect.width, rect.height, ref Visible);
        bool release = Event.current.type == EventType.Repaint && Pointer.Released;
        // 8 个穿戴槽位（与 EquipSlot.Worn 顺序一致：帽/衣/裤/鞋/手套/披风/盾/武器）。
        // 直接读 Profile 当前所穿，不再写死具体道具 id。
        int[] slots = EquipSlot.Worn;
        float[] colX = { 18, 95 };
        float[] rowY = { 45, 85, 125, 165 };
        var nameStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(11 * S) };
        nameStyle.normal.textColor = Color.black;
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = slots[i];
            float x = i == 8 ? 18 : colX[i / 4], y = i == 8 ? 205 : rowY[i % 4];
            GUI.Label(At(r, x, y, 24, 16), EquipSlot.NameOf(slot), nameStyle);
            int id = Profile.EquipOfSlot(slot);
            var iconRect = At(r, x + 26, y - 2, 30, 30);
            if (id > 0)
            {
                var icon = ArmorIcon(id);
                if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                // 点击脱下（武器必须一直持有，不允许在此卸下；换武器走背包）
                if (release && iconRect.Contains(Pointer.Pos) && slot != EquipSlot.Weapon)
                {
                    if (slot == EquipSlot.Medal) NativeMedalWindow.Wear(id, true);
                    else Profile.SetEquipSlot(slot, 0);
                    BottomBar.Toast("已脱下" + EquipSlot.NameOf(slot));
                }
            }
        }
    }

    public static Texture2D ArmorIcon(int itemId)
    {
        var dir = EquipCatalog.DirOf(itemId);
        if (dir == null) return null;
        return Art.Tex("wz://Character/" + dir + "/" + itemId.ToString("D8") + ".img/info/icon");
    }
}

public static class InvenWindow
{
    public static bool Visible;
    static Vector2 pos = new Vector2(30, 90);
    static bool expanded, dragging;
    static Vector2 grab;
    static int tab, scroll;
    static float S => BottomBar.S;
    static Texture2D Tex(string node) => Art.Tex("wz://UI/UIWindow.img/Item/" + node);
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static void Image(Rect r, string node, float x, float y)
    {
        var tex = Tex(node);
        if (tex != null) GUI.DrawTexture(At(r, x, y, tex.width, tex.height), tex);
    }
    static void Text(Rect r, string value, bool right = false, int size = 11)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(size * S), alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        style.normal.textColor = Color.black;
        GUI.Label(r, value, style);
    }
    public static void Toggle() { Visible = !Visible; }
    public static void Draw()
    {
        if (!Visible) return;
        float width = expanded ? 603 : 175;
        pos.x = Mathf.Clamp(pos.x, 0, Mathf.Max(0, UiKit.W / S - width));
        pos.y = Mathf.Clamp(pos.y, 0, Mathf.Max(0, UiKit.H / S - 289));
        var r = new Rect(pos.x * S, pos.y * S, width * S, 289 * S);
        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;
        bool release = Event.current.type == EventType.Repaint && Pointer.Released;
        if (press && At(r, 0, 0, width - 55, 22).Contains(Pointer.Pos)) { dragging = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down) { pos = Pointer.Pos / S - grab; r.position = pos * S; }
        if (!Pointer.Down) dragging = false;
        Image(r, expanded ? "FullBackgrnd" : "backgrnd", 0, 0);
        if (NativeUi.Button(At(r, width - 19, 5, 12, 12), "UI/Basic.img/BtClose")) { Visible = false; return; }
        if (NativeUi.Button(At(r, width - 49, 5, 12, 12), "UI/UIWindow.img/Item/" + (expanded ? "BtSmall" : "BtFull"))) { expanded = !expanded; scroll = 0; return; }
        if (NativeUi.Button(At(r, width - 34, 5, 12, 12), "UI/UIWindow.img/Item/" + (sorted ? "BtSort" : "BtGather"))) sorted = !sorted;
        for (int i = 0; i < 5; i++)
        {
            string state = i == tab ? "1" : "0";
            GUI.DrawTexture(At(r, 8 + i * 29, 23, 21, 19), Art.Tex("wz://UI/Basic.img/Tab2/fill" + state));
            GUI.DrawTexture(At(r, 4 + i * 29, 23, 4, 19), Art.Tex("wz://UI/Basic.img/Tab2/left" + state));
            GUI.DrawTexture(At(r, 29 + i * 29, 23, 4, 19), Art.Tex("wz://UI/Basic.img/Tab2/right" + state));
            Image(r, "Tab/" + (i == tab ? "enabled" : "disabled") + "/" + i, 5 + i * 29, i == tab ? 24 : 25);
            if (release && At(r, 5 + i * 29, 23, 29, 18).Contains(Pointer.Pos)) { tab = i; scroll = 0; }
        }
        int capacity = Profile.InventoryCapacity(tab);
        int maxScroll = Mathf.Max(0, Mathf.CeilToInt(capacity / 4f) - 6);
        scroll = Mathf.Clamp(scroll, 0, maxScroll);
        var entries = new System.Collections.Generic.List<int>();
        if (tab == 0)
        {
            bool equipped = false;
            foreach (string id in Profile.OwnedWeapons)
            {
                // Equipped item occupies the EQUIPPED inventory, not a bag slot.
                if (!equipped && id == Profile.equipWeapon) { equipped = true; continue; }
                if (int.TryParse(id, out int number)) entries.Add(number);
            }
        }
        entries.AddRange(Profile.InventoryItems(tab));
        if (sorted) entries.Sort();
        if (!expanded && maxScroll > 0 && Event.current.type == EventType.Repaint && At(r, 7, 50, 160, 205).Contains(Pointer.Pos))
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > .01f) scroll = Mathf.Clamp(scroll - (int)Mathf.Sign(wheel), 0, maxScroll);
        }
        for (int cell = 0; cell < (expanded ? 96 : 24); cell++)
        {
            int index = expanded ? cell : scroll * 4 + cell;
            int block = expanded ? cell / 24 : 0;
            int local = cell % 24;
            float x = 7 + block * 148 + local % 4 * 36;
            float y = 50 + local / 4 * 34;
            var slot = At(r, x, y, 32, 32);
            if (index >= capacity) { Image(r, "disabled", x, y); continue; }
            if (index >= entries.Count) continue;
            int id = entries[index];
            Texture2D icon = null;
            if (FieldCatalog.Items.TryGetValue(id, out var info) && !string.IsNullOrEmpty(info.icon)) icon = Art.Tex(info.icon);
            else if (tab == 0 && WeaponDefs.TryGet(id.ToString(), out _)) icon = Art.Tex("wz://Character/Weapon/" + id.ToString("D8") + ".img/info/icon");
            else if (tab == 0 && EquipCatalog.DirOf(id) != null) icon = Art.Tex("wz://Character/" + EquipCatalog.DirOf(id) + "/" + id.ToString("D8") + ".img/info/icon");
            if (icon != null) GUI.DrawTexture(slot, icon, ScaleMode.ScaleToFit);
            if (tab != 0) Text(At(r, x, y + 20, 31, 12), Profile.ItemCount(id).ToString(), true, 9);
            if (release && slot.Contains(Pointer.Pos) && tab == 0)
            {
                int eqSlot = EquipSlot.SlotOf(id);
                if (eqSlot == EquipSlot.Weapon) Profile.EquipWeapon(id.ToString());
                else if (eqSlot != 0)
                {
                    string err = EquipLoadout.Equip(id, Profile.Attributes);
                    if (err != null) BottomBar.Toast(err);
                }
            }
        }
        if (!expanded)
        {
            string root = "wz://UI/Basic.img/VScr4/" + (capacity > 24 ? "enabled/" : "disabled/");
            var track = Art.Tex(root + "base");
            if (track != null) GUI.DrawTexture(At(r, 154, 65, 15, 174), track);
            var prev = At(r, 154, 50, 15, 15); var next = At(r, 154, 239, 15, 15);
            GUI.DrawTexture(prev, Art.Tex(root + (capacity > 24 ? "prev0" : "prev")));
            GUI.DrawTexture(next, Art.Tex(root + (capacity > 24 ? "next0" : "next")));
            if (capacity > 24)
            {
                GUI.DrawTexture(At(r, 154, 65 + scroll * 149f / Mathf.Max(1, maxScroll), 15, 25), Art.Tex(root + "thumb0"));
                if (press && prev.Contains(Pointer.Pos)) scroll = Mathf.Max(0, scroll - 1);
                if (press && next.Contains(Pointer.Pos)) scroll = Mathf.Min(maxScroll, scroll + 1);
            }
        }
        Text(At(r, 27, 267, 113, 16), Profile.gold.ToString(), true);
        if (expanded && NativeUi.Button(At(r, width - 106, 267, 93, 18), "UI/UIWindow.img/Item/BtCashshop"))
            BottomBar.Toast("容量扩充需在商城购买：2000点券增加4格，最多96格");
    }
    static bool sorted;
}

public static class ShopWindow
{
    public static bool Visible;
    public static void Toggle() => NativeCashWindow.Toggle();
    public static void Draw() => NativeCashWindow.Draw();
}

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
            245, i => i < 10, i => {
                if (i == 0) InvenWindow.Toggle();
                if (i == 1) EquipWindow.Toggle();
                if (i == 2) StatWindow.Toggle();
                if (i == 3) SkillWindow.Toggle();
                if (i == 4) NativeSocialWindow.Toggle(GameAction.Party);
                if (i == 5) QuestWindow.Toggle();
                if (i == 6) NativeSocialWindow.Toggle(GameAction.Messenger);
                if (i == 7) NativeCommunityWindow.Toggle(GameAction.Family);
                if (i == 8) NativeSocialWindow.Toggle(GameAction.Buddy);
                if (i == 9) NativeMonsterBook.Toggle();
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
    public static void Toggle() { Visible = !Visible; }

    public static void Draw()
    {
        if (!Visible) return;
        Visible = false;
        // 源端 EnterMTSHandler.openCenterScript：帮助入口打开主菜单脚本。
        if (!NpcSession.Blocking) NpcSession.Talk(9900001);
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
    public static bool AnyVisible =>
        StatWindow.Visible || EquipWindow.Visible || InvenWindow.Visible ||
        ShortcutWindow.Visible || HelpWindow.Visible || GameMenuWindow.Visible ||
        SkillWindow.Visible || QuestWindow.Visible || KeyConfigWindow.Visible ||
        NativeMapWindows.WorldVisible || NativeSocialWindow.Visible || NativeMonsterBook.Visible ||
        NativeCommunityWindow.Visible || NativeMedalWindow.Visible || ShopWindow.Visible;

    public static void DrawAll()
    {
        NativeCashWindow.Tick();
        NativeMapWindows.DrawMini();
        StatWindow.Draw();
        EquipWindow.Draw();
        InvenWindow.Draw();
        ShopWindow.Draw();
        QuestWindow.Draw();
        ShortcutWindow.Draw();
        HelpWindow.Draw();
        GameMenuWindow.Draw();
        SkillWindow.Draw();
        NativeMapWindows.DrawWorld();
        NativeSocialWindow.Draw();
        NativeMonsterBook.Draw();
        NativeCommunityWindow.DrawBoard();
        NativeCommunityWindow.Draw();
        NativeMedalWindow.Draw();
    }
}
