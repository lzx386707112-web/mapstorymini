using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 离线探针：实例化 NativeMapView.Show(id) 后导出实际绘制的每个 SpriteRenderer，
// 与源端 WZ 节点逐一对照。只读资源，不修改任何 IMG。
public static class MapDrawProbe
{
    class Row
    {
        public string Name; public int Order; public Vector3 Pos;
        public float W, H; public Vector2 Pivot; public bool Enabled; public Color Color;
    }

    [MenuItem("MapleDash/MapDrawProbe")]
    public static void Menu() => Run("910000000", null);

    // -executeMethod 入口（批处理）
    public static void Main() =>
        Run(Environment.GetEnvironmentVariable("PROBE_MAPS") ?? "910000000",
            Environment.GetEnvironmentVariable("PROBE_OUT") ?? "/tmp/wzwork/probe.txt");

    public static void Run(string idList, string outPath)
    {
        var sb = new StringBuilder();
        foreach (string raw in (idList ?? "910000000").Split(','))
        {
            int id = int.Parse(raw.Trim());
            sb.AppendLine("=== MAP " + id + " ===");
            var go = new GameObject("Probe_" + id);
            var view = go.AddComponent<NativeMapView>();
            try { view.Show(id); }
            catch (Exception e)
            {
                sb.AppendLine("!! Show threw: " + e.GetType().Name + ": " + e.Message);
                sb.AppendLine(e.StackTrace);
            }
            var rows = new List<Row>();
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                rows.Add(new Row {
                    Name = sr.gameObject.name, Order = sr.sortingOrder, Pos = sr.transform.localPosition,
                    W = sr.sprite != null ? sr.sprite.rect.width : -1,
                    H = sr.sprite != null ? sr.sprite.rect.height : -1,
                    Pivot = sr.sprite != null ? sr.sprite.pivot : Vector2.zero,
                    Enabled = sr.enabled, Color = sr.color });
            sb.AppendLine("renderers=" + rows.Count);
            foreach (var r in rows)
                sb.AppendLine(string.Format("{0}\torder={1}\tpos=({2:F1},{3:F1})\tsize={4:F0}x{5:F0}\tpivot=({6:F1},{7:F1})\tenabled={8}\tcolor={9:F2},{10:F2},{11:F2},{12:F2}",
                    r.Name, r.Order, r.Pos.x * Art.PPU, -r.Pos.y * Art.PPU, r.W, r.H, r.Pivot.x, r.Pivot.y,
                    r.Enabled, r.Color.r, r.Color.g, r.Color.b, r.Color.a));
            UnityEngine.Object.DestroyImmediate(go);
        }
        string text = sb.ToString();
        Debug.Log(text);
        if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, text);
    }
}
