using System;
using System.Collections.Generic;
using MapleDash.Wz;

// Quest.wz 数据层：只读原端 Check.img / Act.img / QuestInfo.img，不臆造任何数值。
// 结构与服务端 Quest.java + requirements/* + actions/* 一一对应。
public sealed class QuestItemReq
{
    public int item, count;
}

public sealed class QuestItemReward
{
    public int item, count;
    public int prop = -1;          // -1 = 必定发放；>=0 参与加权随机（原端 ItemAction）
}

public sealed class QuestSkillReward
{
    public int skill, level, masterLevel;
}

public sealed class QuestRequirement
{
    public int level, maxLevel, meso, fame, npc = -1, interval, fieldEnter, completedQuests, monsterBookMin, petTameness;
    public string startDate = "", endDate = "";
    public readonly List<int> pets = new List<int>();
    public readonly List<int> jobs = new List<int>();
    public readonly List<QuestItemReq> items = new List<QuestItemReq>();
    public readonly List<KeyValuePair<int, int>> quests = new List<KeyValuePair<int, int>>();   // (questId, state)
    public readonly List<QuestItemReq> mobs = new List<QuestItemReq>();                          // (mobId, count)
    public readonly List<string> infoEx = new List<string>();
}

public sealed class QuestActions
{
    public int exp, money, fame, nextQuest;
    public readonly List<QuestItemReward> items = new List<QuestItemReward>();
    public readonly List<QuestSkillReward> skills = new List<QuestSkillReward>();
}

public sealed class QuestDef
{
    public int id;
    public string name = "", parent = "";
    public int area, medalCategory, medalItem;
    public string[] info = Array.Empty<string>();
    public string startScript = "", endScript = "";
    public bool autoStart, autoComplete, autoPreComplete, autoAccept;

    public QuestRequirement startReq = new QuestRequirement();
    public QuestRequirement completeReq = new QuestRequirement();
    public QuestActions startAct = new QuestActions();
    public QuestActions completeAct = new QuestActions();

    public bool HasStartScript => !string.IsNullOrEmpty(startScript);
    public bool HasEndScript => !string.IsNullOrEmpty(endScript);

    public string InfoText
    {
        get
        {
            if (info == null || info.Length == 0) return "";
            var lines = new List<string>(info.Length);
            for (int i = 0; i < info.Length; i++)
                if (!string.IsNullOrEmpty(info[i])) lines.Add(info[i]);
            return string.Join("\n", lines);
        }
    }
}

public static class QuestCatalog
{
    public static bool Ready { get; private set; }
    public static int QuestCount => byNpc.Count > 0 ? allIds.Count : 0;

    static ImgNode checkRoot, actRoot, infoRoot;
    static readonly Dictionary<int, QuestDef> cache = new Dictionary<int, QuestDef>();
    static readonly List<int> allIds = new List<int>();
    static readonly Dictionary<int, List<int>> byNpc = new Dictionary<int, List<int>>();
    static bool tried;

    public static void Ensure()
    {
        if (tried) return;
        tried = true;
        try
        {
            checkRoot = WzAssets.FileRoot("Quest/Check.img");
            actRoot = WzAssets.FileRoot("Quest/Act.img");
            infoRoot = WzAssets.FileRoot("Quest/QuestInfo.img");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[Quest] Quest.wz 不可用: " + e.Message);
            return;
        }

        foreach (var child in checkRoot.Children)
        {
            if (!int.TryParse(child.Name, out var id)) continue;
            allIds.Add(id);
            for (int state = 0; state <= 1; state++)
            {
                int npc = child.Get(state + "/npc")?.Value != null ? child.Int(state + "/npc", -1) : -1;
                if (npc <= 0) continue;
                if (!byNpc.TryGetValue(npc, out var list)) byNpc[npc] = list = new List<int>();
                if (!list.Contains(id)) list.Add(id);
            }
        }
        allIds.Sort();
        Ready = true;
        UnityEngine.Debug.Log("[Quest] Quest.wz 任务 " + allIds.Count + " 个，涉及 NPC " + byNpc.Count + " 个");
    }

    public static QuestDef Get(int id)
    {
        Ensure();
        if (cache.TryGetValue(id, out var def)) return def;
        def = Build(id);
        cache[id] = def;
        return def;
    }

    public static bool Exists(int id)
    {
        Ensure();
        return checkRoot != null && checkRoot.Get(id.ToString()) != null;
    }

    public static List<int> Ids { get { Ensure(); return allIds; } }

    // 与这个 NPC 有关（可接或可交）的任务。原客户端靠服务端包驱动，这里用 Check.img 的 npc 字段还原。
    public static List<int> ForNpc(int npcId)
    {
        Ensure();
        return byNpc.TryGetValue(npcId, out var list) ? list : Empty;
    }

    static readonly List<int> Empty = new List<int>();

