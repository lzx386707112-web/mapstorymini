// V 矩阵 / HEXA 矩阵的权威静态数据（282 原端 Etc/VCore.img 与 Etc/HexaCore.img）。
//
// 数据文件由 tools/ms282/export_vhexa.py 生成（Unity 侧 Resources.Load("UI/VMatrix")，
// tools/server-check 侧从 Assets/Resources/UI/*.json 直读）——两侧同一份字节，
// 不在这里硬编码任何核心/费用，避免「窗口能点、服务端拒绝」这类分叉。
//
// 与源端的对应关系：
//   VMatrix.json   ← TMS282.5/data/protocol/tms282.3/vmatrix-data.json（= Etc/VCore.img 转储）
//                    Tms282VMatrixData.java 读的就是它：CoreData 580 核心 / coreGroup 51 职业 /
//                    coreGemstone / vPoint 费用表 / info 四个经济参数。
//   HexaMatrix.json← TMS282.5/data/wz282/Etc.wz/HexaCore.img.xml
//                    hexaSkill/coreData 563 核心 + jobCore 51 职业组 + hexaStat 3 档 + solErda 9 档。
//
// 本项目六转后职业编码是「四转 ID + 1」（113 / 1113），而两张表都按四转 ID 建组，
// 所以 Group(job) 先精确命中、再退 job-1（源端也是拿 chr.getJob() 直接比四转 ID）。
using System;
using System.Collections.Generic;
using System.Globalization;

// ———————————————————————— V 矩阵 ————————————————————————

public sealed class VMatrixCore
{
    public int id, type, maxLevel, expireDays;
    public bool selfOpen;
    public string name = "", desc = "";
    public int[] skills = new int[0], subSkills = new int[0];
    public int[] cooldownSkills = new int[0], removalSkills = new int[0], equipRemovalSkills = new int[0];
    public string[] jobs = new string[0];
    // 特殊核心（type==2）的触发条件与效果；其它类型为空串。
    public string special = "";

    // type: 0 技能核心 / 1 强化核心 / 2 特殊核心 / 3 不可用（源端校验直接拒）
    public bool IsSpecial => type == 2;
    public bool IsBoost => type == 1;
}

public sealed class VMatrixGroup
{
    public int[] skill = new int[0], enforce = new int[0], common = new int[0];

    public bool Contains(int id)
        => Array.IndexOf(skill, id) >= 0 || Array.IndexOf(enforce, id) >= 0 || Array.IndexOf(common, id) >= 0;
}

public sealed class VMatrixGemstone
{
    public int[] itemIds = new int[0];
    public int points, openCore, reqQuest, animation;
    public string questAlert = "";
}

public static class VMatrixCatalog
{
    public const int MaxPoints = 9999;
    public const int UnlockLevel = 200;
    // 五转任务完成才准入（282 Tms282VMatrixService 的 eligible 由调用方按 quest 1465 判）。
    public const int FifthJobQuest = 1465;

    static Dictionary<int, VMatrixCore> cores;
    static Dictionary<int, VMatrixGroup> groups;
    static Dictionary<int, VMatrixGemstone> gems;
    static SortedDictionary<int, int> skillCosts, boostCosts;

    public static int ResetMeso, RegisterSpecialCost, ExtendSpecialCost, TransformCost, TransformItem, TransformMinutes;
    public static bool Loaded => cores != null;

    public static void Invalidate()
    {
        cores = null; groups = null; gems = null; skillCosts = null; boostCosts = null;
    }

    /// <summary>首次访问时从 Resources/磁盘加载；加载失败按空表处理（窗口会显示「数据缺失」）。</summary>
    public static void Ensure()
    {
        if (cores != null) return;
        Load(PlatformRes.LoadText("UI/VMatrix"));
    }

