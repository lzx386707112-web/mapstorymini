using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Original tooltip dispatch/frame geometry is recorded in inventory-and-tooltip.md.
// Common items use CToolTip::SetItem and the source item canvas; equipment has a separate layout.
public static class ItemCatalog
{
    static readonly Dictionary<int, string> nameCache = new Dictionary<int, string>();
    static readonly Dictionary<int, string> descCache = new Dictionary<int, string>();
    static readonly Dictionary<int, string> subcatCache = new Dictionary<int, string>();

    public static bool IsEquip(int id) => id / 1000000 == 1;
    public static bool IsCash(int id) => id / 1000000 == 5;

    // 物品名/描述节点：按 id 首位分段，各分类目录层级不同
    //   Eqp : String/Eqp.img/Eqp/<subcat>/<id>
    //   Etc : String/Etc.img/Etc/<id>        （比其他类多一层 Etc）
    //   Consume/Ins/Cash : String/<cat>.img/<id>
    static ImgNode ItemNode(int id)
    {
        if (id / 10000 == 500) return WzAssets.Node("String/Pet.img/" + id);
        if (IsEquip(id)) return WzAssets.Node("String/Eqp.img/Eqp/" + EqpSubcat(id) + "/" + id);
        int cat = id / 1000000;
        string file = "String/" + CatFile(id) + ".img";
        return WzAssets.Node(cat == 4 ? file + "/Etc/" + id : file + "/" + id);
    }

    public static string Name(int id)
    {
        if (nameCache.TryGetValue(id, out var c)) return c ?? "";
        var node = ItemNode(id);
        string name = node != null ? node.Text("name") : "";
        // 282 迁入的道具（机器人、心脏、心脏卷轴）在本项目的 GMS083 String 树里没有名字，
        // 统一由 Bot/Items.json 兜底。有 String 名的道具仍以 String 树为准。
        if (string.IsNullOrEmpty(name)) name = Server.Bot.BotItemCatalog.Shared.Name(id);
        nameCache[id] = name;
        return name;
    }

    public static string Desc(int id)
    {
        if (descCache.TryGetValue(id, out var c)) return c ?? "";
        var node = ItemNode(id);
        string desc = node != null ? node.Text("desc") : "";
        if (string.IsNullOrEmpty(desc)) desc = Server.Bot.BotItemCatalog.Shared.Description(id);
        descCache[id] = desc;
        return desc;
    }

    static string CatFile(int id)
    {
        switch (id / 1000000)
        {
            case 1: return "Eqp";
            case 2: return "Consume";
            case 3: return "Ins";
            case 4: return "Etc";
            case 5: return "Cash";
            default: return null;
        }
    }

    // 装备 id 前缀 → String/Eqp.img 子目录名（与 Character 目录名不同，单独映射并缓存）
    static string EqpSubcat(int id)
    {
        if (subcatCache.TryGetValue(id, out var s)) return s;
        int p = id / 10000;
        string sub;
        if (p == 100) sub = "Cap";
        else if (p >= 101 && p <= 103) sub = "Accessory";
        else if (p == 104) sub = "Coat";
        else if (p == 105) sub = "Longcoat";
        else if (p == 106) sub = "Pants";
        else if (p == 107) sub = "Shoes";
        else if (p == 108) sub = "Glove";
        else if (p == 109) sub = "Shield";
        else if (p == 110) sub = "Cape";
        else if (p == 111) sub = "Ring";
        else if (p >= 112 && p <= 119) sub = "Accessory";
        else if (p >= 180 && p <= 183) sub = "PetEquip";
        else if (p == 190 || p == 191 || p == 193) sub = "Taming";
        else if (p >= 194 && p <= 197) sub = "Dragon";
        else if (p == 166 || p == 167) sub = "Android";
        else sub = "Weapon"; // 121-160 / 169 / 170
        subcatCache[id] = sub;
        return sub;
    }


}

public static class ItemTooltip
{
    static int hoverId;
    static bool hasHover;
    static InventoryEntry hoverItem;

    static Texture2D white;
    static Texture2D White
    {
        get
        {
            if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            return white;
        }
    }

    // 每帧开始清空，由窗口 Draw 在指向物品时重新置位
    public static void Reset() { hasHover = false; }

    // 验收/截图脚本用：本帧是否有格子真的把物品挂给了提示框（没有就说明指针没落在格子上）
    public static bool HasHover => hasHover;
    public static int HoverId => hasHover ? hoverId : 0;

    public static void SetHover(int id, bool equipped = false, InventoryEntry instance = null)
    {
        if (id <= 0) return;
        hoverItem = instance;
        if (instance == null && GameSession.Character != null)
            foreach (var item in GameSession.Character.inventory)
                if (item.itemId == id && (item.position < 0) == equipped) { hoverItem = item; break; }
        hoverId = id;
        hasHover = true;
    }

