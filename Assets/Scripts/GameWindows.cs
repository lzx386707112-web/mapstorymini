using System.Collections.Generic;
using UnityEngine;

// Window artwork is loaded directly from the original UI IMG files.

public static class WindowShell
{
    public static readonly Color Title = new Color(0.92f, 0.95f, 1f);

    public static bool Button(Rect r, string label, bool on = false)
    {
        return NativeUi.Button(r, "UI/Basic.img/BtOK");
    }

    public static void Label(Rect r, string text, int size = 12, Color? color = null)
    {
        var st = new GUIStyle { fontSize = size };
        st.normal.textColor = color ?? Color.black;
        GUI.Label(r, text, st);
    }
}

// ——————————————— 能力值窗 ———————————————
// 素材：UI/UIWindow.img/Stat/backgrnd 208×347、Stat/backgrnd2 218×203。
// 坐标全部取自 CUIStat::Draw（BeiDou.exe 0x8C59FF 起）里的 push 常量对 (y, x=0x3D)：
//   0x8C5A9C y=0x21(33) 名字   0x8C5B67 y=0x37(55) 职业   0x8C5C73 y=0x4F(79) 等级
//   0x8C5D2D y=0x60(96) 家族   0x8C5E39 y=0x73(115) HP   0x8C5F25 y=0x85(133) MP
//   0x8C603F y=0x97(151) 经验值 0x8C6121 y=0xA9(169) 人气度
// 职业图标由 0x8C5BCF（push 0x35=53, push 0x3E=62）绘制：main 11 高、sub 10 高，正好落在 53..74。
// 四属性数值 0x8C6564 (y=0xF4=244, x=61)，行距 18；AP 数字 (61,213)。
// BtDetail 46×19 于 (124,324)（0x8C4E15 push 0x144 / 0x7C）。
// 原端此窗没有任何音量控件，底部留白由 backgrnd 自带。
public static class StatWindow
{
    public static bool Visible;
    static Vector2 pos = new Vector2(220, 80);
    static bool detail = true, dragging;
    const int WinW = 208, WinH = 347;
    // 六个加点箭头共用右列 x=185（原端 11+ 版式）。
    const int ApArrowX = 185;
    static Vector2 grab;
    static float S => BottomBar.S;
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static Texture2D Tex(string node) => Art.Tex("wz://UI/UIWindow.img/Stat/" + node);
    static void Image(Rect r, string node, float x, float y)
    {
        var tex = Tex(node);
        if (tex != null) GUI.DrawTexture(At(r, x, y, tex.width, tex.height), tex);
    }
    static void Text(Rect r, string text, float x, float y, int width = 135)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(11 * S), clipping = TextClipping.Clip };
        style.normal.textColor = Color.black;
        GUI.Label(At(r, x, y, width, 16), text, style);
    }
    public static void Toggle() { Visible = !Visible; }

    public static void Draw()
    {
        if (!Visible || GameLoop.I == null || GameLoop.I.player == null) return;
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, WinH), S);
        var r = new Rect(pos.x * S, pos.y * S, WinW * S, WinH * S);
        if (Event.current.type == EventType.Repaint && Pointer.Pressed && At(r, 0, 0, WinW - 24, 22).Contains(Pointer.Pos))
        { dragging = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down)
        {
            pos = Pointer.Pos / S - grab;
            pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, WinH), S);
            r.position = pos * S;
        }
        if (!Pointer.Down) dragging = false;
        Image(r, "backgrnd", 0, 0);
        if (NativeUi.Button(At(r, WinW - 19, 5, 12, 12), "UI/Basic.img/BtClose")) { Visible = false; return; }
        var p = GameLoop.I.player;
        var attrs = Profile.Attributes;
        string[] values = { Profile.playerName, "", Profile.level.ToString(), "-",
            Mathf.CeilToInt(p.hp) + " / " + Mathf.CeilToInt(p.hpMax), Mathf.CeilToInt(p.mp) + " / " + Mathf.CeilToInt(p.mpMax),
            Profile.experience + " (" + (Profile.ExpRatio * 100).ToString("0.##") + "%)", "0" };
        int[] textY = { 33, 55, 79, 96, 115, 133, 151, 169 };
        for (int i = 0; i < values.Length; i++) Text(r, values[i], 61, textY[i]);
        // 职业名拆成两张图：main 11 高画在 53，sub 10 高画在 64（两图共用 x=62）。
        int job = Profile.cls == "knight" ? 112 : Profile.cls == "warrior" ? 132 : Profile.cls == "mage" ? 212 : Profile.cls == "priest" ? 232 : Profile.cls == "archer" ? 312 : 0;
        Image(r, "Job/main/" + job / 100, 62, 53);
        Image(r, "Job/sub/" + job / 100 + "/" + job, 62, 64);

        if (Profile.AutomaticAp) Image(r, "basicStat", 8, 195);
        else
        {
            var apStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(11 * S), alignment = TextAnchor.MiddleRight };
            apStyle.normal.textColor = Color.black;
            GUI.Label(At(r, 61, 213, 26, 16), Profile.ap.ToString(), apStyle);
            // 原端 11+ 版式：AP 数字右侧是自动分配控件（100×35 原图按 73×35 显示）
            var auto = Tex("BtAuto/disabled/0");
            if (auto != null) GUI.DrawTexture(At(r, 97, 197, 73, 35), auto);
            int[] stats = { attrs.str, attrs.dex, attrs.intelligence, attrs.luk };
            for (int i = 0; i < stats.Length; i++)
            {
                // 未转职新手只开放 STR/DEX，INT/LUK 用原端 Disabled 素材压住标签
                if (Profile.cls == "beginner" && i >= 2)
                    Image(r, "Disabled/" + (i == 2 ? "INT" : "LUK"), 8, 244 + i * 18);
                Text(r, stats[i].ToString(), 61, 244 + i * 18);
                var up = At(r, ApArrowX, 247 + i * 18, 12, 12);
                if (!Profile.CanAssignAp(i)) Image(r, "BtApUp/disabled/0", ApArrowX, 247 + i * 18);
                else if (NativeUi.Button(up, "UI/UIWindow.img/Stat/BtApUp")) Profile.AssignAp(i);
            }
            // HP/MP 的 AP 转换按钮在原端常驻禁用（涉及职业被动与洗血计数）
            Image(r, "BtApUp/disabled/0", ApArrowX, 117);
            Image(r, "BtApUp/disabled/0", ApArrowX, 135);
        }
        if (NativeUi.Button(At(r, 124, 324, 46, 19), "UI/UIWindow.img/Stat/BtDetail")) detail = !detail;
        if (!detail) return;
        // 详情窗 218×203，与原端一致按主窗底部对齐：347-203=144。
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
            (combat.dex + combat.intelligence + combat.luk).ToString(), "100", "100" };
        // backgrnd2 的九行标签框实测 x=13..75、行高 18（8..170），数值框自 x=76 起。
        for (int i = 0; i < lines.Length; i++) Text(dr, lines[i], 80, 9 + i * 18, 130);
    }
}

