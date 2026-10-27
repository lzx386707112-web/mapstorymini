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

    // 表情名表：BeiDou.exe 0xBEC284 的 23 项静态数组（初始化 0x45E8E8..0x45EDFA，
    // 每项由字符串池 GetString 填充）。下标就是表情编号，和 Face.img 的节点同名。
    //   1 hit / 2 smile / 3 troubled / 4 cry / 5 angry / 6 bewildered / 7 stunned
    //   8 vomit / 9 oops / 10 cheers / 11 chu / 12 wink / 13 pain / 14 glitter
    //   15 blaze / 16 shine / 17 love / 18 despair / 19 hum / 20 bowing / 21 hot / 22 dam
    // 注意 14..18 的顺序和 Face.img 节点罗列顺序不同，必须以这张表为准。
    // 0 号是 blink，客户端不会把它当表情用（SetEmotion 只从 1 起生效）。
    public static readonly string[] EmoteNames =
    {
        "blink", "hit", "smile", "troubled", "cry", "angry", "bewildered", "stunned",
        "vomit", "oops", "cheers", "chu", "wink", "pain", "glitter", "blaze",
        "shine", "love", "despair", "hum", "bowing", "hot", "dam",
    };
    public const int MaxEmote = 22;
    // 0xA244B5：GetTickCount() - lastEmotion < 0x7D0 直接丢弃 = 客户端 2 秒节流。
    public const float EmoteCooldown = 2f;
    // 表情到点回默认脸（服务端 FaceExpressionHandler 注释：5 秒超时）。
    public const float EmoteDuration = 5f;

    public bool Express(int emote)
    {
        if (kind != "chars" || emote < 1 || emote > MaxEmote) return false;
        if (Time.unscaledTime - lastExpression < EmoteCooldown) return false;
        expression = EmoteNames[emote];
        expressionStarted = lastExpression = Time.unscaledTime;
        Apply(idx);
        return true;
    }

    // 0 号表情 = 回默认脸（SetEmotion(0) 的有效路径）。
    public void ClearExpression()
    {
        if (expression == null) return;
        expression = null;
        Apply(idx);
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

    // 假人专用节流：换帧时要现合成纸娃娃（部件混合 + 贴图上传，单个 1~3ms），
    // 几十个假人同时换帧就会把一帧撑长。打开后由 BotWorkPump 的帧预算决定这一帧要不要合成，
    // 预算不够就沿用旧贴图（时间轴照走，动画不会变慢）。
    public bool throttleWhenBusy;

    void Update()
    {
        if (cur == null || sr == null) return;
        if (expression != null)
        {
            if (Time.unscaledTime - expressionStarted >= EmoteDuration) expression = null;
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
            if (throttleWhenBusy && !Server.Bot.BotWorkPump.Has(0.4f)) continue;   // 本帧预算用完：先不合成
            Apply(idx);
        }
    }
}
