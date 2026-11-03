using System;
using System.Collections.Generic;
using UnityEngine;

// CTrunkDlg: original Trunk/backgrnd 463x318; five storage rows, four bag rows.
// Original source coordinates: OnCreate 7C618E, Draw 7C76FD; see storage reverse record.
public static class NativeStorageWindow
{
    const string TakeMesoKey = "TrunkTakeMeso";
    const string PutMesoKey = "TrunkPutMeso";
    static StorageSnapshot storage;
    static int tab = 1;                       // -1 = 全部（Tab 素材只有 5 页，第 0 页起按类型过滤）
    static int selection = -1, bagSelection = -1;
    static float scroll, bagScroll;
    static string takeMeso = "", putMeso = "";
    static Vector2 position = new Vector2(60, 30), grab;
    static bool dragging;
    static int npcId;

    const float W = 463, H = 318;
    const int Rows = 5, BagRows = 4;
    static readonly string[] Tabs = { "装备", "消耗", "装饰", "其他", "现金" };

    public static bool Visible => storage != null;

    public static void Reset()
    {
        storage = null; tab = 1; selection = bagSelection = -1;
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
        result.AddRange(storage.items);
        return result;
    }

    static List<InventoryEntry> Bag()
    {
        var result = new List<InventoryEntry>();
        var character = GameSession.Character;
        if (character == null) return result;
        foreach (var item in character.inventory)
            if (item.position > 0 && item.inventoryType > 0 && item.inventoryType == tab)
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
        int fee = NativeStorageService.TakeFee(npcId);
        NativeUtilPrompt.Confirm(fee > 0 ? "取出道具需要 " + fee + " 金币。\n确定要取出吗？" : "确定要从仓库取出道具吗？", yes =>
        {
            if (!yes || !Visible) return;
            Request(StorageOperation.TakeOut, npcId, inventoryType: slot.item.inventoryType, storageIndex: slot.index);
            selection = -1;
        });
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
        void Put(int quantity)
        {
            int fee = NativeStorageService.PutFee(npcId);
            NativeUtilPrompt.Confirm(fee > 0 ? "保管道具需要 " + fee + " 金币。\n确定要保管吗？" : "确定要保管道具吗？", yes =>
            {
                if (!yes || !Visible) return;
                Request(StorageOperation.Store, npcId, inventoryType: item.inventoryType,
                    itemPosition: item.position, quantity: quantity, itemId: item.itemId);
                bagSelection = -1;
            });
        }
        if (item.isEquip || rechargeable) Put(item.quantity);
        else NativeUtilPrompt.Number("存入多少个？", item.quantity, 1, item.quantity, Put);
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
        NativeNpcShopWindow.Portrait(npcId, 53, 76);
        NativeUi.CharacterPortrait(288, 75);
        if (NativeUi.Button(new Rect(154, 15, 70, 19), "UI/UIWindow.img/Trunk/BtExit", p)) { Close(); return; }
        if (NativeUi.Button(new Rect(154, 35, 70, 19), "UI/UIWindow.img/Trunk/BtGet", p)) TakeOut();
        if (NativeUi.Button(new Rect(154, 55, 71, 18), "UI/UIWindow.img/Trunk/BtSort", p)) Arrange();
        if (NativeUi.Button(new Rect(384, 15, 70, 19), "UI/UIWindow.img/Trunk/BtPut", p)) Store();

        int nextTab = NativeUi.TabsStyle2("UI/UIWindow.img/Trunk/Tab", 5, tab - 1, 235, 92, 222, p) + 1;
        if (nextTab != tab) { tab = nextTab; bagSelection = -1; bagScroll = 0; }
        var shown = Shown();
        scroll = NativeUi.Scroll4(new Rect(210, 88, 15, 197), new Rect(6, 87, 199, 198), scroll,
            Math.Max(0, shown.Count - Rows), 1, p);
        for (int row = 0; row < Rows && row + (int)scroll < shown.Count; row++)
        {
            int index = row + (int)scroll, y = 87 + 40 * row;
            var item = shown[index].item;
            if (selection == index) Image("select", 44, y);
            else if (index < storage.slots) Image("en", 6, y);
            DrawIcon(item.itemId, 7, y + 34);
            GUI.Label(new Rect(50, y + 1, 152, 16), ItemCatalog.Name(item.itemId), Text());
            GUI.Label(new Rect(50, y + 20, 152, 16), item.quantity.ToString(), Text());
            var hit = new Rect(6, y, 199, 35);
            if (hit.Contains(p)) ItemTooltip.SetHover(item.itemId, false, item);
            bool twice = Pointer.DoubleClicked;
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && hit.Contains(p))
            { Pointer.ConsumeClick(); selection = index; if (twice) { TakeOut(); return; } }
        }
        var bag = Bag();
        bagScroll = NativeUi.Scroll4(new Rect(441, 127, 15, 158), new Rect(236, 127, 199, 158), bagScroll,
            Math.Max(0, bag.Count - BagRows), 1, p);
        for (int row = 0; row < BagRows && row + (int)bagScroll < bag.Count; row++)
        {
            int index = row + (int)bagScroll, y = 127 + 40 * row;
            var item = bag[index];
            if (bagSelection == index) Image("select", 273, y);
            DrawIcon(item.itemId, 238, y + 34);
            GUI.Label(new Rect(279, y + 1, 152, 16), ItemCatalog.Name(item.itemId), Text());
            GUI.Label(new Rect(279, y + 20, 152, 16), item.quantity.ToString(), Text());
            var hit = new Rect(236, y, 199, 35);
            if (hit.Contains(p)) ItemTooltip.SetHover(item.itemId, false, item);
            bool twice = Pointer.DoubleClicked;
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && hit.Contains(p))
            { Pointer.ConsumeClick(); bagSelection = index; if (twice) { Store(); return; } }
        }
        GUI.Label(new Rect(44, 296, 146, 16), storage.meso.ToString("N0"), Text(true));
        GUI.Label(new Rect(275, 296, 143, 16), (GameSession.Character?.record.meso ?? 0).ToString("N0"), Text(true));
        if (NativeUi.Button(new Rect(5, 294, 36, 18), "UI/UIWindow.img/Trunk/BtOutCoin", p))
            NativeUtilPrompt.NumberLong("取出多少金币？", 1, 1, storage.meso, value => Request(StorageOperation.Meso, npcId, meso: value));
        if (NativeUi.Button(new Rect(236, 294, 36, 18), "UI/UIWindow.img/Trunk/BtInCoin", p))
            NativeUtilPrompt.NumberLong("存入多少金币？", 1, 1, GameSession.Character.record.meso, value => Request(StorageOperation.Meso, npcId, meso: -value));
    }
}