    public static void Load(string json)
    {
        cores = new Dictionary<int, VMatrixCore>();
        groups = new Dictionary<int, VMatrixGroup>();
        gems = new Dictionary<int, VMatrixGemstone>();
        skillCosts = new SortedDictionary<int, int>();
        boostCosts = new SortedDictionary<int, int>();
        if (string.IsNullOrEmpty(json)) return;

        var root = MiniJsonDb.Parse(json) as Dictionary<string, object>;
        if (root == null) return;
        var info = Obj(root, "info");
        ResetMeso = I(info, "resetMeso");
        RegisterSpecialCost = I(info, "registerSpecialCost");
        ExtendSpecialCost = I(info, "extendSpecialCost");
        TransformCost = I(info, "transformCost");
        TransformItem = I(info, "transformItem");
        TransformMinutes = I(info, "transformMinutes");

        var costs = Obj(root, "costs");
        ReadCosts(Obj(costs, "skill"), skillCosts);
        ReadCosts(Obj(costs, "enforce"), boostCosts);

        if (Obj(root, "cores") is Dictionary<string, object> coreTable)
            foreach (var pair in coreTable)
            {
                var node = pair.Value as Dictionary<string, object>;
                if (node == null) continue;
                int id = I(node, "id", ParseInt(pair.Key));
                cores[id] = new VMatrixCore
                {
                    id = id,
                    type = I(node, "type", -1),
                    maxLevel = Math.Max(1, I(node, "maxLevel", 1)),
                    expireDays = I(node, "expireDays"),
                    selfOpen = B(node, "selfOpen"),
                    name = S(node, "name"),
                    desc = S(node, "desc"),
                    skills = Ints(node, "skills"),
                    subSkills = Ints(node, "subSkills"),
                    cooldownSkills = Ints(node, "cooldownSkills"),
                    removalSkills = Ints(node, "removalSkills"),
                    equipRemovalSkills = Ints(node, "equipRemovalSkills"),
                    jobs = Strings(node, "jobs"),
                    special = node.ContainsKey("special") && node["special"] != null
                        ? NodeText(node["special"]) : "",
                };
            }

        if (Obj(root, "groups") is Dictionary<string, object> groupTable)
            foreach (var pair in groupTable)
            {
                var node = pair.Value as Dictionary<string, object>;
                if (node == null) continue;
                groups[ParseInt(pair.Key)] = new VMatrixGroup
                {
                    skill = Ints(node, "skill"),
                    enforce = Ints(node, "enforce"),
                    common = Ints(node, "common"),
                };
            }

        if (root.TryGetValue("gemstones", out var gemList) && gemList is List<object> list)
            foreach (var item in list)
            {
                var node = item as Dictionary<string, object>;
                if (node == null) continue;
                var gem = new VMatrixGemstone
                {
                    itemIds = Ints(node, "itemIds"),
                    points = I(node, "points"),
                    openCore = I(node, "openCore"),
                    reqQuest = I(node, "reqQuest"),
                    animation = I(node, "animation"),
                    questAlert = S(node, "questAlert"),
                };
                foreach (int itemId in gem.itemIds) gems[itemId] = gem;
            }
    }

