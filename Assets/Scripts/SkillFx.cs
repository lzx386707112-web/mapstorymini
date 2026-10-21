using System.Collections.Generic;
using UnityEngine;

// 技能特效：把 skills/<key>/cast 那一段原版动画，按原版 delay 播一遍就销毁。
//
// 单例化到这一份（SkillSystem 只负责「何时施法、打谁」，不持有特效逻辑）：
//   - 锚点：WZ 里技能特效的 origin 就是「挂在角色原点（脚底）」的那个点，
//     所以施法时把特效放在角色脚底上方一点（SkillSystem.CastAt 已抬 0.5 世界单位），
//     特效自然从角色身上向四周炸开。
//   - 朝向：Skill.wz 的 effect 美术是按「朝右」画的（爆炎向 +x 扩散、剑气向 +x 炸开），
//     所以向左释放时翻转（flipX = facing<0），「前方」永远和角色朝向一致。
//   - 大小：按「目标世界高度」缩放，不同特效给不同倍率，避免火焰/剑气被压成贴纸。
//   - 跟随：突进这类位移技能可挂 follow，让特效跟着施法者走。
public class SkillFx : MonoBehaviour
{
    Art.Clip clip;
    SpriteRenderer sr;
    float speed = 1f;
    float t;
    int i;

    public static SkillFx Play(string key, Vector3 anchor, int facing, float speed = 1f, int order = 360)
    {
        if (string.IsNullOrEmpty(key) || !Art.Has("skills", key)) return null;
        var clip = Art.GetClip("skills", key, "cast");
        if (clip == null || clip.frames.Length == 0) return null;

        var go = new GameObject("SkillFx_" + key);
        go.transform.position = anchor;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        // 朝向规则必须与角色完全一致：原版素材朝左，朝右（facing>0）时翻转。
        // 之前用 facing<0 与角色方向相反，所以剑士释放时特效总在角色背面。
        sr.flipX = facing > 0;

        var fx = go.AddComponent<SkillFx>();
        fx.clip = clip;
        fx.sr = sr;
        fx.speed = Mathf.Max(0.1f, speed);

        // 原版 1:1：WZ 里特效多少像素，游戏里就显示多大（PPU=40）。
        // 之前按「目标高度」压缩（如 flame 压到 4.6u ≈ 原尺寸 1/5），所以特效小得像贴纸。
        sr.sprite = Art.Frame(clip.frames[0]);
        go.transform.localScale = Vector3.one;

        // 播完就销毁；多留 0.1s 给最后一帧停留
        Object.Destroy(go, clip.Duration / fx.speed + 0.1f);
        return fx;
    }

    // 让特效跟着角色走（突进这类位移技能）：每帧把 x 对齐施法者
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
