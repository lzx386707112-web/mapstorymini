using System;
using System.Collections.Generic;
using Server.Bot;
using Server.Bot.Social;
using Server.Bot.Trade;
using UnityEngine;

// CUITradingRoom: all geometry is in the original 565x474 IMG coordinate space.
public static class NativeTradeWindow
{
    static BotTradeResponse state;
    static double nextRead;
    static int invitation;
    static Vector2 position = new Vector2(180, 60), grab;
    static bool dragging;
    static float chatScroll;
    static int messageCount;
    static string chat = "";
    static readonly List<string> messages = new List<string>();
    static InventoryEntry draggedItem;
    static BotOutput subscribed;
    public static bool Visible => state != null && state.active && state.accepted;
    public static bool CanOffer => Visible && !state.locked;
    public static void Reset()
    {
        if (subscribed != null) subscribed.Published -= OnOutput;
        subscribed = null; state = null; invitation = 0; nextRead = 0; chat = ""; messages.Clear(); draggedItem = null;
    }
    static void OnOutput(BotOutput.Message message)
    {
        if (message.kind != BotOutput.Kind.TradeChat || message.targetId != GameSession.CharacterId) return;
        string name = message.actorId == GameSession.CharacterId ? GameSession.Character.record.name : state?.partnerName;
        messages.Add((name ?? "") + " : " + message.text);
        if (messages.Count > 200) messages.RemoveAt(0);
    }
    static BotTradeResponse Request(BotTradeOperation operation, int target = 0, int amount = 0, InventoryEntry item = null, int slot = 0, string text = null)
    {
        if (!GameSession.InGame || GameSession.Net == null) return null;
        state = GameSession.Net.BotTrade(new BotTradeRequest {characterId = GameSession.CharacterId, operation = operation,
            botId = target, meso = amount, inventoryType = item?.inventoryType ?? 0, inventoryPosition = item?.position ?? 0,
            quantity = amount, slot = slot, text = text});
        if (!state.ok) NativeSystemMessage.Drop(5, state.error);
        if (!state.active) { draggedItem = null; messages.Clear(); }
        return state;
    }
    public static void Invite(int id)
    { GameSession.SaveToDb(); Request(BotTradeOperation.Invite, id); }
    public static void Close() { Request(BotTradeOperation.Cancel); NativeUtilPrompt.Clear(); }
    public static void BeginItemDrag(InventoryEntry item)
    { if (CanOffer && !NativeUtilPrompt.Visible && item != null && item.position > 0) draggedItem = item.Copy(); }
    static GUIStyle Style(bool right = false, bool white = false, int size = 12)
    {
        var style = new GUIStyle {font = UiKit.Font, fontSize = size, clipping = TextClipping.Clip,
            alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft, padding = new RectOffset()};
        style.normal.textColor = white ? Color.white : Color.black; return style;
    }
    static void Avatar(string[] ids, float x, float y)
    {
        if (ids == null) return;
        var sprite = WzCharacter.Appearance(string.Join("_", ids), ids, WzCharacter.AppearanceFrame(ids, Time.unscaledTime));
        if (sprite == null) return;
        GUI.DrawTexture(new Rect(x - sprite.pivot.x, y - sprite.rect.height + sprite.pivot.y, sprite.rect.width, sprite.rect.height), sprite.texture);
    }
    public static void Tick()
    {
        if (!GameSession.InGame) { if (state != null) Reset(); return; }
        if (subscribed == null) { subscribed = BotTypeManager.Output; subscribed.Published += OnOutput; }
        if (Time.realtimeSinceStartupAsDouble < nextRead) return;
        nextRead = Time.realtimeSinceStartupAsDouble + .2;
        Request(BotTradeOperation.Read);
        if (state.incomingInvitation && invitation != state.partnerId && !NativeUtilPrompt.Visible)
        {
            invitation = state.partnerId; int partner = invitation;
            NativeUtilPrompt.Confirm(state.partnerName + " 向你提出交易申请。是否接受？", yes =>
            { Request(yes ? BotTradeOperation.Accept : BotTradeOperation.Cancel, partner); invitation = 0; });
        }
        if (!state.incomingInvitation) invitation = 0;
    }
    public static void Draw()
    {
        Tick(); if (!Visible) return;
        float scale = BottomBar.S; var pointer = Pointer.Pos / scale;
        position = NativeUi.ClampWindowPosition(position, new Vector2(565,474), scale);
        var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale,scale,1));
        try
        {
            var r = new Rect(position.x, position.y,565,474);
            if (Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(r.x,r.y,565,20).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) { position = NativeUi.ClampWindowPosition(pointer - grab,new Vector2(565,474),scale); r.position = position; }
            if (!Pointer.Down) dragging = false;
            GUI.DrawTexture(r, Art.Tex("wz://UI/UIWindow.img/TradingRoom/backgrnd"));
            GUI.BeginGroup(r);
            var p = pointer - r.position;
            bool enabled = GUI.enabled; GUI.enabled &= !NativeUtilPrompt.Visible;
            Avatar(WzCharacter.CharacterIds(GameSession.Character.record,GameSession.Character.inventory),227,101);
            Avatar(state.partnerAppearance,66,101);
            var name = Style(false,true); name.alignment = TextAnchor.UpperCenter;
            GUI.Label(new Rect(185,107,88,16),GameSession.Character.record.name,name);
            GUI.Label(new Rect(20,107,88,16),state.partnerName,name);
            DrawItems(state.items,169,p); DrawItems(state.partnerItems,7,p);
            var number = Style(true);
            GUI.Label(new Rect(184,243,97,14),state.meso.ToString("N0"),number);
            GUI.Label(new Rect(22,243,97,14),state.partnerMeso.ToString("N0"),number);
            GUI.Label(new Rect(184,258,97,14),(state.meso - BotTradeService.Fee(state.meso)).ToString("N0"),number);
            GUI.Label(new Rect(22,258,97,14),(state.partnerMeso - BotTradeService.Fee(state.partnerMeso)).ToString("N0"),number);
            if (state.locked) UiKit.Fill(new Rect(169,129,113,109),new Color(1,1,1,.5f));
            if (state.partnerLocked) UiKit.Fill(new Rect(6,129,113,109),new Color(1,1,1,.5f));
            GUI.enabled &= !state.locked;
            if (NativeUi.Button(new Rect(116,49,60,19),"UI/UIWindow.img/TradingRoom/BtTrade",p))
                NativeUtilPrompt.Confirm("确定要进行交易吗？",yes => { if (yes) Request(BotTradeOperation.Confirm); });
            if (NativeUi.Button(new Rect(170,244,14,14),"UI/UIWindow.img/TradingRoom/BtCoin",p))
                NativeUtilPrompt.Number("要放入多少金币？",1,0,Profile.gold,value => Request(BotTradeOperation.OfferMeso,amount:value));
            GUI.enabled = enabled && !NativeUtilPrompt.Visible;
            if (NativeUi.Button(new Rect(116,71,60,19),"UI/UIWindow.img/TradingRoom/BtReset",p)) Close();
            var input = Style(); GUI.SetNextControlName("nativeTradeChat");
            chat = GUI.TextField(new Rect(296,245,192,15),chat,127,input);
            bool send = NativeUi.Button(new Rect(520,244,39,17),"UI/UIWindow.img/Messenger/BtEnter",p);
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "nativeTradeChat")
            { send = true; Event.current.Use(); }
            if (send && !string.IsNullOrWhiteSpace(chat)) { Request(BotTradeOperation.Chat,text:chat); chat = ""; }
            int maxScroll = Math.Max(0,messages.Count - 11);
            if (messageCount != messages.Count) { messageCount = messages.Count; chatScroll = maxScroll; }
            chatScroll = NativeUi.Scroll4(new Rect(542,23,15,192),new Rect(295,25,237,192),chatScroll,maxScroll,1,p);
            GUI.BeginGroup(new Rect(297,27,237,188));
            int first = (int)chatScroll;
            for (int i = first; i < Math.Min(messages.Count,first+11); i++) GUI.Label(new Rect(0,(i - first)*16,237,16),messages[i],Style());
            GUI.EndGroup();
            if (draggedItem != null)
            {
                var icon = NativeItemSource.Icon(draggedItem.itemId);
                if (icon != null) GUI.DrawTexture(new Rect(p.x-16,p.y-16,32,32),icon);
                if (Event.current.type == EventType.Repaint && Pointer.Released)
                {
                    var item = draggedItem; draggedItem = null;
                    for (int i = 0; i < 9; i++) if (new Rect(169+i%3*39,129+i/3*37,32,32).Contains(p))
                    {
                        int slot = i+1;
                        if (item.quantity == 1 || TradeItemRules.Rechargeable(item.itemId)) Request(BotTradeOperation.OfferItem,amount:item.quantity,item:item,slot:slot);
                        else NativeUtilPrompt.Number("要放入多少个？",1,1,item.quantity,value => Request(BotTradeOperation.OfferItem,amount:value,item:item,slot:slot));
                        break;
                    }
                }
            }
            GUI.enabled = enabled; GUI.EndGroup();
        }
        finally { GUI.matrix = matrix; }
    }
    static void DrawItems(List<InventoryEntry> items, int x, Vector2 pointer)
    {
        foreach (var item in items)
        {
            int i = item.position-1; if (i < 0 || i >= 9) continue;
            var r = new Rect(x+i%3*39,129+i/3*37,32,32);
            var iconNode = NativeItemSource.IconNode(item.itemId);
            if (iconNode != null)
            {
                var icon = WzAssets.Texture(iconNode);
                var origin = WzAssets.Origin(iconNode);
                // 0x7C33CB -> 0x5D6458: draw at (171+39*col,161+37*row)
                // minus the original Canvas origin, with no 32px rescaling.
                GUI.DrawTexture(new Rect(r.x+2-origin.x,r.y+32-origin.y,icon.width,icon.height),icon);
            }
            if (item.inventoryType >= 2 && item.inventoryType <= 4) GUI.Label(new Rect(r.x,r.y+20,32,12),item.quantity.ToString(),Style(true,false,9));
            if (Event.current.type == EventType.Repaint && r.Contains(pointer)) ItemTooltip.SetHover(item.itemId,false,item);
        }
    }
}
