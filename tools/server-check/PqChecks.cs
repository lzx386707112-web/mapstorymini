using System;
using System.Collections.Generic;
using System.IO;
using MapleDash.Js;

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
        }
        finally
        {
            Server.Bot.BotRegistry.Remove(bot);
            Server.Bot.BotTypeManager.Parties=previousParties; Server.Bot.BotTypeManager.PartyGrind=previousGrind;
            Server.Bot.Types.Pq.PqDirector.LocalPlayerId=previousId;
            if(File.Exists(db.FilePath)) File.Delete(db.FilePath);
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
