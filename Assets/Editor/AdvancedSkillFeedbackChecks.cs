using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AdvancedSkillFeedbackChecks
{
    public static void Run()
    {
        SessionState.SetBool("AdvancedSkillFeedbackChecks", true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        HudChecks.SetGameViewSize(800, 600);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Hook() => EditorApplication.update += Tick;
    static void Tick()
    {
        if (!SessionState.GetBool("AdvancedSkillFeedbackChecks", false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool("AdvancedSkillFeedbackChecks", false);
        new GameObject("AdvancedSkillFeedbackProbe").AddComponent<AdvancedSkillFeedbackProbe>();
    }
}

public sealed class AdvancedSkillFeedbackProbe : MonoBehaviour
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Set(string key, object value) => typeof(GameSession).GetProperty(key).SetValue(null, value);
    bool draw;
    bool scrollbarChecked;
    void OnGUI()
    {
        if (!draw) return;
        if (!scrollbarChecked && Event.current.type == EventType.Repaint)
        {
            try
            {
                SkillWindow.CancelSkillDrag();
                CurrentFeedbackChecks.CheckScrollbar();
                SkillWindow.PickForTest(1221030);
                scrollbarChecked = true;
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        }
        UiKit.Begin(); VirtualControls.Draw(); SkillWindow.Draw(); VirtualControls.DrawDropTargets();
    }
    IEnumerator Start()
    {
        var steps = Check();
        while (true)
        {
            object current;
            try { if (!steps.MoveNext()) break; current = steps.Current; }
            catch (Exception e) { Debug.LogException(e); ((IDisposable)steps).Dispose(); EditorApplication.Exit(1); yield break; }
            yield return current;
        }
        ((IDisposable)steps).Dispose(); EditorApplication.Exit(0);
    }

    IEnumerator Check()
    {
        var oldData = Profile.Data;
        var saving = typeof(Profile).GetField("debugOverride", Flags);
        var oldSaving = saving.GetValue(null);
        VirtualControls.Ensure();
        var oldElements = VirtualControls.elems;
        var oldName = VirtualControls.activeName;
        var oldNames = new List<string>(VirtualControls.profileNames);
        var manifest = (string)typeof(VirtualControls).GetProperty("ManifestPath", Flags).GetValue(null);
        byte[] manifestBytes = File.Exists(manifest) ? File.ReadAllBytes(manifest) : null;
        var oldKeys = KeyMap.ExportCharacter();
        var oldSkills = new int[QuickSlots.Count];
        for (int i = 0; i < oldSkills.Length; i++) oldSkills[i] = QuickSlots.Id(i);
        string path = Path.Combine(Path.GetTempPath(), "advanced-skills-" + Guid.NewGuid() + ".db");
        VirtualControls.activeName = "advanced-test-" + Guid.NewGuid();
        string virtualPath = (string)typeof(VirtualControls).GetMethod("ProfilePath", Flags).Invoke(null, new object[] { VirtualControls.activeName });
        GameObject fixture = null;
        try
        {
            typeof(Profile).GetField("data", Flags).SetValue(null, new PlayerProfileData());
            saving.SetValue(null, true);
            var db = DbStore.Open(path);
            var login = LoginService.Login(db, "admin", "advanced-skills");
            var created = CharFactory.CreateCharacter(db, login.accountId, 0, "五转验证", 1,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", login.accountId);
            Set("CharacterId", created.characterId); Set("Character", CharService.Load(db, created.characterId)); Set("InGame", true);
            GameSession.ApplyToProfile(); Profile.SetJob(122);
            Profile.Data.level = 200;
            if (GameLoop.I != null) GameLoop.I.gameObject.SetActive(false);
            GameLoop.I = null;
            fixture = new GameObject("AdvancedSkillFixture");
            var loop = fixture.AddComponent<GameLoop>(); loop.enabled = false; GameLoop.I = loop;
            loop.state = GameLoop.GameState.Playing;
            var player = fixture.AddComponent<PlayerCtrl>(); player.enabled = false; loop.player = player;
            player.hp = player.hpMax = 60000; player.mp = player.mpMax = 60000;
            loop.field = fixture.AddComponent<FieldDirector>(); loop.field.enabled = false;
            typeof(FieldDirector).GetField("<Map>k__BackingField", Flags).SetValue(loop.field, new FieldMap { id = 100000000 });
            var system = fixture.AddComponent<SkillSystem>();
            var camera = new GameObject("FeedbackCamera").AddComponent<Camera>();
            camera.transform.SetParent(fixture.transform); camera.tag = "MainCamera";
            camera.orthographic = true; camera.orthographicSize = 3; camera.transform.position = new Vector3(0, 0, -10);
            var button = new VCElem { key = KeyCode.Alpha1, cx = .5f, cy = .5f };
            VirtualControls.elems = new List<VCElem> { button };
            KeyMap.Bind(GameAction.Slot1, KeyCode.Alpha1); QuickSlots.Bind(0, 1221009);
            foreach (int id in new[] { 1221029, 1221020, 1221030, 1221015 })
            {
                SkillProgress.SetLevel(id, 30);
                Require(VirtualControls.TryBindSkill(VirtualControls.Center(button), id), "drop accepted " + id);
                int slot = QuickBarKeys.SkillSlot(KeyCode.Alpha1);
                Require(slot >= 0 && QuickSlots.Id(slot) == id, "explicit key retains dragged skill ID " + id);
                var icon = (Texture2D)typeof(VirtualControls).GetMethod("SlotIcon", Flags).Invoke(null, new object[] { button });
                Require(icon == Art.Tex(SkillBook.Get(id).icon), "button icon matches skill book " + id);
                var send = typeof(VirtualControls).GetMethod("SendButton", Flags);
                send.Invoke(null, new object[] { button, true });
                Require(InputHub.Pressed(QuickSlots.Action(slot)), "virtual key feeds mapped skill action " + id);
                send.Invoke(null, new object[] { button, false });
                Require(system.TryCastBoundSkill(QuickSlots.Id(slot), QuickSlots.Action(slot)), "cast " + id + ": " + system.LastFailure);
                if (id == 1221020 || id == 1221030)
                {
                    var video = FindObjectOfType<NativeSkillVideo>();
                    Require(video != null, "Paladin MCV starts from cast " + id);
                    var playback = (McvPlayback)typeof(NativeSkillVideo).GetField("playback", Flags).GetValue(video);
                    Require(playback.Video.Frames.Length > 0, "original MCV parsed");
                    var until = Time.realtimeSinceStartup + 10;
                    byte[] frame = null;
                    while (Time.realtimeSinceStartup < until && (frame = playback.TakeFrame()) == null && playback.Error == null) yield return null;
                    Require(frame != null && playback.Error == null, "original MCV decodes: " + playback.Error);
                }
                system.CancelActions();
                yield return null;
            }
            Require(NativeSkillReplay.Get(1221030).Count > 0, "original server replay schedule is connected");
            KeyConfigWindow.Toggle();
            var keyPosition = NativeKeyLayout.Positions[KeyCode.Alpha1];
            var keyRect = (Rect)typeof(KeyConfigWindow).GetMethod("R", Flags).Invoke(null,
                new object[] { keyPosition.x, keyPosition.y, 32f, 32f });
            Require(KeyConfigWindow.DropSkill(keyRect.center, 1221030), "keyboard window accepts original skill ID");
            var drafts = (Dictionary<int, int>)typeof(KeyConfigWindow).GetField("skillDraft", Flags).GetValue(null);
            foreach (var entry in drafts)
            {
                Require(entry.Value == 1221030, "keyboard draft preserves skill ID");
                var icon = (Texture2D)typeof(KeyConfigWindow).GetMethod("DraftIcon", Flags).Invoke(null,
                    new object[] { QuickSlots.Action(entry.Key) });
                Require(icon == Art.Tex(SkillBook.Get(1221030).icon), "keyboard draft retains original skill icon");
            }
            KeyConfigWindow.Toggle();
            GameSession.Net.ScriptValue(new ScriptValueRequest { characterId = GameSession.CharacterId,
                name = ExplorerAdvancedSkills.CompletionKey, value = "1", write = true });
            ExplorerAdvancedSkills.Invalidate();
            CheckAllVideoRows();
            Profile.Data.SetJob(122);
            typeof(SkillWindow).GetField("tab", Flags).SetValue(null, 5);
            SkillWindow.Visible = true; VirtualControls.OverlayVisible = true; draw = true;
            SkillWindow.PickForTest(1221030);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath, "../docs/advanced-skill-feedback.png"));
            yield return new WaitForEndOfFrame();
            draw = false; SkillWindow.CancelSkillDrag(); SkillWindow.Visible = false;
            Debug.Log("ADVANCED_SKILL_FEEDBACK_PASS drag_explicit_key keyboard_drop icon_identity virtual_input casts=4 paladin_two_original_MCV replay all_39_video_rows");
        }
        finally
        {
            NativeSkillVideo.Stop(NativeSkillVideo.Channel.PlayerSkill);
            if (fixture != null) Destroy(fixture);
            GameLoop.I = null; Set("InGame", false); GameServer.Stop();
            typeof(Profile).GetField("data", Flags).SetValue(null, oldData); saving.SetValue(null, oldSaving);
            VirtualControls.elems = oldElements; VirtualControls.activeName = oldName; VirtualControls.profileNames = oldNames;
            if (manifestBytes != null) File.WriteAllBytes(manifest, manifestBytes); else if (File.Exists(manifest)) File.Delete(manifest);
            if (File.Exists(virtualPath)) File.Delete(virtualPath);
            KeyMap.LoadCharacter(oldKeys); QuickSlots.LoadCharacter(oldSkills); InputHub.ReleaseAllVirtual();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static void CheckAllVideoRows()
    {
        var videos = (Dictionary<string, object>)MiniJson.Parse(Resources.Load<TextAsset>("NativeSkillVideos").text);
        foreach (var video in videos)
        {
            int id = int.Parse(video.Key), job = id / 10000;
            Profile.Data.SetJob(job);
            typeof(SkillWindow).GetField("tab", Flags).SetValue(null, job < 1000 ? 5 : 4);
            var list = (List<int>)typeof(SkillWindow).GetMethod("TabSkills", Flags).Invoke(null, null);
            Require(list.Contains(id), "MCV skill missing from actual panel " + id);
            int offset = Mathf.Clamp(list.IndexOf(id), 0, Mathf.Max(0, list.Count - 4));
            typeof(SkillWindow).GetField("scroll", Flags).SetValue(null, offset);
            typeof(SkillWindow).GetMethod("ClampState", Flags).Invoke(null, new object[] { list });
            int actual = (int)typeof(SkillWindow).GetField("scroll", Flags).GetValue(null);
            Require(list.IndexOf(id) >= actual && list.IndexOf(id) < actual + 4, "MCV row cannot enter viewport " + id);
        }
    }
}
