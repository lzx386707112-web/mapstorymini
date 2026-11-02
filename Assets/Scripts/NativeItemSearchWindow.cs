using System;
using System.Collections.Generic;
using UnityEngine;

// 密涅瓦的猫头鹰窗口（CItemSearchDlg，itemType==523 双击打开）。
// 底图 UI/UIWindow.img/itemSearch/，四个页面按素材尺寸排：
//   backgrnd     248x271 入口页：气泡输入框 (26,96,140,18) + BtSearch(38x17)，
//                底部两条原图文字热区「人气查询前十名」(y=208)、「分类查询留言」(y=238)
//   resultback   438x430 结果页：顶部输入框 (14,78,300,22)，列标题带 y=140..150（原图文字），
//                8 行 y=158+30*i（列 x：角色 14 / 商店 88 / 组合 170 / 价格 212 / 数量 268 / 道具资料 300），
//                底部 BtBack 返回 (12,400)、BtRetry 重新搜索 (78,400)、BtLeft/BtRight 翻页、BtGo 前往 (370,400)
//   categoryback 270x271 分类 / 人气列表页（列区 y=38..243）
//   searchback   205x271 分类内的道具列表页（列区 y=38..243，底部「高价排序」是原图文字）
// 服务端语义见 NativeOwlService（搜到才扣 523xxxx；结果按单价升序最多 200 条）。
public static class NativeItemSearchWindow
{
    enum Mode { Entry, Results, Hot, Category, Items }
    static readonly string[] Categories = { "装备", "消耗", "装饰", "其他", "现金" };

    static Mode mode = Mode.Entry;
    static bool opened;
    static List<int> suggestions = new List<int>();
    static List<OwlHit> hits = new List<OwlHit>();
    static readonly List<int> filtered = new List<int>();
    static int category = -1, selection = -1, page;
    static string query = "";
    static int owlItemId, owlPosition;
    static Vector2 position = new Vector2(80, 40), grab;
    static bool dragging;
    static double lastClick, pendingVisitAt;
    static int pendingOwner, pendingMap;

    static float W => mode == Mode.Results ? 438 : mode == Mode.Entry ? 248 : mode == Mode.Items ? 205 : 270;
    static float H => mode == Mode.Results ? 430 : 271;

    public static bool Visible => opened;

    public static void Reset()
    {
        mode = Mode.Entry; opened = false; suggestions.Clear(); hits.Clear(); filtered.Clear();
        category = selection = -1; page = 0; query = ""; owlItemId = owlPosition = 0;
        dragging = false; pendingOwner = pendingMap = 0; pendingVisitAt = 0;
    }

    // 双击 523xxxx：先向服务端要推荐位（UseOwlOfMinervaHandler），搜到结果才扣券。
    public static void Open(int itemId, int inventoryPosition)
    {
        if (!GameSession.InGame) return;
        owlItemId = itemId; owlPosition = inventoryPosition;
        var res = Request(OwlOperation.Open);
        if (res?.suggestions != null) suggestions = res.suggestions;
        mode = Mode.Entry; opened = true; selection = -1; page = 0;
    }

    public static void Close() => Reset();

    static OwlResponse Request(OwlOperation operation, int itemId = 0, int ownerId = 0, int mapId = 0)
    {
        if (!GameSession.InGame) return null;
        return GameSession.Net.Owl(new OwlRequest {
            characterId = GameSession.CharacterId, operation = operation,
            itemId = itemId, ownerId = ownerId, mapId = mapId });
    }

    // WarpTo 是异步的；等角色真的落到目标图再进店（服务端 Visit 校验 actor.mapId）。
    public static void Tick()
    {
        if (pendingOwner == 0) return;
        if (pendingVisitAt > 0 && Time.realtimeSinceStartup < pendingVisitAt) return;
        if (Profile.LastMapId != pendingMap) { if (Time.realtimeSinceStartup > pendingVisitAt + 5f) pendingOwner = 0; return; }
        int owner = pendingOwner; pendingOwner = 0;
        NativeMarketWindow.Visit(owner);
    }

    // ————————————————— 动作 —————————————————

    static void SearchByText()
    {
        var text = query.Trim();
        if (text.Length == 0) { BottomBar.Toast("请输入想搜索的道具名称"); return; }
        int itemId;
        if (int.TryParse(text, out itemId) && CatalogName(itemId) != null) { Search(itemId); return; }
        var ids = MatchNames(text);
        if (ids.Count == 1) { Search(ids[0]); return; }
        if (ids.Count == 0) { BottomBar.Toast("没有找到名字里带「" + text + "」的道具"); return; }
        // 多个候选：进道具列表页让玩家挑（挑完直接搜）
        filtered.Clear(); filtered.AddRange(ids);
        category = -1; mode = Mode.Items; selection = -1; page = 0;
    }

