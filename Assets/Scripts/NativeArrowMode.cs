using System;
using System.Collections.Generic;
using UnityEngine;

// 282「魔幻箭筒」= Quiver Cartridge 的箭矢模式切换。
//
// 证据链（全部来自 TMS 282 客户端，未推测）：
//   String/Skill.img/3141005  desc「使用特殊箭矢。#c再次使用技能#可變更欲使用的箭鏃。」
//                            h「魔法箭矢：將以 #u% 機率對敵人造成 #s% 傷害的魔法箭矢
//                              吸血箭矢：使用以 #w% 機率恢復 #t% 最大HP 的吸血箭矢」
//   Skill/314.img/skill/3141005/mode/0  16 帧金红（魔法箭）
//   Skill/314.img/skill/3141005/mode/1  16 帧蓝（吸血箭）
//   common{u=60,w=60,t=1.8,s=292+5*x}   与 h 文案的变量一一对应
//   3140006「魔幻箭筒VI」  common{damage=292+5*x, lt=(-500,-250), rb=(0,250)}
//                          ← damage 与 3141005.s 完全同源，它就是魔法箭的判定体
//   3140007/3140008  只有 common{maxLevel=30} 的空壳（魔法箭 / 吸血箭分支标记）
//   3140010/3140011  追加魔幻箭筒的两支箭，归 NativeQuiverBurst 管（见那边注释）
//
// 行为口径：
//   未开启 → 魔法箭（播 mode/0）→ 吸血箭（播 mode/1）→ 魔法箭 …
//   开启期间，直接攻击命中时按 u% / w% 概率追加一次对应效果。
//   模式是会话内状态（282 的切换技能没有 time 字段，不随时间过期）。
public static class NativeArrowMode
{
    public const int Toggle = 3141005;      // 魔幻箭筒VI（5 转，切换 + 提供数值）
    public const int MagicChild = 3140007;  // 魔幻箭筒VI(魔法箭)，invisible 空壳分支
    public const int DrainChild = 3140008;  // 魔幻箭筒VI(吸血箭)，invisible 空壳分支
    public const int MagicBody = 3140006;   // 魔幻箭筒VI 的攻击判定体（damage = 292+5*x）
    public const int MagicShotFx = 3140010; // 追加魔幻箭筒VI(魔法箭) 的表现源
    public const int DrainShotFx = 3140011; // 追加魔幻箭筒VI(吸血箭) 的表现源

    public const int Off = 0, Magic = 1, Drain = 2;

    static int mode;
    static double switchedAt = -99;
    static NativeSkillDefinition magicDef;
    static int magicDefPercent;

    public static int Mode => mode;
    public static bool Active => mode != Off;
    public static bool IsMagic => mode == Magic;
    public static string Label => mode == Magic ? "魔法箭镞" : mode == Drain ? "吸血箭镞" : "普通箭镞";
    // 状态栏用当前箭镞对应的技能图标（魔法箭 3140010 / 吸血箭 3140011）。
    public static int ActiveFx => mode == Drain ? DrainShotFx : MagicShotFx;
    public static bool IsToggle(int id) => id == Toggle;

    public static void Reset() { mode = Off; switchedAt = -99; }

    // 从存档恢复当前箭镞。挂在角色刷新的必经点（GameSession 重载 Character.growth 之后）：
    // CharService.Full 会把 character_growth 整个下发，换角色自然带回各自的模式；
    // 拿不到内存快照时（例如刚进游戏还没 Load 到 growth）再直接查一次库。
    public static void Sync()
    {
        int saved = Off;
        var character = GameSession.Character;
        if (character != null && character.growth != null) saved = character.growth.quiverMode;
        else
        {
            var db = GameServer.Local?.Db;
            if (db != null && GameSession.CharacterId > 0)
                saved = NativeArrowModeService.Get(db, GameSession.CharacterId);
        }
        mode = saved >= Off && saved <= Drain ? saved : Off;
        switchedAt = -99;
        NativeQuiverBurst.Reset();
    }

