using System.Collections.Generic;
using UnityEngine;

// Character.giveDebuff: one active instance per disease; poison is non-lethal.
public sealed class NativeMobDebuffs : MonoBehaviour
{
    sealed class Disease { public int x, y; public long until, nextTick; }
    readonly Dictionary<int,Disease> active = new Dictionary<int,Disease>();
    PlayerCtrl player;
    public static NativeMobDebuffs For(PlayerCtrl player)
    {
        var result = player.GetComponent<NativeMobDebuffs>() ?? player.gameObject.AddComponent<NativeMobDebuffs>();
        result.player = player;
        return result;
    }
    public bool Has(int id) => active.TryGetValue(id,out var d) && NativeMobController.Now < d.until;
    public int ForcedWalkSpeed => Has(126) ? active[126].x : 0;
    /// <summary>
    /// 状态异常耐性（`asrR`）：按百分比抵抗新施加的异常状态。
    /// 282 把 `asrR` 当「施加异常状态时的抵抗几率」，本工程没有其它抗性来源，
    /// 供给方只有技能：
    ///   · 永久被动 —— 400001004 实用的战斗命令「[被动效果：状态异常抗性 #asrR增加]」
    ///     （1..6%），经 NativeSkillEffects 的被动表落进 setItemBonus 的 ASR 键；
    ///   · 时效增益 —— 400011066 钢铁之躯「在#time秒内…提升状态异常抗性#indieAsrR」，
    ///     落进 NativeBuffs 的 buff stat ASR。
    /// 两段相加（原端同样把 stat 与 temp stat 累加）。字段语义由
    /// tools/ms282/export_skill_effects.py 从 h 原文生成，这里不写死数值。
    /// </summary>
    public int ResistancePercent
    {
        get
        {
            if (player == null) return 0;
            int value = 0;
            var bonus = player.Attributes?.setItemBonus;
            if (bonus != null && bonus.TryGetValue("ASR", out int passive)) value += passive;
            var buffs = player.GetComponent<NativeBuffs>();
            if (buffs != null) value += buffs.Value("ASR");
            return Mathf.Clamp(value, 0, 100);
        }
    }
    public void Apply(int id, int x, int y, long duration, bool region = false)
    {
        if (Has(id) || player.hp <= 0) return;
        // 抵抗判定放在所有其它拒绝条件之前：被抵抗掉的状态不该占「最多两种病」的名额。
        int resist = ResistancePercent;
        if (resist > 0 && Random.value * 100f < resist) return;
        // Two source rules, deliberately not merged: MobSkill.applyDisease's region
        // branch (:525) blocks EVERY disease behind HOLY_SHIELD, while
        // Character.giveDebuff (:2823) lets STUN (123) and SEDUCE (128) through.
        if (region && player.GetComponent<NativeBuffs>()?.HasSource(2321005)==true) return;
        if (id!=123 && id!=128 && player.GetComponent<NativeBuffs>()?.HasSource(2321005)==true) return;
        int count = 0;
        foreach (var disease in active.Values) if (NativeMobController.Now < disease.until) count++;
        if (count >= 2) return; // Character.giveDebuff permits at most two diseases.
        active[id] = new Disease { x = x, y = y, until = NativeMobController.Now + duration, nextTick = NativeMobController.Now + 1000 };
    }
    public void Clear() => active.Clear();
    public void Dispel()
    {
        foreach (int id in new[]{124,121,125,120,122,126}) active.Remove(id);
    }
    public void Purge()
    {
        Dispel(); active.Remove(128); active.Remove(133); active.Remove(132);
    }
    public void Clear(int id) => active.Remove(id);
    public void Input(ref float horizontal, ref bool jump, ref float vertical)
    {
        if (Has(132)) { horizontal = -horizontal; vertical = -vertical; }
        if (Has(122)) jump = false;
        if (Has(123)) { horizontal = vertical = 0; jump = false; }
        if (Has(128))
        {
            int mode = active[128].x;
            horizontal = mode == 1 ? -1 : mode == 2 ? 1 : 0;
            jump = mode == 3; vertical = 0;
        }
    }
    public bool BlocksAttack => Has(123) || Has(128);
    public bool BlocksSkill => Has(120) || BlocksAttack;
    public float Healing(float amount, bool skill) => Has(133) ? (skill ? -amount : amount / 2) : amount;
    void Update()
    {
        if (player == null || player.hp <= 0 || PlayerCtrl.InputLocked || Time.deltaTime <= 0) return;
        if (Has(125))
        {
            var d = active[125];
            if (NativeMobController.Now >= d.nextTick)
            {
                d.nextTick = NativeMobController.Now + 1000;
                player.TakeBossDamage(Mathf.Min(Mathf.Max(0,player.hp-1),Mathf.Max(1,d.x)));
            }
        }
    }
}
