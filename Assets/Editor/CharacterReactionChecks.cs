using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class CharacterReactionChecks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void ClearNumbers()
    {
        foreach (var n in UnityEngine.Object.FindObjectsOfType<NativeDamageNumber>())
            UnityEngine.Object.DestroyImmediate(n.gameObject);
    }
    public static void Run()
    {
        try
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(System.IO.File.ReadAllText("tools/native-character-oracle.json"));
            int count = 0;
            var impulse = typeof(PlayerCtrl).GetMethod("ContactImpulse", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (Dictionary<string, object> row in (List<object>)root["cases"])
            {
                var input = (Dictionary<string, object>)row["input"];
                var output = (Dictionary<string, object>)row["output"];
                foreach (string axis in new[] { "x", "y" })
                    Require((double)impulse.Invoke(null, new object[] { Convert.ToDouble(input["v" + axis]), Convert.ToDouble(input["i" + axis]) }) ==
                        Math.Truncate(Convert.ToDouble(output["v" + axis])), "Original impulse mismatch " + count + axis);
                count++;
            }
            int timings = 0;
            foreach (Dictionary<string, object> row in (List<object>)root["timing"])
            {
                int delay = Math.Max(0, Convert.ToInt32(row["due"]) - Convert.ToInt32(row["base"]));
                Require(NativeDamageLines.OrdinaryLineDelay(Convert.ToInt32(row["skill"]), Convert.ToInt32(row["line"])) == delay,
                    "Original damage schedule mismatch " + timings);
                timings++;
            }
            var go = new GameObject("CharacterReactionCheck");
            var p = go.AddComponent<PlayerCtrl>();
            typeof(PlayerCtrl).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, null);
            PlayerCtrl.SetBounds(-20, 20);
            PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-20, 0, 20, 0) });
            p.PlaceAt(0, 0); p.hp = p.hpMax = 100000;
            p.invulnUntil = 0;
            p.TakeDamage(1000, 1);
            Require(p.hp < 100000 && p.worldX == 0 && !p.grounded, "Contact must launch without teleporting");
            Require(Math.Abs(p.invulnUntil - Time.unscaledTime - 1.5f) < .01f, "Contact immunity must be 1500ms");
            float hp = p.hp;
            p.TakeDamage(1000, -1);
            Require(p.hp == hp, "Contact immunity did not reject repeated damage");
            typeof(PlayerCtrl).GetMethod("TickContactTint", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, null);
            Require(p.GetComponentInChildren<SpriteAnim>().Renderer.color == (Color)new Color32(128, 128, 128, 255),
                "Positive contact did not use native gray modulation");
            var animation = p.GetComponentInChildren<SpriteAnim>();
            Require(animation != null && !animation.IsOneShot, "Ordinary hit hard-locked body animation");
            foreach (string action in new[] { "stand", "alert" })
            {
                var clip = Art.GetClip(animation.kind, animation.key, Art.Resolve(animation.kind, animation.key, action, "stand"));
                Require(clip != null && clip.frames.Length == 3, "Native idle frame count " + action);
                foreach (float delay in clip.delays) Require(Math.Abs(delay - (action == "stand" ? .5f : .3f)) < .0001f,
                    "Native idle delay " + action);
            }
            p.PlaceAt(0, 0); TouchInput.Reset();
            var step = typeof(PlayerCtrl).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance);
            step.Invoke(p, new object[] { .008f });
            Require(animation.Current == "alert", "Hit did not enter alert idle");
            TouchInput.MoveH = 1;
            step.Invoke(p, new object[] { .008f });
            Require(animation.Current == "walk", "Alert overrode moving animation");
            TouchInput.Reset();
            typeof(PlayerCtrl).GetField("alertUntil", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(p, Time.unscaledTime - 1);
            p.PlaceAt(0, 0); step.Invoke(p, new object[] { .008f });
            Require(animation.Current == "stand", "Expired alert did not return to weapon stand pose");
            p.invulnUntil = 0; p.ResetCombatState(); p.PlaceAt(0, 0);
            hp = p.hp;
            p.TakeDamage(0, -1);
            Require(p.hp == hp && p.grounded && p.worldX == 0 && p.invulnUntil > Time.unscaledTime + 1.49f,
                "Miss must keep contact immunity without knockback");
            var expression = typeof(SpriteAnim).GetField("expression", BindingFlags.NonPublic | BindingFlags.Instance);
            animation.ClearExpression(); animation.Express(2); animation.ShowHitExpression();
            Require((string)expression.GetValue(animation) == "hit", "Hit face was blocked by manual emotion cooldown");
            Require(Math.Abs(p.ContactBounds.width * Art.PPU - 44) < .001f &&
                Math.Abs(p.ContactBounds.height * Art.PPU - 65) < .001f, "Native player contact rectangle");
            var contactMob = new GameObject("ContactMobCheck").AddComponent<EnemyUnit>();
            contactMob.artKey = "snail";
            var mobRenderer = contactMob.gameObject.AddComponent<SpriteRenderer>();
            var mobAnimation = contactMob.gameObject.AddComponent<SpriteAnim>();
            mobAnimation.Init("mobs", "snail", "stand", mobRenderer);
            typeof(EnemyUnit).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(contactMob, mobRenderer);
            typeof(EnemyUnit).GetField("vis", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(contactMob, mobAnimation);
            var touches = typeof(EnemyUnit).GetMethod("TouchesPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (string action in new[] { "stand", "walk", "hit" })
            {
                mobAnimation.Play(action, false);
                Require(WzAssets.Node(mobAnimation.FrameUri)?.Get("lt") != null, "Action alias lost original frame body " + action);
                p.PlaceAt(0, 0);
                Require((bool)touches.Invoke(contactMob, new object[] { p }), "Native contact missed overlap " + action);
                p.PlaceAt(2, 0);
                Require(!(bool)touches.Invoke(contactMob, new object[] { p }), "Native contact hit separated bodies " + action);
            }
            Require(Math.Abs(mobAnimation.Duration - .6f) < .001f, "Snail hit1 was accelerated away from native 600ms");
            p.PlaceAt(0, 0);
            contactMob.fieldMode = true;
            foreach (int mobId in new[] { 9300018, 9409000, 9409001 })
            {
                contactMob.fieldMobId = mobId;
                Require(WzAssets.FileRoot("Mob/" + mobId + ".img").Int("info/notAttack") == 1,
                    "Training monster must retain original notAttack flag " + mobId);
                Require(!(bool)touches.Invoke(contactMob, new object[] { p }),
                    "notAttack training monster must not hit overlapping player " + mobId);
            }
            contactMob.fieldMobId = 100100;
            Require((bool)touches.Invoke(contactMob, new object[] { p }),
                "Ordinary field snail contact must still hit overlapping player");
            UnityEngine.Object.DestroyImmediate(contactMob.gameObject);
            UnityEngine.Object.DestroyImmediate(go);
            ClearNumbers();
            // Number delivery must survive the target entering its death animation.
            var mob = new GameObject("DeadNumberTarget").AddComponent<EnemyUnit>();
            mob.dead = true;
            NativeDamageLines.Show(mob, new[] { new DamageResult(101, false), new DamageResult(0, false),
                new DamageResult(303, true) }, false, 0);
            var numbers = UnityEngine.Object.FindObjectsOfType<NativeDamageNumber>();
            Require(numbers.Length == 3, "Multi-line damage lost a line or Miss on dead target");
            foreach (var number in numbers) Require(number.transform.localScale == Vector3.one, "Damage number scaled away from original pixels");
            ClearNumbers();
            NativeDamageLines.Show(mob, new[] { new DamageResult(101, false), new DamageResult(0, false),
                new DamageResult(303, true) }, false, 120, false, 0);
            var delivery = UnityEngine.Object.FindObjectOfType<NativeDamageLines>();
            Require(delivery != null && UnityEngine.Object.FindObjectsOfType<NativeDamageNumber>().Length == 1,
                "Normal multi-hit did not defer subsequent lines");
            var start = typeof(NativeDamageLines).GetField("started", BindingFlags.NonPublic | BindingFlags.Instance);
            var tick = typeof(NativeDamageLines).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
            start.SetValue(delivery, Time.realtimeSinceStartupAsDouble - .121);
            tick.Invoke(delivery, null);
            Require(UnityEngine.Object.FindObjectsOfType<NativeDamageNumber>().Length == 2, "120ms second line missing");
            start.SetValue(delivery, Time.realtimeSinceStartupAsDouble - .241);
            tick.Invoke(delivery, null);
            Require(UnityEngine.Object.FindObjectsOfType<NativeDamageNumber>().Length == 3, "240ms third line missing on dead target");
            ClearNumbers(); UnityEngine.Object.DestroyImmediate(mob.gameObject);
            Debug.Log("[CharacterReactionChecks] PASS original x86 impulse " + count + ", damage schedule " + timings + " cases; no teleport, positive/Miss immunity, gray tint, hit face; stand/alert delays and transitions; frame aliases/contact rectangles; dead-target three-line/Miss/critical numbers at 0/120/240ms");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
