using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// 武器残影（原版 Afterimage.wz）。
//
// 原版客户端在挥砍的**特定帧**把 Character/Afterimage/<武器 info/afterImage>.img
// 里对应动作的画布以半透明叠在角色身上，存续时间用该画布自己的 delay，到点淡出。
// 数据结构：<tier 0>/<动作名>/<帧号>/<画布号>，一帧可以带多张画布（各有 origin/delay）。
//
// 摆位：残影画布的 origin 与角色身体画布的 origin 同一个坐标系——
// 都以「脚底锚点」为原点（身体 canvas origin 即脚底，map/navel 在它上方 21px 上下，
// 用 swingO1 第 2 帧残影的 lt/rb 反推验证过：刀光正好落在身侧胸高度）。
// 所以残影直接挂在角色可视化节点下、localPosition=0，origin 就落在脚底锚点上，
// 朝向用 flipX（原图朝左，向右时翻转）。
public static class AfterimageFx
{
    public struct Canvas2
    {
        public ImgNode node;
        public float delay;      // 秒
    }
    public struct Frame
    {
        public int index;        // 角色挥砍动画的帧号
        public Canvas2[] canvases;
    }

    static readonly Dictionary<string, Frame[]> cache = new Dictionary<string, Frame[]>();

    // 某武器残影在某动作下的帧表；没有数据返回 null。
    // 少数武器的残影键名和姿势名不一致（火器的射击残影叫 shot，指虎只有突刺），
    // 按别名链回退查找。
    public static Frame[] Frames(string afterName, string action)
    {
        if (string.IsNullOrEmpty(afterName) || string.IsNullOrEmpty(action)) return null;
        string key = afterName + "/" + action;
        if (cache.TryGetValue(key, out var frames)) return frames;
        foreach (var alias in Aliases(action))
        {
            frames = Load(afterName, alias);
            if (frames != null) break;
        }
        cache[key] = frames;
        return frames;
    }

    static string[] Aliases(string action)
    {
        switch (action)
        {
            case "shoot1": case "shoot2": case "shootF": return new[] { action, "shot" };
            case "swingO1": return new[] { "swingO1", "stabO1" };
            case "stabT1": return new[] { "stabT1", "swingP1" };
            default: return new[] { action };
        }
    }

    // CUserLocal::TryDoingMeleeAttack/414440: use the action's original lt/rb,
    // not the visible trail's alpha bounds or an invented attack radius.
    public static Rect? AttackBounds(string afterName, string action)
    {
        if (string.IsNullOrEmpty(afterName) || string.IsNullOrEmpty(action)) return null;
        string path="Character/Afterimage/"+afterName+".img";
        if (!WzAssets.HasFile(path)) return null;
        foreach (var alias in Aliases(action))
        {
            var node=WzAssets.Node(path+"/0/"+alias)?.TryResolve();
            var lt=node?.Get("lt");var rb=node?.Get("rb");
            if (lt==null || rb==null) continue;
            return Rect.MinMaxRect(lt.X,lt.Y,rb.X,rb.Y);
        }
        return null;
    }

    static Frame[] Load(string afterName, string action)
    {
        string path = "Character/Afterimage/" + afterName + ".img";
        if (!WzAssets.HasFile(path)) return null;
        var node = WzAssets.Node(path + "/0/" + action);      // tier 0 = 无残影强化档
        if (node == null) return null;
        var raw = WzAssets.Frames(node);                      // <帧号> 子属性
        if (raw.Count == 0) return null;
        var list = new List<Frame>();
        foreach (var f in raw)
        {
            int index;
            if (!int.TryParse(f.Name, out index)) continue;
            var canvases = new List<Canvas2>();
            foreach (var c in f.Children)
            {
                int n;
                if (!int.TryParse(c.Name, out n)) continue;   // 跳过 lt/rb 之类的元数据
                var canvas = c.Resolve();
                if (canvas == null || canvas.Kind != "Canvas") continue;
                canvases.Add(new Canvas2 { node = c, delay = Mathf.Max(0.03f, c.Int("delay", 100) / 1000f) });
            }
            if (canvases.Count > 0)
                list.Add(new Frame { index = index, canvases = canvases.ToArray() });
        }
        return list.Count > 0 ? list.ToArray() : null;
    }

    // 生成一帧残影的全部画布。playback 与角色挥砍的播放速度一致，
    // 这样残影存活时间和挥砍节奏同步（原版就是同一套 delay）。
    public static void Spawn(string afterName, string action, Frame frame, Transform anchor, int facing, float playback)
    {
        if (anchor == null) return;
        foreach (var c in frame.canvases)
        {
            var sprite = WzAssets.Sprite(c.node, "ai:" + afterName + "/0/" + action + "/" + frame.index + "/" + c.node.Name);
            if (sprite == null) continue;
            var go = new GameObject("Afterimage");
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = Vector3.zero;        // origin 落在脚底锚点
            go.transform.localRotation = Quaternion.identity;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.flipX = facing > 0;                            // 原图朝左
            sr.sortingOrder = Mathf.Max(0, anchor.GetComponent<SpriteRenderer>() != null
                ? anchor.GetComponent<SpriteRenderer>().sortingOrder - 1 : 299);
            var ghost = go.AddComponent<AfterimageGhost>();
            ghost.life = c.delay / Mathf.Max(0.1f, playback);
        }
    }
}

// 单张残影：半透明起，按 life 线性淡出后自毁。
// 用 Time.deltaTime（受 timeScale 影响）：HitFeel 顿帧会把残影一起冻住，和角色一致。
public sealed class AfterimageGhost : MonoBehaviour
{
    public float life = 0.2f;
    public float startAlpha = 0.5f;
    float t;
    SpriteRenderer sr;

    void Awake() => sr = GetComponent<SpriteRenderer>();

    void Update()
    {
        t += Time.deltaTime;
        float k = life <= 0f ? 1f : Mathf.Clamp01(t / life);
        if (sr != null)
        {
            var c = sr.color;
            c.a = startAlpha * (1f - k);
            sr.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }

    // 切图/清场：残留残影不能留到下一关
    void OnDisable()
    {
        if (sr != null)
        {
            var c = sr.color;
            c.a = 0f;
            sr.color = c;
        }
    }
}
