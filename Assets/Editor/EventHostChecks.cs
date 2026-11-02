using System;
using MapleDash.Js;
using UnityEditor;
using UnityEngine;

// 事件宿主的三条「源端有、移植端曾经没有」的入口：
//   EventManager.startInstance 的各个重载（玩家 / 队伍 / 指定大厅 / 难度 / 远征队）
//   EventInstanceManager.playerDisconnected（Client.java:969 断线时调）
//   EventInstanceManager.monsterKilled(Character,Monster) → 脚本 monsterValue → killCount
//
// 这里是纯引擎层回归：脚本用内联文本，只验证宿主分派和状态记账。
// 真实事件脚本（怪物公园 / 航海 / 起源之塔）的玩法由 MonsterParkChecks / CommerciVoyageChecks /
// SeedTowerChecks 覆盖，两边不重复。
//
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod EventHostChecks.Run -logFile /tmp/event_host.log
public static class EventHostChecks
{
    static int pass, fail;
    static void Require(bool ok, string what)
    {
        if (ok) { pass++; return; }
        fail++;
        Debug.LogError("[EventHost] 失败：" + what);
    }
    static void Step(string what) => Debug.Log("[EventHost] 通过：" + what);

    const int LocalId = 100;
    const int RecruitMap = 100000000;

    // 只有内联脚本才需要的方法；真实脚本里同名的 function 是脚本自己写的。
    const string HostScript = @"
var calls = 0;
function init() {}
function setup(difficulty, lobby) {
    var eim = em.newInstance('lobby' + lobby);
    eim.setProperty('difficulty', String(difficulty));
    return eim;
}
function playerDisconnected(eim, chr) {
    eim.setProperty('disconnected', String(chr.getId()));
    eim.unregisterPlayer(chr);
}
function playerUnregistered(eim, chr) { eim.setProperty('unregistered', String(chr.getId())); }
function monsterValue(eim, mobId) { return mobId == 9309046 ? 1 : 0; }
function probeKill(eim, chr) { return eim.getKillCount(chr); }
";

