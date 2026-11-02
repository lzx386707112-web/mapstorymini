using System;
using System.Collections.Generic;
using UnityEngine;

// 仓库窗口（CTrunkDlg）。底图 UI/UIWindow.img/Trunk/backgrnd 463x318，双栏：
//   左栏 = 仓库（4 行列表 y=84+40*i，行分隔虚线 y=120/164/204/244 来自像素检测；Tab 5 页
//          27x16/27x14 = 装备/消耗/装饰/其他/现金分类过滤；金币条 BtOutCoin (4,290) + 输入框 + 原图「金币」字）
//   右栏 = 背包（4 行同 y，行底 en 199x35；金币条 BtInCoin (236,290)）
//   描述区 y=6..78：左栏放选中物品图标/名称/描述与 BtGet/BtGetAll，右栏放 BtPut/BtSort/BtExit。
//   select 162x35 仓库行选中底、BtSort 71x18「道具排列」、BtGet/BtPut/BtExit/BtGetAll 70x19
//   （「掏出道具/道具保管/退出仓库/掏出道具」文字都印在图上）。
// 服务端语义见 NativeStorageService（15 级门槛、trunkPut/trunkGet 费用、婚戒禁入、karma 转不可交易）。
public static class NativeStorageWindow
{
    const string TakeMesoKey = "TrunkTakeMeso";
    const string PutMesoKey = "TrunkPutMeso";
    static StorageSnapshot storage;
    static int tab = -1;                       // -1 = 全部（Tab 素材只有 5 页，第 0 页起按类型过滤）
    static int selection = -1, bagSelection = -1;
    static float scroll, bagScroll;
    static string takeMeso = "", putMeso = "";
    static Vector2 position = new Vector2(60, 30), grab;
    static bool dragging;
    static int npcId;

    const float W = 463, H = 318;
    const int Rows = 4;
    static readonly string[] Tabs = { "装备", "消耗", "装饰", "其他", "现金" };

    public static bool Visible => storage != null;

    public static void Reset()
    {
        storage = null; tab = -1; selection = bagSelection = -1;
        scroll = bagScroll = 0; takeMeso = putMeso = ""; dragging = false; npcId = 0;
        NativeEditKeyboard.Close();
    }

    // npc/9030100.js 等：cm.getPlayer().getStorage().sendStorage(cm.getClient(), 9030100)
    public static void Open(int npc)
    {
        if (!GameSession.InGame) return;
        var res = Request(StorageOperation.Open, npc);
        if (res == null) return;
        if (!res.ok) { BottomBar.Toast(res.error); return; }
        npcId = npc; storage = res.storage;
        selection = bagSelection = -1; scroll = bagScroll = 0;
    }

    public static void Close()
    {
        if (storage != null && GameSession.InGame) Request(StorageOperation.Close, npcId);
        Reset();
    }

    static StorageResponse Request(StorageOperation operation, int npc, int inventoryType = 0, int itemPosition = 0,
        int storageIndex = -1, int quantity = 0, int itemId = 0, long meso = 0, int slots = 0)
    {
        if (!GameSession.InGame) return null;
        GameSession.SaveToDb();
        var res = GameSession.Net.Storage(new StorageRequest {
            characterId = GameSession.CharacterId, operation = operation, npcId = npc,
            inventoryType = inventoryType, itemPosition = itemPosition, storageIndex = storageIndex,
            quantity = quantity, itemId = itemId, meso = meso, slots = slots });
        Apply(res);
        return res;
    }

    static void Apply(StorageResponse res)
    {
        if (res == null) return;
        if (res.storage != null) storage = res.storage;
        if (res.inventory != null)
        {
            GameSession.Character.inventory = res.inventory;
            GameSession.ApplyInventoryToProfile();
            Profile.Save();
        }
        if (res.meso >= 0)
        {
            GameSession.Character.record.meso = res.meso;
            Profile.Data.gold = res.meso;
            Profile.Save();
        }
        if (!res.ok && res.error.Length > 0) NativeSystemMessage.Drop(5, res.error);
    }

    // ————————————————— 数据 —————————————————

    static List<StorageSlotData> Shown()
    {
        var result = new List<StorageSlotData>();
        if (storage == null) return result;
        foreach (var slot in storage.items)
            if (tab < 0 || slot.item.inventoryType == tab) result.Add(slot);
        return result;
    }

    static List<InventoryEntry> Bag()
    {
        var result = new List<InventoryEntry>();
        var character = GameSession.Character;
        if (character == null) return result;
        foreach (var item in character.inventory)
            if (item.position > 0 && item.inventoryType > 0 && (tab < 0 || item.inventoryType == tab))
                result.Add(item);
        result.Sort((a, b) => a.inventoryType != b.inventoryType
            ? a.inventoryType.CompareTo(b.inventoryType) : a.position.CompareTo(b.position));
        return result;
    }

    // ————————————————— 动作 —————————————————

