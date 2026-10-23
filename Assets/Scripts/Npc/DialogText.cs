using System;
using System.Collections.Generic;
using System.Text;
using MapleDash.Wz;
using UnityEngine;

// 原端 NPC 对话文本标记解析：
//   #b #r #g #k   颜色开关（无结尾 #）
//   #L<idx>#...#l 选项
//   #m<id># 地图名   #p<id># NPC 名   #t<id># 道具名   #v/#i<id># 道具图标
//   #f<path># 图片   （单机版无对应资源时直接略去）
// 解析结果给 IMGUI 用：正文用 rich text 颜色，选项单独成行。
public sealed class DialogSelection
{
    public int Index;
    public string Text;
    public string HeaderArt;
}

public sealed class DialogContent
{
    public string Body = "";
    public readonly List<DialogSelection> Selections = new List<DialogSelection>();
}

public static class DialogText
{
    const string CBlue = "#4A76C8";
    const string CRed = "#C8483C";
    const string CGreen = "#3E9450";
    const string COrange = "#C08030";
    const string CPurple = "#8A5AC8";
    const string CDefault = "#1E1E1E";

    public static DialogContent Parse(string raw)
    {
        var content = new DialogContent();
        if (string.IsNullOrEmpty(raw)) return content;

        var body = new StringBuilder(raw.Length);
        string color = null;
        bool bold = false;

        int i = 0;
        while (i < raw.Length)
        {
            char ch = raw[i];
            if (ch == '\r') { i++; continue; }
            if (ch == '\n') { body.Append('\n'); i++; continue; }
            if (ch != '#') { body.Append(ch); i++; continue; }
            if (i + 1 >= raw.Length) { body.Append('#'); break; }

            char code = raw[i + 1];
            if (code == '#') { body.Append('#'); i += 2; continue; }
            if (code == 'l') { i += 2; continue; }
            if (code == 'e' || code == 'n')
            {
                if (bold) body.Append("</b>");
                bold = code == 'e';
                if (bold) body.Append("<b>");
                i += 2;
                continue;
            }

            // #L<idx># 选项
            if (code == 'L' || code == 'l')
            {
                int j = i + 2;
                while (j < raw.Length && char.IsDigit(raw[j])) j++;
                if (j < raw.Length && raw[j] == '#')
                {
                    int idx;
                    int.TryParse(raw.Substring(i + 2, j - i - 2), out idx);
                    int end = raw.IndexOf("#l", j + 1, StringComparison.Ordinal);
                    string text = end < 0 ? raw.Substring(j + 1) : raw.Substring(j + 1, end - j - 1);
                    content.Selections.Add(new DialogSelection { Index = idx, Text = StripMarkup(text).Trim() });
                    i = end < 0 ? raw.Length : end + 2;
                    body.Append('\n');
                    continue;
                }
            }

            // 颜色开关：没有闭合的 #
            if (code == 'b' || code == 'r' || code == 'g' || code == 'k' || code == 'y' || code == 'd' || code == 'w')
            {
                if (bold) body.Append("</b>");
                if (color != null) body.Append("</color>");
                switch (code)
                {
                    case 'b': color = CBlue; break;
                    case 'r': color = CRed; break;
                    case 'g': color = CGreen; break;
                    case 'y': color = COrange; break;
                    case 'd': color = CPurple; break;
                    case 'w': color = "#FFFFFF"; break;
                    case 'k': color = null; break;
                    default: break;
                }
                if (color != null) body.Append("<color=" + color + ">");
                if (bold) body.Append("<b>");
                i += 2;
                continue;
            }

            int close = raw.IndexOf('#', i + 2);
            if (close < 0) { i = raw.Length; break; }
            string payload = raw.Substring(i + 2, close - i - 2);
            string head = code.ToString();

            switch (head)
            {
                case "h": body.Append(Profile.playerName); break;
                case "o": body.Append(WzNames.Mob(ToInt(payload))); break;
                case "c": body.Append(Profile.ItemCount(ToInt(payload))); break;
                case "m": body.Append(WzNames.Map(ToInt(payload))); break;
                case "p": body.Append(WzNames.Npc(ToInt(payload))); break;
                case "t": body.Append(WzNames.Item(ToInt(payload))); break;
                case "z": body.Append(WzNames.Item(ToInt(payload))); break;
                case "v":
                case "i": body.Append("[" + WzNames.Item(ToInt(payload)) + "]"); break;
                case "f": break;                                    // UI 图片：略
                case "s": body.Append(payload); break;
                default: break;
            }
            i = close + 1;
        }
        if (bold) body.Append("</b>");
        if (color != null) body.Append("</color>");
        content.Body = body.ToString().Trim('\n');
        return content;
    }

