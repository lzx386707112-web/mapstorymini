using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

// 「自动追加攻击」（`addAttack{isAuto=1}`）的 Play Mode 验证。
//
// 判据：施放 14141000 五连投掷VI 之后，必须看到
//   ① `[AddAttack] 14141000 → 14141001`（链真的被触发，等级继承主技能）
//   ② `[Hit] id=14141001`（隐藏技能真的打出判定，不是只放个空技能）
// 只看到 ① 不够 —— 隐藏技能若 `SkillBook.Native` 取不到或被动作锁挡掉，
// 就只剩一行日志、伤害还是没有。
//
// 命令行（不要 -quit）：
//   Unity -batchmode -projectPath . -executeMethod SkillAddAttackPlayChecks.Run -logFile /tmp/aa.log
public static class SkillAddAttackPlayChecks
{
    const string K_RUN = "AddAttackPlay.running";
    const string K_T0 = "AddAttackPlay.t0";

    // 候选主技能：都要有 `addAttack{isAuto=1}`。14141000 是飞镖技能，
    // 测试角色没有弹药会被拒（上一版就卡在「缺少可用弹药」），所以按顺序往下试，
    // 第一个能施放的就是本轮被测对象 —— 「能不能施放」本身不是这条探针要证明的事。
    static readonly int[] Parents = { 400021020, 14141000, 14141002, 400021034, 400021025, 400021023 };
    static int parent, followup, cursor;
    static float linkDelay;
    // 全部 `[AddAttack]` 行：链头 400021020 应该连补两段（020→021→022），
    // 只看主链那一条会漏掉递归，也会把「链断在第二段」当成通过。
    static readonly List<string> allChains = new List<string>();
    static readonly List<int> hitIds = new List<int>();

