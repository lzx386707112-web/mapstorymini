using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MapleDash.Js;

// 起源之塔 1~20 层闭环（反馈清单第 11 项）。全部走源端原脚本，不在这里重写任何一层的规则：
//   npc/2540000.js（艾丽西亚：大厅菜单 + 塔内「继续 / 结束」）
//   event/SeedTower20.js（1~20 层的判定、刷怪、落石、补油、积分全在它里面）
//   portal/seedTower.js（所有塔门的唯一入口，转 seedPortal）
//   reactor/9922003.js（第 18 层椰子树，转 seedCoconut）
// 宿主侧被验证的是 SeedTowerCompat 的四个服务端钩子（handleAttack / protectsTarget /
// controls / allowReactorHit）、事件实例 + 独立实例地图、以及玩家扩展值结算。
//
// 覆盖：大厅菜单 → 起实例 → 20 层逐层成功（一路用真实 out00 门推进）→ 通关 40 分 → 重登校验
//       → 每层的失败分支（漏怪 / 落石致死 / 弹药耗尽 / 3 分钟 / 护送太远 / Fleta 死亡 /
//         总时间耗尽 / 主动退出 / 死亡 / 中途离图）→ 积分规则 → 不可重复结算。
public static class SeedTowerChecks
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const int LobbyMap = 992000000, Alice = 2540000, ReturnMap = 100000000;
    const int BatYellow = 9309116, BatRed = 9309117;
    const int PointType = 21;                          // extend_value(extend_type=21) = 角色永久扩展值
    const string PointKey = "起源积分", TotalKey = "起源累计积分";
    static int Tower(int floor) => 992000000 + floor * 1000;

    static long clock = 1700000000000;                 // 事件计时与 Date.now() 共用的可控时钟
    static JsValue tower;                              // 当前实例宿主

    static void Set(string key, object value)
        => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null, new[] { value });
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("SeedTower: " + message);
    }
    static int passed;
    static void Step(string message) { passed++; Debug.Log("[SeedTower] 通过 " + message); }

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
    static NativeEventMap EventMap(int id) => NativeEventScripts.ResolveMap(id);
    static JsHostObject PlayerHost()
        => NpcApi.BuildPlayer((NpcSession)typeof(NpcSession).GetMethod("MapContext", Flags).Invoke(null, null));

    sealed class Probe
    {
        readonly JsEngine engine;
        public Probe(string body, params (string, JsValue)[] bindings)
        {
            engine = new JsEngine("function check(){" + body + "}", "seed-tower");
            foreach (var binding in bindings) engine.Bind(binding.Item1, binding.Item2);
            if (GameSession.Server != null) engine.Interp.ResolveHostType = GameSession.Server.Scripts.ResolveHostType;
            engine.Run();
        }
        public int Int() => JsConvert.ToInt32(engine.Call("check"));
        public bool Bool() => JsConvert.ToBoolean(engine.Call("check"));
        public string Text() => JsConvert.ToString(engine.Call("check"));
        public JsValue Value() => engine.Call("check");
    }

    // ——— 实例状态一律从脚本自己的属性表读，回归不另建一份 ———
    static JsValue Eval(string js) => new Probe(js, ("e", tower), ("p", PlayerHost())).Value();
    static int Num(string js) => JsConvert.ToInt32(Eval(js));
    static string Text(string js) => JsConvert.ToString(Eval(js));
    static bool Flag(string js) => JsConvert.ToBoolean(Eval(js));
    static int Prop(string key) => Num("return Number(e.getProperty('" + key + "')||0);");
    // 脚本里 warningAt / oilAt / lastShot 这些是**毫秒时间戳**（~1.7e12），Int32 一定溢出，
    // 读它们必须走 long，否则 >0 的判定会翻负。
    static long PropLong(string key) => (long)JsConvert.ToNumber(Eval("return Number(e.getProperty('" + key + "')||0);"));
    static string PropText(string key) => Text("return ''+(e.getProperty('" + key + "')||'');");
    static void SetProp(string key, string value) { Num("e.setProperty('" + key + "','" + value + "');return 1;"); }
    static int Floor => Prop("floor");
    static int Count(int floor) => Num("return e.getInstanceMap(" + Tower(floor) + ").countMonsters();");
    static int MobCountOnFloor(int mobId)
        => Num("var m=e.getInstanceMap(" + Tower(Floor) + "),arr=m.getAllMonsters(),n=0;" +
               "for(var i=0;i<arr.size();i++)if(Number(arr.get(i).getId())==" + mobId + ")n++;return n;");

    // 脚本自己的 spawn()：LifeFactory.getMonster + spawnMonster(OnGroundBelow) + seedControlled 登记
    static int Spawn(int mobId, int x, int y)
        => Num("var mob=e.invokeScriptFunction('spawn',e," + mobId + "," + x + "," + y + ",false,false);" +
               "return Number(mob.getObjectId());");

    static int KillIds(params int[] ids)
    {
        string want = "[" + string.Join(",", ids) + "]";
        return Num("var f=Number(e.getProperty('floor')),m=e.getInstanceMap(992000000+f*1000);" +
                   "var arr=m.getAllMonsters(),n=arr.size(),want=" + want + ",k=0;" +
                   "for(var i=0;i<n;i++){var mob=arr.get(i);" +
                   "if(want.indexOf(Number(mob.getId()))<0)continue;m.killMonster(mob,p,false);k++;}return k;");
    }

    // 真实门脚本：portal/seedTower.js 读 pi.getPortal().getName() 再转 seedPortal
    static void Portal(string name)
    {
        Require(NpcSession.RunPortal(new FieldPortal { name = name, script = "seedTower", type = 7 }), "portal/seedTower 存在");
        var active = NpcSession.Active;
        Require(active == null || active.LastError == null, "门 " + name + "：" + active?.LastError);
    }

    static void Gain(int itemId, int quantity)
        => Num("p.getAbstractPlayerInteraction().gainItem(" + itemId + "," + quantity + ");return 1;");
    static int ItemCount(int itemId) => Num("return Number(p.getItemQuantity(" + itemId + ",false));");

    // 脚本 drop() 走的是 map.spawnItemDrop —— 东西落在地面上，不进背包。
    // 生产链路是 GroundItemBridge（MapItemService.Spawned → GroundItem → 点击 → MapItemPickup），
    // batchmode 没有 FieldDirector/GroundItem 可点，所以这里显式替玩家「走过去捡」：
    // 先断言地面真的掉了，再摘掉地面实体 + 入背包。
    static int GroundCount(int itemId)
    {
        int n = 0;
        foreach (var drop in Server.Field.MapItemService.DroppedItems(EventMap(Tower(Floor)).Scope))
            if (!drop.pickedUp && drop.meso == 0 && drop.item != null && drop.item.itemId == itemId) n++;
        return n;
    }
    static int PickUp(int itemId)
    {
        int taken = 0;
        var map = EventMap(Tower(Floor));
        foreach (var drop in Server.Field.MapItemService.DroppedItems(map.Scope))
        {
            if (drop.pickedUp || drop.meso != 0 || drop.item == null || drop.item.itemId != itemId) continue;
            Server.Field.MapItemService.PickItemDrop(map.Scope, drop, 2, GameSession.CharacterId);
            Gain(itemId, drop.item.quantity);
            taken += drop.item.quantity;
        }
        return taken;
    }

    // 打满 score：一轮刷 8 只再清，够数就停（刷怪走脚本自己的 spawn，不伪造刷怪点）
    static void Grind(int mobId, int goal, int floor)
    {
        for (int round = 0; round < 400; round++)
        {
            if (Prop("score") >= goal) return;
            for (int i = 0; i < 8; i++) Spawn(mobId, -400 + i * 90, 200);
            KillIds(mobId);
        }
        throw new InvalidOperationException("SeedTower: 第 " + floor + " 层打不到 " + goal + " 分（" + Prop("score") + "）");
    }

    // ——— 实例生命周期 ———
    static JsValue StartTower()
    {
        SetField(typeof(NpcApi), "HeadlessPosition", Vector2.zero);
        Profile.LastMapId = LobbyMap;
        NpcSession.CloseActive();
        var session = NpcSession.Talk(Alice, null);
        Require(session != null && session.LastError == null, "大厅对话：" + session?.LastError);
        Require(session.Dialog.RawText.Contains("起源之塔"), "大厅菜单标题：" + session.Dialog.RawText);
        Require(!session.Dialog.RawText.Contains("系统错误"), "大厅菜单没有脚本错误：" + session.Dialog.RawText);
        session.Reply(1, 0);
        Require(session.LastError == null, "选[挑战前 20 层]：" + session.LastError);
        NpcSession.CloseActive();
        var instance = NativeEventScripts.Instance();
        Require(instance != null && !(instance is JsNull), "起实例后角色挂在事件实例上");
        Require(NativeEventScripts.Manager("SeedTower20") is JsHostObject, "event/SeedTower20.js 已加载");
        tower = instance;
        // 事件地图一建出来 GameServer.Bots 就懒初始化了，BotRuntime 的构造函数会把
        // MapReactorService.Clock 抢回 BotScheduler.Clock；这里重新钉回可控时钟，
        // 否则反应堆的 500ms 节流 / 15 秒重生锁会跟着真实时间跑，回归不可复现。
        Server.Field.MapReactorService.Clock = () => clock;
        // 每个用例从满血开始：上一条用例可能把人打到 1%（第 8 层火柱）或直接打死（第 13 层落石），
        // 而 seedAttack/tick 都用 chr.isAlive() 兜底，带着濒死状态重进会让后面全都不触发。
        Num("p.updateHpMp(Number(p.getCurrentMaxHp()),Number(p.getCurrentMaxMp()));return 1;");
        Require(Flag("return p.isAlive();"), "用例开始时角色是活的");
        Require(PropText("seedTower") == "1", "实例标记 seedTower=1");
        Require(Floor == 1, "playerEntry 直接送进第 1 层，实际 " + Floor);
        Require(Prop("remaining") == 3000000, "总时间 50 分钟，实际 " + Prop("remaining"));
        Require(PropText("award") == "", "新实例的 award 为空（还没结算）");
        Require(Profile.LastMapId == Tower(1), "落在第 1 层实例图，实际 " + Profile.LastMapId);
        return instance;
    }

    // 收尾：确认回大厅、实例释放（event_lobby_delay 之后 lobby 才空出来）
    static void CloseRun(string label)
    {
        Require(Profile.LastMapId == LobbyMap, label + " 结束后回大厅，实际 " + Profile.LastMapId);
        Require(NativeEventScripts.Instance() is JsNull, label + " 结束后不再挂在实例上");
        clock += 12000; Tick();
        tower = JsNull.Value;
    }

    // 跳到某层：enterFloor 是脚本自己的入口，只在计时器还活着时调用
    //（计时器停了），getTimeLeft()<=0 会被它当成总时间耗尽）
    static void Jump(int floor)
    {
        // enterFloor 开头会 set(remaining, getTimeLeft()) 再判「总时间耗尽」：计时器一停 getTimeLeft()
        // 就是负的，等于立刻 timeout。只有休息层（5/15）例外——它本来就不跑计时器。
        int current = Floor;
        Require(current == 5 || current == 15 || Flag("return e.isTimerStarted();"),
            "跳层前计时器必须在跑（当前第 " + current + " 层）");
        Num("e.invokeScriptFunction('enterFloor',e,p," + floor + ");return 1;");
        Require(Floor == floor, "跳到第 " + floor + " 层，实际 " + Floor);
        Require(Profile.LastMapId == Tower(floor), "跳层后落在 " + Tower(floor) + "，实际 " + Profile.LastMapId);
    }

    static int ReactorOid(int floor, string name)
        => Num("var r=e.getInstanceMap(" + Tower(floor) + ").getReactorByName('" + name + "');" +
               "return r?Number(r.getObjectId()):-1;");

    public static void Run()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "seed-tower-" + Guid.NewGuid() + ".json");
        var dataField = typeof(Profile).GetField("data", Flags);
        var savingField = typeof(Profile).GetField("debugOverride", Flags);
        object oldData = dataField.GetValue(null), oldSaving = savingField.GetValue(null);
        int oldMap = Profile.LastMapId;
        var oldJsClock = Field<Func<long>>(typeof(JsDate), "Clock");
        var oldReactorClock = Server.Field.MapReactorService.Clock;
        var oldScriptSource = Server.Field.MapReactorService.ScriptSource;
        var oldEventInstance = Server.Field.MapReactorService.EventInstance;
        var oldReactorPlayer = Server.Field.MapReactorService.Player;
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
            SetField(typeof(JsDate), "Clock", (Func<long>)(() => clock));
            var db = DbStore.Create(dbPath);
            int account = AccountsDao.AutoRegister(db, "seedreview", "isolated");
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", account); Set("InGame", true);
            ScriptSource.ClearCache();
            Application.logMessageReceived += log;

            var record = new CharRecord
            {
                accountid = account, name = "SeedReview", level = 200, face = 20000, hair = 30300,
                hp = 500, mp = 200, maxhp = 500, maxmp = 500,
                equipslots = 96, useslots = 96, setupslots = 96, etcslots = 96, meso = 1000000
            };
            record.id = CharactersDao.Insert(db, record);
            int cid = (int)record.id;
            Set("CharacterId", cid);
            Set("Character", CharService.Load(db, cid));
            GameSession.ApplyToProfile();

            // 反应堆：真实脚本（reactor/9922003.js）+ 实例事件 + 受击时钟。
            // 生产里 EventInstance 用 GameLoop.field.StateId 判断「这张图是不是当前渲染的实例图」，
            // batchmode 没有 GameLoop，回归等价成「有实例就给它」。
            Server.Field.MapReactorService.Clock = () => clock;
            Server.Field.MapReactorService.ScriptSource = ScriptSource.LoadKey;
            Server.Field.MapReactorService.EventInstance = _ => NativeEventScripts.Instance();
            Server.Field.MapReactorService.Player = _ => PlayerHost();

            CheckLobbyMenu(db, cid);
            CheckPortalLobby();
            CheckHappyPath(db, cid);
            CheckFloor1Incomplete(db, cid);
            CheckTowerMobGuards(db, cid);
            CheckEventMobRoam(db, cid);
            CheckFloor13Leak(db, cid);
            CheckFloor13RockDeath(db, cid);
            CheckFloor14Ammo(db, cid);
            CheckFloor14Deadline(db, cid);
            CheckFloor19TooFar(db, cid);
            CheckFloor19FletaDeath(db, cid);
            CheckFloor18Reactor(db, cid);
            CheckTimeoutAward(db, cid);
            CheckVoluntaryExit(db, cid);
            CheckDeathAward(db, cid);
            CheckLeaveMap(db, cid);
            CheckDisconnect(db, cid);
            CheckPartyOverload(db, cid);
            Require(errors.Count == 0, "全程不应出现 Error/Exception 日志：" + string.Join(" | ", errors));

            foreach (var host in UnityEngine.Object.FindObjectsOfType<NativeEventScripts>())
                UnityEngine.Object.DestroyImmediate(host.gameObject);
        }
        catch (Exception error)
        {
            Debug.LogError("[SeedTower] 失败：" + error);
            exitCode = 1;
        }
        finally
        {
            Application.logMessageReceived -= log;
            NpcSession.CloseActive();
            SetField(typeof(NativeEventScripts), "Clock", (Func<long>)(() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            SetField(typeof(JsDate), "Clock", oldJsClock);
            Server.Field.MapReactorService.Clock = oldReactorClock;
            Server.Field.MapReactorService.ScriptSource = oldScriptSource;
            Server.Field.MapReactorService.EventInstance = oldEventInstance;
            Server.Field.MapReactorService.Player = oldReactorPlayer;
            GameServer.Stop(); Set("InGame", false);
            dataField.SetValue(null, oldData); savingField.SetValue(null, oldSaving); Profile.LastMapId = oldMap;
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
        Debug.Log("[SeedTower] 结果：通过 " + passed + " 项，退出码 " + exitCode);
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
    }

    // —— 大厅：菜单文案 / 起源宝库入口 / 积分展示 ——
    static void CheckLobbyMenu(DbStore db, int cid)
    {
        Profile.LastMapId = LobbyMap;
        var session = NpcSession.Talk(Alice, null);
        Require(session != null && session.LastError == null, "大厅脚本：" + session?.LastError);
        string text = session.Dialog.RawText;
        Require(text.Contains("起源积分：#r0#k 分"), "新角色起源积分 0：" + text);
        Require(text.Contains("累计：0 分"), "新角色累计 0 分：" + text);
        Require(text.Contains("挑战前 20 层") && text.Contains("返回射手村"), "大厅给挑战/返回入口");
        Require(text.Contains("玩法说明") && text.Contains("积分商店") && text.Contains("起源抽奖") && text.Contains("奖池预览"),
            "起源宝库四项都在：" + text);
        Require(DialogFlow.Links(session.Dialog.Content).Count == 7, "大厅共 7 个选项");
        Step("大厅菜单：积分/累计读数 + 宝库四项入口 + 7 个选项");

        session.Reply(1, 3);
        Require(session.LastError == null, "查看当前积分：" + session.LastError);
        Require(session.Dialog.RawText.Contains("当前积分：#r0#k 分"), "积分面板读数：" + session.Dialog.RawText);
        Require(session.Dialog.RawText.Contains("每通过 1 层得 2 分"), "积分面板写明规则");
        NpcSession.CloseActive();
        Step("大厅[查看当前积分]按原脚本取 extend_value");

        var shop = NpcSession.Talk(Alice, null);
        shop.Reply(1, 4);
        Require(shop.LastError == null, "打开积分商店：" + shop.LastError);
        var shopPage = NpcSession.Active;
        Require(shopPage != null && shopPage.LastError == null, "积分商店子脚本：" + shopPage?.LastError);
        Require(shopPage.Dialog.Visible, "积分商店有内容");
        NpcSession.CloseActive();
        Step("大厅[积分商店]走 openNpc(9900001,\"起源之塔/积分商店\")");

        var back = NpcSession.Talk(Alice, null);
        back.Reply(1, 1);
        Require(back.LastError == null, "返回射手村：" + back.LastError);
        Require(Profile.LastMapId == ReturnMap, "返回射手村 100000000，实际 " + Profile.LastMapId);
        NpcSession.CloseActive();
        Step("大厅[返回射手村]按原脚本 warp 100000000");
    }

    // —— 大厅门外两道门 + 塔外踩塔门 ——
    static void CheckPortalLobby()
    {
        Profile.LastMapId = LobbyMap;
        Portal("ptOut");
        Require(Profile.LastMapId == ReturnMap, "ptOut 回射手村，实际 " + Profile.LastMapId);
        Step("大厅 ptOut 门回射手村");

        Profile.LastMapId = LobbyMap;
        Portal("go1F");
        var instance = NativeEventScripts.Instance();
        Require(instance != null && !(instance is JsNull), "go1F 直接起实例（等于点[挑战前 20 层]）");
        tower = instance;
        Require(Floor == 1 && Profile.LastMapId == Tower(1), "go1F 送进第 1 层");
        Num("e.invokeScriptFunction('end',e);return 1;");
        CloseRun("go1F 用例");
        Step("大厅 go1F 门按 seedTower.js 起实例并进第 1 层");

        // 塔外地图踩到塔门 + 没有事件实例：脚本把玩家送回大厅，不报错。
        Profile.LastMapId = Tower(3);
        Portal("out00");
        Require(Profile.LastMapId == LobbyMap, "塔图外的 seedTower 门送回大厅，实际 " + Profile.LastMapId);
        Step("非塔图上的 seedTower 门按原脚本把玩家送回大厅");
    }

    // —— 主用例：一路用真实 out00 门推完 20 层 ——
    static void CheckHappyPath(DbStore db, int cid)
    {
        StartTower();
        Require(EventMap(Tower(1)) != null && EventMap(Tower(1)).Scope < 0, "第 1 层是独立实例地图");
        Require(Flag("return e.isTimerStarted()&&e.getTimeLeft()>2990000&&e.getTimeLeft()<=3000000;"),
            "50 分钟计时按 remaining 启动");
        Require(EventMap(Tower(1)).Geometry.npcs.Exists(n => n.id == Alice && n.dynamic), "塔内艾丽西亚按 spawnNpc 落地");
        Step("第 1 层：实例图 / 50 分钟计时 / 塔内 NPC 就位");

        // 第 1 层：先打满 100 只普通水灵，再打超级水灵（顺序 A）
        Require(MobCountOnFloor(9309131) == 1, "第 1 层刷出古代超级水灵");
        Grind(9309046, 100, 1);
        Require(Prop("score") >= 100, "普通水灵打满 100 只，实际 " + Prop("score"));
        Require(Prop("completed") == 0, "只打满 100 只、超级水灵还活着不算过关");
        KillIds(9309131);
        Require(Prop("bossDown") == 1, "超级水灵记为已击败");
        Require(Prop("completed") == 1 && Prop("cleared") == 1, "两个条件都满足才 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(2) && Prop("cleared") == 1, "第 1 层 out00 进第 2 层");
        Step("第 1 层：100 只 + BOSS 两个条件都满足才过关");

        // 第 2 层：四块卡牌石碑。石碑只能「按上提交卡牌」，直接打不掉。
        var sealMap = EventMap(Tower(2));
        Require(sealMap != null && sealMap.Scope < 0, "第 2 层是独立实例地图");
        var keys = new[] { "b", "y", "r", "g" };
        var cardPortals = new[] { "blueCard", "yellowCard", "redCard", "greenCard" };
        var ranks = new int[4];
        for (int i = 0; i < 4; i++)
        {
            ranks[i] = Prop("seal." + keys[i]);
            Require(ranks[i] >= 1 && ranks[i] <= 7, "石碑 " + keys[i] + " 的封印等级在 1..7，实际 " + ranks[i]);
            Require(ReactorOid(2, keys[i]) > 0, "石碑 " + keys[i] + " 按 WZ 名字取得到");
            var seal = Server.Field.MapReactorService.Get(sealMap.Scope).Find(r => r.name == keys[i]);
            // enterFloor 里 resetReactorActions(rank-1)：石碑的初始态就是「封印等级 - 1」。
            Require(seal != null && seal.state == ranks[i] - 1,
                "石碑 " + keys[i] + " 初始态 = 封印等级-1（" + (seal?.state) + " vs " + (ranks[i] - 1) + "）");
        }
        var stone = Server.Field.MapReactorService.Get(sealMap.Scope).Find(r => r.name == "b");
        Require(stone != null && stone.id == 9260004, "石碑反应堆 id 9260004");
        int stoneState = stone.state;
        Require(!Server.Field.MapReactorService.HitPlayer(sealMap.Scope, stone.objectId, cid),
            "第 2 层石碑禁止直接打（allowReactorHit=false）");
        Require(stone.state == stoneState, "被拦下的攻击不改石碑状态");
        Step("第 2 层：石碑 b/y/r/g 就位，物理攻击被 allowReactorHit 拦下");

        for (int i = 0; i < 4; i++)
        {
            string key = keys[i];
            int rank = ranks[i];
            // seedPortal：卡牌 id = 4009900 + 色序号*7 + (卡面数字-1)，只有「同色且卡面数字 > 封印等级」能过；
            // 没有更大的同色卡时 4009928 紫卡万能。
            if (rank > 1)
            {
                int low = 4009900 + i * 7 + (rank - 2);            // 卡面数字 = rank-1，不够大
                Gain(low, 1);
                Portal(cardPortals[i]);
                Require(Prop("sealDone." + key) == 0, "不够大的同色卡不通过（" + key + "）");
                Require(ItemCount(low) == 1, "失败的提交不消耗卡牌（" + key + "）");
                Gain(low, -1);
            }
            int ok = rank < 7 ? 4009900 + i * 7 + rank : 4009928;   // 卡面数字 = rank+1；rank=7 只剩紫卡
            Gain(ok, 1);
            Portal(cardPortals[i]);
            Require(Prop("sealDone." + key) == 1, "卡面更大的同色卡通过（" + key + "，封印 " + rank + "）");
            Require(ItemCount(ok) == 0, "提交后卡牌被收走（" + key + "）");
            var reactor = Server.Field.MapReactorService.Get(sealMap.Scope).Find(r => r.name == key);
            Require(reactor != null && reactor.state == 7, "石碑按 resetReactorActions(7) 置完成态，实际 " + reactor?.state);
        }
        Require(Prop("completed") == 1 && Prop("cleared") == 2, "四块石碑都完成后 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(3), "第 2 层 out00 进第 3 层");
        Step("第 2 层：正确卡牌提交四块石碑并 markClear（错卡不消耗也不通过）");

        // 第 3 层：乌龟蛋累计 1000 分，金蛋 50 分。
        Gain(4009497, 19);
        clock += 600; Tick();
        Require(Prop("score") == 950, "19 个金蛋 = 950 分，实际 " + Prop("score"));
        Require(Prop("completed") == 0, "950 分还没到 1000");
        Require(ItemCount(4009497) == 0, "tick 的 collect 把蛋收走");
        Gain(4009497, 1);
        clock += 600; Tick();
        Require(Prop("score") == 1000 && Prop("completed") == 1, "1000 分 markClear，实际 " + Prop("score"));
        Portal("out00");
        Require(Profile.LastMapId == Tower(4) && Prop("cleared") == 3, "第 3 层 out00 进第 4 层");
        Step("第 3 层：collect 收蛋计分，950 不过 / 1000 过关");

        // 第 4 层：平衡分 300。先打左侧（非 9309132）+5，打错 -1；满 50 次换边。
        KillIds(9309132);
        Require(Prop("score") == 0, "起手打错侧只扣到 0，不会变负（实际 " + Prop("score") + "）");
        Require(Prop("side") == 0, "起手在左侧");
        for (int guard = 0; Prop("score") < 300 && guard < 400; guard++)
        {
            int mob = Prop("side") == 0 ? 9309033 : 9309132;
            for (int i = 0; i < 8; i++) Spawn(mob, -400 + i * 90, 200);
            KillIds(mob);
        }
        Require(Prop("score") >= 300 && Prop("completed") == 1, "平衡分到 300 markClear，实际 " + Prop("score"));
        Portal("out00");
        // 第 5 层是休息层：enterFloor 里直接 markClear，所以一进门 cleared 就 +1（4 → 5）。
        Require(Profile.LastMapId == Tower(5) && Prop("cleared") == 5, "第 4 层 out00 进第 5 层并立即记一层");
        Step("第 4 层：错侧 -1 / 对侧 +5，50 次换边，300 分过关");

        // 第 5 层：休息层，进入即 markClear（停表）。
        Require(Prop("completed") == 1 && Prop("cleared") == 5, "第 5 层休息层立即 markClear");
        Require(!Flag("return e.isTimerStarted();"), "休息层停表");
        Portal("out00");
        Require(Profile.LastMapId == Tower(6), "第 5 层 out00 进第 6 层");
        Step("第 5 层：休息层进入即完成并停表");

        // 第 6 层：300 只水灵，每 30 秒来两只水灵王（每只计 30 只）。
        Grind(9309001, 270, 6);
        Require(Prop("completed") == 0, "270 只还没到 300");
        clock += 30000; Tick();
        Require(Prop("kingWave") == 1, "30 秒刷水灵王波次");
        Require(MobCountOnFloor(9309127) >= 1, "水灵王已刷出");
        Require(KillIds(9309127) >= 1 && Prop("score") >= 300, "水灵王每只计 30 只，实际 " + Prop("score"));
        Require(Prop("completed") == 1, "300 只 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(7) && Prop("cleared") == 6, "第 6 层 out00 进第 7 层");
        Step("第 6 层：普通水灵 + 30 秒水灵王波次凑满 300 只");

        // 第 7 层：爬到顶端出口就完成（出口门自己 markClear）。
        Require(Prop("completed") == 0, "第 7 层进门时还没完成");
        Portal("out00");
        Require(Profile.LastMapId == Tower(8) && Prop("cleared") == 7, "第 7 层 out00 = 到顶，直接进第 8 层");
        Step("第 7 层：out00 视为到顶并 markClear");

        // 第 8 层：100 只火山虫 + 火柱（命中只把 HP/MP 压到 1%，不掉命）。
        int maxHp = Num("return Number(p.getCurrentMaxHp());");
        clock += 9500; Tick();
        Require(PropLong("warningAt") > 0, "第 8 层按 9 秒间隔排下火柱预警");
        clock += 1900; Tick();
        int hp = Num("return Number(p.getHp());");
        Require(hp == Math.Max(1, (int)Math.Floor(maxHp * 0.01)), "火柱命中把 HP 压到 1%，实际 " + hp + "/" + maxHp);
        Require(Flag("return p.isAlive();"), "火柱不致死");
        Require(MobCountOnFloor(9309400) == 1, "火柱视觉怪 9309400 按 spawn 落地");
        clock += 1200; Tick();
        Require(MobCountOnFloor(9309400) == 0, "火柱 1 秒后自行消失");
        Grind(9309044, 100, 8);
        Require(Prop("completed") == 1, "100 只火山虫 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(9) && Prop("cleared") == 8, "第 8 层 out00 进第 9 层");
        Step("第 8 层：火柱预警 → 命中压到 1% HP 但不致死 → 100 只过关");

        // 第 9 层：八段三选一。先故意错一次退段，再按正确答案走完八段。
        int answer1 = Prop("answer.1");
        Portal("pt01_" + (answer1 == 1 ? 2 : 1));
        Require(Prop("solved.1") == 0 && Prop("puzzle") == 0, "选错不算通过并退回上一段");
        for (int stage = 1; stage <= 8; stage++)
        {
            Portal("pt0" + stage + "_" + Prop("answer." + stage));
            Require(Prop("puzzle") == stage, "第 " + stage + " 段解答推进，实际 " + Prop("puzzle"));
        }
        Require(Prop("completed") == 1, "八段全对 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(10) && Prop("cleared") == 9, "第 9 层 out00 进第 10 层");
        Step("第 9 层：错选退段、八段全对过关");

        // 第 10 层：巨大蜘蛛。25 秒织网（反伤），卵被打会裂出三只幼蛛。
        clock += 25000; Tick();
        Require(PropLong("webAt") > 0, "25 秒织网计时写入");
        Require(MobCountOnFloor(9309209) == 1, "织网时掉下一颗蜘蛛卵");
        int before = Count(10);
        KillIds(9309209);
        Require(Count(10) >= before + 2, "蜘蛛卵裂出三只幼蛛，实际 " + Count(10));
        KillIds(9309201);
        Require(Prop("completed") == 1, "击败巨大蜘蛛 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(11) && Prop("cleared") == 10, "第 10 层 out00 进第 11 层");
        Step("第 10 层：织网 / 蜘蛛卵 / 幼蛛按原脚本，击败蜘蛛过关");

        // 第 11 层：300 只花石巨人。
        Grind(9309102, 300, 11);
        Require(Prop("completed") == 1, "300 只 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(12) && Prop("cleared") == 11, "第 11 层 out00 进第 12 层");
        Step("第 11 层：300 只花石巨人过关");

        // 第 12 层：弹射点 + 随机陷阱都在层内处理，出口 markClear。
        Portal("impact00");
        Require(Profile.LastMapId == Tower(12), "impact00 在层内弹射，不换图");
        for (int trap = 0; trap <= 4; trap++)
        {
            Portal("trap0" + trap);
            Require(Profile.LastMapId == Tower(12), "trap0" + trap + " 在层内处理");
        }
        Portal("out00");
        Require(Profile.LastMapId == Tower(13) && Prop("cleared") == 12, "第 12 层 out00 进第 13 层");
        Step("第 12 层：弹射点 / 五个陷阱层内处理，出口过关");

        // 第 13 层：守石碑五波共 80 只（一波 16 只，清完自动下一波）。
        Require(Prop("wave") == 1 && Count(13) >= 16, "进层刷第一波 16 只，实际 " + Count(13));
        for (int guard = 0; Prop("score") < 80 && guard < 40; guard++)
            KillIds(9309000, 9309001, 9309002, 9309003);
        Require(Prop("score") >= 80 && Prop("completed") == 1, "80 只 markClear，实际 " + Prop("score"));
        Require(Prop("leaks") == 0, "全程没有漏怪");
        Portal("out00");
        Require(Profile.LastMapId == Tower(14) && Prop("cleared") == 13, "第 13 层 out00 进第 14 层");
        Step("第 13 层：五波共 80 只守石碑过关，全程无漏怪");

        // 第 14 层：普攻 10 黄 5 红。前三次空挥免费，第四次起每次扣 30 秒。
        Require(SeedTowerCompat.HandleAttack(100, 0), "第 14 层的任何攻击都被脚本接管");
        Require(Prop("shots") == 0, "非法技能不计弹药（脚本回[本层只能使用普通攻击]）");
        int remaining0 = Prop("remaining");
        for (int miss = 1; miss <= 4; miss++)
        {
            clock += 220;
            Require(SeedTowerCompat.HandleAttack(0, 0), "空挥也走 seedAttack 记账");
            Require(Prop("misses") == miss, "空挥计数 " + miss);
            int remaining = Prop("remaining");
            if (miss <= 3) Require(remaining == remaining0, "前三次空挥免费，实际 " + remaining);
            else Require(remaining0 - remaining >= 30000 && remaining0 - remaining <= 32000,
                "第四次空挥扣 30 秒，实际扣 " + (remaining0 - remaining));
        }
        for (int hit = 0; hit < 15; hit++)
        {
            int want = Prop("yellow") < 10 ? BatYellow : BatRed;
            int oid = Num("var m=e.getInstanceMap(" + Tower(14) + "),arr=m.getAllMonsters();" +
                          "for(var i=0;i<arr.size();i++)if(Number(arr.get(i).getId())==" + want + ")" +
                          "return Number(arr.get(i).getObjectId());return 0;");
            Require(oid > 0, "第 14 层有可射击的蝙蝠菇");
            clock += 220;
            Require(SeedTowerCompat.HandleAttack(0, oid), "第 14 层普攻被 seedAttack 接管");
        }
        Require(Prop("yellow") == 10 && Prop("red") == 5, "10 黄 5 红，实际 " + Prop("yellow") + "/" + Prop("red"));
        Require(Prop("completed") == 1, "打完 10 黄 5 红 markClear");
        Require(Prop("shots") == 19, "共 19 发（4 次空挥 + 15 次命中），实际 " + Prop("shots"));
        Portal("out00");
        Require(Profile.LastMapId == Tower(15) && Prop("cleared") == 15, "第 14 层 out00 进第 15 层并立即记一层");
        Step("第 14 层：非法技能拒绝 / 空挥扣时 / 10 黄 5 红命中过关");

        // 第 15 层：休息层。
        Require(Prop("completed") == 1 && Prop("cleared") == 15, "第 15 层休息层立即 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(16), "第 15 层 out00 进第 16 层");
        Step("第 15 层：休息层进入即完成");

        // 第 16 层：200 条鳄鱼。
        Grind(9309004, 200, 16);
        Require(Prop("completed") == 1, "200 条 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(17) && Prop("cleared") == 16, "第 16 层 out00 进第 17 层");
        Step("第 16 层：200 条鳄鱼过关");

        // 第 17 层：黑暗沼泽，走到出口才过（掉落判定依赖客户端坐标，实机复核）。
        Portal("out00");
        Require(Profile.LastMapId == Tower(18) && Prop("cleared") == 17, "第 17 层 out00 过关并进第 18 层");
        Step("第 17 层：out00 过关");

        // 第 18 层：10 个椰子 + 清空螃蟹。四击椰子树是 9922003，三击的是 9922002 系列。
        for (int coconut = 0; coconut < 10; coconut++)
        {
            var tree = Server.Field.MapReactorService.Get(EventMap(Tower(18)).Scope).Find(r => r.name == "seed" + (coconut % 3));
            Require(tree != null && tree.id == 9922003, "第 18 层四击椰子树 9922003 就位");
            Require(tree.template != null, "椰子树反应堆模板已加载（Templates=" + Server.Field.MapReactorService.Templates.Count + "）");
            int dropped = GroundCount(4000968);
            for (int hit = 0; hit < 4; hit++)
            {
                clock += 600;
                Require(Server.Field.MapReactorService.HitPlayer(EventMap(Tower(18)).Scope, tree.objectId, cid),
                    "椰子树第 " + (hit + 1) + " 击（alive=" + tree.alive + " state=" + tree.state +
                    " maxState=" + tree.maxState + " events=" + (tree.template?.Events(tree.state)?.Count ?? -1) + "）");
            }
            Require(GroundCount(4000968) == dropped + 1, "四击椰子在地面上掉 1 个椰子（" + GroundCount(4000968) + "）");
            Require(PickUp(4000968) == 1, "椰子捡进背包");
            clock += 16000; Tick();
            Require(tree.state == 0, "15 秒重生锁到期后置回 state 0");
        }
        Require(Prop("score") >= 10, "collect 收到 10 个椰子，实际 " + Prop("score"));
        Require(Prop("completed") == 1, "10 个椰子 + 清空螃蟹 markClear");
        Portal("out00");
        Require(Profile.LastMapId == Tower(19) && Prop("cleared") == 18, "第 18 层 out00 进第 19 层");
        Step("第 18 层：四击椰子树 → 掉椰子 → 15 秒重生 → 10 个过关");

        // 第 19 层：护送 Fleta。tick 按「距 Fleta 距离」累计超时；路线走完 + 距离 <250 才过。
        Require(Prop("escort") > 0, "第 19 层刷出 Fleta");
        int escortOid = Prop("escort");
        for (int guard = 0; Prop("completed") == 0 && guard < 600; guard++)
        {
            string xy = Text("var mob=e.getInstanceMap(" + Tower(19) + ").getMonsterByOid(" + escortOid + ");" +
                             "var v=mob?mob.getPosition():null;return v?(v.x+','+v.y):'0,0';");
            var parts = xy.Split(',');
            SetField(typeof(NpcApi), "HeadlessPosition", new Vector2(float.Parse(parts[0]), float.Parse(parts[1])));
            clock += 1000; Tick();
        }
        Require(Prop("completed") == 1, "跟着 Fleta 走完全程 markClear");
        Require(Prop("far") == 0, "全程距离都在 500 以内");
        Portal("out00");
        Require(Profile.LastMapId == Tower(20) && Prop("cleared") == 19, "第 19 层 out00 进第 20 层");
        Step("第 19 层：跟着 Fleta 走完全程过关");

        // 第 20 层：铁皮伐木工。补油预警 6 秒，入口按上咆哮可打断，否则回 10%。
        var boss = NativeEventMap.FindByHost(Eval("return e.getInstanceMap(" + Tower(20) + ").getMonsterById(9309205);"));
        Require(boss != null, "第 20 层刷出铁皮伐木工");
        boss.Hp = Math.Max(1, boss.MaxHp / 2);         // 模拟玩家已经打掉一半血，补油才有可回的量
        int bossHp = Num("var b=e.getInstanceMap(" + Tower(20) + ").getMonsterById(9309205);return b?Number(b.getHp()):0;");
        Require(bossHp > 0, "伐木工血量读得到，实际 " + bossHp);
        clock += 31000; Tick();
        Require(PropLong("oilAt") > 0, "30 秒后开始补油预警");
        clock += 6100; Tick();
        int healed = Num("var b=e.getInstanceMap(" + Tower(20) + ").getMonsterById(9309205);return b?Number(b.getHp()):0;");
        Require(healed > bossHp, "没打断的补油给伐木工回血（" + bossHp + " → " + healed + "）");
        clock += 30000; Tick();
        Require(PropLong("oilAt") > 0, "下一轮补油预警");
        Portal("roar");
        Require(Prop("oilAt") == 0, "咆哮打断补油");
        KillIds(9309205);
        Require(Prop("completed") == 1 && PropText("award") == "clear", "击败伐木工 award=clear");
        Require(Prop("cleared") == 20, "通关记 20 层，实际 " + Prop("cleared"));
        clock += 3100; Tick();
        CloseRun("通关用例");
        Step("第 20 层：补油回血 / 咆哮打断 / 击败伐木工 → clear");

        Require(ExtendValue(db, cid, PointKey) == "40", "通关发 40 分，实际 " + ExtendValue(db, cid, PointKey));
        Require(ExtendValue(db, cid, TotalKey) == "40", "累计也 +40，实际 " + ExtendValue(db, cid, TotalKey));
        Step("通关结算：起源积分 +40 / 累计 +40");

        // 重登：CharService 重读，再从大厅 NPC 看到同一份数字。
        Set("Character", CharService.Load(db, cid)); GameSession.ApplyToProfile();
        Profile.LastMapId = LobbyMap;
        var again = NpcSession.Talk(Alice, null);
        Require(again != null && again.LastError == null, "重登后大厅：" + again?.LastError);
        Require(again.Dialog.RawText.Contains("起源积分：#r40#k 分"), "重登后读到 40 分：" + again.Dialog.RawText);
        NpcSession.CloseActive();
        Step("重登后积分仍然在（extend_value 落库）");
    }

    // —— 第 1 层「只打满 100 只不打 BOSS」不通关 ——
    static void CheckFloor1Incomplete(DbStore db, int cid)
    {
        StartTower();
        Grind(9309046, 100, 1);
        Require(Prop("completed") == 0, "第 1 层 100 只但 BOSS 存活不算过");
        Portal("out00");
        Require(Profile.LastMapId == Tower(1), "没过关时 out00 把人留在本层，实际 " + Profile.LastMapId);
        Num("e.invokeScriptFunction('end',e);return 1;");
        CloseRun("第 1 层未通关用例");
        Step("第 1 层：BOSS 没打就走出门会被挡在门口，人留在本层");
    }

    // —— 第 2 层石碑不该能被物理攻击；seedReactorHit 只接管第 18 层 ——
    static void CheckTowerMobGuards(DbStore db, int cid)
    {
        StartTower();
        Jump(2);
        var map = EventMap(Tower(2));
        var stone = Server.Field.MapReactorService.Get(map.Scope).Find(r => r.name == "y");
        Require(stone != null, "第 2 层黄色石碑就位");
        // 石碑初始态是 enterFloor 里 resetReactorActions(rank-1)，不是 0（封印等级每局随机）。
        int before = stone.state;
        Require(before == Prop("seal.y") - 1, "石碑初始态 = 封印等级-1（" + before + "）");
        Require(!Server.Field.MapReactorService.HitPlayer(map.Scope, stone.objectId, cid), "石碑禁止直接打");
        Require(stone.state == before, "被拦的攻击不改状态（" + stone.state + " vs " + before + "）");
        Require(!Flag("return e.invokeScriptFunction('seedReactorHit',e,p," +
                      "e.getInstanceMap(" + Tower(2) + ").getReactorByName('y'));"),
            "seedReactorHit 只接管第 18 层");
        Require(SeedTowerCompat.ProtectsTarget(Tower(8), 9309400), "第 8 层火柱受 protectsTarget 保护");
        Require(SeedTowerCompat.ProtectsTarget(Tower(13), 9309402), "第 13 层落石受 protectsTarget 保护");
        Require(!SeedTowerCompat.ProtectsTarget(Tower(13), 9309403), "9309403 不在这张表里");
        Require(!SeedTowerCompat.ProtectsTarget(Tower(8), 9309044), "第 8 层普通怪不受保护");
        Num("e.invokeScriptFunction('end',e);return 1;");
        CloseRun("第 2 层拦截用例");
        Step("反应堆/受击白名单：石碑与火柱落石按原表拦下，普通怪不受影响");
    }

    // —— 事件怪表现层：巡逻范围不能是单点 ——
    // 反馈「起源之塔怪物只会原地闪」的根因：SpawnNativeMob 曾把 roamMin/roamMax 压成
    // 出生点单点，Roam 每帧翻转 patrolDir 并把 transform.x 钉回出生点，于是怪原地播走路
    // 动画 + 逐帧左右镜像翻转。逻辑层回归（无渲染）测不到，这里实际生成一只真实 EnemyUnit
    // 断言范围与零宽兜底。
    static void CheckEventMobRoam(DbStore db, int cid)
    {
        var holder = new GameObject("SeedTowerRoamFixture");
        try
        {
            var field = holder.AddComponent<FieldDirector>();
            field.enabled = false;                       // batchmode 不跑 Update，避免依赖 GameLoop
            typeof(FieldDirector).GetField("<Map>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(field, new FieldMap { id = LobbyMap });
            var enemy = field.SpawnNativeMob(2220000, Vector3.zero);
            Require(enemy != null, "动态怪可生成");
            enemy.enabled = false;
            Require(enemy.roamMax - enemy.roamMin >= 5.99f,
                "动态怪巡逻范围不能压成单点（实际 " + enemy.roamMin + ".." + enemy.roamMax + "）");

            // 兜底：即便被外部改回零宽，Roam 也不得翻转朝向或钉回出生点。
            enemy.roamMin = enemy.roamMax = 0f;
            var roam = typeof(EnemyUnit).GetMethod("Roam", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = { 0.016f, 1 };
            Require(!(bool)roam.Invoke(enemy, args), "零宽巡逻区不再返回 moving");
            Require((int)args[1] == 1, "零宽时朝向保持不变（不再逐帧镜像）");
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); }
        Step("事件怪巡逻范围非零；零宽兜底不再逐帧镜像翻转（原地闪）");
    }

    // —— 第 13 层：漏 5 只 → 石碑被击碎 ——
    static void CheckFloor13Leak(DbStore db, int cid)
    {
        StartTower();
        Jump(13);
        // 落石预警会先把玩家打死，这条用例只测漏怪：把预警时间推远，让 hazard 不插手。
        //（warningAt 是脚本自己的「预警已排定」标记，推远等于「这一轮落石还在倒计时」。）
        SetProp("warningAt", (clock + 3600000).ToString());
        // 脚本自己的 tick 是 eim.schedule("tick",500)，所以每拍必须推进 ≥500ms 才会真的跑一拍；
        // dt=500 时怪走 55px，小于相邻生成点间距 65px，保证一次只过一只（否则会一拍漏两只、跳过 4）。
        int seen = 0;
        for (int guard = 0; Profile.LastMapId == Tower(13) && guard < 4000; guard++)
        {
            clock += 500; Tick();
            if (Profile.LastMapId == Tower(13)) seen = Math.Max(seen, Prop("leaks"));
        }
        Require(seen >= 4, "漏怪累计到 4 以上才结束，实际看到 " + seen);
        Require(Profile.LastMapId == LobbyMap, "第 5 只漏掉后挑战结束回大厅");
        CloseRun("第 13 层漏怪用例");
        Step("第 13 层：漏 5 只 → 石碑被击碎 → 挑战结束");
    }

    // —— 第 13 层：青色落石直接砸死玩家 ——
    static void CheckFloor13RockDeath(DbStore db, int cid)
    {
        StartTower();
        Jump(13);
        Require(Num("return Number(p.getHp());") > 0, "进层时角色活着");
        // 落石只在「和预警点同处」时才结算，预警点必须取角色真实坐标。
        var spot = Text("var v=p.getPosition();return v?(v.x+' '+v.y):'0 0';").Split(' ');
        SetProp("warning", "1");                       // 1 = 青色落石（addHP(-hp)）
        SetProp("warningAt", clock.ToString());        // 预警已到点
        SetProp("warningX", spot[0]); SetProp("warningY", spot[1]);
        clock += 600; Tick();                          // 拍子间隔 500ms，推 600 才保证真的跑一拍
        Require(Num("return Number(p.getHp());") == 0, "青色落石把 HP 清零");
        clock += 600; Tick();
        Require(Profile.LastMapId == LobbyMap, "角色死亡后挑战结束回大厅");
        CloseRun("第 13 层落石死亡用例");
        Step("第 13 层：青色落石致死 → 角色死亡 → 挑战结束");
    }

    // —— 第 14 层：99 发打空 → 弹药耗尽 ——
    static void CheckFloor14Ammo(DbStore db, int cid)
    {
        StartTower();
        Jump(14);
        for (int shot = 1; shot <= 98; shot++)
        {
            clock += 220;
            Require(SeedTowerCompat.HandleAttack(0, 0), "第 " + shot + " 发空挥被接管");
        }
        Require(Prop("shots") == 98 && Profile.LastMapId == Tower(14), "打到 98 发时还在本层");
        clock += 220;
        SeedTowerCompat.HandleAttack(0, 0);
        Require(Profile.LastMapId == LobbyMap, "第 99 发打完弹药耗尽，挑战结束回大厅");
        CloseRun("第 14 层弹药用例");
        Step("第 14 层：99 发打空 → 弹药耗尽结束");
    }

    // —— 第 14 层：3 分钟限时 ——
    static void CheckFloor14Deadline(DbStore db, int cid)
    {
        StartTower();
        Jump(14);
        clock += 181000; Tick();
        Require(Profile.LastMapId == LobbyMap, "3 分钟到点挑战结束回大厅");
        CloseRun("第 14 层 3 分钟用例");
        Step("第 14 层：3 分钟限时到点结束");
    }

    // —— 第 19 层：离 Fleta 超过 500 持续 10 秒 ——
    static void CheckFloor19TooFar(DbStore db, int cid)
    {
        StartTower();
        Jump(19);
        for (int guard = 0; Profile.LastMapId == Tower(19) && guard < 60; guard++)
        {
            clock += 1000; Tick();
        }
        Require(Profile.LastMapId == LobbyMap, "距离超 500 持续 10 秒后挑战结束");
        CloseRun("第 19 层距离用例");
        Step("第 19 层：离 Fleta 太远 10 秒 → 护送失败");
    }

    // —— 第 19 层：Fleta 死亡 ——
    static void CheckFloor19FletaDeath(DbStore db, int cid)
    {
        StartTower();
        Jump(19);
        int escort = Prop("escort");
        Require(escort > 0, "Fleta 已刷出");
        Num("var m=e.getInstanceMap(" + Tower(19) + "),mob=m.getMonsterByOid(" + escort + ");" +
            "if(mob)m.killMonster(mob,p,false);return 1;");
        Require(Profile.LastMapId == Tower(19), "Fleta 刚死还没结算（要等下一拍 tick）");
        clock += 600; Tick();
        Require(Profile.LastMapId == LobbyMap, "Fleta 死亡后挑战结束回大厅");
        CloseRun("第 19 层 Fleta 死亡用例");
        Step("第 19 层：Fleta 死亡 → tick 的 escort.isAlive() 判定 → 护送失败");
    }

    // —— 第 18 层反应堆：节流 / 未打满不掉落 / 三击树两条分支 ——
    static void CheckFloor18Reactor(DbStore db, int cid)
    {
        StartTower();
        Jump(18);
        var map = EventMap(Tower(18));
        var tree = Server.Field.MapReactorService.Get(map.Scope).Find(r => r.name == "seed0");
        Require(tree != null && tree.id == 9922003, "四击椰子树就位");
        var thin = Server.Field.MapReactorService.Get(map.Scope).Find(r => r.id == 9922002);
        Require(thin != null, "三击椰子树（9922002）就位");

        clock += 600;
        Require(Server.Field.MapReactorService.HitPlayer(map.Scope, tree.objectId, cid), "第一击放行");
        Require(!Server.Field.MapReactorService.HitPlayer(map.Scope, tree.objectId, cid), "500ms 内第二击被 seedReactorHit 节流");
        Require(tree.state == 1, "被节流的攻击不推进状态，实际 " + tree.state);
        Require(GroundCount(4000968) == 0, "没打满 4 下不掉椰子");
        clock += 600;
        Require(Server.Field.MapReactorService.HitPlayer(map.Scope, tree.objectId, cid), "恢复后可继续打");
        clock += 600; Require(Server.Field.MapReactorService.HitPlayer(map.Scope, tree.objectId, cid), "第三击");
        clock += 600; Require(Server.Field.MapReactorService.HitPlayer(map.Scope, tree.objectId, cid), "第四击");
        Require(tree.state == 4, "四击后到 state 4");
        Require(GroundCount(4000968) >= 1, "四击树必掉 1 个椰子（地面掉落）");
        Step("第 18 层：500ms 节流 / 未打满不掉落 / 四击树必掉椰子");

        // 三击树：2/3 掉 1~2 个椰子，1/3 裂出三只螃蟹；每轮都要等 15 秒重生锁。
        int crabs = MobCountOnFloor(9309006);
        int extra = 0;
        for (int round = 0; round < 6; round++)
        {
            clock += 16000; Tick();
            Require(thin.state == 0, "三击树重生回 state 0，实际 " + thin.state);
            int coconuts = GroundCount(4000968);
            for (int hit = 0; hit < 3; hit++)
            {
                clock += 600;
                Require(Server.Field.MapReactorService.HitPlayer(map.Scope, thin.objectId, cid), "三击树第 " + (hit + 1) + " 击");
            }
            extra += GroundCount(4000968) - coconuts;
        }
        int spawned = MobCountOnFloor(9309006) - crabs;
        Require(extra + spawned > 0, "三击树六轮里至少出过一次（椰子 " + extra + " / 螃蟹 " + spawned + "）");
        Require(thin.state >= 3, "三击树打完停在 state 3 及以上，实际 " + thin.state);
        Num("e.invokeScriptFunction('end',e);return 1;");
        CloseRun("第 18 层反应堆用例");
        Step("第 18 层：三击树的掉椰子/裂螃蟹两条分支都按原脚本");
    }

    // —— 总时间耗尽：按已通过层数 ×2 发分 ——
    static void CheckTimeoutAward(DbStore db, int cid)
    {
        int before = int.Parse(ExtendValue(db, cid, PointKey) ?? "0");
        StartTower();
        Jump(3);
        // 用脚本自己的 markClear 记 3 层「已通过」（3/4/5），再跳到第 6 层让计时器重新跑起来：
        // 休息层（5/15）不跑计时器，tick 里也跳过超时判定，停在休息层永远等不到 timeout。
        for (int floor = 3; floor <= 5; floor++)
        {
            Num("e.invokeScriptFunction('markClear',e);return 1;");
            Jump(floor + 1);
        }
        Require(Prop("cleared") == 3, "记 3 层已通过，实际 " + Prop("cleared"));
        Require(Flag("return e.isTimerStarted();"), "第 6 层计时器重新开始");
        clock += 3000000; Tick();                      // 50 分钟总时间耗尽
        Require(Profile.LastMapId == LobbyMap, "总时间耗尽后回大厅");
        CloseRun("超时用例");
        Require(ExtendValue(db, cid, PointKey) == (before + 6).ToString(),
            "超时按已过层数 ×2 = 6 分，实际 " + ExtendValue(db, cid, PointKey));
        Step("总时间耗尽：按已通过层数 ×2 结算（3 层 → 6 分）");
    }

    // —— 塔内 NPC「结束本次挑战」：主动退出 0 分 ——
    static void CheckVoluntaryExit(DbStore db, int cid)
    {
        int before = int.Parse(ExtendValue(db, cid, PointKey) ?? "0");
        StartTower();
        Jump(7);
        Num("e.invokeScriptFunction('markClear',e);return 1;");   // 已过 2 层，主动退出也不发分
        Profile.LastMapId = Tower(7);
        var session = NpcSession.Talk(Alice, null);
        Require(session != null && session.LastError == null, "塔内艾丽西亚：" + session?.LastError);
        Require(session.Dialog.RawText.Contains("继续挑战") && session.Dialog.RawText.Contains("结束本次挑战"),
            "塔内菜单只有继续/结束：" + session.Dialog.RawText);
        Require(DialogFlow.Links(session.Dialog.Content).Count == 2, "塔内菜单共 2 个选项");
        session.Reply(1, 1);
        Require(session.LastError == null, "结束本次挑战：" + session.LastError);
        NpcSession.CloseActive();
        Require(Profile.LastMapId == LobbyMap, "主动退出回大厅，实际 " + Profile.LastMapId);
        CloseRun("主动退出用例");
        Require(ExtendValue(db, cid, PointKey) == before.ToString(), "主动退出不发分");
        Step("塔内 NPC[结束本次挑战]：主动退出不发分");
    }

    // —— 死亡：tick 的 isAlive 判定 ——
    static void CheckDeathAward(DbStore db, int cid)
    {
        int before = int.Parse(ExtendValue(db, cid, PointKey) ?? "0");
        StartTower();
        Jump(11);
        Num("e.invokeScriptFunction('markClear',e);return 1;");
        Num("p.addHP(-Number(p.getHp()));return 1;");
        Require(!Flag("return p.isAlive();"), "HP 清零后 isAlive=false");
        clock += 600; Tick();
        Require(Profile.LastMapId == LobbyMap, "死亡后挑战结束回大厅");
        CloseRun("死亡用例");
        Require(ExtendValue(db, cid, PointKey) == before.ToString(), "死亡不发分");
        Step("角色死亡：tick 判 isAlive → 挑战结束且不发分");
    }

    // —— 中途离图：changedMap 发现换到别的图 → 结束实例 ——
    static void CheckLeaveMap(DbStore db, int cid)
    {
        StartTower();
        Jump(5);
        Profile.LastMapId = ReturnMap;
        NativeEventScripts.ChangedMap(ReturnMap, false);
        Require(NativeEventScripts.Instance() is JsNull, "离图后角色被摘出实例");
        // 源端 end() 里 chr.changeMap(exitMap)：中途离图也会被送回大厅，不是留在新图。
        Require(Profile.LastMapId == LobbyMap, "原脚本的 end() 把离图的玩家送回大厅，实际 " + Profile.LastMapId);
        CloseRun("离图用例");
        Step("中途离图：changedMap 摘人并结束实例");
    }

    // —— 掉线：Client.java:967-970 → eim.playerDisconnected → 脚本 cleanItems + unregister + end ——
    static void CheckDisconnect(DbStore db, int cid)
    {
        int before = int.Parse(ExtendValue(db, cid, PointKey) ?? "0");
        StartTower();
        Jump(9);
        Num("e.invokeScriptFunction('markClear',e);return 1;");   // 已过 2 层，掉线也不结算
        Gain(4000968, 1);
        Require(ItemCount(4000968) == 1, "掉线前背包里有塔内道具");
        var instance = NativeEventScripts.CurrentInstance();
        Require(instance != null, "掉线前角色还挂在实例上");

        instance.PlayerDisconnected(PlayerHost());

        Require(NativeEventScripts.Instance() is JsNull, "掉线后角色被摘出实例");
        Require(ItemCount(4000968) == 0, "脚本 cleanItems 清掉塔内道具");
        // 源端 playerDisconnected 是「先 unregisterPlayer 再 end()」，而 end() 只遍历还在实例里的
        // 玩家 —— 所以掉线的角色不会被 changeMap 拉回大厅，人停在塔里、实例已经释放。
        Require(Profile.LastMapId == Tower(9), "掉线不回大厅（脚本已先摘人），实际 " + Profile.LastMapId);
        clock += 12000; Tick();
        tower = JsNull.Value;
        Require(ExtendValue(db, cid, PointKey) == before.ToString(), "掉线不发分");
        Step("掉线：playerDisconnected → cleanItems + unregisterPlayer + end，0 分");
    }

    // —— 组队重载：源端 startInstance(party, map) ——
    static void CheckPartyOverload(DbStore db, int cid)
    {
        Profile.LastMapId = LobbyMap;
        var manager = NativeEventScripts.Manager("SeedTower20");
        Require(!new Probe("return em.startInstance(party,map);",
            ("em", manager), ("party", Party(false)), ("map", RecruitMap())).Bool(), "远端队长不能凭空拉起起源之塔实例");
        Require(new Probe("return em.startInstance(party,map);",
            ("em", manager), ("party", Party(true)), ("map", RecruitMap())).Bool(), "本地队长可走组队重载起实例");
        tower = NativeEventScripts.Instance();
        Require(!(tower is JsNull), "组队实例注册了本地角色");
        Require(Floor == 1 && Profile.LastMapId == Tower(1), "组队实例同样送进第 1 层");
        Num("e.invokeScriptFunction('end',e);return 1;");
        CloseRun("组队重载用例");
        Step("组队重载（party, map）用本地队长起实例（远端队长被拒绝）");
    }

    static JsValue RecruitMap()
    {
        var map = new JsHostObject(null);
        map.Set("getId", JsFunction.FromNative("getId", c => new JsNumber(LobbyMap)));
        return map;
    }
    static JsValue Party(bool localLeader)
    {
        int leaderId = localLeader ? GameSession.CharacterId : 999999;
        JsValue leader;
        if (localLeader) leader = PlayerHost();
        else
        {
            var remote = new JsHostObject(null);
            remote.Set("getId", JsFunction.FromNative("getId", c => new JsNumber(leaderId)));
            remote.Set("getMapId", JsFunction.FromNative("getMapId", c => new JsNumber(LobbyMap)));
            leader = remote;
        }
        var party = new JsHostObject(null);
        party.Set("getLeaderId", JsFunction.FromNative("getLeaderId", c => new JsNumber(leaderId)));
        party.Set("getLeader", JsFunction.FromNative("getLeader", c => leader));
        return party;
    }

    static string ExtendValue(DbStore db, int cid, string name)
    {
        var row = db.Table("extend_value").Find(row => DbStore.Str(row, "extend_id") == cid.ToString() &&
            DbStore.Int(row, "extend_type") == PointType && DbStore.Str(row, "extend_name") == name);
        return row == null ? null : DbStore.Str(row, "extend_value");
    }
}
