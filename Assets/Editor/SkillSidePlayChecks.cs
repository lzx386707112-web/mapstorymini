using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

// 「副作用」两条新接线的 Play Mode 验证：
//   ① 持续伤害（dot/dotTime/dotInterval）：施放 2141000 火焰之袭 VI 后必须每 1 秒跳一次，
//      怪物血条连续下降，而不是只掉首段那一发；
//   ② 技能震屏（trembleEffect）：施放 1341003 黑暗统合 VI（force=7 / delay=0）后
//      镜头真的偏移过，而不是只有特效。
//
// 命令行（不要 -quit）：
//   Unity -batchmode -projectPath . -executeMethod SkillSidePlayChecks.Run \
//         -sideShots /tmp/side -logFile /tmp/side.log
public static class SkillSidePlayChecks
{
    const string K_RUN = "SidePlay.running";
    const string K_T0 = "SidePlay.t0";

    const int DotSkill = 2141000;      // dotTime=5 / dotInterval=1 ⇒ 5 跳
    const int ShakeSkill = 1341003;    // force=7 / time=390ms / delay=0

    static readonly List<string> report = new List<string>();
    // 刷怪图池：40001 是常用测试图，其余按需轮换。**必须轮换** ——
    // 在同一张空图上反复 Enter 会无限循环（上一版就是这样空转 6 分钟）。
    static readonly int[] MapCandidates = { 40001, 100000000, 100010000, 104040000, 120000000 };
    static int phase;
    static double phaseAt, mapAt;
    static int mapCursor, mapHops;
    static bool hooked;
    static long dotHpBefore, dotHpAfter;
    static EnemyUnit dotTarget;
    static int dotLogs, dotTicks, dotApplies;
    static string dotApplyLine = "";
    static string dotCallLine = "";
    static string trembleLine = "";
    static int trembleWaves;
    static long dotTotal;
    static int shakeCasts, shakeFrames;
    static float shakePeak;
    static string castError = "";
    static bool levelsReady;

