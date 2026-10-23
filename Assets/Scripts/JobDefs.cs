using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 职业技能表：把源端 63 个职业（冒险家 42 + 骑士团 21）搬进运行时。
//
// 数据来自 tools/export_jobs.py 导出的 Assets/Resources/UI/JobTree.json：
//   jobNames        —— 职业 id → 中文名（原客户端名，缺项回退 i18n job.name.*）
//   jobs[].advance  —— 转职规则（等级 / 凭证 / 赠品 / 授技 / SP / AP）
//   jobs[].skills   —— 技能窗列表（旧 6 条线沿用已验证 tab，新职业列该 img 全部技能）
//   skills[<id>]    —— 技能数据（见 SkillBook.cs）
//
// 规则逐条对齐源端：
//   Character.changeJob      —— SP/AP/背包格/HP·MP 成长（JobSystem.cs 落地）
//   Character.resetStats     —— 一转能力值重排
//   scripts-zh-CN/npc/*.js   —— 各阶转职的等级、凭证、赠品、授技
// 详见 docs/job-system-port.md。
// ————————————————————————————————————————————————————————————

// 转职规则。字段与 JobTree.json 的 jobs[].advance 一一对应。
public sealed class JobAdvance
{
    public int level;                     // 等级门槛
    public List<KeyValuePair<string, int>> stat = new List<KeyValuePair<string, int>>();  // 能力值提示（源端 auto 模式不校验）
    public int quest;                     // 需完成的任务 id（0 = 无）
    public int requireItem;               // 需持有的凭证（0 = 无）
    public int consumeItem;               // 转职时消耗的凭证（0 = 不消耗）
    public List<int> grant = new List<int>();    // 转职赠品（一转武器 / 四转技能书）
    public List<int> teach = new List<int>();    // 四转自动授技（直接学到 maxLevel）
    public bool resetStats;               // 一转能力值重排（resetStats）
    public int sp;                        // 转职授予 SP
    public int ap;                        // 转职授予 AP
    public bool derived;                  // 骑士团：源端无脚本，阈值为 v83 惯例推导值
}

public sealed class JobDef
{
    public int id;
    public string name = "";
    public string book = "";              // 技能书名称（String.wz/Skill.img/<job>/bookName）
    public string family = "";            // beginner / warrior / magician / bowman / thief / pirate / noblesse / cygnus-*
    public int tier;                      // 0 新手 / 1 一转 / 2 二转 / 3 三转 / 4 四转
    public int parent = -1;               // 上一阶职业 id；-1 = 无（新手）
    public List<int> next = new List<int>();
    public List<int> skills = new List<int>();      // 技能窗列表
    public List<int> allSkills = new List<int>();   // 该职业全部技能（含被动/隐藏）
    public JobAdvance advance;            // 成为该职业的规则；tier 0 为 null

    public bool IsBeginner => tier == 0;
    public bool IsCygnus => id >= 1000;
}

public static class JobDefs
{
    public static bool Ready { get; private set; }

    static readonly Dictionary<int, JobDef> byId = new Dictionary<int, JobDef>();
    static readonly Dictionary<string, int> byCls = new Dictionary<string, int>();
    static readonly Dictionary<int, string> names = new Dictionary<int, string>();

    /// <summary>旧存档的职业字符串 → job id。老客户端只有 6 个玩具职业。</summary>
    static readonly Dictionary<string, int> LegacyCls = new Dictionary<string, int>
    {
        { "beginner", 0 },
        { "knight", 100 },     // 旧"骑士"= 战士一转（技能实际是勇士线，归 100 由 resetStats 重排）
        { "warrior", 110 },
        { "mage", 200 },
        { "archer", 300 },
        { "priest", 230 },
    };

    public static void Ensure()
    {
        if (Ready) return;
        Ready = true;
        var asset = Resources.Load<TextAsset>("UI/JobTree");
        if (asset == null) { Debug.LogError("[JobDefs] 缺少 UI/JobTree.json"); return; }
        var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
        if (root == null) { Debug.LogError("[JobDefs] JobTree.json 解析失败"); return; }

        if (root.TryGetValue("jobNames", out var jn) && jn is Dictionary<string, object> jnames)
            foreach (var pair in jnames)
                if (int.TryParse(pair.Key, out int id) && pair.Value is string s) names[id] = s;

        if (root.TryGetValue("jobs", out var jl) && jl is List<object> jlist)
        {
            foreach (var item in jlist)
            {
                if (!(item is Dictionary<string, object> d)) continue;
                var def = new JobDef
                {
                    id = Art.Int(d, "id"),
                    name = d.TryGetValue("name", out var n) ? n as string : "",
                    book = d.TryGetValue("book", out var b) ? b as string : "",
                    family = d.TryGetValue("family", out var f) ? f as string : "",
                    tier = Art.Int(d, "tier"),
                    parent = Art.Int(d, "parent", -1),
                };
                def.next = IntList(d, "next");
                def.skills = IntList(d, "skills");
                def.allSkills = IntList(d, "allSkills");
                if (d.TryGetValue("advance", out var av) && av is Dictionary<string, object> ad)
                    def.advance = ParseAdvance(ad);
                byId[def.id] = def;
            }
        }

        foreach (var pair in LegacyCls) byCls[pair.Key] = pair.Value;
        // job id → 旧字符串（美术/兼容 UI 用）：取该职业的代表 cls
        byClsRev[0] = "beginner";
        byClsRev[100] = "knight"; byClsRev[110] = "warrior"; byClsRev[120] = "warrior"; byClsRev[130] = "warrior";
        byClsRev[200] = "mage"; byClsRev[210] = "mage"; byClsRev[220] = "mage"; byClsRev[230] = "mage";
        byClsRev[300] = "archer"; byClsRev[310] = "archer"; byClsRev[320] = "archer";
        byClsRev[400] = "archer"; byClsRev[410] = "archer"; byClsRev[420] = "archer";   // 旧存档无飞侠美术，暂借弓手
        byClsRev[500] = "warrior"; byClsRev[510] = "warrior"; byClsRev[520] = "warrior"; // 旧存档无海盗美术，暂借战士
    }
    static readonly Dictionary<int, string> byClsRev = new Dictionary<int, string>();

