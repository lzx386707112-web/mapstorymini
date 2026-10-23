using System.Collections.Generic;
using UnityEngine;

// Native IMG artwork. Manifest holds game choices; pixels, origins and animation
// frames are read from the original resources by WzAssets/WzCharacter.
public static class Art
{
    public const float PPU = 40f;          // 1 世界单位 = 40px（与 Px.ToUnit 一致）

    public static bool Ready { get; private set; }
    static bool tried;

    // —— 关卡背景（真实地图切片） ——
    //
    // 每个主题对应客户端的一张地图，VR 边界与栅格图片范围分开保存。
    // px/ground/walk 全是「地图像素」，Unity 里按 PPU 换算成世界单位：
    //     worldX = (mapX - ground.x) / PPU
    //     worldY = GroundY + (ground.y - mapY) / PPU
    // 也就是说：地面锚点那一列正好在 x=0，地面线正好在 PlayerCtrl.GroundY。
    public class StageInfo
    {
        public string map;
        public Vector2Int px;          // 窗口左上角（地图像素）
        public Vector2Int size;        // 窗口宽高（地图像素）
        public Vector2 ground;         // 地面锚点：窗口中心 x + 地面线 y
        public Vector2 walk;           // 可走范围（地图像素 x 两端）
        public Color sky;              // 切片外的天空底色
        public Vector4 view;           // 原客户端 VR 边界，地图坐标
        public bool hasView;
        public readonly List<StageBack> backgrounds = new List<StageBack>();
        public string fileBack, fileMid, fileFront;
        public int orderBack = -800, orderMid = -400, orderFront = 600;

        // 窗口内的全部 foothold 线段，已换算成世界坐标 (x1,y1,x2,y2)。
        // 角色/怪物靠它贴地形行走（含斜坡、高台），是"一比一复刻原版地图"的碰撞数据。
        public readonly System.Collections.Generic.List<Vector4> Footholds =
            new System.Collections.Generic.List<Vector4>();

        public readonly List<ClimbLine> Climbs = new List<ClimbLine>();

        public bool HasFront => !string.IsNullOrEmpty(fileFront);

        // 地图像素 → 世界单位
        public float WorldX(float mapX) => (mapX - ground.x) / PPU;
        public float WorldY(float mapY) => PlayerCtrl.GroundY + (ground.y - mapY) / PPU;

        public float WalkMin => WorldX(walk.x);
        public float WalkMax => WorldX(walk.y);
        public float LeftX => WorldX(px.x);
        public float RightX => WorldX(px.x + size.x);
        public float TopY => WorldY(px.y);
        public float BottomY => WorldY(px.y + size.y);
    }

    public class ClimbLine
    {
        public float x, top, bottom;
        public bool ladder, upperExit;
    }

    public class StageBack
    {
        public string file;
        public Vector2 origin, size;
        public int x, y, rx, ry, cx, cy, type, front, alpha, no;
    }

    static Dictionary<string, StageInfo> stages;

    public static StageInfo Stage(string theme)
    {
        Ensure();
        if (stages == null || string.IsNullOrEmpty(theme)) return null;
        StageInfo st;
        return stages.TryGetValue(theme, out st) ? st : null;
    }

