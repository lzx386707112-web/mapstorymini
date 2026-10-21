using System.Globalization;
using UnityEditor;
using UnityEngine;

// 自动 Play 冒烟测试：进入播放模式 → 直奔 Boss 关 → 跑 30 秒 → 退出。
// 目的只有一个：抓运行时异常（空引用、协程错误、Boss 状态机崩），
// 这类问题编译期发现不了，而用户第一次按 Play 就白屏是最糟的体验。
//
// 命令行：Unity -batchmode -projectPath <proj> -executeMethod PlaySmokeTest.Run -logFile /tmp/play_smoke.log
// 注意：不能加 -quit，否则还没进 Play 就退了。
//
// 域重载会把 static 全清掉，所以状态一律存 EditorPrefs。
public static class PlaySmokeTest
{
    const string K_RUN = "PlaySmoke.running";
    const string K_T0  = "PlaySmoke.t0";
    const string K_LV  = "PlaySmoke.level";
    const string K_DUR = "PlaySmoke.dur";
    const string K_SHOTS = "PlaySmoke.shots";
    const string K_CAST  = "PlaySmoke.cast";
    const string K_EVERY = "PlaySmoke.shotEvery";

    static double nextLog;
    static double nextShot;
    static double nextCast;
    static int castIdx;
    static int shotIndex;

