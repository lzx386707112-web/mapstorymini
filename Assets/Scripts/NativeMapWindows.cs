using System;
using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// Original miniMap coordinates: (map position + center) / 2^mag.
public static class NativeMapWindows
{
    public static bool WorldVisible;
    static int miniMode = 2;
    static string worldFile;
    static readonly List<string> worldFiles = new List<string>();
    public static void ToggleMini() { miniMode = (miniMode + 1) % 3; }
    public static void ToggleWorld()
    {
        WorldVisible = !WorldVisible;
        if (!WorldVisible) return;
        if (worldFiles.Count == 0)
            foreach (Dictionary<string, object> file in (List<object>)WzAssets.Index["files"])
            {
                string path = (string)file["path"];
                if (path.StartsWith("Map/WorldMap/WorldMap", StringComparison.Ordinal) && path.EndsWith(".img", StringComparison.Ordinal))
                    worldFiles.Add(path);
            }
        worldFiles.Sort(StringComparer.Ordinal);
        worldFile = "Map/WorldMap/WorldMap.img";
        int best = int.MaxValue;
        foreach (string path in worldFiles)
        {
            var maps = WzAssets.FileRoot(path).Get("MapList");
            if (maps == null) continue;
            int count = 0; bool found = false;
            foreach (var spot in maps.Children)
                foreach (var id in spot.Get("mapNo")?.Children ?? new List<ImgNode>())
                { count++; if (Convert.ToInt32(id.Value) == Profile.LastMapId) found = true; }
            if (found && count < best) { best = count; worldFile = path; }
        }
    }
    static GUIStyle Label(int size = 12)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = size, wordWrap = true };
        style.normal.textColor = Color.white; return style;
    }
    static void Marker(ImgNode icon, Vector2 position, float scale)
    {
        if (icon == null) return;
        var tex = WzAssets.Texture(icon); var origin = WzAssets.Origin(icon);
        GUI.DrawTexture(new Rect(position.x - origin.x * scale, position.y - origin.y * scale,
            tex.width * scale, tex.height * scale), tex);
    }
    public static void DrawMini()
    {
        if (miniMode == 0) return;
        var map = FieldCatalog.GetMap(Profile.LastMapId);
        if (map == null) return;
        var mini = WzAssets.FileRoot(WzAssets.MapPath(map.id)).Get("miniMap");
        var canvas = mini?.Get("canvas");
        float s = BottomBar.S;
        var tex = canvas != null ? WzAssets.Texture(canvas) : null;
        float width = Mathf.Max(160, tex != null ? tex.width : 160);
        var panel = new Rect(8, 8, (width + 12) * s, (miniMode == 1 ? 30 : (tex != null ? tex.height : 0) + 64) * s);
        UiKit.Fill(panel, new Color(.05f, .08f, .12f, .86f));
        GUI.Label(new Rect(panel.x + 6 * s, panel.y + 4 * s, panel.width - 32 * s, 22 * s), map.name, Label(Mathf.RoundToInt(12 * s)));
        if (GUI.Button(new Rect(panel.xMax - 24 * s, panel.y + 3 * s, 21 * s, 22 * s), "−")) ToggleMini();
        if (miniMode != 2 || tex == null) return;
        var r = new Rect(panel.x + 6 * s, panel.y + 52 * s, tex.width * s, tex.height * s);
        GUI.DrawTexture(r, tex);
        GUI.Label(new Rect(panel.x + 6 * s, panel.y + 26 * s, panel.width - 12 * s, 22 * s), map.street, Label(Mathf.RoundToInt(11 * s)));
        float divisor = Mathf.Pow(2, mini.Int("mag", 4));
        Func<float, float, Vector2> point = (x, y) => r.position + new Vector2((x + mini.Int("centerX")) / divisor, (y + mini.Int("centerY")) / divisor) * s;
        var helper = WzAssets.FileRoot("Map/MapHelper.img").Get("minimap");
        foreach (var portal in map.portals)
            if (portal.type == 2 || portal.type == 7) Marker(helper.Get("portal"), point(portal.x, portal.y), s);
        foreach (var npc in map.npcs)
            if (npc.hide == 0) Marker(helper.Get("npc"), point(npc.x, npc.y), s);
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player != null) Marker(helper.Get("user"), point(player.FeetPos.x * Art.PPU, -player.FeetPos.y * Art.PPU), s);
    }
    public static void DrawWorld()
    {
        if (!WorldVisible) return;
        var root = WzAssets.FileRoot(worldFile);
        var frames = root.Get("BaseImg");
        if (frames == null) return;
        float s = Mathf.Min(BottomBar.S, (UiKit.W - 32) / 660, (UiKit.H - 64) / 520);
        var panel = new Rect((UiKit.W - 660 * s) / 2, 20, 660 * s, 520 * s);
        UiKit.Fill(panel, new Color(.08f, .12f, .16f, .98f));
        var anchor = panel.position + new Vector2(330, 273) * s;
        foreach (var frame in frames.Children) Marker(frame, anchor, s);
        var maps = root.Get("MapList");
        string hover = "";
        if (maps != null)
            foreach (var spot in maps.Children)
            {
                var p = spot.Get("spot"); if (p == null) continue;
                var center = anchor + new Vector2(p.X, p.Y) * s;
                var names = new List<string>(); bool current = false;
                foreach (var id in spot.Get("mapNo")?.Children ?? new List<ImgNode>())
                {
                    int number = Convert.ToInt32(id.Value);
                    if (number == Profile.LastMapId) current = true;
                    if (FieldCatalog.Maps.TryGetValue(number, out var map)) names.Add(map.name);
                }
                Marker(WzAssets.Node("Map/MapHelper.img/worldMap/mapImage/" + spot.Int("type")), center, s);
                if (current) Marker(WzAssets.Node("Map/MapHelper.img/worldMap/curPos/0"), center, s);
                if (new Rect(center.x - 12 * s, center.y - 12 * s, 24 * s, 24 * s).Contains(Pointer.Pos)) hover = string.Join(" / ", names);
            }
        var links = root.Get("MapLink");
        if (links != null)
            foreach (var link in links.Children)
            {
                var image = link.Get("link/linkImg"); if (image == null) continue;
                var origin = WzAssets.Origin(image); var tex = WzAssets.Texture(image);
                var hit = new Rect(anchor.x - origin.x * s, anchor.y - origin.y * s, tex.width * s, tex.height * s);
                if (!hit.Contains(Pointer.Pos)) continue;
                Marker(image, anchor, s); hover = link.Text("toolTip");
                if (Event.current.type == EventType.Repaint && Pointer.Clicked)
                { worldFile = "Map/WorldMap/" + link.Text("link/linkMap") + ".img"; return; }
            }
        GUI.Label(new Rect(panel.x + 12 * s, panel.y + 5 * s, 400 * s, 25 * s), "大地图", Label(Mathf.RoundToInt(14 * s)));
        if (GUI.Button(new Rect(panel.xMax - 30 * s, panel.y + 4 * s, 24 * s, 24 * s), "×")) WorldVisible = false;
        string parent = root.Text("info/parentMap");
        if (parent != "" && GUI.Button(new Rect(panel.x + 12 * s, panel.yMax - 30 * s, 80 * s, 24 * s), "上级地图")) worldFile = "Map/WorldMap/" + parent + ".img";
        GUI.Label(new Rect(panel.x + 100 * s, panel.yMax - 30 * s, panel.width - 120 * s, 28 * s), hover, Label(Mathf.RoundToInt(12 * s)));
    }
}
