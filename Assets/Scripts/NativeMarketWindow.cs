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
    static double nextRead;
    public static bool Visible => state?.visiting != null;
    public static void Reset() { state = null; selection = -1; scroll = 0; nextRead = 0; dragging = false; }
    static MarketResponse Request(MarketOperation operation, int owner = 0, int index = 0, int quantity = 0)
    {
        if (!GameSession.InGame) return null;
        state = GameSession.Net.Market(new MarketRequest {characterId = GameSession.CharacterId,
            operation = operation, ownerId = owner, itemIndex = index, quantity = quantity});
        if (!state.ok) NativeSystemMessage.Drop(5,state.error);
        return state;
    }
    public static void Visit(int owner)
    {
        GameSession.SaveToDb();
        var response = Request(MarketOperation.Visit,owner);
        if (response?.visiting != null) { selection = -1; scroll = 0; }
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
    public static void Draw()
    {
        if (!Visible) return;
        if (!GameSession.InGame) { Reset(); return; }
        if (Time.realtimeSinceStartupAsDouble >= nextRead)
        { nextRead = Time.realtimeSinceStartupAsDouble+.25; Request(MarketOperation.Read); }
        var shop = state?.visiting; if (shop == null) return;
        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position,new Vector2(525,553),scale);
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
                Image("backgrnd6",0,0);
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
                GUI.enabled &= selection >= 0 && selection < shop.items.Count && shop.items[selection].exists;
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
                    GUI.Label(new Rect(48,y+1,152,16),ItemCatalog.Name(stock.item.itemId),Text());
                    GUI.Label(new Rect(48,y+19,152,16),stock.item.quantity == 1 ? stock.price.ToString("N0")+" mesos."
                        : stock.item.quantity+" for "+stock.price.ToString("N0")+" mesos.",Text());
                    GUI.Label(new Rect(8,y+23,32,12),stock.bundles.ToString(),Text(true));
                    if (!stock.exists || stock.bundles == 0) Image("soldout",7,y);
                    if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && new Rect(7,y,199,35).Contains(p))
                    { selection = index; Pointer.ConsumeClick(); }
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }
}
