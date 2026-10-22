using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class NativeAssetChecks
{
    // Batch check: raw-reader geometry vs exported baseline, decode every referenced frame,
    // render screenshots without changing a scene, player preferences or save data.
    public static void Run()
    {
        GameObject cameraObject = null;
        int checkedMaps = 0;
        try
        {
            FieldCatalog.Ensure();
            cameraObject = new GameObject("DirectImgCheckCamera");
            var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true;
            camera.orthographicSize = 6.75f; camera.aspect = 16f / 9; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.4f, 0.65f, 0.9f);
            var output = Path.GetFullPath("Library/NativeAssetChecks"); Directory.CreateDirectory(output);
            foreach (var theme in new[] { "harbor", "forest", "cliff", "nest" })
            {
                var stage = Art.Stage(theme);
                var go = new GameObject("ChapterCheck_" + theme);
                try { go.AddComponent<NativeMapView>().Show(int.Parse(stage.map)); }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            var baselineRoot = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(Resources.Load<TextAsset>("Field/FieldMaps").text);
            var baselines = new System.Collections.Generic.Dictionary<int, FieldMap>();
            var build = typeof(FieldCatalog).GetMethod("BuildMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            foreach (var item in (System.Collections.Generic.List<object>)baselineRoot["maps"])
            {
                var m = (FieldMap)build.Invoke(null, new object[] { item }); baselines.Add(m.id, m);
            }
            foreach (var pair in baselines)
            {
                var raw = NativeMapView.LoadMap(pair.Value);
                var baseline = baselines[pair.Key];
                Require(raw.footholds.Count == baseline.footholds.Count, pair.Key + " foothold count");
                foreach (var f in raw.footholds)
                {
                    var expected = baseline.footholds.Find(e => e.id == f.id && e.layer == f.layer && e.group == f.group);
                    Require(expected != null && expected.x1 == f.x1 && expected.x2 == f.x2 && expected.y1 == f.y1 && expected.y2 == f.y2,
                        pair.Key + " foothold " + f.id);
                }
                Require(raw.ropes.Count == baseline.ropes.Count, pair.Key + " ropes");
                for (int i = 0; i < raw.ropes.Count; i++)
                {
                    var a = raw.ropes[i]; var b = baseline.ropes[i];
                    Require(a.x == b.x && a.y1 == b.y1 && a.y2 == b.y2 && a.ladder == b.ladder && a.upperExit == b.upperExit, pair.Key + " rope " + i);
                }
                Require(raw.portals.Count == baseline.portals.Count, pair.Key + " portals");
                for (int i = 0; i < raw.portals.Count; i++)
                {
                    var p = raw.portals[i]; var e = baseline.portals[i];
                    // Baseline FieldCatalog reads JSON numbers through float (999999999 loses precision).
                    Require(e != null && e.x == p.x && e.y == p.y && e.type == p.type && (float)e.tm == (float)p.tm && e.targetName == p.targetName, pair.Key + " portal " + p.name);
                }
                Require(raw.spawns.Count == baseline.spawns.Count, pair.Key + " spawns");
                var root = new GameObject("DirectImg_" + pair.Key);
                try
                {
                    var view = root.AddComponent<NativeMapView>(); view.Show(pair.Key);
                    Require(view.DrawCount > 0 && view.TextureCount > 0, pair.Key + " artwork");
                    if (pair.Key == 40001 || pair.Key == 0 || pair.Key == 1000000)
                    {
                        var spawn = raw.SpawnPortal ?? raw.FirstPortal;
                        camera.transform.position = new Vector3((spawn?.x ?? 0) / Art.PPU, -((spawn?.y ?? 0) - 100) / Art.PPU, -10);
                        view.UpdateBackgrounds(camera, 0);
                        var target = RenderTexture.GetTemporary(1200, 675, 24);
                        var previous = RenderTexture.active;
                        var image = new Texture2D(1200, 675, TextureFormat.RGB24, false);
                        try
                        {
                            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                            image.ReadPixels(new Rect(0, 0, 1200, 675), 0, 0); image.Apply();
                            File.WriteAllBytes(Path.Combine(output, pair.Key + ".png"), image.EncodeToPNG());
                        }
                        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
                    }
                    checkedMaps++;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            CheckActorsAndUi();
            Debug.Log("[NativeAssetChecks] PASS maps=" + checkedMaps + " geometry + all referenced map/NPC/portal animation textures; screenshots=" + output);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally { if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject); }
    }
    static void CheckActorsAndUi()
    {
        var index = WzAssets.Index;
        int aliases = 0, actorFrames = 0;
        var avatarRecords = new System.Collections.Generic.List<object>();
        var avatarDir = Path.GetFullPath("Library/NativeAssetChecks/avatars"); Directory.CreateDirectory(avatarDir);
        foreach (var pair in (System.Collections.Generic.Dictionary<string, object>)index["aliases"])
        {
            var descriptor = (System.Collections.Generic.Dictionary<string, object>)pair.Value;
            var node = WzAssets.Node((string)descriptor["source"]);
            Require(node != null, pair.Key + " source");
            var texture = WzAssets.Texture(pair.Key);
            Require(texture != null, pair.Key + " texture"); aliases++;
        }
        var manifest = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(File.ReadAllText("Assets/Editor/Fixtures/LegacyArtManifest.json"));
        foreach (string kind in new[] { "chars", "mobs" })
            foreach (var actor in (System.Collections.Generic.Dictionary<string, object>)manifest[kind])
            {
                var spec = (System.Collections.Generic.Dictionary<string, object>)actor.Value;
                foreach (var anim in (System.Collections.Generic.Dictionary<string, object>)spec["anims"])
                {
                    var clip = Art.GetClip(kind, actor.Key, anim.Key);
                    Require(clip != null && clip.frames.Length > 0, kind + "/" + actor.Key + "/" + anim.Key);
                    var old = (System.Collections.Generic.Dictionary<string, object>)anim.Value;
                    var oldFrames = (System.Collections.Generic.List<object>)old["frames"];
                    if (clip.frames.Length != oldFrames.Count) Debug.Log("[NativeAssetChecks] frame-count change " + kind + "/" + actor.Key + "/" + anim.Key + " old=" + oldFrames.Count + " native=" + clip.frames.Length);
                    foreach (string frame in clip.frames)
                    {
                        var sprite = Art.Frame(frame); Require(sprite != null, frame); actorFrames++;
                        if (kind == "chars" && actor.Key.EndsWith("_h0", StringComparison.Ordinal))
                        {
                            string file = actor.Key + "_" + anim.Key + "_" + Array.IndexOf(clip.frames, frame) + ".png";
                            File.WriteAllBytes(Path.Combine(avatarDir, file), sprite.texture.EncodeToPNG());
                            avatarRecords.Add(new System.Collections.Generic.Dictionary<string, object> {
                                { "frame", frame }, { "file", file }, { "originX", sprite.pivot.x }, { "originY", sprite.rect.height - sprite.pivot.y } });
                        }
                    }
                }
            }
        File.WriteAllText(Path.Combine(avatarDir, "frames.json"), SerializeRecords(avatarRecords));
        foreach (var key in new[] { "knight_h0", "mage_h0", "warrior_h0", "archer_h0", "priest_h0" })
        {
            var clip = Art.GetClip("chars", key, "stand"); var sprite = Art.Frame(clip.frames[0]);
            File.WriteAllBytes(Path.GetFullPath("Library/NativeAssetChecks/" + key + ".png"), sprite.texture.EncodeToPNG());
        }
        Require(WzAssets.MissingCount == 0, "Missing native aliases: " + WzAssets.MissingCount);
        Debug.Log("[NativeAssetChecks] actors + UI: frames=" + actorFrames + " aliases=" + aliases + " sourceFiles=" + WzAssets.FileCount);
    }
    static string SerializeRecords(System.Collections.Generic.List<object> records)
    {
        var rows = new System.Collections.Generic.List<string>();
        foreach (System.Collections.Generic.Dictionary<string, object> row in records)
            rows.Add("{\"frame\":\"" + row["frame"] + "\",\"file\":\"" + row["file"] + "\",\"originX\":" +
                Convert.ToSingle(row["originX"]).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ",\"originY\":" +
                Convert.ToSingle(row["originY"]).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}");
        return "[" + string.Join(",", rows) + "]";
    }
    static void Require(bool ok, string message) { if (!ok) throw new Exception("[NativeAssetChecks] " + message); }
}