// ——————————————— 装备窗 ———————————————
// 素材：Equip/backgrnd 175×304（槽位名已印在底图上）、Equip/FullBackgrnd 208×291。
// 槽位网格实测（两张底图共用）：左上 (5,35)，格子 32×32，步长 33，小窗 5 列、展开态 6 列，
// 各 7 行；5+4*33+32=169、5+5*33+32=202，与底图右边距 6 一致。
// BtDetail 46×19 于 (5,267)（0x7FE222 push 0x10B / 5），点击在两种底图之间切换。
// BtPetEquipShow 67×18 于 (102,280)（0x7FE0F3 push 0x118 / 0x66），宠物装备本项目未实现。
public static class EquipWindow
{
    public static bool Visible;
    static Vector2 pos = new Vector2(200, 100);
    static bool expanded, dragging;
    static Vector2 grab;
    static float S => BottomBar.S;
    const int SmallW = 175, SmallH = 304, FullW = 208, FullH = 291;
    const int GridX = 5, GridY = 35, Step = 33, Cell = 32;
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static Texture2D Tex(string node) => Art.Tex("wz://UI/UIWindow.img/Equip/" + node);

    // 每格对应的原端装备位；0 = 该项目没有实现（戒指/肩饰/腰带/副手/图腾/骑兽等）。
    // 行 0..6 自上而下、列 0..4 自左而右，按 backgrnd 上的中文槽位名对齐：
    //   帽子 / 勋章 额饰 戒指 戒指 / 眼饰 耳饰 肩饰 / 披风 上衣 项链 武器 盾牌 /
    //   手套 裤裙 腰带 指环 指环 / 鞋子 副手 图腾 / 骑兽 鞍子 心脏 徽章 纹章
    static readonly int[,] SmallCells =
    {
        { 0, EquipSlot.Hat, 0, 0, 0 },
        { EquipSlot.Medal, EquipSlot.FaceAccessory, 0, 0, 0 },
        { 0, 0, EquipSlot.EyeAccessory, EquipSlot.Earring, 0 },
        { EquipSlot.Cape, EquipSlot.Top, 0, EquipSlot.Weapon, EquipSlot.Shield },
        { EquipSlot.Glove, EquipSlot.Bottom, 0, 0, 0 },
        { 0, 0, EquipSlot.Shoes, 0, 0 },
        { 0, 0, 0, 0, 0 },
    };

