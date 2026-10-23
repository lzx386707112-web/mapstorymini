using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 技能书：把 Skill.wz 的整棵职业技能树搬到运行时。
//
// 数据来自 tools/export_jobs.py 导出的 UI/JobTree.json：
//   jobs[]             —— 63 个职业（冒险家 42 + 骑士团 21），每个职业一条技能线
//   jobs[].skills      —— 技能窗列表（旧 6 条线沿用已验证 tab，新职业列该 img 全部技能）
//   jobs[].allSkills   —— 该职业全部技能（含被动/隐藏）
//   skills[<id>]       —— 中文名、说明、上限等级、类型、前置、逐级数值、图标
// 逐级数值是 WZ 里 level/N 的原始字段（mpCon / damage / attackCount / cooltime …），
// 没有任何本地估算。
//
// 技能点：源端 level_up_sp_gain=3，且 levelUpGainSp() 对新手直接返回（新手无 SP）；
// 一转起 SP 是存档累计字段（Profile.sp），转职另按 JobSystem 授予。
// ————————————————————————————————————————————————————————————

public sealed class SkillData
{
    public int id, maxLevel, masterLevel, job;
    public string name, desc, type, icon;
    public bool magic;
    public bool noName;       // 源端 String.wz 缺中文名（五六转 V 技组件），UI 回退显示 id
    public bool autoLearn;   // 不是手动加点的技能（如精灵的祝福，按账号等级自动习得）
    public List<int[]> req = new List<int[]>();      // [前置技能id, 需要等级]
    public Dictionary<string, object> levels = new Dictionary<string, object>();

    public Dictionary<string, object> Level(int lv)
    {
        if (lv < 1) lv = 1;
        if (lv > maxLevel) lv = maxLevel;
        return levels.TryGetValue(lv.ToString(), out var row)
            ? row as Dictionary<string, object> : null;
    }

    public bool IsAttack => type == "attack";
    public bool IsBuff => type == "buff";
    public bool IsSummon => type == "summon";
    public bool IsPassive => type == "passive";
}

public static class SkillBook
{
    public static bool Ready { get; private set; }

    static readonly Dictionary<int, SkillData> byId = new Dictionary<int, SkillData>();
    static readonly List<Dictionary<string, object>> lines = new List<Dictionary<string, object>>();
    static readonly Dictionary<int, Dictionary<string, object>> lineByJob = new Dictionary<int, Dictionary<string, object>>();
    static Dictionary<string, object> jobNames = new Dictionary<string, object>();

    public static void Ensure()
    {
        if (Ready) return;
        Ready = true;
        var asset = Resources.Load<TextAsset>("UI/JobTree");
        if (asset == null) { Debug.LogError("[SkillBook] 缺少 UI/JobTree.json"); return; }
        var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
        if (root == null) { Debug.LogError("[SkillBook] JobTree.json 解析失败"); return; }

        // skills 是 JSON 对象（id → 数据），MiniJson 解析成 Dictionary
        foreach (var pair in (Dictionary<string, object>)root["skills"])
        {
            var d = pair.Value as Dictionary<string, object>;
            var s = new SkillData
            {
                id = (int)Art.Num(d, "id"),
                maxLevel = Mathf.Max(1, (int)Art.Num(d, "maxLevel", 1)),
                masterLevel = (int)Art.Num(d, "masterLevel"),
                job = (int)Art.Num(d, "job"),
                name = d.TryGetValue("name", out var n) ? n as string : "",
                desc = d.TryGetValue("desc", out var de) ? de as string : "",
                type = d.TryGetValue("type", out var t) ? t as string : "attack",
                icon = d.TryGetValue("icon", out var ic) ? ic as string : "",
            };
            s.levels = d.TryGetValue("levels", out var lv) ? lv as Dictionary<string, object> : s.levels;
            s.autoLearn = d.TryGetValue("auto", out var au) && au != null &&
                          au.ToString().ToLowerInvariant() == "true";
            // 源端 String.wz 缺名字的技能（五六转 V 技组件）：保留 id 文本，UI 不显示空白行
            s.noName = string.IsNullOrEmpty(s.name);
            if (s.noName) s.name = s.id.ToString();
            // 前置技能：[技能id, 需要等级]，客户端加点的硬性条件（服务端不校验）
            if (d.TryGetValue("req", out var rq) && rq is List<object> reqList)
                foreach (var item in reqList)
                    if (item is List<object> reqPair && reqPair.Count >= 2)
                        s.req.Add(new[] { int.Parse(reqPair[0].ToString()), int.Parse(reqPair[1].ToString()) });
            // 有 mad（魔法攻击力）的技能就是魔法技能，原版伤害公式按这个分流
            foreach (var row in s.levels.Values)
                if (row is Dictionary<string, object> rd && rd.ContainsKey("mad")) { s.magic = true; break; }
            byId[s.id] = s;
        }

        // JobTree 的 jobs[] 是「一职业一线」；合成旧结构（line.tabs[0] = 该职业全部技能），
        // 让技能窗/推荐技能等既有消费方不用改数据形状。
        foreach (var item in (List<object>)root["jobs"])
        {
            if (!(item is Dictionary<string, object> d)) continue;
            int id = Art.Int(d, "id");
            var skillIds = new List<object>();
            if (d.TryGetValue("skills", out var sk) && sk is List<object> skList) skillIds = skList;

            var tab = new Dictionary<string, object>
            {
                { "job", id },
                { "name", d.TryGetValue("name", out var jn) ? jn as string : "" },
                { "book", d.TryGetValue("book", out var bk) ? bk as string : "" },
                { "skills", skillIds },
            };
            var line = new Dictionary<string, object>
            {
                { "id", id },
                { "name", d.TryGetValue("name", out var ln) ? ln as string : "" },
                { "book", d.TryGetValue("book", out var lb) ? lb as string : "" },
                { "tabs", new List<object> { tab } },
            };
            lines.Add(line);
            lineByJob[id] = line;
        }

        if (root.TryGetValue("jobNames", out var jn2)) jobNames = jn2 as Dictionary<string, object>;
    }