    static readonly List<string> report = new List<string>();
    static readonly int[] MapCandidates = { 40001, 100000000, 100010000, 104040000, 120000000 };
    static int phase;
    static double phaseAt, mapAt;
    static int mapCursor, mapHops;
    static bool hooked, levelsReady;
    static string castError = "", chainLine = "", hitLine = "";
    static int chainCount, followupHits, followupTargets;

    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/技能诊断/追加攻击 Play 验证")]
    public static void Run()
    {
        report.Clear();
        phase = 0; mapCursor = mapHops = 0; mapAt = 0;
        levelsReady = false; hooked = false;
        castError = chainLine = hitLine = "";
        chainCount = followupHits = followupTargets = 0;
        parent = followup = cursor = 0; linkDelay = 0f;
        allChains.Clear();
        hitIds.Clear();
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, Now());
        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("debug_god", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.SetInt("debug_start_level", 0);
        PlayerPrefs.SetInt("debug_hitlog", 1);
        PlayerPrefs.Save();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
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

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrEmpty(condition)) return;
        if (condition.StartsWith("[AddAttack] ", StringComparison.Ordinal) &&
            condition.IndexOf(" → ", StringComparison.Ordinal) > 0)
        {
            if (!allChains.Contains(condition)) allChains.Add(condition);
            if (parent != 0 && Number(condition, "] ") == parent)
            {
                chainCount++;
                if (chainLine.Length == 0) chainLine = condition;
            }
            return;
        }
        if (condition.StartsWith("[Hit] id=", StringComparison.Ordinal))
        {
            int id = Number(condition, "id=");
            if (id > 0 && !hitIds.Contains(id)) hitIds.Add(id);
        }
        if (condition.StartsWith("[Hit] id=", StringComparison.Ordinal) &&
            followup != 0 && Number(condition, "id=") == followup)
        {
            followupHits++;
            followupTargets = Mathf.Max(followupTargets, Number(condition, "命中="));
            if (hitLine.Length == 0) hitLine = condition;
        }
    }

    static void Tick()
    {
        try { TickBody(); }
        catch (Exception e) { Finish(1, e.GetType().Name + ": " + e.Message); }
    }

    static void TickBody()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        if (!EditorApplication.isPlaying)
        {
            if (Elapsed() > 90) Finish(1, "没进播放模式");
            return;
        }
        Application.runInBackground = true;
        if (!hooked)
        {
            hooked = true;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }
        if (Elapsed() > 180) { Finish(1, "超时"); return; }

        var gl = GameLoop.I;
        if (gl == null || gl.player == null || gl.field == null || gl.field.Map == null) return;
        if (Elapsed() < 1.5) return;
        var sys = gl.player.GetComponent<SkillSystem>();
        if (sys == null) { Finish(1, "玩家没有 SkillSystem"); return; }
        if (!levelsReady)
        {
            levelsReady = true;
            var response = new VMatrixResponse();
            foreach (int id in Parents)
                response.cores.Add(new VMatrixCoreView
                {
                    coreId = 0, level = 30, maxLevel = 30, skills = new[] { id }, name = "", desc = "",
                });
            VMatrixLevels.Capture(response, 990002);
            report.Add("注入测试等级：" + Parents[0] + " = " + SkillProgress.LevelOf(Parents[0]));
        }
        if (gl.state != GameLoop.GameState.Playing) return;

        if (EnemyUnit.All.Count == 0 && phase < 2)
        {
            if (EditorApplication.timeSinceStartup - mapAt > 1.6)
            {
                mapAt = EditorApplication.timeSinceStartup;
                mapHops++;
                if (mapHops > 8) { Finish(1, "8 次换图都没有小怪"); return; }
                int map = mapHops == 1 ? gl.field.Map.id : MapCandidates[mapCursor++ % MapCandidates.Length];
                report.Add("换图找怪：" + map);
                gl.field.Enter(map, null);
            }
            return;
        }

        switch (phase)
        {
            case 0:
            {
                var mob = Nearest(gl);
                if (mob == null) { Finish(1, "场上没有小怪"); return; }
                StandNear(gl, mob);
                Prep(gl, sys);
                PinHp();
                phase = 1;
                phaseAt = EditorApplication.timeSinceStartup;
                return;
            }
            case 1:
            {
                // spawn 是异步的，等 0.8s 再贴脸施放（同 SkillSidePlayChecks 的踩坑）。
                if (EditorApplication.timeSinceStartup - phaseAt < .8) return;
                var mob = Nearest(gl);
                if (mob == null) { Finish(1, "准备阶段没有小怪"); return; }
                StandNear(gl, mob);
                Prep(gl, sys);
                PinHp();
                // 逐个试候选主技能：飞镖系会因为测试角色没弹药被拒，换到不吃弹药的六转技能。
                // 每试一个都重新 Prep（上一条失败的施法可能留下动作锁/冷却）。
                while (cursor < Parents.Length)
                {
                    int candidate = Parents[cursor++];
                    string failure;
                    if (!Cast(sys, candidate, out failure))
                    {
                        report.Add("  跳过 " + candidate + "：" + failure);
                        Prep(gl, sys);
                        continue;
                    }
                    parent = candidate;
                    if (!SkillSystem.AddAttackOf(parent, out followup, out linkDelay))
                    {
                        Finish(1, parent + " 在追加攻击表里取不到链路");
                        return;
                    }
                    report.Add("施放 " + parent + " 成功 → 期望追加 " + followup);
                    phase = 2;
                    phaseAt = EditorApplication.timeSinceStartup;
                    return;
                }
                Finish(1, "所有候选主技能都施放失败：" + castError);
                return;
            }
            case 2:
            {
                // batchmode 的 WaitForSeconds 会被节流拉长：第二段协程常要几秒才醒，
                // 6 秒窗口跑两次会出现一次「共 2 段」、一次「共 1 段」的假回归。
                // 链尾命中一到就收工，否则给足 20 秒。
                if (allChains.Count >= 2 && hitIds.Contains(400021022)) { Finish(0, null); return; }
                if (EditorApplication.timeSinceStartup - phaseAt < 20) return;
                Finish(0, null);
                return;
            }
        }
    }

    /// <summary>把小怪血量抬到 10 亿：主技能 10 段会把 8 血测试怪打死，
    /// 0.3 秒后的追加段就「选中=0」，看着像追加段没打中人。
    /// 原生怪的真血量在 `NativeMobController.health`（`Maximum` 是 get-only），
    /// 只写 `EnemyUnit.hp` 没用，必须反射改 backing field。</summary>
    static void PinHp()
    {
        const long Cap = 1000000000L;
        foreach (var e in EnemyUnit.All)
        {
            if (e == null || e.dead) continue;
            if (e.hpMax < Cap) e.hpMax = Cap;
            var health = e.NativeController != null ? HealthField?.GetValue(e.NativeController) : null;
            if (health != null && HealthMaxField?.GetValue(health) is long m && m < Cap)
            {
                HealthMaxField.SetValue(health, Cap);
                HealthCurField?.SetValue(health, Cap);
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
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(SkillSystem);
        type.GetField("actionUntil", F)?.SetValue(sys, 0f);
        type.GetField("actionBlocking", F)?.SetValue(sys, false);
        type.GetField("actionSkillId", F)?.SetValue(sys, 0);
        (type.GetField("states", F)?.GetValue(sys) as System.Collections.IDictionary)?.Clear();
    }

    static bool Cast(SkillSystem sys, int id, out string error)
    {
        error = "";
        try
        {
            if (sys.TryCastSkill(id)) return true;
            error = id + " 施放失败：" + sys.LastFailure;
            return false;
        }
        catch (Exception e)
        {
            error = id + " 异常 " + e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    static void Finish(int code, string fatal)
    {
        // 递归链：一条链的 next 是另一条链的 parent。链头 400021020 必须出现 020→021→022 两段。
        int recursive = 0;
        foreach (var line in allChains)
        {
            int nextId = Number(line, "→ ");
            foreach (var other in allChains)
                if (Number(other, "] ") == nextId) { recursive++; break; }
        }
        bool recursiveExpected = parent == 400021020;
        bool recursiveOk = !recursiveExpected || recursive >= 1;
        // 链尾（没有再往下的那段）也必须真的打出 `[Hit]`：只有 `[AddAttack]` 日志
        // 只能证明协程起来了，不能证明第三段真的打到怪。
        int tailId = 0;
        foreach (var line in allChains)
        {
            int nextId = Number(line, "→ ");
            bool hasNext = false;
            foreach (var other in allChains)
                if (Number(other, "] ") == nextId) { hasNext = true; break; }
            if (!hasNext) tailId = nextId;
        }
        bool tailOk = tailId == 0 || hitIds.Contains(tailId);
        bool chainOk = chainCount >= 1 && followup != 0 && chainLine.Contains(followup.ToString());
        bool hitOk = followupHits >= 1 && followupTargets > 0;
        report.Insert(0, "=== 自动追加攻击（addAttack isAuto=1）");
        report.Insert(1, "  表内自动链：" + SkillSystem.AddAttackCount + " 条");
        report.Insert(2, "  ① [AddAttack] 触发 " + chainCount + " 次" +
                         (chainLine.Length > 0 ? "（" + chainLine.Replace("[AddAttack] ", "") + "）" : "") +
                         "  " + (chainOk ? "✓" : "✗"));
        report.Insert(3, "  ② [Hit] id=" + followup + " 打出 " + followupHits +
                         " 次判定 / 命中 " + followupTargets + " 只怪" +
                         (hitLine.Length > 0 ? "（" + hitLine + "）" : "") + "  " + (hitOk ? "✓" : "✗"));
        report.Insert(4, "  ③ 递归链：共 " + allChains.Count + " 段 / 首尾相接 " + recursive + " 处" +
                         (recursiveExpected ? "（链头 " + parent + " 期望 ≥1）" : "") +
                         "  " + (recursiveOk ? "✓" : "✗"));
        report.Insert(5, "  ④ 链尾 " + tailId + " 打出判定：" + (tailOk ? "✓" : "✗") +
                         "（本轮出现 [Hit] 的技能 " + hitIds.Count + " 个）");
        foreach (var line in allChains) report.Add("     " + line.Replace("[AddAttack] ", ""));
        if (!string.IsNullOrEmpty(castError)) report.Add("  施放错误：" + castError);
        if (!string.IsNullOrEmpty(fatal)) report.Add("  致命错误：" + fatal);
        bool ok = chainOk && hitOk && recursiveOk && tailOk && string.IsNullOrEmpty(fatal);
        report.Add("  结论：" + (ok ? "✓ 主技能结算后自动补出隐藏技能并打出判定" : "✗ 追加段没有生效"));
        string text = string.Join("\n", report.ToArray());
        try { System.IO.File.WriteAllText("/tmp/skill_add_attack.txt", text); } catch { }
        Debug.Log("[AddAttackPlay]\n" + text);
        EditorPrefs.DeleteKey(K_RUN);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
