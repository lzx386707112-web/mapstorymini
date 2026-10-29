using System;
using System.Collections.Generic;
using UnityEngine;

// 2026-10-28 用户明确授权按截图重建；这些是本地ID，并非声称源端任务号。
public static class ScreenshotTrainingQuests
{
    [Serializable] public sealed class Records { public Record[] quests; }
    [Serializable] public sealed class Record
    {
        public int id, npc, mob, count, maxLevel, exp;
        public string name, available, progress, completed;
    }

    public static void Register(Dictionary<int, QuestDef> cache, List<int> ids, Dictionary<int, List<int>> byNpc)
    {
        var asset = Resources.Load<TextAsset>("Quests/screenshot_training");
        if (asset == null) return;
        var records = JsonUtility.FromJson<Records>(asset.text);
        foreach (var r in records.quests)
        {
            if (ids.Contains(r.id)) throw new InvalidOperationException("截图任务ID与WZ冲突: " + r.id);
            var q = new QuestDef { id = r.id, name = r.name, area = 20,
                info = new[] { r.available, r.progress, r.completed },
                summary = "消灭指定数量的怪物。", demandSummary = "#o" + r.mob + "# #a" + r.id + "1#\n\n完成后对话：#r#p" + r.npc + "##k" };
            q.startReq.npc = q.completeReq.npc = r.npc;
            q.startReq.maxLevel = r.maxLevel;
            q.completeAct.exp = r.exp;
            q.completeReq.mobs.Add(new QuestItemReq { item = r.mob, count = r.count });
            for (int i = 0; i < q.info.Length; i++) q.stateInfo[i] = q.info[i];
            cache.Add(r.id, q); ids.Add(r.id);
            if (!byNpc.TryGetValue(r.npc, out var list)) byNpc[r.npc] = list = new List<int>();
            // 8142仍可读，供旧存档保留；这两个NPC不再绑定错误的跨NPC任务。
            list.Remove(8142);
            list.Add(r.id);
        }
    }
}