    public static SkillData Get(int id)
    {
        Ensure();
        return byId.TryGetValue(id, out var d) ? d : null;
    }

    public static int MaxLevelOf(int id)
    {
        var d = Get(id);
        return d == null ? 0 : d.maxLevel;
    }

    /// <summary>源端 Skill.isFourthJob()：技能所属职业 job%10==2（112/212/312…）。</summary>
    public static bool IsFourthJobSkill(int skillId) => skillId / 10000 % 10 == 2;

    /// <summary>
    /// 加点上限。源端 AssignSPProcessor.java:82：
    ///   curLevel + 1 &lt;= (skill.isFourthJob() ? player.getMasterLevel(skill) : skill.getMaxLevel())
    /// 四转技能的 masterLevel 来自 Skill.wz 的 masterLevel 节点（1121001=10、1121012=30），
    /// 再被技能册（Item/Consume/0228.img，SkillBookHandler）抬到 20/30。
    /// 没有 masterLevel 节点的技能（1121006 等）不受限，直接用 maxLevel。
    /// </summary>
    public static int MasterCapOf(int id)
    {
        var d = Get(id);
        if (d == null) return 0;
        int cap = d.maxLevel;
        if (IsFourthJobSkill(d.id) && d.masterLevel > 0) cap = Mathf.Min(cap, d.masterLevel);
        int book = SkillBookItems.MasterLevelFor(d.id);
        return book > cap ? book : cap;
    }

    // 当前职业对应哪条线。cls 字符串 → job id（见 JobDefs.LegacyCls）。
    // 新手（job 0）技能固定为 团队治疗/疾风步/蜗牛投掷术/精灵的祝福；
    // 页签名取自 String.wz/Skill.img/<job>/bookName。
    public static Dictionary<string, object> LineFor(string cls) => LineForJob(JobDefs.JobIdOf(cls));

    public static Dictionary<string, object> LineForJob(int jobId)
    {
        Ensure();
        if (lineByJob.TryGetValue(jobId, out var line)) return line;
        var known = new List<string>();
        foreach (var l in lines) known.Add(l.TryGetValue("id", out var id) ? id.ToString() : "?");
        Debug.LogWarning("[SkillBook] 没有职业线 " + jobId + "，可用：" + string.Join(",", known.ToArray()));
        return lines.Count > 0 ? lines[0] : null;
    }

    // MiniJson 的数组统一是 List<object>，元素是 Dictionary<string,object>；
    // 直接强转 List<Dictionary<..>> 会抛 InvalidCastException，只能逐个转。
    public static List<Dictionary<string, object>> Tabs(Dictionary<string, object> line)
    {
        var result = new List<Dictionary<string, object>>();
        if (line == null || !line.TryGetValue("tabs", out var raw) || !(raw is List<object> list)) return result;
        foreach (var item in list)
            if (item is Dictionary<string, object> tab) result.Add(tab);
        return result;
    }

    public static string JobName(int job)
    {
        Ensure();
        return jobNames.TryGetValue(job.ToString(), out var v) ? v as string : job.ToString();
    }

