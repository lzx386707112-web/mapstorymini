using System.Collections.Generic;
using System.Text.RegularExpressions;
using MapleDash.Wz;
using UnityEngine;

// BeiDou.exe 87EC3F / 883126 (list), 87BAE7 / 87C388 (detail).
public static class QuestWindow
{
    public static bool Visible;
    static int tab = 1, selectedId;
    static float listScroll, bodyScroll, summaryScroll;
    static bool detailVisible = true;
    static readonly HashSet<int> collapsed = new HashSet<int>();
    static readonly List<Entry> rows = new List<Entry>();
    struct Entry { public int id, area, count; }

    public static void Toggle() { Visible = !Visible; }
    public static void Select(int id) { selectedId = id; tab = Profile.QuestStatus(id); detailVisible = Visible = true; }

    public static void Draw()
    {
        if (!Visible) return;
        bool showDetail = detailVisible && selectedId > 0;
        float scale = Mathf.Min(BottomBar.S, (UiKit.W - 16f) / (showDetail ? 550f : 245f), (UiKit.H - BottomBar.Height - 16f) / 396f);
        var origin = new Vector2((UiKit.W - (showDetail ? 550 : 245) * scale) / 2f,
            Mathf.Max(0, (UiKit.H - BottomBar.Height - 396 * scale) / 2f));
        QuestUi.Begin(origin, scale);
        QuestUi.Image("Quest/backgrnd", 0, 0);
        if (QuestUi.Button("UI/Basic.img/BtClose", 229, 6)) { Visible = false; return; }
        DrawTabs();
        BuildRows();
        if (rows.Count == 0)
        {
            var notice = Art.Tex("wz://UI/UIWindow.img/Quest/notice" + tab);
            if (notice != null) QuestUi.Image("Quest/notice" + tab, (210 - notice.width) / 2 - 2, (309 - notice.height) / 2 + 50);
        }
        listScroll = QuestUi.Scroll(new Rect(225, 50, 15, 309), new Rect(10, 50, 215, 309), listScroll, Mathf.Max(0, rows.Count - 14), 1);
        int first = (int)listScroll;
        for (int i = first; i < rows.Count && i < first + 14; i++)
        {
            var entry = rows[i]; float y = 50 + (i - first) * 22;
            if (entry.id == 0)
            {
                QuestUi.Fill(new Rect(10, y - 1, 208, 19), new Color32(159, 181, 206, 255));
                bool collapseClick = QuestUi.Button("UI/Basic.img/" + (collapsed.Contains(entry.area) ? "BtMax" : "BtMin"), 13, y + 2);
                string category = WzAssets.FileRoot("Etc/QuestCategory.img")?.Text(entry.area.ToString(), "") ?? "";
                QuestUi.Label(new Rect(30, y + 3, 188, 16), category + " (" + entry.count + ")", Color.white);
                if (collapseClick || QuestUi.Click(QuestUi.Rect(10, y, 208, 19)))
                { if (!collapsed.Add(entry.area)) collapsed.Remove(entry.area); }
            }
            else
            {
                bool selected = selectedId == entry.id;
                if (selected) QuestUi.Fill(new Rect(28, y, 192, 19), new Color32(57, 96, 147, 255));
                QuestUi.Image("Quest/" + (tab == 0 ? "icon0" : tab == 2 ? "icon4" : QuestLogic.CanComplete(QuestCatalog.Get(entry.id), 0) ? "icon2/0" : "icon3/0"), 11, y + 2);
                QuestUi.Label(new Rect(30, y + 2, 192, 16), QuestUi.Fit(QuestCatalog.Get(entry.id).name, 192), selected ? Color.white : Color.black);
                if (QuestUi.Click(QuestUi.Rect(28, y, 192, 19)))
                { selectedId = entry.id; detailVisible = true; bodyScroll = summaryScroll = 0; }
            }
        }
        if (detailVisible && selectedId > 0) DrawDetail();
    }

