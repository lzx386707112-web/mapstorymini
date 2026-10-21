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

    public string Current => curName;
    public bool IsOneShot => oneShot;
    public SpriteRenderer Renderer => sr;

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

    // 一次性动作：播完回 base
    public void PlayOnce(string anim, float speedMul = 1f)
    {
        if (!Play(anim, true)) return;
        this.speed = speedMul;
    }

    public bool Play(string anim, bool once)
    {
        var clip = Art.GetClip(kind, key, Art.Resolve(kind, key, anim, baseAnim));
        if (clip == null || clip.frames.Length == 0) return false;
        if (curName == anim && oneShot == once) return true;

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
        var sp = Art.Frame(cur.frames[i]);
        if (sp != null) sr.sprite = sp;
    }

    void Update()
    {
        if (cur == null || sr == null) return;
        float dt = Time.deltaTime * Mathf.Max(0.05f, speed);
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
