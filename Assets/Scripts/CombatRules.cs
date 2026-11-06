using System;

public readonly struct DamageResult
{
    // 单段伤害是 long：源端 DamageCapService 的个人上限之外，本工程允许突破 21 亿，
    // int 会把飘字/累计伤害静默截断在 2,147,483,647。
    public readonly long Amount;
    public readonly bool Critical;
    public DamageResult(long amount, bool critical) { Amount = amount; Critical = critical; }
}

// 接受本地攻击方已计算的伤害；不在受击方重新抽暴击。
// 原端 DamageCapService 的初始个人上限；成长突破尚未接入。
public static class CombatRules
{
    public const long InitialDamageCap = DamageCap.Initial;

    public static DamageResult Resolve(double rolledDamage, bool critical = false,
                                       long damageCap = InitialDamageCap)
    {
        if (double.IsNaN(rolledDamage) || rolledDamage <= 0) return new DamageResult(0, false);
        long cap = Math.Max(InitialDamageCap, damageCap);
        double rolled = Math.Floor(rolledDamage);
        // double 能精确表示的整数上限是 2^53；超过时直接按 long 上限截断（不会再被 21 亿卡住）
        if (rolled >= cap) return new DamageResult(cap, critical);
        return new DamageResult((long)rolled, critical);
    }

    // Character.calculateMaxBaseDamage；实际攻击随机分布不由此方法推定。
    public static int MaxPhysicalDamage(int mainStat, int secondaryStat, int weaponAttack, double weaponMultiplier)
        => (int)Math.Min(int.MaxValue, Math.Max(0,
            Math.Ceiling((weaponMultiplier * mainStat + secondaryStat) * weaponAttack / 100.0)));
    // AbstractDealDamageHandler 魔法上界；总魔攻含 INT。它是原端校验上界而非 EXE 随机公式。
    public static double MaxMagicDamage(int intelligence, int totalMagic, double skillMagicAttack)
        => (Math.Ceiling((totalMagic * Math.Ceiling(totalMagic / 1000.0) + totalMagic) / 30.0)
            + Math.Ceiling(intelligence / 200.0)) * skillMagicAttack;

    // 怪物防御采用282百分比字段；等级差、随机分布仍属于既有战斗管线。
    public static DamageResult Attack(CharacterAttributes a, WeaponDef w, int watk, int matk,
        System.Collections.Generic.Dictionary<string, object> target, NativeSkillDefinition skill,
        double roll, double critRoll, double hitRoll,long damageCap=InitialDamageCap)
    {
        bool magic = skill != null && skill.Magic;
        // 强化核心（VMatrix type=1）对既有技能的加成：最终伤害/无视防御/Boss 伤害。
        // 未装核心时全为 0，既有口径不变。见 NativeSkillEnhance.cs。
        NativeSkillEnhance.Bonus(skill != null ? skill.Id : 0,
            out int enhanceFinal, out _, out int enhanceIgnore, out int enhanceBoss);
        int diff = Math.Max(0, NativeCombatData.Int(target,"level",1) - a.level);
        double accuracy = magic ? a.intelligence / 10.0 + a.luk / 10.0 : a.accuracy + a.dex * .8 + a.luk * .5;
        double required = (1.84 + .07 * diff) * NativeCombatData.Int(target,"eva");
        double hitChance = required <= 0 ? 1 : Math.Max(0, Math.Min(1, accuracy / required));
        if (hitRoll >= hitChance) return new DamageResult(0, false);
        // StatEffect.fixdamage / AbstractDealDamageHandler.getFixDamage:
        // these skills do not use the absent percentage-damage field.
        if (skill != null && skill.FixedDamage >= 0) return Resolve(skill.FixedDamage,damageCap:damageCap);
        bool dexWeapon = w.kind == WeaponKind.bow || w.kind == WeaponKind.crossbow || w.kind == WeaponKind.gun;
        int main = w.kind == WeaponKind.claw ? a.luk : dexWeapon ? a.dex : a.str;
        int secondary = w.kind == WeaponKind.claw ? a.dex + a.str : dexWeapon ? a.str : a.dex;
        // 282 的魔法技能把倍率写在 mad 里；但 V/HEXA 整批没有 mad（526 条无一带），
        // 它们的倍率写在 damage%。mad 缺位时按 100 收口，再由下面的 damage% 相乘，
        // 否则 MaxMagicDamage 尾部 ×0 会把整条技能打死。
        double magicAttack = skill == null || skill.MagicAttack <= 0 ? 100 : skill.MagicAttack;
        double maximum = magic ? MaxMagicDamage(a.intelligence,matk,magicAttack) :
            MaxPhysicalDamage(main,secondary,watk,w.multiplier);
        double minimum = magic ? maximum * .5 : ((w.multiplier * main * .9 * Math.Max(.1, Math.Min(1,a.mastery)) + secondary) * watk / 100.0);
        double damage = minimum + (maximum-minimum) * Math.Max(0,Math.Min(1,roll));
        damage *= Math.Max(0,1-.01*diff);
        int ignoreDefense=0;
        if(a.setItemBonus!=null)a.setItemBonus.TryGetValue("IgnoreDefense",out ignoreDefense);
        if (enhanceIgnore > 0) ignoreDefense = Math.Min(100, ignoreDefense + enhanceIgnore);
        damage *= PercentageDefense.Multiplier(NativeCombatData.Int(target,magic ? "MDRate" : "PDRate"),100-(100-ignoreDefense)*(1-a.hexaIgnorePercent/100.0));
        bool critical = !magic && critRoll < Math.Max(0,Math.Min(1,a.criticalChance));
        // 282 的 V/HEXA 技能把伤害写在 damage% 里、mad 整条留空（526 条无一带 mad），
        // 只有 info/magicDamage=1 说明它走魔法管线。这种「魔法但没有 mad」的技能
        // 仍要乘 damage%，否则法师的 V 技能只剩裸魔攻、伤害掉两个数量级。
        // 有 mad 的技能保持原口径：mad 本身就是倍率，再由 MaxMagicDamage 相乘。
        if (!magic || skill.MagicAttack <= 0)
            // 爆击强化 400031023 的「超出 100% 的暴击率折算成暴击伤害」只加在暴击段上；
            // overCritBonus 在没有那条 buff 时恒为 0，所以既有口径不变。
            damage *= (skill == null ? 100 : skill.DamagePercent) / 100.0 +
                      (critical ? a.criticalBonus + a.overCritBonus : 0);
        // 强化核心的 damR_5th 是「对这条技能的最终伤害 +%」，乘在 damage% 之后、
        // 套装/Boss/最终伤害applyDamage 之前（与源端 StatEffect 的独立乘区同序）。
        if (enhanceFinal > 0) damage *= 1.0 + enhanceFinal / 100.0;
        double element = ElementModifier(target,skill == null ? "" : skill.Element);
        damage = element == 0 ? 0 : Math.Max(1,damage * element);
        // ≡ MapleMap 收到攻击后 SetItemManager.applyDamage：套装 Damage%/Boss|NormalDamage%/FinalDamage%
        if (damage > 0 && a.setItemBonus != null)
            damage = AdminSetItem.ApplyDamage(a.setItemBonus, NativeCombatData.Int(target,"boss",0) != 0,
                (long)Math.Ceiling(damage),a.hexaDamagePercent+
                    (NativeCombatData.Int(target,"boss",0)!=0 ? a.hexaBossPercent + enhanceBoss : 0));
        return Resolve(damage,critical,damageCap);
    }

