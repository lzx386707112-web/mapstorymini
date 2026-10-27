// 后台列表里的名字解析（怪物/道具/任务/NPC/技能名）。
//
// 源端是 ItemInformationProvider / LifeFactory / Quest 直接查 WZ；本工程的 WZ 解析器
// （WzNames / ItemCatalog）在 Unity 侧，而 Admin 模块要能被 tools/admin-check 离线编译，
// 所以这里只留函数槽：运行期由 AdminHost 注入真实实现，离线自检用 "ID: xxx" 兜底。
using System;

public static class AdminNames
{
    public static Func<int, string> Mob;
    public static Func<int, string> Npc;
    public static Func<int, string> Item;
    public static Func<int, string> ItemDesc;
    public static Func<int, string> Quest;
    public static Func<int, string> Map;
    public static Func<int, string> Skill;

    public static string MobName(int id) => Resolve(Mob, id);
    public static string NpcName(int id) => Resolve(Npc, id);
    public static string ItemName(int id) => Resolve(Item, id);
    public static string QuestName(int id) => Resolve(Quest, id);
    public static string MapName(int id) => Resolve(Map, id);
    public static string SkillName(int id) => Resolve(Skill, id);

    public static string ItemDescription(int id)
    {
        if (id <= 0) return "";
        try { return ItemDesc != null ? (ItemDesc(id) ?? "") : ""; }
        catch { return ""; }
    }

    static string Resolve(Func<int, string> fn, int id)
    {
        if (id <= 0) return "";
        try
        {
            string name = fn != null ? fn(id) : null;
            return string.IsNullOrEmpty(name) ? id.ToString() : name;
        }
        catch { return id.ToString(); }
    }

    // 源端 DropService.getItemCategory 的中文分类（后台掉落/怪物掉落共用）
    public static string ItemCategory(int id)
    {
        if (id <= 0) return "金币";
        switch (id / 1000000)
        {
            case 1: return "装备";
            case 2: return "消耗";
            case 3: return "设置";
            case 4: return "其他";
            case 5: return "特殊";
            default: return "未知";
        }
    }
}
