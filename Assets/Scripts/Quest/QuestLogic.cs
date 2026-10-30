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

    // Character.needQuestItem + Quest.get{Start,Complete}ItemAmountNeeded.
    public static bool NeedQuestItem(int questId,int itemId)
    {
        if (Server.Field.MapItemPickup.NeedQuestItemUnrestricted(questId)) return true;
        int status=Profile.QuestStatus(questId);
        if (status!=0 && status!=1) return false;
        var quest=QuestCatalog.Get(questId);
        if (quest==null) return false;
        var items=status==0 ? quest.startReq.items : quest.completeReq.items;
        foreach (var row in items)
            if (row.item==itemId) return Profile.ItemCount(itemId)<row.count;
        return status==1;
    }

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

    // NPC 头顶图标与 NPC 任务菜单都用这一套严格口径 —— 已核对原端客户端 CNpc::Update
    // （BeiDou.exe 6D1779..6D1B10）：它把任务逐个丢给 721163，721163 除了等级上下限/职业/
    // npc/起止日期，还会在 0x721487 起遍历 Check.img/0/item 并统计背包 + 已装备数量
    // （0x4282f7 取格子、0x4e7cd0 查已装备、0x5ca785 判装备 id）。源端客户端与服务端
    // Quest.canStart 的口径是同一套，不存在「客户端只看等级」的捷径。
    public static bool InProgressAt(QuestDef q, int npcId) =>
        q.completeReq.npc == npcId && Profile.QuestStatus(q.id) == PlayerProfileData.QuestStartedState;

    // Quest/Say.img/<id>/1/stop uses the original requirement names.
    public static string IncompleteStop(QuestDef q)
    {
        foreach (var need in q.completeReq.items)
        {
            int have = Profile.ItemCount(need.item);
            if (need.count <= 0 ? have > 0 : have < need.count) return "item";
        }
        foreach (var need in q.completeReq.mobs)
            if (Profile.QuestProgressInt(q.id, need.item.ToString()) < need.count) return "mob";
        return "";
    }

    // BeiDou.exe 7213E1..721407: the notification client checks both date bounds.
    // The server deliberately exempts old event end dates; that request validation
    // must not make expired events appear in a newly-created character's bulb.
    public static bool NotificationDateAvailable(QuestRequirement requirement, DateTime now)
    {
        return (!TryNotificationDate(requirement.startDate, out var start) || now >= start) &&
               (!TryNotificationDate(requirement.endDate, out var end) || now <= end);
    }

    static bool TryNotificationDate(string text, out DateTime date)
        => DateTime.TryParseExact(text, "yyyyMMddHH", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);

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
    // Quest.getMobAmountNeeded reads Check.img/1/mob; QuestStatus.progress caps at that count.
    public static void OnMobKilled(int mobId)
    {
        if (mobId <= 0) return;
        var started = Profile.StartedQuests;
        bool changed = false;
        for (int i = 0; i < started.Count; i++)
        {
            var q = QuestCatalog.Get(started[i]);
            if (q == null) continue;
            var mobs = q.completeReq.mobs;
            int needed = 0;
            for (int k = 0; k < mobs.Count; k++)
                if (mobs[k].item == mobId) { needed = mobs[k].count; break; }

            string key = mobId.ToString();
            int current = Profile.QuestProgressInt(q.id, key);
            if (current >= needed) continue;
            bool wasReady = CanComplete(q, 0);
            Profile.SetQuestProgress(q.id, key, (current + 1).ToString(), false);
            QuestAlarm.Progress(q.id);
            if (!wasReady && CanComplete(q, 0)) QuestNotice.Ready(q.id);
            changed = true;
        }
        if (changed) { Profile.Save(); NpcField.InvalidateMarkers(); }
    }

    // 接任务：与 Quest.start 一致 —— 先校验动作，再全部执行，最后置状态。
    public static bool Start(QuestDef q, int npcId, bool applyActions)
    {
        if (q == null) return false;
        if (applyActions) Apply(q.startAct, q.id);
        Profile.StartQuest(q.id);
        Debug.Log("[Quest] 接取 " + q.id + " " + q.name + " (NPC " + npcId + ")");
        return true;
    }

    public static bool Complete(QuestDef q, int npcId, bool applyActions)
    {
        if (q == null) return false;
        if (applyActions) Apply(q.completeAct, q.id);
        Profile.CompleteQuest(q.id);
        QuestAlarm.Remove(q.id);
        QuestNotice.RemoveReady(q.id);
        QuestNotice.Push(q.name);
        Debug.Log("[Quest] 完成 " + q.id + " " + q.name + " (NPC " + npcId + ")");
        return true;
    }

    // —— Act/1/nextQuest：交完任务后客户端把后继任务的接取对话接在同一个 NPC 上 ——
    // Quest.complete（Quest.java:336）跑完 completeActs 后，`if (!hasNextQuestAction()) announceUpdateQuest(INFO)`：
    // 有后继任务时不发 INFO 刷新，因为 NextQuestAction.run（NextQuestAction.java:48）已经发了
    // PacketCreator.updateQuestFinish(questId, npc, nextQuest)，由客户端当场续开下一条。
    // 续开的还是「接取对话」，所以 Act/0 的 item 动作一定会跑到——不能像以前那样只改状态，
    // 否则 1034「好吃的蘑菇棒棒糖」就拿不到妮娜在 Act/0 发的 10 块蘑菇棒棒糖（4031792），
    // 交付条件（Check/1/item）永远凑不齐。
    public static QuestDef ChainedNext(QuestActions act, int npcId)
    {
        if (act == null || act.nextQuest <= 0) return null;
        var next = QuestCatalog.Get(act.nextQuest);
        if (next == null) return null;
        if (next.startReq.npc > 0 && next.startReq.npc != npcId) return null;   // 同一个 NPC 才续
        string reason;
        return CanStart(next, npcId, out reason) ? next : null;                  // 对应 Quest.start 的 canStart
    }

    // Quest.restoreLostItem（Quest.java:632）→ ItemAction.restoreLostItem（ItemAction.java:308）：
    // 任务进行中、Act/0 发的任务道具已经不在身上时，按 Check/1 需求数量补足缺口（只补差额，不多给）。
    // 原端由客户端发 QuestActionHandler case 0（Restore lost item）触发；本工程还没有任务窗的「找回」按钮，
    // 所以在交付对话开始时补一次，避免任务永久卡死（1034 少了妮娜那 10 块蘑菇棒棒糖就是这种情况）。
    public static bool RestoreLostItems(QuestDef q)
    {
        if (q == null || Profile.QuestStatus(q.id) != PlayerProfileData.QuestStartedState) return false;
        bool restored = false;
        for (int i = 0; i < q.completeReq.items.Count; i++)
        {
            var need = q.completeReq.items[i];
            if (need.count <= 0) continue;                       // 「必须没有」那类条件不补
            int have = Profile.ItemCount(need.item);
            if (have >= need.count) continue;
            bool fromStart = false;
            for (int k = 0; k < q.startAct.items.Count; k++)
                if (q.startAct.items[k].item == need.item && q.startAct.items[k].count > 0) { fromStart = true; break; }
            if (!fromStart) continue;                            // 只有「接取时自己发过」的道具才补
            Profile.AddItem(need.item, need.count - have);
            Debug.Log("[Quest] 补发任务道具 " + q.id + " 道具 " + need.item + " x" + (need.count - have));
            restored = true;
        }
        return restored;
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
        // nextQuest 不在动作层直接改状态：原端是发 updateQuestFinish 让客户端续开接取对话，
        // 由 NpcSession.BeginDataQuest 收尾时用 ChainedNext 接上，接取仍走完整 Start。
    }
}

