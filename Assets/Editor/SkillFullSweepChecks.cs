using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

// 全职业技能回归扫测（Play Mode）。
//
// 目的：一次性回答四个问题
//   ① 这个技能能不能按（被动/无定义/被冷却或动作锁挡住）
//   ② 按下之后有没有出特效层
//   ③ 出了伤害体的话有没有真的打中怪（[Hit] 选中>0）
//   ④ 施放过程会不会抛异常
//
// 覆盖集合 = JobTree 的职业技能 ∪ VMatrix 核心连接的技能（去重、去 invisible）。
// 每发之前统一 Prep（补 mpMax/mp、清 actionUntil/states），并贴到最近小怪身前，
// 避免「打不到怪」被站位掩盖；被动的技能单独归类，不算失败。
//
// 命令行（不要 -quit）：
//   Unity -batchmode -projectPath . -executeMethod SkillFullSweepChecks.Run \
//         -sweepFrom 0 -sweepCount 200 -sweepHold 0.18 -logFile /tmp/sweep0.log
//
// 产物：/tmp/skill_sweep.txt（追加写，按批次累加；同名文件先删再跑）
public static class SkillFullSweepChecks
{
    const string K_RUN = "Sweep.running";
    const string K_T0 = "Sweep.t0";
    const string K_FROM = "Sweep.from";
    const string K_COUNT = "Sweep.count";
    const string K_HOLD = "Sweep.hold";

    static readonly int[] MapCandidates = { 40001, 100000000, 100010000, 104040000, 120000000 };

    sealed class Row
    {
        public int id, level;
        public string name, type, kind;
        public bool cast, passive, hit, damageSkill;
        public string fail = "";
        public int fx;
        public readonly List<string> parts = new List<string>();
        public int hitBags, sel, otherHits;
        public long damage;
        public string error = "";
    }

    static readonly List<Row> rows = new List<Row>();
    static readonly List<int> ids = new List<int>();
    static readonly HashSet<string> seenFx = new HashSet<string>();
    static int index, mapCursor, castThis, repop;
    static bool hooked, levelsReady, everFoundMobs, castIssued;
    static double castAt, mapHopAt;
    static float hold = .18f;
    static int pendingHitId;

