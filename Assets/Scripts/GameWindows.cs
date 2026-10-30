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
    public static InventoryEntry EquipmentAt(Vector2 pointer)
    {
        if(!Visible || GameSession.Character==null) return null;
        int w=expanded ? FullW : SmallW,h=expanded ? FullH : SmallH;
        var origin=NativeUi.ClampWindowPosition(pos,new Vector2(w,h),S)*S;
        var r=new Rect(origin.x,origin.y,w*S,h*S);
        var cells=expanded ? FullCells : SmallCells;
        for(int row=0;row<cells.GetLength(0);row++) for(int col=0;col<cells.GetLength(1);col++)
            if(cells[row,col]!=0 && At(r,GridX+col*Step,GridY+row*Step,Cell,Cell).Contains(pointer))
                return GameSession.Character.inventory.Find(i=>i.position==cells[row,col] && i.inventoryType==1);
        return null;
    }
    public static bool Visible;
    static Vector2 pos = new Vector2(200, 100);
    static bool expanded, dragging;
    static Vector2 grab;
    static float S => BottomBar.S;
    const int SmallW = 175, SmallH = 304, FullW = 208, FullH = 291;
    const int GridX = 5, GridY = 35, Step = 33, Cell = 32;
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static Texture2D Tex(string node) => Art.Tex("wz://UI/UIWindow.img/Equip/" + node);

    // 每格对应原 IMG 背景已有的装备位；0 = 空白或未迁移的骑兽/宠物位。
    // 行 0..6 自上而下、列 0..4 自左而右，按 backgrnd 上的中文槽位名对齐：
    //   帽子 / 勋章 额饰 戒指 戒指 / 眼饰 耳饰 肩饰 / 披风 上衣 项链 武器 盾牌 /
    //   手套 裤裙 腰带 指环 指环 / 鞋子 副手 图腾 / 骑兽 鞍子 心脏 徽章 纹章
    static readonly int[,] SmallCells =
    {
        { 0, EquipSlot.Hat, 0, 0, 0 },
        { EquipSlot.Medal, EquipSlot.FaceAccessory, 0, -12, -13 },
        { 0, 0, EquipSlot.EyeAccessory, EquipSlot.Earring, -20 },
        { EquipSlot.Cape, EquipSlot.Top, -17, EquipSlot.Weapon, EquipSlot.Shield },
        { EquipSlot.Glove, EquipSlot.Bottom, -50, -15, -16 },
        { 0, 0, EquipSlot.Shoes, -51, -53 },
        { 0, 0, -54, -55, -56 },
    };

    // 展开态 6 列，同样按 FullBackgrnd 的槽位名对齐：
    //   帽子 / 勋章 脸饰 戒指 戒指 / 眼饰 耳环 / 披风 上衣 项链 项链 武器 盾牌 /
    //   手套 裤裙 腰带 戒指 戒指 / 鞋子 宠物MP / 骑兽 鞍子 宠物装备 宠物装饰 宠物HP
    static readonly int[,] FullCells =
    {
        { 0, EquipSlot.Hat, 0, 0, 0, 0 },
        { EquipSlot.Medal, EquipSlot.FaceAccessory, 0, 0, -12, -13 },
        { 0, 0, EquipSlot.EyeAccessory, 0, EquipSlot.Earring, 0 },
        { EquipSlot.Cape, EquipSlot.Top, -17, 0, EquipSlot.Weapon, EquipSlot.Shield },
        { EquipSlot.Glove, EquipSlot.Bottom, -50, 0, -15, -16 },
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
    static readonly List<KeyValuePair<Rect,InventoryEntry>> equipmentTargets=new List<KeyValuePair<Rect,InventoryEntry>>();
    public static InventoryEntry EquipmentAt(Vector2 pointer)
    {
        if(!Visible || tab!=0) return null;
        foreach(var target in equipmentTargets) if(target.Key.Contains(pointer)) return target.Value;
        return null;
    }
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

        if(Event.current.type==EventType.Repaint) equipmentTargets.Clear();
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
            if(Event.current.type==EventType.Repaint && equipment!=null) equipmentTargets.Add(new KeyValuePair<Rect,InventoryEntry>(slot,equipment[index]));
            var icon = NativeItemSource.Icon(id);
            if (icon != null) GUI.DrawTexture(slot, icon, ScaleMode.ScaleToFit);
            if (tab != 0) Text(At(r, x, y + 20, 31, 12), Profile.ItemCount(id).ToString(), true, 9);
            if (Event.current.type == EventType.Repaint && id > 0 && slot.Contains(Pointer.Pos)) ItemTooltip.SetHover(id, false, equipment == null ? null : equipment[index]);
            if (press && NativeTradeWindow.CanOffer && slot.Contains(Pointer.Pos))
            {
                var offered = equipment == null ? GameSession.Character.inventory.Find(item => item.itemId == id && item.inventoryType == tab + 1 && item.position > 0) : equipment[index];
                NativeTradeWindow.BeginItemDrag(offered);
            }
            if (press && !NativeTradeWindow.CanOffer && slot.Contains(Pointer.Pos)) NativeInventoryActions.Pick(id,equipment==null ? 0 : equipment[index].position);
            if (Event.current.type == EventType.Repaint && Pointer.DoubleClicked && slot.Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick();
                if (!NativeTradeWindow.CanOffer) NativeInventoryActions.DoubleClick(id, equipment == null ? 0 : equipment[index].position);
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

// ——————————————— 底栏两个弹出菜单（目录 / 界面） ———————————————
// 几何全部来自源端：面板 93 宽，右缘与大按钮右缘对齐，底边贴按钮顶边（y=543）。
//   目录（BtMenu，第 3 枚大按钮）  backgrnd 93×140 + 4 个 81×25 按钮，行距 26（y=24+26i）
//   界面（BtShort，第 4 枚大按钮） backgrnd 93×245 + 11 个 81×25 按钮，行距 21（y=5+21i）
// 首行 y=24 不是随手取的：源端 GameMenu 构建函数（0x849F3E）里的控件坐标就是 24/50/76/102，
// 空出来的 24px 正好是底图上印死的标题（「主菜单」/「界面」）。
// 两个菜单互斥：开一个自动收起另一个；同一下标按钮再点一次收起。
// 两者都是浮层，点到面板外就收起——唯独点在两个菜单按钮上时交给 BottomBar.PressBig 决定，
// 否则会出现「外点收起 + 同一下点击又把它打开」的抖动。
public static class BottomMenu
{
    public const int None = -1, Catalog = 0, Interface = 1;

    public static int Open { get; private set; } = None;
    public static bool IsOpen(int kind) { return Open == kind; }
    public static bool AnyOpen { get { return Open != None; } }
    public static void Toggle(int kind) { Open = Open == kind ? None : kind; }
    public static void Show(int kind) { Open = kind; }
    public static void Close() { Open = None; }

    // 源端 0x849F3E 只建这 4 个按钮（y=24/50/76/102、id 1000..1003）。
    // BtSkin（「场景变更 / SKIN CHANGE」）的 4 态素材在 WZ 里齐全，但 exe 里**没有任何代码引用**它
    // （字符串池 id 2207，8115 个 GetString 调用点零命中），也没有配套窗口底图 —— 是上游留下的死素材，
    // 源端菜单里根本没有这一项，所以这里也不摆。
    static readonly string[] CatalogButtons = { "BtChannel", "BtGameOpt", "BtSysOpt", "BtQuit" };
    static readonly string[] InterfaceButtons =
    {
        "BtItem", "BtEquip", "BtStat", "BtSkill", "BtParty", "BtQuest",
        "BtMessenger", "BtGuild", "BtComm", "BtMobbook", "BtRanking",
    };

    static string[] Buttons(int kind) { return kind == Catalog ? CatalogButtons : InterfaceButtons; }
    static int Height(int kind) { return kind == Catalog ? 140 : 245; }
    static int Pitch(int kind) { return kind == Catalog ? 26 : 21; }
    static int AnchorButton(int kind) { return kind == Catalog ? 2 : 3; }
    static string MenuArt(int kind, string node)
    {
        return "ui/" + (kind == Catalog ? "gamemenu" : "shortcut") + "/" + node + ".bytes";
    }

    // 首行：目录 24（让开底图标题），界面 5（11 项要填满 245，源端那 8 项的排法塞不下）
    static int FirstRow(int kind) { return kind == Catalog ? 24 : 5; }

    // 源端只有「我的排行」（BtRanking）没做，其余界面项都在；目录 4 项全可用。
    static bool Enabled(int kind, int index)
    {
        return kind == Catalog ? true : index < InterfaceButtons.Length - 1;
    }

    static string DisabledHint(int kind, int index)
    {
        return "我的排行：源端未实现，手机端未开放";
    }

    static void Click(int kind, int index)
    {
        if (kind == Interface) { ClickInterface(index); return; }
        switch (index)
        {
            case 0: ChannelWindow.Toggle(); break;
            case 1: GameOptWindow.Toggle(); break;
            case 2: SysOptWindow.Toggle(); break;
            case 3: ConfirmQuit(); break;
        }
    }

    static void ClickInterface(int index)
    {
        if (index == 0) InvenWindow.Toggle();
        if (index == 1) EquipWindow.Toggle();
        if (index == 2) StatWindow.Toggle();
        if (index == 3) SkillWindow.Toggle();
        if (index == 4) NativeSocialWindow.Toggle(GameAction.Party);
        if (index == 5) QuestWindow.Toggle();
        if (index == 6) NativeSocialWindow.Toggle(GameAction.Messenger);
        if (index == 7) NativeGuildWindow.Toggle();
        if (index == 8) NativeSocialWindow.Toggle(GameAction.Buddy);
        if (index == 9) NativeMonsterBook.Toggle();
    }

    // 源端「游戏结束」直接退客户端；手机端退出就是杀进程，加一道 YesNo 确认免得误触。
    static void ConfirmQuit()
    {
        NativeUtilPrompt.Confirm("确定要结束游戏吗？", ok => { if (ok) Application.Quit(); });
    }

    // —— 几何：面板/条目矩形对外公开，绘制与无渲染断言（MenuLayoutChecks）共用同一份算法 ——
    // 面板 93 宽；右缘与大按钮右缘对齐、底边贴按钮顶边；窄屏/竖屏时夹回可视区。
    public static Rect PanelRect(int kind)
    {
        float s = BottomBar.S;
        var anchor = BottomBar.BigButtonRect(AnchorButton(kind));
        float w = 93f * s, h = Height(kind) * s;
        float x = Mathf.Clamp(anchor.xMax - w, 0f, Mathf.Max(0f, UiKit.W - w));
        float y = Mathf.Clamp(anchor.y - h, 0f, Mathf.Max(0f, UiKit.H - h));
        return new Rect(x, y, w, h);
    }

    public static Rect ItemRect(int kind, int index)
    {
        var panel = PanelRect(kind);
        float s = BottomBar.S;
        return new Rect(panel.x + 6f * s, panel.y + (FirstRow(kind) + index * Pitch(kind)) * s, 81f * s, 25f * s);
    }

    public static int ItemCount(int kind) { return Buttons(kind).Length; }
    public static bool ItemEnabled(int kind, int index) { return Enabled(kind, index); }

    // 按钮文字是源端 IMG 里烤死的，这两份名字只给日志/断言读，别拿去画界面。
    static readonly string[] CatalogNames = { "频道变更", "游戏设置", "系统设置", "游戏结束" };
    static readonly string[] InterfaceNames =
    {
        "物品", "装备", "能力值", "技能", "组队", "任务", "好友", "公会", "社区", "怪物图鉴", "我的排行",
    };
    public static string NameOf(int kind, int index)
    {
        var names = kind == Catalog ? CatalogNames : InterfaceNames;
        return index >= 0 && index < names.Length ? names[index] : "?";
    }

    public static void Draw(int kind)
    {
        if (!IsOpen(kind)) return;
        var panel = PanelRect(kind);

        var bg = Art.Tex(MenuArt(kind, "backgrnd"));
        if (bg != null) GUI.DrawTexture(panel, bg, ScaleMode.StretchToFill);

        var buttons = Buttons(kind);
        bool blocking = NpcSession.Blocking || KeyConfigWindow.Visible || VirtualControlsEditor.Visible;

        for (int i = 0; i < buttons.Length; i++)
        {
            var rect = ItemRect(kind, i);
            bool enabled = Enabled(kind, i);
            bool hover = rect.Contains(Pointer.Pos) && !blocking;
            string state = !enabled ? "disabled" : hover ? (Pointer.Down ? "pressed" : "mouseOver") : "normal";
            var tex = Art.Tex(MenuArt(kind, buttons[i] + "_" + state)) ?? Art.Tex(MenuArt(kind, buttons[i] + "_normal"));
            if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);

            if (!hover || Event.current.type != EventType.Repaint || !Pointer.Released) continue;
            Pointer.ConsumeClick();
            if (!enabled) { BottomBar.Toast(DisabledHint(kind, i)); continue; }
            AudioManager.Play("UI/BtMouseClick", 0.7f, false);
            Close();
            Click(kind, i);
            return;
        }

        if (blocking || Event.current.type != EventType.Repaint || !Pointer.Clicked) return;
        if (panel.Contains(Pointer.Pos)) return;
        // 点到底栏两个菜单按钮时不在这里关：那一下由 PressBig 去 Toggle，避免同一点击先关后开
        if (BottomBar.BigButtonRect(2).Contains(Pointer.Pos) ||
            BottomBar.BigButtonRect(3).Contains(Pointer.Pos)) return;
        Close();
    }
}

public static class ShortcutWindow
{
    public static bool Visible
    {
        get { return BottomMenu.IsOpen(BottomMenu.Interface); }
        set
        {
            if (value) BottomMenu.Show(BottomMenu.Interface);
            else if (Visible) BottomMenu.Close();
        }
    }
    public static void Toggle() { BottomMenu.Toggle(BottomMenu.Interface); }
    public static void Draw() { BottomMenu.Draw(BottomMenu.Interface); }
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
    public static bool Visible
    {
        get { return BottomMenu.IsOpen(BottomMenu.Catalog); }
        set
        {
            if (value) BottomMenu.Show(BottomMenu.Catalog);
            else if (Visible) BottomMenu.Close();
        }
    }
    public static void Toggle() { BottomMenu.Toggle(BottomMenu.Catalog); }
    public static void Draw() { BottomMenu.Draw(BottomMenu.Catalog); }
}

// 所有浮窗的统一绘制顺序（技能窗在最后，压在虚拟键盘之上）
public static class GameWindows
{
    public static bool AnyVisible =>
        StatWindow.Visible || EquipWindow.Visible || InvenWindow.Visible ||
        ShortcutWindow.Visible || HelpWindow.Visible || GameMenuWindow.Visible ||
        SkillWindow.Visible || QuestWindow.Visible || KeyConfigWindow.Visible ||
        NativeMapWindows.WorldVisible || NativeSocialWindow.Visible || NativeMonsterBook.Visible ||
        NativeCommunityWindow.Visible || NativeGuildWindow.Visible || NativeGuildGradeWindow.Visible ||
        NativeMedalWindow.Visible || ShopWindow.Visible || GameOptWindow.Visible ||
        SysOptWindow.Visible || ChannelWindow.Visible ||
        NativeVegaWindow.Visible || NativeResetWindow.Visible || NativeTradeWindow.Visible || NativeUtilPrompt.Visible || NativeCharacterMenu.Visible || NativeMarketWindow.Visible || NativeNpcShopWindow.Visible;

    // ——————————————— Esc 分层关闭 ———————————————
    // 原版 Esc 是「系统菜单」键，但用户要求：只要有面板开着，Esc 先关面板。
    // 为了「后开的先关」，这里按打开顺序记录一个栈；打开入口五花八门
    // （快捷键 / 底部栏按钮 / 快捷菜单），所以在绘制阶段统一观测可见性变化，
    // 不要求每个入口都记得来登记。
    enum Panel
    {
        Stat, Equip, Inven, Shop, Quest, Shortcut, Help, GameMenu, Skill,
        World, Social, MonsterBook, Community, Guild, Medal, NpcShop,
        GameOpt, SysOpt, Channel,
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
            case Panel.NpcShop: return NativeNpcShopWindow.Visible;
            case Panel.Quest: return QuestWindow.Visible;
            case Panel.Shortcut: return ShortcutWindow.Visible;
            case Panel.Help: return HelpWindow.Visible;
            case Panel.GameMenu: return GameMenuWindow.Visible;
            case Panel.Skill: return SkillWindow.Visible;
            case Panel.World: return NativeMapWindows.WorldVisible;
            case Panel.Social: return NativeSocialWindow.Visible;
            case Panel.MonsterBook: return NativeMonsterBook.Visible;
            case Panel.Community: return NativeCommunityWindow.Visible;
            case Panel.Guild: return NativeGuildWindow.Visible || NativeGuildGradeWindow.Visible;
            case Panel.Medal: return NativeMedalWindow.Visible;
            case Panel.GameOpt: return GameOptWindow.Visible;
            case Panel.SysOpt: return SysOptWindow.Visible;
            case Panel.Channel: return ChannelWindow.Visible;
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
            case Panel.NpcShop: NativeNpcShopWindow.Close(); return;
            case Panel.Quest: QuestWindow.Visible = false; return;
            case Panel.Shortcut: ShortcutWindow.Visible = false; return;
            case Panel.Help: HelpWindow.Visible = false; return;
            case Panel.GameMenu: GameMenuWindow.Visible = false; return;
            case Panel.Skill: SkillWindow.Visible = false; return;
            case Panel.World: NativeMapWindows.CloseWorld(); return;
            case Panel.Social: NativeSocialWindow.Close(); return;
            case Panel.MonsterBook: NativeMonsterBook.Visible = false; return;
            case Panel.Community: NativeCommunityWindow.Reset(); return;
            case Panel.Guild: NativeGuildWindow.Reset(); return;
            case Panel.Medal: NativeMedalWindow.Visible = false; return;
            case Panel.GameOpt: GameOptWindow.Close(); return;
            case Panel.SysOpt: SysOptWindow.Close(); return;
            case Panel.Channel: ChannelWindow.Close(); return;
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
        if (NativeUtilPrompt.Visible) { NativeUtilPrompt.Cancel(); return true; }
        if (NativeVegaWindow.Visible) { if(!NativeVegaWindow.Busy) NativeVegaWindow.Close(); return true; }
        if (NativeResetWindow.Visible) { NativeResetWindow.Close(); return true; }
        if (NativeCharacterMenu.Visible) { NativeCharacterMenu.Close(); return true; }
        if (NativeTradeWindow.Visible) { NativeTradeWindow.Close(); return true; }
        if (NativeMarketWindow.Visible) { NativeMarketWindow.Close(); return true; }
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
        bool enabled = GUI.enabled;
        GUI.enabled &= !NativeUtilPrompt.Visible;
        try
        {
        NativeMapWindows.DrawMini();
        StatWindow.Draw();
        EquipWindow.Draw();
        InvenWindow.Draw();
        ShopWindow.Draw();
        QuestWindow.Draw();
        HelpWindow.Draw();
        SkillWindow.Draw();
        NativeMapWindows.DrawWorld();
        NativeSocialWindow.Draw();
        NativeMonsterBook.Draw();
        NativeCommunityWindow.DrawBoard();
        NativeCommunityWindow.Draw();
        NativeGuildWindow.Draw();
        NativeGuildGradeWindow.Draw();   // BtInfo 的附属窗，必须跟着主窗一起进绘制链
        NativeMedalWindow.Draw();
        NativeTradeWindow.Draw();
        NativeMarketWindow.Draw();
        NativeNpcShopWindow.Draw();
        NativeCharacterMenu.Draw();
        // 目录菜单里开出来的三个窗（源端只有底图，实现见 NativeSettingsWindows.cs）
        GameOptWindow.Draw();
        SysOptWindow.Draw();
        ChannelWindow.Draw();
        // 底栏两个弹出菜单画在最后：它是贴着底栏的浮层，要压在其它窗口之上
        GameMenuWindow.Draw();
        ShortcutWindow.Draw();
        NativeVegaWindow.Draw();
        NativeResetWindow.Draw();
        ItemTooltip.Flush();
        }
        finally { GUI.enabled = enabled; }
        NativeUtilPrompt.Draw();
        UiPreviewHook.Emit();
    }
}