    // 展开态 6 列，同样按 FullBackgrnd 的槽位名对齐：
    //   帽子 / 勋章 脸饰 戒指 戒指 / 眼饰 耳环 / 披风 上衣 项链 项链 武器 盾牌 /
    //   手套 裤裙 腰带 戒指 戒指 / 鞋子 宠物MP / 骑兽 鞍子 宠物装备 宠物装饰 宠物HP
    static readonly int[,] FullCells =
    {
        { 0, EquipSlot.Hat, 0, 0, 0, 0 },
        { EquipSlot.Medal, EquipSlot.FaceAccessory, 0, 0, 0, 0 },
        { 0, 0, EquipSlot.EyeAccessory, 0, EquipSlot.Earring, 0 },
        { EquipSlot.Cape, EquipSlot.Top, 0, 0, EquipSlot.Weapon, EquipSlot.Shield },
        { EquipSlot.Glove, EquipSlot.Bottom, 0, 0, 0, 0 },
        { 0, 0, EquipSlot.Shoes, 0, 0, 0 },
        { 0, 0, 0, 0, 0, 0 },
    };

    public static void Toggle() { Visible = !Visible; }

    public static void Draw()
    {
        if (!Visible) return;
        int w = expanded ? FullW : SmallW, h = expanded ? FullH : SmallH;
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(w, h), S);
        var r = new Rect(pos.x * S, pos.y * S, w * S, h * S);
        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;
        bool release = Event.current.type == EventType.Repaint && Pointer.Released;
        if (press && At(r, 0, 0, w - 24, 22).Contains(Pointer.Pos)) { dragging = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down)
        {
            pos = NativeUi.ClampWindowPosition(Pointer.Pos / S - grab, new Vector2(w, h), S);
            r.position = pos * S;
        }
        if (!Pointer.Down) dragging = false;
        var bg = Tex(expanded ? "FullBackgrnd" : "backgrnd");
        if (bg != null) GUI.DrawTexture(r, bg);
        if (NativeUi.Button(At(r, w - 19, 5, 12, 12), "UI/Basic.img/BtClose")) { Visible = false; return; }

        var cells = expanded ? FullCells : SmallCells;
        int rows = cells.GetLength(0), cols = cells.GetLength(1);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                int slot = cells[row, col];
                if (slot == 0) continue;
                var cellRect = At(r, GridX + col * Step, GridY + row * Step, Cell, Cell);
                int id = Profile.EquipOfSlot(slot);
                if (id <= 0) continue;
                var icon = IconOf(slot, id);
                if (icon != null) GUI.DrawTexture(cellRect, icon, ScaleMode.ScaleToFit);
                if (Event.current.type == EventType.Repaint && id > 0 && cellRect.Contains(Pointer.Pos)) ItemTooltip.SetHover(id, true);
                // 双击脱下：武器必须一直持有，卸武器走背包换装
                if (Event.current.type == EventType.Repaint && Pointer.DoubleClicked && cellRect.Contains(Pointer.Pos) && slot != EquipSlot.Weapon)
                {
                    Pointer.ConsumeClick();
                    if (slot == EquipSlot.Medal) NativeMedalWindow.Wear(id, true);
                    else EquipLoadout.Unequip(slot);
                    if (Profile.EquipOfSlot(slot) == 0) BottomBar.Toast("已脱下" + EquipSlot.NameOf(slot));
                }
            }

        if (NativeUi.Button(At(r, 5, 267, 46, 19), "UI/UIWindow.img/Equip/BtDetail")) { expanded = !expanded; return; }
        if (!expanded && NativeUi.Button(At(r, 102, 280, 67, 18), "UI/UIWindow.img/Equip/BtPetEquipShow"))
            BottomBar.Toast("宠物装备暂未开放");
    }

    static Texture2D IconOf(int slot, int id)
    {
        if (slot == EquipSlot.Weapon)
            return Art.Tex("wz://Character/Weapon/" + id.ToString("D8") + ".img/info/icon");
        return ArmorIcon(id);
    }

    public static Texture2D ArmorIcon(int itemId)
    {
        var dir = EquipCatalog.DirOf(itemId);
        if (dir == null) return null;
        return Art.Tex("wz://Character/" + dir + "/" + itemId.ToString("D8") + ".img/info/icon");
    }
}