    // 当前正在测的技能：日志按 id 归因
    static Row current;

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
        int from = 0, count = 200;
        var args = Environment.GetCommandLineArgs();
        // `-sweepIds` 是上一次运行写进 EditorPrefs 的**持久状态**：不清掉的话，
        // 本次只传 -sweepFrom/-sweepCount 也会被那 90 个残留 id 覆盖，
        // 表现是「要 450 个只跑了 90 个」，而且分批结果无法和全库对上。
        EditorPrefs.DeleteKey("Sweep.ids");
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-sweepFrom") int.TryParse(args[i + 1], out from);
            if (args[i] == "-sweepCount") int.TryParse(args[i + 1], out count);
            if (args[i] == "-sweepHold") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out hold);
        }
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-sweepIds")
                foreach (string part in args[i + 1].Split(','))
                    if (int.TryParse(part.Trim(), out int one)) EditorPrefs.SetString("Sweep.ids", EditorPrefs.GetString("Sweep.ids", "") + one + ",");
        if (!string.IsNullOrEmpty(EditorPrefs.GetString("Sweep.ids", ""))) { }
        else EditorPrefs.DeleteKey("Sweep.ids");
        EditorPrefs.SetInt(K_FROM, from);
        EditorPrefs.SetInt(K_COUNT, count);
        EditorPrefs.SetFloat(K_HOLD, hold);
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, Now());
        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("debug_god", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.SetInt("debug_start_level", 0);
        PlayerPrefs.SetInt("debug_hitlog", 1);
        PlayerPrefs.Save();
        rows.Clear(); ids.Clear();
        index = 0; mapCursor = 0; hooked = false; levelsReady = false; everFoundMobs = false;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        Debug.Log("[Sweep] 准备进入 Play from=" + from + " count=" + count);
        EditorApplication.EnterPlaymode();
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (current == null || string.IsNullOrEmpty(condition)) return;
        if (condition.StartsWith("[FxPlay]", StringComparison.Ordinal))
        {
            int i = condition.IndexOf("part=", StringComparison.Ordinal);
            if (i >= 0)
            {
                string part = condition.Substring(i + 5).Trim();
                if (seenFx.Add(part)) { current.fx++; if (current.parts.Count < 6) current.parts.Add(part); }
            }
            return;
        }
        if (condition.StartsWith("[Hit]", StringComparison.Ordinal))
        {
            int i = condition.IndexOf("id=", StringComparison.Ordinal);
            if (i < 0) return;
            string id = ""; i += 3;
            while (i < condition.Length && char.IsDigit(condition[i])) id += condition[i++];
            int.TryParse(id, out int hitId);
            int sel = Number(condition, "选中=");
            int landed = Number(condition, "命中=");
            long dealt = Number(condition, "总伤害=");
            // 「打到怪」以 选中>0 为准：测试角色攻击力只有 0.02，怪又有防御，
            // 命中数与伤害都可能是 0，但那一发确实框到了目标（Impact/hit 层也照常播）。
            if (sel <= 0 && landed <= 0) return;
            if (hitId == current.id) { current.sel += sel; current.hitBags += landed; current.hit = true; }
            else { current.otherHits++; }
            current.damage += dealt;
            return;
        }
    }

    static int Number(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return 0;
        i += key.Length;
        int n = 0;
        while (i < text.Length && char.IsDigit(text[i])) n = n * 10 + (text[i++] - '0');
        return n;
    }

    static string Now() => EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);
    static double Elapsed() =>
        EditorApplication.timeSinceStartup -
        (double.TryParse(EditorPrefs.GetString(K_T0, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0);

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        if (!EditorApplication.isPlaying)
        {
            if (Elapsed() > 45) { Debug.LogError("[Sweep] 未进入播放模式"); Finish(1); }
            return;
        }
        Application.runInBackground = true;
        if (hooked == false) { hooked = true; Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog; }
        if (Elapsed() > 2400) { Debug.LogError("[Sweep] 超时"); Finish(1); return; }
        var gl = GameLoop.I;
        if (gl == null || gl.player == null || gl.field == null || gl.field.Map == null) return;
        if (Elapsed() < 1.5) return;
        var sys = gl.player.GetComponent<SkillSystem>();
        if (sys == null) { Debug.LogError("[Sweep] 玩家没有 SkillSystem"); Finish(1); return; }

        if (!levelsReady) { PrepareSkills(gl, sys); levelsReady = true; }
        if (EnemyUnit.All.Count == 0)
        {
            // 怪被清空：重进当前地图刷怪。**不能只是 return** —— 那样整个批次会静默停住，
            // 看起来像「探针卡死」，而且后面所有技能都会记成 0 命中（假阴性）。
            if (EditorApplication.timeSinceStartup - mapHopAt > 1.2 && repop < 6)
            {
                mapHopAt = EditorApplication.timeSinceStartup;
                repop++;
                gl.field.Enter(gl.field.Map.id, null);
            }
            else if (repop >= 6 && EditorApplication.timeSinceStartup - mapHopAt > 1.2)
            {
                repop = 0; mapHopAt = EditorApplication.timeSinceStartup;
                if (mapCursor < MapCandidates.Length) gl.field.Enter(MapCandidates[mapCursor++], null);
            }
            if (EditorApplication.timeSinceStartup - mapHopAt < 1.2) return;
            if (EnemyUnit.All.Count == 0) return;
        }
        everFoundMobs = true;

        if (index >= ids.Count) { Finish(0); return; }

        if (!castIssued)
        {
            current = rows[index];
            Prep(gl, sys);
            StandNearMob(gl);
            bool ok;
            try { ok = sys.TryCastSkill(current.id); }
            catch (Exception e) { ok = false; current.error = e.GetType().Name; }
            current.cast = ok;
            if (!ok) current.fail = sys.LastFailure ?? "";
            castIssued = true;
            castAt = EditorApplication.timeSinceStartup;
            return;
        }

        if (EditorApplication.timeSinceStartup - castAt < EditorPrefs.GetFloat(K_HOLD, .18f)) return;
        castIssued = false;
        index++;
        if (index % 50 == 0) Debug.Log("[Sweep] 进度 " + index + "/" + rows.Count);
    }

    // 把 JobTree 的技能与 V 核心技能一起装到满级：等级读的是存档，直接写 Profile.Data
    // 不走 SkillProgress.SetLevel（它会每个技能存一次盘，1000 个技能会很慢）。
    static void PrepareSkills(GameLoop gl, SkillSystem sys)
    {
        rows.Clear(); ids.Clear();
        foreach (int id in JobTreeIds()) ids.Add(id);
        foreach (int id in CoreSkillIds()) if (!ids.Contains(id)) ids.Add(id);
        ids.Sort();

        string explicitIds = EditorPrefs.GetString("Sweep.ids", "");
        var slice = new List<int>();
        if (!string.IsNullOrEmpty(explicitIds))
        {
            foreach (string part in explicitIds.Split(','))
                if (int.TryParse(part.Trim(), out int one) && !slice.Contains(one)) slice.Add(one);
        }
        else
        {
            int from = EditorPrefs.GetInt(K_FROM, 0);
            int count = Mathf.Max(1, EditorPrefs.GetInt(K_COUNT, 200));
            if (from < 0) from = 0;
            for (int i = from; i < Mathf.Min(ids.Count, from + count); i++) slice.Add(ids[i]);
        }
        ids.Clear(); ids.AddRange(slice);

        var data = Profile.Data;
        foreach (int id in ids)
        {
            var d = SkillBook.Get(id);
            var row = new Row { id = id };
            if (d == null)
            {
                row.name = "(无定义)"; row.type = "-"; row.kind = "-";
                rows.Add(row); continue;
            }
            row.name = d.name;
            row.type = d.type;
            row.kind = d.type;
            int cap = Mathf.Max(1, d.maxLevel);
            // 五转核心技能等级由 VMatrixLevels 供，写存档无效；这里只写职业技能。
            if (!VMatrixLevels.TryGet(id, out _))
            {
                try { data.SetSkillLevel(id, cap); } catch { }
            }
            row.level = SkillProgress.LevelOf(id);
            row.passive = d.IsPassive;
            row.damageSkill = d.type == "attack";
            rows.Add(row);
        }
        try { Profile.Save(); } catch { }
        Debug.Log("[Sweep] 本批 " + rows.Count + " 个技能（全库 " + (JobTreeIds().Count + CoreSkillIds().Count) + "）");
    }

    static List<int> jobIds, coreIds;
    // 职业技能取自 UI/JobTree.json 的 skills 表（面板/加点用的是同一份），
    // 跳过 invisible=1 的内部技能（子状态、包装技能，玩家按不到也不该按）。
    static List<int> JobTreeIds()
    {
        if (jobIds != null) return jobIds;
        jobIds = new List<int>();
        var asset = Resources.Load<TextAsset>("UI/JobTree");
        if (asset == null) { Debug.LogError("[Sweep] 缺少 UI/JobTree.json"); return jobIds; }
        if (!(MiniJson.Parse(asset.text) is Dictionary<string, object> root)) return jobIds;
        if (!root.TryGetValue("skills", out var raw) || !(raw is Dictionary<string, object> table)) return jobIds;
        foreach (var pair in table)
        {
            if (!int.TryParse(pair.Key, out int id) || id <= 0) continue;
            if (pair.Value is Dictionary<string, object> row && Art.Int(row, "invisible") != 0) continue;
            jobIds.Add(id);
        }
        return jobIds;
    }
    static List<int> CoreSkillIds()
    {
        if (coreIds != null) return coreIds;
        coreIds = new List<int>();
        VMatrixCatalog.Ensure();
        foreach (var core in VMatrixCatalog.AllCores)
        {
            if (core.skills == null) continue;
            foreach (int id in core.skills) if (!coreIds.Contains(id)) coreIds.Add(id);
        }
        return coreIds;
    }

    static void Prep(GameLoop gl, SkillSystem sys)
    {
        gl.player.mpMax = Mathf.Max(gl.player.mpMax, 999999f);
        gl.player.mp = gl.player.mpMax;
        // HP 消耗技能（`common/hpCon` → `NativeSkillDefinition.HpCost`）会一发一发把血扣到 0，
        // 之后 `BeginCast` 开头 `p.hp <= 0` 把后面所有技能全拒成「当前无法释放技能」。
        // 只补 MP 的旧版在 1200-1343 这一批出现 23 条（重跑 105 条）假拒绝，越往后越多。
        gl.player.hpMax = Mathf.Max(gl.player.hpMax, 999999f);
        gl.player.hp = gl.player.hpMax;
        gl.player.attackDamage = 0.02f;
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(SkillSystem);
        type.GetField("actionUntil", F)?.SetValue(sys, 0f);
        type.GetField("actionBlocking", F)?.SetValue(sys, false);
        type.GetField("actionSkillId", F)?.SetValue(sys, 0);
        if (type.GetField("states", F)?.GetValue(sys) is System.Collections.IDictionary dict) dict.Clear();
        foreach (var fx in UnityEngine.Object.FindObjectsOfType<SkillFx>()) if (fx != null) fx.Stop();
        seenFx.Clear();
    }

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

    static void Finish(int code)
    {
        var lines = new List<string>();
        int ok = 0, passive = 0, failCast = 0, attackNoHit = 0, error = 0;
        foreach (var r in rows)
        {
            if (r.passive) passive++;
            else if (!r.cast) failCast++;
            if (r.error.Length > 0) error++;
            bool bad = !r.passive && (!r.cast || (r.damageSkill && r.sel == 0 && r.otherHits == 0));
            if (!bad) ok++;
            if (!r.passive && r.cast && r.damageSkill && r.sel == 0 && r.otherHits == 0) attackNoHit++;
            lines.Add(string.Join("\t", new[]
            {
                r.id.ToString(), r.name ?? "", r.type ?? "", r.level.ToString(),
                r.passive ? "passive" : (r.cast ? "cast" : "reject:" + r.fail),
                r.error,
                "fx=" + r.fx + (r.parts.Count > 0 ? "(" + string.Join("/", r.parts.ToArray()) + ")" : ""),
                "sel=" + r.sel + " landed=" + r.hitBags + " other=" + r.otherHits + " dmg=" + r.damage,
            }));
        }
        string head = "# 本批 " + rows.Count + " 个：正常 " + ok + "，被动 " + passive +
                      "，按不动 " + failCast + "，攻击型零命中 " + attackNoHit + "，异常 " + error;
        string text = head + "\n" + string.Join("\n", lines.ToArray());
        try { System.IO.File.WriteAllText("/tmp/skill_sweep_" + EditorPrefs.GetInt(K_FROM, 0) + ".txt", text); } catch { }
        Debug.Log("[Sweep]\n" + head);
        EditorPrefs.DeleteKey(K_RUN);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(code);
    }
}