    static JobAdvance ParseAdvance(Dictionary<string, object> d)
    {
        var a = new JobAdvance
        {
            level = Art.Int(d, "level"),
            quest = Art.Int(d, "quest"),
            requireItem = Art.Int(d, "requireItem"),
            consumeItem = Art.Int(d, "consumeItem"),
            resetStats = Art.Int(d, "resetStats") != 0,
            sp = Art.Int(d, "sp"),
            ap = Art.Int(d, "ap"),
            derived = Art.Int(d, "derived") != 0,
        };
        a.grant = IntList(d, "grant");
        a.teach = IntList(d, "teach");
        if (d.TryGetValue("stat", out var st) && st is Dictionary<string, object> sd)
            foreach (var pair in sd)
                if (int.TryParse(pair.Key, out _) && pair.Value != null)
                    a.stat.Add(new KeyValuePair<string, int>(pair.Key, Art.Int(sd, pair.Key)));
        return a;
    }

    static List<int> IntList(Dictionary<string, object> d, string key)
    {
        var result = new List<int>();
        if (d != null && d.TryGetValue(key, out var raw) && raw is List<object> list)
            foreach (var item in list)
                if (item != null && int.TryParse(item.ToString(), out int v)) result.Add(v);
        return result;
    }

    public static JobDef Get(int jobId)
    {
        Ensure();
        return byId.TryGetValue(jobId, out var def) ? def : null;
    }

    public static bool IsValid(int jobId) { Ensure(); return byId.ContainsKey(jobId); }

    public static string NameOf(int jobId)
    {
        Ensure();
        if (byId.TryGetValue(jobId, out var def) && !string.IsNullOrEmpty(def.name)) return def.name;
        return names.TryGetValue(jobId, out var n) ? n : jobId.ToString();
    }

    public static int TierOf(int jobId) { Ensure(); return byId.TryGetValue(jobId, out var def) ? def.tier : 0; }

    /// <summary>技能书分组下标（源端 GameConstants.getSkillBook：仅龙神 2210-2218 非 0，其余全 0）。</summary>
    public static int SkillBookOf(int jobId) => JobBook.Of(jobId);

    public static string FamilyOf(int jobId)
    {
        Ensure();
        return byId.TryGetValue(jobId, out var def) ? def.family : "beginner";
    }

    public static int ParentOf(int jobId)
    {
        Ensure();
        return byId.TryGetValue(jobId, out var def) ? def.parent : -1;
    }

    public static List<int> NextOf(int jobId)
    {
        Ensure();
        return byId.TryGetValue(jobId, out var def) ? def.next : Empty;
    }
    static readonly List<int> Empty = new List<int>();

    /// <summary>
    /// 装备 reqJob 位掩码里本职业的那一位（源端 GameConstants.getJobNiche + EquipmentCatalogService.matchesJob）：
    ///   1 战士 / 2 法师 / 4 弓手 / 8 飞侠 / 16 海盗；新手（niche 0）返回 0 = 只能穿 reqJob=0 的全职业装。
    /// 骑士团 id 同样按 (id/100)%10 归一：1100→战士、1200→法师、1300→弓、1400→飞、1500→海盗。
    /// </summary>
    public static int JobBit(int jobId)
    {
        if (jobId <= 0) return 0;
        int niche = jobId / 100 % 10;            // 1..5
        if (niche < 1 || niche > 5) return 0;
        return 1 << (niche - 1);
    }

    /// <summary>能否从 from 转成 to（只校验职业树邻接，不含等级/道具）。</summary>
    public static bool IsNext(int from, int to)
    {
        Ensure();
        return byId.TryGetValue(from, out var def) && def.next.Contains(to);
    }

    // —— 旧 cls 字符串兼容 ——
    public static int JobIdOf(string cls)
    {
        Ensure();
        if (string.IsNullOrEmpty(cls)) return 0;
        return byCls.TryGetValue(cls, out int id) ? id : 0;
    }

    public static string ClsOf(int jobId)
    {
        Ensure();
        return byClsRev.TryGetValue(jobId, out var cls) ? cls : "beginner";
    }

    public static int Count { get { Ensure(); return byId.Count; } }
    public static IEnumerable<JobDef> All { get { Ensure(); return byId.Values; } }
}
