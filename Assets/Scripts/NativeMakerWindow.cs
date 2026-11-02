using System;
using System.Collections.Generic;
using UnityEngine;

// 制作（Maker）窗口。版式用客户端自带的 UI/UIWindow.img/Maker（backgrnd 312x393），
// 按钮图（BtStart / BtCancel / ComboBox）都没有文字，坐标也拿不到反汇编依据（窗口路径是
// "UI/UIWindow.img/%s/backgrnd" 拼的），所以这里只在底图空区内重排控件、按钮用中文文字。
//
// 三条业务流程与源端 MakerProcessor.makerAction 一一对应：
//   制作（type 0）：配方 → 材料/金币/等级/制作技能等级校验 → 可选刺激剂 + 强化宝石
//   分解（type 4）：背包里的装备 → 退回配方里点名的一半结晶
//   结晶（type 3）：100 份怪物原石 → 一颗怪物结晶（档位按掉这只原石的怪的等级）
public static class NativeMakerWindow
{
    const string Root = "UI/UIWindow.img/Maker/";
    const float W = 312, H = 393;
    const float RowH = 19f;
    const int RowsVisible = 17;
    const float ListX = 8, ListY = 42, ListW = 148, ListH = H - ListY - 6;
    const float PanelX = 162, PanelW = 142;

    static bool visible;
    static int tab;                       // 0 制作 / 1 分解 / 2 结晶
    static bool jobOnly = true;

    static List<MakerRecipeView> recipes = new List<MakerRecipeView>();
    static List<int> rows = new List<int>();        // 当前列表里的 itemId
    static float scroll;
    static int selected;

    static bool stimulant;
    static List<int> reagents = new List<int>();     // 已选强化宝石（可重复）
    static bool picking;                             // 宝石选择层

    static int level, makerLevel;
    static long meso;
    static string hint = "";

    static Vector2 position = new Vector2(240, 50), grab;
    static bool dragging;
    static int generation;

    public static bool Visible => visible;

    public static void Reset()
    {
        visible = false; tab = 0; jobOnly = true;
        recipes = new List<MakerRecipeView>(); rows = new List<int>();
        scroll = 0; selected = 0; stimulant = false; reagents = new List<int>();
        picking = false; hint = ""; generation++;
    }

    public static void Open()
    {
        if (!GameSession.InGame) return;
        Reset();
        if (!EnsureTable()) { BottomBar.Toast("制作数据缺失"); return; }
        var res = Request(MakerOperation.Open);
        if (!res.ok) { BottomBar.Toast(res.error); Reset(); return; }
        recipes = res.recipes ?? new List<MakerRecipeView>();
        level = res.level; makerLevel = res.makerLevel; meso = res.meso;
        visible = true;
        Rebuild();
    }

    public static void Close() => Reset();

    static bool EnsureTable()
    {
        if (MakerTable.Loaded) return true;
        var asset = Resources.Load<TextAsset>("Maker/MakerTable");
        if (asset == null) return false;
        MakerTable.Load(asset.text);
        return true;
    }

    static MakerResponse Request(MakerOperation operation, int toCreate = 0, int position_ = 0,
        bool useStimulant = false, int[] reagentIds = null)
    {
        if (!GameSession.InGame) return new MakerResponse { ok = false, error = "请先选择角色" };
        GameSession.SaveToDb();
        var res = GameSession.Net.Maker(new MakerRequest
        {
            characterId = GameSession.CharacterId, operation = operation, toCreate = toCreate,
            position = position_, stimulant = useStimulant, reagentIds = reagentIds,
        });
        if (res.inventory != null) { GameSession.Character.inventory = res.inventory; GameSession.ApplyInventoryToProfile(); }
        if (res.meso >= 0) { GameSession.Character.record.meso = res.meso; Profile.Data.gold = res.meso; meso = res.meso; }
        if (res.inventory != null || res.meso >= 0) Profile.Save();
        return res;
    }

    // ————————————————— 列表 —————————————————

    static MakerRecipeView RecipeOf(int itemId)
    {
        for (int i = 0; i < recipes.Count; i++) if (recipes[i].itemId == itemId) return recipes[i];
        return null;
    }

    static void Rebuild()
    {
        rows = new List<int>();
        scroll = 0;
        if (tab == 0)
        {
            int category = MakerTable.CategoryOf(GameSession.Character != null ? GameSession.Character.record.job : 0);
            foreach (var recipe in recipes)
                if (!jobOnly || recipe.category == 0 || recipe.category == category) rows.Add(recipe.itemId);
        }
        else
        {
            var seen = new HashSet<int>();
            foreach (var item in GameSession.Character.inventory)
            {
                if (item.position <= 0 || !seen.Add(item.itemId)) continue;
                if (tab == 1)
                {
                    if (item.inventoryType != 1) continue;
                    if (!NativeMakerService.CanDisassemble(item.itemId)) continue;
                    rows.Add(item.itemId);
                }
                else
                {
                    if (item.inventoryType != 4) continue;
                    if (MakerTable.CrystalFromLeftover(item.itemId) == -1) continue;
                    rows.Add(item.itemId);
                }
            }
            rows.Sort();
        }
        selected = 0;
        stimulant = false;
        reagents = new List<int>();
        picking = false;
    }

