using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 282「可叠层主动 buff」：400001024 普力特的祝福。
//
// h 把 6 层效果逐层写死（每叠一层获得前面所有效果 + 新的一层），数值全在 common，
// 公式里的 x 是等级：w=10（CD 缩短%）、z=10+d(x/2)（异常耐性）、q=25+x（全属性）、
// u=10+d(x/2)（攻/魔）、v=10+d(x/2)（Boss 伤害%）、第 6 层无敌；x=6 层、s=30 秒、
// cooltime=25 秒，若持续时间结束或叠满则冷却 y=240 秒。
//
// 全部数值来自 Assets/Resources/NativeSkillStacks.json
// （tools/ms282/export_skill_stacks.py 按 1..maxLevel 展开），本文件不写死常量。
// stat 键对应项目现有消费点，CooldownReduce 不是 buff stat，由 EffectiveCooldown 单独读。
public partial class SkillSystem
{
    static Dictionary<string, object> stackData;
    static Dictionary<int, Dictionary<string, object>> stackSpecs;
    int stackCount, stackLevel, stackCooldownReduce, stackExhaustCooldown;
    double stackUntil;
    long stackGeneration;
    bool stackExhaust;

    static void EnsureStacks()
    {
        if (stackData != null) return;
        var text = Resources.Load<TextAsset>("NativeSkillStacks")?.text;
        stackData = string.IsNullOrEmpty(text)
            ? new Dictionary<string, object>()
            : (Dictionary<string, object>)MiniJson.Parse(text);
        stackSpecs = new Dictionary<int, Dictionary<string, object>>();
        if (!stackData.TryGetValue("stacks", out var raw) || !(raw is Dictionary<string, object> table)) return;
        foreach (var pair in table)
            if (int.TryParse(pair.Key, out int id) && pair.Value is Dictionary<string, object> spec)
                stackSpecs[id] = spec;
    }

    public static bool IsStackSkill(int id)
    {
        EnsureStacks();
        return stackSpecs.ContainsKey(id);
    }

    static Dictionary<string, object> StackRow(int id, int level)
    {
        EnsureStacks();
        if (!stackSpecs.TryGetValue(id, out var spec)) return null;
        var levels = spec.TryGetValue("levels", out var lv) ? lv as Dictionary<string, object> : null;
        if (levels == null || !levels.TryGetValue(Mathf.Clamp(level, 1, 30).ToString(), out var row)) return null;
        return row as Dictionary<string, object>;
    }

    static int StackInt(Dictionary<string, object> row, string key, int fallback = 0)
        => row != null && row.TryGetValue(key, out var raw) ? Convert.ToInt32(raw) : fallback;

    /// <summary>本次施放的冷却覆盖：叠满或耗尽时走 h 的 y 秒，否则用源端 cooltime。</summary>
    public int StackCooldown(int id, float defaultCd)
        => IsStackSkill(id) && stackExhaust && stackExhaustCooldown > 0
            ? stackExhaustCooldown : (int)defaultCd;

    /// <summary>1 层效果：其余技能冷却缩短 w%（对所有技能，不只自己）。</summary>
    public int CooldownReducePercent => stackCount > 0 && Time.realtimeSinceStartupAsDouble < stackUntil
        ? stackCooldownReduce : 0;

    float EffectiveCooldown(SkillDef s)
    {
        float cd = StackCooldown(s.native.Id, s.cd);
        int reduce = CooldownReducePercent;
        return reduce > 0 ? cd * (100 - reduce) / 100f : cd;
    }

    /// <summary>施放成功：叠一层、刷新 30 秒、重写当前生效的 stat 集合。</summary>
    public void ApplyStack(SkillDef cast)
    {
        if (cast == null || cast.native == null || p == null || !IsStackSkill(cast.native.Id)) return;
        var row = StackRow(cast.native.Id, cast.native.Level);
        if (row == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        int maxStacks = Mathf.Max(1, StackInt(row, "maxStacks", 6));
        int duration = StackInt(row, "durationSec", 30);
        stackExhaustCooldown = StackInt(row, "exhaustCooldownSec", 240);
        if (now > stackUntil || stackLevel == 0) stackCount = 1;
        else stackCount = Mathf.Min(maxStacks, stackCount + 1);
        stackLevel = cast.native.Level;
        stackUntil = now + duration;
        stackExhaust = stackCount >= maxStacks;
        stackGeneration++;

        var effects = row.TryGetValue("effects", out var raw) ? raw as List<object> : null;
        var stats = new Dictionary<string, int>();
        stackCooldownReduce = 0;
        if (effects != null)
            foreach (var item in effects)
            {
                if (!(item is Dictionary<string, object> effect)) continue;
                if (StackInt(effect, "layer") > stackCount) continue;
                int value = StackInt(effect, "value");
                if (!effect.TryGetValue("stats", out var keysRaw) || !(keysRaw is List<object> keys)) continue;
                foreach (var keyObj in keys)
                {
                    string key = Convert.ToString(keyObj);
                    if (key == "CooldownReduce") { stackCooldownReduce = value; continue; }
                    stats[key] = value;
                }
            }
        // 即使只有 1 层（只有 CooldownReduce）也要注册，否则叠层状态没有 buff 图标/到期依据。
        stats["STACK_BLESSING"] = stackCount;
        NativeBuffs.For(p).ApplyStack(cast.native.Id, cast.native.Level, stats, duration * 1000, now);
        long generation = stackGeneration;
        StartCoroutine(StackExpiry(cast.native.Id, generation));
        if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[Stack] " + cast.native.Id + " layer=" + stackCount + "/" + maxStacks +
                      " cdReduce=" + stackCooldownReduce + " stats=" + string.Join(",", stats.Keys));
    }

    IEnumerator StackExpiry(int id, long generation)
    {
        while (Time.realtimeSinceStartupAsDouble < stackUntil)
        {
            if (stackGeneration != generation) yield break;
            yield return null;
        }
        if (stackGeneration != generation || p == null) yield break;
        // h「若未刷新疊加而持續時間結束…則冷卻時間為 #y 秒」：到期清层并锁长冷却。
        stackCount = 0; stackLevel = 0; stackCooldownReduce = 0; stackExhaust = false;
        NativeBuffs.For(p).Cancel(id);
        State(id).TryStart(Time.realtimeSinceStartupAsDouble, stackExhaustCooldown);
        if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[Stack] expired → cooldown " + stackExhaustCooldown + "s");
    }

    void ClearStacks()
    {
        stackGeneration++;
        stackCount = 0; stackLevel = 0; stackCooldownReduce = 0;
        stackUntil = 0; stackExhaust = false;
    }
}
