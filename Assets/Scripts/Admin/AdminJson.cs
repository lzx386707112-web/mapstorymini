// 后台管理（gms-ui）HTTP 层的 JSON 工具。
//
// 为什么不用 JsonUtility：它不支持 Dictionary/动态结构，而后台接口的返回体是
// 「任意字段的对象 + 数组」，只能走字典树。
// 为什么不用第三方库：Unity IL2CPP 下最稳的是纯 C# 手写；解析直接复用 DbStore 的
// MiniJsonDb（已经过单测），本文件只补序列化与取值辅助。
//
// 约束：纯 C#，不引用 UnityEngine，可被 tools/ 下的离线工程直接编译。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class AdminJson
{
    // —————————— 序列化 ——————————
    public static string Write(object value)
    {
        var sb = new StringBuilder(256);
        WriteValue(sb, value);
        return sb.ToString();
    }

    static void WriteValue(StringBuilder sb, object v)
    {
        if (v == null) { sb.Append("null"); return; }

        if (v is string s) { WriteString(sb, s); return; }
        if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
        if (v is char c) { WriteString(sb, c.ToString()); return; }

        if (v is int || v is long || v is short || v is sbyte ||
            v is uint || v is ulong || v is ushort || v is byte)
        {
            sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (v is float f) { WriteDouble(sb, f); return; }
        if (v is double d) { WriteDouble(sb, d); return; }
        if (v is decimal m) { sb.Append(m.ToString(CultureInfo.InvariantCulture)); return; }

        if (v is IDictionary<string, object> dict)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, kv.Key);
                sb.Append(':');
                WriteValue(sb, kv.Value);
            }
            sb.Append('}');
            return;
        }

        if (v is IDictionary rawDict)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry e in rawDict)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                sb.Append(':');
                WriteValue(sb, e.Value);
            }
            sb.Append('}');
            return;
        }

        if (v is IEnumerable list)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
            return;
        }

        WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
    }

    static void WriteDouble(StringBuilder sb, double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("0"); return; }
        // 整数值的 double 按整数写，避免 1 变 1.0（前端表格会显示成 1.0）
        if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
            sb.Append(((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture));
        else
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
    }

    static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20 || c == 0x2028 || c == 0x2029)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // —————————— 解析 ——————————
    public static object Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        return MiniJsonDb.Parse(json);
    }

    // 前端拦截器把 POST/PUT 的 body 包成 { requestId, data }，这里统一拆包。
    // 没包的直接返回原对象，兼容 GET 风格或测试工具直接发裸 body。
    public static object UnwrapSubmit(object body)
    {
        if (body is Dictionary<string, object> d && d.ContainsKey("data") &&
            (d.ContainsKey("requestId") || d.Count == 1 || d.Count == 2))
            return d["data"];
        return body;
    }

    // —————————— 取值辅助 ——————————
    public static Dictionary<string, object> AsDict(object o)
        => o as Dictionary<string, object>;

    public static List<object> AsList(object o)
        => o as List<object>;

    public static object Get(object o, string key)
    {
        var d = AsDict(o);
        if (d == null) return null;
        return d.TryGetValue(key, out var v) ? v : null;
    }

    public static Dictionary<string, object> GetDict(object o, string key)
        => AsDict(Get(o, key));

    public static List<object> GetList(object o, string key)
        => AsList(Get(o, key));

    public static string GetString(object o, string key, string fallback = "")
    {
        var v = Get(o, key);
        if (v == null) return fallback;
        return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    public static long GetLong(object o, string key, long fallback = 0)
    {
        var v = Get(o, key);
        if (v == null) return fallback;
        if (v is long l) return l;
        if (v is int i) return i;
        if (v is double dd) return (long)dd;
        if (v is bool bb) return bb ? 1 : 0;
        if (long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var r)) return r;
        return fallback;
    }

    public static int GetInt(object o, string key, int fallback = 0)
        => (int)GetLong(o, key, fallback);

    public static bool GetBool(object o, string key, bool fallback = false)
    {
        var v = Get(o, key);
        if (v == null) return fallback;
        if (v is bool b) return b;
        if (v is long l) return l != 0;
        if (v is double d) return Math.Abs(d) > 1e-9;
        var s = Convert.ToString(v, CultureInfo.InvariantCulture);
        if (bool.TryParse(s, out var rb)) return rb;
        return s == "1";
    }

    // —————————— 返回体（对齐源端 ResultBody） ——————————
    public const int CodeSuccess = 20000;
    public const int CodeError = -1;

    public static Dictionary<string, object> Ok(object data = null)
    {
        return new Dictionary<string, object>
        {
            { "code", CodeSuccess },
            { "message", "success" },
            { "responseId", Guid.NewGuid().ToString() },
            { "data", data },
        };
    }

    public static Dictionary<string, object> Fail(string message, int code = CodeError)
    {
        return new Dictionary<string, object>
        {
            { "code", code },
            { "message", message ?? "error" },
            { "responseId", Guid.NewGuid().ToString() },
            { "data", null },
        };
    }

    // —————————— 常用结构 ——————————
    public static Dictionary<string, object> Obj(params object[] kv)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i + 1 < kv.Length; i += 2)
            d[Convert.ToString(kv[i], CultureInfo.InvariantCulture)] = kv[i + 1];
        return d;
    }

    // 对齐 mybatis-flex Page<T>：records/pageNumber/pageSize/totalPage/totalRow
    public static Dictionary<string, object> Page(List<object> records, int pageNumber, int pageSize, long total)
    {
        int size = pageSize <= 0 ? 20 : pageSize;
        long totalPage = total <= 0 ? 0 : (total + size - 1) / size;
        return new Dictionary<string, object>
        {
            { "records", records ?? new List<object>() },
            { "pageNumber", pageNumber },
            { "pageSize", size },
            { "totalPage", totalPage },
            { "totalRow", total },
        };
    }
}
