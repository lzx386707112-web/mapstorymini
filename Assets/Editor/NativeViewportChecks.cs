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

    public static void RunNpc()
    {
        SessionState.SetBool("NpcViewportChecks", true);
        Run();
    }

    public static void RunMarkers()
    {
        SessionState.SetBool("NpcMarkerChecks", true);
        Run();
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
    bool overview, npcOverview, markerOverview;
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
            if (npcOverview) NpcDialogUi.Draw();
            if (markerOverview) NpcDialogUi.DrawField();
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
    IEnumerator MarkerFlow()
    {
        Profile.Data.level = 1; Profile.Data.jobId = 0;
        Profile.Data.questStarted.Clear(); Profile.Data.questCompleted.Clear();
        var oldMap = NpcField.Map;
        Camera.main.orthographic = true; Camera.main.orthographicSize = 3;
        foreach (var pair in new[] { new Vector2Int(40000, 2004), new Vector2Int(1010000, 12100), new Vector2Int(2, 9101003) })
        {
            if (pair.y == 12100) Profile.Data.questStarted.Add(1040);
            if (pair.y == 9101003)
            {
                Profile.Data.level = 2;
                Profile.Data.questStarted.Clear(); Profile.Data.questStarted.Add(8142);
                Profile.Data.questProgress.Clear();
            }
            var map = FieldCatalog.GetMap(pair.x);
            var npc = map.npcs.Find(n => n.id == pair.y);
            Check(npc != null, "original map must contain NPC " + pair.y);
            NpcField.Bind(map);
            var marker = NpcField.MarkerOf(npc.id);
            Check(pair.y == 9101003 ? marker.progress && !marker.complete : marker.start,
                "NPC must expose its original available/progress quest marker " + npc.id);
            float x = map.WorldX(npc.x), y = map.WorldY(npc.y) + (npc.head + 26f) / Art.PPU;
            Camera.main.transform.position = new Vector3(x, y, -10);
            markerOverview = false;
            yield return new WaitForEndOfFrame();
            var before = ScreenCapture.CaptureScreenshotAsTexture();
            markerOverview = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            var after = ScreenCapture.CaptureScreenshotAsTexture();
            int changed = 0;
            for (int py = after.height / 2 - 35; py < after.height / 2 + 35; py++)
                for (int px = after.width / 2 - 35; px < after.width / 2 + 35; px++)
                    if (before.GetPixel(px, py) != after.GetPixel(px, py)) changed++;
            Check(changed > 20, "original QuestIcon must actually render at projected NPC head " + npc.id);
            File.WriteAllBytes("/tmp/npc-marker-" + npc.id + ".png", after.EncodeToPNG());
            Destroy(before); Destroy(after);
            markerOverview = false;
        }
        NpcField.Bind(oldMap);
        Debug.Log("[NpcMarkerChecks] PASS " + passed + " checks; original markers rendered for 2004, 12100 and training delivery NPC 9101003");
    }

    IEnumerator NpcFlow()
    {
        var player = GameLoop.I.player;
        player.hpMax = 50; player.hp = 50; player.mpMax = 5;
        Profile.Data.questStarted.Clear(); Profile.Data.questCompleted.Clear();
        // Exercise the actual InputBridge gate while a conversation remains open, across shared page modes.
        var bridge = player.gameObject.AddComponent<InputBridge>(); // inactive fixture: no automatic Update/Awake
        var inputUpdate = typeof(InputBridge).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        var game = GameLoop.I;
        var priorState = game.state;
        bool priorInput = InputHub.Enabled;
        game.state = GameLoop.GameState.Playing;
        InputHub.Enabled = true;
        foreach (NpcDialogMode mode in new[] { NpcDialogMode.Next, NpcDialogMode.Prev, NpcDialogMode.NextPrev,
            NpcDialogMode.Ok, NpcDialogMode.YesNo, NpcDialogMode.AcceptDecline, NpcDialogMode.Simple,
            NpcDialogMode.GetText, NpcDialogMode.GetNumber })
        {
            var session = NpcSession.Conversation(2000, null, (current, reply) => { });
            session.SetDialog(mode, "window coexistence");
            foreach (var action in new[] { GameAction.Inven, GameAction.Quest, GameAction.Equip, GameAction.Stat, GameAction.Skill })
            {
                InputHub.SetVirtualAction(action, true);
                inputUpdate.Invoke(bridge, null);
                InputHub.EndFrame();
                InputHub.SetVirtualAction(action, false);
            }
            Check(InvenWindow.Visible && QuestWindow.Visible && EquipWindow.Visible && StatWindow.Visible && SkillWindow.Visible,
                mode + ": actual InputBridge opens independent windows during dialogue");
            Check(NpcSession.Active == session && session.Dialog.Mode == mode && !session.Closed,
                mode + ": opening windows must preserve the dialogue page");
            yield return Step(() => {
                var drawSmall = typeof(BottomBar).GetMethod("DrawSmallButtons", BindingFlags.Static | BindingFlags.NonPublic);
                InvenWindow.Visible = false;
                var button = BottomBar.SmallButtonRect(1);
                PointerAt(button.center); Pointer.Released = true;
                drawSmall.Invoke(null, null);
                Check(InvenWindow.Visible && NpcSession.Active == session && !session.Closed,
                    mode + ": real bottom-bar inventory button works without ending dialogue");
                PointerAt(new Vector2(-100, -100));
            });
            InvenWindow.Visible = QuestWindow.Visible = EquipWindow.Visible = StatWindow.Visible = SkillWindow.Visible = false;
            session.Close();
            yield return null;
        }
        InputHub.ReleaseAllVirtual();
        Action pressTalk = () => {
            InputHub.SetVirtualAction(GameAction.NpcTalk, true);
            inputUpdate.Invoke(bridge, null);
            InputHub.EndFrame();
            InputHub.SetVirtualAction(GameAction.NpcTalk, false);
        };
        var quest = QuestCatalog.Get(1021);
        // Actual first press: InputBridge -> nearest NPC -> task menu, not TalkQuest/Reply.
        var oldMap = NpcField.Map;
        var npcMap = new FieldMap();
        npcMap.npcs.Add(new FieldNpc { id = 2000, x = player.FeetPos.x * Art.PPU, y = -player.FeetPos.y * Art.PPU });
        NpcField.Bind(npcMap);
        pressTalk();
        Check(NpcSession.Active.Dialog.Mode == NpcDialogMode.Simple, "NPC interaction opens real quest selection menu");
        yield return null;
        pressTalk();
        Check(NpcSession.Active != null && NpcSession.Active.QuestId == 1021 &&
            NpcSession.Active.Dialog.Mode == NpcDialogMode.Next, "second NPC talk press selects Roger quest from menu");
        yield return null;
        pressTalk();
        Check(NpcSession.Active.Dialog.Mode == NpcDialogMode.NextPrev, "talk key advances Roger's first page");
        yield return null;
        pressTalk();
        Check(NpcSession.Active.Dialog.Mode == NpcDialogMode.AcceptDecline, "talk key reaches explicit accept choice");
        yield return null;
        pressTalk(); // Same key confirms the default accept button.
        Check(Profile.ItemCount(2010007) == 1, "accepting original script gives apple");
        yield return null;
        pressTalk();
        Check(NpcSession.Active.Dialog.Mode == NpcDialogMode.Prev && NpcSession.Active.Dialog.HasFooterOk,
            "sendPrev must expose both previous and footer OK");
        yield return null;
        pressTalk();
        Check(!NpcSession.Blocking && Profile.IsQuestStarted(1021), "last talk key must reach showInfo and dispose");
        var guide = player.GetComponentInChildren<NativeImgAnimation>(true);
        Check(guide != null && guide.Image.sprite != null && guide.Image.enabled && guide.Image.color.a > 0,
            "original tutorial animation must actually be visible at first frame");
        // The inactive fixture suppresses PlayerCtrl.Awake; detach only the spawned visual so its real Update/render can run.
        guide.transform.SetParent(null, true);
        Camera.main.orthographic = true; Camera.main.orthographicSize = 4;
        Camera.main.transform.position = new Vector3(player.FeetPos.x, player.FeetPos.y + 2, -10);
        yield return new WaitForSecondsRealtime(.3f);
        Check(guide != null && guide.Image.isVisible, "tutorial renderer must reach the camera");
        yield return new WaitForEndOfFrame();
        var tutorialShot = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("/tmp/roger-guide-fixed.png", tutorialShot.EncodeToPNG()); Destroy(tutorialShot);
        Check(NpcSession.UseRestorative(2010007) && player.hp == 50, "apple must restore Roger's damaged HP");
        yield return null;
        NativeKeyActions.Execute(GameAction.Inven);
        NativeKeyActions.Execute(GameAction.Quest);
        Check(InvenWindow.Visible && QuestWindow.Visible && !NpcSession.Blocking, "inventory and quest must coexist after tutorial dialogue");
        yield return Step(() => { PointerAt(new Vector2(-100,-100)); GameWindows.DrawAll(); });
        NativeKeyActions.Execute(GameAction.Quest);
        Check(InvenWindow.Visible && !QuestWindow.Visible, "closing quest must preserve inventory");
        NativeKeyActions.Execute(GameAction.Quest);
        Check(InvenWindow.Visible && QuestWindow.Visible, "quest can reopen while inventory stays open");
        InvenWindow.Visible = QuestWindow.Visible = false;
        if (guide != null) guide.Remove();
        NpcSession.TalkQuest(quest, 2000, true);
        yield return null;
        pressTalk();
        yield return null;
        pressTalk();
        Check(NpcSession.Active.Dialog.Mode == NpcDialogMode.Prev && NpcSession.Active.Dialog.RawText.Contains("10 exp"),
            "reach screenshot reward page through real frame-separated talk presses");
        npcOverview = true;
        yield return new WaitForEndOfFrame();
        var rewardShot = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("/tmp/roger-reward-fixed.png", rewardShot.EncodeToPNG()); Destroy(rewardShot);
        int apples = Profile.ItemCount(2010000), greenApples = Profile.ItemCount(2010009);
        // Click the actual rendered original OK canvas; do not call Reply to bypass the missing-button regression.
        yield return Step(() => {
            var dialog = NpcSession.Active.Dialog;
            float contentHeight = DialogFlow.ContentHeight(dialog.Content, 529, false) + 18;
            float height = DialogFlow.BodyHeight(contentHeight) + 86;
            var bottomRight = new Vector2((UiKit.W + 529) / 2, (UiKit.H + height) / 2);
            PointerAt(bottomRight - new Vector2(31,17), clicked:true);
            NpcDialogUi.Draw();
        });
        npcOverview = false;
        Check(Profile.IsQuestCompleted(1021) && Profile.ItemCount(2010000) == apples + 3 &&
            Profile.ItemCount(2010009) == greenApples + 3 && !NpcSession.Blocking,
            "real footer OK click must grant both rewards and release dialogue");
        // Also exercise the same final page through the talk key, without granting twice.
        NpcSession.TalkQuest(quest, 2000, true);
        Check(Profile.ItemCount(2010000) == apples + 3, "reopening completed quest cannot duplicate reward");
        NpcSession.CloseActive();
        NpcField.Bind(oldMap);
        InputHub.Enabled = priorInput;
        game.state = priorState;
        Debug.Log("[NpcViewportChecks] PASS " + passed + " checks; screenshots /tmp/roger-{guide,reward}-fixed.png");
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
        var camera = new GameObject("UI camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.backgroundColor = new Color32(75, 90, 100, 255);
        try
        {
            if (SessionState.GetBool("NpcMarkerChecks", false))
            {
                SessionState.SetBool("NpcMarkerChecks", false);
                yield return MarkerFlow();
                EditorApplication.Exit(0);
                yield break;
            }
            if (SessionState.GetBool("NpcViewportChecks", false))
            {
                SessionState.SetBool("NpcViewportChecks", false);
                yield return NpcFlow();
                EditorApplication.Exit(0);
                yield break;
            }
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
