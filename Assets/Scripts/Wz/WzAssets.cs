using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MapleDash.Wz;

// The game's sole bitmap source. Packaged files are byte-identical original IMG.
// MAPLEDASH_IMG_ROOT optionally mounts a different Data directory for development.
public static class WzAssets
{
    public static string DataRoot
    {
        get
        {
            string mounted = Environment.GetEnvironmentVariable("MAPLEDASH_IMG_ROOT");
            if (!string.IsNullOrEmpty(mounted)) return mounted;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (WzWebLoader.Root == null) throw new InvalidOperationException("Native assets not downloaded yet");
            return WzWebLoader.Root;
#elif UNITY_ANDROID && !UNITY_EDITOR
            return "/sdcard/mapstory/Data";
#else
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../../MapleDashResources/Data"));
#endif
        }
    }
    static Dictionary<string, object> index;
    static HashSet<string> inventory;
    static readonly HashSet<string> embeddedFallbacks = new HashSet<string>();
    public static int EmbeddedFallbackCount => embeddedFallbacks.Count;
    public static bool HasFile(string relative)
    {
#if !UNITY_ANDROID && !UNITY_WEBGL || UNITY_EDITOR
        return File.Exists(Path.Combine(DataRoot, relative));
#else
        if (inventory == null)
        {
            inventory = new HashSet<string>();
            foreach (Dictionary<string, object> file in (List<object>)Index["files"]) inventory.Add((string)file["path"]);
        }
        return inventory.Contains(relative);
#endif
    }
    public static Dictionary<string, object> Index
    {
        get
        {
            if (index == null)
            {
                var asset = Resources.Load<TextAsset>("NativeAssetIndex");
                if (asset == null) throw new FileNotFoundException("NativeAssetIndex missing; run tools/package_native_assets.py");
                index = (Dictionary<string, object>)MiniJson.Parse(asset.text);
            }
            return index;
        }
    }
    static readonly Dictionary<string, ImgNode> roots = new Dictionary<string, ImgNode>();
    static readonly Dictionary<ImgNode, byte[]> pixels = new Dictionary<ImgNode, byte[]>();
    static readonly Dictionary<ImgNode, Texture2D> textures = new Dictionary<ImgNode, Texture2D>();
    static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    static readonly Dictionary<string, Texture2D> aliases = new Dictionary<string, Texture2D>();
    static readonly Dictionary<string, Art.Clip> mobClips = new Dictionary<string, Art.Clip>();
    static readonly HashSet<string> missing = new HashSet<string>();
    public static int MissingCount => missing.Count;
    public static int FileCount => roots.Count;
    public static int TextureCount => textures.Count;
    public static string MapPath(int id) => "Map/Map/Map" + id / 100000000 + "/" + id.ToString("D9") + ".img";
    public static ImgNode FileRoot(string relative)
    {
        if (roots.TryGetValue(relative, out var root)) return root;
        if (relative.Contains("..") || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid IMG path " + relative);
        using (var img = new ImgFile(Path.Combine(DataRoot, relative))) root = img.Root;
        roots.Add(relative, root); return root;
    }
    public static ImgNode Node(string source)
    {
        if (source.StartsWith("wz://", StringComparison.Ordinal)) source = source.Substring(5);
        int split = source.IndexOf(".img/", StringComparison.Ordinal);
        if (split < 0) return FileRoot(source);
        return FileRoot(source.Substring(0, split + 4)).Get(source.Substring(split + 5))?.Resolve();
    }
    public static ImgNode Canvas(ImgNode frame)
    {
        var n = frame.Resolve();
        for (int i = 0; i < 64; i++)
        {
            string link = n.Text("_inlink");
            if (link != "")
            {
                var root = n; while (root.Parent != null) root = root.Parent;
                n = root.Get(link)?.Resolve();
            }
            else if ((link = n.Text("_outlink")) != "")
            {
                int split = link.IndexOf(".img/", StringComparison.Ordinal);
                string file = split < 0 ? link : link.Substring(0, split + 4);
                if (!HasFile(file) && n.Kind == "Canvas")
                {
                    // This extracted Data materializes full pixels but retains stale _Canvas links.
                    // Accept only a genuinely decodable local Canvas, never a blank substitute.
                    if (!pixels.ContainsKey(n)) pixels.Add(n, n.Rgba());
                    if (embeddedFallbacks.Add(file)) Debug.LogWarning("[WzAssets] Missing outlink file " + file + "; using verified embedded Canvas pixels");
                    return n;
                }
                n = Node(link);
            }
            else
            {
                if (n.Kind != "Canvas") throw new InvalidDataException("Expected Canvas: " + n.Name);
                return n;
            }
            if (n == null) throw new InvalidDataException("Missing Canvas link " + link);
        }
        throw new InvalidDataException("Canvas link cycle: " + frame.Name);
    }
    public static byte[] Pixels(ImgNode frame)
    {
        var canvas = Canvas(frame);
        if (!pixels.TryGetValue(canvas, out var rgba)) { rgba = canvas.Rgba(); pixels.Add(canvas, rgba); }
        return rgba;
    }
    public static Texture2D Texture(ImgNode frame)
    {
        var canvas = Canvas(frame);
        if (textures.TryGetValue(canvas, out var texture)) return texture;
        texture = Upload(Pixels(canvas), canvas.Width, canvas.Height, frame.Name);
        textures.Add(canvas, texture); return texture;
    }
    public static Texture2D Upload(byte[] rgba, int width, int height, string name)
    {
        int row = checked(width * 4);
        var flipped = new byte[rgba.Length];
        for (int y = 0; y < height; y++) Buffer.BlockCopy(rgba, y * row, flipped, (height - 1 - y) * row, row);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = name; texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
        texture.LoadRawTextureData(flipped); texture.Apply(); // readable: existing character dye UI uses pixels.
        return texture;
    }
    public static Vector2 Origin(ImgNode frame)
    {
        var origin = frame.Get("origin") ?? Canvas(frame).Get("origin");
        return new Vector2(origin?.X ?? 0, origin?.Y ?? 0);
    }
    public static Sprite Sprite(ImgNode frame, string key = null)
    {
        key = key ?? "node:" + frame.GetHashCode();
        if (sprites.TryGetValue(key, out var sprite)) return sprite;
        var texture = Texture(frame); var origin = Origin(frame);
        sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(origin.x / texture.width, 1f - origin.y / texture.height), Art.PPU, 0, SpriteMeshType.FullRect);
        sprite.name = key; sprites.Add(key, sprite); return sprite;
    }
    public static string AliasKey(string relative)
    {
        int dot = relative.LastIndexOf('.'), slash = relative.LastIndexOf('/');
        return dot > slash ? relative.Substring(0, dot) : relative;
    }
    public static ImgNode AliasNode(string relative)
    {
        if (relative.StartsWith("wz://", StringComparison.Ordinal)) return Node(relative);
        string key = AliasKey(relative);
        var entries = (Dictionary<string, object>)Index["aliases"];
        if (entries.TryGetValue(key, out var entry)) return Node((string)((Dictionary<string, object>)entry)["source"]);
        if (missing.Add(key)) Debug.LogWarning("[WzAssets] Unmapped resource: " + relative);
        return null;
    }
    public static Texture2D Texture(string relative)
    {
        if (aliases.TryGetValue(relative, out var texture)) return texture;
        var frame = AliasNode(relative); if (frame == null) return null;
        texture = Texture(frame);
        var entries = (Dictionary<string, object>)Index["aliases"];
        if (!relative.StartsWith("wz://", StringComparison.Ordinal) && entries.TryGetValue(AliasKey(relative), out var descriptor) &&
            ((Dictionary<string, object>)descriptor).TryGetValue("crop", out var crop))
        {
            var rect = (List<object>)crop; int x = Convert.ToInt32(rect[0]), y = Convert.ToInt32(rect[1]);
            int w = Convert.ToInt32(rect[2]), h = Convert.ToInt32(rect[3]);
            var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
            cropped.SetPixels(texture.GetPixels(x, texture.height - y - h, w, h)); cropped.Apply();
            cropped.filterMode = FilterMode.Point; cropped.wrapMode = TextureWrapMode.Clamp; cropped.name = relative;
            texture = cropped;
        }
        aliases.Add(relative, texture); return texture;
    }
    public static Sprite Frame(string relative)
    {
        if (relative.StartsWith("avatar://", StringComparison.Ordinal)) return WzCharacter.Frame(relative);
        var frame = AliasNode(relative); return frame == null ? null : Sprite(frame, relative);
    }
    public static List<ImgNode> Frames(ImgNode action)
    {
        var result = new List<ImgNode>();
        if (action == null) return result;
        action = action.Resolve();
        if (action.Kind == "Canvas") { result.Add(action); return result; }
        foreach (var n in action.Children) if (int.TryParse(n.Name, out _)) result.Add(n);
        result.Sort((a, b) => int.Parse(a.Name).CompareTo(int.Parse(b.Name)));
        return result;
    }
    public static Art.Clip MobClip(string key, string action)
    {
        string clipKey = key + "/" + action;
        if (mobClips.TryGetValue(clipKey, out var cached)) return cached;
        var ids = (Dictionary<string, object>)Index["mobs"];
        if (!ids.TryGetValue(key, out var id))
        {
            if (!int.TryParse(key, out var numericId)) return null;
            id = numericId;
        }
        string file = "Mob/" + Convert.ToInt32(id).ToString("D7") + ".img";
        var root = FileRoot(file);
        for (int i = 0; root.Get("info/link") != null; i++)
        {
            if (i >= 64) throw new InvalidDataException("Mob link cycle");
            file = "Mob/" + Convert.ToInt32(root.Get("info/link").Value).ToString("D7") + ".img";
            root = FileRoot(file);
        }
        var frames = Frames(root.Get(action)); if (frames.Count == 0) return null;
        var clip = new Art.Clip { frames = new string[frames.Count], delays = new float[frames.Count] };
        for (int i = 0; i < frames.Count; i++)
        {
            clip.frames[i] = "wz://" + file + "/" + action + "/" + frames[i].Name;
            clip.delays[i] = Math.Max(1, Math.Abs(frames[i].Resolve().Int("delay", 100))) / 1000f;
        }
        mobClips.Add(clipKey, clip); return clip;
    }
}
