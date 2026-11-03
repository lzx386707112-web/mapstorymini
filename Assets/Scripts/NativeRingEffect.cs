using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// ————————————————————————————————————————————————————————————
// Character/Ring/<id>.img/info/effect：戒指穿在身上的表现。
//
// 源端同一个字段带两种模式（按字段有无区分，不是两套数据）：
//   fixed / animate / pos        固定在角色身上的循环动画
//                                （灵魂戒指 1118000-、寂寞单身戒指 1112916…，82 枚）
//   follow + 区域 + dx/dy        在角色周围按 interval 发射粒子，delay 是单颗粒子寿命
//     + interval + delay         （音符环绕 1112900、閃星环绕 1112901、极光 1112404…，31 枚）
// 区域用的是 WZ 的屏幕坐标（y 向下为正）：left/top/right/bottom，所以世界 y 要取反。
//
// 素材一律直读 Effect/CharacterEff.img/<path 末段>，和技能特效同一条 NativeImgAnimation
// 通路（保留 origin / 每帧 delay / alpha）。113 枚里 94 枚在数据集内有节点，其余源端也缺。
// ————————————————————————————————————————————————————————————
public sealed class NativeRingEffect : MonoBehaviour
{
    // 四格戒指：普通槽 -12/-13/-15/-16，现金戒指落 -112…，WornSlotOf 负责现金优先。
    static readonly int[] cells = { -12, -13, -15, -16 };

    int current = -1;
    float nextCheck;
    readonly List<GameObject> owned = new List<GameObject>();
    Coroutine emitter;
    ImgNode effect;
    string path;
    PlayerCtrl player;

    void Awake() => player = GetComponent<PlayerCtrl>();

    void Update()
    {
        // 换戒指最多晚 0.2s 生效，没必要每帧扫四格（EquipOfSlot 对现金槽是线性查表）。
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .2f;
        int id = CurrentRing();
        if (id != current) Rebuild(id);
    }

    static int CurrentRing()
    {
        if (!GameSession.InGame) return 0;
        foreach (int cell in cells)
        {
            int id = Profile.EquipOfSlot(EquipSlot.WornSlotOf(cell));
            if (id <= 0) continue;
            if (EquipCatalog.Info(id)?.Get("effect")?.Get("path") != null) return id;
        }
        return 0;
    }

    void Rebuild(int id)
    {
        Clear();
        current = id;
        if (id <= 0) return;
        if (player == null) player = GetComponent<PlayerCtrl>();
        var fx = EquipCatalog.Info(id)?.Get("effect");
        if (fx == null) return;
        path = fx.Text("path");
        if (string.IsNullOrEmpty(path)) return;
        effect = WzAssets.Node(path);
        DebugRing(id, fx);
        if (effect == null || effect.Children.Count == 0) return;
        int order = (player != null ? player.SortingOrder : 300) + (fx.Int("z", 0) < 0 ? -1 : 1);

        // 固定/循环类：每组建一个循环动画，挂在角色身上跟着走。
        if (fx.Get("fixed") != null || fx.Get("animate") != null)
        {
            float lift = fx.Int("pos") == 1 ? 60f / Art.PPU : 0f;
            Vector3 anchor = (player != null ? (Vector3)player.FeetPos : transform.position) + Vector3.up * lift;
            foreach (var group in effect.Children)
            {
                if (!IsNumber(group.Name)) continue;
                var anim = NativeImgAnimation.Spawn(path + "/" + group.Name,
                    player != null ? player.transform : transform, anchor, order, false, true);
                if (anim != null) owned.Add(anim.gameObject);
            }
            return;
        }

        // 粒子类：源端是发射器，这里按 interval 在区域里撒点，粒子自己飘。
        if (fx.Get("follow") != null || fx.Get("interval") != null || fx.Get("genOnMove") != null)
            emitter = StartCoroutine(Emit(fx, order));
    }

