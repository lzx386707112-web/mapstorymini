using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 职业成长规则层：逐条对齐源端，单机不校验服务端封包，只保留
// 「能不能转 / 转的时候发生什么」。
//
// 源端依据（gms-server/src/main/java/org/gms）：
//   Character.changeJob(Job)          —— SP/AP/背包格/HP·MP 成长
//   Character.resetStats()            —— 一转能力值重排（use_auto_assign_starters_ap=true）
//   Character.levelUpGainSp()         —— 新手升级不给 SP，一转起每级 +3
//   AbstractPlayerInteraction.canGetFirstJob / getFirstJobStatRequirement
//   scripts-zh-CN/npc/1022000.js 等   —— 各阶转职的等级、凭证、赠品、授技
//
// 与源端的已知偏差（有意为之，均记在 docs/job-system-port.md）：
//   * canGetFirstJob 在源端 auto 配点模式下恒真 → 这里能力值只做 UI 提示，不拦转职；
//   * 背包格源端每转 +4 格 × 4 个分页，本客户端只落装备分页（equipmentSlots）；
//   * 新手 SP 沿用旧公式 (等级-1, 封顶 6)，一转起改为存档累计字段。
// ————————————————————————————————————————————————————————————

public static class JobSystem
{
    /// <summary>当前职业 id（存档字段，旧 cls 字符串已迁移）。</summary>
    public static int Job => Profile.JobId;

    public static JobDef Def => JobDefs.Get(Job);

    public static string JobName => JobDefs.NameOf(Job);

    public static int Tier => JobDefs.TierOf(Job);

    // —— 源端 game_config 默认值（V1.7.0__create_game_config.sql）——
    public const bool UseAutoAssignStartersAp = true;   // use_auto_assign_starters_ap
    public const bool UseStartingAp4 = false;           // use_starting_ap_4
    public const int LevelUpSpGain = 3;                 // level_up_sp_gain

    // ————————————————————————————————————————
    // 转职校验
    // ————————————————————————————————————————

    /// <summary>能否转成 to。reason 为 null 表示可以。</summary>
    public static bool CanAdvance(int to, out string reason)
    {
        reason = null;
        var target = JobDefs.Get(to);
        if (target == null) { reason = "未知职业"; return false; }
        if (target.advance == null) { reason = "该职业无法直接转入"; return false; }
        if (!JobDefs.IsNext(Job, to)) { reason = "只能转往下一阶职业"; return false; }

        var rule = target.advance;
        if (Profile.level < rule.level)
        {
            reason = "等级不足 Lv." + rule.level + "（当前 Lv." + Profile.level + "）";
            return false;
        }
        if (rule.quest > 0 && !Profile.IsQuestCompleted(rule.quest))
        {
            reason = "尚未通过转职考验（任务 " + rule.quest + "）";
            return false;
        }
        if (rule.requireItem > 0 && Profile.ItemCount(rule.requireItem) <= 0)
        {
            reason = "缺少凭证 " + ItemNames.Of(rule.requireItem);
            return false;
        }
        // 源端 2020008.js：三转要求把 SP 花到只剩 (等级-70)*3+10 以内
        if (target.tier == 3 && Profile.Sp > (Profile.level - 70) * 3 + 10)
        {
            reason = "请先在技能窗用完多余的技能点";
            return false;
        }
        return true;
    }

    /// <summary>能力值提示（源端 auto 模式不校验，仅 NPC 对白里的要求）。</summary>
    public static string StatHint(int job)
    {
        var def = JobDefs.Get(job);
        if (def?.advance == null || def.advance.stat.Count == 0) return "";
        var parts = new List<string>();
        foreach (var kv in def.advance.stat)
            parts.Add(StatLabel(kv.Key) + " " + kv.Value);
        return string.Join(" / ", parts);
    }

    public static string StatLabel(string key)
    {
        switch (key)
        {
            case "str": return "力量";
            case "dex": return "敏捷";
            case "int": return "智力";
            case "luk": return "运气";
            default: return key;
        }
    }

    // ————————————————————————————————————————
    // 转职执行（Character.changeJob）
    // ————————————————————————————————————————

