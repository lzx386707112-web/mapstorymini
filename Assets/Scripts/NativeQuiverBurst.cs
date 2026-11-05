using System;
using System.Collections.Generic;
using UnityEngine;

// 282「追加魔幻箭筒」= Extra Quiver Cartridge 的装填 / 全弹发射。
//
// 证据链（全部来自 TMS 282 客户端 String/Skill.img 与 Skill/314.img）：
//   3141009 追加魔幻箭筒   desc「擴充魔幻箭筒，並裝填特殊箭簇。使用魔幻箭筒的箭矢時，
//                              在目前未使用的魔幻箭筒中額外裝填特殊箭簇。
//                              使用追加魔幻箭筒時，若直接攻擊的技能命中，則追加發射裝填的特殊箭簇。
//                              在自動使用模式中，當箭筒內裝填的箭簇超過一定數量時，則自動使用技能。」
//                         h   「消耗MP#mpCon，在#time秒內追加使用特殊箭頭，箭頭耗盡時自動結束
//                              #c魔法箭#：以#s%的傷害攻擊 / #c吸血箭#：以#dummyStr%的傷害攻擊
//                              每個魔幻箭筒最多可裝填#w2個
//                              自動使用模式下，當各彈匣裝填的箭總合達到#w個以上時自動發動額外魔幻箭筒
//                              冷卻時間#cooltime秒」
//                         common{mpCon=30+3*x, time=30, cooltime=30,
//                                s=150+3*x, dummyStr=1350+52*x, w=50, w2=50}
//                         skillList=[3100001, 3120008, 95001000, 95001016, 3140010, 3140011]
//                                 ↑ 最后两项就是追加发射的判定体，这是归属的铁证
//   3140010 追加魔幻箭筒VI(魔法箭)  common{damage=150+3*x}   ← 与 3141009.s 完全同源
//   3140011 追加魔幻箭筒VI(吸血箭)  common{damage=1350+52*x} ← 与 3141009.dummyStr 完全同源
//   3140010/3140011 的判定框 lt=(-700,-350) rb=(100,350)，比 3141005 的 (-500,-250)-(0,250) 更大。
//
// 行为口径：
//   装填：3141005 的箭镞真正触发时 +1，上限 w2。装的是「目前未使用的箭筒」，
//         所以 3141009 没开的时候照样累积（这一条把「+1 装填 / -1 发射」的净零死循环解开了）。
//   发射：3141009 生效期间，直接攻击命中就消耗 1 发，追加一次 3140010 / 3140011 的伤害。
//   爆发：装填达到 w2 时自动发动一次 QuiverFullBurst，把这一匣整个打出去并清空。
//         EXE 里 QuiverFullBurst 落在「攻击单元名」字符串表（OrbitalExplosion / WeaponVarietyFinale
//         / NAThanatosDescent 同一段），所以它是攻击而不是 buff 状态。
public static class NativeQuiverBurst
{
    public const int Skill = 3141009;       // 追加魔幻箭筒（装填 + 开关）
    public const int MagicShot = 3140010;   // 追加魔幻箭筒VI(魔法箭) 150+3*x
    public const int DrainShot = 3140011;   // 追加魔幻箭筒VI(吸血箭) 1350+52*x

    const int FallbackCapacity = 50;        // common/w2 读不到时的源端值
    const int FallbackThreshold = 50;       // common/w 读不到时的源端值

    static double until = -99;
    static int loads;
    static NativeSkillDefinition magicDef, drainDef;
    static int magicPercent, drainPercent;

    public static bool IsSkill(int id) => id == Skill;

    // buff 到期不清空装填：源端装的是「目前未使用的箭筒」，不是临时子弹。
    public static bool Active => Time.realtimeSinceStartupAsDouble < until;
    public static int Loads => loads;
    public static float Remaining => Mathf.Max(0f, (float)(until - Time.realtimeSinceStartupAsDouble));

