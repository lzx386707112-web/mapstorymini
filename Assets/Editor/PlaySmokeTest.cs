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
    const string K_RUNPLAN = "PlaySmoke.runPlan";
    const string K_CLIMBALL = "PlaySmoke.climbAll";
    const string K_WARP   = "PlaySmoke.warp";
    const string K_PROBE  = "PlaySmoke.uiProbe";
    const string K_CLICKAT = "PlaySmoke.uiClickAt";
    const string K_DIALOG = "PlaySmoke.dialog";

    static int climbIdx, climbOk, climbFail, climbSkip, climbPhase;
    static int climbMapIdx;
    static System.Collections.Generic.List<int> climbMaps;
    static double climbT;
    static float climbStartY, climbTopY, climbBottomY;

    static double nextLog;
    static double nextShot;    static double nextCast;
    static int castIdx;
    static int shotIndex;

    // 怪物速度实测：跟一只怪，按秒采样 |Δx|，换算回 px/s 与 WZ 换算值对比
    static EnemyUnit speedProbe;
    static float speedProbeX;
    static double speedProbeT, speedSum;
    static int speedN;

    // 换图渐变实测：站到门上按 ↑，逐帧采样黑幕 alpha，核对「全黑那一刻才换图」
    static int warpPhase, warpFrames, warpSwitchFrames;
    static int warpFromMap, warpToMap;
    static bool warped;
    static float warpAlphaMax, warpAlphaAtSwitch;
    static float warpX, warpY;
    static double warpT, warpStart;

    [InitializeOnLoadMethod]
    static void Hook()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += st =>
        {
            Debug.Log("[PlaySmoke] 播放状态 → " + st);
            // 有图形界面时域重载可能吃掉好几秒，计时从真正进入 Play 重新起算
            if (st == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(K_RUN, false))
            {
                EditorPrefs.SetString(K_T0,
                    EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
                nextLog = 0;
                nextShot = 2.0;
                nextCast = 2.5;
            }
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
        int lv = 0, dur = 30, playerLevel = 0;
        int width = 960, height = 540;
        bool cast = false;
        string shots = "";
        string cls = "";
        float shotEvery = 4f;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-smokeWidth") int.TryParse(args[i + 1], out width);
            if (args[i] == "-smokeHeight") int.TryParse(args[i + 1], out height);
            if (args[i] == "-smokeLevel") int.TryParse(args[i + 1], out lv);
            // 角色等级（不是关卡序号）：验收等级/经验条时用，临时抬等级且不写存档
            if (args[i] == "-smokePlayerLevel") int.TryParse(args[i + 1], out playerLevel);
            if (args[i] == "-smokeDuration") int.TryParse(args[i + 1], out dur);
            if (args[i] == "-smokeShots") shots = args[i + 1];
            if (args[i] == "-smokeCast") cast = args[i + 1] != "0";
            // 按职业跑：不同职业的四格技能覆盖不同的效果部件（弹道/落矢/地火/流星）
            if (args[i] == "-smokeClass") cls = args[i + 1];
            // 伤害数字只活 0.95s，默认 4s 一张根本抓不到；核对数字时传 0.5 连拍
            if (args[i] == "-smokeShotEvery") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out shotEvery);
            // 原版地图模式：直接进某张地图（-smokeMap 40001）
            if (args[i] == "-smokeMap")
            {
                int map;
                if (int.TryParse(args[i + 1], out map))
                {
                    PlayerPrefs.SetInt("debug_field_map", map);
                    PlayerPrefs.SetInt("debug_field_log", 1);   // batchmode 下看不见画面，只能靠日志核对刷怪/掉落
                }
            }
            // 移动剧本：右走/上爬/左走，用来实机验证台阶与绳梯
            // 格式 "right:6,up:3,none:1"（方向:秒）
            if (args[i] == "-smokeRun") EditorPrefs.SetString(K_RUNPLAN, args[i + 1]);
            // 绳梯全量爬升验证：把角色放到每条绳梯脚下按上，看能不能爬上去
            if (args[i] == "-smokeClimbAll") EditorPrefs.SetBool(K_CLIMBALL, args[i + 1] != "0");
            // 换图渐变验证：站到第一个可见传送门上按 ↑，采样黑幕 alpha 与换图时机
            if (args[i] == "-smokeWarp") EditorPrefs.SetBool(K_WARP, args[i + 1] != "0");
            // UI 悬停探针：把虚拟指针钉在 "x,y"（960x720 逻辑坐标），失焦跑截图时用来验 tooltip/提示
            if (args[i] == "-smokeUiProbe") EditorPrefs.SetString(K_PROBE, args[i + 1]);
            // 进入 Play 多少秒后在探针位置按一下左键（验证加点/页签）
            if (args[i] == "-smokeUiClickAt") EditorPrefs.SetFloat(K_CLICKAT, float.Parse(args[i + 1], CultureInfo.InvariantCulture));
            // Positive NPC id opens original dialogue; negative id opens the quest chooser.
            if (args[i] == "-smokeDialog") EditorPrefs.SetInt(K_DIALOG, int.Parse(args[i + 1]));
            // UI 验收：打开原版键位设置 / 技能窗（截图用，不写存档）
            if (args[i] == "-smokeKeyConfig") PlayerPrefs.SetInt("debug_keyconfig", args[i + 1] != "0" ? 1 : 0);
            if (args[i] == "-smokeSkill") PlayerPrefs.SetInt("debug_skillwin", args[i + 1] != "0" ? 1 : 0);
        }

        if (playerLevel > 0) PlayerPrefs.SetInt("debug_level", playerLevel);

        if (!string.IsNullOrEmpty(cls))
        {
            // debug_cls 只覆盖本次运行的职业，不碰玩家存档（存档快照优先于 mxd.cls）
            PlayerPrefs.SetString("debug_cls", cls);
            EditorPrefs.SetBool("PlaySmoke.clsRestore", true);
        }

        EditorPrefs.SetInt("PlaySmoke.width", Mathf.Max(320, width));
        EditorPrefs.SetInt("PlaySmoke.height", Mathf.Max(240, height));
        Screen.SetResolution(Mathf.Max(320, width), Mathf.Max(240, height), false);
        if (System.Array.IndexOf(args, "-smokeFixedView") >= 0) HudChecks.SetGameViewSize(width, height);
        EditorPrefs.SetBool(K_RUN, true);
        speedProbe = null; speedSum = 0; speedN = 0;
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
        climbIdx = 0;
        climbOk = climbFail = climbSkip = 0;
        climbPhase = 0;
        climbT = 0;
        climbStartY = 0f;
        climbMapIdx = 0;
        climbMaps = null;
        warpPhase = 0;
        warped = false;
        warpToMap = -1;
        warpAlphaMax = 0f;
        warpAlphaAtSwitch = -1f;
        warpFrames = warpSwitchFrames = 0;

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

        // 有图形界面跑截图时编辑器可能失焦，失焦默认会暂停播放，截图就只剩一帧
        Application.runInBackground = true;

        // 失焦时 Pointer 不再采样真实鼠标，正好用探针坐标驱动悬停
        var probeArg = EditorPrefs.GetString(K_PROBE, "");
        if (!string.IsNullOrEmpty(probeArg))
        {
            var parts = probeArg.Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float px) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float py))
                Pointer.TestPos = new Vector2(px, py);
        }

        double t0 = Parse(EditorPrefs.GetString(K_T0, "0"));
        double dur = EditorPrefs.GetInt(K_DUR, 30);
        double t = EditorApplication.timeSinceStartup;

        int dialogNpc = EditorPrefs.GetInt(K_DIALOG, 0);
        if (dialogNpc != 0 && t - t0 >= 1 && GameLoop.I != null && GameLoop.I.field != null && GameLoop.I.field.Map != null)
        {
            EditorPrefs.DeleteKey(K_DIALOG);
            if (dialogNpc > 0) NpcSession.Talk(dialogNpc);
            else NpcField.Interact(-dialogNpc);
        }

        float clickAt = EditorPrefs.GetFloat(K_CLICKAT, -1f);
        if (clickAt >= 0f) Pointer.TestClick = t - t0 >= clickAt;

        DriveInput(t - t0);
        if (TickClimbAll()) return;
        if (TickWarp()) return;

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

                var pl = gl.player;
                string pos = pl == null ? "" : " 玩家=" + pl.worldX.ToString("F2") + "," +
                             (pl.terrainY + pl.jumpY).ToString("F2") + (pl.IsClimbing ? " 爬" : "");

                Debug.Log("[PlaySmoke] t=" + (t - t0).ToString("0.0") + "s" +
                          " 关=" + (st != null && st.CurLevel != null ? st.CurLevel.id : "-") +
                          " 段=" + (st != null ? st.segIndex : -1) +
                          " 怪=" + EnemyUnit.All.Count +
                          " boss=" + boss +
                          " 玩家HP=" + (gl.player != null ? gl.player.hp.ToString("0") : "-") +
                          " 击杀=" + gl.killCount +
                          " 死亡=" + gl.gameOver + pos +
                          " 怪伤=" + HurtCount() +
                          " 通关=" + (st != null && st.allClear) +
                          " 指针=" + Pointer.Pos + (Pointer.TestPos.HasValue ? " 探针" : ""));

                // 怪物速度实测：跟住一只怪，每秒记一次 |Δx|（绕边界回头会拉低平均值）
                var probe = EnemyUnit.All.Count > 0 ? EnemyUnit.All[0] : null;
                if (probe != null)
                {
                    if (probe != speedProbe)
                    {
                        speedProbe = probe; speedProbeX = probe.transform.position.x; speedProbeT = t;
                    }
                    else if (t - speedProbeT >= 1.0)
                    {
                        speedSum += Mathf.Abs(probe.transform.position.x - speedProbeX) * Art.PPU /
                                    (float)(t - speedProbeT);
                        speedN++;
                        speedProbeX = probe.transform.position.x; speedProbeT = t;
                    }
                }
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
            if (speedN > 0)
                Debug.Log("[MobSpeed] 实测 " + (speedSum / speedN).ToString("0.0") + " px/s（" +
                          speedN + " 次采样；WZ 换算值见 [FieldCheck] 蜗牛 43.8px/s）");
            Debug.Log("[PlaySmoke] 时长到，正常退出");
            Finish(0);
        }
    }

    // 绳梯全量爬升验证：把角色依次放到每条绳梯脚下、按住上 1 秒，看脚底有没有真的升高。
    // 返回 true 表示本轮已经跑完并退出。
    static bool TickClimbAll()
    {
        if (!EditorPrefs.GetBool(K_CLIMBALL, false)) return false;
        var gl = GameLoop.I;
        if (gl == null || gl.field == null || gl.field.Map == null || gl.player == null) return false;

        var f = gl.field;
        var pl = gl.player;

        // 懒初始化：按地图 id 排序，只看有绳梯的图
        if (climbMaps == null)
        {
            climbMaps = new System.Collections.Generic.List<int>();
            foreach (var kv in FieldCatalog.Maps)
                if (kv.Value.ropes.Count > 0) climbMaps.Add(kv.Key);
            climbMaps.Sort();
            climbMapIdx = 0;
            climbIdx = 0;
            climbOk = climbFail = climbSkip = 0;
        }

        if (climbMapIdx >= climbMaps.Count)
        {
            int total = climbOk + climbFail + climbSkip;
            Debug.Log("[ClimbCheck] 绳梯 " + total + " 条（" + climbMaps.Count + " 张图）：成功 " +
                      climbOk + "，失败 " + climbFail + "，跳过 " + climbSkip + "（绳底悬空/只能从上方）");
            Profile.LastMapId = 1000000;
            Finish(climbFail == 0 ? 0 : 1);
            return true;
        }

        int wantMap = climbMaps[climbMapIdx];
        if (f.Map == null || f.Map.id != wantMap)
        {
            f.Enter(wantMap, null);
            climbIdx = 0;
            climbT = 0;
            return false;
        }

        var ropes = f.Map.ropes;
        if (climbIdx >= ropes.Count)
        {
            climbMapIdx++;
            climbIdx = 0;
            climbT = 0;
            return false;
        }

        var r = ropes[climbIdx];
        float x = f.Map.WorldX(r.x);
        float bottom = f.Map.WorldY(r.y2);

        if (climbT <= 0)
        {
            pl.PlaceAt(x, PlayerCtrl.GroundAt(x, bottom));
            climbStartY = pl.terrainY + pl.jumpY;
            climbTopY = f.Map.WorldY(r.y1);
            climbBottomY = bottom;
            climbPhase = 0;
            TouchInput.MoveV = 1f;
            climbT = EditorApplication.timeSinceStartup;
            return false;
        }

        if (EditorApplication.timeSinceStartup - climbT < 1.0) return false;

        float endY = pl.terrainY + pl.jumpY;

        if (climbPhase == 0)
        {
            if (endY - climbStartY > 0.3f) climbOk++;          // 站梯脚按上，爬起来了
            else if (climbStartY >= climbTopY - 0.3f)
            {
                // 绳子整体挂在平台下方（绳顶比脚底还低）时按上本来就该往上走，
                // 这条只能从上面按↓爬下去，改测下爬
                climbPhase = 1;
                climbStartY = endY;
                TouchInput.MoveV = -1f;
                climbT = EditorApplication.timeSinceStartup;
                return false;
            }
            else if (climbStartY < climbBottomY - 100f / Art.PPU)
            {
                climbSkip++;                                    // 原数据绳底悬空，下面够不着
            }
            else
            {
                climbFail++;
                Debug.LogError("[ClimbCheck] 失败：地图 " + f.Map.id + " 绳梯 x=" + r.x +
                               " y1=" + r.y1 + " y2=" + r.y2 + " 起始 y=" + climbStartY.ToString("F2") +
                               " 结束 y=" + endY.ToString("F2") + " 攀爬中=" + pl.IsClimbing);
            }
        }
        else
        {
            if (climbStartY - endY > 0.3f) climbOk++;           // 站在绳顶按↓，爬下去了
            else
            {
                climbFail++;
                Debug.LogError("[ClimbCheck] 失败（下爬）：地图 " + f.Map.id + " 绳梯 x=" + r.x +
                               " 起始 y=" + climbStartY.ToString("F2") + " 结束 y=" + endY.ToString("F2") +
                               " 攀爬中=" + pl.IsClimbing);
            }
        }

        TouchInput.MoveV = 0f;
        climbIdx++;
        climbT = 0;
        climbPhase = 0;
        return false;
    }

    // 换图渐变实测（-smokeWarp 1）：
    //   0: 先把角色挪到门旁 3u，让 portalArmed 复位（否则出生就站在门上时按上不响应）
    //   1: 0.3s 后站到门上，按 ↑
    //   2: 逐帧采样 ScreenFade.Alpha，核对三件事：
    //      ① alpha 真的到过 1（画面全黑）② 换图发生在 alpha≈1 的那一帧
    //      ③ 渐显结束后输入锁解除（否则角色再也动不了）
    static bool TickWarp()
    {
        if (!EditorPrefs.GetBool(K_WARP, false)) return false;
        var gl = GameLoop.I;
        if (gl == null || gl.field == null || gl.field.Map == null || gl.player == null) return false;

        var f = gl.field;
        var pl = gl.player;
        double now = EditorApplication.timeSinceStartup;

        if (warpPhase == 0)
        {
            var p = PickWarpPortal(f.Map);
            if (p == null)
            {
                Debug.LogError("[WarpCheck] 地图 " + f.Map.id + " 没有可用的 pt=2 传送门");
                Finish(1);
                return true;
            }
            warpFromMap = f.Map.id;
            warpX = f.Map.WorldX(p.x);
            warpY = f.Map.WorldY(p.y);
            float side = warpX + 3f;
            pl.PlaceAt(side, PlayerCtrl.GroundAt(side, warpY));
            warpT = now;
            warpPhase = 1;
            return false;
        }

        if (warpPhase == 1)
        {
            if (now - warpT < 0.35) return false;
            pl.PlaceAt(warpX, PlayerCtrl.GroundAt(warpX, warpY));
            TouchInput.MoveV = 1f;          // CheckPortals 读的就是这个
            warpStart = now;
            warpPhase = 2;
            return false;
        }

        // 阶段 2：逐帧采样黑幕 alpha，并记录「换图那一刻的 alpha」
        if (warpPhase == 2)
        {
            float a = ScreenFade.Alpha;
            if (a > 0.001f) warpFrames++;
            if (a > warpAlphaMax) warpAlphaMax = a;

            if (!warped && f.Map != null && f.Map.id != warpFromMap)
            {
                warped = true;
                warpToMap = f.Map.id;
                warpAlphaAtSwitch = a;
                warpSwitchFrames = warpFrames;
            }

            if (warped && a <= 0.0005f)
            {
                warpT = now;
                warpPhase = 3;
                return false;
            }

            if (now - warpStart > 8.0)
            {
                TouchInput.MoveV = 0f;
                string fail = "[WarpCheck] 超时：地图 " + (f.Map != null ? f.Map.id : -1) +
                              " alpha=" + a.ToString("F2") + " 已换图=" + warped +
                              "（门 " + warpFromMap + " 未触发或渐变未结束）";
                System.IO.File.WriteAllText("/tmp/warp_result.txt", fail + "\n");
                Debug.LogError(fail);
                Finish(1);
                return true;
            }
            return false;
        }

        // 阶段 3：alpha 归零后再等 0.2s，确认输入锁真的解开了（不是差一帧）
        if (now - warpT < 0.2) return false;

        TouchInput.MoveV = 0f;
        double cost = now - warpStart;
        bool ok = warpAlphaMax > 0.99f && warpAlphaAtSwitch > 0.99f && !PlayerCtrl.InputLocked;
        string msg = "[WarpCheck] " + warpFromMap + " → " + warpToMap +
                     "：渐隐峰值 " + warpAlphaMax.ToString("F2") +
                     "，换图时 alpha " + warpAlphaAtSwitch.ToString("F2") +
                     "，黑场共 " + warpFrames + " 帧（换图在第 " + warpSwitchFrames + " 帧）" +
                     "，总耗时 " + cost.ToString("F2") + "s" +
                     "，输入锁已解除=" + (!PlayerCtrl.InputLocked) +
                     "（理论 " + ScreenFade.TotalSeconds.ToString("F2") + "s）";
        // batchmode 退出时普通日志缓冲会丢，除了打日志再落一份文件（测试工具专用）
        System.IO.File.WriteAllText("/tmp/warp_result.txt", msg + "\n");
        if (ok) Debug.LogWarning(msg); else Debug.LogError(msg + " ← 不符合渐变预期");
        Finish(ok ? 0 : 1);
        return true;
    }

    // 挑第一个「可见(pt=2)、且真的换图」的门
    static FieldPortal PickWarpPortal(FieldMap map)
    {
        foreach (var p in map.portals)
            if (p.type == 2 && p.Warps && p.tm != map.id) return p;
        return null;
    }

    // 按剧本驱动触屏输入（batchmode 下没有键盘，只能写 TouchInput）
    // plan 形如 "right:6,up:3,none:1"，单位秒，超出时长后归零。
    static void DriveInput(double elapsed)
    {
        string plan = EditorPrefs.GetString(K_RUNPLAN, "");
        if (string.IsNullOrEmpty(plan)) return;

        float acc = 0f;
        string dir = "none";
        foreach (var seg in plan.Split(','))
        {
            var parts = seg.Split(':');
            if (parts.Length != 2) continue;
            float len;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out len) || len < 0f) continue;
            if (elapsed < acc + len) { dir = parts[0].Trim(); break; }
            acc += len;
        }

        TouchInput.MoveH = dir == "right" ? 1f : dir == "left" ? -1f : 0f;
        TouchInput.MoveV = dir == "up" ? 1f : dir == "down" ? -1f : 0f;
    }

    // 用 RenderTexture 抓帧：batchmode 下 ScreenCapture 常常出黑图，这条稳
    static void Shot(string dir, string name)
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[PlaySmoke] 无相机，跳过截图"); return; }
        int W = EditorPrefs.GetInt("PlaySmoke.width", 960);
        int H = EditorPrefs.GetInt("PlaySmoke.height", 540);
        Rect viewport = cam.rect;
        float aspect = cam.aspect;
        RenderTexture rt = null, prev = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            // 离屏纹理使用本次检查指定的屏幕比例。
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.aspect = (float)W / H;
            // batchmode 的 Screen 可能仍是编辑器比例，背景平铺需按目标纹理重新铺满。
            foreach (var view in Object.FindObjectsOfType<NativeMapView>())
                view.SendMessage("LateUpdate");
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

    // 掉过血的怪数量：用来验证「站下层打不到上层的怪」（不该有任何上层怪掉血）
    static int HurtCount()
    {
        int n = 0;
        foreach (var e in EnemyUnit.All) if (e != null && !e.dead && e.hp < e.hpMax) n++;
        return n;
    }

    static void Finish(int code)
    {
        if (EditorPrefs.GetBool("PlaySmoke.clsRestore", false))
        {
            PlayerPrefs.DeleteKey("debug_cls");
            PlayerPrefs.Save();
            EditorPrefs.DeleteKey("PlaySmoke.clsRestore");
        }
        EditorPrefs.DeleteKey(K_RUN);
        EditorPrefs.DeleteKey(K_RUNPLAN);
        EditorPrefs.DeleteKey(K_CLIMBALL);
        EditorPrefs.DeleteKey(K_WARP);
        EditorPrefs.DeleteKey(K_PROBE);
        EditorPrefs.DeleteKey(K_CLICKAT);
        EditorPrefs.DeleteKey(K_DIALOG);
        Pointer.TestPos = null;
        Pointer.TestClick = false;
        EditorApplication.update -= Tick;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(code);
    }
}
