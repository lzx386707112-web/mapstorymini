using System.Collections.Generic;
using UnityEngine;

// 「一个技能什么时候放哪个部件」的唯一来源。
//
// 时间轴（秒）：
//     prepare ──windup──> cast ──impact(或弹道飞行)──> hit 爆点 + 追加部件
//
// 数值优先取 WZ 里导出到 manifest 的 parts 标量（special.count / interval、
// tile.effectDistance、special.fall + duration），这里只填原版没有的那部分：
// 施放延迟、弹道速度、落点铺开这些「客户端手感」参数。
public sealed class SkillFxPlan
{
    public enum SpecialKind
    {
        None,   // 没有 special 部件
        Wave,   // 贴地推进的波次（斗气死亡断层 4 道）
        Fall,   // 从空中砸下来的落矢（箭雨、烈火箭）
        Burst,  // 原地连续爆炸（末日烈焰）
    }

    public float windup;            // 起手（prepare）时长
    public float impact;            // 施放到判定的时间
    public bool ball;               // 是否有弹道
    public float ballDelay;         // 出弹时刻（起手动作先做完）
    public float ballSpeed = 14f;   // 弹道速度（世界单位/秒）
    public float ballRange = 12f;   // 没有目标时飞多远
    public float ballHeight = 0.95f;// 出手高度（角色手部）
    public SpecialKind special = SpecialKind.None;
    public float specialGap = 0.12f;// Wave / Burst 的间隔
    public float specialSpread = 4f;// Fall / Burst 的横向铺开（世界单位）
    public bool tiles;              // 有没有砸地后停留的 tile 部件
    public float tileGap = 0.14f;
    public bool atImpact;           // 追加部件是围着「弹道落点」铺，而不是围着角色（烈火箭）
    public bool affected;           // 增益光环（愤怒之火 / 天秤之使）
    public float cast2Delay = -1f;  // 次要施法层出现时刻（<0 = 不播）
    public bool cast2Follow;        // 次要层是否跟着角色走（突进残影）

    static readonly Dictionary<string, SkillFxPlan> table = new Dictionary<string, SkillFxPlan>
    {
        // —— 骑士 ——
        { "crush",    new SkillFxPlan { impact = 0.25f } },                                    // 防御崩坏：只有 mob 部件
        { "dash",     new SkillFxPlan { impact = 0f, cast2Delay = 0f, cast2Follow = true } }, // 突进：残影跟人
        { "roar",     new SkillFxPlan { impact = 0.30f } },                                    // 虎咆哮：hit + mob
        { "illusion", new SkillFxPlan { impact = 0f } },                                       // 剑影分身：判定时刻由原端毫秒表驱动
        // —— 狂战士 ——
        { "rage",     new SkillFxPlan { impact = 0f, affected = true } },                      // 愤怒之火：只有光环
        { "rupture",  new SkillFxPlan { impact = 1.0f, special = SkillFxPlan.SpecialKind.Wave, specialGap = 0.42f } },
        { "magnet",   new SkillFxPlan { impact = 0.18f } },                                    // 磁石：吸附，无爆点
        { "berserk",  new SkillFxPlan { impact = 0.20f } },
        // —— 魔法师 ——
        { "firearrow", new SkillFxPlan { ball = true, ballDelay = 0.24f, ballSpeed = 15f } },
        { "doom",      new SkillFxPlan { windup = 0.92f, impact = 1.15f,
                                        special = SkillFxPlan.SpecialKind.Burst, specialSpread = 7.5f } },
        { "chain",     new SkillFxPlan { impact = 0.20f } },
        { "starfall",  new SkillFxPlan { impact = 0.85f, cast2Delay = 0.65f, tiles = true, tileGap = 0.16f } },
        // —— 弓箭手 ——
        { "firebolt",  new SkillFxPlan { ball = true, ballDelay = 0.20f, ballSpeed = 14f, atImpact = true,
                                        special = SkillFxPlan.SpecialKind.Fall, tiles = true, tileGap = 0.12f } },
        { "arrowrain", new SkillFxPlan { impact = 0.35f,
                                        special = SkillFxPlan.SpecialKind.Fall, specialSpread = 2.1f } },
        { "dragon",    new SkillFxPlan { ball = true, ballDelay = 0.30f, ballSpeed = 12f } },
        { "storm",     new SkillFxPlan { impact = 0.20f } },
        // —— 牧师 ——
        { "holyarrow", new SkillFxPlan { ball = true, ballDelay = 0.18f, ballSpeed = 15f } },
        { "holy",      new SkillFxPlan { impact = 0.30f } },
        { "ray",       new SkillFxPlan { ball = true, ballDelay = 0.18f, ballSpeed = 16f } },
        { "libra",     new SkillFxPlan { impact = 0f, affected = true } },
    };

    public static SkillFxPlan Get(string key)
    {
        SkillFxPlan plan;
        return key != null && table.TryGetValue(key, out plan) ? plan : null;
    }
}
