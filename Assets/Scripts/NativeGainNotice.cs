using System.Collections.Generic;
using UnityEngine;

// CUIStatusBar gain log: 89AE9A / 89B159 / 89B60F (original 800x600).
public static class NativeGainNotice
{
    sealed class Line { public string text; public float time; public Color color; }
    static readonly List<Line> lines = new List<Line>();
    static GUIStyle style;
    public static void Clear() => lines.Clear();
    public static void Add(string text, bool yellow = false)
    {
        if (lines.Count == 6) lines.RemoveAt(0);
        lines.Add(new Line { text = text, time = Time.unscaledTime,
            color = yellow ? new Color32(255,255,32,255) : Color.white });
    }
    public static void Experience(long amount) { if (amount > 0) Add("获得了经验值（+" + amount + "）"); }
    public static void Pickup(int id, long amount)
    {
        if (amount <= 0) return;
        string name = id == 0 ? "金币" : FieldCatalog.Items.TryGetValue(id, out var item) && !string.IsNullOrEmpty(item.name) ? item.name : id.ToString();
        Add("获得了" + name + "（+" + amount + "）");
    }
    public static void Draw()
    {
        lines.RemoveAll(line => Time.unscaledTime - line.time >= 6f);
        if (lines.Count == 0) return;
        float s = BottomBar.S;
        if (style == null) style = new GUIStyle { padding = new RectOffset(), alignment = TextAnchor.UpperRight };
        style.font = UiKit.Font; style.fontSize = Mathf.Max(1, Mathf.RoundToInt(12*s));
        float y = UiKit.H - (BottomBar.ChatExpanded ? 235 : 157)*s + (6-lines.Count)*14*s;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i]; float alpha = Mathf.Clamp01(1f - (Time.unscaledTime-line.time)/6f);
            var r = new Rect(UiKit.W-296*s, y+14*i*s, 290*s, 14*s);
            style.normal.textColor = new Color(0,0,0,alpha);
            GUI.Label(new Rect(r.x+s,r.y+s,r.width,r.height),line.text,style);
            var color=line.color; color.a=alpha; style.normal.textColor=color;
            GUI.Label(r,line.text,style);
        }
    }
}
