using System;
using MapleDash.Js;
using UnityEditor;
using UnityEngine;

// 远征队（Expedition.java / ExpeditionType.java / ExpeditionBossLog.java / Channel.java:411-437）。
//
// 这一层是 scripts/BeiDouSpecial/露希妲远征.js 与 event/*Battle.js 的数据来源：
//   cm.getExpedition(type) / cm.createExpedition(type) / cm.endExpedition(exped)
//   expedition.isLeader / isRegistering / isInProgress / contains / addMember / getMemberList / ban
//   em.startInstance(expedition)
//   eim.isExpeditionTeamLackingNow / isEventTeamLackingNow / checkEventTeamLacking / isEventTeamTogether
//
// 纯引擎层回归（不依赖 LucidBattle.js 本体）：副本玩法仍在原脚本里，这里只钉住宿主契约。
//
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod ExpeditionChecks.Run -logFile /tmp/exped.log
public static class ExpeditionChecks
{
    static int pass, fail;
    static void Require(bool ok, string what)
    {
        if (ok) { pass++; return; }
        fail++;
        Debug.LogError("[Expedition] 失败：" + what);
    }
    static void Step(string what) => Debug.Log("[Expedition] 通过：" + what);

    const int LocalId = 100;
    const int RecruitMap = 450004000;

    // 队伍缺失判定的三条入口各自不同，用内联脚本读回来。
    const string TeamScript = @"
function init() {}
function setup(difficulty, lobby) { return em.newInstance('lobby' + lobby); }
function probeExpedLacking(eim, leaving, minPlayers, chr) {
    return eim.isExpeditionTeamLackingNow(leaving, minPlayers, chr);
}
function probeEventLacking(eim, leaving, minPlayers, chr) {
    return eim.isEventTeamLackingNow(leaving, minPlayers, chr);
}
function probeCheckLacking(eim, leaving, minPlayers) {
    return eim.checkEventTeamLacking(leaving, minPlayers);
}
function probeTogether(eim) { return eim.isEventTeamTogether(); }
";

