using System.Collections.Generic;
using Server.Bot;
using UnityEngine;

// CEntrustedShop 50D56C/50D97B: employee frames are attached to
// the map position and retain their original origin and frame delays.
// 店招：0x97A3FB（店铺场地对象构建）同时加载 Item/Cash/0503.img/{permit}/skin 的
// shop/backgrnd/signboard 与 employee——雇佣商人头顶除名牌外还挂着 166x166 的 signboard
// 店招（写店名/描述）；此前按「人物商店展示分支」的误判只出了商人立绘，漏了招牌。
public sealed class NativeMarketField : MonoBehaviour
{
    readonly Dictionary<int, NativeImgAnimation> employees = new Dictionary<int, NativeImgAnimation>();
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
        }
        removed.Clear();
        foreach (var employee in employees) if (!present.Contains(employee.Key)) removed.Add(employee.Key);
        foreach (int owner in removed) { employees[owner].Remove(); employees.Remove(owner); }
    }
    public void Clear()
    {
        foreach (var employee in employees.Values) if (employee != null) employee.Remove();
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
                    margin = new RectOffset(),alignment = TextAnchor.UpperCenter,
                    clipping = TextClipping.Clip,wordWrap = true};
            signStyle.normal.textColor = Color.white;
            foreach (var shop in shops)
            {
                // 店招挂点：商人立绘 stand 帧 80px 高（0503 全系 47x80），再留 6px 链距。
                // signboard 166x166，水平以商人脚点为中心。
                float headTop = shop.y - 86f;
                var screen = camera.WorldToScreenPoint(new Vector3((shop.x-83f)/Art.PPU,-(headTop-166f)/Art.PPU,0));
                if (screen.z >= 0)
                {
                    var signAnchor = new Vector2(screen.x/UiKit.Scale,(Screen.height-screen.y)/UiKit.Scale)/scale;
                    var sign = Signboard(shop.permitId);
                    if (sign != null)
                    {
                        GUI.DrawTexture(new Rect(signAnchor.x,signAnchor.y,sign.width,sign.height),sign);
                        if (!string.IsNullOrWhiteSpace(shop.description))
                        {
                            // 不同 permit 皮肤底色深浅不一（05030000 是白板），白字加黑描边保证可读。
                            var textRect = new Rect(signAnchor.x+13f,signAnchor.y+42f,140f,64f);
                            signStyle.normal.textColor = Color.black;
                            GUI.Label(new Rect(textRect.x-1,textRect.y+1,textRect.width,textRect.height),shop.description,signStyle);
                            GUI.Label(new Rect(textRect.x+1,textRect.y+1,textRect.width,textRect.height),shop.description,signStyle);
                            signStyle.normal.textColor = Color.white;
                            GUI.Label(textRect,shop.description,signStyle);
                        }
                    }
                }
                if (shop.playerStore) continue;
                var feet = camera.WorldToScreenPoint(new Vector3(shop.x/Art.PPU,-shop.y/Art.PPU,0));
                if (feet.z < 0) continue;
                var anchor = new Vector2(feet.x/UiKit.Scale,(Screen.height-feet.y)/UiKit.Scale)/scale;
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
