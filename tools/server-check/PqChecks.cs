using System;
using System.Collections.Generic;
using System.IO;
using MapleDash.Js;
using PqAction = Server.Bot.Types.Pq.PqAction;
using PqCatalog = Server.Bot.Types.Pq.PqCatalog;
using PqDef = Server.Bot.Types.Pq.PqDef;
using PqStage = Server.Bot.Types.Pq.PqStage;
using PqStep = Server.Bot.Types.Pq.PqStep;

// 组队任务（KPQ / LudiPQ / EllinPQ / PiratePQ）招募入口的回归。
//
// 症状：在废弃都市点拉克里斯（9020000）选「我想参加组队任务」直接弹
// 「对话脚本出错（详见日志）」。根因是 EventManager.getEligibleParty(Party) 没迁移：
// 源端它不是 Java 侧算的，而是把 party.getPartyMembersOnline() 交给事件脚本自己的
// getEligibleParty(party) 判等级/人数（EventManager.java:994）。四个 PQ 的招募 NPC
// 第一句都是它，所以四个副本会一起坏。
//
// 这里直接跑源端事件脚本的同一段判定，断言：
//   1. 合格的单人队长（solo expedition 开关打开）能拿到 1 个名额；
//   2. 等级不够 / 不在招募图 时返回空名单，而不是抛异常把对话打断。
public static class PqChecks
{
    // EventManager.getEligibleParty 的入参就是 recruitMap 上的队伍成员，
    // 四个副本的门槛来自各自事件脚本（KPQ 21 / LPQ 35 / EPQ 45 / PPQ 55）。
    sealed class Def
    {
        public string eventName;
        public int recruitMap, minLevel;
    }

    static readonly Def[] Defs =
    {
        new Def { eventName = "KerningPQ", recruitMap = 103000000, minLevel = 21 },
        new Def { eventName = "LudiPQ", recruitMap = 221024500, minLevel = 35 },
        new Def { eventName = "EllinPQ", recruitMap = 300030100, minLevel = 45 },
        new Def { eventName = "PiratePQ", recruitMap = 251010404, minLevel = 55 },
    };

    public static void Run(Action<bool, string> Check)
    {
        PartyIndexChecks(Check);
        CombatChecks(Check);
        StageOneChecks(Check);
        LudiChecks(Check);
        EllinChecks(Check);
        PirateChecks(Check);
        ReactorDropChecks(Check);
        AmbientDropChecks(Check);
        string dir = EventScriptDir();
        if (dir == null)
        {
            Console.WriteLine("  SKIP PqChecks：找不到源端 event 脚本目录");
            return;
        }

        foreach (var def in Defs)
        {
            string path = Path.Combine(dir, def.eventName + ".js");
            if (!File.Exists(path))
            {
                Console.WriteLine("  SKIP " + def.eventName + "：缺少 " + path);
                continue;
            }

            NativeEventScriptService service = null;
            NativeEventScriptService.Manager manager;
            try
            {
                service = new NativeEventScriptService(
                    key => ReadEvent(dir, key), () => 0,
                    engine => engine.Interp.ResolveHostType = ResolveHostType);
                manager = service.GetManager(def.eventName);
            }
            catch (Exception error)
            {
                Check(false, def.eventName + " 事件脚本加载/init 失败：" + error.Message);
                continue;
            }
            Check(manager != null, def.eventName + " 事件管理器可实例化");
            if (manager == null) continue;

            // 1) 合格：队长在招募图、等级超过门槛（solo 开关打开时 minPlayers=1）。
            var eligible = Call(manager.Host, "getEligibleParty", Party(def.recruitMap, def.minLevel + 5));
            Check(Size(eligible) == 1, def.eventName + " getEligibleParty 对合格队长返回 1 人（实际 " + Size(eligible) + "）");

            // 2) 等级不足：脚本必须自己过滤掉，返回空名单。
            var tooLow = Call(manager.Host, "getEligibleParty", Party(def.recruitMap, 1));
            Check(Size(tooLow) == 0, def.eventName + " 等级不足时返回空名单（实际 " + Size(tooLow) + "）");

            // 3) 不在招募图：同样被过滤。
            var wrongMap = Call(manager.Host, "getEligibleParty", Party(10000, def.minLevel + 5));
            Check(Size(wrongMap) == 0, def.eventName + " 队员不在招募图时返回空名单（实际 " + Size(wrongMap) + "）");

            // 4) 没队伍时源端直接给空表（EventManager.java:995），不能抛。
            var none = Call(manager.Host, "getEligibleParty", JsNull.Value);
            Check(Size(none) == 0, def.eventName + " 无队伍时返回空名单");
        }

        // 5) getMonster 是宿主注入的；没注入时要报明确错误，而不是静默返回 null
        //    （PiratePQ 的波次怪靠它，静默 null 会在 registerMonster 处炸得更难查）。
        var bare = new NativeEventScriptService(
            key => key == "event/KerningPQ" ? "function init() {}" : "", () => 0);
        var bareManager = bare.GetManager("KerningPQ");
        if (bareManager != null)
        {
            bool threw = false;
            try { Call(bareManager.Host, "getMonster", new JsNumber(9300114)); }
            catch (JsRuntimeException) { threw = true; }
            Check(threw, "未注入 MonsterFactory 时 em.getMonster 明确报错");
        }
    }

    // BotPartyService 有两套编号：角色 id（membership 的键）和队伍编号（nextId 从 1000000001 起）。
    // For()/Get() 收角色 id，ById() 收队伍编号。PqRun.Begin 曾经拿队伍编号去 For()，
    // 结果稳定是 null —— 表现就是「真人进了副本，队伍里的假人全留在招募图」。
    static void PartyIndexChecks(Action<bool, string> Check)
    {
        var db = DbStore.Create(Path.Combine(Path.GetTempPath(), "maple-pq-party-" + Guid.NewGuid().ToString("N") + ".json"));
        var parties = new Server.Bot.BotPartyService(db);
        var leader = new Server.Bot.BotActor { id = 1001, name = "Leader", level = 50, world = 0, mapId = 103000000 };
        var bot = new Server.Bot.BotActor { id = 900001, name = "Bot", level = 50, world = 0, mapId = 103000000 };

        Check(parties.Create(leader), "组队任务：真人可以建队");
        var party = parties.For(leader.id);
        Check(party != null && party.leader == leader.id, "组队任务：For(角色 id) 取到队伍");
        if (party == null) return;

        Check(parties.Join(bot, party.id), "组队任务：假人能加入队伍");
        var byId = parties.ById(party.id);
        Check(ReferenceEquals(byId, party), "组队任务：ById(队伍编号) 取到同一支队伍");
        Check(byId != null && byId.members.Count == 2,
            "组队任务：按队伍编号能取到全部成员（实际 " + (byId != null ? byId.members.Count : 0) + " 人）");
        // 契约守卫：两套编号不可混用。哪天 For() 被改成兼容队伍编号，这条会亮，提醒回来重看 Begin。
        Check(parties.For(party.id) == null, "组队任务：For(队伍编号) 不命中（编号语义不可混用）");
    }