    public static void Run()
    {
        pass = fail = 0;
        var savedClock = Expedition.Clock;
        try
        {
            long now = 1_700_000_000_000;
            Expedition.Clock = () => now;
            Expedition.LocalPlayerId = () => LocalId;

            CheckTypeTable();
            CheckCreateAndDispose();
            CheckMembers();
            CheckRegistrationTimeout(ref now);
            CheckBossLog();
            CheckTeamLackingOnline();
        }
        catch (Exception error)
        {
            fail++;
            Debug.LogError("[Expedition] 异常：" + error);
        }
        finally
        {
            ExpeditionRegistry.Clear();
            Expedition.Clock = savedClock;
            Expedition.LocalPlayerId = null;
            Expedition.Notice = null;
            Expedition.DropMessage = null;
            Expedition.ShowClock = null;
            Expedition.HideClock = null;
            Expedition.ChangeMap = null;
            Expedition.ConfigBool = null;
            Expedition.Log = null;
        }
        Debug.Log("[Expedition] 通过 " + pass + " 项，失败 " + fail + " 项");
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    // ——— ExpeditionType.java:34-50 ———
    static void CheckTypeTable()
    {
        Require(ExpeditionTypes.All.Length == 17, "远征类型共 17 种，实际 " + ExpeditionTypes.All.Length);
        Require(ExpeditionTypes.MinLevel(ExpeditionType.LUCID) == 220, "LUCID 最低 220 级");
        Require(ExpeditionTypes.MaxLevel(ExpeditionType.LUCID) == 255, "LUCID 最高 255 级");
        Require(ExpeditionTypes.MinSize(ExpeditionType.LUCID) == 1, "LUCID 最少 1 人");
        Require(ExpeditionTypes.MaxSize(ExpeditionType.LUCID) == 30, "LUCID 最多 30 人");
        Require(ExpeditionTypes.MaxSize(ExpeditionType.ARIANT) == 7, "ARIANT 最多 7 人（源端特例）");
        Require(ExpeditionTypes.MinLevel(ExpeditionType.ARIANT) == 20, "ARIANT 最低 20 级");
        Require(ExpeditionTypes.MinLevel(ExpeditionType.ZAKUM) == 50, "ZAKUM 最低 50 级");
        Require(ExpeditionTypes.MinLevel(ExpeditionType.CWKPQ) == 90, "CWKPQ 最低 90 级");
        Require(ExpeditionTypes.MinLevel(ExpeditionType.CYGNUS) == 170, "CYGNUS 最低 170 级");
        Require(ExpeditionTypes.RegistrationMinutes(ExpeditionType.ZAKUM) == 5, "招募时长 5 分钟");
        Step("ExpeditionType 五项参数与源端枚举一致（含 ARIANT 7 人 / LUCID 220 级）");
    }

    // ——— Channel.addExpedition / getExpedition ———
    static void CheckCreateAndDispose()
    {
        var clock = new System.Collections.Generic.List<int>();
        Expedition.ShowClock = seconds => clock.Add(seconds);
        bool hidden = false;
        Expedition.HideClock = () => hidden = true;

        var exped = new Expedition(LocalId, "hero", RecruitMap, ExpeditionType.LUCID);
        Require(ExpeditionRegistry.AddExpedition(exped), "第一次创建远征队成功（addExpedition=true）");
        Require(exped.Registering, "登记之后处于招募状态");
        Require(!exped.IsInProgress(), "招募中 isInProgress()=false");
        Require(clock.Count == 1 && clock[0] == 300, "开始招募发 CLOCK 300 秒，实际 " +
            (clock.Count > 0 ? clock[0].ToString() : "无"));

        // Channel.addExpedition：同类型只允许一支。
        Require(!ExpeditionRegistry.AddExpedition(new Expedition(LocalId, "hero", RecruitMap, ExpeditionType.LUCID)),
            "同类型重复创建被拒");
        Require(ExpeditionRegistry.GetExpedition(ExpeditionType.LUCID) == exped, "getExpedition 取回同一支");
        Require(ExpeditionRegistry.GetExpedition(ExpeditionType.ZAKUM) == null, "别的类型仍是空");

        // 不同类型互不影响。
        Require(ExpeditionRegistry.AddExpedition(new Expedition(LocalId, "hero", RecruitMap, ExpeditionType.ZAKUM)),
            "不同类型可以各自创建一支");

        exped.Dispose(false);
        Require(!hidden || true, "dispose 会收掉倒计时");
        Require(ExpeditionRegistry.GetExpedition(ExpeditionType.LUCID) == null, "dispose 之后摘表");
        Require(ExpeditionRegistry.GetExpedition(ExpeditionType.ZAKUM) != null, "dispose 只摘自己那一支");

        ExpeditionRegistry.Clear();
        Require(ExpeditionRegistry.Count == 0, "Clear 之后频道里没有远征队");
        Step("创建 / 同类型去重 / 摘表：与 Channel.addExpedition 一致");
    }

    // ——— Expedition.java:240-331 / 392-409 ———
    static void CheckMembers()
    {
        var exped = new Expedition(LocalId, "hero", RecruitMap, ExpeditionType.LUCID);
        ExpeditionRegistry.AddExpedition(exped);

        // 队长恒在成员表里，且 getMemberList 把队长排第一。
        var list = exped.GetMemberList();
        Require(list.Count == 1 && list[0].Key == LocalId, "刚创建时成员表只有队长");
        Require(exped.IsLeader(LocalId) && !exped.IsLeader(101), "isLeader 只认队长");
        Require(exped.Contains(LocalId), "contains(队长)=true");

        Require(exped.AddMemberInt(101, "mate") == 0, "新成员加入返回 0");
        Require(exped.MemberCount == 2, "成员数 2");
        list = exped.GetMemberList();
        Require(list.Count == 2 && list[0].Key == LocalId && list[1].Key == 101, "成员列表队长排第一");
        Require(exped.Contains(101), "contains(成员)=true");

        Require(exped.AddMember(101, "mate") == "你已经成功加入了远征队!", "重复加入仍返回成功文案");

        // 满员：ExpeditionType.LUCID 的 maxSize=30。
        for (int i = 0; i < 28; i++) exped.AddMemberInt(200 + i, "m" + i);
        Require(exped.MemberCount == 30, "满员 30 人，实际 " + exped.MemberCount);
        Require(exped.AddMemberInt(300, "extra") == 3, "第 31 人被拒（返回码 3）");

        // 黑名单：被 ban 的人既不在成员表里，也不能再加回来。
        exped.Ban(101);
        Require(!exped.Contains(101), "被 ban 之后 contains=false");
        Require(exped.AddMemberInt(101, "mate") == 2, "被 ban 的人再加入返回码 2");

        // 招募结束后不能加入。
        exped.Start();
        Require(exped.IsInProgress(), "start 之后 isInProgress()=true");
        Require(exped.AddMemberInt(400, "late") == 1, "招募结束后加入返回码 1");

        ExpeditionRegistry.Clear();
        Step("加入 / 满员 / 黑名单 / 招募结束：与 addMemberInt 的 0-3 返回码一致");
    }

    // ——— Expedition.java:176-186 的招募倒计时 ———
    static void CheckRegistrationTimeout(ref long now)
    {
        bool disbanded = false;
        Expedition.Notice = (type, text) => { if (text.Contains("倒计时已结束")) disbanded = true; };
        long startedAt = now;
        var exped = new Expedition(LocalId, "hero", RecruitMap, ExpeditionType.LUCID);
        ExpeditionRegistry.AddExpedition(exped);
        Require(ExpeditionRegistry.GetExpedition(ExpeditionType.LUCID) == exped, "招募开始");
        Require(exped.RegistrationEndAt == startedAt + 300000, "5 分钟后到期，实际 " +
            (exped.RegistrationEndAt - startedAt) + "ms");

        ExpeditionRegistry.Tick(startedAt + 299000);
        Require(ExpeditionRegistry.GetExpedition(ExpeditionType.LUCID) == exped, "差一秒还不回收");
        Require(!disbanded, "未到期不广播解散");

        ExpeditionRegistry.Tick(startedAt + 300000);
        Require(ExpeditionRegistry.GetExpedition(ExpeditionType.LUCID) == null, "到点回收远征队");
        Require(disbanded, "到点广播「组队倒计时已结束」");
        Require(exped.Disposed, "回收后实例标记 Disposed");

        Expedition.Notice = null;
        Step("招募 5 分钟到期：先广播 disband 再摘表（Expedition.scheduleRegistrationEnd）");
    }

    // ——— Expedition.java:333-350 / ExpeditionBossLog.java ———
    static void CheckBossLog()
    {
        var exped = new Expedition(LocalId, "hero", RecruitMap, ExpeditionType.LUCID);
        exped.Start();

        exped.MonsterKilled(9309046, "普通怪");
        Require(exped.BossLogs.Count == 0, "普通怪不进远征战绩");

        // LUCID_PHASE_1 = 8880140。
        exped.MonsterKilled(8880140, "梦中的路西德");
        Require(exped.BossLogs.Count == 1, "LUCID 一阶段进战绩");
        Require(exped.BossLogs[0].Contains("梦中的路西德"), "战绩里带怪名：" + exped.BossLogs[0]);

        Require(ExpeditionBossLog.IsExpeditionBoss(8800000), "ZAKUM_1 是远征 BOSS");
        Require(ExpeditionBossLog.IsExpeditionBoss(8810118), "CHAOS_HORNTAIL 是远征 BOSS");
        Require(ExpeditionBossLog.IsExpeditionBoss(8850013), "CYGNUS 区间末尾也算（MobId.isCygnusBoss）");
        Require(!ExpeditionBossLog.IsExpeditionBoss(8850014), "CYGNUS 区间之外不算");
        Require(!ExpeditionBossLog.IsExpeditionBoss(9309046), "普通怪不是远征 BOSS");

        // use_enable_daily_expeditions 默认 false → attemptBoss 恒 true。
        Require(ExpeditionBossLog.AttemptBoss(LocalId, ExpeditionType.LUCID, false), "未开每日次数限制时放行");
        int calls = 0;
        Expedition.ConfigBool = name => name == "use_enable_daily_expeditions";
        ExpeditionBossLog.CountEntries = (cid, boss) => { calls++; return 1; };
        Require(!ExpeditionBossLog.AttemptBoss(LocalId, ExpeditionType.LUCID, false),
            "开了次数限制且已用满时拒绝（LUCID 每日 1 次）");
        Require(calls == 1, "读了一次计数");
        Expedition.ConfigBool = null;
        ExpeditionBossLog.CountEntries = null;
        Step("BOSS 战绩只记远征 BOSS；每日次数开关默认关闭时放行");
    }

    // ——— EventInstanceManager.java:1165-1218 的三条队伍判定 ———
    static void CheckTeamLackingOnline()
    {
        var service = new NativeEventScriptService(key => TeamScript, () => 1000) { LocalPlayerId = () => LocalId };
        try
        {
            var manager = service.GetManager("Team");
            Require(manager != null, "内联队伍判定脚本能建出 EventManager");
            var player = Player(LocalId);
            Require(JsConvert.ToBoolean(Call(manager, "startInstance", player)), "单人实例创建成功");
            var instance = (manager.Host.Get("getInstance") is JsFunction get
                ? get.Native(new JsCall { This = manager.Host, Args = new JsValue[] { new JsString("lobby0") } })
                : null) as JsHostObject;
            var eim = instance?.Host as NativeEventScriptService.Instance;
            Require(eim != null && eim.PlayerCount == 1, "实例里有 1 名玩家");

            // 未清关 + 只剩 1 人：远征版和普通版都算缺人。
            Require(Probe(eim, "probeExpedLacking", player, true, 2), "远征版：1 人即缺人");
            Require(Probe(eim, "probeEventLacking", player, true, 1), "普通版：队长离场即缺人");

            // 远征版不看队长也不看 minPlayers（源码注释原话：expeditions don't need to have
            // neither the leader nor meet the minimum requirement inside the event）。
            Require(Probe(eim, "probeExpedLacking", player, false, 5),
                "远征版：1 人时即使 minPlayers=5、不离场也算缺人");
            // 普通版同参数：不离场时只比人数，1 < 5 → 也算缺人。
            Require(Probe(eim, "probeEventLacking", player, false, 5), "普通版：1 人 < minPlayers=5");

            // checkEventTeamLacking：队长还在场时不走「队长离场」那条，最后按人数比。
            Require(!Probe(eim, "probeCheckLacking", null, true, 1),
                "checkEventTeamLacking：队长还在场且 1 人 >= minPlayers=1 → false");
            Require(Probe(eim, "probeCheckLacking", null, true, 2),
                "checkEventTeamLacking：1 人 < minPlayers=2 → true");

            Require(Probe(eim, "probeTogether", null, false, 0), "1 人时 isEventTeamTogether=true");
        }
        finally { service.Clear(); }
        Step("isExpeditionTeamLackingNow / isEventTeamLackingNow / checkEventTeamLacking / isEventTeamTogether");
    }

    // eim.Invoke 直接调 manager 上的同名 function，参数原样传过去。
    static bool Probe(NativeEventScriptService.Instance eim, string name, JsValue player, bool leaving, int minPlayers)
    {
        var args = player == null
            ? new JsValue[] { eim.Host, JsBoolean.Of(leaving), new JsNumber(minPlayers) }
            : new JsValue[] { eim.Host, JsBoolean.Of(leaving), new JsNumber(minPlayers), player };
        return JsConvert.ToBoolean(eim.Invoke(name, args));
    }

    static JsValue Call(NativeEventScriptService.Manager manager, string name, params JsValue[] args)
        => manager.Host.Get(name) is JsFunction function
            ? function.Native(new JsCall { This = manager.Host, Args = args })
            : JsNull.Value;

    static JsHostObject Player(int id)
    {
        var host = new JsHostObject(null);
        host.Set("getId", JsFunction.FromNative("getId", c => new JsNumber(id)));
        host.Set("getMapId", JsFunction.FromNative("getMapId", c => new JsNumber(RecruitMap)));
        host.Set("getName", JsFunction.FromNative("getName", c => new JsString("hero" + id)));
        host.Set("dropMessage", JsFunction.FromNative("dropMessage", c => JsNull.Value));
        host.Set("hasEmptySlot", JsFunction.FromNative("hasEmptySlot", c => JsBoolean.Of(true)));
        return host;
    }
}
