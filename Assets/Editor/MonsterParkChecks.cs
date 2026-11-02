using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 怪物公园六图闭环：真实 NPC 9071000 + 原始 npc/mPark_*.js、portal/mPark_*.js、
// event/MonsterPark.js，跑真实事件宿主与独立实例地图，不 Mock 任何一步。
// 覆盖：菜单/每日次数 → 起实例 → 六张私有图各自刷怪 → 未清怪不放行 → 清怪进下一图 →
//       末图结算（经验/金币/纪念币/每日勋章任务）→ 满背包分支 → 超时收尾 → 主动退出。
public static class MonsterParkChecks
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const int LobbyMap = 951000000, StartMap = 953030000, Coin = 4310020;
    const int FinalMap = StartMap + 500;

    static void Set(string key, object value)
        => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null, new[] { value });
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("MonsterPark: " + message);
    }
    static int passed;
    static void Step(string message) { passed++; Debug.Log("[MonsterPark] 通过 " + message); }

    static object InvokeStatic(Type type, string name, params object[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null) throw new InvalidOperationException("缺少方法 " + type.Name + "." + name);
        return method.Invoke(null, args);
    }
    static T Field<T>(Type type, string name) => (T)type.GetField(name, Flags).GetValue(null);
    static void SetField(Type type, string name, object value) => type.GetField(name, Flags).SetValue(null, value);

    static NativeEventScripts Host() => Field<NativeEventScripts>(typeof(NativeEventScripts), "active");
    // batchmode 不跑 Update：事件计时/延迟任务靠这里手动推进。
    static void Tick()
        => typeof(NativeEventScripts).GetMethod("TickNow", Flags).Invoke(Host(), null);
    static NativeEventMap EventMap(int id) => (NativeEventMap)InvokeStatic(typeof(NativeEventScripts), "ResolveMap", id);
    static MapleDash.Js.JsHostObject MapContext()
        => NpcApi.BuildPlayer((NpcSession)typeof(NpcSession).GetMethod("MapContext", Flags).Invoke(null, null));

    sealed class Probe
    {
        readonly MapleDash.Js.JsEngine engine;
        public Probe(string body, params (string, MapleDash.Js.JsValue)[] bindings)
        {
            engine = new MapleDash.Js.JsEngine("function check(){" + body + "}", "monster-park");
            foreach (var binding in bindings) engine.Bind(binding.Item1, binding.Item2);
            // Java.type 的未内置类走正式 Scripts.ResolveHostType（否则 ExtendUtil 之类会解析成 null）。
            if (GameSession.Server != null) engine.Interp.ResolveHostType = GameSession.Server.Scripts.ResolveHostType;
            engine.Run();
        }
        public int Int() => MapleDash.Js.JsConvert.ToInt32(engine.Call("check"));
        public bool Bool() => MapleDash.Js.JsConvert.ToBoolean(engine.Call("check"));
        public string Text() => MapleDash.Js.JsConvert.ToString(engine.Call("check"));
        public MapleDash.Js.JsValue Value() => engine.Call("check");
    }

    static int MobCount(NativeEventMap map) => new Probe("return m.countMonsters();", ("m", map.Host)).Int();
    static void ClearMobs(NativeEventMap map) => new Probe("m.killAllMonsters();return m.countMonsters();", ("m", map.Host)).Int();
    static string InstanceText(MapleDash.Js.JsValue instance, string body)
        => new Probe("return e." + body + ";", ("e", instance)).Text();
    static bool InstanceFlag(MapleDash.Js.JsValue instance, string body)
        => new Probe("return e." + body + ";", ("e", instance)).Bool();

    public static void Run()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "monster-park-" + Guid.NewGuid() + ".json");
        var dataField = typeof(Profile).GetField("data", Flags);
        var savingField = typeof(Profile).GetField("debugOverride", Flags);
        object oldData = dataField.GetValue(null), oldSaving = savingField.GetValue(null);
        int oldMap = Profile.LastMapId;
        long clock = 1000000;
        var errors = new List<string>();
        Application.LogCallback log = (text, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) errors.Add(text);
        };
        passed = 0;
        int exitCode = 0;
        try
        {
            savingField.SetValue(null, true);
            SetField(typeof(NativeEventScripts), "Clock", (Func<long>)(() => clock));
            var db = DbStore.Create(dbPath);
            int account = AccountsDao.AutoRegister(db, "parkreview", "isolated");
            AccountsDao.FindById(db, account)["nxCredit"] = 1000000L;
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", account); Set("InGame", true);
            ScriptSource.ClearCache();
            Application.logMessageReceived += log;

            var record = new CharRecord
            {
                accountid = account, name = "ParkReview", level = 200, face = 20000, hair = 30300,
                hp = 500, maxhp = 500, maxmp = 500,
                equipslots = 96, useslots = 96, setupslots = 96, etcslots = 96, meso = 1000000
            };
            record.id = CharactersDao.Insert(db, record);
            int cid = (int)record.id;
            Set("CharacterId", cid);
            Set("Character", CharService.Load(db, cid));
            GameSession.ApplyToProfile();

            CheckMenuAndEntry(db, cid);
            CheckGateOrder();
            CheckFinalRewards(db, cid, ref clock);
            CheckFullBagBranch(db, cid, ref clock);
            CheckTimeout(db, cid, ref clock);
            CheckVoluntaryExit(db, cid, ref clock);
            CheckPartyOverload(db, cid);
            Require(errors.Count == 0, "全程不应出现 Error/Exception 日志：" + string.Join(" | ", errors));

            foreach (var host in UnityEngine.Object.FindObjectsOfType<NativeEventScripts>())
                UnityEngine.Object.DestroyImmediate(host.gameObject);
        }
        catch (Exception error)
        {
            Debug.LogError("[MonsterPark] 失败：" + error);
            exitCode = 1;
        }
        finally
        {
            Application.logMessageReceived -= log;
            NpcSession.CloseActive();
            SetField(typeof(NativeEventScripts), "Clock", (Func<long>)(() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            GameServer.Stop(); Set("InGame", false);
            dataField.SetValue(null, oldData); savingField.SetValue(null, oldSaving); Profile.LastMapId = oldMap;
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
        Debug.Log("[MonsterPark] 结果：通过 " + passed + " 项，退出码 " + exitCode);
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
    }

    // —— 入口：原菜单 → 起实例 → 六张私有图各自刷怪 → 每日次数 ——
    static void CheckMenuAndEntry(DbStore db, int cid)
    {
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(9071000, "mPark_in00");
        Require(session != null && session.LastError == null, "原始公园大厅脚本：" + session?.LastError);
        Require(session.Dialog.Visible && session.Dialog.RawText.Contains("初级园区"), "大厅列出初级园区：" + session.Dialog.RawText);
        Require(session.Dialog.RawText.Contains("今日剩余次数"), "大厅显示每日剩余次数");
        Require(session.Dialog.RawText.Contains("苔藓森林"), "大厅列出原版课程");
        var options = DialogFlow.Links(session.Dialog.Content);
        Require(options.Count == 10, "初级园区应有十条课程，实际 " + options.Count);
        Step("大厅原脚本菜单（园区 / 课程 / 每日次数）");

        session.Reply(1, 0);
        Require(session.LastError == null, "选课后起实例：" + session.LastError);
        Require(!session.Dialog.RawText.Contains("系统错误"), "选课后没有脚本错误：" + session.Dialog.RawText);

        var instance = NativeEventScripts.Instance();
        Require(instance != null && !(instance is MapleDash.Js.JsNull), "角色进入独立事件实例");
        Require(NativeEventScripts.Manager("MonsterPark") is MapleDash.Js.JsHostObject, "event/MonsterPark.js 已加载");
        Require(InstanceText(instance, "getProperty('course')") == StartMap.ToString(), "实例记录课程起始图");
        Require(InstanceText(instance, "getProperty('parkRegion')") == "MPARK_BASIC", "实例记录园区");

        var counts = new List<int>();
        for (int stage = 0; stage < 6; stage++)
        {
            int id = StartMap + stage * 100;
            var map = EventMap(id);
            Require(map != null, "实例地图缺失 " + id);
            Require(map.Id == id, "实例地图对脚本仍用原地图 id " + id);
            Require(map.Scope < 0, "实例地图用独立 Scope 承载可变状态 " + id);
            int count = MobCount(map);
            Require(count > 0, "课程图 " + id + " resetPQ 后应有怪物，实际 " + count);
            counts.Add(count);
        }
        Require(counts[0] == 16 && counts[1] == 16, "苔藓森林/第二关按原 life 刷满 16/16，实际 " + counts[0] + "/" + counts[1]);
        Step("六张私有课程图按原 life 独立刷怪（" + string.Join("/", counts) + "）");

        Require(InstanceFlag(instance, "isTimerStarted() && e.getTimeLeft()>599000 && e.getTimeLeft()<=600000"),
            "实例计时未按 eventTime=10 分钟启动");
        Step("实例计时按 eventTime=10 分钟启动");

        string daily = DailyValue(db, cid).ToString();
        Require(daily == "1", "进入后按园区消耗一次每日次数，实际 " + daily);
        Step("每日次数按园区真实记账（MPARK_BASIC=1）");

        // 次数用完时原脚本必须拦住第二条路线，不能再起实例。
        SetDaily(db, cid, 2);
        Profile.LastMapId = LobbyMap;
        var blocked = NpcSession.Talk(9071000, "mPark_in00");
        blocked.Reply(1, 1);
        Require(blocked.LastError == null, "次数用完的提示：" + blocked.LastError);
        Require(blocked.Dialog.RawText.Contains("次数已经用完"), "次数用完按原脚本拒绝进入：" + blocked.Dialog.RawText);
        Require(EventMap(StartMap + 100) != null && MobCount(EventMap(StartMap + 100)) > 0, "被拒绝时不会新建第二份实例");
        NpcSession.CloseActive();
        Step("每日次数用完按原脚本拦截（不新建实例）");
    }

    // —— 逐图清怪才放行 ——
    static void CheckGateOrder()
    {
        for (int stage = 0; stage < 5; stage++)
        {
            int id = StartMap + stage * 100;
            Profile.LastMapId = id;
            RunPortal("mPark_nextStage");
            Require(Profile.LastMapId == id, "第 " + stage + " 关未清怪不能走 next00，实际落到 " + Profile.LastMapId);

            var map = EventMap(id);
            ClearMobs(map);
            Require(MobCount(map) == 0, "清怪后本图计数归零 " + id);

            RunPortal("mPark_nextStage");
            Require(Profile.LastMapId == id + 100, "清怪后 next00 进入下一关 " + (id + 100) + "，实际 " + Profile.LastMapId);
            var nextMap = EventMap(id + 100);
            Require(nextMap != null && !ReferenceEquals(nextMap, map), "下一关是另一张私有图 " + (id + 100));
            Require(MobCount(nextMap) > 0, "下一关带自己的怪物 " + (id + 100));
        }
        Step("五道 next00 门都要求本图清空，清完才逐关推进（私有图互不影响）");
    }

    // —— 末图结算：经验 / 金币 / 纪念币 / 每日勋章任务 ——
    static void CheckFinalRewards(DbStore db, int cid, ref long clock)
    {
        var instance = NativeEventScripts.Instance();
        Require(!(instance is MapleDash.Js.JsNull), "结算前仍挂在事件实例上");

        // 原脚本按星期取每日勋章任务，先把今天那条置为进行中。
        int weekday = (int)DateTime.Now.DayOfWeek;
        var medalQuests = new[] { 0, 15187, 15181, 15182, 15183, 15184, 15185, 15186 };
        int medal = medalQuests[weekday];
        if (medal > 0) Profile.Data.SetQuestStarted(medal);

        Profile.LastMapId = FinalMap;
        ClearMobs(EventMap(FinalMap));
        Require(MobCount(EventMap(FinalMap)) == 0, "末图清场");

        long exp = Profile.experience, meso = Profile.gold;
        int coins = InventoryDao.CountOf(db, cid, Coin);
        RunPortal("mPark_final");

        Require(Profile.experience - exp == 24000, "原公式经验 level*120=24000，实际 " + (Profile.experience - exp));
        Require(Profile.gold - meso == 16000, "原公式金币 level*80=16000，实际 " + (Profile.gold - meso));
        Require(InventoryDao.CountOf(db, cid, Coin) == coins + 1, "初级园区给 1 枚怪物公园纪念币");
        if (medal > 0) Require(Profile.QuestStatus(medal) == 2, "当日勋章任务被原脚本强制完成（" + medal + "）");
        Require(Profile.LastMapId == LobbyMap, "结算后按 playerExit 回大厅，实际 " + Profile.LastMapId);
        Require(InstanceFlag(instance, "isEventCleared()"), "结算走 setEventCleared");
        Require(NativeEventScripts.Instance() is MapleDash.Js.JsNull, "结算后角色已退出事件实例");
        Step("末图结算：经验 24000 / 金币 16000 / 纪念币 1 / 每日勋章任务完成 / 回大厅");

        // 玩家已经离场，改用实例句柄取它自己的图（脚本侧 getMapInstance 仍然可用）。
        var mapValue = new Probe("return e.getMapInstance(" + StartMap + ");", ("e", instance)).Value();
        var startMap = (NativeEventMap)((MapleDash.Js.JsHostObject)mapValue).Host;
        int scope = startMap.Scope;
        Require(Server.Field.MapService.Has(scope), "实例图在场景内注册");
        clock += 11000;
        Tick();
        Require(!Server.Field.MapService.Has(scope), "实例释放后几何注册一并回收");
        Step("通关 event_lobby_delay 后实例释放并回收六张私有图");
    }

    // —— 满背包分支：经验/金币照发，纪念币按原提示不发 ——
    static void CheckFullBagBranch(DbStore db, int cid, ref long clock)
    {
        // 角色对象要换（ETC 只留 1 格），必须在起新实例之前换，避免打断事件宿主。
        var record = CharactersDao.FindById(db, cid);
        record.etcslots = 1; CharactersDao.Save(db, record);
        db.Table("inventoryitems").Delete(x => DbStore.Int(x, "characterid") == cid &&
            DbStore.Int(x, "inventorytype") == InventoryDao.Etc);
        Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
        Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest { characterId = cid, itemId = 4000000, quantity = 1 }).ok,
            "塞满 ETC 栏");

        SetDaily(db, cid, 0);
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(9071000, "mPark_in00");
        Require(session != null && session.LastError == null, "满背包用例进入菜单：" + session?.LastError);
        session.Reply(1, 0);
        Require(session.LastError == null, "满背包用例起实例：" + session.LastError);
        Require(!(NativeEventScripts.Instance() is MapleDash.Js.JsNull), "满背包用例有实例");

        Profile.LastMapId = FinalMap;
        ClearMobs(EventMap(FinalMap));
        long exp = Profile.experience, meso = Profile.gold;
        RunPortal("mPark_final");
        Require(Profile.experience - exp == 24000 && Profile.gold - meso == 16000, "满背包仍按原脚本发经验/金币");
        Require(InventoryDao.CountOf(db, cid, Coin) == 0, "满背包不发纪念币，也不吞物品");
        Require(Profile.LastMapId == LobbyMap, "满背包分支仍然正常离场");
        Step("满背包：经验/金币照发，纪念币按原提示不发");
    }

    // —— 超时：10 分钟到点 end(eim) → 回大厅 → 释放地图 ——
    static void CheckTimeout(DbStore db, int cid, ref long clock)
    {
        SetDaily(db, cid, 0);
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(9071000, "mPark_in00");
        Require(session != null && session.LastError == null, "超时用例进入菜单：" + session?.LastError);
        session.Reply(1, 0);
        Require(session.LastError == null, "超时用例起实例：" + session.LastError);
        Require(!(NativeEventScripts.Instance() is MapleDash.Js.JsNull), "超时用例实例存在");
        Profile.LastMapId = StartMap;

        clock += 601000;
        Tick();
        Require(Profile.LastMapId == LobbyMap, "10 分钟到点 scheduledTimeout 送回大厅，实际 " + Profile.LastMapId);
        Require(NativeEventScripts.Instance() is MapleDash.Js.JsNull, "超时后角色不再挂在事件实例上");
        Require(EventMap(StartMap) == null, "超时 end(eim) 释放六张私有图");
        Step("超时：scheduledTimeout → end(eim) → 回大厅并释放地图");
    }

    // —— 主动退出：关内 mPark_retire「确定离开」 ——
    static void CheckVoluntaryExit(DbStore db, int cid, ref long clock)
    {
        SetDaily(db, cid, 0);
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(9071000, "mPark_in00");
        session.Reply(1, 0);
        Require(session.LastError == null, "主动退出用例起实例：" + session.LastError);
        Profile.LastMapId = StartMap + 200;

        var retire = NpcSession.Talk(9071005, "mPark_retire");
        Require(retire != null && retire.LastError == null, "关内休菲凯曼脚本：" + retire?.LastError);
        Require(retire.Dialog.RawText.Contains("当前还剩"), "关内脚本读出剩余怪物数：" + retire.Dialog.RawText);
        retire.Reply(1, 0);
        Require(retire.LastError == null, "选择离开：" + retire.LastError);
        retire.Reply(1, -1);
        Require(retire.LastError == null, "确定离开：" + retire.LastError);
        Require(Profile.LastMapId == LobbyMap, "主动退出回大厅，实际 " + Profile.LastMapId);
        NpcSession.CloseActive();
        Step("关内主动退出：mPark_retire 确认后回大厅");
    }

    // —— 组队重载：源端 EventManager.startInstance(Party, MapleMap) ——
    // 移植端只有本地角色是真实 Character，远端队长没有可注册的人，必须明确返回 false。
    static void CheckPartyOverload(DbStore db, int cid)
    {
        SetDaily(db, cid, 0);
        Profile.LastMapId = LobbyMap;
        var manager = NativeEventScripts.Manager("MonsterPark");

        bool refused = new Probe(
            "em.setProperty('course','" + StartMap + "');em.setProperty('parkRegion','MPARK_BASIC');" +
            "return em.startInstance(party,map);",
            ("em", manager), ("party", Party(false)), ("map", RecruitMap())).Bool();
        Require(!refused, "远端队长不能凭空拉起实例");
        Step("组队重载拒绝没有真实角色的远端队长");

        bool started = new Probe(
            "em.setProperty('course','" + StartMap + "');em.setProperty('parkRegion','MPARK_BASIC');" +
            "return em.startInstance(party,map);",
            ("em", manager), ("party", Party(true)), ("map", RecruitMap())).Bool();
        Require(started, "本地队长可走组队重载起实例");
        var instance = NativeEventScripts.Instance();
        Require(!(instance is MapleDash.Js.JsNull), "组队实例注册了本地角色");
        Require(InstanceText(instance, "getProperty('course')") == StartMap.ToString(), "组队实例记录课程");
        Require(EventMap(StartMap) != null && MobCount(EventMap(StartMap)) > 0, "组队实例的私有图同样刷怪");
        new Probe("e.dispose();return 1;", ("e", instance)).Int();
        NpcSession.CloseActive();
        Step("组队重载（party, map）用本地队长起实例并刷六图");
    }

    static MapleDash.Js.JsHostObject RecruitMap()
    {
        var map = new MapleDash.Js.JsHostObject(null);
        map.Set("getId", MapleDash.Js.JsFunction.FromNative("getId", c => new MapleDash.Js.JsNumber(LobbyMap)));
        return map;
    }
    static MapleDash.Js.JsHostObject Party(bool localLeader)
    {
        int leaderId = localLeader ? GameSession.CharacterId : 999999;
        MapleDash.Js.JsValue leader;
        if (localLeader) leader = MapContext();
        else
        {
            var remote = new MapleDash.Js.JsHostObject(null);
            remote.Set("getId", MapleDash.Js.JsFunction.FromNative("getId", c => new MapleDash.Js.JsNumber(leaderId)));
            remote.Set("getMapId", MapleDash.Js.JsFunction.FromNative("getMapId", c => new MapleDash.Js.JsNumber(LobbyMap)));
            leader = remote;
        }
        var party = new MapleDash.Js.JsHostObject(null);
        party.Set("getLeaderId", MapleDash.Js.JsFunction.FromNative("getLeaderId", c => new MapleDash.Js.JsNumber(leaderId)));
        party.Set("getLeader", MapleDash.Js.JsFunction.FromNative("getLeader", c => leader));
        return party;
    }

    static void RunPortal(string script)
    {
        var portal = new FieldPortal { name = script, script = script, type = 7 };
        Require(NpcSession.RunPortal(portal), "portal/" + script + " 存在");
        var active = NpcSession.Active;
        Require(active == null || active.LastError == null, "portal/" + script + "：" + active?.LastError);
        NpcSession.CloseActive();
    }

    // 每日次数落库口径：extend_value(id=角色, type=22 CHARACTER_EXTEND_DAILY, name=园区)。
    static DbRow DailyRow(DbStore db, int cid)
        => db.Table("extend_value").Find(row => DbStore.Str(row, "extend_id") == cid.ToString() &&
            DbStore.Str(row, "extend_type") == "22" && DbStore.Str(row, "extend_name") == "MPARK_BASIC");
    static int DailyValue(DbStore db, int cid)
    {
        var row = DailyRow(db, cid);
        return row == null ? 0 : int.Parse(DbStore.Str(row, "extend_value"));
    }
    static void SetDaily(DbStore db, int cid, int used)
    {
        var row = DailyRow(db, cid);
        if (row == null) db.Table("extend_value").Insert(new DbRow
        {
            ["extend_id"] = cid.ToString(), ["extend_type"] = "22", ["extend_name"] = "MPARK_BASIC",
            ["extend_value"] = used.ToString(), ["create_time"] = 0L, ["update_time"] = 0L
        });
        else row["extend_value"] = used.ToString();
        db.Save();
    }
}
