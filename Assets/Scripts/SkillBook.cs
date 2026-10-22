using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 技能书：把 Skill.wz 的整棵职业技能树搬到运行时。
//
// 数据来自 tools/wz_ui_bar.py 导出的 UI/JobSkills.json：
//   jobs[].tabs[].skills[]  —— 页签（职业转职阶段）与它下面的技能 id
//   skills[<id>]            —— 中文名、说明、上限等级、类型、前置、逐级数值、图标
// 逐级数值是 WZ 里 level/N 的原始字段（mpCon / damage / attackCount / cooltime …），
// 没有任何本地估算。
//
// 技能点：服务端 game_config 的 level_up_sp_gain = 3（新手不受此影响），
// 本地按 (等级-1)*3 累计，已在 docs 的适配表登记。
// ————————————————————————————————————————————————————————————

public sealed class SkillData
{
    public int id, maxLevel, job;
    public string name, desc, type, icon;
    public bool magic;
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
    static Dictionary<string, object> jobNames = new Dictionary<string, object>();

    public static void Ensure()
    {
        if (Ready) return;
        Ready = true;
        var asset = Resources.Load<TextAsset>("UI/JobSkills");
        if (asset == null) { Debug.LogError("[SkillBook] 缺少 UI/JobSkills.json"); return; }
        var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
        if (root == null) { Debug.LogError("[SkillBook] JobSkills.json 解析失败"); return; }

        // skills 是 JSON 对象（id → 数据），MiniJson 解析成 Dictionary
        foreach (var pair in (Dictionary<string, object>)root["skills"])
        {
            var d = pair.Value as Dictionary<string, object>;
            var s = new SkillData
            {
                id = (int)Art.Num(d, "id"),
                maxLevel = Mathf.Max(1, (int)Art.Num(d, "maxLevel", 1)),
                job = (int)Art.Num(d, "job"),
                name = d.TryGetValue("name", out var n) ? n as string : "",
                desc = d.TryGetValue("desc", out var de) ? de as string : "",
                type = d.TryGetValue("type", out var t) ? t as string : "attack",
                icon = d.TryGetValue("icon", out var ic) ? ic as string : "",
            };
            s.levels = d.TryGetValue("levels", out var lv) ? lv as Dictionary<string, object> : s.levels;
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
        foreach (var l in (List<object>)root["jobs"]) lines.Add(l as Dictionary<string, object>);
        if (root.TryGetValue("jobNames", out var jn)) jobNames = jn as Dictionary<string, object>;
    }

    public static SkillData Get(int id)
    {
        Ensure();
        return byId.TryGetValue(id, out var d) ? d : null;
    }

    // 当前职业对应哪条线（ClassDefs 的 5 个职业 → 原版职业线 id）
    public static Dictionary<string, object> LineFor(string cls)
    {
        Ensure();
        foreach (var line in lines)
            if (line.TryGetValue("id", out var id) && (string)id == cls) return line;
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

// 技能等级与技能点。等级/已花费的 SP 落在 PlayerPrefs，和键位一样是本地存档。
public static class SkillProgress
{
    public const int SpPerLevel = 3;   // 服务端 level_up_sp_gain

    public static int LevelOf(int id)
    {
        var d = SkillBook.Get(id);
        int lv = PlayerPrefs.GetInt("mxd.skill." + id, 0);
        return d == null ? 0 : Mathf.Clamp(lv, 0, d.maxLevel);
    }

    public static void SetLevel(int id, int lv)
    {
        var d = SkillBook.Get(id);
        if (d == null) return;
        PlayerPrefs.SetInt("mxd.skill." + id, Mathf.Clamp(lv, 0, d.maxLevel));
        PlayerPrefs.Save();
    }

    public static int TotalSp => SpPerLevel * Mathf.Max(0, Profile.level - 1);

    public static int SpentSp
    {
        get
        {
            int sum = 0;
            foreach (var d in AllSkills()) sum += LevelOf(d.id);
            return sum;
        }
    }

    public static int AvailableSp => Mathf.Max(0, TotalSp - SpentSp);

    static readonly List<SkillData> cache = new List<SkillData>();
    static bool cacheReady;
    static string cacheLine;

    static List<SkillData> AllSkills()
    {
        if (cacheReady && cacheLine == Profile.cls) return cache;
        cacheReady = true;
        cacheLine = Profile.cls;
        cache.Clear();
        var line = SkillBook.LineFor(Profile.cls);
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

    // 前置技能没满点时不能加（原版 req 节点）
    public static bool CanRaise(SkillData d)
    {
        if (d == null || LevelOf(d.id) >= d.maxLevel) return false;
        if (AvailableSp <= 0) return false;
        foreach (var r in d.req)
            if (LevelOf(r[0]) < r[1]) return false;
        return true;
    }

    public static bool Raise(SkillData d)
    {
        if (!CanRaise(d)) return false;
        SetLevel(d.id, LevelOf(d.id) + 1);
        return true;
    }

    public static void Reset()
    {
        foreach (var d in AllSkills()) PlayerPrefs.DeleteKey("mxd.skill." + d.id);
        PlayerPrefs.Save();
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