// ——————————————— 背包窗 ———————————————
// 素材：Item/backgrnd 175×289、Item/FullBackgrnd 603×289。
// 页签控件 (x=3, y=23, 宽 170 = 5×34) 见 0x81C978；图元 Item/Tab/{enabled,disabled}/i 27×16；
//   底板用 Basic.img/Tab2 的 left/fill/right 切片（4×19 + 1×19 平铺 + 4×19）。
// 滚动条 (x=152, y=51, 长 200) 见 0x81CCCF，素材 Basic.img/VScr4（base/prev/next 15×13、thumb 15×25）。
// BtCoin 14×14 于 (7,266)（0x81CD74）；BtCashshop 93×18 原点(46,8) 中心 (495,266)
//   → 左上 (449,258)（0x81CF0E）。
// 格子实测：小窗起 (8,50)、步长 36×34、格 32、4 列×6 行；展开态 4 组，组距 149、起 x=7。
public static class InvenWindow
{
    public static bool Visible;
    static Vector2 pos = new Vector2(30, 90);
    static bool expanded, dragging, sorted;
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

    // 页签底板：Tab2 左右各 4×19 + 1×19 平铺中段
    static void TabPlate(Rect r, bool on)
    {
        string state = on ? "1" : "0";
        var left = Art.Tex("wz://UI/Basic.img/Tab2/left" + state);
        var fill = Art.Tex("wz://UI/Basic.img/Tab2/fill" + state);
        var right = Art.Tex("wz://UI/Basic.img/Tab2/right" + state);
        float edge = 4f * S;
        if (fill != null) GUI.DrawTexture(new Rect(r.x + edge, r.y, r.width - edge * 2, r.height), fill);
        if (left != null) GUI.DrawTexture(new Rect(r.x, r.y, edge, r.height), left);
        if (right != null) GUI.DrawTexture(new Rect(r.xMax - edge, r.y, edge, r.height), right);
    }

    public static void Draw()
    {
        if (!Visible) return;
        int width = expanded ? 603 : 175;
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(width, 289), S);
        var r = new Rect(pos.x * S, pos.y * S, width * S, 289 * S);
        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;
        bool release = Event.current.type == EventType.Repaint && Pointer.Released;
        if (press && At(r, 0, 0, width - 50, 22).Contains(Pointer.Pos)) { dragging = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down)
        {
            pos = NativeUi.ClampWindowPosition(Pointer.Pos / S - grab, new Vector2(width, 289), S);
            r.position = pos * S;
        }
        if (!Pointer.Down) dragging = false;
        Image(r, expanded ? "FullBackgrnd" : "backgrnd", 0, 0);

        // 页签：控件 x=3、y=23、单页宽 34，图元 27×16 居中
        for (int i = 0; i < 5; i++)
        {
            bool on = i == tab;
            var plate = At(r, 3 + i * 34, 23, 34, 19);
            TabPlate(plate, on);
            Image(r, "Tab/" + (on ? "enabled" : "disabled") + "/" + i, 3 + i * 34 + (34 - 27) * 0.5f, on ? 24 : 25);
            if (release && plate.Contains(Pointer.Pos)) { tab = i; scroll = 0; }
        }