    // 废都第一关的「人数 → 要几张证」口径回归。
    //
    // 源端 npc/9020001.js:141-150 是队长分支：
    //   numpasses = eim.getPlayerCount() - 1;  if (cm.hasItem(4001008, numpasses)) clearStage(...)
    // 移植端以前只把本地角色登记进实例，所以这个数字恒为 0 —— 队长空手就能过关，
    // 「要收几张券」整段队员分支从来没跑过。这里锁住修复后的三条：
    //   1. 假人能登记进事件实例，PlayerCount 跟着涨（队长的 numpasses 才是真人数）；
    //   2. eim.gridCheck 的题目号能被 C# 侧读回，按题目表换算成要收几张券；
    //   3. 第一关的动作表必须是「领题 → 收券 → 换证 → 等队长 → 交证」，不能退回直发。
    static void StageOneChecks(Action<bool, string> check)
    {
        var kpq = Server.Bot.Types.Pq.PqCatalog.Get("KPQ");
        check(kpq != null && kpq.stages.Count >= 1, "KPQ 数据表可读");
        if (kpq == null) return;

        // —— 题库（源端 npc/9020001.js:38）——
        int[] expect = { 10, 35, 20, 25, 25, 30, 8 };
        bool answers = true;
        for (int i = 0; i < expect.Length; i++)
            if (Server.Bot.Types.Pq.PqCatalog.Stage1Answer(i + 1) != expect[i]) answers = false;
        check(answers, "KPQ 第一关 7 道题的答案表与脚本一致");
        check(Server.Bot.Types.Pq.PqCatalog.Stage1Answer(0) == 0 &&
              Server.Bot.Types.Pq.PqCatalog.Stage1Answer(8) == 0,
              "KPQ 越界题目号返回 0（没领到题）");

        // —— 2/3/4 关的随机组合表（源端 npc/9020001.js:49-60）——
        int[] lens = { 4, 5, 6 };
        string[] props = { "stg2Property", "stg3Property", "stg4Property" };
        for (int i = 0; i < props.Length; i++)
        {
            var combo = Server.Bot.Types.Pq.PqCatalog.Combo(props[i], 0);
            int ones = 0;
            if (combo != null) foreach (int v in combo) ones += v;
            check(combo != null && combo.Length == lens[i] && ones == 3,
                "KPQ " + props[i] + " 组合长度 " + lens[i] + " 且恰好 3 个位置要站人");
        }
        check(Server.Bot.Types.Pq.PqCatalog.Combo("stg2Property", 99) == null,
            "KPQ 组合行号越界返回 null（不乱站位）");

        // —— 第一关的动作表：必须是真流程 ——
        var stage1 = kpq.stages[0];
        bool hasAsk = false, hasCollect = false, hasExchange = false, hasWait = false, hasHandOver = false, direct = false;
        foreach (var step in stage1.steps)
        {
            if (step.action == Server.Bot.Types.Pq.PqAction.BotAsk) hasAsk = true;
            if (step.action == Server.Bot.Types.Pq.PqAction.BotCollect) hasCollect = true;
            if (step.action == Server.Bot.Types.Pq.PqAction.BotExchange) hasExchange = true;
            if (step.action == Server.Bot.Types.Pq.PqAction.BotWait) hasWait = true;
            if (step.action == Server.Bot.Types.Pq.PqAction.HandOver) hasHandOver = true;
            if (step.action == Server.Bot.Types.Pq.PqAction.GrantItem &&
                step.itemId == Server.Bot.Types.Pq.PqCatalog.PassTicket) direct = true;
        }
        check(hasAsk && hasCollect && hasExchange && hasWait && hasHandOver,
            "KPQ 第一关：领题→收券→换证→等队长→交证 五步齐全");
        check(!direct && stage1.requireItem == 0,
            "KPQ 第一关不再直发通行证（由队员交上来）");

        // 收券那一步必须留空 need / itemId：每人题目不同，靠 couponNeed 各收各的。
        // 写死 need 会让 WorkBotCollect 按「全队合计」判定，凑够几张就停（曾经填成 1 张）。
        var collectStep = stage1.steps.Find(s => s.action == Server.Bot.Types.Pq.PqAction.BotCollect);
        check(collectStep != null && collectStep.need == 0 && collectStep.itemId == 0,
            "KPQ 第一关：收券步不写死 need（按每人的题目各收各的）");
        check(collectStep != null && Array.IndexOf(collectStep.ids, 9300001) >= 0,
            "KPQ 第一关：只打鳄鱼 9300001（别把地图上别的怪当成收券目标）");

        // —— 「按怪种过滤」必须比模板 id，不能比运行时句柄 ——
        // BotGrindMob.id 是 Unity GetInstanceID()（随机大数），mobId 才是 9300001。
        // 以前三处过滤都拿 id 比，永远不相等 ⇒ 一步都选不出怪 ⇒ 假人站在 NPC 旁边不动。
        var croc = new Server.Bot.BotGrindMob { id = 88990011, mobId = 9300001 };
        check(Server.Bot.Types.Pq.PqRun.IsWanted(collectStep, croc),
            "KPQ 第一关：模板 id 命中（运行时句柄 ≠ 模板 id 也要认出来）");
        var other = new Server.Bot.BotGrindMob { id = 9300001, mobId = 9300002 };
        check(!Server.Bot.Types.Pq.PqRun.IsWanted(collectStep, other),
            "KPQ 第一关：句柄恰好等于模板 id 也不能误判成鳄鱼");
        var legacy = new Server.Bot.BotGrindMob { id = 9300001, mobId = 0 };
        check(Server.Bot.Types.Pq.PqRun.IsWanted(collectStep, legacy),
            "旧数据没填 mobId 时退回按 id 判定（自检/老存档兼容）");

        // 两人同处低层时也应分工追全图目标；目标死亡后可重选。
        var hunt = new Server.Bot.Types.Pq.PqRun(null, 1, 1);
        var a = new Server.Bot.BotCharacter { id = 987610, mapId = 103000800 };
        var b = new Server.Bot.BotCharacter { id = 987611, mapId = 103000800 };
        var huntFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var members = (System.Collections.Generic.List<int>)typeof(Server.Bot.Types.Pq.PqRun).GetField("bots", huntFlags).GetValue(hunt);
        var select = typeof(Server.Bot.Types.Pq.PqRun).GetMethod("Nearest", huntFlags);
        var near = new Server.Bot.BotGrindMob { id = 1, alive = true, x = 50 };
        var far = new Server.Bot.BotGrindMob { id = 2, alive = true, x = 1800, y = -900 };
        var candidates = new System.Collections.Generic.List<Server.Bot.BotGrindMob> { near, far };
        Server.Bot.BotRegistry.Add(a); Server.Bot.BotRegistry.Add(b);
        members.Add(a.id); members.Add(b.id);
        try
        {
            check(ReferenceEquals(select.Invoke(hunt, new object[] { a, candidates }), near), "PQ：首名队员选近怪");
            check(ReferenceEquals(select.Invoke(hunt, new object[] { b, candidates }), far), "PQ：第二名队员选远处高层未认领怪，不受局部半径限制");
            far.x = 1900;
            check(ReferenceEquals(select.Invoke(hunt, new object[] { b, candidates }), far), "PQ：追逐中保持目标，不反复换层");
            far.alive = false;
            check(ReferenceEquals(select.Invoke(hunt, new object[] { b, candidates }), near), "PQ：只剩一怪时允许合击");
            far.alive = true; far.x = 600; far.y = -220;
            var crowded = new Server.Bot.BotGrindMob { id = 3, alive = true, x = 60 };
            candidates.Add(crowded);
            ((Dictionary<int,int>)typeof(Server.Bot.Types.Pq.PqRun).GetField("combatClaims", huntFlags).GetValue(hunt)).Remove(b.id);
            check(ReferenceEquals(select.Invoke(hunt, new object[] { b, candidates }), far), "PQ：邻近多只怪不再吸走全部队员，另一区域优先分工");
        }
        finally { Server.Bot.BotRegistry.Remove(a); Server.Bot.BotRegistry.Remove(b); }

        // 快照那一头也必须真的把模板 id 填进来，否则上面这层等于没有数据源。
        string combat = ReadProjectText("Assets/Scripts/Field/BotCombatField.cs");
        if (combat != null)
            check(combat.Contains("mobId=enemy.fieldMobId"),
                "BotCombatField 快照填了 mobId（模板 id），不是只给 Unity 句柄");

        // —— 队长要几张 = 实例人数-1：假人登记 / 题目号读回 ——
        NativeEventScriptService service = null;
        try
        {
            string dir = EventScriptDir();
            if (dir == null) { Console.WriteLine("  SKIP 实例计数检查：找不到源端脚本目录"); return; }
            service = new NativeEventScriptService(key => ReadEvent(dir, key), () => 0,
                engine => engine.Interp.ResolveHostType = ResolveHostType);
            var manager = service.GetManager("KerningPQ");
            var instance = manager.NewInstance("PQCountCheck");
            check(instance.PlayerCount == 0, "新实例初始人数 0");
            var leader = FakePlayer(90001, 103000800);
            instance.Register(leader, false);
            check(instance.PlayerCount == 1, "登记队长后人数 1（numpasses=0）");
            int bot1 = 90002;
            instance.Register(FakePlayer(bot1, 103000800), false);
            check(instance.PlayerCount == 2, "登记一名假人队友后人数 2（队长要 1 张证）");
            instance.Register(FakePlayer(90002, 103000800), false);
            check(instance.PlayerCount == 2, "同一名假人重复登记不重复计数");
            // eim.gridInsert(玩家, 3) → gridCheck 读回 3，C# 侧同一份数据
            var insert = instance.Host.Get("gridInsert") as JsFunction;
            insert.Native(new JsCall { This = instance.Host,
                Args = new JsValue[] { FakePlayer(bot1, 103000800), new JsNumber(3) } });
            check(instance.Grid(bot1) == 3 && instance.Grid(90003) == -1,
                "gridInsert 写入题目号，Grid 读回 3 / 未领题 -1");
            check(Server.Bot.Types.Pq.PqCatalog.Stage1Answer(instance.Grid(bot1)) == 20,
                "题目号 3 → 要收 20 张优惠券（源端 stage1Answers）");
            instance.Unregister(FakePlayer(bot1, 103000800));
            check(instance.PlayerCount == 1, "假人离开实例后人数回到 1");
        }
        catch (Exception error)
        {
            check(false, "实例计数检查抛出：" + error.Message);
        }
        finally
        {
            try { service?.Clear(); } catch (Exception) { }
        }

        // —— 交证：从假人背包扣、落到队长背包 ——
        var previousGrant = Server.Bot.Types.Pq.PqDirector.GrantItem;
        var previousItem = Server.Bot.Types.Pq.PqDirector.BotItem;
        var bot = new Server.Bot.BotCharacter { id = 91001, name = "PqHandOver", level = 30, mapId = 103000800 };
        bot.inventory.Add(new InventoryEntry { itemId = Server.Bot.Types.Pq.PqCatalog.PassTicket, inventoryType = 4, quantity = 1 });
        Server.Bot.BotRegistry.Add(bot);
        int granted = 0;
        try
        {
            Server.Bot.Types.Pq.PqDirector.GrantItem = (_, count) => granted += count;
            Server.Bot.Types.Pq.PqDirector.BotItem = (target, itemId, count) =>
            {
                for (int i = target.inventory.Count - 1; i >= 0 && count < 0; i--)
                {
                    var entry = target.inventory[i];
                    if (entry == null || entry.itemId != itemId) continue;
                    int take = Math.Min(entry.quantity, -count);
                    entry.quantity -= take; count += take;
                    if (entry.quantity <= 0) target.inventory.RemoveAt(i);
                }
            };
            var run = new Server.Bot.Types.Pq.PqRun(kpq, 1, 100001);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            ((List<int>)typeof(Server.Bot.Types.Pq.PqRun).GetField("bots", huntFlags).GetValue(run)).Add(bot.id);
            var hand = typeof(Server.Bot.Types.Pq.PqRun).GetMethod("WorkHandOver", flags);
            var step = new Server.Bot.Types.Pq.PqStep
            { action = Server.Bot.Types.Pq.PqAction.HandOver, itemId = Server.Bot.Types.Pq.PqCatalog.PassTicket };
            hand.Invoke(run, new object[] { step });
            check(granted == 1, "交证：队长背包收到 1 张通行证（实际 " + granted + "）");
            int left = 0;
            foreach (var entry in bot.inventory) if (entry != null && entry.itemId == Server.Bot.Types.Pq.PqCatalog.PassTicket) left += entry.quantity;
            check(left == 0, "交证：假人背包里的证被扣掉（剩 " + left + "）");
        }
        finally
        {
            Server.Bot.BotRegistry.Remove(bot);
            Server.Bot.Types.Pq.PqDirector.GrantItem = previousGrant;
            Server.Bot.Types.Pq.PqDirector.BotItem = previousItem;
        }
    }

