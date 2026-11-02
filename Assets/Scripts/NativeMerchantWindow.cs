using System;
using System.Collections.Generic;
using UnityEngine;

// 雇佣商店店主窗口。版式沿用 UI/UIWindow.img/PersonalShop（525x553，PersonalShop/backgrnd6），
// 店主专属按钮在源端是 UI/UIWindow.img/EntrustedShop（BtArrange / BtArrange2 / BtArrange_3 / BtCoin），
// 这里改用文字按钮，避免按位置猜图标的语义。
// 规则全部来自 HiredMerchant / PlayerInteractionHandler：
//   - 营业中不能上架、不能取回（message11 / message14）
//   - 提金币 = Character.withdrawMerchantMesos（寄存越久 Fredrick 抽成越高）
//   - 关店时能装下就把货退回背包，装不下的留在 Fredrick 寄存
public static class NativeMerchantWindow
{
    static MerchantSnapshot shop;
    static Vector2 position = new Vector2(120, 10), grab;
    static bool dragging;
    static int shopSelection = -1, bagSelection = -1;
    static float shopScroll, bagScroll;
    static int generation;
    static double nextRead;

    public static bool Visible => shop != null;
    public static bool HasMerchant => shop != null;

    public static void Reset()
    {
        shop = null; shopSelection = bagSelection = -1;
        shopScroll = bagScroll = 0; dragging = false; generation++;
    }

    // 用许可道具开店 / 打开已有店铺：两者都先拿一次快照。
    public static void Open(int permitItemId)
    {
        if (!GameSession.InGame) return;
        if (Profile.LastMapId / 1000000 != 910 || Profile.LastMapId <= 910000000)
        { BottomBar.Toast("只能在自由市场里开雇佣商店"); return; }
        Reset();
        var res = Request(MerchantOperation.Snapshot);
        if (res?.shop != null) { shop = res.shop; return; }
        var box = Request(MerchantOperation.Box);
        if (box == null || !box.ok) { BottomBar.Toast(box?.error ?? "商店盒无法打开"); return; }
        if (!box.box) { BottomBar.Toast(box.error); return; }
        var created = GameSession.Net.Merchant(new MerchantRequest {
            characterId = GameSession.CharacterId, operation = MerchantOperation.Create,
            price = permitItemId, description = "" });
        if (!created.ok) { BottomBar.Toast(created.error); return; }
        shop = created.shop;
        RefreshInventory(created);
    }

    // Fredrick（NPC 9030000）取回寄存：FredrickProcessor.fredrickRetrieveItems。
    public static void Retrieve()
    {
        if (!GameSession.InGame) return;
        GameSession.SaveToDb();
        var res = GameSession.Net.Merchant(new MerchantRequest { characterId = GameSession.CharacterId, operation = MerchantOperation.Retrieve });
        if (!res.ok) { BottomBar.Toast(res.error); return; }
        if (res.inventory != null) { GameSession.Character.inventory = res.inventory; GameSession.ApplyInventoryToProfile(); }
        if (res.meso >= 0) { GameSession.Character.record.meso = res.meso; Profile.Data.gold = res.meso; }
        Profile.Save();
        BottomBar.Toast(res.code == NativeMerchantService.FredrickSuccess ? "已取回雇佣商店寄存" : res.error);
    }

    // Character.closeHiredMerchant(true)：关窗时只有「店主且货架为空」才真正删店，
    // 否则只是离开窗口（店继续保持营业），货和金币都留在店里。
    public static void Close()
    {
        if (shop != null && GameSession.InGame && shop.items.Count == 0)
            Request(MerchantOperation.Close);
        Reset();
    }

    static MerchantResponse Request(MerchantOperation operation, int slot = -1, int quantity = 0, int bundles = 0, int price = 0,
        int inventoryType = 0, int itemPosition = 0)
    {
        if (!GameSession.InGame) return new MerchantResponse { ok = false, error = "请先选择角色" };
        int version = generation;
        GameSession.SaveToDb();
        var res = GameSession.Net.Merchant(new MerchantRequest {
            characterId = GameSession.CharacterId, operation = operation, slot = slot, quantity = quantity,
            bundles = bundles, price = price, inventoryType = inventoryType, itemPosition = itemPosition });
        if (version != generation) return res;
        if (!res.ok) { if (!string.IsNullOrEmpty(res.error)) BottomBar.Toast(res.error); return res; }
        if (res.shop != null) shop = res.shop;
        RefreshInventory(res);
        return res;
    }