    // 原版等级数据 → NativeSkillDefinition（伤害/命中数/范围全部来自 WZ 对应等级）
    public static NativeSkillDefinition Native(int id, int level)
    {
        var d = Get(id);
        var row = d?.Level(level);
        if (row == null) return null;
        string kind = d.IsSummon ? "Summon" : d.IsBuff ? "Buff" : d.IsPassive ? "Passive" : "Attack";
        var skill = new Dictionary<string, object>
        {
            { "id", d.id }, { "kind", kind }, { "magic", d.magic },
            { "element", "" }, { "summonInterval", 1f },
        };
        var data = new Dictionary<string, object>(row);
        data["level"] = level;
        return new NativeSkillDefinition(skill, data);
    }
}

/// <summary>
/// 四转技能册：Item/Consume/0228.img/&lt;0228xxxx&gt;/info/{masterLevel, skill/&lt;n&gt;=&lt;skillid&gt;}。
/// 源端 SkillBookHandler.java:71-87 —— 用了技能册就把该技能的 masterLevel 抬到册上的值。
/// 直读 IMG，不落任何导出表。
/// </summary>
public static class SkillBookItems
{
    static readonly Dictionary<int, List<KeyValuePair<int, int>>> bySkill =
        new Dictionary<int, List<KeyValuePair<int, int>>>();   // skillId → [(itemId, masterLevel)]
    static bool tried;

    public static void Ensure()
    {
        if (tried) return;
        tried = true;
        var root = WzAssets.FileRoot("Item/Consume/0228.img");
        if (root == null) return;
        foreach (var item in root.Children)
        {
            if (!int.TryParse(item.Name, out int itemId)) continue;
            var info = item.Get("info");
            if (info == null) continue;
            int master = info.Int("masterLevel");
            var skills = info.Get("skill");
            if (master <= 0 || skills == null) continue;
            foreach (var s in skills.Children)
            {
                if (!int.TryParse(s.Value == null ? s.Name : s.Value.ToString(), out int skillId)) continue;
                if (!bySkill.TryGetValue(skillId, out var list))
                    bySkill[skillId] = list = new List<KeyValuePair<int, int>>();
                list.Add(new KeyValuePair<int, int>(itemId, master));
            }
        }
    }

    /// <summary>该技能被「已持有」的技能册抬到的上限；没有对应册子返回 0。</summary>
    public static int MasterLevelFor(int skillId)
    {
        Ensure();
        if (!bySkill.TryGetValue(skillId, out var list)) return 0;
        int best = 0;
        foreach (var kv in list)
            if (Profile.ItemCount(kv.Key) > 0 && kv.Value > best) best = kv.Value;
        return best;
    }
}

// 技能等级与技能点。等级/已花费的 SP 落在 PlayerPrefs，和键位一样是本地存档；
// 剩余 SP 是存档累计字段（PlayerProfileData.sp，Profile.sp 读取）。
//
// 源端模型（Character.levelUpGainSp / changeJob / resetStats）：
//   新手升级不给 SP（三个新手技能免费，源端靠任务/教官传授）；
//   一转起每级 +3，转职另按 JobSystem 授予（一转 +1、四转 +3）；
//   一转时 resetStats 按 1+(等级-10)*3 重算（法师 1+(等级-8)*3）。
// 本地为兼容旧存档保留「新手虚拟 SP」：等级-1、封顶 6（旧公式，见 docs 适配表）。
public static class SkillProgress
{
    public const int SpPerLevel = 3;   // 源端 level_up_sp_gain

    // 新手虚拟 SP 总量（旧客户端公式，源端新手本无 SP）
    public static int BeginnerSp => Mathf.Clamp(Profile.level - 1, 0, 6);

    public static int LevelOf(int id)
    {
        var d = SkillBook.Get(id);
        int lv = Profile.Data.SkillLevelOf(id);
        return d == null ? 0 : Mathf.Clamp(lv, 0, SkillBook.MasterCapOf(id));
    }

    public static void SetLevel(int id, int lv)
    {
        var d = SkillBook.Get(id);
        if (d == null) return;
        Profile.Data.SetSkillLevel(id, Mathf.Clamp(lv, 0, SkillBook.MasterCapOf(id)));
        Profile.Save();
    }

    // —— DB 桥接（GameSession 进游戏/存档用）——
    public static void ClearAll() { Profile.Data.ClearSkills(); Profile.Save(); }

    public static void SetLearned(int id, int lv) => SetLevel(id, lv);

