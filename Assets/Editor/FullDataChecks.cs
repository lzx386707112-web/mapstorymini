using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MapleDash.Wz;

public static class FullDataChecks
{
    public static void Run()
    {
        var failures = new List<string>(); int parsed = 0, linked = 0;
        try
        {
            FieldCatalog.Ensure();
            foreach (var pair in FieldCatalog.Maps)
            {
                // Independent reader instances avoid retaining thousands of parsed maps.
                try
                {
                    using (var file = new ImgFile(Path.Combine(WzAssets.DataRoot, WzAssets.MapPath(pair.Key))))
                    {
                        if (file.Root.Get("info") == null) throw new InvalidDataException("Missing info");
                        if (file.Root.Get("info/link") != null) linked++;
                    }
                    parsed++;
                }
                catch (Exception e) { failures.Add(pair.Key + ": " + e.Message); }
            }
            Directory.CreateDirectory("Library/FullDataChecks");
            File.WriteAllLines("Library/FullDataChecks/map-parse-failures.txt", failures);
            Debug.Log("[FullDataChecks] maps=" + FieldCatalog.Maps.Count + " parsed=" + parsed + " linked=" + linked + " failures=" + failures.Count);
            foreach (int id in new[] { 100000000, 101000000, 102000000, 103000000, 104000000, 200000000, 220000000, 230000000, 240000000, 100010100, 220010000, 240010000 })
            {
                try
                {
                var map = FieldCatalog.GetMap(id);
                if (map == null) throw new Exception("Missing catalogue map " + id);
                var go = new GameObject("FullDataMap_" + id);
                try { go.AddComponent<NativeMapView>().Show(id); }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                foreach (var spawn in map.spawns)
                {
                    var clip = WzAssets.MobClip(spawn.mob.ToString(), "stand") ?? WzAssets.MobClip(spawn.mob.ToString(), "move");
                    if (clip != null) foreach (string frame in clip.frames) WzAssets.Frame(frame);
                }
                Debug.Log("[FullDataChecks] rendered map=" + id + " monsters=" + map.spawns.Count);
                }
                catch (Exception e) { failures.Add("render " + id + ": " + e.Message); Debug.LogWarning("[FullDataChecks] render failed " + id + ": " + e.Message); }
            }
            File.WriteAllLines("Library/FullDataChecks/failures.txt", failures);
            Debug.Log("[FullDataChecks] completed failures=" + failures.Count);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
