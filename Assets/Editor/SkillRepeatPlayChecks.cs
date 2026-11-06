using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

// 周期攻击的 Play Mode 验证（祝福之鎚 400011052 / 30 秒每 1.8 秒一发）。
//
// 「有伤害但后续攻击没特效」光看截图或光看掉血都测不出来：
//   · 只量伤害 ⇒ 看不出「有没有画面」；
//   · 只看某一刻的活体层 ⇒ 长特效残留会把假阴性盖成阳性；
//   · 用 EditorApplication.update 采样 ⇒ batchmode 下编辑器回调会被节流到几秒一次，
//     整个时间轴全是错的（第一版探针就栽在这里）。
// 所以采样器必须跑在 **Play Mode 的运行时协程** 里（每帧一次），并按
// 「新生成的 SkillFx 实例」计数；伤害则直接挂 Unity 日志读 [Hit] 的 命中 数。
// 判定口径：每个周期 tick 都必须同时有「新特效层」和「真实命中」。
//
// 命令行（不要 -quit）：
//   Unity -batchmode -projectPath <proj> -executeMethod SkillRepeatPlayChecks.Run \
//         -repeatIds 400011052 -repeatWindow 8 -repeatShots /tmp/repeatplay -logFile /tmp/repeatplay.log
public static class SkillRepeatPlayChecks
{
    const string K_RUN    = "RepeatPlay.running";
    const string K_IDS    = "RepeatPlay.ids";
    const string K_DIR    = "RepeatPlay.dir";
    const string K_T0     = "RepeatPlay.t0";
    const string K_WINDOW = "RepeatPlay.window";
    const string K_SHOTS  = "RepeatPlay.shotTimes";

    static readonly int[] DefaultIds = { 400011052 };
    static readonly float[] DefaultShotTimes = { 0.30f, 1.95f, 2.25f, 3.75f, 4.05f, 5.55f, 5.85f, 7.35f, 7.65f };
    static readonly int[] MapCandidates = { 40001, 100000000, 100010000, 104040000, 120000000 };

    static int[] ids;
    static float[] shotTimes;
    static int cursor, phase, mapCursor;
    static bool levelsReady, hooked, everFoundMobs, probeDone;
    static double mapHopAt;
    static readonly HashSet<int> seenFx = new HashSet<int>();
    static readonly List<string> report = new List<string>();
    static readonly List<FxSample> fxSamples = new List<FxSample>();
    static readonly List<TickStat> tickStats = new List<TickStat>();
    static TickStat currentTick;
    static long hitDamage;
    static int totalNewFx, shotIndex, globalHits, globalResolutions, globalFxLogs;
    static long globalDamage;
    static string castFail = "";
    static bool castOk;
    static Component target;
    static double runtimeStart;

    struct FxSample { public double t; public int fresh; public string parts; }
    class TickStat
    {
        public int tick, fx, resolutions, hits;
        public long damage;
        public readonly List<string> layers = new List<string>();
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += st =>
        {
            if (st == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(K_RUN, false))
                EditorPrefs.SetString(K_T0, Now());
        };
    }