    static void ReadStages(Dictionary<string, object> root)
    {
        if (!root.ContainsKey("stages")) return;
        var dict = root["stages"] as Dictionary<string, object>;
        if (dict == null) return;
        stages = new Dictionary<string, StageInfo>();
        foreach (var kv in dict)
        {
            var n = kv.Value as Dictionary<string, object>;
            if (n == null) continue;

            var st = new StageInfo { map = Str(n, "map") };
            var px = ReadVec(n, "px");
            st.px = new Vector2Int(Mathf.RoundToInt(px.x), Mathf.RoundToInt(px.y));
            st.size = new Vector2Int(Mathf.RoundToInt(ReadNum(n, "px", 2)),
                                     Mathf.RoundToInt(ReadNum(n, "px", 3)));
            st.ground = ReadVec(n, "ground");
            st.walk = n.ContainsKey("walk")
                ? ReadVec(n, "walk")
                : new Vector2(st.px.x, st.px.x + st.size.x);
            st.sky = ToColor(Int(n, "sky", 0x6FB7E8));
            st.hasView = n.ContainsKey("view");
            if (st.hasView)
                st.view = new Vector4(ReadNum(n, "view", 0), ReadNum(n, "view", 1),
                                      ReadNum(n, "view", 2), ReadNum(n, "view", 3));
            if (n.ContainsKey("backgrounds") && n["backgrounds"] is List<object> backs)
                foreach (var item in backs)
                {
                    var b = item as Dictionary<string, object>;
                    if (b == null) continue;
                    st.backgrounds.Add(new StageBack {
                        file = Str(b, "file"), origin = ReadVec(b, "origin"), size = ReadVec(b, "size"),
                        x = Int(b, "x", 0), y = Int(b, "y", 0), rx = Int(b, "rx", 0), ry = Int(b, "ry", 0),
                        cx = Int(b, "cx", 0), cy = Int(b, "cy", 0), type = Int(b, "type", 0),
                        front = Int(b, "front", 0), alpha = Int(b, "a", 255), no = Int(b, "no", 0)
                    });
                }

            // foothold 线段（地图像素 y 向下）→ 世界坐标
            if (n.ContainsKey("footholds") && n["footholds"] is List<object> fh)
            {
                foreach (var o in fh)
                {
                    var e = o as List<object>;
                    if (e == null || e.Count < 4) continue;
                    float mx1 = ToF(e[0]), my1 = ToF(e[1]);
                    float mx2 = ToF(e[2]), my2 = ToF(e[3]);
                    st.Footholds.Add(new Vector4(st.WorldX(mx1), st.WorldY(my1),
                                                 st.WorldX(mx2), st.WorldY(my2)));
                }
            }

            if (n.ContainsKey("ladderRope") && n["ladderRope"] is List<object> ropes)
                foreach (var item in ropes)
                {
                    var r = item as Dictionary<string, object>;
                    if (r == null) continue;
                    st.Climbs.Add(new ClimbLine {
                        x = st.WorldX(Num(r,"x")), top = st.WorldY(Num(r,"y1")),
                        bottom = st.WorldY(Num(r,"y2")), ladder = Int(r,"l",0) != 0,
                        upperExit = Int(r,"uf",0) != 0
                    });
                }

            var files = n.ContainsKey("files") ? n["files"] as Dictionary<string, object> : null;
            if (files != null)
            {
                st.fileBack = Str(files, "back");
                st.fileMid = Str(files, "mid");
                st.fileFront = Str(files, "front");
            }
            var ord = n.ContainsKey("orders") ? n["orders"] as Dictionary<string, object> : null;
            if (ord != null)
            {
                st.orderBack = Int(ord, "back", -800);
                st.orderMid = Int(ord, "mid", -400);
                st.orderFront = Int(ord, "front", 600);
            }
            var native = NativeMapView.LoadMap(new FieldMap { id = int.Parse(st.map) });
            st.Footholds.Clear(); st.Climbs.Clear();
            foreach (var f in native.footholds) st.Footholds.Add(new Vector4(st.WorldX(f.x1), st.WorldY(f.y1), st.WorldX(f.x2), st.WorldY(f.y2)));
            foreach (var r in native.ropes) st.Climbs.Add(new ClimbLine { x = st.WorldX(r.x), top = st.WorldY(r.y1), bottom = st.WorldY(r.y2), ladder = r.ladder, upperExit = r.upperExit });
            st.view = new Vector4(native.vr[0], native.vr[1], native.vr[2], native.vr[3]); st.hasView = true;
            stages[kv.Key] = st;
        }
    }

    static float ReadNum(Dictionary<string, object> node, string key, int idx)
    {
        var arr = node.ContainsKey(key) ? node[key] as List<object> : null;
        return arr != null && idx < arr.Count ? ToF(arr[idx]) : 0f;
    }

    static Color ToColor(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }

    // —— 伤害数字（原版 Effect.wz/BasicEff.img 的 NoRed/NoCri/NoBlue/NoViolet） ——
    //
    // 每个数字由两层叠出来：edge 是带黑描边的深色外层，base 是白黄橙的内层高光。
    // 两者 origin 不同（外层的锚点比内层偏 3,5），所以必须各自按自己的 origin 摆，
    // 不能把两层合成一张图 —— 合成会把描边的位置写死，换字号就对不上了。
    public class DmgGlyph
    {
        public string file;
        public Vector2 origin;   // 像素，左上原点
        public Vector2 size;
    }

