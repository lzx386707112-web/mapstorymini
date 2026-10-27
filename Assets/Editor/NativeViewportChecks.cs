using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Actual OnGUI drag/resize/close regressions, using an inactive game/profile fixture.
// No login, server, preference save, or player database is involved.
public static class NativeViewportChecks
{
    public static void Run()
    {
        SessionState.SetBool("NativeViewportChecks", true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        HudChecks.SetGameViewSize(1280, 720);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool("NativeViewportChecks", false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool("NativeViewportChecks", false);
        new GameObject("Native viewport fixture").AddComponent<NativeViewportProbe>();
    }
}

public class NativeViewportProbe : MonoBehaviour
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    Action repaint;
    bool overview;
    int passed;
    static object Get(Type type, string name) => type.GetField(name, Static).GetValue(null);
    static void Set(Type type, string name, object value) => type.GetField(name, Static).SetValue(null, value);
    void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("[NativeViewportChecks] " + name);
        passed++;
    }
    static void PointerAt(Vector2 point, bool down = false, bool pressed = false, bool clicked = false)
    {
        Pointer.Pos = point;
        Pointer.Down = down; Pointer.Pressed = pressed; Pointer.Clicked = clicked;
        Pointer.Released = Pointer.RightPressed = Pointer.DoubleClicked = false;
    }
    void OnGUI()
    {
        try
        {
            UiKit.Begin();
            if (Event.current.type == EventType.Repaint && repaint != null)
            {
                var action = repaint;
                repaint = null;
                action();
            }
            if (overview)
            {
                PointerAt(new Vector2(-100, -100));
                StatWindow.Draw(); EquipWindow.Draw(); InvenWindow.Draw(); SkillWindow.Draw();
            }
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    IEnumerator Step(Action action)
    {
        repaint = action;
        while (repaint != null) yield return null;
    }
    void Draw(Type type)
    {
        if (type == typeof(StatWindow)) StatWindow.Draw();
        else if (type == typeof(EquipWindow)) EquipWindow.Draw();
        else if (type == typeof(InvenWindow)) InvenWindow.Draw();
        else SkillWindow.Draw();
    }
    void Inside(Type type, float width, float height)
    {
        var p = (Vector2)Get(type, "pos");
        float s = BottomBar.S;
        Check(p.x >= 0 && p.y >= 0 && (p.x + width) * s <= UiKit.W + .01f &&
            (p.y + height) * s <= UiKit.H + .01f, type.Name + " stays inside " + Screen.width + "x" + Screen.height);
    }
    IEnumerator Panels()
    {
        var types = new[] { typeof(StatWindow), typeof(EquipWindow), typeof(InvenWindow), typeof(SkillWindow) };
        var widths = new[] { 208f, 208f, 603f, 380f };
        var heights = new[] { 347f, 291f, 289f, 289f };
        Set(typeof(EquipWindow), "expanded", true); Set(typeof(InvenWindow), "expanded", true);
        Set(typeof(SkillWindow), "macroVisible", true);
        foreach (var type in types) type.GetField("Visible").SetValue(null, true);
        for (int i = 0; i < types.Length; i++)
        {
            var type = types[i]; float width = widths[i], height = heights[i];
            // Stale positions after a narrower/shorter viewport must recover without a drag.
            yield return Step(() =>
            {
                Set(type, "pos", new Vector2(3000, 3000));
                PointerAt(new Vector2(-100, -100)); Draw(type); Inside(type, width, height);
            });
            yield return Step(() =>
            {
                Set(type, "pos", new Vector2(40, 40));
                PointerAt(new Vector2(50, 50) * BottomBar.S, true, true); Draw(type);
            });
            // Assert the very frame that moves past the edge, rather than the next repaint.
            yield return Step(() =>
            {
                PointerAt(new Vector2(UiKit.W * 2, UiKit.H * 2), true); Draw(type); Inside(type, width, height);
            });
            yield return Step(() =>
            {
                PointerAt(new Vector2(-100, -100), true); Draw(type); Inside(type, width, height);
            });
            yield return Step(() => { PointerAt(new Vector2(-100, -100)); Draw(type); });
        }

        // A click at the newly dragged skill close button must work in that same repaint.
        // Previously Draw used the old 'win' while SkillMacro already used the new 'pos'.
        yield return Step(() =>
        {
            Set(typeof(SkillWindow), "pos", new Vector2(40, 40));
            PointerAt(new Vector2(50, 50) * BottomBar.S, true, true); SkillWindow.Draw();
        });
        yield return Step(() =>
        {
            Set(typeof(SkillWindow), "dragOffset", new Vector2(162, 11));
            PointerAt(new Vector2(462, 131) * BottomBar.S, true, false, true);
            SkillWindow.Draw();
            Check(!SkillWindow.Visible, "skill draw/hit rect follows drag in the same repaint");
            var macro = (Rect)typeof(SkillWindow).GetProperty("MacroRect", Static).GetValue(null);
            Check(Mathf.Abs(macro.x - 473 * BottomBar.S) < .01f, "macro shares moved skill origin");
        });
    }
    void CameraBounds()
    {
        var obj = new GameObject("Viewport camera check");
        var camera = obj.AddComponent<Camera>();
        var rig = obj.AddComponent<CameraRig>();
        rig.enabled = false;
        var clampY = typeof(CameraRig).GetMethod("ClampY", BindingFlags.Instance | BindingFlags.NonPublic);
        var update = typeof(CameraRig).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            foreach (var size in new[] { new Vector2Int(800, 600), new Vector2Int(1365, 768), new Vector2Int(2400, 1080) })
            {
                var texture = new RenderTexture(size.x, size.y, 0);
                camera.targetTexture = texture;
                try
                {
                    rig.baseSize = 767f / (2 * Art.PPU);
                    rig.SetBounds(0, 200, 0, 100); rig.SnapTo(-1000);
                    float halfWidth = Mathf.Floor(767f * size.x / size.y / 2f) / Art.PPU;
                    Check(Mathf.Abs(obj.transform.position.x - halfWidth) < .0001f, "snap uses current viewport integer half-width " + size);
                    float y = (float)clampY.Invoke(rig, new object[] { -1000f });
                    Check(Mathf.Abs(y - 383f / Art.PPU) < .0001f, "odd logical height uses integer half-height");
                    rig.SetBounds(0, 2, 0, 2); rig.SnapTo(1000);
                    Check(Mathf.Abs(obj.transform.position.x - 1) < .0001f && Mathf.Abs(obj.transform.position.y - 1) < .0001f,
                        "small map locks both axes to midpoint");
                    update.Invoke(rig, null);
                    Check(Mathf.Abs(obj.transform.position.y - 1) < .0001f, "no target does not bypass camera bounds");
                }
                finally { camera.targetTexture = null; DestroyImmediate(texture); }
            }
        }
        finally { DestroyImmediate(obj); }
    }
    void SkillIcons()
    {
        var root = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(
            Resources.Load<TextAsset>("UI/JobTree").text);
        var skills = (System.Collections.Generic.Dictionary<string, object>)root["skills"];
        int icons = 0;
        foreach (var entry in skills)
        {
            var skill = SkillBook.Get(int.Parse(entry.Key));
            if (string.IsNullOrEmpty(skill.icon)) continue;
            var texture = Art.Tex(skill.icon);
            Check(texture != null && texture.width > 1 && texture.height > 1, "skill icon resolves " + skill.id);
            bool visible = false;
            foreach (var pixel in texture.GetPixels32()) if (pixel.a != 0) { visible = true; break; }
            Check(visible, "skill icon has visible pixels " + skill.id);
            icons++;
        }
        Debug.Log("[NativeViewportChecks] decoded " + icons + " nonempty skill icons");
    }
    IEnumerator Start()
    {
        Application.runInBackground = true;
        var fixture = new GameObject("Inactive game fixture"); fixture.SetActive(false);
        var game = fixture.AddComponent<GameLoop>(); GameLoop.I = game;
        game.player = fixture.AddComponent<PlayerCtrl>();
        var previousData = Get(typeof(Profile), "data");
        var previousOverride = Get(typeof(Profile), "debugOverride");
        Set(typeof(Profile), "data", new PlayerProfileData()); Set(typeof(Profile), "debugOverride", true);
        new GameObject("UI camera").AddComponent<Camera>().backgroundColor = new Color32(75, 90, 100, 255);
        try
        {
            yield return Step(SkillIcons);
            CameraBounds();
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(2400, 1080),
                new Vector2Int(2560, 1080), new Vector2Int(1024, 768), new Vector2Int(800, 600) })
            {
                HudChecks.SetGameViewSize(size.x, size.y);
                yield return new WaitForSecondsRealtime(.5f);
                yield return Panels();
                Debug.Log("[NativeViewportChecks] checked " + Screen.width + "x" + Screen.height);
                if (size.x != 2400 && size.x != 800) continue;
                yield return Step(() =>
                {
                    StatWindow.Visible = EquipWindow.Visible = InvenWindow.Visible = SkillWindow.Visible = true;
                    Set(typeof(EquipWindow), "expanded", false); Set(typeof(InvenWindow), "expanded", false);
                    Set(typeof(StatWindow), "detail", false);
                    Set(typeof(StatWindow), "pos", new Vector2(0, 30)); Set(typeof(EquipWindow), "pos", new Vector2(220, 30));
                    Set(typeof(InvenWindow), "pos", new Vector2(410, 30)); Set(typeof(SkillWindow), "pos", new Vector2(610, 30));
                    overview = true;
                });
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory("docs/hud-review");
                File.WriteAllBytes("docs/hud-review/native-viewport-" + size.x + ".png", shot.EncodeToPNG());
                Destroy(shot); overview = false;
            }
            Debug.Log("[NativeViewportChecks] PASS " + passed + " checks, 5 viewport sizes, same-frame drag/hit, integer camera bounds");
        }
        finally
        {
            Set(typeof(Profile), "data", previousData); Set(typeof(Profile), "debugOverride", previousOverride);
            GameLoop.I = null; Destroy(fixture);
        }
        EditorApplication.Exit(0);
    }
}
