using System;
using System.Collections.Generic;
using UnityEngine;

// 个人商店店主窗口（CPersonalShopDlg）。底图 UI/UIWindow.img/PersonalShop/backgrnd（525x553），
// 商品区坐标沿用反汇编结论：
//   backgrnd 525x553 / select 162x35 / soldout 199x35 / bar 103x17
//   商品可视 5 行 y=157+40*i，select 在 (44,y)，icon 锚点 (8,y+34)，
//   名称 (48,y+1) 裁 152px，列表滚动条 (211,158,15,197)，
//   BtBuy (144,90) / BtExit (144,110) / BtInfo·BtItem (144,62)，聊天输入 (238,323) 231x15。
// 规则全部来自 PlayerShop / PlayerInteractionHandler：
//   - 营业中不能上架、不能取回；每格最多 2000 个；货架 16 格
//   - 开张营业才扣营业执照（use_erase_permit_on_open_shop）
//   - 关店把货退回背包；卖空自动关店
public static class NativePlayerShopWindow
{
    static PlayerShopInfo shop;
    static long meso;
    static Vector2 position = new Vector2(100, 20), grab;
    static bool dragging;
    static int shopSelection = -1, bagSelection = -1, page;
    static float shopScroll, bagScroll;
    static string chatInput = "";
    static double nextRead;

    const float W = 525, H = 553;

    public static bool Visible => shop != null;
    public static bool HasShop => shop != null;

    static readonly string[] pages = { "背包", "已售出", "聊天" };

    public static void Reset()
    {
        shop = null; bagSelection = shopSelection = -1;
        shopScroll = bagScroll = 0; page = 0; chatInput = ""; dragging = false;
    }

    // 双击 514xxxx 营业执照：已有店就开窗，没有就输入店名后开店（= CREATE 4/5）。
    public static void Open(int permitItemId)
    {
        if (!GameSession.InGame) return;
        if (Profile.LastMapId / 1000000 != 910 || Profile.LastMapId <= 910000000)
        { BottomBar.Toast("只能在自由市场里开个人商店"); return; }
        var snapshot = Request(PlayerShopOperation.Snapshot);
        if (snapshot?.shop != null) return;
        NativeUtilPrompt.Text("商店名称（最多 40 字）", "", 1, 40, text =>
        {
            var res = Request(PlayerShopOperation.Create, permitItemId: permitItemId, description: text);
            if (res != null && !res.ok) BottomBar.Toast(res.error);
        });
    }

    public static void Close()
    {
        // EXIT(0x0A)：关店 + 退道具；装不下时服务端会拒绝并保留店内货物。
        if (shop != null && GameSession.InGame) Request(PlayerShopOperation.Close);
        Reset();
    }

    static PlayerShopResponse Request(PlayerShopOperation operation, int slot = -1, int inventoryType = 0,
        int itemPosition = 0, int perBundle = 0, int bundles = 0, int price = 0, int permitItemId = 0,
        string description = "", string name = "", string text = "")
    {
        if (!GameSession.InGame) return new PlayerShopResponse { ok = false, error = "请先选择角色" };
        GameSession.SaveToDb();
        var loop = GameLoop.I;
        var player = loop == null ? null : loop.player;
        var res = GameSession.Net.PlayerShop(new PlayerShopRequest {
            characterId = GameSession.CharacterId, operation = operation, slot = slot,
            inventoryType = inventoryType, itemPosition = itemPosition, perBundle = perBundle,
            bundles = bundles, price = price, permitId = permitItemId, description = description,
            name = name, text = text,
            x = player == null ? 0 : Mathf.RoundToInt(player.transform.position.x * Art.PPU),
            y = player == null ? 0 : Mathf.RoundToInt(-player.transform.position.y * Art.PPU) });
        if (res == null) return null;
        Apply(res);
        return res;
    }

