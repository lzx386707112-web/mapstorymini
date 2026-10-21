using System.Collections.Generic;
using UnityEngine;

// 素材库：把 tools/wz_assets.py 从客户端 WZ 抽出来的 PNG 读进 Unity。
//
// 为什么不放 Assets/Resources/Art/*.png：那样要靠 Unity 的贴图导入设置
// （Texture Type = Sprite、pivot、PPU），换台机器/换个人重装项目就会走样。
// 这里统一存成 .png.bytes（Unity 当二进制资源），运行时 Texture2D.LoadImage
// 自己建 Sprite —— 轴心、PPU 全部来自 manifest.json，跨平台一致。
//
// manifest 结构见 art/manifest.json：chars / mobs / tiles / backs。
public static class Art
{
    public const float PPU = 40f;          // 1 世界单位 = 40px（与 Px.ToUnit 一致）

    public static bool Ready { get; private set; }
    static bool tried;

    class Sheet
    {
        public Vector2 pivot;              // 像素，左上原点
        public Dictionary<string, object> anims;
        public Dictionary<string, object> parts;   // 部件的原版标量参数（count/interval/effectDistance）
    }

    static readonly Dictionary<string, Sheet> sheets = new Dictionary<string, Sheet>();
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    // 技能一个 key 下有多个部件（cast / ball / hit / tile ...），每个部件的画布和锚点都不同，
    // 所以轴心按「帧路径」记录，而不是按整套技能一个 pivot。
    static readonly Dictionary<string, Vector2> framePivot = new Dictionary<string, Vector2>();
    static Dictionary<string, object> tiles, backs;

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

        var ta = Resources.Load<TextAsset>("Art/manifest");
        if (ta == null)
        {
            Debug.LogWarning("[Art] 找不到 Art/manifest，继续用纯色方块");
            return;
        }

        var root = MiniJson.Parse(ta.text) as Dictionary<string, object>;
        if (root == null) { Debug.LogWarning("[Art] manifest 解析失败"); return; }

        ReadSheets(root, "chars");
        ReadSheets(root, "mobs");
        ReadSheets(root, "skills");        // 技能特效帧（结构与角色一致，只是动作固定叫 cast）
        tiles = root.ContainsKey("tiles") ? root["tiles"] as Dictionary<string, object> : null;
        backs = root.ContainsKey("backs") ? root["backs"] as Dictionary<string, object> : null;
        ReadStages(root);
        ReadDamage(root);

