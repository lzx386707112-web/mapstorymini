using System.Collections.Generic;
using UnityEngine;

// 原端 UtilDlgEx 正文排版 + 滚动。
//
// 纸面几何直接取自 IMG，不猜：
//   UtilDlgEx/c  第 152 列起是白纸，第 506 列是纸面右描边
//   UtilDlgEx/t  第 23 行起是纸面顶
//   UtilDlgEx/s  第 14 行止是纸面底（= 窗口底往上 45 行）
// 正文测量宽 341：BeiDou.exe 9A3D07；line 素材宽度不是正文布局宽度。
//
// 滚动条只用原版 UI/Basic.img/VScr4（15 宽，13 高的上下箭头 + 25 高的滑块），
// 所有像素都来自 IMG，不再依赖 Unity 默认皮肤，也不画任何自绘元素。
public static class DialogFlow
{
    public const float PaperX = 152f;
    public const float PaperRight = 506f;
    public const float PaperY = 23f;
    public const float PaperBottom = 45f;

    const float BarW = 15f;
    const float BarArrow = 13f;
    const float BarTile = 13f;
    const float ThumbH = 25f;
    const float LineH = 16f;
    const int FontSize = 12;

    const string BarRoot = "wz://UI/Basic.img/VScr4/enabled/";
    const string DlgRoot = "wz://UI/UIWindow.img/UtilDlgEx/";

    public sealed class Span
    {
        public DialogRun Run;
        public string Text;
        public Rect Rect;
        public Texture2D Tex;
    }

    public sealed class Layout
    {
        public readonly List<Span> Spans = new List<Span>();
        public float Height;
    }

    static GUIStyle plain, strong;
    static readonly Dictionary<char, float> plainWidth = new Dictionary<char, float>();
    static readonly Dictionary<char, float> strongWidth = new Dictionary<char, float>();
    static bool dragging;
    static float grabOffset;

    // 窗口宽度 → 纸面宽度（右侧留 529-506=23 的边框）
    public static float PaperWidth(float windowWidth) => windowWidth - PaperX - (529f - PaperRight);

