using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// CUIVega: 82B89C / 82BB83 / 82C28B / 82CE58, same BeiDou.exe identity
// recorded in inventory-use-and-equipment.md. All artwork is original IMG.
public static class NativeVegaWindow
{
    const string Root="UI/UIWindow.img/VegaSpell/";
    static int cashId;
    static InventoryEntry equip,scroll;
    static readonly HashSet<int> allowed=new HashSet<int>();
    static Vector2 position=new Vector2(420,100),grab;
    static bool dragging,resultSound;
    static double started;
    static EquipPatchResponse result;
    public static bool Visible=>cashId!=0;
    public static bool Busy=>result!=null;
    static float S=>BottomBar.S;
    static Rect Window=>new Rect(position.x*S,position.y*S,178*S,235*S);
    static Rect At(Rect r,float x,float y,float w,float h)=>new Rect(r.x+x*S,r.y+y*S,w*S,h*S);
    public static void Close(){cashId=0;equip=null;scroll=null;result=null;dragging=false;resultSound=false;}
    public static void Open(int id)
    {
        Close();
        if(id!=5610000 && id!=5610001) return;
        if(allowed.Count==0) foreach(var row in WzAssets.Node("Etc/VegaSpell.img").Children) allowed.Add(row.Int("item"));
        cashId=id;
        position=new Vector2((UiKit.W/S-178)/2,(UiKit.H/S-235)/2);
    }
    // The native drop validator accepts inventory types, not item names.
    public static bool Drop(Vector2 pointer,int id,int inventoryPosition)
    {
        if(!Visible || !Window.Contains(pointer)) return false;
        if(Busy) return true;
        var item=GameSession.Character?.inventory.Find(x=>x.itemId==id && x.position>0 &&
            (inventoryPosition==0 || x.position==inventoryPosition));
        if(item==null) return true;
        string error=Select(item);
        if(error!=null) BottomBar.Toast(error);
        return true;
    }
    public static string Select(InventoryEntry item)
    {
        if(!Visible || Busy || item==null || item.position<=0) return "当前无法选择物品";
        if(item.inventoryType==1)
        {
            int slots=NativeScrollService.HasStats(item) ? item.upgradeslots : NativeItemSource.Node(item.itemId)?.Int("info/tuc") ?? 0;
            if(slots<=0) return "装备可升级次数不足";
            equip=item.Copy();return null;
        }
        if(item.inventoryType!=2 || item.itemId/10000!=204) return "请选择装备或强化卷轴";
        int id=item.itemId;
        if(id==2041211 || id==2041212 || id==2049100 || !allowed.Contains(id) ||
            NativeItemSource.Node(id)?.Int("info/success")!=(cashId==5610000 ? 10 : 60)) return "此卷轴无法使用这个咒语";
        scroll=item.Copy();return null;
    }
    public static string Start()
    {
        if(!Visible || Busy || equip==null || scroll==null) return "请先选择装备和卷轴";
        var requirements=new List<int>();
        var req=NativeItemSource.Node(scroll.itemId)?.Get("req");
        if(req!=null) foreach(var row in req.Children) requirements.Add(System.Convert.ToInt32(row.Value));
        var response=GameSession.Net.UpdateEquip(new EquipPatchRequest {characterId=GameSession.CharacterId,
            inventoryItemId=equip.itemRowId,scrollId=scroll.itemId,scrollPosition=scroll.position,
            scrollRequirements=requirements.ToArray(),vegaItemId=cashId});
        if(!response.ok) return response.error;
        // Settle the authoritative snapshot before any other action can save it.
        // Only the presentation waits for the original three-second sequence.
        GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();
        GameLoop.I?.player?.RefreshBuffAttributes();Profile.Save();
        result=response;started=NativeBuffs.Now;AudioManager.Play("UI/VegaTwinkling",1,false);return null;
    }
    static void Image(Rect r,string path,float x,float y)
    {
        var n=WzAssets.Node(Root+path);var tex=WzAssets.Texture(n);var origin=WzAssets.Origin(n);
        GUI.DrawTexture(At(r,x-origin.x,y-origin.y,tex.width,tex.height),tex);
    }
    static void Animation(Rect r,string path,double elapsed,float x,float y,bool loop)
    {
        var node=WzAssets.Node(Root+path);
        if(node==null) return;
        var frames=WzAssets.Frames(node);
        int duration=0;
        foreach(var frame in frames) duration+=System.Math.Max(1,System.Math.Abs(frame.Resolve().Int("delay",100)));
        if(duration<=0) return;
        int time=(int)(elapsed*1000);
        if(loop) time%=duration;
        else if(time>=duration) return;
        foreach(var frame in frames)
        {
            time-=System.Math.Max(1,System.Math.Abs(frame.Resolve().Int("delay",100)));
            if(time>=0) continue;
            Image(r,path+"/"+frame.Name,x,y);return;
        }
    }
    static void Icon(Rect r,InventoryEntry item,float x,float y)
    {
        if(item==null) return;
        var icon=NativeItemSource.Icon(item.itemId);
        if(icon!=null) GUI.DrawTexture(At(r,x,y-32,32,32),icon,ScaleMode.ScaleToFit);
    }
    public static void Draw()
    {
        if(!Visible) return;
        position=NativeUi.ClampWindowPosition(position,new Vector2(178,235),S);
        var r=Window;
        bool repaint=Event.current.type==EventType.Repaint;
        if(repaint && Pointer.Pressed && !NativeInventoryActions.Carrying && At(r,0,0,150,22).Contains(Pointer.Pos))
        {dragging=true;grab=Pointer.Pos/S-position;}
        if(dragging && Pointer.Down) {position=NativeUi.ClampWindowPosition(Pointer.Pos/S-grab,new Vector2(178,235),S);r=Window;}
        if(!Pointer.Down) dragging=false;
        if(Busy && NativeBuffs.Now-started>=3)
        {
            bool success=result.scrollResult=="success";
            var dialog=new Rect((UiKit.W-152*S)/2,(UiKit.H-159*S)/2,152*S,159*S);
            Image(dialog,success ? "SuccessWnd" : "FailWnd",0,0);
            Icon(dialog,equip,60,97);
            Animation(dialog,success ? "EffectSuccess" : "EffectFail",NativeBuffs.Now-started-3,success ? 79 : 6,success ? 71 : 45,true);
            if(!resultSound) {AudioManager.Play(success ? "UI/VegaSuccess" : "UI/VegaFail",1,false);resultSound=true;}
            if(NativeUi.Button(At(dialog,55,135,43,17),Root+"BtOK")) Close();
            return;
        }
        Image(r,cashId==5610000 ? "backgrnd10" : "backgrnd60",0,0);
        Animation(r,"EffectArrow",NativeBuffs.Now,91,41,true);
        Icon(r,equip,30,157);Icon(r,scroll,117,157);
        if(scroll!=null)
        {
            int before=cashId==5610000 ? 10 : 60,after=cashId==5610000 ? 30 : 90;
            Image(r,"Count/"+before/10,31,37);Image(r,"Count/0",39,37);
            Image(r,"Count/"+after/10,123,37);Image(r,"Count/0",131,37);
        }
        if(Busy)
        {
            Animation(r,"EffectSpelling",NativeBuffs.Now-started,85,114,false);
            if(NativeBuffs.Now-started<2.5) Animation(r,"EffectTwinkling",NativeBuffs.Now-started,85,114,true);
            var bar=Art.Tex("wz://"+Root+"GaugeBar/gauge");
            float end=Mathf.Min(159,12+(float)(NativeBuffs.Now-started)*63);
            if(bar!=null && end>19) GUI.DrawTexture(At(r,19,178,end-19,9),bar);
        }
        bool enabled=GUI.enabled;GUI.enabled&=!Busy && equip!=null && scroll!=null;
        if(NativeUi.Button(At(r,67,209,43,17),Root+"BtStart")) {string error=Start();if(error!=null) BottomBar.Toast(error);}
        GUI.enabled=enabled && !Busy;
        if(NativeUi.Button(At(r,158,8,12,12),Root+"BtCancel")) Close();
        GUI.enabled=enabled;
        if(repaint && equip!=null && At(r,31,117,25,40).Contains(Pointer.Pos)) ItemTooltip.SetHover(equip.itemId,false,equip);
        if(repaint && scroll!=null && At(r,117,117,25,40).Contains(Pointer.Pos)) ItemTooltip.SetHover(scroll.itemId);
    }
}
