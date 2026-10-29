using System;
using System.Collections.Generic;
using UnityEngine;

// CUIMessenger modes and control geometry from 0x850264 / 0x8505E2.
public static class NativeMessengerWindow
{
    static int mode, knownCount;
    static Vector2 position = new Vector2(100,40), grab;
    static bool dragging;
    static string input = "";
    static float scroll;
    static long sequence;
    static readonly List<string> messages = new List<string>();
    public static void Reset() { mode = 0; input = ""; scroll = 0; sequence = 0; knownCount = 0; messages.Clear(); }
    static GUIStyle Text(bool center = false)
    {
        var style = new GUIStyle {font = UiKit.Font,fontSize = 12,padding = new RectOffset(),clipping = TextClipping.Clip,
            alignment = center ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft};
        style.normal.textColor = Color.black; return style;
    }
    static void Avatar(string[] ids,float x,float y)
    {
        if (ids == null) return;
        var sprite = WzCharacter.Appearance("messenger_"+string.Join("_",ids),ids,WzCharacter.AppearanceFrame(ids,Time.unscaledTime));
        if (sprite != null) GUI.DrawTexture(new Rect(x-sprite.pivot.x,y-sprite.rect.height+sprite.pivot.y,sprite.rect.width,sprite.rect.height),sprite.texture);
    }
    public static void Draw(SocialResponse state)
    {
        foreach (var message in state.messengerMessages)
            if (message.sequence > sequence)
            { sequence = message.sequence; if (!message.typing) messages.Add(message.sender+" : "+message.text); }
        if (messages.Count > 200) messages.RemoveRange(0,messages.Count-200);
        float scale = BottomBar.S;
        Vector2 size = mode == 0 ? new Vector2(295,364) : mode == 1 ? new Vector2(392,240) : new Vector2(184,22);
        position = NativeUi.ClampWindowPosition(position,size,scale);
        var pointer = Pointer.Pos / scale;
        var matrix = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale,scale,1));
        try
        {
            if (Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x,position.y,size.x-50,22).Contains(pointer))
            { dragging = true; grab = pointer-position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer-grab,size,scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x,position.y,size.x,size.y));
            try
            {
                var p = pointer-position;
                var background = Art.Tex("wz://UI/UIWindow.img/Messenger/"+(mode==0?"backgrnd":mode==1?"backgrnd2":"backgrnd3"));
                // CWnd::Draw (0x9E0502) copies the canvas at its native size.
                // Mode 1 has a 392px logical window but a 295px background; it is not stretched.
                if (background != null) GUI.DrawTexture(new Rect(0,0,background.width,background.height),background);
                GUI.enabled = enabled && !NativeUtilPrompt.Visible;
                int closeX = mode == 2 ? 166 : 276;
                if (NativeUi.Button(new Rect(closeX,2,12,12),"UI/Basic.img/BtClose",p)) { NativeSocialWindow.Close(); return; }
                bool active = GUI.enabled; GUI.enabled &= mode != 2;
                if (NativeUi.Button(new Rect(closeX-28,2,12,12),"UI/Basic.img/BtMin",p)) { mode = Math.Min(2,mode+1); return; }
                GUI.enabled = active && mode != 0;
                if (NativeUi.Button(new Rect(closeX-14,2,12,12),"UI/Basic.img/BtMax",p)) { mode = Math.Max(0,mode-1); return; }
                GUI.enabled = active;
                if (mode == 2) return;
                for (int i = 0; i < Math.Min(3,state.messengerMembers.Count); i++)
                {
                    var member = state.messengerMembers[i];
                    int seat = member.messengerPosition;
                    if (mode == 0)
                    {
                        var ids = member.id == GameSession.CharacterId ? WzCharacter.CharacterIds(GameSession.Character.record,GameSession.Character.inventory) : member.appearance;
                        Avatar(ids,54+94*seat,118);
                    }
                    float y = mode == 0 ? 122 : 28;
                    var bar = Art.Tex("wz://UI/UIWindow.img/Messenger/NameBar/"+(seat+1));
                    if (bar != null) GUI.DrawTexture(new Rect(9+94*seat,y,89,19),bar);
                    GUI.Label(new Rect(9+94*seat,y,89,19),member.name,Text(true));
                }
                int lines = mode == 0 ? 11 : 9;
                int top = mode == 0 ? 155 : 60;
                int maximum = Math.Max(0,messages.Count-lines);
                if (knownCount != messages.Count) { knownCount = messages.Count; scroll = maximum; }
                scroll = NativeUi.Scroll4(new Rect(273,mode==0?148:55,15,mode==0?154:125),new Rect(9,top,264,lines*13),scroll,maximum,1,p);
                for (int i = (int)scroll; i < Math.Min(messages.Count,(int)scroll+lines); i++)
                    GUI.Label(new Rect(14,top+(i-(int)scroll)*13,253,13),messages[i],Text());
                int editY = mode == 0 ? 318 : 194;
                GUI.SetNextControlName("nativeMessengerChat");
                input = GUI.TextField(new Rect(9,editY,207,15),input,127,Text());
                bool send = NativeUi.Button(new Rect(225,editY-3,39,17),"UI/UIWindow.img/Messenger/BtEnter",p);
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl()=="nativeMessengerChat")
                { send = true; Event.current.Use(); }
                if (send && !string.IsNullOrWhiteSpace(input))
                {
                    var response = NativeSocialWindow.Request(SocialOperation.SendChat,text:input,channel:NativeChatChannel.Messenger);
                    if (response.ok) { messages.Add(GameSession.Character.record.name+" : "+input); input = ""; }
                    else NativeSystemMessage.Drop(5,response.error);
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.enabled = enabled; GUI.matrix = matrix; }
    }
}
