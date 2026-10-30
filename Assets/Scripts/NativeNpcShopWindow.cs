using System;
using System.Collections.Generic;
using Server.Bot.Trade;
using UnityEngine;

// CUIShop: 7532CE creates controls; 754E78/75577A draw five 40px rows.
// Ordinary NPC shops deliberately do not share the cash-shop alias ShopWindow.
public static class NativeNpcShopWindow
{
    const string Root = "UI/UIWindow.img/Shop/";
    static NpcShopResponse state;
    static Vector2 position = new Vector2(100, 70), grab;
    static bool dragging;
    static int buySelection = -1, sellSelection = -1, sellTab;
    static float buyScroll, sellScroll;
    static int generation;
    public static bool Visible => state?.opened == true;
    public static bool HasNpc(int id) => GameSession.InGame && GameSession.Net.HasNpcShop(id);
    public static void Reset() { state = null; dragging = false; buySelection = sellSelection = -1; buyScroll = sellScroll = 0; generation++; }
    public static void Close()
    {
        if (Visible && GameSession.InGame) GameSession.Net.NpcShop(new NpcShopRequest { operation = NpcShopOperation.Close, characterId = GameSession.CharacterId });
        Reset();
    }
    public static void OpenNpc(int id) => Open(id, false, false);
    public static void OpenShop(int id, bool fallback) => Open(id, true, fallback);
    static void Open(int id, bool shop, bool fallback)
    {
        if (!GameSession.InGame) return;
        Close(); GameSession.SaveToDb();
        state = GameSession.Net.NpcShop(new NpcShopRequest { characterId = GameSession.CharacterId, id = id, fallback = fallback,
            operation = shop ? NpcShopOperation.OpenShop : NpcShopOperation.OpenNpc });
        sellTab = 0;
        if (!state.ok) { BottomBar.Toast(state.error); Reset(); }
    }
    static void Transact(NpcShopRequest request)
    {
        if (!Visible || !GameSession.InGame) return;
        GameSession.SaveToDb(); request.characterId = GameSession.CharacterId;
        state = GameSession.Net.NpcShop(request);
        if (!state.ok) BottomBar.Toast(state.error);
        if (state.character == null) return;
        GameSession.Character.inventory = state.character.inventory;
        GameSession.Character.record.meso = state.character.record.meso;
        Profile.Data.gold = state.character.record.meso;
        GameSession.ApplyInventoryToProfile(); Profile.Save();
    }
    static List<int> BuyItems()
    {
        var result = new List<int>();
        if (state != null) for (int i = 0; i < state.items.Count; i++)
            if (state.items[i].price > 0 || state.items[i].pitch > 0) result.Add(i);
        return result;
    }
    static List<InventoryEntry> SellItems()
    {
        var result = new List<InventoryEntry>();
        if (GameSession.Character != null) foreach (var item in GameSession.Character.inventory)
        {
            if (item.position <= 0 || item.inventoryType != sellTab + 1) continue;
            var info = NativeItemSource.Node(item.itemId)?.Get("info");
            // CUIShop::SetSellItems excludes cash, no-sale and quest items.
            if (info == null || info.Int("cash") != 0 || info.Int("notSale") != 0 || info.Int("quest") != 0 || item.expiration > 0) continue;
            result.Add(item);
        }
        result.Sort((a, b) => a.position.CompareTo(b.position)); return result;
    }
    public static void BuySelected()
    {
        if (!Visible || buySelection < 0 || buySelection >= state.items.Count) return;
        int index = buySelection, version = generation;
        var stock = state.items[index];
        Action<int> buy = q => { if (version == generation && Visible) Transact(new NpcShopRequest { operation = NpcShopOperation.Buy, index = index, itemId = stock.itemId, quantity = q }); };
        if (stock.itemId / 1000000 == 1 || TradeItemRules.Rechargeable(stock.itemId)) buy(1);
        else NativeUtilPrompt.Number("要购买多少个？", 1, 1, short.MaxValue, buy);
    }
    public static void SellSelected()
    {
        var items = SellItems();
        if (!Visible || sellSelection < 0 || sellSelection >= items.Count) return;
        var item = items[sellSelection]; int version = generation;
        Action<int> sell = q =>
        {
            if (version != generation || !Visible) return;
            Transact(new NpcShopRequest { operation = NpcShopOperation.Sell, inventoryType = item.inventoryType, position = item.position, quantity = q });
            sellSelection = -1;
        };
        if (TradeItemRules.Rechargeable(item.itemId) || item.quantity <= 1)
            NativeUtilPrompt.Confirm("要出售这件道具吗？", yes => { if (yes) sell(1); });
        else NativeUtilPrompt.Number("要出售多少个？", 1, 1, item.quantity, sell);
    }
    static GUIStyle Text(bool right = false)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = 12, padding = new RectOffset(), clipping = TextClipping.Clip,
            alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft };
        style.normal.textColor = Color.black; return style;
    }
    static void Image(string node, float x, float y)
    {
        var texture = Art.Tex("wz://" + node);
        if (texture != null) GUI.DrawTexture(new Rect(x, y, texture.width, texture.height), texture);
    }
    static bool Hit(Rect rect, Vector2 pointer)
    {
        if (!GUI.enabled || Event.current.type != EventType.Repaint || !Pointer.Released || !rect.Contains(pointer)) return false;
        Pointer.ConsumeClick(); return true;
    }
    // CCtrlTab style 2 (4DE15F): 6px ends, 12px joins, selected uses disabled label.
    static int Tabs(string labels, int count, int selected, float x, float width, Vector2 pointer, int fixedWidth = 0)
    {
        float inner = fixedWidth > 0 ? fixedWidth - 8 : Mathf.Floor((width - (count - 1) * 12 - 12) / count);
        Image("UI/Basic.img/Tab2/left" + (selected == 0 ? 1 : 0), x, 95);
        for (int i = 0; i < count; i++)
        {
            float start = x + 6 + i * (inner + 12), end = start + inner;
            var fill = Art.Tex("wz://UI/Basic.img/Tab2/fill" + (selected == i ? 1 : 0));
            if (fill != null) GUI.DrawTexture(new Rect(start, 95, inner, fill.height), fill);
            Image("UI/Basic.img/Tab2/" + (i == count - 1 ? "right" + (selected == i ? 1 : 0) : "middle" + (selected == i ? 1 : selected == i + 1 ? 2 : 0)), end, 95);
            var label = Art.Tex("wz://" + Root + labels + "/" + (selected == i ? "disabled/" : "enabled/") + i);
            if (label != null) GUI.DrawTexture(new Rect(start + (inner - label.width) / 2, 95 + (19 - label.height) / 2 + 2, label.width, label.height), label);
            if (Hit(new Rect(start - 6, 95, inner + 12, 19), pointer)) selected = i;
        }
        return selected;
    }
    static void Icon(int id, float x, float foot)
    {
        var icon = NativeItemSource.IconNode(id); if (icon == null) return;
        var texture = WzAssets.Texture(icon); if (texture == null) return;
        var origin = WzAssets.Origin(icon);
        GUI.DrawTexture(new Rect(x - origin.x, foot - origin.y, texture.width, texture.height), texture);
    }
    static void Portrait()
    {
        var root = WzAssets.FileRoot("Npc/" + state.npcId.ToString("D7") + ".img");
        var seen = new HashSet<int>();
        while (root.Get("info/link") != null)
        {
            int id = root.Int("info/link"); if (!seen.Add(id)) return;
            root = WzAssets.FileRoot("Npc/" + id.ToString("D7") + ".img");
        }
        var frames = WzAssets.Frames(root.Get("stand")); if (frames.Count == 0) return;
        float total = 0; foreach (var frame in frames) total += Math.Max(1, frame.Int("delay", 100));
        float time = Time.unscaledTime * 1000 % total;
        var selected = frames[0];
        foreach (var frame in frames) { selected = frame; time -= Math.Max(1, frame.Int("delay", 100)); if (time < 0) break; }
        var texture = WzAssets.Texture(selected); var origin = WzAssets.Origin(selected);
        if (texture != null) GUI.DrawTexture(new Rect(56 - origin.x, 76 - origin.y, texture.width, texture.height), texture);
    }
    public static void Draw()
    {
        if (!Visible) return;
        if (!GameSession.InGame || Profile.LastMapId != state.mapId) { Close(); return; }
        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position, new Vector2(463, 339), scale);
        var pointer = Pointer.Pos / scale; var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x, position.y, 463, 12).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, new Vector2(463, 339), scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, 463, 339));
            try
            {
                var p = pointer - position;
                Image(Root + "backgrnd", 0, 0); Portrait();
                GUI.Label(new Rect(350, 64, 97, 16), Profile.Data.gold.ToString("N0"), Text(true));
                if (NativeUi.Button(new Rect(142, 15, 70, 19), "UI/UIWindow.img/PersonalShop/BtExit", p)) { Close(); return; }
                if (NativeUi.Button(new Rect(142, 35, 70, 19), Root + "BtBuy", p)) BuySelected();
                if (NativeUi.Button(new Rect(372, 35, 70, 19), Root + "BtSell", p)) SellSelected();
                Tabs("TabBuy", 1, 0, 5, 222, p, 50);
                int tab = Tabs("TabSell", 5, sellTab, 235, 222, p);
                if (tab != sellTab) { sellTab = tab; sellScroll = 0; sellSelection = -1; }
                var buyItems = BuyItems(); var sellItems = SellItems();
                buyScroll = NativeUi.Scroll4(new Rect(210, 127, 15, 194), new Rect(6, 127, 199, 194), buyScroll, Math.Max(0, buyItems.Count - 5), 1, p);
                sellScroll = NativeUi.Scroll4(new Rect(441, 127, 15, 194), new Rect(236, 127, 199, 194), sellScroll, Math.Max(0, sellItems.Count - 5), 1, p);
                for (int row = 0; row < 5; row++)
                {
                    int at = row + (int)buyScroll, y = 127 + row * 40;
                    if (at < buyItems.Count)
                    {
                        int index = buyItems[at]; var item = state.items[index];
                        if (index == buySelection) Image(Root + "select", 43, y);
                        Icon(item.itemId, 8, y + 34);
                        GUI.Label(new Rect(49, y + 1, 152, 16), ItemCatalog.Name(item.itemId), Text());
                        Image(Root + (item.price > 0 ? "meso" : "token"), 49, y + 20);
                        GUI.Label(new Rect(65, y + 19, 137, 16), (item.price > 0 ? item.price : item.pitch).ToString("N0"), Text());
                        var hit = new Rect(6, y, 199, 35);
                        if (hit.Contains(p)) ItemTooltip.SetHover(item.itemId);
                        bool twice = Pointer.DoubleClicked;
                        if (Hit(hit, p)) { buySelection = index; if (twice) BuySelected(); }
                    }
                    at = row + (int)sellScroll;
                    if (at < sellItems.Count)
                    {
                        var item = sellItems[at];
                        if (at == sellSelection) Image(Root + "select", 273, y - 2);
                        Icon(item.itemId, 238, y + 34);
                        GUI.Label(new Rect(279, y - 1, 152, 16), ItemCatalog.Name(item.itemId), Text());
                        state.sellPrices.TryGetValue(item.inventoryType * 1000 + item.position, out int sellPrice);
                        GUI.Label(new Rect(279, y + 19, 122, 16), Math.Max(0, sellPrice).ToString("N0") + " mesos.", Text());
                        if (TradeItemRules.Rechargeable(item.itemId) && NativeUi.Button(new Rect(404, y, 35, 19), Root + "BtRecharge", p))
                            Transact(new NpcShopRequest { operation = NpcShopOperation.Recharge, inventoryType = item.inventoryType, position = item.position });
                        var hit = new Rect(236, y, 165, 35);
                        if (hit.Contains(p)) ItemTooltip.SetHover(item.itemId, false, item);
                        bool twice = Pointer.DoubleClicked;
                        if (Hit(hit, p)) { sellSelection = at; if (twice) SellSelected(); }
                    }
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }
}
