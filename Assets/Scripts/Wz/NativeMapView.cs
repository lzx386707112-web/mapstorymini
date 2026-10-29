using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MapleDash.Wz;

// Native IMG map geometry and animated artwork; game rules remain in the existing session.
public sealed class NativeMapView : MonoBehaviour
{
    static string MapPath(int id) => "Map/Map/Map" + id / 100000000 + "/" + id.ToString("D9") + ".img";
    static List<ImgNode> Children(ImgNode n) => n == null ? new List<ImgNode>() : n.Children;

    public static ImgNode MapRoot(int id)
    {
        var seen = new HashSet<int>();
        while (seen.Add(id))
        {
            var root = WzAssets.FileRoot(MapPath(id));
            var link = root.Get("info/link");
            if (link == null) return root;
            id = Convert.ToInt32(link.Value);
        }
        throw new InvalidDataException("Map link cycle: " + id);
    }

    // Static NPCs have no movement controller to settle the spawn y onto their
    // assigned foothold. Use that foothold, never a nearest unrelated platform.
    public static float NpcGroundY(ImgNode map, ImgNode life)
    {
        int id = life.Int("fh");
        float x = life.Int("x");
        if (id > 0)
            foreach (var layer in Children(map.Get("foothold")))
                foreach (var group in layer.Children)
                {
                    var f = group.Get(id.ToString());
                    if (f == null) continue;
                    float x1 = f.Int("x1"), x2 = f.Int("x2");
                    if (x1 == x2 || x < Math.Min(x1, x2) || x > Math.Max(x1, x2)) continue;
                    return f.Int("y1") + (f.Int("y2") - f.Int("y1")) * (x - x1) / (x2 - x1);
                }
        // Unattached/floating NPCs retain their authored position.
        return life.Int("y");
    }

    public static FieldMap LoadMap(FieldMap previous)
    {
        var n = MapRoot(previous.id);
        // MapFactory retains the requested map's infoData when mapData follows
        // info/link. Rules must not silently become the linked field's rules.
        var info = WzAssets.FileRoot(MapPath(previous.id)).Get("info");
        if (info == null) throw new InvalidDataException("Missing map info " + previous.id);
        var m = new FieldMap { id = previous.id, name = previous.name, street = previous.street,
            town = info.Int("town") != 0, returnMap = info.Int("returnMap"),
            fieldLimit = info.Int("fieldLimit"), fieldType = info.Int("fieldType"), bgm = info.Text("bgm"),
            createMobInterval = info.Int("createMobInterval", 5000),
            onUserEnter=info.Text("onUserEnter"),onFirstUserEnter=info.Text("onFirstUserEnter"),
            decHp=info.Int("decHP"),protectItem=info.Int("protectItem"),everlast=info.Int("everlast")!=0,
            forcedReturn=info.Int("forcedReturn",999999999),timeLimit=info.Int("timeLimit",-1),
            timeMobId=info.Get("timeMob")?.Int("id",-1) ?? -1,timeMobMessage=info.Get("timeMob")?.Text("message"),
            mobRate = info.Get("mobRate")?.Value == null ? 1f : Convert.ToSingle(info.Get("mobRate").Value) };
        foreach (var layer in Children(n.Get("foothold")))
            foreach (var group in layer.Children)
                foreach (var f in group.Children)
                    m.footholds.Add(new FieldFoothold { id = int.Parse(f.Name), layer = int.Parse(layer.Name),
                        group = int.Parse(group.Name), x1 = f.Int("x1"), y1 = f.Int("y1"), x2 = f.Int("x2"), y2 = f.Int("y2"),
                        forbidFallDown = f.Int("forbidFallDown"), force = f.Int("force"), drag = f.Int("drag") });
        foreach (var r in Children(n.Get("ladderRope")))
            m.ropes.Add(new FieldRope { x = r.Int("x"), y1 = r.Int("y1"), y2 = r.Int("y2"),
                ladder = r.Int("l") != 0, upperExit = r.Int("uf") != 0 });
        foreach (var p in Children(n.Get("portal")))
            m.portals.Add(new FieldPortal { name = p.Text("pn"), targetName = p.Text("tn"), type = p.Int("pt"),
                tm = p.Int("tm", 999999999), x = p.Int("x"), y = p.Int("y"), script = p.Text("script") });
        foreach (var life in Children(n.Get("life")))
        {
            if (life.Text("type") == "m") m.spawns.Add(new FieldSpawn { mob = int.Parse(life.Text("id")),
                mobTime = life.Int("mobTime"), fh = life.Int("fh"), team = life.Int("team", -1), dir = life.Int("f"),
                x = life.Int("x"), y = life.Int("y"), cy = life.Int("cy"), rx0 = life.Int("rx0"), rx1 = life.Int("rx1") });
            else if (life.Text("type") == "n") m.npcs.Add(new FieldNpc { id = int.Parse(life.Text("id")),
                hide = life.Int("hide"), x = life.Int("x"), y = NpcGroundY(n, life),
                head = NpcHeadHeight(int.Parse(life.Text("id"))) });
        }
        // Use source VR when present; maps without VR use a provisional foothold-based camera envelope.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var f in m.footholds)
        {
            minX = Math.Min(minX, (int)Math.Min(f.x1, f.x2)); maxX = Math.Max(maxX, (int)Math.Max(f.x1, f.x2));
            minY = Math.Min(minY, (int)Math.Min(f.y1, f.y2)); maxY = Math.Max(maxY, (int)Math.Max(f.y1, f.y2));
        }
        if (m.footholds.Count == 0) { minX = -400; maxX = 400; minY = 0; maxY = 300; }
        var geometryInfo = n.Get("info");
        m.vr = new[] { geometryInfo.Int("VRLeft", minX), geometryInfo.Int("VRTop", minY - 300),
            geometryInfo.Int("VRRight", maxX), geometryInfo.Int("VRBottom", maxY + 100) };
        return m;
    }

