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
    // CUIStat::Create (8C7B19..8C7CDA)：六个箭头共用 x=153。
    const int ApArrowX = 153;
    static Vector2 grab;
    static float S => BottomBar.S;
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static Texture2D Tex(string node) => Art.Tex("wz://UI/UIWindow.img/Stat/" + node);
    static void Image(Rect r, string node, float x, float y)
    {
        var tex = Tex(node);
        if (tex != null) GUI.DrawTexture(At(r, x, y, tex.width, tex.height), tex);
    }
    static void Text(Rect r, string text, float x, float y, int width = 135, int size = 11)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(size * S), clipping = TextClipping.Clip };
        style.normal.textColor = Color.black;
        GUI.Label(At(r, x, y, width, 16), text, style);
    }
    /// <summary>
    /// 四维的「基础值（+叠加值）」：基础 = AP 分配值（Profile.Attributes），
    /// 叠加 = 面板最终值 − 基础值（装备 inc*、套装、潜力、星力、282 成长、增益）。
    /// 无叠加时只写基础值，不给满屏 (+0)。
    /// </summary>
    static string StatLine(int baseValue, int total)
    {
        int bonus = total - baseValue;
        if (bonus == 0) return baseValue.ToString();
        return baseValue + " (" + (bonus > 0 ? "+" : "") + bonus + ")";
    }
    public static void Toggle() { Visible = !Visible; }

    static void DrawAuto(Rect r, string node, int y, bool brawler)
    {
        var texture = Tex(node + "/normal/0");
        if (texture == null) return;
        var rect = At(r, 97, y, texture.width, texture.height);
        var plan = Profile.AutoApPlan(brawler);
        bool enabled = plan != null && System.Array.Exists(plan, n => n > 0);
        if (!enabled) { Image(r, node + "/disabled/0", 97, y); return; }
        if (!NativeUi.Button(rect, "UI/UIWindow.img/Stat/" + node)) return;
        string[] names = { "力量", "敏捷", "智力", "运气" };
        string text = "    能力点将按如下方式分配：\n\n";
        for (int i = 0; i < 4; i++) text += "     " + names[i] + " : +" + plan[i] + "\n";
        int job = Profile.JobId;
        NativeUtilPrompt.Confirm(text, yes =>
        {
            if (!yes || Profile.JobId != job) return;
            // 分配被拒（AP 不足 / 已达上限）必须弹提示框，否则点了确认像没生效
            if (!Profile.AssignAutoAp(plan)) NativeUtilPrompt.Notice("能力点不足，无法按该方案分配。");
        });
    }

    public static void Draw()
    {
        if (!Visible || GameLoop.I == null || GameLoop.I.player == null) return;
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(WinW, WinH), S);
        var r = new Rect(pos.x * S, pos.y * S, WinW * S, WinH * S);
        NativeUi.RegisterWindow("StatWindow", r, () => StatWindow.Visible);
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
        int watk, matk;
        var weapon = WeaponDefs.Get(Profile.equipWeapon);
        var combat = p.ReadCombatAttributes(out watk, out matk);
        string[] values = { Profile.playerName, "", Profile.level.ToString(), "-",
            Mathf.CeilToInt(p.hp) + " / " + combat.maxHp, Mathf.CeilToInt(p.mp) + " / " + combat.maxMp,
            Profile.experience + " (" + (Profile.ExpRatio * 100).ToString("0.##") + "%)", "0" };
        int[] textY = { 33, 55, 79, 96, 115, 133, 151, 169 };
        for (int i = 0; i < values.Length; i++) Text(r, values[i], 61, textY[i]);
        // 职业两行图：源端 CUIStat::Draw 在 y=0x37 处调 0x8C7FB6（职业名 helper，
        // 素材路径就写在该函数里：main / sub/%d，GM 走 main/premium 分支）。
        // 常规职业取 Stat/Job/main/<jobId/100>（11 高，画在 53）与
        // Stat/Job/sub/<jobId/100>/<jobId>（10 高，画在 64），两图共用 x=62。
        // 这里**必须用真实 jobId**：以前按 Profile.cls 猜四转（mage→212），
        // 于是没二转的法师也显示「火毒魔导师」。
        // 骑士团（jobId/100 = 11..15）与 GM（9xx）在素材里没有图，退回一行文本。
        int job = Profile.JobId;
        string jobSub = "Job/sub/" + job / 100 + "/" + job;
        if (Tex(jobSub) != null)
        {
            Image(r, "Job/main/" + job / 100, 62, 53);
            Image(r, jobSub, 62, 64);
        }
        else Text(r, JobDefs.NameOf(job), 61, 55);

        if (Profile.AutomaticAp) Image(r, "basicStat", 8, 195);
        else
        {
            var apStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(11 * S), alignment = TextAnchor.MiddleRight };
            apStyle.normal.textColor = Color.black;
            GUI.Label(At(r, 61, 213, 26, 16), Profile.ap.ToString(), apStyle);
            // 8C7D5C: BtAuto at (97,197), native 100x35; pirate beginner has two choices.
            if (Profile.JobId == 500)
            {
                DrawAuto(r, "BtAuto1", 197, true);
                DrawAuto(r, "BtAuto2", 217, false);
            }
            else DrawAuto(r, "BtAuto", 197, false);
            int[] stats = { combat.str, combat.dex, combat.intelligence, combat.luk };
            // 基础值取存档 AP 分配（与穿戴门槛同源：门槛比面板值，括号里就是面板比基础多出来那部分）
            var baseAttrs = Profile.Attributes;
            int[] baseStats = { baseAttrs.str, baseAttrs.dex, baseAttrs.intelligence, baseAttrs.luk };
            // 数字右边界别压到 x=153 的加号按钮（原端箭头共用 x=153）
            const int StatWidth = ApArrowX - 61 - 4;
            for (int i = 0; i < stats.Length; i++)
            {
                // 未转职新手只开放 STR/DEX，INT/LUK 用原端 Disabled 素材压住标签
                // （判 jobId==0 而不是 cls=="beginner"：cls 只是大类字符串，
                //   骑士团初心者 1000 与冒险家新手共用它，能力点规则却不同）
                if (Profile.JobId == 0 && i >= 2)
                    Image(r, "Disabled/" + (i == 2 ? "INT" : "LUK"), 8, 244 + i * 18);
                string line = StatLine(baseStats[i], stats[i]);
                // 六位数叠加会顶到按钮，长串降一档字号（11 → 9），不裁不挤压
                Text(r, line, 61, 244 + i * 18, StatWidth, line.Length > 12 ? 9 : 11);
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
// BtPetEquipShow 67×18 于 (102,280)（0x7FE0F3 push 0x118 / 0x66），子窗偏移 (172,123)。
public static class EquipWindow
{
    public static InventoryEntry EquipmentAt(Vector2 pointer)
    {
        if(!Visible || GameSession.Character==null) return null;
        int w=expanded ? FullW : SmallW,h=expanded ? FullH : SmallH;
        var origin=NativeUi.ClampWindowPosition(pos,new Vector2(w,h),S)*S;
        var r=new Rect(origin.x,origin.y,w*S,h*S);
        if (petVisible)
        {
            var pet = At(r,172,123,177,181);
            for (int i=0;i<PetCells.GetLength(0);i++)
                if ((PetCells[i,3]<0 || PetCells[i,3]==petIndex) &&
                    At(pet,PetCells[i,1],PetCells[i,2],32,32).Contains(pointer))
                    return GameSession.Character.inventory.Find(item=>item.inventoryType==1 && item.position==-PetCells[i,0]);
        }
        var cells=expanded ? FullCells : SmallCells;
        for(int row=0;row<cells.GetLength(0);row++) for(int col=0;col<cells.GetLength(1);col++)
            if(cells[row,col]!=0 && At(r,GridX+col*Step,GridY+row*Step,Cell,Cell).Contains(pointer))
                return GameSession.Character.inventory.Find(i=>i.position==EquipSlot.WornSlotOf(cells[row,col]) && i.inventoryType==1);
        return null;
    }

    /// <summary>
    /// 指针命中的装备格，回传底图上的<b>普通</b>槽位号（-1..-19/-49..-56，不含现金 -100）。
    /// 背包拖装备进来时用它判断「这一格收不收这件东西」。
    /// </summary>
    public static bool SlotAt(Vector2 pointer,out int cellSlot)
    {
        // 机器人换装面板贴在装备窗右侧，命中优先（两块不重叠，顺序只为少算一次）
        if (NativeAndroidWindow.SlotAt(pointer, out cellSlot)) return true;
        cellSlot = 0;
        if(!Visible || GameSession.Character==null) return false;
        int w=expanded ? FullW : SmallW,h=expanded ? FullH : SmallH;
        var origin=NativeUi.ClampWindowPosition(pos,new Vector2(w,h),S)*S;
        var r=new Rect(origin.x,origin.y,w*S,h*S);
        var cells=expanded ? FullCells : SmallCells;
        for(int row=0;row<cells.GetLength(0);row++) for(int col=0;col<cells.GetLength(1);col++)
            if(cells[row,col]!=0 && At(r,GridX+col*Step,GridY+row*Step,Cell,Cell).Contains(pointer))
            { cellSlot=cells[row,col]; return true; }
        return false;
    }
    public static bool Visible;
    static Vector2 pos = new Vector2(200, 100);
    static bool expanded, dragging, petVisible;
    static int petIndex;
    public static int SelectedPet => petIndex;
    static double petReadAt;
    static readonly InventoryEntry[] pets = new InventoryEntry[3];
    static Vector2 grab;
    static float S => BottomBar.S;
    const int SmallW = 175, SmallH = 304, FullW = 208, FullH = 291;
    const int GridX = 5, GridY = 35, Step = 33, Cell = 32;
    static Rect At(Rect r, float x, float y, float w, float h) => new Rect(r.x + x * S, r.y + y * S, w * S, h * S);
    static Texture2D Tex(string node) => Art.Tex("wz://UI/UIWindow.img/Equip/" + node);

    // 每格对应原 IMG 背景已有的装备位；BE2260: -18=(5,233), -19=(38,233)。
    // 行 0..6 自上而下、列 0..4 自左而右，按 backgrnd 上的中文槽位名对齐：
    //   帽子 / 勋章 额饰 戒指 戒指 / 眼饰 耳饰 肩饰 / 披风 上衣 项链 武器 盾牌 /
    //   手套 裤裙 腰带 指环 指环 / 鞋子 副手 图腾 / 骑兽 鞍子 空白 空白 纹章
    // 心脏在机器人面板、符文徽章在独立窗口操作；旧心脏/徽章格由 NativeEquipBackdrop 清除。
    static readonly int[,] SmallCells =
    {
        { 0, EquipSlot.Hat, 0, 0, 0 },
        { EquipSlot.Medal, EquipSlot.FaceAccessory, 0, -12, -13 },
        { 0, 0, EquipSlot.EyeAccessory, EquipSlot.Earring, -20 },
        { EquipSlot.Cape, EquipSlot.Top, -17, EquipSlot.Weapon, EquipSlot.Shield },
        { EquipSlot.Glove, EquipSlot.Bottom, -50, -15, -16 },
        { 0, 0, EquipSlot.Shoes, -51, -53 },
        { -18, -19, 0, 0, -56 },
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
        { -18, -19, 0, 0, 0, 0 },
    };

    public static void Toggle() { Visible = !Visible; if (!Visible) petVisible = false; }

    /// <summary>窗口在虚拟坐标系里的左上角（机器人面板靠它贴边）。</summary>
    public static Vector2 Position => pos;
    public static Vector2 Size => new Vector2(expanded ? FullW : SmallW, expanded ? FullH : SmallH);

    public static void Draw()
    {
        if (!Visible) return;
        int w = expanded ? FullW : SmallW, h = expanded ? FullH : SmallH;
        pos = NativeUi.ClampWindowPosition(pos, new Vector2(w, h), S);
        var r = new Rect(pos.x * S, pos.y * S, w * S, h * S);
        NativeUi.RegisterWindow("EquipWindow", r, () => EquipWindow.Visible);
        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;
        bool release = Event.current.type == EventType.Repaint && Pointer.Released;
        if (press && At(r, 0, 0, w - 24, 22).Contains(Pointer.Pos)) { dragging = true; grab = Pointer.Pos / S - pos; }
        if (dragging && Pointer.Down)
        {
            pos = NativeUi.ClampWindowPosition(Pointer.Pos / S - grab, new Vector2(w, h), S);
            r.position = pos * S;
        }
        if (!Pointer.Down) dragging = false;
        // 折叠态修复原图后补槽位的文字，并移除旧心脏/徽章格。
        // 展开态是原版 6 列图，没有这处重复格。
        var bg = expanded ? Tex("FullBackgrnd") : (NativeEquipBackdrop.Texture() ?? Tex("backgrnd"));
        if (bg != null) GUI.DrawTexture(r, bg);
        if (NativeUi.Button(At(r, w - 19, 5, 12, 12), "UI/Basic.img/BtClose")) { Visible = false; return; }

        var cells = expanded ? FullCells : SmallCells;
        int rows = cells.GetLength(0), cols = cells.GetLength(1);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                int cellSlot = cells[row, col];
                if (cellSlot == 0) continue;
                var cellRect = At(r, GridX + col * Step, GridY + row * Step, Cell, Cell);
                // 现金槽优先：同一格上「现金戒指 -112」和「普通戒指 -12」只显示一件，
                // 现金那件非空就盖掉普通那件（原端 7FEE65 并行读取的顺序）。
                int slot = EquipSlot.WornSlotOf(cellSlot);
                int id = Profile.EquipOfSlot(slot);
                if (id <= 0) continue;
                var icon = IconOf(slot, id);
                if (icon != null) GUI.DrawTexture(cellRect, icon, ScaleMode.ScaleToFit);
                if (Event.current.type != EventType.Repaint) continue;
                if (cellRect.Contains(Pointer.Pos)) ItemTooltip.SetHover(id, true);
                if (press && cellRect.Contains(Pointer.Pos) && !NativeInventoryActions.Carrying)
                    NativeInventoryActions.PickEquipped(id, slot);
                // 只保护实际普通武器 -11；覆盖在它上面的现金武器 -111 可以卸下。
                if (Pointer.DoubleClicked && cellRect.Contains(Pointer.Pos) && slot != EquipSlot.Weapon)
                {
                    Pointer.ConsumeClick();
                    NativeInventoryActions.CancelCarry();
                    if (cellSlot == EquipSlot.Medal) NativeMedalWindow.Wear(id, true);
                    else EquipLoadout.Unequip(slot);
                    if (Profile.EquipOfSlot(slot) == 0) BottomBar.Toast("已脱下" + EquipSlot.NameOf(cellSlot));
                }
            }

        if ((Profile.JobId / 100 == 22 || Profile.JobId == 2001) &&
            NativeUi.Button(At(r, 5, 267, 46, 19), "UI/UIWindow.img/Equip/BtDetail")) { expanded = !expanded; return; }
        if (!expanded && NativeUi.Button(At(r, 102, 280, 67, 18), "UI/UIWindow.img/Equip/BtPetEquipShow"))
        { petVisible = !petVisible; petReadAt = 0; }
        // 机器人换装入口：原底图没有这一页的图元，按钮自绘。
        // 位置避让：BtDetail(5,267) 只有 22xx/2001 才画，那两职业把按钮右移 51px。
        float toggleX = (Profile.JobId / 100 == 22 || Profile.JobId == 2001) ? 56f : 5f;
        if (NativeAndroidWindow.ToggleButton(At(r, toggleX, expanded ? 268f : 280f, 46f, 18f),
            NativeAndroidWindow.Visible, "機器人"))
        {
            NativeAndroidWindow.Toggle();
            Pointer.ConsumeClick();
        }
        if (petVisible) DrawPet(r);
    }

    // Native CUIEquipPet window at parent+(172,123), 177x181 (7FE12F / 7FE299).
    // Slot coordinates are the original POINT table BE2260 indexed by body part - 1.
    static readonly int[,] PetCells =
    {
        {14,112,77,0},{21,79,110,0},{22,13,44,0},{23,46,44,0},
        {24,13,11,-1},{25,79,11,-1},{26,79,77,0},{27,13,110,0},{28,46,77,0},{29,112,110,0},
        {30,112,77,1},{31,79,110,1},{32,112,110,1},{33,13,44,1},{34,46,44,1},{35,79,77,1},{36,13,110,1},{37,46,77,1},
        {38,112,77,2},{39,79,110,2},{40,112,110,2},{41,13,44,2},{42,46,44,2},{43,79,77,2},{44,13,110,2},{45,46,77,2},
        {46,13,77,0},{47,13,77,1},{48,13,77,2}
    };
    public static bool DropPetPotion(Vector2 pointer,int id)
    {
        if(!Visible || !petVisible || !GameSession.InGame) return false;
        var r=At(new Rect(pos.x*S,pos.y*S,0,0),172,123,177,181);
        for(int i=0;i<2;i++)
        {
            if(!At(r,i==0 ? 46 : 112,11,32,32).Contains(pointer)) continue;
            var spec=NativeItemSource.Node(id)?.Get("spec");
            bool hp=i==0;
            if(Profile.EquipOfSlot(hp ? -24 : -25)<=0)
            { BottomBar.Toast("请先给宠物装备对应的自动药水包"); return true; }
            if(spec==null || id/1000000!=2 || (spec.Int(hp ? "hp" : "mp")<=0 && spec.Int(hp ? "hpR" : "mpR")<=0))
            { BottomBar.Toast(hp ? "请放入恢复HP的药水" : "请放入恢复MP的药水"); return true; }
            KeyMap.BindPetPotion(hp,id);
            int alarm=hp ? ClientSettings.HpAlarm : ClientSettings.MpAlarm;
            BottomBar.Toast(alarm==0 ? "药水已绑定，请在系统设置中调高"+(hp ? "HP" : "MP")+"警报比例以启用自动补药" : "已绑定"+WzNames.Item(id));
            return true;
        }
        return false;
    }
    static void DrawPet(Rect parent)
    {
        if (!GameSession.InGame) return;
        if (Time.realtimeSinceStartupAsDouble >= petReadAt)
        {
            petReadAt = Time.realtimeSinceStartupAsDouble + .25;
            for (int i = 0; i < 3; i++)
                pets[i] = GameSession.Net.ChangeInventory(new InventoryChangeRequest
                { characterId = GameSession.CharacterId, getPet = true, petSlot = i }).pet;
            if (pets[petIndex] == null) petIndex = 0;
        }
        var r = At(parent, 172, 123, 177, 181);
        ImagePet("pet", 0, 0);
        void ImagePet(string path, int x, int y)
        {
            var t = Tex(path);
            if (t != null) GUI.DrawTexture(At(r, x, y, t.width, t.height), t);
        }
        if (NativeUi.Button(At(r,155,159,17,16), "UI/UIWindow.img/Equip/BtPetEquipHide"))
        { petVisible = false; return; }
        bool enabled = GUI.enabled;
        for (int i = 0; i < 3; i++)
        {
            GUI.enabled = enabled && pets[i] != null;
            string path = "UI/UIWindow.img/UserInfo/BtPet" + (i + 1);
            var t = Art.Tex("wz://" + path + "/normal/0");
            if (t != null && NativeUi.Button(At(r,10+34*i,151,t.width,t.height),path)) petIndex = i;
        }
        GUI.enabled = enabled;
        for(int i=0;i<2;i++)
        {
            int id=KeyMap.PetPotion(i==0);
            var cell=At(r,i==0 ? 46 : 112,11,32,32);
            var icon=id>0 ? NativeItemSource.Icon(id) : null;
            var origin=id>0 ? NativeItemSource.IconNode(id)?.Get("origin") : null;
            if(icon!=null) GUI.DrawTexture(At(r,(i==0 ? 46 : 112)-(origin?.X??0),43-(origin?.Y??0),icon.width,icon.height),icon);
            if(id>0 && Event.current.type==EventType.Repaint && cell.Contains(Pointer.Pos))
            {
                ItemTooltip.SetHover(id,false);
                if(Pointer.DoubleClicked) { Pointer.ConsumeClick(); KeyMap.BindPetPotion(i==0,0); }
            }
        }
        // Native Draw uses 0x40FF0000 rectangles for unavailable pet-specific slots.
        if (pets[petIndex] == null)
            foreach (var p in new[] {new Vector2(112,77),new Vector2(79,110),new Vector2(112,110),new Vector2(46,11),new Vector2(112,11)})
                UiKit.Fill(At(r,p.x,p.y,32,32),new Color32(255,0,0,64));
        for (int i = 0; i < PetCells.GetLength(0); i++)
        {
            if (PetCells[i,3] >= 0 && PetCells[i,3] != petIndex) continue;
            int slot = -PetCells[i,0], id = Profile.EquipOfSlot(slot);
            if (id <= 0) continue;
            var icon = NativeItemSource.Icon(id);
            var origin = NativeItemSource.IconNode(id)?.Get("origin");
            int x = PetCells[i,1], y = PetCells[i,2];
            if (icon != null) GUI.DrawTexture(At(r,x-(origin?.X??0),y+32-(origin?.Y??0),icon.width,icon.height),icon);
            var cell = At(r,x,y,32,32);
            if (Event.current.type == EventType.Repaint && cell.Contains(Pointer.Pos))
            {
                ItemTooltip.SetHover(id,true);
                if (Pointer.DoubleClicked) { Pointer.ConsumeClick(); EquipLoadout.Unequip(slot); }
            }
        }
    }

    static Texture2D IconOf(int slot, int id)
    {
        if (slot == EquipSlot.Weapon)
            return Art.Tex("wz://Character/Weapon/" + id.ToString("D8") + ".img/info/icon");
        return ArmorIcon(id);
    }

    // 后补槽位（副手 / 图腾 / 心脏 / 徽章 / 纹章）的槽位名。
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

    // 当前背包页对应的服务端 inventorytype（1 装备 / 2 消耗 / 3 设置 / 4 其他 / 5 现金）。
    public static int InventoryType => tab + 1;

    // 指针落在哪个背包格：position = 源端 inventoryitems.position（1 起、含空格）。
    // 源端把「手上拿着东西再点一格」做成 ITEM_MOVE(type, src, dst)
    // （BeiDou.exe 0x4F3057 用 0x81DB7E 命中格子拿 dst），所以空槽也必须报出槽位号。
    // 这里按当前窗位实时算矩形，不用上一帧缓存，避免拖动窗体后落点错位。
    public static bool SlotAt(Vector2 pointer, out int position)
    {
        position = 0;
        if (!Visible) return false;
        int capacity = Profile.InventoryCapacity(tab);
        var r = new Rect(pos.x * S, pos.y * S, (expanded ? 603 : 175) * S, 289 * S);
        for (int cell = 0; cell < (expanded ? 96 : 24); cell++)
        {
            int index = expanded ? cell : scroll * 4 + cell;
            if (index >= capacity) break;
            int block = expanded ? cell / 24 : 0;
            int local = cell % 24;
            float x = 8 + block * (expanded ? 149 : 0) + local % 4 * 36;
            float y = 50 + local / 4 * 34;
            if (!At(r, x, y, 32, 32).Contains(pointer)) continue;
            position = index + 1;
            return true;
        }
        return false;
    }
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

    // ITEM_SORT2(sort) / ITEM_SORT(merge)：服务端真实整理，回包带整份背包。
    // 源端改完 position 后是「全部移除再按顺序 addItem」，这里用同一份回包覆盖内存背包，
    // 再投影到 Profile，界面顺序才跟着服务端槽位走。
    static void Organize(bool merge)
    {
        if (!GameSession.InGame) return;
        var response = GameSession.Net.ChangeInventory(new InventoryChangeRequest
        {
            characterId = GameSession.CharacterId,
            mergeItems = merge, sortItems = !merge,
            sortInventoryType = tab + 1,
        });
        if (response == null || !response.ok)
        {
            BottomBar.Toast(response == null || string.IsNullOrEmpty(response.error) ? "无法整理背包" : response.error);
            return;
        }
        GameSession.Character.inventory = response.inventory;
        GameSession.ApplyInventoryToProfile();
        Profile.Save();
        scroll = 0;
    }

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
        NativeUi.RegisterWindow("InvenWindow", r, () => InvenWindow.Visible);
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
        // 格子内容一律按服务端槽位取：源端背包就是按 inventoryitems.position 画格，
        // 空格留空、同 id 的两个堆叠各占一格（Profile 的扁平 itemIds 会把它们并成一格，
        // 那样「点 A 格再点 B 格互换位置」就没有落点可言）。整理后 position 变了这里也跟着变。
        bool live = GameSession.InGame && GameSession.Character != null && GameSession.Character.inventory != null;
        var byPosition = new Dictionary<int, InventoryEntry>();
        List<InventoryEntry> equipment = null;
        List<int> fallback = null;
        if (live)
        {
            foreach (var item in GameSession.Character.inventory)
                if (item.inventoryType == tab + 1 && item.position > 0) byPosition[item.position] = item;
            if (tab == 0)
            {
                equipment = new List<InventoryEntry>(byPosition.Values);
                equipment.Sort((left, right) => left.position.CompareTo(right.position));
            }
        }
        else
        {
            // 没进游戏（读不到 Character.inventory）时退回 Profile 的扁平投影，保持原行为。
            fallback = new List<int>();
            if (tab == 0)
            {
                bool equipped = false;
                foreach (string id in Profile.OwnedWeapons)
                {
                    // 已装备的武器占 EQUIPPED 位，不再占用背包格
                    if (!equipped && id == Profile.equipWeapon) { equipped = true; continue; }
                    if (int.TryParse(id, out int number)) fallback.Add(number);
                }
            }
            fallback.AddRange(Profile.InventoryItems(tab));
        }
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
            float x = 8 + block * (expanded ? 149 : 0) + local % 4 * 36;
            float y = 50 + local / 4 * 34;
            var slot = At(r, x, y, 32, 32);
            if (index >= capacity) { Image(r, "disabled", x, y); continue; }
            int position = index + 1;
            InventoryEntry entry = null;
            int id = 0;
            if (live)
            {
                byPosition.TryGetValue(position, out entry);
                if (entry != null) id = entry.itemId;
            }
            else if (index < fallback.Count) id = fallback[index];
            if (id <= 0) continue;   // 空格：只可能作为「放下」的落点，由 NativeInventoryActions.SlotAt 处理
            if(Event.current.type==EventType.Repaint && equipment!=null && entry!=null) equipmentTargets.Add(new KeyValuePair<Rect,InventoryEntry>(slot,entry));
            var icon = NativeItemSource.Icon(id);
            if (icon != null) GUI.DrawTexture(slot, icon, ScaleMode.ScaleToFit);
            if (tab != 0)
            {
                // 数量画的是这一格自己的堆叠数（源端逐格数字，不是同 id 总和）。
                float digitX = x;
                int count = entry != null ? entry.quantity : Profile.ItemCount(id);
                foreach (char digit in count.ToString())
                {
                    var image = Art.Tex("wz://UI/Basic.img/ItemNo/" + digit);
                    if (image == null) continue;
                    GUI.DrawTexture(At(r, digitX, y + 20, image.width, image.height), image);
                    digitX += image.width;
                }
            }
            if (Event.current.type == EventType.Repaint && id > 0 && slot.Contains(Pointer.Pos)) ItemTooltip.SetHover(id, false, entry);
            if (press && NativeTradeWindow.CanOffer && slot.Contains(Pointer.Pos))
                NativeTradeWindow.BeginItemDrag(entry);
            // 手上有东西时不再覆盖手上的道具：落格由 CarryPointerStep（ITEM_MOVE src→dst）处理。
            if (press && !NativeTradeWindow.CanOffer && !NativeInventoryActions.Carrying && slot.Contains(Pointer.Pos))
                NativeInventoryActions.PickFromInventory(id, entry != null ? entry.position : position);
            if (Event.current.type == EventType.Repaint && Pointer.DoubleClicked && slot.Contains(Pointer.Pos))
            {
                Pointer.ConsumeClick();
                if (!NativeTradeWindow.CanOffer) NativeInventoryActions.DoubleClick(id, entry != null ? entry.position : position);
                // Finish drawing this snapshot; stopping here blanks all later cells.
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
        // 按钮创建于 0x81CD74（id 0x7D2）；点击分派 0x81D3A3(cmp eax,0x7D2) → 0x81D996 丢金币流程。
        if (NativeUi.Button(At(r, 7, 266, 14, 14), "UI/UIWindow.img/Item/BtCoin"))
            NativeInventoryActions.PromptDropMeso();
        // 81DC84: comma grouping; 81DCD8/81DD5F: right edge138, y266, font25 (black,12px).
        var mesoStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * S),
            alignment = TextAnchor.UpperRight, clipping = TextClipping.Clip };
        mesoStyle.normal.textColor = Color.black;
        GUI.Label(At(r, 25, 266, 113, 16), Profile.gold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), mesoStyle);

        // 81CE50 / 81E47D：内部宽 W-20，再分别减30/15；基类负责最右侧关闭。
        if (NativeUi.Button(At(r, width - 50, 6, 12, 12), "UI/UIWindow.img/Item/" + (expanded ? "BtSmall" : "BtFull"))) { expanded = !expanded; scroll = 0; return; }
        // 源端 BtGather / BtSort 是同一条控件槽位的两种状态（EXE 0x81E3A6 按状态值 0/1
        // 二选一创建，id 0x7D3/0x7D4，坐标同为 (W-15,6) 中心）：
        //   状态 0 画 BtGather → 点击发 ITEM_SORT  → InventoryMergeHandler（聚拢）
        //   状态 1 画 BtSort   → 点击发 ITEM_SORT2 → InventorySortHandler（排序）
        // 状态与图的对应按语义推定；EXE 的消息分派分支未反编译出来，记录在 reference 待核实。
        if (NativeUi.Button(At(r, width - 35, 6, 12, 12), "UI/UIWindow.img/Item/" + (sorted ? "BtSort" : "BtGather")))
        {
            Organize(merge: !sorted);
            sorted = !sorted;
            return;
        }
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

    // 目录 4 项与「界面」11 项（含最后的 BtRanking）现在全部可用。
    static bool Enabled(int kind, int index) => true;

    static string DisabledHint(int kind, int index) => "";

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
        if (index == 10) NativeRankingWindow.Toggle();
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
        bool blocking = NpcSession.Blocking || VirtualControlsEditor.Visible;

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
    public static bool AnyVisible
    {
        get
        {
            if (NativeUtilPrompt.Visible || KeyConfigWindow.Visible) return true;
            foreach (var panel in panels) if (VisibleOf(panel)) return true;
            return false;
        }
    }

    // ——————————————— Esc 分层关闭 ———————————————
    // 原版 Esc 是「系统菜单」键，但用户要求：只要有面板开着，Esc 先关面板。
    // 为了「后开的先关」，这里按打开顺序记录一个栈；打开入口五花八门
    // （快捷键 / 底部栏按钮 / 快捷菜单），所以在绘制阶段统一观测可见性变化，
    // 不要求每个入口都记得来登记。
    // 顺序 = 绘制层级的初始顺序（越靠后越在上）。运行时会按「谁被点中就浮上来」重排，
    // 见文件末尾的 zOrder / BringToFront。
    enum Panel
    {
        MiniMap, Stat, Equip, Android, Inven, Shop, Quest, Help, Skill, World, Social, MonsterBook,
        CommunityBoard, AdBoard, Community, Guild, GuildGrade, Medal, Trade, Market, NpcShop, Delivery,
        Merchant, Maker, Ranking, PlayerShop, ItemSearch, Storage, Claim, Rps, Memo, MapleTv,
        CarnivalHud, CarnivalLobby, RemoteGachapon, CharacterInfo, CharacterMenu,
        GameOpt, SysOpt, Channel, GameMenu, Shortcut,
        Symbol, HyperStat, InnerAbility, VMatrix, HexaMatrix, Enhancement, Vega, ApReset, SpReset,
        Teleport, KeyConfig,
        // 只参与 Esc 关闭：宠物排除窗画在角色信息窗内部，没有独立绘制层
        PetExclusion,
    }
    static readonly Panel[] panels = (Panel[])System.Enum.GetValues(typeof(Panel));
    static readonly List<Panel> openOrder = new List<Panel>();
    static readonly Dictionary<Panel, bool> lastVisible = new Dictionary<Panel, bool>();
    // 本帧新打开、等待置顶的面板（TrackPanels 只登记，重排留给 DrawAll，保持 Esc 栈逻辑纯粹）
    static Panel raisedPanel;
    static bool raisedPending;

    static bool VisibleOf(Panel p)
    {
        switch (p)
        {
            case Panel.Symbol: return NativeSymbolWindow.Visible;
            case Panel.HyperStat: return NativeHyperStatWindow.Visible;
            case Panel.InnerAbility: return NativeInnerAbilityWindow.Visible;
            case Panel.VMatrix: return NativeVMatrixWindow.Visible;
            case Panel.HexaMatrix: return NativeHexaMatrixWindow.Visible;
            case Panel.Enhancement: return NativeEnhancementWindow.Visible;
            case Panel.Vega: return NativeVegaWindow.Visible;
            case Panel.ApReset: return NativeResetWindow.Visible;
            case Panel.SpReset: return NativeSpResetWindow.Visible;
            case Panel.Teleport: return NativeTeleportWindow.Visible;
            case Panel.CharacterMenu: return NativeCharacterMenu.Visible;
            case Panel.CharacterInfo: return NativeCharacterInfo.Visible;
            case Panel.Trade: return NativeTradeWindow.Visible;
            case Panel.Market: return NativeMarketWindow.Visible;
            case Panel.Android: return NativeAndroidWindow.Visible;
            case Panel.Memo: return NativeMemoWindow.Visible;
            case Panel.PetExclusion: return NativePetExclusionWindow.Visible;
            case Panel.GuildGrade: return NativeGuildGradeWindow.Visible;
            case Panel.Stat: return StatWindow.Visible;
            case Panel.Equip: return EquipWindow.Visible;
            case Panel.Inven: return InvenWindow.Visible;
            case Panel.Shop: return ShopWindow.Visible;
            case Panel.NpcShop: return NativeNpcShopWindow.Visible;
            case Panel.Delivery: return NativeDeliveryWindow.Visible;
            case Panel.Merchant: return NativeMerchantWindow.Visible;
            case Panel.Maker: return NativeMakerWindow.Visible;
            case Panel.Ranking: return NativeRankingWindow.Visible;
            case Panel.PlayerShop: return NativePlayerShopWindow.Visible;
            case Panel.ItemSearch: return NativeItemSearchWindow.Visible;
            case Panel.Storage: return NativeStorageWindow.Visible;
            case Panel.Claim: return NativeClaimWindow.Visible;
            case Panel.Rps: return NativeRpsWindow.Visible;
            case Panel.RemoteGachapon: return NativeRemoteGachaponWindow.Visible;
            case Panel.Quest: return QuestWindow.Visible;
            case Panel.Shortcut: return ShortcutWindow.Visible;
            case Panel.Help: return HelpWindow.Visible;
            case Panel.GameMenu: return GameMenuWindow.Visible;
            case Panel.Skill: return SkillWindow.Visible;
            case Panel.World: return NativeMapWindows.WorldVisible;
            case Panel.Social: return NativeSocialWindow.Visible;
            case Panel.MonsterBook: return NativeMonsterBook.Visible;
            case Panel.Community: return NativeCommunityWindow.Visible;
            case Panel.Guild: return NativeGuildWindow.Visible;
            case Panel.Medal: return NativeMedalWindow.Visible;
            case Panel.GameOpt: return GameOptWindow.Visible;
            case Panel.SysOpt: return SysOptWindow.Visible;
            case Panel.Channel: return ChannelWindow.Visible;
            case Panel.KeyConfig: return KeyConfigWindow.Visible;
            // 以下不是独立窗口（小地图常驻 / 宿主窗口内部的附属层），只占一个绘制槽位，
            // 因此永远不参与「谁置顶」，也不进 Esc 关栈。
            case Panel.MiniMap:
            case Panel.CommunityBoard:
            case Panel.AdBoard:
            case Panel.MapleTv:
            case Panel.CarnivalHud:
            case Panel.CarnivalLobby:
                return false;
        }
        return false;
    }

    static void ClosePanel(Panel p)
    {
        switch (p)
        {
            case Panel.Symbol: NativeSymbolWindow.Close(); return;
            case Panel.HyperStat: NativeHyperStatWindow.Close(); return;
            case Panel.InnerAbility: NativeInnerAbilityWindow.Close(); return;
            case Panel.VMatrix: NativeVMatrixWindow.Escape(); return;
            case Panel.HexaMatrix: NativeHexaMatrixWindow.Escape(); return;
            case Panel.Enhancement: NativeEnhancementWindow.Close(); return;
            case Panel.Vega: if (!NativeVegaWindow.Busy) NativeVegaWindow.Close(); return;
            case Panel.ApReset: NativeResetWindow.Close(); return;
            case Panel.SpReset: NativeSpResetWindow.Close(); return;
            case Panel.Teleport: NativeTeleportWindow.Close(); return;
            case Panel.CharacterMenu: NativeCharacterMenu.Close(); return;
            case Panel.CharacterInfo: NativeCharacterInfo.Close(); return;
            case Panel.Trade: NativeTradeWindow.Close(); return;
            case Panel.Market: NativeMarketWindow.Close(); return;
            case Panel.Android: NativeAndroidWindow.Visible = false; return;
            case Panel.Memo: NativeMemoWindow.Close(); return;
            case Panel.PetExclusion: NativePetExclusionWindow.Close(); return;
            case Panel.GuildGrade: NativeGuildGradeWindow.Reset(); return;
            case Panel.Stat: StatWindow.Visible = false; return;
            case Panel.Equip: EquipWindow.Visible = false; return;
            case Panel.Inven: InvenWindow.Visible = false; return;
            // 商城开着的期间 timeScale=0，必须走 Reset 才会把时间轴还回去
            case Panel.Shop: NativeCashWindow.Reset(); return;
            case Panel.NpcShop: NativeNpcShopWindow.Close(); return;
            case Panel.Delivery: NativeDeliveryWindow.Close(); return;
            case Panel.Merchant: NativeMerchantWindow.Close(); return;
            case Panel.Maker: NativeMakerWindow.Close(); return;
            case Panel.Ranking: NativeRankingWindow.Close(); return;
            case Panel.PlayerShop: NativePlayerShopWindow.Close(); return;
            case Panel.ItemSearch: NativeItemSearchWindow.Close(); return;
            case Panel.Storage: NativeStorageWindow.Close(); return;
            case Panel.Claim: NativeClaimWindow.Close(); return;
            case Panel.Rps: NativeRpsWindow.RequestLeave(); return;
            case Panel.RemoteGachapon: NativeRemoteGachaponWindow.Close(); return;
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
            case Panel.KeyConfig: KeyConfigWindow.Close(); return;
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
            if (now) { openOrder.Add(p); raisedPanel = p; raisedPending = true; }
        }
    }

    // 关掉最后打开的那个面板；没有面板时返回 false（调用方去开系统菜单）。
    public static bool CloseTop()
    {
        if (NativeUtilPrompt.Visible) { NativeUtilPrompt.Cancel(); return true; }
        if(NativeSoulWeaponPanel.HasModal){NativeSoulWeaponPanel.CloseModal();return true;}
        TrackPanels();
        if (openOrder.Count == 0) return false;
        var top = openOrder[openOrder.Count - 1];
        ClosePanel(top);
        if (!VisibleOf(top)) openOrder.Remove(top);
        lastVisible[top] = VisibleOf(top);
        return true;
    }

    public static void DrawAll()
    {
        TrackPanels();
        // 新打开的面板一律浮到最上（原版行为：后开的窗压住先开的窗）
        if (raisedPending) { BringToFront(raisedPanel); raisedPending = false; }
        ItemTooltip.Reset();
        NativeSoulWeapon.Tick();
        NativeCashWindow.Tick();
        NativeDeliveryWindow.Tick();
        NativeItemSearchWindow.Tick();

        // 层级不再写死：先看指针压在哪个窗口上，按下就把它搬到最上，
        // 本帧的绘制顺序随即按新的 zOrder 生效。
        var pressed = TopPanelAtPointer();
        if (pressed.HasValue && Pointer.Pressed) BringToFront(pressed.Value);
        int top = pressed.HasValue ? zOrder.IndexOf(pressed.Value) : -1;

        bool enabled = GUI.enabled;
        // The global confirmation/input dialog is above every window, including matrices.
        var promptPointer = new Pointer.InputMask(NativeUtilPrompt.Visible);
        GUI.enabled &= !NativeUtilPrompt.Visible;
        try
        {
            for (int i = 0; i < zOrder.Count; i++)
            {
                var panel = zOrder[i];
                // 被更上层窗口盖住的面板必须看不到这根指针，否则先画的下层会抢走点击
                // （IMGUI 里同一 depth 内先画的先处理事件，顺序正好和视觉层级相反）。
                // 只遮指针、不动 GUI.enabled：否则被遮住的窗口按钮会画成 disabled 灰图。
                if (top < 0 || i >= top) { DrawPanel(panel); continue; }
                var covered = new Pointer.InputMask(true);
                try { DrawPanel(panel); }
                finally { covered.Dispose(); }
            }
            ItemTooltip.Flush();
        }
        finally
        {
            GUI.enabled = enabled;
            promptPointer.Dispose();
        }
        NativeUtilPrompt.Draw();
        UiPreviewHook.Emit();
    }

    // ——————————————— 面板层级 ———————————————
    // 绘制顺序即层级（后画 = 在上）。zOrder 的初始值和旧的硬编码绘制链一致，
    // 之后由「新开置顶」和「点击置顶」两件事驱动重排。
    static readonly List<Panel> zOrder = new List<Panel>
    {
        Panel.MiniMap, Panel.Stat, Panel.Equip, Panel.Android, Panel.Inven, Panel.Shop,
        Panel.Quest, Panel.Help, Panel.Skill, Panel.World, Panel.Social, Panel.MonsterBook,
        Panel.CommunityBoard, Panel.AdBoard, Panel.Community, Panel.Guild, Panel.GuildGrade,
        Panel.Medal, Panel.Trade, Panel.Market, Panel.NpcShop, Panel.Delivery, Panel.Merchant,
        Panel.Maker, Panel.Ranking, Panel.PlayerShop, Panel.ItemSearch, Panel.Storage, Panel.Claim,
        Panel.Rps, Panel.Memo, Panel.MapleTv, Panel.CarnivalHud, Panel.CarnivalLobby,
        Panel.RemoteGachapon, Panel.CharacterInfo, Panel.CharacterMenu,
        Panel.GameOpt, Panel.SysOpt, Panel.Channel, Panel.GameMenu, Panel.Shortcut,
        Panel.Vega, Panel.Enhancement, Panel.Symbol, Panel.HyperStat, Panel.InnerAbility,
        Panel.VMatrix, Panel.HexaMatrix, Panel.ApReset, Panel.SpReset, Panel.Teleport,
        Panel.KeyConfig,
    };

    // 贴着宿主画、且必须紧跟在宿主之后的附属窗（顺序不能拆）
    static bool IsFollower(Panel p) => p == Panel.Android || p == Panel.GuildGrade;
    static Panel HostOf(Panel p) => p == Panel.Android ? Panel.Equip : p == Panel.GuildGrade ? Panel.Guild : p;

    static void BringToFront(Panel p)
    {
        if (IsFollower(p)) p = HostOf(p);
        int at = zOrder.IndexOf(p);
        if (at < 0) return;
        var block = new List<Panel> { p };
        if (p == Panel.Equip) block.Add(Panel.Android);
        else if (p == Panel.Guild) block.Add(Panel.GuildGrade);
        // 已经在最上（含附属窗已就位）就不用动
        if (at == zOrder.Count - block.Count) return;
        foreach (var b in block) zOrder.Remove(b);
        zOrder.AddRange(block);
    }

    // 窗口矩形来自各窗口自己的 RegisterWindow 登记（上一帧几何，够快也够准）
    static readonly Dictionary<Panel, string[]> hitKeys = new Dictionary<Panel, string[]>
    {
        { Panel.Stat, new[] { "StatWindow" } },
        { Panel.Equip, new[] { "EquipWindow" } },
        { Panel.Android, new[] { "AndroidWindow" } },
        { Panel.Inven, new[] { "InvenWindow" } },
        { Panel.Quest, new[] { "QuestWindow" } },
        { Panel.Skill, new[] { "SkillWindow" } },
        { Panel.World, new[] { "NativeMapWindows" } },
        { Panel.Social, new[] { "NativeSocialWindow" } },
        { Panel.MonsterBook, new[] { "NativeMonsterBook" } },
        { Panel.Community, new[] { "NativeCommunityWindow" } },
        { Panel.Guild, new[] { "NativeGuildWindow" } },
        { Panel.GuildGrade, new[] { "NativeGuildGradeWindow" } },
        { Panel.Trade, new[] { "NativeTradeWindow" } },
        { Panel.Market, new[] { "NativeMarketWindow" } },
        { Panel.NpcShop, new[] { "NativeNpcShopWindow" } },
        { Panel.Delivery, new[] { "NativeDeliveryWindow" } },
        { Panel.Merchant, new[] { "NativeMerchantWindow" } },
        { Panel.Maker, new[] { "NativeMakerWindow" } },
        { Panel.Ranking, new[] { "NativeRankingWindow" } },
        { Panel.PlayerShop, new[] { "NativePlayerShopWindow" } },
        { Panel.ItemSearch, new[] { "NativeItemSearchWindow" } },
        { Panel.Storage, new[] { "NativeStorageWindow" } },
        { Panel.Claim, new[] { "NativeClaimWindow" } },
        { Panel.Rps, new[] { "NativeRpsWindow" } },
        { Panel.RemoteGachapon, new[] { "NativeRemoteGachaponWindow" } },
        { Panel.CharacterInfo, new[] { "NativeCharacterInfo" } },
        { Panel.CharacterMenu, new[] { "NativeCharacterMenu" } },
        { Panel.Memo, new[] { "NativeMemoWindow" } },
        { Panel.Channel, new[] { "ChannelWindow" } },
        { Panel.CarnivalLobby, new[] { "NativeCarnivalLobby" } },
        { Panel.Vega, new[] { "NativeVegaWindow" } },
        { Panel.ApReset, new[] { "NativeResetWindow" } },
        { Panel.SpReset, new[] { "NativeSpResetWindow" } },
        { Panel.Teleport, new[] { "NativeTeleportWindow" } },
        { Panel.Symbol, new[] { "NativeSymbolWindow" } },
        { Panel.HyperStat, new[] { "NativeHyperStatWindow" } },
        { Panel.InnerAbility, new[] { "NativeInnerAbilityWindow" } },
        { Panel.VMatrix, new[] { "NativeVMatrixWindow" } },
        { Panel.HexaMatrix, new[] { "NativeHexaMatrixWindow" } },
        { Panel.Enhancement, new[] { "NativeEnhancementWindow", "NativeEnhancementChoice", "SoulWeaponWz2Modal" } },
        { Panel.KeyConfig, new[] { "KeyConfigWindow" } },
    };

    static bool PanelAtPointer(Panel p)
    {
        if (!hitKeys.TryGetValue(p, out var keys)) return false;
        for (int i = 0; i < keys.Length; i++)
            if (NativeUi.TryWindowRect(keys[i], out var r) && Pointer.HitsWindow(r)) return true;
        return false;
    }

    // 指针压住的最上层可见面板；都在它下面则返回 null
    static Panel? TopPanelAtPointer()
    {
        for (int i = zOrder.Count - 1; i >= 0; i--)
        {
            var p = zOrder[i];
            if (!VisibleOf(p)) continue;
            if (PanelAtPointer(p)) return p;
        }
        return null;
    }

    static void DrawPanel(Panel p)
    {
        switch (p)
        {
            case Panel.MiniMap: NativeMapWindows.DrawMini(); return;
            case Panel.Stat: StatWindow.Draw(); return;
            case Panel.Equip: EquipWindow.Draw(); return;
            // 机器人换装面板贴在装备窗右侧，必须紧跟其后（依赖它的位置）
            case Panel.Android: NativeAndroidWindow.Draw(); return;
            case Panel.Inven: InvenWindow.Draw(); return;
            case Panel.Shop: ShopWindow.Draw(); return;
            case Panel.Quest: QuestWindow.Draw(); return;
            case Panel.Help: HelpWindow.Draw(); return;
            case Panel.Skill: SkillWindow.Draw(); return;
            case Panel.World: NativeMapWindows.DrawWorld(); return;
            case Panel.Social: NativeSocialWindow.Draw(); return;
            case Panel.MonsterBook: NativeMonsterBook.Draw(); return;
            // 黑板：原端 0x48A935 type1003 → 0x4890C4 气泡布局（旧占位框与新黑板同画会重叠，故紧随其后）
            case Panel.CommunityBoard: NativeCommunityWindow.DrawBoard(); return;
            case Panel.AdBoard: NativeAdBoard.Draw(); return;
            case Panel.Community: NativeCommunityWindow.Draw(); return;
            case Panel.Guild: NativeGuildWindow.Draw(); return;
            // BtInfo 的附属窗，必须跟着主窗一起进绘制链
            case Panel.GuildGrade: NativeGuildGradeWindow.Draw(); return;
            case Panel.Medal: NativeMedalWindow.Draw(); return;
            case Panel.Trade: NativeTradeWindow.Draw(); return;
            case Panel.Market: NativeMarketWindow.Draw(); return;
            case Panel.NpcShop: NativeNpcShopWindow.Draw(); return;
            case Panel.Delivery: NativeDeliveryWindow.Draw(); return;
            case Panel.Merchant: NativeMerchantWindow.Draw(); return;
            case Panel.Maker: NativeMakerWindow.Draw(); return;
            case Panel.Ranking: NativeRankingWindow.Draw(); return;
            case Panel.PlayerShop: NativePlayerShopWindow.Draw(); return;
            case Panel.ItemSearch: NativeItemSearchWindow.Draw(); return;
            case Panel.Storage: NativeStorageWindow.Draw(); return;
            case Panel.Claim: NativeClaimWindow.Draw(); return;
            case Panel.Rps: NativeRpsWindow.Draw(); return;
            case Panel.Memo: NativeMemoWindow.Draw(); return;
            case Panel.MapleTv: NativeMapleTvDisplay.Draw(); return;
            case Panel.CarnivalHud: NativeCarnivalHud.Draw(); return;
            case Panel.CarnivalLobby: NativeCarnivalLobby.Draw(); return;
            case Panel.RemoteGachapon: NativeRemoteGachaponWindow.Draw(); return;
            case Panel.CharacterInfo: NativeCharacterInfo.Draw(); return;
            case Panel.CharacterMenu: NativeCharacterMenu.Draw(); return;
            // 目录菜单里开出来的三个窗（源端只有底图，实现见 NativeSettingsWindows.cs）
            case Panel.GameOpt: GameOptWindow.Draw(); return;
            case Panel.SysOpt: SysOptWindow.Draw(); return;
            case Panel.Channel: ChannelWindow.Draw(); return;
            // 贴着底栏的浮层
            case Panel.GameMenu: GameMenuWindow.Draw(); return;
            case Panel.Shortcut: ShortcutWindow.Draw(); return;
            case Panel.Vega: NativeVegaWindow.Draw(); return;
            case Panel.Enhancement: NativeEnhancementWindow.Draw(); return;
            // 282 角色成长的三扇独立窗（徽章 / 极限属性 / 内在能力）
            case Panel.Symbol: NativeSymbolWindow.Draw(); return;
            case Panel.HyperStat: NativeHyperStatWindow.Draw(); return;
            case Panel.InnerAbility: NativeInnerAbilityWindow.Draw(); return;
            // 282 五六转的核心成长窗（V 矩阵 / HEXA 矩阵）
            case Panel.VMatrix: NativeVMatrixWindow.Draw(); return;
            case Panel.HexaMatrix: NativeHexaMatrixWindow.Draw(); return;
            case Panel.ApReset: NativeResetWindow.Draw(); return;
            case Panel.SpReset: NativeSpResetWindow.Draw(); return;
            case Panel.Teleport: NativeTeleportWindow.Draw(); return;
            // 键位设置窗也进这条链：它曾经固定画在 DrawAll 之后，导致永远压住其它面板
            case Panel.KeyConfig: KeyConfigWindow.Draw(); return;
        }
    }
}
