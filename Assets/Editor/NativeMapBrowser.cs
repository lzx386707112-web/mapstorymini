using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Browse the complete Data catalogue without parsing all maps at startup.
public sealed class NativeMapBrowser : EditorWindow
{
    string query = "";
    Vector2 scroll;
    [MenuItem("MapleDash/地图目录（完整 Data）")]
    static void Open() => GetWindow<NativeMapBrowser>("原始地图目录");
    void OnGUI()
    {
        if (GUILayout.Button("读取 Data 地图目录")) FieldCatalog.Ensure();
        query = EditorGUILayout.TextField("ID / 地图名", query);
        if (!FieldCatalog.Ready) return;
        EditorGUILayout.LabelField("地图总数", FieldCatalog.Maps.Count.ToString());
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var map in FieldCatalog.Maps.Values.OrderBy(m => m.id))
        {
            string label = map.id.ToString("D9") + "  " + map.street + " / " + map.name;
            if (query.Length > 0 && label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!GUILayout.Button(label)) continue;
            try
            {
                if (Application.isPlaying && GameLoop.I != null && GameLoop.I.field != null)
                    GameLoop.I.field.Enter(map.id, null);
                else
                {
                    PlayerPrefs.SetInt("debug_field_map", map.id);
                    PlayerPrefs.SetInt("autostart", 1); PlayerPrefs.Save();
                    EditorApplication.EnterPlaymode();
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            break;
        }
        EditorGUILayout.EndScrollView();
    }
}
