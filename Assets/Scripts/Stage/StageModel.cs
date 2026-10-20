using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// chapter1.json 的运行时模型。
// JSON 是唯一数据源，Unity 端只读不写：改关卡数值请改 data/chapter1.json，
// 然后重跑 data/build.py（同步注入网页原型），再把 json 拷到
// Assets/Resources/Stage/chapter1.json（本类的加载路径）。
// ─────────────────────────────────────────────────────────────

// 像素 → 世界单位的换算。
// 网页原型的内部坐标是「玩家高 60px」，Unity 里玩家高 1.5 世界单位，
// 所以 1 世界单位 = 40px。所有 JSON 里的像素量（speed / radius）都要过这个函数。
public static class Px
{
    public const float PerUnit = 40f;
    public static float ToUnit(float px) => px / PerUnit;
}

public class StageSpawn
{
    public string type = "mushroom";
    public int    count = 1;
    public float  gap = 0.5f;
    public string side = "right";
    public string z = "random";
}

public class StageSegment
{
    public string kind = "walk";      // walk / combat / rest / boss
    public float  duration;           // walk / rest 用
    public string hint = "";
    public string name = "";
    public string boss = "";
    public List<StageSpawn> spawns = new List<StageSpawn>();
}

public class StageLevel
{
    public string id = "";
    public string name = "";
    public string goal = "";
    public string theme = "";
    public string teaches = "";
    public int    suppression;
    public int    recLevel = 1;
    public List<string> dialogue = new List<string>();
    public List<StageSegment> segments = new List<StageSegment>();
    public int rewardExp, rewardGold;
}

public class SlamDef
{
    public float telegraph = 1.0f;    // 红圈预警时长
    public float air = 0.5f;          // 滞空
    public float radius = 122f;       // 像素
    public float damage = 26f;
    public float recover = 1.6f;      // 落地硬直 = 唯一输出窗口
}

public class PhaseDef
{
    public float at = 1f;             // 血量比例阈值
    public float cooldown = 2.2f;     // 该阶段技能间隔
}

public class SummonDef
{
    public int    count = 3;
    public float  gap = 0.35f;
    public string type = "mushroom";
}

public class StageBossDef
{
    public string id = "";
    public string name = "";
    public string type = "";
    public string hint = "";
    public float  hp = 900f;
    public float  spd = 46f;          // 像素/秒
    public float  scale = 3f;
    public float  contact = 16f;
    public SlamDef slam = new SlamDef();
    public SummonDef summon = new SummonDef();
    public List<PhaseDef> phases = new List<PhaseDef>();
    public List<string> pattern = new List<string>();
}

public class StageTheme
{
    public Color bgTop, bgBottom, mid, ground, groundDeep, accent;
}

public class StageChapter
{
    public int chapterId;
    public string name = "";
    public string subtitle = "";
    public string goal = "";
    public Dictionary<string, StageTheme>    themes = new Dictionary<string, StageTheme>();
    public Dictionary<string, StageBossDef>  bosses = new Dictionary<string, StageBossDef>();
    public List<StageLevel> levels = new List<StageLevel>();

    // ——————————————— 加载 ———————————————

    public static StageChapter Load(string resourcePath)
    {
        var ta = Resources.Load<TextAsset>(resourcePath);
        if (ta == null)
        {
            Debug.LogWarning("[Stage] 找不到关卡数据：" + resourcePath +
                             "（应放在 Assets/Resources/" + resourcePath + ".json）");
            return null;
        }
        return FromJson(ta.text);
    }

