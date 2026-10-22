using System.Collections.Generic;
using UnityEngine;

// 原客户端地图数据（tools/wz_field.py 从 Map.wz / String.wz / Mob.wz / 原服 SQL 导出）。
// 这里只做「读数据 + 坐标换算」，任何玩法数值都不在这里臆造：
//   地图像素 → 世界单位：x / PPU、-y / PPU（地图 y 向下，世界 y 向上）

public sealed class FieldFoothold
{
    public int id, layer, group;
    public float x1, y1, x2, y2;
}

public sealed class FieldRope
{
    public float x, y1, y2;
    public bool ladder, upperExit;
}

public sealed class FieldPortal
{
    public string name, targetName, script;
    public int type, tm;
    public float x, y;
    public bool IsSpawn => type == 0;
    public bool Warps => tm > 0 && tm < 999999999;
}

public sealed class FieldSpawn
{
    public int mob, mobTime, fh, team, dir;
    public float x, y, rx0, rx1, cy;
}

public sealed class FieldNpc
{
    public int id;
    public int hide;                          // life/hide=1：原客户端不绘制
    public float x, y;
    public Dictionary<string, object> art;   // { file, origin }
}

public sealed class FieldBack
{
    public string file;
    public Vector2 origin, size;
    public int x, y, rx, ry, cx, cy, type, front, alpha, no;
}

public sealed class FieldChunk
{
    public string file;
    public float x, y, w, h;
}

public sealed class FieldMap
{
    public int id, returnMap, fieldLimit;
    public string name, street, bgm;
    public bool town;
    public float mobRate;
    public int[] vr = new int[4];

    public readonly List<FieldFoothold> footholds = new List<FieldFoothold>();
    public readonly List<FieldRope> ropes = new List<FieldRope>();
    public readonly List<FieldPortal> portals = new List<FieldPortal>();
    public readonly List<FieldSpawn> spawns = new List<FieldSpawn>();
    public readonly List<FieldNpc> npcs = new List<FieldNpc>();
    public readonly List<FieldBack> backs = new List<FieldBack>();
    public readonly List<FieldChunk> chunks = new List<FieldChunk>();

    public int Width => vr[2] - vr[0];
    public int Height => vr[3] - vr[1];

    public float WorldX(float mapX) => mapX / Art.PPU;
    public float WorldY(float mapY) => -mapY / Art.PPU;

    // VR 是原端相机中心的活动范围，换算成世界坐标后交给 CameraRig
    public float ViewMinX => WorldX(vr[0]);
    public float ViewMaxX => WorldX(vr[2]);
    public float ViewMinY => WorldY(vr[3]);
    public float ViewMaxY => WorldY(vr[1]);

    public FieldPortal Portal(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var p in portals) if (p.name == name) return p;
        return null;
    }

    public FieldPortal SpawnPortal
    {
        get
        {
            foreach (var p in portals) if (p.type == 0 && p.name == "sp") return p;
            foreach (var p in portals) if (p.type == 0) return p;
            return null;
        }
    }

    // Character.changeMap(to, null) 在原服回退到 to.getPortal(0)，即 WZ portal 列表里的第一条
    public FieldPortal FirstPortal => portals.Count > 0 ? portals[0] : null;
}

public sealed class FieldMob
{
    public int id;
    public string name, artKey;
    public Dictionary<string, object> stats;
}

public sealed class FieldDropEntry
{
    public int item, min, max, quest, chance;
}

public sealed class FieldItemInfo
{
    public int id;
    public string name, cat, icon;
}

public static class FieldCatalog
{
    public const int ChanceDenominator = 999999;

    public static bool Ready { get; private set; }
    public static readonly Dictionary<int, FieldMap> Maps = new Dictionary<int, FieldMap>();
    public static readonly Dictionary<int, FieldMob> Mobs = new Dictionary<int, FieldMob>();
    public static readonly Dictionary<int, List<FieldDropEntry>> Drops =
        new Dictionary<int, List<FieldDropEntry>>();
    public static readonly List<FieldDropEntry> GlobalDrops = new List<FieldDropEntry>();
    public static readonly Dictionary<int, FieldItemInfo> Items = new Dictionary<int, FieldItemInfo>();

    static bool tried;

    public static void Ensure()
    {
        if (tried) return;
        tried = true;

        var maps = Resources.Load<TextAsset>("Field/FieldMaps");
        if (maps == null)
        {
            Debug.LogWarning("[Field] 缺少 Field/FieldMaps.json，原版地图模式不可用");
            return;
        }

        var root = MiniJson.Parse(maps.text) as Dictionary<string, object>;
        var list = GetList(root, "maps");
        if (list != null)
            foreach (var item in list)
            {
                var m = BuildMap(item as Dictionary<string, object>);
                if (m != null) Maps[m.id] = m;
            }

        var mobs = Resources.Load<TextAsset>("Field/FieldMobs");
        if (mobs != null)
        {
            var mr = MiniJson.Parse(mobs.text) as Dictionary<string, object>;
            var arr = GetList(mr, "mobs");
            if (arr != null)
                foreach (var item in arr)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    var mob = new FieldMob
                    {
                        id = (int)Num(d, "id"),
                        name = Str(d, "name"),
                        artKey = Str(d, "artKey"),
                        stats = d.ContainsKey("stats") ? d["stats"] as Dictionary<string, object>
                                                       : new Dictionary<string, object>(),
                    };
                    Mobs[mob.id] = mob;
                }
        }

