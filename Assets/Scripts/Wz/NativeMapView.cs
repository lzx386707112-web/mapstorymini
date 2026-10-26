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
        var n = MapRoot(previous.id); var info = n.Get("info");
        if (info == null) throw new InvalidDataException("Missing map info " + previous.id);
        var m = new FieldMap { id = previous.id, name = previous.name, street = previous.street,
            town = info.Int("town") != 0, returnMap = info.Int("returnMap"),
            fieldLimit = info.Int("fieldLimit"), bgm = info.Text("bgm"), mobRate = info.Get("mobRate")?.Value == null ? 1f : Convert.ToSingle(info.Get("mobRate").Value) };
        foreach (var layer in Children(n.Get("foothold")))
            foreach (var group in layer.Children)
                foreach (var f in group.Children)
                    m.footholds.Add(new FieldFoothold { id = int.Parse(f.Name), layer = int.Parse(layer.Name),
                        group = int.Parse(group.Name), x1 = f.Int("x1"), y1 = f.Int("y1"), x2 = f.Int("x2"), y2 = f.Int("y2"),
                        forbidFallDown = f.Int("forbidFallDown") });
        foreach (var r in Children(n.Get("ladderRope")))
            m.ropes.Add(new FieldRope { x = r.Int("x"), y1 = r.Int("y1"), y2 = r.Int("y2"),
                ladder = r.Int("l") != 0, upperExit = r.Int("uf") != 0 });
        foreach (var p in Children(n.Get("portal")))
            m.portals.Add(new FieldPortal { name = p.Text("pn"), targetName = p.Text("tn"), type = p.Int("pt"),
                tm = p.Int("tm"), x = p.Int("x"), y = p.Int("y"), script = p.Text("script") });
        foreach (var life in Children(n.Get("life")))
        {
            if (life.Text("type") == "m") m.spawns.Add(new FieldSpawn { mob = int.Parse(life.Text("id")),
                mobTime = life.Int("mobTime"), fh = life.Int("fh"), team = life.Int("team", -1), dir = life.Int("f"),
                x = life.Int("x"), y = life.Int("y"), cy = life.Int("cy"), rx0 = life.Int("rx0"), rx1 = life.Int("rx1") });
            else if (life.Text("type") == "n") m.npcs.Add(new FieldNpc { id = int.Parse(life.Text("id")),
                hide = life.Int("hide"), x = life.Int("x"), y = NpcGroundY(n, life) });
        }
        // Use source VR when present; maps without VR use a provisional foothold-based camera envelope.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var f in m.footholds)
        {
            minX = Math.Min(minX, (int)Math.Min(f.x1, f.x2)); maxX = Math.Max(maxX, (int)Math.Max(f.x1, f.x2));
            minY = Math.Min(minY, (int)Math.Min(f.y1, f.y2)); maxY = Math.Max(maxY, (int)Math.Max(f.y1, f.y2));
        }
        if (m.footholds.Count == 0) { minX = -400; maxX = 400; minY = 0; maxY = 300; }
        m.vr = new[] { info.Int("VRLeft", minX), info.Int("VRTop", minY - 300),
            info.Int("VRRight", maxX), info.Int("VRBottom", maxY + 100) };
        return m;
    }

    readonly HashSet<string> files = new HashSet<string>();
    readonly HashSet<ImgNode> framesUsed = new HashSet<ImgNode>();
    readonly List<Animation> animations = new List<Animation>();
    readonly List<Background> backgrounds = new List<Background>();
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
                var f = child.Resolve();
                if (f.Kind == "Canvas") frames.Add(f);
            }
        }
        if (frames.Count == 0) throw new InvalidDataException("No Canvas frames " + name);
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
            foreach (var d in Children(n.Get("tile")))
            {
                var art = FileRoot("Map/Tile/" + n.Text("info/tS") + ".img").Get(d.Text("u") + "/" + d.Int("no"));
                if (art == null) throw new InvalidDataException("Missing tile " + d.Name);
                draws.Add(new MapDraw { Definition = d, Art = art, Layer = layer,
                    Z = art.Resolve().Int("z") + 10L * (3000L * layer - d.Int("zM")) - 0x3FFFB1EA, Sequence = draws.Count });
            }
            foreach (var d in Children(n.Get("obj")))
            {
                var art = FileRoot("Map/Obj/" + d.Text("oS") + ".img").Get(d.Text("l0") + "/" + d.Text("l1") + "/" + d.Text("l2"));
                draws.Add(new MapDraw { Definition = d, Art = art, Layer = layer,
                    Z = d.Int("z") + 30000L * layer - 0x3FFFF830, Sequence = draws.Count });
            }
        }
        draws.Sort((a, b) => { int z = a.Z.CompareTo(b.Z); return z == 0 ? a.Sequence.CompareTo(b.Sequence) : z; });
        for (int i = 0; i < draws.Count; i++)
        {
            var item = draws[i]; var d = item.Definition;
            var draw = Draw(item.Art, d.Parent.Name + "_" + item.Layer + "_" + d.Name, d.Int("x"), d.Int("y"), i, d.Int("f") != 0);
            draw.Renderer.transform.SetParent(body.transform, false);
        }
        foreach (var b in Children(map.Get("back")))
        {
            if (b.Text("bS") == "") continue;
            var root = FileRoot("Map/Back/" + b.Text("bS") + ".img");
            string kind = b.Int("ani") != 0 ? "ani" : "back";
            var art = root.Get(kind + "/" + b.Int("no"));
            var a = Draw(art, "Back_" + b.Name, 0, 0,
                (b.Int("front") != 0 ? 600 : -800) + int.Parse(b.Name), b.Int("f") != 0, b.Int("a", 255));
            a.Renderer.enabled = false;
            backgrounds.Add(new Background { Definition = b, Art = a });
        }
        foreach (var life in Children(map.Get("life")))
        {
            if (life.Text("type") != "n" || life.Int("hide") != 0) continue;
            string npcId = int.Parse(life.Text("id")).ToString("D7");
            var npc = FileRoot("Npc/" + npcId + ".img");
            if (npc.Get("info/link") != null) npc = FileRoot("Npc/" + Convert.ToInt32(npc.Get("info/link").Value).ToString("D7") + ".img");
            Draw(npc.Get("stand"), "Npc_" + npcId, life.Int("x"), NpcGroundY(map, life), 100, life.Int("f") != 0);
        }
        foreach (var p in Children(map.Get("portal")))
            if (p.Int("pt") == 2) Draw(FileRoot("Map/MapHelper.img").Get("portal/game/pv"), "Portal_" + p.Name, p.Int("x"), p.Int("y"), 200, defaultDelay: 120);
        Debug.Log("[DirectIMG] Map=" + id + " files=" + FileCount + " draws=" + DrawCount + " textures=" + TextureCount + " animations=" + animations.Count);
    }
    void LateUpdate()
    {
        foreach (var a in animations) a.Tick(Time.timeSinceLevelLoad);
        if (Camera.main != null) UpdateBackgrounds(Camera.main, Time.timeSinceLevelLoad);
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
