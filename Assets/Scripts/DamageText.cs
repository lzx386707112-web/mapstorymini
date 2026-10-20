using System.Collections.Generic;
using UnityEngine;

// 原版伤害数字。
//
// 素材来自 Effect.wz/BasicEff.img 的 NoRed / NoCri / NoBlue / NoViolet：
// 每个数字是**两层**——edge（带黑描边的深色外层）在下，base（白黄橙高光）在上，
// 按各自 origin 叠在同一锚点上。只画一层会得到扁平的纯色字，一眼就假。
//
// 原版的动态，四个特征缺一不可：
//   1. 逐位出场：相邻数字差 ~28ms，像被「扫」出来，不是整块同时出现；
//   2. 弹入：出现瞬间放大约 1.55 倍，110ms 内收回到 1，是「啪」的手感来源；
//   3. 上抛后减速：数字整体往上飞，越飞越慢，末尾几乎悬停；
//   4. 两侧外散：多位数整体以受击点为中心，左右两端的字向外拉开一点，中间几乎不动。
//
// 时间用 Time.deltaTime（受 timeScale 影响）：HitFeel 的顿帧会把数字一起冻住，
// 60ms 的定格反而让数字更「实」，和原版一致。
public class DamageText : MonoBehaviour
{
    const float Life     = 0.95f;   // 总时长
    const float FadeFrom = 0.62f;   // 从这里开始淡出
    const float PopTime  = 0.11f;   // 弹入时长
    const float RiseV    = 3.15f;   // 上抛初速（世界单位/秒，1 单位 = 40px → 126px/s）
    const float Gravity  = 3.05f;   // 上抛重力，峰值约 0.81 单位 ≈ 32px
    const int   Order    = 900;     // 数字永远在最上层：前景层是 600，别被树/柱子挡住

    class Glyph
    {
        public Transform tr;
        public SpriteRenderer edge, body;
        public float delay;
    }

    static Transform root;
    static float lastSpawn;

    readonly List<Glyph> glyphs = new List<Glyph>();
    SpriteRenderer burst;
    Vector2 burstCenter;     // 星图中心相对 pivot 的像素偏移（用于摆位）
    Vector3 anchor;
    float t, scale = 1f, drift;

    // ————————————— 对外入口 —————————————
    //
    // skin: "red" 打怪 / "cri" 暴击 / "blue" 玩家受伤 / "violet" 持续伤害
    public static void Show(Vector3 worldPos, float amount, string skin = "red", float scale = 1f)
    {
        var s = Art.Damage(skin);
        if (s == null) return;
        int v = Mathf.Max(0, Mathf.RoundToInt(amount));
        Spawn(worldPos, v.ToString(), s, scale);
    }

    public static void Miss(Vector3 worldPos, float scale = 1f, string skin = "red")
    {
        var s = Art.Damage(skin);
        if (s == null || s.miss == null) return;
        Spawn(worldPos, "Miss", s, scale);
    }

    static void Spawn(Vector3 pos, string text, Art.DmgSkin skin, float scale)
    {
        if (root == null)
        {
            var r = GameObject.Find("DamageTextRoot");
            if (r == null) r = new GameObject("DamageTextRoot");
            root = r.transform;
        }

        // 同一时刻的多段伤害不要完全重合：错开一点横向位置和高度
        float now = Time.time;
        int stack = now - lastSpawn < 0.14f ? 1 : 0;
        lastSpawn = now;

        var go = new GameObject("Dmg_" + text);
        go.transform.SetParent(root, false);
        go.transform.position = pos + new Vector3(Random.Range(-0.12f, 0.12f) + stack * Random.Range(0.18f, 0.34f),
                                                  stack * 0.14f, 0f);

        var dt = go.AddComponent<DamageText>();
        dt.Build(text, skin, scale);
    }

    void Build(string text, Art.DmgSkin skin, float scale)
    {
        this.scale = scale;
        anchor = transform.position;
        drift = Random.value < 0.5f ? -1f : 1f;

        // Miss 不是数字，是整张字牌
        if (text == "Miss" && skin.miss != null)
        {
            var g = new Glyph { tr = transform, delay = 0f };
            g.body = MakeRenderer(transform, skin.miss, Order + 1);
            glyphs.Add(g);
            return;
        }

        // —— 排布：先量一遍总宽，再整体居中 ——
        // 数字画布是紧贴字形裁的，origin.x 基本在 w/2，所以按 w*0.88 递进就是原版的紧排。
        var widths = new float[text.Length];
        var refs = new Art.DmgGlyph[text.Length];
        float total = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            var g = GlyphFor(text, i, skin);
            refs[i] = g;
            float w = g != null ? g.size.x : 20f;
            widths[i] = w;
            total += w * 0.88f;
        }

