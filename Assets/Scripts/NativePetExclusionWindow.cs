using System;
using System.Collections.Generic;
using UnityEngine;

// UserInfo 9009BF -> 900A7C; ExceptionItemSearch 8FC031 -> 8FC432 -> 8FB64B.
public static class NativePetExclusionWindow
{
    static bool visible, search, results;
    public static bool Visible => visible;
    static int petId, selected = -1, resultSelected = -1;
    static float scroll, resultScroll;
    static string query = "";
    static List<int> entries = new List<int>(), matches = new List<int>();
    const string User = "UI/UIWindow.img/UserInfo/", Search = "UI/UIWindow.img/ExceptionItemSearch/";
    public static void Close() { visible = search = results = false; }
    public static bool CoversPointer(Vector2 parent, int mode, float scale)
    {
        if(!visible)return false;
        var origin=parent+new Vector2(270,mode==0?34:196);
        var point=Pointer.Pos/scale;
        bool Hit(Rect rect) => rect.Contains(point) ||
            (Pointer.Down || Pointer.Released) && rect.Contains(Pointer.PressPosition/scale);
        if(Hit(new Rect(origin.x,origin.y,159,147)))return true;
        if(!search)return false;
        var at=NativeUi.ClampWindowPosition(origin,new Vector2(219,results?240:123),scale);
        return Hit(new Rect(at.x,at.y,219,results?240:123));
    }
    public static void Toggle(InventoryEntry pet)
    {
        if (visible && petId == pet.petId) { Close(); return; }
        var reply = GameSession.Net.ChangeInventory(new InventoryChangeRequest { characterId = GameSession.CharacterId,
            petId = pet.petId, readPetExclusions = true });
        if (!reply.ok) { BottomBar.Toast(reply.error); return; }
        petId = pet.petId; entries = reply.petExclusions; selected = -1; scroll = 0;
        visible = true; search = results = false;
    }
    static void Save(List<int> replacement)
    {
        var reply = GameSession.Net.ChangeInventory(new InventoryChangeRequest { characterId = GameSession.CharacterId,
            petId = petId, writePetExclusions = true, petExclusions = replacement.ToArray() });
        if (!reply.ok) { BottomBar.Toast(reply.error); return; }
        entries = reply.petExclusions; selected = -1;
    }
    static void Add(int id)
    {
        // 8FCEDE: client limit ten including mesos, reject duplicates, order by localized name.
        if (entries.Count >= 10 || entries.Contains(id)) return;
        var next = new List<int>(entries) { id };
        next.Sort((a,b) => string.Compare(Name(a), Name(b), StringComparison.CurrentCulture)); Save(next);
    }
    static string Name(int id) => id == int.MaxValue ? "金币" : WzNames.Item(id);
    static void Find()
    {
        matches.Clear(); resultSelected = -1; resultScroll = 0;
        string needle = query.Replace(" ", "").ToLowerInvariant();
        if (needle.Length > 0) foreach (int id in Server.Bot.BotItemCatalog.Shared.ItemIds)
        {
            string name = Server.Bot.BotItemCatalog.Shared.Name(id);
            if (string.IsNullOrEmpty(name) || !name.ToLowerInvariant().Contains(needle)) continue;
            var item = NativeItemSource.Node(id);
            if (item != null && item.Int("info/cash") == 0) matches.Add(id);
        }
        results = true;
    }
    static void Image(string path, int x, int y)
    {
        var tex = Art.Tex("wz://" + path);
        if (tex != null) GUI.DrawTexture(new Rect(x,y,tex.width,tex.height),tex);
    }
    static bool Button(string path, int x, int y, Vector2 pointer, bool enabled = true)
    {
        var tex = Art.Tex("wz://" + path + "/normal/0"); if (tex == null) return false;
        bool old = GUI.enabled; GUI.enabled = old && enabled;
        bool hit = NativeUi.Button(new Rect(x,y,tex.width,tex.height),path,pointer); GUI.enabled = old; return hit;
    }
    static void List(List<int> items, ref float offset, ref int selection, int y, int count, Vector2 p, GUIStyle style)
    {
        for (int row = 0; row < count; row++)
        {
            int index = (int)offset + row; if (index >= items.Count) break;
            var rect = new Rect(10,y+row*14,138,13); bool hover = rect.Contains(p);
            // 8FCB2C/8FCB7E and 8FBAEB/8FBB43 native FillRect colors.
            if (index == selection || hover)
            {
                var old = GUI.color; GUI.color = index == selection ? new Color32(91,123,158,255) : new Color32(187,201,217,255);
                GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color = old;
            }
            GUI.Label(new Rect(y == 12 ? 10 : 15,rect.y,138,14),Name(items[index]),style);
            if (GUI.enabled && hover && Event.current.type == EventType.Repaint && Pointer.Clicked)
            { selection = index; Pointer.ConsumeClick(); }
        }
    }
    public static void Draw(Vector2 parent, int mode, float scale)
    {
        if (!visible || !NativeCharacterInfo.Visible) return;
        Vector2 origin = parent + new Vector2(270,mode == 0 ? 34 : 196);
        var matrix = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale,scale,1));
        var style = new GUIStyle {font=UiKit.Font,fontSize=12,clipping=TextClipping.Clip}; style.normal.textColor=Color.black;
        GUI.BeginGroup(new Rect(origin.x,origin.y,159,147));
        try
        {
            NativeUi.RegisterWindow("PetExclusions",new Rect(origin.x*scale,origin.y*scale,159*scale,147*scale),()=>visible && NativeCharacterInfo.Visible);
            var p = Pointer.Pos/scale-origin; GUI.enabled=enabled && !NativeUtilPrompt.Visible && !search;
            Image(User+"backgrnd6",0,0);
            if(Button(User+"BtExceptionHide",142,5,p)) {Close();return;}
            if(Button(User+"BtMeso",10,124,p,entries.Contains(int.MaxValue) || entries.Count<10))
            {if(entries.Contains(int.MaxValue)) {var next=new List<int>(entries);next.Remove(int.MaxValue);Save(next);} else Add(int.MaxValue);}
            if(Button(User+"BtRegister",80,124,p,entries.Count<10)) {search=true;query="";}
            if(Button(User+"BtDelete",124,124,p,selected>=0 && selected<entries.Count))
            {var next=new List<int>(entries);next.RemoveAt(selected);Save(next);}
            scroll=NativeUi.Scroll4(new Rect(140,25,15,90),new Rect(10,30,128,84),scroll,Mathf.Max(0,entries.Count-6),1,p);
            List(entries,ref scroll,ref selected,30,6,p,style);
        }
        finally {GUI.EndGroup();GUI.enabled=enabled;GUI.matrix=matrix;}
        if (!search) return;
        var searchOrigin = NativeUi.ClampWindowPosition(origin,new Vector2(219,results?240:123),scale);
        GUI.matrix=matrix*Matrix4x4.Scale(new Vector3(scale,scale,1));
        GUI.BeginGroup(new Rect(searchOrigin.x,searchOrigin.y,219,123));
        try
        {
            NativeUi.RegisterWindow("PetExclusions.Search",new Rect(searchOrigin.x*scale,searchOrigin.y*scale,219*scale,(results?240:123)*scale),()=>visible && search && NativeCharacterInfo.Visible);
            var p=Pointer.Pos/scale-searchOrigin; GUI.enabled=enabled && !NativeUtilPrompt.Visible;
            Image(Search+"backgrnd",0,0);
            // 8FC031 -> 92C0BF: Basic/BtClose, x195/y8.
            if(Button("UI/Basic.img/BtClose",195,8,p)) {search=results=false;return;}
            query=GUI.TextField(new Rect(15,78,115,13),query,256,style);
            if(Button(Search+"BtSearch",140,76,p))Find();
        }
        finally {GUI.EndGroup();GUI.enabled=enabled;GUI.matrix=matrix;}
        if(!results)return;
        var resultOrigin=searchOrigin+new Vector2(0,120);
        GUI.matrix=matrix*Matrix4x4.Scale(new Vector3(scale,scale,1));
        GUI.BeginGroup(new Rect(resultOrigin.x,resultOrigin.y,162,117));
        try
        {
            var p=Pointer.Pos/scale-resultOrigin; GUI.enabled=enabled && !NativeUtilPrompt.Visible;
            Image(Search+"resultback",0,0);
            resultScroll=NativeUi.Scroll4(new Rect(142,8,15,75),new Rect(10,12,130,70),resultScroll,Mathf.Max(0,matches.Count-5),1,p);
            List(matches,ref resultScroll,ref resultSelected,12,5,p,style);
            if(Button(Search+"BtSave",49,94,p,resultSelected>=0 && resultSelected<matches.Count))Add(matches[resultSelected]);
            if(Button(Search+"BtDelete",82,94,p,resultSelected>=0 && resultSelected<matches.Count))
            {var next=new List<int>(entries);next.Remove(matches[resultSelected]);Save(next);}
        }
        finally {GUI.EndGroup();GUI.enabled=enabled;GUI.matrix=matrix;}
    }
}
