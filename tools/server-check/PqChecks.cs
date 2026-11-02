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