    static void TakeOut()
    {
        var shown = Shown();
        if (selection < 0 || selection >= shown.Count) return;
        var slot = shown[selection];
        Request(StorageOperation.TakeOut, npcId, inventoryType: slot.item.inventoryType, storageIndex: slot.index);
        selection = -1;
    }

    static void TakeOutAll()
    {
        // BtGetAll：逐个取出，背包满 / 金币不足时服务端会拒，循环即止
        for (int i = 0; i < 48; i++)
        {
            var shown = Shown();
            if (shown.Count == 0) break;
            var slot = shown[0];
            var res = Request(StorageOperation.TakeOut, npcId, inventoryType: slot.item.inventoryType, storageIndex: slot.index);
            if (res == null || !res.ok) break;
        }
        selection = -1;
    }

    static void Store()
    {
        var bag = Bag();
        if (bagSelection < 0 || bagSelection >= bag.Count) return;
        var item = bag[bagSelection];
        bool rechargeable = Server.Bot.Trade.TradeItemRules.Rechargeable(item.itemId);
        NativeUtilPrompt.Number("存入多少个？", 1, 1, item.quantity, quantity =>
        {
            Request(StorageOperation.Store, npcId, inventoryType: item.inventoryType,
                itemPosition: item.position, quantity: rechargeable ? item.quantity : quantity, itemId: item.itemId);
            bagSelection = -1;
        });
    }

    static void Arrange() => Request(StorageOperation.Arrange, npcId);

    static void Meso(bool takeOut)
    {
        string text = takeOut ? takeMeso : putMeso;
        if (!long.TryParse(text, out long amount) || amount <= 0) { BottomBar.Toast("请输入金币数量"); return; }
        Request(StorageOperation.Meso, npcId, meso: takeOut ? amount : -amount);
        if (takeOut) takeMeso = ""; else putMeso = "";
    }

    // ————————————————— 绘制 —————————————————