        float cursor = -total * 0.5f;
        for (int i = 0; i < text.Length; i++)
        {
            var g = refs[i];
            if (g == null) { cursor += widths[i] * 0.88f; continue; }

            var holder = new GameObject("g" + i);
            holder.transform.SetParent(transform, false);
            // 以像素中心对齐：pivot 就在 origin，所以 localPosition 直接是锚点位置
            holder.transform.localPosition = new Vector3((cursor + widths[i] * 0.5f) / Art.PPU,
                                                         Random.Range(-1.5f, 1.5f) / Art.PPU, 0f);

            var glyph = new Glyph { tr = holder.transform, delay = i * 0.028f };

            // 外层先画（order 小），内层压上去
            if (skin.edgeDigit != null && skin.edgeDigit.Length > 0)
            {
                var e = PickGlyph(text, i, skin.edgeDigit);
                if (e != null)
                {
                    glyph.edge = MakeRenderer(holder.transform, e, Order);
                    glyph.edge.sortingOrder = Order;
                }
            }
            glyph.body = MakeRenderer(holder.transform, g, Order + 1);

            glyph.tr.localScale = Vector3.zero;
            glyphs.Add(glyph);
            cursor += widths[i] * 0.88f;
        }

        // 暴击星爆：原图 origin(41,70) 是按「数字锚点」定的，但它比数字还大一圈，
        // 压到数字上只会从黑描边里露出一条白边，反而像字糊了。所以整体抬到数字头顶，
        // 当成一个「暴击标记」，随数字一起上飘。
        if (skin.fx != null)
        {
            burst = MakeRenderer(transform, skin.fx, Order - 1);
            burst.color = new Color(1f, 1f, 1f, 0f);
            burstCenter = new Vector2(skin.fx.size.x * 0.5f - skin.fx.origin.x,
                                      skin.fx.origin.y - skin.fx.size.y * 0.5f);
        }
    }

    static Art.DmgGlyph GlyphFor(string text, int i, Art.DmgSkin skin)
    {
        char c = text[i];
        if (c >= '0' && c <= '9') return skin.baseDigit[c - '0'];
        return null;   // Miss 之类整图另走 Miss()
    }

    static Art.DmgGlyph PickGlyph(string text, int i, Art.DmgGlyph[] arr)
    {
        char c = text[i];
        if (c < '0' || c > '9') return null;
        int k = c - '0';
        return k < arr.Length ? arr[k] : null;
    }

    static SpriteRenderer MakeRenderer(Transform parent, Art.DmgGlyph g, int order)
    {
        var go = new GameObject("sp");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Art.FrameAt(g.file, g.origin);
        sr.sortingOrder = order;
        return sr;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= Life)
        {
            Destroy(gameObject);
            return;
        }

        // 上抛 + 两侧外散：中间的字几乎不动，两端的字往外拉开
        float rise = RiseV * t - 0.5f * Gravity * t * t;
        transform.position = anchor + new Vector3(drift * 0.30f * t * t, rise, 0f);

        float alpha = t <= FadeFrom ? 1f : 1f - (t - FadeFrom) / (Life - FadeFrom);

        for (int i = 0; i < glyphs.Count; i++)
        {
            var g = glyphs[i];
            float lt = t - g.delay;
            if (lt < 0f)
            {
                if (g.body != null) g.body.enabled = false;
                if (g.edge != null) g.edge.enabled = false;
                continue;
            }

            if (g.body != null) g.body.enabled = true;
            if (g.edge != null) g.edge.enabled = true;

            // 弹入：1.55 → 1，ease-out，110ms
            float k = Mathf.Clamp01(lt / PopTime);
            float pop = 1f + 0.55f * (1f - k) * (1f - k);
            g.tr.localScale = new Vector3(scale * pop, scale * pop, 1f);

            if (g.body != null) SetAlpha(g.body, alpha);
            if (g.edge != null) SetAlpha(g.edge, alpha);
        }

        if (burst != null)
        {
            float k = Mathf.Clamp01(t / 0.34f);
            float s = scale * Mathf.Lerp(0.40f, 1.02f, 1f - (1f - k) * (1f - k));
            // 图中心对准「锚点上方 95px」：pivot = 目标点 - 中心偏移 * s
            float lift = 95f / Art.PPU;
            burst.transform.localPosition = new Vector3(-burstCenter.x * s / Art.PPU,
                                                        lift - burstCenter.y * s / Art.PPU, 0f);
            burst.transform.localScale = new Vector3(s, s, 1f);
            burst.transform.localRotation = Quaternion.Euler(0f, 0f, -20f * k * drift);
            burst.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - k));
        }
    }

    static void SetAlpha(SpriteRenderer sr, float a)
    {
        var c = sr.color;
        c.a = a;
        sr.color = c;
    }
}