    static void Search(int itemId)
    {
        var res = Request(OwlOperation.Search, itemId);
        if (res == null) return;
        if (!res.ok) { BottomBar.Toast(res.error); return; }
        if (res.charged && !Profile.RemoveItem(owlItemId, 1, owlPosition))
            BottomBar.Toast("猫头鹰搜索器已用完");
        hits = res.hits ?? new List<OwlHit>();
        mode = Mode.Results; selection = -1; page = 0;
    }

    // 按名字模糊匹配（客户端本地，等价原版「分类查询」的道具名过滤）
    static List<int> MatchNames(string text)
    {
        var result = new List<int>();
        var catalog = Server.Bot.BotItemCatalog.Shared;
        foreach (int id in catalog.ItemIds)
        {
            string name = catalog.Name(id);
            if (!string.IsNullOrEmpty(name) && name.Contains(text)) result.Add(id);
            if (result.Count >= 200) break;
        }
        result.Sort(ByName);
        return result;
    }

    static string CatalogName(int id) => Server.Bot.BotItemCatalog.Shared.Name(id);

    static int ByName(int a, int b)
    {
        string na = CatalogName(a) ?? "", nb = CatalogName(b) ?? "";
        return string.CompareOrdinal(na, nb);
    }

    static void Warp(OwlHit hit)
    {
        var res = Request(OwlOperation.Warp, itemId: CurrentItemId(), ownerId: hit.ownerId, mapId: hit.mapId);
        if (res == null) return;
        if (!res.ok || res.map < 0) { NativeSystemMessage.Drop(5, res.error); return; }
        pendingOwner = hit.ownerId; pendingMap = res.map;
        pendingVisitAt = Time.realtimeSinceStartup + 0.5f;
        Reset();
        if (GameLoop.I?.field != null && Profile.LastMapId != res.map) GameLoop.I.field.WarpTo(res.map);
    }

    static int CurrentItemId()
    {
        int index = page * Rows + selection;
        return index >= 0 && index < hits.Count ? hits[index].item.itemId : 0;
    }

    // ————————————————— 绘制 —————————————————

    const int Rows = 8;

