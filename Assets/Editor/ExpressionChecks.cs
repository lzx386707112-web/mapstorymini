using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 真实游玩环境下的表情回归：不进登录流程，直接 StartGame 拿到真角色外观，
// 然后走 InputBridge 真正用的那条派发（NativeKeyActions.Execute(FaceXxx)）。
// 关键点：只看「返回 true / sprite 对象变了」会被合成失败退回普通帧骗过，
// 所以这里逐像素比对，并把每个表情落成 PNG 供人眼核对。
[InitializeOnLoad]
public static class ExpressionChecks
{
    const string Key = "MapleDash.ExpressionChecks";
    static readonly string[] SaveKeys = { "mxd.profile.v1", "mxd.profile.v1.backup" };
    static readonly GameAction[] Faces = {
        GameAction.FaceHit, GameAction.FaceSmile, GameAction.FaceTroubled, GameAction.FaceCry,
        GameAction.FaceAngry, GameAction.FaceBewildered, GameAction.FaceStunned
    };
    static readonly string[] FaceNames = { "hit", "smile", "troubled", "cry", "angry", "bewildered", "stunned" };
    const string OutDir = "/tmp/expr";
    static double next;
    static ExpressionChecks() { EditorApplication.update += Tick; }

    public static void Run()
    {
        for (int i = 0; i < SaveKeys.Length; i++)
        {
            SessionState.SetBool(Key + i + "exists", PlayerPrefs.HasKey(SaveKeys[i]));
            SessionState.SetString(Key + i, PlayerPrefs.GetString(SaveKeys[i], ""));
        }
        SessionState.SetInt(Key + "phase", 0);
        SessionState.SetBool(Key + "failed", false);
        SessionState.SetFloat(Key + "start", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void Require(bool condition, string label)
    {
        if (!condition) throw new Exception("[ExpressionChecks] " + label);
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        int phase = SessionState.GetInt(Key + "phase", 0);
        if (phase == 9 && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            for (int i = 0; i < SaveKeys.Length; i++)
            {
                if (SessionState.GetBool(Key + i + "exists", false)) PlayerPrefs.SetString(SaveKeys[i], SessionState.GetString(Key + i, ""));
                else PlayerPrefs.DeleteKey(SaveKeys[i]);
            }
            PlayerPrefs.Save();
            SessionState.SetBool(Key, false);
            EditorApplication.Exit(SessionState.GetBool(Key + "failed", false) ? 1 : 0);
            return;
        }
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "start", 0) > 180)
        {
            Fail(new Exception("[ExpressionChecks] timed out")); return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < next) return;
        var game = GameLoop.I;
        if (game == null) return;
        try
        {
            switch (phase)
            {
                case 0:
                    typeof(Profile).GetField("data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, new PlayerProfileData());
                    if (game.player != null) game.TeardownBattle();
                    game.StartGame();
                    // 原版地图模式（FieldDirector）下没有章节 stage，别在这里空引用
                    if (game.stage != null) game.stage.StopSession();
                    game.player.hp = game.player.hpMax;
                    game.player.invulnUntil = Time.unscaledTime + 1000;
                    next = EditorApplication.timeSinceStartup + 0.5;
                    break;
                case 1:
                    Check(game);
                    Debug.Log("[ExpressionChecks] PASS " + SessionState.GetString(Key + "report", ""));
                    SessionState.SetInt(Key + "phase", 9);
                    EditorApplication.ExitPlaymode();
                    return;
            }
            SessionState.SetInt(Key + "phase", phase + 1);
        }
        catch (Exception ex) { Fail(ex); }
    }

    static void Check(GameLoop game)
    {
        var player = game.player;
        Require(player != null, "StartGame 没有产生角色");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var animation = (SpriteAnim)typeof(PlayerCtrl).GetField("vis", flags).GetValue(player);
        Require(animation != null && animation.Renderer != null, "角色没有 SpriteAnim/SpriteRenderer");
        Debug.Log("[ExpressionChecks] appearance key=" + animation.key + " base=" + animation.Current);

        // 1) 键位：F1~F7 必须真的绑在 7 个表情动作上
        var expected = new[] { KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7 };
        for (int i = 0; i < Faces.Length; i++)
        {
            var key = KeyMap.Of(Faces[i]);
            Debug.Log("[ExpressionChecks] " + Faces[i] + " -> " + KeyMap.Label(key) + " (" + key + ")");
            Require(key == expected[i], Faces[i] + " 没绑在 " + expected[i] + "（实际 " + key + "）");
        }

        // 2) 静态 pose 下逐个表情：派发 → 换帧 → 像素变化
        animation.Play("stand", false);
        var baseline = Snapshot(animation.Renderer.sprite, "00_default");
        Directory.CreateDirectory(OutDir);
        var report = "appearance=" + animation.key;
        for (int i = 0; i < Faces.Length; i++)
        {
            // 绕过 1.5s 节流（单独在 3) 里验证），以便一口气测完 7 个
            typeof(SpriteAnim).GetField("lastExpression", flags).SetValue(animation, -10f);
            animation.Play("stand", false);
            NativeKeyActions.Execute(Faces[i]);
            var sprite = animation.Renderer.sprite;
            Require(sprite != null, FaceNames[i] + " 派发后没有帧");
            Require(sprite.name.Contains("|" + FaceNames[i] + "/"), FaceNames[i] + " 没有换到原 WZ face/" + FaceNames[i] + "（实际 " + sprite.name + "）");
            var shot = Snapshot(sprite, (i + 1).ToString("D2") + "_" + FaceNames[i]);
            int diff = Diff(baseline, shot);
            report += " " + FaceNames[i] + "=" + diff + "px";
            Debug.Log("[ExpressionChecks] " + FaceNames[i] + " tex=" + shot.width + "x" + shot.height + " diff=" + diff);
            Require(diff > 40, FaceNames[i] + " 合成后画面几乎没变（差异 " + diff + " 像素）");
        }
        SessionState.SetString(Key + "report", report);

        // 3) 节流：原端 0xA244B5 是 GetTickCount 差 < 0x7D0（2000ms）丢弃，
        //    不是服务端注释里的 1.5 秒。1.6s 必被吞，2.1s 必须放行。
        var lastField = typeof(SpriteAnim).GetField("lastExpression", flags);
        lastField.SetValue(animation, Time.unscaledTime - 1.6f);
        var held = animation.Renderer.sprite;
        Require(!animation.Express(2), "1.6s 内第二次表情没有被 2 秒节流挡住");
        Require(animation.Renderer.sprite == held, "被节流挡住时画面仍然变了");
        lastField.SetValue(animation, Time.unscaledTime - 2.1f);
        Require(animation.Express(2), "超过 2 秒后表情仍然被节流挡住");
        Require(animation.Renderer.sprite.name.Contains("|smile/"), "2 秒后放行的表情没有换到 smile");

        // 4) 名字表 / 合法范围：0xBEC284 那 23 项，顺序不能靠 Face.img 的节点罗列顺序猜
        CheckEmoteTable(animation, flags);

        // 5) 改绑到 1/2/3 之后还能不能触发
        CheckRebind(animation, flags);
    }

    static void CheckEmoteTable(SpriteAnim animation, BindingFlags flags)
    {
        string[] expect =
        {
            "blink", "hit", "smile", "troubled", "cry", "angry", "bewildered", "stunned",
            "vomit", "oops", "cheers", "chu", "wink", "pain", "glitter", "blaze",
            "shine", "love", "despair", "hum", "bowing", "hot", "dam",
        };
        Require(SpriteAnim.EmoteNames.Length == expect.Length, "表情名表长度不是 23");
        for (int i = 0; i < expect.Length; i++)
            Require(SpriteAnim.EmoteNames[i] == expect[i],
                "表情名表第 " + i + " 项应为 " + expect[i] + "，实际 " + SpriteAnim.EmoteNames[i]);

        var lastField = typeof(SpriteAnim).GetField("lastExpression", flags);
        var player = GameLoop.I.player;
        lastField.SetValue(animation, Time.unscaledTime - 10f);
        Require(!animation.Express(0), "0 号（blink）不该当按键表情用");
        Require(!animation.Express(23), "23 超出 0x17 上界却仍被接受");
        Require(!animation.Express(-1), "负数表情没有被拒绝");

        // 8..22 是现金表情道具：没有 5159992+emote（5160000..5160014）就不显示。
        animation.Play("stand", false);
        lastField.SetValue(animation, Time.unscaledTime - 10f);
        player.Express(8);
        Require(!animation.Renderer.sprite.name.Contains("|vomit/"), "没有现金表情道具也显示了 vomit");

        var character = GameSession.Character;
        if (character != null && character.inventory != null)
        {
            character.inventory.Add(new InventoryEntry { itemId = 5160000, quantity = 1, inventoryType = 5 });
            try
            {
                animation.Play("stand", false);
                lastField.SetValue(animation, Time.unscaledTime - 10f);
                player.Express(8);
                var sprite = animation.Renderer.sprite;
                Debug.Log("[ExpressionChecks] 持有 5160000 后 8 号表情 sprite=" + sprite.name);
                Require(sprite.name.Contains("|vomit/"), "持有 5160000 后 8 号表情没有换成 vomit（实际 " + sprite.name + "）");
            }
            finally { character.inventory.RemoveAt(character.inventory.Count - 1); }
        }
        else Debug.Log("[ExpressionChecks] 没有 GameSession.Character，跳过现金表情道具门控");
        lastField.SetValue(animation, Time.unscaledTime - 10f);
        animation.ClearExpression();
    }

    // 用户在键盘设定里把表情拖到 1/2/3 却「点了没反应」，这里按同一条路径复现：
    // KeyMap.Apply（键盘设定按确定时走的就是它）→ 按下沿 → InputBridge 那张派发表 → 换帧。
    static void CheckRebind(SpriteAnim animation, BindingFlags flags)
    {
        var saved = new Dictionary<GameAction, KeyCode>();
        foreach (var kv in KeyMap.All) saved[kv.Key] = kv.Value;
        try
        {
            var draft = KeyMap.Defaults();
            draft[GameAction.FaceSmile] = KeyCode.Alpha1;
            draft[GameAction.FaceTroubled] = KeyCode.Alpha2;
            draft[GameAction.FaceCry] = KeyCode.Alpha3;
            KeyMap.Apply(draft);
            Debug.Log("[ExpressionChecks] rebind 1/2/3 -> " +
                KeyMap.Of(GameAction.FaceSmile) + "/" + KeyMap.Of(GameAction.FaceTroubled) + "/" + KeyMap.Of(GameAction.FaceCry));
            Require(KeyMap.Of(GameAction.FaceSmile) == KeyCode.Alpha1, "键盘设定按确定后 1 没有绑到微笑");
            // 1 号键原默认是「对所有人聊天」。原端 keymap 以扫描码为唯一键，
            // 一个键不可能同时挂聊天和表情；这里必须把旧主人清掉，否则按下 1 先开聊天、
            // 聊天框抢走焦点后所有动作都不再派发 —— 表现就是「点了没反应」。
            foreach (var kv in KeyMap.All)
                Require(kv.Value != KeyCode.Alpha1 || kv.Key == GameAction.FaceSmile,
                    "1 键仍同时被 " + kv.Key + " 占用（会先开聊天抢焦点）");
            var rows = KeyMap.ExportCharacter();
            var alpha1 = rows.FindAll(r => r.key == 2);
            Require(alpha1.Count == 1, "导出键位表里扫描码 2 有 " + alpha1.Count + " 行（原端一码一行）");
            Require(alpha1[0].type == 6 && alpha1[0].action == 101,
                "导出扫描码 2 应为 type6/action101，实际 " + alpha1[0].type + "/" + alpha1[0].action);

            typeof(SpriteAnim).GetField("lastExpression", flags).SetValue(animation, -10f);
            animation.Play("stand", false);
            // 屏上虚拟按键和物理键都归到这一条：注入按下沿，再跑 InputBridge 的派发循环
            InputHub.SetVirtual(KeyCode.Alpha1, true);
            bool pressed = InputHub.Pressed(GameAction.FaceSmile);
            if (pressed) NativeKeyActions.Execute(GameAction.FaceSmile);
            InputHub.EndFrame();
            InputHub.SetVirtual(KeyCode.Alpha1, false);
            Require(pressed, "按下 1 没有产生 FaceSmile 的按下沿");
            var sprite = animation.Renderer.sprite;
            Debug.Log("[ExpressionChecks] 1 键派发后 sprite=" + sprite.name);
            Require(sprite.name.Contains("|smile/"), "按下 1 没有换到微笑脸（实际 " + sprite.name + "）");
        }
        finally
        {
            KeyMap.Apply(saved);
            InputHub.ReleaseAllVirtual();
        }
    }

    struct Shot { public Color32[] pixels; public int width, height; }

    static Shot Snapshot(Sprite sprite, string file)
    {
        var texture = sprite.texture;
        var shot = new Shot { pixels = texture.GetPixels32(), width = texture.width, height = texture.height };
        Directory.CreateDirectory(OutDir);
        File.WriteAllBytes(Path.Combine(OutDir, file + ".png"), texture.EncodeToPNG());
        return shot;
    }

    static int Diff(Shot a, Shot b)
    {
        if (a.width != b.width || a.height != b.height)
            return Mathf.Max(a.width * a.height, b.width * b.height);
        int different = 0;
        for (int i = 0; i < a.pixels.Length; i++)
        {
            var x = a.pixels[i]; var y = b.pixels[i];
            if (x.r != y.r || x.g != y.g || x.b != y.b || x.a != y.a) different++;
        }
        return different;
    }

    static void Fail(Exception ex)
    {
        Debug.LogException(ex);
        SessionState.SetBool(Key + "failed", true);
        SessionState.SetInt(Key + "phase", 9);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