    public static void Run()
    {
        pass = fail = 0;
        try
        {
            CheckStartInstanceOverloads();
            CheckPlayerDisconnected();
            CheckMonsterValueKillCount();
        }
        catch (Exception error)
        {
            fail++;
            Debug.LogError("[EventHost] 异常：" + error);
        }
        Debug.Log("[EventHost] 通过 " + pass + " 项，失败 " + fail + " 项");
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    static NativeEventScriptService NewService(out NativeEventScriptService.Manager manager)
    {
        var service = new NativeEventScriptService(key => HostScript, () => 1000) { LocalPlayerId = () => LocalId };
        manager = service.GetManager("Host");
        Require(manager != null, "内联事件脚本能建出 EventManager");
        return service;
    }

    // ——— EventManager.startInstance 的重载分派 ———
    static void CheckStartInstanceOverloads()
    {
        var service = NewService(out var manager);
        try
        {
            var player = Player(LocalId);
            var map = Map(RecruitMap);

            // startInstance(Character)：单人入口，队长就是这名玩家。
            var single = Start(manager, "startInstance", player);
            Require(single, "startInstance(player) 返回 true");
            var instance = InstanceOf(manager, 0);
            Require(instance != null && instance.Leader == LocalId, "单人实例的队长是本地角色");

            // startInstance(Party, MapleMap)：队长必须还在招募图上（EventManager.java:738 + registerParty）。
            var leader = Player(LocalId);
            var party = Party(leader, LocalId);
            Require(Start(manager, "startInstance", party, map), "startInstance(party,map) 本地队长可起");
            Require(InstanceOf(manager, 1)?.Leader == LocalId, "队伍实例的队长从 party.getLeader() 取");

            // 远端队长：移植端没有真实 Character 可注册，必须返回 false 而不是伪造一支队伍。
            var remoteLeader = Player(999);
            Require(!Start(manager, "startInstance", Party(remoteLeader, 999), map),
                "startInstance(party,map) 远端队长返回 false");
            Require(InstanceOf(manager, 2) == null, "被拒的队伍入口不留下空实例");

            // startInstance(int lobby, Character)：显式大厅（EventManager.java:646）。
            Start(manager, "startInstance", new JsNumber(2), player);
            Require(InstanceOf(manager, 2)?.Lobby == 2, "startInstance(2,player) 落在 2 号大厅");

            // startInstance(int lobby, Party, MapleMap, int difficulty)（EventManager.java:820）。
            Start(manager, "startInstance", new JsNumber(4), Party(Player(LocalId), LocalId), map, new JsNumber(3));
            var difficulty = InstanceOf(manager, 4);
            Require(difficulty != null && difficulty.Lobby == 4, "startInstance(4,party,map,3) 落在 4 号大厅");
            Require(difficulty != null && difficulty.Property("difficulty") == "3", "难度传给脚本的 setup");

            // startInstance(Expedition)：EventManager.java:531-605。队长仍是本地真实角色，
            // exped.start() 先关招募，再按 registerExpeditionTeam 只登记「还在招募图上」的成员。
            var exped = new Expedition(LocalId, "hero" + LocalId, RecruitMap, ExpeditionType.LUCID);
            ExpeditionRegistry.AddExpedition(exped);
            var expedHost = Host(exped);
            Require(Start(manager, "startInstance", expedHost), "startInstance(expedition) 返回 true");
            var expeditionInstance = InstanceOf(manager, 3);
            Require(expeditionInstance != null && expeditionInstance.Leader == LocalId,
                "远征队实例的队长取自 expedition.getLeader()");
            Require(expeditionInstance != null && expeditionInstance.PlayerCount == 1,
                "招募图上的队长被登记进实例");
            Require(!exped.Registering, "exped.start() 之后不再处于招募状态");
            ExpeditionRegistry.Clear();

            // 已用满的大厅：请求同一个显式大厅必须失败，不能覆盖上一个实例。
            Require(!Start(manager, "startInstance", new JsNumber(2), player), "重复请求同一个显式大厅返回 false");
        }
        finally { service.Clear(); }
        Step("startInstance：玩家 / 队伍 / 显式大厅 / 难度 / 远征队 五条分派");
    }

    // ——— EventInstanceManager.playerDisconnected ———
    static void CheckPlayerDisconnected()
    {
        var service = NewService(out var manager);
        try
        {
            var player = Player(LocalId);
            Start(manager, "startInstance", player);
            var instance = InstanceOf(manager, 0);
            Require(instance != null && instance.PlayerCount == 1, "实例里有 1 名玩家");

            // 源端 Client.java:969：连接结束后调 eim.playerDisconnected，脚本自己负责摘人。
            instance.PlayerDisconnected(player);
            Require(instance.Property("disconnected") == LocalId.ToString(),
                "脚本 playerDisconnected 收到真实玩家，实际 " + instance.Property("disconnected"));
            Require(instance.PlayerCount == 0, "脚本 unregisterPlayer 之后实例里没人了");
            Require(instance.Property("unregistered") == LocalId.ToString(), "playerUnregistered 回调也走到了");

            // 已经没人了再掉线一次不能抛（重复回调）。
            instance.PlayerDisconnected(player);
            Require(instance.PlayerCount == 0, "重复掉线回调不炸也不改状态");
        }
        finally { service.Clear(); }
        Step("playerDisconnected：脚本收到玩家 → unregisterPlayer → playerUnregistered");
    }

    // ——— monsterValue → killCount ———
    static void CheckMonsterValueKillCount()
    {
        var service = NewService(out var manager);
        try
        {
            var player = Player(LocalId);
            Start(manager, "startInstance", player);
            var instance = InstanceOf(manager, 0);
            Require(instance != null, "killCount 用例拿到实例");

            Require(instance.GetKillCount(player) == 0, "初始击杀计数为 0");
            // EventInstanceManager.java:607-626：只认脚本 monsterValue 的返回值。
            instance.MonsterKilledBy(player, 9309046);
            instance.MonsterKilledBy(player, 9309046);
            Require(instance.GetKillCount(player) == 2, "monsterValue 返回 1 的怪累计 2 次，实际 " + instance.GetKillCount(player));

            instance.MonsterKilledBy(player, 9400000);
            Require(instance.GetKillCount(player) == 2, "monsterValue 返回 0 的怪不计分");

            // getKillCount 是对脚本可见的实例方法（各 PQ 用它算奖励档位）。
            var reported = JsConvert.ToInt32(instance.Invoke("probeKill", instance.Host, player));
            Require(reported == 2, "eim.getKillCount(chr) 读回的也是 2，实际 " + reported);

            instance.MonsterKilledBy(player, 0);
            Require(instance.GetKillCount(player) == 2, "非法 mobId 不改计数");

            instance.Dispose();
            Require(instance.GetKillCount(player) == 0, "dispose 之后 killCount 清空");
        }
        finally { service.Clear(); }
        Step("monsterValue：按脚本返回值记账，getKillCount 可读，dispose 清零");
    }

    // ——— 构造脚本侧对象 ———
    static JsHostObject Interaction()
    {
        var api = new JsHostObject(null);
        api.Set("gainItem", JsFunction.FromNative("gainItem", c => JsNull.Value));
        api.Set("removeAll", JsFunction.FromNative("removeAll", c => JsNull.Value));
        return api;
    }

    static JsHostObject Player(int id, int mapId = RecruitMap)
    {
        var host = new JsHostObject(null);
        host.Set("getId", JsFunction.FromNative("getId", c => new JsNumber(id)));
        host.Set("getMapId", JsFunction.FromNative("getMapId", c => new JsNumber(mapId)));
        host.Set("getName", JsFunction.FromNative("getName", c => new JsString("hero" + id)));
        host.Set("hasEmptySlot", JsFunction.FromNative("hasEmptySlot", c => JsBoolean.Of(true)));
        host.Set("dropMessage", JsFunction.FromNative("dropMessage", c => JsNull.Value));
        host.Set("getAbstractPlayerInteraction", JsFunction.FromNative("getAbstractPlayerInteraction", c => Interaction()));
        return host;
    }

    static JsHostObject Party(JsHostObject leader, int leaderId)
    {
        var host = new JsHostObject(null);
        host.Set("getLeaderId", JsFunction.FromNative("getLeaderId", c => new JsNumber(leaderId)));
        host.Set("getLeader", JsFunction.FromNative("getLeader", c => leader));
        return host;
    }

    static JsHostObject Map(int id)
    {
        var host = new JsHostObject(null);
        host.Set("getId", JsFunction.FromNative("getId", c => new JsNumber(id)));
        return host;
    }

    // 远征队宿主的最小面：EventManager.startInstance(Expedition) 只用到 getLeader + 本体。
    static JsHostObject Host(Expedition exped)
    {
        var host = new JsHostObject(exped);
        host.Set("getLeader", JsFunction.FromNative("getLeader", c => Player(exped.LeaderId)));
        return host;
    }

    static JsValue Call(NativeEventScriptService.Manager manager, string name, params JsValue[] args)
        => manager.Host.Get(name) is JsFunction function
            ? function.Native(new JsCall { This = manager.Host, Args = args })
            : JsNull.Value;

    static bool Start(NativeEventScriptService.Manager manager, string name, params JsValue[] args)
        => JsConvert.ToBoolean(Call(manager, name, args));

    static NativeEventScriptService.Instance InstanceOf(NativeEventScriptService.Manager manager, int lobby)
    {
        // setup() 里是 em.newInstance('lobby'+lobby)，名字由脚本决定；这里按同一个键取回宿主。
        if (!(manager.Host.Get("getInstance") is JsFunction get)) return null;
        var value = get.Native(new JsCall
        {
            This = manager.Host,
            Args = new JsValue[] { new JsString("lobby" + lobby) }
        });
        return (value as JsHostObject)?.Host as NativeEventScriptService.Instance;
    }
}