    [InitializeOnLoadMethod]
    static void Hook()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += st =>
        {
            Debug.Log("[PlaySmoke] 播放状态 → " + st);
        };
    }

    // 素材自检：不开 Play，直接确认伤害数字的 manifest、贴图、Sprite 三层都通。
    // （batchmode 下截图抓不到 0.95s 的数字时，先用它判断是「没打中」还是「素材没加载」）
    public static void SelfCheck()
    {
        Debug.Log("[Check] Art.Ready=" + Art.Ready);
        foreach (var key in new[] { "red", "cri", "blue", "violet" })
        {
            var s = Art.Damage(key);
            if (s == null) { Debug.LogError("[Check] 皮肤缺失 " + key); continue; }
            var g = s.baseDigit[3];
            var sp = Art.FrameAt(g.file, g.origin);
            Debug.Log("[Check] " + key + " digit3=" + g.file + " origin=" + g.origin +
                      " size=" + g.size + " sprite=" +
                      (sp == null ? "NULL" : sp.name + " rect=" + sp.rect.size + " ppu=" + sp.pixelsPerUnit));
            if (s.miss != null)
            {
                var m = Art.FrameAt(s.miss.file, s.miss.origin);
                Debug.Log("[Check] " + key + ".miss sprite=" + (m == null ? "NULL" : m.name));
            }
            if (s.fx != null)
            {
                var f = Art.FrameAt(s.fx.file, s.fx.origin);
                Debug.Log("[Check] " + key + ".fx sprite=" + (f == null ? "NULL" : f.name));
            }
        }
    }

    public static void Run()
    {
        int lv = 0, dur = 30;
        bool cast = false;
        string shots = "";
        float shotEvery = 4f;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-smokeLevel") int.TryParse(args[i + 1], out lv);
            if (args[i] == "-smokeDuration") int.TryParse(args[i + 1], out dur);
            if (args[i] == "-smokeShots") shots = args[i + 1];
            if (args[i] == "-smokeCast") cast = args[i + 1] != "0";
            // 伤害数字只活 0.95s，默认 4s 一张根本抓不到；核对数字时传 0.5 连拍
            if (args[i] == "-smokeShotEvery") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out shotEvery);
        }

        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
        EditorPrefs.SetInt(K_LV, lv);
        EditorPrefs.SetInt(K_DUR, dur);
        EditorPrefs.SetString(K_SHOTS, shots);
        EditorPrefs.SetBool(K_CAST, cast);
        EditorPrefs.SetFloat(K_EVERY, shotEvery);
        nextShot = 2.0;
        nextCast = 2.5;
        castIdx = 0;
        shotIndex = 0;

        PlayerPrefs.SetInt("debug_start_level", lv);
        PlayerPrefs.SetInt("debug_god", 1);          // 测试期间玩家不死，否则到不了 Boss 段
        PlayerPrefs.SetInt("autostart", 1);          // 自动化测试直接出战（否则会停在大厅）
        PlayerPrefs.SetInt("hide_tuning", string.IsNullOrEmpty(shots) ? 0 : 1);   // 截图时别让调参面板入镜
        PlayerPrefs.Save();

        Debug.Log("[PlaySmoke] 准备进入 Play，起始关索引 " + lv + "，时长 " + dur + "s");
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        if (!EditorApplication.isPlaying)
        {
            // 进 Play 失败（编译错误等）→ 10 秒后退出，避免永远挂着
            if (EditorApplication.timeSinceStartup - Parse(EditorPrefs.GetString(K_T0, "0")) > 40)
            {
                Debug.LogError("[PlaySmoke] 未进入播放模式，退出");
                Finish(1);
            }
            return;
        }

        double t0 = Parse(EditorPrefs.GetString(K_T0, "0"));
        double dur = EditorPrefs.GetInt(K_DUR, 30);
        double t = EditorApplication.timeSinceStartup;

        // 截图走的是 960x540 的 RenderTexture，但 batchmode 下 Screen 的比例可能不是 16:9，
        // CameraRig 用 cam.aspect 夹边界，两者不一致就会在画面右边露出切片外的天空。
        // 这里把相机比例钉死成 16:9，让夹边界和渲染分辨率对齐。
        if (!string.IsNullOrEmpty(EditorPrefs.GetString(K_SHOTS, "")))
        {
            var c = Camera.main;
            if (c != null && !Mathf.Approximately(c.aspect, 16f / 9f)) c.aspect = 16f / 9f;
        }

        if (t - nextLog >= 1.0)
        {            nextLog = t;
            var gl = GameLoop.I;
            if (gl == null)
            {
                Debug.Log("[PlaySmoke] t=" + (t - t0).ToString("0.0") + "s  GameLoop 尚未创建");
            }
            else
            {
                var st = gl.stage;
                string boss = "-";
                if (st != null && st.boss != null)
                    boss = (st.boss.def != null ? st.boss.def.name : "?") + " " +
                           st.boss.hp.ToString("0") + "/" + st.boss.hpMax.ToString("0");

                Debug.Log("[PlaySmoke] t=" + (t - t0).ToString("0.0") + "s" +
                          " 关=" + (st != null && st.CurLevel != null ? st.CurLevel.id : "-") +
                          " 段=" + (st != null ? st.segIndex : -1) +
                          " 怪=" + EnemyUnit.All.Count +
                          " boss=" + boss +
                          " 玩家HP=" + (gl.player != null ? gl.player.hp.ToString("0") : "-") +
                          " 击杀=" + gl.killCount +
                          " 死亡=" + gl.gameOver +
                          " 通关=" + (st != null && st.allClear));
            }
        }

        // 自动放技能：截图时才能看到 WZ 特效帧到底对不对
        if (EditorPrefs.GetBool(K_CAST, false) && t - nextCast >= 2.2)
        {
            nextCast = t;
            var gl2 = GameLoop.I;
            if (gl2 != null && gl2.player != null)
            {
                var sys = gl2.player.GetComponent<SkillSystem>();
                if (sys != null && sys.TryCast(castIdx % 4))
                    Debug.Log("[PlaySmoke] 施放技能 " + castIdx % 4 + " (" + sys.skills[castIdx % 4].name + ")");
                castIdx = (castIdx + 1) % 4;
            }
        }

        // 截图：按 -smokeShots 指定的目录存 960x540 的 PNG，用来肉眼核对素材
        string dir = EditorPrefs.GetString(K_SHOTS, "");
        if (!string.IsNullOrEmpty(dir) && t - t0 >= nextShot)
        {
            nextShot += Mathf.Max(0.25f, EditorPrefs.GetFloat(K_EVERY, 4f));
            int idx = shotIndex++;
            Shot(dir, "shot" + idx.ToString("00") + ".png");

            // 额外抓一张「含 UI」的全屏图：RenderTexture 那条路径不含 IMGUI，
            // 技能栏图标只有走 ScreenCapture 才拍得到。
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "ui" + idx.ToString("00") + ".png"), 1);
            }
            catch (System.Exception e) { Debug.LogWarning("[PlaySmoke] UI 截图失败：" + e.Message); }
        }

        if (t - t0 >= dur)
        {
            Debug.Log("[PlaySmoke] 时长到，正常退出");
            Finish(0);
        }
    }

    // 用 RenderTexture 抓帧：batchmode 下 ScreenCapture 常常出黑图，这条稳
    static void Shot(string dir, string name)
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[PlaySmoke] 无相机，跳过截图"); return; }
        const int W = 960, H = 540;
        Rect viewport = cam.rect;
        float aspect = cam.aspect;
        RenderTexture rt = null, prev = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            // 离屏纹理本身就是 16:9，不应再次套用手机屏幕的留边。
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.aspect = (float)W / H;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();

            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name), tex.EncodeToPNG());
            Debug.Log("[PlaySmoke] 截图 " + name);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[PlaySmoke] 截图失败：" + e.Message);
        }
        finally
        {
            if (cam != null)
            {
                cam.targetTexture = null;
                cam.rect = viewport;
                cam.aspect = aspect;
            }
            RenderTexture.active = prev;
            if (rt != null) Object.Destroy(rt);
            if (tex != null) Object.Destroy(tex);
        }
    }

    static double Parse(string s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    static void Finish(int code)
    {
        EditorPrefs.DeleteKey(K_RUN);
        EditorApplication.update -= Tick;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(code);
    }
}
