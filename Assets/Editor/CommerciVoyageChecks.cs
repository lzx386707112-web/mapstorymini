using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 凯梅尔兹航海闭环：真实 NPC 9390220（交易所/出航）、9390221（领取/兑换）与
// event/CommerciVoyage.js，走正式 NPC 宿主、事件宿主和独立实例地图，不 Mock 任何一步。
// 覆盖：菜单 → 买货（金币/凯梅尔兹币、船舱上限、每日库存）→ 出航（能量/货物/active 存档）
//       → 逐波刷怪与 2 秒换波 → 全清提前到港（100%）→ 3 秒 end 回大厅 → NPC 领取（币/舰船经验）
//       → 重复领取拦截 → 超时到港（按剩余波数打折）→ 满背包不吞币 → 兑换漩涡装备
//       → 每日 00:00 恢复 → 中途离图 → 组队重载。
public static class CommerciVoyageChecks
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const int LobbyMap = 865000001, Denaro = 4310100, NpcVoyage = 9390220, NpcClaim = 9390221;
    // CommerciVoyage.js: routeMaps / routeWaves[0] = 9 只 9390800、12 只 9390806、3 只 9390803。
    static readonly int[] RouteMaps = { 865000100, 865000200, 865000300, 865000400, 865000501, 865000900 };
    static readonly int[] RouteMapCounts = { 9, 12, 3 };
    static readonly int[] RouteXp = { 180, 300, 420, 1000, 1500, 2200 };
    static readonly int[] RouteEnergy = { 10, 12, 15, 20, 25, 30 };
    static readonly int[] RouteMinutes = { 3, 5, 7, 9, 11, 13 };

    static void Set(string key, object value)
        => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null, new[] { value });
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("CommerciVoyage: " + message);
    }
    static int passed;
    static void Step(string message) { passed++; Debug.Log("[CommerciVoyage] 通过 " + message); }

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
    static void Tick() => typeof(NativeEventScripts).GetMethod("TickNow", Flags).Invoke(Host(), null);
    static NativeEventMap EventMap(int id) => (NativeEventMap)InvokeStatic(typeof(NativeEventScripts), "ResolveMap", id);
    static MapleDash.Js.JsHostObject MapContext()
        => NpcApi.BuildPlayer((NpcSession)typeof(NpcSession).GetMethod("MapContext", Flags).Invoke(null, null));

    sealed class Probe
    {
        readonly MapleDash.Js.JsEngine engine;
        public Probe(string body, params (string, MapleDash.Js.JsValue)[] bindings)
        {
            engine = new MapleDash.Js.JsEngine("function check(){" + body + "}", "commerci-voyage");
            foreach (var binding in bindings) engine.Bind(binding.Item1, binding.Item2);
            if (GameSession.Server != null) engine.Interp.ResolveHostType = GameSession.Server.Scripts.ResolveHostType;
            engine.Run();
        }
        public int Int() => MapleDash.Js.JsConvert.ToInt32(engine.Call("check"));
        public bool Bool() => MapleDash.Js.JsConvert.ToBoolean(engine.Call("check"));
        public string Text() => MapleDash.Js.JsConvert.ToString(engine.Call("check"));
        public MapleDash.Js.JsValue Value() => engine.Call("check");
    }

    static int MobCount(NativeEventMap map) => new Probe("return m.countMonsters();", ("m", map.Host)).Int();
    static int ClearMobs(NativeEventMap map) => new Probe("m.killAllMonsters();return m.countMonsters();", ("m", map.Host)).Int();
    static int Prop(MapleDash.Js.JsValue instance, string name)
        => new Probe("return e.getIntProperty('" + name + "');", ("e", instance)).Int();
    static bool IsNull(MapleDash.Js.JsValue value) => value is MapleDash.Js.JsNull || value is MapleDash.Js.JsUndefined;

    public static void Run()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "commerci-voyage-" + Guid.NewGuid() + ".json");
        var dataField = typeof(Profile).GetField("data", Flags);
        var savingField = typeof(Profile).GetField("debugOverride", Flags);
        object oldData = dataField.GetValue(null), oldSaving = savingField.GetValue(null);
        int oldMap = Profile.LastMapId;
        long clock = 5000000;
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
            int account = AccountsDao.AutoRegister(db, "voyagereview", "isolated");
            AccountsDao.FindById(db, account)["nxCredit"] = 1000000L;
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", account); Set("InGame", true);
            ScriptSource.ClearCache();
            Application.logMessageReceived += log;

            var record = new CharRecord
            {
                accountid = account, name = "VoyageReview", level = 200, face = 20000, hair = 30300,
                hp = 500, maxhp = 500, maxmp = 500,
                equipslots = 96, useslots = 96, setupslots = 96, etcslots = 96, meso = 1000000
            };
            record.id = CharactersDao.Insert(db, record);
            int cid = (int)record.id;
            Set("CharacterId", cid);
            Set("Character", CharService.Load(db, cid));
            GameSession.ApplyToProfile();
            Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest
                { characterId = cid, itemId = Denaro, quantity = 200 }).ok, "预置凯梅尔兹金币");

            CheckMenuAndPurchase(db, cid);
            CheckLaunchAndWaves(db, cid, ref clock);
            CheckTimeoutVoyage(db, cid, ref clock);
            CheckFullBagClaim(db, cid, ref clock);
            CheckExchange(db, cid);
            CheckDailyReset(db, cid);
            CheckRouteTable(ref clock);
            CheckUnlockAndSoldOut(db, cid);
            CheckUpgrades(db, cid);
            CheckLeaveMap(db, cid, ref clock);
            CheckPartyOverload(db, cid);
            Require(errors.Count == 0, "全程不应出现 Error/Exception 日志：" + string.Join(" | ", errors));

            foreach (var host in UnityEngine.Object.FindObjectsOfType<NativeEventScripts>())
                UnityEngine.Object.DestroyImmediate(host.gameObject);
        }
        catch (Exception error)
        {
            Debug.LogError("[CommerciVoyage] 失败：" + error);
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
        Debug.Log("[CommerciVoyage] 结果：通过 " + passed + " 项，退出码 " + exitCode);
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
    }

    // —— 交易所菜单 + 买货：源端 9390220 的 defaultState 与货物表 ——
    static void CheckMenuAndPurchase(DbStore db, int cid)
    {
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(NpcVoyage, null);
        Require(session != null && session.LastError == null, "交易所脚本：" + session?.LastError);
        string main = session.Dialog.RawText;
        Require(main.Contains("凯梅尔兹贸易"), "主菜单标题：" + main);
        Require(main.Contains("货船 Lv.1"), "新角色默认货船 Lv.1：" + main);
        Require(main.Contains("100/100"), "初始能量 100/100：" + main);
        Require(main.Contains("0/2"), "初始船舱 0/2：" + main);
        Step("交易所主菜单按原脚本显示舰船/能量/船舱/凯梅尔兹金币");

        // 菜单 2 = 购买并装载贸易品。
        session.Reply(1, 2);
        Require(session.LastError == null, "打开货物列表：" + session.LastError);
        Require(session.Dialog.RawText.Contains("肥皂") && session.Dialog.RawText.Contains("五色鳞片"), "货物表按原脚本列出六种：");
        Require(session.Dialog.RawText.Contains("今日剩余 #r6"), "肥皂每日供应 6 份");
        var goods = DialogFlow.Links(session.Dialog.Content);
        Require(goods.Count == 6, "货物列表应有 6 条，实际 " + goods.Count);

        long meso = Profile.gold;
        session.Reply(1, 0);
        Require(session.LastError == null, "选择肥皂：" + session.LastError);
        session.Reply(1, 1);
        Require(session.LastError == null, "购买 1 份肥皂：" + session.LastError);
        Require(session.Dialog.RawText.Contains("已购买并装载"), "购买成功提示：" + session.Dialog.RawText);
        Require(Profile.gold - meso == -100000, "肥皂按原价 100000 金币扣款，实际 " + (Profile.gold - meso));
        var state = State(db, cid);
        Require(state[11] == "1,0,0,0,0,0", "装载 1 份肥皂进船舱，实际 " + state[11]);
        Require(state[10] == "5,5,4,3,1,1", "每日库存扣 1 份，实际 " + state[10]);
        Require(state[7] == "2", "初始船舱上限 2 格");
        Step("买货：金币扣款、船舱装载、每日库存与状态落库（areaInfo_32010）");

        // 船舱上限 2：再买 1 份装满，第三次必须被原脚本拒绝。
        var again = NpcSession.Talk(NpcVoyage, null);
        again.Reply(1, 2);
        again.Reply(1, 0);
        again.Reply(1, 1);
        Require(again.LastError == null, "再买 1 份肥皂：" + again.LastError);
        Require(State(db, cid)[11] == "2,0,0,0,0,0", "两格船舱装满，实际 " + State(db, cid)[11]);
        var full = NpcSession.Talk(NpcVoyage, null);
        full.Reply(1, 2);
        full.Reply(1, 0);
        Require(full.LastError == null, "船舱满时打开货物：" + full.LastError);
        Require(full.Dialog.RawText.Contains("船舱已满"), "船舱满按原脚本拒绝：" + full.Dialog.RawText);
        NpcSession.CloseActive();

        // 也验证数量超过剩余格数会被原脚本拦下。
        EditState(db, cid, p => p[11] = "1,0,0,0,0,0");
        var over = NpcSession.Talk(NpcVoyage, null);
        over.Reply(1, 2);
        over.Reply(1, 0);
        over.Reply(1, 2);
        Require(over.LastError == null, "超量购买：" + over.LastError);
        Require(over.Dialog.RawText.Contains("购买数量无效"), "超过剩余格数按原脚本拒绝：" + over.Dialog.RawText);
        Require(State(db, cid)[11] == "1,0,0,0,0,0", "被拒绝的购买不改船舱");
        NpcSession.CloseActive();

        // 回到只装 1 份，便于后面按 route 0 精确核对收益。
        EditState(db, cid, p => { p[11] = "1,0,0,0,0,0"; p[10] = "5,5,4,3,1,1"; });
        Step("船舱上限按 cargoCap 拦截超装");
    }

    // —— 出航 → 逐波刷怪 → 全清提前到港 → NPC 领取 ——
    static void CheckLaunchAndWaves(DbStore db, int cid, ref long clock)
    {
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(NpcVoyage, null);
        session.Reply(1, 0);                       // 进行贸易
        Require(session.LastError == null, "打开航线列表：" + session.LastError);
        Require(session.Dialog.RawText.Contains("多尔切"), "航线列表按原脚本列出目的地");
        session.Reply(1, 0);                       // 多尔切（route 0）
        Require(session.LastError == null, "选择航线：" + session.LastError);
        Require(session.Dialog.RawText.Contains("预计收益"), "出航确认显示预计收益：" + session.Dialog.RawText);
        session.Reply(1, 1);
        Require(session.LastError == null, "确认出航：" + session.LastError);

        var instance = NativeEventScripts.Instance();
        Require(!IsNull(instance), "出航后角色挂在 CommerciVoyage 实例上");
        Require(NativeEventScripts.Manager("CommerciVoyage") is MapleDash.Js.JsHostObject, "event/CommerciVoyage.js 已加载");
        Require(Prop(instance, "route") == 0, "实例记录路线下标 route=0");
        Require(Prop(instance, "waveCount") == RouteMapCounts.Length, "实例记录三波");
        Require(Prop(instance, "finished") == 0, "实例未结束");

        var state = State(db, cid);
        Require(state[5] == (100 - RouteEnergy[0]).ToString(), "出航按原脚本扣能量 10，实际 " + state[5]);
        Require(state[11] == "0,0,0,0,0,0", "出航后货物装船（船舱清空），实际 " + state[11]);
        Require(state[16] == "1" && state[17] == "0", "出航写入 active/activeRoute");
        Require(state[18] == "3", "activeReward = 基础 1 + 肥皂 1×2 = 3，实际 " + state[18]);
        Require(state[19] == RouteXp[0].ToString(), "activeExp 按航线经验 180，实际 " + state[19]);
        Require(Profile.LastMapId == RouteMaps[0], "playerEntry 送进航线私有图，实际 " + Profile.LastMapId);
        Step("出航：能量/货物/active 存档按原脚本写入并进入航线私有图");

        var map = EventMap(RouteMaps[0]);
        Require(map != null && map.Scope < 0, "航线图是独立实例地图");
        Require(MobCount(map) == RouteMapCounts[0], "第一波按 routeWaves[0][0] 生成 9 只，实际 " + MobCount(map));
        Require(new Probe("return e.isTimerStarted()&&e.getTimeLeft()>" + (RouteMinutes[0] * 60000 - 2000)
            + "&&e.getTimeLeft()<=" + (RouteMinutes[0] * 60000) + ";", ("e", instance)).Bool(),
            "实例计时按 routeMinutes[0]=3 分钟启动");
        Step("第一波刷怪与 3 分钟计时按原脚本");

        for (int wave = 0; wave < RouteMapCounts.Length; wave++)
        {
            Require(Prop(instance, "wave") == wave, "当前波次 " + wave);
            Require(ClearMobs(map) == 0, "第 " + (wave + 1) + " 波清空");
            if (wave + 1 < RouteMapCounts.Length)
            {
                Require(Prop(instance, "waveTransition") == 1, "清波后置 waveTransition=1 防重复推进");
                clock += 2000; Tick();
                Require(MobCount(map) == RouteMapCounts[wave + 1],
                    "2 秒后刷第 " + (wave + 2) + " 波 " + RouteMapCounts[wave + 1] + " 只，实际 " + MobCount(map));
                Require(Profile.LastMapId == RouteMaps[0], "换波时仍在航线图上");
            }
            else
            {
                Require(Prop(instance, "finished") == 1, "最后一波清完 completeVoyage(clearedAllWaves)");
            }
        }
        Step("三波逐波推进：清波 → 2 秒换波 → 末波清完提前到港");

        state = State(db, cid);
        int coins = int.Parse(state[12]);
        Require(coins >= 3 && coins <= 18, "全清按 100% 结算基础 3 枚（含 25% BOSS 追加），实际 " + coins);
        Require(state[13] == RouteXp[0].ToString(), "pendingExp 记录航线经验 180，实际 " + state[13]);
        Require(state[9] == "1,0,0,0,0,0", "路线完成次数 +1，实际 " + state[9]);
        Require(state[16] == "0" && state[17] == "-1", "到港后清空 active 状态");
        Step("全清结算写入 pendingCoins/PendingExp/完成次数并清 active");

        clock += 3000; Tick();                     // finishVoyage → end(eim)
        Require(Profile.LastMapId == LobbyMap, "3 秒后 end(eim) 送回交易所，实际 " + Profile.LastMapId);
        Require(IsNull(NativeEventScripts.Instance()), "到港后角色退出事件实例");
        Require(EventMap(RouteMaps[0]) == null, "实例地图随 dispose 回收");
        Step("finishVoyage → end → playerExit 回大厅并释放航线图");

        // 领取：先重登（CharService.Load 重读 areaInfo），验证持久化。
        Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
        int coinsBefore = InventoryDao.CountOf(db, cid, Denaro);
        Profile.LastMapId = LobbyMap;
        var claim = NpcSession.Talk(NpcClaim, null);
        Require(claim != null && claim.LastError == null, "领取 NPC 脚本：" + claim?.LastError);
        Require(claim.Dialog.RawText.Contains("领取本次航海收益"), "有待领取时显示领取入口：" + claim.Dialog.RawText);
        claim.Reply(1, 0);
        Require(claim.LastError == null, "领取收益：" + claim.LastError);
        Require(InventoryDao.CountOf(db, cid, Denaro) == coinsBefore + coins,
            "领取后凯梅尔兹金币 +" + coins + "，实际 " + (InventoryDao.CountOf(db, cid, Denaro) - coinsBefore));
        var claimed = State(db, cid);
        Require(claimed[12] == "0" && claimed[13] == "0", "领取后 pending 清零");
        Require(claimed[4] == RouteXp[0].ToString(), "舰船经验累加 180，实际 " + claimed[4]);
        Step("NPC 领取：重登后 pending 仍在，领取发币并累加舰船经验");

        var repeat = NpcSession.Talk(NpcClaim, null);
        Require(repeat != null && repeat.LastError == null, "再次打开领取 NPC：" + repeat?.LastError);
        Require(!repeat.Dialog.RawText.Contains("领取本次航海收益"), "领完后不再显示领取入口：" + repeat.Dialog.RawText);
        NpcSession.CloseActive();
        Step("重复领取被原脚本拦截（不会重复发币）");
    }

    // —— 超时到港：不清波按剩余波数打折 ——
    static void CheckTimeoutVoyage(DbStore db, int cid, ref long clock)
    {
        EditState(db, cid, p => { p[11] = "1,0,0,0,0,0"; p[5] = "100"; });
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(NpcVoyage, null);
        session.Reply(1, 0);
        session.Reply(1, 0);
        session.Reply(1, 1);
        Require(session.LastError == null, "超时用例出航：" + session.LastError);
        var instance = NativeEventScripts.Instance();
        Require(!IsNull(instance), "超时用例进入实例");

        clock += RouteMinutes[0] * 60000; Tick();   // scheduledTimeout → completeVoyage(false)
        var state = State(db, cid);
        // 一波都没清：remaining=3 → rewardPercent=100-3×10=70 → floor(3×70/100)=2。
        Require(state[12] == "2", "超时到港按 70% 结算 2 枚，实际 " + state[12]);
        Require(state[13] == RouteXp[0].ToString(), "超时也结算舰船经验 180，实际 " + state[13]);
        Require(state[16] == "0", "超时后清空 active");
        Require(Prop(instance, "finished") == 1, "超时走 completeVoyage(false)");
        Step("超时到港：按剩余波数 70% 折算奖励，不复制货物");

        clock += 3000; Tick();
        Require(Profile.LastMapId == LobbyMap, "超时 3 秒后送回交易所，实际 " + Profile.LastMapId);
        Step("超时 end(eim) 回大厅");
    }

    // —— 满背包领取：原脚本拒绝且不清 pending ——
    static void CheckFullBagClaim(DbStore db, int cid, ref long clock)
    {
        var record = CharactersDao.FindById(db, cid);
        record.etcslots = 1; CharactersDao.Save(db, record);
        db.Table("inventoryitems").Delete(x => DbStore.Int(x, "characterid") == cid &&
            DbStore.Int(x, "inventorytype") == InventoryDao.Etc);
        Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
        Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest { characterId = cid, itemId = 4000000, quantity = 1 }).ok,
            "塞满 ETC 栏");

        Profile.LastMapId = LobbyMap;
        var claim = NpcSession.Talk(NpcClaim, null);
        Require(claim != null && claim.LastError == null, "满背包领取入口：" + claim?.LastError);
        claim.Reply(1, 0);
        Require(claim.LastError == null, "满背包领取：" + claim.LastError);
        Require(claim.Dialog.RawText.Contains("空间不足"), "满背包按原脚本拒绝：" + claim.Dialog.RawText);
        Require(State(db, cid)[12] == "2", "领取失败不清 pendingCoins");
        NpcSession.CloseActive();
        Step("满背包：领取被拒且 pending 不被清空");

        record = CharactersDao.FindById(db, cid);
        record.etcslots = 96; CharactersDao.Save(db, record);
        Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
        var again = NpcSession.Talk(NpcClaim, null);
        again.Reply(1, 0);
        Require(again.LastError == null, "恢复背包后领取：" + again.LastError);
        Require(State(db, cid)[12] == "0", "恢复背包后补领成功");
        Step("空间恢复后按原脚本补领成功");
    }

    // —— 兑换漩涡装备 ——
    static void CheckExchange(DbStore db, int cid)
    {
        int coins = InventoryDao.CountOf(db, cid, Denaro);
        // 上一用例清空过 ETC 栏重建背包，这里按需补到 200 枚再兑换。
        if (coins < 200)
        {
            Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest
                { characterId = cid, itemId = Denaro, quantity = 200 - coins }).ok, "补足凯梅尔兹金币");
            // haveItem/itemQuantity 读的是 Profile 的库存镜像，补完要按角色重读一次。
            Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
            coins = InventoryDao.CountOf(db, cid, Denaro);
        }
        Require(coins >= 200, "兑换用例需要 200 枚凯梅尔兹金币，实际 " + coins);
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(NpcClaim, null);
        session.Reply(1, 1);
        Require(session.LastError == null, "打开兑换列表：" + session.LastError);
        Require(session.Dialog.RawText.Contains("漩涡文身") && session.Dialog.RawText.Contains("漩涡手铳"), "兑换目录按原脚本列出 26 件");
        int before = InventoryDao.CountOf(db, cid, 1012438);
        session.Reply(1, 0);
        Require(session.LastError == null, "选择漩涡文身：" + session.LastError);
        session.Reply(1, 1);
        Require(session.LastError == null, "确认兑换：" + session.LastError);
        Require(session.Dialog.RawText.Contains("成功兑换"), "兑换成功提示：" + session.Dialog.RawText);
        Require(InventoryDao.CountOf(db, cid, 1012438) == before + 1, "兑换得到漩涡文身");
        Require(InventoryDao.CountOf(db, cid, Denaro) == coins - 160, "按原价扣 160 枚凯梅尔兹金币");
        Step("兑换：按原价扣币并发放装备");
    }

    // —— 每日 00:00 恢复能量与库存 ——
    static void CheckDailyReset(DbStore db, int cid)
    {
        EditState(db, cid, p =>
        {
            p[1] = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
            p[5] = "3";
            p[10] = "0,0,0,0,0,0";
        });
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(NpcVoyage, null);
        Require(session != null && session.LastError == null, "跨天进入交易所：" + session?.LastError);
        var state = State(db, cid);
        Require(state[1] == DateTime.Now.ToString("yyyyMMdd"), "loadState 把日期刷成今天，实际 " + state[1]);
        Require(state[5] == "100", "跨天把能量恢复到上限，实际 " + state[5]);
        Require(state[10] == "6,5,4,3,1,1", "跨天重置每日库存，实际 " + state[10]);
        NpcSession.CloseActive();
        Step("跨天：日期/能量/每日库存按 loadState 重置");
    }

    // —— 六条航线的波次表：routeWaves[route][0].count 与 routeMaps ——
    static void CheckRouteTable(ref long clock)
    {
        var manager = NativeEventScripts.Manager("CommerciVoyage");
        int[] firstWave = { 9, 12, 12, 15, 15, 15 };
        int[] waveCounts = { 3, 3, 3, 4, 5, 5 };
        for (int route = 0; route < RouteMaps.Length; route++)
        {
            Profile.LastMapId = LobbyMap;
            bool started = new Probe("return em.startInstance(-1,p,p," + (route + 1) + ");",
                ("em", manager), ("p", MapContext())).Bool();
            Require(started, "起 route " + route + " 实例");
            var instance = NativeEventScripts.Instance();
            Require(!IsNull(instance), "route " + route + " 实例存在");
            Require(Prop(instance, "route") == route, "difficulty=" + (route + 1) + " 映射到 route " + route);
            Require(Prop(instance, "waveCount") == waveCounts[route],
                "route " + route + " 波数 " + waveCounts[route] + "，实际 " + Prop(instance, "waveCount"));
            var map = EventMap(RouteMaps[route]);
            Require(map != null, "route " + route + " 私有图 " + RouteMaps[route]);
            Require(MobCount(map) == firstWave[route],
                "route " + route + " 第一波 " + firstWave[route] + " 只，实际 " + MobCount(map));
            // 交接文档要求「六条航线全清」：route 0 的完整链路在 CheckLaunchAndWaves 里，
            // 这里把其余航线的每一波都真正清干净，验证波次推进、换波计时和末波提前到港，
            // 而不是只看 routeWaves 表里的数字。
            for (int wave = 0; wave < waveCounts[route]; wave++)
            {
                Require(Prop(instance, "wave") == wave, "route " + route + " 第 " + (wave + 1) + " 波下标 " + wave);
                Require(ClearMobs(map) == 0, "route " + route + " 第 " + (wave + 1) + " 波清空");
                if (wave + 1 < waveCounts[route])
                {
                    Require(Prop(instance, "waveTransition") == 1,
                        "route " + route + " 清波后置 waveTransition=1");
                    clock += 2000; Tick();
                    Require(MobCount(map) > 0, "route " + route + " 2 秒后刷第 " + (wave + 2) + " 波");
                    Require(Profile.LastMapId == RouteMaps[route], "route " + route + " 换波时仍在航线图上");
                }
                else
                {
                    Require(Prop(instance, "finished") == 1,
                        "route " + route + " 末波清完 completeVoyage(clearedAllWaves)");
                }
            }
            new Probe("e.dispose();return 1;", ("e", instance)).Int();
            NpcSession.CloseActive();
        }
        Profile.LastMapId = LobbyMap;
        Step("六条航线的地图/波数/首波数量与 routeWaves 表一致，且每条航线逐波清完到港");
    }

    // —— 通行证：未解锁拦截、支付解锁、售罄 ——
    static void CheckUnlockAndSoldOut(DbStore db, int cid)
    {
        EditState(db, cid, p =>
        {
            p[8] = "1"; p[9] = "5,0,0,0,0,0"; p[12] = "0"; p[13] = "0";
            p[5] = "100"; p[11] = "1,0,0,0,0,0";
        });
        int coins = InventoryDao.CountOf(db, cid, Denaro);
        if (coins < 20)
        {
            Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest
                { characterId = cid, itemId = Denaro, quantity = 20 - coins }).ok, "补解锁用凯梅尔兹金币");
            Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
            coins = InventoryDao.CountOf(db, cid, Denaro);
        }
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(NpcVoyage, null);
        session.Reply(1, 0);
        session.Reply(1, 1);                       // 露娜（route 1，未解锁）
        Require(session.LastError == null, "选择未解锁航线：" + session.LastError);
        Require(session.Dialog.RawText.Contains("是否解锁"), "完成前置次数后进入支付确认：" + session.Dialog.RawText);
        session.Reply(1, 1);
        Require(session.LastError == null, "支付解锁：" + session.LastError);
        Require(InventoryDao.CountOf(db, cid, Denaro) == coins - 5, "解锁扣 5 枚凯梅尔兹金币");
        Require(State(db, cid)[8] == "3", "unlockMask 记录 route 1 已解锁，实际 " + State(db, cid)[8]);
        Step("航线解锁：按原脚本校验前置完成次数并扣凯梅尔兹金币");

        EditState(db, cid, p => p[10] = "0,0,0,0,0,0");
        var sold = NpcSession.Talk(NpcVoyage, null);
        sold.Reply(1, 2);
        sold.Reply(1, 0);
        Require(sold.LastError == null, "售罄购买：" + sold.LastError);
        Require(sold.Dialog.RawText.Contains("售罄"), "库存为 0 按原脚本提示售罄：" + sold.Dialog.RawText);
        NpcSession.CloseActive();
        EditState(db, cid, p => p[10] = "6,5,4,3,1,1");
        Step("每日库存售罄按原脚本拒绝");
    }

    // —— 升级链：船只升阶 → 船舱 / 能量上限 ——
    static void CheckUpgrades(DbStore db, int cid)
    {
        EditState(db, cid, p =>
        {
            p[2] = "0"; p[3] = "1"; p[4] = "0"; p[7] = "2"; p[5] = "100"; p[6] = "100";
            p[8] = "3"; p[9] = "5,5,0,0,0,0"; p[12] = "0"; p[13] = "0";
        });
        Profile.LastMapId = LobbyMap;
        var capped = NpcSession.Talk(NpcVoyage, null);
        capped.Reply(1, 4);
        Require(capped.LastError == null, "低阶升船舱：" + capped.LastError);
        Require(capped.Dialog.RawText.Contains("请先将船只升阶"), "低阶船舱按原脚本要求先升阶：" + capped.Dialog.RawText);
        var lowLevel = NpcSession.Talk(NpcVoyage, null);
        lowLevel.Reply(1, 6);
        Require(lowLevel.LastError == null, "低等级升船：" + lowLevel.LastError);
        Require(lowLevel.Dialog.RawText.Contains("10 级"), "船只未满 10 级按原脚本拒绝：" + lowLevel.Dialog.RawText);
        Step("升级前置：低阶/低等级按原脚本拦截");

        // 船只升阶需要 1000 万金币。
        var record = CharactersDao.FindById(db, cid);
        record.meso = 12000000; CharactersDao.Save(db, record);
        Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
        EditState(db, cid, p => { p[3] = "10"; p[4] = "0"; });
        var ship = NpcSession.Talk(NpcVoyage, null);
        ship.Reply(1, 6);
        Require(ship.LastError == null, "升阶确认：" + ship.LastError);
        ship.Reply(1, 1);
        Require(ship.LastError == null, "升阶：" + ship.LastError);
        var state = State(db, cid);
        Require(state[2] == "1", "船只升阶到帆船，实际 tier=" + state[2]);
        Require(state[3] == "1", "升阶后船只等级重置为 1");
        Require(Profile.gold == 2000000, "升阶扣 1000 万金币，实际余额 " + Profile.gold);
        Step("船只升阶：重置等级、扣 1000 万金币");

        int coins = InventoryDao.CountOf(db, cid, Denaro);
        if (coins < 40)
        {
            Require(GameSession.Net.ChangeInventory(new InventoryChangeRequest
                { characterId = cid, itemId = Denaro, quantity = 40 - coins }).ok, "补升级用凯梅尔兹金币");
            Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
            coins = InventoryDao.CountOf(db, cid, Denaro);
        }
        var cargo = NpcSession.Talk(NpcVoyage, null);
        cargo.Reply(1, 4);
        Require(cargo.LastError == null, "升船舱确认：" + cargo.LastError);
        cargo.Reply(1, 1);
        Require(cargo.LastError == null, "升船舱：" + cargo.LastError);
        Require(State(db, cid)[7] == "3", "船舱 2 → 3，实际 " + State(db, cid)[7]);
        Require(InventoryDao.CountOf(db, cid, Denaro) == coins - 20, "船舱升级扣 20 枚");

        var energy = NpcSession.Talk(NpcVoyage, null);
        energy.Reply(1, 5);
        Require(energy.LastError == null, "升能量确认：" + energy.LastError);
        energy.Reply(1, 1);
        Require(energy.LastError == null, "升能量：" + energy.LastError);
        Require(State(db, cid)[6] == "105", "能量上限 100 → 105，实际 " + State(db, cid)[6]);
        Require(InventoryDao.CountOf(db, cid, Denaro) == coins - 40, "能量升级再扣 20 枚");
        NpcSession.CloseActive();
        Step("船舱/能量上限：按原公式扣凯梅尔兹金币并提升上限");
    }

    // —— 中途离图：changedMap 未换到航线图 → unregister → end ——
    static void CheckLeaveMap(DbStore db, int cid, ref long clock)
    {
        EditState(db, cid, p => { p[11] = "1,0,0,0,0,0"; p[5] = "100"; });
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(NpcVoyage, null);
        session.Reply(1, 0);
        session.Reply(1, 0);
        session.Reply(1, 1);
        Require(session.LastError == null, "离图用例出航：" + session.LastError);
        Require(!IsNull(NativeEventScripts.Instance()), "离图用例进入实例");

        // 源端 changedMap：目标不是本航线图就 unregister，没人了直接 end。
        // 真实顺序是玩家先换到新图（Character.changeMap）再回调 changedMap，所以这里先落新图。
        var routeMap = RouteMaps[0];
        var scope = EventMap(routeMap).Scope;
        Profile.LastMapId = RouteMaps[1];
        InvokeStatic(typeof(NativeEventScripts), "ChangedMap", RouteMaps[1], false);
        Require(IsNull(NativeEventScripts.Instance()), "离开航线图后角色被摘出实例");
        Require(Profile.LastMapId == RouteMaps[1], "event 不会把已经离图的玩家强行拉回大厅，实际 " + Profile.LastMapId);
        Require(!Server.Field.MapService.Has(scope), "最后一人离开后航线图回收");
        Require(State(db, cid)[16] == "1", "中途离图不清 active（源端由回到大厅的 loadState 清理）");

        // 回到交易所再打开菜单：loadState 发现 active 且已在大厅 → 视为航行中断，清掉 active。
        Profile.LastMapId = LobbyMap;
        var back = NpcSession.Talk(NpcVoyage, null);
        Require(back != null && back.LastError == null, "中断后回交易所：" + back?.LastError);
        Require(State(db, cid)[16] == "0" && State(db, cid)[17] == "-1", "回大厅后 loadState 清掉中断的 active");
        NpcSession.CloseActive();
        Step("中途离图：changedMap 摘人，最后一人离开直接结束实例，回大厅按原逻辑清理 active");
    }

    // —— 组队重载：源端 startInstance(party, map, difficulty) ——
    static void CheckPartyOverload(DbStore db, int cid)
    {
        EditState(db, cid, p => { p[11] = "1,0,0,0,0,0"; p[5] = "100"; });
        Profile.LastMapId = LobbyMap;
        var manager = NativeEventScripts.Manager("CommerciVoyage");
        bool refused = new Probe("return em.startInstance(party,map,1);",
            ("em", manager), ("party", Party(false)), ("map", RecruitMap())).Bool();
        Require(!refused, "远端队长不能凭空拉起航海实例");

        bool started = new Probe("return em.startInstance(party,map,1);",
            ("em", manager), ("party", Party(true)), ("map", RecruitMap())).Bool();
        Require(started, "本地队长可走组队重载起航海实例");
        var instance = NativeEventScripts.Instance();
        Require(!IsNull(instance), "组队实例注册了本地角色");
        Require(Prop(instance, "route") == 0, "组队实例按 difficulty=1 取 route 0");
        var map = EventMap(RouteMaps[0]);
        Require(map != null && MobCount(map) == RouteMapCounts[0], "组队实例同样刷第一波");
        new Probe("e.dispose();return 1;", ("e", instance)).Int();
        NpcSession.CloseActive();
        Step("组队重载（party, map, difficulty）用本地队长起实例并刷波");
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

    // 航海状态落在 areaInfo_32010：extend_value(type=21 CHARACTER, name=areaInfo_32010)。
    static DbRow StateRow(DbStore db, int cid)
        => db.Table("extend_value").Find(row => DbStore.Str(row, "extend_id") == cid.ToString() &&
            DbStore.Int(row, "extend_type") == 21 && DbStore.Str(row, "extend_name") == "areaInfo_32010");
    static string[] State(DbStore db, int cid)
    {
        var row = StateRow(db, cid);
        Require(row != null, "航海状态已写入 areaInfo_32010");
        var parts = DbStore.Str(row, "extend_value").Split('|');
        Require(parts.Length >= 21 && parts[0] == "C1", "航海状态按原脚本 C1 编码");
        return parts;
    }
    static void EditState(DbStore db, int cid, Action<string[]> edit)
    {
        var row = StateRow(db, cid);
        Require(row != null, "航海状态已写入 areaInfo_32010");
        var parts = DbStore.Str(row, "extend_value").Split('|');
        edit(parts);
        row["extend_value"] = string.Join("|", parts);
        db.Save();
    }
}
