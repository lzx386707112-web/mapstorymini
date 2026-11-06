using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

// Play Mode 特效验证：真的进游戏、按技能 id 放技能、在固定时间点截图，
// 并把「这一刻场上还活着哪些特效层（clip.path）」一并记下来。
//
// 和 SkillVfxLayerChecks 的分工：
//   SkillVfxLayerChecks —— 静态问「取层函数会取哪些层」（不看画面）
//   本文件             —— 动态问「按下去之后画面上到底出现了什么」（截图 + 活体层清单）
//
// 命令行（不要加 -quit，否则还没进 Play 就退了）：
//   Unity -batchmode -projectPath <proj> -executeMethod SkillVfxPlayChecks.Run \
//         -vfxPlayIds 400011003,400011072 -vfxPlayShots /tmp/vfxplay -logFile /tmp/vfxplay.log
//
// 域重载会清 static，跨阶段状态一律走 EditorPrefs。
public static class SkillVfxPlayChecks
{
    const string K_RUN     = "VfxPlay.running";
    const string K_IDS     = "VfxPlay.ids";
    const string K_DIR     = "VfxPlay.dir";
    const string K_LEVEL   = "VfxPlay.level";
    const string K_GAP     = "VfxPlay.gap";
    const string K_OFFSETS = "VfxPlay.offsets";
    const string K_T0      = "VfxPlay.t0";
    const string K_CLS     = "VfxPlay.clsRestore";
    const string K_NOSHOT  = "VfxPlay.noShots";

    static readonly int[] DefaultIds = { 400011003, 400011052, 400011072, 400011131 };
    // 施法特效的总时长多在 0.4~2.4s，这组偏移覆盖「起手 / 主体 / 尾段」。
    static readonly float[] DefaultOffsets = { 0.08f, 0.20f, 0.40f, 0.70f, 1.10f, 1.60f };

    static int[] ids;
    static float[] offsets;
    static int cursor;          // 当前放到第几个技能
    static int offsetCursor;    // 当前技能截到第几张
    static double nextShot;
    static bool levelsReady;
    static bool noShots;
    static int peakLayers;
    static readonly List<string> report = new List<string>();