        var drops = Resources.Load<TextAsset>("Field/FieldDrops");
        if (drops != null)
        {
            var dr = MiniJson.Parse(drops.text) as Dictionary<string, object>;
            var table = dr != null && dr.ContainsKey("drops") ? dr["drops"] as Dictionary<string, object> : null;
            if (table != null)
                foreach (var kv in table)
                {
                    int id;
                    if (!int.TryParse(kv.Key, out id)) continue;
                    var rows = new List<FieldDropEntry>();
                    foreach (var row in kv.Value as List<object>)
                    {
                        var d = row as Dictionary<string, object>;
                        if (d == null) continue;
                        rows.Add(new FieldDropEntry
                        {
                            item = (int)Num(d, "item"), min = (int)Num(d, "min", 1),
                            max = (int)Num(d, "max", 1), quest = (int)Num(d, "quest"),
                            chance = (int)Num(d, "chance"),
                        });
                    }
                    Drops[id] = rows;
                }
            var glob = dr != null ? dr["globalDrops"] as List<object> : null;
            if (glob != null)
                foreach (var row in glob)
                {
                    var d = row as Dictionary<string, object>;
                    if (d == null) continue;
                    GlobalDrops.Add(new FieldDropEntry
                    {
                        item = (int)Num(d, "item"), min = (int)Num(d, "min", 1),
                        max = (int)Num(d, "max", 1), quest = (int)Num(d, "quest"),
                        chance = (int)Num(d, "chance"),
                    });
                }
            var items = dr != null ? dr["items"] as Dictionary<string, object> : null;
            if (items != null)
                foreach (var kv in items)
                {
                    var d = kv.Value as Dictionary<string, object>;
                    if (d == null) continue;
                    var info = new FieldItemInfo
                    {
                        id = (int)Num(d, "id"), name = Str(d, "name"),
                        cat = Str(d, "cat"), icon = Str(d, "icon"),
                    };
                    Items[info.id] = info;
                }
        }

        FieldPortalArt.Ensure();

