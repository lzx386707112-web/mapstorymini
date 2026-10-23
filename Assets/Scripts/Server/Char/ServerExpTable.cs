// 经验表（Server 侧）。与客户端 ExpTable 同源：Field/ExpTable.json。
using System;
using System.Collections.Generic;
using System.Globalization;

public static class ServerExpTable
{
    static int[] exp;
    static int maxLevel = 255;

    public static void Ensure()
    {
        if (exp != null) return;
        var text = PlatformRes.LoadText("Field/ExpTable");
        var list = new List<int>();
        if (!string.IsNullOrEmpty(text))
        {
            var root = MiniJsonDb.Parse(text) as Dictionary<string, object>;
            if (root != null && root.TryGetValue("exp", out var e) && e is List<object> rows)
            foreach (var v in rows)
            {
                double d = v is double dd ? dd : v is long ll ? ll : v is int ii ? ii : 0d;
                list.Add((int)d);
            }
            if (root != null && root.TryGetValue("maxLevel", out var ml) && ml != null)
                maxLevel = (int)Convert.ToInt64(ml, CultureInfo.InvariantCulture);
        }
        if (list.Count == 0) { list.Add(0); list.Add(15); list.Add(34); }
        exp = list.ToArray();
    }

    public static int MaxLevel { get { Ensure(); return maxLevel; } }

    // 源端 ExpTable.getExpNeededForLevel：超出表尾 clamp 到最后一项
    public static int GetExpNeededForLevel(int level)
    {
        Ensure();
        if (level < 0) level = 0;
        if (level >= exp.Length) level = exp.Length - 1;
        return exp[level];
    }
}
