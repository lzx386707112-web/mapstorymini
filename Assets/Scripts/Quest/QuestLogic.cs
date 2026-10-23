using System;
using System.Collections.Generic;
using UnityEngine;

// 任务规则层：逐条对齐原端 Quest.java / requirements/* / actions/*。
// 单机客户端不校验服务端封包，只保留「能不能接 / 能不能交 / 接交后发生什么」。
public static class QuestLogic
{
    static readonly System.Random rng = new System.Random();

    // 原端 Character.job → 数字 id。客户端职业是自定义名称，这里给出对应的冒险家分支 id。
    public static int JobId(string cls)
    {
        switch (cls)
        {
            case "warrior": return 100;
            case "knight": return 110;
            case "mage": return 200;
            case "archer": return 300;
            case "priest": return 230;
            default: return 0;         // beginner
        }
    }

    // 职业主键已经是 job id（存档字段）；JobId(cls) 只留给旧存档迁移路径。
    public static int PlayerJob => Profile.JobId;

    public static bool CanStart(QuestDef q, int npcId, out string reason)
    {
        int status = Profile.QuestStatus(q.id);
        if (status == PlayerProfileData.QuestStartedState) { reason = "任务已在进行中"; return false; }
        if (status == PlayerProfileData.QuestCompletedState && q.startReq.interval <= 0)
        {
            reason = "任务已完成";
            return false;
        }
        return Check(q.startReq, q.id, npcId, out reason);
    }

    public static bool CanComplete(QuestDef q, int npcId, out string reason)
    {
        if (Profile.QuestStatus(q.id) != PlayerProfileData.QuestStartedState) { reason = "任务未开始"; return false; }
        return Check(q.completeReq, q.id, npcId, out reason);
    }

    public static bool CanStart(QuestDef q, int npcId) { string _; return CanStart(q, npcId, out _); }
    public static bool CanComplete(QuestDef q, int npcId) { string _; return CanComplete(q, npcId, out _); }

