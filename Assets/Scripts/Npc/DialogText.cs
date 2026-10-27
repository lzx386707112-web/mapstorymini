using System;
using System.Collections.Generic;
using System.Text;
using MapleDash.Wz;
using UnityEngine;

// 原端 NPC 对话文本标记解析：
//   #b #r #g #y #d #w #k   颜色开关（无结尾 #）
//   #e / #n                粗体开 / 关
//   #L<idx>#...#l          选项。原端是「正文流里的行内可点文字」，
//                          不是另起一段列表，所以这里只在流里打 Link 标记，不搬走文字。
//   #m<id># 地图名   #p<id># NPC 名   #t<id>#/#z<id># 道具名
//   #i<id># / #v<id>#  道具图标（原端就是画图标，不画名字）
//   #f<path># 图片   #h 角色名   #c<id># 持有数量   #s<string># 原样文字
public enum DialogRunKind { Text, Link, Icon }

public sealed class DialogRun
{
    public DialogRunKind Kind;
    public string Text = "";        // Text / Link：可见文字
    public int Link = -1;           // Link：回给脚本的选项号
    public bool Bold;
    public Color Color = DialogText.Ink;
    public string Art = "";         // Icon：WZ 路径（相对 Data 根）
    public string Fallback = "";    // Icon：取不到图时退回的文字
}

// 程序生成的列表行（任务菜单 / 多脚本选择）。与 #L 无关，画在正文下方。
public sealed class DialogSelection
{
    public int Index;
    public string Text;
    public string HeaderArt;
}

public sealed class DialogContent
{
    public string Body = "";        // 纯文本快照（日志/检查用）
    public readonly List<DialogRun> Runs = new List<DialogRun>();
    public readonly List<DialogSelection> Selections = new List<DialogSelection>();
}

public static class DialogText
{
    public static readonly Color Ink = new Color(0.12f, 0.12f, 0.14f);
    public static readonly Color Blue = new Color(0.29f, 0.46f, 0.78f);
    public static readonly Color Red = new Color(0.78f, 0.28f, 0.23f);
    public static readonly Color Green = new Color(0.24f, 0.58f, 0.31f);
    public static readonly Color Orange = new Color(0.75f, 0.50f, 0.19f);
    public static readonly Color Purple = new Color(0.54f, 0.35f, 0.78f);
    public static readonly Color White = Color.white;

    public static DialogContent Parse(string raw)
    {
        var content = new DialogContent();
        if (string.IsNullOrEmpty(raw)) return content;

        var buffer = new StringBuilder(raw.Length);
        var color = Ink;
        bool bold = false;
        int link = -1;

        void Flush()
        {
            if (buffer.Length == 0) return;
            string text = buffer.ToString();
            buffer.Length = 0;
            content.Runs.Add(new DialogRun
            {
                Kind = link >= 0 ? DialogRunKind.Link : DialogRunKind.Text,
                Link = link,
                Text = text,
                Bold = bold,
                Color = color,
            });
        }

        int i = 0;
        while (i < raw.Length)
        {
            char ch = raw[i];
            if (ch == '\r') { i++; continue; }
            if (ch == '\t') { buffer.Append("    "); i++; continue; }   // 原端 tab 定宽，脚本用它对列
            if (ch != '#') { buffer.Append(ch); i++; continue; }
            if (i + 1 >= raw.Length) { buffer.Append('#'); break; }

            char code = raw[i + 1];
            if (code == '#') { buffer.Append('#'); i += 2; continue; }
            if (code == 'n') { if (bold) { Flush(); bold = false; } i += 2; continue; }
            if (code == 'e') { if (!bold) { Flush(); bold = true; } i += 2; continue; }
            if (code == 'l') { if (link >= 0) { Flush(); link = -1; } i += 2; continue; }

            // #L<idx>#：开始一个行内选项，文字继续留在正文流里
            if (code == 'L')
            {
                int j = i + 2;
                while (j < raw.Length && char.IsDigit(raw[j])) j++;
                if (j > i + 2 && j < raw.Length && raw[j] == '#')
                {
                    Flush();
                    int.TryParse(raw.Substring(i + 2, j - i - 2), out link);
                    i = j + 1;
                    continue;
                }
            }

            // 颜色开关：没有闭合的 #
            if (code == 'b' || code == 'r' || code == 'g' || code == 'k' || code == 'y' || code == 'd' || code == 'w')
            {
                Flush();
                color = code == 'b' ? Blue : code == 'r' ? Red : code == 'g' ? Green :
                        code == 'y' ? Orange : code == 'd' ? Purple : code == 'w' ? White : Ink;
                i += 2;
                continue;
            }

            int close = raw.IndexOf('#', i + 2);
            if (close < 0) break;
            string payload = raw.Substring(i + 2, close - i - 2);

            switch (code)
            {
                case 'h': buffer.Append(Profile.playerName); break;
                case 'o': buffer.Append(WzNames.Mob(ToInt(payload))); break;
                case 'c': buffer.Append(Profile.ItemCount(ToInt(payload))); break;
                case 'm': buffer.Append(WzNames.Map(ToInt(payload))); break;
                case 'p': buffer.Append(WzNames.Npc(ToInt(payload))); break;
                case 't':
                case 'z': buffer.Append(WzNames.Item(ToInt(payload))); break;
                case 's': buffer.Append(payload); break;
                case 'v':
                case 'i':
                    {
                        Flush();
                        int id = ToInt(payload);
                        content.Runs.Add(new DialogRun
                        {
                            Kind = DialogRunKind.Icon,
                            Art = ItemIconPath(id) ?? "",
                            Fallback = "[" + WzNames.Item(id) + "]",
                        });
                        break;
                    }
                case 'f':
                    Flush();
                    if (payload.Length > 0) content.Runs.Add(new DialogRun { Kind = DialogRunKind.Icon, Art = payload });
                    break;
                default: break;
            }
            i = close + 1;
        }
        Flush();

        var plain = new StringBuilder();
        foreach (var run in content.Runs)
            plain.Append(run.Kind == DialogRunKind.Icon ? run.Fallback : run.Text);
        content.Body = plain.ToString().Trim('\n');
        return content;
    }

    static int ToInt(string s)
    {
        int v;
        return int.TryParse(s, out v) ? v : 0;
    }

    // 道具图标节点：#i<id>#/#v<id># 在原端画的是 IMG 里的 info/icon。
    public static string ItemIconPath(int id)
    {
        if (id <= 0) return null;
        int category = id / 1000000;
        if (category == 1)
        {
            var dir = EquipCatalog.DirOf(id);
            if (!string.IsNullOrEmpty(dir)) return "Character/" + dir + "/" + id.ToString("D8") + ".img/info/icon";
            if (WeaponDefs.TryGet(id.ToString(), out _)) return "Character/Weapon/" + id.ToString("D8") + ".img/info/icon";
            return null;
        }
        string folder = category == 2 ? "Consume" : category == 3 ? "Install" :
                        category == 4 ? "Etc" : category == 5 ? "Cash" : null;
        if (folder == null) return null;
        return "Item/" + folder + "/" + (id / 10000).ToString("D4") + ".img/" +
               id.ToString("D8") + "/info/icon";
    }

    // 选项文字里的颜色/图标标记也要去掉（程序生成的列表行用不到标记，这里只兜底）
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

