using System;

public readonly struct DamageResult
{
    public readonly int Amount;
    public readonly bool Critical;
    public DamageResult(int amount, bool critical) { Amount = amount; Critical = critical; }
}

// 接受本地攻击方已计算的伤害；不在受击方重新抽暴击。
// 原端 DamageCapService 的初始个人上限；成长突破尚未接入。
public static class CombatRules
{
    public const int InitialDamageCap = 19999999;

    public static DamageResult Resolve(double rolledDamage, bool critical = false,
                                       int damageCap = InitialDamageCap)
    {
        if (double.IsNaN(rolledDamage) || rolledDamage <= 0) return new DamageResult(0, false);
        var cap = Math.Max(InitialDamageCap, damageCap);
        return new DamageResult((int)Math.Min(cap, Math.Floor(rolledDamage)), critical);
    }

    // Character.calculateMaxBaseDamage；实际攻击随机分布不由此方法推定。
    public static int MaxPhysicalDamage(int mainStat, int secondaryStat, int weaponAttack, double weaponMultiplier)
        => (int)Math.Min(int.MaxValue, Math.Max(0,
            Math.Ceiling((weaponMultiplier * mainStat + secondaryStat) * weaponAttack / 100.0)));
    // AbstractDealDamageHandler 魔法上界；总魔攻含 INT。它是原端校验上界而非 EXE 随机公式。
    public static double MaxMagicDamage(int intelligence, int totalMagic, double skillMagicAttack)
        => (Math.Ceiling((totalMagic * Math.Ceiling(totalMagic / 1000.0) + totalMagic) / 30.0)
            + Math.Ceiling(intelligence / 200.0)) * skillMagicAttack;

    // 单机伤害策略：旧版平减防御、等级差、熟练度和逐段独立暴击。
    // 原服务端只收客户端结果；下列随机/防御系数尚不能据服务端证明为当前 EXE 常量。
    public static DamageResult Attack(CharacterAttributes a, WeaponDef w, int watk, int matk,
        System.Collections.Generic.Dictionary<string, object> target, NativeSkillDefinition skill,
        double roll, double critRoll, double hitRoll)
    {
        bool magic = skill != null && skill.Magic;
        int diff = Math.Max(0, NativeCombatData.Int(target,"level",1) - a.level);
        double accuracy = magic ? a.intelligence / 10.0 + a.luk / 10.0 : a.accuracy + a.dex * .8 + a.luk * .5;
        double required = (1.84 + .07 * diff) * NativeCombatData.Int(target,"eva");
        double hitChance = required <= 0 ? 1 : Math.Max(0, Math.Min(1, accuracy / required));
        if (hitRoll >= hitChance) return new DamageResult(0, false);
        bool dexWeapon = w.kind == WeaponKind.bow || w.kind == WeaponKind.crossbow || w.kind == WeaponKind.gun;
        int main = w.kind == WeaponKind.claw ? a.luk : dexWeapon ? a.dex : a.str;
        int secondary = w.kind == WeaponKind.claw ? a.dex + a.str : dexWeapon ? a.str : a.dex;
        double maximum = magic ? MaxMagicDamage(a.intelligence,matk,skill.MagicAttack) :
            MaxPhysicalDamage(main,secondary,watk,w.multiplier);
        double minimum = magic ? maximum * .5 : ((w.multiplier * main * .9 * Math.Max(.1, Math.Min(1,a.mastery)) + secondary) * watk / 100.0);
        double damage = minimum + (maximum-minimum) * Math.Max(0,Math.Min(1,roll));
        damage *= Math.Max(0,1-.01*diff);
        damage -= NativeCombatData.Int(target,magic ? "MDDamage" : "PDDamage") * (magic ? .6 : .5);
        bool critical = !magic && critRoll < Math.Max(0,Math.Min(1,a.criticalChance));
        if (!magic) damage *= (skill == null ? 100 : skill.DamagePercent) / 100.0 + (critical ? a.criticalBonus : 0);
        double element = ElementModifier(target,skill == null ? "" : skill.Element);
        damage = element == 0 ? 0 : Math.Max(1,damage * element);
        // ≡ MapleMap 收到攻击后 SetItemManager.applyDamage：套装 Damage%/Boss|NormalDamage%/FinalDamage%
        if (damage > 0 && a.setItemBonus != null)
            damage = AdminSetItem.ApplyDamage(a.setItemBonus, NativeCombatData.Int(target,"boss",0) != 0,
                (int)Math.Min(int.MaxValue, Math.Ceiling(damage)));
        return Resolve(damage,critical);
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

    public static DamageResult Incoming(double attack, CharacterAttributes a, bool magic, int attackerLevel, double roll)
    {
        if (double.IsNaN(attack) || attack <= 0) return new DamageResult(0,false);
        double defense = magic ? a.magicDefense : a.weaponDefense;
        double value = attack * System.Math.Max(0,1-.01*System.Math.Max(0,a.level-attackerLevel)) * (.85 + .3 * Math.Max(0,Math.Min(1,roll))) - defense * (magic ? .6 : .5);
        return Resolve(Math.Max(1,value),false,int.MaxValue);
    }

}

// 原端以角色持有的 skillId -> 开始时间/时长存冷却。单机使用真实秒数，顿帧不延长冷却。
public sealed class SkillRuntimeState
{
    public double ReadyAt { get; private set; }
    public double Remaining(double now) => Math.Max(0, ReadyAt - now);
    public bool TryStart(double now, double duration)
    {
        if (double.IsNaN(now) || double.IsNaN(duration) || duration < 0 || Remaining(now) > 0) return false;
        ReadyAt = now + duration;
        return true;
    }
}
