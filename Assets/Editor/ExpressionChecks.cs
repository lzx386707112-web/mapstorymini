using System;
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

        // 3) 节流：连续两次必须只认第一下（原端 Character 1.5s）
        typeof(SpriteAnim).GetField("lastExpression", flags).SetValue(animation, -10f);
        Require(animation.Express(1), "受伤表情首次没有生效");
        var first = animation.Renderer.sprite;
        var second = animation.Express(2) ? animation.Renderer.sprite : null;
        Require(second == null || second == first, "1.5s 内连按两次表情被重复覆盖");
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