    // ————————————————— 玩具塔101（LudiPQ，npc/2040036~2040044.js）—————————————————
    // 九关一个套路，四步缺一不可：
    //   ① 队长点 NPC 听规则（statusStgN: -1 → 0，只发一页说明，不做判定）
    //   ② 队员去打怪 / 砸箱子，把 4001022 攒够（第 9 关要 BOSS 掉的 4001023）
    //   ③ 队员把券交给队长（源端是当面交易）
    //   ④ 队长再点一次 NPC → cm.haveItem(...) → 扣券 + clearStage + linkToNextStage
    // 张数照抄脚本：25 / 15 / 32 / 6 / 24 / 3。以前这里全靠 Give 直发券、每关只点一次，
    // 脚本停在 statusStgN=0 永远不 clearStage，队伍卡在第一关。这组断言锁住真流程。
    static void LudiChecks(Action<bool, string> check)
    {
        var lpq = PqCatalog.Get("LPQ");
        check(lpq != null, "LPQ 数据表可读");
        if (lpq == null) return;

        int[] maps =
        {
            922010100, 922010200, 922010300, 922010400, 922010500, 922010600,
            922010700, 922010800, 922010900, 922011000, 922011100,
        };
        check(lpq.stages.Count == maps.Length, "LPQ 共 11 个阶段：9 关 + 两个奖励关（实际 " + lpq.stages.Count + "）");
        bool chain = lpq.stages.Count == maps.Length;
        for (int i = 0; i < maps.Length && i < lpq.stages.Count; i++)
            if (lpq.stages[i].mapId != maps[i]) chain = false;
        check(chain, "LPQ 阶段图顺序与源端 922010100 → 922011100 一致");

        int[] need = { 25, 15, 32, 6, 24, 3 };                 // 第 1~5 关、第 7 关
        int[] index = { 0, 1, 2, 3, 4, 6 };
        int[] npc = { 2040036, 2040037, 2040038, 2040039, 2040040, 2040042 };
        for (int i = 0; i < need.Length; i++)
        {
            var stage = lpq.stages[index[i]];
            int no = index[i] + 1;                       // 源端关卡号（第 7 关在上表的第 6 项）
            var watch = StepOf(stage, PqAction.Talk);
            var collect = StepOf(stage, PqAction.BotCollect);
            var hand = StepOf(stage, PqAction.HandOver);
            var close = Last(stage);
            check(watch != null && watch.openOnly && watch.npc == npc[i],
                "LPQ 第" + no + "关：队长先听 " + npc[i] + " 讲规则（openOnly，只开一页）");
            check(collect != null && collect.itemId == PqCatalog.LudiPass && collect.need == need[i],
                "LPQ 第" + no + "关：队员真去收 " + need[i] + " 张通行证");
            check(hand != null && hand.itemId == PqCatalog.LudiPass && hand.itemCount == need[i],
                "LPQ 第" + no + "关：攒够 " + need[i] + " 张才交给队长");
            check(close != null && close.action == PqAction.Talk && close.npc == npc[i] &&
                  close.awaitProperty == no + "stageclear",
                "LPQ 第" + no + "关：队长交完要等 " + no + "stageclear 才放行");
        }

        // 跨子图：第 2 关多一个陷阱房、第 4 关黑暗塔五间、第 5 关六个子图。
        check(HasAll(lpq.stages[1].steps, 922010201), "LPQ 第2关：陷阱房 922010201 的 4 个箱子也算进收集");
        var dark = StepOf(lpq.stages[3], PqAction.BotCollect);
        var five = StepOf(lpq.stages[4], PqAction.BotCollect);
        check(dark != null && dark.maps.Length == 5, "LPQ 第4关：黑暗塔五间 922010401~405 都要去");
        check(five != null && five.maps.Length == 6, "LPQ 第5关：六个子图 922010501~506 都要去");
        check(lpq.extraMaps.Count == 12 && lpq.extraMaps.Contains(922010201) &&
              lpq.extraMaps.Contains(922010405) && lpq.extraMaps.Contains(922010506),
            "LPQ 子图全部进预热表（12 张，加载页提前备好分片）");

        // 第 9 关：砸 2201003 召出阿丽莎乐，BOSS 掉 4001023 钥匙。
        var key = StepOf(lpq.stages[8], PqAction.BotCollect);
        var keyHand = StepOf(lpq.stages[8], PqAction.HandOver);
        check(key != null && key.itemId == PqCatalog.LudiKey && key.need == 1 &&
              Array.IndexOf(key.ids, 9300012) >= 0 && Array.IndexOf(key.reactors, 2201003) >= 0,
            "LPQ 第9关：砸祭坛 2201003 召 9300012，打死拿 4001023 钥匙");
        check(keyHand != null && keyHand.itemId == PqCatalog.LudiKey, "LPQ 第9关：钥匙交给队长");

        check(!AnyGrant(lpq, PqCatalog.LudiPass) && !AnyGrant(lpq, PqCatalog.LudiKey),
            "LPQ 不再直发通行证/钥匙（一律队员真收真交）");

        // 砸箱子的三处必须是源端那三种反应堆，且不能重复掉券：
        //   2202003 dropItems 掉 4001022（第 2/5 关）
        //   2201001 只 spawnMonster(9300007)×3（第 3 关，券由怪掉）
        //   2201002 只 spawnMonster(9300010)（第 7 关）
        check(HasReactor(lpq.stages[1], 2202003) && HasReactor(lpq.stages[4], 2202003),
            "LPQ 第2/5关：2202003 是 dropItems 那种箱子（券从箱子里掉）");
        check(HasReactor(lpq.stages[2], 2201001) && HasReactor(lpq.stages[6], 2201002),
            "LPQ 第3/7关：2201001/2201002 是刷怪那种箱子（券由怪掉，箱子本身不掉）");
        var third = StepOf(lpq.stages[2], PqAction.BotCollect);
        check(third != null && third.need == 32, "LPQ 第3关：8 只原怪 + 8×3 只刷出来的 = 32 张");
    }

