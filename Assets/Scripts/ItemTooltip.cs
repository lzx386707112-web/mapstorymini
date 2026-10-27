using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 背包 / 装备窗 物品悬浮详情（tooltip）。
//
// 源端依据（BeiDou / GMS083 WZ）：
//   UI/UIWindow.img/ToolTip/Equip  是需求/属性小图标图集：
//     Can / Cannot / Dot / WeaponCategory / ItemCategory / Speed / Property /
//     mesos / cash / GrowthEnabled / GrowthDisabled。这些是用客户端位图字体预先
//     渲染好的小标签（reqLEV、职业名、Cannot 等）。
//   除图标外，原端用客户端字体绘制物品名称与各数值，并用代码绘制半透明深色
//   背景框——本 WZ 的 ToolTip 节点下没有任何 backgrnd/cover/line 帧图，属于
//   「原端确实用代码绘制」的例外，按项目规则照搬其绘制逻辑（深色框 + 1px 描边）。
//   物品名称/描述取自 String/{Eqp,Consume,Ins,Etc,Cash}.img（按 id 首位分段）；
//   装备属性/需求取自 Character/<类别>/<8位id>.img/info，与 EquipCatalog 同源。
//
// 渲染约定：本项目所有窗口的中文文本都走 UiKit.Font（StatWindow / SkillWindow /
// NpcDialog 一致），名称与数值同理用运行时字体绘制；小标签的源端位图图集仅作
// 还原参考，不强行逐字贴图。
// ————————————————————————————————————————————————————————————

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
        nameCache[id] = name;
        return name;
    }

    public static string Desc(int id)
    {
        if (descCache.TryGetValue(id, out var c)) return c ?? "";
        string desc = "";
        if (!IsEquip(id))
        {
            var node = ItemNode(id);
            if (node != null) desc = node.Text("desc");
        }
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
        else sub = "Weapon"; // 121-160 / 169 / 170
        subcatCache[id] = sub;
        return sub;
    }

    // 稀有度颜色：现金道具金色，其余白色（源端 v83 装备默认白、现金黄）
    public static Color RarityColor(int id) => IsCash(id) ? new Color(1f, 0.85f, 0.20f) : Color.white;
}

public static class ItemTooltip
{
    static int hoverId;
    static bool hasHover;

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

    public static void SetHover(int id)
    {
        if (id <= 0) return;
        hoverId = id;
        hasHover = true;
    }

    // 在 DrawAll 末尾调用，压在所有窗口之上绘制
    public static void Flush()
    {
        if (!hasHover || hoverId <= 0) return;
        if (Event.current.type != EventType.Repaint) return;
        Draw(hoverId);
        hasHover = false;
    }

    class Line { public string text; public Color color; public int size; public bool wrap; public float y, h; }

    static readonly Color light = new Color(0.86f, 0.90f, 1.00f);
    static readonly Color statColor = new Color(0.70f, 1.00f, 0.80f);
    static readonly Color reqFail = new Color(1.00f, 0.50f, 0.50f);
    static readonly Color reqOk = new Color(0.80f, 0.85f, 0.95f);