    public static IEnumerable<KeyValuePair<int, int>> AllLearned()
    {
        var d = Profile.Data;
        for (int i = 0; i < d.skillIds.Count && i < d.skillLevels.Count; i++)
            if (d.skillLevels[i] > 0) yield return new KeyValuePair<int, int>(d.skillIds[i], d.skillLevels[i]);
    }

    /// <summary>总获得 SP（展示用）。新手 = 虚拟公式；其余 = 剩余 + 已花费。</summary>
    public static int TotalSp
        => Profile.JobId == 0 ? BeginnerSp : Profile.Sp + SpentSp;

    public static int SpentSp
    {
        get
        {
            int sum = 0;
            foreach (var d in AllSkills()) sum += LevelOf(d.id);
            return sum;
        }
    }

    /// <summary>
    /// 源端 AssignSPProcessor.java:71-78：新手虚拟 SP 只扣这三个技能的等级，
    ///   total = Lv(1000) + Lv(1001) + Lv(1002)；remainingSp = min(level-1, 6) - total
    /// 新手线里还有「精灵的祝福」(12) 这类自动习得技能，不能算进消耗。
    /// </summary>
    public static int BeginnerSpent
        => LevelOf(1000) + LevelOf(1001) + LevelOf(1002);

    /// <summary>可花费 SP。新手 = 虚拟总量 - 三个新手技能已点等级；其余直接读存档累计字段。</summary>
    public static int AvailableSp
        => Profile.JobId == 0 ? Mathf.Max(0, BeginnerSp - BeginnerSpent) : Mathf.Max(0, Profile.Sp);

    static readonly List<SkillData> cache = new List<SkillData>();
    static bool cacheReady;
    static int cacheJob = -1;

    static List<SkillData> AllSkills()
    {
        if (cacheReady && cacheJob == Profile.JobId) return cache;
        cacheReady = true;
        cacheJob = Profile.JobId;
        cache.Clear();
        var line = SkillBook.LineForJob(Profile.JobId);
        foreach (var tab in SkillBook.Tabs(line))
        {
            if (!tab.TryGetValue("skills", out var list)) continue;
            foreach (var raw in (List<object>)list)
            {
                var d = SkillBook.Get(int.Parse(raw.ToString()));
                if (d != null) cache.Add(d);
            }
        }
        return cache;
    }

    // 前置技能没满点时不能加（原版 req 节点）；自动习得的技能（精灵的祝福）永远不能手动加点；
    // 四转技能还要受 masterLevel 限制（技能册可突破）。新手只能点 1000/1001/1002 三个。
    public static bool CanRaise(SkillData d)
    {
        if (d == null || d.autoLearn) return false;
        if (LevelOf(d.id) >= SkillBook.MasterCapOf(d.id)) return false;
        if (AvailableSp <= 0) return false;
        if (Profile.JobId == 0 && d.id != 1000 && d.id != 1001 && d.id != 1002) return false;
        foreach (var r in d.req)
            if (LevelOf(r[0]) < r[1]) return false;
        return true;
    }

    public static bool Raise(SkillData d)
    {
        if (!CanRaise(d)) return false;
        SetLevel(d.id, LevelOf(d.id) + 1);
        if (Profile.JobId != 0) Profile.AddSp(-1);
        return true;
    }

    // 重置当前职业技能等级，已花费 SP 返还（源端 resetSkillsAndResetSP(returnSp=true)）
    public static void Reset()
    {
        int refund = 0;
        foreach (var d in AllSkills())
        {
            refund += LevelOf(d.id);
            Profile.Data.SetSkillLevel(d.id, 0);
        }
        if (Profile.JobId != 0 && refund > 0) Profile.AddSp(refund);
        Profile.Save();
    }
}

// 技能施放统一入口：技能窗双击、虚拟按键、快捷格三条路都走这里，
// 失败原因统一由 SkillSystem.Report 报到 HUD 上，不会出现"点了没反应也没提示"。
public static class SkillCast
{
    public static bool TryCast(int skillId, out string fail)
    {
        fail = null;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        var sys = player != null ? player.GetComponent<SkillSystem>() : null;
        if (sys == null) { fail = "当前无法施放技能"; return false; }

        var data = SkillBook.Get(skillId);
        if (data == null) { fail = "缺少技能数据"; return false; }
        if (data.IsPassive) { fail = "被动技能无需施放"; sys.Report(fail); return false; }
        if (SkillProgress.LevelOf(skillId) <= 0) { fail = "技能尚未学习（技能窗里加点）"; sys.Report(fail); return false; }

        if (sys.TryCastSkill(skillId)) return true;
        fail = sys.LastFailure;
        if (!string.IsNullOrEmpty(fail)) sys.Report(fail);
        return false;
    }
}