    readonly HashSet<string> files = new HashSet<string>();
    readonly HashSet<ImgNode> framesUsed = new HashSet<ImgNode>();
    readonly List<Animation> animations = new List<Animation>();
    readonly List<Background> backgrounds = new List<Background>();
    readonly HashSet<SpriteRenderer> weatherSkyBacks = new HashSet<SpriteRenderer>();
    NativeWeatherField weather;
    public int DrawCount { get; private set; }
    public int TextureCount => framesUsed.Count;
    public int FileCount => files.Count;
    ImgNode FileRoot(string relative)
    {
        files.Add(relative); return WzAssets.FileRoot(relative);
    }
    Sprite Sprite(ImgNode frame)
    {
        framesUsed.Add(frame); return WzAssets.Sprite(frame);
    }
    sealed class Animation
    {
        public SpriteRenderer Renderer;
        public Sprite[] Frames;
        public float[] Delays;
        public float Cycle;
        public void Tick(float time)
        {
            float t = time % Cycle;
            int frame = 0;
            while (frame < Delays.Length - 1 && t >= Delays[frame]) t -= Delays[frame++];
            Renderer.sprite = Frames[frame];
        }
    }
    Animation Draw(ImgNode node, string name, float x, float y, int order, bool flip = false, int alpha = 255, int defaultDelay = 100)
    {
        if (node == null) throw new InvalidDataException("Missing IMG artwork " + name);
        node = node.Resolve();
        var frames = new List<ImgNode>();
        if (node.Kind == "Canvas") frames.Add(node);
        else
        {
            var indexed = new List<ImgNode>();
            foreach (var child in node.Children) if (int.TryParse(child.Name, out _)) indexed.Add(child);
            indexed.Sort((a, b) => int.Parse(a.Name).CompareTo(int.Parse(b.Name)));
            foreach (var child in indexed)
            {
                // 悬空 UOL 用不抛异常的解析：一帧坏链接不该打断整段收集。
                var f = child.TryResolve();
                if (f != null && f.Kind == "Canvas") frames.Add(f);
            }
        }
        if (frames.Count == 0) return null;
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(x / Art.PPU, -y / Art.PPU, 0);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sortingOrder = order; sr.flipX = flip;
        sr.color = new Color(1, 1, 1, alpha / 255f);
        var a = new Animation { Renderer = sr, Frames = new Sprite[frames.Count], Delays = new float[frames.Count] };
        for (int i = 0; i < frames.Count; i++)
        {
            a.Frames[i] = Sprite(frames[i]); a.Delays[i] = Math.Max(1, Math.Abs(frames[i].Int("delay", defaultDelay))) / 1000f;
            a.Cycle += a.Delays[i];
        }
        sr.sprite = a.Frames[0];
        if (frames.Count > 1) animations.Add(a);
        DrawCount++; return a;
    }
    sealed class Background
    {
        public ImgNode Definition;
        public Animation Art;
        public readonly List<SpriteRenderer> Cells = new List<SpriteRenderer>();
    }
    sealed class MapDraw
    {
        public ImgNode Definition, Art;
        public long Z;
        public int Layer, Sequence;
    }
    // 原端 MakeObj 0x63C263：解析不到素材节点时只跳过一次引用计数调用
    // （cmp eax,edi / je 0x63c275），随后照常算 z 并创建对象，函数不会中断。
    // RestoreTile 同理。也就是说缺一件素材只少那一件，绝不会让整张地图加载失败。
    // 本实现等价地「跳过该条目并告警」：不生成空渲染器，效果与原端一致。
    // 修复前这里直接 throw，导致 69 张地图（eventCN1/blizzardCastle/城SG 等
    // 数据里本来就引用了本版 Data 不存在的素材）整图渲染失败、白屏。
    readonly HashSet<string> reportedMissing = new HashSet<string>();
    public int MissingArt { get; private set; }
    // 与 Draw() 的帧收集一致：节点本身是 Canvas，或数字子节点里至少一帧可解析。
    static bool Drawable(ImgNode node)
    {
        var n = node?.TryResolve();
        if (n == null) return false;
        if (n.Kind == "Canvas") return true;
        foreach (var child in n.Children)
            if (int.TryParse(child.Name, out _) && child.TryResolve()?.Kind == "Canvas") return true;
        return false;
    }
    void Missing(ImgNode owner, int mapId, string kind, string path, string label)
    {
        MissingArt++;
        if (reportedMissing.Add(kind + "/" + path))
            Debug.LogWarning("[NativeMapView] 地图 " + mapId + " 缺少 " + kind + " 素材 " + path +
                             "（节点 " + owner.Name + "/" + label + "），按原端行为跳过");
    }
    // NPC img 支持 info/link 链；链上任一文件缺失返回 null，由调用方跳过。
    static ImgNode NpcRoot(string npcId)
    {
        string file = "Npc/" + npcId + ".img";
        for (int i = 0; i < 16; i++)
        {
            if (!WzAssets.HasFile(file)) return null;
            var root = WzAssets.FileRoot(file);
            var link = root.Get("info/link");
            if (link == null) return root;
            file = "Npc/" + Convert.ToInt32(link.Value).ToString("D7") + ".img";
        }
        return null;
    }
    // NPC 名字/任务图标的锚点：站姿首帧 origin.y 就是脚底到画面顶端的像素高度。
    // 原来固定 82px 会让高 NPC 的名字压在头上、矮 NPC 的名字悬空。
    static float NpcHeadHeight(int npcId)
    {
        try
        {
            var root = NpcRoot(npcId.ToString("D7"));
            var frames = root == null ? null : WzAssets.Frames(root.Get("stand"));
            if (frames == null || frames.Count == 0) return 80f;
            float originY = WzAssets.Origin(frames[0]).y;
            return originY > 4f ? originY : WzAssets.Canvas(frames[0]).Height;
        }
        catch (Exception) { return 80f; }
    }
    public void Show(int id)
    {
        var map = MapRoot(id);
        var body = new GameObject("MapLayers"); body.transform.SetParent(transform, false);
        body.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = -400;
        // Native layer z: RestoreTile 0x0063A91F and MakeObj 0x0063C289.
        // Tiles and objects have different base bands; sorting both by their small z fields is wrong.
        var draws = new List<MapDraw>();
        for (int layer = 0; layer < 9; layer++)
        {
            var n = map.Get(layer.ToString());
            if (n == null) continue;
            string tileSet = n.Text("info/tS");
            string tileFile = "Map/Tile/" + tileSet + ".img";
            bool hasTiles = !string.IsNullOrEmpty(tileSet) && WzAssets.HasFile(tileFile);
            foreach (var d in Children(n.Get("tile")))
            {
                var art = hasTiles ? FileRoot(tileFile).Get(d.Text("u") + "/" + d.Int("no")) : null;
                if (!Drawable(art)) { Missing(n, id, "tile", tileSet + "/" + d.Text("u") + "/" + d.Int("no"), d.Name); continue; }
                draws.Add(new MapDraw { Definition = d, Art = art, Layer = layer,
                    Z = art.Resolve().Int("z") + 10L * (3000L * layer - d.Int("zM")) - 0x3FFFB1EA, Sequence = draws.Count });
            }
            foreach (var d in Children(n.Get("obj")))
            {
                string oS = d.Text("oS");
                string objFile = "Map/Obj/" + oS + ".img";
                var art = WzAssets.HasFile(objFile) ? FileRoot(objFile).Get(d.Text("l0") + "/" + d.Text("l1") + "/" + d.Text("l2")) : null;
                if (!Drawable(art)) { Missing(n, id, "obj", oS + "/" + d.Text("l0") + "/" + d.Text("l1") + "/" + d.Text("l2"), d.Name); continue; }
                draws.Add(new MapDraw { Definition = d, Art = art, Layer = layer,
                    Z = d.Int("z") + 30000L * layer - 0x3FFFF830, Sequence = draws.Count });
            }
        }
        draws.Sort((a, b) => { int z = a.Z.CompareTo(b.Z); return z == 0 ? a.Sequence.CompareTo(b.Sequence) : z; });
        for (int i = 0; i < draws.Count; i++)
        {
            var item = draws[i]; var d = item.Definition;
            var draw = Draw(item.Art, d.Parent.Name + "_" + item.Layer + "_" + d.Name, d.Int("x"), d.Int("y"), i, d.Int("f") != 0);
            if (draw == null) continue;
            draw.Renderer.transform.SetParent(body.transform, false);
        }
        foreach (var b in Children(map.Get("back")))
        {
            if (b.Text("bS") == "") continue;
            string backFile = "Map/Back/" + b.Text("bS") + ".img";
            string kind = b.Int("ani") != 0 ? "ani" : "back";
            var root = WzAssets.HasFile(backFile) ? FileRoot(backFile) : null;
            var art = root != null ? root.Get(kind + "/" + b.Int("no")) : null;
            if (!Drawable(art)) { Missing(b, id, "back", b.Text("bS") + "/" + kind + "/" + b.Int("no"), b.Name); continue; }
            var a = Draw(art, "Back_" + b.Name, 0, 0,
                (b.Int("front") != 0 ? 600 : -800) + int.Parse(b.Name), b.Int("f") != 0, b.Int("a", 255));
            if (a == null) continue;
            a.Renderer.enabled = false;
            backgrounds.Add(new Background { Definition = b, Art = a });
        }
        foreach (var life in Children(map.Get("life")))
        {
            if (life.Text("type") != "n" || life.Int("hide") != 0) continue;
            string npcId = int.Parse(life.Text("id")).ToString("D7");
            var npc = NpcRoot(npcId);
            if (!Drawable(npc?.Get("stand"))) { Missing(life, id, "npc", npcId + "/stand", life.Name); continue; }
            Draw(npc.Get("stand"), "Npc_" + npcId, life.Int("x"), NpcGroundY(map, life), 100, life.Int("f") != 0);
        }
        foreach (var p in Children(map.Get("portal")))
            if (p.Int("pt") == 2) Draw(FileRoot("Map/MapHelper.img").Get("portal/game/pv"), "Portal_" + p.Name, p.Int("x"), p.Int("y"), 200, defaultDelay: 120);
        weather = gameObject.AddComponent<NativeWeatherField>();
        weather.Bind(this, id, map);
        Debug.Log("[DirectIMG] Map=" + id + " files=" + FileCount + " draws=" + DrawCount + " textures=" + TextureCount +
                  " animations=" + animations.Count + " missingArt=" + MissingArt);
    }
    void LateUpdate()
    {
        foreach (var a in animations) a.Tick(Time.timeSinceLevelLoad);
        weather?.Tick();
        if (Camera.main != null) UpdateBackgrounds(Camera.main, Time.timeSinceLevelLoad);
    }
    public SpriteRenderer AddWeatherBack(Dictionary<string,object> row, int order)
    {
        string file = "Map/Back/" + (string)row["bank"] + ".img";
        var node = FileRoot(file).Get((Convert.ToInt32(row["ani"])!=0 ? "ani/" : "back/") + Convert.ToInt32(row["no"]));
        if (!Drawable(node)) throw new InvalidDataException("Missing original weather art " + file);
        var b = new ImgNode { Name = "weather", Kind = "Property" };
        foreach (var entry in row)
            b.Children.Add(new ImgNode {Name=entry.Key,Kind="Int",Value=entry.Value,Parent=b});
        if ((bool)row["sizePitch"])
        {
            var canvas = WzAssets.Canvas(node.Resolve().Kind=="Canvas" ? node : node.Get("0"));
            b.Get("cx").Value=canvas.Width; b.Get("cy").Value=canvas.Height;
        }
        var a=Draw(node,"Weather_"+row["group"],0,0,order);
        if(a==null)return null;
        a.Renderer.enabled=false; a.Renderer.color=new Color(1,1,1,0);
        backgrounds.Add(new Background {Definition=b,Art=a});return a.Renderer;
    }
    public void PromoteWeatherBacks(ImgNode map)
    {
        var first=map.Get("back/0");
        foreach(var back in backgrounds)
        {
            var b=back.Definition;
            if(b.Name=="weather" || b.Int("front")!=0)continue;
            int index=int.Parse(b.Name);
            bool repeated=string.Equals(b.Text("bS"),first.Text("bS"),StringComparison.OrdinalIgnoreCase) && b.Int("no")==first.Int("no");
            if(index<2 || repeated) {back.Art.Renderer.sortingOrder=-1200+index;weatherSkyBacks.Add(back.Art.Renderer);}
        }
    }
    public void SetWeatherSkyVisibility(bool hidden)
    {
        foreach(var image in weatherSkyBacks)if(image!=null) {var color=image.color;color.a=hidden?0:1;image.color=color;}
    }
    public void UpdateBackgrounds(Camera camera, float elapsed)
    {
        float mx = (camera.transform.position.x - transform.position.x) * Art.PPU;
        float my = -(camera.transform.position.y - transform.position.y) * Art.PPU;
        float hy = camera.orthographicSize * Art.PPU, hx = hy * camera.aspect;
        foreach (var back in backgrounds)
        {
            var b = back.Definition; var source = back.Art.Renderer;
            int type = b.Int("type"); bool sx = type == 4 || type == 6, sy = type == 5 || type == 7;
            bool rx = type == 1 || type == 3 || type == 4 || type == 6 || type == 7;
            bool ry = type == 2 || type == 3 || type == 5 || type == 6 || type == 7;
            var sp = source.sprite; float w = sp.rect.width, h = sp.rect.height;
            float bx = b.Int("x") + mx * (1f + (sx ? 0 : b.Int("rx") / 100f));
            float by = b.Int("y") + my * (1f + (sy ? 0 : b.Int("ry") / 100f));
            if (sx) bx -= elapsed * 5 * b.Int("rx"); if (sy) by -= elapsed * 5 * b.Int("ry");
            float stepX = b.Int("cx") > 0 ? b.Int("cx") : w, stepY = b.Int("cy") > 0 ? b.Int("cy") : h;
            float left = bx - (source.flipX ? w - sp.pivot.x : sp.pivot.x), top = by - (h - sp.pivot.y);
            int x0 = rx ? Mathf.FloorToInt((mx - hx - left - w) / stepX) + 1 : 0;
            int x1 = rx ? Mathf.CeilToInt((mx + hx - left) / stepX) - 1 : 0;
            int y0 = ry ? Mathf.FloorToInt((my - hy - top - h) / stepY) + 1 : 0;
            int y1 = ry ? Mathf.CeilToInt((my + hy - top) / stepY) - 1 : 0;
            int count = 0;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                if (count == back.Cells.Count)
                {
                    var go = new GameObject("BackCell"); go.transform.SetParent(transform, false);
                    back.Cells.Add(go.AddComponent<SpriteRenderer>());
                }
                var sr = back.Cells[count++]; sr.enabled = true; sr.sprite = sp;
                sr.sortingOrder = source.sortingOrder; sr.flipX = source.flipX; sr.color = source.color;
                sr.transform.localPosition = new Vector3((bx + x * stepX) / Art.PPU, -(by + y * stepY) / Art.PPU, 0);
            }
            for (int i = count; i < back.Cells.Count; i++) back.Cells[i].enabled = false;
        }
    }
}
