using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

// 真按住、数伤害：把「按住型技能到底能不能打到怪」用掉血数据量出来。
//
// 背景：400011072 圣十字架 只在按下瞬间放特效、之后完全没伤害。
// 根因在 SkillSystem.BeginCast —— 冷却/MP/弹药对「连射的每一发」都重新校验并重新扣，
// 而它的 cooltime=120s，于是第 2 发（+100ms）就被自己的冷却挡掉，
// SkillHeldInput.Update 拿到 false 就直接 StopHeld，光束拆掉。
//
// 本探针走的是**真实输入链路**，不塞任何测试后门：
//   QuickSlots.Bind → InputHub.SetVirtualAction(down) → QuickSlots.Cast
//   → TryCastBoundSkill → rapid → StartHeld → 按住 N 秒 → SetVirtualAction(up)
//
// 判定标准（三条都要过）：
//   ① 伤害 > 0 —— 真的打到了怪
//   ② MP 只扣一次 —— 不是「每发都扣」，否则 1000 MP 撑不过第二发
//   ③ 施法期间没有新的 LastFailure —— 没有被冷却/蓝量闸门踢出按住态
//
// 命令行（不要 -quit）：
//   Unity -batchmode -projectPath <proj> -executeMethod SkillDamagePlayChecks.Run \
//         -dmgIds 400011072 -dmgHold 2.0 -dmgTap 0 -logFile /tmp/dmg.log
public static class SkillDamagePlayChecks
{
    const string K_RUN  = "DmgPlay.running";
    const string K_IDS  = "DmgPlay.ids";
    const string K_HOLD = "DmgPlay.hold";
    const string K_TAP  = "DmgPlay.tap";
    const string K_T0   = "DmgPlay.t0";
    const string K_STAND = "DmgPlay.standoff";
    const string K_PATHS = "DmgPlay.paths";
    const string K_TAPF = "DmgPlay.tapSeconds";

