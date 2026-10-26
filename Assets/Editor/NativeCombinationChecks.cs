using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Exercise the player's real input/animation path without opening a session.
public static class NativeCombinationChecks
{
    public static void Run()
    {
        var terrain = PlayerCtrl.Terrain;
        var climbs = PlayerCtrl.Climbs;
        float min = PlayerCtrl.XMin, max = PlayerCtrl.XMax;
        var actor = new GameObject("NativeCombinationCheck");
        try
        {
            CheckRepeat();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var player = actor.AddComponent<PlayerCtrl>();
            typeof(PlayerCtrl).GetMethod("Awake", flags).Invoke(player, null);
            var animation = (SpriteAnim)typeof(PlayerCtrl).GetField("vis", flags).GetValue(player);
            if (animation == null) throw new Exception("Missing native character animation");
            PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-20, 0, 20, 0) });
            PlayerCtrl.Climbs = null;
            PlayerCtrl.SetBounds(-20, 20);
            player.PlaceAt(0, 0);
            TouchInput.Reset();
            var step = typeof(PlayerCtrl).GetMethod("Step", flags);
            TouchInput.MoveV = -1;
            step.Invoke(player, new object[] { 1f / 60 });
            Require(player.Crouching && animation.Current == "prone", "Down did not select original prone pose");
            player.RequestAttack();
            step.Invoke(player, new object[] { 1f / 60 });
            Require(animation.Current == "proneStab" && animation.IsOneShot, "Down + attack did not select proneStab");
            float attackSpeed = animation.speed;
            int facing = player.facing;
            float x = player.worldX;
            TouchInput.MoveH = -facing;
            TouchInput.PushJump();
            step.Invoke(player, new object[] { 1f / 60 });
            Require(player.grounded, "Grounded attack incorrectly allowed normal jump");
            Require(player.facing == facing && player.worldX == x, "Grounded attack allowed movement/turning");
            Require(animation.speed == attackSpeed, "Movement tick reset attack playback speed");
            TouchInput.MoveH = 0;
            var native = Art.GetClip(animation.kind, animation.key, "proneStab");
            Require(native != null && native.delays.Length > 0, "Missing original proneStab frames/delays");
            CheckAttackTiming(animation, native);
            animation.Play("stand", false);
            TouchInput.MoveV = 0;
            step.Invoke(player, new object[] { 1f / 60 });
            Require(!player.Crouching && animation.Current == "stand", "Releasing Down kept prone pose");
            TouchInput.PushJump();
            step.Invoke(player, new object[] { 1f / 60 });
            TouchInput.MoveV = -1;
            step.Invoke(player, new object[] { 1f / 60 });
            Require(!player.Crouching && !player.grounded, "Airborne Down incorrectly selected prone pose");
            animation.Play("stand", false);
            TouchInput.MoveV = 0;
            typeof(PlayerCtrl).GetField("atkTimer", flags).SetValue(player, 0f);
            player.RequestAttack();
            step.Invoke(player, new object[] { 1f / 60 });
            Require(!player.grounded && animation.Current == "atk", "Jump -> attack rejected airborne basic attack");
            CheckExpression(animation);
            CheckDownJump(player, step, flags);
            Debug.Log("[NativeCombinationChecks] PASS native repeat clock/virtual release, down + attack, attack -> jump gate, jump -> attack, face expression, down jump and per-frame weapon timing");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actor);
            PlayerCtrl.SetTerrain(terrain);
            PlayerCtrl.Climbs = climbs;
            PlayerCtrl.SetBounds(min, max);
            TouchInput.Reset();
        }
    }
    // F1~F7 的表情要真的换掉当前帧：走 PlayerCtrl.Express → SpriteAnim → 原 WZ face/<表情> 合成。
    // 合成失败时 ExpressionFrame 会退回普通帧，只比对返回值看不出来，所以直接比贴图。
    static void CheckExpression(SpriteAnim animation)
    {
        animation.SetBase("stand");
        animation.Play("stand", false);
        var before = animation.Renderer != null ? animation.Renderer.sprite : null;
        Require(before != null, "表情检查拿不到当前角色帧");
        Require(animation.Express(2), "微笑表情没有进入 SpriteAnim.Express");
        Require(animation.Renderer.sprite != before, "微笑表情没有替换当前帧（原 WZ face/smile 未合成）");
        Require(!animation.Express(3), "表情没有按原端 2 秒节流（0xA244B5: < 0x7D0 丢弃）");
    }

    // ↓ + 跳跃 = 穿过脚下平台（源端 0x94C4F8 / 0x9B1C51）。
    // 三层校验：穿得下去、落得到下一层、没落点时不把人送进虚空。
    static void CheckDownJump(PlayerCtrl player, MethodInfo step, BindingFlags flags)
    {
        var terrain = PlayerCtrl.Terrain;
        var animation = (SpriteAnim)typeof(PlayerCtrl).GetField("vis", flags).GetValue(player);
        try
        {
            // 上下两层平台
            PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-20, 0, 20, 0), new Vector4(-20, -3, 20, -3) });
            player.PlaceAt(0, 0);
            TouchInput.Reset();
            // SetBase 决定下一次自动换动作，Play 负责清掉上一段一次性动作；
            // 只调 Play 会让 baseAnim 停在 "jump"，SetBase("jump") 就变成空操作。
            animation.SetBase("stand");
            animation.Play("stand", false);
            TouchInput.MoveV = -1;                 // ↓
            TouchInput.PushJump();
            step.Invoke(player, new object[] { 1f / 60 });
            Require(!player.grounded && player.IsDroppingThrough, "Down + jump did not punch through the foothold");
            Require(player.jumpY < 0f, "Down jump did not leave the platform downwards");
            Require(animation.Current == "jump", "Down jump did not use the native jump action");
            for (int i = 0; i < 240 && !player.grounded; i++) step.Invoke(player, new object[] { 1f / 60 });
            Require(player.grounded && Mathf.Abs(player.terrainY + 3f) < 0.01f,
                "Down jump did not land on the foothold below");
            Require(!player.IsDroppingThrough, "Landing kept the ignored foothold");

            // 下方没有落脚点：退回普通跳跃，不把人送进地图外
            PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-20, 0, 20, 0) });
            player.PlaceAt(0, 0);
            TouchInput.Reset();
            TouchInput.MoveV = -1;
            TouchInput.PushJump();
            step.Invoke(player, new object[] { 1f / 60 });
            Require(!player.IsDroppingThrough && player.jumpY > 0f,
                "Down jump fired without a foothold below");

            // 不按 ↓：仍然是普通跳跃
            player.PlaceAt(0, 0);
            TouchInput.Reset();
            TouchInput.PushJump();
            step.Invoke(player, new object[] { 1f / 60 });
            Require(!player.grounded && player.jumpY > 0f && !player.IsDroppingThrough,
                "Plain jump turned into a down jump");
            // 该段原端标了 forbidFallDown：不许穿下去
            PlayerCtrl.SetTerrain(new List<Vector4> { new Vector4(-20, 0, 20, 0), new Vector4(-20, -3, 20, -3) },
                new List<bool> { true, false });
            player.PlaceAt(0, 0);
            TouchInput.Reset();
            TouchInput.MoveV = -1;
            TouchInput.PushJump();
            step.Invoke(player, new object[] { 1f / 60 });
            Require(!player.IsDroppingThrough && player.jumpY > 0f,
                "forbidFallDown foothold was punched through");
        }
        finally
        {
            PlayerCtrl.SetTerrain(terrain);
            TouchInput.Reset();
        }
    }
    static void CheckAttackTiming(SpriteAnim animation, Art.Clip native)
    {
        // Original integer arithmetic: 90 * 13 / 16 = 73ms, not 73.125ms.
        Require(Mathf.Abs(WzCharacter.AttackFrameDelay(.09f, 3) - .073f) < .000001f,
            "Attack delay lost the source per-frame integer truncation");
        Require(WzCharacter.AttackFrameDelay(.12f, -1) == .09f &&
            WzCharacter.AttackFrameDelay(.12f, 20) == .15f, "Native attack speed bounds mismatch");
        foreach (int speed in new[] { 2, 6, 10 })
        {
            animation.PlayNativeAttack("proneStab", speed);
            float expected = 0;
            foreach (float delay in native.delays) expected += WzCharacter.AttackFrameDelay(delay, speed);
            Require(Mathf.Abs(animation.Duration - expected) < .000001f && animation.speed == 1f,
                "Native attack did not use independently rounded frame delays");
        }
        Require(ReferenceEquals(native, Art.GetClip(animation.kind, animation.key, "proneStab")),
            "Native attack replaced the shared source clip");
        animation.Play("stand", false);
        animation.PlayOnce("proneStab");
        Require(Mathf.Abs(animation.Duration - native.Duration) < .000001f,
            "Attack speed leaked into the shared source clip");
    }
    static void CheckRepeat()
    {
        var clock = new NativeKeyRepeat();
        Require(clock.Delay == 1f && clock.Interval == .4f, "Client fallback timing mismatch");
        clock.Press(18, 0);
        Require(!clock.Poll(true, .999f), "Jump repeated before native initial delay");
        Require(clock.Poll(true, 1f) && !clock.Poll(true, 1.2f) && clock.Poll(true, 1.4f), "Held key repeat cadence mismatch");
        clock.Press(39, 1.5f);
        Require(clock.Key == 39 && !clock.Poll(true, 2f), "New direction did not replace repeat key");
        Require(!clock.Poll(false, 3f) && clock.Key == 0, "Released key kept repeating");
        clock.Configure(0, 31);
        clock.Press(18, 0);
        Require(clock.Poll(true, .25f) && clock.Poll(true, .283f), "Native keyboard speed limits mismatch");
        Require(clock.Poll(true, 10f) && !clock.Poll(true, 10f), "Frame hitch emitted a repeat burst");

        InputHub.ReleaseAllVirtual();
        try
        {
            InputHub.SetVirtualAction(GameAction.Jump, true);
            InputHub.TickRepeat(0, true);
            InputHub.EndFrame();
            InputHub.TickRepeat(1, true);
            Require(InputHub.Repeated(GameAction.Jump), "Held virtual jump did not reach native repeat clock");
            InputHub.TickRepeat(2, false);
            Require(!InputHub.Repeated(GameAction.Jump), "Blocked input repeated jump");
            InputHub.SetVirtualAction(GameAction.Jump, false);
            InputHub.TickRepeat(3, true);
            Require(!InputHub.Repeated(GameAction.Jump), "Virtual release left a repeat event");
            InputHub.EndFrame();
            InputHub.SetVirtualAction(GameAction.Attack, true);
            InputHub.TickRepeat(4, true);
            InputHub.EndFrame();
            InputHub.TickRepeat(5, true);
            Require(InputHub.Repeated(GameAction.Attack), "Attack did not repeat through native clock");
            InputHub.SetVirtualAxis(1, 0);
            InputHub.TickRepeat(5.1f, true);
            InputHub.TickRepeat(6.1f, true);
            Require(!InputHub.Repeated(GameAction.Attack) && InputHub.Repeated(GameAction.Right),
                "Stick direction did not replace the held attack repeat owner");
            InputHub.ClearVirtualAxis();
            InputHub.TickRepeat(7, true);
            Require(!InputHub.Repeated(GameAction.Attack) && !InputHub.Repeated(GameAction.Right),
                "Releasing stick fell back to an older held attack");
        }
        finally { InputHub.ReleaseAllVirtual(); InputHub.EndFrame(); }
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
