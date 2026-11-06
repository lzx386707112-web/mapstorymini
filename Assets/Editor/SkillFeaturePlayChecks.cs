using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

// 四条新接线的 Play Mode 综合验证（按日志顺序归因，不依赖 batchmode 的帧时间戳）：
//   ① keydownFinish 松手收尾  ② mask/rushEffect 取层
//   ③ 極冰風暴 诅咒+冰塊 proc ④ 普力特祝福 六层叠加
//
// 命令行（不要 -quit）：
//   Unity -batchmode -projectPath . -executeMethod SkillFeaturePlayChecks.Run \
//         -featureHold 2.0 -featureShots /tmp/feature -logFile /tmp/feature.log
public static class SkillFeaturePlayChecks
{
    const string K_RUN = "FeaturePlay.running";
    const string K_T0 = "FeaturePlay.t0";
    const string K_DIR = "FeaturePlay.dir";
    const string K_HOLD = "FeaturePlay.hold";

    static readonly int[] MapCandidates = { 40001, 100000000, 100010000, 104040000, 120000000 };

    sealed class Stage
    {
        public string name;
        public int casts, castOk;
        public readonly HashSet<string> fx = new HashSet<string>();
        public readonly Dictionary<string, int> marks = new Dictionary<string, int>();
        public readonly Dictionary<string, int> hits = new Dictionary<string, int>();
        public readonly List<string> notes = new List<string>();
        public void Mark(string tag)
        {
            if (!marks.ContainsKey(tag)) marks[tag] = 0;
            marks[tag]++;
        }
        public string Report()
        {
            var lines = new List<string>
            {
                "=== " + name + "  施放=" + castOk + "/" + casts,
                "  新特效层 " + fx.Count + ": " + string.Join(" ", new List<string>(fx).ToArray()),
                "  标记 " + string.Join(" ", Array.ConvertAll(new List<string>(marks.Keys).ToArray(),
                    k => k + "=" + marks[k])),
            };
            var hitList = new List<string>();
            foreach (var pair in hits) hitList.Add(pair.Key + "=" + pair.Value);
            lines.Add("  [Hit] " + string.Join(" ", hitList.ToArray()));
            lines.AddRange(notes);
            return string.Join("\n", lines.ToArray());
        }
    }

    static readonly List<Stage> stages = new List<Stage>();
    static Stage current;
    static int stageIndex, mapCursor;
    static double stageAt, mapHopAt;
    static bool hooked, levelsReady, everFoundMobs, holding;
    static int heldSlot;
    static GameAction heldAction;
    static float hold;
    // 收尾冲击波跨阶段统计：8 波在 batchmode 下会被节流拉成好几秒，不能按阶段窗口归属。
    static bool finisherStage;
    static readonly List<string> finisherWaves = new List<string>();
    static readonly Dictionary<string, int> totalHits = new Dictionary<string, int>();
    static int stackLogs, curseCastLogs, curseIceLogs;
    static string dir = "/tmp/feature";
    static readonly List<string> report = new List<string>();

