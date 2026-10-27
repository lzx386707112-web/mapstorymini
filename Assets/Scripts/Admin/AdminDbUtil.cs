// 后台 DB 类接口的分页与行取值小工具（纯 C#）。
//
// 源端 DB 分页走 mybatis-flex Page<T>，形状是 records/pageNumber/pageSize/totalPage/totalRow
// （AdminJson.Page 已对齐）；入参 BasePageDTO 的 onlyTotal=只统计、notPage=不分页 两条语义
// 在 BasePageUtil.create(...).page() 里统一处理，这里照抄。
using System;
using System.Collections.Generic;

public static class AdminDbUtil
{
    public static Dictionary<string, object> Page<T>(List<T> all, int pageNo, int pageSize,
        bool onlyTotal, bool notPage, Func<T, object> toDto)
    {
        if (all == null) all = new List<T>();
        long total = all.Count;
        int p = pageNo <= 0 ? 1 : pageNo;
        int s = pageSize <= 0 ? 20 : pageSize;

        if (onlyTotal) return AdminJson.Page(new List<object>(), p, s, total);

        var records = new List<object>();
        if (notPage)
        {
            foreach (var item in all) records.Add(toDto(item));
            return AdminJson.Page(records, 1, (int)Math.Max(1, total), total);
        }

        int start = (p - 1) * s;
        for (int i = start; i < all.Count && i < start + s; i++)
            records.Add(toDto(all[i]));
        return AdminJson.Page(records, p, s, total);
    }

    public static Dictionary<string, object> Page(List<object> records, int pageNo, int pageSize,
        bool onlyTotal, bool notPage, long total)
    {
        int p = pageNo <= 0 ? 1 : pageNo;
        int s = pageSize <= 0 ? 20 : pageSize;
        if (onlyTotal) return AdminJson.Page(new List<object>(), p, s, total);
        if (notPage) return AdminJson.Page(records, 1, (int)Math.Max(1, total), total);
        return AdminJson.Page(records, p, s, total);
    }

    public static long NextId(DbStore db, string table)
    {
        long max = 0;
        foreach (var row in db.Table(table).rows)
            max = Math.Max(max, DbStore.Long(row, "id"));
        return max + 1;
    }

    // 数值列要以 long 存（DbStore 的 INTEGER），文本列以 string 存，避免 JSON 往返出 double
    public static void Set(DbRow row, string col, object value)
    {
        if (value == null) { row[col] = null; return; }
        if (value is bool b) { row[col] = b ? 1L : 0L; return; }
        if (value is string s) { row[col] = s; return; }
        if (value is double d) { row[col] = Math.Abs(d - Math.Round(d)) < 1e-9 ? (long)Math.Round(d) : (object)d; return; }
        if (value is long || value is int) { row[col] = Convert.ToInt64(value); return; }
        row[col] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string Text(DbRow row, string col)
    {
        return DbStore.Str(row, col);
    }
}
