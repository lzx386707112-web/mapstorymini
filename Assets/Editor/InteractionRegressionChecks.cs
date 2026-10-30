using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Isolated, deterministic checks; never writes the player's profile or drop DB.
public static class InteractionRegressionChecks
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Get(Type t, object o, string n) => t.GetField(n, Private).GetValue(o);
    static void Set(Type t, object o, string n, object v) => t.GetField(n, Private).SetValue(o, v);
    static object Call(Type t, object o, string n, params object[] a) => t.GetMethod(n, Private).Invoke(o, a);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        object data = Get(typeof(Profile), null, "data"), saving = Get(typeof(Profile), null, "debugOverride");
        var db = DropStore.DbProvider; var drops = DropStore.JsonDrops; var global = DropStore.JsonGlobalDrops;
        var go = new GameObject("Interaction regression"); int exit = 0;
        try
        {
            Set(typeof(Profile), null, "data", new PlayerProfileData()); Set(typeof(Profile), null, "debugOverride", true);
            QuestDef quest = null; int item = 0, amount = 0;
            foreach (int id in QuestCatalog.Ids)
            {
                var q = QuestCatalog.Get(id);
                if (q.completeReq.items.Count == 0 || q.startReq.items.Count != 0 || q.completeReq.items[0].count <= 0) continue;
                quest = q; item = q.completeReq.items[0].item; amount = q.completeReq.items[0].count; break;
            }
            Require(quest != null, "No original collection quest found");
            var rows = new List<DropEntry> {
                new DropEntry { item = 2000000, quest = 0, chance = 999999 },
                new DropEntry { item = 2000001, quest = -1, chance = 999999 },
                new DropEntry { item = item, quest = quest.id, chance = 999999 }
            };
            DropStore.DbProvider = () => null;
            DropStore.JsonDrops = () => new Dictionary<int, List<DropEntry>> { [1] = rows };
            DropStore.JsonGlobalDrops = () => new List<DropEntry> { rows[2] }; DropStore.Invalidate();
            Func<int> roll = () => { int count = 0; FieldLoot.Roll(1, (i, q, d) => count++, new System.Random(1), 1, QuestLogic.NeedQuestItem); return count; };
            Require(roll() == 2, "Unaccepted quest drops leaked");
            Profile.StartQuest(quest.id); Require(roll() == 4, "Active quest mob/global drops missing");
            Profile.AddItem(item, amount); Require(roll() == 2, "Sufficient quest items still drop");
            Profile.CompleteQuest(quest.id); Require(roll() == 2, "Completed quest items still drop");

            InputHub.Enabled = true; InputHub.ReleaseAllVirtual();
            var session = NpcSession.TalkQuest(QuestCatalog.Get(1009), 12101, false);
            Require(session != null && NpcSession.Blocking, "NPC dialog fixture did not open");
            var bridge = go.AddComponent<InputBridge>();
            InputHub.SetVirtualAction(GameAction.Menu, true);
            Call(typeof(InputBridge), bridge, "Update");
            Require(!NpcSession.Blocking && session.Closed, "Escape/menu action did not close NPC dialog");
            Require(!GameSideMenu.Blocking, "Same Escape leaked into side menu");
            InputHub.ReleaseAllVirtual();

            var p = go.AddComponent<PlayerCtrl>(); Call(typeof(PlayerCtrl), p, "Awake"); p.PlaceAt(0, 0);
            var field = go.AddComponent<FieldDirector>(); var map = new FieldMap();
            var portal = new FieldPortal { type = 2, tm = 100000000, x = 0, y = 0 };
            map.portals.Add(portal); Set(typeof(FieldDirector), field, "<Map>k__BackingField", map);
            Set(typeof(FieldDirector), field, "portalLock", 0f); Set(typeof(FieldDirector), field, "portalArmed", false);
            TouchInput.MoveV = 1; Call(typeof(FieldDirector), field, "CheckPortals", p, .016f);
            Require(!(bool)Get(typeof(FieldDirector), field, "portalArmed"), "Held up bounced through arrival portal");
            TouchInput.MoveV = 0; Call(typeof(FieldDirector), field, "CheckPortals", p, .016f);
            Require((bool)Get(typeof(FieldDirector), field, "portalArmed"), "Release did not rearm portal in place");
            TouchInput.MoveV = 1; Call(typeof(FieldDirector), field, "CheckPortals", p, .016f);
            Require((bool)typeof(FieldDirector).GetProperty("Warping", Private).GetValue(field), "Fresh up failed to enter portal without moving");
            Set(typeof(FieldDirector), field, "warpState", Enum.ToObject(typeof(FieldDirector).GetField("warpState", Private).FieldType, 0));
            portal.type = 1; Set(typeof(FieldDirector), field, "portalArmed", false); TouchInput.MoveV = 0;
            Call(typeof(FieldDirector), field, "CheckPortals", p, .016f);
            Require(!(bool)Get(typeof(FieldDirector), field, "portalArmed"), "Touch arrival portal rearmed without leaving");

            Set(typeof(StatusBar), null, "gaugeInit", false);
            Call(typeof(StatusBar), null, "TrackGauges", new object[] { new float[] { .2f, .3f, .1f } });
            Call(typeof(StatusBar), null, "TrackGauges", new object[] { new float[] { .8f, .9f, .2f } });
            var at = (float[])Get(typeof(StatusBar), null, "gaugeAt");
            for (int i = 0; i < 2; i++)
            {
                float middle = (float)Call(typeof(StatusBar), null, "GaugeRatio", i, at[i] + .35f);
                Require(Mathf.Abs(middle - (.5f + i * .1f)) < .001f, "Gauge does not interpolate over 700ms");
            }
            Require((float)Call(typeof(StatusBar), null, "GaugeRatio", 2, Time.unscaledTime) == .2f,
                "Native EXP base should update immediately");
            Func<long,long,long,Rect> temporaryRect = (e,m,t) => (Rect)Call(typeof(StatusBar), null, "TemporaryExpRect", e,m,t);
            Require(temporaryRect(20,100,0).width == 0, "Ordinary EXP generated a fake temporary strip");
            Require(temporaryRect(20,100,30) == new Rect(29,1,34,15), "Banked EXP native integer geometry mismatch");
            Require(temporaryRect(90,100,30) == new Rect(109,1,11,15), "Banked EXP overflow was not capped at level end");
            Require(temporaryRect(0,0,30) == Rect.zero && temporaryRect(100,100,30).width == 0,
                "Max level/full EXP retained temporary strip");
            Require(temporaryRect(0,100,30) == new Rect(6,1,34,15), "Level rollover retained previous EXP position");
            var vis = (SpriteAnim)Get(typeof(PlayerCtrl), p, "vis");
            p.PresentDeath(0); Require(vis.transform.localPosition == new Vector3(-10, 20, 0) / Art.PPU && vis.Renderer.color.a == 0, "Death origin/initial alpha mismatch");
            p.PresentDeath(.5f); Require(vis.transform.localPosition == new Vector3(0, 30, 0) / Art.PPU && Mathf.Abs(vis.Renderer.color.a - .2f) < .001f, "Death rotation/alpha mismatch");
            p.PresentDeath(2.5f); Require(vis.Renderer.color.a == 1, "Death alpha did not finish in 2500ms");
            p.ClearDeathPresentation(); Require(vis.transform.localPosition == Vector3.zero && vis.Renderer.color == Color.white, "Death presentation leaked into revival");
            Debug.Log("[InteractionRegressionChecks] PASS quest drop lifecycle (mob/global), manual portal release/repress, touch portal safety, HP/MP 700ms and death vector/alpha/reset");
        }
        catch (Exception e) { Debug.LogException(e); exit = 1; }
        finally
        {
            DropStore.DbProvider = db; DropStore.JsonDrops = drops; DropStore.JsonGlobalDrops = global; DropStore.Invalidate();
            TouchInput.Reset(); PlayerCtrl.InputLocked = false; ScreenFade.Set(0);
            NpcSession.CloseActive(); InputHub.ReleaseAllVirtual();
            UnityEngine.Object.DestroyImmediate(go);
            Set(typeof(Profile), null, "data", data); Set(typeof(Profile), null, "debugOverride", saving);
        }
        EditorApplication.Exit(exit);
    }
}
