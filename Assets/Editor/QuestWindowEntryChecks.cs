using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class QuestWindowEntryChecks
{
    public static void Run()
    {
        SessionState.SetBool("QuestWindowEntryChecks", true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        HudChecks.SetGameViewSize(1280, 720);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool("QuestWindowEntryChecks", false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool("QuestWindowEntryChecks", false);
        new GameObject("Quest entry fixture").AddComponent<QuestWindowEntryProbe>();
    }
}
public class QuestWindowEntryProbe : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    Action repaint;
    bool drawing;
    static object Get(Type type, string name) => type.GetField(name, Private).GetValue(null);
    static void Set(Type type, string name, object value) => type.GetField(name, Private).SetValue(null, value);
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    void OnGUI()
    {
        try
        {
            UiKit.Begin();
            GUI.depth = -1000; // Keep the isolated window above the automatic login fixture.
            if (drawing) QuestWindow.Draw();
            if (Event.current.type == EventType.Repaint && repaint != null)
            { var action = repaint; repaint = null; action(); }
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    IEnumerator Step(Action action) { repaint = action; while (repaint != null) yield return null; }
    IEnumerator Capture(string file)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "../docs/hud-review"));
        File.WriteAllBytes(Path.Combine(Application.dataPath, "../docs/hud-review/" + file), image.EncodeToPNG());
        Destroy(image);
    }
    IEnumerator Start()
    {
        Application.runInBackground = true;
        var oldData = Get(typeof(Profile), "data");
        var oldOverride = Get(typeof(Profile), "debugOverride");
        Set(typeof(Profile), "data", new PlayerProfileData()); Set(typeof(Profile), "debugOverride", true);
        var camera = new GameObject("Quest check camera").AddComponent<Camera>();
        camera.backgroundColor = new Color32(75, 90, 100, 255);
        Pointer.Pos = new Vector2(-100, -100);
        try
        {
            // Repaint-driven pointer capture: hold the native title, move, release.
            yield return Step(() =>
            {
                Set(typeof(QuestAlarm), "position", new Vector2(500, 35));
                float s = BottomBar.S;
                Pointer.Pos = new Vector2(550, 45) * s; Pointer.Pressed = Pointer.Down = true;
                QuestAlarm.Draw(); Pointer.Pressed = false;
                Pointer.Pos += new Vector2(-100, 80) * s; QuestAlarm.Draw();
                Require(Vector2.Distance((Vector2)Get(typeof(QuestAlarm), "position"), new Vector2(400, 115)) < .01f,
                    "Quest alarm did not follow held mouse: " + Get(typeof(QuestAlarm), "position") + " scale=" + s + " view=" + UiKit.W);
                Pointer.Down = false; QuestAlarm.Draw();
                Pointer.Pos += new Vector2(30, 30) * s; QuestAlarm.Draw();
                Require(Vector2.Distance((Vector2)Get(typeof(QuestAlarm), "position"), new Vector2(400, 115)) < .01f, "Quest alarm kept dragging after release");
                Pointer.Pos = new Vector2(545, 120) * s; Pointer.Pressed = Pointer.Down = true;
                QuestAlarm.Draw();
                Require(!(bool)Get(typeof(QuestAlarm), "dragging"), "Quest alarm button started a title drag");
                Pointer.Pressed = Pointer.Down = Pointer.Clicked = false; Pointer.Pos = new Vector2(-100,-100);
                Debug.Log("[QuestAlarmDragChecks] PASS held title drag, release and button exclusion");
            });
            Profile.StartQuest(1009);
            Set(typeof(QuestWindow), "tab", 1); Set(typeof(QuestWindow), "selectedId", 0);
            QuestWindow.Visible = false;
            NativeKeyActions.Execute(GameAction.Quest);
            Require(QuestWindow.Visible && (int)Get(typeof(QuestWindow), "selectedId") == 1009 &&
                (bool)Get(typeof(QuestWindow), "detailVisible"), "Quest key did not select/open the sole running task");
            drawing = true;
            yield return Capture("quest-key-entry.png");
            NativeKeyActions.Execute(GameAction.Quest);
            Require(!QuestWindow.Visible, "Quest key did not toggle closed");
            // Real FadeYesNo confirm button, not a direct replacement call to Select.
            QuestNotice.Ready(1009);
            yield return Step(() =>
            {
                var box = BottomBar.NotifyBoxRect(); float s = BottomBar.S;
                Pointer.Pos = new Vector2(box.x + (33 - 140 + 140) * s, box.y - 48 * s + 12 * s);
                Pointer.Clicked = true;
                QuestNotice.DrawReady();
                Pointer.Pos = new Vector2(-100,-100); Pointer.Clicked = false;
            });
            Require(QuestWindow.Visible && (int)Get(typeof(QuestWindow), "selectedId") == 1009 &&
                (int)Get(typeof(QuestWindow), "tab") == 1 && (bool)Get(typeof(QuestWindow), "detailVisible"),
                "Completion notice opened a different selection/layout");
            yield return Capture("quest-notice-entry.png");
            // Reopening restores details even after the user hid the detail pane.
            Set(typeof(QuestWindow), "detailVisible", false);
            NativeKeyActions.Execute(GameAction.Quest); NativeKeyActions.Execute(GameAction.Quest);
            Require((bool)Get(typeof(QuestWindow), "detailVisible"), "Quest key retained hidden detail pane");
            Profile.CompleteQuest(1009);
            QuestWindow.Visible = false; Set(typeof(QuestWindow), "tab", 1);
            NativeKeyActions.Execute(GameAction.Quest);
            Require((int)Get(typeof(QuestWindow), "tab") == 0 && (int)Get(typeof(QuestWindow), "selectedId") > 0,
                "Empty running list did not fall back to/select available task");
            typeof(QuestWindow).GetMethod("SelectTab", Private).Invoke(null, new object[] {2});
            Require((int)Get(typeof(QuestWindow), "selectedId") == 1009 && (bool)Get(typeof(QuestWindow), "detailVisible"),
                "Completed tab did not select/show task");
            var collapsed = (HashSet<int>)Get(typeof(QuestWindow), "collapsed");
            foreach (int id in QuestCatalog.Ids) if (id >= 1000 && id < 1100) Profile.StartQuest(id);
            QuestWindow.Select(1009);
            int target = 1009;
            var rows = (IList)Get(typeof(QuestWindow), "rows");
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                int id = (int)rows[i].GetType().GetField("id").GetValue(rows[i]);
                if (id > 0) { target = id; break; }
            }
            collapsed.Add(QuestCatalog.Get(target).area);
            Set(typeof(QuestWindow), "listScroll", 0f);
            QuestWindow.Select(target);
            Require(!collapsed.Contains(QuestCatalog.Get(target).area), "Notice selection retained collapsed task category");
            rows = (IList)Get(typeof(QuestWindow), "rows"); int selectedRow = -1;
            for (int i = 0; i < rows.Count; i++) if ((int)rows[i].GetType().GetField("id").GetValue(rows[i]) == target) selectedRow = i;
            float scroll = (float)Get(typeof(QuestWindow), "listScroll");
            Require(selectedRow >= scroll && selectedRow < scroll + 14, "Notice task remains outside visible list rows");
            Debug.Log("[QuestWindowEntryChecks] PASS real quest key and FadeYesNo click; native automatic selection, empty-list fallback, tabs, detail restoration and collapsed/offscreen task reveal");
        }
        finally
        {
            QuestWindow.Visible = false; QuestNotice.RemoveReady(1009);
            Set(typeof(Profile), "data", oldData); Set(typeof(Profile), "debugOverride", oldOverride);
        }
        EditorApplication.Exit(0);
    }
}
