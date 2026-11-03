using System;
using System.Collections.Generic;
using UnityEngine;

// 机器人玩法任务（2026-11-03 迁移，docs/tms282-skill/11-android-migration.md §4.3）。
//
// 282 的机器人来自商城 + 任务 38743「神選之人」链；本项目没有那条链，也没有怪物公园，
// 所以改成本地三条收集任务：补给型 → 高级型 → 阿莎。任务定义走数据（Resources/Quests/robot.json），
// 不写 Quest.wz、不动源端文件——与 ScreenshotTrainingQuests 同一条路。
//
// 奖励道具不放在 QuestDef.completeAct 里，全部由 content/script-overrides/quest/<id>.js 发放：
// 「男款/女款任选」在数据层表达不了（QuestActions.items 是固定列表），只能在脚本里分叉。
// 这里只管需求与文本，JSON 的 exp 会写进 completeAct.exp 作为兜底（脚本没写 gainExp 时仍给经验）。
public static class RobotQuests
{
    [Serializable] public sealed class Records { public Record[] quests; }
    [Serializable] public sealed class Record
    {
        public int id, npc, level, exp, area;
        public string name, summary;
        public string[] info;
        public ItemReq[] items;
        public Prereq[] prereq;
    }
    [Serializable] public sealed class ItemReq { public int item, count; }
    [Serializable] public sealed class Prereq { public int id, state; }

    public const string Resource = "Quests/robot";

    public static void Register(Dictionary<int, QuestDef> cache, List<int> ids, Dictionary<int, List<int>> byNpc)
    {
        var asset = Resources.Load<TextAsset>(Resource);
        if (asset == null) return;
        var records = JsonUtility.FromJson<Records>(asset.text);
        if (records?.quests == null) return;

        foreach (var r in records.quests)
        {
            if (r.id <= 0 || r.npc <= 0) continue;
            if (ids.Contains(r.id)) throw new InvalidOperationException("机器人任务ID与WZ冲突: " + r.id);
            if (cache.ContainsKey(r.id)) continue;

            var q = new QuestDef
            {
                id = r.id,
                name = string.IsNullOrEmpty(r.name) ? "機器人任务 " + r.id : r.name,
                area = r.area > 0 ? r.area : 20,
                summary = string.IsNullOrEmpty(r.summary) ? "机器人玩法任务。" : r.summary,
            };
            q.info = r.info ?? Array.Empty<string>();
            for (int i = 0; i < q.info.Length; i++) q.stateInfo[i] = q.info[i];

            q.startReq.npc = q.completeReq.npc = r.npc;
            if (r.level > 0) q.startReq.level = q.completeReq.level = r.level;
            if (r.prereq != null)
                foreach (var p in r.prereq)
                    q.startReq.quests.Add(new KeyValuePair<int, int>(p.id, p.state));
            if (r.items != null)
                foreach (var it in r.items)
                    if (it.item > 0) q.completeReq.items.Add(new QuestItemReq { item = it.item, count = it.count });
            q.completeAct.exp = r.exp;

            // 任务列表右侧的「需求」栏。收集类任务写成「图标 + 名字 + 计数占位」，
            // #a<questId>1# 由客户端按当前任务进度展开成 n/m。
            string demand = "";
            if (r.items != null)
                foreach (var it in r.items)
                    demand += "#i" + it.item + "# #t" + it.item + "# #a" + r.id + "1#\r\n";
            if (demand.Length == 0) demand = "与 #r#p" + r.npc + "##k 对话。\r\n";
            q.demandSummary = demand + "\r\n交付：#r#p" + r.npc + "##k";

            cache.Add(r.id, q);
            ids.Add(r.id);
            if (!byNpc.TryGetValue(r.npc, out var list)) byNpc[r.npc] = list = new List<int>();
            if (!list.Contains(r.id)) list.Add(r.id);
        }
    }
}