    public class DmgSkin
    {
        public DmgGlyph[] baseDigit = new DmgGlyph[10];
        public DmgGlyph[] edgeDigit;      // 可能是 null（个别皮肤没有外层）
        public DmgGlyph miss;
        public DmgGlyph fx;               // 暴击星爆
    }

    static Dictionary<string, DmgSkin> dmgSkins;

    public static DmgSkin Damage(string skin)
    {
        Ensure();
        if (dmgSkins == null || string.IsNullOrEmpty(skin)) return null;
        DmgSkin s;
        return dmgSkins.TryGetValue(skin, out s) ? s : null;
    }

    static void ReadDamage(Dictionary<string, object> root)
    {
        if (!root.ContainsKey("damage")) return;
        var dict = root["damage"] as Dictionary<string, object>;
        if (dict == null) return;
        dmgSkins = new Dictionary<string, DmgSkin>();
        foreach (var kv in dict)
        {
            var n = kv.Value as Dictionary<string, object>;
            if (n == null) continue;
            var s = new DmgSkin { baseDigit = ReadGlyphs(n, "base") };
            if (s.baseDigit == null) continue;
            s.edgeDigit = ReadGlyphs(n, "edge");
            s.miss = ReadGlyph(n, "miss");
            s.fx = ReadGlyph(n, "fx");
            dmgSkins[kv.Key] = s;
        }
    }

    static DmgGlyph[] ReadGlyphs(Dictionary<string, object> node, string key)
    {
        var arr = node.ContainsKey(key) ? node[key] as List<object> : null;
        if (arr == null) return null;
        var outArr = new DmgGlyph[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            var e = arr[i] as List<object>;
            if (e == null || e.Count < 5) continue;
            outArr[i] = new DmgGlyph
            {
                file = e[0].ToString(),
                origin = new Vector2(ToF(e[1]), ToF(e[2])),
                size = new Vector2(ToF(e[3]), ToF(e[4])),
            };
        }
        return outArr;
    }

    static DmgGlyph ReadGlyph(Dictionary<string, object> node, string key)
    {
        var e = node.ContainsKey(key) ? node[key] as List<object> : null;
        if (e == null || e.Count < 5) return null;
        return new DmgGlyph
        {
            file = e[0].ToString(),
            origin = new Vector2(ToF(e[1]), ToF(e[2])),
            size = new Vector2(ToF(e[3]), ToF(e[4])),
        };
    }

    // 按 WZ 的 origin 建轴心：origin 是「锚点相对左上角的像素偏移」，
    // 换到 Unity 的归一化轴心就是 (ox/w, 1 - oy/h)。
    public static Sprite FrameAt(string rel, Vector2 originPx)
    {
        Ensure();
        return Frame(rel, originPx, true);
    }

    public class Clip
    {
        public string[] frames;
        public float[] delays;             // 秒
        public float Duration
        {
            get { float d = 0f; for (int i = 0; i < delays.Length; i++) d += delays[i]; return Mathf.Max(0.05f, d); }
        }
    }

    public static void Ensure()
    {
        if (tried) return;
        tried = true;

        var ta = Resources.Load<TextAsset>("NativePresentation");
        if (ta == null)
        {
            throw new System.IO.InvalidDataException("Missing NativePresentation configuration");
        }

        var root = MiniJson.Parse(ta.text) as Dictionary<string, object>;
        if (root == null) { Debug.LogWarning("[Art] manifest 解析失败"); return; }

        ReadStages(root);
        ReadDamage(root);

        Ready = WzAssets.Index != null;
        if (Ready) Debug.Log("[Art] Native IMG resource library ready");
    }

    static Vector2 ReadVec(Dictionary<string, object> node, string key)    {
        if (!node.ContainsKey(key)) return Vector2.zero;
        var arr = node[key] as List<object>;
        if (arr == null || arr.Count < 2) return Vector2.zero;
        return new Vector2(ToF(arr[0]), ToF(arr[1]));
    }