        Ready = sheets.Count > 0;
        if (Ready) Debug.Log("[Art] 素材就绪：" + sheets.Count + " 组");
    }

    static void ReadSheets(Dictionary<string, object> root, string kind)
    {
        if (!root.ContainsKey(kind)) return;
        var dict = root[kind] as Dictionary<string, object>;
        if (dict == null) return;
        foreach (var kv in dict)
        {
            var node = kv.Value as Dictionary<string, object>;
            if (node == null) continue;
            var s = new Sheet { pivot = Vector2.zero };
            s.pivot = ReadVec(node, "pivot");
            s.anims = node.ContainsKey("anims") ? node["anims"] as Dictionary<string, object> : null;
            s.parts = node.ContainsKey("parts") ? node["parts"] as Dictionary<string, object> : null;
            sheets[kind + "/" + kv.Key] = s;

            // 每个动画自带 pivot（技能部件用）；没有就回落到整套的 pivot（角色/怪物）
            if (s.anims == null) continue;
            foreach (var a in s.anims)
            {
                var anim = a.Value as Dictionary<string, object>;
                if (anim == null || !anim.ContainsKey("frames")) continue;
                var pivot = anim.ContainsKey("pivot") ? ReadVec(anim, "pivot") : s.pivot;
                var frames = anim["frames"] as List<object>;
                if (frames == null) continue;
                foreach (var f in frames) framePivot[f.ToString()] = pivot;
            }
        }
    }

    static Vector2 ReadVec(Dictionary<string, object> node, string key)    {
        if (!node.ContainsKey(key)) return Vector2.zero;
        var arr = node[key] as List<object>;
        if (arr == null || arr.Count < 2) return Vector2.zero;
        return new Vector2(ToF(arr[0]), ToF(arr[1]));
    }

    static float ToF(object o) => o == null ? 0f : float.Parse(o.ToString(), System.Globalization.CultureInfo.InvariantCulture);

    // —— 单帧 ——
    public static Sprite Frame(string rel)
    {
        Ensure();
        if (!Ready || string.IsNullOrEmpty(rel)) return null;

        int cut = rel.LastIndexOf('/');
        string sheetKey = cut > 0 ? rel.Substring(0, cut) : rel;
        Vector2 pivotPx = Vector2.zero;
        Sheet sheet;
        if (sheets.TryGetValue(sheetKey, out sheet)) pivotPx = sheet.pivot;
        // 技能部件帧（skills/<key>/<part>/f000.png）用的是部件自己的轴心
        Vector2 own;
        if (framePivot.TryGetValue(rel, out own)) pivotPx = own;

        // manifest 的 pivot 是像素、左上原点；换算出 Unity 的归一化轴心
        return Frame(rel, pivotPx, true);
    }

    // 背景图层用：origin=(0,0) 在 WZ 里是「左上角」，当背景铺地要改成左下角
    public static Sprite FrameBottom(string rel)
    {
        Ensure();
        return Frame(rel, Vector2.zero, false);
    }

    static Sprite Frame(string rel, Vector2 pivotPx, bool fromTop)
    {
        string key = rel + (fromTop ? "" : "|bl");
        Sprite sp;
        if (cache.TryGetValue(key, out sp) && sp != null) return sp;

        // 贴图存成 "xxx.bytes"（不带 .png），路径里也就不能带扩展名
        string path = "Art/" + rel.Substring(0, rel.Length - 4);
        var ta = Resources.Load<TextAsset>(path);
        if (ta == null) return null;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(ta.bytes)) { Object.Destroy(tex); return null; }
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.name = rel;

        float w = tex.width, h = tex.height;
        Vector2 pivot = fromTop
            ? new Vector2(w > 0 ? Mathf.Clamp01(pivotPx.x / w) : 0.5f,
                          h > 0 ? Mathf.Clamp01(1f - pivotPx.y / h) : 0f)
            : Vector2.zero;   // 左下角

        sp = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, PPU, 0, SpriteMeshType.FullRect);
        sp.name = rel;
        cache[key] = sp;
        return sp;
    }

    public static Clip GetClip(string kind, string key, string anim)
    {
        Ensure();
        Sheet s;
        if (!sheets.TryGetValue(kind + "/" + key, out s) || s.anims == null) return null;
        if (!s.anims.ContainsKey(anim)) return null;
        var node = s.anims[anim] as Dictionary<string, object>;
        if (node == null) return null;

        var fl = node.ContainsKey("frames") ? node["frames"] as List<object> : null;
        var dl = node.ContainsKey("delays") ? node["delays"] as List<object> : null;
        if (fl == null || fl.Count == 0) return null;

        var clip = new Clip
        {
            frames = new string[fl.Count],
            delays = new float[fl.Count],
        };
        for (int i = 0; i < fl.Count; i++)
        {
            clip.frames[i] = fl[i].ToString();
            clip.delays[i] = (dl != null && i < dl.Count) ? ToF(dl[i]) / 1000f : 0.15f;
        }
        return clip;
    }

    public static bool Has(string kind, string key)
    {
        Ensure();
        return sheets.ContainsKey(kind + "/" + key);
    }

    // 部件里的原版标量参数（special.count / special.interval / tile.effectDistance ...）
    public static Dictionary<string, object> PartMeta(string kind, string key, string part)
    {
        Ensure();
        Sheet s;
        if (!sheets.TryGetValue(kind + "/" + key, out s) || s.parts == null) return null;
        return s.parts.ContainsKey(part) ? s.parts[part] as Dictionary<string, object> : null;
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
    static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

    public static Texture2D Tex(string rel)
    {
        Ensure();
        if (string.IsNullOrEmpty(rel)) return null;
        Texture2D tex;
        if (texCache.TryGetValue(rel, out tex) && tex != null) return tex;

        string path = "Art/" + rel.Substring(0, rel.Length - 4);
        var ta = Resources.Load<TextAsset>(path);
        if (ta == null) return null;
        tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(ta.bytes)) { Object.Destroy(tex); return null; }
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        texCache[rel] = tex;
        return tex;
    }

    // 技能图标：约定路径 skills/<key>/icon.png（由 tools/wz_skill.py 生成）
    public static Texture2D SkillIcon(string skillKey)
    {
        Ensure();
        if (string.IsNullOrEmpty(skillKey)) return null;
        return Tex("skills/" + skillKey + "/icon.png");
    }

    // —— 地形 / 背景 ——
    public static List<Dictionary<string, object>> BackLayers(string set)
    {
        Ensure();
        var outList = new List<Dictionary<string, object>>();
        if (backs == null || !backs.ContainsKey(set)) return outList;
        var arr = backs[set] as List<object>;
        if (arr == null) return outList;
        foreach (var o in arr)
        {
            var d = o as Dictionary<string, object>;
            if (d != null) outList.Add(d);
        }
        return outList;
    }

    public static List<Dictionary<string, object>> TileGroup(string set, string group)
    {
        Ensure();
        var outList = new List<Dictionary<string, object>>();
        if (tiles == null || !tiles.ContainsKey(set)) return outList;
        var setDict = tiles[set] as Dictionary<string, object>;
        if (setDict == null || !setDict.ContainsKey(group)) return outList;
        var arr = setDict[group] as List<object>;
        if (arr == null) return outList;
        foreach (var o in arr)
        {
            var d = o as Dictionary<string, object>;
            if (d != null) outList.Add(d);
        }
        return outList;
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