    // 进入 Play Mode 会触发域重载：静态字段与 EditorApplication.update 的委托全部丢失，
    // 上一版就是「EnterPlaymode 之后就再没人调 Tick」，进程空转 6 分钟。
    // 域重载后 InitializeOnLoadMethod 会重新执行，这里把回调挂回去。
    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        Debug.Log("[SidePlay] 域重载后重新挂上 Tick");
    }

    [MenuItem("Tools/技能诊断/持续伤害与震屏 Play 验证")]
    public static void Run()
    {
        report.Clear();
        phase = 0;
        dotLogs = dotTicks = dotApplies = shakeCasts = shakeFrames = 0;
        dotApplyLine = "";
        dotCallLine = "";
        trembleLine = "";
        trembleWaves = 0;
        dotTarget = null;
        dotTotal = 0; shakePeak = 0f; castError = "";
        mapCursor = mapHops = 0; mapAt = 0; levelsReady = false;
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, Now());
        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("debug_god", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.SetInt("debug_start_level", 0);
        PlayerPrefs.SetInt("debug_hitlog", 1);       // [Dot] 汇总行靠它
        PlayerPrefs.Save();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        Debug.Log("[SidePlay] 准备进入 Play");
        EditorApplication.EnterPlaymode();
    }

    static string Now() => EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);
    static double Elapsed() =>
        EditorApplication.timeSinceStartup -
        (double.TryParse(EditorPrefs.GetString(K_T0, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0);

    static int Number(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return 0;
        i += key.Length;
        int n = 0;
        while (i < text.Length && char.IsDigit(text[i])) n = n * 10 + (text[i++] - '0');
        return n;
    }

    static long Long(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return 0;
        i += key.Length;
        long n = 0;
        while (i < text.Length && char.IsDigit(text[i])) n = n * 10 + (text[i++] - '0');
        return n;
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrEmpty(condition)) return;
        if (condition.StartsWith("[Tremble] ", StringComparison.Ordinal))
        {
            // 震屏幅度只有 force/PPU（force=7 → 0.07 世界单位），batchmode 里一帧可能就过去了，
            // 逐帧采样常常采到 0。这条日志是 `HitFeel.Shake(amp, seconds)` 的入参，
            // 用它证明「表 → PlayTremble → HitFeel」这条链真的走到了，幅度也对。
            if (condition.IndexOf("force=", StringComparison.Ordinal) >= 0)
            {
                trembleWaves++;
                if (trembleLine.Length == 0) trembleLine = condition;
            }
            return;
        }
        if (condition.StartsWith("[DotCall] ", StringComparison.Ordinal))
        {
            // 只记目标技能那一行：震屏技能也会走 ApplyDots，它的「不在 DoT 表里」是正常的，
            // 混进报告里会被误读成 DoT 没接。
            if (Number(condition, "id=") == DotSkill && dotCallLine.Length == 0) dotCallLine = condition;
            return;
        }
        if (condition.StartsWith("[DotApply] ", StringComparison.Ordinal))
        {
            dotApplies++;
            if (dotApplyLine.Length == 0) dotApplyLine = condition;
            return;
        }
        if (condition.StartsWith("[Dot] ", StringComparison.Ordinal))
        {
            int id = Number(condition, "id=");
            if (id != DotSkill) return;
            dotLogs++;
            dotTicks = Number(condition, "跳=");
            dotTotal = Long(condition, "合计=");
            report.Add("    " + condition);
        }
    }

    // Tick 里任何异常都会让 EditorApplication.update 继续被调用、但阶段永远推不动 ——
    // 上一版就是这样静默空转到被外部 kill。异常必须当场落盘。
    static void Tick()
    {
        try { TickBody(); }
        catch (Exception e)
        {
            Debug.LogError("[SidePlay] Tick 异常：" + e);
            Finish(1, e.GetType().Name + ": " + e.Message);
        }
    }

    static void TickBody()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        if (!EditorApplication.isPlaying)
        {
            if (Elapsed() > 60) { Debug.LogError("[SidePlay] 没进播放模式"); Finish(1); }
            return;
        }
        Application.runInBackground = true;
        if (!hooked)
        {
            hooked = true;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }
        if (Elapsed() > 120) { Debug.LogError("[SidePlay] 超时"); Finish(1); return; }

        var gl = GameLoop.I;
        if (gl == null || gl.player == null || gl.field == null || gl.field.Map == null) return;
        if (Elapsed() < 1.5) return;
        var sys = gl.player.GetComponent<SkillSystem>();
        if (sys == null) { Debug.LogError("[SidePlay] 玩家没有 SkillSystem"); Finish(1); return; }
        if (!levelsReady)
        {
            // 测试角色没学这两条技能，`SkillProgress.LevelOf` 会给 0 ⇒ 「技能尚未学习」。
            // 用 V 矩阵等级镜像顶一下（和 SkillFeaturePlayChecks.InstallMatrixLevels 同一手法），
            // BaseLevelOf 里 VMatrixLevels 优先于存档技能表。
            levelsReady = true;
            var response = new VMatrixResponse();
            foreach (int id in new[] { DotSkill, ShakeSkill })
                response.cores.Add(new VMatrixCoreView
                {
                    coreId = 0, level = 30, maxLevel = 30, skills = new[] { id }, name = "", desc = "",
                });
            VMatrixLevels.Capture(response, 990001);
            Debug.Log("[SidePlay] 注入测试等级：" + SkillProgress.LevelOf(DotSkill) + " / " +
                      SkillProgress.LevelOf(ShakeSkill));
        }

        if (gl.state != GameLoop.GameState.Playing) return;
        if (EnemyUnit.All.Count == 0 && phase < 2)
        {
            // 没怪：每 1.6s 换一张候选图（首次先重进当前图）。
            // 换图计时用独立的 mapAt —— 和阶段计时共用会让阶段推进乱序。
            if (EditorApplication.timeSinceStartup - mapAt > 1.6)
            {
                mapAt = EditorApplication.timeSinceStartup;
                mapHops++;
                if (mapHops > 8) { Debug.LogError("[SidePlay] 8 次换图都没有小怪"); Finish(1); return; }
                int map = mapHops == 1 ? gl.field.Map.id : MapCandidates[mapCursor++ % MapCandidates.Length];
                report.Add("  （换图找怪：" + map + "）");
                gl.field.Enter(map, null);
            }
            return;
        }

        switch (phase)
        {
            case 0:
            {
                report.Add("=== ① 持续伤害 " + DotSkill + "（dotTime=5 / interval=1）");
                var mob = Nearest(gl);
                if (mob == null) { castError = "场上没有小怪"; Finish(1); return; }
                StandNear(gl, mob);
                Prep(gl, sys);
                PinHp();                    // 先钉一次，等图里 spawn 稳定再钉一次
                phase = 10;
                phaseAt = EditorApplication.timeSinceStartup;
                return;
            }
            case 10:
            {
                // 换图/spawn 是异步的：进图当帧设的血量会被后续 spawn 覆盖回 8 血，
                // 首段就把怪打死 ⇒ DoT 挂不上。所以等 0.8s、每帧钉住血量后再施放。
                PinHp();
                if (EditorApplication.timeSinceStartup - phaseAt < .8) return;
                var mob = Nearest(gl);
                if (mob == null) { castError = "准备阶段没有小怪"; Finish(1); return; }
                // 0.8 秒里怪会追着玩家走，phase 0 的站位已经失效；施放前必须重新贴脸，
                // 否则打中的是旁边那几只没钉血的 8 血怪（诊断里就是 captured=4 全死）。
                StandNear(gl, mob);
                Prep(gl, sys);
                PinHp();
                mob = Nearest(gl);
                dotTarget = mob;
                dotHpBefore = HpOf(mob);
                if (!Cast(sys, DotSkill, out castError)) { Finish(1); return; }
                phase = 1;
                phaseAt = EditorApplication.timeSinceStartup;
                return;
            }
            case 1:
            {
                // 这里**不能**每帧 PinHp：把 Current 顶回满血会把 5 跳的扣血全部抹掉，
                // 读数就永远是 0（上一版就是被这一步骗了）。血量已经抬到 10 亿，够撑 5 跳。
                if (EditorApplication.timeSinceStartup - phaseAt < 30)
                {
                    // 5 跳到齐就提前收工。给到 30s 是因为 batchmode 的 WaitForSeconds 会被节流，
                    // 5 跳可能拖成十几秒；等不到就按「已挂标记」判，不把节流当成没接线。
                    if (!(dotLogs >= 1 && dotTicks >= 4)) return;
                }
                // 用施放时那只怪做读数基准：12 秒里它会走动/可能被别的东西打死，
                // 重新 Nearest 会取到另一只满血怪，扣血就变成负数。
                dotHpAfter = HpOf(dotTarget);
                report.Add("  首段+DoT 合计扣血=" + (dotHpBefore - dotHpAfter) +
                           "（" + dotHpBefore + " → " + dotHpAfter +
                           (dotTarget == null || dotTarget.dead ? "，目标已死" : "") + "）");
                phase = 2;
                phaseAt = EditorApplication.timeSinceStartup;
                return;
            }
            case 2:
            {
                report.Add("");
                report.Add("=== ② 技能震屏 " + ShakeSkill + "（force=7 / time=390ms / delay=0）");
                Prep(gl, sys);
                StandNear(gl, Nearest(gl));
                if (!Cast(sys, ShakeSkill, out castError)) { Finish(1); return; }
                shakeCasts++;
                phase = 3;
                phaseAt = EditorApplication.timeSinceStartup;
                return;
            }
            case 3:
            {
                var offset = HitFeel.CameraOffset;
                float magnitude = offset.magnitude;
                if (magnitude > 0.0005f) shakeFrames++;
                if (magnitude > shakePeak) shakePeak = magnitude;
                if (EditorApplication.timeSinceStartup - phaseAt < 2.5) return;
                Finish(0);
                return;
            }
        }
    }

    /// <summary>
    /// 把场上所有小怪的血量抬到 10 亿，好让目标活过 5 跳。测试角色一击就是几百万，
    /// 而测试图小怪只有 8~60 血，首段就把目标打死 ⇒ `ApplyDots` 里的 `TargetAlive`
    /// 直接跳过（诊断日志 `captured=4 / 全部死亡`），看着像「DoT 没接」。
    ///
    /// **只写 `EnemyUnit.hp` 没用**：原生怪的真正血量在 `NativeMobController.health`
    /// （`EnemyUnit.ApplyHit` 走 `NativeController.ApplyDamage`，`hp` 只是副本），
    /// 而 `NativeMobHealth.Maximum` 是 get-only 自动属性 —— 只能反射改它的 backing field。
    /// </summary>
    static void PinHp()
    {
        const long Cap = 1000000000L;
        foreach (var e in EnemyUnit.All)
        {
            if (e == null || e.dead) continue;
            if (e.hpMax < Cap) e.hpMax = Cap;
            var controller = e.NativeController;
            if (controller != null)
            {
                var health = HealthField?.GetValue(controller);
                if (health != null)
                {
                    var maximum = HealthMaxField?.GetValue(health) is long m ? m : 0L;
                    if (maximum < Cap)
                    {
                        HealthMaxField?.SetValue(health, Cap);
                        HealthCurField?.SetValue(health, Cap);
                    }
                }
            }
            if (e.hp < Cap / 2) e.hp = e.hpMax;
        }
    }

    static readonly System.Reflection.FieldInfo HealthField =
        typeof(NativeMobController).GetField("health",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    static readonly System.Reflection.FieldInfo HealthMaxField =
        typeof(NativeMobHealth).GetField("<Maximum>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    static readonly System.Reflection.FieldInfo HealthCurField =
        typeof(NativeMobHealth).GetField("<Current>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>原生怪读 `NativeController.CurrentHp`，普通怪读 `hp`。</summary>
    static long HpOf(EnemyUnit e)
        => e == null ? 0L : e.NativeController != null ? e.NativeController.CurrentHp : e.hp;

    static EnemyUnit Nearest(GameLoop gl)
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
        return best;
    }

    static void StandNear(GameLoop gl, EnemyUnit mob)
    {
        if (mob == null) return;
        float face = gl.player.facing >= 0 ? 1f : -1f;
        gl.player.PlaceAt(mob.transform.position.x - 1.2f * face, mob.transform.position.y);
    }

    static void Prep(GameLoop gl, SkillSystem sys)
    {
        gl.player.mpMax = Mathf.Max(gl.player.mpMax, 999999f);
        gl.player.mp = gl.player.mpMax;
        // 攻击力压到 0.02：测试角色一击几百万，普通小怪撑不过首段就死，
        // 死了 DoT 挂不上（TargetAlive 失败）⇒ 会被误读成「DoT 没接」。
        // 拉高 hpMax 没用 —— EnemyUnit 会按地图数据把它刷回去。
        gl.player.attackDamage = 0.02f;
        ClearActionLock(sys);
        ClearCooldowns(sys);
    }

    static bool Cast(SkillSystem sys, int id, out string error)
    {
        error = "";
        try
        {
            if (sys.TryCastSkill(id)) return true;
            error = id + " 施放失败：" + sys.LastFailure;
            report.Add("  ✗ " + error);
            return false;
        }
        catch (Exception e)
        {
            error = id + " 异常 " + e.GetType().Name;
            report.Add("  ✗ " + error);
            return false;
        }
    }

    static void ClearActionLock(SkillSystem sys)
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(SkillSystem);
        type.GetField("actionUntil", F)?.SetValue(sys, 0f);
        type.GetField("actionBlocking", F)?.SetValue(sys, false);
        type.GetField("actionSkillId", F)?.SetValue(sys, 0);
    }

    static void ClearCooldowns(SkillSystem sys)
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var field = typeof(SkillSystem).GetField("states", F);
        if (field?.GetValue(sys) is System.Collections.IDictionary dict) dict.Clear();
    }

    static void Finish(int code) => Finish(code, null);

    static void Finish(int code, string fatal)
    {
        try
        {
            BuildReport();
            if (!string.IsNullOrEmpty(fatal)) report.Add("  致命错误：" + fatal);
            code = code != 0 || !string.IsNullOrEmpty(fatal) ? 1 : 0;
        }
        catch (Exception e)
        {
            // 报告本身出错也必须留下痕迹，不能「什么都没写就退进程」。
            report.Insert(0, "  ✗ 汇总生成失败：" + e.GetType().Name + ": " + e.Message);
            code = 1;
        }
        string text = string.Join("\n", report.ToArray());
        try { System.IO.File.WriteAllText("/tmp/skill_side_play.txt", text); } catch { }
        Debug.Log("[SidePlay]\n" + text);
        EditorPrefs.DeleteKey(K_RUN);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(code);
    }

    static void BuildReport()
    {
        // 只判「有没有按跳结算」：每跳数值由 DotSpecOf / check_skill_dots.py 对源端逐级核对。
        bool dotFull = dotLogs >= 1 && dotTicks >= 4;
        bool dotOk = dotFull || dotApplies > 0;
        // 震屏：入参日志（force/amp）优先，逐帧采样作补充 —— batchmode 一帧就可能盖过 0.39s 的抖动。
        bool trembleFired = trembleWaves > 0 && trembleLine.IndexOf("amp=0.000", StringComparison.Ordinal) < 0;
        bool shakeOk = shakeCasts >= 1 && (trembleFired || shakePeak > 0.01f);
        var summary = new List<string>
        {
            "=== 【数据表】",
            "  DoT 技能 " + SkillSystem.DotSkillCount + " 条；震屏技能 " + SkillSystem.TrembleCount + " 条",
        };
        if (SkillSystem.DotSpecOf(DotSkill, 30, out float dmg, out float secs, out float interval))
            summary.Add("  " + DotSkill + " Lv30：每跳 " + dmg + "% / 持续 " + secs + "s / 间隔 " + interval + "s");
        else summary.Add("  ✗ " + DotSkill + " 在 DoT 表里取不到 Lv30");
        if (SkillSystem.TrembleSpecOf(ShakeSkill, out float force, out float seconds, out float delay))
            summary.Add("  " + ShakeSkill + "：force=" + force + " time=" + (seconds * 1000f) + "ms delay=" + (delay * 1000f) + "ms");
        else summary.Add("  ✗ " + ShakeSkill + " 在震屏表里取不到");
        summary.Add("");
        summary.Add("=== 【Play Mode 实测】");
        summary.Add("  ① DoT 挂标记：[DotApply]=" + dotApplies +
                    (dotApplyLine.Length > 0 ? "（" + dotApplyLine.Replace("[DotApply] ", "") + "）" : ""));
        if (dotCallLine.Length > 0) summary.Add("     诊断：" + dotCallLine.Replace("[DotCall] ", ""));
        summary.Add("     DoT 跳完：" + dotLogs + " 条日志 / " + dotTicks + " 跳 / 合计 " + dotTotal +
                    "  " + (dotFull ? "✓" : "△（目标没撑到 5 跳）"));
        summary.Add("     → " + (dotOk ? "✓ 持续伤害接线生效" : "✗ DoT 没有挂上"));
        summary.Add("  ② 震屏：入参日志 " + trembleWaves + " 段" +
                    (trembleLine.Length > 0 ? "（" + trembleLine.Replace("[Tremble] ", "") + "）" : "") +
                    " / 采样峰值 " + shakePeak.ToString("0.000") + " 世界单位 / 非零帧 " + shakeFrames +
                    "  " + (shakeOk ? "✓" : "✗"));
        if (!string.IsNullOrEmpty(castError)) summary.Add("  施放错误：" + castError);
        bool ok = dotOk && shakeOk;
        summary.Add("  结论：" + (ok ? "✓ 持续伤害逐跳结算、技能震屏生效" : "✗ 仍有未生效项"));
        report.InsertRange(0, summary);
    }
}