    // 解析失败时返回 0f 而不是抛异常——数据里有布尔/脏值时（如 JobTree.json 的 resetStats:true）
    // 一旦抛 FormatException 会连累整段解析（Profile.Load 崩溃、游戏场景加载不完 → 启动超时）。
    static float ToF(object o)
    {
        if (o == null) return 0f;
        if (o is bool b) return b ? 1f : 0f;
        if (o is double d) return (float)d;
        if (o is long l) return (float)l;
        if (o is int i) return (float)i;
        if (o is float f) return f;
        if (o is string s && float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
        return 0f;
    }

    // —— 单帧 ——
    public static Sprite Frame(string rel)
    {
        Ensure();
        return string.IsNullOrEmpty(rel) ? null : WzAssets.Frame(rel);
    }

    public static Sprite FrameBottom(string rel) => Frame(rel);

    static Sprite Frame(string rel, Vector2 pivotPx, bool fromTop)
    {
        // Original Canvas origin supersedes the former exported-frame/union pivot.
        return Frame(rel);
    }

    public static Clip GetClip(string kind, string key, string anim)
    {
        Ensure();
        if (kind == "chars") return WzCharacter.Clip(key, anim);
        if (kind == "mobs") return WzAssets.MobClip(key, anim);
        return null;
    }

    public static bool Has(string kind, string key)
    {
        Ensure();
        return kind == "chars" ? WzCharacter.Has(key) : kind == "mobs" && (((Dictionary<string, object>)WzAssets.Index["mobs"]).ContainsKey(key) || int.TryParse(key, out _));
    }

    // 角色/怪物没有某个动作时的退化表：怪物没有 walk，只有 move
    public static string Resolve(string kind, string key, string anim, string fallback)
    {
        if (GetClip(kind, key, anim) != null) return anim;
        string alt = anim;
        if (anim == "walk") alt = "move";
        else if (anim == "stand") alt = "move";
        else if (anim == "atk") alt = "attack1";
        else if (anim == "hit") alt = "hit1";
        else if (anim == "die") alt = "die1";
        if (GetClip(kind, key, alt) != null) return alt;
        return fallback;
    }

    // 建一个子节点承载精灵：父节点 scale 保持 1，
    // 这样血条/红圈等按世界单位定位的东西不会被贴图缩放带歪。
    public static SpriteAnim Attach(Transform parent, string kind, string key, string idle,
                                    float targetHeight, string name = "Visual")
    {
        Ensure();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 300;

        SpriteAnim anim = null;
        if (Ready && Has(kind, key))
        {
            anim = go.AddComponent<SpriteAnim>();
            anim.Init(kind, key, idle, sr);
            var clip = GetClip(kind, key, Resolve(kind, key, idle, idle));
            if (clip != null)
            {
                var first = Frame(clip.frames[0]);
                if (first != null && targetHeight > 0.01f && first.bounds.size.y > 0.01f)
                {
                    float k = targetHeight / first.bounds.size.y;
                    go.transform.localScale = new Vector3(k, k, 1f);
                }
            }
        }
        else
        {
            sr.sprite = SpriteFactory.Bottom();
            go.transform.localScale = new Vector3(0.8f, 1.3f, 1f);
        }
        return anim;
    }

    // —— 技能图标 ——
    //
    // 图标是原版 32x32 的小图，用 IMGUI 画会糊成一片，所以统一按整数倍最近邻放大。

    public static Texture2D Tex(string rel)
    {
        Ensure();
        return string.IsNullOrEmpty(rel) ? null : WzAssets.Texture(rel);
    }

    public static Vector2 Origin(string rel)
    {
        Ensure();
        var node = string.IsNullOrEmpty(rel) ? null : WzAssets.Node(rel);
        return node == null ? Vector2.zero : WzAssets.Origin(node);
    }

    public static string Str(Dictionary<string, object> d, string key, string def = "")
        => d != null && d.ContainsKey(key) && d[key] != null ? d[key].ToString() : def;

    public static int Int(Dictionary<string, object> d, string key, int def = 0)
        => d != null && d.ContainsKey(key) && d[key] != null ? Mathf.RoundToInt(ToF(d[key])) : def;

    public static float Num(Dictionary<string, object> d, string key, float def = 0f)
        => d != null && d.ContainsKey(key) && d[key] != null ? ToF(d[key]) : def;

    // 背景图层是按「像素」存的，画的时候要按 PPU 换成世界单位
    public static Vector2 Size(Dictionary<string, object> d)
    {
        if (d == null || !d.ContainsKey("size")) return Vector2.one;
        var arr = d["size"] as List<object>;
        if (arr == null || arr.Count < 2) return Vector2.one;
        return new Vector2(ToF(arr[0]), ToF(arr[1]));
    }
}
