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
        else if (p >= 180 && p <= 183) sub = "PetEquip";
        else if (p == 190 || p == 191 || p == 193) sub = "Taming";
        else if (p >= 194 && p <= 197) sub = "Dragon";
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
        Draw(hoverId);
        hasHover = false;
    }

    const string Atlas = "UI/UIWindow.img/ToolTip/Equip/";
    static void Draw(int id)
    {
        if (ItemCatalog.IsEquip(id)) DrawEquipment(id);
        else DrawCommon(id);
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

    // 8E8252 -> 8ED0D2 (requirements) / 8F3BC3 (property rows).
    static void DrawEquipment(int id)
    {
        var info = EquipCatalog.Info(id);
        if (info == null) return;
        float s = BottomBar.S;
        var rows = new List<string[]>();
        int prefix = id / 10000;
        string[] weaponCategories = { " ONE-HANDED SWORD", " ONE-HANDED AXE", " ONE_HANDED MACE", " DAGGER", "", "", "", " WAND", " STAFF", "", " TWO-HANDED SWORD", " TWO-HANDED AXE", " TWO-HANDED MACE", " SPEAR", " POLE ARM", " BOW", " CROSSBOW", " CLAW", "KNUCKLE", "GUN" };
        string category = prefix >= 130 && prefix <= 149 ? weaponCategories[prefix - 130] : null;
        if (category == null)
        {
            switch (prefix)
            {
                case 100: category = "HAT"; break;
                case 101: category = "FACE ACCESSORY"; break;
                case 102: category = "EYE ACCESSORY"; break;
                case 103: category = "EARRING"; break;
                case 104: category = "TOP"; break;
                case 105: category = "OVERALL"; break;
                case 106: category = "BOTTOM"; break;
                case 107: category = "SHOES"; break;
                case 108: category = "GLOVE"; break;
                case 109: category = "SHIELD"; break;
                case 110: category = "CAPE"; break;
                case 111: category = "RING"; break;
                case 112: category = "PENDANT"; break;
                case 113: category = "BELT"; break;
                case 114: category = "MEDAL"; break;
            }
        }
        if (!string.IsNullOrEmpty(category)) rows.Add(new[] { "CATEGORY :", category });
        if (prefix >= 130 && prefix <= 149 && info.Get("attackSpeed") != null)
        {
            string[] speeds = { " FASTEST", " FASTEST", " FASTER", " FASTER", " FAST", " FAST", " NORMAL", " SLOW", " SLOW", " SLOWER", " SLOWER", " SLOWEST", " SLOWEST" };
            int speed = info.Int("attackSpeed");
            if (speed >= 0 && speed < speeds.Length) rows.Add(new[] { "ATTACK SPEED :", speeds[speed] });
        }
        string[] keys = { "incSTR", "incDEX", "incINT", "incLUK", "incMHP", "incMMP", "incPAD", "incMAD", "incPDD", "incMDD", "incACC", "incEVA", "incCraft", "incSpeed", "incJump" };
        string[] labels = { "STR :", "DEX :", "INT :", "LUK :", "HP :", "MP :", "WEAPON ATTACK :", "MAGIC ATTACK :", "WEAPON DEF. :", "MAGIC DEF. :", "ACCURACY :", "AVOIDABILITY :", "HANDS :", "SPEED :", "JUMP :" };
        int[] actual = hoverItem?.isEquip == true ? new[] { hoverItem.str, hoverItem.dex, hoverItem.int_, hoverItem.luk,
            hoverItem.hp, hoverItem.mp, hoverItem.watk, hoverItem.matk, hoverItem.wdef, hoverItem.mdef,
            hoverItem.acc, hoverItem.avoid, hoverItem.hands, hoverItem.speed, hoverItem.jump } : null;
        for (int i = 0; i < keys.Length; i++)
        {
            int value = actual == null ? info.Int(keys[i]) : actual[i];
            if (value > 0) rows.Add(new[] { labels[i], " +" + value });
        }
        int upgrades = actual == null ? info.Int("tuc") : hoverItem.upgradeslots;
        if (upgrades > 0) rows.Add(new[] { "NUMBER OF UPGRADES AVAILABLE :", " " + upgrades });
        float height = Mathf.Max(166, 166 + rows.Count * 13) * s;
        var p = Pointer.Pos;
        var rect = new Rect(Mathf.Clamp(p.x, 0, Mathf.Max(0, UiKit.W - 236 * s)),
            Mathf.Clamp(p.y + 20 * s, 0, Mathf.Max(0, UiKit.H - height)), 236 * s, height);
        DrawFrame(rect, s);
        var title = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(12 * s) };
        title.normal.textColor = Color.white;
        GUI.Label(new Rect(rect.x + 18 * s, rect.y + 10 * s, 208 * s, 20 * s), ItemCatalog.Name(id), title);
        Canvas(rect, "Dot/0", 10, 15, s);
        DrawIcon(rect, id, 0, s);
        var a = Profile.Attributes;
        string[] req = { "reqLevel", "reqSTR", "reqDEX", "reqINT", "reqLUK", "reqPOP" };
        string[] sprites = { "reqLEV", "reqSTR", "reqDEX", "reqINT", "reqLUK", "reqPOP" };
        int[] have = { a.level, a.str, a.dex, a.intelligence, a.luk, GameSession.Character?.record?.fame ?? 0 };
        for (int i = 0; i < req.Length; i++)
        {
            int need = info.Int(req[i]);
            string root = (have[i] >= need ? "Can/" : "Cannot/");
            Canvas(rect, root + sprites[i], 94, 32 + i * 12, s);
            float x = 144;
            if (need == 0 && i == 5) Canvas(rect, root + "none", x, 32 + i * 12, s);
            else foreach (char digit in need.ToString())
            {
                Canvas(rect, root + digit, x, 32 + i * 12, s);
                x += 6;
            }
        }
        string[] jobs = { "beginner", "warrior", "magician", "bowman", "thief", "pirate" };
        int[] jobX = { 10, 52, 92, 132, 171, 197 };
        if (info.Get("level") == null)
        {
            Canvas(rect, "GrowthDisabled/itemLEV", 94, 104, s);
            Canvas(rect, "GrowthDisabled/itemEXP", 94, 116, s);
            Canvas(rect, "GrowthDisabled/none", 148, 104, s);
            Canvas(rect, "GrowthDisabled/none", 148, 116, s);
        }
        int mask = info.Int("reqJob");
        for (int i = 0; i < jobs.Length; i++)
        {
            bool allowed = i == 0 ? mask <= 0 : mask == 0 || mask != -1 && (mask & (1 << (i - 1))) != 0;
            Canvas(rect, (allowed ? "Can/" : "Cannot/") + jobs[i], jobX[i], 133, s);
        }
        Fill(new Rect(rect.x + 6 * s, rect.y + 152 * s, rect.width - 12 * s, s), Color.white);
        var label = new GUIStyle { font = UiKit.Font, fontSize = Mathf.RoundToInt(9 * s) };
        label.normal.textColor = new Color32(250,232,203,255);
        var valueStyle = new GUIStyle(label); valueStyle.normal.textColor = Color.white;
        for (int i = 0; i < rows.Count; i++)
        {
            float y = rect.y + (159 + i * 13) * s;
            float labelWidth = label.CalcSize(new GUIContent(rows[i][0])).x;
            GUI.Label(new Rect(rect.x + 10 * s, y, 216 * s, 13 * s), rows[i][0], label);
            GUI.Label(new Rect(rect.x + 10 * s + labelWidth, y, 216 * s - labelWidth, 13 * s), rows[i][1], valueStyle);
        }
    }

    // BeiDou 8EDFC7 -> 8F3141 / 8F421E / 8F4535 / 8F4818.
    static void DrawCommon(int id)
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
        float height = header + Mathf.Max(116 * s, 52 * s + textHeight);
        Vector2 p = Pointer.Pos;
        var rect = new Rect(Mathf.Clamp(p.x, 0, Mathf.Max(0, UiKit.W - 290 * s)),
            Mathf.Clamp(p.y + 20 * s, 0, Mathf.Max(0, UiKit.H - height)), 290 * s, height);
        DrawFrame(rect, s);
        // 8F4818: name at (18,10), with the source's leading marker at (10,15).
        GUI.Label(new Rect(rect.x + 18 * s, rect.y + 10 * s, 262 * s, 20 * s), ItemCatalog.Name(id), style);
        var dot = WzAssets.Texture("UI/UIWindow.img/ToolTip/Equip/Dot/0");
        if (dot != null) GUI.DrawTexture(new Rect(rect.x + 10 * s, rect.y + 15 * s, dot.width * s, dot.height * s), dot);
        if (questItem)
        {
            var flagStyle = new GUIStyle(style) { alignment = TextAnchor.UpperCenter };
            flagStyle.normal.textColor = new Color32(255, 138, 24, 255); // font 11, 8E52FB
            GUI.Label(new Rect(rect.x, rect.y + 29 * s, rect.width, 19 * s), "Quest Item", flagStyle);
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

    static Texture2D frame;
    static int frameWidth, frameHeight;
    static void DrawFrame(Rect rect, float s)
    {
        int w = Mathf.RoundToInt(rect.width / s), h = Mathf.RoundToInt(rect.height / s);
        if (frame == null || w != frameWidth || h != frameHeight)
        {
            if (frame != null) Object.Destroy(frame);
            frameWidth = w; frameHeight = h;
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
        }
        GUI.DrawTexture(rect, frame);
    }

}
