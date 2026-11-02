using System;

// 经验曲线扩展（纯 System，无 UnityEngine，Server 侧与客户端共用）。
//
// 源端 gms-server ExpTable.java 只到 199 级是有意义的（200 级起每级只涨 ~0.5%，
// 表尾还被 int 上限 2,100,000,000 削平）。本工程按需求把它改成：
//   · 等级上限 300（源端 V2.1.50 是 255）
//   · 200 级（含）以上的升级需求改成逐级在上一级基础上 +20%
//
// 200 级以上的需求已经不适用于原表（1.6e9 × 1.2^100 ≈ 1.6e17），所以这一段的
// 需求在 long 里现算，不落 JSON：JSON 只保存源端 0..199 的真实值。
// 21 亿不再是天花板，经验条 / 存档 / DB 列的宽度都要跟着改成 long/BIGINT。
public static class ExpCurve
{
    /// <summary>等级上限（源端 255 → 本工程 300）。</summary>
    public const int CapLevel = 300;

    /// <summary>从这一级（含）开始，升级需求改为按上一级递增。</summary>
    public const int GrowthFromLevel = 200;

    /// <summary>每级递增的百分比（20 = ×1.2，原文 "每级递增20%"）。</summary>
    public const int GrowthPercent = 20;

    /// <summary>
    /// 把源端表（下标 = 等级）扩成 0..CapLevel 的完整 long 表。
    /// 源端表不足 200 项时按原端规则取表尾，不臆造中间值。
    /// </summary>
    public static long[] Build(long[] source)
    {
        var table = new long[CapLevel + 1];
        int last = source == null || source.Length == 0 ? -1 : source.Length - 1;
        for (int level = 0; level <= CapLevel; level++)
        {
            if (level < GrowthFromLevel)
            {
                table[level] = last < 0 ? 15 : source[Math.Min(level, last)];
                continue;
            }
            long previous = Math.Max(1, table[level - 1]);
            table[level] = previous + previous / 100 * GrowthPercent + previous % 100 * GrowthPercent / 100;
        }
        return table;
    }

    /// <summary>升级需求；超出上限取上限那一级的值（与源端 clamp 到表尾同义）。</summary>
    public static long Needed(long[] table, int level)
    {
        if (table == null || table.Length == 0) return 15;
        return table[Math.Max(0, Math.Min(level, table.Length - 1))];
    }
}