    // ————————————————— 毒雾森林（EllinPQ）—————————————————
    // 这一路没有「队员交东西」以外的花活，只有两处靠反应堆脚本：
    //   930000200 的 3009000 要打断毒刺，930000600 的 3001000 召出毒液石头人。
    // 关键判定在 930000500：只有 3002001 掉 4001163（reactordrops 表概率 1），
    // 队长没带石头去点 2133004 会被脚本当场传回出口图。
    static void EllinChecks(Action<bool, string> check)
    {
        var epq = PqCatalog.Get("EPQ");
        check(epq != null, "EPQ 数据表可读");
        if (epq == null) return;

        int[] maps = { 930000000, 930000100, 930000200, 930000300, 930000500, 930000600, 930000700 };
        bool chain = epq.stages.Count == maps.Length;
        for (int i = 0; i < maps.Length && i < epq.stages.Count; i++)
            if (epq.stages[i].mapId != maps[i]) chain = false;
        check(chain, "EPQ 阶段图顺序与源端 930000000 → 930000700 一致（实际 " + epq.stages.Count + " 段）");

        check(HasReactor(epq.stages[2], 3009000), "EPQ 变质的森林：要砸断 3009000 毒刺");
        check(HasReactor(epq.stages[5], 3001000), "EPQ 毒树丛：要砸开 3001000 召出毒液石头人");

        var stone = StepOf(epq.stages[4], PqAction.BotCollect);
        check(stone != null && stone.itemId == PqCatalog.EllinStone && stone.need == 1,
            "EPQ 森林空地：假人真去砸紫色魔力石 4001163");
        check(stone != null && stone.reactors.Length == 2 &&
              Array.IndexOf(stone.reactors, 3002001) >= 0 && Array.IndexOf(stone.reactors, 3002002) >= 0,
            "EPQ 森林空地：9 个石堆按 3002001/3002002 两种模板砸");
        var stoneHand = StepOf(epq.stages[4], PqAction.HandOver);
        check(stoneHand != null && stoneHand.itemId == PqCatalog.EllinStone && stoneHand.itemCount == 1,
            "EPQ 森林空地：魔力石交给队长（他没带就会被 2133004 传出本）");
        var door = Last(epq.stages[4]);
        check(door != null && door.action == PqAction.Talk && door.npc == 2133004,
            "EPQ 森林空地：最后才由队长点 2133004 开门");
        check(!AnyGrant(epq, PqCatalog.EllinStone), "EPQ 不直发魔力石（由 3002001 真掉）");
    }

    // ————————————————— 老海盗 / 海盗船（PiratePQ）—————————————————
    // 关键差别：2094002 只走**一页**对话（npc/2094002.js 再点一次就把队长传回城），
    // 所以三轮勋章都用 openOnly；每轮的完成信号是实例属性 stage2 = "1"/"2"/"3"。
    static void PirateChecks(Action<bool, string> check)
    {
        var ppq = PqCatalog.Get("PPQ");
        check(ppq != null, "PPQ 数据表可读");
        if (ppq == null) return;

        int[] maps = { 925100000, 925100100, 925100200, 925100300, 925100400, 925100500, 925100600 };
        bool chain = ppq.stages.Count == maps.Length;
        for (int i = 0; i < maps.Length && i < ppq.stages.Count; i++)
            if (ppq.stages[i].mapId != maps[i]) chain = false;
        check(chain, "PPQ 阶段图顺序与源端 925100000 → 925100600 一致（实际 " + ppq.stages.Count + " 段）");

        // 第二轮勋章：20 个一档，三轮三种勋章，交完一轮才轮下一轮。
        int[] medals = { PqCatalog.PirateLow, PqCatalog.PirateMid, PqCatalog.PirateHigh };
        string[] awaited = { "1", "2", "3" };
        var round = ppq.stages[1];
        int collects = 0, hands = 0, talks = 0;
        bool rounds = true;
        for (int i = 0; i < round.steps.Count; i++)
        {
            var step = round.steps[i];
            if (step.action == PqAction.BotCollect)
            {
                if (collects >= medals.Length) { rounds = false; break; }
                if (step.itemId != medals[collects] || step.need != 20) rounds = false;
                if (Array.IndexOf(step.ids, 9300114) < 0 || Array.IndexOf(step.ids, 9300115) < 0 ||
                    Array.IndexOf(step.ids, 9300116) < 0) rounds = false;
                collects++;
            }
            else if (step.action == PqAction.HandOver)
            {
                if (hands >= medals.Length || step.itemId != medals[hands] || step.itemCount != 20) rounds = false;
                hands++;
            }
            else if (step.action == PqAction.Talk)
            {
                if (talks >= awaited.Length) { rounds = false; break; }
                if (step.npc != 2094002 || !step.openOnly) rounds = false;
                if (step.awaitProperty != "stage2" || step.awaitValue != awaited[talks]) rounds = false;
                talks++;
            }
        }
        check(rounds && collects == 3 && hands == 3 && talks == 3,
            "PPQ 突破船头：三轮勋章 4001120→4001121→4001122 各 20 个，交一轮等一次 stage2");
        check(!AnyGrant(ppq, PqCatalog.PirateLow) && !AnyGrant(ppq, PqCatalog.PirateMid) &&
              !AnyGrant(ppq, PqCatalog.PirateHigh),
            "PPQ 不直发勋章（9300114/5/6 真掉，队员真捡真交）");

        // 三个跨子舱清怪的关卡：主图 + 子舱都要人。
        var hold1 = StepOf(ppq.stages[2], PqAction.ClearMobs);
        var hold2 = StepOf(ppq.stages[3], PqAction.ClearMobs);
        check(hold1 != null && hold1.maps.Length == 2 && Array.IndexOf(hold1.maps, 925100201) >= 0,
            "PPQ 突破船甲1：子舱 925100201 那批怪也要清");
        check(hold2 != null && hold2.maps.Length == 2 && Array.IndexOf(hold2.maps, 925100301) >= 0,
            "PPQ 突破船甲2：子舱 925100301 那批怪也要清");

        // 四个动力门：砸到 state>=1 才算，清完还得玩家自己踩 next00（会顺手刷 BOSS）。
        var doors = StepOf(ppq.stages[4], PqAction.BreakReactors);
        bool four = doors != null && doors.need == 4;
        for (int id = 2519000; id <= 2519003 && four; id++)
            if (Array.IndexOf(doors.ids, id) < 0) four = false;
        check(four, "PPQ 击退海盗：四个动力门 2519000~2519003 逐个砸");
        check(ppq.stages[4].gateMs >= 10000, "PPQ 击退海盗：留时间给玩家自己踩 next00（BOSS 是门刷的）");
    }