    static void ReadCosts(Dictionary<string, object> node, SortedDictionary<int, int> target)
    {
        if (node == null) return;
        foreach (var pair in node)
            if (int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
                target[level] = (int)Num(pair.Value);
    }

    public static VMatrixCore Core(int id)
    {
        Ensure();
        return cores.TryGetValue(id, out var core) ? core : null;
    }

    /// <summary>职业组：先按原样命中，再退四转 ID（六转编码 = 四转 + 1）。</summary>
    public static VMatrixGroup Group(int job)
    {
        Ensure();
        if (groups.TryGetValue(job, out var group)) return group;
        return groups.TryGetValue(job - 1, out group) ? group : null;
    }

    /// <summary>
    /// 源端 Tms282VMatrixData.allowed：group 必须存在，特殊核心（type==2）不再看是否在组内。
    /// 注意这里是「core.type == 2 || group.contains(id)」而不是「type==2 直接放行」——
    /// 职业完全没有 V 矩阵组时连特殊核心也不该可用。
    /// </summary>
    public static bool Allowed(int job, int id)
    {
        var core = Core(id);
        if (core == null) return false;
        var group = Group(job);
        return group != null && (core.type == 2 || group.Contains(id));
    }

    /// <summary>
    /// 升到 targetLevel 这一级要多少 V 点。源端 Native10510E0 取「第一个 ≥ 目标等级的阈值」，
    /// 不是 floor 查表——用 ceilingEntry 而不是「向上取整到最近的已列等级」。
    /// </summary>
    public static int CostForLevel(int id, int level)
    {
        var core = Core(id);
        if (core == null || core.type > 1 || level < 1 || level > core.maxLevel) return -1;
        var table = core.type == 0 ? skillCosts : boostCosts;
        foreach (var pair in table)
            if (pair.Key >= level) return pair.Value > 0 ? pair.Value : -1;
        return -1;
    }

    public static long CostBetween(int id, int from, int to)
    {
        if (from < 0 || to <= from) return -1;
        long total = 0;
        for (int level = from + 1; level <= to; level++)
        {
            int cost = CostForLevel(id, level);
            if (cost < 0) return -1;
            total += cost;
        }
        return total;
    }

    /// <summary>重置时的保留下限：可自开的核心或技能组核心保留 1 级，其余归零（源端 resetFloor）。</summary>
    public static int ResetFloor(int job, int id)
    {
        var core = Core(id);
        if (core == null) return 0;
        var group = Group(job);
        return !core.selfOpen || (group != null && Array.IndexOf(group.skill, id) >= 0) ? 1 : 0;
    }

    public static VMatrixGemstone Gemstone(int itemId)
    {
        Ensure();
        return gems.TryGetValue(itemId, out var gem) ? gem : null;
    }

    public static IEnumerable<VMatrixCore> AllCores
    {
        get { Ensure(); return cores.Values; }
    }

    public static int CoreCount { get { Ensure(); return cores.Count; } }

    // —————————— MiniJson 读取小工具（两张表共用） ——————————

    internal static Dictionary<string, object> Obj(Dictionary<string, object> node, string key)
        => node != null && node.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

    internal static int I(Dictionary<string, object> node, string key, int def = 0)
        => node != null && node.TryGetValue(key, out var value) ? (int)Num(value, def) : def;

    internal static bool B(Dictionary<string, object> node, string key, bool def = false)
        => node != null && node.TryGetValue(key, out var value) && value is bool b ? b : def;

    internal static string S(Dictionary<string, object> node, string key)
        => node != null && node.TryGetValue(key, out var value) && value is string s ? s : "";

    internal static int[] Ints(Dictionary<string, object> node, string key)
    {
        if (node == null || !node.TryGetValue(key, out var value) || !(value is List<object> list)) return new int[0];
        var result = new int[list.Count];
        for (int i = 0; i < list.Count; i++) result[i] = (int)Num(list[i]);
        return result;
    }

    internal static string[] Strings(Dictionary<string, object> node, string key)
    {
        if (node == null || !node.TryGetValue(key, out var value) || !(value is List<object> list)) return new string[0];
        var result = new string[list.Count];
        for (int i = 0; i < list.Count; i++) result[i] = list[i] as string ?? "";
        return result;
    }

    internal static long Num(object value, long def = 0)
    {
        if (value is long l) return l;
        if (value is double d) return (long)d;
        if (value is int i) return i;
        return def;
    }

    internal static int ParseInt(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    /// <summary>特殊核心的 spCoreOption 原样保留成 JSON 文本，客户端只读 effect.skill_id。</summary>
    internal static string NodeText(object node)
    {
        if (node is Dictionary<string, object> map)
        {
            var parts = new List<string>();
            foreach (var pair in map) parts.Add("\"" + pair.Key + "\":" + NodeText(pair.Value));
            return "{" + string.Join(",", parts) + "}";
        }
        if (node is List<object> list)
        {
            var parts = new List<string>();
            foreach (var item in list) parts.Add(NodeText(item));
            return "[" + string.Join(",", parts) + "]";
        }
        if (node is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        if (node is bool b) return b ? "true" : "false";
        if (node == null) return "null";
        return Convert.ToString(node, CultureInfo.InvariantCulture);
    }
}

// ———————————————————————— HEXA 矩阵 ————————————————————————

public sealed class HexaCore
{
    public int id, type, maxLevel;
    public string name = "", desc = "";
    public int[] skills = new int[0], subSkills = new int[0], reqForActivation = new int[0];
    public Dictionary<int,int> requiredSkills = new Dictionary<int,int>();
}

public sealed class HexaGroup
{
    public int[] mastery = new int[0], enforce = new int[0], skill = new int[0], common = new int[0];

    public IEnumerable<int> All
    {
        get
        {
            foreach (int id in mastery) yield return id;
            foreach (int id in enforce) yield return id;
            foreach (int id in skill) yield return id;
            foreach (int id in common) yield return id;
        }
    }

    public bool Contains(int id)
        => Array.IndexOf(mastery, id) >= 0 || Array.IndexOf(enforce, id) >= 0
           || Array.IndexOf(skill, id) >= 0 || Array.IndexOf(common, id) >= 0;
}

public sealed class HexaStatCore
{
    public int id, reqLevel, maxLevel, reqCore;
    public string name = "", desc = "";
}

public static class HexaCatalog
{
    // 门禁三件套（docs/tms282-skill/20-v-hexa-panel.md §6.3，已拍板）：
    //   lvmin=260（38601/38609/38624/38625 四个任务一致写的唯一等级门槛）
    //   && quest(38625) 完成（'[6轉]HEXA矩陣'，客户端唯一解锁标记）
    //   && job ∈ 38601 的 47 项白名单 + job_TW 4 项（原样比对六转编码，不做 -1 归一化）
    public const int UnlockLevel = 260;
    public const int UnlockQuest = 38625;

    public static readonly int[] JobWhitelist =
    {
        113, 123, 133, 213, 223, 233, 313, 323, 333, 413, 423, 435, 513, 523, 533,
        1113, 1213, 1313, 1413, 1513, 2113, 2219, 2313, 2413, 2513, 2713, 3113, 3123,
        3213, 3313, 3513, 3613, 3713, 5113, 6113, 6313, 6413, 6513, 10113, 14213,
        15113, 15213, 15413, 15513, 16113, 16213, 16413,
        4113, 4213, 17213, 17513,
    };

    // 索尔艾尔达（solErda）九档：经验条上限与可兑换道具（HexaCore.img/solErda）。
    public sealed class SolErdaTier { public int exp; public int[] itemIds = new int[0]; }

    static Dictionary<int, HexaCore> cores;
    static Dictionary<int, HexaGroup> groups;
    static Dictionary<int, HexaStatCore> stats;
    static List<SolErdaTier> tiers;
    static Dictionary<string,object> statValues;

    public static int GaugeMax, SlotMax;
    public static bool Loaded => cores != null;

    public static void Invalidate()
    {
        cores = null; groups = null; stats = null; tiers = null;
    }

    public static void Ensure()
    {
        if (cores != null) return;
        Load(PlatformRes.LoadText("UI/HexaMatrix"));
    }

    public static void Load(string json)
    {
        cores = new Dictionary<int, HexaCore>();
        groups = new Dictionary<int, HexaGroup>();
        stats = new Dictionary<int, HexaStatCore>();
        tiers = new List<SolErdaTier>();
        if (string.IsNullOrEmpty(json)) return;

        var root = MiniJsonDb.Parse(json) as Dictionary<string, object>;
        if (root == null) return;
        statValues = VMatrixCatalog.Obj(root,"statValues");
        var sol = VMatrixCatalog.Obj(root, "solErda");
        GaugeMax = VMatrixCatalog.I(sol, "gaugeMax", 1000);
        SlotMax = VMatrixCatalog.I(sol, "slotMax", 20);
        if (sol != null && sol.TryGetValue("tiers", out var tierList) && tierList is List<object> list)
            foreach (var item in list)
            {
                var node = item as Dictionary<string, object>;
                if (node == null) continue;
                tiers.Add(new SolErdaTier
                {
                    exp = VMatrixCatalog.I(node, "exp"),
                    itemIds = VMatrixCatalog.Ints(node, "itemIds"),
                });
            }

        if (VMatrixCatalog.Obj(root, "cores") is Dictionary<string, object> coreTable)
            foreach (var pair in coreTable)
            {
                var node = pair.Value as Dictionary<string, object>;
                if (node == null) continue;
                int id = VMatrixCatalog.I(node, "id", VMatrixCatalog.ParseInt(pair.Key));
                cores[id] = new HexaCore
                {
                    id = id,
                    type = VMatrixCatalog.I(node, "type", -1),
                    maxLevel = Math.Max(1, VMatrixCatalog.I(node, "maxLevel", 1)),
                    name = VMatrixCatalog.S(node, "name"),
                    desc = VMatrixCatalog.S(node, "desc"),
                    skills = VMatrixCatalog.Ints(node, "skills"),
                    subSkills = VMatrixCatalog.Ints(node, "subSkills"),
                    reqForActivation = VMatrixCatalog.Ints(node, "reqForActivation"),
                    requiredSkills = Requirements(VMatrixCatalog.Obj(node, "requiredSkills")),
                };
            }

        if (VMatrixCatalog.Obj(root, "groups") is Dictionary<string, object> groupTable)
            foreach (var pair in groupTable)
            {
                var node = pair.Value as Dictionary<string, object>;
                if (node == null) continue;
                groups[VMatrixCatalog.ParseInt(pair.Key)] = new HexaGroup
                {
                    mastery = VMatrixCatalog.Ints(node, "mastery"),
                    enforce = VMatrixCatalog.Ints(node, "enforce"),
                    skill = VMatrixCatalog.Ints(node, "skill"),
                    common = VMatrixCatalog.Ints(node, "common"),
                };
            }

        if (VMatrixCatalog.Obj(root, "stats") is Dictionary<string, object> statTable)
            foreach (var pair in statTable)
            {
                var node = pair.Value as Dictionary<string, object>;
                if (node == null) continue;
                int id = VMatrixCatalog.I(node, "id", VMatrixCatalog.ParseInt(pair.Key));
                stats[id] = new HexaStatCore
                {
                    id = id,
                    reqLevel = VMatrixCatalog.I(node, "reqLevel"),
                    maxLevel = Math.Max(1, VMatrixCatalog.I(node, "maxLevel", 1)),
                    reqCore = VMatrixCatalog.I(node, "reqCore"),
                    name = VMatrixCatalog.S(node, "name"),
                    desc = VMatrixCatalog.S(node, "desc"),
                };
            }
    }

    public static HexaCore Core(int id)
    {
        Ensure();
        return cores.TryGetValue(id, out var core) ? core : null;
    }

    static Dictionary<int,int> Requirements(Dictionary<string,object> node)
    {
        var result = new Dictionary<int,int>();
        if (node != null) foreach (var pair in node)
            if (int.TryParse(pair.Key, out int id)) result[id] = Convert.ToInt32(pair.Value);
        return result;
    }

    // PerM 原值为万分比；PAD/MAD/indieStat 保留 IMG 原始整数单位。
    // GameConstants.getMainBaseStatForJob + JobConstants.JobEnum；额外保留机械师DEX分支。
    public static string MainStat(int job)
    {
        if(job/10==312 || job==3101) return "HP";
        switch(job/100)
        {
            case 2: case 12: case 22: case 27: case 32: case 42: case 112:
            case 142: case 152: case 162: case 172: case 182: return "INT";
            case 3: case 13: case 23: case 33: case 35: case 63: case 65: case 175: return "DEX";
            case 4: case 14: case 24: case 64: case 154: case 164: return "LUK";
            case 5: return job/10==52 ? "DEX" : "STR";
            default: return "STR";
        }
    }
    public static int MainStatValue(int weight,int job) => weight*(job/100==36 ? 40 : MainStat(job)=="HP" ? 630 : 30);

    public static int StatValue(int type,int level,bool main)
    {
        Ensure();
        var table=VMatrixCatalog.Obj(statValues,main ? "main" : "additional");
        var values=VMatrixCatalog.Ints(table,type.ToString());
        return level>=0 && level<values.Length ? values[level] : 0;
    }

    public static HexaStatCore Stat(int id)
    {
        Ensure();
        return stats.TryGetValue(id, out var stat) ? stat : null;
    }

    /// <summary>
    /// 属性核心按解锁等级排（260 / 265 / 270）。hexaStat 页的六个槽是
    /// 「三档核心 × 两套预设」摆成的六边形，所以窗口需要这个稳定顺序。
    /// </summary>
    public static List<HexaStatCore> AllStats
    {
        get
        {
            Ensure();
            var list = new List<HexaStatCore>(stats.Values);
            list.Sort((a, b) => a.reqLevel != b.reqLevel
                ? a.reqLevel.CompareTo(b.reqLevel) : a.id.CompareTo(b.id));
            return list;
        }
    }

    public static HexaGroup Group(int job)
    {
        Ensure();
        if (groups.TryGetValue(job, out var group)) return group;
        return groups.TryGetValue(job - 1, out group) ? group : null;
    }

    /// <summary>
    /// 六转职业编码是否在 38601 白名单内（47 项 + job_TW 4 项）。
    ///
    /// 白名单里存的是**六转编码**（113 / 1113 …），而本项目存档里躺着的是**四转编码**
    /// （112 / 1112）——这里既没有 jobLevel 也没有「六转回写 job」这一步，因为技能资源
    /// （Skill/112.img 等）全按四转 ID 组织，改 job 会让整棵技能树查不到。
    /// 所以两种编码都认：同一职业只能落到同一个答案，而「真的做了六转」由 38625
    /// 的完成状态去卡（UnlockQuest），不靠 job 值区分。
    /// </summary>
    public static bool JobAllowed(int job)
        => Array.IndexOf(JobWhitelist, job) >= 0 || Array.IndexOf(JobWhitelist, job + 1) >= 0;

    public static bool Allowed(int job, int coreId)
    {
        var core = Core(coreId);
        if (core == null) return false;
        var group = Group(job);
        return group != null && group.Contains(coreId);
    }

    public static IEnumerable<HexaCore> AllCores
    {
        get { Ensure(); return cores.Values; }
    }

    public static IReadOnlyList<SolErdaTier> Tiers
    {
        get { Ensure(); return tiers; }
    }

    public static int SolErdaExp(int itemId)
    {
        Ensure();
        foreach (var tier in tiers)
            if (Array.IndexOf(tier.itemIds, itemId) >= 0) return tier.exp;
        return 0;
    }
}