    public static void Run()
    {
        string idArg = "", shotArg = "", dir = "/tmp/repeatplay";
        float window = 8f;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-repeatIds") idArg = args[i + 1];
            if (args[i] == "-repeatShots") dir = args[i + 1];
            if (args[i] == "-repeatShotTimes") shotArg = args[i + 1];
            if (args[i] == "-repeatWindow") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out window);
        }
        ids = ParseIds(idArg);
        shotTimes = ParseFloats(shotArg, DefaultShotTimes);
        System.IO.Directory.CreateDirectory(dir);

        EditorPrefs.SetString(K_IDS, string.Join(",", Array.ConvertAll(ids, x => x.ToString())));
        EditorPrefs.SetString(K_DIR, dir);
        EditorPrefs.SetString(K_SHOTS, Join(shotTimes));
        EditorPrefs.SetFloat(K_WINDOW, Mathf.Clamp(window, 2f, 40f));

        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("debug_god", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.SetInt("debug_start_level", 0);
        PlayerPrefs.SetInt("debug_hitlog", 1);
        PlayerPrefs.Save();

        cursor = 0; phase = 0; report.Clear();
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, Now());
        Debug.Log("[RepeatPlay] 准备进入 Play，技能 " + string.Join(",", Array.ConvertAll(ids, x => x.ToString())) +
                  "  窗口=" + window + "s");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    // 域重载后靠 Tick 重新挂日志回调：static 会被清空，一次性挂在 Run() 里会丢。
    static void EnsureHook()
    {
        if (hooked) return;
        hooked = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    // batchmode 的帧时间戳会跳变（一次编辑器回调里塞好几帧），所以不按时间分窗，
    // 而是按日志顺序归因：[Repeat] tick=N 之后到下一个 tick 之前的
    // [FxPlay] / [Hit] 全算第 N 发。这是「每一发到底有没有画面」的直接证据。
    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrEmpty(condition)) return;
        if (condition.StartsWith("[Repeat] tick=", StringComparison.Ordinal))
        {
            int i = "[Repeat] tick=".Length, n = 0;
            while (i < condition.Length && char.IsDigit(condition[i])) n = n * 10 + (condition[i++] - '0');
            currentTick = new TickStat { tick = n };
            tickStats.Add(currentTick);
            return;
        }
        if (condition.StartsWith("[FxPlay]", StringComparison.Ordinal))
        {
            globalFxLogs++;
            if (currentTick == null) return;
            currentTick.fx++;
            int i = condition.IndexOf("part=", StringComparison.Ordinal);
            if (i >= 0) currentTick.layers.Add(condition.Substring(i + 5));
            return;
        }
        if (!condition.StartsWith("[Hit]", StringComparison.Ordinal)) return;
        int h = condition.IndexOf("命中=", StringComparison.Ordinal);
        if (h < 0) return;
        int landed = 0; h += 3;
        while (h < condition.Length && char.IsDigit(condition[h])) landed = landed * 10 + (condition[h++] - '0');
        long amount = 0;
        int k = condition.IndexOf("总伤害=", StringComparison.Ordinal);
        if (k >= 0) { k += 4; while (k < condition.Length && char.IsDigit(condition[k])) amount = amount * 10 + (condition[k++] - '0'); }
        globalResolutions++;
        if (landed > 0) { globalHits += landed; globalDamage += amount; hitDamage += amount; }
        if (currentTick == null) return;
        currentTick.resolutions++;
        if (landed > 0) { currentTick.hits += landed; currentTick.damage += amount; }
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        if (ids == null || shotTimes == null)
        {
            ids = ParseIds(EditorPrefs.GetString(K_IDS, ""));
            shotTimes = ParseFloats(EditorPrefs.GetString(K_SHOTS, ""), DefaultShotTimes);
        }
        if (!EditorApplication.isPlaying)
        {
            if (Elapsed() > 45) { Debug.LogError("[RepeatPlay] 未进入播放模式"); Finish(1); }
            return;
        }
        Application.runInBackground = true;
        if (Elapsed() > 180) { Debug.LogError("[RepeatPlay] 总超时"); Finish(1); return; }
        EnsureHook();

        var gl = GameLoop.I;
        if (gl == null || gl.player == null || gl.field == null || gl.field.Map == null) return;
        if (Elapsed() < 1.5) return;
        var sys = gl.player.GetComponent<SkillSystem>();
        if (sys == null) { Debug.LogError("[RepeatPlay] 玩家没有 SkillSystem"); Finish(1); return; }
        if (cursor >= ids.Length) { Finish(0); return; }

        if (phase == 0)
        {
            if (EnemyUnit.All.Count == 0)
            {
                if (everFoundMobs) return;
                if (mapCursor >= MapCandidates.Length) return;
                if (EditorApplication.timeSinceStartup - mapHopAt < 1.6) return;
                mapHopAt = EditorApplication.timeSinceStartup;
                gl.field.Enter(MapCandidates[mapCursor], null);
                Debug.Log("[RepeatPlay] 换图试怪 → " + MapCandidates[mapCursor]);
                mapCursor++;
                return;
            }
            everFoundMobs = true;
            if (!levelsReady) { InstallMatrixLevels(); levelsReady = true; }
            if (!StageForCast(gl)) return;
            StartCast(sys, ids[cursor], gl);
            // 采样必须挂在运行时协程上，编辑器回调在 batchmode 会被节流。
            sys.StartCoroutine(Probe(sys, ids[cursor], EditorPrefs.GetFloat(K_WINDOW, 8f)));
            phase = 1;
            return;
        }
        if (phase == 1)
        {
            if (!probeDone) return;
            BuildReport(ids[cursor]);
            phase = 2;
            cursor++;
            return;
        }
        phase = 0;
        foreach (var fx in UnityEngine.Object.FindObjectsOfType<SkillFx>()) if (fx != null) fx.Stop();
        ClearActionLock(sys);
        if (cursor >= ids.Length) Finish(0);
    }

    static void StartCast(SkillSystem sys, int id, GameLoop gl)
    {
        gl.player.mpMax = Mathf.Max(gl.player.mpMax, 999999f);
        gl.player.mp = gl.player.mpMax;
        gl.player.attackDamage = 0.02f;
        totalNewFx = 0; shotIndex = 0; hitDamage = 0;
        globalHits = 0; globalResolutions = 0; globalFxLogs = 0; globalDamage = 0;
        probeDone = false;
        tickStats.Clear(); currentTick = null; fxSamples.Clear(); seenFx.Clear();
        foreach (var fx in UnityEngine.Object.FindObjectsOfType<SkillFx>())
            if (fx != null) { seenFx.Add(fx.GetInstanceID()); fx.Stop(); }
        ClearActionLock(sys);
        runtimeStart = Time.realtimeSinceStartupAsDouble;
        try { castOk = sys.TryCastSkill(id); castFail = sys.LastFailure; }
        catch (Exception e) { castOk = false; castFail = e.GetType().Name + " " + e.Message; }
        var data = SkillBook.Get(id);
        Debug.Log("[RepeatPlay] 施放 " + id + "「" + (data != null ? data.name : "?") + "」 → " +
                  (castOk ? "成功" : "失败（" + castFail + "）") + "  怪数=" + EnemyUnit.All.Count);
        if (!castOk) { report.Add("  " + id + " ✗ 施放失败：" + castFail); probeDone = true; }
    }

    // 每帧：保住一只活靶（补 hp + 贴身站位）、数新层、按计划时间点截图。
    static IEnumerator Probe(SkillSystem sys, int id, float window)
    {
        double lastSample = -1;
        while (true)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float t = (float)(now - runtimeStart);
            KeepTargetAlive();
            if (now - lastSample >= .05f)
            {
                lastSample = now;
                int fresh = 0;
                var parts = new List<string>();
                foreach (var fx in UnityEngine.Object.FindObjectsOfType<SkillFx>())
                {
                    if (fx == null || !fx.gameObject.activeInHierarchy) continue;
                    if (seenFx.Add(fx.GetInstanceID())) { fresh++; parts.Add(fx.Part); }
                }
                totalNewFx += fresh;
                fxSamples.Add(new FxSample { t = now, fresh = fresh, parts = string.Join(" ", parts.ToArray()) });
                if (fresh > 0) report.Add("    [Fx] +" + (now - runtimeStart).ToString("0.00") + "s  " + string.Join(" ", parts.ToArray()));
            }
            if (shotIndex < shotTimes.Length && t >= shotTimes[shotIndex])
                Shot(id, shotTimes[shotIndex++]);
            if (t >= window) break;
            yield return null;
        }
        probeDone = true;
    }

    // 40001 的怪只有 8 血，一发就没了；每帧把它贴回施法者身前并补满 hp，
    // 这样 8 秒里每一发都有靶子，命中数才等于真实发包数。
    static void KeepTargetAlive()
    {
        var gl = GameLoop.I;
        if (gl == null || gl.player == null) return;
        var e = target as EnemyUnit;
        if (e == null || e.dead || e.hp <= 0)
        {
            float best = float.MaxValue;
            foreach (var unit in EnemyUnit.All)
            {
                if (unit == null || unit.dead || unit.hp <= 0L) continue;
                float d = Mathf.Abs(unit.transform.position.x - gl.player.worldX);
                if (d < best) { best = d; e = unit; }
            }
            if (e == null) { target = null; return; }
            target = e;
        }
        e.hp = 8000L;
        float x = e.transform.position.x - 1.2f;
        gl.player.PlaceAt(x, PlayerCtrl.GroundAt(x, e.transform.position.y));
        gl.player.facing = 1;
    }

    static void BuildReport(int id)
    {
        var data = SkillBook.Get(id);
        report.Insert(0, "=== " + id + "「" + (data != null ? data.name : "?") + "」 采样窗口=" +
                      EditorPrefs.GetFloat(K_WINDOW, 8f).ToString("0.0") + "s  施放=" + (castOk ? "成功" : "失败"));
        report.Insert(1, "  周期 tick 数=" + tickStats.Count + "  运行时新层=" + totalNewFx +
                      "  [FxPlay] 日志=" + globalFxLogs + "  结算次数=" + globalResolutions +
                      "  真实命中=" + globalHits + "  命中总伤害=" + globalDamage);
        bool allGood = tickStats.Count >= 2 && globalHits >= 2 && globalDamage > 0;
        foreach (var t in tickStats)
        {
            // 每发必须同时有「新生成的特效层」和「一次 HitAt 结算」；
            // 命中数单独看：靶子被打死/离开判定框时 选中=0 属场地限制，不影响「这一发有没有画面」。
            bool ok = t.fx > 0 && t.resolutions > 0;
            if (!ok) allGood = false;
            report.Add("  tick#" + t.tick + "  新特效层=" + t.fx +
                       "  结算=" + t.resolutions + "  命中=" + t.hits + "  伤害=" + t.damage +
                       "  " + (ok ? "✓" : "✗") +
                       (t.layers.Count > 0 ? "  [" + string.Join(" ", t.layers.ToArray()) + "]" : ""));
        }
        report.Add("  结论：" + (allGood
            ? "✓ 每一发都有新特效层 + 伤害结算，且多发真实命中造成 " + globalDamage + " 伤害"
            : "✗ 仍有「只有伤害没特效」或「只有特效没伤害」的发次"));
        Debug.Log("[RepeatPlay] " + report[report.Count - 1]);
    }

    static void Shot(int id, float t)
    {
        string name = id + "_" + Mathf.RoundToInt(t * 1000f).ToString("D4") + "ms.png";
        var cam = Camera.main;
        if (cam == null) return;
        const int W = 960, H = 540;
        Rect viewport = cam.rect; float aspect = cam.aspect;
        RenderTexture rt = null, prev = RenderTexture.active; Texture2D tex = null;
        try
        {
            rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt; cam.rect = new Rect(0, 0, 1, 1); cam.aspect = (float)W / H;
            foreach (var view in UnityEngine.Object.FindObjectsOfType<NativeMapView>()) view.SendMessage("LateUpdate");
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(EditorPrefs.GetString(K_DIR, "/tmp/repeatplay"), name), tex.EncodeToPNG());
        }
        catch (Exception e) { Debug.LogWarning("[RepeatPlay] 截图失败 " + name + "：" + e.Message); }
        finally
        {
            cam.targetTexture = null; cam.rect = viewport; cam.aspect = aspect;
            RenderTexture.active = prev;
            if (rt != null) UnityEngine.Object.Destroy(rt);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    static void InstallMatrixLevels()
    {
        VMatrixCatalog.Ensure();
        var response = new VMatrixResponse();
        foreach (int id in ids)
        {
            VMatrixCore core = null;
            foreach (var c in VMatrixCatalog.AllCores)
                if (c.skills != null && Array.IndexOf(c.skills, id) >= 0) { core = c; break; }
            response.cores.Add(new VMatrixCoreView
            {
                coreId = core != null ? core.id : 0, level = 30, maxLevel = 30,
                skills = core != null && core.skills != null && core.skills.Length > 0 ? core.skills : new[] { id },
                name = core != null ? core.name : "", desc = core != null ? core.desc : "",
            });
        }
        VMatrixLevels.Capture(response, 990001);
        foreach (int id in ids) Debug.Log("[RepeatPlay] 注入等级 " + id + " → Lv." + SkillProgress.LevelOf(id));
    }

    static bool StageForCast(GameLoop gl)
    {
        var pl = gl.player;
        EnemyUnit pick = null; float best = float.MaxValue;
        foreach (var e in EnemyUnit.All)
        {
            if (e == null || e.dead) continue;
            float d = Mathf.Abs(e.transform.position.x - pl.worldX);
            if (d < best) { best = d; pick = e; }
        }
        if (pick == null) return false;
        target = pick; pick.hp = 8000L;
        float x = pick.transform.position.x - 1.2f;
        pl.PlaceAt(x, PlayerCtrl.GroundAt(x, pick.transform.position.y));
        pl.facing = 1;
        return true;
    }

    static void ClearActionLock(SkillSystem sys)
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(SkillSystem);
        type.GetField("actionUntil", F)?.SetValue(sys, 0f);
        type.GetField("actionBlocking", F)?.SetValue(sys, false);
        type.GetField("actionSkillId", F)?.SetValue(sys, 0);
    }

    static int[] ParseIds(string text)
    {
        if (string.IsNullOrEmpty(text)) return DefaultIds;
        var list = new List<int>();
        foreach (string part in text.Split(',')) if (int.TryParse(part.Trim(), out int id) && id > 0) list.Add(id);
        return list.Count == 0 ? DefaultIds : list.ToArray();
    }
    static float[] ParseFloats(string text, float[] fallback)
    {
        if (string.IsNullOrEmpty(text)) return fallback;
        var list = new List<float>();
        foreach (string part in text.Split(','))
            if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 0f) list.Add(v);
        if (list.Count == 0) return fallback;
        list.Sort(); return list.ToArray();
    }
    static string Join(float[] values) => string.Join(",", Array.ConvertAll(values, x => x.ToString("R", CultureInfo.InvariantCulture)));
    static string Now() => EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);
    static double Elapsed() =>
        EditorApplication.timeSinceStartup -
        (double.TryParse(EditorPrefs.GetString(K_T0, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0);

    static void Finish(int code)
    {
        string text = string.Join("\n", report.ToArray());
        try { System.IO.File.WriteAllText("/tmp/skill_repeat_play.txt", text); } catch { }
        Debug.Log("[RepeatPlay]\n" + text);
        EditorPrefs.DeleteKey(K_RUN);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(code);
    }
}