    static readonly string[] Tags = { "[Finisher]", "[Curse]", "[Stack]", "[Repeat]", "[FxPlay]", "[Hit]" };

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
        hold = 2.0f;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-featureHold") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out hold);
            if (args[i] == "-featureShots") dir = args[i + 1];
        }
        System.IO.Directory.CreateDirectory(dir);
        EditorPrefs.SetFloat(K_HOLD, Mathf.Max(.5f, hold));
        EditorPrefs.SetString(K_DIR, dir);
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, Now());
        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("debug_god", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.SetInt("debug_start_level", 0);
        PlayerPrefs.SetInt("debug_hitlog", 1);
        PlayerPrefs.Save();
        stages.Clear(); report.Clear();
        finisherWaves.Clear(); totalHits.Clear(); finisherStage = false;
        stackLogs = 0; curseCastLogs = 0; curseIceLogs = 0;
        stageIndex = 0; hooked = false; levelsReady = false; everFoundMobs = false;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        Debug.Log("[FeaturePlay] 准备进入 Play");
        EditorApplication.EnterPlaymode();
    }

    static void EnsureHook()
    {
        if (hooked) return;
        hooked = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (current == null || string.IsNullOrEmpty(condition)) return;
        foreach (string tag in Tags)
            if (condition.StartsWith(tag, StringComparison.Ordinal)) { current.Mark(tag); break; }
        if (condition.StartsWith("[FxPlay]", StringComparison.Ordinal))
        {
            int i = condition.IndexOf("part=", StringComparison.Ordinal);
            if (i >= 0) current.fx.Add(condition.Substring(i + 5).Trim());
        }
        if (condition.StartsWith("[Hit]", StringComparison.Ordinal))
        {
            int i = condition.IndexOf("id=", StringComparison.Ordinal);
            if (i >= 0)
            {
                string id = ""; i += 3;
                while (i < condition.Length && char.IsDigit(condition[i])) id += condition[i++];
                if (id.Length > 0)
                {
                    if (!current.hits.ContainsKey(id)) current.hits[id] = 0;
                    current.hits[id]++;
                    if (!totalHits.ContainsKey(id)) totalHits[id] = 0;
                    totalHits[id]++;
                }
            }
        }
        if (condition.StartsWith("[Finisher] wave=", StringComparison.Ordinal)) finisherWaves.Add(condition);
        if (condition.StartsWith("[Stack]", StringComparison.Ordinal)) stackLogs++;
        if (condition.StartsWith("[Curse] cast", StringComparison.Ordinal)) curseCastLogs++;
        if (condition.StartsWith("[Curse] ice", StringComparison.Ordinal)) curseIceLogs++;
        foreach (string tag in new[] { "[Finisher]", "[Curse]", "[Stack]" })
            if (condition.StartsWith(tag, StringComparison.Ordinal) && current.notes.Count < 12)
                current.notes.Add("    " + condition);
    }

    static Stage MakeStage(string name)
    {
        var s = new Stage { name = name };
        stages.Add(s);
        return s;
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        if (!EditorApplication.isPlaying)
        {
            if (Elapsed() > 45) { Debug.LogError("[FeaturePlay] 未进入播放模式"); Finish(1); }
            return;
        }
        Application.runInBackground = true;
        EnsureHook();
        if (Elapsed() > 240) { Debug.LogError("[FeaturePlay] 超时"); Finish(1); return; }
        var gl = GameLoop.I;
        if (gl == null || gl.player == null || gl.field == null || gl.field.Map == null) return;
        if (Elapsed() < 1.5) return;
        var sys = gl.player.GetComponent<SkillSystem>();
        if (sys == null) { Debug.LogError("[FeaturePlay] 玩家没有 SkillSystem"); Finish(1); return; }

        if (!levelsReady) { InstallMatrixLevels(); levelsReady = true; }
        if (EnemyUnit.All.Count == 0)
        {
            if (everFoundMobs) return;
            if (mapCursor < MapCandidates.Length && EditorApplication.timeSinceStartup - mapHopAt > 1.6)
            {
                mapHopAt = EditorApplication.timeSinceStartup;
                gl.field.Enter(MapCandidates[mapCursor++], null);
            }
            return;
        }
        everFoundMobs = true;

        if (stages.Count == 0)
        {
            BuildPlan();
            stageIndex = 0;
        }
        if (stageIndex >= stages.Count) { Finish(0); return; }

        var stage = stages[stageIndex];
        if (current != stage)
        {
            current = stage;
            RunStage(stage, sys, gl);
            stageAt = EditorApplication.timeSinceStartup;
            return;
        }
        double elapsed = EditorApplication.timeSinceStartup - stageAt;
        double wait = EditorPrefs.GetFloat(K_HOLD, 2f) + .6;
        if (holding && elapsed >= EditorPrefs.GetFloat(K_HOLD, 2f))
        {
            InputHub.SetVirtualAction(heldAction, false);
            QuickSlots.Bind(heldSlot, 0);
            holding = false;
        }
        // 收尾冲击波是 8 波 x intervalMs，batchmode 的 WaitForSeconds 被节流后真实耗时会拉长
        // 好几倍；它又是最后一段，所以给这一段单独放宽窗口，等 8 波全到或超时。
        // 必须放在松手之后判：先松手才有收尾，顺序反了就永远等不到。
        if (finisherStage && finisherWaves.Count < 8 && elapsed < 20) return;
        if (elapsed < wait) return;
        report.Add(stage.Report());
        stageIndex++;
    }

    static void BuildPlan()
    {
        // 收尾冲击波放在最后：8 波要跨好几秒真实时间，放中间会被后面的阶段切断统计。
        MakeStage("A 施放 400011028 火神重擊（mask 是纯黑 stencil，必须**不**出画面）");
        MakeStage("B 施放 400011104 奔馳的閃光（rushEffect/0 必须出层）");
        MakeStage("C 施放 400011121 極冰風暴 + 400011072 命中（诅咒/冰塊）");
        MakeStage("D 连按 400001024 普力特的祝福 6 次（六层叠加）");
        MakeStage("E 回归 400011052 祝福之鎚");
        MakeStage("F 回归 400011131 雷神戰槌");
        MakeStage("G 按住 400011068 槍刺旋風 → 松手 keydownFinish 400011069（8 波）");
    }

    static void RunStage(Stage stage, SkillSystem sys, GameLoop gl)
    {
        switch (stageIndex)
        {
            case 0: Cast(stage, sys, gl, 400011028); break;
            case 1: Cast(stage, sys, gl, 400011104); break;
            case 2:
                // 極冰風暴的诅咒是按 400011121 自己的 lt/rb(-500,-400)..(500,250) 选目标的，
                // 测试场小怪常常站在 500px 外 ⇒ 上一轮 targets=0、诅咒一个都没上。
                // 先贴到最近的小怪身前再放，这才是「技能打得到人」的正常站位。
                StandNearMob(gl);
                Cast(stage, sys, gl, 400011121);
                // 极冰风暴自己是 Buff 分支不打伤害；再用一次直接攻击去命中被诅咒目标。
                Cast(stage, sys, gl, 400011072);
                break;
            case 3:
                for (int i = 0; i < 6; i++) Cast(stage, sys, gl, 400001024);
                break;
            case 4: Cast(stage, sys, gl, 400011052); break;
            case 5: Cast(stage, sys, gl, 400011131); break;
            case 6:
                finisherStage = true;
                Prep(gl, sys);
                heldSlot = 0;
                QuickSlots.Bind(heldSlot, 400011068);
                heldAction = QuickSlots.Action(heldSlot);
                InputHub.Enabled = true;
                InputHub.SetVirtualAction(heldAction, true);
                stage.casts++;
                QuickSlots.Cast(heldSlot);     // void；成功与否看后续 [Hit]/[Finisher] 日志
                stage.castOk++;
                holding = true;
                break;
        }
    }

    static void Prep(GameLoop gl, SkillSystem sys)
    {
        gl.player.mpMax = Mathf.Max(gl.player.mpMax, 999999f);
        gl.player.mp = gl.player.mpMax;
        gl.player.attackDamage = 0.02f;
        ClearActionLock(sys);
        ClearCooldowns(sys);
    }

    // 站到最近小怪身前 1.2 单位，保证 lt/rb 只有 500px 的技能也能框到人。
    static void StandNearMob(GameLoop gl)
    {
        EnemyUnit best = null;
        float bestDistance = float.MaxValue;
        foreach (var e in EnemyUnit.All)
        {
            if (e == null || e.dead) continue;
            float d = ((Vector2)e.transform.position - gl.player.FeetPos).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d; best = e;
        }
        if (best == null) return;
        float face = gl.player.facing >= 0 ? 1f : -1f;
        gl.player.PlaceAt(best.transform.position.x - 1.2f * face, best.transform.position.y);
    }

    static void Cast(Stage stage, SkillSystem sys, GameLoop gl, int id)
    {        // 每发之前都要重置：阶段内连放两个技能时，前一个的 actionUntil 会把后一个
        // 拒成「正在释放技能」（上一轮 400011131 就是这么假失败的）。
        Prep(gl, sys);
        stage.casts++;
        try
        {
            if (sys.TryCastSkill(id)) stage.castOk++;
            else stage.notes.Add("    ✗ " + id + " 施放失败：" + sys.LastFailure);
        }
        catch (Exception e) { stage.notes.Add("    ✗ " + id + " 异常 " + e.GetType().Name); }
    }

    // 连按 6 次要绕过 25 秒冷却；反射清 states（SkillRuntimeState 字典）。
    static void ClearCooldowns(SkillSystem sys)
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var field = typeof(SkillSystem).GetField("states", F);
        if (field?.GetValue(sys) is System.Collections.IDictionary dict) dict.Clear();
    }

    static void ClearActionLock(SkillSystem sys)
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(SkillSystem);
        type.GetField("actionUntil", F)?.SetValue(sys, 0f);
        type.GetField("actionBlocking", F)?.SetValue(sys, false);
        type.GetField("actionSkillId", F)?.SetValue(sys, 0);
    }

    static void InstallMatrixLevels()
    {
        VMatrixCatalog.Ensure();
        int[] ids = { 400011068, 400011069, 400011028, 400011104, 400011121, 400011122,
                      400011072, 400001024, 400011052, 400011131 };
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
        Debug.Log("[FeaturePlay] 等级注入完成，400011068 → Lv." + SkillProgress.LevelOf(400011068));
    }

    static string Now() => EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);
    static double Elapsed() =>
        EditorApplication.timeSinceStartup -
        (double.TryParse(EditorPrefs.GetString(K_T0, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0);

    static void Finish(int code)
    {
        var hitParts = new List<string>();
        foreach (var pair in totalHits) hitParts.Add(pair.Key + "=" + pair.Value);
        bool finisherOk = finisherWaves.Count >= 8 && totalHits.ContainsKey("400011069");
        bool stackOk = stackLogs >= 6;
        bool curseOk = curseCastLogs >= 1 && curseIceLogs >= 1;
        report.Add("=== 【汇总】");
        report.Add("  ① keydownFinish 收尾：波次 " + finisherWaves.Count + "/8，" +
                   "400011069 命中 " + (totalHits.ContainsKey("400011069") ? totalHits["400011069"] : 0) +
                   "  " + (finisherOk ? "✓" : "✗"));
        bool maskLeak = stages.Count > 0 && stages[0].fx.Contains("mask");
        bool rushOk = stages.Count > 1 && stages[1].fx.Contains("rushEffect/0");
        report.Add("  ② rushEffect 取层：" + (rushOk ? "✓" : "✗") +
                   "；mask 不误画：" + (maskLeak ? "✗ 出现黑条" : "✓"));
        report.Add("  ③ 極冰風暴 诅咒/冰塊：[Curse] cast=" + curseCastLogs + " ice=" + curseIceLogs +
                   "  " + (curseOk ? "✓" : "✗"));
        report.Add("  ④ 普力特的祝福 叠层：[Stack]=" + stackLogs + "  " + (stackOk ? "✓" : "✗"));
        report.Add("  全程命中：" + string.Join(" ", hitParts.ToArray()));
        string text = string.Join("\n", report.ToArray());
        try { System.IO.File.WriteAllText("/tmp/skill_feature_play.txt", text); } catch { }
        Debug.Log("[FeaturePlay]\n" + text);
        EditorPrefs.DeleteKey(K_RUN);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(code);
    }
}
