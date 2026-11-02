using System;
using System.Diagnostics;
using UnityEngine;

// Targeted user regressions; no login, database or profile writes.
public static class FeedbackRegressionChecks
{
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        var warrior = Profile.BuildAutoApPlan(100,20,10,new CharacterAttributes { str=80,dex=35 });
        Require(warrior[0]==5 && warrior[1]==5, "auto AP secondary priority and remaining primary");
        var gun = Profile.BuildAutoApPlan(500,30,5,new CharacterAttributes { str=28,dex=120 });
        var fist = Profile.BuildAutoApPlan(500,30,5,new CharacterAttributes { str=33,dex=48 },true);
        Require(gun[0]==2 && gun[1]==3 && fist[0]==3 && fist[1]==2, "pirate dual auto AP choices");
        Require(Profile.BuildAutoApPlan(100,20,0,new CharacterAttributes())==null, "zero AP rejection");
        var tabs = SkillBook.Tabs(SkillBook.TreeForJob(112));
        int[] books = {0,100,110,111,112};
        Require(tabs.Count==books.Length, "advancement retains all acquired books");
        for (int i=0;i<books.Length;i++) Require(Art.Int(tabs[i],"job")==books[i], "job tab order " + i);
        var frames = new[] { .3f, .15f, .35f };
        Require(PlayerCtrl.NativeMeleeImpactMs(frames, 6, 0) == 450, "normal weapon base impact");
        Require(PlayerCtrl.NativeMeleeImpactMs(frames, 2, 0) == 336, "per-frame integer truncation before ratio");
        Require(PlayerCtrl.NativeMeleeImpactMs(frames, 6, 100) == 600, "distance-dependent native hit time");
        Require(PlayerCtrl.NativeMeleeImpactMs(frames, 6, -1) == 452, "x86 nearest-even distance conversion");
        var previous = NativeWeatherHost.Shared;
        var cameraObject = new GameObject("FeedbackWeatherCamera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 10; camera.aspect = 2;
        GameObject mapObject = null;
        try
        {
            var regions = new NativeWeatherRegion[8];
            for (int i = 0; i < regions.Length; i++)
                regions[i] = new NativeWeatherRegion { Forced = 0, Weights = new double[9] };
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var weather = new NativeWeatherService(regions, now, () => .25);
            NativeWeatherHost.Shared = weather;
            for (int pass = 0; pass < 2; pass++)
            {
                mapObject = new GameObject("FeedbackPerion");
                var view = mapObject.AddComponent<NativeMapView>();
                var watch = Stopwatch.StartNew();
                view.Show(102000000);
                UnityEngine.Debug.Log("FEEDBACK_PERION map_ms=" + watch.Elapsed.TotalMilliseconds + " leaf_on_entry=" + (pass == 1));
                var field = mapObject.GetComponent<NativeWeatherField>();
                Require(field != null, "Perion weather bound");
                weather.Override(6, null, 600000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                watch.Restart(); field.Tick();
                var lateUpdate = typeof(NativeWeatherField).GetMethod("LateUpdate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                lateUpdate.Invoke(field, null);
                UnityEngine.Debug.Log("FEEDBACK_PERION leaf_transition_ms=" + watch.Elapsed.TotalMilliseconds);
                var particles = mapObject.GetComponent<NativeWeatherParticles>();
                Require(particles != null && particles.Count > 0, "actual maple leaf particles");
                int count = particles.Count;
                field.Tick(); lateUpdate.Invoke(field, null);
                Require(particles.Count == count, "unchanged leaf weather does not rebuild particles");
                UnityEngine.Object.DestroyImmediate(mapObject); mapObject = null;
            }
            UnityEngine.Debug.Log("FEEDBACK_REGRESSION_PASS auto_ap skill_parent_tabs melee_native_timing perion_leaf_entry_and_transition");
        }
        finally
        {
            if (mapObject != null) UnityEngine.Object.DestroyImmediate(mapObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            NativeWeatherHost.Shared = previous;
        }
    }
}
