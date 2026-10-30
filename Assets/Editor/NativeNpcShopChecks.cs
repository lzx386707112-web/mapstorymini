using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Isolated actual NPC host/IMG regressions; never touches a player's database/profile.
public static class NativeNpcShopChecks
{
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static void Session(string name, object value) => typeof(GameSession).GetProperty(name).GetSetMethod(true).Invoke(null, new[] { value });
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "npc-shop-unity-" + Guid.NewGuid() + ".db");
        var data = Profile.Data; string snapshot = JsonUtility.ToJson(data);
        var debug = typeof(Profile).GetField("debugOverride", BindingFlags.NonPublic | BindingFlags.Static);
        bool previous = (bool)debug.GetValue(null); int code = 0;
        try
        {
            debug.SetValue(null, true);
            var db = DbStore.Create(path); var server = GameServer.StartWith(db);
            var record = new CharRecord { accountid = 1, name = "ShopChecks", level = 30, meso = 100000,
                equipslots = 24, useslots = 24, setupslots = 24, etcslots = 24 };
            record.id = CharactersDao.Insert(db, record);
            Session("Net", server.Transport); Session("CharacterId", (int)record.id);
            Session("Character", CharService.Load(db, (int)record.id)); Session("InGame", true);
            data.gold = 100000; data.level = 30; data.jobId = 0; data.questCompleted.Clear(); data.questStarted.Clear();
            foreach (int id in new[] { 11100, 1001100, 1011100, 1021100, 1031100 })
            {
                NativeNpcShopWindow.Close(); var script = NpcSession.Talk(id);
                Check(script == null && NativeNpcShopWindow.Visible, "no-script merchant opens shop " + id);
            }
            foreach (int id in new[] { 11000, 2100002, 2100003 })
            {
                NativeNpcShopWindow.Close(); var script = NpcSession.Talk(id);
                Check(script != null && script.LastError == null && NativeNpcShopWindow.Visible, "unchanged script opens shop " + id);
                Check(script.Closed, "dispose leaves shop open " + id);
            }
            NativeNpcShopWindow.Close();
            var locked = NpcSession.Talk(9201099);
            Check(locked != null && locked.LastError == null && !NativeNpcShopWindow.Visible && locked.Dialog.Visible, "quest 8224 gate remains closed");
            NpcSession.CloseActive(); data.questCompleted.Add(8224);
            var unlocked = NpcSession.Talk(9201099);
            Check(unlocked != null && unlocked.LastError == null && NativeNpcShopWindow.Visible, "quest 8224 completion opens shop");
            Check(GameWindows.AnyVisible, "ordinary shop blocks gameplay input");
            GameWindows.TrackPanels(); Check(GameWindows.CloseTop() && !NativeNpcShopWindow.Visible, "Escape closes ordinary shop");
            foreach (int mapId in new[] { 1000003, 1010003, 104000002, 100000102 })
            {
                var root = NativeMapView.MapRoot(mapId);
                var go = new GameObject("ShopDepthCheck");
                try
                {
                    var view = go.AddComponent<NativeMapView>(); view.Show(mapId);
                    foreach (var life in root.Get("life").Children)
                    {
                        if (life.Text("type") != "n" || life.Int("hide") != 0) continue;
                        int npc = int.Parse(life.Text("id")); if (!server.NpcShops.HasNpc(npc)) continue;
                        var renderer = Find(go, "Npc_" + life.Text("id"));
                        Check(renderer != null && renderer.transform.parent.name == "MapLayers", "merchant shares map sorting group " + mapId + "/" + npc);
                        // Source counter foreground layer must occlude the NPC; no position workaround.
                        foreach (var layer in root.Children)
                        {
                            if (!int.TryParse(layer.Name, out int z)) continue;
                            var objects = layer.Get("obj"); if (objects == null) continue;
                            foreach (var obj in objects.Children)
                            {
                                if (obj.Text("oS") != "shop" || Math.Abs(obj.Int("x") - life.Int("x")) > 100) continue;
                                long depth = obj.Int("z") + 30000L * z - 0x3FFFF830;
                                if (depth <= NativeMapView.NpcDepth(root, life.Int("fh"))) continue;
                                var counter = Find(go, "obj_" + z + "_" + obj.Name);
                                Check(counter != null && counter.sortingOrder > renderer.sortingOrder, "original foreground counter occludes merchant " + mapId + "/" + npc);
                            }
                        }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            Debug.Log("[NativeNpcShopChecks] PASS " + checks + " actual host, quest gate, Escape and counter depth checks");
        }
        catch (Exception e) { code = 1; Debug.LogException(e); }
        finally
        {
            NativeNpcShopWindow.Close(); NpcSession.CloseActive(); Session("InGame", false); Session("Character", null); Session("Net", null);
            GameServer.Stop(); JsonUtility.FromJsonOverwrite(snapshot, data); debug.SetValue(null, previous);
            if (File.Exists(path)) File.Delete(path);
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
    static SpriteRenderer Find(GameObject root, string name)
    {
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>()) if (renderer.name == name) return renderer;
        return null;
    }
}

public static class NativeNpcShopSceneChecks
{
    public static void Run()
    {
        SessionState.SetBool("NativeNpcShopSceneChecks", true);
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        HudChecks.SetGameViewSize(800, 600); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Hook() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool("NativeNpcShopSceneChecks", false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool("NativeNpcShopSceneChecks", false);
        new GameObject("NpcShopSceneFixture").AddComponent<NativeNpcShopSceneProbe>();
    }
}
public class NativeNpcShopSceneProbe : MonoBehaviour
{
    string dbPath, snapshot;
    bool previousOverride, hadMap;
    int previousMap;
    FieldInfo debug;
    void OnGUI() { UiKit.Begin(); NativeNpcShopWindow.Draw(); }
    void Set(string name, object value) => typeof(GameSession).GetProperty(name).GetSetMethod(true).Invoke(null, new[] { value });
    System.Collections.IEnumerator Start()
    {
        Application.runInBackground = true;
        debug = typeof(Profile).GetField("debugOverride", BindingFlags.NonPublic | BindingFlags.Static);
        previousOverride = (bool)debug.GetValue(null); snapshot = JsonUtility.ToJson(Profile.Data); debug.SetValue(null, true);
        dbPath = Path.Combine(Path.GetTempPath(), "npc-shop-scene-" + Guid.NewGuid() + ".db");
        var server = GameServer.StartWith(DbStore.Create(dbPath));
        var record = new CharRecord { accountid = 1, name = "商店检查", level = 30, meso = 100000, map = 1000003,
            equipslots = 24, useslots = 24, setupslots = 24, etcslots = 24 };
        record.id = CharactersDao.Insert(server.Db, record);
        Set("Net", server.Transport); Set("CharacterId", (int)record.id); Set("Character", CharService.Load(server.Db, (int)record.id)); Set("InGame", true);
        hadMap = PlayerPrefs.HasKey("mxd.fieldmap"); previousMap = Profile.LastMapId;
        Profile.Data.gold = record.meso; Profile.LastMapId = record.map;
        var camera = new GameObject("NpcShopCamera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.orthographic = true; camera.orthographicSize = 150 / Art.PPU;
        camera.transform.position = new Vector3(-257 / Art.PPU, -15 / Art.PPU, -10);
        camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor;
        new GameObject("NpcShopMap").AddComponent<NativeMapView>().Show(record.map);
        Directory.CreateDirectory("docs/hud-review");
        yield return new WaitForSecondsRealtime(2);
        yield return Capture("docs/hud-review/npc-shop-counter.png");
        NpcSession.Talk(11100);
        yield return new WaitForSecondsRealtime(2);
        yield return Capture("docs/hud-review/npc-shop-window.png");
        Debug.Log("[NativeNpcShopSceneChecks] PASS counter and ordinary shop rendered");
        NativeNpcShopWindow.Close(); NpcSession.CloseActive(); Set("InGame", false); Set("Character", null); Set("Net", null);
        GameServer.Stop(); JsonUtility.FromJsonOverwrite(snapshot, Profile.Data); debug.SetValue(null, previousOverride);
        if (hadMap) Profile.LastMapId = previousMap; else { PlayerPrefs.DeleteKey("mxd.fieldmap"); PlayerPrefs.Save(); }
        if (File.Exists(dbPath)) File.Delete(dbPath);
        EditorApplication.Exit(0);
    }
    System.Collections.IEnumerator Capture(string path)
    {
        yield return new WaitForEndOfFrame();
        var texture = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(path, texture.EncodeToPNG()); Destroy(texture);
    }
}
