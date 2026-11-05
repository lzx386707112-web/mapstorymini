using UnityEngine;

// 实用的战斗命令（400001004）：施放后 time 秒内**所有技能等级 +x**（x=1）。
//
// 技能自己的 h 只有三条信息，没有任何 stat：
//   「消耗HP #hpCon，在#time秒内所有技能等级+1 / 冷却时间 #cooltime秒 /
//     [被动效果：状态异常抗性 #asrR增加]」
// common = { x:1, time:180+3*x, asrR:u(x/5), cooltime:180, hpCon:100, maxLevel:30 }。
//
// 所以它不是属性 buff：NativeBuffs.SkillStats 对它的时效侧一条 stat 都不产出，过去
// ApplySkill 直接 return false —— 按下去没有图标、也没有任何效果。现在由
// NativeBuffs 放一个 SKILL_ORDER 注册标记（值 = x）承载生效期与 buff 图标，
// 等级换算在本文件；括号里的 asrR 是永久被动，走 NativeSkillEffects 的被动通道
// （字段 → setItemBonus 的 ASR，消费方见 Field/NativeMobDebuffs.cs）。
//
// 谁能被提升（desc 原文）：
//   「一定时间内提升自身的所有技能等级。限于4转技能可以增加到最大等级以上，
//     其他技能只能提高到最大等级为止。部分例外的特别技能和初心者技能、战斗命令、
//     实用的战斗命令、超技能、5转、6转技能的等级是无法提高技能等级。」
//
// 判据用职业编码（id/10000 的个位），不用「282 的 combatOrders 节点」：那个节点确实存在
// （每条技能一个 int，值只有 1），但 282 只给自己的 750 条保留技能写了它 —— JobTree
// 476 条在 282 包里的技能只有 43 条带，其余四转技能一条都没有。拿它当判据会让绝大多数
// 四转技能失效，比不判更糟。职业编码则与 desc 的分档严格对应：
//   个位 0/1/2 = 一转/二转/三转/四转（含骑士团的 1100/1110/1111/1112）；可以提升
//   个位 3..9 = 五转觉醒（四转+1）、六转（四转+2）与超技能编码；不可提升
//   id ≥ 400000000 = 五转 V 核心 / 六转 V 编码 / 强化核心 400004xxx；不可提升
//   job % 1000 == 0 = 初学者与职业群共通（1000、2000…，精灵的祝福这类）；不可提升
public static class NativeCombatOrders
{
    /// <summary>生效中的加成级数；0 = 没生效、或这条技能不参与提升。</summary>
    public static int Bonus(int skillId)
    {
        int bonus = ActiveBonus;
        return bonus > 0 && Applies(skillId) ? bonus : 0;
    }

    /// <summary>
    /// 控制者身上的加成级数（不判技能）。生效期与图标由 NativeBuffs 的 SKILL_ORDER 承载，
    /// 这里只读当前胜出表、不做 Tick —— SkillProgress.LevelOf 会在每次伤害结算里被调用。
    /// </summary>
    public static int ActiveBonus
    {
        get
        {
            var player = GameLoop.I != null ? GameLoop.I.player : null;
            var buffs = player != null ? player.GetComponent<NativeBuffs>() : null;
            return buffs != null ? buffs.SkillLevelBoost : 0;
        }
    }

    /// <summary>这条技能的等级是否参与提升（不含「提升后能不能超过上限」，那是 Cap）。</summary>
    public static bool Applies(int id)
    {
        var d = SkillBook.Get(id);
        if (d == null) return false;
        if (d.autoLearn) return false;                 // 精灵的祝福：随账号等级自动习得
        if (d.hexa) return false;                      // 六转：等级只跟 HEXA 矩阵走
        if (id >= 400000000) return false;             // 五转 V / 强化核心（含技能自己）
        int job = id / 10000;
        if (job % 1000 == 0) return false;             // 初学者 / 职业群共通
        return job % 10 <= 2;                          // 一二三四转
    }

    /// <summary>
    /// 加成后的等级上限。
    /// · 四转：可以越过加点上限（技能册的 masterLevel=10 这种），所以是 MasterCap + bonus；
    /// · 其他：desc「只能提高到最大等级为止」，即 MasterCap（非四转时它本来就等于 maxLevel）。
    /// 两侧最后都夹到 WZ 的 maxLevel —— 282 的技能表只有 1..maxLevel 行，31 级没有数值可读，
    /// 所以「满 maxLevel 的四转技能」在本工程里提升后数值不变（客户端原版是有 31 级行的，
    /// 这是数据缺口，不是判定缺口）。
    /// </summary>
    public static int Cap(int id, int bonus)
    {
        var d = SkillBook.Get(id);
        if (d == null) return 0;
        int cap = SkillBook.MasterCapOf(id);
        if (!SkillBook.IsFourthJobSkill(id)) return cap;
        return Mathf.Min(d.maxLevel, cap + bonus);
    }
}
