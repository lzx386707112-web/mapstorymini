using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NativeSkillNodeChecks
{
    public static void Require(bool ok, string label) { if (!ok) throw new Exception(label); }
    public static void Run()
    {
        try
        {
            NativeGameplayNodeChecks.Check();
            var fixture = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText("tools/native_skill_nodes.json"));
            int cases = 0, clips = 0, frames = 0;
            foreach (Dictionary<string, object> row in (List<object>)fixture["cases"])
            {
                int id = Convert.ToInt32(row["id"]), level = Convert.ToInt32(row["level"]);
                string key = NativeSkillVfx.Key(id, level);
                foreach (var expected in (Dictionary<string, object>)row["clips"])
                {
                    var clip = NativeSkillVfx.Get(key, expected.Key);
                    Require(clip != null && clip.frames.Length == Convert.ToInt32(expected.Value),
                        "WZ parser mismatch: " + key + "/" + expected.Key);
                    foreach (var frame in clip.frames)
                    {
                        var node = WzAssets.Node(frame.file);
                        Require(node != null && node.Kind == "Canvas" && node.Width > 0 && node.Height > 0 && frame.delay > 0,
                            "Unresolved Canvas: " + frame.file);
                        frames++;
                    }
                    clips++;
                }
                cases++;
            }
            Debug.Log("[NativeSkillNodeChecks] PASS " + fixture["skillCount"] + " skills / " + cases +
                " level cases / " + clips + " clips / " + frames + " frame references against independent wzpy inventory");
            foreach (int id in new[] { 1311001, 1311002 })
            foreach (int level in new[] { 1, 30 })
            {
                var clip = Art.GetClip("chars", "knight_h0", "skill_" + id + "@" + level);
                Require(clip != null && clip.frames.Length > 0 && clip.frames[0].Contains(level == 1 ? "/burster1/" : "/burster2/"),
                    "Level-local scalar action ignored: " + id + "@" + level);
            }
            foreach (var entry in new[] { (id:4111006, frames:5), (id:14101004, frames:8), (id:11101005, frames:6) })
            {
                var clip = NativeSkillVfx.Get(NativeSkillVfx.Key(entry.id, 1), "movement");
                Require(clip != null && clip.frames.Length == entry.frames &&
                    clip.frames[0].file.StartsWith("wz://Effect/BasicEff.img/"), "Missing native movement effect " + entry.id);
                Require(Art.FrameAt(clip.frames[0].file,clip.frames[0].origin) != null, "Movement effect cannot decode " + entry.id);
            }
            Require(NativeSkillVfx.Get(NativeSkillVfx.Key(1121012, 1), "screen") != null,
                "ID lookup lost audited Map/Effect supplemental layer");
            SessionState.SetBool("NativeSkillNodeChecks", true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool("NativeSkillNodeChecks", false) || !EditorApplication.isPlaying) return;
        SessionState.SetBool("NativeSkillNodeChecks", false);
        new GameObject("NativeSkillNodeProbe").AddComponent<NativeSkillNodeProbe>();
    }
}