    static void RefreshInventory(MerchantResponse res)
    {
        if (res?.inventory != null) { GameSession.Character.inventory = res.inventory; GameSession.ApplyInventoryToProfile(); }
        if (res != null && res.meso >= 0) { GameSession.Character.record.meso = res.meso; Profile.Data.gold = res.meso; }
        if (res?.inventory != null || res?.meso >= 0) Profile.Save();
    }

    static List<InventoryEntry> Bag()
    {
        var result = new List<InventoryEntry>();
        var character = GameSession.Character;
        if (character == null) return result;
        foreach (var item in character.inventory)
            if (item.position > 0 && item.inventoryType > 0) result.Add(item);
        result.Sort((a, b) => a.inventoryType != b.inventoryType ? a.inventoryType.CompareTo(b.inventoryType) : a.position.CompareTo(b.position));
        return result;
    }

    static void Stock()
    {
        var bag = Bag();
        if (bagSelection < 0 || bagSelection >= bag.Count) return;
        var item = bag[bagSelection];
        bool rechargeable = Server.Bot.Trade.TradeItemRules.Rechargeable(item.itemId);
        int version = generation;
        int perBundle = 1, bundles = 1;
        Action askPrice = () => NativeUtilPrompt.Number("每份卖多少金币？", 1, 1, int.MaxValue, price =>
        {
            if (version != generation) return;
            Request(MerchantOperation.AddItem, 0, perBundle, bundles, price, item.inventoryType, item.position);
            bagSelection = -1;
        });
        Action askBundles = () => NativeUtilPrompt.Number("一共摆几份？", 1, 1, 2000, value =>
        {
            bundles = value;
            if (version != generation) return;
            askPrice();
        });
        if (rechargeable || item.quantity <= 1)
        {
            perBundle = rechargeable ? 1 : item.quantity;
            askPrice();
            return;
        }
        NativeUtilPrompt.Number("每份多少个？", 1, 1, item.quantity, value => { perBundle = value; if (version == generation) askBundles(); });
    }

    // ————————————————— 绘制 —————————————————