    // CCtrlTab mode1: Basic/Tab2, fixed segment65 (87EC3F), leading/trailing4, middle8.
    static void DrawTabs()
    {
        TabPiece("left" + (tab == 0 ? 1 : 0), 3, 23);
        for (int i = 0; i < 3; i++)
        {
            float start = 7 + i * 65, end = start + 57;
            var fill = Art.Tex("wz://UI/Basic.img/Tab2/fill" + (tab == i ? 1 : 0));
            if (fill != null) GUI.DrawTexture(QuestUi.Rect(start, 23, 57, fill.height), fill);
            TabPiece(i == 2 ? "right" + (tab == i ? 1 : 0) : "middle" + (tab == i ? 1 : tab == i + 1 ? 2 : 0), end, 23);
            var label = Art.Tex("wz://UI/UIWindow.img/Quest/Tab/" + (tab == i ? "disabled/" : "enabled/") + i);
            if (label != null) GUI.DrawTexture(QuestUi.Rect(start + 57 / 2 - label.width / 2, 23 + 19 / 2 - label.height / 2 + 2, label.width, label.height), label);
            if (QuestUi.Click(QuestUi.Rect(start - 4, 23, 65, 19)) && tab != i)
            { tab = i; selectedId = 0; listScroll = bodyScroll = summaryScroll = 0; }
        }
    }

    static void TabPiece(string piece, float x, float y)
    {
        var image = Art.Tex("wz://UI/Basic.img/Tab2/" + piece);
        if (image != null) GUI.DrawTexture(QuestUi.Rect(x, y, image.width, image.height), image);
    }

    static void BuildRows()
    {
        var groups = new SortedDictionary<int, List<int>>();
        foreach (int id in QuestCatalog.Ids)
        {
            var q = QuestCatalog.Get(id);
            if (q == null || Profile.QuestStatus(id) != tab) continue;
            if (tab == 0 && (!QuestLogic.CanStart(q, 0) || !QuestLogic.NotificationDateAvailable(q.startReq, System.DateTime.Now))) continue;
            if (!groups.TryGetValue(q.area, out var list)) groups[q.area] = list = new List<int>();
            list.Add(id);
        }
        rows.Clear();
        foreach (var group in groups)
        {
            rows.Add(new Entry { area = group.Key, count = group.Value.Count });
            if (collapsed.Contains(group.Key)) continue;
            group.Value.Sort((a, b) => string.CompareOrdinal(QuestCatalog.Get(a).name, QuestCatalog.Get(b).name));
            foreach (int id in group.Value) rows.Add(new Entry { id = id });
        }
    }

    static void DrawDetail()
    {
        var q = QuestCatalog.Get(selectedId); if (q == null) return;
        bool summary = !string.IsNullOrEmpty(q.demandSummary) || !string.IsNullOrEmpty(q.summary) || !string.IsNullOrEmpty(q.rewardSummary);
        QuestUi.Image("Quest/" + (summary ? "backgrnd4" : "backgrnd2"), 245, 0);
        if (QuestUi.Button("UI/Basic.img/BtHide", 245 + 285, 6)) { detailVisible = false; return; }
        QuestUi.Label(new Rect(245 + 35, 40, 150, 18), QuestUi.Fit(q.name, 150), Color.white);
        var level = q.startReq;
        string levelText = level.maxLevel > 0 ? "等级 " + level.maxLevel + "以下" : level.level > 0 ? "等级 " + level.level + "以上" : "";
        QuestUi.Label(new Rect(245 + 23, 60, 150, 18), levelText, Color.white);
        DrawNpc(q.startReq.npc, 245 + 242, 110);
        string text = q.stateInfo.TryGetValue(Profile.QuestStatus(q.id), out var state) ? state : "";
        DrawContent(ExpandProgress(q, text), new Rect(245 + 17, 122, 265, summary ? 120 : 243), ref bodyScroll, 120);
        if (summary)
        {
            var blocks = new List<string> { "#fUI/UIWindow.img/Quest/summary#" };
            if (!string.IsNullOrEmpty(q.summary)) blocks.Add(q.summary);
            if (!string.IsNullOrEmpty(q.demandSummary)) blocks.Add(q.demandSummary);
            if (!string.IsNullOrEmpty(q.rewardSummary))
            {
                blocks.Add(" ");
                blocks.Add("#fUI/UIWindow.img/Quest/reward#");
                blocks.Add(q.rewardSummary);
            }
            for (int i = 0; i < blocks.Count; i++) blocks[i] = ExpandProgress(q, blocks[i]);
            DrawContent(blocks, new Rect(245 + 17, 248, 265, 110), ref summaryScroll);
        }
        int status = Profile.QuestStatus(q.id);
        if (status != PlayerProfileData.QuestStartedState && status != PlayerProfileData.QuestCompletedState) return;
        bool old = GUI.enabled;
        GUI.enabled = old && status == PlayerProfileData.QuestStartedState && (q.id < 1200 || q.id >= 1400);
        if (QuestUi.Button("UI/UIWindow.img/UtilDlgEx/BtQGiveup", 245 + 242, 372))
        { Profile.ResetQuest(q.id); selectedId = 0; NpcField.InvalidateMarkers(); }
        GUI.enabled = old && status == PlayerProfileData.QuestStartedState && (q.id < 1200 || q.id >= 1400) && QuestAlarm.CanTrack(q);
        if (QuestUi.Button("UI/UIWindow.img/Quest/BtAlert", 245 + 150, 372)) QuestAlarm.Track(q.id);
        GUI.enabled = old;
    }