    // 招募图大厅的「氛围丢东西」不许丢任务道具。
    //
    // 症状：假人把 4001007（证书）/ 4001008（通行许可证）/ 4001022（次元的通行证）当杂物
    // 丢在招募图上，玩家或队员顺手捡走一摞；而废都第一关的脚本是按
    // haveItem(4001007) == 题目张数 判定的，地上多出来的券会把这一关直接卡死。
    // PqBot 的运行期过滤（IsQuestItem）读的是 Bot/Items.json 的 info.quest/pquest，
    // 这里核两件事：掉落池里没有任务道具；四个副本阶段要用的道具确实都带标记，
    // 否则运行期过滤等于没开。
    static void AmbientDropChecks(Action<bool, string> check)
    {
        // 8.6MB 的不进输出目录，按工程根往上找（和 Maps 分片一个口径）。
        var items = LoadProjectJson("Assets/Resources/Bot/Items.json");
        check(items != null, "Bot/Items.json 可取到（PqBot 氛围掉的过滤依据）");
        if (items == null) return;

        var pool = LobbyDropPool();
        check(pool != null && pool.Length > 0, "PqBot.LobbyDrops 非空（大厅还有东西可丢）");
        if (pool == null || pool.Length == 0) return;
        foreach (int id in pool)
            check(!QuestFlagged(items, id), "氛围掉落池不含任务道具 " + id);

        var needed = new HashSet<int>();
        foreach (var def in PqCatalog.All)
            foreach (var stage in def.stages)
            {
                foreach (var step in stage.steps) if (step.itemId > 0) needed.Add(step.itemId);
                if (stage.requireItem > 0) needed.Add(stage.requireItem);
            }
        check(needed.Count >= 4, "副本阶段道具至少 4 种（实际 " + needed.Count + "）");
        foreach (int id in needed)
            check(QuestFlagged(items, id), "副本阶段道具 " + id + " 带 info/pquest（运行期过滤才挡得住）");

        // 这三个正是以前被当杂物丢出去的（废都证书 / 通行许可证 / 次元的通行证）；
        // 废都证书只走「假人领题」这条路，不在阶段 step.itemId 里，所以单独点一次名。
        check(QuestFlagged(items, PqCatalog.PassCoupon), "废都证书 4001007 带 info/pquest");
        check(QuestFlagged(items, PqCatalog.PassTicket), "废都通行许可证 4001008 带 info/pquest");
        check(QuestFlagged(items, PqCatalog.LudiPass), "玩具塔次元的通行证 4001022 带 info/pquest");
    }

