using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MapleDash.Js;
using Server.Field;

// 野外 BOSS 事件链实机自检（batchmode，直接跑源端 event/AreaBoss*.js 原文）：
//   Unity -batchmode -projectPath <proj> -executeMethod WildBossEventChecks.Run -logFile /tmp/wild_event.log
//
// 覆盖的是「传送脚本万能传送 → 野外Boss」里那些地图本身 life 没有 Boss 的情况：
// 源端靠 Channel 启动时 init 掉的 AreaBoss*.js 把它们刷进地图，移植端原来一步都没跑。
public static class WildBossEventChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    static long now;
    static string lastError;

    public static void Run()
    {
        try { Body(); }
        catch (Exception error)
        {
            Debug.LogError("[WildBossEvent] 失败：" + error.Message);
            // 事件服务仍挂着定时任务时，batchmode 靠 executeMethod 自然返回会一直不退出，
            // 必须显式 Exit 才能让 -executeMethod 的调用方拿到退出码。
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw;
        }
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Body()
    {
        FieldCatalog.Ensure();
        FieldDirector.ResetWildBosses();
        now = 0;
        lastError = null;

        var service = new NativeEventScriptService(ScriptSource.LoadKey, () => now, engine =>
        {
            var java = NpcApi.BuildJava();
            engine.Interp.ResolveHostType = type =>
                engine.Interp.Call(engine.Interp.GetMember(java, "type"), java, new JsValue[] { new JsString(type) });
        });
        const BindingFlags any = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var mapContext = typeof(NpcSession).GetMethod("MapContext", any);
        var buildChannel = typeof(NpcApi).GetMethod("BuildChannelServer", any);
        Require(mapContext != null && buildChannel != null, "找不到内部方法 MapContext/BuildChannelServer");
        service.ChannelServer = () =>
        {
            var session = (NpcSession)mapContext.Invoke(null, null);
            var player = NpcApi.BuildPlayer(session);
            return (JsValue)(JsHostObject)buildChannel.Invoke(null, new object[] { player });
        };
        service.Error = error => lastError = error.Message;
        // 检测周期压成 0：一次 Tick 就够了，不用真等 60 分钟
        service.BossTime = value => 0;

        // ——— 1. 源端脚本原文 + 真实事件引擎：start() 应该把红蜗牛王登记进 104000400 ———
        var manager = service.GetManager("AreaBossMano");
        Require(manager != null, "event/AreaBossMano.js 没有加载（脚本包里缺文件？）");
        service.Tick();
        Require(lastError == null, "AreaBossMano 执行报错：" + lastError);
        Require(FieldDirector.HasWildBoss(104000400, 2220000),
            "AreaBossMano.start() 没有把红蜗牛王登记到地图 104000400");

        var host = new GameObject("WildBossEventChecks");
        try
        {
            var field = host.AddComponent<FieldDirector>();
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var buildPoints = typeof(FieldDirector).GetMethod("BuildPoints", instance);
            var materialize = typeof(FieldDirector).GetMethod("MaterializeWildBosses", instance);
            var mapProperty = typeof(FieldDirector).GetProperty("Map");
            var map = FieldCatalog.GetMap(104000400);
            Require(map.spawns.TrueForAll(s => s.mob != 2220000), "地图 life 里本来就不该有红蜗牛王（本用例测事件刷怪）");

            void Enter()
            {
                mapProperty.SetValue(field, map);
                buildPoints.Invoke(field, new object[] { map });
                materialize.Invoke(field, new object[] { map });
            }

            Enter();
            var boss = Find(2220000);
            Require(boss != null, "进图后没有生成红蜗牛王");
            var p = boss.transform.position;
            Require(p.x >= map.ViewMinX && p.x <= map.ViewMaxX && p.y >= map.ViewMinY && p.y <= map.ViewMaxY,
                "红蜗牛王刷在可视范围外：x=" + p.x + " y=" + p.y);

            // ——— 2. 击杀后登记册要释放（否则 AreaBoss 永远认为 Boss 还在，就再也不刷了）———
            typeof(FieldDirector).GetMethod("MarkWildBossKilled", any)
                .Invoke(null, new object[] { 104000400, 2220000 });
            boss.dead = true;
            UnityEngine.Object.DestroyImmediate(boss.gameObject);
            Require(!FieldDirector.HasWildBoss(104000400, 2220000), "击杀后登记册没有释放");

            Enter();
            Require(Find(2220000) == null, "击杀后回图又冒出一只（冷却应当是下一个 AreaBoss 检测周期）");

            // ——— 3. 下一个检测周期：脚本再刷一次，玩家就站在图里也要立刻出现 ———
            service.Tick();
            Require(lastError == null, "AreaBossMano 第二次执行报错：" + lastError);
            Require(FieldDirector.HasWildBoss(104000400, 2220000), "第二个检测周期没有重新登记红蜗牛王");
            // 游戏里这一步由 Update 里的 wildDirty 分支做，这里直接调同一个方法
            materialize.Invoke(field, new object[] { map });
            Require(Find(2220000) != null, "玩家就在图里，重新登记后没有立刻生成红蜗牛王");
            Require(!FieldDirector.HasWildBoss(999999999, 2220000), "别的图被误登记了");
        }
        finally
        {
            foreach (var unit in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
                UnityEngine.Object.DestroyImmediate(unit.gameObject);
            UnityEngine.Object.DestroyImmediate(host);
            FieldDirector.ResetWildBosses();
        }

        // ——— 4. 按图 id 命名的 onUserEnter（源端 MapFactory 的默认值就是地图 id）———
        var scriptName = typeof(NativeMapScripts).GetMethod("ScriptName", any);
        Require(scriptName != null, "找不到 NativeMapScripts.ScriptName");
        string Name(int mapId, string declared) => (string)scriptName.Invoke(null, new object[] { mapId, declared });
        Require(Name(677000003, "") == "677000003", "按图 id 命名的 onUserEnter 没有生效（黑暗独角兽藏身之地）");
        Require(Name(800040401, "") == "emperorToadBoss", "服务端 wz 独有的 onUserEnter 没有生效（枫城天皇蟾蜍）");
        Require(Name(800040208, "") == "armoredSamuraiBoss", "服务端 wz 独有的 onUserEnter 没有生效（甲胄武士）");
        Require(Name(100000000, "goAdventure") == "goAdventure", "WZ 里写明的 onUserEnter 被覆盖了");

        // ——— 5. 真的跑一遍 map/onUserEnter/677000003.js（Boss 藏身之地，life 里没有 Boss）———
        FieldDirector.ResetWildBosses();
        Profile.LastMapId = 677000003;
        var mapScripts = new NativeMapScriptService();
        var ms = NpcApi.Build((NpcSession)mapContext.Invoke(null, null), 0, 0, false);
        mapScripts.Run(1, 677000003, "onUserEnter/677000003", false, ScriptSource.LoadKey, ms, engine =>
        {
            var java = NpcApi.BuildJava();
            engine.Interp.ResolveHostType = type =>
                engine.Interp.Call(engine.Interp.GetMember(java, "type"), java, new JsValue[] { new JsString(type) });
        });
        Require(FieldDirector.HasWildBoss(677000003, 9400610),
            "map/onUserEnter/677000003.js 没有把黑暗独角兽刷进藏身之地");

        Debug.Log("WILD_BOSS_EVENT_PASS area_boss_spawn kill_release respawn_same_map mapid_onuserenter");
    }

    static EnemyUnit Find(int mob)
    {
        foreach (var unit in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
            if (unit.fieldMobId == mob && !unit.dead) return unit;
        return null;
    }
}