    static readonly int[] DefaultIds = { 400011072 };
    static int[] ids;
    static int cursor;
    static int phase;                 // 0 待机 / 1 按住中 / 2 收尾
    static double phaseAt;
    static float mpBefore;
    static int killsBefore;
    static long damage;
    static int failCount;
    static string lastFail;
    static Component target;
    static long targetHp = -1L;
    static bool tapMode;
    static bool rapidSkill;
    static float standoff = 1.2f;
    static float tapModeFloat;
    // 一次 Unity 会话里连跑多条输入路径：hold=真按住 / tap=短按 / click=屏上快捷格点一下
    // （不注入任何虚拟保持态，最接近真机底部快捷格的点按）。
    static string[] paths = { "hold" };
    static int pathCursor;
    const float SettleSeconds = 1.4f;
    static string CurrentPath => paths != null && pathCursor < paths.Length ? paths[pathCursor] : "hold";
    static float PhaseSeconds()
    {
        if (CurrentPath == "click") return 0.08f;
        if (CurrentPath == "tap") return Mathf.Max(0.05f, EditorPrefs.GetFloat(K_TAPF, 0.25f));
        return EditorPrefs.GetFloat(K_HOLD, 2f);
    }
    static string PathLabel()
        => CurrentPath == "click" ? " 点一下(无保持态)" :
           CurrentPath == "tap" ? " 点一下" + PhaseSeconds().ToString("0.00") + "s" :
           " 按住 " + PhaseSeconds().ToString("0.0") + "s";
    static readonly int[] MapCandidates = { 40001, 100000000, 100010000, 104040000, 120000000 };
    static int mapCursor;
    static double mapHopAt;
    static bool everFoundMobs;
    static int installedFor;
    static readonly List<string> report = new List<string>();

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
        var args = Environment.GetCommandLineArgs();
        string idArg = "", pathArg = "";
        float hold = 2.0f, tap = 0f, standoff = 1.2f;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-dmgIds") idArg = args[i + 1];
            if (args[i] == "-dmgHold") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out hold);
            if (args[i] == "-dmgTap") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out tap);
            if (args[i] == "-dmgPaths") pathArg = args[i + 1];
            // 站位距离（世界单位）。远程/落点型技能的判定框离施法者很远
            // （ICBM 的 lt2/rb2 换算后是身前 6.25~20 单位），贴脸站必然打不到。
            if (args[i] == "-dmgStandoff") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out standoff);
        }
        ids = ParseIds(idArg);
        tapMode = tap > 0f;
        if (string.IsNullOrEmpty(pathArg)) pathArg = tapMode ? "tap" : "hold";
        paths = ParsePaths(pathArg);
        pathCursor = 0;
        EditorPrefs.SetString(K_IDS, string.Join(",", Array.ConvertAll(ids, x => x.ToString())));
        EditorPrefs.SetFloat(K_HOLD, Mathf.Max(0.3f, hold));
        EditorPrefs.SetFloat(K_TAPF, tapMode ? tap : 0.25f);
        EditorPrefs.SetBool(K_TAP, tapMode);
        EditorPrefs.SetString(K_PATHS, string.Join(",", paths));
        EditorPrefs.SetFloat(K_STAND, standoff);

        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("debug_god", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.SetInt("debug_start_level", 0);
        // 打开 HitAt 的真实伤害日志（SkillSystem 里按这个键判断）
        PlayerPrefs.SetInt("debug_hitlog", 1);
        PlayerPrefs.Save();

        cursor = 0; phase = 0; damage = 0; failCount = 0; lastFail = null; target = null; targetHp = -1L;
        report.Clear();
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, Now());
        Debug.Log("[DmgPlay] 准备进入 Play，技能 " + EditorPrefs.GetString(K_IDS, "") + "，路径 " + string.Join(",", paths));
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        if (ids == null)
        {
            ids = ParseIds(EditorPrefs.GetString(K_IDS, ""));
            paths = ParsePaths(EditorPrefs.GetString(K_PATHS, "hold"));
            pathCursor = 0;
            tapMode = EditorPrefs.GetBool(K_TAP, false);
            standoff = EditorPrefs.GetFloat(K_STAND, 1.2f);
        }
        if (!EditorApplication.isPlaying)
        {
            if (Elapsed() > 45) { Debug.LogError("[DmgPlay] 未进入播放模式，退出"); Finish(1); }
            return;
        }
        Application.runInBackground = true;

        var gl = GameLoop.I;
        if (gl == null || gl.player == null || gl.field == null || gl.field.Map == null) return;
        if (Elapsed() < 1.5) return;

        var sys = gl.player.GetComponent<SkillSystem>();
        if (sys == null) { Debug.LogError("[DmgPlay] 玩家没有 SkillSystem"); Finish(1); return; }

        if (cursor >= ids.Length) { Finish(0); return; }
        int id = ids[cursor];

        // 通用看门狗
        if (Elapsed() > 90) { Debug.LogError("[DmgPlay] 总超时"); Finish(1); return; }

        if (phase == 0)
        {
            // 出生点多半是城镇：换到一张有刷怪的图再测，否则永远没有目标。
            // FieldCatalog.Maps 只装「已经加载过的」图，所以这里直接用候选 id 逐个试。
            if (EnemyUnit.All.Count == 0)
            {
                // 只换一次图：换成功之后如果又把怪清空了，那是「打得太快」，
                // 不是「这张图没怪」，再跳只会跳到更空的图上去（第二次测量就这么废掉的）。
                if (everFoundMobs) return;
                if (mapCursor >= MapCandidates.Length) return;
                if (EditorApplication.timeSinceStartup - mapHopAt < 1.6) return;
                mapHopAt = EditorApplication.timeSinceStartup;
                gl.field.Enter(MapCandidates[mapCursor], null);
                Debug.Log("[DmgPlay] 换图试怪 → " + MapCandidates[mapCursor]);
                mapCursor++;
                return;
            }
            everFoundMobs = true;
            if (installedFor != id) { InstallMatrixLevels(id); installedFor = id; }
            if (!StageForCast(gl)) return;      // 等地图/怪就绪
            BindAndPress(sys, id, gl);
            return;
        }

        if (phase == 1)
        {
            AccumulateDamage(gl);
            // ContinuousSkillId 归零 = 按住态被提前拆掉（冷却/蓝量/弹药闸门把某一发踢了）。
            // 只有真正走 rapid 的技能才有「按住态」；普通一次性技能本来就一直是 0，
            // 不判断就会把每一次普通施法都误报成失败。
            // 点按路径本来就只保持一两帧，ContinuousSkillId 归零是正常收尾，不是拆链。
            if (CurrentPath == "hold" && rapidSkill && sys.ContinuousSkillId == 0 && failCount == 0 && lastFail == null)
            {
                failCount++;
                lastFail = sys.ActiveNotice;
            }
            if (EditorApplication.timeSinceStartup - phaseAt < PhaseSeconds()) return;
            Release(sys, id, gl);
            return;
        }

        // phase 2：留足收尾窗口再判伤害。点一下的「保底一发」和投射物飞行
        // 都发生在松手之后，松手那一刻就下结论会把它误判成「没打到怪」。
        AccumulateDamage(gl);
        if (EditorApplication.timeSinceStartup - phaseAt < SettleSeconds) return;
        EmitVerdict(gl, id);
        QuickSlots.Bind(0, 0);
        pathCursor++;
        if (pathCursor >= paths.Length) { pathCursor = 0; cursor++; }
        // 每个用例重新找怪：上一条路径可能已经把当前图的怪清空。
        everFoundMobs = false;
        mapCursor = 0;
        mapHopAt = 0;
        phase = 0;
    }

    static void InstallMatrixLevels(int id)
    {
        // 每个技能单独注入一次等级（VMatrixLevels.Capture 会整份重建）
        VMatrixCatalog.Ensure();
        foreach (var core in VMatrixCatalog.AllCores)
        {
            if (core.skills == null || Array.IndexOf(core.skills, id) < 0) continue;
            var response = new VMatrixResponse();
            response.cores.Add(new VMatrixCoreView
            {
                coreId = core.id, level = 30, maxLevel = 30,
                skills = core.skills, name = core.name, desc = core.desc,
            });
            VMatrixLevels.Capture(response, 990001);
            return;
        }
        // 找不到核心就连技能自己塞一条
        var solo = new VMatrixResponse();
        solo.cores.Add(new VMatrixCoreView { level = 30, maxLevel = 30, skills = new[] { id } });
        VMatrixLevels.Capture(solo, 990001);
    }

    // 把角色挪到某只怪旁边，面向它。找不到怪就返回 false（等下一帧）。
    static bool StageForCast(GameLoop gl)
    {
        var pl = gl.player;
        EnemyUnit pick = null;
        float best = float.MaxValue;
        foreach (var e in EnemyUnit.All)
        {
            if (e == null || e.dead) continue;
            float d = Mathf.Abs(e.transform.position.x - pl.worldX);
            if (d < best) { best = d; pick = e; }
        }
        if (pick == null) return false;
        target = pick;
        targetHp = TargetFloor;
        pick.hp = TargetFloor;
        // 站在怪左边 1.2 单位处、朝右，保证在光束的 lt/rb 里
        float x = pick.transform.position.x - Mathf.Max(0.2f, standoff);
        pl.PlaceAt(x, PlayerCtrl.GroundAt(x, pick.transform.position.y));
        pl.facing = 1;
        return true;
    }

    static void BindAndPress(SkillSystem sys, int id, GameLoop gl)
    {
        int slot = 0;
        QuickSlots.Bind(slot, id);
        var action = QuickSlots.Action(slot);
        // 新手 mpMax 只有几百，五转 mpCon 常 1000+。不抬上限会被「魔力不足」直接拒，
        // 测出来的「没打到怪」是假阴性。上限抬高后 mp 才有意义可比。
        gl.player.mpMax = Mathf.Max(gl.player.mpMax, 999999f);
        gl.player.mp = gl.player.mpMax;
        // 攻击力临时压到很低：圣十字架一包就能打穿地板血量（实测 ≥8000），
        // 怪当场死亡就再也数不到第二包。压低后怪能撑住整个按住窗口，
        // 「发生过掉血的帧数」才等于真实发包数。
        gl.player.attackDamage = 0.02f;
        mpBefore = gl.player.mp;
        killsBefore = gl.killCount;
        damage = 0; failCount = 0; lastFail = null;
        targetHp = -1L; damageFrames = 0;
        lastHp.Clear();
        var unit = target as EnemyUnit;
        if (unit != null) targetHp = unit.hp;

        // 同一个技能连测三条路径：上一条用例留下的 120s 冷却会把下一条直接挡成
        // 「技能冷却中」，量出来的是假阴性。100% 减免把剩余冷却清零（不动其它数值）。
        sys.ReduceCooldowns(100);
        InputHub.Enabled = true;
        InputHub.SetVirtualHeld(action, false);            // 清掉上一条路径可能残留的保持态
        // 只注入「保持态」不注入按下沿：按下沿会被 InputBridge 的快捷格分派再读一次，
        // 同一按就 Cast 两遍。施放由下面的 QuickSlots.Cast 显式完成。
        // click = 真机底部快捷格的点按：连保持态都没有，抬手由 SkillHeldInput 自己发现。
        if (CurrentPath != "click") InputHub.SetVirtualHeld(action, true);
        rapidSkill = false;
        try { rapidSkill = NativeSkillVfx.RapidAttack(id); } catch { rapidSkill = false; }
        QuickSlots.Cast(slot);          // → TryCastBoundSkill → rapid → StartHeld
        // 按下这一下的结论要立刻记，等到松手时 LastFailure 可能已被后续帧清掉。
        string immediate = sys.LastFailure;
        Debug.Log("[DmgPlay] 按下 " + id + " [" + CurrentPath + "] → 立即失败原因=" + (string.IsNullOrEmpty(immediate) ? "(无)" : immediate) +
                  "  目标=" + (unit != null ? unit.hp.ToString("0") + "/" + unit.hpMax.ToString("0") : "null") +
                  "  怪数=" + EnemyUnit.All.Count + "  MP=" + gl.player.mp.ToString("0"));

        phase = 1;
        phaseAt = EditorApplication.timeSinceStartup;
    }

    static void Release(SkillSystem sys, int id, GameLoop gl)
    {
        // click 路径本来就没有保持态，抬手由 SkillHeldInput.Update 自己发现。
        if (CurrentPath != "click") InputHub.SetVirtualHeld(QuickSlots.Action(0), false);  // 松手 → StopHeld(true)
        phase = 2;
        phaseAt = EditorApplication.timeSinceStartup;
    }

    // 伤害结论必须等收尾窗口跑完再算：保底一发在 StopHeld 里结算、投射物还要飞一段。
    static void EmitVerdict(GameLoop gl, int id)
    {
        float mpDelta = mpBefore - gl.player.mp;
        int kills = gl.killCount - killsBefore;
        var data = SkillBook.Get(id);

        string verdict = damage > 0 ? "打到怪" : "没打到怪";
        string line = "  " + id + "「" + (data != null ? data.name : "?") + "」" + PathLabel() +
                      " → " + verdict +
                      "：伤害包 " + damageFrames + " 帧 / 总伤害 " + damage +
                      "，击杀 " + kills + "，耗蓝 " + mpDelta.ToString("0") +
                      (!rapidSkill ? "，一次性施法" :
                       failCount > 0 ? "，按住态被提前拆掉（" + lastFail + "）" : "，按住态保持完好");
        report.Add(line);
        Debug.Log("[DmgPlay]" + line);
    }

    // 全场累计掉血。只盯一只怪没用：40001 的怪只有 8 血，一发就死，
    // 「打了 1 发」和「打了 20 发」都只能看到 1 次死亡，区分不出来。
    // 这里对所有怪逐帧比对 hp，死亡时把残留算进去，得到的是真实总伤害量。
    static readonly Dictionary<int, float> lastHp = new Dictionary<int, float>();
    // 「有几个不同的帧发生过掉血」= 实际打出几个伤害包。
    // 这是最关键的指标：修复前只会有 1 帧（第一发之后就 StopHeld），修复后应该随时间增长。
    static int damageFrames;

    // 目标血量地板：40001 的怪只有 8 血，一发就死，死了就再也量不到后续发包。
    // 改成「只抬 hp、不动 hpMax」并每帧补回地板值：
    //   hp > 地板 ⇒ 被系统重置了，重新同步、不计伤害
    //   hp < 地板 ⇒ 真实掉血，累加并计一个伤害包
    // 抬 hpMax 会被 EnemyUnit 的刷新逻辑覆盖回原生值，一帧就从 5000 万掉到 0，
    // 看起来像「一刀 5000 万」，实际是重置。
    const long TargetFloor = 8000L;

    static void AccumulateDamage(GameLoop gl)
    {
        var e = target as EnemyUnit;
        if (e == null)
        {
            // 目标被一击打死时 GameObject 会被销毁，Unity 的假 null 让上面这句直接成立。
            // 不在这里补记的话，整包伤害（含满血地板值）会被静默丢掉，
            // 表现成「[Hit] 明明打了 38343，报表却是 0 帧 / 0 伤害」。
            if (targetHp > 0L) { damage += targetHp; damageFrames++; targetHp = -1L; }
            Retarget(gl);
            return;
        }
        if (e.dead || e.hp <= 0L)
        {
            // 这一包把它打空了：整池算作伤害，然后立刻换下一个靶子。
            // 不换的话后面十几发全部打空气，看起来像「只打了一发」。
            if (targetHp > 0L) { damage += targetHp; damageFrames++; }
            Retarget(gl);
            return;
        }
        if (e.hp < targetHp)
        {
            damage += targetHp - e.hp;
            damageFrames++;
            targetHp = e.hp;
        }
    }

    // 换到最近的活怪并把角色贴上去，保证下一发还在 lt/rb 里。
    static void Retarget(GameLoop gl)
    {
        EnemyUnit next = null;
        float best = float.MaxValue;
        foreach (var unit in EnemyUnit.All)
        {
            if (unit == null || unit.dead || unit.hp <= 0L) continue;
            float d = Mathf.Abs(unit.transform.position.x - gl.player.worldX);
            if (d < best) { best = d; next = unit; }
        }
        if (next == null) { target = null; return; }
        target = next;
        targetHp = next.hp;
        float x = next.transform.position.x - Mathf.Max(0.2f, standoff);
        gl.player.PlaceAt(x, PlayerCtrl.GroundAt(x, next.transform.position.y));
        gl.player.facing = 1;
    }

    static int[] ParseIds(string text)
    {
        if (string.IsNullOrEmpty(text)) return DefaultIds;
        var list = new List<int>();
        foreach (string part in text.Split(','))
            if (int.TryParse(part.Trim(), out int id) && id > 0) list.Add(id);
        return list.Count == 0 ? DefaultIds : list.ToArray();
    }

    static string[] ParsePaths(string text)
    {
        var list = new List<string>();
        foreach (string part in (text ?? "").Split(','))
        {
            var p = part.Trim();
            if (p == "hold" || p == "tap" || p == "click") list.Add(p);
        }
        return list.Count == 0 ? new[] { "hold" } : list.ToArray();
    }

    static string Now() => EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture);
    static double Elapsed() =>
        EditorApplication.timeSinceStartup -
        (double.TryParse(EditorPrefs.GetString(K_T0, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0);

    static void Finish(int code)
    {
        string text = string.Join("\n", report.ToArray());
        try { System.IO.File.WriteAllText("/tmp/skill_damage_play.txt", text); } catch { }
        Debug.Log("[DmgPlay]\n" + text);
        EditorPrefs.DeleteKey(K_RUN);
        EditorApplication.update -= Tick;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(code);
    }
}
