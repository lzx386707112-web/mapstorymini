using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class NativeCashWindow {
    static CashResponse state;

    static string target = "", message = "", messageSecond = "", birthday = "";
    static int selected, currency, page;
    static float previousTimeScale = 1;

    static Vector2 pointer;
    static int category = 1, subcategory, lockerRow, inventoryRow, categoryGroup, pageGroup;
    static bool giftVisible, purchaseVisible, birthdayVisible;
    static int purchaseMask;
    static int previewBackground;
    static bool previewEnabled = true;
    static bool previewUndressed;
    static int previewItem;
    static long stockSelection;
    static bool stockSelectionLocker;
    static float stockClickAt;
    static readonly Dictionary<string, Sprite> previewFrames = new Dictionary<string, Sprite>();
    const string CashRoot = "UI/CashShop.img/";
    static readonly int[] currencies = { 1, 2, 4 };
    public static void Toggle() {
        if (ShopWindow.Visible) { Request(CashOperation.Close); Reset(); return; }
        var result = Request(CashOperation.Open); ShopWindow.Visible = result != null && result.ok;
        if (ShopWindow.Visible) { category = 1; subcategory = page = lockerRow = inventoryRow = 0; previousTimeScale = Time.timeScale; Time.timeScale = 0; }
    }
    public static void Reset() { if (ShopWindow.Visible && Time.timeScale == 0) Time.timeScale = previousTimeScale; ShopWindow.Visible = false; state = null; giftVisible = purchaseVisible = birthdayVisible = false; }
    static CashResponse Request(CashOperation operation, long itemId = 0, int inventory = 0, int[] wishes = null) {
        if (!GameSession.InGame) { BottomBar.Toast("请先选择角色"); return null; }
        // 目录解析在 OnGUI 里做，解析失败不能让整个 HUD 每帧抛异常。
        if (GameSession.Server.Cash.Catalog == null) {
            try { GameSession.Server.Cash.Catalog = new NativeCashCatalog(); }
            catch (Exception e) { BottomBar.Toast("商城目录解析失败：" + e.Message); return null; }
        }
        GameSession.SaveToDb(); int.TryParse(birthday, out int date);
        state = GameSession.Net.Cash(new CashRequest { characterId = GameSession.CharacterId, operation = operation,
            sn = selected, itemId = itemId, currency = currencies[currency], target = target, message = message + (messageSecond.Length > 0 ? "\r\n" + messageSecond : ""), birthday = date, inventoryType = inventory, wishes = wishes });
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
    // BeiDou.exe 004B965F: ten cells, two columns, 206px / 81px strides.
    // Button coordinates: 004BA74B, 004BA7F1, 004BA8EB.
    public static Rect CatalogCell(int index) => new Rect(index % 2 * 206, index / 2 * 81 + 2, 200, 80);
    // 004B8A5A → 0x988690 逐位倒序每 3 位插入 0x2C 逗号，之后拼货币串（571=" NX"，5639="meso"）；
    // 004B8B1A 起商品数量 >1 时再追加 "(%d)"（572）。
    static string PriceText(CashCommodity entry) {
        string text = entry.price.ToString("N0", CultureInfo.InvariantCulture) +
            (entry.itemId >= 80000000 && entry.itemId < 90000000 ? " 金币" : " 点券");
        return entry.count > 1 ? text + "(" + entry.count + ")" : text;
    }
    static void DrawCatalog(Vector2 origin, List<CashCommodity> entries) {
        const string root = "UI/CashShop.img/CSList/";
        // Pointer has already been transformed into the native 800x600 scene.
        var label = new GUIStyle(GUI.skin.label) { font = UiKit.Font, fontSize = 12,
            padding = new RectOffset(), clipping = TextClipping.Clip };
        label.normal.textColor = Color.black;
        for (int i = 0; i < 10 && page * 10 + i < entries.Count; i++) {
            var entry = entries[page * 10 + i];
            var cell = CatalogCell(i); cell.position += origin;
            var background = Art.Tex("wz://" + root + "Base");
            if (background != null) GUI.DrawTexture(cell, background);
            // Name at +82,+6 (width 112, 004B81CC..004B81E1); icon bottom-left baseline at +7,+73 (004B8066).
            GUI.Label(new Rect(cell.x + 82, cell.y + 6, 112, 23), WzNames.Item(entry.itemId), label);
            var icon = NativeItemSource.Icon(entry.itemId);
            // 原端把 32px 道具图标按 2 倍点阵贴进 68x70 图标白框，左下角对齐 (7,73)。
            if (icon != null) GUI.DrawTexture(new Rect(cell.x + 7, cell.y + 73 - icon.height * 2,
                icon.width * 2, icon.height * 2), icon);
            if (Hit(new Rect(cell.x,cell.y,78,80))) previewItem = entry.itemId;
            GUI.Label(new Rect(cell.x + 82, cell.y + 31, 112, 18), PriceText(entry), label);
            if (NativeUi.Button(new Rect(cell.x + 80, cell.y + 53, 37, 19), root + "BtBuy", pointer)) {
                selected = entry.sn;
                purchaseMask = (state.credit >= entry.price ? 1 : 0) | (state.points >= entry.price ? 2 : 0) | (state.prepaid >= entry.price ? 4 : 0);
                if (entry.price == 0) purchaseMask = 2;
                if (purchaseMask == 0) BottomBar.Toast("点券不足");
                else { currency = purchaseMask == 2 ? 1 : purchaseMask == 4 ? 2 : 0; purchaseVisible = true; }
            }
            if (NativeUi.Button(new Rect(cell.x + 119, cell.y + 53, 37, 19), root + "BtGift", pointer))
                 { selected = entry.sn; giftVisible = true; }
            bool wished = state.wishes.Contains(entry.sn);
            if (NativeUi.Button(new Rect(cell.x + 158, cell.y + 53, 37, 19), root + (wished ? "BtRemove" : "BtReserve"), pointer)) {
                selected = entry.sn;
                var wishes = new List<int>(state.wishes);
                if (!wishes.Remove(selected)) wishes.Add(selected);
                Request(CashOperation.WishList, 0, 0, wishes.ToArray());
            }
        }
    }
    // The original is a full scene, not a floating window over the Field HUD.
    // Only this outer transform adapts the fixed 800x600 layout to a mobile viewport.
    public static Rect SceneRect(float width, float height) {
        float scale = Mathf.Min(width / 800f, height / 600f);
        return new Rect((width - 800f * scale) / 2f, (height - 600f * scale) / 2f, 800f * scale, 600f * scale);
    }
    public static string BackgroundNode(int job) => CashRoot + "Base/backgrnd" +
        (job / 1000 == 1 ? "1" : job / 100 == 21 || job == 2000 ? "2" : "");
    public static Rect LockerCell(int index) => new Rect(20 + index % 6 * 35, 348 + index / 6 * 35, 32, 32);
    public static Rect InventoryCell(int index) => new Rect(22 + index % 4 * 35, 481 + index / 4 * 35, 32, 32);
    static void Image(string node, float x, float y) {
        var texture = Art.Tex("wz://" + CashRoot + node);
        if (texture != null) GUI.DrawTexture(new Rect(x, y, texture.width, texture.height), texture);
    }
    static bool Button(string node, float x, float y) {
        var texture = Art.Tex("wz://" + CashRoot + node + "/normal/0");
        return texture != null && NativeUi.Button(new Rect(x, y, texture.width, texture.height), CashRoot + node, pointer);
    }
    static GUIStyle TextStyle(bool right = false) {
        var style = new GUIStyle { font = UiKit.Font, fontSize = 12, clipping = TextClipping.Clip,
            alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft };
        style.normal.textColor = Color.black;
        return style;
    }
    static bool Hit(Rect rect) {
        if (!GUI.enabled || Event.current.type != EventType.Repaint || !Pointer.Clicked || !rect.Contains(pointer)) return false;
        Pointer.ConsumeClick();
        AudioManager.Play("UI/BtMouseClick", .7f, false);
        return true;
    }
    public static void Draw() {
        if (!ShopWindow.Visible || state == null) return;
        var previous = GUI.matrix;
        Rect scene = SceneRect(UiKit.W, UiKit.H);
        float scale = scene.width / 800f;
        pointer = (Pointer.Pos - scene.position) / scale;
        // Presentation bars belong to aspect fitting, outside the original game canvas.
        UiKit.Fill(new Rect(0,0,scene.x,UiKit.H),Color.black);
        UiKit.Fill(new Rect(scene.xMax,0,UiKit.W - scene.xMax,UiKit.H),Color.black);
        UiKit.Fill(new Rect(0,0,UiKit.W,scene.y),Color.black);
        UiKit.Fill(new Rect(0,scene.yMax,UiKit.W,UiKit.H - scene.yMax),Color.black);
        GUI.matrix = previous * Matrix4x4.TRS(scene.position, Quaternion.identity, new Vector3(scale, scale, 1));
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && !giftVisible && !purchaseVisible;
        Image(BackgroundNode(state.character != null ? state.character.record.job : 0).Substring(CashRoot.Length), 0, 0);
        DrawPreview();
        DrawCategories();
        var entries = state.catalog.FindAll(x => category == 9 ? state.wishes.Contains(x.sn) :
            x.sn / 10000000 == category && x.sn / 100000 % 100 == subcategory);
        page = Mathf.Clamp(page, 0, Mathf.Max(0, (entries.Count - 1) / 10));
        DrawCatalog(new Vector2(275, 95), entries);
        // Native page selector occupies the strip below the ten commodity cells.
        DrawPages(entries.Count);
        DrawStorage();
        DrawBalances();
        bool exit = Button("CSStatus/BtExit",632,545);
        GUI.enabled = enabled;
        if (giftVisible) { GUI.enabled = enabled && !birthdayVisible; DrawGift(); GUI.enabled = enabled; }
        if (birthdayVisible) DrawBirthday();
        if (purchaseVisible) DrawPurchase();
        GUI.matrix = previous;
        if (exit) Toggle();
    }
    static void DrawCategories() {
        // 004B684B maps category 1..7 to Tab/2..8; help(8) to 1; wishes(9) to 9.
        int bitmap = category == 8 ? 1 : category == 9 ? 9 : category + 1;
        Image("CSTab/Tab/" + bitmap, 274, 17);
        int[] starts = { 57,69,107,119,157,169,209,220,260,272,311,323,362,374,3,3,451,451 };
        for (int c = 1; c <= 9; c++) {
            if (c == category) continue;
            int x = starts[2 * (c - 1) + (c < category ? 0 : 1)];
            if (Hit(new Rect(272 + x, 39, 51, 31))) { category = c; subcategory = page = categoryGroup = pageGroup = 0; }
        }
        var nodes = WzAssets.FileRoot("Etc/Category.img");
        var labels = new List<string>(); var ids = new List<int>();
        foreach (var node in nodes.Children) if (node.Int("Category") == category) {
            labels.Add(node.Text("Name")); ids.Add(node.Int("CategorySub"));
        }
        int chosen = DrawTextStrip(labels, ids.IndexOf(subcategory), 280,76,485, CategoryColor(category), category >= 5 && category <= 7 || category == 9, 20, ref categoryGroup);
        if (chosen >= 0) { subcategory = ids[chosen]; page = 0; }
    }
    // 004D96FA / 004D9D9F: text-based horizontal selector (not CCtrlComboBox).
    // The original also draws this 14px strip with FillRect and font glyphs.
    static int DrawTextStrip(List<string> labels, int selectedIndex, float x, float y, float width,
        int background, bool whiteText, int perGroup, ref int group) {
        UiKit.Fill(new Rect(x,y,width,14), new Color32((byte)(background >> 16),(byte)(background >> 8),(byte)background,255));
        group = Mathf.Clamp(group, 0, Mathf.Max(0,(labels.Count - 1) / perGroup));
        int start = group * perGroup, end = Mathf.Min(labels.Count,start + perGroup);
        var spans = new List<string>(); var indices = new List<int>();
        if (start > 0) { spans.Add("◀"); indices.Add(-2); }
        for (int i = start; i < end; i++) {
            if (i > start) { spans.Add(" |"); indices.Add(-3); }
            spans.Add(labels[i]); indices.Add(i);
        }
        if (end < labels.Count) { spans.Add(" ▶"); indices.Add(-1); }
        var style = TextStyle();
        float total = 0;
        foreach (string text in spans) total += style.CalcSize(new GUIContent(text)).x;
        float offset = (width - total) / 2; int result = -1;
        for (int i = 0; i < spans.Count; i++) {
            int index = indices[i];
            bool selectedLabel = index >= 0 && index == selectedIndex;
            style.normal.textColor = whiteText != selectedLabel ? Color.white : Color.black;
            float w = style.CalcSize(new GUIContent(spans[i])).x;
            var rect = new Rect(x + offset,y - 1,w,14);
            GUI.Label(rect,spans[i],style);
            if (index != -3 && Hit(rect)) {
                if (index == -2) group--;
                else if (index == -1) group++;
                else result = index;
            }
            offset += w;
        }
        return result;
    }
    static int CategoryColor(int value) {
        int[] colors = { 0,-7353517,-1874292,-12338980,-1664967,-11711023,-3381933,-7127089,-12346660,-2668626 };
        return colors[value];
    }
    static void DrawPages(int count) {
        // 004B96B5: native horizontal selector, one-based page labels, ten per group.
        var labels = new List<string>();
        for (int i = 0; i < Mathf.CeilToInt(count / 10f); i++) labels.Add((i + 1).ToString());
        int chosen = DrawTextStrip(labels, page, 279, 507, 406, unchecked((int)0xff3a67aa), true, 10, ref pageGroup);
        if (chosen >= 0) page = chosen;
        if (count <= 10) return;
        if (Event.current.type == EventType.ScrollWheel && new Rect(275,95,412,430).Contains(pointer)) {
            page = Mathf.Clamp(page + (Event.current.delta.y > 0 ? 1 : -1), 0, (count - 1) / 10);
            Event.current.Use();
        }
    }
    static void DrawStorage() {
        var locker = state.stock.FindAll(x => x.characterId == 0);
        var held = state.stock.FindAll(x => x.characterId != 0);
        lockerRow = Mathf.Clamp(lockerRow, 0, Mathf.Max(0, (locker.Count - 1) / 6 - 1));
        inventoryRow = Mathf.Clamp(inventoryRow, 0, Mathf.Max(0, (held.Count - 1) / 4 - 2));
        for (int i = 0; i < 12; i++) {
            int li = i + lockerRow * 6, hi = i + inventoryRow * 4;
            if (li < locker.Count) DrawStock(locker[li], LockerCell(i), true);
            if (hi < held.Count) DrawStock(held[hi], InventoryCell(i), false);
        }
        string[] expansion = { "BtExEquip", "BtExConsume", "BtExInstall", "BtExEtc", "BtExTrunk" };
        for (int i = 0; i < expansion.Length; i++)
            if (Button("CSInventory/" + expansion[i], 176, 453 + 27 * i)) {
                if (i < 4) Request(CashOperation.ExpandInventory, 0, i + 1);
                else BottomBar.Toast("当前服务端尚未接入仓库扩充");
            }
        if (Event.current.type == EventType.ScrollWheel) {
            if (new Rect(0,318,256,104).Contains(pointer)) { lockerRow += Event.current.delta.y > 0 ? 1 : -1; Event.current.Use(); }
            else if (new Rect(0,426,170,163).Contains(pointer)) { inventoryRow += Event.current.delta.y > 0 ? 1 : -1; Event.current.Use(); }
        }
    }
    static void DrawStock(CashStock item, Rect rect, bool locker) {
        var icon = NativeItemSource.Icon(item.itemId);
        if (icon != null) GUI.DrawTexture(new Rect(rect.x, rect.yMax - icon.height, icon.width, icon.height), icon);
        if (item.quantity > 1) GUI.Label(new Rect(rect.x,rect.yMax - 12,32,12), item.quantity.ToString(), TextStyle(true));
        if (stockSelection == item.id && stockSelectionLocker == locker) {
            // 004B0116 / 004B1989: selected native 32px cell and 2px border.
            UiKit.Fill(rect,new Color32(16,48,204,64));
            var border = new Color32(16,48,204,170);
            UiKit.Fill(new Rect(rect.x,rect.y - 1,32,2),border);
            UiKit.Fill(new Rect(rect.x,rect.yMax - 1,32,2),border);
            UiKit.Fill(new Rect(rect.x - 1,rect.y,2,32),border);
            UiKit.Fill(new Rect(rect.xMax - 1,rect.y,2,32),border);
        }
        if (Hit(rect)) {
            bool twice = stockSelection == item.id && stockSelectionLocker == locker && Time.unscaledTime - stockClickAt < .4f;
            stockSelection = item.id; stockSelectionLocker = locker; stockClickAt = Time.unscaledTime;
            if (twice) Request(locker ? CashOperation.Withdraw : CashOperation.Deposit,item.id);
        }
    }
    static void DrawPreview() {
        Image("Base/Preview/" + previewBackground,24,40);
        if (Hit(new Rect(24,40,212,165)) || Hit(new Rect(95,5,30,25))) previewEnabled = !previewEnabled;
        if (state.character != null && previewEnabled) {
            // 合成走的是 OnGUI，单个坏道具不能把整个商城 UI 拖崩。
            try {
                var appearance = new List<InventoryEntry>(state.character.inventory);
                if (previewUndressed) appearance.Clear();
                if (previewItem / 1000000 == 1) {
                    int slot = EquipSlot.SlotOf(previewItem);
                    appearance.RemoveAll(x => x.position == slot && x.inventoryType == InventoryDao.Equipped);
                    appearance.Add(new InventoryEntry { itemId=previewItem,position=slot,inventoryType=InventoryDao.Equipped });
                }
                var ids = WzCharacter.CharacterIds(state.character.record,appearance);
                int frame = WzCharacter.AppearanceFrame(ids,Time.unscaledTime);
                string key = string.Join("_",ids) + "/" + frame;
                if (!previewFrames.TryGetValue(key,out var sprite)) {
                    sprite = WzCharacter.Appearance(string.Join("_",ids),ids,frame); previewFrames[key] = sprite;
                }
                if (sprite != null) {
                    GUI.BeginGroup(new Rect(24,40,212,165));
                    GUI.DrawTexture(new Rect(130 - sprite.pivot.x,125 - (sprite.rect.height - sprite.pivot.y),
                        sprite.rect.width,sprite.rect.height),sprite.texture);
                    GUI.EndGroup();
                }
            }
            catch (Exception) { }
        }
        if (state.character != null) {
            GUI.Label(new Rect(50,268,175,16),state.character.record.name,TextStyle(true));
            GUI.Label(new Rect(50,287,175,16),GameSession.AccountName ?? "-",TextStyle(true));
        }
        bool enabled = GUI.enabled;
        GUI.enabled = false;
        Button("CSChar/BtBuyAvatar",16,237);
        GUI.enabled = enabled;
        if (Button("CSChar/BtDefaultAvatar",101,237)) { previewItem=0; previewUndressed=false; }
        if (Button("CSChar/BtTakeoffAvatar",187,237)) { previewItem=0; previewUndressed=true; }
    }
    static void DrawPurchase() {
        // 0046B537 + 0046ABD2: mode 0, one commodity => 286x196 centered.
        var origin = new Vector2(257,202);
        Image("CSNotice/0/0",origin.x,origin.y);
        Image("CSNotice/0/1",origin.x,origin.y + 21);
        Image("CSNotice/0/2",origin.x,origin.y + 61);
        Image("CSNotice/0/3",origin.x,origin.y + 66);
        Image("CSNotice/0/4",origin.x,origin.y + 85);
        var entry = state.catalog.Find(x => x.sn == selected);
        if (entry == null) { purchaseVisible = false; return; }
        var icon = NativeItemSource.Icon(entry.itemId);
        if (icon != null) GUI.DrawTexture(new Rect(origin.x + 21,origin.y + 57 - icon.height,icon.width,icon.height),icon);
        GUI.Label(new Rect(origin.x + 63,origin.y + 27,202,16),WzNames.Item(entry.itemId),TextStyle());
        GUI.Label(new Rect(origin.x + 63,origin.y + 43,202,16),PriceText(entry),TextStyle());
        var center = TextStyle(); center.alignment = TextAnchor.UpperCenter;
        GUI.Label(new Rect(origin.x,origin.y + 73,286,16),"确定要购买这个道具吗？",center);
        // 00469FDF: points/prepaid/credit controls at x25, height-95/-80/-65.
        int[] order = { 1,2,0 };
        for (int i = 0; i < order.Length; i++) {
            int c = order[i];
            bool available = (purchaseMask & currencies[c]) != 0;
            var rect = new Rect(origin.x + 25,origin.y + 101 + i * 15,12,12);
            var texture = Art.Tex("wz://UI/Basic.img/CheckBox/" + (!available ? "2" : c == currency ? "1" : "0"));
            if (texture != null) GUI.DrawTexture(rect,texture);
            if (available && Hit(new Rect(rect.x,rect.y,230,14))) currency = c;
        }
        if (NativeUi.Button(new Rect(origin.x + 157,origin.y + 159,47,19),"UI/Basic.img/BtOK2",pointer)) {
            var response = Request(CashOperation.Buy);
            if (response != null && response.ok) purchaseVisible = false;
        }
        if (NativeUi.Button(new Rect(origin.x + 207,origin.y + 159,47,19),"UI/Basic.img/BtCancel2",pointer)) purchaseVisible = false;
    }
    static void DrawGift() {
        // 007E23A9 / 007E2717: collapsed recipient/message dialog, 266x169.
        var origin = new Vector2(267,215);
        Image("CSGift/backgrnd",origin.x,origin.y);
        var field = TextStyle();
        target = GUI.TextField(new Rect(origin.x + 84,origin.y + 52,122,13),target,13,field);
        message = GUI.TextField(new Rect(origin.x + 26,origin.y + 76,210,13),message,34,field);
        messageSecond = GUI.TextField(new Rect(origin.x + 26,origin.y + 92,210,13),messageSecond,34,field);
        bool enabled = GUI.enabled;
        GUI.enabled = false;
        NativeUi.Button(new Rect(origin.x + 56,origin.y + 139,53,18),CashRoot + "CSGift/BtGuild",pointer);
        NativeUi.Button(new Rect(origin.x + 110,origin.y + 139,53,18),CashRoot + "CSGift/BtBuddy",pointer);
        GUI.enabled = enabled;
        if (NativeUi.Button(new Rect(origin.x + 168,origin.y + 139,47,19),"UI/Basic.img/BtOK2",pointer)) {
            birthday = ""; birthdayVisible = true;
        }
        if (NativeUi.Button(new Rect(origin.x + 210,origin.y + 139,47,19),"UI/Basic.img/BtCancel2",pointer)) giftVisible = false;
    }
    static void DrawBirthday() {
        // 009ACF95 uses CUtilDlgEx type=3, compact=1 (argument +14), not a third Gift field.
        const string prompt = "For the sake of privacy,\nplease enter your 8-digit birthday code.\nex) year/month/day -> 19820911";
        var style = TextStyle(); style.wordWrap = true;
        // 009A3D07: compact type3 text width225. Font metrics belong to the port.
        float textHeight = Mathf.Ceil(style.CalcHeight(new GUIContent(prompt),225));
        float height = textHeight + 99;
        var origin = new Vector2(267,(600 - height) / 2);
        const string basic = "wz://UI/Basic.img/";
        // 009A4F1D: Notice3 t/c; Notice4 s at height-78, center rows20px.
        var top = Art.Tex(basic + "Notice3/t");
        var middle = Art.Tex(basic + "Notice3/c");
        var bottom = Art.Tex(basic + "Notice4/s");
        if (top != null) GUI.DrawTexture(new Rect(origin.x,origin.y,266,21),top);
        for (float y = 21; y < height - 78; y += 20)
            if (middle != null) GUI.DrawTexture(new Rect(origin.x,origin.y + y,266,20),middle);
        if (bottom != null) GUI.DrawTexture(new Rect(origin.x,origin.y + height - 78,266,78),bottom);
        GUI.Label(new Rect(origin.x + 20,origin.y + 22,225,textHeight),prompt,style);
        // 009A83F6 copies native black/white input rectangles before creating the edit control.
        UiKit.Fill(new Rect(origin.x + 19,origin.y + textHeight + 24,229,18),Color.black);
        UiKit.Fill(new Rect(origin.x + 20,origin.y + textHeight + 25,227,16),Color.white);
        birthday = GUI.TextField(new Rect(origin.x + 22,origin.y + textHeight + 27,224,14),birthday,8,TextStyle());
        if (NativeUi.Button(new Rect(origin.x + 212,origin.y + height - 26,43,19),"UI/UIWindow.img/UtilDlgEx/BtOK",pointer)) {
            if (birthday.Length != 8 || !int.TryParse(birthday,out _)) { BottomBar.Toast("请输入8位生日代码"); return; }
            var response = Request(CashOperation.Gift);
            if (response != null && response.ok) giftVisible = birthdayVisible = false;
        }
        if (NativeUi.Button(new Rect(origin.x + 9,origin.y + height - 26,43,19),"UI/UIWindow.img/UtilDlgEx/BtClose",pointer)) birthdayVisible = false;
    }
    static void DrawBalances() {
        // 004BC713 clears only the native numeric fields, then right-aligns at x220.
        int[] values = { state.credit, state.points, state.prepaid };
        int[] y = { 9,23,38 };
        for (int i = 0; i < 3; i++) {
            UiKit.Fill(new Rect(374,530 + y[i] + 2,107,11), new Color32(243,244,245,255));
            GUI.Label(new Rect(374,530 + y[i],100,14), values[i].ToString("N0"), TextStyle(true));
        }
        if (Button("CSStatus/BtCheck",543,543)) Request(CashOperation.Read);
        bool enabled = GUI.enabled;
        GUI.enabled = false; // External billing/coupon endpoints are not available in the local server.
        Button("CSStatus/BtCharge",502,543); Button("CSStatus/BtCoupon",584,543);
        GUI.enabled = enabled;
    }
}
