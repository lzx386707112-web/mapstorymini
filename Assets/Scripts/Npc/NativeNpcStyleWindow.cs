using System;
using System.Collections.Generic;
using UnityEngine;

// CUtilDlgEx type5 (NPC packet type7): 9A92C9 / 9AC92C / 9AA708.
// Original 419x306 IMG, coordinates and wraparound selection; no substitute menu.
public static class NativeNpcStyleWindow
{
    const string Root="UI/UIWindow.img/UtilDlgEx_Avatar/";
    static readonly Dictionary<string,Sprite> frames=new Dictionary<string,Sprite>();
    static GUIStyle nameStyle;
    public static void Move(NpcDialog dialog,int direction)
    {
        int count=dialog.Styles.Length;
        if(count>0) dialog.StyleIndex=(dialog.StyleIndex+direction%count+count)%count;
    }
    public static string[] PreviewIds(NpcDialog dialog)
    {
        var character=GameSession.Character;
        if(character==null || dialog.Styles.Length==0) return Array.Empty<string>();
        var ids=WzCharacter.CharacterIds(character.record,dialog.StyleClothed ? character.inventory : new List<InventoryEntry>());
        int id=dialog.Styles[dialog.StyleIndex];
        int family=dialog.Styles[0]/10000;
        if(family==2) ids[2]=id.ToString("D8");
        else if(family>=3) ids[3]=id.ToString("D8");
        else { ids[0]=(2000+id).ToString("D8");ids[1]=(12000+id).ToString("D8"); }
        return ids;
    }
    static void Image(Rect r,string path,float x,float y)
    {
        var texture=Art.Tex("wz://"+Root+path);
        if(texture!=null) GUI.DrawTexture(new Rect(r.x+x,r.y+y,texture.width,texture.height),texture);
    }
    static bool Button(Rect r,string path,float x,float y)
    {
        var texture=Art.Tex("wz://"+path+"/normal/0");
        return texture!=null && NativeUi.Button(new Rect(r.x+x,r.y+y,texture.width,texture.height),path);
    }
    public static void Draw(NpcSession session)
    {
        var dialog=session.Dialog;
        var r=new Rect((UiKit.W/NpcDialogUi.WindowScale-419)/2,(UiKit.H/NpcDialogUi.WindowScale-306)/2,419,306);
        Image(r,"backgrnd",0,0);
        Image(r,"shadow",174,239);
        var layout=DialogFlow.Measure(dialog.Content,280);
        float textY=28+(int)((100-layout.Height-6)/2);
        GUI.BeginClip(new Rect(r.x+111,r.y+textY+1,278,98));
        foreach(var span in layout.Spans)
        {
            var rect=new Rect(span.Rect.x-1,span.Rect.y-1,span.Rect.width,span.Rect.height);
            if(span.Tex!=null) GUI.DrawTexture(rect,span.Tex);
            else
            {
                var style=new GUIStyle {font=UiKit.Font,fontSize=12,fontStyle=span.Run.Bold ? FontStyle.Bold : FontStyle.Normal};
                style.normal.textColor=span.Run.Color;GUI.Label(rect,span.Text,style);
            }
        }
        GUI.EndClip();
        if(dialog.Styles.Length>0)
        {
            var ids=PreviewIds(dialog);
            if(ids.Length>0)
            {
                int frame=WzCharacter.AppearanceFrame(ids,Time.unscaledTime);
                string key=string.Join("_",ids)+"/"+frame;
                if(!frames.TryGetValue(key,out var sprite))
                {sprite=WzCharacter.Appearance(string.Join("_",ids),ids,frame);frames[key]=sprite;}
                if(sprite!=null) GUI.DrawTexture(new Rect(r.x+207-sprite.pivot.x,r.y+245-(sprite.rect.height-sprite.pivot.y),sprite.rect.width,sprite.rect.height),sprite.texture);
            }
            Image(r,"nameTag",123,246);
            if(nameStyle==null) nameStyle=new GUIStyle {font=UiKit.Font,fontSize=12,alignment=TextAnchor.UpperCenter};
            nameStyle.normal.textColor=Color.black;
            int id=dialog.Styles[dialog.StyleIndex];
            GUI.Label(new Rect(r.x+123,r.y+246,173,19),WzNames.HasItem(id) ? WzNames.Item(id) : "",nameStyle);
            if(Button(r,Root+"BtPrev",15,195)) Move(dialog,-1);
            if(Button(r,Root+"BtNext",341,195)) Move(dialog,1);
            if(Button(r,"UI/Basic.img/BtOK2",314,281)) {session.Reply(1,dialog.StyleIndex);return;}
            string toggle=dialog.StyleClothed ? "BtOff" : "BtOn";
            if(Button(r,Root+toggle,172,281)) dialog.StyleClothed=!dialog.StyleClothed;
        }
        if(Button(r,"UI/Basic.img/BtCancel2",363,281) || Button(r,Root+"BtExit",9,281)) session.Cancel();
    }
}