    /// <summary>执行转职。调用方需先 CanAdvance 确认，道具/任务扣减在此完成。</summary>
    public static bool Advance(int to)
    {
        if (!CanAdvance(to, out _)) return false;
        var rule = JobDefs.Get(to).advance;

        // 1) 凭证消耗 / 赠品（脚本 gainItem(±n)）
        if (rule.consumeItem > 0) Profile.RemoveItem(rule.consumeItem, 1);
        foreach (int item in rule.grant) Profile.AddItem(item, 1);

        // 2) 能力值重排（一转，Character.resetStats）
        if (rule.resetStats) ResetStats(to);

        // 3) SP / AP（Character.changeJob：一转 +1，二转三转 +1，四转 +3；AP 二转起 +5，骑士团 +7）
        Profile.AddSp(rule.sp);
        Profile.AddAp(rule.ap);

        // 4) HP·MP 成长（Character.changeJob 的 addhp/addmp 区间，含随机）
        JobChangeHpMp(to, out int hpMin, out int hpMax, out int mpMin, out int mpMax);
        Profile.AddBaseHpMp(Random.Range(hpMin, hpMax + 1), Random.Range(mpMin, mpMax + 1));

        // 5) 背包格 +4（源端 gainSlots(i, 4) × 4 个分页；本客户端只落装备分页）
        Profile.AddEquipSlots(4);

        // 6) 切换职业
        Profile.SetJob(to);

        // 7) 四转自动授技（源端 Character.setMasteries → changeSkillLevel(skill, 0, 10, -1)，
        //    即解锁到 masterLevel=10；这里直接给到同样的上限，省掉再手动点一遍）
        foreach (int sid in rule.teach) SkillProgress.SetLevel(sid, SkillBook.MasterCapOf(sid));
        return true;
    }

    /// <summary>
    /// 脚本原语 cm.changeJobById(jobId) —— 源端 AbstractPlayerInteraction.changeJobById
    /// 直接落到 Character.changeJob(Job)，**不做等级/凭证校验**（校验在脚本里已经做过）。
    /// 与 Advance 的区别：不消耗凭证、不发赠品、不调 resetStats（脚本各自调用）。
    /// </summary>
    public static bool ChangeJobById(int jobId)
    {
        if (!JobDefs.IsValid(jobId)) { Debug.LogWarning("[JobSystem] 未知职业 id " + jobId); return false; }
        if (jobId == Profile.JobId) return true;

        Profile.AddSp(SpForJobChange(jobId));
        Profile.AddAp(ApForJobChange(jobId));

        JobChangeHpMp(jobId, out int hpMin, out int hpMax, out int mpMin, out int mpMax);
        Profile.AddBaseHpMp(Random.Range(hpMin, hpMax + 1), Random.Range(mpMin, mpMax + 1));

        Profile.AddEquipSlots(4);
        Profile.SetJob(jobId);

        // 四转 setMasteries：核心技能解锁到 masterLevel（源端 changeSkillLevel(skill, 0, 10, -1)）
        var rule = JobDefs.Get(jobId)?.advance;
        if (rule != null)
            foreach (int sid in rule.teach) SkillProgress.SetLevel(sid, SkillBook.MasterCapOf(sid));
        return true;
    }

    /// <summary>源端 AbstractPlayerInteraction.canGetFirstJob(jobType)（:1350）。
    /// jobType：1 战士 / 2 法师 / 3 弓手 / 4 飞侠 / 5 海盗（和脚本里的常量一致）。</summary>
    public static bool CanGetFirstJob(int jobType)
    {
        if (UseAutoAssignStartersAp) return true;      // 源端默认 true → 恒真
        var a = Profile.Attributes;
        switch (jobType)
        {
            case 1: return a.str >= 35;
            case 2: return a.intelligence >= 20;
            case 3:
            case 4: return a.dex >= 25;
            case 5: return a.dex >= 20;
            default: return true;
        }
    }

    /// <summary>源端 getFirstJobStatRequirement(jobType)（:1376），对白里直接拼接的文案。</summary>
    public static string FirstJobStatRequirement(int jobType)
    {
        switch (jobType)
        {
            case 1: return "力量 " + 35;
            case 2: return "智力 " + 20;
            case 3:
            case 4: return "敏捷 " + 25;
            case 5: return "敏捷 " + 20;
            default: return "";
        }
    }

