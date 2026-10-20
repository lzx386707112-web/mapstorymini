using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 极简 JSON 解析器：零依赖。
// 为什么不用 Unity 自带的 JsonUtility：它不支持字典，
// 而 chapter1.json 里的 themes / bosses 都是「键 → 对象」的映射。
// 装 Newtonsoft 要走 UPM 联网拉包，这里自己写一个够用的更稳。
//
// 返回类型：Dictionary<string,object> / List<object> / string / double / bool / null
public static class MiniJson
{
    public static object Parse(string src)
    {
        if (string.IsNullOrEmpty(src)) return null;
        int i = 0;
        var v = ParseValue(src, ref i);
        SkipWs(src, ref i);
        if (i < src.Length)
            throw new FormatException("JSON 尾部有多余内容，位置 " + i);
        return v;
    }

    static void SkipWs(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\uFEFF') i++;
            else break;
        }
    }

    static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) throw new FormatException("JSON 意外结束");
        char c = s[i];
        switch (c)
        {
            case '{': return ParseObject(s, ref i);
            case '[': return ParseArray(s, ref i);
            case '"': return ParseString(s, ref i);
            case 't':
                Expect(s, ref i, "true");
                return true;
            case 'f':
                Expect(s, ref i, "false");
                return false;
            case 'n':
                Expect(s, ref i, "null");
                return null;
            default:
                return ParseNumber(s, ref i);
        }
    }

    static void Expect(string s, ref int i, string word)
    {
        if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            throw new FormatException("JSON 期望 " + word + "，位置 " + i);
        i += word.Length;
    }

    static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        var d = new Dictionary<string, object>();
        i++; // {
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 对象的键必须是字符串，位置 " + i);
            string key = ParseString(s, ref i);
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 缺少冒号，位置 " + i);
            i++;
            d[key] = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 对象未闭合");
            if (s[i] == ',') { i++; continue; }
            if (s[i] == '}') { i++; return d; }
            throw new FormatException("JSON 对象里出现意外字符 '" + s[i] + "'，位置 " + i);
        }
    }

    static List<object> ParseArray(string s, ref int i)
    {
        var list = new List<object>();
        i++; // [
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return list; }
        while (true)
        {
            list.Add(ParseValue(s, ref i));
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 数组未闭合");
            if (s[i] == ',') { i++; continue; }
            if (s[i] == ']') { i++; return list; }
            throw new FormatException("JSON 数组里出现意外字符 '" + s[i] + "'，位置 " + i);
        }
    }

    static string ParseString(string s, ref int i)
    {
        i++; // "
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= s.Length) throw new FormatException("JSON 字符串未闭合");
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }

            if (i >= s.Length) throw new FormatException("JSON 转义未完成");
            char e = s[i++];
            switch (e)
            {
                case '"':  sb.Append('"');  break;
                case '\\': sb.Append('\\'); break;
                case '/':  sb.Append('/');  break;
                case 'b':  sb.Append('\b'); break;
                case 'f':  sb.Append('\f'); break;
                case 'n':  sb.Append('\n'); break;
                case 'r':  sb.Append('\r'); break;
                case 't':  sb.Append('\t'); break;
                case 'u':
                    if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                    sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                    i += 4;
                    break;
                default: throw new FormatException("JSON 未知转义 \\" + e);
            }
        }
    }

    static double ParseNumber(string s, ref int i)
    {
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length)
        {
            char c = s[i];
            if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') i++;
            else break;
        }
        var span = s.Substring(start, i - start);
        double d;
        if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            throw new FormatException("JSON 数字格式错误：" + span);
        return d;
    }
}