    // 切换后立刻落库：成长数据走的是「操作即保存」，不靠退出时统一写回。
    // 内存里的 growth 也要同步改，否则下一次 SaveToDb 会用旧值把库覆盖回去。
    static void Persist()
    {
        var character = GameSession.Character;
        if (character != null && character.growth != null) character.growth.quiverMode = mode;
        var db = GameServer.Local?.Db;
        if (db != null && GameSession.CharacterId > 0)
            NativeArrowModeService.Set(db, GameSession.CharacterId, mode);
    }

    // 当前等级的参数：u/w 是百分比概率，s 是魔法箭伤害%，t 是吸血的 % 最大HP。
    // 3141005 只有 common 没有 level，SkillBook.Level() 对它返回 null，
    // 必须走 Common() 的公式求值（s = "292+5*x"，x 是等级）。
    public static bool Stats(out int magicChance, out int magicDamage, out int drainChance, out float drainHeal)
    {
        magicChance = 0; magicDamage = 0; drainChance = 0; drainHeal = 0;
        int level = SkillProgress.LevelOf(Toggle);
        if (level <= 0) return false;
        magicChance = Mathf.RoundToInt(SkillBook.Common(Toggle, "u", level, 60));
        magicDamage = Mathf.RoundToInt(SkillBook.Common(Toggle, "s", level));
        drainChance = Mathf.RoundToInt(SkillBook.Common(Toggle, "w", level, 60));
        drainHeal = SkillBook.Common(Toggle, "t", level, 1.8f);
        return magicChance > 0 || drainChance > 0;
    }

    // 再次使用技能 → 切到下一支箭镞，并播对应 mode/N 的 16 帧切换特效。
    public static string Switch(PlayerCtrl player)
    {
        mode = mode == Magic ? Drain : Magic;
        switchedAt = Time.realtimeSinceStartupAsDouble;
        PlaySwitch(player);
        Persist();
        return "箭镞：" + (mode == Magic ? "魔法箭" : "吸血箭");
    }

    static void PlaySwitch(PlayerCtrl player)
    {
        if (player == null) return;
        int level = Mathf.Max(1, SkillProgress.LevelOf(Toggle));
        string key = NativeSkillVfx.Key(Toggle, level);
        string branch = "mode/" + (mode == Magic ? 0 : 1);
        // mode/0、mode/1 各是一段 16 帧的整段动画（Read 已按连续帧收成单 clip）。
        foreach (var clip in NativeSkillVfx.Parts(key, branch))
            SkillFx.PlayPart(key, clip.path, player.FeetPos, player.facing, 1, player.SortingOrder + 1, false);
        AudioManager.Play("Skill/" + Toggle + "/Use", 0.9f);
    }

    // 直接攻击命中后触发一次。返回是否真的有追加效果（供守卫/调试）。
    // 只在「远程武器 + 模式开启」时生效；每次命中只掷一次骰，不按目标数放大。
    public static bool OnHit(PlayerCtrl player, Component target, int direction)
    {
        if (!Active || player == null || target == null) return false;
        if (!Stats(out int magicChance, out int magicDamage, out int drainChance, out float drainHeal)) return false;
        bool magic = mode == Magic;
        bool fired;
        if (magic)
        {
            int percent = magicDamage > 0 ? magicDamage : MagicBowPercent();
            fired = magicChance > 0 && UnityEngine.Random.value * 100f < magicChance &&
                FireMagicArrow(player, target, direction, percent);
        }
        else
        {
            fired = drainChance > 0 && UnityEngine.Random.value * 100f < drainChance &&
                DrainLife(player, target, direction, drainHeal);
        }
        // 3141009 追加魔幻箭筒：箭镞真的触发时往「未使用的箭筒」装填一颗；
        // 装填达到 w2 会自动打出一发全弹爆发（QuiverFullBurst），那里会清空弹匣。
        if (fired) NativeQuiverBurst.Load(player, target, direction, magic);
        // 3141009 生效期间，每一次直接攻击命中都消耗一发装填并追加发射。
        NativeQuiverBurst.TryFire(player, target, direction, magic);
        return fired;
    }