    static int[] LobbyDropPool()
    {
        var field = typeof(Server.Bot.Types.Pq.PqBot).GetField("LobbyDrops",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(null) as int[];
    }

    static bool QuestFlagged(Dictionary<string, object> items, int itemId)
    {
        if (!(items.TryGetValue("items", out var raw) && raw is Dictionary<string, object> all)) return false;
        if (!(all.TryGetValue(itemId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var row)
              && row is Dictionary<string, object> item
              && item.TryGetValue("info", out var infoRaw)
              && infoRaw is Dictionary<string, object> info)) return false;
        return InfoInt(info, "quest") == 1 || InfoInt(info, "pquest") == 1;
    }

    static long InfoInt(Dictionary<string, object> info, string key)
    {
        if (info == null || !info.TryGetValue(key, out var value) || value == null) return 0;
        if (value is long l) return l;
        if (value is double d) return (long)d;
        return long.TryParse(Convert.ToString(value), out long n) ? n : 0;
    }

    // 反应堆掉落表 + 两个最要命的 reactordrops 重载回归。
    //
    // 症状：玩具塔第 2/5 关砸完箱子一张券都不掉；毒雾森林砸石堆不掉魔力石。
    // 根因两条，都在 ReactorActionManager 的绑定面上：
    //   1. 源端 dropItems() 是**零参**重载，移植端第一版只认 5 参，直接抛
    //      「Reactor dropItems overload is not migrated: 0」→ 整段 act() 中断；
    //   2. 3002001.js 第一句是 rm.getEventInstance().showClearEffect(rm.getMap().getId())，
    //      rm.getMap 没绑定 → 抛在 dropItems 之前。
    // 这里用真脚本 + 假宿主跑一遍，锁住「不抛异常」这条底线。
    static void ReactorDropChecks(Action<bool, string> check)
    {
        var drops = LoadJson("Bot/ReactorDrops.json");
        check(drops != null, "ReactorDrops.json 可取到（源端 V1.0.58__reactordrops_insert_data.sql）");
        if (drops == null) return;
        check(DropRow(drops, 2202003, 4001022) == 1, "reactordrops：2202003 → 4001022 概率 1（玩具塔 2/5 关的券在箱子里）");
        check(DropRow(drops, 3002001, 4001163) == 1, "reactordrops：3002001 → 4001163 概率 1（毒雾森林的魔力石）");
        check(DropRow(drops, 2201001, 4001022) == 1, "reactordrops：2201001 → 4001022（源端 SQL 有这行，但脚本只刷怪）");

        try { Server.Field.NativeReactorDrops.Load(drops); }
        catch (Exception error) { check(false, "反应堆掉落表加载失败：" + error.Message); return; }

        string dir = ReactorScriptDir();
        if (dir == null) { Console.WriteLine("  SKIP 反应堆脚本回归：找不到 scripts-zh-CN/reactor"); return; }

        // ① 2202003.js = dropItems()（零参）。以前这里必抛「overload is not migrated: 0」。
        var errors = new List<string>();
        var service = new NativeReactorScriptService(key => ReadScript(dir, key));
        service.Error = error => errors.Add(error.Message);
        var box = new Server.Field.MapReactor { mapId = 922010200, id = 2202003, objectId = 1, x = 10, y = 20 };
        service.Act(box, 0);
        check(errors.Count == 0, "reactor/2202003.js 跑得通（dropItems 零参重载）"
            + (errors.Count > 0 ? "：" + errors[0] : ""));

        // ② 2201001.js = 循环 3 次 spawnMonster(9300007)：砸 8 个箱子要刷出 24 只怪。
        var spawned = new List<int>();
        var spawner = new NativeReactorScriptService(key => ReadScript(dir, key));
        spawner.Error = error => errors.Add(error.Message);
        spawner.SpawnMonster = (map, mob, x, y, qty) => { for (int i = 0; i < qty; i++) spawned.Add(mob); return true; };
        errors.Clear();
        spawner.Act(new Server.Field.MapReactor { mapId = 922010300, id = 2201001, objectId = 2, x = 0, y = 0 }, 0);
        check(errors.Count == 0 && spawned.Count == 3 && spawned.TrueForAll(id => id == 9300007),
            "reactor/2201001.js 刷出 3 只 9300007（实际 " + spawned.Count + " 只"
            + (errors.Count > 0 ? "，错误：" + errors[0] : "") + "）");

        // ③ 3002001.js 先 rm.getEventInstance().showClearEffect(rm.getMap().getId()) 再 dropItems()：
        //    getMap / getEventInstance 任一没绑，石头就会「砸了不掉」——源端也是这个顺序。
        var stone = new NativeReactorScriptService(key => ReadScript(dir, key));
        stone.Error = error => errors.Add(error.Message);
        stone.Map = _ => FakeMap(930000500);
        stone.EventInstance = _ => FakeClearEffect();
        errors.Clear();
        stone.Act(new Server.Field.MapReactor { mapId = 930000500, id = 3002001, objectId = 3, x = 0, y = 0 }, 0);
        check(errors.Count == 0, "reactor/3002001.js 跑得通（getMap + getEventInstance + dropItems）"
            + (errors.Count > 0 ? "：" + errors[0] : ""));

        // ④ 没接线 spawnMonster 时只记日志、不吞掉同一段脚本里的掉落。
        var bare = new NativeReactorScriptService(key => "function act() { rm.dropItems(); rm.spawnMonster(1); }");
        var bareErrors = new List<string>();
        bare.Error = error => bareErrors.Add(error.Message);
        bare.Act(new Server.Field.MapReactor { mapId = 922010200, id = 2202003, objectId = 4, x = 0, y = 0 }, 0);
        check(bareErrors.Count == 1 && bareErrors[0].Contains("spawnMonster"),
            "spawnMonster 未接线：只报一条明确错误，dropItems 照常执行");

        // ⑤ 地图分片里到底有没有这些箱子。分片缺 reactors 字段时，假人和玩家都砸不到箱子，
        //    整关只能靠 Give 凭空发——这正是「玩具塔一关都过不去」的老症状。
        //    张数关系：第 2 关 11 + 陷阱房 4 = 15，第 5 关 6×4 = 24，第 3 关 8 箱 ×3 只 = 24 只。
        if (ShardMap("922", 922010200) == null)
        {
            Console.WriteLine("  SKIP 地图分片检查：找不到 Assets/Resources/Bot/Maps");
            return;
        }
        check(ReactorCount("922", 922010200, 2202003) == 11, "地图数据：922010200 有 11 个 2202003 箱子");
        check(ReactorCount("922", 922010201, 2202003) == 4, "地图数据：陷阱房 922010201 有 4 个 2202003 箱子（11+4=15）");
        int boxes = 0;
        for (int map = 922010501; map <= 922010506; map++) boxes += ReactorCount("922", map, 2202003);
        check(boxes == 24, "地图数据：第5关六个子图各 4 个箱子，共 24（实际 " + boxes + "）");
        check(ReactorCount("922", 922010300, 2201001) == 8, "地图数据：922010300 有 8 个 2201001（每个刷 3 只，8×3+8=32）");
        check(ReactorCount("922", 922010700, 2201002) == 3, "地图数据：922010700 有 3 个 2201002");
        check(ReactorCount("922", 922010900, 2201003) == 1, "地图数据：922010900 祭坛 2201003 只有一个");
        check(ReactorCount("922", 922010100, 0) == 0, "地图数据：922010100 没有箱子（第1关全靠打怪捡券）");
        check(ReactorCount("930", 930000500, 3002001) == 1, "地图数据：930000500 的 9 个石堆里只有 1 个 3002001 掉魔力石");
        check(ReactorCount("930", 930000200, 3009000) == 1, "地图数据：930000200 的毒刺 3009000");
        check(ReactorCount("930", 930000600, 3001000) == 1, "地图数据：930000600 的祭坛 3001000");
        int doors = 0;
        for (int id = 2519000; id <= 2519003; id++) doors += ReactorCount("925", 925100400, id);
        check(doors == 4, "地图数据：925100400 四个动力门 sMob1~4（实际 " + doors + "）");
        check(ReactorCount("103", 103000800, 0) == 0, "地图数据：103000800 没有箱子（废都第一关靠打鳄鱼）");

        // ⑥ 重进同一关箱子得能回来。reactorTime<=0 的箱子破坏后不会自己重生，
        //    靠 PqRun.EnterStage 里的 MapReactorService.RespawnAll 兜住；
        //    以前没有这一步，第二遍跑玩具塔一个箱子都没有。
        var fresh = Server.Field.MapReactorService.Spawn(922010200, 2202003, 0, 0);
        check(fresh != null && fresh.Hittable, "反应堆：新实例可打");
        for (int i = 0; i < 4; i++) Server.Field.MapReactorService.Hit(922010200, fresh.objectId);
        check(!fresh.alive, "反应堆：砸 4 下破坏（maxState=4）");
        Server.Field.MapReactorService.RespawnAll(922010200);
        check(fresh.alive && fresh.state == 0 && fresh.Hittable,
            "反应堆：RespawnAll 让箱子回到初始状态（重进本关还能砸）");
        Server.Field.MapReactorService.ClearAll();
    }

    static JsValue FakeMap(int mapId)
    {
        var host = new JsHostObject(null);
        host.Set("getId", JsFunction.FromNative("getId", _ => new JsNumber(mapId)));
        return host;
    }

    // 事件实例的最小宿主：3002001.js 只用到 showClearEffect。
    static JsValue FakeClearEffect()
    {
        var host = new JsHostObject(null);
        host.Set("showClearEffect", JsFunction.FromNative("showClearEffect", _ => JsUndefined.Value));
        return host;
    }

    // ————————————————————— 小工具 —————————————————————
    static PqStep StepOf(PqStage stage, PqAction action)
    {
        if (stage == null) return null;
        foreach (var step in stage.steps) if (step.action == action) return step;
        return null;
    }

    static PqStep Last(PqStage stage)
        => stage == null || stage.steps.Count == 0 ? null : stage.steps[stage.steps.Count - 1];

    static bool HasAll(List<PqStep> steps, int mapId)
    {
        foreach (var step in steps)
            if (step.maps != null && Array.IndexOf(step.maps, mapId) >= 0) return true;
        return false;
    }

    // BreakReactors 把模板 id 写在 ids 上（BotCollect 才用 reactors + ids 区分怪和箱子），
    // 两处都查，免得断言因为字段选错而误报。
    static bool HasReactor(PqStage stage, int reactorId)
    {
        if (stage == null) return false;
        foreach (var step in stage.steps)
        {
            if (step.reactors != null && Array.IndexOf(step.reactors, reactorId) >= 0) return true;
            if (step.action == PqAction.BreakReactors && step.ids != null &&
                Array.IndexOf(step.ids, reactorId) >= 0) return true;
        }
        return false;
    }

    static bool AnyGrant(PqDef def, int itemId)
    {
        foreach (var stage in def.stages)
            foreach (var step in stage.steps)
                if (step.action == PqAction.GrantItem && step.itemId == itemId) return true;
        return false;
    }

    // 组队任务用到的掉落表/脚本都不在 exe 旁边，按输出目录往上找工程根（与 EventScriptDir 同口径）。
    static string ReactorScriptDir()
    {
        string root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && root != null; i++)
        {
            string candidate = Path.Combine(root, "BeiDou-Server", "gms-server", "scripts-zh-CN", "reactor");
            if (Directory.Exists(candidate)) return candidate;
            root = Path.GetDirectoryName(root);
        }
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "mxd", "BeiDou-Server", "gms-server", "scripts-zh-CN", "reactor");
        return Directory.Exists(fallback) ? fallback : null;
    }

    // 传进来的 key 形如 "reactor/2202003"。
    static string ReadScript(string dir, string key)
    {
        int slash = key == null ? -1 : key.LastIndexOf('/');
        string name = slash >= 0 ? key.Substring(slash + 1) : key;
        string path = Path.Combine(dir, name + ".js");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    static Dictionary<string, object> LoadJson(string relative)
    {
        string path = Path.Combine(AppContext.BaseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        return MiniJsonDb.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
    }

    // 地图分片不在输出目录里（体积大、且是源数据），按工程根往上找。
    static readonly Dictionary<string, Dictionary<string, object>> shardCache =
        new Dictionary<string, Dictionary<string, object>>();

    static string ProjectPath(string relative)
    {
        string root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && root != null; i++)
        {
            string candidate = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            root = Path.GetDirectoryName(root);
        }
        return null;
    }

    static string ReadProjectText(string relative)
    {
        string path = ProjectPath(relative);
        return path != null ? File.ReadAllText(path) : null;
    }

    static Dictionary<string, object> LoadProjectJson(string relative)
    {
        string path = ProjectPath(relative);
        return path != null ? MiniJsonDb.Parse(File.ReadAllText(path)) as Dictionary<string, object> : null;
    }

    // 分片文件名 = mapId / 1000000（export_solomapling_maps.py 的切法）。
    static Dictionary<string, object> ShardMap(string shardKey, int mapId)
    {
        if (!shardCache.TryGetValue(shardKey, out var root))
        {
            root = LoadProjectJson("Assets/Resources/Bot/Maps/" + shardKey + ".json");
            shardCache[shardKey] = root;
        }
        if (root == null) return null;
        return root.TryGetValue(mapId.ToString(), out object raw) ? raw as Dictionary<string, object> : null;
    }

    // reactorId == 0 表示「这张图有几个反应堆都算」。
    static int ReactorCount(string shardKey, int mapId, int reactorId)
    {
        var map = ShardMap(shardKey, mapId);
        if (map == null || !map.TryGetValue("reactors", out object raw) || !(raw is List<object> rows)) return 0;
        int count = 0;
        foreach (var entry in rows)
            if (entry is Dictionary<string, object> row && reactorId != 0 &&
                Convert.ToInt32(row["id"]) == reactorId) count++;
        return reactorId == 0 ? rows.Count : count;
    }

    // 掉落表里 (反应堆模板 id → 物品) 的 chance；没有这行返回 -1。
    static int DropRow(Dictionary<string, object> root, int reactorId, int itemId)
    {
        if (!root.TryGetValue(reactorId.ToString(), out object raw)) return -1;
        if (!(raw is List<object> rows)) return -1;
        foreach (var entry in rows)
            if (entry is Dictionary<string, object> row && Convert.ToInt32(row["item"]) == itemId)
                return Convert.ToInt32(row["chance"]);
        return -1;
    }

    // 实例登记用的最小玩家宿主：getId/getMapId 就够 registerParty/gridCheck 用。
    static JsValue FakePlayer(int id, int mapId)
    {
        var player = new JsHostObject(null);
        player.Set("getId", JsFunction.FromNative("getId", _ => new JsNumber(id)));
        player.Set("getMapId", JsFunction.FromNative("getMapId", _ => new JsNumber(mapId)));
        player.Set("getName", JsFunction.FromNative("getName", _ => new JsString("P" + id)));
        player.Set("dropMessage", JsFunction.FromNative("dropMessage", _ => JsUndefined.Value));
        return player;
    }

    static void CombatChecks(Action<bool,string> check)
    {
        var db = DbStore.Create(Path.Combine(Path.GetTempPath(), "pq-combat-" + Guid.NewGuid().ToString("N") + ".json"));
        check(GameConfigDao.GetInt(db, "mxj_max_level") == 255, "PQ 默认等级上限取源端 V2.1.50 的255");
        var cap = db.Table("game_config").Find(r => DbStore.Str(r,"config_code") == "mxj_max_level");
        cap["config_value"] = "180"; GameConfigDao.EnsureLevelCaps(db);
        check(GameConfigDao.GetInt(db,"mxj_max_level") == 180, "补配置不覆盖管理员设置");
        db.Table("game_config").Delete(r => DbStore.Str(r,"config_code") == "mxj_max_level");
        db.Save();
        check(GameConfigDao.GetInt(DbStore.Open(db.FilePath),"mxj_max_level") == 255, "旧存档缺失等级上限也补齐");
        File.Delete(db.FilePath);

        var previousParties = Server.Bot.BotTypeManager.Parties;
        var previousGrind = Server.Bot.BotTypeManager.PartyGrind;
        var previousId = Server.Bot.Types.Pq.PqDirector.LocalPlayerId;
        var parties = new Server.Bot.BotPartyService(db);
        var player = new Server.Bot.BotActor { id=81001, level=35, mapId=103000800 };
        var bot = new Server.Bot.BotCharacter { id=81002, level=35, job=100, mapId=103000800 };
        Server.Bot.BotRegistry.Add(bot);
        parties.Create(player); parties.Join(bot,parties.For(player.id).id);
        using var grind = new Server.Bot.BotPartyGrindService(parties, _ => null, new Random(1), db);
        try
        {
            Server.Bot.BotTypeManager.Parties=parties; Server.Bot.BotTypeManager.PartyGrind=grind;
            Server.Bot.Types.Pq.PqDirector.LocalPlayerId=()=>player.id;
            var run=new Server.Bot.Types.Pq.PqRun(Server.Bot.Types.Pq.PqCatalog.Get("KPQ"),parties.For(player.id).id,player.id);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            ((List<int>)typeof(Server.Bot.Types.Pq.PqRun).GetField("bots",flags).GetValue(run)).Add(bot.id);
            run.stageIndex=0;
            var mob=new Server.Bot.BotGrindMob {id=1,alive=true,maxHp=1000,x=0,y=300};
            grind.Monsters=(_,__,___)=>new List<Server.Bot.BotGrindMob> {mob};
            int hits=0; grind.Hit=(_,__,___,____,_____)=>hits++;
            var work=typeof(Server.Bot.Types.Pq.PqRun).GetMethod("WorkMobs",flags);
            void Tick(long now)=>work.Invoke(run,new object[] {new Server.Bot.Types.Pq.PqStep(),now,false});
            Tick(1000);
            check(hits==0 && bot.y==300, "PQ 同X不同楼层先接近，不原地循环打空");
            Tick(1500); Tick(2000);
            check(hits==1, "PQ 到达射程后攻击，800ms冷却内不重复结算");
            Tick(2500);
            check(hits==2, "PQ 冷却结束后继续攻击");
            bot.job=212; bot.equips[-11]=1382000; bot.x=0;bot.y=300;mob.x=300;
            Tick(3500);
            check(hits==3 && bot.x==0, "PQ 远程职业使用真实Strike同口径射程停步攻击");
            var previousWalk = Server.Bot.Types.Pq.PqDirector.WalkToItem;
            try
            {
                int walks = 0;
                Server.Bot.Types.Pq.PqDirector.WalkToItem = (_, __, ___) => { walks++; return true; };
                bot.job = 100; bot.equips.Clear(); mob.x = 20; mob.y = 300; mob.mobId = 9300001;
                var collect = typeof(Server.Bot.Types.Pq.PqRun).GetMethod("WorkBotCollect", flags);
                collect.Invoke(run, new object[] { new PqStep { itemId = PqCatalog.PassCoupon, need = 20,
                    ids = new[] { 9300001 } }, 4500L });
                check(hits == 4 && walks == 0, "PQ 收券：邻层券寻路不能抢占已在射程内的攻击");
            }
            finally { Server.Bot.Types.Pq.PqDirector.WalkToItem = previousWalk; }
            CheckKerningRoutes(check, run, bot, grind);
        }
        finally
        {
            Server.Bot.BotRegistry.Remove(bot);
            Server.Bot.BotTypeManager.Parties=previousParties; Server.Bot.BotTypeManager.PartyGrind=previousGrind;
            Server.Bot.Types.Pq.PqDirector.LocalPlayerId=previousId;
            if(File.Exists(db.FilePath)) File.Delete(db.FilePath);
        }
    }

    // 真正执行地图导航和 Strike，不用未观察地图的直接改坐标捷径。
    static void CheckKerningRoutes(Action<bool,string> check, Server.Bot.Types.Pq.PqRun run,
        Server.Bot.BotCharacter bot, Server.Bot.BotPartyGrindService grind)
    {
        var map = Server.Bot.Environment.BotNavigationMapLoader.Load().Geometry(103000800);
        Server.Field.MapService.Register(map);
        Server.Bot.Environment.BotMovementService.Graphs.Get(map);
        var observed = Server.Bot.Environment.BotMovementService.IsObserved;
        var profile = Server.Bot.Environment.BotMovementService.ResolveProfile;
        long clock = Server.Bot.BotScheduler.Clock;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var strike = typeof(Server.Bot.Types.Pq.PqRun).GetMethod("StrikeNearest", flags);
        var ropeActor = new Server.Bot.BotCharacter { id = unchecked(map.mapId * 1000 + 4), mapId = map.mapId };
        var ropeProfile = new Server.Bot.Environment.BotMovementProfile(125,105);
        var ropeMotion = new Server.Bot.Environment.BotMovementRuntime(ropeActor, map, ropeProfile);
        ropeMotion.Teleport(new Server.Bot.Environment.BotNavigationGraph.Point(282,-82));
        var ropeNav = new Server.Bot.Environment.BotNavigationRuntime(ropeMotion,
            Server.Bot.Environment.BotMovementService.Graphs.Get(map,ropeProfile),new Random(ropeActor.id));
        bool reached = false;
        for (int tick = 0; tick < 2400 && !reached; tick++)
        {
            ropeNav.Tick(new Server.Bot.Environment.BotNavigationGraph.Point(-642,-1125),tick*50L,true,true);
            reached = Math.Abs(ropeActor.x + 642) <= 70 && Math.Abs(ropeActor.y + 1125) <= 50;
        }
        check(reached, $"废都长绳：不靠卡住后随机跳跃救援也能上层，终点({ropeActor.x},{ropeActor.y})");
        try
        {
            Server.Bot.Environment.BotMovementService.IsObserved = _ => true;
            bot.job = 100; bot.equips.Clear();
            // 原图底层、左右中层及高层平台。每次均从 Cloto 所在底层出发。
            int[][] spots = { new[] { 282, -82 }, new[] { -668, -217 }, new[] { -254, -343 },
                new[] { -730, -894 }, new[] { -642, -1125 }, new[] { 380, -1175 } };
            long now = 10000;
            foreach (var movementProfile in new[] { Server.Bot.Environment.BotMovementProfile.Base,
                new Server.Bot.Environment.BotMovementProfile(125,105),
                new Server.Bot.Environment.BotMovementProfile(130,110),
                new Server.Bot.Environment.BotMovementProfile(155,123) })
            {
                Server.Bot.Environment.BotMovementService.Graphs.Get(map, movementProfile);
                Server.Bot.Environment.BotMovementService.ResolveProfile = (_, __) => movementProfile;
                foreach (var spot in spots)
                {
                    Server.Bot.Environment.BotMovementService.Disable(bot);
                    Server.Bot.BotScheduler.Clock = now;
                    bot.x = -1077; bot.y = 111;
                    var mob = new Server.Bot.BotGrindMob { id = 92000 + spot[0], mobId = 9300001,
                        alive = true, maxHp = 1000, x = spot[0], y = spot[1] };
                    grind.Monsters = (_, __, ___) => new List<Server.Bot.BotGrindMob> { mob };
                    int hits = 0;
                    grind.Hit = (_, __, ___, ____, _____) => { hits++; mob.alive = false; };
                    long end = now + 120000;
                    for (; now < end && hits == 0; now += 50)
                    {
                        Server.Bot.BotScheduler.Clock = now;
                        if (now % 500 == 0) strike.Invoke(run, new object[] { bot,
                            new PqStep { ids = new[] { 9300001 } }, now, false });
                        Server.Bot.BotScheduler.Drive();
                    }
                    check(hits > 0, $"废都103000800：速度/跳跃{movementProfile.speed}/{movementProfile.jump}底层实际导航至({spot[0]},{spot[1]})并结算攻击，终点({bot.x},{bot.y})");
                }
            }
        }
        finally
        {
            Server.Bot.Environment.BotMovementService.Disable(bot);
            Server.Bot.Environment.BotMovementService.IsObserved = observed;
            Server.Bot.Environment.BotMovementService.ResolveProfile = profile;
            Server.Bot.BotScheduler.Clock = clock;
        }
    }

    static string EventScriptDir()
    {
        string root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && root != null; i++)
        {
            string candidate = Path.Combine(root, "BeiDou-Server", "gms-server", "scripts-zh-CN", "event");
            if (Directory.Exists(candidate)) return candidate;
            root = Path.GetDirectoryName(root);
        }
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "mxd", "BeiDou-Server", "gms-server", "scripts-zh-CN", "event");
        return Directory.Exists(fallback) ? fallback : null;
    }

    static string ReadEvent(string dir, string key)
    {
        // GetManager 传的是 "event/<name>"。
        string name = key.StartsWith("event/", StringComparison.Ordinal) ? key.Substring(6) : key;
        string path = Path.Combine(dir, name + ".js");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    // 事件脚本顶层只依赖 org.gms.config.GameConfig（getServerInt / getServerBoolean）。
    // 这里给一份「单机全开」的配置：mxj_max_level=200、solo expedition 开启（minPlayers→1）。
    static JsValue ResolveHostType(string name)
    {
        if (name != "org.gms.config.GameConfig") return null;
        var type = new JsHostObject(null);
        type.Set("getServerInt", JsFunction.FromNative("getServerInt",
            c => new JsNumber(JsConvert.ToString(c.Arg(0)) == "mxj_max_level" ? 200 : 0)));
        type.Set("getServerString", JsFunction.FromNative("getServerString", c => new JsString("")));
        type.Set("getServerBoolean", JsFunction.FromNative("getServerBoolean", c => JsBoolean.Of(true)));
        return type;
    }

    // 模拟 NpcApi.BuildParty().getPartyMembersOnline()：Java List 形态的 PartyCharacter 列表。
    static JsValue Party(int mapId, int level)
    {
        var members = new JsArray();
        var member = new JsHostObject(null);
        member.Set("getId", JsFunction.FromNative("getId", _ => new JsNumber(1)));
        member.Set("getMapId", JsFunction.FromNative("getMapId", _ => new JsNumber(mapId)));
        member.Set("getLevel", JsFunction.FromNative("getLevel", _ => new JsNumber(level)));
        member.Set("isLeader", JsFunction.FromNative("isLeader", _ => JsBoolean.Of(true)));
        members.Items.Add(member);

        // BuildJavaList：size/get/isEmpty/toArray —— 事件脚本正是靠 toArray() 取到成员数组。
        var list = new JsHostObject(members);
        list.Set("size", JsFunction.FromNative("size", _ => new JsNumber(members.Items.Count)));
        list.Set("get", JsFunction.FromNative("get", c => members.GetIndex(JsConvert.ToInt32(c.Arg(0)))));
        list.Set("isEmpty", JsFunction.FromNative("isEmpty", _ => JsBoolean.Of(members.Items.Count == 0)));
        list.Set("toArray", JsFunction.FromNative("toArray", _ => members));

        var party = new JsHostObject(null);
        party.Set("getPartyMembersOnline", JsFunction.FromNative("getPartyMembersOnline", _ => list));
        party.Set("getLeaderId", JsFunction.FromNative("getLeaderId", _ => new JsNumber(1)));
        return party;
    }

    static JsValue Call(JsHostObject host, string member, params JsValue[] args)
    {
        if (!(host.Get(member) is JsFunction fn))
            throw new JsRuntimeException("Unmigrated event manager API: " + member);
        return fn.Native(new JsCall { This = host, Args = args });
    }

    // getEligibleParty 的返回是 JS 数组（Java.to 在移植端是恒等转换）。
    static int Size(JsValue value)
    {
        if (value is JsArray array) return array.Items.Count;
        if (value is JsNull || value is JsUndefined) return 0;
        throw new JsRuntimeException("getEligibleParty 应返回名单数组，实际 " + value);
    }
}