    static void Apply(PlayerShopResponse res)
    {
        shop = res.shop;
        if (res.inventory != null) { GameSession.Character.inventory = res.inventory; GameSession.ApplyInventoryToProfile(); }
        if (res.meso >= 0) { meso = res.meso; GameSession.Character.record.meso = res.meso; Profile.Data.gold = res.meso; }
        if (res.inventory != null || res.meso >= 0) Profile.Save();
        if (res.shop == null) Reset();
    }

    // ————————————————— 动作 —————————————————

    static List<InventoryEntry> Bag()
    {
        var result = new List<InventoryEntry>();
        var character = GameSession.Character;
        if (character == null) return result;
        foreach (var item in character.inventory)
            if (item.position > 0 && item.inventoryType > 0) result.Add(item);
        result.Sort((a, b) => a.inventoryType != b.inventoryType
            ? a.inventoryType.CompareTo(b.inventoryType) : a.position.CompareTo(b.position));
        return result;
    }

    // ADD_ITEM(0x16)：每份数量 → 份数 → 每份单价，然后扣背包。
    static void Stock()
    {
        var list = Bag();
        if (bagSelection < 0 || bagSelection >= list.Count) return;
        var item = list[bagSelection];
        bool rechargeable = Server.Bot.Trade.TradeItemRules.Rechargeable(item.itemId);
        int perBundle = 1, bundles = 1;
        Action askPrice = () => NativeUtilPrompt.Number("每份卖多少金币？", 1, 1, int.MaxValue, price =>
        {
            Request(PlayerShopOperation.AddItem, inventoryType: item.inventoryType, itemPosition: item.position,
                perBundle: perBundle, bundles: bundles, price: price);
            bagSelection = -1;
        });
        Action askBundles = () => NativeUtilPrompt.Number("一共摆几份？（每格上限 2000 个）", 1, 1,
            Math.Max(1, Math.Min(2000 / Math.Max(1, perBundle), 2000)), value =>
        {
            bundles = value;
            askPrice();
        });
        if (rechargeable || item.quantity <= 1)
        {
            perBundle = rechargeable ? 1 : item.quantity;
            askPrice();
            return;
        }
        NativeUtilPrompt.Number("每份多少个？", 1, 1, item.quantity, value => { perBundle = value; askBundles(); });
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
        // 成交是假人异步买的，隔一会儿拉一次快照（源端是服务端主动推 UPDATE_PLAYERSHOP）。
        if (Time.realtimeSinceStartupAsDouble >= nextRead)
        {
            nextRead = Time.realtimeSinceStartupAsDouble + .5;
            var res = GameSession.Net.PlayerShop(new PlayerShopRequest {
                characterId = GameSession.CharacterId, operation = PlayerShopOperation.Snapshot });
            if (res != null) Apply(res);
        }
        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position, new Vector2(W, H), scale);
        NativeUi.RegisterWindow("NativePlayerShopWindow", new Rect(position.x * scale, position.y * scale, W * scale, H * scale), () => Visible);
        var pointer = Pointer.Pos / scale; var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x, position.y, 500, 22).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, new Vector2(W, H), scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, W, H));
            try
            {
                var p = pointer - position;
                Image("backgrnd", 0, 0);
                GUI.Label(new Rect(23, 9, 460, 16), string.IsNullOrEmpty(shop.description) ? "个人商店" : shop.description, Text());
                GUI.Label(new Rect(9, 135, 96, 16), shop.ownerName, Text(false, TextAnchor.UpperCenter));
                if (NativeUi.Button(new Rect(144, 110, 60, 19), "UI/UIWindow.img/PersonalShop/BtExit", p)) { Close(); return; }

                // 左列：货架（16 格，可视 5 行）
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
                    if (!slot.exists || slot.bundles < 1) Image("soldout", 7, y);
                    if (Hit(new Rect(7, y, 199, 35), p)) shopSelection = index;
                }

                // 右列：背包 / 已售出 / 聊天
                for (int index = 0; index < pages.Length; index++)
                    if (GUI.Button(new Rect(236 + 90 * index, 133, 84, 18), pages[index], Small()) && page != index)
                    { page = index; bagSelection = -1; }

                if (page == 0)
                {
                    var list = Bag();
                    bagScroll = NativeUi.Scroll4(new Rect(508, 158, 15, 197), new Rect(236, 157, 270, 200), bagScroll,
                        Math.Max(0, list.Count - 10), 1, p);
                    for (int row = 0; row < 10; row++)
                    {
                        int index = row + (int)bagScroll;
                        if (index >= list.Count) break;
                        var item = list[index];
                        float y = 157 + 20 * row;
                        if (index == bagSelection) GUI.Box(new Rect(236, y, 270, 19), "");
                        Icon(item.itemId, 246, y + 16);
                        GUI.Label(new Rect(262, y + 2, 240, 14), ItemCatalog.Name(item.itemId)
                            + (item.quantity > 1 ? " x" + item.quantity : ""), Text());
                        if (Hit(new Rect(236, y, 270, 19), p)) bagSelection = index;
                    }
                }
                else if (page == 1)
                {
                    GUI.Label(new Rect(236, 157, 270, 14), "买家 / 物品 / 实收 " + shop.sold.Count + " 笔", Text());
                    for (int row = 0; row < 9 && row < shop.sold.Count; row++)
                    {
                        var line = shop.sold[shop.sold.Count - 1 - row];
                        float y = 175 + 20 * row;
                        GUI.Label(new Rect(236, y, 74, 14), line.buyer, Text());
                        GUI.Label(new Rect(310, y, 130, 14), ItemCatalog.Name(line.itemId) + " x" + line.quantity, Text());
                        GUI.Label(new Rect(440, y, 66, 14), line.meso.ToString("N0"), Text(true));
                    }
                }
                else
                {
                    for (int row = 0; row < 9; row++)
                    {
                        int index = shop.chat.Count - 9 + row;
                        if (index < 0) continue;
                        GUI.Label(new Rect(236, 175 + 14 * row, 270, 13), shop.chat[index], Text());
                    }
                    GUI.Label(new Rect(236, 306, 270, 13), "店铺聊天（单机没有其他真人）", Text());
                    chatInput = GUI.TextField(new Rect(238, 323, 231, 15), chatInput, 40);
                    if (GUI.Button(new Rect(472, 321, 40, 18), "说", Small()) && chatInput.Length > 0)
                    {
                        var text = chatInput; chatInput = "";
                        Request(PlayerShopOperation.Chat, text: text);
                    }
                }

                // 店主按钮
                float by = 366;
                bool editing = !shop.open;
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && editing;
                if (GUI.Button(new Rect(7, by, 64, 20), "上架", Small())) Stock();
                if (GUI.Button(new Rect(75, by, 64, 20), "取回", Small()) && shopSelection >= 0)
                {
                    Request(PlayerShopOperation.TakeItemBack, slot: shop.items[shopSelection].slot);
                    shopSelection = -1;
                }
                GUI.enabled = wasEnabled;
                if (editing && GUI.Button(new Rect(143, by, 78, 20), "开始营业", Small())) Request(PlayerShopOperation.Open);
                if (!editing) GUI.Label(new Rect(143, by + 3, 78, 16), "营业中", Text(false, TextAnchor.UpperCenter));
                if (GUI.Button(new Rect(225, by, 78, 20), "拉黑", Small()))
                    NativeUtilPrompt.Text("把谁请出店铺？", "", 1, 13, name => Request(PlayerShopOperation.Ban, name: name));
                if (GUI.Button(new Rect(307, by, 78, 20), "关闭店铺", Small())) Close();
                GUI.Label(new Rect(7, by + 24, 400, 14),
                    "金币 " + meso.ToString("N0") + (editing ? "（准备中，可自由上下架）" : "（营业中，货架锁定）"), Text());
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }
}
