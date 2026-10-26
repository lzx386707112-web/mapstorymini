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
    // 注入装备武器后的配方缓存：Frame() 每帧都会走 Recipe，不能每次现拷集合
    static readonly Dictionary<string, Dictionary<string, object>> swappedRecipes =
        new Dictionary<string, Dictionary<string, object>>();
    static Dictionary<string, object> Recipe(string key)
    {
        if (appearanceRecipes.TryGetValue(key, out var appearance)) return appearance;
        // The original starter outfit uses the same native IMG parts as the legacy knight recipe.
        if (key.StartsWith("beginner_", StringComparison.Ordinal)) key = "knight_" + key.Substring(9);
        var recipes = (Dictionary<string, object>)WzAssets.Index["characters"];
        if (!recipes.TryGetValue(key, out var raw)) return null;
        var recipe = (Dictionary<string, object>)raw;

        // 装备中的武器替换配方里的职业默认武器（ids 最后一项）：
        // 原版客户端的纸娃娃、stand/walk 变体、攻击动作全部由当前武器决定，
        // 原来写死职业默认剑，导致换弓/换巨剑只是数值变化，动作和外观都不动。
        string equipped = EquippedWeaponId();
        if (equipped == null) return recipe;
        string cacheKey = key + "|" + equipped;
        if (swappedRecipes.TryGetValue(cacheKey, out var cached)) return cached;
        var ids = (List<object>)recipe["ids"];
        if (ids.Count == 0 || Category((string)ids[ids.Count - 1]) != "Weapon") return recipe;
        var swapped = new Dictionary<string, object>(recipe);
        var copy = new List<object>(ids);
        copy[copy.Count - 1] = equipped;
        swapped["ids"] = copy;
        swappedRecipes[cacheKey] = swapped;
        return swapped;
    }

    // Profile.equipWeapon（7 位物品 ID）→ Character/Weapon/<8 位>.img。
    // 文件不存在（未打包/非武器）时返回 null，配方保持职业默认武器。
    static string equippedIdFor, equippedIdCache;
    static string EquippedWeaponId()
    {
        string eq = Profile.equipWeapon;
        if (eq == equippedIdFor) return equippedIdCache;
        equippedIdFor = eq;
        equippedIdCache = null;
        if (string.IsNullOrEmpty(eq)) return null;
        string id;
        try { id = WeaponDefs.Canonical(eq); } catch { return null; }
        if (id.Length == 7) id = "0" + id;
        if (id.Length != 8) return null;
        int type;
        try { type = int.Parse(id) / 10000; } catch { return null; }
        if (type < 121 || type > 160) return null;      // 只接受武器类
        if (!WzAssets.HasFile("Character/Weapon/" + id + ".img")) return null;
        equippedIdCache = id;
        return id;
    }
    // 登录预览与场景共用完整的原生 ID，保留性别、肤色、脸型和发色。
    public static string[] CharacterIds(CharRecord rec, IEnumerable<InventoryEntry> inventory)
    {
        var ids = new List<string> { (2000 + rec.skincolor).ToString("D8"),
            (12000 + rec.skincolor).ToString("D8"), rec.face.ToString("D8"), rec.hair.ToString("D8") };
        int weapon = 0;
        foreach (var item in inventory)
        {
            if (item.inventoryType != InventoryDao.Equipped) continue;
            if (item.position == EquipSlot.Weapon) weapon = item.itemId;
            else if (Array.IndexOf(EquipSlot.Worn, item.position) >= 0 && item.itemId > 0)
                ids.Add(item.itemId.ToString("D8"));
        }
        if (weapon > 0) ids.Add(weapon.ToString("D8"));
        return ids.ToArray();
    }

    public static string CurrentAppearanceKey()
    {
        if (!GameSession.InGame || GameSession.Character == null) return null;
        var inventory = new List<InventoryEntry>();
        foreach (int slot in EquipSlot.Worn)
        {
            int id = Profile.Data.EquipOfSlot(slot);
            if (id > 0) inventory.Add(new InventoryEntry { inventoryType = InventoryDao.Equipped,
                position = slot, itemId = id });
        }
        var ids = CharacterIds(GameSession.Character.record, inventory);
        string key = "character_" + string.Join("_", ids);
        if (!appearanceRecipes.ContainsKey(key))
            appearanceRecipes.Add(key, new Dictionary<string, object> {
                { "ids", new List<object>(ids) }, { "attack", "swingO1" } });
        return key;
    }

    public static bool Has(string key) => Recipe(key) != null;
    static string Category(string id)
    {
        int type = int.Parse(id) / 10000;
        switch (type)
        {
            case 0: return "Body"; case 1: return "Head"; case 2: case 5: return "Face";
            case 3: case 4: case 6: return "Hair"; case 104: return "Coat";
            case 100: return "Cap"; case 105: return "Longcoat";
            case 108: return "Glove"; case 109: return "Shield"; case 110: return "Cape";
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
            case "atk": return AttackPose(recipe, weapon);
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
    // —— 攻击动作：原版客户端按武器 info/attack（攻击类型索引）选姿势 ——
    // 索引语义（用各职业武器文件的 info/attack 与姿势集反推验证）：
    //   1=单手挥砍(剑/斧/钝器/短刀)  2=枪矛突刺  3=弓  4=弩  5=双手挥砍
    //   6=魔杖/杖  7=拳爪  8=指虎  9=火器
    // 主姿势身体没有时按回退链找，再不行退回配方里的职业默认动作。
    // 指虎归突刺：Afterimage/knuckle.img 只覆盖 stabO1/stabO2（出拳是直线动作）。
    static readonly string[][] attackFallback =
    {
        null,                                   // 0 不用
        new[]{ "swingO1","swingO2","swingO3","stabO1","stabO2" },
        new[]{ "stabT1","stabT2","swingT2","swingP1" },
        new[]{ "shoot1","shoot2","shootF" },
        new[]{ "shoot2","shoot1","shootF" },
        new[]{ "swingT1","swingT2","swingT3" },
        new[]{ "swingO1","swingO2","stabO1" },
        new[]{ "stabO1","stabO2","swingO1" },
        new[]{ "stabO1","stabO2","swingO1" },
        new[]{ "shoot1","shoot2","shootF" },
    };
    static string AttackPose(Dictionary<string, object> recipe, ImgNode weapon)
    {
        var body = Equipment((string)((List<object>)recipe["ids"])[0]);
        int t = weapon.Int("info/attack", 1);
        if (t >= 1 && t < attackFallback.Length && attackFallback[t] != null)
            foreach (var pose in attackFallback[t])
                if (body.Get(pose) != null) return pose;
        string fallback = recipe.ContainsKey("attack") ? recipe["attack"] as string : null;
        if (fallback != null && body.Get(fallback) != null) return fallback;
        return "swingO1";
    }

    // 当前(key+装备)下的普攻姿势名（swingO1/stabT1/shoot1…）：
    // 残影数据按姿势名索引，外部拿不到 Pose() 就走这里。
    public static string AttackPoseName(string key)
    {
        var recipe = Recipe(key);
        return recipe == null ? null : Pose(recipe, "atk");
    }

    public static int WeaponAttackSpeed(string key)
    {
        var recipe = Recipe(key);
        if (recipe == null) return 6;
        var ids = (List<object>)recipe["ids"];
        foreach (string id in ids)
            if (Category(id) == "Weapon") return Equipment(id).Int("info/attackSpeed", 6);
        return 6; // EXE 0040A845: missing attackSpeed defaults to 6.
    }

    // EXE 00454776..004547E8: clamp speed to 2..10, then truncate each
    // frame's integer milliseconds separately, rather than scaling the total.
    public static float AttackFrameDelay(float delay, int attackSpeed)
    {
        int milliseconds = Mathf.RoundToInt(delay * 1000f);
        return milliseconds * (Mathf.Clamp(attackSpeed, 2, 10) + 10) / 16 / 1000f;
    }

    // 当前武器的攻击音效键（Sound/Weapon/<family>/Attack）。
    // 原版按武器 info/attack 索引 Sound/Weapon.img 的家族名，与姿势同一套索引。
    public static string AttackSoundKey(string key)
    {
        var recipe = Recipe(key); if (recipe == null) return null;
        var ids = (List<object>)recipe["ids"];
        if (ids.Count == 0) return null;
        string id = (string)ids[ids.Count - 1];
        int t;
        try { t = Equipment(id).Int("info/attack", 1); } catch { return null; }
        string family;
        switch (t)
        {
            case 2: family = "poleArm"; break;   // 枪/矛突刺
            case 3: family = "bow"; break;
            case 4: family = "cBow"; break;
            case 5: family = "swordS"; break;    // 双手挥砍
            case 6: family = "mace"; break;      // 杖
            case 7: family = "tGlove"; break;    // 拳爪
            case 8: family = "knuckle"; break;   // 指虎
            case 9: family = "gun"; break;       // 火器
            default: family = "swordL"; break;   // 单手挥砍
        }
        return "Weapon/" + family + "/Attack";
    }

    // 当前武器的残影文件名（Character/Afterimage/<name>.img）。
    // 取武器自己的 info/afterImage；文件没打包时按武器大类给最近的一档。
    public static string Afterimage(string key)
    {
        var recipe = Recipe(key); if (recipe == null) return null;
        string id = (string)((List<object>)recipe["ids"])[((List<object>)recipe["ids"]).Count - 1];
        string name = null;
        try { name = Equipment(id).Text("info/afterImage"); } catch { return null; }
        if (string.IsNullOrEmpty(name)) name = AfterimageFallback(id);
        if (string.IsNullOrEmpty(name)) return null;
        return WzAssets.HasFile("Character/Afterimage/" + name + ".img") ? name : null;
    }
    static string AfterimageFallback(string id)
    {
        int type;
        try { type = int.Parse(id) / 10000; } catch { return null; }
        switch (type)
        {
            case 130: case 133: case 137: case 147: return "swordOL";
            case 131: return "axe";
            case 132: return "mace";
            case 138: return "swordOL";
            case 140: return "swordTS";
            case 141: return "axe";
            case 142: return "mace";
            case 143: return "spear";
            case 144: return "poleArm";
            case 145: return "bow";
            case 146: return "crossBow";
            case 148: return "knuckle";
            case 149: return "gun";
            default: return null;
        }
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
            if (category == "Face")
            {
                paths.Clear();
                if (recipe.TryGetValue("expression", out var expression))
                    paths.Add((string)expression + "/" + recipe["expressionFrame"]);
                paths.Add("default");
            }
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
    public static Sprite ExpressionFrame(string uri, string expression, float elapsed)
    {
        string[] address = uri.Substring("avatar://".Length).Split('/');
        var recipe = Recipe(address[0]);
        if (recipe == null) return Frame(uri);
        ImgNode face = null;
        foreach (string id in (List<object>)recipe["ids"])
            if (Category(id) == "Face") { face = Equipment(id); break; }
        var frames = WzAssets.Frames(face?.Get(expression));
        if (frames.Count == 0) return Frame(uri);
        float duration = 0f;
        foreach (var frame in frames) duration += Math.Max(1, frame.Int("delay", 100));
        float time = Mathf.Repeat(elapsed * 1000f, duration);
        var selected = frames[0];
        foreach (var frame in frames)
        {
            selected = frame;
            time -= Math.Max(1, frame.Int("delay", 100));
            if (time < 0f) break;
        }
        string cacheKey = uri + "|" + expression + "/" + selected.Name;
        if (cache.TryGetValue(cacheKey, out var sprite)) return sprite;
        var expressed = new Dictionary<string, object>(recipe) {
            ["expression"] = expression, ["expressionFrame"] = selected.Name
        };
        return Compose(cacheKey, expressed, address[1], int.Parse(address[2]));
    }

    public static Sprite Frame(string uri)
    {
        if (cache.TryGetValue(uri, out var sprite)) return sprite;
        string[] address = uri.Substring("avatar://".Length).Split('/');
        var recipe = Recipe(address[0]); if (recipe == null) throw new InvalidDataException(uri);
        string pose = address[1]; int frame = int.Parse(address[2]);
        return Compose(uri, recipe, pose, frame);
    }

    static readonly Dictionary<string, Dictionary<string, object>> appearanceRecipes =
        new Dictionary<string, Dictionary<string, object>>();

    // 登录/选角/建号界面的预览：外观由 CHARLIST/建号选项直接给装备 ID，
    // 不经过职业配方，仍由同一套原版部件合成器拼帧。
    // Login previews use the weapon-selected native stand pose and body delays.
    public static int AppearanceFrame(string[] ids, float elapsed)
    {
        var weapon = Equipment(ids[ids.Length - 1]);
        string pose = "stand" + weapon.Int("info/stand", 1);
        var frames = Equipment(ids[0]).Get(pose);
        if (frames == null) return 0;
        float duration = 0f;
        int count = 0;
        for (; frames.Get(count.ToString()) != null; count++)
            duration += Math.Max(1, frames.Get(count.ToString()).Int("delay", 100)) / 1000f;
        if (count == 0) return 0;
        float time = Mathf.Repeat(Mathf.Max(0f, elapsed), duration);
        for (int i = 0; i < count; i++)
        {
            time -= Math.Max(1, frames.Get(i.ToString()).Int("delay", 100)) / 1000f;
            if (time < 0f) return i;
        }
        return count - 1;
    }

    public static Sprite Appearance(string key, string[] ids, int frame = 0)
    {
        string cacheKey = "appearance://" + key + "/stand/" + frame;
        if (cache.TryGetValue(cacheKey, out var sprite)) return sprite;
        Dictionary<string, object> recipe;
        if (!appearanceRecipes.TryGetValue(key, out recipe))
        {
            var list = new List<object>(ids.Length);
            foreach (string id in ids) list.Add(id);
            recipe = new Dictionary<string, object> { { "ids", list }, { "attack", "swingO1" } };
            appearanceRecipes.Add(key, recipe);
        }
        string pose = Pose(recipe, "stand");
        if (pose == null) return null;
        return Compose(cacheKey, recipe, pose, frame);
    }

    static Sprite Compose(string uri, Dictionary<string, object> recipe, string pose, int frame)
    {
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
        var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(-left / (float)width, 1f + top / (float)height), Art.PPU, 0, SpriteMeshType.FullRect);
        sprite.name = uri; cache.Add(uri, sprite); return sprite;
    }
}
