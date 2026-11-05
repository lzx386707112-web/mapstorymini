using System;
using System.Collections.Generic;
using UnityEngine;

// 五转 / 六转任务链（2026-11-04，docs/tms282-skill/07-job-advancement.md §2 §3）。
//
// 为什么不用源端 Quest.wz：GMS083 的 Quest/Check.img 里 1450..1500 与 38600..38630
// **一条都没有**（本项目资源已实测，Check/Act/QuestInfo 三张表都是 0 命中）。282 的任务脚本
// （q1460s 起）客户端与服务端也都没有。所以按用户拍板的「可确证部分 + 最小合理实现」：
// 任务 id、名字、顺序、门槛、前置链照 282 原样，对白自己写。
//
// 与源端的差异（都是资源缺失导致，不是偷懒）：
//   1. 六转链的 NPC（莉莉 3006370 / 艾爾文 3006371 / 三女神 3006373-3006388）在本项目
//      Npc/ 里都不存在，区域 251 的地图也没有。整条链暂时挂在 270010111 的凝視者 2140001
//      ——就是五转那条链用的同一个 NPC，玩家到得了。
//   2. 39722（[Borderless] 本來的樣貌）同样没有，六转链头的前置只保留 1465。
//   3. 1481 的星力门槛（总星力 100 / 武器 10）没有对应字段，QuestRequirement 只有
//      level/items/mobs/quests，所以这一步降级成一次确认。
//
// 关于「玩家怎么走过去」：旁观者站的 270010111 在時間神殿深处，整片 27xxxx 区域在本项目里
// 没有任何「主世界直达」的普通传送门，入口全靠脚本门串起来 ——
//   31165 裂縫任务 → 272000000 的 crack00(portal/timeCrack) → 272030000 →
//   270000000 → 270010000 → 270010100 的 in00(portal/timeQuest，需 3501 完成) →
//   270010110 → 普通门 in01 → 270010111。
// 这条链上的脚本（timeCrack / timeQuest）与门禁任务（31165 / 3500..3505）本项目都齐，
// tools/check_advanced_quests.py 会逐环核对，缺一环就报错——因为那等于玩家永远见不到旁观者，
// 1465 / 38625 两道门禁也就永远开不了。
//
// 数据只放文本与前置；不写 quest/<id>.js：NpcSession.BeginDataQuest 在找不到脚本时
// 会走 Quest/Say.img → quest.info[] 的通用流程，任务里没有任何收集/击杀条件，
// 所以「接取 → 再对话 → 完成」这条主路径完整可用。
public static class AdvancedQuests
{
    [Serializable] public sealed class Records { public Record[] quests; }
    [Serializable] public sealed class Record
    {
        public int id, npc, level, exp, area;
        // 282 QuestInfo.img 的 autoStart：1 = 该任务出「头顶灯泡」通知（NpcField.QuestAlerts 的候选来源）。
        // 原节点里只有 1460/1461/1465/1478/1481/38601/38611/38624/38625 为 1；
        // 38611 是「繼續進行轉職」旁支，本项目未收录，其余 8 条原样照搬。
        public int autoStart;
        public string name, summary;
        public string[] info;
        public ItemReq[] items;
        public Prereq[] prereq;        // 接取条件
        public Prereq[] prereqDone;    // 交付条件（1461 要三女神全完成这种）
    }
    [Serializable] public sealed class ItemReq { public int item, count; }
    [Serializable] public sealed class Prereq { public int id, state; }

    public const string Resource = "Quests/advanced";

    public static void Register(Dictionary<int, QuestDef> cache, List<int> ids, Dictionary<int, List<int>> byNpc)
    {
        var asset = Resources.Load<TextAsset>(Resource);
        if (asset == null) return;
        var records = JsonUtility.FromJson<Records>(asset.text);
        if (records?.quests == null) return;

        foreach (var r in records.quests)
        {
            if (r.id <= 0 || r.npc <= 0) continue;
            // 与 Quest.wz 撞号说明资源升级了，必须以源端为准，不能偷偷覆盖。
            if (ids.Contains(r.id)) throw new InvalidOperationException("进阶任务ID与WZ冲突: " + r.id);
            if (cache.ContainsKey(r.id)) continue;

            var q = new QuestDef
            {
                id = r.id,
                name = string.IsNullOrEmpty(r.name) ? "進階任务 " + r.id : r.name,
                // 任务窗的分类标题取自 Etc/QuestCategory.img，45..51 只有「时间神殿」到「活动」这一段。
                // 整条链接在 270010111（时间神殿）的凝視者身上，归到 46 = 时间神殿。
                area = r.area > 0 ? r.area : 46,
                summary = string.IsNullOrEmpty(r.summary) ? "五轉 / 六轉任务。" : r.summary,
            };
            q.info = r.info ?? Array.Empty<string>();
            for (int i = 0; i < q.info.Length; i++) q.stateInfo[i] = q.info[i];

            q.startReq.npc = q.completeReq.npc = r.npc;
            if (r.level > 0) q.startReq.level = q.completeReq.level = r.level;
            // 灯泡候选：等级/前置由 QuestLogic.CanStart 二次过滤，所以到 200 级只有 1460、
            // 到 260 级且五转完成后只有 38601 会冒出来，链中段的 38602..38609 原端本来就不出灯泡。
            q.autoStart = r.autoStart == 1;
            if (r.prereq != null)
                foreach (var p in r.prereq)
                    q.startReq.quests.Add(new KeyValuePair<int, int>(p.id, p.state));
            if (r.prereqDone != null)
                foreach (var p in r.prereqDone)
                    q.completeReq.quests.Add(new KeyValuePair<int, int>(p.id, p.state));
            if (r.items != null)
                foreach (var it in r.items)
                    if (it.item > 0) q.completeReq.items.Add(new QuestItemReq { item = it.item, count = it.count });
            q.completeAct.exp = r.exp;

            // 需求栏：这几条都是纯对话任务，把前置任务名字写出来比「收集 x N」有用。
            string demand = "与 #r#p" + r.npc + "##k 对话。\r\n";
            if (r.prereqDone != null && r.prereqDone.Length > 0)
            {
                demand += "\r\n交付前需完成：";
                foreach (var p in r.prereqDone)
                    demand += "\r\n· #b" + (cache.TryGetValue(p.id, out var before) ? before.name : "任务 " + p.id) + "#k";
            }
            if (r.items != null)
                foreach (var it in r.items)
                    demand += "\r\n#i" + it.item + "# #t" + it.item + "# #a" + r.id + "1#";
            q.demandSummary = demand;

            cache.Add(r.id, q);
            ids.Add(r.id);
            if (!byNpc.TryGetValue(r.npc, out var list)) byNpc[r.npc] = list = new List<int>();
            if (!list.Contains(r.id)) list.Add(r.id);
        }
    }
}
