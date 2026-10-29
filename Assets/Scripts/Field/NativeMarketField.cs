using System.Collections.Generic;
using Server.Bot;
using UnityEngine;

// CEntrustedShop 50D56C/50D97B: employee frames are attached to
// the map position and retain their original origin and frame delays.
public sealed class NativeMarketField : MonoBehaviour
{
    readonly Dictionary<int, NativeImgAnimation> employees = new Dictionary<int, NativeImgAnimation>();
    readonly HashSet<int> present = new HashSet<int>();
    readonly List<int> removed = new List<int>();
    List<MarketShopSnapshot> shops = new List<MarketShopSnapshot>();
    int map, world, channel;
    float nextRead;
    GUIStyle nameStyle;
    public void Synchronize(int mapId, int worldId, int channelId)
    {
        bool changed = map != mapId || world != worldId || channel != channelId;
        if (!changed && Time.unscaledTime < nextRead) return;
        nextRead = Time.unscaledTime + .5f;
        map = mapId; world = worldId; channel = channelId;
        if (!GameSession.InGame) { Clear(); return; }
        var response = GameSession.Net.Market(new MarketRequest {characterId = GameSession.CharacterId,
            operation = MarketOperation.Read});
        shops = response.shops;
        present.Clear();
        foreach (var shop in shops)
        {
            if (shop.playerStore) continue;
            present.Add(shop.ownerId);
            if (employees.ContainsKey(shop.ownerId)) continue;
            var animation = NativeImgAnimation.Spawn("Item/Cash/0503.img/"+shop.permitId.ToString("D8")+
                "/employee/stand",transform,new Vector3(shop.x/Art.PPU,-shop.y/Art.PPU,0),300,repeat:true);
            if (animation != null) employees.Add(shop.ownerId,animation);
        }
        removed.Clear();
        foreach (var employee in employees) if (!present.Contains(employee.Key)) removed.Add(employee.Key);
        foreach (int owner in removed) { employees[owner].Remove(); employees.Remove(owner); }
    }
    public void Clear()
    {
        foreach (var employee in employees.Values) if (employee != null) employee.Remove();
        employees.Clear(); shops.Clear(); nextRead = 0;
    }
    public bool TryVisit(Vector3 point, System.Func<int, SpriteRenderer> playerRenderer)
    {
        foreach (var shop in shops)
        {
            var renderer = shop.playerStore ? playerRenderer(shop.ownerId)
                : employees.TryGetValue(shop.ownerId,out var employee) ? employee.Image : null;
            if (renderer == null || renderer.sprite == null) continue;
            if (!renderer.bounds.Contains(new Vector3(point.x,point.y,renderer.bounds.center.z))) continue;
            NativeMarketWindow.Visit(shop.ownerId); Pointer.ConsumeClick(); return true;
        }
        return false;
    }
    void OnGUI()
    {
        var camera = Camera.main;
        if (camera == null || !GameSession.InGame || shops.Count == 0) return;
        UiKit.Begin();
        if (nameStyle == null)
        {
            nameStyle = new GUIStyle {font = UiKit.Font,fontSize = 12,padding = new RectOffset(),
                margin = new RectOffset(),alignment = TextAnchor.UpperLeft,clipping = TextClipping.Clip};
            nameStyle.normal.textColor = Color.white;
        }
        float scale = UiKit.H/(2*camera.orthographicSize*Art.PPU);
        var matrix = GUI.matrix;
        GUI.matrix = matrix*Matrix4x4.Scale(new Vector3(scale,scale,1));
        try
        {
            foreach (var shop in shops)
            {
                if (shop.playerStore) continue;
                var screen = camera.WorldToScreenPoint(new Vector3(shop.x/Art.PPU,-shop.y/Art.PPU,0));
                if (screen.z < 0) continue;
                var anchor = new Vector2(screen.x/UiKit.Scale,(Screen.height-screen.y)/UiKit.Scale)/scale;
                string name = shop.ownerName+"'s Hired Merchant";
                // 50D76A -> 5F0334(type1000), absent NameTag/1000:
                // font0, A0000000, width=text+5, height=16, four clear corners.
                NpcDialogUi.NamePlateRects(anchor,nameStyle.CalcSize(new GUIContent(name)),out var plate,out var text);
                var background = new Color(0,0,0,160f/255);
                UiKit.Fill(new Rect(plate.x,plate.y+1,plate.width,plate.height-2),background);
                UiKit.Fill(new Rect(plate.x+1,plate.y,plate.width-2,1),background);
                UiKit.Fill(new Rect(plate.x+1,plate.yMax-1,plate.width-2,1),background);
                GUI.Label(text,name,nameStyle);
                if (Event.current.type == EventType.Repaint && Pointer.DoubleClicked && !Pointer.Blocked &&
                    !GameWindows.AnyVisible && plate.Contains(Pointer.Pos/scale))
                { NativeMarketWindow.Visit(shop.ownerId); Pointer.ConsumeClick(); }
            }
        }
        finally { GUI.matrix = matrix; }
    }
    void OnDestroy() => Clear();
}
