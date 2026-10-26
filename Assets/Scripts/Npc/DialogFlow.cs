using System.Collections.Generic;
using UnityEngine;

// 原端 UtilDlgEx 正文排版 + 滚动。
//
// 纸面几何直接取自 IMG，不猜：
//   UtilDlgEx/c  第 152 列起是白纸，第 506 列是纸面右描边
//   UtilDlgEx/t  第 23 行起是纸面顶
//   UtilDlgEx/s  第 14 行止是纸面底（= 窗口底往上 45 行）
// 正文宽度 = 纸宽 - 左右各 4px - 滚动条列（15 + 3）≈ 328，与原端 332 宽的 line 节点基本一致。
//
// 滚动条只用原版 UI/Basic.img/VScr4（15 宽，13 高的上下箭头 + 25 高的滑块），
// 所有像素都来自 IMG，不再依赖 Unity 默认皮肤，也不画任何自绘元素。
public static class DialogFlow
{
    public const float PaperX = 152f;
    public const float PaperRight = 506f;
    public const float PaperY = 23f;
    public const float PaperBottom = 45f;

    const float Pad = 4f;
    const float BarW = 15f;
    const float BarArrow = 13f;
    const float BarTile = 13f;
    const float ThumbH = 25f;
    const float LineH = 17f;
    const int FontSize = 13;

    const string BarRoot = "wz://UI/Basic.img/VScr4/enabled/";
    const string DlgRoot = "wz://UI/UIWindow.img/UtilDlgEx/";

    public sealed class Span
    {
        public DialogRun Run;
        public string Text;
        public Rect Rect;
        public Texture2D Tex;
    }

    public sealed class Row
    {
        public DialogSelection Selection;
        public Rect Rect;
    }

    public sealed class Layout
    {
        public readonly List<Span> Spans = new List<Span>();
        public float Height;
    }

    static GUIStyle plain, strong, rowStyle;
    static readonly Dictionary<char, float> plainWidth = new Dictionary<char, float>();
    static readonly Dictionary<char, float> strongWidth = new Dictionary<char, float>();
    static bool dragging;
    static float grabOffset;

    // 窗口宽度 → 纸面宽度（右侧留 529-506=23 的边框）
    public static float PaperWidth(float windowWidth) => windowWidth - PaperX - (529f - PaperRight);

    // 正文可用宽度：纸面左右各 4px，右侧整列留给滚动条
    public static float ContentWidth(float windowWidth) => PaperWidth(windowWidth) - Pad * 2f - BarW - 3f;

    public static float PaperHeight(float windowHeight) => windowHeight - PaperY - PaperBottom;

    static void EnsureStyles()
    {
        if (plain != null) return;
        var font = UiKit.Font;
        plain = new GUIStyle
        {
            font = font,
            fontSize = FontSize,
            richText = false,
            wordWrap = false,
            clipping = TextClipping.Overflow,
        };
        plain.normal.textColor = DialogText.Ink;
        strong = new GUIStyle(plain) { fontStyle = FontStyle.Bold };
        rowStyle = new GUIStyle
        {
            font = font,
            fontSize = FontSize,
            richText = false,
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft,
        };
        rowStyle.normal.textColor = new Color(.32f, .25f, .45f);
    }

    // ——————————————— 排版 ———————————————
    public static Layout Measure(DialogContent content, float width)
    {
        var layout = new Layout();
        EnsureStyles();
        if (content == null || width <= 1f) return layout;
        new Builder(layout, width).Run(content.Runs);
        return layout;
    }

    sealed class Builder
    {
        readonly Layout layout;
        readonly float maxW;
        float x, y;
        float lineH = LineH;     // 当前行高：行内出现更高的图标时按图标长高
        int lineFirst;           // 当前行第一个 Span 的下标

        public Builder(Layout layout, float maxW) { this.layout = layout; this.maxW = maxW; }