        Ready = Maps.Count > 0;
        Debug.Log("[Field] 原版地图 " + Maps.Count + " 张，怪物 " + Mobs.Count + " 种，掉落表 " +
                  Drops.Count + " 份，物品 " + Items.Count + " 件");
    }
    static FieldMap BuildMap(Dictionary<string, object> d)
    {
        if (d == null || !d.ContainsKey("vr")) return null;
        var m = new FieldMap
        {
            id = (int)Num(d, "id"), name = Str(d, "name"), street = Str(d, "street"),
            bgm = Str(d, "bgm"), town = Num(d, "town") != 0,
            returnMap = (int)Num(d, "returnMap"), fieldLimit = (int)Num(d, "fieldLimit"),
            mobRate = Num(d, "mobRate", 1f),
        };
        var vr = GetList(d, "vr");
        if (vr != null && vr.Count >= 4)
            for (int i = 0; i < 4; i++) m.vr[i] = (int)ToF(vr[i]);

        var fh = GetList(d, "footholds");
        if (fh != null)
            foreach (var item in fh)
            {
                var f = item as Dictionary<string, object>;
                if (f == null) continue;
                m.footholds.Add(new FieldFoothold
                {
                    id = (int)Num(f, "id"), layer = (int)Num(f, "l"), group = (int)Num(f, "g"),
                    x1 = Num(f, "x1"), y1 = Num(f, "y1"), x2 = Num(f, "x2"), y2 = Num(f, "y2"),
                });
            }

        var ropes = GetList(d, "ropes");
        if (ropes != null)
            foreach (var item in ropes)
            {
                var r = item as Dictionary<string, object>;
                if (r == null) continue;
                m.ropes.Add(new FieldRope
                {
                    x = Num(r, "x"), y1 = Num(r, "y1"), y2 = Num(r, "y2"),
                    ladder = Num(r, "l") != 0, upperExit = Num(r, "uf") != 0,
                });
            }

        var portals = GetList(d, "portals");
        if (portals != null)
            foreach (var item in portals)
            {
                var p = item as Dictionary<string, object>;
                if (p == null) continue;
                m.portals.Add(new FieldPortal
                {
                    name = Str(p, "pn"), targetName = Str(p, "tn"), type = (int)Num(p, "pt"),
                    tm = (int)Num(p, "tm"), x = Num(p, "x"), y = Num(p, "y"), script = Str(p, "script"),
                });
            }

        var spawns = GetList(d, "spawns");
        if (spawns != null)
            foreach (var item in spawns)
            {
                var s = item as Dictionary<string, object>;
                if (s == null) continue;
                m.spawns.Add(new FieldSpawn
                {
                    mob = (int)Num(s, "mob"), mobTime = (int)Num(s, "mobTime"),
                    x = Num(s, "x"), y = Num(s, "y"), fh = (int)Num(s, "fh"),
                    rx0 = Num(s, "rx0"), rx1 = Num(s, "rx1"), cy = Num(s, "cy"),
                    dir = (int)Num(s, "f"), team = (int)Num(s, "team", -1),
                });
            }

        var npcs = GetList(d, "npcs");
        if (npcs != null)
            foreach (var item in npcs)
            {
                var n = item as Dictionary<string, object>;
                if (n == null) continue;
                m.npcs.Add(new FieldNpc
                {
                    id = (int)Num(n, "id"), x = Num(n, "x"), y = Num(n, "y"),
                    hide = (int)Num(n, "hide"),
                    art = n.ContainsKey("art") ? n["art"] as Dictionary<string, object> : null,
                });
            }

        var backs = GetList(d, "backs");
        if (backs != null)
            foreach (var item in backs)
            {
                var b = item as Dictionary<string, object>;
                if (b == null) continue;
                m.backs.Add(new FieldBack
                {
                    file = Str(b, "file"), origin = Vec(b, "origin"), size = Vec(b, "size"),
                    x = (int)Num(b, "x"), y = (int)Num(b, "y"), rx = (int)Num(b, "rx"),
                    ry = (int)Num(b, "ry"), cx = (int)Num(b, "cx"), cy = (int)Num(b, "cy"),
                    type = (int)Num(b, "type"), front = (int)Num(b, "front"),
                    alpha = (int)Num(b, "a", 255), no = (int)Num(b, "no"),
                });
            }

        var body = d.ContainsKey("body") ? d["body"] as Dictionary<string, object> : null;
        var chunks = body != null ? GetList(body, "chunks") : null;
        if (chunks != null)
            foreach (var item in chunks)
            {
                var c = item as Dictionary<string, object>;
                if (c == null) continue;
                var pos = GetList(c, "pos");
                var size = GetList(c, "size");
                if (pos == null || size == null || pos.Count < 2 || size.Count < 2) continue;
                m.chunks.Add(new FieldChunk
                {
                    file = Str(c, "file"), x = ToF(pos[0]), y = ToF(pos[1]),
                    w = ToF(size[0]), h = ToF(size[1]),
                });
            }
        return m;
    }

    // ——— JSON 取值 ———
    public static float ToF(object o)
    {
        if (o == null) return 0f;
        if (o is double) return (float)(double)o;
        if (o is float) return (float)o;
        if (o is int) return (int)o;
        float v;
        return float.TryParse(o.ToString(), System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f;
    }

    public static float Num(Dictionary<string, object> d, string key, float def = 0f)
        => d != null && d.ContainsKey(key) ? ToF(d[key]) : def;
    public static string Str(Dictionary<string, object> d, string key)
        => d != null && d.ContainsKey(key) && d[key] != null ? d[key].ToString() : "";
    public static List<object> GetList(Dictionary<string, object> d, string key)
        => d != null && d.ContainsKey(key) ? d[key] as List<object> : null;
    public static Vector2 Vec(Dictionary<string, object> d, string key)
    {
        var a = GetList(d, key);
        return a != null && a.Count >= 2 ? new Vector2(ToF(a[0]), ToF(a[1])) : Vector2.zero;
    }
}

// 传送门「光洞」动画：MapHelper.img/portal/game/pv（原端 pt=2 才画）。
// 帧表由 tools/wz_field.py --portals 导出，贴图与地图主体同一套 art/ 目录。
public static class FieldPortalArt
{
    public sealed class Frame
    {
        public string file;
        public Vector2 origin;
        public float delay;          // 秒
    }

    public static readonly List<Frame> Visible = new List<Frame>();
    static bool tried;

    public static void Ensure()
    {
        if (tried) return;
        tried = true;

        var ta = Resources.Load<TextAsset>("Field/FieldPortals");
        if (ta == null)
        {
            Debug.LogWarning("[Field] 缺少 Field/FieldPortals.json，传送门不会有光洞");
            return;
        }

        var root = MiniJson.Parse(ta.text) as Dictionary<string, object>;
        var pv = root != null && root.ContainsKey("pv") ? root["pv"] as Dictionary<string, object> : null;
        var frames = FieldCatalog.GetList(pv, "frames");
        if (frames == null) return;

        foreach (var item in frames)
        {
            var d = item as Dictionary<string, object>;
            if (d == null) continue;
            Visible.Add(new Frame
            {
                file = FieldCatalog.Str(d, "file"),
                origin = FieldCatalog.Vec(d, "origin"),
                delay = Mathf.Max(0.03f, FieldCatalog.Num(d, "delay", 120f) / 1000f),
            });
        }
        Debug.Log("[Field] 传送门动画 " + Visible.Count + " 帧");
    }
}