    static bool Check(QuestRequirement r, int questId, int npcId, out string reason)
    {
        reason = null;
        if (r.completedQuests > Profile.CompletedQuests.Count) { reason = "已完成任务数量不足 " + r.completedQuests; return false; }
        if (r.monsterBookMin > 0) {
            var book = GameSession.InGame ? GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId }) : null;
            if (book == null || book.cards.Count < r.monsterBookMin) { reason = "怪物手册卡片种类不足 " + r.monsterBookMin; return false; }
        }
        // A pet in cash storage is not a summoned pet. Do not award pet medals for ownership alone.
        if (r.pets.Count > 0 || r.petTameness > 0) { reason = "需要召唤符合条件及亲密度的宠物"; return false; }
        if (r.endDate.Length == 10) {
            if (int.TryParse(r.endDate.Substring(0, 4), out int year) && int.TryParse(r.endDate.Substring(4, 2), out int month) &&
                int.TryParse(r.endDate.Substring(6, 2), out int day) && int.TryParse(r.endDate.Substring(8, 2), out int hour)) {
                // Source Calendar.set uses zero-based months and explicitly exempts historical dates.
                long end = new DateTimeOffset(new DateTime(year, 1, 1).AddMonths(month).AddDays(day - 1).AddHours(hour)).ToUnixTimeMilliseconds();
                if (end >= 1732027571809L && end < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { reason = "活动已结束"; return false; }
            }
        }
        if (r.level > 0 && Profile.level < r.level) { reason = "等级不足 Lv." + r.level; return false; }
        if (r.maxLevel > 0 && Profile.level > r.maxLevel) { reason = "等级过高"; return false; }

        if (r.jobs.Count > 0)
        {
            bool ok = false;
            for (int i = 0; i < r.jobs.Count; i++) if (r.jobs[i] == PlayerJob) { ok = true; break; }
            if (!ok) { reason = "职业不符"; return false; }
        }

        if (r.npc > 0 && npcId > 0 && r.npc != npcId) { reason = "需要找指定 NPC"; return false; }
        if (r.meso > 0 && Profile.gold < r.meso) { reason = "金币不足"; return false; }

        for (int i = 0; i < r.items.Count; i++)
        {
            var need = r.items[i];
            int have = Profile.ItemCount(need.item);
            // ItemRequirement.check：need<=0 表示「必须没有」
            if (need.count <= 0 ? have > 0 : have < need.count)
            {
                reason = "道具不足 " + need.item + " x" + need.count;
                return false;
            }
        }

        for (int i = 0; i < r.quests.Count; i++)
        {
            var need = r.quests[i];
            if (Profile.QuestStatus(need.Key) != need.Value)
            {
                reason = "前置任务 " + need.Key + " 状态不符";
                return false;
            }
        }

        for (int i = 0; i < r.mobs.Count; i++)
        {
            var need = r.mobs[i];
            if (Profile.QuestProgressInt(questId, need.item.ToString()) < need.count)
            {
                reason = "击杀进度不足 " + need.item + " (" + Profile.QuestProgressInt(questId, need.item.ToString()) + "/" + need.count + ")";
                return false;
            }
        }

        for (int i = 0; i < r.infoEx.Count; i++)
        {
            string expected = r.infoEx[i] ?? "";
            string actual = Profile.QuestProgress(questId, "info" + i, "");
            if (actual != expected) { reason = "任务进度条件未满足"; return false; }
        }

        if (r.fieldEnter > 0 && Profile.LastMapId != r.fieldEnter) { reason = "需要进入指定地图"; return false; }

        if (r.interval > 0 && Profile.IsQuestCompleted(questId))
        {
            long done = Profile.QuestCompletedTime(questId);
            if (done > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - done < (long)r.interval * 60000L)
            {
                reason = "任务冷却中";
                return false;
            }
        }
        return true;
    }

    // 击杀推进：对应服务端 Character 里的任务击杀计数。
    // 只累加"已接取且 Check.img/0/mob 里确实列了这个怪"的任务，key 用怪物 id（与
    // QuestProgressRequirement 读的是同一个键）；上限不截断，交任务时只比较 >=。
    public static void OnMobKilled(int mobId)
    {
        if (mobId <= 0) return;
        var started = Profile.StartedQuests;
        bool changed = false;
        for (int i = 0; i < started.Count; i++)
        {
            var q = QuestCatalog.Get(started[i]);
            if (q == null) continue;
            var mobs = q.startReq.mobs;
            bool needed = false;
            for (int k = 0; k < mobs.Count; k++)
                if (mobs[k].item == mobId) { needed = true; break; }
            if (!needed) continue;

            string key = mobId.ToString();
            Profile.SetQuestProgress(q.id, key, (Profile.QuestProgressInt(q.id, key) + 1).ToString(), false);
            changed = true;
        }
        if (changed) Profile.Save();
    }

    // 接任务：与 Quest.start 一致 —— 先校验动作，再全部执行，最后置状态。
    public static bool Start(QuestDef q, int npcId, bool applyActions)
    {
        if (applyActions) Apply(q.startAct, q.id);
        Profile.StartQuest(q.id);
        Debug.Log("[Quest] 接取 " + q.id + " " + q.name + " (NPC " + npcId + ")");
        return true;
    }

    public static bool Complete(QuestDef q, int npcId, bool applyActions)
    {
        if (applyActions) Apply(q.completeAct, q.id);
        Profile.CompleteQuest(q.id);
        QuestNotice.Push(q.name);
        Debug.Log("[Quest] 完成 " + q.id + " " + q.name + " (NPC " + npcId + ")");
        return true;
    }

    public static void Apply(QuestActions a, int questId)
    {
        if (a == null) return;
        if (a.exp > 0) Profile.GrantExperience(a.exp);
        if (a.money != 0) Profile.AddGold(a.money);

        // ItemAction：先扣负数条目，再按 prop 权重发奖励
        for (int i = 0; i < a.items.Count; i++)
            if (a.items[i].count < 0) Profile.RemoveItem(a.items[i].item, -a.items[i].count);

        int props = 0;
        for (int i = 0; i < a.items.Count; i++)
            if (a.items[i].count >= 0 && a.items[i].prop >= 0) props += a.items[i].prop;

        int roll = props > 0 ? rng.Next(props) : -1;
        int acc = 0;
        for (int i = 0; i < a.items.Count; i++)
        {
            var reward = a.items[i];
            if (reward.count <= 0) continue;
            if (reward.prop >= 0)
            {
                acc += reward.prop;
                if (acc <= roll) continue;
                acc = int.MinValue;          // 权重命中后其余带 prop 的条目不再发放
            }
            Profile.AddItem(reward.item, reward.count);
        }

        for (int i = 0; i < a.skills.Count; i++)
        {
            var s = a.skills[i];
            if (s.skill > 0 && s.level > 0) SkillProgress.SetLevel(s.skill, s.level);
        }

        if (a.nextQuest > 0) Profile.StartQuest(a.nextQuest);
    }
}

// 任务完成提示（原端 showSpecialEffect(9) + 提示语）
public static class QuestNotice
{
    public static string Text { get; private set; }
    static float until;

    public static void Push(string questName)
    {
        Text = "完成任务：" + questName;
        until = Time.unscaledTime + 2.5f;
    }

    public static string Active => Time.unscaledTime < until ? Text : null;
}
