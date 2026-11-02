// 经验表（Server 侧）。与客户端 ExpTable 同源：Field/ExpTable.json，
// 200 级以上的扩展与 300 级上限统一走 ExpCurve（见 ExpCurve.cs）。
using System;
using System.Collections.Generic;
using System.Globalization;

public static class ServerExpTable
{
    static long[] exp;
    static int maxLevel = ExpCurve.CapLevel;

    public static void Ensure()
    {
        if (exp != null) return;
        var text = PlatformRes.LoadText("Field/ExpTable");
        var list = new List<long>();
        if (!string.IsNullOrEmpty(text))
        {
            var root = MiniJsonDb.Parse(text) as Dictionary<string, object>;
            if (root != null && root.TryGetValue("exp", out var e) && e is List<object> rows)
            foreach (var v in rows)
                list.Add(v == null ? 0L : Convert.ToInt64(v, CultureInfo.InvariantCulture));
            if (root != null && root.TryGetValue("maxLevel", out var ml) && ml != null)
            {
                long value = Convert.ToInt64(ml, CultureInfo.InvariantCulture);
                maxLevel = (int)Math.Max(1, Math.Min(ExpCurve.CapLevel, value));
            }
        }
        if (list.Count == 0) { list.Add(0); list.Add(15); list.Add(34); }
        exp = ExpCurve.Build(list.ToArray());
    }

    public static int MaxLevel { get { Ensure(); return maxLevel; } }

    // 源端 ExpTable.getExpNeededForLevel，200 级以上按 ExpCurve 的 +20%/级 扩展
    public static long GetExpNeededForLevel(int level)
    {
        Ensure();
        return ExpCurve.Needed(exp, level);
    }
}
