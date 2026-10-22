using System;

// 等级 / 经验 / HP·MP 成长，规则逐条对应原服务端 Character.levelUp()。
//
// 原端依据（gms-server/src/main/java/org/gms/client/Character.java）：
//   addhp/addmp 区间    isBeginner 12..16 / 10..12
//                       warrior 24..28 / 4..6
//                       magician 10..14 / 22..24
//                       bowman、thief 20..24 / 14..16
//                       pirate 22..28 / 18..23
//   use_randomize_hpmp_gain=true → 非法师 MP 再加 INT/10，法师加 INT/20
//   level_up_ap_gain=5           → 每级 5 AP（新手前10次升级自动分配，之后累计）
//   use_level_up_protect=true    → 一次经验结算最多升 1 级（防止连升）
//   getMaxClassLevel()=255       → 等级上限（V2.1.50 把 mxj_max_level 改成 255）
//   升满级后经验封顶 99%（getMaxLevelExpLimit），避免客户端把终止经验值画崩
//
// 本文件不碰 Unity，方便用编辑器自检在无 Play 的情况下跑完整条升级链路。
public enum JobBranch { Beginner, Warrior, Magician, Bowman, Thief, Pirate, Aran }

public static class LevelSystem
{
    public const int ApPerLevel = 5;      // level_up_ap_gain
    public const int SpPerLevel = 3;      // level_up_sp_gain（本地暂不消费）
    public const bool LevelUpProtect = true;
    public const bool RandomizeHpMpGain = true;

    // 本项目五个职业 → 原端职业分支。原端没有“骑士/牧师”这两个独立分支，
    // 骑士属战士系、牧师属法师系，这里按技能/武器归属映射，不另造成长曲线。
    public static JobBranch BranchOf(string cls)
    {
        switch (cls)
        {
            case "knight":
            case "warrior": return JobBranch.Warrior;
            case "mage":
            case "priest": return JobBranch.Magician;
            case "archer": return JobBranch.Bowman;
            default: return JobBranch.Beginner;
        }
    }

    // 与 Character.levelUp 的 Randomizer.rand(min, max) 一致：上下界都包含。
    public static void HpMpRange(JobBranch branch, out int hpMin, out int hpMax, out int mpMin, out int mpMax)
    {
        switch (branch)
        {
            case JobBranch.Warrior:   hpMin = 24; hpMax = 28; mpMin = 4;  mpMax = 6;  return;
            case JobBranch.Magician:  hpMin = 10; hpMax = 14; mpMin = 22; mpMax = 24; return;
            case JobBranch.Bowman:    hpMin = 20; hpMax = 24; mpMin = 14; mpMax = 16; return;
            case JobBranch.Thief:     hpMin = 20; hpMax = 24; mpMin = 14; mpMax = 16; return;
            case JobBranch.Pirate:    hpMin = 22; hpMax = 28; mpMin = 18; mpMax = 23; return;
            case JobBranch.Aran:      hpMin = 44; hpMax = 48; mpMin = 4;  mpMax = 8;  return;
            default:                  hpMin = 12; hpMax = 16; mpMin = 10; mpMax = 12; return;
        }
    }

    // 返回本次结算实际升了几级。protect=true 时最多 1 级（服务端默认配置）。
    public static int AddExperience(PlayerProfileData d, long amount, bool protect)
    {
        if (d == null || amount <= 0) return 0;
        EnsureBaseline(d);

        int maxLevel = ExpTable.MaxLevel;
        long total = Math.Max(0, d.experience) + amount;
        if (total > int.MaxValue) total = int.MaxValue;   // 原端 exp 是 int

        int levels = 0;
        while (d.level < maxLevel)
        {
            int need = Math.Max(1, ExpTable.Needed(d.level));
            if (total < need) break;
            total -= need;
            LevelUp(d);
            levels++;
            if (protect) break;
        }

        if (d.level >= maxLevel)
        {
            // 满级保留 99%，与原端 getMaxLevelExpLimit 一致
            long cap = (long)Math.Max(1, ExpTable.Needed(d.level)) * 99 / 100;
            if (total > cap) total = cap;
        }

        d.experience = total;
        return levels;
    }

