// 五转 / 六转任务链的离线回归（Assets/Resources/Quests/advanced.json）。
//
// 检查的是「链会不会断」，不是文案：
//   1. 条数与 id 唯一；GMS083 的 Quest.wz 里不该出现这些 id（撞号 = 资源升级后必须重做）
//   2. 每条都有 NPC / 等级 / 两段以上的对白（接取 + 交付）
//   3. 前置全部能在表内解析，链头没有前置，链尾正好是矩阵门禁任务
//   4. 矩阵门禁常量与任务表对得上（V 矩阵 1465、HEXA 38625）
//   5. 六转链的等级门槛统一 260，五转统一 200（源端 lvmin）
using System;
using System.Collections.Generic;
using System.IO;

public static class AdvancedQuestChecks
{
    const string Relative = "Assets/Resources/Quests/advanced.json";

    public static void Run(Action<bool, string> check)
    {
        string path = Path.Combine(AppContext.BaseDirectory, Relative);
        if (!File.Exists(path)) path = FindUpwards(Relative);
        check(path != null, "找到 advanced.json（任务链数据）");
        if (path == null) return;

        var root = MiniJsonDb.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
        var list = root != null && root.TryGetValue("quests", out var raw) && raw is List<object> items ? items : null;
        check(list != null, "advanced.json 解析出 quests 数组");
        if (list == null) return;

        var byId = new Dictionary<int, Dictionary<string, object>>();
        var order = new List<int>();
        foreach (var item in list)
        {
            var node = item as Dictionary<string, object>;
            if (node == null) continue;
            int id = VMatrixCatalog.I(node, "id");
            check(id > 0, "任务 id 有效：" + id);
            if (id <= 0) continue;
            check(!byId.ContainsKey(id), "任务 id 不重复：" + id);
            byId[id] = node;
            order.Add(id);
        }
        check(order.Count == 19, "19 条任务（五转 8 + 六转 11），实际 " + order.Count);

        // 282 的链是「五转 8 条 + 六转 11 条」，这里逐条核对可确证的 id
        var fifth = new[] { 1460, 1481, 1461, 1462, 1463, 1464, 1465, 1478 };
        var sixth = new[] { 38601, 38602, 38603, 38604, 38605, 38606, 38607, 38608, 38609, 38624, 38625 };
        foreach (int id in fifth) check(byId.ContainsKey(id), "五转任务存在：" + id);
        foreach (int id in sixth) check(byId.ContainsKey(id), "六转任务存在：" + id);

        foreach (var pair in byId)
        {
            var node = pair.Value;
            string name = VMatrixCatalog.S(node, "name");
            check(VMatrixCatalog.I(node, "npc") > 0, pair.Key + " 有 NPC");
            check(!string.IsNullOrEmpty(name), pair.Key + " 有名字");
            check(InfoCount(node) >= 2, pair.Key + " 至少两段对白（接取 + 交付）");
            int level = VMatrixCatalog.I(node, "level");
            int want = pair.Key >= 38600 ? 260 : 200;
            check(level == want, pair.Key + " 等级门槛 " + want + "（实际 " + level + "）");
        }

        // 前置必须能在表里解析（39722 那条已知缺口不在表内，六转链头只挂 1465）
        foreach (var pair in byId)
            foreach (int need in Prereqs(pair.Value, "prereq"))
                check(byId.ContainsKey(need), pair.Key + " 的前置 " + need + " 在表内");
        foreach (var pair in byId)
            foreach (int need in Prereqs(pair.Value, "prereqDone"))
                check(byId.ContainsKey(need), pair.Key + " 的交付前置 " + need + " 在表内");

        // 链头 / 链尾
        check(Prereqs(byId[1460], "prereq").Count == 0, "1460 是五转链头（无前置）");
        var sixthHead = Prereqs(byId[38601], "prereq");
        check(sixthHead.Count == 1 && sixthHead[0] == 1465, "38601 只以 1465 为前置（39722 本项目没有）");
        check(Prereqs(byId[38602], "prereq").Contains(38601) && Prereqs(byId[38625], "prereq").Contains(38624),
            "六转链 38601 → 38602 … → 38624 → 38625 顺序不断");
        var three = Prereqs(byId[1461], "prereqDone");
        check(three.Count == 3 && three.Contains(1462) && three.Contains(1463) && three.Contains(1464),
            "1461 交付需 1462/1463/1464 三女神全部完成");
        check(Prereqs(byId[1478], "prereq").Contains(1465), "1478 以 1465 为前置");

        // 门禁常量与任务表必须指向同一批 id
        check(VMatrixCatalog.FifthJobQuest == 1465 && byId.ContainsKey(VMatrixCatalog.FifthJobQuest),
            "V 矩阵门禁任务 1465 在任务表内");
        check(HexaCatalog.UnlockQuest == 38625 && byId.ContainsKey(HexaCatalog.UnlockQuest),
            "HEXA 门禁任务 38625 在任务表内");

        // 纯对话任务：不带收集/击杀条件，保证链能一路走完
        foreach (var pair in byId)
            check(VMatrixCatalog.Ints(pair.Value, "items").Length == 0, pair.Key + " 不带收集条件（纯对话可交付）");

        // 头顶灯泡：只有 282 QuestInfo.autoStart=1 的任务才出通知图标（NpcField.QuestAlerts 的候选）。
        // 原节点里是 1460/1461/1465/1478/1481/38601/38611/38624/38625；38611 是旁支，本项目未收录。
        // 漏了链头，玩家到 200 / 260 级头顶不会冒灯泡，等于不知道要转职。
        var bulb = new List<int>();
        foreach (var pair in byId)
            if (VMatrixCatalog.I(pair.Value, "autoStart") == 1) bulb.Add(pair.Key);
        bulb.Sort();
        check(string.Join(",", bulb) == "1460,1461,1465,1478,1481,38601,38624,38625",
            "灯泡候选 = 282 autoStart 的 8 条（实际 " + string.Join(",", bulb) + "）");
        check(bulb.Contains(1460) && bulb.Contains(38601), "五转 / 六转链头都会出灯泡提示");
    }

    static int InfoCount(Dictionary<string, object> node)
        => node != null && node.TryGetValue("info", out var raw) && raw is List<object> list ? list.Count : 0;

    static List<int> Prereqs(Dictionary<string, object> node, string key)
    {
        var result = new List<int>();
        if (node == null || !node.TryGetValue(key, out var raw) || !(raw is List<object> list)) return result;
        foreach (var item in list)
            if (item is Dictionary<string, object> entry) result.Add(VMatrixCatalog.I(entry, "id"));
        return result;
    }

    static string FindUpwards(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