    // 分解页要按「格子」而不是「道具」，同一件装备可能有多件。
    static int EquipPositionOf(int itemId)
    {
        foreach (var item in GameSession.Character.inventory)
            if (item.inventoryType == 1 && item.position > 0 && item.itemId == itemId) return item.position;
        return 0;
    }

    static int HeldOf(int itemId)
    {
        int total = 0;
        foreach (var item in GameSession.Character.inventory)
            if (item.itemId == itemId && item.position > 0) total += item.quantity;
        return total;
    }

    // ————————————————— 动作 —————————————————

    static void DoCraft()
    {
        if (selected < 0 || selected >= rows.Count) return;
        int itemId = rows[selected];
        var recipe = RecipeOf(itemId);
        if (recipe == null) return;
        if (recipe.reqLevel > level) { hint = "等级不足"; return; }

        int[] picks = reagents.ToArray();
        var res = Request(MakerOperation.Craft, itemId, 0, stimulant, picks);
        if (!res.ok) { hint = res.error; BottomBar.Toast(res.error); return; }
        if (!res.craftSucceeded) { hint = "制作失败，材料已消耗"; BottomBar.Toast("制作失败"); }
        else
        {
            string name = ItemCatalog.Name(itemId);
            hint = "已制作 " + name;
            BottomBar.Toast("制作成功：" + name);
        }
        stimulant = false; reagents = new List<int>();
        var refresh = Request(MakerOperation.Open);
        if (refresh.ok) { recipes = refresh.recipes ?? recipes; level = refresh.level; makerLevel = refresh.makerLevel; }
        Rebuild();
    }

    static void DoDisassemble()
    {
        if (selected < 0 || selected >= rows.Count) return;
        int itemId = rows[selected];
        int slot = EquipPositionOf(itemId);
        if (slot == 0) { hint = "背包里没有这件装备"; return; }
        var res = Request(MakerOperation.Disassemble, itemId, slot);
        if (!res.ok) { hint = res.error; BottomBar.Toast(res.error); return; }
        var gains = NativeMakerService.DisassemblyGains(itemId);
        string text = "已分解";
        foreach (var gain in gains)
            if (gain.count > 0) text += "  " + ItemCatalog.Name(gain.itemId) + " x" + gain.count;
        hint = text;
        BottomBar.Toast(text);
        var refresh = Request(MakerOperation.Open);
        if (refresh.ok) { recipes = refresh.recipes ?? recipes; level = refresh.level; makerLevel = refresh.makerLevel; }
        Rebuild();
    }

    static void DoCrystal()
    {
        if (selected < 0 || selected >= rows.Count) return;
        int leftover = rows[selected];
        int crystal = MakerTable.CrystalFromLeftover(leftover);
        if (crystal == -1) { hint = "这种原石不能合成"; return; }
        if (HeldOf(leftover) < 100) { hint = "需要 100 份 " + ItemCatalog.Name(leftover); return; }
        var res = Request(MakerOperation.Crystal, leftover);
        if (!res.ok) { hint = res.error; BottomBar.Toast(res.error); return; }
        hint = "已合成 " + ItemCatalog.Name(crystal);
        BottomBar.Toast("已合成 " + ItemCatalog.Name(crystal));
        var refresh = Request(MakerOperation.Open);
        if (refresh.ok) { recipes = refresh.recipes ?? recipes; level = refresh.level; makerLevel = refresh.makerLevel; }
        Rebuild();
    }

    // ————————————————— 绘制 —————————————————