        public void Run(List<DialogRun> runs)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].Kind == DialogRunKind.Icon) AddIcon(runs[i]);
                else AddText(runs[i]);
            }
            layout.Height = layout.Spans.Count == 0 ? LineH : y + lineH;
        }

        void AddIcon(DialogRun run)
        {
            var tex = string.IsNullOrEmpty(run.Art) ? null : Art.Tex("wz://" + run.Art);
            if (tex == null)
            {
                // 取不到图就退回文字，别把内容吞掉
                if (!string.IsNullOrEmpty(run.Fallback))
                    AddText(new DialogRun { Kind = DialogRunKind.Text, Text = run.Fallback, Bold = run.Bold, Color = run.Color });
                return;
            }
            // 原端行内图标按原始像素画（道具图标 30 高、#f 的小箭头 9 高），行高跟着长
            float w = tex.width, h = tex.height;
            if (x > 0f && x + w > maxW) NewLine();
            GrowLine(h);
            layout.Spans.Add(new Span
            {
                Run = run,
                Rect = new Rect(x, y + (lineH - h) * .5f, w, h),
                Tex = tex,
            });
            x += w;
        }

        void AddText(DialogRun run)
        {
            string s = run.Text;
            if (string.IsNullOrEmpty(s)) return;
            int start = 0, i = 0;
            float segX = x;
            while (i < s.Length)
            {
                if (s[i] == '\n')
                {
                    Flush(run, s, start, i, segX);
                    NewLine();
                    i++;
                    start = i;
                    segX = x;
                    continue;
                }
                float cw = CharWidth(s[i], run.Bold);
                if (x > segX && x + cw > maxW)     // 行首不折行，避免单字比整行还宽时死循环
                {
                    Flush(run, s, start, i, segX);
                    NewLine();
                    while (i < s.Length && s[i] == ' ') i++;   // 折行处丢掉行首空格
                    start = i;
                    segX = x;
                    continue;
                }
                x += cw;
                i++;
            }
            Flush(run, s, start, i, segX);
        }

        void Flush(DialogRun run, string s, int start, int end, float segX)
        {
            if (end <= start) return;
            string text = s.Substring(start, end - start).TrimEnd(' ');
            if (text.Length == 0) return;
            float w = (run.Bold ? strong : plain).CalcSize(new GUIContent(text)).x;
            layout.Spans.Add(new Span
            {
                Run = run,
                Text = text,
                Rect = new Rect(segX, y + (lineH - LineH) * .5f, w, LineH),
            });
        }

        // 行内出现更高的图标：整行长高，行内已有内容一起挪到新的垂直中线上
        void GrowLine(float h)
        {
            if (h <= lineH) return;
            float delta = (h - lineH) * .5f;
            for (int k = lineFirst; k < layout.Spans.Count; k++)
            {
                var r = layout.Spans[k].Rect;
                r.y += delta;
                layout.Spans[k].Rect = r;
            }
            lineH = h;
        }

        void NewLine()
        {
            y += lineH;
            x = 0f;
            lineH = LineH;
            lineFirst = layout.Spans.Count;
        }
    }

    static float CharWidth(char ch, bool bold)
    {
        var cache = bold ? strongWidth : plainWidth;
        if (cache.TryGetValue(ch, out float w)) return w;
        var style = bold ? strong : plain;
        // CalcSize(" ") 会把空格当空白裁掉，用 "a a" 反推它的前进宽度
        w = ch == ' '
            ? Mathf.Max(1f, style.CalcSize(new GUIContent("a a")).x - style.CalcSize(new GUIContent("a")).x * 2f)
            : style.CalcSize(new GUIContent(ch.ToString())).x;
        cache[ch] = w;
        return w;
    }

    static List<Row> MeasureRows(List<DialogSelection> selections, float width, float top)
    {
        var rows = new List<Row>();
        if (selections == null) return rows;
        float y = top;
        for (int i = 0; i < selections.Count; i++)
        {
            var sel = selections[i];
            float h = string.IsNullOrEmpty(sel.HeaderArt)
                ? Mathf.Max(22f, rowStyle.CalcHeight(new GUIContent(sel.Text ?? ""), width - 22f) + 4f)
                : 34f;
            rows.Add(new Row { Selection = sel, Rect = new Rect(0f, y, width, h) });
            y += h;
        }
        return rows;
    }

    public static float ContentHeight(DialogContent content, float windowWidth)
    {
        if (content == null) return LineH;
        float width = ContentWidth(windowWidth);
        float h = Measure(content, width).Height;
        var rows = MeasureRows(content.Selections, width, h);
        return rows.Count > 0 ? rows[rows.Count - 1].Rect.yMax : h;
    }

    // ——————————————— 绘制 ———————————————
    // 返回 >= 0 表示玩家点了这个选项号；-1 表示没点。
    public static int DrawPane(Rect window, DialogContent content, ref float scroll)
    {
        EnsureStyles();
        if (content == null) return -1;

        float width = ContentWidth(window.width);
        var paper = new Rect(window.x + PaperX, window.y + PaperY, PaperWidth(window.width), PaperHeight(window.height));
        var inner = new Rect(paper.x + Pad, paper.y + Pad, paper.width - Pad * 2f, paper.height - Pad * 2f);

        var layout = Measure(content, width);
        var rows = MeasureRows(content.Selections, width, layout.Height);
        float contentH = rows.Count > 0 ? Mathf.Max(layout.Height, rows[rows.Count - 1].Rect.yMax) : layout.Height;
        float maxScroll = Mathf.Max(0f, contentH - inner.height);
        scroll = Mathf.Clamp(scroll, 0f, maxScroll);

        if (maxScroll > 0f) DrawBar(inner, contentH, maxScroll, ref scroll);
        else dragging = false;

        var e = Event.current;
        bool inside = inner.Contains(Pointer.Pos);
        var local = Pointer.Pos - inner.position + new Vector2(0f, scroll);
        int clicked = -1;

        if (maxScroll > 0f && inside && e.type == EventType.Repaint)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > .01f)
                scroll = Mathf.Clamp(scroll - wheel * LineH * 2f, 0f, maxScroll);
        }

        GUI.BeginClip(inner);
        for (int i = 0; i < layout.Spans.Count; i++)
        {
            var span = layout.Spans[i];
            var r = new Rect(span.Rect.x, span.Rect.y - scroll, span.Rect.width, span.Rect.height);
            if (r.yMax < 0f || r.y > inner.height) continue;
            if (span.Tex != null)
            {
                GUI.DrawTexture(r, span.Tex);
            }
            else
            {
                var style = span.Run.Bold ? strong : plain;
                style.normal.textColor = span.Run.Color;
                GUI.Label(r, span.Text, style);
            }
            if (span.Run.Kind == DialogRunKind.Link && span.Run.Link >= 0 && inside &&
                span.Rect.Contains(local) && e.type == EventType.Repaint && Pointer.Clicked)
                clicked = span.Run.Link;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var r = new Rect(row.Rect.x, row.Rect.y - scroll, row.Rect.width, row.Rect.height);
            if (r.yMax < 0f || r.y > inner.height) continue;
            if (!string.IsNullOrEmpty(row.Selection.HeaderArt))
            {
                var header = Art.Tex(DlgRoot + row.Selection.HeaderArt);
                if (header != null) GUI.DrawTexture(new Rect(r.x, r.y + 8f, header.width, header.height), header);
                continue;
            }
            bool hover = inside && row.Rect.Contains(local);
            var dot = Art.Tex(DlgRoot + (hover ? "dot0" : "dot1"));
            if (dot != null) GUI.DrawTexture(new Rect(r.x + 10f, r.y + 7f, dot.width, dot.height), dot);
            GUI.Label(new Rect(r.x + 22f, r.y + 2f, r.width - 24f, r.height), row.Selection.Text, rowStyle);
            if (hover && e.type == EventType.Repaint && Pointer.Clicked && row.Selection.Index >= 0)
                clicked = row.Selection.Index;
        }
        GUI.EndClip();

        if (clicked >= 0) Pointer.ConsumeClick();
        return clicked;
    }

    static void DrawBar(Rect inner, float contentH, float maxScroll, ref float scroll)
    {
        var bar = new Rect(inner.xMax - BarW, inner.y, BarW, inner.height);
        float trackTop = bar.y + BarArrow;
        float trackH = bar.height - BarArrow * 2f;
        if (trackH <= 0f) return;

        for (float y = trackTop; y < trackTop + trackH; y += BarTile)
            GUI.DrawTexture(new Rect(bar.x, y, BarW, Mathf.Min(BarTile, trackTop + trackH - y)), Art.Tex(BarRoot + "base"));
        var prev = new Rect(bar.x, bar.y, BarW, BarArrow);
        var next = new Rect(bar.x, bar.yMax - BarArrow, BarW, BarArrow);
        GUI.DrawTexture(prev, Art.Tex(BarRoot + "prev0"));
        GUI.DrawTexture(next, Art.Tex(BarRoot + "next0"));

        float thumbH = Mathf.Clamp(trackH * inner.height / contentH, ThumbH, trackH);
        float travel = Mathf.Max(1f, trackH - thumbH);
        var thumb = new Rect(bar.x, trackTop + travel * (scroll / maxScroll), BarW, thumbH);

        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;
        if (press)
        {
            if (prev.Contains(Pointer.Pos)) { scroll = Mathf.Clamp(scroll - LineH, 0f, maxScroll); Pointer.ConsumeClick(); }
            else if (next.Contains(Pointer.Pos)) { scroll = Mathf.Clamp(scroll + LineH, 0f, maxScroll); Pointer.ConsumeClick(); }
            else if (thumb.Contains(Pointer.Pos))
            {
                dragging = true;
                grabOffset = Pointer.Pos.y - thumb.y;
                Pointer.ConsumeClick();
            }
        }
        if (dragging)
        {
            if (Pointer.Down)
                scroll = Mathf.Clamp((Pointer.Pos.y - grabOffset - trackTop) / travel * maxScroll, 0f, maxScroll);
            else
                dragging = false;
        }

        // 拖动后按新的 scroll 重新定位滑块
        thumb.y = trackTop + travel * (scroll / maxScroll);
        GUI.DrawTexture(thumb, Art.Tex(BarRoot + "thumb0"));
    }
}
