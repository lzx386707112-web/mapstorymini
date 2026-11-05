using System.Collections.Generic;
using Server.Bot;
using UnityEngine;

// CEntrustedShop 50D56C/50D97B: employee frames are attached to
// the map position and retain their original origin and frame delays.
// 店招：50D897 -> 48BBCA(type5) 读取 permit skin/signboard；
// 48DF57..48DFA5 / 48E8AB..48E8CE：相对脚点(-width/2, -height+2)。
// Canvas origin 用于板内文字偏移，不把整张透明画布再挂到店员头顶。
public sealed class NativeMarketField : MonoBehaviour
{
    readonly Dictionary<int, NativeImgAnimation> employees = new Dictionary<int, NativeImgAnimation>();
    readonly Dictionary<int, GameObject> stalls = new Dictionary<int, GameObject>();
    readonly Dictionary<int, Texture2D> signboards = new Dictionary<int, Texture2D>();
    readonly HashSet<int> present = new HashSet<int>();
    readonly List<int> removed = new List<int>();
    List<MarketShopSnapshot> shops = new List<MarketShopSnapshot>();
    int map, world, channel;
    float nextRead;
    GUIStyle nameStyle, signStyle;

    Texture2D Signboard(int permit)
    {
        if (signboards.TryGetValue(permit, out var cached)) return cached;
        Texture2D tex = TrySignboard(permit) ?? TrySignboard(5030000);   // 514xxxx 系（真人摊）无自有皮肤，回退 0503 默认
        signboards[permit] = tex;   // 缺失也缓存，避免每帧重查 WZ
        return tex;
    }
    static Texture2D TrySignboard(int permit)
    {
        try { return Art.Tex("wz://Item/Cash/0503.img/" + permit.ToString("D8") + "/skin/signboard"); }
        catch (System.Exception) { return null; }
    }
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
            CreateStall(shop);
        }
        removed.Clear();
        foreach (var employee in employees) if (!present.Contains(employee.Key)) removed.Add(employee.Key);
        foreach (int owner in removed)
        {
            employees[owner].Remove(); employees.Remove(owner);
            if (stalls.TryGetValue(owner,out var stall)) { Destroy(stall); stalls.Remove(owner); }
        }
    }
    void CreateStall(MarketShopSnapshot shop)
    {
        if (stalls.ContainsKey(shop.ownerId)) return;
        var sign = Signboard(shop.permitId);
        if (sign == null) return;
        var stall = new GameObject("merchant-skin");
        stall.transform.SetParent(transform,false);
        stalls.Add(shop.ownerId,stall);
        // 48D3BC/48D602: shop and backgrnd share the signboard top-left.
        // 48D6DB..48D749 / 50DC40..50DC58: backgrnd < employee < shop.
        AddStallLayer(stall,shop,sign,"backgrnd",299);
        AddStallLayer(stall,shop,sign,"shop",301);
    }
    static void AddStallLayer(GameObject stall, MarketShopSnapshot shop, Texture2D sign, string node, int order)
    {
        string path = "wz://Item/Cash/0503.img/"+shop.permitId.ToString("D8")+"/skin/"+node;
        var sprite = Art.Frame(path);
        if (sprite == null) return;
        var origin = Art.Origin(path);
        var layer = new GameObject(node);
        layer.transform.SetParent(stall.transform,false);
        // Sprite already contains Canvas origin; cancel it when placing the common top-left.
        layer.transform.position = new Vector3((shop.x-sign.width/2+origin.x)/Art.PPU,
            (-shop.y+sign.height-2-origin.y)/Art.PPU,0);
        var renderer = layer.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite; renderer.sortingOrder = order;
    }
    public void Clear()
    {
        foreach (var employee in employees.Values) if (employee != null) employee.Remove();
        foreach (var stall in stalls.Values) if (stall != null) Destroy(stall);
        stalls.Clear();
        employees.Clear(); signboards.Clear(); shops.Clear(); nextRead = 0;
    }
    public bool TryVisit(Vector3 point, System.Func<int, SpriteRenderer> playerRenderer)
    {
        foreach (var shop in shops)
        {
            var renderer = shop.playerStore ? playerRenderer(shop.ownerId)
                : employees.TryGetValue(shop.ownerId,out var employee) ? employee.Image : null;
            if (renderer == null || renderer.sprite == null) continue;
            if (!renderer.bounds.Contains(new Vector3(point.x,point.y,renderer.bounds.center.z))) continue;
            // 点自家摊位：源端 visitShop 允许店主自己进店，这里直接把自己那扇窗重新打开。
            if (shop.playerStore && shop.ownerId == GameSession.CharacterId)
            {
                if (shop.permitId / 10000 == 514) NativePlayerShopWindow.Open(shop.permitId);
                else NativeMerchantWindow.Open(shop.permitId);
                Pointer.ConsumeClick(); return true;
            }
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
            if (signStyle == null)
                signStyle = new GUIStyle {font = UiKit.Font,fontSize = 12,padding = new RectOffset(),
                    margin = new RectOffset(),alignment = TextAnchor.UpperLeft,
                    clipping = TextClipping.Clip,wordWrap = false};
            signStyle.normal.textColor = Color.black;
            foreach (var shop in shops)
            {
                var sign = Signboard(shop.permitId);
                if (sign != null)
                {
                    // 皮肤画布含透明留白；原端从脚点减画布高度，不能再减店员高度。
                    var screen = camera.WorldToScreenPoint(new Vector3(shop.x/Art.PPU,-shop.y/Art.PPU,0));
                    if (screen.z >= 0)
                    {
                        var feetAnchor = new Vector2(screen.x/UiKit.Scale,(Screen.height-screen.y)/UiKit.Scale)/scale;
                        var signRect = new Rect(feetAnchor.x-sign.width/2,feetAnchor.y-sign.height+2,sign.width,sign.height);
                        GUI.DrawTexture(signRect,sign);
                        // 48C49C/48C4B6 取 Canvas origin；48DDDD/48DEF8 在 origin+(8,8)/(8,22)
                        // 用 font1（12px 黑字）写两行，48DD1E 将每行宽度限制为100px。
                        int skin = shop.permitId / 10000 == 503 ? shop.permitId : 5030000;
                        var origin = Art.Origin("wz://Item/Cash/0503.img/"+skin.ToString("D8")+"/skin/signboard");
                        DrawDescription(signRect.x+origin.x+8,signRect.y+origin.y+8,shop.description);
                        if (!shop.playerStore && Event.current.type == EventType.Repaint && Pointer.DoubleClicked && !Pointer.Blocked &&
                            !GameWindows.AnyVisible && signRect.Contains(Pointer.Pos/scale))
                        { NativeMarketWindow.Visit(shop.ownerId); Pointer.ConsumeClick(); }
                    }
                }
                if (shop.playerStore) continue;
                var feet = camera.WorldToScreenPoint(new Vector3(shop.x/Art.PPU,-shop.y/Art.PPU,0));
                if (feet.z < 0) continue;
                var anchor = new Vector2(feet.x/UiKit.Scale,(Screen.height-feet.y)/UiKit.Scale)/scale;
                string name = shop.ownerName+"的雇佣商店";
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
    void DrawDescription(float x, float y, string description)
    {
        if (string.IsNullOrEmpty(description)) return;
        string remaining = description.Replace("\r", "").Replace("\n", " ");
        for (int line = 0; line < 2 && remaining.Length > 0; line++)
        {
            int length = 0;
            while (length < remaining.Length &&
                signStyle.CalcSize(new GUIContent(remaining.Substring(0,length+1))).x <= 100)
                length++;
            if (length == 0) break;
            GUI.Label(new Rect(x,y+14*line,100,14),remaining.Substring(0,length),signStyle);
            remaining = remaining.Substring(length);
        }
    }
    void OnDestroy() => Clear();
}
