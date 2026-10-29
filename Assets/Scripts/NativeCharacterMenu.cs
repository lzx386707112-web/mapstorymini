using UnityEngine;

// CUIContextMenu 0x996570 / 0x9967FB: native 100px menu and 15px rows.
public static class NativeCharacterMenu
{
    static Server.Bot.BotCharacter target;
    static Vector2 position;
    static bool party;
    public static bool Visible => target != null;
    public static void Close() { target = null; }
    public static void Open(Server.Bot.BotCharacter actor)
    {
        target = actor;
        var social = NativeSocialWindow.Request(SocialOperation.Read);
        party = !social.party || social.partyLeader == GameSession.CharacterId;
        position = NativeUi.ClampWindowPosition(Pointer.Pos / BottomBar.S, new Vector2(100, party ? 120 : 105), BottomBar.S);
        Pointer.ConsumeClick();
    }
    public static void Draw()
    {
        if (target == null) return;
        if (!target.online || target.mapId != Profile.LastMapId || target.world != GameSession.World ||
            target.channel != GameSession.Channel) { Close(); return; }
        float scale = BottomBar.S;
        var pointer = Pointer.Pos / scale;
        int rows = party ? 6 : 5;
        var rect = new Rect(position.x, position.y,100,(rows + 2) * 15);
        var matrix = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale,scale,1));
        try
        {
            UiKit.Fill(rect, Color.white);
            void Image(string name, float x, float y, float w, float h)
            { var texture = Art.Tex("wz://UI/UIWindow.img/ContextMenu/" + name); if (texture != null) GUI.DrawTexture(new Rect(position.x+x,position.y+y,w,h),texture); }
            Image("t",0,0,100,23);
            for (int i = 0; i < rows; i++) Image("c",3,23+i*15,97,15);
            Image("s",3,23+rows*15,97,7);
            // 0x997194: native font 0, name clipped to 78px, drawn at (11,6).
            var title = new GUIStyle {font = UiKit.Font, fontSize = 12,
                clipping = TextClipping.Clip, padding = new RectOffset()};
            title.normal.textColor = Color.black;
            string name = target.name;
            while (name.Length > 0 && title.CalcSize(new GUIContent(name)).x > 78)
                name = name.Substring(0,name.Length-1);
            GUI.Label(new Rect(position.x+11,position.y+6,78,16),name,title);
            bool Button(string name,int y,bool active = true)
            {
                GUI.enabled = enabled && active && !NativeUtilPrompt.Visible;
                return NativeUi.Button(new Rect(position.x+9,position.y+y,85,15),"UI/UIWindow.img/ContextMenu/"+name,pointer);
            }
            Button("BtInfo",24,false);
            if (Button("BtSave",39)) { NativeSocialWindow.Request(SocialOperation.AddBuddy,target.name); Close(); return; }
            if (Button("BtTrade",54)) { NativeTradeWindow.Invite(target.id); Close(); return; }
            if (Button("BtWhisper",69)) { BottomBar.FocusChat(NativeChatChannel.Whisper,target.name); Close(); return; }
            if (party && Button("BtParty",84)) { var response = NativeSocialWindow.Request(SocialOperation.InviteParty,target.name); if (!response.ok) NativeSystemMessage.Drop(5,response.error); Close(); return; }
            Button("BtClaim",party ? 99 : 84,false);
            if (Event.current.type == EventType.Repaint && Pointer.Clicked && !rect.Contains(pointer)) Close();
        }
        finally { GUI.enabled = enabled; GUI.matrix = matrix; }
    }
}
