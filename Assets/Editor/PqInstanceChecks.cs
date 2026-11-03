using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using MapleDash.Js;
using UnityEditor;
using UnityEngine;

// 组队任务实例图回归（batchmode，不进 Play）：
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod PqInstanceChecks.Run -logFile /tmp/pq_instance.log
//
// 症状：废弃都市 PQ 进本后全程无怪——源端 getMapInstance 返回的是频道地图，刷怪系统
// 自开服起就在跑；KerningPQ 的 respawnStages 只调 instanceMapRespawn、从不 resetPQ。
// 移植端此前要等脚本 resetPQ 才建刷怪点，结果第一关鳄鱼(9300001)不存在，
// 通行证(4001007)无从掉落，整条 PQ 卡死（LudiPQ/EllinPQ/PiratePQ/HenesysPQ 都在
// setup 里 resetPQ，所以只有废都中招）。
//
// 断言（全部跑真实 event/*.js）：
//   1. 四个 PQ 跑完 setup 后入口图 countMonsters() > 0；KPQ 第一关必须 ≥ 模板 life 数
//   2. 杀掉一只怪 countMonsters 递减（实例图可打可清，killMonster(hasKiller=true) 不抛）
//   3. KPQ 鳄鱼 9300001 带 4001007 掉落行（源端 drop_data 20%、questid=0），
//      大样本击杀命中率与 chance/999999 在 4σ 内
public static class PqInstanceChecks
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    sealed class Def
    {
        public string eventName;
        public int entry, recruit, minLevel;
        public int expectMin;
    }

    // expectMin 按 map life 的怪数下限取（核对过 IMG）：废都第一关 22 只鳄鱼、
    // 玩具塔 25 只、海盗 26 只；HenesysPQ 入口图也有模板怪。
    static readonly Def[] Defs =
    {
        new Def { eventName = "KerningPQ", entry = 103000800, recruit = 103000000, minLevel = 21, expectMin = 20 },
        new Def { eventName = "LudiPQ", entry = 922010100, recruit = 221024500, minLevel = 35, expectMin = 20 },
        new Def { eventName = "EllinPQ", entry = 930000000, recruit = 300030100, minLevel = 45, expectMin = 0 },
        new Def { eventName = "PiratePQ", entry = 925100000, recruit = 251010404, minLevel = 55, expectMin = 20 },
        new Def { eventName = "HenesysPQ", entry = 910010000, recruit = 100000200, minLevel = 10, expectMin = 5 },
    };

    static int pass, fail;
    static void Require(bool ok, string what)
    {
        if (ok) { pass++; Debug.Log("[PqInstance] 通过 " + what); return; }
        fail++;
        Debug.LogError("[PqInstance] 失败：" + what);
    }
    static void Set(string key, object value)
        => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null, new[] { value });
    static object InvokeStatic(Type type, string name, params object[] args)
        => type.GetMethod(name, Flags).Invoke(null, args);

    sealed class Probe
    {
        readonly JsEngine engine;
        public Probe(string body, params (string, JsValue)[] bindings)
        {
            engine = new JsEngine("function check(){" + body + "}", "pq-instance-probe");
            foreach (var binding in bindings) engine.Bind(binding.Item1, binding.Item2);
            if (GameSession.Server != null) engine.Interp.ResolveHostType = GameSession.Server.Scripts.ResolveHostType;
            engine.Run();
        }
        public int Int() => JsConvert.ToInt32(engine.Call("check"));
        public JsValue Value() => engine.Call("check");
    }

    static int MobCount(JsValue mapHost) => new Probe("return m.countMonsters();", ("m", mapHost)).Int();
    static JsValue EntryMap(JsValue eim, int mapId)
        => new Probe("return e.getMapInstance(" + mapId + ");", ("e", eim)).Value();

    // —— 源端 EventManager.startInstance(party, map) 的最小队伍替身 ——
    // leader 必须是本地角色（NativeEventScriptService.StartParty 校验 LocalPlayerId），
    // playerEntry 里脚本会调 player.changeMap(...)，dropAllExclusiveItems 会调
    // player.removeAll(item)（清上一局的通行证）。
    static (JsValue party, JsValue leader) FakeParty(int characterId, int recruitMap)
    {
        var member = new JsHostObject(null);
        member.Set("getId", JsFunction.FromNative("getId", _ => new JsNumber(characterId)));
        member.Set("getMapId", JsFunction.FromNative("getMapId", _ => new JsNumber(recruitMap)));
        member.Set("getLevel", JsFunction.FromNative("getLevel", _ => new JsNumber(35)));
        member.Set("isLeader", JsFunction.FromNative("isLeader", _ => JsBoolean.Of(true)));
        member.Set("changeMap", JsFunction.FromNative("changeMap", _ => JsUndefined.Value));
        member.Set("removeAll", JsFunction.FromNative("removeAll", _ => JsUndefined.Value));

        var list = new JsHostObject(null);
        var array = new JsArray();
        array.Items.Add(member);
        list.Set("size", JsFunction.FromNative("size", _ => new JsNumber(1)));
        list.Set("get", JsFunction.FromNative("get", c => array.GetIndex(JsConvert.ToInt32(c.Arg(0)))));
        list.Set("isEmpty", JsFunction.FromNative("isEmpty", _ => JsBoolean.Of(false)));
        list.Set("toArray", JsFunction.FromNative("toArray", _ => array));

        var party = new JsHostObject(null);
        party.Set("getPartyMembersOnline", JsFunction.FromNative("getPartyMembersOnline", _ => list));
        party.Set("getLeaderId", JsFunction.FromNative("getLeaderId", _ => new JsNumber(characterId)));
        party.Set("getLeader", JsFunction.FromNative("getLeader", _ => member));

        var recruit = new JsHostObject(null);
        recruit.Set("getId", JsFunction.FromNative("getId", _ => new JsNumber(recruitMap)));
        return (party, member);
    }

    static JsValue CallMember(JsHostObject host, string member, params JsValue[] args)
    {
        if (!(host.Get(member) is JsFunction fn))
            throw new JsRuntimeException("Unmigrated event manager API: " + member);
        return fn.Native(new JsCall { This = host, Args = args });
    }

    public static void Run()
    {
        pass = fail = 0;
        string dbPath = Path.Combine(Path.GetTempPath(), "pq-instance-" + Guid.NewGuid().ToString("N") + ".json");
        var dataField = typeof(Profile).GetField("data", Flags);
        var savingField = typeof(Profile).GetField("debugOverride", Flags);
        object oldData = dataField.GetValue(null), oldSaving = savingField.GetValue(null);
        int oldMap = Profile.LastMapId;
        var errors = new List<string>();
        Application.LogCallback log = (text, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) errors.Add(text);
        };
        int exitCode = 0;
        try
        {
            savingField.SetValue(null, true);
            var db = DbStore.Create(dbPath);
            int account = AccountsDao.AutoRegister(db, "pqreview", "isolated");
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", account); Set("InGame", true);
            ScriptSource.ClearCache();
            Application.logMessageReceived += log;

            var record = new CharRecord
            {
                accountid = account, name = "PqReview", level = 35, face = 20000, hair = 30300,
                hp = 500, maxhp = 500, maxmp = 500,
                equipslots = 96, useslots = 96, setupslots = 96, etcslots = 96, meso = 1000000
            };
            record.id = CharactersDao.Insert(db, record);
            int cid = (int)record.id;
            Set("CharacterId", cid);
            Set("Character", CharService.Load(db, cid));
            GameSession.ApplyToProfile();

            FieldCatalog.Ensure();
            Require(FieldCatalog.Ready, "FieldCatalog 加载（怪 " + FieldCatalog.Mobs.Count + " 种）");

            foreach (int itemId in new[] {4001007,4001008})
            {
                var icons = (Sprite[])typeof(GroundItem).GetMethod("LoadIcons", Flags).Invoke(null,new object[] {itemId,1});
                Require(icons != null && icons.Length > 0 && icons[0] != null, "PQ " + itemId + " 可加载实际地面掉落图标");
            }

            var host = (NativeEventScripts)InvokeStatic(typeof(NativeEventScripts), "Current");
            Require(host != null, "事件宿主就绪");
            var service = (NativeEventScriptService)typeof(NativeEventScripts)
                .GetField("service", Flags).GetValue(host);
            Require(service != null, "事件服务就绪");
            if (service == null) { EditorApplication.Exit(1); return; }

            foreach (var def in Defs)
            {
                var manager = service.GetManager(def.eventName);
                Require(manager != null, def.eventName + " 事件脚本加载");
                if (manager == null) continue;

                // 真实 Social → NpcApi → event JS 链路，不能再用 fake 配置掩盖缺失等级上限。
                var parties = server.Social.Parties;
                parties.Leave(cid);
                var local = parties.Actor(record);
                local.mapId = def.recruit; local.level = def.minLevel + 5;
                Require(parties.Create(local), def.eventName + " 创建实际队伍");
                var botIds = new List<int>();
                for (int i = 0; i < 5; i++)
                {
                    var bot = new Server.Bot.BotCharacter { id = 2000000 + i, name = "PQ" + i,
                        level = def.minLevel + 5, mapId = def.recruit, world = local.world };
                    Server.Bot.BotRegistry.Add(bot); botIds.Add(bot.id);
                    Require(parties.Join(bot, parties.For(cid).id), def.eventName + " 假人入队 " + i);
                }
                var actualParty = (JsValue)InvokeStatic(typeof(NpcApi), "BuildParty", new object[] { null });
                int eligible = new Probe("return em.getEligibleParty(p).length;", ("em", manager.Host), ("p", actualParty)).Int();
                Require(eligible == 6, def.eventName + " 真实六人队伍资格=" + eligible);
                var (party, leader) = FakeParty(cid, def.recruit);
                var recruitHost = new JsHostObject(null);
                recruitHost.Set("getId", JsFunction.FromNative("getId", _ => new JsNumber(def.recruit)));
                // startInstance 按源端返回 boolean；实例本体从会话侧取（cm.getEventInstance 同口径）。
                var started = JsConvert.ToBoolean(CallMember(manager.Host, "startInstance", party, recruitHost));
                Require(started, def.eventName + " startInstance 建立实例");
                if (!started) continue;
                var eim = NativeEventScripts.Instance();
                Require(eim is JsHostObject, def.eventName + " 会话侧能取到 eim");
                if (!(eim is JsHostObject)) continue;

                var map = EntryMap(eim, def.entry);
                Require(map is JsHostObject, def.eventName + " 入口图实例化");
                if (!(map is JsHostObject)) continue;

                var pq = Server.Bot.Types.Pq.PqCatalog.ByStageMap(def.entry);
                if (pq != null && map is JsHostObject mapWrapper && mapWrapper.Host is NativeEventMap instanceMap)
                {
                    var previousParties = Server.Bot.BotTypeManager.Parties;
                    var previousPlayerId = Server.Bot.Types.Pq.PqDirector.LocalPlayerId;
                    try
                    {
                        Server.Bot.BotTypeManager.Parties = parties;
                        Server.Bot.Types.Pq.PqDirector.LocalPlayerId = () => cid;
                        var fieldObject = new GameObject("PqMapIdentityCheck");
                        try
                        {
                            var field = fieldObject.AddComponent<FieldDirector>();
                            typeof(FieldDirector).GetProperty("Map").GetSetMethod(true).Invoke(field, new object[] { instanceMap.Template });
                            typeof(FieldDirector).GetProperty("EventMap").GetSetMethod(true).Invoke(field, new object[] { instanceMap });
                            Require(field.BotMapId == def.entry && field.StateId == instanceMap.Scope,
                                def.eventName + " 场景导航/视图与实例战斗编号分离");
                            local.mapId = field.BotMapId;
                        }
                        finally { UnityEngine.Object.DestroyImmediate(fieldObject); }
                        Require(instanceMap.Scope < 0 && instanceMap.Geometry.templateMapId == def.entry,
                            def.eventName + " 实例负编号保留原地图身份");
                        InvokeStatic(typeof(Server.Bot.Types.Pq.PqDirector), "AdoptStageEntry", local, 1000L);
                        var run = Server.Bot.Types.Pq.PqDirector.RunOf(parties.For(cid).id);
                        Require(run != null && run.Bots.Count == 5, def.eventName + " 实例入场接管五名队友");
                        run?.Tick(1500);
                        Require(botIds.TrueForAll(id => Server.Bot.BotRegistry.Get(id).mapId == instanceMap.Id),
                            def.eventName + " 五名假人进入当前阶段导航地图");
                        Require(Server.Bot.BotRegistry.OnMap(instanceMap.Id).Count == 5,
                            def.eventName + " 地图渲染注册表能取到五名队友");
                        if (pq.stages.Count > 1)
                        {
                            var nextHost = (JsHostObject)EntryMap(eim, pq.stages[1].mapId);
                            var nextMap = (NativeEventMap)nextHost.Host;
                            local.mapId = nextMap.Id;
                            run?.Tick(2000);
                            Require(botIds.TrueForAll(id => Server.Bot.BotRegistry.Get(id).mapId == nextMap.Id),
                                def.eventName + " 下一阶段仍跟随当前导航地图");
                        }
                    }
                    finally
                    {
                        Server.Bot.Types.Pq.PqDirector.Reset();
                        Server.Bot.BotTypeManager.Parties = previousParties;
                        Server.Bot.Types.Pq.PqDirector.LocalPlayerId = previousPlayerId;
                    }
                }
                parties.Leave(cid);
                foreach (int id in botIds) Server.Bot.BotRegistry.Remove(id);

                int count = MobCount(map);
                Require(count >= def.expectMin,
                    def.eventName + " 入口图 " + def.entry + " 初始刷怪 " + count + " ≥ " + def.expectMin);

                if (def.eventName == "KerningPQ")
                {
                    // 源端 respawnStages 每 15 秒一次 instanceMapRespawn：刷怪点已建好时必须能补怪
                    new Probe("m.instanceMapRespawn();return 0;", ("m", map)).Int();
                    int afterRespawn = MobCount(map);
                    Require(afterRespawn >= count, "KPQ instanceMapRespawn 后怪数不减少（" + afterRespawn + "≥" + count + "）");

                    // 杀一只：实例图可打可清，且 killMonster(hasKiller=true) 不抛
                    int before = MobCount(map);
                    new Probe("m.killMonster(m.getAllMonsters().get(0), true);return 0;", ("m", map)).Int();
                    Require(MobCount(map) == before - 1, "KPQ killMonster 后怪数递减（" + before + "→" + MobCount(map) + "）");

                    int actualMob = new Probe("return m.getAllMonsters().get(0).getId();", ("m", map)).Int();
                    Require(actualMob == 9300001, "KPQ 实例入口实际怪物 ID=" + actualMob);
                    FieldCatalog.LoadMob(actualMob);
                    Require(FieldCatalog.Mobs[actualMob].id == actualMob, "怪物 IMG link 不替换掉落模板 ID");
                    foreach (int passMob in new[] { 9300000, 9300002, 9300003 })
                    {
                        int passes = 0;
                        for (int i = 0; i < 100; i++)
                            FieldLoot.Roll(passMob, (item, qty, d) => { if (item == 4001008) passes += qty; },
                                new System.Random(i), 1, QuestLogic.NeedQuestItem);
                        Require(passes == 100, "KPQ " + passMob + " 每次掉通行证：" + passes + "/100");
                    }

                    // 掉落行：源端 drop_data (9300001, 4001007, 20%)，questid=0 不拦拾取
                    var row = DropStore.ForMob(9300001).Find(r => r.item == 4001007);
                    Require(row != null, "鳄鱼(PQ) 9300001 有 4001007 掉落行");
                    if (row != null)
                    {
                        Require(row.quest == 0, "4001007 questid=0（源端如此，任何队员可拾）");
                        Require(row.chance == 200000, "4001007 chance=" + row.chance + "，源端 200000");

                        const int trials = 20000;
                        var rng = new System.Random(20261102);
                        int hits = 0;
                        for (int i = 0; i < trials; i++)
                            FieldLoot.Roll(9300001, (itemId, qty, d) => { if (itemId == 4001007) hits++; },
                                rng, 1.0, QuestLogic.NeedQuestItem);
                        double p = (double)row.chance / DropStore.ChanceDenominator;
                        double expected = p * trials, sd = Math.Sqrt(expected * (1 - p));
                        Require(Math.Abs(hits - expected) <= 4 * sd + 3,
                            $"通行证命中率 {hits}/{trials}（期望 {expected:F0}±{4 * sd:F0}）");
                    }
                }
            }

            Require(errors.Count == 0, "全程不应出现 Error/Exception 日志：" + string.Join(" | ", errors));
            foreach (var h in UnityEngine.Object.FindObjectsOfType<NativeEventScripts>())
                UnityEngine.Object.DestroyImmediate(h.gameObject);
        }
        catch (Exception error)
        {
            Debug.LogError("[PqInstance] 失败：" + error);
            exitCode = 1;
        }
        finally
        {
            Application.logMessageReceived -= log;
            NpcSession.CloseActive();
            GameServer.Stop(); Set("InGame", false);
            dataField.SetValue(null, oldData); savingField.SetValue(null, oldSaving); Profile.LastMapId = oldMap;
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
        Debug.Log("[PqInstance] 结束：通过 " + pass + "，失败 " + fail);
        if (fail > 0 || exitCode != 0) EditorApplication.Exit(1);
    }
}