    static QuestDef Build(int id)
    {
        var def = new QuestDef { id = id };
        var check = checkRoot?.Get(id.ToString());
        var act = actRoot?.Get(id.ToString());
        var info = infoRoot?.Get(id.ToString());

        if (info != null)
        {
            def.name = info.Text("name");
            def.parent = info.Text("parent");
            def.area = info.Int("area");
            def.medalCategory = info.Int("medalCategory", -1);
            def.medalItem = info.Int("viewMedalItem");
            def.autoStart = info.Int("autoStart") == 1;
            def.autoAccept = info.Int("autoAccept") == 1;
            def.autoPreComplete = info.Int("autoPreComplete") == 1;
            def.autoComplete = info.Int("autoComplete") == 1 || info.Int("autoPreComplete") == 1;
            var pages = new List<string>();
            for (int i = 0; ; i++)
            {
                var node = info.Get(i.ToString());
                if (node == null) break;
                pages.Add(node.Value as string ?? "");
            }
            def.info = pages.ToArray();
        }
        if (string.IsNullOrEmpty(def.name)) def.name = "任务 " + id;

        def.startReq = ReadRequirement(check?.Get("0"));
        def.completeReq = ReadRequirement(check?.Get("1"));
        def.startScript = check?.Text("0/startscript", "") ?? "";
        def.endScript = check?.Text("1/endscript", "") ?? "";

        def.startAct = ReadActions(act?.Get("0"));
        def.completeAct = ReadActions(act?.Get("1"));
        return def;
    }

    static QuestRequirement ReadRequirement(ImgNode node)
    {
        var r = new QuestRequirement();
        if (node == null) return r;

        r.completedQuests = node.Int("questComplete");
        r.monsterBookMin = node.Int("mbmin");
        r.petTameness = node.Int("pettamenessmin");
        r.startDate = node.Text("start", "");
        r.endDate = node.Text("end", "");
        var pets = node.Get("pet"); if (pets != null) foreach (var pet in pets.Children) r.pets.Add(pet.Int("id"));
        r.level = node.Int("lvmin", node.Int("level"));
        r.maxLevel = node.Int("lvmax", node.Int("maxLevel"));
        r.meso = node.Get("meso") != null ? node.Int("meso") : 0;
        r.fame = node.Get("fame") != null ? node.Int("fame") : 0;
        r.interval = node.Get("interval") != null ? node.Int("interval") : 0;
        // FieldEnterRequirement.processData reads the first child, not the container.
        r.fieldEnter = node.Get("fieldEnter/0") != null ? node.Int("fieldEnter/0") : 0;
        r.npc = node.Get("npc") != null ? node.Int("npc", -1) : -1;

        var jobs = node.Get("job");
        if (jobs != null)
            foreach (var j in jobs.Children)
                if (j.Value != null) r.jobs.Add(Convert.ToInt32(j.Value));

        ReadPairs(node.Get("item"), r.items, "id", "count");
        ReadPairs(node.Get("mob"), r.mobs, "id", "count");

        var quests = node.Get("quest");
        if (quests != null)
            foreach (var q in quests.Children)
            {
                var qid = q.Get("id");
                var state = q.Get("state");
                if (qid?.Value == null) continue;
                r.quests.Add(new KeyValuePair<int, int>(Convert.ToInt32(qid.Value), state?.Value != null ? Convert.ToInt32(state.Value) : 0));
            }

        var iex = node.Get("infoEx");
        if (iex != null)
            foreach (var e in iex.Children)
                r.infoEx.Add(e.Value as string ?? "");

        return r;
    }

    static void ReadPairs(ImgNode parent, List<QuestItemReq> list, string idKey, string countKey)
    {
        if (parent == null) return;
        foreach (var child in parent.Children)
        {
            var id = child.Get(idKey);
            if (id?.Value == null) continue;
            list.Add(new QuestItemReq
            {
                item = Convert.ToInt32(id.Value),
                count = child.Get(countKey)?.Value != null ? Convert.ToInt32(child.Get(countKey).Value) : 0,
            });
        }
    }

    static QuestActions ReadActions(ImgNode node)
    {
        var a = new QuestActions();
        if (node == null) return a;
        if (node.Get("exp") != null) a.exp = node.Int("exp");
        if (node.Get("money") != null) a.money = node.Int("money");
        if (node.Get("fame") != null) a.fame = node.Int("fame");
        if (node.Get("nextQuest") != null) a.nextQuest = node.Int("nextQuest");

        var items = node.Get("item");
        if (items != null)
            foreach (var child in items.Children)
            {
                var id = child.Get("id");
                if (id?.Value == null) continue;
                var reward = new QuestItemReward
                {
                    item = Convert.ToInt32(id.Value),
                    count = child.Get("count")?.Value != null ? Convert.ToInt32(child.Get("count").Value) : 1,
                };
                var prop = child.Get("prop");
                if (prop?.Value != null) reward.prop = Convert.ToInt32(prop.Value);
                a.items.Add(reward);
            }

        var skills = node.Get("skill");
        if (skills != null)
            foreach (var child in skills.Children)
            {
                var id = child.Get("id");
                if (id?.Value == null) continue;
                a.skills.Add(new QuestSkillReward
                {
                    skill = Convert.ToInt32(id.Value),
                    level = child.Get("level")?.Value != null ? Convert.ToInt32(child.Get("level").Value) : 1,
                    masterLevel = child.Get("masterLevel")?.Value != null ? Convert.ToInt32(child.Get("masterLevel").Value) : 0,
                });
            }
        return a;
    }
}