// 右上八个快捷格 → 技能 id 的绑定。0 表示空格。
// 和原版一样：技能图标从技能窗拖到格子上完成绑定，格子再被键盘键触发。
public static class QuickSlots
{
    public const int Count = 26;
    public static GameAction Action(int index) => index < 8
        ? (GameAction)((int)GameAction.Slot1 + index)
        : (GameAction)((int)GameAction.Slot9 + index - 8);
    public static int Index(GameAction action) => action >= GameAction.Slot1 && action <= GameAction.Slot8
        ? (int)action - (int)GameAction.Slot1
        : action >= GameAction.Slot9 && action <= GameAction.Slot26 ? (int)action - (int)GameAction.Slot9 + 8 : -1;
    static readonly int[] ids = new int[Count];
    static bool loaded;

    public static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        for (int i = 0; i < Count; i++) ids[i] = PlayerPrefs.GetInt("mxd.qs." + i, 0);
    }

    public static void LoadCharacter(int[] skills)
    {
        loaded = true;
        System.Array.Clear(ids, 0, ids.Length);
        System.Array.Copy(skills, ids, System.Math.Min(skills.Length, ids.Length));
    }

    public static int Id(int slot)
    {
        Ensure();
        return slot >= 0 && slot < Count ? ids[slot] : 0;
    }

    public static void Bind(int slot, int skillId)
    {
        Ensure();
        if (slot < 0 || slot >= Count) return;
        ids[slot] = skillId;
        PlayerPrefs.SetInt("mxd.qs." + slot, skillId);
        PlayerPrefs.Save();
    }

    public static void Clear(int slot) => Bind(slot, 0);

    public static void Cast(int slot)
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player == null) return;
        int id = Id(slot);
        var sys = player.GetComponent<SkillSystem>();
        if (sys == null) return;
        if (id == 0) { sys.Report("快捷格 " + (slot + 1) + " 是空的（技能窗里拖技能上去）"); return; }
        bool ok = sys.TryCastSkill(id);
        if (!ok) sys.Report(sys.LastFailure);
    }
}


// Display selectors are independent of the key-to-function map (native QuickSlot model).
public static class QuickBarKeys
{
    static readonly KeyCode[] defaults = {
        KeyCode.LeftShift, KeyCode.Insert, KeyCode.Home, KeyCode.PageUp,
        KeyCode.LeftControl, KeyCode.Delete, KeyCode.End, KeyCode.PageDown
    };
    public static KeyCode[] Defaults()
    {
        var keys = new KeyCode[QuickSlots.Count];
        System.Array.Copy(defaults, keys, defaults.Length);
        return keys;
    }
    public static KeyCode Of(int index) => NativeKeyLayout.FunctionKey((KeyCode)PlayerPrefs.GetInt("mxd.quickDisplay." + index,
        index < defaults.Length ? (int)defaults[index] : (int)KeyCode.None));
    public static KeyCode[] Copy()
    {
        var keys = new KeyCode[QuickSlots.Count];
        for (int i = 0; i < keys.Length; i++) keys[i] = Of(i);
        return keys;
    }
    public static void Save(KeyCode[] keys)
    {
        for (int i = 0; i < keys.Length; i++) PlayerPrefs.SetInt("mxd.quickDisplay." + i, (int)keys[i]);
        PlayerPrefs.Save();
    }
    public static int SkillSlot(KeyCode key)
    {
        if (key == KeyCode.None) return -1;
        for (int i = 0; i < QuickSlots.Count; i++)
            if (KeyMap.Of(QuickSlots.Action(i)) == key && QuickSlots.Id(i) > 0) return i;
        return -1;
    }
    public static GameAction? Function(KeyCode key)
    {
        if (key == KeyCode.None) return null;
        foreach (var kv in KeyMap.All)
            if (kv.Value == key && QuickSlots.Index(kv.Key) < 0) return kv.Key;
        return null;
    }
    public static bool BindSkill(int displayIndex, int skillId)
    {
        var key = Of(displayIndex);
        if (key == KeyCode.None) { BottomBar.Toast("请先在键盘设置中为此快捷格选择按键"); return false; }
        int slot = SkillSlot(key);
        if (slot < 0)
            for (int i = 0; i < QuickSlots.Count; i++)
                if (QuickSlots.Id(i) == 0 || KeyMap.Of(QuickSlots.Action(i)) == KeyCode.None) { slot = i; break; }
        if (slot < 0) { BottomBar.Toast("技能绑定已满"); return false; }
        KeyMap.Bind(QuickSlots.Action(slot), key);
        QuickSlots.Bind(slot, skillId);
        return true;
    }
}
