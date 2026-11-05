using System;
using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// 282 的 shootobj 是「Actor 化发射物」（EXE 里 ASkillProjectile / WZ2SkillProjectileSpawnParam），
// 不走 ball 的逐帧 Canvas，而是「出生点 + 路径点 + 速度 + 独立层动画」。
//
// v 的单位：像素 / 逻辑帧（60fps）。判定依据是 30 个 shootobj 的飞行时长与 layerList 动画时长
// 交叉比对（见 docs/tms282-skill/23-shootobj-ballistics.md）：
//   400021004  dist/v=12.5  → 750ms，b1 8 帧 = 720ms（动画播完即命中）
//   400051003  dist/v=78.4  → 1307ms，b1 17 帧 = 1500ms
// 若按 100ms 口径，上面两个会变成 4500ms / 7844ms，比动画长 6 倍以上，不成立。
// 所以速度 = v * 60 px/s。
public sealed class ShootObjPoint
{
    public Vector2 pos;      // WZ 坐标：x 前向为负，y 向上为负
    public float v;          // 像素 / 帧 @60fps
    public float delayMs;    // 到达该点之前的等待
    public bool hasPos;
}
public sealed class ShootObjPath
{
    public string name;
    public readonly List<ShootObjPoint> points = new List<ShootObjPoint>();
}
public sealed class ShootObjData
{
    public int skillId;
    public Vector2 start, bodyWH = new Vector2(100, 100);
    public float startDelayMs, deleteAlphaDelayMs;
    public int deleteAlpha;              // 1 = 消失前渐隐（22/30 个 shootobj 带）
    public float infoSpeed;              // info/v：没有 moveList 的技能把速度写在 info 上（5211020 / 5241013）
    public int maxMobCount, totalHitCount;
    public bool pickDir3Way, pickDir5Way, multiStartPos;
    public int groupCount = 1;
    public float groupIntervalMs, groupVMin, groupVMax;
    public float randomDelayMinMs, randomDelayMaxMs;
    public string sprayType;

    // —— 命中预算族（282 用这一组控制「一支弹幕能打几下」） ——
    // hitLimitEachMob：单个目标最多被这支弹幕命中几次（9 个技能带，值 1..40）
    // hitLimitEveryMob：同上但含 multiAttack 段（5 个技能带，与 multiAttackCount 同值）
    // totalHitCount：整个发射物总共能命中几次（3 个：1321025/1341009=3、2341000=12）
    public int hitLimitEachMob, hitLimitEveryMob;
    public int multiAttackCount = 1;     // 命中后同一目标的追加段数
    public float multiAttackHitDelayMs;  // 追加段间隔
    public bool noHitWhenMaxCount;       // 达上限后不再命中
    public bool useUpWhenMaxMobCount;    // 打满 maxMobCount 就销毁（11 个技能带）

    // —— 表现族 ——
    public bool rotateAttack;            // 飞行层朝飞行方向旋转
    public float flipRotate;             // rorateWhenFlip（源端拼写错）：镜像时额外转 180°
    public float startAlphaDelayMs;      // 起手后延迟渐显
    public bool startOffset;             // group 间起点递推
    public bool triggerSpecialCore;      // 命中后触发六转特殊核心
    public bool chaseTarget;             // sprayType=AngelRay：追踪最近目标

    public readonly List<ShootObjPath> paths = new List<ShootObjPath>();
    public readonly List<string> layers = new List<string>();
    public readonly List<Vector2> angles = new List<Vector2>();   // angleList：min/max 角度

    // 一次碰撞判定最多结算几个目标（maxMobCount 的原意是「每次判定」而不是「总共」）。
    public int PerSweep => Mathf.Max(1, maxMobCount);
    // 单个目标最多被命中几次。两个字段在源端并存，取大的那个不会漏限制。
    public int PerMobLimit => Mathf.Max(1, Mathf.Max(hitLimitEachMob, hitLimitEveryMob));
    // 整个发射物的命中总预算；totalHitCount=0 时退到 noHitWhenMaxCount 的 mobCount 口径。
    public int HitBudget(int fallback) => totalHitCount > 0 ? totalHitCount : noHitWhenMaxCount ? Mathf.Max(1, fallback) : int.MaxValue;

    public const float FramesPerSecond = 60f;
    public static float Speed(float v) => v <= 0 ? 0 : v * FramesPerSecond;

    // 一条路径飞完需要多久（含段间 delay），用于守卫与调试。
    public float Duration(ShootObjPath path)
    {
        float total = 0;
        Vector2 cur = start;
        foreach (var point in path.points)
        {
            total += point.delayMs;
            float speed = Speed(point.v);
            if (speed > 0)
            {
                float dx = point.pos.x - cur.x, dy = point.pos.y - cur.y;
                total += Mathf.Sqrt(dx * dx + dy * dy) / speed * 1000f;
            }
            cur = point.pos;
        }
        return total;
    }
}

public static class NativeShootObj
{
    static readonly Dictionary<int, ShootObjData> cache = new Dictionary<int, ShootObjData>();

    public static ShootObjData Get(int id)
    {
        if (cache.TryGetValue(id, out var cached)) return cached;
        var data = Read(id);
        cache.Add(id, data);
        return data;
    }
    public static bool Has(int id) => Get(id) != null;

