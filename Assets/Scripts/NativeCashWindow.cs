using System;
using System.Collections.Generic;
using UnityEngine;

public static class NativeCashWindow {
    static CashResponse state;
    static Vector2 catalogScroll, stockScroll;
    static string filter = "", target = "", message = "", birthday = "20050511";
    static int selected, currency, tab, page;
    static float previousTimeScale = 1;
    static readonly int[] currencies = { 1, 2, 4 };
    public static void Toggle() {
        if (ShopWindow.Visible) { Request(CashOperation.Close); Reset(); return; }
        var result = Request(CashOperation.Open); ShopWindow.Visible = result != null && result.ok;
        if (ShopWindow.Visible) { previousTimeScale = Time.timeScale; Time.timeScale = 0; }
    }
    public static void Reset() { if (ShopWindow.Visible && Time.timeScale == 0) Time.timeScale = previousTimeScale; ShopWindow.Visible = false; state = null; }
    static CashResponse Request(CashOperation operation, long itemId = 0, int inventory = 0, int[] wishes = null) {
        if (!GameSession.InGame) { BottomBar.Toast("请先选择角色"); return null; }
        if (GameSession.Server.Cash.Catalog == null) GameSession.Server.Cash.Catalog = new NativeCashCatalog();
        GameSession.SaveToDb(); int.TryParse(birthday, out int date);
        state = GameSession.Net.Cash(new CashRequest { characterId = GameSession.CharacterId, operation = operation,
            sn = selected, itemId = itemId, currency = currencies[currency], target = target, message = message, birthday = date, inventoryType = inventory, wishes = wishes });
        SyncInventory(state.character);
        if (!state.ok) BottomBar.Toast(state.error);
        return state;
    }
    // Apply deltas only: unrelated ground loot and quest progress are not overwritten by opening CS.
    public static void SyncInventory(FullCharacter full) {
        if (full == null || GameSession.Character == null) return;
        var counts = new Dictionary<int, int>();
        foreach (var item in GameSession.Character.inventory) if (item.position > 0) { counts.TryGetValue(item.itemId, out int n); counts[item.itemId] = n - item.quantity; }
        foreach (var item in full.inventory) if (item.position > 0) { counts.TryGetValue(item.itemId, out int n); counts[item.itemId] = n + item.quantity; }
        foreach (var pair in counts) { if (pair.Value > 0) Profile.Data.AddItem(pair.Key, pair.Value); else if (pair.Value < 0) Profile.Data.RemoveItem(pair.Key, Math.Min(Profile.ItemCount(pair.Key), -pair.Value)); }
        foreach (int slot in EquipSlot.Worn) {
            int previous = 0, next = 0;
            foreach (var it in GameSession.Character.inventory) if (it.position == slot && it.inventoryType == 1) previous = it.itemId;
            foreach (var it in full.inventory) if (it.position == slot && it.inventoryType == 1) next = it.itemId;
            if (previous != next) Profile.Data.SetEquipSlot(slot, next);
        }
        GameSession.Character.inventory = full.inventory;
        GameSession.Character.record = full.record;
        Profile.Data.gold = full.record.meso; Profile.Data.equipmentSlots = full.record.equipslots; Profile.Save();
    }
    static float nextExpiryCheck;
    public static void Tick() {
        if (!GameSession.InGame || GameSession.Character == null || Time.unscaledTime < nextExpiryCheck) return;
        nextExpiryCheck = Time.unscaledTime + 1;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (GameSession.Character.inventory.Exists(x => x.expiration > 0 && x.expiration <= now)) {
            var refreshed = GameSession.Net.Cash(new CashRequest { characterId = GameSession.CharacterId });
            SyncInventory(refreshed.character);
            if (ShopWindow.Visible) state = refreshed;
        }
    }
    public static void Draw() {
        if (!ShopWindow.Visible || state == null) return;
        var rect = new Rect(8, 8, UiKit.W - 16, UiKit.H - 16);
        GUI.Box(rect, "");
        var old = GUI.skin.font; GUI.skin.font = UiKit.Font;
        GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20));
        GUILayout.BeginHorizontal(); GUILayout.Label("现金商城   点券 " + state.credit + "   抵用券 " + state.points + "   预付点券 " + state.prepaid);
        if (GUILayout.Button("离开商城", GUILayout.Width(100))) Toggle(); GUILayout.EndHorizontal();
        currency = GUILayout.Toolbar(currency, new[] { "点券", "抵用券", "预付点券" });
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(rect.width * 0.52f));
        filter = GUILayout.TextField(filter, 50);
        catalogScroll = GUILayout.BeginScrollView(catalogScroll);
        int matched = 0;
        GUILayout.BeginHorizontal(); if (GUILayout.Button("上一页")) page = Mathf.Max(0, page - 1); GUILayout.Label("第 " + (page + 1) + " 页"); if (GUILayout.Button("下一页")) page++; GUILayout.EndHorizontal();
        foreach (var entry in state.catalog) {
            string label = WzNames.Item(entry.itemId); if (filter.Length > 0 && label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && !entry.itemId.ToString().Contains(filter) && !entry.sn.ToString().Contains(filter)) continue;
            int index = matched++; if (index < page * 12 || index >= (page + 1) * 12) continue;
            if (GUILayout.Button((selected == entry.sn ? "▶ " : "") + label + " ×" + entry.count + "  " + entry.price + "点  " + (entry.period == -1 ? "永久" : entry.period + "天"))) selected = entry.sn;
        }
        if (page > 0 && page * 12 >= matched) page = Mathf.Max(0, (matched - 1) / 12);
        GUILayout.EndScrollView(); GUILayout.EndVertical();
        GUILayout.BeginVertical();
        CashCommodity chosen = state.catalog.Find(x => x.sn == selected);
        if (chosen != null) {
            GUILayout.Label(WzNames.Item(chosen.itemId) + "  SN " + chosen.sn);
            var icon = NativeItemSource.Icon(chosen.itemId); if (icon != null) GUILayout.Label(icon, GUILayout.Width(48), GUILayout.Height(48));
            GUILayout.BeginHorizontal(); if (GUILayout.Button("购买")) Request(CashOperation.Buy); if (GUILayout.Button("角色栏扩充")) Request(CashOperation.ExpandCharacters); GUILayout.EndHorizontal();
            if (GUILayout.Button(state.wishes.Contains(selected) ? "移出愿望清单" : "加入愿望清单")) { var wishes = new List<int>(state.wishes); if (!wishes.Remove(selected)) wishes.Add(selected); Request(CashOperation.WishList, 0, 0, wishes.ToArray()); }
            GUILayout.Label("赠送：角色名 / 赠言 / 账号生日YYYYMMDD"); target = GUILayout.TextField(target, 13); message = GUILayout.TextField(message, 73); birthday = GUILayout.TextField(birthday, 8);
            if (GUILayout.Button("赠送（使用预付点券）")) Request(CashOperation.Gift);
        }
        if (state.gifts > 0 && GUILayout.Button("领取礼物 " + state.gifts + " 份")) Request(CashOperation.ClaimGifts);
        GUILayout.BeginHorizontal(); for (int type = 1; type <= 4; type++) { int t = type; if (GUILayout.Button(new[] { "", "装备+4", "消耗+4", "设置+4", "其他+4" }[type])) Request(CashOperation.ExpandInventory, 0, t); } GUILayout.EndHorizontal();
        tab = GUILayout.Toolbar(tab, new[] { "现金仓库", "角色持有", "愿望清单" });
        stockScroll = GUILayout.BeginScrollView(stockScroll);
        if (tab == 2) { foreach (int sn in state.wishes) if (GUILayout.Button("SN " + sn)) selected = sn; }
        else foreach (var item in state.stock.ToArray()) {
            if (tab == 0 ? item.characterId != 0 : item.characterId == 0) continue;
            GUILayout.Label(WzNames.Item(item.itemId) + " ×" + item.quantity + (item.expiration < 0 ? " 永久" : " 到期 " + DateTimeOffset.FromUnixTimeMilliseconds(item.expiration).LocalDateTime.ToString("MM-dd HH:mm")) + (string.IsNullOrEmpty(item.giftFrom) ? "" : " 赠自 " + item.giftFrom));
            GUILayout.BeginHorizontal(); if (GUILayout.Button(tab == 0 ? "取出" : "存入")) Request(tab == 0 ? CashOperation.Withdraw : CashOperation.Deposit, item.id);
            if (tab == 1 && item.itemId / 1000000 == 1) { bool equipped = state.character.inventory.Exists(x => x.itemId == item.itemId && x.position < 0); if (GUILayout.Button(equipped ? "卸下" : "装备")) Request(equipped ? CashOperation.Unequip : CashOperation.Equip, item.id); }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView(); GUILayout.EndVertical(); GUILayout.EndHorizontal();
        GUILayout.EndArea(); GUI.skin.font = old;
    }
}