    // 在 DrawAll 末尾调用，压在所有窗口之上绘制
    public static void Flush()
    {
        if (!hasHover || hoverId <= 0) return;
        if (Event.current.type != EventType.Repaint) return;
        Draw(hoverId, Pointer.Pos);
        hasHover = false;
    }

    const string Atlas = "UI/UIWindow.img/ToolTip/Equip/";
    public static void DrawForStatus(int id) => Draw(id, Pointer.Pos);

    // 预览/验收用：不依赖鼠标与窗口位置，直接在给定屏幕锚点画一次。
    // 之前靠「钉指针 + 抓全屏」实拍，S 或 GameView 尺寸一变指针就落空（曾出现 S=0.067，
    // 图里根本没有悬浮框），所以把绘制入口和指针解耦，预览直接调这条。
    public static void PreviewDraw(int id, Vector2 anchor, InventoryEntry instance)
    {
        if (id <= 0) return;
        hoverItem = instance;
        hoverId = id;
        Draw(id, anchor);
    }

    // 最近一次绘制的外框（屏幕像素），供预览脚本裁切；套装块为 null 表示本次没画。
    public static Rect LastEquipRect { get; private set; }
    public static Rect? LastSetRect { get; private set; }

    static void Draw(int id, Vector2 anchor)
    {
        LastSetRect = null;
        if (ItemCatalog.IsEquip(id)) DrawEquipment(id, anchor);
        else DrawCommon(id, anchor);
    }

    static void Canvas(Rect rect, string node, float x, float y, float s)
    {
        var path = Atlas + node;
        var texture = WzAssets.Texture(path);
        if (texture == null) return;
        var origin = WzAssets.Node(path)?.Get("origin");
        GUI.DrawTexture(new Rect(rect.x + (x - (origin?.X ?? 0)) * s,
            rect.y + (y - (origin?.Y ?? 0)) * s, texture.width * s, texture.height * s), texture);
    }

    static void DrawIcon(Rect rect, int id, float header, float s)
    {
        Fill(new Rect(rect.x + 10 * s, rect.y + (header + 32) * s, 68 * s, 68 * s), new Color32(255,255,255,160));
        var icon = NativeItemSource.Icon(id);
        if (icon == null) return;
        var origin = NativeItemSource.IconNode(id)?.Get("origin");
        GUI.DrawTexture(new Rect(rect.x + (12 - 2 * (origin?.X ?? 0)) * s,
            rect.y + (header + 98 - 2 * (origin?.Y ?? 0)) * s, icon.width * 2 * s, icon.height * 2 * s), icon);
    }

    // —— 中文标签 ——
    // 源端 EXE 的字符串池是英文，中文由 ijl15.dll 启动时按「字符串池 id」逐条替换：
    // .rdata 里一张 1914 条的 {int id; std::string 中文} 表（0x10036948，步长 0x1C），
    // GetString 被 hook 后先查这张表（0x10013D68），命中就返回中文。
    // 下面每条注释里的数字就是字符串池 id，完整表见
    // .agents/skills/beidou-client-reverse/references/string-pool-zh.json。
    const string LCategory = "装备分类 :";      // 5530  CATEGORY :
    const string LAttackSpeed = "攻击速度 :";   // 656   ATTACK SPEED :
    const string LUpgrades = "可升级次数 :";    // 668   NUMBER OF UPGRADES AVAILABLE :
    const string LQuestItem = "任务道具";       // 693   Quest Item
    // 2042..2047, 657..665；HP/MP 源端没有翻译，保留原样
    static readonly string[] StatLabels =
    {
        "力量 :", "敏捷 :", "智力 :", "运气 :", "HP :", "MP :",
        "攻击力 :", "魔法力 :", "防御力 :", "魔法防御力 :",
        "命中率 :", "回避率 :", "手技 :", "移动速度 :", "跳跃力 :"
    };
    // 644..650：FASTEST/FASTER/FAST/NORMAL/SLOW/SLOWER/SLOWEST
    static readonly string[] SpeedNames = { "非常快", "非常快", "比较快", "比较快", "快", "快", "普通", "慢", "慢", "比较慢", "比较慢", "非常慢", "非常慢" };
    // 601..616 武器分类；614(拳套) 源端漏翻，按中文习惯补
    static readonly string[] WeaponNames =
    {
        "单手剑", "单手斧", "单手钝器", "短刀", "", "", "", "短杖", "长杖", "", "双手剑",
        "双手斧", "双手钝器", "枪", "矛", "弓", "弩", "拳套", "指节", "短枪"
    };

    // 8E8252 -> 8ED0D2 (requirements) / 8F3BC3 (property rows).
    // 职业图标行：这些 canvas 自带左右透明边（beginner 40 宽里只有 23px 是实心、
    // warrior 左边 9px 全透明），按 canvas 宽顺序排会显得「新手→战士」中间空一大截。
    // 这里按实心像素范围 + 固定 6px 间距排，六格看起来才是等距的。
    static readonly string[] JobNames = { "beginner", "warrior", "magician", "bowman", "thief", "pirate" };
    static readonly int[] JobPaint = { 0, 23, 9, 32, 1, 36, 2, 36, 0, 23, 3, 26 };  // 每职业 (实心左, 实心右)
    static readonly float[] JobX = BuildJobX();

