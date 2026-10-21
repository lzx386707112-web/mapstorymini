using UnityEngine;

// 弹道本体：把原版的 ball 部件（箭矢 / 火球 / 龙波 / 落矢）搬成一段位移。
//
// 原版里 ball 不是一个「飞出去的对象」，而是技能节点下的一小段循环帧，
// 客户端按子弹速度把它从施法者推向命中点。这里照做：
//   - 帧：Art.GetClip(skills/<key>, ball) 循环播放，朝向跟施法一致；
//   - 位移：直线移动到 end，速度 = 距离 / 期望时长（由 SkillFxPlan 给）；
//   - 到达后回调，由 SkillSystem 决定命中判定和爆点。
// 箭雨的落矢、烈火箭的溅射也用同一套，只是起终点不同（从上往下砸）。
public class SkillBall : MonoBehaviour
{
    Art.Clip clip;
    SpriteRenderer sr;
    Vector2 from, to;
    float duration;
    float elapsed;
    float frameT;
    int frame;
    System.Action<Vector2> onArrive;

    public static SkillBall Spawn(string key, string anim, Vector2 from, Vector2 to, int facing,
                                  float speed, System.Action<Vector2> onArrive = null)
    {
        var clip = Art.GetClip("skills", key, anim);
        if (clip == null) return null;

        float distance = Vector2.Distance(from, to);
        // 兜底：距离为 0 时给一个最短时长，避免除零后直接瞬移
        float travel = Mathf.Max(0.05f, distance / Mathf.Max(0.5f, speed));

        var go = new GameObject("SkillBall_" + key + "_" + anim);
        go.transform.position = from;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 355;
        sr.flipX = facing > 0;

        var ball = go.AddComponent<SkillBall>();
        ball.clip = clip;
        ball.sr = sr;
        ball.from = from;
        ball.to = to;
        ball.duration = travel;
        ball.onArrive = onArrive;

        var first = Art.Frame(clip.frames[0]);
        if (first == null) { Destroy(go); return null; }
        sr.sprite = first;

        Destroy(go, travel + 0.1f);
        return ball;
    }

    void Update()
    {
        // 循环播放 ball 帧
        if (clip != null && clip.frames.Length > 1)
        {
            frameT += Time.deltaTime;
            while (frameT >= clip.delays[frame])
            {
                frameT -= clip.delays[frame];
                frame = (frame + 1) % clip.frames.Length;
                var sp = Art.Frame(clip.frames[frame]);
                if (sp != null) sr.sprite = sp;
            }
        }

        elapsed += Time.deltaTime;
        float k = Mathf.Clamp01(elapsed / duration);
        transform.position = Vector2.Lerp(from, to, k);
        if (k < 1f) return;

        var cb = onArrive;
        onArrive = null;
        if (cb != null) cb(to);
        Destroy(gameObject);
    }
}
