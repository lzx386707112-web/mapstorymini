using System;
using System.Collections.Generic;
using UnityEngine;

// 282 的护盾类技能：一个会被消耗的池子，不是属性 stat。
//
// 为什么不能塞进 NativeBuffs.SkillStats：那个函数产出的是「属性 → 数值」，
// 挂上去就是一个恒定生效的 buff。护盾有存量、会被打掉、会衰减、会过期，
// 玩家按键之后什么都没发生就是因为它被当成普通 buff 处理（stats 为空 → ApplySkill 直接 false）。
//
// 字段语义不在这里猜。tools/ms282/export_skill_shields.py 从 String.wz 的 h 文案
// 逐条核对后落成 Assets/Resources/NativeShieldContract.json，本文件只是执行器：
//
//   pool       突击之盾VI   「生成相当于最大HP的#x%的护盾，持续 #time秒」
//              一次性定量护盾，吸收完或超时消失。
//   stack      海神护体     「生成可吸收#x%最大HP伤害的保护罩#time秒／可累积等同#u%最大HP的数值，
//                            每秒失去#y% + #z当前保护罩数值」
//              每次命中累积，有上限，按秒衰减。
//   elements   西尔芙之壁   「生成持有#w的元素之风之屏障…受击时每造成相当于最大HP的1%伤害
//                            便#z消耗元素以无效化伤害」
//              计数型，按「最大HP 1%」为一档扣元素，元素耗尽护盾消失。
//   invincible 虚无型态     「在#time间进入无敌」
//              纯无敌，不在这里执行 —— 走 SkillStats 的 INVINCIBLE，见 BuffInvincible。
//
// 吸收位置：PlayerCtrl.TakeDamage 里，防御与 Achilles/高防之后、Magic Guard 之前。
// 也就是「先吃护盾，剩下的才由 MP / 金币分摊」。
public sealed class NativeShields : MonoBehaviour
{
    static Dictionary<string, object> contract;

    sealed class Slot
    {
        public int Id, Level;
        public string Mode;
        public double Until, NextDecay;
        public float Amount, Cap;
        public float Initial;                       // 首次施放的存量，HUD 用它做分母
        public float DecayPrimaryPct, DecaySecondaryPct;
        public int Elements, ElementsMax, Cost;
        public float StepAmount;
    }

    /// <summary>给 HUD 的一格只读视图（不暴露 Slot，避免外部改到运行时状态）。</summary>
    public struct Entry
    {
        public int Id, Level;
        public string Mode;
        public float Value;      // pool/stack：剩余可吸收量；elements：剩余元素个数
        public float Max;        // pool/elements：初始值；stack：上限
        public int RemainMs;     // 距消失还有多久
        public bool Counted => Mode == "elements";   // 显示成「12/300」而不是数值条
    }

    readonly List<Slot> slots = new List<Slot>();

    static Dictionary<string, object> Section(string name)
    {
        if (contract == null)
        {
            var text = Resources.Load<TextAsset>("NativeShieldContract")?.text;
            // 缺表时退化为「没有护盾」，不影响既有玩法；守卫脚本会报出来。
            contract = string.IsNullOrEmpty(text)
                ? new Dictionary<string, object>()
                : (Dictionary<string, object>)MiniJson.Parse(text);
        }
        return contract.TryGetValue(name, out var value) ? value as Dictionary<string, object> : null;
    }

    static Dictionary<string, object> Spec(int id)
    {
        var all = Section("shields");
        if (all == null || !all.TryGetValue(id.ToString(), out var raw)) return null;
        return raw as Dictionary<string, object>;
    }

    /// <summary>该技能的护盾是不是纯无敌型（由 NativeBuffs.SkillStats 写成 INVINCIBLE）。</summary>
    public static bool IsInvincible(int id)
    {
        var spec = Spec(id);
        return spec != null && (string)spec["mode"] == "invincible";
    }

