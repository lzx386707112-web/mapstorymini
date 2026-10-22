using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MapleDash.Wz;

// Runtime paper-doll composition. Native body origin remains the feet anchor;
// navel/neck/brow/hand attach points and z slots come from each original IMG frame.
public static class WzCharacter
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static readonly Dictionary<string, Art.Clip> clips = new Dictionary<string, Art.Clip>();
    static readonly string[] anchorPriority = { "navel", "neck", "hand", "brow", "handMove" };
    sealed class Part
    {
        public string Category, Name, Slot;
        public ImgNode Node;
        public Vector2Int Origin, Top;
        public readonly Dictionary<string, Vector2Int> Anchors = new Dictionary<string, Vector2Int>();
        public int Sequence, Z;
    }
    static Dictionary<string, object> Recipe(string key)
    {
        // The original starter outfit uses the same native IMG parts as the legacy knight recipe.
        if (key.StartsWith("beginner_", StringComparison.Ordinal)) key = "knight_" + key.Substring(9);
        var recipes = (Dictionary<string, object>)WzAssets.Index["characters"];
        return recipes.TryGetValue(key, out var recipe) ? (Dictionary<string, object>)recipe : null;
    }
    public static bool Has(string key) => Recipe(key) != null;
    static string Category(string id)
    {
        int type = int.Parse(id) / 10000;
        switch (type)
        {
            case 0: return "Body"; case 1: return "Head"; case 2: case 5: return "Face";
            case 3: case 4: case 6: return "Hair"; case 104: return "Coat";
            case 106: return "Pants"; case 107: return "Shoes";
            default:
                if (type >= 121 && type <= 160) return "Weapon";
                throw new NotSupportedException("Character equipment category " + type);
        }
    }
    static ImgNode Equipment(string id)
    {
        string cat = Category(id);
        return WzAssets.FileRoot("Character/" + (cat == "Body" || cat == "Head" ? "" : cat + "/") + id + ".img");
    }
    static string Pose(Dictionary<string, object> recipe, string action)
    {
        var ids = (List<object>)recipe["ids"];
        var weapon = Equipment((string)ids[ids.Count - 1]);
        switch (action)
        {
            case "stand": return "stand" + weapon.Int("info/stand", 1);
            case "walk": return "walk" + weapon.Int("info/walk", 1);
            case "atk": return (string)recipe["attack"];
            case "hit": return "alert";
            case "die": return "dead";
        }
        if (action.StartsWith("skill_", StringComparison.Ordinal))
        {
            string key = action.Substring(6); bool prepare = key.EndsWith("_prepare", StringComparison.Ordinal);
            if (prepare) key = key.Substring(0, key.Length - 8);
            var skills = (Dictionary<string, object>)WzAssets.Index["skills"];
            if (!skills.TryGetValue(key, out var sid)) return null;
            int id = Convert.ToInt32(sid);
            var skill = WzAssets.Node("Skill/" + (id / 10000).ToString("D3") + ".img/skill/" + id);
            if (prepare) return skill.Get("prepare/action")?.Value as string;
            var actions = skill.Get("action");
            if (actions == null) return null;
            var body = Equipment((string)ids[0]);
            foreach (var n in actions.Children) if (n.Value is string pose && body.Get(pose) != null) return pose;
            return null;
        }
        return action;
    }
    public static Art.Clip Clip(string key, string action)
    {
        string cacheKey = key + "/" + action;
        if (clips.TryGetValue(cacheKey, out var clip)) return clip;
        var recipe = Recipe(key); if (recipe == null) return null;
        string pose = Pose(recipe, action); if (pose == null) return null;
        var body = Equipment((string)((List<object>)recipe["ids"])[0]);
        var frames = WzAssets.Frames(body.Get(pose));
        // Existing dead pose has no source frame: retain its transparent single-frame transition.
        if (frames.Count == 0 && action != "die") return null;
        int count = Math.Max(1, frames.Count);
        clip = new Art.Clip { frames = new string[count], delays = new float[count] };
        for (int i = 0; i < count; i++)
        {
            string frame = frames.Count > 0 ? frames[i].Name : "0";
            clip.frames[i] = "avatar://" + key + "/" + pose + "/" + frame;
            clip.delays[i] = frames.Count > 0 ? Math.Max(1, Math.Abs(frames[i].Resolve().Int("delay", 120))) / 1000f : .12f;
        }
        clips.Add(cacheKey, clip); return clip;
    }
    static Vector2Int Vector(ImgNode node) => new Vector2Int(node?.X ?? 0, node?.Y ?? 0);
    static int BaseZ(string slot)
    {
        var names = (List<object>)WzAssets.Index["zOrder"];
        if (string.IsNullOrEmpty(slot)) return names.Count;
        int index = names.IndexOf(slot); if (index >= 0) return index;
        if (slot.StartsWith("back", StringComparison.Ordinal))
        {
            string front = slot.Length > 4 ? char.ToLowerInvariant(slot[4]) + slot.Substring(5) : slot;
            int near = names.IndexOf(front); if (near >= 0) return Math.Max(1, near - 5);
            foreach (string sep in new[] { "Below", "Over" })
            {
                int split = slot.IndexOf(sep, StringComparison.Ordinal);
                if (split < 0) continue;
                string tail = slot.Substring(split + sep.Length);
                if (tail == "") continue;
                int z = names.IndexOf("back" + char.ToUpperInvariant(tail[0]) + tail.Substring(1));
                if (z >= 0) return Math.Max(1, z + (sep == "Below" ? -1 : 1));
            }
            return Math.Max(1, names.IndexOf("body") - 1);
        }
        return names.Count / 2;
    }
    static int Z(Part part, string pose)
    {
        string slot = part.Slot;
        if (pose == "stand1" && part.Category == "Weapon" && slot == "weaponOverArm") slot = "weapon";
        else if (Array.IndexOf(new[] { "prone", "proneStab", "shoot1", "shoot2", "shootF", "swingT1", "stabO1", "stabO2" }, pose) >= 0)
        {
            if (part.Category == "Weapon" && (slot == "weapon" || slot == "weaponBelowArm")) slot = "weaponOverHand";
            else if (part.Category == "Body" && (slot == "arm" || slot == "armOverHair")) slot = "handOverHair";
        }
        else if (Array.IndexOf(new[] { "swingT2", "stabT1", "stabT2" }, pose) >= 0 && part.Category == "Body" &&
            (slot == "arm" || slot == "armBelowHead" || slot == "armBelowHeadOverMailChest")) slot = "handOverHair";
        else if (pose == "swingT3" && part.Category == "Body" && (slot == "arm" || slot == "armBelowHeadOverMailChest")) slot = "handOverHair";
        else if (pose == "stand2" && part.Category == "Weapon" && slot == "weaponBelowArm") slot = "weaponOverArm";
        var remap = (Dictionary<string, object>)WzAssets.Index["overCapRemap"];
        if (slot != null && remap.TryGetValue(slot, out var target)) slot = (string)target;
        if (pose == "ladder" || pose == "rope")
        {
            var absolute = (Dictionary<string, object>)WzAssets.Index["backAbsoluteZ"];
            if (slot != null && absolute.TryGetValue(slot, out var az)) return Convert.ToInt32(az);
            var back = (Dictionary<string, object>)WzAssets.Index["backZ"];
            if (slot != null && back.TryGetValue(slot, out var bz)) return Convert.ToInt32(bz);
            if (slot == null || !slot.StartsWith("back", StringComparison.Ordinal)) return -1;
        }
        return BaseZ(slot);
    }
    static List<Part> Placements(Dictionary<string, object> recipe, ref string pose, ref int frame)
    {
        var ids = (List<object>)recipe["ids"];
        var body = Equipment((string)ids[0]);
        for (int i = 0; i < 64; i++)
        {
            var redirect = body.Get(pose + "/" + frame);
            string action = redirect?.Get("action")?.Value as string;
            if (action == null) break;
            pose = action; frame = redirect.Int("frame");
            if (i == 63) throw new InvalidDataException("Character action cycle");
        }
        var weapon = Equipment((string)ids[ids.Count - 1]);
        if (weapon.Get(pose) == null && pose.StartsWith("swingO", StringComparison.Ordinal) && weapon.Get(pose.Replace("swingO", "swingT")) != null)
            pose = pose.Replace("swingO", "swingT");
        var parts = new List<Part>();
        foreach (string id in ids)
        {
            string category = Category(id);
            var root = Equipment(id);
            var paths = new List<string> { pose + "/" + frame };
            if (category == "Face") { paths.Clear(); paths.Add("default"); }
            else if (category == "Head") paths.Add("front");
            else if (category == "Hair") paths.Add("default");
            var seen = new HashSet<ImgNode>(); var seenZ = new HashSet<string>();
            foreach (string path in paths)
            {
                var node = root.Get(path)?.Resolve(); if (node == null) continue;
                int before = parts.Count;
                foreach (var child in node.Children)
                {
                    if (category == "Head" && child.Name != "head" && child.Name != "humanEar") continue;
                    var canvas = child.Resolve(); if (canvas.Kind != "Canvas" || !seen.Add(canvas)) continue;
                    string slot = canvas.Get("z")?.Value as string;
                    if (slot != null && !seenZ.Add(slot)) continue;
                    var part = new Part { Category = category, Name = child.Name, Slot = slot, Node = canvas,
                        Origin = Vector(canvas.Get("origin")), Sequence = parts.Count };
                    var anchors = canvas.Get("map");
                    if (anchors != null) foreach (var point in anchors.Children) part.Anchors[point.Name] = Vector(point);
                    parts.Add(part);
                }
                if (parts.Count > before) break;
            }
        }
        parts.Sort((a, b) =>
        {
            int ap = a.Category == "Body" ? (a.Name == "body" ? 0 : 1) : a.Category == "Head" ? (a.Name == "head" ? 2 : 3) : 4;
            int bp = b.Category == "Body" ? (b.Name == "body" ? 0 : 1) : b.Category == "Head" ? (b.Name == "head" ? 2 : 3) : 4;
            int compare = ap.CompareTo(bp); return compare != 0 ? compare : a.Sequence.CompareTo(b.Sequence);
        });
        var world = new Dictionary<string, Vector2Int>();
        var navel = Vector2Int.zero; bool bodyPlaced = false;
        foreach (var part in parts)
        {
            if (part.Category == "Body" && part.Name == "body" && !bodyPlaced)
            {
                part.Anchors.TryGetValue("navel", out navel); part.Top = -part.Origin - navel; bodyPlaced = true;
            }
            else
            {
                string anchor = part.Category == "Head" ? "neck" : part.Category == "Face" || part.Category == "Hair" ? "brow" : part.Category == "Weapon" ? "hand" : "navel";
                foreach (var name in anchorPriority) if (part.Anchors.ContainsKey(name)) { anchor = name; break; }
                part.Anchors.TryGetValue(anchor, out var point);
                if (world.TryGetValue(anchor, out var position)) part.Top = position - part.Origin - point;
                else part.Top = -part.Origin - (anchor == "handMove" ? navel : Vector2Int.zero);
            }
            foreach (var anchor in part.Anchors) if (!world.ContainsKey(anchor.Key)) world.Add(anchor.Key, part.Top + part.Origin + anchor.Value);
        }
        foreach (var part in parts) { part.Top += navel; part.Z = Z(part, pose); }
        parts.Sort((a, b) => { int z = a.Z.CompareTo(b.Z); return z != 0 ? z : a.Sequence.CompareTo(b.Sequence); });
        return parts;
    }
    public static Sprite Frame(string uri)
    {
        if (cache.TryGetValue(uri, out var sprite)) return sprite;
        string[] address = uri.Substring("avatar://".Length).Split('/');
        var recipe = Recipe(address[0]); if (recipe == null) throw new InvalidDataException(uri);
        string pose = address[1]; int frame = int.Parse(address[2]);
        var parts = Placements(recipe, ref pose, ref frame);
        int left = 0, top = 0, right = 1, bottom = 1;
        foreach (var part in parts)
        {
            var canvas = WzAssets.Canvas(part.Node);
            left = Math.Min(left, part.Top.x); top = Math.Min(top, part.Top.y);
            right = Math.Max(right, part.Top.x + canvas.Width); bottom = Math.Max(bottom, part.Top.y + canvas.Height);
        }
        int width = right - left, height = bottom - top;
        var pixels = new byte[checked(width * height * 4)];
        foreach (var part in parts)
        {
            var canvas = WzAssets.Canvas(part.Node); byte[] source = WzAssets.Pixels(part.Node);
            for (int y = 0; y < canvas.Height; y++) for (int x = 0; x < canvas.Width; x++)
            {
                int s = (y * canvas.Width + x) * 4;
                int d = ((part.Top.y - top + y) * width + part.Top.x - left + x) * 4;
                int alpha = source[s + 3]; if (alpha == 0) continue;
                if (alpha == 255 || pixels[d + 3] == 0) { Buffer.BlockCopy(source, s, pixels, d, 4); continue; }
                int da = pixels[d + 3], blend = da * (255 - alpha), a = alpha * 255 + blend;
                for (int c = 0; c < 3; c++) pixels[d + c] = (byte)((source[s + c] * alpha * 255 + pixels[d + c] * blend + a / 2) / a);
                pixels[d + 3] = (byte)((a + 127) / 255);
            }
        }
        var texture = WzAssets.Upload(pixels, width, height, uri);
        sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(-left / (float)width, 1f + top / (float)height), Art.PPU, 0, SpriteMeshType.FullRect);
        sprite.name = uri; cache.Add(uri, sprite); return sprite;
    }
}