public sealed class NativeSkillNodeProbe : MonoBehaviour
{
    IEnumerator Start()
    {
        var routine = Check();
        while (true)
        {
            object next;
            try { if (!routine.MoveNext()) break; next = routine.Current; }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); yield break; }
            yield return next;
        }
        EditorApplication.Exit(0);
    }
    IEnumerator Check()
    {
        // In-memory profile only; no login, database, or preference writes.
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        typeof(Profile).GetField("data", flags).SetValue(null, new PlayerProfileData());
        typeof(Profile).GetField("debugOverride", flags).SetValue(null, true);
        var gameObject = new GameObject("InactiveSkillGame");
        gameObject.SetActive(false);
        var game = gameObject.AddComponent<GameLoop>();
        GameLoop.I = game; game.state = GameLoop.GameState.Playing;
        var actor = new GameObject("SkillPlayer"); actor.AddComponent<SpriteRenderer>();
        var player = actor.AddComponent<PlayerCtrl>(); player.enabled = false;
        PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-100, 0, 100, 0) });
        player.PlaceAt(0, 0); game.player = player;
        var system = actor.AddComponent<SkillSystem>();
        new GameObject("SkillHitFeelRunner").AddComponent<HitFeelRunner>();
        var cameraObject = new GameObject("SkillCamera");
        var camera = cameraObject.AddComponent<Camera>(); cameraObject.tag = "MainCamera";
        camera.orthographic = true; camera.orthographicSize = 1.6f;
        camera.transform.position = new Vector3(2, .5f, -10);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.cullingMask = 1 << 30;
        var target = new RenderTexture(256, 128, 24); camera.targetTexture = target;
        var copy = new Texture2D(256, 128, TextureFormat.RGBA32, false);
        Directory.CreateDirectory("docs/skill-regression");
        for (int level = 1; level <= 3; level++)
        {
            yield return new WaitForSeconds(.5f);
            Profile.Data.SetSkillLevel(1000, level); player.mp = 1000; player.facing = 1;
            Debug.Log("[NativeSkillNodeChecks] casting snail level " + level + ", duration=" + player.GetSkillActionDuration("1000") +
                ", ball=" + NativeSkillVfx.Parts(NativeSkillVfx.Key(1000, level), "ball").Count + ", timeScale=" + Time.timeScale);
            NativeSkillNodeChecks.Require(system.TryCastSkill(1000), "Snail ID cast rejected: " + system.LastFailure);
            SkillFx ball = null;
            float until = Time.time + 3;
            while (Time.time < until && ball == null)
            {
                foreach (var fx in FindObjectsOfType<SkillFx>()) if (fx.Part == "ball") ball = fx;
                yield return null;
            }
            NativeSkillNodeChecks.Require(ball != null && ball.CurrentSprite != null, "No level-local snail projectile");
            NativeSkillNodeChecks.Require(ball.CurrentSprite.name.Contains("level/" + level + "/ball"), "Wrong level art");
            ball.gameObject.layer = 30;
            var previous = RenderTexture.active; camera.Render(); RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, 256, 128), 0, 0); copy.Apply(); RenderTexture.active = previous;
            int visible = 0; foreach (var px in copy.GetPixels32()) if (px.a > 0) visible++;
            NativeSkillNodeChecks.Require(visible > 20, "Projectile is not visible in rendered camera");
            File.WriteAllBytes("docs/skill-regression/snail-level-" + level + ".png", copy.EncodeToPNG());
            float x = ball.transform.position.x;
            yield return new WaitForSeconds(.08f);
            NativeSkillNodeChecks.Require(ball != null && ball.transform.position.x > x, "Projectile did not travel");
            system.CancelActions(); yield return null;
            NativeSkillNodeChecks.Require(FindObjectsOfType<SkillFx>().Length == 0, "Cancelled projectile leaked");

            var enemyObject = new GameObject("SnailTarget");
            var enemy = enemyObject.AddComponent<EnemyUnit>(); enemy.artKey = "snail"; enemy.Init(2, 3, 0);
            enemy.enabled = false; EnemyUnit.All.Add(enemy); enemy.hp = enemy.hpMax = 10000;
            player.mp = 1000;
            NativeSkillNodeChecks.Require(system.TryCastSkill(1000), "Targeted snail cast rejected");
            until = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < until && enemy.hp == 10000) yield return null;
            int damage = SkillBook.Native(1000, level).FixedDamage;
            NativeSkillNodeChecks.Require(enemy.hp == 10000 - damage, "Fixed-damage hit mismatch");
            bool hit = false;
            foreach (var fx in FindObjectsOfType<SkillFx>())
                hit |= fx.Part == "hit/0" && fx.CurrentSprite.name.Contains("level/" + level + "/hit/0");
            NativeSkillNodeChecks.Require(hit, "Missing level-local hit effect");
            EnemyUnit.All.Remove(enemy); Destroy(enemyObject);
            system.CancelActions(); yield return new WaitForSeconds(.6f);
        }
        player.facing = -1; player.mp = 1000;
        NativeSkillNodeChecks.Require(system.TryCastSkill(1000), "Left-facing cast rejected");
        yield return new WaitForSeconds(.45f);
        var left = FindObjectOfType<SkillFx>();
        NativeSkillNodeChecks.Require(left != null && left.Part == "ball" && left.transform.position.x < player.FeetPos.x &&
            !left.GetComponent<SpriteRenderer>().flipX, "Left-facing projectile placement/flip mismatch");
        system.CancelActions();
        Debug.Log("[NativeSkillNodeChecks] PASS Three Snails levels 1/2/3: ID casts, rendered pixels, travelling empty casts, targeted hits, fixed damage, cancellation");
        player.enabled = true;
        PlayerCtrl.InputLocked = true;
        yield return null;
        foreach (int id in new[] { 3111002, 3111005, 5211001, 2321020 })
        {
            var definition = SkillBook.Native(id, 1);
            NativeSkillNodeChecks.Require(NativeBuffs.For(player).ApplySkill(id, 1, NativeBuffs.Now), "Summon status registration failed: " + id);
            var summon = NativeSummon.Spawn(system, player,
                new SkillDef(new ClassSkill { name = id.ToString(), art = id.ToString() }, definition));
            summon.gameObject.layer = 30;
            float appear = NativeMobController.AnimationMillis(NativeSummon.Root(id, 1).Get("summoned")) / 1000f;
            yield return new WaitForSeconds(appear + .15f);
            var renderer = summon.GetComponent<SpriteRenderer>();
            var sprite = renderer.sprite;
            NativeSkillNodeChecks.Require(sprite != null, "No summon sprite " + id);
            camera.transform.position = new Vector3(renderer.bounds.center.x, renderer.bounds.center.y, -10);
            camera.orthographicSize = Mathf.Max(1.6f, renderer.bounds.extents.y + .2f);
            var previous = RenderTexture.active; camera.Render(); RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, 256, 128), 0, 0); copy.Apply(); RenderTexture.active = previous;
            int visible = 0; foreach (var px in copy.GetPixels32()) if (px.a > 0) visible++;
            NativeSkillNodeChecks.Require(visible > 20, "Summon invisible " + id);
            File.WriteAllBytes("docs/skill-regression/summon-" + id + ".png", copy.EncodeToPNG());
            var start = summon.Position;
            player.PlaceAt(player.FeetPos.x + 2, 0);
            yield return new WaitForSeconds(.1f);
            if (NativeSummon.Stationary(id))
                NativeSkillNodeChecks.Require(summon.Position == start, "Stationary summon followed owner " + id);
            else NativeSkillNodeChecks.Require(summon.Position != start, "Following summon did not move " + id);
            Destroy(summon.gameObject);
            yield return null;
        }
        player.enabled = false;
        PlayerCtrl.InputLocked = false;
        Profile.Data.SetSkillLevel(1101004, 1);
        var cost = SkillBook.Native(1101004, 1);
        NativeSkillNodeChecks.Require(cost.HpCost > 0, "HP-cost fixture missing");
        player.hp = cost.HpCost; player.mp = 1000;
        NativeSkillNodeChecks.Require(!system.TryCastSkill(1101004) && player.hp == cost.HpCost && player.mp == 1000 &&
            system.RemainingById(1101004) == 0, "HP-cost rejection mutated cast state");
        player.hp = cost.HpCost + 1;
        NativeSkillNodeChecks.Require(system.TryCastSkill(1101004) && player.hp == 1 && player.mp == 1000 - cost.MpCost,
            "Successful cast did not deduct exact HP/MP cost");
        system.CancelActions();
        Debug.Log("[NativeSkillNodeChecks] PASS four summon renders/stationary behavior and HP-cost cast transactions");
        foreach (int id in new[] { 4111006, 14101004, 11101005 })
        {
            yield return new WaitForSeconds(.6f);
            Profile.Data.SetSkillLevel(id, 1); player.PlaceAt(0, 0); player.mp = 1000;
            player.grounded = id == 11101005;
            NativeSkillNodeChecks.Require(system.TryCastSkill(id), "Movement cast rejected " + id + ": " + system.LastFailure);
            NativeSkillNodeChecks.Require(!player.grounded && !player.CanUseFlashJump(id), "Movement impulse/once-per-jump state missing " + id);
            yield return null;
            SkillFx movement = null;
            foreach (var fx in FindObjectsOfType<SkillFx>()) if (fx.Part == "movement") movement = fx;
            NativeSkillNodeChecks.Require(movement != null && movement.CurrentSprite != null, "Movement cast has no rendered sprite " + id);
            movement.gameObject.layer = 30;
            var renderer = movement.GetComponent<SpriteRenderer>();
            camera.transform.position = new Vector3(renderer.bounds.center.x, renderer.bounds.center.y, -10);
            camera.orthographicSize = Mathf.Max(1.6f, renderer.bounds.extents.y + .2f);
            var previous = RenderTexture.active; camera.Render(); RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, 256, 128), 0, 0); copy.Apply(); RenderTexture.active = previous;
            int visible = 0; foreach (var px in copy.GetPixels32()) if (px.a > 0) visible++;
            NativeSkillNodeChecks.Require(visible > 20, "Movement effect invisible " + id);
            File.WriteAllBytes("docs/skill-regression/movement-" + id + ".png", copy.EncodeToPNG());
            system.CancelActions();
        }
        Debug.Log("[NativeSkillNodeChecks] PASS three movement ID casts, impulse state, original effect rendered pixels");
        var field = gameObject.AddComponent<FieldDirector>(); field.enabled = false; game.field = field;
        var map = FieldCatalog.GetMap(100000005);
        typeof(FieldDirector).GetProperty("Map").SetValue(field, map);
        var serverMap = Server.Bot.Environment.BotNavigationMapLoader.Load().Geometry(map.id);
        Server.Field.MapService.Unregister(map.id); // No dependency on bot registration.
        int teleport = 2101002, range = Art.Int(SkillBook.Get(teleport).Level(1), "range");
        bool found = false; int startX = 0, startY = 0;
        foreach (var f in serverMap.footholds)
        {
            if (f.x1 >= f.x2) continue;
            int x = Mathf.RoundToInt((f.x1 + f.x2) / 2), y = Mathf.RoundToInt(f.y1 + (x - f.x1) * (f.y2 - f.y1) / (f.x2 - f.x1));
            if (!NativeTeleportMovement.Destination(serverMap, x, y, 1, 0, range, out _, out _)) continue;
            startX = x; startY = y; found = true; break;
        }
        NativeSkillNodeChecks.Require(found, "Teleport map fixture has no directional foothold destination");
        PlayerCtrl.SetBounds(map.ViewMinX,map.ViewMaxX);
        Profile.Data.SetSkillLevel(teleport, 1); player.PlaceAt(map.WorldX(startX), map.WorldY(startY));
        player.mp = 1000; TouchInput.MoveH = 1; TouchInput.MoveV = 0;
        float beforeX = player.FeetPos.x;
        NativeSkillNodeChecks.Require(system.TryCastSkill(teleport), "Teleport ID cast rejected: " + system.LastFailure);
        NativeSkillNodeChecks.Require(Mathf.Abs(player.FeetPos.x - beforeX - range / Art.PPU) < .001f &&
            player.mp == 1000 - SkillBook.Native(teleport, 1).MpCost, "Teleport landing/cost mismatch");
        system.CancelActions(); yield return new WaitForSeconds(.2f);
        TouchInput.MoveH = 0; player.mp = 1000;
        NativeSkillNodeChecks.Require(!system.TryCastSkill(teleport) && player.mp == 1000, "Rejected teleport consumed MP");
        game.field = null;
        Debug.Log("[NativeSkillNodeChecks] PASS teleport ID cast on original map: directional landing, exact MP, invalid direction no charge");
    }
}