    /// <summary>逐级数组按下标取；越界夹到首尾（与 NativeSkillEffects 一致）。</summary>
    static int At(Dictionary<string, object> spec, string key, int level, int fallback = 0)
    {
        if (spec == null || !spec.TryGetValue(key, out var raw)) return fallback;
        if (!(raw is List<object> list) || list.Count == 0) return fallback;
        return Convert.ToInt32(list[Mathf.Clamp(level - 1, 0, list.Count - 1)]);
    }

    public static NativeShields For(PlayerCtrl player)
        => player.GetComponent<NativeShields>() ?? player.gameObject.AddComponent<NativeShields>();

    /// <summary>剩余护盾总量，给 HUD / 调试用。</summary>
    public float Remaining
    {
        get
        {
            float total = 0f;
            foreach (var slot in slots)
                total += slot.Mode == "elements" ? slot.Elements * slot.StepAmount : slot.Amount;
            return total;
        }
    }

    /// <summary>
    /// 当前生效的护盾（按挂上顺序，供 TemporaryStatView 画状态图标）。
    /// 不做任何计算，只把运行时状态摊平；过期槽位不返回（Update 会先清掉，这里再兜一次）。
    /// </summary>
    public void Snapshot(List<Entry> into)
    {
        if (into == null) return;
        into.Clear();
        double now = NativeBuffs.Now;
        foreach (var slot in slots)
        {
            if (now >= slot.Until) continue;
            into.Add(new Entry
            {
                Id = slot.Id,
                Level = slot.Level,
                Mode = slot.Mode,
                Value = slot.Mode == "elements" ? slot.Elements : slot.Amount,
                Max = slot.Mode == "stack" ? slot.Cap
                    : slot.Mode == "elements" ? slot.ElementsMax : slot.Initial,
                RemainMs = (int)Math.Min(int.MaxValue, Math.Max(0, Math.Ceiling((slot.Until - now) * 1000)))
            });
        }
    }

    /// <summary>TemporaryStatView 的便捷入口：没有护盾组件时返回 0 条。</summary>
    public static void Snapshot(PlayerCtrl player, List<Entry> into)
    {
        var shields = player != null ? player.GetComponent<NativeShields>() : null;
        if (shields == null) { into?.Clear(); return; }
        shields.Snapshot(into);
    }

    Slot Find(int id)
    {
        foreach (var slot in slots) if (slot.Id == id) return slot;
        return null;
    }

    /// <summary>施放护盾技能（SkillSystem.Cast 调用）。同一技能重复施放会覆盖旧池。</summary>
    public bool Activate(int id, int level, float maxHp)
    {
        var spec = Spec(id);
        if (spec == null || maxHp <= 0f) return false;
        string mode = (string)spec["mode"];
        if (mode == "invincible") return false;         // 无敌走 buff stat，不在这里

        double now = NativeBuffs.Now;
        var slot = Find(id);
        if (slot != null && mode != "stack") slots.Remove(slot);   // pool / elements 重新施放 = 新池
        slot = Find(id) ?? new Slot { Id = id };
        slot.Level = level;
        slot.Mode = mode;
        slot.Until = now + At(spec, "time", level, 1);
        slot.NextDecay = now + 1.0;

        switch (mode)
        {
            case "pool":
                slot.Amount = maxHp * At(spec, "amountPct", level) / 100f;
                break;
            case "stack":
                slot.Cap = maxHp * At(spec, "capPct", level) / 100f;
                float gain = maxHp * At(spec, "gainPct", level) / 100f;
                slot.Amount = Mathf.Min(slot.Cap, slot.Amount + gain);
                slot.DecayPrimaryPct = At(spec, "decayPrimaryPct", level);
                slot.DecaySecondaryPct = At(spec, "decaySecondaryPct", level);
                break;
            case "elements":
                slot.Elements = At(spec, "elements", level);
                slot.ElementsMax = slot.Elements;
                slot.Cost = Mathf.Max(1, At(spec, "cost", level, 1));
                slot.StepAmount = maxHp * At(spec, "stepPct", level, 1) / 100f;
                break;
            default:
                return false;
        }
        // pool 的分母是首次施放的定量；stack 用上限、elements 用初始元素数（见 Snapshot）。
        if (mode == "pool") slot.Initial = Mathf.Max(1f, slot.Amount);
        if (slot.Amount <= 0f && slot.Elements <= 0) return false;
        if (Find(id) == null) slots.Add(slot);
        return true;
    }