    static GUIStyle Text(bool right = false)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = 12, padding = new RectOffset(),
            clipping = TextClipping.Clip, alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft };
        style.normal.textColor = Color.black; return style;
    }

    static GUIStyle Center() => new GUIStyle(Text()) { alignment = TextAnchor.UpperCenter };

    static void Image(string name, float x, float y)
    {
        var t = Art.Tex("wz://UI/UIWindow.img/itemSearch/" + name);
        if (t != null) GUI.DrawTexture(new Rect(x, y, t.width, t.height), t);
    }

    static void Hotspot(string caption, Rect rect, Vector2 p, Action click)
    {
        bool hover = rect.Contains(p);
        if (hover) { GUI.color = new Color(1f, 1f, 1f, 0.25f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
        if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && hover)
        { Pointer.ConsumeClick(); click(); }
    }

    public static void Draw()
    {
        if (!Visible) return;
        if (!GameSession.InGame) { Reset(); return; }
        float scale = BottomBar.S;
        var size = new Vector2(W, H);
        position = NativeUi.ClampWindowPosition(position, size, scale);
        NativeUi.RegisterWindow("NativeItemSearchWindow", new Rect(position.x * scale, position.y * scale, W * scale, H * scale), () => Visible);
        var pointer = Pointer.Pos / scale; var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed &&
                new Rect(position.x, position.y, W, 22).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, size, scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, W, H));
            try
            {
                var p = pointer - position;
                switch (mode)
                {
                    case Mode.Entry: DrawEntry(p); break;
                    case Mode.Results: DrawResults(p); break;
                    case Mode.Hot: DrawHot(p); break;
                    case Mode.Category: DrawCategory(p); break;
                    case Mode.Items: DrawItems(p); break;
                }
                DrawClose(p);
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }

    static void DrawClose(Vector2 p)
    {
        // 素材没有关闭钮：标题栏右侧画一个 × 热区
        var rect = new Rect(W - 20, 4, 16, 16);
        bool hover = rect.Contains(p);
        if (hover) { GUI.color = new Color(1f, 0.85f, 0.85f, 0.5f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
        GUI.Label(rect, "×", new GUIStyle(Text()) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
        if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && hover)
        { Pointer.ConsumeClick(); Close(); }
    }

    static void DrawEntry(Vector2 p)
    {
        Image("backgrnd", 0, 0);
        GUI.SetNextControlName("owlquery");
        query = GUI.TextField(new Rect(26, 96, 140, 18), query, 30);
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
            GUI.GetNameOfFocusedControl() == "owlquery")
        { Event.current.Use(); SearchByText(); }
        if (NativeUi.Button(new Rect(170, 96, 38, 17), "UI/UIWindow.img/itemSearch/BtSearch", p)) SearchByText();
        Hotspot("人气查询前十名", new Rect(4, 208, 240, 29), p, () => { mode = Mode.Hot; selection = -1; page = 0; });
        Hotspot("分类查询留言", new Rect(4, 238, 240, 29), p, () => { mode = Mode.Category; selection = -1; page = 0; });
    }

    static void DrawResults(Vector2 p)
    {
        Image("resultback", 0, 0);
        query = GUI.TextField(new Rect(14, 80, 300, 22), query, 30);
        if (NativeUi.Button(new Rect(322, 81, 38, 17), "UI/UIWindow.img/itemSearch/BtSearch", p)) SearchByText();
        if (NativeUi.Button(new Rect(12, 399, 59, 17), "UI/UIWindow.img/itemSearch/BtBack", p)) { mode = Mode.Entry; return; }
        if (NativeUi.Button(new Rect(78, 399, 59, 17), "UI/UIWindow.img/itemSearch/BtRetry", p)) SearchByText();
        int pages = Mathf.Max(1, (hits.Count + Rows - 1) / Rows);
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && page > 0;
        if (NativeUi.Button(new Rect(300, 401, 13, 13), "UI/UIWindow.img/itemSearch/BtLeft", p)) page--;
        GUI.enabled = enabled && page < pages - 1;
        if (NativeUi.Button(new Rect(318, 401, 13, 13), "UI/UIWindow.img/itemSearch/BtRight", p)) page++;
        GUI.enabled = enabled;
        GUI.Label(new Rect(334, 402, 30, 13), (page + 1) + "/" + pages, Center());
        if (NativeUi.Button(new Rect(370, 399, 43, 17), "UI/UIWindow.img/itemSearch/BtGo", p) && selection >= 0 && selection < Rows)
        { int index = page * Rows + selection; if (index < hits.Count) Warp(hits[index]); }

        for (int row = 0; row < Rows; row++)
        {
            float y = 158 + 30 * row;
            int index = page * Rows + row;
            var rect = new Rect(8, y - 4, 422, 28);
            bool hover = rect.Contains(p);
            if (index == selection) { GUI.color = new Color(0.8f, 0.9f, 1f, 0.45f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
            else if (hover) { GUI.color = new Color(1f, 1f, 1f, 0.25f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
            if (index >= hits.Count) continue;
            var hit = hits[index];
            GUI.Label(new Rect(14, y, 72, 14), hit.ownerName, Text());
            GUI.Label(new Rect(88, y, 80, 14), hit.shopName, Text());
            GUI.Label(new Rect(170, y, 38, 14), hit.bundles.ToString(), Text(true));
            GUI.Label(new Rect(212, y, 52, 14), hit.price.ToString("N0"), Text(true));
            GUI.Label(new Rect(268, y, 26, 14), hit.quantity.ToString(), Text(true));
            GUI.Label(new Rect(300, y, 130, 14), Info(hit), Text());
            if (GUI.enabled && Event.current.type == EventType.Repaint && hover)
            {
                if (Pointer.Released)
                {
                    Pointer.ConsumeClick();
                    if (Time.realtimeSinceStartupAsDouble - lastClick < 0.45 && selection == row) { Warp(hit); return; }
                    lastClick = Time.realtimeSinceStartupAsDouble; selection = row;
                }
                else if (Pointer.Pressed) Pointer.ConsumeClick();
            }
        }
    }

    static string Info(OwlHit hit)
    {
        if (!hit.equip || hit.item == null) return "";
        var item = hit.item;
        var parts = new List<string>();
        void Add(string label, int value) { if (value != 0) parts.Add(label + value); }
        Add("攻", item.watk); Add("魔", item.matk); Add("防", item.wdef); Add("魔防", item.mdef);
        Add("力", item.str); Add("敏", item.dex); Add("智", item.int_); Add("运", item.luk);
        Add("HP", item.hp); Add("MP", item.mp); Add("移", item.speed); Add("跳", item.jump);
        return parts.Count == 0 ? "装备" : string.Join(" ", parts.ToArray());
    }

    // 人气查询前十名：backgrnd 的热区跳到这里，用 categoryback 列物品名。
    static void DrawHot(Vector2 p)
    {
        Image("categoryback", 0, 0);
        GUI.Label(new Rect(22, 40, 200, 14), "人气查询前十名", Text());
        ListPage(p, suggestions.Count, index =>
        {
            int id = suggestions[index];
            string name = CatalogName(id);
            GUI.Label(new Rect(22, 58 + 22 * (index - page * Rows), 230, 16), name == null ? id.ToString() : name, Text());
            GUI.Label(new Rect(150, 58 + 22 * (index - page * Rows), 100, 16), id.ToString(), Text(true));
        }, index =>
        {
            int id = suggestions[index];
            if (id > 0) Search(id);
        });
        if (NativeUi.Button(new Rect(12, 240, 59, 17), "UI/UIWindow.img/itemSearch/BtBack", p)) mode = Mode.Entry;
    }

    static void DrawCategory(Vector2 p)
    {
        Image("categoryback", 0, 0);
        GUI.Label(new Rect(22, 40, 200, 14), "分类查询（先在首页输入名字可过滤）", Text());
        ListPage(p, Categories.Length, index =>
        {
            GUI.Label(new Rect(30, 58 + 22 * (index - page * Rows), 220, 16), Categories[index], Text());
        }, index =>
        {
            int type = index + 1;
            filtered.Clear();
            foreach (int id in Server.Bot.BotItemCatalog.Shared.ItemIds)
            {
                if (id / 1000000 != type) continue;
                string name = CatalogName(id);
                if (query.Trim().Length > 0 && (name == null || !name.Contains(query.Trim()))) continue;
                filtered.Add(id);
                if (filtered.Count >= 200) break;
            }
            filtered.Sort(ByName);
            category = index; mode = Mode.Items; selection = -1; page = 0;
        });
        if (NativeUi.Button(new Rect(12, 240, 59, 17), "UI/UIWindow.img/itemSearch/BtBack", p)) mode = Mode.Entry;
    }

    // 分类 / 名字过滤出的道具列表：searchback，底部「高价排序」是原图文字。
    static void DrawItems(Vector2 p)
    {
        Image("searchback", 0, 0);
        string title = category >= 0 && category < Categories.Length ? Categories[category] : "搜索结果";
        GUI.Label(new Rect(18, 40, 170, 14), title + "（" + filtered.Count + "）", Text());
        ListPage(p, filtered.Count, index =>
        {
            int id = filtered[index];
            string name = CatalogName(id);
            GUI.Label(new Rect(18, 58 + 22 * (index - page * Rows), 120, 16), string.IsNullOrEmpty(name) ? id.ToString() : name, Text());
            GUI.Label(new Rect(140, 58 + 22 * (index - page * Rows), 55, 16), id.ToString(), Text(true));
        }, index =>
        {
            int id = filtered[index];
            if (id > 0) Search(id);
        });
        if (NativeUi.Button(new Rect(12, 240, 59, 17), "UI/UIWindow.img/itemSearch/BtBack", p))
        { mode = category >= 0 ? Mode.Category : Mode.Entry; page = 0; }
    }

    // categoryback / searchback 共用的 8 行列表 + ◀▶ 翻页（列区 y=58..238）。
    static void ListPage(Vector2 p, int count, Action<int> drawRow, Action<int> clickRow)
    {
        int pages = Mathf.Max(1, (count + Rows - 1) / Rows);
        page = Mathf.Clamp(page, 0, pages - 1);
        for (int row = 0; row < Rows; row++)
        {
            int index = page * Rows + row;
            if (index >= count) break;
            var rect = new Rect(4, 54 + 22 * row, W - 8, 21);
            bool hover = rect.Contains(p);
            if (index == selection) { GUI.color = new Color(0.8f, 0.9f, 1f, 0.45f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
            else if (hover) { GUI.color = new Color(1f, 1f, 1f, 0.25f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
            drawRow(index);
            if (GUI.enabled && Event.current.type == EventType.Repaint && hover && Pointer.Released)
            { Pointer.ConsumeClick(); selection = index; clickRow(index); }
        }
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && page > 0;
        if (NativeUi.Button(new Rect(160, 244, 13, 13), "UI/UIWindow.img/itemSearch/BtLeft", p)) page--;
        GUI.enabled = enabled && page < pages - 1;
        if (NativeUi.Button(new Rect(178, 244, 13, 13), "UI/UIWindow.img/itemSearch/BtRight", p)) page++;
        GUI.enabled = enabled;
        GUI.Label(new Rect(196, 245, 60, 13), (page + 1) + "/" + pages, Text());
    }
}