    static GUIStyle Label(bool right = false, int size = 11)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = size, padding = new RectOffset(), clipping = TextClipping.Clip,
            alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft, wordWrap = false };
        style.normal.textColor = Color.black; return style;
    }

    static GUIStyle Small()
        => new GUIStyle(GUI.skin.button) { font = UiKit.Font, fontSize = 10, padding = new RectOffset(2, 2, 1, 1) };

    static void Image(string node, float x, float y)
    {
        var texture = Art.Tex("wz://" + node);
        if (texture != null) GUI.DrawTexture(new Rect(x, y, texture.width, texture.height), texture);
    }

    static void Icon(int itemId, float x, float foot)
    {
        var icon = NativeItemSource.IconNode(itemId); if (icon == null) return;
        var texture = WzAssets.Texture(icon); if (texture == null) return;
        var origin = WzAssets.Origin(icon);
        GUI.DrawTexture(new Rect(x - origin.x, foot - origin.y, texture.width, texture.height), texture);
    }

    static bool Hit(Rect rect, Vector2 pointer)
    {
        if (!GUI.enabled || Event.current.type != EventType.Repaint || !Pointer.Released || !rect.Contains(pointer)) return false;
        Pointer.ConsumeClick(); return true;
    }

    static void Tabs(Vector2 p)
    {
        string[] names = { "制作", "分解", "结晶" };
        for (int i = 0; i < names.Length; i++)
        {
            var rect = new Rect(10 + i * 46, 22, 44, 17);
            if (tab == i) GUI.Box(rect, "");
            if (GUI.Button(rect, names[i], Small())) { tab = i; Rebuild(); return; }
        }
        var toggle = new Rect(10 + names.Length * 46, 22, 44, 17);
        if (tab == 0 && GUI.Button(toggle, jobOnly ? "本职业" : "全部", Small())) { jobOnly = !jobOnly; Rebuild(); }
        GUI.Label(new Rect(W - 92, 24, 84, 14), "金币 " + meso.ToString("N0"), Label(true));
    }

    static void DrawList(Vector2 p)
    {
        int max = Math.Max(0, rows.Count - RowsVisible);
        scroll = NativeUi.Scroll4(new Rect(ListX + ListW - 4, ListY, 12, ListH), new Rect(ListX, ListY, ListW, ListH), scroll, max, 1, p);
        for (int i = 0; i < RowsVisible; i++)
        {
            int index = i + (int)scroll;
            if (index >= rows.Count) break;
            int itemId = rows[index];
            float y = ListY + i * RowH;
            var rect = new Rect(ListX, y, ListW, RowH - 1);
            if (index == selected) GUI.Box(rect, "");
            Icon(itemId, ListX + 14, y + RowH - 3);
            string name = ItemCatalog.Name(itemId) ?? itemId.ToString();
            if (name.Length > 9) name = name.Substring(0, 9);
            GUI.Label(new Rect(ListX + 26, y + 2, ListW - 60, 14), name, Label());
            var recipe = RecipeOf(itemId);
            string tail = tab == 0 ? (recipe != null ? "Lv." + recipe.reqLevel : "") : "x" + HeldOf(itemId);
            GUI.Label(new Rect(ListX + ListW - 34, y + 2, 30, 14), tail, Label(true));
            if (Hit(rect, p)) { selected = index; hint = ""; picking = false; reagents = new List<int>(); }
        }
    }

    static void DrawCraftPanel(Vector2 p)
    {
        if (selected < 0 || selected >= rows.Count) { GUI.Label(new Rect(PanelX, ListY, PanelW, 14), "请选择配方", Label()); return; }
        var recipe = RecipeOf(rows[selected]);
        if (recipe == null) return;
        float y = ListY;
        GUI.Label(new Rect(PanelX, y, PanelW, 26), ItemCatalog.Name(recipe.itemId), Label());
        y += 26;

        bool levelOk = level >= recipe.reqLevel, skillOk = makerLevel >= recipe.reqSkillLevel;
        GUI.Label(new Rect(PanelX, y, PanelW, 14), "等级 " + level + " / " + recipe.reqLevel + (levelOk ? "" : " 不足"), Label());
        y += 14;
        GUI.Label(new Rect(PanelX, y, PanelW, 14), "制作技能 " + makerLevel + " / " + recipe.reqSkillLevel + (skillOk ? "" : " 不足"), Label());
        y += 14;
        GUI.Label(new Rect(PanelX, y, PanelW, 14), "金币 " + recipe.meso.ToString("N0"), Label());
        y += 16;

        GUI.Label(new Rect(PanelX, y, PanelW, 13), "材料", Label());
        y += 13;

        if (picking)
        {
            // 强化宝石选择层：只列背包里的 425xxxx
            float top = y;
            GUI.Label(new Rect(PanelX, top, PanelW, 13), "选强化宝石（点击加入）", Label());
            top += 14;
            int limit = NativeMakerService.ReagentSlotsFor(recipe.itemId);
            int pickRow = 0;
            foreach (var item in GameSession.Character.inventory)
            {
                if (item.position <= 0 || item.quantity <= 0) continue;
                if (!NativeMakerService.IsMakerReagent(item.itemId)) continue;
                var rect = new Rect(PanelX, top + pickRow * 16, PanelW, 15);
                string label = (ItemCatalog.Name(item.itemId) ?? "") + " x" + item.quantity;
                if (GUI.Button(rect, label, Small()))
                {
                    if (reagents.Count >= limit) hint = "这件装备只能放 " + limit + " 颗宝石";
                    else reagents.Add(item.itemId);
                    return;
                }
                pickRow++;
                if (pickRow > 8) break;
            }
            if (GUI.Button(new Rect(PanelX, ListY + ListH - 40, PanelW, 18), "返回", Small())) picking = false;
            return;
        }

        int line = 0;
        foreach (var req in recipe.reqItems)
        {
            if (line >= 5) { GUI.Label(new Rect(PanelX, y, PanelW, 13), "…", Label()); break; }
            bool ok = req.held >= req.count;
            GUI.Label(new Rect(PanelX, y, PanelW, 13),
                ItemCatalog.Name(req.itemId) + "  " + req.held + "/" + req.count + (ok ? "" : " 缺"), Label());
            y += 13; line++;
        }

        y += 4;
        int slots = NativeMakerService.ReagentSlotsFor(recipe.itemId);
        int stimulantId = NativeMakerService.StimulantFor(recipe.itemId);
        if (stimulantId != -1)
        {
            stimulant = GUI.Toggle(new Rect(PanelX, y, PanelW, 15), stimulant,
                " 刺激剂 " + ItemCatalog.Name(stimulantId), Small());
            y += 16;
        }

        GUI.Label(new Rect(PanelX, y, 60, 14), "宝石 " + reagents.Count + "/" + slots, Label());
        if (GUI.Button(new Rect(PanelX + 70, y - 1, 28, 16), "+", Small())) picking = true;
        if (reagents.Count > 0 && GUI.Button(new Rect(PanelX + 102, y - 1, 36, 16), "清空", Small())) reagents = new List<int>();
        y += 18;
        foreach (int reagentId in reagents)
        {
            GUI.Label(new Rect(PanelX, y, PanelW, 13), "· " + ItemCatalog.Name(reagentId), Label());
            y += 13;
        }

        if (GUI.Button(new Rect(PanelX, ListY + ListH - 24, PanelW, 22), "制作", Small())) DoCraft();
    }

    static void DrawDisassemblePanel(Vector2 p)
    {
        if (selected < 0 || selected >= rows.Count) { GUI.Label(new Rect(PanelX, ListY, PanelW, 14), "请选择装备", Label()); return; }
        int itemId = rows[selected];
        float y = ListY;
        GUI.Label(new Rect(PanelX, y, PanelW, 26), ItemCatalog.Name(itemId), Label());
        y += 28;
        GUI.Label(new Rect(PanelX, y, PanelW, 14), "分解可得（配方的一半）", Label());
        y += 14;
        foreach (var gain in NativeMakerService.DisassemblyGains(itemId))
        {
            GUI.Label(new Rect(PanelX, y, PanelW, 13), ItemCatalog.Name(gain.itemId) + " x" + gain.count, Label());
            y += 13;
        }
        if (GUI.Button(new Rect(PanelX, ListY + ListH - 24, PanelW, 22), "分解", Small())) DoDisassemble();
    }

    static void DrawCrystalPanel(Vector2 p)
    {
        if (selected < 0 || selected >= rows.Count) { GUI.Label(new Rect(PanelX, ListY, PanelW, 14), "请选择原石", Label()); return; }
        int leftover = rows[selected];
        int crystal = MakerTable.CrystalFromLeftover(leftover);
        float y = ListY;
        GUI.Label(new Rect(PanelX, y, PanelW, 26), ItemCatalog.Name(leftover), Label());
        y += 28;
        GUI.Label(new Rect(PanelX, y, PanelW, 14), "→ " + ItemCatalog.Name(crystal), Label());
        y += 14;
        GUI.Label(new Rect(PanelX, y, PanelW, 14), "需要 100 份（持有 " + HeldOf(leftover) + "）", Label());
        if (GUI.Button(new Rect(PanelX, ListY + ListH - 24, PanelW, 22), "合成", Small())) DoCrystal();
    }

    public static void Draw()
    {
        if (!visible) return;
        if (!GameSession.InGame) { Reset(); return; }

        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position, new Vector2(W, H), scale);
        NativeUi.RegisterWindow("NativeMakerWindow", new Rect(position.x * scale, position.y * scale, W * scale, H * scale), () => Visible);
        var pointer = Pointer.Pos / scale; var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x, position.y, W, 16).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, new Vector2(W, H), scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, W, H));
            try
            {
                var p = pointer - position;
                Image(Root + "backgrnd", 0, 0);
                GUI.Label(new Rect(10, 4, 200, 14), "制作", Label());
                if (NativeUi.Button(new Rect(W - 22, 4, 16, 16), "UI/UIWindow.img/BtUIClose2", p)) { Close(); return; }
                Tabs(p);
                DrawList(p);
                if (tab == 0) DrawCraftPanel(p); else if (tab == 1) DrawDisassemblePanel(p); else DrawCrystalPanel(p);
                if (!string.IsNullOrEmpty(hint)) GUI.Label(new Rect(10, H - 16, W - 20, 13), hint, Label());
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }
}
