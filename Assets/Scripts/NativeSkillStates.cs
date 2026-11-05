using System.Collections.Generic;
using UnityEngine;

// 282 的「技能子状态」：由母技能施放时挂上、自己不能按的 invisible buff id。
//
// 这些 id 在 UI/AdvancedSkills.json 里被标成 type=buff，于是过去一直被
// check_skill_effects.py 报成「buff 但无 stat／按下没反应」。查 282 原始节点才知道
// 它们不是技能：invisible=1、String.wz 里没有 h、level/1 只有 time/subTime，
// 而同名兄弟 id 有完整 h + mpCon/damage/cooltime；影子 id 的 time 与母技能 level 行
// 逐级相等（详见 tools/ms282/export_skill_states.py 的四条证据）。
//
// 例：
//   11141500 银河星爆 h「使用時#w2秒內最終傷害增加#q2%」 → 状态 11141501(time=20,y=q2)
//   5141500  海龙霸拳 h「在#time秒內…使用五轉技能時觸發水龍鬥氣」 → 状态 5141501.skillList
//   4240016  暗影之隱 h「隱身術持續#v秒以上時，強化為暗影之刃」   → 状态 4240017(time=w=3)
//
// 所以这里的职责只有两件：
//   ① 母技能施放 → 记一条有时限的状态（timedEffect 的顺手走 NativeBuffs 注册增益）；
//   ② 让 proc / 连击链能查询「状态现在还在不在」。
// 状态本身永远不产生 stat，也不画 buff 图标（invisible）。
public sealed class NativeSkillStates : MonoBehaviour
{
    static Dictionary<string, object> table;
    readonly Dictionary<int, double> until = new Dictionary<int, double>();
    readonly Dictionary<int, double> started = new Dictionary<int, double>();

    static Dictionary<string, object> States
    {
        get
        {
            if (table == null)
            {
                var text = Resources.Load<TextAsset>("NativeSkillStates")?.text;
                table = string.IsNullOrEmpty(text)
                    ? new Dictionary<string, object>()
                    : (Dictionary<string, object>)MiniJson.Parse(text);
            }
            return table.TryGetValue("states", out var value)
                ? value as Dictionary<string, object> : null;
        }
    }

    public static Dictionary<string, object> Spec(int id)
    {
        var all = States;
        if (all == null || !all.TryGetValue(id.ToString(), out var raw)) return null;
        return raw as Dictionary<string, object>;
    }

    public static bool IsState(int id) => Spec(id) != null;

    /// <summary>
    /// 该状态是否要额外挂一条有时限的属性增益。
    /// NativeBuffs.OverTime 对这类 id 判 false（info 里没有 effect/skillType），
    /// 所以 ApplySkill 要靠这个标记放行。
    /// </summary>
    public static bool IsTimedEffect(int id)
        => Int(id, "timedEffect") != 0;

    /// <summary>该状态是不是「无视一次 HP 归零」（天堂之門VI 2341012）。</summary>
    public static bool IsDeathGuard(int id)
        => Int(id, "deathGuard") != 0;

    public static int Int(int id, string key, int fallback = 0)
    {
        var spec = Spec(id);
        if (spec == null || !spec.TryGetValue(key, out var raw)) return fallback;
        if (raw is bool flag) return flag ? 1 : 0;
        return System.Convert.ToInt32(raw);
    }

    public static int[] Ints(int id, string key)
    {
        var spec = Spec(id);
        if (spec == null || !spec.TryGetValue(key, out var raw) || !(raw is List<object> list))
            return new int[0];
        var result = new int[list.Count];
        for (int i = 0; i < list.Count; i++) result[i] = System.Convert.ToInt32(list[i]);
        return result;
    }

    /// <summary>表里的全部状态 id（给 proc 链遍历用，避免把技能 id 硬编码进 C#）。</summary>
    public static int[] AllIds()
    {
        var all = States;
        if (all == null) return new int[0];
        var result = new List<int>();
        foreach (var key in all.Keys) if (int.TryParse(key, out int id)) result.Add(id);
        return result.ToArray();
    }

    public static NativeSkillStates For(PlayerCtrl player)
        => player.GetComponent<NativeSkillStates>() ?? player.gameObject.AddComponent<NativeSkillStates>();

    /// <summary>母技能施放：把它名下的子状态全部激活（一般只有一个）。</summary>
    public void Activate(int parentId, int level)
    {
        var all = States;
        if (all == null) return;
        foreach (var entry in all)
        {
            if (!int.TryParse(entry.Key, out int id)) continue;
            if (Int(id, "parent") != parentId) continue;
            ActivateState(id, level);
        }
    }

    /// <summary>
    /// 直接激活某状态（暗影之刃由隐身时长/暗影雾杀触发，不是按键触发的）。
    /// secondsOverride > 0 时用它，否则用状态表里登记的时间。
    /// </summary>
    public void ActivateState(int stateId, int level, float secondsOverride = 0f)
    {
        if (!IsState(stateId)) return;
        float seconds = secondsOverride > 0f ? secondsOverride : Int(stateId, "time");
        if (seconds <= 0f) return;
        double now = NativeBuffs.Now;
        // 重复激活只续期，不重置「已挂上多久」（暗影之隐要看隐身持续 #v 秒）。
        if (!started.ContainsKey(stateId) || !Has(stateId)) started[stateId] = now;
        until[stateId] = now + seconds;
        if (!IsTimedEffect(stateId)) return;
        // 银河星爆：#w2 秒内最终伤害 +q2%。增益字段走 NativeSkillEffects 的 alias
        // （y → FinalDamage），时长取状态自己的 time，与 w2 逐级相等。
        var player = GetComponent<PlayerCtrl>();
        if (player != null) NativeBuffs.For(player).ApplySkill(stateId, level, NativeBuffs.Now);
    }

    public bool Has(int stateId)
        => until.TryGetValue(stateId, out double end) && NativeBuffs.Now < end;

    /// <summary>一次性状态用掉后立刻失效（天堂之门：无视一次 HP 归零）。</summary>
    public void Consume(int stateId)
    {
        until.Remove(stateId);
        started.Remove(stateId);
    }

    /// <summary>状态已经持续了多少秒（暗影之隐「隐身术持续 #v 秒以上」用）。</summary>
    public double Since(int stateId)
        => started.TryGetValue(stateId, out double from) ? NativeBuffs.Now - from : 0.0;

    void Update()
    {
        if (until.Count == 0) return;
        double now = NativeBuffs.Now;
        var expired = new List<int>();
        foreach (var pair in until) if (now >= pair.Value) expired.Add(pair.Key);
        foreach (int id in expired) { until.Remove(id); started.Remove(id); }
    }

    public void Clear() { until.Clear(); started.Clear(); }
}
