using System;
using System.Collections.Generic;

// 制作（Maker）数据表。源端没有独立的「配方」权威源：
//   ItemInformationProvider.getMakerItemEntry 读 makercreatedata / makerrecipedata，
//   而这两张表是由 MapleSkillMakerFetcher 从 Etc.wz/ItemMake.img 生成的。
// 本工程直接用 IMG 原文（tools/export_maker_data.py 导出成 Maker/MakerTable.txt），
// 与客户端 Maker 窗口显示的数据同源——源端那份 SQL 是从**另一版本**的 IMG 生成的，
// 4250000 的 req_meso 就对不上（SQL 110000 / 客户端 IMG 100000），照抄会出现
// 「界面标价 10 万、实际扣 11 万」。
//
// 约束：纯 C#，不引用 UnityEngine（tools/server-check 直接喂文本验证）。
public sealed class MakerRecipe
{
    public int itemId;
    public int category;        // 客户端分页：0 全职业 / 1 战士 / 2 法师 / 4 弓手 / 8 飞侠 / 16 海盗
    public int reqLevel;
    public int reqSkillLevel;
    public int itemNum = 1;
    public int tuc;
    public int meso;
    public int catalyst = -1;   // ItemMake.img 的 catalyst（Source.getMakerStimulant 的等价物）
    // 源端 getMakerItemEntry 不读这三列（makercreatedata 的 req_item/req_equip 也没被读），
    // 只在导出表里留档，制作流程不消费。
    public int reqItem, reqQuest, reqEquip;

    public readonly List<MakerReqItem> reqItems = new List<MakerReqItem>();      // recipe[]
    public readonly List<MakerRewardItem> rewards = new List<MakerRewardItem>(); // randomReward[]
}

public sealed class MakerReqItem
{
    public int itemId, count;
}

public sealed class MakerRewardItem
{
    public int itemId, quantity, prob;
}

public static class MakerTable
{
    /// <summary>Unity 侧注入（Resources.Load&lt;TextAsset&gt;("Maker/MakerTable")）；server-check 直接给磁盘文本。</summary>
    public static Func<string> TextProvider;

    static Dictionary<int, MakerRecipe> recipes;
    static Dictionary<int, MakerStatUp> reagents;
    static Dictionary<int, int> crystals;
    static List<MakerRecipe> ordered;

    public sealed class MakerStatUp
    {
        public string stat;
        public int value;
    }

    public static bool Loaded => recipes != null;

    public static void Invalidate()
    {
        recipes = null; reagents = null; crystals = null; ordered = null;
    }

    public static void Load(string text)
    {
        var byId = new Dictionary<int, MakerRecipe>();
        var stats = new Dictionary<int, MakerStatUp>();
        var crystalsByLeftover = new Dictionary<int, int>();
        var list = new List<MakerRecipe>();
        if (string.IsNullOrEmpty(text)) { recipes = byId; reagents = stats; crystals = crystalsByLeftover; ordered = list; return; }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 3 || line[0] == '#') continue;
            var f = line.Split('|');
            switch (f[0])
            {
                case "M":
                    if (f.Length < 12) break;
                    var recipe = new MakerRecipe
                    {
                        category = I(f[1]), itemId = I(f[2]), reqLevel = I(f[3]), reqSkillLevel = I(f[4]),
                        itemNum = I(f[5]), tuc = I(f[6]), meso = I(f[7]), catalyst = I(f[8], -1),
                        reqItem = I(f[9]), reqQuest = I(f[10]), reqEquip = I(f[11]),
                    };
                    byId[recipe.itemId] = recipe;
                    list.Add(recipe);
                    break;
                case "R":
                    if (f.Length < 4 || !byId.TryGetValue(I(f[1]), out var owner)) break;
                    owner.reqItems.Add(new MakerReqItem { itemId = I(f[2]), count = I(f[3]) });
                    break;
                case "O":
                    if (f.Length < 5 || !byId.TryGetValue(I(f[1]), out var rewardOwner)) break;
                    rewardOwner.rewards.Add(new MakerRewardItem { itemId = I(f[2]), quantity = I(f[3]), prob = I(f[4]) });
                    break;
                case "S":
                    if (f.Length < 4) break;
                    stats[I(f[1])] = new MakerStatUp { stat = f[2], value = I(f[3]) };
                    break;
                case "C":
                    if (f.Length < 3) break;
                    crystalsByLeftover[I(f[1])] = I(f[2]);
                    break;
            }
        }
        recipes = byId; reagents = stats; crystals = crystalsByLeftover; ordered = list;
    }

    static int I(string s, int fallback = 0)
        => int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : fallback;

    static void Ensure()
    {
        if (recipes != null) return;
        var text = TextProvider != null ? TextProvider() : null;
        Load(text);
    }

    public static MakerRecipe Get(int itemId)
    {
        Ensure();
        return recipes != null && recipes.TryGetValue(itemId, out var recipe) ? recipe : null;
    }

    public static MakerStatUp Reagent(int itemId)
    {
        Ensure();
        return reagents != null && reagents.TryGetValue(itemId, out var up) ? up : null;
    }

    /// <summary>ItemInformationProvider.getMakerCrystalFromLeftover：取不到回 -1。</summary>
    public static int CrystalFromLeftover(int leftoverId)
    {
        Ensure();
        return crystals != null && crystals.TryGetValue(leftoverId, out int crystal) ? crystal : -1;
    }

    /// <summary>职业位掩码（1 战士 / 2 法师 / 4 弓手 / 8 飞侠 / 16 海盗），非冒险家回 0。</summary>
    public static int CategoryOf(int job)
    {
        if (job <= 0) return 0;
        // v83 职业 id 是三段：100 战士 / 110 剑客 / 412 夜使者……百位就是职业群。
        int group = job / 100 % 10;
        return group >= 1 && group <= 5 ? 1 << (group - 1) : 0;
    }

    /// <summary>全部配方（导出顺序）。</summary>
    public static List<MakerRecipe> All()
    {
        Ensure();
        return ordered ?? new List<MakerRecipe>();
    }

    /// <summary>该职业能看到的配方：全职业（分类 0）+ 本职业分类。</summary>
    public static List<MakerRecipe> ForJob(int job)
    {
        Ensure();
        int category = CategoryOf(job);
        var result = new List<MakerRecipe>();
        if (ordered == null) return result;
        foreach (var recipe in ordered)
            if (recipe.category == 0 || recipe.category == category) result.Add(recipe);
        return result;
    }
}