    static int LevelOf(int id, int fallback)
    {
        int level = SkillProgress.LevelOf(id);
        return level > 0 ? level : Mathf.Max(1, fallback);
    }

    public static int Capacity
    {
        get
        {
            int level = LevelOf(Skill, 1);
            return Mathf.Max(1, Mathf.RoundToInt(SkillBook.Common(Skill, "w2", level, FallbackCapacity)));
        }
    }

    public static int Threshold
    {
        get
        {
            int level = LevelOf(Skill, 1);
            return Mathf.Max(1, Mathf.RoundToInt(SkillBook.Common(Skill, "w", level, FallbackThreshold)));
        }
    }

    public static void Reset() { until = -99; loads = 0; }

    // 施放 3141009：刷新持续时间。装填数保留（换关卡不清弹匣）。
    public static void Activate(int level)
    {
        float seconds = SkillBook.Common(Skill, "time", Mathf.Max(1, level), 30f);
        until = Time.realtimeSinceStartupAsDouble + Mathf.Max(1f, seconds);
    }

    // 装填：3141005 的箭镞真正触发时调用。
    // 没学 3141009 就没有「额外箭筒」可装，直接跳过。
    // 返回是否刚好装满并打出了一发全弹爆发。
    public static bool Load(PlayerCtrl player, Component target, int direction, bool magic)
    {
        if (SkillProgress.LevelOf(Skill) <= 0) return false;
        int capacity = Capacity;
        if (loads < capacity) loads++;
        if (loads < Threshold) return false;
        return FullBurst(player, target, direction, magic);
    }

    // 发射：3141009 生效期间，直接攻击命中时消耗一发装填。
    public static bool TryFire(PlayerCtrl player, Component target, int direction, bool magic)
    {
        if (!Active || loads <= 0 || player == null || target == null) return false;
        loads--;
        return Shoot(player, target, direction, magic, false);
    }

    // 全弹发射：把整匣一次性打出去。源端没有独立的 QuiverFullBurst 技能数据，
    // 所以伤害取当前箭镞的单发数值，范围取 3140010/3140011 的判定框。
    static bool FullBurst(PlayerCtrl player, Component target, int direction, bool magic)
    {
        int shots = loads;
        loads = 0;
        if (shots <= 0) return false;
        var definition = Definition(magic);
        if (definition == null) return false;
        var feet = (Vector3)player.FeetPos;
        int hitCount = 0;
        foreach (var enemy in EnemyUnit.All)
        {
            if (enemy == null || enemy.dead || !InBurst(feet, direction, enemy)) continue;
            if (Settle(player, enemy, null, definition, direction, magic)) hitCount++;
        }
        foreach (var boss in UnityEngine.Object.FindObjectsOfType<BossUnit>())
        {
            if (boss == null || boss.dead || !InBurst(feet, direction, boss)) continue;
            if (Settle(player, null, boss, definition, direction, magic)) hitCount++;
        }
        if (hitCount == 0) return false;
        PlayBurst(player, direction, magic);
        return true;
    }

    static bool InBurst(Vector3 feet, int direction, Component target)
    {
        int shot = MagicShot;
        var lt = SkillBook.CommonVector(shot, "lt", new Vector2(-700f, -350f));
        var rb = SkillBook.CommonVector(shot, "rb", new Vector2(100f, 350f));
        // WZ：身前是负 x、向上是负 y；世界坐标按朝向镜像。
        float near = feet.x + rb.x / Art.PPU * direction;
        float far = feet.x + lt.x / Art.PPU * direction;
        var box = Rect.MinMaxRect(Mathf.Min(near, far), feet.y - rb.y / Art.PPU,
            Mathf.Max(near, far), feet.y - lt.y / Art.PPU);
        var enemy = target as EnemyUnit;
        Rect bounds = enemy != null && enemy.SelectionBounds.HasValue
            ? enemy.SelectionBounds.Value
            : new Rect(target.transform.position.x, target.transform.position.y, 0, 0);
        return box.Overlaps(bounds);
    }