    static void Draw(int id)
    {
        float s = BottomBar.S;
        var a = Profile.Attributes;
        var lines = new List<Line>();

        string name = ItemCatalog.Name(id);
        if (string.IsNullOrEmpty(name)) name = "#" + id;
        lines.Add(new Line { text = name, color = ItemCatalog.RarityColor(id), size = 13 });

        if (ItemCatalog.IsEquip(id))
        {
            int slot = EquipSlot.SlotOf(id);
            if (slot != 0) lines.Add(new Line { text = EquipSlot.NameOf(slot), color = reqOk, size = 10 });
            var info = EquipCatalog.Info(id);
            if (info != null)
            {
                AddStat(lines, info, "incSTR", "力量");
                AddStat(lines, info, "incDEX", "敏捷");
                AddStat(lines, info, "incINT", "智力");
                AddStat(lines, info, "incLUK", "运气");
                AddStat(lines, info, "incMHP", "最大HP");
                AddStat(lines, info, "incMMP", "最大MP");
                AddStat(lines, info, "incPAD", "攻击力");
                AddStat(lines, info, "incMAD", "魔法力");
                AddStat(lines, info, "incPDD", "物理防御");
                AddStat(lines, info, "incMDD", "魔法防御");
                AddStat(lines, info, "incACC", "命中率");
                AddStat(lines, info, "incEVA", "回避率");
                AddStat(lines, info, "incSpeed", "移动速度");
                AddStat(lines, info, "incJump", "跳跃力");

                bool anyReq = false;
                int reqLev = info.Int("reqLevel");
                int reqJob = info.Int("reqJob");
                int reqStr = info.Int("reqSTR"), reqDex = info.Int("reqDEX");
                int reqInt = info.Int("reqINT"), reqLuk = info.Int("reqLUK");
                if (reqLev > 0 || reqJob > 0 || reqStr > 0 || reqDex > 0 || reqInt > 0 || reqLuk > 0) anyReq = true;
                if (anyReq) lines.Add(new Line { text = "", color = light, size = 4 }); // 分隔

                if (reqLev > 0)
                    lines.Add(new Line { text = "需要等级 " + reqLev, color = a.level >= reqLev ? reqOk : reqFail, size = 11 });
                if (reqJob > 0)
                {
                    bool ok = (reqJob & JobDefs.JobBit(Profile.JobId)) != 0;
                    lines.Add(new Line { text = "需要职业 " + ReqJobNames(reqJob), color = ok ? reqOk : reqFail, size = 11 });
                }
                AddReq(lines, a.str, reqStr, "需要力量 ");
                AddReq(lines, a.dex, reqDex, "需要敏捷 ");
                AddReq(lines, a.intelligence, reqInt, "需要智力 ");
                AddReq(lines, a.luk, reqLuk, "需要运气 ");
            }
        }
        else
        {
            string desc = ItemCatalog.Desc(id);
            if (!string.IsNullOrEmpty(desc))
                lines.Add(new Line { text = desc, color = light, size = 11, wrap = true });
        }

        // 测量高度
        float pad = 8 * s;
        float innerW = 190 * s;
        float y = pad;
        var style = new GUIStyle { font = UiKit.Font, wordWrap = true, clipping = TextClipping.Overflow };
        foreach (var ln in lines)
        {
            style.fontSize = Mathf.RoundToInt((ln.size > 0 ? ln.size : 11) * s);
            style.normal.textColor = ln.color;
            float h = ln.wrap ? style.CalcHeight(new GUIContent(ln.text), innerW)
                              : (ln.size > 0 ? ln.size : 11) * s + 4 * s;
            if (h < 1) h = 12 * s;
            ln.h = h; ln.y = y; y += h;
        }
        float w = innerW + pad * 2;
        float hTotal = y + pad;

        // 跟随光标，靠近右/下边缘时翻转到左侧/上方
        Vector2 p = Pointer.Pos;
        float x = p.x + 18 * s;
        if (x + w > UiKit.W) x = p.x - w - 18 * s;
        x = Mathf.Clamp(x, 4 * s, Mathf.Max(4 * s, UiKit.W - w - 4 * s));
        float ty = Mathf.Clamp(p.y, 4 * s, Mathf.Max(4 * s, UiKit.H - hTotal - 4 * s));

        var rect = new Rect(x, ty, w, hTotal);
        // 背景框（原端代码绘制的半透明深色框 + 1px 描边）
        GUI.color = new Color(0.06f, 0.08f, 0.14f, 0.95f);
        GUI.DrawTexture(rect, White);
        GUI.color = new Color(0.55f, 0.65f, 0.85f, 0.90f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1 * s), White);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1 * s, rect.width, 1 * s), White);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 1 * s, rect.height), White);
        GUI.DrawTexture(new Rect(rect.xMax - 1 * s, rect.y, 1 * s, rect.height), White);
        GUI.color = Color.white;

        // 文本（覆盖绘制）
        foreach (var ln in lines)
        {
            if (string.IsNullOrEmpty(ln.text)) continue;
            style.fontSize = Mathf.RoundToInt((ln.size > 0 ? ln.size : 11) * s);
            style.normal.textColor = ln.color;
            style.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(rect.x + pad, rect.y + ln.y, innerW, ln.h), ln.text, style);
        }
    }

    static void AddStat(List<Line> lines, ImgNode info, string key, string label)
    {
        int v = info.Int(key);
        if (v != 0) lines.Add(new Line { text = label + " : +" + v, color = statColor, size = 11 });
    }

    static void AddReq(List<Line> lines, int have, int need, string label)
    {
        if (need > 0) lines.Add(new Line { text = label + need, color = have >= need ? reqOk : reqFail, size = 11 });
    }

    static string ReqJobNames(int mask)
    {
        var names = new List<string>();
        if ((mask & 1) != 0) names.Add("战士");
        if ((mask & 2) != 0) names.Add("法师");
        if ((mask & 4) != 0) names.Add("弓箭手");
        if ((mask & 8) != 0) names.Add("飞侠");
        if ((mask & 16) != 0) names.Add("海盗");
        return names.Count > 0 ? string.Join("/", names) : "无";
    }
}