    IEnumerator Emit(ImgNode fx, int order)
    {
        float interval = Mathf.Max(.03f, fx.Int("interval", 180) / 1000f);
        float life = Mathf.Clamp(fx.Int("delay", 3000) / 1000f, .1f, 30f);
        int emission = Mathf.Max(1, fx.Int("emission", 1));
        float left = fx.Int("left"), right = fx.Int("right");
        float top = fx.Int("top"), bottom = fx.Int("bottom");
        float dx = fx.Int("dx"), dy = fx.Int("dy");
        bool onlyOnMove = fx.Get("genOnMove") != null;

        var groups = new List<string>();
        foreach (var group in effect.Children) if (IsNumber(group.Name)) groups.Add(group.Name);
        if (groups.Count == 0) yield break;

        int cursor = 0;
        Vector3 last = player != null ? (Vector3)player.FeetPos : transform.position;
        while (true)
        {
            Vector3 feet = player != null ? (Vector3)player.FeetPos : transform.position;
            bool moving = (feet - last).sqrMagnitude > .000001f;
            last = feet;
            if (!onlyOnMove || moving)
            {
                for (int i = 0; i < emission; i++)
                {
                    string group = groups[cursor++ % groups.Count];
                    float px = Random.Range(Mathf.Min(left, right), Mathf.Max(left, right));
                    float py = Random.Range(Mathf.Min(top, bottom), Mathf.Max(top, bottom));
                    // WZ 区域是屏幕坐标（y 向下），世界坐标要取反。
                    Vector3 at = feet + new Vector3(px / Art.PPU, -py / Art.PPU, 0);
                    var anim = NativeImgAnimation.Spawn(path + "/" + group, null, at, order, false, true, life);
                    if (anim == null) continue;
                    var particle = anim.gameObject.AddComponent<NativeRingParticle>();
                    // dy 在数据里是向上飘的像素速度；dx 只取很小一部分做横向抖动，避免粒子飞散。
                    particle.Init(player != null ? player.transform : null,
                        new Vector3(Random.Range(-.35f, .35f) * dx / Art.PPU, Mathf.Abs(dy) / Art.PPU, 0));
                    owned.Add(anim.gameObject);
                }
            }
            yield return new WaitForSeconds(interval);
        }
    }

    static bool IsNumber(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (char c in name) if (c < '0' || c > '9') return false;
        return true;
    }

    // debug_ringfx=1（一次有效）：打印这枚戒指走的是哪条模式、素材在不在。
    static bool debugTried, debugOn;
    void DebugRing(int id, ImgNode fx)
    {
        if (!debugTried)
        {
            debugTried = true;
            debugOn = PlayerPrefs.GetInt("debug_ringfx", 0) != 0;
            if (debugOn) PlayerPrefs.DeleteKey("debug_ringfx");
        }
        if (!debugOn) return;
        int groups = 0;
        if (effect != null) foreach (var g in effect.Children) if (IsNumber(g.Name)) groups++;
        string mode = fx.Get("fixed") != null || fx.Get("animate") != null ? "fixed"
            : fx.Get("interval") != null || fx.Get("follow") != null || fx.Get("genOnMove") != null ? "emitter" : "none";
        Debug.Log("[Ring/Fx] ring=" + id + " path=" + path + " node=" + (effect != null) +
                  " groups=" + groups + " mode=" + mode);
    }

    void Clear()
    {
        if (emitter != null) { StopCoroutine(emitter); emitter = null; }
        foreach (var go in owned)
            if (go != null) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
        owned.Clear();
    }

    void OnDisable() { Clear(); current = -1; }
    void OnDestroy() { Clear(); }
}

// 粒子：跟着角色位移同步挪，再叠自身速度，寿命交给 NativeImgAnimation 的 lifetime。
public sealed class NativeRingParticle : MonoBehaviour
{
    Transform follow;
    Vector3 last, velocity;

    public void Init(Transform target, Vector3 speed)
    {
        follow = target;
        velocity = speed;
        last = target != null ? target.position : transform.position;
    }

    void Update()
    {
        if (follow != null)
        {
            transform.position += follow.position - last;
            last = follow.position;
        }
        transform.position += velocity * Time.deltaTime;
    }
}
