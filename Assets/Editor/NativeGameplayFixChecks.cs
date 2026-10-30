using System;
using System.Collections.Generic;
using System.Reflection;
using MapleDash.Wz;
using UnityEditor;
using UnityEngine;

// Regression of original Say.img questions, below-rope passing, portable chairs,
// training portal artwork and death timeline. No player-save writes.
public static class NativeGameplayFixChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    public static void Run()
    {
        string snapshot = JsonUtility.ToJson(Profile.Data);
        var saving = typeof(Profile).GetField("debugOverride", BindingFlags.Static | BindingFlags.NonPublic);
        bool previous = (bool)saving.GetValue(null);
        saving.SetValue(null, true);
        int exit = 0;
        GameObject playerGo = null, mapGo = null, deathGo = null;
        try
        {
            Profile.Data.questStarted.Clear(); Profile.Data.questCompleted.Clear(); Profile.Data.questProgress.Clear();
            // Read every wrong/correct link from IMG, including correct choices other than zero.
            for (int id = 1009; id <= 1015; id++)
            {
                var q = QuestCatalog.Get(id);
                Profile.StartQuest(id);
                var say = WzAssets.Node("Quest/Say.img/" + id + "/1");
                var question = DialogText.Parse(say.Text("0"));
                foreach (int link in DialogFlow.Links(question))
                {
                    var session = NpcSession.TalkQuest(q, 12101, true);
                    Require(session != null && session.Dialog.Mode == NpcDialogMode.Simple, "Say ask must display options: " + id);
                    long exp = Profile.Data.experience;
                    session.Reply(1, link);
                    string wrong = say.Text("stop/0/" + link);
                    if (!string.IsNullOrEmpty(wrong))
                    {
                        Require(session.Dialog.RawText == wrong && session.Dialog.Mode == NpcDialogMode.Ok, "Wrong answer must use original stop text: " + id + "/" + link);
                        session.Reply(1);
                        Require(session.Closed && Profile.IsQuestStarted(id) && Profile.Data.experience == exp, "Wrong answer completed/rewarded quest: " + id);
                    }
                    else Require(session.Dialog.RawText == say.Text("1"), "Correct answer did not advance: " + id + "/" + link);
                    NpcSession.CloseActive();
                }
            }
            playerGo = new GameObject("GameplayFixPlayer");
            var p = playerGo.AddComponent<PlayerCtrl>(); Call(p, "Awake");
            PlayerCtrl.SetBounds(-20, 20);
            PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-20, 0, 20, 0) });
            var rope = new Art.ClimbLine { x = 0, bottom = 50f / Art.PPU, top = 5 };
            PlayerCtrl.Climbs = new List<Art.ClimbLine> { rope };
            p.PlaceAt(0, 0);
            Set(p, "climbInput", 1f);
            Require(!(bool)Call(p, "StepClimb", 1f, false, 1f / 60) && !p.IsClimbing && p.FeetPos.y == 0,
                "Walking below rope, even with upward joystick component, must not lift player");
            p.PlaceAt(0, 2); p.grounded = false;
            Require((bool)Call(p, "StepClimb", 0f, false, 1f / 60) && p.IsClimbing, "Rope in reach no longer attaches");
            p.PlaceAt(0, 0); p.hp = p.hpMax;
            Require(p.UseChair(3010000) && p.Sitting && p.ChairId == 3010000, "Portable chair cannot be used");
            Require(p.GetComponent<NativeBotChair>().GetComponentsInChildren<SpriteRenderer>().Length > 1, "Chair IMG effect missing");
            p.RequestAttack(); Require(!p.Sitting && p.ChairId == 0, "Attack retained chair");
            Require(p.UseChair(3010001), "Second chair cannot be used");
            p.PlaceAt(1, 0); Require(!p.Sitting && p.ChairId == 0, "Map placement retained chair");
            p.UseChair(3010000); p.PlayDeadPose(); Require(!p.Sitting && p.ChairId == 0, "Death retained chair");
            mapGo = new GameObject("TrainingPortalCheck");
            mapGo.AddComponent<NativeMapView>().Show(1010000);
            var portal = mapGo.transform.Find("Portal_1");
            Require(portal != null && portal.GetComponent<SpriteRenderer>().sprite != null, "Mai training portal has no visible original artwork");
            var authored = NativeMapView.MapRoot(1010000).Get("portal/1");
            Require(authored.Int("pt") == 7 && authored.Text("script") == "entertraining", "Training portal script/type changed");
            deathGo = new GameObject("DeathTimelineCheck");
            var flow = deathGo.AddComponent<DeathFlow>(); Call(flow, "SpawnTombstone", p);
            flow.phase = DeathFlow.Phase.Dying;
            var tomb = (GameObject)Get(flow, "tombGo");
            Require(Mathf.Abs(tomb.transform.position.y - p.FeetPos.y - 600f / Art.PPU) < .001f, "Tomb does not start 600px above player");
            Set(flow, "timer", .9f); Call(flow, "Update");
            Require(Mathf.Abs(tomb.transform.position.y - p.FeetPos.y) < .001f, "Tomb has not landed after 880ms");
            Set(flow, "timer", 2.1f); Call(flow, "Update");
            Require(tomb.GetComponent<SpriteRenderer>().sprite == WzAssets.Sprite(WzAssets.Node("Effect/Tomb.img/land/0")), "Tomb never reaches land frame");
            Require(flow.phase == DeathFlow.Phase.Dying, "Death notice appeared before native delay");
            Set(flow, "timer", 2.3f); Call(flow, "Update");
            Require(flow.phase == DeathFlow.Phase.Confirm, "Death confirmation never opens");
            var notice = Art.Tex("wz://UI/UIWindow.img/Notice/0");
            Require(notice != null && notice.width == 286 && notice.height == 146, "Native death notice missing");
            Require(Art.Tex("wz://UI/Basic.img/BtOK2/normal/0") != null, "Native death confirmation button missing");
            flow.ForceClear();
            Debug.Log("[NativeGameplayFixChecks] PASS: all Rein IMG answers, rope reach, chair lifecycle, training portal artwork, tomb/drop/land and native death notice");
        }
        catch (Exception e) { Debug.LogException(e); exit = 1; }
        finally
        {
            NpcSession.CloseActive(); TouchInput.Reset(); PlayerCtrl.Climbs = null;
            if (deathGo != null) UnityEngine.Object.DestroyImmediate(deathGo);
            if (mapGo != null) UnityEngine.Object.DestroyImmediate(mapGo);
            if (playerGo != null) UnityEngine.Object.DestroyImmediate(playerGo);
            JsonUtility.FromJsonOverwrite(snapshot, Profile.Data); saving.SetValue(null, previous);
        }
        EditorApplication.Exit(exit);
    }
}