    static GUIStyle Text(bool right = false, TextAnchor align = TextAnchor.UpperLeft)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = 11, padding = new RectOffset(), clipping = TextClipping.Clip, alignment = align };
        style.normal.textColor = Color.black; return style;
    }

    static GUIStyle Small()
        => new GUIStyle(GUI.skin.button) { font = UiKit.Font, fontSize = 10, padding = new RectOffset(2, 2, 1, 1) };

    static void Image(string name, float x, float y)
    {
        var texture = Art.Tex("wz://UI/UIWindow.img/PersonalShop/" + name);
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

    public static void Draw()
    {
        if (!Visible) return;
        if (!GameSession.InGame) { Reset(); return; }
        // 店里的成交是假人异步买的，隔一会儿拉一次快照（源端是服务端主动推 updateHiredMerchant）。
        if (Time.realtimeSinceStartupAsDouble >= nextRead)
        {
            nextRead = Time.realtimeSinceStartupAsDouble + .5;
            var res = GameSession.Net.Merchant(new MerchantRequest { characterId = GameSession.CharacterId, operation = MerchantOperation.Snapshot });
            if (res?.shop != null) shop = res.shop;
            else if (res != null && res.ok && res.shop == null) Reset();
        }
        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position, new Vector2(525, 553), scale);
        NativeUi.RegisterWindow("NativeMerchantWindow", new Rect(position.x * scale, position.y * scale, 525 * scale, 553 * scale), () => Visible);
        var pointer = Pointer.Pos / scale; var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x, position.y, 500, 22).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, new Vector2(525, 553), scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, 525, 553));
            try
            {
                var p = pointer - position;
                Image("backgrnd6", 0, 0);
                var employee = WzAssets.Node("Item/Cash/0503.img/" + shop.permitId.ToString("D8") + "/employee/stand/0");
                if (employee != null)
                {
                    var texture = WzAssets.Texture(employee);
                    if (texture != null) GUI.DrawTexture(new Rect(53 - texture.width / 2, 127 - texture.height, texture.width, texture.height), texture);
                }
                GUI.Label(new Rect(23, 9, 460, 16), (string.IsNullOrEmpty(shop.description) ? "雇佣商店" : shop.description)
                    + (shop.open ? "（营业中）" : "（准备中）"), Text());
                GUI.Label(new Rect(9, 135, 96, 16), shop.ownerName, Text(false, TextAnchor.UpperCenter));
                GUI.Label(new Rect(9, 147, 180, 14), "店内存货 · 金币 " + shop.merchantMeso.ToString("N0"), Text());

                // 左侧：店内货架（最多 16 格，5 行滚动）
                shopScroll = NativeUi.Scroll4(new Rect(211, 158, 15, 197), new Rect(7, 157, 199, 200), shopScroll,
                    Math.Max(0, shop.items.Count - 5), 1, p);
                for (int row = 0; row < 5; row++)
                {
                    int index = row + (int)shopScroll;
                    if (index >= shop.items.Count) break;
                    var slot = shop.items[index];
                    float y = 157 + 40 * row;
                    if (index == shopSelection) Image("select", 44, y);
                    if (slot.item != null) Icon(slot.item.itemId, 8, y + 34);
                    GUI.Label(new Rect(48, y + 1, 152, 16), slot.item == null ? "?" : ItemCatalog.Name(slot.item.itemId), Text());
                    GUI.Label(new Rect(48, y + 19, 152, 16), (slot.item?.quantity ?? 0) + " 个 / " + slot.price.ToString("N0") + " 金币", Text());
                    GUI.Label(new Rect(176, y + 23, 24, 12), "x" + slot.bundles, Text(true));
                    if (slot.bundles < 1) Image("soldout", 7, y);
                    if (Hit(new Rect(7, y, 199, 35), p)) shopSelection = index;
                }

                // 右侧：背包（选一件上架）
                GUI.Label(new Rect(236, 135, 200, 14), "背包 · 选中后点「上架」", Text());
                var bag = Bag();
                bagScroll = NativeUi.Scroll4(new Rect(508, 158, 15, 197), new Rect(236, 157, 270, 200), bagScroll,
                    Math.Max(0, bag.Count - 10), 1, p);
                for (int row = 0; row < 10; row++)
                {
                    int index = row + (int)bagScroll;
                    if (index >= bag.Count) break;
                    var item = bag[index];
                    float y = 157 + 20 * row;
                    if (index == bagSelection) GUI.Box(new Rect(236, y, 270, 19), "");
                    Icon(item.itemId, 246, y + 16);
                    GUI.Label(new Rect(262, y + 2, 240, 14), ItemCatalog.Name(item.itemId)
                        + (item.quantity > 1 ? " x" + item.quantity : ""), Text());
                    if (Hit(new Rect(236, y, 270, 19), p)) bagSelection = index;
                }

                // 店主按钮：源端 EntrustedShop 的 BtArrange / BtArrange2 / BtArrange_3 / BtCoin 语义
                var closeRect = new Rect(144, 110, 60, 19);
                if (NativeUi.Button(closeRect, "UI/UIWindow.img/PersonalShop/BtExit", p)) { Close(); return; }
                float by = 366;
                bool editing = !shop.open;
                if (editing && GUI.Button(new Rect(7, by, 64, 20), "上架", Small())) Stock();
                if (editing && GUI.Button(new Rect(75, by, 64, 20), "取回", Small()) && shopSelection >= 0)
                {
                    var slot = shop.items[shopSelection];
                    Request(MerchantOperation.TakeItemBack, slot.slot);
                    shopSelection = -1;
                }
                if (editing && GUI.Button(new Rect(143, by, 64, 20), "开始营业", Small())) Request(MerchantOperation.MaintenanceOff);
                if (!editing && GUI.Button(new Rect(7, by, 64, 20), "结束营业", Small())) Request(MerchantOperation.SetOpen, 0, 0);
                if (!editing && GUI.Button(new Rect(75, by, 64, 20), "整理", Small())) Request(MerchantOperation.Organize);
                if (GUI.Button(new Rect(211, by, 64, 20), "提金币", Small())) Request(MerchantOperation.WithdrawMesos);
                if (GUI.Button(new Rect(279, by, 64, 20), "关店", Small()))
                {
                    int version = generation;
                    NativeUtilPrompt.Confirm("要关闭雇佣商店吗？\n装不下的道具会留在弗利德里那里寄存。", yes =>
                    { if (yes && version == generation) { Request(MerchantOperation.Close); Reset(); } });
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }
}