    static bool Shoot(PlayerCtrl player, Component target, int direction, bool magic, bool burst)
    {
        var definition = Definition(magic);
        if (definition == null) return false;
        var enemy = target as EnemyUnit;
        var boss = target as BossUnit;
        if (!Settle(player, enemy, boss, definition, direction, magic)) return false;
        PlayHit(player, target, direction, magic);
        return true;
    }

    static bool Settle(PlayerCtrl player, EnemyUnit enemy, BossUnit boss, NativeSkillDefinition definition,
        int direction, bool magic)
    {
        var stats = enemy != null ? enemy.CombatStats : boss != null ? boss.CombatStats : null;
        if (stats == null) return false;
        int shot = magic ? MagicShot : DrainShot;
        var hit = player.RollAttack(stats, definition);
        if (enemy != null) enemy.TakeReplayHit(new[] { hit }, direction, false, false, 120, false, shot);
        else boss.TakeReplayHit(new[] { hit }, direction, false, 120, false, shot);
        return true;
    }

    // 单发表现取 hit（6 帧，带 0..5 的变体壳）；全弹发射取 effect（25 帧）。
    static void PlayHit(PlayerCtrl player, Component target, int direction, bool magic)
    {
        int shot = magic ? MagicShot : DrainShot;
        int level = LevelOf(shot, Mathf.Max(1, SkillProgress.LevelOf(Skill)));
        string key = NativeSkillVfx.Key(shot, level);
        var clips = NativeSkillVfx.Parts(key, "hit");
        if (clips.Count == 0) return;
        var anchor = target != null
            ? new Vector3(target.transform.position.x, target.transform.position.y + 30f / Art.PPU, 0)
            : (Vector3)player.FeetPos;
        SkillFx.PlayPart(key, clips[UnityEngine.Random.Range(0, clips.Count)].path, anchor, direction, 1,
            player.SortingOrder + 2, false);
    }

    static void PlayBurst(PlayerCtrl player, int direction, bool magic)
    {
        int shot = magic ? MagicShot : DrainShot;
        int level = LevelOf(shot, Mathf.Max(1, SkillProgress.LevelOf(Skill)));
        string key = NativeSkillVfx.Key(shot, level);
        var clips = NativeSkillVfx.Parts(key, "effect");
        if (clips.Count == 0) return;
        // 爆发中心取 3140010/3140011 判定框的中点（身前约 300px）。
        var center = (Vector3)player.FeetPos + Vector3.right * (300f / Art.PPU * direction);
        SkillFx.PlayPart(key, clips[0].path, center, direction, 1, player.SortingOrder + 2, false);
        AudioManager.Play("Skill/" + Skill + "/Use", 0.9f);
    }

    // 3140010 / 3140011 只有 common 没有 level 节点，伤害必须走公式求值
    // （damage = "150+3*x" / "1350+52*x"，x 是等级）。
    static NativeSkillDefinition Definition(bool magic)
    {
        int shot = magic ? MagicShot : DrainShot;
        int level = LevelOf(shot, Mathf.Max(1, SkillProgress.LevelOf(Skill)));
        int percent = Mathf.RoundToInt(SkillBook.Common(shot, "damage", level));
        if (percent <= 0) return null;
        if (magic && magicDef != null && magicPercent == percent) return magicDef;
        if (!magic && drainDef != null && drainPercent == percent) return drainDef;
        var skill = new Dictionary<string, object>
        {
            { "id", shot }, { "kind", "Attack" }, { "magic", false }, { "summonInterval", 1f },
        };
        var row = new Dictionary<string, object>
        {
            { "level", 1 }, { "damage", percent }, { "attackCount", 1 }, { "mobCount", 1 },
        };
        var definition = new NativeSkillDefinition(skill, row);
        if (magic) { magicDef = definition; magicPercent = percent; }
        else { drainDef = definition; drainPercent = percent; }
        return definition;
    }
}