    public static string ExpandProgress(QuestDef q, string text)
    {
        return Regex.Replace(text ?? "", @"#a(\d+)#", match =>
        {
            string value = match.Groups[1].Value, prefix = q.id.ToString();
            if (!value.StartsWith(prefix) || !int.TryParse(value.Substring(prefix.Length), out int index) || index < 1 || index > q.completeReq.mobs.Count) return "";
            var need = q.completeReq.mobs[index - 1];
            return Profile.QuestProgressInt(q.id, need.item.ToString()) + "/" + need.count;
        }).Replace(@"\r\n", "\n").Replace(@"\n", "\n");
    }

    static void DrawContent(string text, Rect view, ref float scroll, float rangeHeight)
        => DrawContent(new[] { text }, view, ref scroll, rangeHeight);

    static void DrawContent(IList<string> blocks, Rect view, ref float scroll, float rangeHeight = 110)
    {
        // 882625/8827A0: text helper inset1, measure width265; each block is laid out separately.
        // 87BAE7: clip at origin+(1,1), 263 wide; draw runs relative to the original origin.
        var layouts = new List<DialogFlow.Layout>(blocks.Count);
        float height = 0;
        foreach (string block in blocks)
        {
            var layout = DialogFlow.Measure(DialogText.Parse(block), view.width, false, 1);
            layouts.Add(layout); height += layout.Height;
        }
        // 87DCA9 reserves seven extra pixels per block in the original scroll range.
        float maximum = Mathf.Max(0, Mathf.Floor((height + blocks.Count * 7 - rangeHeight) / 8f) * 8);
        scroll = QuestUi.Scroll(new Rect(view.x + 266, view.y, 15, view.height), view, scroll, maximum, 8);
        GUI.BeginClip(QuestUi.Rect(view.x + 1, view.y + 1, view.width - 2, view.height - 2));
        float offset = 0;
        foreach (var layout in layouts)
        {
            foreach (var span in layout.Spans)
            {
                var r = new Rect((span.Rect.x - 1) * QuestUi.Scale, (span.Rect.y + offset - scroll - 1) * QuestUi.Scale,
                    span.Rect.width * QuestUi.Scale, span.Rect.height * QuestUi.Scale);
                if (span.Tex != null) GUI.DrawTexture(r, span.Tex);
                else QuestUi.LocalLabel(r, span.Text, span.Run.Color, span.Run.Bold);
            }
            offset += layout.Height;
        }
        GUI.EndClip();
    }

    static void DrawNpc(int id, float x, float y)
    {
        if (id <= 0) return;
        var npc = WzAssets.FileRoot("Npc/" + id.ToString("D7") + ".img");
        var seen = new HashSet<int>();
        while (npc?.Get("info/link") != null && seen.Add(id)) { id = npc.Int("info/link"); npc = WzAssets.FileRoot("Npc/" + id.ToString("D7") + ".img"); }
        var frames = WzAssets.Frames(npc?.Get("stand")); if (frames.Count == 0) return;
        float total = 0; foreach (var frame in frames) total += Mathf.Max(1, Mathf.Abs(frame.Int("delay", 120)));
        float age = Mathf.Repeat(Time.unscaledTime * 1000, total); var current = frames[0];
        foreach (var frame in frames) { current = frame; age -= Mathf.Max(1, Mathf.Abs(frame.Int("delay", 120))); if (age < 0) break; }
        var texture = WzAssets.Texture(current); var origin = WzAssets.Origin(current);
        if (texture != null) GUI.DrawTexture(QuestUi.Rect(x - origin.x, y - origin.y, texture.width, texture.height), texture);
    }
}