    static GUIStyle Text(bool right = false)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = 12, padding = new RectOffset(),
            clipping = TextClipping.Clip, alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft };
        style.normal.textColor = Color.black; return style;
    }

    static GUIStyle Center() => new GUIStyle(Text()) { alignment = TextAnchor.UpperCenter };

    static void Image(string name, float x, float y)
    {
        var t = Art.Tex("wz://UI/UIWindow.img/Trunk/" + name);
        if (t != null) GUI.DrawTexture(new Rect(x, y, t.width, t.height), t);
    }

    static void DrawIcon(int itemId, float x, float y)
    {
        var icon = NativeItemSource.IconNode(itemId);
        if (icon == null) return;
        var origin = icon.Get("origin");
        var t = WzAssets.Texture(icon);
        GUI.DrawTexture(new Rect(x - (origin?.X ?? 0), y - (origin?.Y ?? 0), t.width, t.height), t);
    }

    static void Highlight(Rect rect)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    public static void Draw()
    {
        if (!Visible) return;
        if (!GameSession.InGame) { Reset(); return; }
        // 每个 GUI 事件先读键盘快照：Android 输入法最终文本可能与 Done 同帧到达。
        // 金币提交仍走 BtOutCoin/BtInCoin 按钮，Done 只收键盘不代提交（不改变交易口径）。
        NativeEditKeyboard.Sync(TakeMesoKey, ref takeMeso);
        NativeEditKeyboard.Sync(PutMesoKey, ref putMeso);
        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position, new Vector2(W, H), scale);
        NativeUi.RegisterWindow("NativeStorageWindow", new Rect(position.x * scale, position.y * scale, W * scale, H * scale), () => Visible);
        var pointer = Pointer.Pos / scale; var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed &&
                new Rect(position.x, position.y, W, 22).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, new Vector2(W, H), scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, W, H));
            try { DrawBody(pointer - position); }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }

    static void DrawBody(Vector2 p)
    {
        Image("backgrnd", 0, 0);

        // ——— 左栏 5 页签（Tab 素材 27x16 enabled / 27x14 disabled）———
        for (int i = -1; i < Tabs.Length; i++)
        {
            if (i >= 0)
            {
                float x = 8 + 27 * i;
                var rect = new Rect(x, 8, 27, 16);
                if (NativeUi.Button(rect, "UI/UIWindow.img/Trunk/Tab/" + (tab == i ? "enabled" : "disabled"), p)) { tab = tab == i ? -1 : i; selection = -1; scroll = bagScroll = 0; }
                GUI.Label(new Rect(x, 9, 27, 14), Tabs[i], Center());
            }
        }

        // ——— 左栏描述区：选中仓库物品 ———
        var shown = Shown();
        var sel = selection >= 0 && selection < shown.Count ? shown[selection].item : null;
        if (sel != null)
        {
            DrawIcon(sel.itemId, 12, 30);
            GUI.Label(new Rect(54, 28, 160, 14), ItemCatalog.Name(sel.itemId), Text());
            GUI.Label(new Rect(54, 44, 160, 14), "x" + sel.quantity, Text());
        }
        else GUI.Label(new Rect(10, 30, 210, 14), "选择要掏出的道具", Text());

        // ——— 右栏描述区按钮 ———
        GUI.Label(new Rect(244, 8, 210, 14), "背包（点击道具后保管）", Text());
        if (NativeUi.Button(new Rect(246, 24, 70, 19), "UI/UIWindow.img/Trunk/BtPut", p)) Store();
        if (NativeUi.Button(new Rect(320, 24, 71, 18), "UI/UIWindow.img/Trunk/BtSort", p)) Arrange();
        if (NativeUi.Button(new Rect(246, 46, 70, 19), "UI/UIWindow.img/Trunk/BtExit", p)) { Close(); return; }
        if (NativeUi.Button(new Rect(320, 46, 70, 19), "UI/UIWindow.img/Trunk/BtGetAll", p)) TakeOutAll();

        // ——— 左栏描述区按钮：取出 ———
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && sel != null;
        if (NativeUi.Button(new Rect(10, 60, 70, 19), "UI/UIWindow.img/Trunk/BtGet", p)) TakeOut();
        GUI.enabled = enabled;

        // ——— 仓库列表 4 行 ———
        scroll = NativeUi.Scroll4(new Rect(216, 86, 15, 194), new Rect(6, 84, 210, 196), scroll,
            Math.Max(0, shown.Count - Rows), 1, p);
        for (int row = 0; row < Rows && row + (int)scroll < shown.Count; row++)
        {
            int index = row + (int)scroll, y = 84 + 40 * row;
            var slot = shown[index];
            if (selection == row) Image("select", 8, y - 2);
            else if (new Rect(6, y - 2, 210, 38).Contains(p)) Highlight(new Rect(6, y - 2, 210, 38));
            DrawIcon(slot.item.itemId, 14, y + 3);
            GUI.Label(new Rect(48, y + 4, 130, 14), ItemCatalog.Name(slot.item.itemId), Text());
            GUI.Label(new Rect(48, y + 21, 130, 13), "x" + slot.item.quantity, Text());
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && new Rect(6, y - 2, 210, 38).Contains(p))
            { Pointer.ConsumeClick(); selection = selection == row ? -1 : row; }
        }

        // ——— 背包列表 4 行 ———
        var bag = Bag();
        bagScroll = NativeUi.Scroll4(new Rect(444, 86, 15, 194), new Rect(236, 84, 212, 196), bagScroll,
            Math.Max(0, bag.Count - Rows), 1, p);
        for (int row = 0; row < Rows && row + (int)bagScroll < bag.Count; row++)
        {
            int index = row + (int)bagScroll, y = 84 + 40 * row;
            var item = bag[index];
            if (bagSelection == row) Image("en", 236, y - 2);
            else if (new Rect(236, y - 2, 214, 38).Contains(p)) Highlight(new Rect(236, y - 2, 214, 38));
            DrawIcon(item.itemId, 244, y + 3);
            GUI.Label(new Rect(278, y + 4, 140, 14), ItemCatalog.Name(item.itemId), Text());
            GUI.Label(new Rect(278, y + 21, 140, 13), "x" + item.quantity, Text());
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && new Rect(236, y - 2, 214, 38).Contains(p))
            { Pointer.ConsumeClick(); bagSelection = bagSelection == row ? -1 : row; }
        }

        // ——— 金币条：左取右存（「金币」字在底图上）———
        var takeRect = new Rect(44, 292, 164, 16);
        var putRect = new Rect(276, 292, 158, 16);
        takeMeso = GUI.TextField(takeRect, takeMeso, 12);
        if (NativeUi.Button(new Rect(4, 290, 36, 18), "UI/UIWindow.img/Trunk/BtOutCoin", p)) Meso(true);
        putMeso = GUI.TextField(putRect, putMeso, 12);
        if (NativeUi.Button(new Rect(236, 290, 36, 18), "UI/UIWindow.img/Trunk/BtInCoin", p)) Meso(false);
        // 点击金币输入框拉起数字键盘（group 内 IMG 坐标用 p 命中）；同框已开着不重开。
        if (Event.current.type == EventType.Repaint && GUI.enabled && Pointer.Clicked)
        {
            if (takeRect.Contains(p)) { Pointer.ConsumeClick(); NativeEditKeyboard.Open(TakeMesoKey, takeMeso, TouchScreenKeyboardType.NumberPad, 12); }
            else if (putRect.Contains(p)) { Pointer.ConsumeClick(); NativeEditKeyboard.Open(PutMesoKey, putMeso, TouchScreenKeyboardType.NumberPad, 12); }
        }
        GUI.Label(new Rect(84, 296, 120, 12), "仓库 " + storage.meso.ToString("N0"), Text());
        GUI.Label(new Rect(316, 296, 120, 12), "身上 " + (GameSession.Character?.record.meso ?? 0).ToString("N0"), Text());

        // 右上角关闭热区
        var close = new Rect(W - 20, 2, 16, 14);
        if (close.Contains(p)) Highlight(close);
        GUI.Label(close, "×", new GUIStyle(Center()) { fontSize = 13 });
        if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && close.Contains(p))
        { Pointer.ConsumeClick(); Close(); }
    }
}