        int capacity = Profile.InventoryCapacity(tab);
        int maxScroll = Mathf.Max(0, Mathf.CeilToInt(capacity / 4f) - 6);
        scroll = Mathf.Clamp(scroll, 0, maxScroll);
        var entries = new List<int>();
        List<InventoryEntry> equipment = null;
        if (tab == 0 && GameSession.InGame)
        {
            equipment = GameSession.Character.inventory.FindAll(item => item.inventoryType == 1 && item.position > 0);
            equipment.Sort((left, right) => sorted ? left.itemId.CompareTo(right.itemId) : left.position.CompareTo(right.position));
            foreach (var item in equipment) entries.Add(item.itemId);
        }
        else if (tab == 0)
        {
            bool equipped = false;
            foreach (string id in Profile.OwnedWeapons)
            {
                // 已装备的武器占 EQUIPPED 位，不再占用背包格
                if (!equipped && id == Profile.equipWeapon) { equipped = true; continue; }
                if (int.TryParse(id, out int number)) entries.Add(number);
            }
        }
        if (equipment == null) entries.AddRange(Profile.InventoryItems(tab));
        if (sorted && equipment == null) entries.Sort();
        if (!expanded && maxScroll > 0 && Event.current.type == EventType.Repaint && At(r, 8, 50, 148, 202).Contains(Pointer.Pos))
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > .01f) scroll = Mathf.Clamp(scroll - (int)Mathf.Sign(wheel), 0, maxScroll);
        }

        for (int cell = 0; cell < (expanded ? 96 : 24); cell++)
        {
            int index = expanded ? cell : scroll * 4 + cell;
            int block = expanded ? cell / 24 : 0;
            int local = cell % 24;
            float x = (expanded ? 7 : 8) + block * (expanded ? 149 : 0) + local % 4 * 36;
            float y = 50 + local / 4 * 34;
            var slot = At(r, x, y, 32, 32);
            if (index >= capacity) { Image(r, "disabled", x, y); continue; }
            if (index >= entries.Count) continue;
            int id = entries[index];
            var icon = NativeItemSource.Icon(id);
            if (icon != null) GUI.DrawTexture(slot, icon, ScaleMode.ScaleToFit);
            if (tab != 0) Text(At(r, x, y + 20, 31, 12), Profile.ItemCount(id).ToString(), true, 9);
            if (Event.current.type == EventType.Repaint && id > 0 && slot.Contains(Pointer.Pos)) ItemTooltip.SetHover(id, false, equipment == null ? null : equipment[index]);
            if (Event.current.type == EventType.Repaint && Pointer.DoubleClicked && slot.Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick();
                NativeInventoryActions.DoubleClick(id, equipment == null ? 0 : equipment[index].position);
                break; // The action can change the inventory and open another window.
            }
        }

        if (!expanded)
        {
            // 滚动条：x=152、y=51、长 200（0x81CCCF）
            string root = "wz://UI/Basic.img/VScr4/" + (capacity > 24 ? "enabled/" : "disabled/");
            var track = Art.Tex(root + "base");
            const float sx = 152, sy = 51, sh = 200;
            if (track != null)
                for (float y = sy + 13; y < sy + sh - 13; y += 13)
                    GUI.DrawTexture(At(r, sx, y, 15, Mathf.Min(13, sy + sh - 13 - y)), track);
            var prev = At(r, sx, sy, 15, 13);
            var next = At(r, sx, sy + sh - 13, 15, 13);
            var prevTex = Art.Tex(root + (capacity > 24 ? "prev0" : "prev"));
            var nextTex = Art.Tex(root + (capacity > 24 ? "next0" : "next"));
            if (prevTex != null) GUI.DrawTexture(prev, prevTex);
            if (nextTex != null) GUI.DrawTexture(next, nextTex);
            if (capacity > 24)
            {
                float span = sh - 13 - 13 - 25;
                GUI.DrawTexture(At(r, sx, sy + 13 + scroll * span / Mathf.Max(1, maxScroll), 15, 25), Art.Tex(root + "thumb0"));
                if (press && prev.Contains(Pointer.Pos)) scroll = Mathf.Max(0, scroll - 1);
                if (press && next.Contains(Pointer.Pos)) scroll = Mathf.Min(maxScroll, scroll + 1);
            }
        }

        // 金币：BtCoin (7,266) 14×14，数字框 backgrnd 自带于 (25,267)-(142,280)
        Image(r, "BtCoin/normal/0", 7, 266);
        Text(At(r, 25, 267, 115, 14), Profile.gold.ToString(), true);

        // 81CE50 / 81E47D：内部宽 W-20，再分别减30/15；基类负责最右侧关闭。
        if (NativeUi.Button(At(r, width - 50, 6, 12, 12), "UI/UIWindow.img/Item/" + (expanded ? "BtSmall" : "BtFull"))) { expanded = !expanded; scroll = 0; return; }
        if (NativeUi.Button(At(r, width - 35, 6, 12, 12), "UI/UIWindow.img/Item/" + (sorted ? "BtSort" : "BtGather"))) sorted = !sorted;
        if (NativeUi.Button(At(r, width - 20, 6, 12, 12), "UI/Basic.img/BtClose")) { Visible = false; return; }
        if (expanded && NativeUi.Button(At(r, 449, 258, 93, 18), "UI/UIWindow.img/Item/BtCashshop"))
            BottomBar.Toast("容量扩充需在商城购买：2000点券增加4格，最多96格");
    }
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

    // ——————————————— Esc 分层关闭 ———————————————
    // 原版 Esc 是「系统菜单」键，但用户要求：只要有面板开着，Esc 先关面板。
    // 为了「后开的先关」，这里按打开顺序记录一个栈；打开入口五花八门
    // （快捷键 / 底部栏按钮 / 快捷菜单），所以在绘制阶段统一观测可见性变化，
    // 不要求每个入口都记得来登记。
    enum Panel
    {
        Stat, Equip, Inven, Shop, Quest, Shortcut, Help, GameMenu, Skill,
        World, Social, MonsterBook, Community, Medal,
    }
    static readonly Panel[] panels = (Panel[])System.Enum.GetValues(typeof(Panel));
    static readonly List<Panel> openOrder = new List<Panel>();
    static readonly Dictionary<Panel, bool> lastVisible = new Dictionary<Panel, bool>();

    static bool VisibleOf(Panel p)
    {
        switch (p)
        {
            case Panel.Stat: return StatWindow.Visible;
            case Panel.Equip: return EquipWindow.Visible;
            case Panel.Inven: return InvenWindow.Visible;
            case Panel.Shop: return ShopWindow.Visible;
            case Panel.Quest: return QuestWindow.Visible;
            case Panel.Shortcut: return ShortcutWindow.Visible;
            case Panel.Help: return HelpWindow.Visible;
            case Panel.GameMenu: return GameMenuWindow.Visible;
            case Panel.Skill: return SkillWindow.Visible;
            case Panel.World: return NativeMapWindows.WorldVisible;
            case Panel.Social: return NativeSocialWindow.Visible;
            case Panel.MonsterBook: return NativeMonsterBook.Visible;
            case Panel.Community: return NativeCommunityWindow.Visible;
            case Panel.Medal: return NativeMedalWindow.Visible;
        }
        return false;
    }

    static void ClosePanel(Panel p)
    {
        switch (p)
        {
            case Panel.Stat: StatWindow.Visible = false; return;
            case Panel.Equip: EquipWindow.Visible = false; return;
            case Panel.Inven: InvenWindow.Visible = false; return;
            // 商城开着的期间 timeScale=0，必须走 Reset 才会把时间轴还回去
            case Panel.Shop: NativeCashWindow.Reset(); return;
            case Panel.Quest: QuestWindow.Visible = false; return;
            case Panel.Shortcut: ShortcutWindow.Visible = false; return;
            case Panel.Help: HelpWindow.Visible = false; return;
            case Panel.GameMenu: GameMenuWindow.Visible = false; return;
            case Panel.Skill: SkillWindow.Visible = false; return;
            case Panel.World: NativeMapWindows.CloseWorld(); return;
            case Panel.Social: NativeSocialWindow.Close(); return;
            case Panel.MonsterBook: NativeMonsterBook.Visible = false; return;
            case Panel.Community: NativeCommunityWindow.Reset(); return;
            case Panel.Medal: NativeMedalWindow.Visible = false; return;
        }
    }

    public static void TrackPanels()
    {
        foreach (var p in panels)
        {
            bool now = VisibleOf(p);
            lastVisible.TryGetValue(p, out bool before);
            if (now == before) continue;
            lastVisible[p] = now;
            openOrder.Remove(p);
            if (now) openOrder.Add(p);
        }
    }

    // 关掉最后打开的那个面板；没有面板时返回 false（调用方去开系统菜单）。
    public static bool CloseTop()
    {
        TrackPanels();
        if (openOrder.Count == 0) return false;
        var top = openOrder[openOrder.Count - 1];
        openOrder.RemoveAt(openOrder.Count - 1);
        ClosePanel(top);
        lastVisible[top] = VisibleOf(top);
        return true;
    }

    public static void DrawAll()
    {
        TrackPanels();
        ItemTooltip.Reset();
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
        ItemTooltip.Flush();
    }
}
