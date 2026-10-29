using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class RopeJumpChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
    public static void Run()
    {
        try
        {
            foreach (bool ladder in new[] { false, true })
            foreach (int direction in new[] { -1, 1 })
            foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 120 })
            {
                var go = new GameObject("RopeJumpCheck");
                var p = go.AddComponent<PlayerCtrl>();
                typeof(PlayerCtrl).GetMethod("Awake", Private).Invoke(p, null);
                PlayerCtrl.SetBounds(-20, 20);
                PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-20, -10, 20, -10) });
                var line = new Art.ClimbLine { x = 0, bottom = -1, top = 5, ladder = ladder };
                PlayerCtrl.Climbs = new List<Art.ClimbLine> { line };
                p.PlaceAt(0, 2); p.grounded = false;
                var climb = typeof(PlayerCtrl).GetField("climb", Private);
                var stepClimb = typeof(PlayerCtrl).GetMethod("StepClimb", Private);
                climb.SetValue(p, line);
                var animation = p.GetComponentInChildren<SpriteAnim>();
                animation.SetBase(ladder ? "ladder" : "rope"); animation.speed = 0;
                Require((bool)stepClimb.Invoke(p, new object[] { 0f, true, dt }) && p.IsClimbing,
                    "Jump without horizontal input detached rope/ladder");
                Require(!(bool)stepClimb.Invoke(p, new object[] { (float)direction, true, dt }) && !p.IsClimbing,
                    "Direction plus jump failed to detach rope/ladder");
                Require(Math.Abs((float)typeof(PlayerCtrl).GetField("vy", Private).GetValue(p) - p.jumpSpeed * .5f) < .00001,
                    "Rope jump vertical speed differs from original 0.5 multiplier");
                Require(Math.Abs((double)typeof(PlayerCtrl).GetField("horizontalVelocity", Private).GetValue(p) -
                    direction * p.moveSpeed * Art.PPU * 1.3) < .00001, "Rope jump horizontal launch speed");
                Require(p.facing == direction, "Rope jump facing");
                TouchInput.Reset(); TouchInput.MoveH = direction;
                typeof(PlayerCtrl).GetMethod("Step", Private).Invoke(p, new object[] { dt });
                Require(animation.Current == "jump" && !animation.IsOneShot && animation.speed == 1,
                    "Rope idle/pause leaked into jump animation");
                Require(!p.IsClimbing && p.worldX * direction > 0 && p.FeetPos.y > 2,
                    "Rope jump trajectory or grab cooldown regressed");
                UnityEngine.Object.DestroyImmediate(go);
            }
            TouchInput.Reset(); PlayerCtrl.Climbs = null;
            Debug.Log("[RopeJumpChecks] PASS rope/ladder left/right at 30/60/120Hz; direction requirement, 0.5 vertical/1.3 horizontal launch, facing and unpaused jump action");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