// 任务完成提示（原端 showSpecialEffect(9) + 提示语）
public static class QuestNotice
{
    sealed class ReadyEntry { public int id; public float time; }
    static readonly List<ReadyEntry> ready = new List<ReadyEntry>();
    static GUIStyle readyStyle;
    public static bool HasReady(int id) => ready.Exists(entry => entry.id == id && Time.unscaledTime - entry.time < 31);

    // FadeYesNo type7 (523408): target requirements met, before NPC turn-in.
    public static void Ready(int id)
    {
        RemoveReady(id);
        ready.Add(new ReadyEntry { id = id, time = Time.unscaledTime });
    }
    public static void RemoveReady(int id) => ready.RemoveAll(entry => entry.id == id);

    public static void DrawReady()
    {
        ready.RemoveAll(entry => Time.unscaledTime - entry.time >= 31 || !Profile.IsQuestStarted(entry.id));
        if (ready.Count == 0) return;
        float scale = BottomBar.S;
        if (readyStyle == null) readyStyle = new GUIStyle { font = UiKit.Font, clipping = TextClipping.Clip };
        readyStyle.fontSize = Mathf.RoundToInt(12 * scale);
        readyStyle.normal.textColor = Color.white;
        for (int i = 0; i < ready.Count; i++)
        {
            var entry = ready[i];
            float age = Time.unscaledTime - entry.time;
            var oldColor = GUI.color;
            GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, oldColor.a * Mathf.Clamp01(age) * Mathf.Clamp01(31 - age));
            // backgrnd4 tail tip is (140,43); point it at the memo slot left of EquipKey.
            var notifyBox = BottomBar.NotifyBoxRect();
            QuestUi.Begin(new Vector2(notifyBox.x + (33 - 140) * scale,
                notifyBox.y - (43 + (ready.Count - i) * 5) * scale), scale);
            QuestUi.Image("FadeYesNo/backgrnd4", 0, 0);
            QuestUi.Image("FadeYesNo/icon7", 6, (37 - 12) / 2);
            GUI.Label(QuestUi.Rect(27, 7, 100, 16), "任务完成！", readyStyle);
            GUI.Label(QuestUi.Rect(27, 20, 100, 16), QuestUi.Fit(QuestCatalog.Get(entry.id)?.name ?? "", 100), readyStyle);
            bool accept = QuestUi.Button("UI/UIWindow.img/FadeYesNo/BtOK", 136, 7);
            bool dismiss = QuestUi.Button("UI/UIWindow.img/FadeYesNo/BtCancel", 136, 20);
            GUI.color = oldColor;
            if (accept || dismiss)
            {
                if (accept) QuestWindow.Select(entry.id);
                ready.RemoveAt(i);
                break;
            }
        }
    }

    public static string Text { get; private set; }
    static float until;

    public static void Push(string questName)
    {
        Text = "完成任务：" + questName;
        until = Time.unscaledTime + 2.5f;
        ShowCompleteEffect();
    }

    // CUser effect 9 (93806E..938111): player-origin QuestClear, then Game/QuestClear.
    public static void ShowCompleteEffect()
    {
        NativeImgAnimation.ShowInfo("Effect/BasicEff.img/QuestClear");
        AudioManager.Play("Game/QuestClear", 1f, false);
    }

    public static string Active => Time.unscaledTime < until ? Text : null;
}