    // 老存档的 experience 是“累计值”，可能已经远超当前等级需求。
    // 读档时把多余经验按原曲线补成等级，避免经验条长期停在 100% 以上。
    public static int Reconcile(PlayerProfileData d)
    {
        if (d == null) return 0;
        EnsureBaseline(d);

        int levels = 0;
        while (d.level < ExpTable.MaxLevel && d.experience >= Math.Max(1, ExpTable.Needed(d.level)))
        {
            d.experience -= Math.Max(1, ExpTable.Needed(d.level));
            LevelUp(d);
            levels++;
        }

        // 与 AddExperience 相同的满级封顶
        if (d.level >= ExpTable.MaxLevel)
        {
            long cap = (long)Math.Max(1, ExpTable.Needed(d.level)) * 99 / 100;
            if (d.experience > cap) d.experience = cap;
        }
        return levels;
    }

    // 直接抬到目标等级（调试 / 验收用）：按 LevelUp 的成长区间逐级补，
    // 和真练上去的数值同分布，不用伪造的固定 HP/MP。
    public static int SetLevel(PlayerProfileData d, int target)
    {
        if (d == null) return 0;
        EnsureBaseline(d);
        target = Math.Min(ExpTable.MaxLevel, Math.Max(1, target));
        int gained = 0;
        while (d.level < target) { LevelUp(d); gained++; }
        d.attributes.level = d.level;
        d.attributes.maxHp = d.baseMaxHp;
        d.attributes.maxMp = d.baseMaxMp;
        return gained;
    }

    // 只在基础值缺失时补默认，绝不覆盖已经练出来的数值。
    public static void EnsureBaseline(PlayerProfileData d)
    {
        if (d.attributes == null) d.attributes = new CharacterAttributes();
        if (d.level <= 0) d.level = 1;
        if (d.baseMaxHp <= 0) d.baseMaxHp = Math.Max(50, d.attributes.maxHp);
        if (d.baseMaxMp <= 0) d.baseMaxMp = Math.Max(5, d.attributes.maxMp);
        if (d.experience < 0) d.experience = 0;
        d.attributes.level = d.level;
        d.attributes.maxHp = d.baseMaxHp;
        d.attributes.maxMp = d.baseMaxMp;
    }

    // Migrate only unspent AP from the old implementation, preserving earned stats.
    public static bool MigrateNoviceAp(PlayerProfileData d)
    {
        if (d.noviceApMigrated) return false;
        d.noviceApMigrated = true;
        if (d.cls != "beginner" || d.ap <= 0) return true;
        int remaining = Math.Min(d.ap, Math.Min(10, Math.Max(0, d.level - 1)) * 5);
        for (int oldLevel = 1; oldLevel < Math.Min(d.level, 11) && remaining > 0; oldLevel++)
        {
            int strength = Math.Min(remaining, oldLevel < 6 ? 5 : 4);
            d.attributes.str += strength;
            d.ap -= strength; remaining -= strength;
            if (oldLevel >= 6 && remaining > 0) { d.attributes.dex++; d.ap--; remaining--; }
        }
        return true;
    }

    static void LevelUp(PlayerProfileData d)
    {
        var branch = BranchOf(d.cls);
        HpMpRange(branch, out int hpMin, out int hpMax, out int mpMin, out int mpMax);

        int addHp = UnityEngine.Random.Range(hpMin, hpMax + 1);
        int addMp = UnityEngine.Random.Range(mpMin, mpMax + 1);

        if (RandomizeHpMpGain)
        {
            // Character.levelUp：use_randomize_hpmp_gain=true 时法师 INT/20，其余 INT/10
            int intel = d.attributes != null ? d.attributes.intelligence : 0;
            addMp += branch == JobBranch.Magician ? intel / 20 : intel / 10;
        }

        d.baseMaxHp = Math.Max(1, d.baseMaxHp + addHp);
        d.baseMaxMp = Math.Max(1, d.baseMaxMp + addMp);
        d.level = Math.Min(ExpTable.MaxLevel, d.level + 1);
        // Character.levelUp checks the OLD level before incrementing it.
        if (branch == JobBranch.Beginner && d.level - 1 < 11)
        {
            d.attributes.str += d.level - 1 < 6 ? 5 : 4;
            if (d.level - 1 >= 6) d.attributes.dex++;
        }
        else d.ap = Math.Max(0, d.ap) + ApPerLevel;

        d.attributes.level = d.level;
        d.attributes.maxHp = d.baseMaxHp;
        d.attributes.maxMp = d.baseMaxMp;
    }
}