    static float[] BuildJobX()
    {
        var xs = new float[JobNames.Length];
        float cursor = 10f;     // 与源端职业行起点一致（0x8ED3xx push 0xa）
        for (int i = 0; i < xs.Length; i++)
        {
            xs[i] = cursor - JobPaint[i * 2];
            cursor += (JobPaint[i * 2 + 1] - JobPaint[i * 2]) + 6f;
        }
        return xs;
    }

    static void DrawEquipment(int id, Vector2 anchor)
    {
        // Hover references can outlive an authoritative inventory replacement after a cube.
        if (hoverItem != null && GameSession.Character != null)
            hoverItem = GameSession.Character.inventory.Find(x => x.itemRowId == hoverItem.itemRowId) ?? hoverItem;
        var info = EquipCatalog.Info(id);
        if (info == null) return;
        float s = BottomBar.S;
        var rows = new List<string[]>();
        int prefix = id / 10000;
        string category = prefix >= 130 && prefix <= 149 && prefix - 130 < WeaponNames.Length
            ? WeaponNames[prefix - 130] : null;
        if (string.IsNullOrEmpty(category))
        {
            switch (prefix)
            {
                case 100: category = "帽子"; break;        // 617
                case 101: category = "脸饰"; break;        // 618
                case 102: category = "眼饰"; break;        // 619
                case 103: category = "耳环"; break;        // 620
                case 104: category = "上衣"; break;        // 622
                case 105: category = "套服"; break;        // 621
                case 106: category = "裤/裙"; break;       // 623
                case 107: category = "鞋子"; break;        // 624
                case 108: category = "手套"; break;        // 625
                case 109: category = "盾牌"; break;        // 627
                case 110: category = "披风"; break;        // 626
                case 111: category = "戒指"; break;        // 628
                case 112: category = "坠子"; break;        // 629
                case 113: category = "腰带"; break;        // 631
                case 114: category = "勋章"; break;        // 630
            }
        }
        if (!string.IsNullOrEmpty(category)) rows.Add(new[] { LCategory, " " + category });
        if (prefix >= 130 && prefix <= 149 && info.Get("attackSpeed") != null)
        {
            int speed = info.Int("attackSpeed");
            if (speed >= 0 && speed < SpeedNames.Length) rows.Add(new[] { LAttackSpeed, " " + SpeedNames[speed] });
        }
        string[] keys = { "incSTR", "incDEX", "incINT", "incLUK", "incMHP", "incMMP", "incPAD", "incMAD", "incPDD", "incMDD", "incACC", "incEVA", "incCraft", "incSpeed", "incJump" };
        string[] labels = StatLabels;
        int[] actual = hoverItem?.isEquip == true ? new[] { hoverItem.str, hoverItem.dex, hoverItem.int_, hoverItem.luk,
            hoverItem.hp, hoverItem.mp, hoverItem.watk, hoverItem.matk, hoverItem.wdef, hoverItem.mdef,
            hoverItem.acc, hoverItem.avoid, hoverItem.hands, hoverItem.speed, hoverItem.jump } : null;
        bool version282 = hoverItem?.equipmentData != null &&
            hoverItem.equipmentData.TryGetValue("enhancementVersion", out var version) && System.Convert.ToInt32(version) == 282;
        bool vestige = hoverItem?.equipmentData != null &&
            hoverItem.equipmentData.TryGetValue("starForceVestige", out var trace) && System.Convert.ToInt32(trace) != 0;
        string[] statKeys = { "str", "dex", "int", "luk", "hp", "mp", "watk", "matk", "wdef", "mdef", "acc", "avoid", "hands", "speed", "jump" };
        var baseStats = new Dictionary<string, int>();
        bool native = NativeScrollService.HasStats(hoverItem);
        for (int i = 0; i < keys.Length; i++)
            baseStats[statKeys[i]] = version282 ? (native && actual != null ? actual[i] : info.Int(keys[i]))
                : actual == null ? info.Int(keys[i]) : System.Math.Max(actual[i], info.Int(keys[i]));
        int requiredLevel = info.Int("reqLevel");
        if (hoverItem?.equipmentData != null && hoverItem.equipmentData.TryGetValue("iIncReq", out var incReq))
            requiredLevel += System.Convert.ToInt32(incReq);
        var starStats = version282 && !vestige
            ? StarForceStats.Calculate(id, requiredLevel, hoverItem.starLevel, info.Int("superiorEqp") != 0, baseStats)
            : new Dictionary<string, int>();
        for (int i = 0; i < keys.Length; i++)
        {
            starStats.TryGetValue(statKeys[i], out int delta);
            int value = baseStats[statKeys[i]] + delta;
            if (value > 0) rows.Add(new[] { labels[i], " +" + value });
        }
        if (version282)
        {
            rows.Add(new[] { vestige ? "装备痕迹 :" : "星力强化 :", " " + hoverItem.starLevel + "星" });
            if (!vestige)
            {
                starStats.TryGetValue("watk", out int starAttack);
                starStats.TryGetValue("matk", out int starMagic);
                rows.Add(new[] { "星力攻击力 :", " +" + starAttack });
                rows.Add(new[] { "星力魔法力 :", " +" + starMagic });
            }
        }
        int upgrades = actual == null ? info.Int("tuc") : hoverItem.upgradeslots;
        // 老存档里存在「建了装备行但没种 WZ 值」的实例（upgradeslots 一直是 0）。
        // 这种行不是砸光了次数（level/星力都是 0），按道具数据补显示，否则新装备看着像不能砸卷。
        if (actual != null && upgrades <= 0 && hoverItem.equipLevel == 0 && hoverItem.starLevel == 0)
            upgrades = System.Math.Max(upgrades, info.Int("tuc"));
        if (upgrades > 0) rows.Add(new[] { LUpgrades, " " + upgrades });

        // 282 options belong to this equipment instance, never to the template or candidate journal.
        if (hoverItem?.equipmentData != null)
        {
            int Read(string key) => hoverItem.equipmentData.TryGetValue(key, out var v) ? System.Convert.ToInt32(v) : 0;
            int bucket = PotentialCatalog.OptionLevel(info.Int("reqLevel") + Read("iIncReq"));
            string[] grades = { "", "特殊", "稀有", "罕见", "传说" };
            foreach (bool bonus in new[] { false, true })
            {
                string prefixKey = bonus ? "bonusOption" : "potentialOption";
                int grade = Read(bonus ? "bonusGrade" : "potentialGrade");
                if (Read(prefixKey + "1") == 0) continue;
                rows.Add(new[] { bonus ? "附加潜能 :" : "主潜能 :", " " + grades[System.Math.Max(0, System.Math.Min(4, grade))] });
                for (int line = 1; line <= 3; line++)
                {
                    int option = Read(prefixKey + line);
                    if (option != 0) rows.Add(new[] { option < 0 ? "未鉴定潜能" : PotentialCatalog.Shared.Describe(option, bucket), "" });
                }
            }
        }

        // —— 星级条 ——
        // 282 的 tooltip 星条就是 Enchant.img/starForce/summaryStar 那三张 11x10 图标
        // （UIToolTip.img/Item/Equip/textIcon/starForce 直接 _outlink 过去）：
        //   步进 11px、每 5 颗多 10px（starForce/summaryStar/space X1=0/X5=10）、每行 15 颗；
        //   颜色即原始像素：金 0xFFFFD800、尊貴蓝 0xFF75E6FE、未点亮 0xFF1D232A(70%)。
        // 条高沿用 BeiDou DLL 的一列 27 / 两列 42 占位，星星在条内垂直居中。
        string equipName = ItemCatalog.Name(id);
        int stars = hoverItem != null ? hoverItem.starLevel : StarFromName(equipName);
        // Capacity is a template/rules property, not an enhancement-version marker.
        // A pristine item and its first successful enhancement must show the same slots.
        int capacity = info.Int("cash") == 0 && info.Int("tuc") > 0
            ? StarForceRules.Shared.Cap(id, requiredLevel, info.Int("superiorEqp") != 0) : 0;
        if (stars > capacity) capacity = stars;
        float starBar = capacity > 0 ? (capacity > 15 ? 42f : 27f) : 0f;
        // 星条画在**顶部**（源端 DLL 把星星 layer 锚在框顶，第一列 y=6、第二列 y=21），
        // 下面所有内容整体下移一个条高，否则星星会压在名字/图标上。
        float dy = capacity > 0 ? starBar + 4f : 0f;

        // 0x8E8E43 的高度：0xA6(166) + 行数×0x10(16) + 名称换行/附加段 + 星条
        // BeiDou 8E8E81 reads equipment desc too; 8E973A/8E9756 measures it
        // with font 7 at x=10, right=221, then appends it after the stat rows.
        string description = FormatDescription(ItemCatalog.Desc(id));
        var descriptionStyle = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * s),
            wordWrap = true, richText = true };
        descriptionStyle.normal.textColor = Color.white;
        float descriptionHeight = string.IsNullOrEmpty(description) ? 0
            : descriptionStyle.CalcHeight(new GUIContent(description), 211 * s);
        float height = (166 + rows.Count * 16 + starBar) * s + descriptionHeight;
        var p = anchor;
        var rect = new Rect(Mathf.Clamp(p.x, 0, Mathf.Max(0, UiKit.W - 236 * s)),
            Mathf.Clamp(p.y + 20 * s, 0, Mathf.Max(0, UiKit.H - height)), 236 * s, height);
        LastEquipRect = rect;
        DrawFrame(rect, s);
        var title = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * s) };
        title.normal.textColor = Color.white;
        GUI.Label(new Rect(rect.x + 18 * s, rect.y + 10 * s, 208 * s, 20 * s), equipName, title);
        Canvas(rect, "Dot/0", 15, 15, s);
        DrawIcon(rect, id, dy, s);
        var a = Profile.Attributes;
        string[] req = { "reqLevel", "reqSTR", "reqDEX", "reqINT", "reqLUK", "reqPOP" };
        string[] sprites = { "reqLEV", "reqSTR", "reqDEX", "reqINT", "reqLUK", "reqPOP" };
        int[] have = { a.level, a.str, a.dex, a.intelligence, a.luk, GameSession.Character?.record?.fame ?? 0 };
        for (int i = 0; i < req.Length; i++)
        {
            int need = info.Int(req[i]);
            string root = (have[i] >= need ? "Can/" : "Cannot/");
            Canvas(rect, root + sprites[i], 94, 32 + dy + i * 12, s);
            float x = 144;
            if (need == 0 && i == 5) Canvas(rect, root + "none", x, 32 + dy + i * 12, s);
            else foreach (char digit in need.ToString())
            {
                Canvas(rect, root + digit, x, 32 + dy + i * 12, s);
                x += 6;
            }
        }
        string[] jobs = JobNames;
        float[] jobX = JobX;
        if (info.Get("level") == null)
        {
            Canvas(rect, "GrowthDisabled/itemLEV", 94, 104 + dy, s);
            Canvas(rect, "GrowthDisabled/itemEXP", 94, 116 + dy, s);
            Canvas(rect, "GrowthDisabled/none", 148, 104 + dy, s);
            Canvas(rect, "GrowthDisabled/none", 148, 116 + dy, s);
        }
        int mask = info.Int("reqJob");
        for (int i = 0; i < jobs.Length; i++)
        {
            bool allowed = i == 0 ? mask <= 0 : mask == 0 || mask != -1 && (mask & (1 << (i - 1))) != 0;
            Canvas(rect, (allowed ? "Can/" : "Cannot/") + jobs[i], jobX[i], 133 + dy, s);
        }
        Fill(new Rect(rect.x + 6 * s, rect.y + (152 + dy) * s, rect.width - 12 * s, s), Color.white);
        var label = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * s) };
        label.normal.textColor = new Color32(250,232,203,255);
        var valueStyle = new GUIStyle(label); valueStyle.normal.textColor = Color.white;
        for (int i = 0; i < rows.Count; i++)
        {
            float y = rect.y + (159 + dy + i * 16) * s;
            float labelWidth = label.CalcSize(new GUIContent(rows[i][0])).x;
            GUI.Label(new Rect(rect.x + 10 * s, y, 216 * s, 13 * s), rows[i][0], label);
            GUI.Label(new Rect(rect.x + 10 * s + labelWidth, y, 216 * s - labelWidth, 13 * s), rows[i][1], valueStyle);
        }
        if (descriptionHeight > 0)
            GUI.Label(new Rect(rect.x + 10 * s, rect.y + (161 + dy + rows.Count * 16) * s,
                211 * s, descriptionHeight), description, descriptionStyle);
        if (capacity > 0)
            DrawStarBar(new Rect(rect.x, rect.y + 26 * s, rect.width, starBar * s),
                capacity, stars, info.Int("superiorEqp") != 0, s);
        DrawSetPanel(rect, id, s);
    }

    // 装备等级 → 星级上限（dll 0x65C81949 的跳表，等价于 GMS 星之力上限表）
    public static int StarCapacity(int level)
    {
        if (level < 0) return 0;
        if (level <= 94) return 5;
        if (level <= 107) return 8;
        if (level <= 117) return 10;
        if (level <= 127) return 15;
        if (level <= 137) return 20;
        return 25;
    }

    // 星数在名字尾部："<名>12★"（dll 0x65C81650 从字符串头读两位数字，再校验 GBK A1EF = ★）
    public static int StarFromName(string name)
    {
        if (string.IsNullOrEmpty(name) || name[name.Length - 1] != '★') return 0;
        int i = name.Length - 2, stars = 0, mult = 1;
        while (i >= 0 && name[i] >= '0' && name[i] <= '9' && mult <= 10)
        { stars += (name[i] - '0') * mult; mult *= 10; i--; }
        return stars;
    }

    // 星条：282 summaryStar 图标（11x10）、每行 15 颗、整块水平居中、条内垂直居中
    static void DrawStarBar(Rect bar, int capacity, int stars, bool superior, float s)
    {
        if (capacity <= 0 || !StarForceStars.Ready) return;
        StarForceStars.Draw(bar.x + bar.width * .5f, bar.y + bar.height * .5f, s, stars, capacity, superior);
    }

    // —— 套装块（源端 BeiDouSetItemCompat.dll 的 native set panel）——
    // 0x65C82F00 段：宽 0xEC(236)；标题 y=7「%s (%d/%d)」居中金 FFFFE137；
    // 装备行 y=25 起步进 16，左 x=8 名称（悬停件金 / 已穿白 / 未穿 90949D），右对齐「(%s)」槽位名；
    // 「套装效果」= 槽位末 +3；「[%d件]」= 槽位末 +19（白/灰），其属性行 x=16「%s +%d%s」（D2D2D2/90949D）步进 16。
    static int setCacheKey = int.MinValue;
    static List<AdminSetItem.SetPanel> setCache;
    static readonly List<int> wornBuffer = new List<int>();

    static void DrawSetPanel(Rect equip, int itemId, float s)
    {
        if (itemId <= 0) return;
        wornBuffer.Clear();
        if (GameSession.Character != null)
            foreach (var it in GameSession.Character.inventory)
                if (it.position < 0 && it.itemId > 0) wornBuffer.Add(it.itemId);
        wornBuffer.Sort();
        int key = itemId;
        foreach (int w in wornBuffer) key = key * 31 + w;
        if (key != setCacheKey || setCache == null)
        {
            setCache = AdminSetItem.PanelsFor(itemId, wornBuffer);
            setCacheKey = key;
        }
        var panels = setCache;
        if (panels.Count == 0) return;
        var panel = panels[0];

        int tierRows = 0;
        foreach (var t in panel.Tiers) tierRows += 1 + t.Stats.Count;
        float slotEnd = 25f + panel.Slots.Count * 16f;
        float h = slotEnd + (panel.Tiers.Count > 0 ? 19f + tierRows * 16f : 0f) + 8f;

        // 属性一多（八件套三档几十行）会比屏幕还高：面板自己放不下时，把每档属性并成一行自动折行，
        // 信息不丢，高度回到可用范围。源端按候选 alt 总数估高度，本身就会留白，这里按实际行数。
        bool compact = h > UiKit.H - 8f;
        if (compact)
        {
            float body = 0f;
            foreach (var t in panel.Tiers) body += PackStats(t.Stats).Count * 16f;
            h = slotEnd + 19f + body + 8f;
        }

        // 面板位置（源端 0x65C83012 段）：x = 装备框右侧 + 4、y = 装备框顶部，即**并排**在右边，
        // 不是叠在装备框下面。旧实现「贴下方 + 放不下就往上翻」会把装备框整个盖住，
        // 只剩底部几行露出来，看起来就像装备面板没画。
        var rect = SetPanelRect(equip, h, s, UiKit.W, UiKit.H);
        LastSetRect = rect;
        DrawFrame(rect, s);

        var gold = new Color32(255, 225, 55, 255);
        // 字号与装备框属性行一致（12）：源端两侧行距都是 16，9 号字摆在双倍行距里明显偏小
        // （用户 2026-10-29 反馈「下方属性描述的文字又很小」）。
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * s) };
        style.normal.textColor = gold;
        var center = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };
        var right = new GUIStyle(style) { alignment = TextAnchor.MiddleRight };
        var goldStyle = new GUIStyle(style);
        var white = new GUIStyle(style); white.normal.textColor = Color.white;
        var gray = new GUIStyle(style); gray.normal.textColor = new Color32(144, 148, 157, 255);
        var pale = new GUIStyle(style); pale.normal.textColor = new Color32(210, 210, 210, 255);

        // 标题：套装名 (已穿/完整)
        GUI.Label(new Rect(rect.x + 8 * s, rect.y + 5 * s, (236 - 16) * s, 16 * s),
            panel.Name + " (" + panel.EquippedCount + "/" + panel.CompleteCount + ")", center);

        // 装备行
        for (int i = 0; i < panel.Slots.Count; i++)
        {
            var slot = panel.Slots[i];
            var alt = slot.Alts.Count > 0 ? slot.Alts[0] : null;
            foreach (var a in slot.Alts) if (a.Equipped) { alt = a; break; }
            foreach (var a in slot.Alts) if (a.ItemId == itemId) { alt = a; break; }
            if (alt == null) continue;
            var st = alt.ItemId == itemId ? goldStyle : (alt.Equipped ? white : gray);
            float y = rect.y + (25 + i * 16 - 2) * s;
            GUI.Label(new Rect(rect.x + 8 * s, y, 150 * s, 16 * s), alt.Name, st);
            GUI.Label(new Rect(rect.x + 158 * s, y, 70 * s, 16 * s), "(" + alt.SlotName + ")", right);
        }

        if (panel.Tiers.Count == 0) return;
        float seY = rect.y + (slotEnd + 1) * s;
        GUI.Label(new Rect(rect.x + 8 * s, seY, (236 - 16) * s, 16 * s), "套装效果", goldStyle);

        float y2 = rect.y + (slotEnd + 17) * s;
        if (compact)
        {
            // 每行按属性粒度装箱，绝不把「最大HP +100」从数字中间切开
            foreach (var t in panel.Tiers)
            {
                bool active = panel.EquippedCount >= t.Required;
                var lines = PackStats(t.Stats);
                for (int i = 0; i < lines.Count; i++)
                {
                    string text = i == 0 ? ("[" + t.Required + "件] " + lines[i]) : ("  " + lines[i]);
                    GUI.Label(new Rect(rect.x + 8 * s, y2, (236 - 16) * s, 16 * s), text,
                        active ? pale : gray);
                    y2 += 16 * s;
                }
            }
            return;
        }
        foreach (var t in panel.Tiers)
        {
            bool active = panel.EquippedCount >= t.Required;
            GUI.Label(new Rect(rect.x + 8 * s, y2, (236 - 16) * s, 16 * s), "[" + t.Required + "件]", active ? white : gray);
            y2 += 16 * s;
            foreach (var kv in t.Stats)
            {
                string text = SetStatName(kv.Key) + " +" + kv.Value + SetStatSuffix(kv.Key);
                GUI.Label(new Rect(rect.x + 16 * s, y2, (236 - 24) * s, 16 * s), text, active ? pale : gray);
                y2 += 16 * s;
            }
        }
    }

    // 套装面板的落点。源端 0x65C83012：x = 236 + 装备框 x + 4、y = 装备框 y（顶部对齐）。
    // 右边放不下改贴左侧；两侧都放不下才夹进屏幕。抽成纯函数是为了无渲染断言（TooltipLayoutChecks）。
    public static Rect SetPanelRect(Rect equip, float h, float s, float screenW, float screenH)
    {
        float gap = 4f * s, w = 236f * s, ph = h * s;
        float x = equip.xMax + gap;
        if (x + w > screenW - 4f)
        {
            if (equip.x - gap - w >= 4f) x = equip.x - gap - w;
            else x = Mathf.Clamp(x, 4f, Mathf.Max(4f, screenW - w - 4f));
        }
        float y = Mathf.Clamp(equip.y, 4f, Mathf.Max(4f, screenH - ph - 4f));
        return new Rect(x, y, w, ph);
    }

    // 紧凑模式的装箱：按属性整段折行，一行约 17 个字符宽（236-16=220px，12 号字）
    static List<string> PackStats(List<KeyValuePair<string, int>> stats)
    {
        const int perLine = 17;
        var lines = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var kv in stats)
        {
            string piece = SetStatName(kv.Key) + " +" + kv.Value + SetStatSuffix(kv.Key);
            if (cur.Length > 0 && cur.Length + 1 + piece.Length > perLine)
            {
                lines.Add(cur.ToString());
                cur.Length = 0;
            }
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(piece);
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add("—");
        return lines;
    }

    // DLL 里属性 key 走 strstr 匹配：PAD→攻击力、MAD→魔法攻击力、HP→最大HP、MP→最大MP、
    // FinalDamage→最终伤害、BossDamage→Boss伤害、ExpRate→经验获得、DropRate→掉落率、MesoRate→金币获得。
    static string SetStatName(string key)
    {
        if (key == null) return "属性";
        if (key.Contains("FinalDamage")) return "最终伤害";
        if (key.Contains("BossDamage")) return "Boss伤害";
        if (key.Contains("NormalDamage")) return "普通伤害";
        if (key.Contains("PAD")) return "攻击力";
        if (key.Contains("MAD")) return "魔法攻击力";
        if (key.Contains("PDD")) return "物理防御力";
        if (key.Contains("MDD")) return "魔法防御力";
        if (key.Contains("ACC")) return "命中率";
        if (key.Contains("EVA")) return "回避率";
        if (key.Contains("HP")) return "最大HP";
        if (key.Contains("MP")) return "最大MP";
        if (key.Contains("ExpRate")) return "经验获得";
        if (key.Contains("DropRate")) return "掉落率";
        if (key.Contains("MesoRate")) return "金币获得";
        if (key.Contains("STR")) return "力量";
        if (key.Contains("DEX")) return "敏捷";
        if (key.Contains("INT")) return "智力";
        if (key.Contains("LUK")) return "幸运";
        if (key.Contains("SPD")) return "移动速度";
        if (key.Contains("JMP")) return "跳跃力";
        return "属性";
    }

    // 百分比后缀：StatusRes/BuffDuration 是绝对值，其余（Damage/Pct/Rate）带 %
    static string SetStatSuffix(string key)
    {
        if (key == null) return "";
        if (key.Contains("StatusRes") || key.Contains("BuffDuration")) return "";
        if (key.Contains("Pct") || key.Contains("Rate") || key.Contains("Damage")) return "%";
        return "";
    }

    // BeiDou 8EDFC7 -> 8F3141 / 8F421E / 8F4535 / 8F4818.
    static void DrawCommon(int id, Vector2 anchor)
    {
        float s = BottomBar.S;
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * s),
            wordWrap = true, padding = new RectOffset(), margin = new RectOffset() };
        style.normal.textColor = Color.white;
        string desc = FormatDescription(ItemCatalog.Desc(id));
        style.richText = true;
        bool questItem = NativeItemSource.Node(id)?.Get("info")?.Int("quest") == 1;
        float header = questItem ? 19 * s : 0;
        float textHeight = style.CalcHeight(new GUIContent(desc), 178 * s);
        // 0x8EEE9E：116 + (字高+4)×(行数-4)，超出四行才增高；同时兜住实际文本高度避免裁字
        int lines = Mathf.Max(1, Mathf.RoundToInt(textHeight / Mathf.Max(1f, 12f * s)));
        float srcHeight = (116f + Mathf.Max(0, lines - 4) * 16f) * s;
        float height = header + Mathf.Max(srcHeight, 52 * s + textHeight);
        Vector2 p = anchor;
        var rect = new Rect(Mathf.Clamp(p.x, 0, Mathf.Max(0, UiKit.W - 290 * s)),
            Mathf.Clamp(p.y + 20 * s, 0, Mathf.Max(0, UiKit.H - height)), 290 * s, height);
        LastEquipRect = rect;
        DrawFrame(rect, s);
        // 8F4818: name at (18,10), with the source's leading marker at (10,15).
        GUI.Label(new Rect(rect.x + 18 * s, rect.y + 10 * s, 262 * s, 20 * s), ItemCatalog.Name(id), style);
        var dot = WzAssets.Texture("UI/UIWindow.img/ToolTip/Equip/Dot/0");
        if (dot != null) GUI.DrawTexture(new Rect(rect.x + 10 * s, rect.y + 15 * s, dot.width * s, dot.height * s), dot);
        if (questItem)
        {
            var flagStyle = new GUIStyle(style) { alignment = TextAnchor.UpperCenter };
            flagStyle.normal.textColor = new Color32(255, 138, 24, 255); // font 11, 8E52FB
            GUI.Label(new Rect(rect.x, rect.y + 29 * s, rect.width, 19 * s), LQuestItem, flagStyle);
        }
        // 8F421E: 68-square background, doubled source canvas, anchor (x+2,y+66).
        Fill(new Rect(rect.x + 10 * s, rect.y + header + 32 * s, 68 * s, 68 * s), new Color32(255, 255, 255, 160));
        var icon = NativeItemSource.Icon(id);
        var canvas = NativeItemSource.IconNode(id);
        if (icon != null)
        {
            var origin = canvas.Get("origin");
            float ox = origin?.X ?? 0, oy = origin?.Y ?? 0;
            GUI.DrawTexture(new Rect(rect.x + (12 - 2 * ox) * s, rect.y + header + (98 - 2 * oy) * s,
                icon.width * 2 * s, icon.height * 2 * s), icon);
        }
        GUI.Label(new Rect(rect.x + 92 * s, rect.y + header + 32 * s, 178 * s, height - header - 40 * s), desc, style);
    }

    // 8E4217..8E423B toggles emphasis on # (optional c); font 9 is FFFF9900 at 98ADCC.
    public static string FormatDescription(string text)
    {
        text = (text ?? "").Replace("\\\\n", "\n").Replace("\\n", "\n");
        var result = new System.Text.StringBuilder();
        bool emphasis = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '#')
            {
                result.Append(emphasis ? "</color>" : "<color=#FF9900>");
                if (!emphasis && i + 1 < text.Length && text[i + 1] == 'c') i++;
                emphasis = !emphasis;
            }
            else if (ch == '<') result.Append("&lt;");
            else result.Append(ch);
        }
        if (emphasis) result.Append("</color>");
        return result.ToString();
    }

    static void Fill(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, White);
        GUI.color = previous;
    }

    // 帧纹理按 (宽,高) 缓存：物品框与技能框尺寸不同，来回悬浮时不能每次重建
    static readonly Dictionary<int, Texture2D> frames = new Dictionary<int, Texture2D>();
    // 8F3141 的帧：物品 kind5/4 与技能 kind7 共用同一条源函数，这里对外公开供 SkillWindow 复用。
    public static void DrawFrame(Rect rect, float s)
    {
        int w = Mathf.RoundToInt(rect.width / s), h = Mathf.RoundToInt(rect.height / s);
        int key = w * 4096 + h;
        if (!frames.TryGetValue(key, out var frame) || frame == null)
        {
            if (frames.Count >= 6)
            {
                foreach (var old in frames.Values) if (old != null) Object.Destroy(old);
                frames.Clear();
            }
            frame = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[w * h];
            // 8F3141: fill A0000040; clear four corners and the inset 1-pixel contour (00FFFFFF).
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                bool clear = (x == 0 || x == w - 1) && (y == 0 || y == h - 1)
                    || (x == 1 || x == w - 2) && y >= 2 && y < h - 2
                    || (y == 1 || y == h - 2) && x >= 2 && x < w - 2;
                pixels[y * w + x] = clear ? new Color32(255,255,255,0) : new Color32(0,0,64,160);
            }
            frame.SetPixels32(pixels); frame.Apply();
            frames[key] = frame;
        }
        GUI.DrawTexture(rect, frame);
    }

}