    static ShootObjData Read(int id)
    {
        var root = WzAssets.Node("Skill/" + (id / 10000).ToString("D3") + ".img/skill/" + id.ToString("D7") + "/shootobj")?.TryResolve();
        if (root == null) return null;
        var data = new ShootObjData { skillId = id };
        var info = root.Get("info")?.TryResolve();
        if (info != null)
        {
            data.start = Vec(info.Get("start"));
            var body = Vec(info.Get("bodyWH"));
            if (body.x > 0 && body.y > 0) data.bodyWH = body;
            data.startDelayMs = info.Int("startDelay");
            data.infoSpeed = Num(info.Get("v"));
            data.maxMobCount = info.Int("maxMobCount", 1);
            data.totalHitCount = info.Int("totalHitCount", 1);
            data.deleteAlphaDelayMs = info.Int("deleteAlphaDelay");
            data.deleteAlpha = info.Int("deleteAlpha");
            data.pickDir3Way = info.Int("pickDir3Way") != 0;
            data.pickDir5Way = info.Int("pickDir5Way") != 0;
            data.multiStartPos = info.Int("multiStartPos") != 0;
            data.groupCount = Mathf.Max(1, info.Int("groupbuild/count", 1));
            // 原端两个技能的键大小写不同（startInterval / startinterval），都认。
            data.groupIntervalMs = info.Int("groupbuild/startInterval", info.Int("groupbuild/startinterval"));
            data.groupVMin = info.Int("groupbuild/vmin");
            data.groupVMax = info.Int("groupbuild/vmax");
            data.randomDelayMinMs = info.Int("randomCreateDelayMin");
            data.randomDelayMaxMs = info.Int("randomCreateDelayMax");
            data.sprayType = info.Text("sprayType");
            // 命中预算族：这些字段 282 才用，缺省值必须保留「一怪一次」的老行为。
            data.hitLimitEachMob = info.Int("hitLimitEachMob");
            data.hitLimitEveryMob = info.Int("hitLimitEveryMob");
            data.multiAttackCount = Mathf.Max(1, info.Int("multiAttackCount", 1));
            data.multiAttackHitDelayMs = info.Int("multiAttackHitDelay");
            data.noHitWhenMaxCount = info.Int("noHitWhenMaxCount") != 0;
            data.useUpWhenMaxMobCount = info.Int("useUpWhenMaxMobCount") != 0;
            data.rotateAttack = info.Int("rotateAttack") != 0;
            data.flipRotate = Num(info.Get("rorateWhenFlip"));   // 原端拼错，键名照抄
            data.startAlphaDelayMs = info.Int("startAlphaDelay");
            data.startOffset = info.Int("startOffset") != 0;
            data.triggerSpecialCore = info.Int("triggerSpecialCore") != 0;
            // WZ 里没有 chase 键：追踪是 sprayType 带来的行为差异（EXE 的 Set/GetChaseTarget）。
            // 全库只有 AngelRay（2341000 天使之箭VI）是追踪箭。
            data.chaseTarget = data.sprayType == "AngelRay";
            var angleList = info.Get("angleList");
            if (angleList != null)
                foreach (var a in angleList.Children)
                {
                    var node = a.TryResolve();
                    if (node == null) continue;
                    data.angles.Add(new Vector2(node.Int("min"), node.Int("max")));
                }
        }
        var moveList = root.Get("moveList")?.TryResolve();
        if (moveList != null)
            foreach (var pathNode in moveList.Children)
            {
                var node = pathNode.TryResolve();
                if (node == null) continue;
                var path = new ShootObjPath { name = pathNode.Name };
                foreach (var pointNode in node.Children)
                {
                    var point = pointNode.TryResolve();
                    if (point == null) continue;
                    var posNode = point.Get("pos")?.TryResolve();
                    path.points.Add(new ShootObjPoint
                    {
                        pos = Vec(posNode),
                        hasPos = posNode != null && posNode.Kind == "Shape2D#Vector2D",
                        v = Num(point.Get("v")),
                        delayMs = point.Int("delay"),
                    });
                }
                if (path.points.Count > 0) data.paths.Add(path);
            }
        var layerList = root.Get("layerList")?.TryResolve();
        if (layerList != null) foreach (var layer in layerList.Children) data.layers.Add(layer.Name);
        // 既没有路径也没有 info 速度 ⇒ 这个 shootobj 没有位移信息，不接管。
        if (data.paths.Count == 0 && data.infoSpeed <= 0) return null;
        return data;
    }
    static Vector2 Vec(ImgNode node)
    {
        var value = node?.TryResolve();
        if (value == null) return Vector2.zero;
        if (value.Kind == "Shape2D#Vector2D") return new Vector2(value.X, value.Y);
        if (value.Value is int number) return new Vector2(number, 0);
        return Vector2.zero;
    }
    static float Num(ImgNode node)
    {
        var value = node?.TryResolve();
        return value?.Value == null ? 0 : Convert.ToSingle(value.Value);
    }
    // 供守卫脚本核对：所有 shootobj 技能的路径条数与速度。
    public static int Count { get { return cache.Count; } }
}
