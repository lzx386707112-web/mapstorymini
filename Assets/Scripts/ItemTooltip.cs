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

    // —— 中文标签 ——
    // 源端 EXE 的字符串池是英文，中文由 ijl15.dll 启动时按「字符串池 id」逐条替换：
    // .rdata 里一张 1914 条的 {int id; std::string 中文} 表（0x10036948，步长 0x1C），
    // GetString 被 hook 后先查这张表（0x10013D68），命中就返回中文。
    // 下面每条注释里的数字就是字符串池 id，完整表见
    // .agents/skills/beidou-client-reverse/references/string-pool-zh.json。
    const string LCategory = "装备分类 :";      // 5530  CATEGORY :
    const string LAttackSpeed = "攻击速度 :";   // 656   ATTACK SPEED :
    const string LUpgrades = "可升级次数 :";    // 668   NUMBER OF UPGRADES AVAILABLE :
    const string LPowerGain = "战斗力提升量";   // 282 装备框图标右侧顶部的小标签（客户端算，非 WZ 文案）
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

    static void DrawEquipment(int id, Vector2 anchor)
    {
        // Hover references can outlive an authoritative inventory replacement after a cube.
        if (hoverItem != null && GameSession.Character != null)
            hoverItem = GameSession.Character.inventory.Find(x => x.itemRowId == hoverItem.itemRowId) ?? hoverItem;
        var info = EquipCatalog.Info(id);
        if (info == null) return;
        float s = BottomBar.S;
        var rows = new List<string[]>();
        int upgradeSlots = -1;   // -1 = 本次没有「可升级次数」行
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
        if (prefix >= 130 && prefix <= 149 && info.Get("attackSpeed") != null)
        {
            int speed = info.Int("attackSpeed");
            if (speed >= 0 && speed < SpeedNames.Length) rows.Add(new[] { LAttackSpeed, " " + SpeedNames[speed] });
        }
        // 基础 / 星力 / 星火三层分开取：ItemStats 是 tooltip 与战斗力预览共用的一份口径，
        // 免得「面板说 +36、预览按 +24 算」这种两套数字。
        var stats = hoverItem != null ? ItemStats.Of(hoverItem) : ItemStats.Template(id);
        string[] labels = StatLabels;
        int[] actual = hoverItem?.isEquip == true ? new[] { hoverItem.str, hoverItem.dex, hoverItem.int_, hoverItem.luk,
            hoverItem.hp, hoverItem.mp, hoverItem.watk, hoverItem.matk, hoverItem.wdef, hoverItem.mdef,
            hoverItem.acc, hoverItem.avoid, hoverItem.hands, hoverItem.speed, hoverItem.jump } : null;
        bool version282 = hoverItem?.equipmentData != null &&
            hoverItem.equipmentData.TryGetValue("enhancementVersion", out var version) && System.Convert.ToInt32(version) == 282;
        bool vestige = stats.starForceVestige;
        int requiredLevel = info.Int("reqLevel");
        if (hoverItem?.equipmentData != null && hoverItem.equipmentData.TryGetValue("iIncReq", out var incReq))
            requiredLevel += System.Convert.ToInt32(incReq);
        for (int i = 0; i < labels.Length; i++)
        {
            int baseValue = stats.baseStats[i];
            int delta = stats.starStats[i];
            int flameValue = stats.flameStats[i];
            int value = baseValue + delta + flameValue;
            if (value > 0)
            {
                string detail = " +" + value;
                if (delta != 0 || flameValue != 0)
                {
                    detail += " (" + baseValue;
                    if (delta != 0) detail += " <color=#FFCC00>+" + delta + "</color>";
                    if (flameValue != 0) detail += " <color=#0AE3AD>+" + flameValue + "</color>";
                    detail += ")";
                }
                rows.Add(new[] { labels[i], detail });
            }
        }
        if (version282)
        {
            rows.Add(new[] { vestige ? "装备痕迹 :" : "星力强化 :", " " + hoverItem.starLevel + "星" });
            if (!vestige)
            {
                rows.Add(new[] { "星力攻击力 :", " +" + stats.starStats[6] });
                rows.Add(new[] { "星力魔法力 :", " +" + stats.starStats[7] });
            }
        }
        int upgrades = actual == null ? info.Int("tuc") : hoverItem.upgradeslots;
        // 老存档里存在「建了装备行但没种 WZ 值」的实例（upgradeslots 一直是 0）。
        // 这种行不是砸光了次数（level/星力都是 0），按道具数据补显示，否则新装备看着像不能砸卷。
        if (actual != null && upgrades <= 0 && hoverItem.equipLevel == 0 && hoverItem.starLevel == 0)
            upgrades = System.Math.Max(upgrades, info.Int("tuc"));
        // 282 恒定画这一行：UIToolTip.img/Item/Equip/textIcon/scroll/normal 是「次数已用尽」的
        // 灰色 (#85919F)，/enhanced 是「还有次数」的紫色 (#AFADFF)，与 starForce、bonusStat 的
        // normal/enhanced 同一套约定 —— normal 不代表隐藏，所以剩 0 次也要画出来（否则銀之心、
        // 砸满卷的装备整行消失，用户看到的就是「漏了一个可升级次数」）。
        // 只有模板完全没有 tuc 节点的装备（现金/特殊装备）才没有这一行。
        if (info.Get("tuc") != null || upgrades > 0)
        {
            upgradeSlots = upgrades;
            rows.Add(new[] { LUpgrades, " " + upgrades });
        }

        // 282 的「戰鬥力提升量」不在正文里：它是图标右侧顶部的一行小标签，数值画在标签下面
        // （实拍：`戰鬥力提升量` 上行、`+ 220萬 4312` 下行，下面是类别徽标；已穿着时那一行
        // 换成 imgFont/atkPow/equipped 那张 145×18 的「目前穿戴中的裝備」）。
        // 所以这里只算数，正文不再加行，绘制在 header 那段。
        long powerGain = 0;
        bool hasPowerLabel = CombatPower.IsEquippable(id);
        bool hasPowerGain = CombatPower.TryPreview(hoverItem, out long powerNow, out long powerAfter);
        if (hasPowerGain) powerGain = powerAfter - powerNow;

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

        // —— 282 成长系统：徽章 / 星火 / 灵魂（都挂在实例行上，模板里没有）——
        if (hoverItem != null && SymbolCatalog.IsSymbol(hoverItem.itemId))
        {
            // 282 EquipSymbol：等级 1..20（圣徽 11）、经验 / 本级门槛、按装备给的 ARC/AUT 点数。
            var symbol = SymbolCatalog.Of(hoverItem.itemId);
            if (symbol != null)
            {
                int level = hoverItem.symbolLevel < 1 ? 1 : hoverItem.symbolLevel;
                int need = symbol.GrowthCost(level);
                rows.Add(new[] { "徽章等级 :", " Lv" + level + " / " + symbol.maxLevel });
                rows.Add(new[] { "徽章经验 :", " " + hoverItem.symbolExp + " / " + need });
                rows.Add(new[] { symbol.IsArcane ? "ARC :" : "AUT :", " +" + hoverItem.symbolInc });
                // 升级时写进装备行的主属性（282 的 setInc 同口径），单独列一行避免和 inc 混淆。
                string column = NativeSymbolService.MainStatColumn(Profile.JobId);
                int mainStat = column == "dex" ? hoverItem.dex : column == "int" ? hoverItem.int_
                    : column == "luk" ? hoverItem.luk : column == "hp" ? hoverItem.hp : hoverItem.str;
                if (mainStat > 0) rows.Add(new[] { column == "hp" ? "HP增加 :" : "主属性增加 :", " +" + mainStat });
            }
        }
        else if (hoverItem != null)
        {
            foreach (var line in NativeGrowthStats.DescribeFlame(hoverItem))
                if (line.value > 0 && line.statVal >= 17) rows.Add(new[] { "星火 · " + line.label + " :", " +" + line.value });
            if (!string.IsNullOrEmpty(hoverItem.flameChoice)) rows.Add(new[] { "星火：", " 有待确认的结果" });
            var modernSoul=GameSession.Character?.soulWeapon;
            // 成长按武器实例存：悬停任意一把练过的武器都显示它自己的进度。
            var bound=modernSoul?.Of(hoverItem.itemRowId);
            if(bound!=null)
            {
                bool activated=modernSoul.weapon==hoverItem.itemRowId;
                bool effective=activated && modernSoul.Equipped(GameSession.Character);
                rows.Add(new[] {"灵魂武器 · ", "第"+bound.grade+"阶段"});
                rows.Add(new[] {"", "Lv. "+bound.level+"（攻击力／魔法力：+"+SoulWeaponWz2Catalog.PassiveAttack(bound.level)+"）"});
                string optionText = SoulWeaponWz2Catalog.DescribeOption(bound.option);
                if (bound.option > 0) rows.Add(new[] {"", optionText});
                string usable=effective?"可使用":activated?"装备后可使用":"启用并装备后可使用";
                rows.Add(new[] {"", usable+"「"+SoulWeaponWz2Catalog.SkillName(bound.grade)+"」技能"});
            }
            if (hoverItem.soulSocketId > 0)
            {
                rows.Add(new[] { "灵魂插槽 :", " 已开启" });
                if (hoverItem.soulItemId > 0)
                    rows.Add(new[] { "灵魂 :", " " + ItemCatalog.Name(hoverItem.soulItemId) });
                var soul = SoulCatalog.Of(hoverItem.soulOption);
                if (soul != null && !string.IsNullOrEmpty(soul.key))
                    rows.Add(new[] { "灵魂属性 :", " " + soul.key + " +" + soul.value });
            }
        }

        // —— 星级条 ——
        // 282 的 tooltip 星条就是 Enchant.img/starForce/summaryStar 那三张 11x10 图标
        // （UIToolTip.img/Item/Equip/textIcon/starForce 直接 _outlink 过去）：
        //   步进 11px、每 5 颗多 10px（starForce/summaryStar/space X1=0/X5=10）、每行 15 颗；
        //   颜色即原始像素：金 0xFFFFD800、尊貴蓝 0xFF75E6FE、未点亮 0xFF1D232A(70%)。
        // 条高沿用 BeiDou DLL 的一列 27 / 两列 42 占位，星星在条内垂直居中。
        string equipName = ItemCatalog.Name(id);
        if (string.IsNullOrEmpty(equipName)) equipName = WzNames.Item(id);
        if (string.IsNullOrEmpty(equipName)) equipName = id.ToString();
        int stars = hoverItem != null ? hoverItem.starLevel : StarFromName(equipName);
        // Capacity is a template/rules property, not an enhancement-version marker.
        // A pristine item and its first successful enhancement must show the same slots.
        int capacity = info.Int("cash") == 0 && info.Int("tuc") > 0 && info.Int("exceptUpgrade") == 0 && info.Int("exUpgradeBlock") == 0
            ? StarForceRules.Shared.Cap(id, requiredLevel, info.Int("superiorEqp") != 0) : 0;
        if (stars > capacity) capacity = stars;
        float starBar = capacity > 0 ? (capacity > 15 ? 42f : 27f) : 0f;
        // User-requested screenshot layout; geometry/colors/artwork come from
        // UIToolTip.img/Item/Equip. This is not a claim of EXE pixel equivalence.
        const float width = 324, margin = 15, bodyWidth = width - margin * 2;
        var body = new GUIStyle { font = UiKit.Font, fontSize = 12, wordWrap = true,
            richText = true, padding = new RectOffset(), margin = new RectOffset() };
        body.normal.textColor = Color.white;
        var title = new GUIStyle(body) { fontSize = 14, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter };
        var gray = new GUIStyle(body); gray.normal.textColor = new Color32(183,191,197,255);
        float nameY = 6 + starBar;
        float nameHeight = title.CalcHeight(new GUIContent(equipName), bodyWidth);
        float iconY = nameY + nameHeight + 22;
        float contentY = iconY + 82;
        var requirements = new List<string[]>();
        requirements.Add(new[] { "装备种类", category ?? "装备" });
        requirements.Add(new[] { "要求等级", "Lv. " + requiredLevel });
        string[] reqKeys = { "reqSTR", "reqDEX", "reqINT", "reqLUK", "reqPOP" };
        string[] reqNames = { "力量", "敏捷", "智力", "运气", "人气" };
        for (int i = 0; i < reqKeys.Length; i++)
            if (info.Int(reqKeys[i]) > 0) requirements.Add(new[] { reqNames[i], info.Int(reqKeys[i]).ToString() });
        int jobMask = info.Int("reqJob");
        string[] jobLabels = { "战士", "魔法师", "弓箭手", "飞侠", "海盗" };
        string jobRequirement = jobMask == 0 ? "全职业" : jobMask == -1 ? "新手" : "";
        for (int i = 0; jobMask > 0 && i < jobLabels.Length; i++)
            if ((jobMask & (1 << i)) != 0) jobRequirement += (jobRequirement.Length > 0 ? "／" : "") + jobLabels[i];
        requirements.Add(new[] { "可用职业", jobRequirement });
        float reqHeight = 0;
        foreach (var requirement in requirements)
            reqHeight += Mathf.Max(16, body.CalcHeight(new GUIContent(requirement[1]), width - 100 - margin));
        contentY += reqHeight + 8;
        var rowHeights = new List<float>();
        foreach (var row in rows)
        {
            float rh = Mathf.Max(16, body.CalcHeight(new GUIContent(row[0] + row[1]), bodyWidth - 15));
            if (PotentialHeader(row[0])) rh += 9;
            rowHeights.Add(rh);
        }
        string description = FormatDescription(ItemCatalog.Desc(id));
        float descriptionHeight = string.IsNullOrEmpty(description) ? 0
            : body.CalcHeight(new GUIContent(description), bodyWidth) + 8;
        // User-requested mobile overflow: flow rows into original-skin columns.
        var setRows = SetRowsFor(id, out string setTitle);
        var rowColumns = new List<int>();
        var rowPositions = new List<float>();
        var columnHeights = new List<float>();
        float maxHeight = 0;
        int setColumns = 0, setRowsPerColumn = 0;
        s = Mathf.Max(.01f, Mathf.Min(s, (UiKit.H - 8) / (contentY + 44)));
        for (int attempt = 0; attempt < 8; attempt++)
        {
            maxHeight = (UiKit.H - 8) / s;
            FlowEquipRows(rows, rowHeights, contentY, descriptionHeight, maxHeight,
                rowColumns, rowPositions, columnHeights);
            setRowsPerColumn = Mathf.Max(2, Mathf.FloorToInt((maxHeight - 38) / 16));
            setColumns = setRows.Count == 0 ? 0 : (setRows.Count + setRowsPerColumn - 1) / setRowsPerColumn;
            float totalWidth = columnHeights.Count * 328 - 4 + setColumns * 240;
            float fit = (UiKit.W - 8) / totalWidth;
            if (s <= fit + .0001f) break;
            s = Mathf.Max(.01f, fit);
        }
        float equipmentWidth = columnHeights.Count * 328 - 4;
        float groupWidth = equipmentWidth + setColumns * 240;
        float groupHeight = 0;
        foreach (float ch in columnHeights) groupHeight = Mathf.Max(groupHeight, ch);
        if (setColumns > 0) groupHeight = Mathf.Max(groupHeight, 38 + Mathf.Min(setRows.Count, setRowsPerColumn) * 16);
        float groupX = Mathf.Clamp(anchor.x, 4, Mathf.Max(4, UiKit.W - groupWidth * s - 4));
        float groupY = Mathf.Clamp(anchor.y + 20 * s, 4, Mathf.Max(4, UiKit.H - groupHeight * s - 4));
        var rect = new Rect(groupX, groupY, width * s, columnHeights[0] * s);
        LastEquipRect = new Rect(groupX, groupY, equipmentWidth * s, groupHeight * s);
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;
        var oldContentColor = GUI.contentColor;
        bool oldEnabled = GUI.enabled;
        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.contentColor = Color.white;
        try
        {
            GUI.matrix = oldMatrix * Matrix4x4.TRS(new Vector3(rect.x, rect.y, 0), Quaternion.identity, new Vector3(s, s, 1));
            for (int column = 0; column < columnHeights.Count; column++)
            {
                float left = column * 328, ch = columnHeights[column];
                EquipTexture("frame/common/mid", new Rect(left, 0, width, ch));
                EquipTexture("frame/common/top", new Rect(left, 0, width, 30));
                EquipTexture("frame/common/btm", new Rect(left, ch - 12, width, 12));
            }
            if (capacity > 0)
                DrawEquipStars(width, starBar, capacity, stars, info.Int("superiorEqp") != 0);
            GUI.Label(new Rect(margin, nameY, bodyWidth, nameHeight), equipName, title);
            // Match the authoritative trade flags; do not mark every equipment bound.
            bool bound = hoverItem != null && (hoverItem.flag & 8) != 0;
            bool restricted = info.Int("tradeBlock") == 1 || info.Int("accountSharable") == 1 || info.Int("quest") == 1;
            if (bound || restricted && (hoverItem == null || (hoverItem.flag & 16) == 0))
            {
                var restriction = new GUIStyle(body) { alignment = TextAnchor.UpperCenter };
                restriction.normal.textColor = new Color32(255,153,0,255);
                GUI.Label(new Rect(margin, nameY + nameHeight, bodyWidth, 16), "无法交换", restriction);
            }
            EquipTexture("frame/common/line", new Rect(0, iconY - 7, width, 3));
            EquipTexture("itemIcon/base", new Rect(margin, iconY, 78, 78));
            var icon = NativeItemSource.Icon(id);
            var origin = NativeItemSource.IconNode(id)?.Get("origin");
            if (icon != null)
                GUI.DrawTexture(new Rect(margin + 6 - 2 * (origin?.X ?? 0), iconY + 72 - 2 * (origin?.Y ?? 0),
                    icon.width * 2, icon.height * 2), icon);
            // 282 实拍：图标右侧上方是「戰鬥力提升量」小标签，下面一行是大号数值
            // `+ 220萬 4312`（增亮白 / 减洋红 #F00068）；已穿着的装备把数值这一行换成
            // imgFont/atkPow/equipped 那张「目前穿戴中的裝備」。
            var state = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
            state.normal.textColor = gray.normal.textColor;
            if (hoverItem != null && hoverItem.position < 0)
                GUI.Label(new Rect(100, iconY + 18, width - 100 - margin, 32), "当前穿戴中的装备", state);
            if (hasPowerLabel)
            {
                GUI.Label(new Rect(100, iconY - 1, width - 100 - margin, 16), LPowerGain,
                    new GUIStyle(gray) { alignment = TextAnchor.UpperRight });
                if (hasPowerGain && powerGain != 0)
                {
                    string text = (powerGain > 0 ? "+ " : "- ") + NumberFormat.Compact(System.Math.Abs(powerGain));
                    var amount = new GUIStyle(title)
                    { alignment = TextAnchor.MiddleRight, fontSize = PowerFontSize(text), fontStyle = FontStyle.Bold };
                    amount.normal.textColor = powerGain > 0 ? Color.white : new Color32(240, 0, 104, 255);
                    GUI.Label(new Rect(100, iconY + 16, width - 100 - margin, 30), text, amount);
                }
            }
            string categoryText = category ?? "装备";
            float categoryWidth = gray.CalcSize(new GUIContent(categoryText)).x + 18;
            float categoryX = width - margin - categoryWidth;
            EquipTexture("frame/common/category/w", new Rect(categoryX, iconY + 57, 8, 14));
            EquipTexture("frame/common/category/c", new Rect(categoryX + 8, iconY + 57, categoryWidth - 16, 14));
            EquipTexture("frame/common/category/e", new Rect(categoryX + categoryWidth - 8, iconY + 57, 8, 14));
            GUI.Label(new Rect(categoryX, iconY + 56, categoryWidth, 16), categoryText,
                new GUIStyle(gray) { alignment = TextAnchor.UpperCenter });
            float requirementY = iconY + 82;
            foreach (var requirement in requirements)
            {
                float rh = Mathf.Max(16, body.CalcHeight(new GUIContent(requirement[1]), width - 100 - margin));
                GUI.Label(new Rect(margin, requirementY, 85, rh), requirement[0], gray);
                GUI.Label(new Rect(100, requirementY, width - 100 - margin, rh), requirement[1], body);
                requirementY += rh;
            }
            EquipTexture("frame/common/line", new Rect(0, contentY - 6, width, 3));
            float y = contentY;
            string potentialKind = null, potentialGrade = null;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float columnX = rowColumns[i] * 328;
                y = rowPositions[i];
                bool header = PotentialHeader(row[0]);
                if (header)
                {
                    EquipTexture("frame/common/line", new Rect(columnX, y + 2, width, 3));
                    y += 9;
                    potentialKind = row[0].StartsWith("附加") ? "additionalPotential" : "potential";
                    int grade = 0;
                    if (hoverItem?.equipmentData != null && hoverItem.equipmentData.TryGetValue(
                        potentialKind == "potential" ? "potentialGrade" : "bonusGrade", out var g)) grade = System.Convert.ToInt32(g);
                    potentialGrade = new[] { "normal", "rare", "epic", "unique", "legendary" }[Mathf.Clamp(grade, 0, 4)];
                }
                else if (row[1].Length != 0) potentialKind = null;
                var style = new GUIStyle(body);
                float x = margin;
                if (potentialKind != null)
                {
                    // Only the section heading has the grade color; option text stays white.
                    if (header) style.normal.textColor = PotentialColor(potentialGrade);
                    EquipNode("textIcon/" + potentialKind + "/" + (header ? "title/" : "detail/") + potentialGrade, columnX + margin, y);
                    x += header ? 15 : 12;
                }
                else if (row[0].StartsWith("星火")) style.normal.textColor = new Color32(10,227,173,255);
                else if (row[0] == LUpgrades)
                {
                    // 282 把这一行前置一个 11x9 的卷轴图标（textIcon/scroll），文字取
                    // font/scroll/normal = #AFADFF；剩 0 次时图标切到灰色 normal 态。
                    style.normal.textColor = new Color32(175,173,255,255);
                    EquipNode("textIcon/scroll/" + (upgradeSlots > 0 ? "enhanced" : "normal"),
                        columnX + margin, y + 3);
                    x += 15;   // 图标 11 + space/textIconSpaceX 4
                }
                GUI.Label(new Rect(columnX + x, y, width - margin - x, rowHeights[i]), row[0] + row[1], style);
                y += rowHeights[i] - (header ? 9 : 0);
            }
            if (descriptionHeight > 0)
                GUI.Label(new Rect((columnHeights.Count - 1) * 328 + margin,
                    columnHeights[columnHeights.Count - 1] - descriptionHeight - 6, bodyWidth, descriptionHeight), description, body);
        }
        finally
        {
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            GUI.contentColor = oldContentColor;
            GUI.enabled = oldEnabled;
        }
        DrawSetColumns(new Vector2(groupX + (equipmentWidth + 4) * s, groupY),
            setRows, setTitle, setRowsPerColumn, s);
    }

    static void FlowEquipRows(List<string[]> rows, List<float> heights, float firstY,
        float descriptionHeight, float limit, List<int> columns, List<float> positions, List<float> pageHeights)
    {
        columns.Clear(); positions.Clear(); pageHeights.Clear();
        int column = 0;
        float y = firstY;
        for (int i = 0; i < rows.Count; i++)
        {
            float keep = heights[i];
            // Keep potential headings with their options and soul headings with their details.
            if (PotentialHeader(rows[i][0]) || rows[i][0].StartsWith("灵魂武器"))
                for (int next = i + 1; next < rows.Count && rows[next][1].Length == 0; next++) keep += heights[next];
            if (y + keep + 12 > limit && y > 15)
            {
                pageHeights.Add(y + 12); column++; y = 15;
            }
            columns.Add(column); positions.Add(y); y += heights[i];
        }
        if (descriptionHeight > 0 && y + descriptionHeight + 12 > limit)
        {
            pageHeights.Add(y + 12); y = 15;
        }
        pageHeights.Add(y + descriptionHeight + 12);
    }

    const string EquipAtlas = "UI/UIToolTip.img/Item/Equip/";

    static ImgNode equipmentSkin;
    static ImgNode EquipSkinNode(string path)
    {
        if (equipmentSkin == null)
        {
            var bundled = Resources.Load<TextAsset>("UI/EquipmentTooltip");
            if (bundled != null)
            {
                using (var image = new ImgFile(bundled.bytes, "UI/UIToolTip.img"))
                    equipmentSkin = image.Root.Get("Item/Equip");
            }
            else equipmentSkin = WzAssets.Node(EquipAtlas.TrimEnd('/'));
        }
        return equipmentSkin?.Get(path)?.TryResolve();
    }

    static void DrawEquipStars(float width, float height, int capacity, int stars, bool superior)
    {
        int rows = StarForceStars.Rows(capacity);
        float top = 4 + (height - StarForceStars.BlockHeight(rows)) * .5f;
        for (int row = 0; row < rows; row++)
        {
            int count = Mathf.Min(15, capacity - row * 15);
            float left = (width - StarForceStars.RowWidth(count)) * .5f;
            for (int i = 0; i < count; i++)
                EquipTexture("textIcon/starForce/" + (row * 15 + i < stars ? (superior ? "blue" : "star") : "empty"),
                    new Rect(left + i * 11 + i / 5 * 10, top + row * 15, 11, 10));
        }
    }

    static Color32 PotentialColor(string grade)
    {
        switch (grade)
        {
            case "rare": return new Color32(102,255,255,255);
            case "epic": return new Color32(187,119,255,255);
            case "unique": return new Color32(255,204,0,255);
            case "legendary": return new Color32(204,255,0,255);
            default: return new Color32(133,145,159,255);
        }
    }

    static bool PotentialHeader(string label) => label == "主潜能 :" || label == "附加潜能 :";

    static void EquipTexture(string path, Rect rect)
    {
        var node = EquipSkinNode(path);
        // Some original icon states are vectors (no artwork), not Canvas nodes.
        if (node == null || node.Kind != "Canvas") return;
        var texture = WzAssets.Texture(node);
        if (texture != null) GUI.DrawTexture(rect, texture);
    }

    static void EquipNode(string path, float x, float y)
    {
        var node = EquipSkinNode(path);
        // Some original icon states are vectors (no artwork), not Canvas nodes.
        if (node == null || node.Kind != "Canvas") return;
        var texture = WzAssets.Texture(node);
        if (texture == null) return;
        var origin = EquipSkinNode(path)?.Get("origin");
        GUI.DrawTexture(new Rect(x - (origin?.X ?? 0), y - (origin?.Y ?? 0), texture.width, texture.height), texture);
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

    sealed class SetRow
    {
        public string text, slot;
        public Color32 color;
        public float inset;
    }

    static List<SetRow> SetRowsFor(int itemId, out string title)
    {
        title = null;
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
        var result = new List<SetRow>();
        if (setCache.Count == 0) return result;
        var panel = setCache[0];
        title = panel.Name + " (" + panel.EquippedCount + "/" + panel.CompleteCount + ")";
        var gold = new Color32(255,225,55,255);
        var gray = new Color32(144,148,157,255);
        foreach (var slot in panel.Slots)
        {
            var alt = slot.Alts.Count > 0 ? slot.Alts[0] : null;
            foreach (var candidate in slot.Alts) if (candidate.Equipped) { alt = candidate; break; }
            foreach (var candidate in slot.Alts) if (candidate.ItemId == itemId) { alt = candidate; break; }
            if (alt == null) continue;
            result.Add(new SetRow { text = alt.Name, slot = "(" + alt.SlotName + ")",
                color = alt.ItemId == itemId ? gold : alt.Equipped ? (Color32)Color.white : gray });
        }
        if (panel.Tiers.Count > 0) result.Add(new SetRow { text = "套装效果", color = gold });
        foreach (var tier in panel.Tiers)
        {
            bool active = panel.EquippedCount >= tier.Required;
            result.Add(new SetRow { text = "[" + tier.Required + "件]", color = active ? (Color32)Color.white : gray });
            foreach (var stat in tier.Stats)
                result.Add(new SetRow { text = SetStatName(stat.Key) + " +" + stat.Value + SetStatSuffix(stat.Key),
                    inset = 8, color = active ? new Color32(210,210,210,255) : gray });
        }
        return result;
    }

    /// <summary>
    /// 「战斗力提升量」数值的字号：槽位只有 209px。兆/京级的数会拉长到二十多字，
    /// 20px 会顶出边框，所以按字符数退档（282 那边是图数字形，宽度固定，我们只能缩字号）。
    /// </summary>
    public static int PowerFontSize(string text)
    {
        int length = string.IsNullOrEmpty(text) ? 0 : text.Length;
        if (length <= 10) return 20;
        if (length <= 16) return 16;
        if (length <= 22) return 14;
        return 12;
    }

    static void DrawSetColumns(Vector2 origin, List<SetRow> rows, string title, int perColumn, float s)
    {
        if (rows.Count == 0) return;
        int columns = (rows.Count + perColumn - 1) / perColumn;
        float tallest = (38 + Mathf.Min(rows.Count, perColumn) * 16) * s;
        LastSetRect = new Rect(origin.x, origin.y, (columns * 240 - 4) * s, tallest);
        var style = new GUIStyle { font = UiKit.Font, fontSize = Mathf.Max(1, Mathf.RoundToInt(12 * s)) };
        for (int column = 0; column < columns; column++)
        {
            int start = column * perColumn, count = Mathf.Min(perColumn, rows.Count - start);
            var rect = new Rect(origin.x + column * 240 * s, origin.y, 236 * s, (38 + count * 16) * s);
            DrawFrame(rect, s);
            style.normal.textColor = new Color32(255,225,55,255);
            GUI.Label(new Rect(rect.x + 8*s, rect.y + 5*s, 220*s, 16*s), title,
                new GUIStyle(style) { alignment = TextAnchor.MiddleCenter });
            for (int i = 0; i < count; i++)
            {
                var row = rows[start + i];
                style.normal.textColor = row.color;
                float y = rect.y + (25 + i * 16) * s;
                GUI.Label(new Rect(rect.x + (8 + row.inset)*s, y,
                    (row.slot == null ? 220 - row.inset : 150)*s, 16*s), row.text, style);
                if (row.slot != null)
                    GUI.Label(new Rect(rect.x + 158*s, y, 70*s, 16*s), row.slot,
                        new GUIStyle(style) { alignment = TextAnchor.MiddleRight });
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