    // 3140006 是魔法箭的判定体：damage = "292+5*x"。读不到时才退回 3141005 的 s。
    static int MagicBowPercent()
    {
        int level = Mathf.Max(1, SkillProgress.LevelOf(MagicBody));
        return Mathf.RoundToInt(SkillBook.Common(MagicBody, "damage", level));
    }

    // 魔法箭：走 3140006 的 damage（与 3141005.s 同源），表现取 3140010 的 hit。
    static bool FireMagicArrow(PlayerCtrl player, Component target, int direction, int damagePercent)
    {
        var definition = MagicDefinition(damagePercent);
        if (definition == null) return false;
        var enemy = target as EnemyUnit;
        var boss = target as BossUnit;
        var stats = enemy != null ? enemy.CombatStats : boss != null ? boss.CombatStats : null;
        if (stats == null) return false;
        var hit = player.RollAttack(stats, definition);
        if (enemy != null) enemy.TakeReplayHit(new[] { hit }, direction, false, false, 120, false, MagicShotFx);
        else if (boss != null) boss.TakeReplayHit(new[] { hit }, direction, false, 120, false, MagicShotFx);
        PlayShotFx(player, target, direction, MagicShotFx);
        return true;
    }

    // 吸血箭：按 t% 最大 HP 回复，不造成伤害；表现取 3140011 的 hit。
    static bool DrainLife(PlayerCtrl player, Component target, int direction, float healPercent)
    {
        int heal = Mathf.Max(1, Mathf.RoundToInt(player.hpMax * healPercent / 100f));
        float before = player.hp;
        player.hp = Mathf.Min(player.hpMax, player.hp + heal);
        int gained = Mathf.RoundToInt(player.hp - before);
        PlayShotFx(player, target, direction, DrainShotFx);
        if (gained > 0)
            DamageText.Show((Vector3)player.FeetPos + Vector3.up * (60f / Art.PPU), (long)gained, "blue");
        return true;
    }

    static void PlayShotFx(PlayerCtrl player, Component target, int direction, int fxId)
    {
        int level = Mathf.Max(1, SkillProgress.LevelOf(fxId));
        string key = NativeSkillVfx.Key(fxId, level);
        var clips = NativeSkillVfx.Parts(key, "hit");
        if (clips.Count == 0) return;
        var anchor = target != null
            ? new Vector3(target.transform.position.x, target.transform.position.y + 30f / Art.PPU, 0)
            : (Vector3)player.FeetPos;
        SkillFx.PlayPart(key, clips[UnityEngine.Random.Range(0, clips.Count)].path, anchor, direction, 1,
            player.SortingOrder + 2, false);
    }

    // 轻量构造：3140006 是 invisible 判定体（没有 level 节点、没有美术），
    // 伤害来自 common/damage 的公式求值，攻击框 lt/rb 是单体判定因此不用展开。
    static NativeSkillDefinition MagicDefinition(int damagePercent)
    {
        if (damagePercent <= 0) return null;
        if (magicDef != null && magicDefPercent == damagePercent) return magicDef;
        var skill = new Dictionary<string, object>
        {
            { "id", MagicBody }, { "kind", "Attack" }, { "magic", false }, { "summonInterval", 1f },
        };
        var level = new Dictionary<string, object>
        {
            { "level", 1 }, { "damage", damagePercent }, { "attackCount", 1 }, { "mobCount", 1 },
        };
        magicDef = new NativeSkillDefinition(skill, level);
        magicDefPercent = damagePercent;
        return magicDef;
    }

    // 切换后 0.35 秒内的输入忽略，避免一键两切（282 的切换层是 960ms，这里只挡双击）。
    public static bool CooledDown => Time.realtimeSinceStartupAsDouble - switchedAt > 0.35;
}
