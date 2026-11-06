using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 282 技能的震屏（`Skill/<img>.img/skill/<id>/trembleEffect`）。
//
// 商业端参数：`force`（原端点阵振幅）/ `time`（持续毫秒）/ `delay`（起播延迟毫秒）。
// 以前这 56 条完全没接 —— 技能放出来只有特效和伤害，镜头一动不动，
// 「重击」全靠闪白撑，手感是全套技能里最容易被一眼看出假的一块。
//
// 换算口径照抄怪物震屏那条已经验证过的路（NativeMobController.TrembleAmplitude）：
// 原端 API 的振幅单位就是地图点阵像素，项目里一律 `/ Art.PPU` 换成世界单位。
//   force=7  → 0.07 世界单位（7 像素）
//   force=70 → 0.70 世界单位（道米尼奧 VI 那种全屏大招）
// 不再另设夹值：夹了就跟源端不成比例，而 `Art.PPU` 本身就是唯一的换算系数。
//
// `HitFeel.Shake` 内部已经处理了「系统设置 → 画面效果」总开关，这里不重复判断。
public partial class SkillSystem
{
    // 一段震屏。源端有两种写法：单段 `trembleEffect`、多段 `trembleEffectList/<n>`
    // （每段自带 delay/force/time）。多段是「连续重击」的节奏，必须逐段起震，
    // 只播第一段会让暴风系列、连续打击类技能少掉后半段的抖动。
    sealed class TrembleWave
    {
        public float Force;
        public float Seconds;
        public float Delay;
    }

    sealed class TrembleSpec
    {
        public readonly List<TrembleWave> Waves = new List<TrembleWave>();
    }

    static Dictionary<int, TrembleSpec> trembles;
    static bool tremblesLoaded;

    static Dictionary<int, TrembleSpec> Trembles
    {
        get
        {
            if (tremblesLoaded) return trembles;
            tremblesLoaded = true;
            var asset = Resources.Load<TextAsset>("NativeSkillTremble");
            if (asset == null)
            {
                Debug.LogWarning("[Tremble] 缺 Assets/Resources/NativeSkillTremble.json，技能震屏不会播");
                return trembles = new Dictionary<int, TrembleSpec>();
            }
            if (!(MiniJson.Parse(asset.text) is Dictionary<string, object> root) ||
                !root.TryGetValue("skills", out var raw) ||
                !(raw is Dictionary<string, object> skills))
                return trembles = new Dictionary<int, TrembleSpec>();

            trembles = new Dictionary<int, TrembleSpec>();
            foreach (var pair in skills)
            {
                // 技能 id 会超过 2^28，float 步长 32 会把 400011072 舍成 400011072 附近的整十数，
                // 一律走 long 再窄化（IRON-RULES §7.1）。
                if (!long.TryParse(pair.Key, out long id) || id <= 0 || id > int.MaxValue) continue;
                if (!(pair.Value is Dictionary<string, object> row)) continue;
                if (!row.TryGetValue("waves", out var rawWaves) || !(rawWaves is List<object> waves)) continue;
                var spec = new TrembleSpec();
                foreach (var item in waves)
                {
                    if (!(item is Dictionary<string, object> wave)) continue;
                    float force = Art.Num(wave, "force");
                    float time = Mathf.Max(1f, Art.Num(wave, "time"));
                    if (force <= 0f) continue;
                    spec.Waves.Add(new TrembleWave
                    {
                        Force = force,
                        Seconds = time / 1000f,
                        Delay = Mathf.Max(0f, Art.Num(wave, "delay") / 1000f),
                    });
                }
                if (spec.Waves.Count > 0) trembles[(int)id] = spec;
            }
            if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
                Debug.Log("[Tremble] 载入 " + trembles.Count + " 条技能震屏");
            return trembles;
        }
    }

    /// <summary>施放时按源端 `delay` 逐段起震；没有 trembleEffect 的技能什么都不做。</summary>
    void PlayTremble(SkillDef s)
    {
        if (s?.native == null) return;
        if (!Trembles.TryGetValue(s.native.Id, out var spec)) return;
        // 每一段各起一条协程：同一技能的多段是「delay 递减 / force 递增」的节奏，
        // 合到一个协程里串行会让后面的段全推到最后。
        foreach (var wave in spec.Waves) StartCoroutine(TrembleAfter(wave));
    }

    IEnumerator TrembleAfter(TrembleWave wave)
    {
        if (wave.Delay > 0f) yield return new WaitForSeconds(wave.Delay);
        // 换图 / 死亡之后不要补震：那一下会震在下一张图的空镜头上。
        if (p == null || p.hp <= 0 || GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Playing) yield break;
        HitFeel.Shake(wave.Force / Art.PPU, wave.Seconds);
        if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[Tremble] force=" + wave.Force + " time=" + (wave.Seconds * 1000f) +
                      "ms delay=" + (wave.Delay * 1000f) + "ms amp=" +
                      (wave.Force / Art.PPU).ToString("0.000"));
    }

    /// <summary>探针用：该技能第一段震屏的参数（没有则 false）。</summary>
    public static bool TrembleSpecOf(int skillId, out float force, out float seconds, out float delay)
    {
        force = seconds = delay = 0f;
        if (!Trembles.TryGetValue(skillId, out var spec) || spec.Waves.Count == 0) return false;
        var wave = spec.Waves[0];
        force = wave.Force; seconds = wave.Seconds; delay = wave.Delay;
        return true;
    }

    /// <summary>探针/守卫用：该技能有几段震屏。</summary>
    public static int TrembleWaveCount(int skillId)
        => Trembles.TryGetValue(skillId, out var spec) ? spec.Waves.Count : 0;

    public static int TrembleCount => Trembles.Count;
}