// Quest's original VScr4 controls; coordinates stay in native IMG pixels.
public static class QuestUi
{
    public static float Scale { get; private set; }
    static Vector2 origin;
    static GUIStyle style, measureStyle;
    static int dragging;
    static float grab;
    public static void Begin(Vector2 position, float scale) { origin = position; Scale = scale; }
    public static Rect Rect(float x, float y, float w, float h) => new Rect(origin.x + x * Scale, origin.y + y * Scale, w * Scale, h * Scale);
    public static void Image(string node, float x, float y)
    {
        var texture = Art.Tex("wz://UI/UIWindow.img/" + node);
        if (texture != null) GUI.DrawTexture(Rect(x, y, texture.width, texture.height), texture);
    }
    public static bool Button(string path, float x, float y)
    {
        var texture = Art.Tex("wz://" + path + "/normal/0");
        return texture != null && NativeUi.Button(Rect(x, y, texture.width, texture.height), path);
    }
    public static bool Click(Rect rect)
    {
        bool click = Event.current.type == EventType.Repaint && Pointer.Clicked && rect.Contains(Pointer.Pos);
        if (click) Pointer.ConsumeClick(); return click;
    }
    public static void Fill(Rect rect, Color color) => UiKit.Fill(Rect(rect.x, rect.y, rect.width, rect.height), color);
    public static void Label(Rect rect, string text, Color color) => LocalLabel(Rect(rect.x, rect.y, rect.width, rect.height), text, color);
    // 988CF3: reserve string2192 ("..") before truncating to the native pixel width.
    public static string Fit(string text, float width)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (measureStyle == null) measureStyle = new GUIStyle { font = UiKit.Font, fontSize = 12 };
        if (measureStyle.CalcSize(new GUIContent(text)).x <= width) return text;
        float available = width - measureStyle.CalcSize(new GUIContent("..")).x;
        int count = text.Length;
        while (count > 0 && measureStyle.CalcSize(new GUIContent(text.Substring(0, count))).x > available) count--;
        return text.Substring(0, count) + "..";
    }
    public static void LocalLabel(Rect rect, string text, Color color, bool bold = false)
    {
        if (style == null) style = new GUIStyle { font = UiKit.Font, clipping = TextClipping.Clip };
        style.fontSize = Mathf.RoundToInt(12 * Scale); style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }
    public static float Scroll(Rect bar, Rect view, float value, float maximum, float step)
    {
        const string root = "wz://UI/Basic.img/VScr4/";
        value = Mathf.Clamp(value, 0, maximum);
        bool enabled = maximum > 0;
        string state = enabled ? "enabled/" : "disabled/";
        var track = new Rect(bar.x, bar.y + 13, 15, bar.height - 26);
        for (float y = track.y; y < track.yMax; y += 13)
        {
            var texture = Art.Tex(root + state + "base"); float height = Mathf.Min(13, track.yMax - y);
            if (texture != null) GUI.DrawTextureWithTexCoords(Rect(bar.x, y, 15, height), texture, new Rect(0, 1 - height / 13, 1, height / 13));
        }
        GUI.DrawTexture(Rect(bar.x, bar.y, 15, 13), Art.Tex(root + state + (enabled ? "prev0" : "prev")));
        GUI.DrawTexture(Rect(bar.x, bar.yMax - 13, 15, 13), Art.Tex(root + state + (enabled ? "next0" : "next")));
        if (!enabled) return 0;
        float travel = Mathf.Max(1, track.height - 25);
        var thumb = Rect(bar.x, track.y + travel * value / maximum, 15, 25);
        int key = GUIUtility.GetControlID(FocusType.Passive);
        if (Event.current.type == EventType.Repaint)
        {
            if (Rect(view.x, view.y, view.width + 15, view.height).Contains(Pointer.Pos)) value -= Input.mouseScrollDelta.y * step * 2;
            if (Pointer.Pressed)
            {
                if (thumb.Contains(Pointer.Pos)) { dragging = key; grab = Pointer.Pos.y - thumb.y; Pointer.ConsumeClick(); }
                else if (Rect(bar.x, bar.y, 15, 13).Contains(Pointer.Pos)) { value -= step; Pointer.ConsumeClick(); }
                else if (Rect(bar.x, bar.yMax - 13, 15, 13).Contains(Pointer.Pos)) { value += step; Pointer.ConsumeClick(); }
            }
            if (dragging == key) { if (Pointer.Down) value = (Pointer.Pos.y - grab - origin.y - track.y * Scale) / (travel * Scale) * maximum; else dragging = 0; }
        }
        value = Mathf.Clamp(Mathf.Round(value / step) * step, 0, maximum);
        GUI.DrawTexture(Rect(bar.x, track.y + travel * value / maximum, 15, 25), Art.Tex(root + "enabled/thumb0"));
        return value;
    }
}