    [InitializeOnLoadMethod]
    static void Hook()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += st =>
        {
            Debug.Log("[VfxPlay] 播放状态 → " + st);
            if (st == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(K_RUN, false))
                EditorPrefs.SetString(K_T0, Now());
        };
    }

    public static void Run()
    {
        string idArg = "", dir = "/tmp/vfxplay";
        int level = 30;
        float gap = 2.6f;
        string offsetArg = "";
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-vfxPlayIds") idArg = args[i + 1];
            if (args[i] == "-vfxPlayShots") dir = args[i + 1];
            if (args[i] == "-vfxPlayLevel") int.TryParse(args[i + 1], out level);
            if (args[i] == "-vfxPlayGap") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out gap);
            if (args[i] == "-vfxPlayOffsets") offsetArg = args[i + 1];
            // 批量扫描用：只统计「施法后有没有层在跑」，不落 PNG（157 技能会拍出 300+ 张）
            if (args[i] == "-vfxPlayNoShots") noShots = args[i + 1] != "0";
        }

        ids = ParseIds(idArg, DefaultIds);
        offsets = ParseFloats(offsetArg, DefaultOffsets);

        System.IO.Directory.CreateDirectory(dir);
        EditorPrefs.SetString(K_IDS, Join(ids));
        EditorPrefs.SetString(K_OFFSETS, Join(offsets));
        EditorPrefs.SetString(K_DIR, dir);
        EditorPrefs.SetInt(K_LEVEL, Mathf.Clamp(level, 1, 30));
        EditorPrefs.SetFloat(K_GAP, Mathf.Max(1f, gap));
        EditorPrefs.SetBool(K_NOSHOT, noShots);
        EditorPrefs.SetString(K_CLS, "");

        PlayerPrefs.SetInt("autostart", 1);      // 直接出战，不停在大厅
        PlayerPrefs.SetInt("debug_god", 1);      // 测试期间不死
        PlayerPrefs.SetInt("hide_tuning", 1);    // 调参面板别入镜
        PlayerPrefs.SetInt("debug_start_level", 0);
        PlayerPrefs.Save();

        cursor = 0; offsetCursor = 0; levelsReady = false;
        report.Clear();
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, Now());
        Debug.Log("[VfxPlay] 准备进入 Play，技能 " + string.Join(",", Array.ConvertAll(ids, x => x.ToString())));
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        // 域重载会把 static 全清掉（进 Play 时必发生一次），计划必须从 EditorPrefs 读回，
        // 否则 InstallMatrixLevels 里 foreach(ids) 直接 NullReference，每帧重试、永不结束。
        if (ids == null || offsets == null)
        {
            ids = ParseIds(EditorPrefs.GetString(K_IDS, ""), DefaultIds);
            offsets = ParseFloats(EditorPrefs.GetString(K_OFFSETS, ""), DefaultOffsets);
            noShots = EditorPrefs.GetBool(K_NOSHOT, false);
        }
        if (!EditorApplication.isPlaying)
        {
            if (Elapsed() > 45)
            {
                Debug.LogError("[VfxPlay] 未进入播放模式，退出");
                Finish(1);
            }
            return;
        }
        Application.runInBackground = true;

        // 硬看门狗：任何一个技能卡住（放不出来、场景没就绪）都不能让进程永远挂着。
        float budget = 60f + ids.Length * (offsets[offsets.Length - 1] + EditorPrefs.GetFloat(K_GAP, 2.6f) + 8f);
        if (Elapsed() > budget)
        {
            Debug.LogError("[VfxPlay] 超时 " + Elapsed().ToString("0.0") + "s / 预算 " + budget.ToString("0.0") + "s，强制收尾");
            Finish(1);
            return;
        }

        var gl = GameLoop.I;
        if (gl == null || gl.player == null || gl.field == null || gl.field.Map == null) return;
        if (Elapsed() < 1.0) return;

        var sys = gl.player.GetComponent<SkillSystem>();
        if (sys == null) { Debug.LogError("[VfxPlay] 玩家身上没有 SkillSystem"); Finish(1); return; }

        // 五转技能等级走核心快照；第一次就绪时把四个核心投影成 Lv.N。
        if (!levelsReady)
        {
            InstallMatrixLevels(EditorPrefs.GetInt(K_LEVEL, 30));
            levelsReady = true;
            Stage(gl);
            nextShot = EditorApplication.timeSinceStartup + 1.0;
            report.Add("职业=" + Profile.cls + "  地图=" + gl.field.Map.id + "  等级=" + EditorPrefs.GetInt(K_LEVEL, 30));
            return;
        }

        if (cursor >= ids.Length)
        {
            Finish(0);
            return;
        }

        int id = ids[cursor];
        if (offsetCursor == 0)
        {
            // 开新技能：补满 MP、把上一轮的残留层记一笔，然后施放
            // 五转技能 mpCon 动辄 1000~4000，新手 mpMax 只有几百 ⇒ 只补 mp 不够，
            // 必须把上限一起抬上去，否则 BeginCast 直接以「魔力不足」拒绝，截图全空。
            gl.player.mpMax = 999999f;
            gl.player.mp = 999999f;
            int live = LiveFxCount();
            peakLayers = 0;
            bool ok = false;
            string fail = null;
            try { ok = sys.TryCastSkill(id); fail = sys.LastFailure; }
            catch (Exception e) { fail = e.GetType().Name + " " + e.Message; }

            var data = SkillBook.Get(id);
            currentName = data != null ? data.name : "?";
            currentCastOk = ok;
            currentFail = string.IsNullOrEmpty(fail) ? "" : fail;
            if (!noShots)
            {
                report.Add("");
                report.Add("=== " + id + "「" + currentName + "」 type=" + (data != null ? data.type : "?") +
                           "  Lv=" + SkillProgress.LevelOf(id) +
                           "  施放=" + (ok ? "成功" : "失败") + (string.IsNullOrEmpty(fail) ? "" : "（" + fail + "）") +
                           "  残留层=" + live);
            }
            if (!ok)
            {
                // 放不出来就不用等了，直接跳到下一个
                if (noShots) report.Add(SweepLine(id));
                cursor++;
                offsetCursor = 0;
                return;
            }
            nextShot = EditorApplication.timeSinceStartup + offsets[0];
            offsetCursor = 1;
            return;
        }

        if (EditorApplication.timeSinceStartup < nextShot) return;

        int nowLive = LiveFxCount();
        if (nowLive > peakLayers) peakLayers = nowLive;
        if (!noShots)
        {
            string name = id + "_" + Mathf.RoundToInt(offsets[offsetCursor - 1] * 1000f).ToString("D4") + "ms.png";
            string alive = LiveFxSummary();
            Shot(EditorPrefs.GetString(K_DIR, "/tmp/vfxplay"), name);
            report.Add("   +" + offsets[offsetCursor - 1].ToString("0.00") + "s  " + name + "  活体层：" + alive);
        }

        if (offsetCursor >= offsets.Length)
        {
            if (noShots) report.Add(SweepLine(id));
            cursor++;
            offsetCursor = 0;
            // 换技能前把上一个技能的残留层清掉、并把角色挪到相机中心：
            // 神圣团结的 repeat/special 波次能拖到 8 秒，叠加会让下一张截图分不清是谁的层。
            Stage(gl);
            nextShot = EditorApplication.timeSinceStartup + EditorPrefs.GetFloat(K_GAP, 2.6f);
        }
        else
        {
            nextShot += offsets[offsetCursor] - offsets[offsetCursor - 1];
            offsetCursor++;
        }
    }

    static string currentName = "";
    static bool currentCastOk;
    static string currentFail = "";

    // 扫描模式每个技能一行：只关心「放得出来吗 / 有没有层在跑」
    static string SweepLine(int id)
    {
        string verdict = !currentCastOk ? "✗ 施放失败（" + currentFail + "）"
            : peakLayers == 0 ? "✗ 放出来了但一层特效都没有"
            : "✓ " + peakLayers + " 层";
        return "  " + id + "  " + currentName + "  " + verdict;
    }

    // 五转核心等级来源是 VMatrixLevels（服务端快照投影），不是 SP 存档。
    // 这里构造一份等价快照，让 SkillProgress.LevelOf 能拿到等级，
    // 否则 SkillSystem 会以「技能尚未学习」直接拒绝，截图就全是空的。
    static void InstallMatrixLevels(int level)
    {
        VMatrixCatalog.Ensure();
        var response = new VMatrixResponse();
        foreach (int id in ids)
        {
            var core = CoreOf(id);
            var view = new VMatrixCoreView
            {
                coreId = core != null ? core.id : 0,
                level = level,
                maxLevel = level,
                skills = core != null && core.skills != null && core.skills.Length > 0 ? core.skills : new[] { id },
                name = core != null ? core.name : "",
                desc = core != null ? core.desc : "",
            };
            response.cores.Add(view);
        }
        VMatrixLevels.Capture(response, 990001);
        foreach (int id in ids)
            Debug.Log("[VfxPlay] 注入等级 " + id + " → Lv." + SkillProgress.LevelOf(id));
    }

    // 从目录里反查「哪个核心连着这个技能」
    static VMatrixCore CoreOf(int skillId)
    {
        foreach (var core in VMatrixCatalog.AllCores)
            if (core.skills != null && Array.IndexOf(core.skills, skillId) >= 0) return core;
        return null;
    }

    // 换技能前的场地准备：
    //   ① 清掉上一个技能还在跑的 SkillFx（长技能能拖 8 秒，不清就叠在一起）
    //   ② 把角色挪到相机中心。贴着地图右边界时相机被 VR 夹住，特效会被屏幕边缘截断，
    //      截出来的图和导出基准图根本没法叠在一起看。
    static void Stage(GameLoop gl)
    {
        foreach (var fx in UnityEngine.Object.FindObjectsOfType<SkillFx>())
            if (fx != null) fx.Stop();
        ClearActionLock(gl);
        var map = gl.field != null ? gl.field.Map : null;
        var pl = gl.player;
        if (map == null || pl == null) return;
        float x = (map.ViewMinX + map.ViewMaxX) * 0.5f;
        float y = map.WorldY((map.vr[1] + map.vr[3]) * 0.5f);
        pl.PlaceAt(x, PlayerCtrl.GroundAt(x, y));
    }

    // 批量扫描必须清掉上一个技能的动作锁：某些技能的 SkillActionDuration 长达数十秒
    // （如 time=30 的 buff），不清的话后面每个技能都只回「正在释放技能」，
    // 整份报告会变成一片假失败。ClearActionLock 是 private，这里按字段名清。
    static void ClearActionLock(GameLoop gl)
    {
        var sys = gl.player != null ? gl.player.GetComponent<SkillSystem>() : null;
        if (sys == null) return;
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(SkillSystem);
        type.GetField("actionUntil", F)?.SetValue(sys, 0f);
        type.GetField("actionBlocking", F)?.SetValue(sys, false);
        type.GetField("actionSkillId", F)?.SetValue(sys, 0);
    }

    static int LiveFxCount()
    {
        int n = 0;
        foreach (var fx in UnityEngine.Object.FindObjectsOfType<SkillFx>())
            if (fx != null && fx.gameObject.activeInHierarchy) n++;
        return n;
    }

    // 活体层清单：这一刻画面上真的在渲染哪些 clip.path。
    // 和 SkillVfxLayerChecks 的「会取哪些层」两相对照，就能判断是「没取到」还是「取到了没演」。
    static string LiveFxSummary()
    {
        var parts = new List<string>();
        foreach (var fx in UnityEngine.Object.FindObjectsOfType<SkillFx>())
        {
            if (fx == null || !fx.gameObject.activeInHierarchy) continue;
            var sr = fx.GetComponent<SpriteRenderer>();
            parts.Add(fx.Part + (sr != null ? "@" + sr.sortingOrder : "") +
                      (fx.follow != null ? "·跟" : ""));
        }
        parts.Sort(StringComparer.Ordinal);
        return parts.Count == 0 ? "（无）" : parts.Count + " 层 " + string.Join(" ", parts.ToArray());
    }

    static void Shot(string dir, string name)
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[VfxPlay] 无相机，跳过截图"); return; }
        const int W = 960, H = 540;
        Rect viewport = cam.rect;
        float aspect = cam.aspect;
        RenderTexture rt = null, prev = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.aspect = (float)W / H;
            // 背景平铺按目标纹理重铺，否则 batchmode 下会是编辑器比例
            foreach (var view in UnityEngine.Object.FindObjectsOfType<NativeMapView>())
                view.SendMessage("LateUpdate");
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name), tex.EncodeToPNG());
        }
        catch (Exception e) { Debug.LogWarning("[VfxPlay] 截图失败 " + name + "：" + e.Message); }
        finally
        {
            if (cam != null) { cam.targetTexture = null; cam.rect = viewport; cam.aspect = aspect; }
            RenderTexture.active = prev;
            if (rt != null) UnityEngine.Object.Destroy(rt);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    static int[] ParseIds(string text, int[] fallback)
    {
        if (string.IsNullOrEmpty(text)) return fallback;
        var list = new List<int>();
        foreach (string part in text.Split(','))
            if (int.TryParse(part.Trim(), out int id) && id > 0) list.Add(id);
        return list.Count == 0 ? fallback : list.ToArray();
    }

    static float[] ParseFloats(string text, float[] fallback)
    {
        if (string.IsNullOrEmpty(text)) return fallback;
        var list = new List<float>();
        foreach (string part in text.Split(','))
            if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 0f) list.Add(v);
        if (list.Count == 0) return fallback;
        list.Sort();
        return list.ToArray();
    }

    static string Join(int[] values) => string.Join(",", Array.ConvertAll(values, x => x.ToString()));
    static string Join(float[] values) => string.Join(",", Array.ConvertAll(values, x => x.ToString("R", CultureInfo.InvariantCulture)));
    static string Now() => EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);
    static double Elapsed() =>
        EditorApplication.timeSinceStartup -
        (double.TryParse(EditorPrefs.GetString(K_T0, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0);

    static void Finish(int code)
    {
        string text = string.Join("\n", report.ToArray());
        try { System.IO.File.WriteAllText("/tmp/skill_vfx_play.txt", text); } catch { }
        Debug.Log("[VfxPlay]\n" + text);
        EditorPrefs.DeleteKey(K_RUN);
        EditorPrefs.DeleteKey(K_IDS);
        EditorPrefs.DeleteKey(K_OFFSETS);
        EditorPrefs.DeleteKey(K_DIR);
        EditorApplication.update -= Tick;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(code);
    }
}