    // 9A3D07：正文测量宽度包含内部 8px 缩进，不预扣滚动条。
    public static float ContentWidth(float windowWidth) => 341f;

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
    }

    // ——————————————— 排版 ———————————————
    public static Layout Measure(DialogContent content, float width, bool input = false)
    {
        var layout = new Layout();
        EnsureStyles();
        if (content == null || width <= 1f) return layout;
        var runs = new List<DialogRun>(content.Runs);
        // 724BAC / 字符串 3191..3195：程序菜单也使用同一套 #f/#L 正文排版。
        foreach (var selection in content.Selections)
        {
            string raw = selection.HeaderArt != null
                ? "\n\n#fUI/UIWindow.img/UtilDlgEx/" + selection.HeaderArt + "#\n"
                : "#d#L" + selection.Index + "# " + selection.Text + "#l#k\n";
            runs.AddRange(DialogText.Parse(raw).Runs);
        }
        new Builder(layout, width, input ? 2f : 8f).Run(runs);
        return layout;
    }

    sealed class Builder
    {
        readonly Layout layout;
        readonly float maxW;
        readonly float margin;
        float x, y;
        float lineH = LineH;     // 当前行高：行内出现更高的图标时按图标长高
        int lineFirst;           // 当前行第一个 Span 的下标

        public Builder(Layout layout, float maxW, float margin)
        { this.layout = layout; this.margin = margin; this.maxW = maxW - margin; x = y = margin; }

        public void Run(List<DialogRun> runs)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].Kind == DialogRunKind.Icon) AddIcon(runs[i]);
                else AddText(runs[i]);
            }
            layout.Height = layout.Spans.Count == 0 ? LineH : y + lineH + margin + 2f;
        }

        void AddIcon(DialogRun run)
        {
            var tex = string.IsNullOrEmpty(run.Art) ? null : Art.Tex("wz://" + run.Art);
            if (tex == null)
            {
                // 取不到图就退回文字，别把内容吞掉
                if (!string.IsNullOrEmpty(run.Fallback))
                    AddText(new DialogRun { Kind = run.Link >= 0 ? DialogRunKind.Link : DialogRunKind.Text, Link = run.Link, Text = run.Fallback, Bold = run.Bold, Color = run.Color });
                return;
            }
            // 原端行内图标按原始像素画（道具图标 30 高、#f 的小箭头 9 高），行高跟着长
            float w = run.SelectionMarker ? 18f : tex.width, h = run.SelectionMarker ? LineH : tex.height;
            if (x > margin && x + w > maxW) NewLine();
            GrowLine(h);
            layout.Spans.Add(new Span
            {
                Run = run,
                Rect = new Rect(x, y + lineH - h + (run.Link >= 0 ? 10f : 0f), w, h),
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
                if (x > margin && x + cw > maxW)     // 行首不折行，避免单字比整行还宽时死循环
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
                Rect = new Rect(segX, y + lineH - LineH + (run.Link >= 0 ? 10f : 0f), w, LineH),
            });
        }

        // 9A2D95：同行按底部对齐，高图标使已有内容向下移动。
        void GrowLine(float h)
        {
            if (h <= lineH) return;
            float delta = h - lineH;
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
            y += lineH + 2f;
            x = margin;
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

    public static float ContentHeight(DialogContent content, float windowWidth, bool input = false)
        => Measure(content, ContentWidth(windowWidth), input).Height;

    // 9ACC20：超出最大正文区不足 16px 时仍扩大窗口，不启用滚动。
    public static bool NeedsScroll(float contentHeight) => (int)(contentHeight - 240f) / 8 > 1;
    public static float BodyHeight(float contentHeight) => NeedsScroll(contentHeight) ? 240f : Mathf.Max(120f, contentHeight);

    // ——————————————— 绘制 ———————————————
    // 返回 >= 0 表示玩家点了这个选项号；-1 表示没点。
    public static int DrawPane(Rect window, DialogContent content, ref float scroll, float reservedHeight = 0f, bool input = false)
    {
        EnsureStyles();
        if (content == null) return -1;

        float width = ContentWidth(window.width);
        var layout = Measure(content, width, input);
        float contentH = layout.Height + reservedHeight;
        float bodyH = window.height - 86f;
        bool scrolling = !input && NeedsScroll(contentH);
        float textX = scrolling ? 155f : 157f;
        float textY = scrolling ? 22f : 28f + (int)((bodyH - contentH - (input ? 20f : 6f)) / 2f);
        // 9A3E9F：裁剪矩形比正文起点右/下各 1，宽 339；绘制仍使用正文原点。
        var inner = new Rect(window.x + textX + 1f, window.y + textY + 1f, width - 2f, bodyH - 2f);
        float maxScroll = scrolling ? Mathf.Max(0f, Mathf.Floor((contentH - bodyH) / 8f) * 8f) : 0f;
        scroll = Mathf.Clamp(scroll, 0f, maxScroll);

        if (maxScroll > 0f) DrawBar(inner, contentH, maxScroll, ref scroll);
        else dragging = false;

        var e = Event.current;
        bool inside = inner.Contains(Pointer.Pos);
        var local = Pointer.Pos - inner.position + new Vector2(1f, scroll + 1f);
        int clicked = -1;

        if (maxScroll > 0f && inside && e.type == EventType.Repaint)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > .01f)
                scroll = Mathf.Clamp(scroll - Mathf.Round(wheel) * 16f, 0f, maxScroll);
        }

        GUI.BeginClip(inner);
        for (int i = 0; i < layout.Spans.Count; i++)
        {
            var span = layout.Spans[i];
            var r = new Rect(span.Rect.x - 1f, span.Rect.y - scroll - 1f, span.Rect.width, span.Rect.height);
            if (r.yMax < 0f || r.y > inner.height) continue;
            if (span.Tex != null)
            {
                if (span.Run.SelectionMarker)
                {
                    var texture = Art.Tex(DlgRoot + (inside && span.Rect.Contains(local) ? "dot0" : "dot1"));
                    if (texture != null) GUI.DrawTexture(new Rect(r.x + 10f, r.y + 2f, texture.width, texture.height), texture);
                }
                else GUI.DrawTexture(r, span.Tex);
            }
            else
            {
                var style = span.Run.Bold ? strong : plain;
                style.normal.textColor = span.Run.Color;
                GUI.Label(r, span.Text, style);
            }
            if (span.Run.Link >= 0 && inside &&
                span.Rect.Contains(local) && e.type == EventType.Repaint && Pointer.Clicked)
                clicked = span.Run.Link;
        }

        GUI.EndClip();

        if (clicked >= 0) Pointer.ConsumeClick();
        return clicked;
    }

    static void DrawBar(Rect inner, float contentH, float maxScroll, ref float scroll)
    {
        var bar = new Rect(inner.x + 337f, inner.y + 1f, BarW, inner.height + 5f);
        float trackTop = bar.y + BarArrow;
        float trackH = bar.height - BarArrow * 2f;
        if (trackH <= 0f) return;

        var baseTexture = Art.Tex(BarRoot + "base");
        for (float y = trackTop; y < trackTop + trackH; y += BarTile)
        {
            float height = Mathf.Min(BarTile, trackTop + trackH - y);
            if (baseTexture != null) GUI.DrawTextureWithTexCoords(new Rect(bar.x, y, BarW, height), baseTexture,
                new Rect(0f, 1f - height / BarTile, 1f, height / BarTile));
        }
        var prev = new Rect(bar.x, bar.y, BarW, BarArrow);
        var next = new Rect(bar.x, bar.yMax - BarArrow, BarW, BarArrow);
        GUI.DrawTexture(prev, Art.Tex(BarRoot + "prev0"));
        GUI.DrawTexture(next, Art.Tex(BarRoot + "next0"));

        float thumbH = Mathf.Min(ThumbH, trackH);
        float travel = Mathf.Max(1f, trackH - thumbH);
        var thumb = new Rect(bar.x, trackTop + travel * (scroll / maxScroll), BarW, thumbH);

        bool press = Event.current.type == EventType.Repaint && Pointer.Pressed;
        if (press)
        {
            if (prev.Contains(Pointer.Pos)) { scroll = Mathf.Clamp(scroll - 8f, 0f, maxScroll); Pointer.ConsumeClick(); }
            else if (next.Contains(Pointer.Pos)) { scroll = Mathf.Clamp(scroll + 8f, 0f, maxScroll); Pointer.ConsumeClick(); }
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
                scroll = Mathf.Clamp(Mathf.Round((Pointer.Pos.y - grabOffset - trackTop) / travel * maxScroll / 8f) * 8f, 0f, maxScroll);
            else
                dragging = false;
        }

        // 拖动后按新的 scroll 重新定位滑块
        thumb.y = trackTop + travel * (scroll / maxScroll);
        GUI.DrawTexture(thumb, Art.Tex(BarRoot + "thumb0"));
    }
}
