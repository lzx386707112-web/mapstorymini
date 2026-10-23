using UnityEngine;

// 极简特效：生成一个贴图，0.1~0.25 秒后销毁。
//
// 早期版本是纯色方块——背景还是色块时看不出来，换成真实地图之后就非常刺眼，
// 像贴了一张便利贴。这里改成运行时生成的柔光/圆环/月牙贴图（64x64，
// 双线性过滤），成本几乎为零，但不再是硬边矩形。
public static class Fx
{
    public static void Slash(Vector3 pos, Color c, int facing)
    {
        var sr = Quad("FxSlash", pos, FxTex.Arc(), new Vector2(1.9f, 1.7f), c, 400);
        // 月牙的开口朝身后，弧背朝出手方向
        sr.transform.localScale = new Vector3(1.9f * (facing >= 0 ? 1f : -1f), 1.7f, 1f);
        var f = sr.gameObject.AddComponent<FxLife>();
        f.life = 0.13f;
        f.grow = new Vector3(1.45f, 1.25f, 1f);
        f.drift = new Vector3(facing * 1.2f, 0f, 0f);
    }

    public static void Burst(Vector3 pos, float radius, Color c, float life = 0.22f)
    {
        var sr = Quad("FxBurst", pos, FxTex.Glow(), new Vector2(radius * 2.4f, radius * 2.4f), c, 350);
        var f = sr.gameObject.AddComponent<FxLife>();
        f.life = life;
        f.grow = new Vector3(0.15f, 0.15f, 1f);   // 由大缩小，像冲击波收拢
    }

    public static void Ring(Vector3 pos, float radius, Color c, float life = 0.28f)
    {
        var sr = Quad("FxRing", pos, FxTex.Ring(), new Vector2(radius * 2f, radius * 2f), c, 350);
        var f = sr.gameObject.AddComponent<FxLife>();
        f.life = life;
        f.grow = new Vector3(2.2f, 2.2f, 1f);     // 由小扩大，像冲击波扩散
    }

    // 小白点：掉落出现/拾取时的火花（原版掉落物出场与拾取都伴随一个白点）
    public static void Sparkle(Vector3 pos, Color c, float size = 0.4f, float life = 0.18f)
    {
        var sr = Quad("FxSparkle", pos, FxTex.Glow(), new Vector2(size, size), c, 380);
        var f = sr.gameObject.AddComponent<FxLife>();
        f.life = life;
        f.grow = new Vector3(1.7f, 1.7f, 1f);     // 由小放大后淡出
    }

    public static SpriteRenderer Quad(string name, Vector3 pos, Sprite sp, Vector2 size, Color c, int order)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.color = c;
        sr.sortingOrder = order;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        return sr;
    }

    // 受击闪白：临时改颜色再恢复。最廉价、最有效的反馈。
    public static void Flash(SpriteRenderer sr, Color flash)
    {
        if (sr == null) return;
        var f = sr.gameObject.GetComponent<FxFlash>();
        if (f == null) f = sr.gameObject.AddComponent<FxFlash>();
        f.Play(flash, HitFeel.FlashDur);
    }
}

// 运行时生成的特效贴图：白色 + alpha 形状，颜色一律由 SpriteRenderer.color 染。
static class FxTex
{
    const int N = 64;
    static Sprite _glow, _ring, _arc;

    static Sprite Build(System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha((x + 0.5f) / N, (y + 0.5f) / N)));
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        // PPU = N：贴图正好 1 个世界单位见方，外面用 localScale 定大小
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
    }

    // 柔光：中心最亮，往外二次衰减
    public static Sprite Glow()
    {
        if (_glow != null) return _glow;
        _glow = Build((u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            return Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
        });
        return _glow;
    }

    // 圆环：一圈羽化的边，用来做红圈/冲击波
    public static Sprite Ring()
    {
        if (_ring != null) return _ring;
        _ring = Build((u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.84f) / 0.16f);
            return a * a;
        });
        return _ring;
    }

    // 月牙：大圆减去一个左偏的小圆，剩下的弧就是挥砍
    public static Sprite Arc()
    {
        if (_arc != null) return _arc;
        _arc = Build((u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float d1 = Mathf.Sqrt(dx * dx + dy * dy) / 0.48f;      // 大圆
            float ex = dx + 0.20f;
            float d2 = Mathf.Sqrt(ex * ex + dy * dy) / 0.44f;      // 挖空的小圆
            float a = Mathf.Clamp01((1f - d1) / 0.16f) * Mathf.Clamp01((d2 - 0.90f) / 0.16f);
            return a;
        });
        return _arc;
    }
}

public class FxLife : MonoBehaviour
{
    public float life = 0.2f;
    public Vector3 grow = Vector3.one;
    public Vector3 drift;
    float t;
    Vector3 startScale;

    void Start() => startScale = transform.localScale;

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        transform.localScale = new Vector3(startScale.x * Mathf.Lerp(1f, grow.x, k),
                                           startScale.y * Mathf.Lerp(1f, grow.y, k), 1f);
        transform.position += drift * Time.deltaTime;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            var c = sr.color;
            c.a = 1f - k;
            sr.color = c;
        }
        if (t >= life) Destroy(gameObject);
    }
}

public class FxFlash : MonoBehaviour
{
    SpriteRenderer sr;
    Color baseColor;
    Color flashColor;
    float dur;
    float t = -1f;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
    }

    public void Play(Color c, float d)
    {
        if (sr == null) return;
        if (t < 0f) baseColor = sr.color;
        flashColor = c;
        dur = Mathf.Max(0.01f, d);
        t = 0f;
    }

    void Update()
    {
        if (t < 0f) return;
        t += Time.deltaTime;
        if (t >= dur)
        {
            sr.color = baseColor;
            t = -1f;
        }
        else
        {
            sr.color = Color.Lerp(flashColor, baseColor, t / dur);
        }
    }
}
