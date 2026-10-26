using System;
using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// Original miniMap coordinates: (map position + center) / 2^mag.
public static class NativeMapWindows
{
    public static bool WorldVisible;
    // EXE: 0=MaxMap, 1=MinMap, 2=Min (0x856400).
    static int miniMode;
    static string worldFile;
    static readonly List<string> worldFiles = new List<string>();
    public static void ToggleMini() { miniMode = (miniMode + 2) % 3; }
    // Esc 关闭大地图：只收起，不重算当前区域
    public static void CloseWorld() { WorldVisible = false; }
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
        var map = FieldCatalog.GetMap(Profile.LastMapId);
        if (map == null) return;
        var mapRoot = WzAssets.FileRoot(WzAssets.MapPath(map.id));
        var mini = mapRoot.Get("miniMap");
        var canvas = mini?.Get("canvas");
        float s = BottomBar.S;
        var tex = canvas != null ? WzAssets.Texture(canvas) : null;
        // EXE 0x8588ff: large canvases use the native compact viewport.
        float mapWidth = tex != null ? (tex.width >= 280 ? 210 : tex.width) : 0;
        float mapHeight = tex != null ? (tex.height >= 150 ? 112 : tex.height) : 0;
        string markName = mapRoot.Get("info/mapMark")?.Value as string;
        var mark = string.IsNullOrEmpty(markName) ? null : Art.Tex("wz://Map/MapHelper.img/mark/" + markName);
        var miniLabel = Label(Mathf.RoundToInt(12 * s));
        miniLabel.wordWrap = false;
        float markSpace = miniMode == 0 && mark != null ? 40 : 0;
        float streetWidth = miniLabel.CalcSize(new GUIContent(map.street)).x / s + markSpace + 5;
        float nameWidth = miniLabel.CalcSize(new GUIContent(map.name)).x / s + markSpace + 5;
        // EXE 0x8563c1/0x856400: MaxMap accommodates both labels and the viewport;
        // Min uses the combined label widths plus 80, MinMap only the viewport.
        float contentWidth = miniMode == 0 ? Mathf.Max(mapWidth, Mathf.Max(streetWidth, nameWidth)) : mapWidth;
        float width = miniMode == 2 ? streetWidth + nameWidth + 80 : Mathf.Max(120, contentWidth + 12);
        float header = miniMode == 0 ? 72 : 29;
        float height = miniMode == 2 ? 20 : mapHeight + (miniMode == 0 ? 87 : 43);
        var panel = new Rect(8, 8, width * s, height * s);
        const string root = "UI/UIWindow.img/MiniMap/";
        if (miniMode == 2)
        {
            DrawPart(root + "Min/w", panel.x, panel.y, 8 * s, panel.height);
            DrawPart(root + "Min/c", panel.x + 8 * s, panel.y, panel.width - 12 * s, panel.height);
            DrawPart(root + "Min/e", panel.xMax - 4 * s, panel.y, 4 * s, panel.height);
        }
        else DrawMiniFrame(panel, root + (miniMode == 0 ? "MaxMap/" : "MinMap/"), header * s,
            (miniMode == 0 ? 15 : 14) * s, s);
        // EXE 0x857616: title canvas is copied at (8,10).
        var title = Art.Tex("wz://" + root + "title");
        if (title != null) GUI.DrawTexture(new Rect(panel.x + 8 * s, panel.y + 10 * s, title.width * s, title.height * s), title);
        // EXE 0x8583ea: controls at W-42/y6, collapsed W-44/y4.
        float buttonY = panel.y + (miniMode == 2 ? 4 : 6) * s;
        float buttonX = panel.xMax - (miniMode == 2 ? 44 : 42) * s;
        if (NativeUi.Button(new Rect(buttonX, buttonY, 36 * s, 12 * s), root + "BtMap")) ToggleWorld();
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && miniMode != 0 && tex != null;
        bool maximize = NativeUi.Button(new Rect(buttonX - 14 * s, buttonY, 12 * s, 12 * s), "UI/Basic.img/BtMax");
        GUI.enabled = enabled && miniMode != 2 && tex != null;
        bool minimize = NativeUi.Button(new Rect(buttonX - 27 * s, buttonY, 12 * s, 12 * s), "UI/Basic.img/BtMin");
        GUI.enabled = enabled;
        if (maximize || minimize) { miniMode += maximize ? -1 : 1; return; }
        if (miniMode == 2) return;
        if (miniMode == 0)
        {
            // EXE 0x8576a4/0x857a6a/0x85817f: labels at x=8(+40 with mark), y=28/44;
            // the original shadow copies occupy the adjacent pixels. Mark is copied at (6,23).
            float labelX = panel.x + (mark != null ? 48 : 8) * s;
            if (mark != null) GUI.DrawTexture(new Rect(panel.x + 6 * s, panel.y + 23 * s, mark.width * s, mark.height * s), mark);
            var streetStyle = new GUIStyle(miniLabel);
            streetStyle.normal.textColor = new Color32(82, 117, 156, 255);
            GUI.Label(new Rect(labelX, panel.y + 28 * s, panel.xMax - labelX - 6 * s, 18 * s), map.street, streetStyle);
            GUI.Label(new Rect(labelX, panel.y + 44 * s, panel.xMax - labelX - 6 * s, 18 * s), map.name, miniLabel);
        }
        if (tex == null) return;
        var r = new Rect(panel.x + (width - mapWidth) * .5f * s, panel.y + header * s, mapWidth * s, mapHeight * s);
        float divisor = Mathf.Pow(2, mini.Int("mag", 4));
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        float px = player != null ? player.FeetPos.x * Art.PPU : 0;
        float py = player != null ? -player.FeetPos.y * Art.PPU : 0;
        // EXE 0x85abbc: scroll a native-size viewport; never shrink the map bitmap.
        float scrollX = Mathf.Floor((Mathf.Min(Mathf.Max(px - mapWidth * divisor / 2, -mini.Int("centerX")),
            mini.Int("width", tex.width * (int)divisor) - mini.Int("centerX") - mapWidth * divisor) + mini.Int("centerX")) / divisor);
        float scrollY = Mathf.Floor((Mathf.Min(Mathf.Max(py - mapHeight * divisor / 2, -mini.Int("centerY")),
            mini.Int("height", tex.height * (int)divisor) - mini.Int("centerY") - mapHeight * divisor) + mini.Int("centerY")) / divisor);
        GUI.BeginGroup(r);
        GUI.DrawTexture(new Rect(-scrollX * s, -scrollY * s, tex.width * s, tex.height * s), tex);
        Func<float, float, Vector2> point = (x, y) => new Vector2(
            Mathf.Floor((x + mini.Int("centerX")) / divisor) - scrollX,
            Mathf.Floor((y + mini.Int("centerY")) / divisor) - scrollY) * s;
        var helper = WzAssets.FileRoot("Map/MapHelper.img").Get("minimap");
        foreach (var portal in map.portals)
            if (portal.type == 2 || portal.type == 7) Marker(helper.Get("portal"), point(portal.x, portal.y), s);
        foreach (var npc in map.npcs)
            if (npc.hide == 0) Marker(helper.Get("npc"), point(npc.x, npc.y), s);
        if (player != null) Marker(helper.Get("user"), point(px, py), s);
        GUI.EndGroup();
    }

    static void DrawPart(string path, float x, float y, float width, float height)
    {
        if (width <= 0 || height <= 0) return;
        GUI.DrawTexture(new Rect(x, y, width, height), Art.Tex("wz://" + path));
    }
    static void DrawMiniFrame(Rect r, string root, float top, float bottom, float s)
    {
        DrawPart(root + "c", r.x + 6 * s, r.y + top, r.width - 12 * s, r.height - top - bottom);
        DrawPart(root + "nw", r.x, r.y, 60 * s, top);
        DrawPart(root + "n", r.x + 60 * s, r.y, r.width - 66 * s, top);
        DrawPart(root + "ne", r.xMax - 6 * s, r.y, 6 * s, top);
        DrawPart(root + "w", r.x, r.y + top, 6 * s, r.height - top - bottom);
        DrawPart(root + "e", r.xMax - 6 * s, r.y + top, 6 * s, r.height - top - bottom);
        DrawPart(root + "sw", r.x, r.yMax - bottom, 6 * s, bottom);
        DrawPart(root + "s", r.x + 6 * s, r.yMax - bottom, r.width - 12 * s, bottom);
        DrawPart(root + "se", r.xMax - 6 * s, r.yMax - bottom, 6 * s, bottom);
    }
    public static void DrawWorld()
    {
        if (!WorldVisible) return;
        var root = WzAssets.FileRoot(worldFile);
        var frames = root.Get("BaseImg");
        if (frames == null) return;
        // CreateWnd 009EB594: 666 x 524. Draw 009EBA31: BaseImg at (13,35).
        float s = Mathf.Min(BottomBar.S, (UiKit.W - 32) / 666, (UiKit.H - 64) / 524);
        var panel = new Rect((UiKit.W - 666 * s) / 2, 20, 666 * s, 524 * s);
        DrawWorldFrame(panel, s);
        var baseImage = frames.Get("0");
        var baseOrigin = baseImage != null ? WzAssets.Origin(baseImage) : Vector2.zero;
        var anchor = panel.position + (new Vector2(13, 35) + baseOrigin) * s;
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
                { Pointer.ConsumeClick(); worldFile = "Map/WorldMap/" + link.Text("link/linkMap") + ".img"; return; }
            }
        // 009EB80D / 009EB80F: original close button at (W-20,6).
        if (NativeUi.Button(new Rect(panel.xMax - 20 * s, panel.y + 6 * s, 12 * s, 12 * s),
            "UI/Basic.img/BtClose")) WorldVisible = false;
        var title = Art.Tex("wz://UI/UIWindow.img/WorldMap/title");
        if (title != null) GUI.DrawTexture(new Rect(panel.x + 8 * s, panel.y + 10 * s,
            title.width * s, title.height * s), title);
        if (hover.Length > 0)
            GUI.Label(new Rect(panel.x + 13 * s, panel.yMax - 17 * s, panel.width - 26 * s, 17 * s),
                hover, Label(Mathf.RoundToInt(12 * s)));
    }

    static void DrawWorldFrame(Rect r, float s)
    {
        const string border = "UI/UIWindow.img/WorldMap/Border/";
        // EXE 009EC12A..009EC143: FillRect(2, 2, W-4, H-4, 0xFFFFFFFF).
        // This is an original-client fill, underneath the IMG frame and map canvases.
        Color previousColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(r.x + 2 * s, r.y + 2 * s,
            r.width - 4 * s, r.height - 4 * s), Texture2D.whiteTexture);
        GUI.color = previousColor;
        // 009EC2F6..009EC6C5 tile the one-pixel strips; corners are copied last.
        DrawPart(border + "1", r.x + 7 * s, r.y, r.width - 14 * s, 33 * s);
        DrawPart(border + "6", r.x + 7 * s, r.yMax - 18 * s, r.width - 14 * s, 18 * s);
        DrawPart(border + "3", r.x, r.y + 32 * s, 7 * s, r.height - 50 * s);
        DrawPart(border + "4", r.xMax - 7 * s, r.y + 32 * s, 7 * s, r.height - 50 * s);
        DrawPart(border + "0", r.x, r.y, 60 * s, 33 * s);
        DrawPart(border + "2", r.xMax - 7 * s, r.y, 7 * s, 32 * s);
        DrawPart(border + "5", r.x, r.yMax - 18 * s, 7 * s, 18 * s);
        DrawPart(border + "7", r.xMax - 7 * s, r.yMax - 18 * s, 7 * s, 18 * s);
    }
}