    static int ToInt(string s)
    {
        int v;
        return int.TryParse(s, out v) ? v : 0;
    }

    // 选项文字里的颜色/图标标记也要去掉
    public static string StripMarkup(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            char ch = raw[i];
            if (ch != '#') { sb.Append(ch); i++; continue; }
            if (i + 1 >= raw.Length) break;
            char code = raw[i + 1];
            if (code == '#') { sb.Append('#'); i += 2; continue; }
            if (code == 'l') { i += 2; continue; }
            if (code == 'e' || code == 'n') { i += 2; continue; }
            if (code == 'b' || code == 'r' || code == 'g' || code == 'k' || code == 'y' || code == 'd' || code == 'w')
            {
                i += 2;
                continue;
            }
            int close = raw.IndexOf('#', i + 2);
            if (close < 0) break;
            string payload = raw.Substring(i + 2, close - i - 2);
            if (code == 'm') sb.Append(WzNames.Map(ToInt(payload)));
            else if (code == 'h') sb.Append(Profile.playerName);
            else if (code == 'c') sb.Append(Profile.ItemCount(ToInt(payload)));
            else if (code == 'o') sb.Append(WzNames.Mob(ToInt(payload)));
            else if (code == 'p') sb.Append(WzNames.Npc(ToInt(payload)));
            else if (code == 't' || code == 'z') sb.Append(WzNames.Item(ToInt(payload)));
            else if (code == 'v' || code == 'i') sb.Append("[" + WzNames.Item(ToInt(payload)) + "]");
            i = close + 1;
        }
        return sb.ToString();
    }
}

// WZ 名字查询（String.wz）。带缓存，避免每帧读 IMG。
public static class WzNames
{
    static readonly Dictionary<int, string> npc = new Dictionary<int, string>();
    static readonly Dictionary<int, string> map = new Dictionary<int, string>();
    static readonly Dictionary<int, string> item = new Dictionary<int, string>();
    static readonly Dictionary<int, string> mob = new Dictionary<int, string>();

    public static string Npc(int id)
    {
        if (npc.TryGetValue(id, out var v)) return v;
        v = WzAssets.FileRoot("String/Npc.img").Get(id.ToString())?.Text("name");
        if (string.IsNullOrEmpty(v)) v = "NPC " + id;
        npc[id] = v;
        return v;
    }

    public static string Mob(int id)
    {
        if (mob.TryGetValue(id, out var v)) return v;
        v = WzAssets.FileRoot("String/Mob.img").Get(id.ToString())?.Text("name");
        if (string.IsNullOrEmpty(v)) v = "怪物 " + id;
        mob[id] = v;
        return v;
    }

    public static string Map(int id)
    {
        if (map.TryGetValue(id, out var v)) return v;
        v = null;
        var root = WzAssets.FileRoot("String/Map.img");
        foreach (var region in root.Children)
        {
            var entry = region.Get(id.ToString());
            if (entry == null) continue;
            v = entry.Text("mapName");
            if (!string.IsNullOrEmpty(v)) break;
        }
        if (string.IsNullOrEmpty(v)) v = "地图 " + id;
        map[id] = v;
        return v;
    }

    public static string Item(int id)
    {
        if (item.TryGetValue(id, out var v)) return v;
        v = ItemUncached(id);
        if (string.IsNullOrEmpty(v)) v = "道具 " + id;
        item[id] = v;
        return v;
    }

    static string ItemUncached(int id)
    {
        int cat = id / 1000000;
        try
        {
            if (cat == 1)
            {
                // Eqp.img/<类别>/<id>/name；类别名未知时遍历一层
                var eqp = WzAssets.FileRoot("String/Eqp.img");
                foreach (var group in eqp.Children)
                {
                    var n = group.Get(id.ToString())?.Text("name");
                    if (!string.IsNullOrEmpty(n)) return n;
                }
                return null;
            }
            string file = cat == 2 ? "Consume" : cat == 3 ? "Ins" : cat == 4 ? "Etc" : cat == 5 ? "Cash" : null;
            if (file == null) return null;
            return WzAssets.FileRoot("String/" + file + ".img").Get(id.ToString())?.Text("name");
        }
        catch { return null; }
    }
}