    public static StageChapter FromJson(string json)
    {
        var root = MiniJson.Parse(json) as Dictionary<string, object>;
        if (root == null) { Debug.LogError("[Stage] JSON 根节点不是对象"); return null; }

        var c = new StageChapter();

        var ch = Dict(root, "chapter");
        if (ch != null)
        {
            c.chapterId = Int(ch, "id");
            c.name      = Str(ch, "name");
            c.subtitle  = Str(ch, "subtitle");
            c.goal      = Str(ch, "goal");
        }

        var th = Dict(root, "themes");
        if (th != null)
            foreach (var kv in th)
            {
                var t = kv.Value as Dictionary<string, object>;
                if (t == null) continue;
                c.themes[kv.Key] = new StageTheme
                {
                    bgTop      = Hex(Str(t, "bgTop"),      new Color(0.11f, 0.13f, 0.18f)),
                    bgBottom   = Hex(Str(t, "bgBottom"),   new Color(0.16f, 0.19f, 0.25f)),
                    mid        = Hex(Str(t, "mid"),        new Color(0.19f, 0.23f, 0.29f)),
                    ground     = Hex(Str(t, "ground"),     new Color(0.17f, 0.19f, 0.23f)),
                    groundDeep = Hex(Str(t, "groundDeep"), new Color(0.12f, 0.13f, 0.16f)),
                    accent     = Hex(Str(t, "accent"),     new Color(0.42f, 0.62f, 0.5f)),
                };
            }

        var bs = Dict(root, "bosses");
        if (bs != null)
            foreach (var kv in bs)
            {
                var b = kv.Value as Dictionary<string, object>;
                if (b == null) continue;
                var def = new StageBossDef
                {
                    id      = kv.Key,
                    name    = Str(b, "name"),
                    type    = Str(b, "type"),
                    hint    = Str(b, "hint"),
                    hp      = Flt(b, "hp", 900f),
                    spd     = Flt(b, "spd", 46f),
                    scale   = Flt(b, "scale", 3f),
                    contact = Flt(b, "contact", 16f),
                };
                var sl = Dict(b, "slam");
                if (sl != null)
                    def.slam = new SlamDef
                    {
                        telegraph = Flt(sl, "telegraph", 1.0f),
                        air       = Flt(sl, "air", 0.5f),
                        radius    = Flt(sl, "radius", 122f),
                        damage    = Flt(sl, "damage", 26f),
                        recover   = Flt(sl, "recover", 1.6f),
                    };
                var sm = Dict(b, "summon");
                if (sm != null)
                    def.summon = new SummonDef
                    {
                        count = Int(sm, "count", 3),
                        gap   = Flt(sm, "gap", 0.35f),
                        type  = Str(sm, "type", "mushroom"),
                    };
                var ph = Arr(b, "phases");
                if (ph != null)
                    foreach (var o in ph)
                    {
                        var p = o as Dictionary<string, object>;
                        if (p == null) continue;
                        def.phases.Add(new PhaseDef { at = Flt(p, "at", 1f), cooldown = Flt(p, "cooldown", 2.2f) });
                    }
                var pt = Arr(b, "pattern");
                if (pt != null)
                    foreach (var o in pt) if (o is string) def.pattern.Add((string)o);
                c.bosses[kv.Key] = def;
            }

        var lvs = Arr(root, "levels");
        if (lvs != null)
            foreach (var o in lvs)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var lv = new StageLevel
                {
                    id          = Str(d, "id"),
                    name        = Str(d, "name"),
                    goal        = Str(d, "goal"),
                    theme       = Str(d, "theme"),
                    teaches     = Str(d, "teaches"),
                    suppression = Int(d, "suppression"),
                    recLevel    = Int(d, "recLevel", 1),
                };
                var dg = Arr(d, "dialogue");
                if (dg != null)
                    foreach (var x in dg)
                        if (x is string) lv.dialogue.Add((string)x);

                var rw = Dict(d, "rewards");
                if (rw != null) { lv.rewardExp = Int(rw, "exp"); lv.rewardGold = Int(rw, "gold"); }

                var segs = Arr(d, "segments");
                if (segs != null)
                    foreach (var so in segs)
                    {
                        var sd = so as Dictionary<string, object>;
                        if (sd == null) continue;
                        var seg = new StageSegment
                        {
                            kind     = Str(sd, "kind", "walk"),
                            duration = Flt(sd, "duration"),
                            hint     = Str(sd, "hint"),
                            name     = Str(sd, "name"),
                            boss     = Str(sd, "bossId", Str(sd, "boss")),
                        };
                        var sps = Arr(sd, "spawns");
                        if (sps != null)
                            foreach (var po in sps)
                            {
                                var pd = po as Dictionary<string, object>;
                                if (pd == null) continue;
                                seg.spawns.Add(new StageSpawn
                                {
                                    type  = Str(pd, "type", "mushroom"),
                                    count = Int(pd, "count", 1),
                                    gap   = Flt(pd, "gap", 0.5f),
                                    side  = Str(pd, "side", "right"),
                                    z     = Str(pd, "z", "random"),
                                });
                            }
                        lv.segments.Add(seg);
                    }
                c.levels.Add(lv);
            }

        return c;
    }

    // ——————————————— 取值小工具 ———————————————

    static Dictionary<string, object> Dict(Dictionary<string, object> d, string k)
        => d != null && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;

    static List<object> Arr(Dictionary<string, object> d, string k)
        => d != null && d.TryGetValue(k, out var v) ? v as List<object> : null;

    static string Str(Dictionary<string, object> d, string k, string def = "")
        => d != null && d.TryGetValue(k, out var v) && v is string s ? s : def;

    static float Flt(Dictionary<string, object> d, string k, float def = 0f)
    {
        if (d == null || !d.TryGetValue(k, out var v) || v == null) return def;
        if (v is double dv) return (float)dv;
        if (v is string sv && float.TryParse(sv, out var f)) return f;
        return def;
    }

    static int Int(Dictionary<string, object> d, string k, int def = 0)
    {
        if (d == null || !d.TryGetValue(k, out var v) || v == null) return def;
        if (v is double dv) return (int)dv;
        return def;
    }

    // "#rrggbb" 或 "#rrggbbaa"
    public static Color Hex(string s, Color fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        s = s.TrimStart('#');
        if (s.Length != 6 && s.Length != 8) return fallback;
        int r = System.Convert.ToInt32(s.Substring(0, 2), 16);
        int g = System.Convert.ToInt32(s.Substring(2, 2), 16);
        int b = System.Convert.ToInt32(s.Substring(4, 2), 16);
        int a = s.Length == 8 ? System.Convert.ToInt32(s.Substring(6, 2), 16) : 255;
        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }
}
