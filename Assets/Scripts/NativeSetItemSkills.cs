using System.Collections.Generic;

// 282 套装档位授予的 activeSkill（8000xxxx 段）运行时表。
//
// 数据链已完整：Etc/SetItemInfo.img/<set>/Effect/<档>/activeSkill/<n>/{id,level}
// → String/Skill.img/<id> 的名称/描述 → AdminSetItem282.json。
// 但 EXE 的「装备变更 → 档位重算 → 技能增删」触发链未逐指令定位
// （见 .agents/skills/beidou-client-reverse/references/inventory-and-tooltip.md
//  「282 套装 Effect 的 Option / activeSkill 链」）。
//
// 本工程的实现口径：不写存档，按「当前穿着」每次重算一张授予表。
// EquipLoadout.CurrentSetBonus() 是权威刷新点（换装/进游戏后会被调用），
// SkillProgress.BaseLevelOf() 读这张表，于是卸下装备后下一次重算自动移除技能。
// 推断边界：本表的刷新时机由工程侧决定，不代表 282 EXE 的逐指令行为。
public static class NativeSetItemSkills
{
    static readonly Dictionary<int, int> granted = new Dictionary<int, int>();
    static readonly Dictionary<int, string> names = new Dictionary<int, string>();
    static int lastSignature;

    public static int Count => granted.Count;

    public static bool IsSkill(int id) => granted.ContainsKey(id);

    public static int LevelOf(int id) => granted.TryGetValue(id, out int lv) ? lv : 0;

    public static string NameOf(int id) => names.TryGetValue(id, out var n) ? n : ("技能 " + id);

    /// <summary>按当前穿着重算授予表；wornIds 未变化时直接跳过，避免每帧扫 877 套。</summary>
    public static void Refresh(IEnumerable<int> wornIds, int jobIndex)
    {
        int sig = 17;
        foreach (int id in wornIds) sig = unchecked(sig * 31 + id);
        sig = unchecked(sig * 31 + jobIndex);
        if (sig == lastSignature) return;
        lastSignature = sig;

        granted.Clear();
        names.Clear();
        foreach (var skill in AdminSetItem.ActiveSkillsFor(wornIds, jobIndex))
        {
            granted[skill.Id] = skill.Level;
            names[skill.Id] = string.IsNullOrEmpty(skill.Name) ? ("技能 " + skill.Id) : skill.Name;
        }
    }
}
