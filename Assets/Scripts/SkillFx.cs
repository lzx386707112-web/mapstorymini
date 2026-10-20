using UnityEngine;

// 技能特效：把 Skill.wz 的 `effect` 帧按原版 delay 播一遍就销毁。
//
// 三条约定（和角色贴图一致，不能改）：
//   1. 帧已经共用同一个锚点（tools/wz_skill.py 里按 max(origin) 对齐），
//      所以 transform.position 就是「技能挂在角色身上的那个点」，
//      不再需要每帧补偏移。
//   2. 原图朝右（见下方 sr.flipX 的说明），向左释放时翻转。
//      翻转是绕轴心翻的，锚点不动，位置天然正确。
//   3. 锚点是角色的「身体中心」而不是脚底：我们角色贴图轴心在脚底，
//      所以要往上抬 21px（0.525 世界单位），否则特效会整体沉到地里。
public class SkillFx : MonoBehaviour
{
    public const float AnchorUp = 21f / 40f;   // WZ 角色 origin 离脚底的距离

    Art.Clip clip;
    SpriteRenderer sr;
    float speed = 1f;
    float t;
    int i;

    public static SkillFx Play(string key, Vector3 anchor, int facing, float speed = 1f, int order = 350)
    {
        if (string.IsNullOrEmpty(key) || !Art.Has("skills", key)) return null;
        var clip = Art.GetClip("skills", key, "cast");
        if (clip == null || clip.frames.Length == 0) return null;

        var go = new GameObject("SkillFx_" + key);
        go.transform.position = anchor;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.sprite = Art.Frame(clip.frames[0]);
        // 注意：技能特效的朝向和角色**相反**。
        // 角色原图朝左（向右走才 flipX=true），但 Skill.wz 的 effect 美术是按朝右画的：
        //   斗气死亡断层的爆炎向 +x 扩散、烈焰翔斩向 +x 炸开、突进的光尾在 +x 侧。
        // 所以向左释放时要翻转，向右释放时不翻，这样「前方」永远和角色朝向一致。
        sr.flipX = facing < 0;

        var fx = go.AddComponent<SkillFx>();
        fx.clip = clip;
        fx.sr = sr;
        fx.speed = Mathf.Max(0.1f, speed);

        // 播完就销毁；多留 0.1s 是给最后一帧的停留
        Object.Destroy(go, clip.Duration / fx.speed + 0.1f);
        return fx;
    }

    // 让特效跟着角色走（突进这种位移技能）：每帧把 x 对齐施法者
    public Transform follow;
    public float followX;
    public bool  followMove;

    void Update()
    {
        if (clip == null) return;

        if (followMove && follow != null)
        {
            var p = transform.position;
            p.x = follow.position.x + followX;
            transform.position = p;
        }

        t += Time.deltaTime * speed;
        while (i < clip.frames.Length - 1 && t >= clip.delays[i])
        {
            t -= clip.delays[i];
            i++;
            var sp = Art.Frame(clip.frames[i]);
            if (sp != null) sr.sprite = sp;
        }
    }
}