    /// <summary>
    /// 吃掉一次伤害，返回实际吸收量。damage 会被就地削减。
    /// 后施放的护盾先吸收（与「最近挂上的效果最先响应」一致）。
    /// </summary>
    public float Absorb(ref float damage)
    {
        if (damage <= 0f) return 0f;
        float absorbed = 0f;
        for (int i = slots.Count - 1; i >= 0 && damage > 0f; i--)
        {
            var slot = slots[i];
            if (slot.Mode == "elements")
            {
                if (slot.StepAmount <= 0f) { slots.RemoveAt(i); continue; }
                // 「每造成相当于最大HP的1%伤害便消耗 z 个元素」：不足一档也按一档算。
                int steps = Mathf.CeilToInt(damage / slot.StepAmount);
                int needed = steps * slot.Cost;
                if (slot.Elements >= needed)
                {
                    slot.Elements -= needed;
                    absorbed += damage;
                    damage = 0f;
                }
                else
                {
                    // 元素不够就顶到耗尽为止，护盾随之消失（h：全部消耗时风之屏障消失）。
                    float usable = slot.Elements / (float)slot.Cost * slot.StepAmount;
                    absorbed += Mathf.Min(damage, usable);
                    damage = Mathf.Max(0f, damage - usable);
                    slot.Elements = 0;
                }
                if (slot.Elements <= 0) slots.RemoveAt(i);
                continue;
            }

            float take = Mathf.Min(slot.Amount, damage);
            slot.Amount -= take;
            absorbed += take;
            damage -= take;
            if (slot.Amount <= 0f) slots.RemoveAt(i);
        }
        return absorbed;
    }

    /// <summary>
    /// 「连接技能命中时」——海神护体的累积入口。SkillSystem 每次主技能结算命中后调用一次。
    /// 技能本身按不按都行：它是命中触发的，按一下只等于多累积一次。
    /// 遍历表而不是写死 id：登记新技能不用改这里。
    /// </summary>
    public static void OnSkillHit(PlayerCtrl player, float maxHp)
    {
        if (player == null || maxHp <= 0f) return;
        var all = Section("shields");
        if (all == null) return;
        foreach (var entry in all)
        {
            if (!(entry.Value is Dictionary<string, object> spec)) continue;
            if (!spec.TryGetValue("mode", out var mode) || (string)mode != "stack") continue;
            if (!int.TryParse(entry.Key, out int id)) continue;
            int level = SkillProgress.LevelOf(id);
            if (level <= 0) continue;                    // 没学就不会累积
            For(player).Activate(id, level, maxHp);
        }
    }

    void Update()
    {
        if (slots.Count == 0) return;
        double now = NativeBuffs.Now;
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (now >= slot.Until) { slots.RemoveAt(i); continue; }
            if (slot.Mode != "stack" || now < slot.NextDecay) continue;
            slot.NextDecay = now + 1.0;
            // h：「每秒失去 #y% + #z当前保护罩数值」。y 与 z 是同一个衰减率拆成的两段
            // （逐级 y+z = 90 → 75 平滑递减、两者同步下降），基准都是**当前存量**。
            // 若按「上限的 y%」算，30 级每秒就掉 25% 上限，护盾永远攒不起来，与「可累积」矛盾。
            float rate = (slot.DecayPrimaryPct + slot.DecaySecondaryPct) / 100f;
            slot.Amount -= slot.Amount * rate;
            if (slot.Amount <= 0f) slots.RemoveAt(i);
        }
    }

    public void Clear() => slots.Clear();

    /// <summary>
    /// 写进 buff stat 的无敌标记（NativeBuffs.SkillStats 调用）。
    /// 只认表里登记成 invincible 的技能，其余技能不受影响。
    /// </summary>
    public static void BuffInvincible(Dictionary<string, int> stats, int id)
    {
        if (stats == null || !IsInvincible(id)) return;
        stats["INVINCIBLE"] = 100;
    }
}