    /// <summary>Character.changeJob 的 HP/MP 加成区间，按新职业 id 逐行对齐。</summary>
    public static void JobChangeHpMp(int newJobId, out int hpMin, out int hpMax, out int mpMin, out int mpMax)
    {
        int job_ = newJobId % 1000;
        if (job_ == 100) { hpMin = 200; hpMax = 250; mpMin = 0; mpMax = 0; }          // 1st warrior
        else if (job_ == 200) { hpMin = 0; hpMax = 0; mpMin = 100; mpMax = 150; }     // 1st mage
        else if (job_ % 100 == 0) { hpMin = 100; hpMax = 150; mpMin = 25; mpMax = 50; }  // 1st others
        else if (job_ > 0 && job_ < 200) { hpMin = 300; hpMax = 350; mpMin = 0; mpMax = 0; }   // 2nd~4th warrior
        else if (job_ < 300) { hpMin = 0; hpMax = 0; mpMin = 450; mpMax = 500; }      // 2nd~4th mage
        else { hpMin = 300; hpMax = 350; mpMin = 150; mpMax = 200; }                  // 2nd~4th others
    }

    /// <summary>转职授予的 SP：一转~三转 +1，四转（id%10==2）+3。源端 use_enforce_job_sp_range 默认关，
    /// 不走 getChangedJobSp 上限表。</summary>
    public static int SpForJobChange(int newJobId) => newJobId % 10 == 2 ? 3 : 1;

    /// <summary>转职授予的 AP：一转 +0，二转起冒险家 +5 / 骑士团 +7。</summary>
    public static int ApForJobChange(int newJobId)
    {
        if (newJobId % 100 < 1) return UseStartingAp4 && newJobId % 1000 >= 1 ? 4 : 0;
        return newJobId >= 1000 ? 7 : 5;
    }

    // ————————————————————————————————————————
    // 一转能力值重排（Character.resetStats）
    // ————————————————————————————————————————

    /// <summary>按目标职业重排四维，剩余点数回 AP，并按等级重算 SP。
    /// AP 不够时源端只记日志、不改动，这里同样返回 false 不改动。</summary>
    public static bool ResetStats(int job)
    {
        var attrs = Profile.Attributes;
        int tap = Profile.ap + attrs.str + attrs.dex + attrs.intelligence + attrs.luk;
        int tstr = 4, tdex = 4, tint = 4, tluk = 4;
        int tsp = 1;

        switch (job)
        {
            case 100: case 1100: case 2100:   // 战士系一转
                tstr = 35;
                tsp += (Profile.level - 10) * 3;
                break;
            case 200: case 1200:              // 法师系一转
                tint = 20;
                tsp += (Profile.level - 8) * 3;
                break;
            case 300: case 1300:              // 弓手系一转
            case 400: case 1400:              // 飞侠系一转
                tdex = 25;
                tsp += (Profile.level - 10) * 3;
                break;
            case 500: case 1500:              // 海盗系一转
                tdex = 20;
                tsp += (Profile.level - 10) * 3;
                break;
        }

        tap -= tstr + tdex + tint + tluk;
        if (tap < 0)
        {
            Debug.LogWarning("[JobSystem] AP 不足，无法重排能力值");
            return false;
        }
        Profile.SetBaseStats(tstr, tdex, tint, tluk, tap, Mathf.Max(0, tsp));
        return true;
    }
}

/// <summary>道具中文名（String.wz 的 Etc/Consume/Eqp 名，缺失回退 id）。</summary>
public static class ItemNames
{
    static readonly Dictionary<int, string> cache = new Dictionary<int, string>();

    public static string Of(int id)
    {
        if (cache.Count == 0) Load();
        return cache.TryGetValue(id, out var n) ? n : id.ToString();
    }

    static void Load()
    {
        // 转职相关道具的固定中文名（String.wz/Etc.img、String.wz/Eqp.img/Weapon，
        // 由 tools/export_jobs.py 同源数据核对）。其余道具走 WZ 查询，失败回退 id。
        cache[4031008] = "一转教官的介绍信";
        cache[4031009] = "一转教官的介绍信";
        cache[4031010] = "一转教官的介绍信";
        cache[4031011] = "一转教官的介绍信";
        cache[4031012] = "英雄的证明";
        cache[4031013] = "黑珠";
        cache[4031057] = "三转试炼证明";
        cache[4031058] = "智慧项链";
        cache[4031059] = "智慧项链";
        cache[2280003] = "技能书";
    }
}
