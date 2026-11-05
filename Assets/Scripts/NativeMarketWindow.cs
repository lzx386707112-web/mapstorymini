using System;
using Server.Bot;
using UnityEngine;

// CPersonalShopDlg / inherited CEntrustedShopDlg (6FA4B2 / 6FB99D).
public static class NativeMarketWindow
{
    static MarketResponse state;
    static Vector2 position = new Vector2(100,20), grab;
    static bool dragging;
    static int selection = -1;
    static float scroll;
    static string chatInput = "";
    const string ChatKey = "MarketChat";
    static double nextRead;
    public static bool Visible => state?.visiting != null;
    public static void Reset() { if (NativeEditKeyboard.Owns(ChatKey)) NativeEditKeyboard.Close(); chatInput = ""; state = null; selection = -1; scroll = 0; nextRead = 0; dragging = false; }
    static MarketResponse Request(MarketOperation operation, int owner = 0, int index = 0, int quantity = 0, string text = null)
    {
        if (!GameSession.InGame) return null;
        state = GameSession.Net.Market(new MarketRequest {characterId = GameSession.CharacterId,
            operation = operation, ownerId = owner, itemIndex = index, quantity = quantity, text = text});
        if (!state.ok) NativeSystemMessage.Drop(5,state.error);
        return state;
    }
    public static void Visit(int owner)
    {
        GameSession.SaveToDb();
        var response = Request(MarketOperation.Visit,owner);
        if (response?.visiting != null) { selection = -1; scroll = 0; chatInput = ""; }
    }
    public static void Close() { Request(MarketOperation.Leave); Reset(); }
    static GUIStyle Text(bool right = false)
    {
        var style = new GUIStyle {font = UiKit.Font,fontSize = 12,padding = new RectOffset(),clipping = TextClipping.Clip,
            alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft};
        style.normal.textColor = Color.black; return style;
    }
    static void Image(string name,float x,float y)
    {
        var t = Art.Tex("wz://UI/UIWindow.img/PersonalShop/"+name);
        if (t != null) GUI.DrawTexture(new Rect(x,y,t.width,t.height),t);
    }
    public static void BuySelected()
    {
        var shop = state?.visiting;
        if (shop == null || selection < 0 || selection >= shop.items.Count) return;
        int index = selection, owner = shop.ownerId;
        var stock = shop.items[index];
        if (!stock.exists || stock.bundles <= 0) return;
        NativeUtilPrompt.Number("要购买多少捆？",1,1,Math.Min(stock.bundles,short.MaxValue),quantity =>
        {
            Request(MarketOperation.Buy,owner,index,quantity);
        });
    }
    static void DrawVisitors(MarketShopSnapshot shop)
    {
        foreach (var visitor in shop.visitors)
        {
            if (visitor.slot < 0 || visitor.slot >= 3) continue;
            float x = (visitor.slot+3)*95;
            var character = visitor.id == GameSession.CharacterId ? GameSession.Character : visitor.character;
            if (character != null)
            {
                var ids = WzCharacter.CharacterIds(character.record,character.inventory);
                var sprite = WzCharacter.Appearance("market_"+string.Join("_",ids),ids,
                    WzCharacter.AppearanceFrame(ids,Time.unscaledTime));
                if (sprite != null)
                    GUI.DrawTexture(new Rect(x-sprite.pivot.x,127-sprite.rect.height+sprite.pivot.y,
                        sprite.rect.width,sprite.rect.height),sprite.texture);
            }
            var style = Text(); style.alignment = TextAnchor.UpperCenter; style.normal.textColor = Color.white;
            GUI.Label(new Rect(x-45,135,90,16),visitor.name,style);
        }
    }
    static void SendChat()
    {
        if (string.IsNullOrWhiteSpace(chatInput)) return;
        var response = Request(MarketOperation.Chat,text:chatInput);
        if (response?.ok == true) { chatInput = ""; }
    }
    static void DrawChat(MarketShopSnapshot shop, Vector2 pointer)
    {
        // 原聊天输入框6FA648..6FA65F：(238,323),231×15。
        var signal = NativeEditKeyboard.Sync(ChatKey,ref chatInput);
        if (chatInput.Length > 127) chatInput = chatInput.Substring(0,127);
        GUI.BeginGroup(new Rect(238,159,258,158));
        for (int row = 0; row < 11 && row+Math.Max(0,shop.chat.Count-11) < shop.chat.Count; row++)
            GUI.Label(new Rect(0,row*14,258,14),shop.chat[row+Math.Max(0,shop.chat.Count-11)],Text());
        GUI.EndGroup();
        var inputRect = new Rect(238,323,231,15);
        GUI.SetNextControlName(ChatKey);
        chatInput = GUI.TextField(inputRect,chatInput,127,Text());
        if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed && inputRect.Contains(pointer))
        {
            GUI.FocusControl(ChatKey);
            NativeEditKeyboard.Open(ChatKey,chatInput,TouchScreenKeyboardType.Default,127);
            Pointer.ConsumeClick();
        }
        bool send = NativeUi.Button(new Rect(482,321,34,19),"UI/UIWindow.img/PersonalShop/BtEnter",pointer);
        if (GUI.enabled && Event.current.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == ChatKey &&
            (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
        { send = true; Event.current.Use(); }
        if (GUI.enabled && (send || signal == NativeEditKeyboard.Signal.Done)) SendChat();
    }
    public static void Draw()
    {
        if (!Visible) return;
        if (!GameSession.InGame) { Reset(); return; }
        if (Time.realtimeSinceStartupAsDouble >= nextRead)
        { nextRead = Time.realtimeSinceStartupAsDouble+.25; Request(MarketOperation.Read); }
        var shop = state?.visiting; if (shop == null) { Reset(); return; }
        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position,new Vector2(525,553),scale);
        NativeUi.RegisterWindow("NativeMarketWindow", new Rect(position.x*scale,position.y*scale,525*scale,553*scale), () => NativeMarketWindow.Visible);
        var pointer = Pointer.Pos/scale; var matrix = GUI.matrix;
        GUI.matrix = matrix*Matrix4x4.Scale(new Vector3(scale,scale,1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed &&
                new Rect(position.x,position.y,500,22).Contains(pointer))
            { dragging = true; grab = pointer-position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer-grab,new Vector2(525,553),scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x,position.y,525,553));
            try
            {
                var p = pointer-position;
                // 514xxxx 是个人商店（PlayerShop），用 PersonalShop/backgrnd；
                // 503xxxx 是雇佣商店（EntrustedShop），沿用 backgrnd6。
                Image(shop.permitId / 10000 == 514 ? "backgrnd" : "backgrnd6",0,0);
                if (!shop.playerStore)
                {
                    // CEntrustedShopDlg::DrawEmployee 5181EE: slot 0 from
                    // 6FC3FD is (53,127); Copy uses width/2 and full height.
                    var employee = WzAssets.Node("Item/Cash/0503.img/"+shop.permitId.ToString("D8")+"/employee/stand/0");
                    if (employee != null)
                    {
                        var texture = WzAssets.Texture(employee);
                        if (texture != null) GUI.DrawTexture(new Rect(53-texture.width/2,127-texture.height,
                            texture.width,texture.height),texture);
                    }
                }
                GUI.Label(new Rect(23,9,460,16),shop.description,Text());
                GUI.Label(new Rect(9,135,96,16),shop.ownerName,new GUIStyle(Text()) {alignment = TextAnchor.UpperCenter});
                if (NativeUi.Button(new Rect(144,110,60,19),"UI/UIWindow.img/PersonalShop/BtExit",p)) { Close(); return; }
                bool enabled = GUI.enabled;
                GUI.enabled &= selection >= 0 && selection < shop.items.Count && shop.items[selection].exists && shop.items[selection].bundles > 0;
                if (NativeUi.Button(new Rect(144,90,60,19),"UI/UIWindow.img/PersonalShop/BtBuy",p)) BuySelected();
                GUI.enabled = enabled;
                scroll = NativeUi.Scroll4(new Rect(211,158,15,197),new Rect(7,157,199,200),scroll,
                    Math.Max(0,shop.items.Count-5),1,p);
                for (int row = 0; row < 5 && row+(int)scroll < shop.items.Count; row++)
                {
                    int index = row+(int)scroll, y = 157+40*row;
                    var stock = shop.items[index];
                    if (selection == index) Image("select",44,y);
                    var icon = NativeItemSource.IconNode(stock.item.itemId);
                    if (icon != null)
                    {
                        var origin = icon.Get("origin"); var t = WzAssets.Texture(icon);
                        GUI.DrawTexture(new Rect(8-(origin?.X ?? 0),y+34-(origin?.Y ?? 0),t.width,t.height),t);
                    }
                    // 6FBE46: 原端 FF白、alpha80 的199×35遮罩，不盖soldout文字图。
                    if (!stock.exists || stock.bundles == 0)
                        UiKit.Fill(new Rect(7,y,199,35),new Color32(255,255,255,128));
                    GUI.Label(new Rect(48,y+1,152,16),ItemCatalog.Name(stock.item.itemId),Text());
                    bool counted = stock.item.itemId/1000000 >= 2 && stock.item.itemId/1000000 <= 4
                        || Server.Bot.Trade.TradeItemRules.Rechargeable(stock.item.itemId);
                    GUI.Label(new Rect(48,y+19,152,16),counted
                        ? stock.item.quantity+"个 / "+stock.price.ToString("N0")+" 金币"
                        : stock.price.ToString("N0")+" 金币",Text());
                    if (counted)
                    {
                        float digitX = 8;
                        foreach (char digit in ((long)stock.item.quantity*stock.bundles).ToString())
                        {
                            var t = Art.Tex("wz://UI/Basic.img/ItemNo/"+digit);
                            if (t == null) continue;
                            GUI.DrawTexture(new Rect(digitX,y+23,t.width,t.height),t); digitX += t.width;
                        }
                    }
                    if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && new Rect(7,y,199,35).Contains(p))
                    { selection = index; Pointer.ConsumeClick(); }
                }
                DrawVisitors(shop);
                DrawChat(shop,p);
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }
}