    public static double ElementModifier(System.Collections.Generic.Dictionary<string,object> target, string element)
    {
        if (target == null || string.IsNullOrEmpty(element) || !target.TryGetValue("elemAttr",out var raw)) return 1;
        string attributes = raw as string;
        if (string.IsNullOrEmpty(attributes)) return 1;
        for (int i=0;i+1<attributes.Length;i+=2)
            if (char.ToUpperInvariant(attributes[i]) == char.ToUpperInvariant(element[0]))
                switch (attributes[i+1]) { case '1': return 0; case '2': return .5; case '3': return 1.5; }
        return 1;
    }

    // BeiDou 79286E; roll is the native uint remainder modulo 10,000,000.
    public static bool PhysicalMiss(int totalAvoid,int accuracy,int level,int mobLevel,int job,int roll)
    {
        int avoid=Math.Max(0,Math.Min(999,totalAvoid)-Math.Max(0,mobLevel-level)/2);
        accuracy=Math.Max(0,Math.Min(999,accuracy));
        double chance=avoid/(accuracy*4.5)*100;
        if(double.IsNaN(chance))return false;
        bool thief=job%1000/100==4;
        chance=Math.Max(thief ? 5 : 2,Math.Min(thief ? 95 : 80,chance));
        return chance>(roll%10000000)*(100.0/9999999);
    }

    public static DamageResult Incoming(double attack, CharacterAttributes a, bool magic, int attackerLevel, double roll,int invincible=0)
    {
        if (double.IsNaN(attack) || attack <= 0) return new DamageResult(0,false);
        double defense = magic ? a.magicDefense : a.weaponDefense;
        double value = attack * System.Math.Max(0,1-.01*System.Math.Max(0,a.level-attackerLevel)) * (.85 + .3 * Math.Max(0,Math.Min(1,roll))) - defense * (magic ? .6 : .5);
        // 满额无敌 = 完全免伤。InvincibleDamage 会把伤害归零，但尾部的 Math.Max(1, …)
        // 又把它抬回 1 点 —— 过去「无敌」每次受击还掉 1 血。虚无型态（在#time间进入无敌）
        // 与特殊核心的无敌都走这条路，且对魔法同样生效（真无敌不该分物理魔法）。
        if (invincible >= 100) return new DamageResult(0,false);
        // 0x7933B2..0x7933E6: physical damage, after defense, before minimum/integer conversion.
        if (!magic) value=InvincibleDamage(value,invincible);
        return Resolve(Math.Max(1,value),false,long.MaxValue);
    }

    public static double InvincibleDamage(double damage,int percent)
        => damage-percent*damage*.01;

}

// 原端以角色持有的 skillId -> 开始时间/时长存冷却。单机使用真实秒数，顿帧不延长冷却。
public sealed class SkillRuntimeState
{
    public double ReadyAt { get; private set; }
    public double Remaining(double now) => Math.Max(0, ReadyAt - now);
    public void Reset() => ReadyAt=0;
    public bool TryStart(double now, double duration)
    {
        if (double.IsNaN(now) || double.IsNaN(duration) || duration < 0 || Remaining(now) > 0) return false;
        ReadyAt = now + duration;
        return true;
    }
    // 特殊核心（回归 / 没有结束的攻击 / 忍耐II / 耐性I）的「剩余冷却时间减少 N%」：
    // 按剩余量打折，已经转好的技能不受影响。
    public void Reduce(double now, int percent)
    {
        double left = Remaining(now);
        if (left <= 0 || percent <= 0) return;
        ReadyAt = now + left * (1 - Math.Min(100, percent) / 100.0);
    }
}
