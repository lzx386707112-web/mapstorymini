using UnityEngine;

// 逐帧动画播放器：数据全部来自 WZ 的 delay，不手调帧率。
// 三种状态：base（站立/移动循环）、oneShot（攻击/受击/死亡，播完自动回 base）。
public class SpriteAnim : MonoBehaviour
{
    public string kind = "chars";
    public string key = "knight";
    public float  speed = 1f;

    SpriteRenderer sr;
    Art.Clip cur;
    string curName = "";
    string baseAnim = "stand";
    bool oneShot;
    float t;
    int idx;
    string expression;
    float expressionStarted, lastExpression = -10f;

    public bool Express(int emote)
    {
        // FaceExpressionHandler: basic faces 1..7, five-second timeout; Character throttles at 1.5s.
        string[] names = { "hit", "smile", "troubled", "cry", "angry", "bewildered", "stunned" };
        if (kind != "chars" || emote < 1 || emote > names.Length ||
            Time.unscaledTime - lastExpression < 1.5f) return false;
        expression = names[emote - 1];
        expressionStarted = lastExpression = Time.unscaledTime;
        Apply(idx);
        return true;
    }

    public string Current => curName;
    public bool IsOneShot => oneShot;
    public SpriteRenderer Renderer => sr;

    // 当前clip总时长（秒）：死亡动画播多久、攻击前烧多长，都以 WZ 的 delay 为准
    public float Duration => cur != null ? cur.Duration : 0f;
    // 当前播到第几帧（0 起）：残影要「角色挥到第几帧才出哪张图」，必须能读到
    public int FrameIndex => idx;
    // 距当前帧结束还有多少秒：供外部在精确帧边界上触生效果
    public float FrameRemaining => cur != null ? Mathf.Max(0f, cur.delays[idx] - t) : 0f;

    public void Init(string kind, string key, string idle, SpriteRenderer sr)
    {
        this.kind = kind;
        this.key = key;
        this.sr = sr;
        baseAnim = idle;
        Play(idle, false);
    }

    // 循环动作：站立/走路
    public void SetBase(string anim)
    {
        if (baseAnim == anim) return;
        baseAnim = anim;
        if (!oneShot) Play(anim, false);
    }

    // 一次性动作：播完回 base。
    // force=true 时同一个动作也会从头重播——原版每一下普攻都重新起挥，
    // 如果沿用「同名不重播」，攻速快于动画时长时挥砍会被吞掉，看起来像卡住。
    public void PlayOnce(string anim, float speedMul = 1f, bool force = true)
    {
        if (!Play(anim, true, force)) return;
        this.speed = speedMul;
    }

    public void PlayNativeAttack(string anim, int attackSpeed)
    {
        if (!Play(anim, true, true)) return;
        var delays = new float[cur.delays.Length];
        for (int i = 0; i < delays.Length; i++)
            delays[i] = WzCharacter.AttackFrameDelay(cur.delays[i], attackSpeed);
        // Do not mutate the shared original clip: skills and other actors use it.
        cur = new Art.Clip { frames = cur.frames, delays = delays };
        speed = 1f;
    }

    public bool Play(string anim, bool once, bool force = false)
    {
        var clip = Art.GetClip(kind, key, Art.Resolve(kind, key, anim, baseAnim));
        if (clip == null || clip.frames.Length == 0) return false;
        if (!force && curName == anim && oneShot == once) return true;

        cur = clip;
        curName = anim;
        oneShot = once;
        t = 0f;
        idx = 0;
        if (!once) speed = 1f;
        Apply(idx);
        return true;
    }

    public void SetFacing(int dir)
    {
        if (sr == null) return;
        // WZ 里的角色/怪物原图朝左，向右走时必须翻转
        sr.flipX = dir > 0;
    }

    void Apply(int i)
    {
        if (cur == null || sr == null || i < 0 || i >= cur.frames.Length) return;
        var sp = expression != null && kind == "chars"
            ? WzCharacter.ExpressionFrame(cur.frames[i], expression, Time.unscaledTime - expressionStarted)
            : Art.Frame(cur.frames[i]);
        if (sp != null) sr.sprite = sp;
    }

    void LateUpdate()
    {
        if (kind != "chars" || sr == null || sr.sprite == null) return;
        var camera = Camera.main;
        if (camera == null || !camera.orthographic) return;
        // Gr2D_DX8.dll 0x5040E2FD: native-size, unrotated copies use POINT;
        // scaled copies use LINEAR (0x5040E377/0x5040E387). Mobile viewport
        // scaling must be included even though the character's world scale is 1.
        float pixelsPerUnit = camera.pixelHeight / (2f * camera.orthographicSize);
        Vector3 scale = transform.lossyScale;
        float sx = pixelsPerUnit * Mathf.Abs(scale.x) / sr.sprite.pixelsPerUnit;
        float sy = pixelsPerUnit * Mathf.Abs(scale.y) / sr.sprite.pixelsPerUnit;
        bool nativeSize = Mathf.Abs(sx - 1f) < .0001f && Mathf.Abs(sy - 1f) < .0001f;
        var mode = nativeSize ? FilterMode.Point : FilterMode.Bilinear;
        if (sr.sprite.texture.filterMode != mode) sr.sprite.texture.filterMode = mode;
    }

    void Update()
    {
        if (cur == null || sr == null) return;
        if (expression != null)
        {
            if (Time.unscaledTime - expressionStarted >= 5f) expression = null;
            Apply(idx);
        }
        float dt = Time.deltaTime * Mathf.Max(0f, speed);
        if (dt <= 0f) return;

        t += dt;
        while (t >= cur.delays[idx])
        {
            t -= cur.delays[idx];
            idx++;
            if (idx >= cur.frames.Length)
            {
                if (oneShot)
                {
                    oneShot = false;
                    speed = 1f;
                    Play(baseAnim, false);
                    return;
                }
                idx = 0;
            }
            Apply(idx);
        }
    }
}
