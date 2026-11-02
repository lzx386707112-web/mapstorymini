using System;
using System.Collections.Generic;
using System.IO;
using Server.Bot;
using Server.Bot.Types.Pq;
using UnityEditor;
using UnityEngine;

// 组队任务假人的数据回归。四副本（废都 / 玩具塔101 / 毒雾森林 / 海盗船）的
// 地图、NPC、物品、关键词全部是数据表 PqCatalog，只要有一处打错，表现就是
// 「假人在入口凑齐了人，进本后卡在第一关」——很难靠肉眼发现。
//
// 这里只做纯数据断言（不渲染、不建运行时），可以在 batchmode 里跑：
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod PqChecks.Run -logFile /tmp/pq.log
public static class PqChecks
{
    static int pass, fail;
    static void Require(bool ok, string what)
    {
        if (ok) { pass++; return; }
        fail++;
        Debug.LogError("[Pq] 失败：" + what);
    }
    static void Step(string what) => Debug.Log("[Pq] 通过：" + what);

    public static void Run()
    {
        pass = fail = 0;
        try
        {
            var maps = LoadMapIndex();
            Require(maps.Count > 0, "地图索引 Bot/Maps.json 读不到（后续地图断言会全部跳过）");
            CheckStartupLatency();
            CheckShardResidency();
            foreach (var def in PqCatalog.All) CheckDef(def, maps);
        }
        catch (Exception error)
        {
            fail++;
            Debug.LogError("[Pq] 异常：" + error);
        }
        Debug.Log("[Pq] 结束：通过 " + pass + "，失败 " + fail);
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    // 进图响应回归：入口假人是 CreateBotPollReadiness(..., playArrival:false) 生成的，
    // 没有出生编舞可等。ManuallyStartBot 默认仍按 7~10s 推迟首 tick，若 PqBot 没声明
    // SkipsSpawnChoreography，表现就是「走到假人面前它们愣好几秒才开始动」。
    static void CheckStartupLatency()
    {
        Require(BotGeneration.SpawnChoreographyMaxMs >= 7000,
            "出生编舞窗口常量变了，请同步核对 BotTypeManager.ManuallyStartBot 的首 tick 错峰");
        int totalMaps = 0;
        foreach (var def in PqCatalog.All)
        {
            var probe = new PqBot(new BotCharacter { mapId = def.recruitMap });
            Require(probe.SkipsSpawnChoreography, def.key + " 的 PqBot 没跳过出生编舞（首 tick 会白等 7~10 秒）");
            totalMaps += 3 + def.stages.Count;   // 招募 / 入口 / 出口 / 各阶段，预热队列按这个铺
        }
        Require(totalMaps >= 30, "组队任务地图总数异常（" + totalMaps + "），预热队列可能漏图");
        Step("进图响应：PqBot 跳过出生编舞，预热覆盖 " + totalMaps + " 张图");
    }

    // 分片常驻回归。反复卡顿的根因：Adopt 会 BotWarmup.Release 掉预热表引用，
    // 而 MaxCachedShards=2 又把 7 个分片互相挤出 shardCache —— 副本内每换一张阶段图
    // 就得重新在主线程 PlatformRes.LoadText 一个 1MB 级 TextAsset。
    // 这里锁住「pin 接口还在 + PrefetchAll 覆盖全部 PQ 地图」，防止被改回去。
    static void CheckShardResidency()
    {
        var raw = LoadRawIndex();
        var shards = new HashSet<string>();
        foreach (var def in PqCatalog.All)
        {
            void Add(int id)
            {
                if (raw.TryGetValue(id.ToString(), out var value) && value is string path && path.Length > 0) shards.Add(path);
            }
            Add(def.recruitMap); Add(def.entryMap); Add(def.exitMap);
            foreach (var stage in def.stages) Add(stage.mapId);
        }
        Require(shards.Count == 7,
            "组队任务分片数不是 7（实际 " + shards.Count + "），pin 常驻与预热口径要跟着改");

        var loader = typeof(Server.Bot.Environment.BotNavigationMapLoader);
        Require(loader.GetMethod("PinShard") != null, "PinShard 被删了：分片会被 LRU 驱逐，进本反复读 TextAsset");
        Require(loader.GetMethod("IsPinned") != null, "IsPinned 被删了");
        Require(loader.GetProperty("PinnedCount") != null, "PinnedCount 被删了");
        Require(typeof(PqDirector).GetMethod("PrefetchAll") != null, "PrefetchAll 被删了：加载页退场前排不完分片");

        // 退场前一次排完，必须真的覆盖每张 PQ 图（不能只推队列前几张）。
        PqDirector.Reset();
        var collected = new List<int>();
        PqDirector.PrefetchMap = collected.Add;
        PqDirector.PrefetchAll();
        PqDirector.PrefetchMap = null;
        foreach (var def in PqCatalog.All)
        {
            Require(collected.Contains(def.recruitMap), def.key + " 招募图没进预热队列");
            Require(collected.Contains(def.entryMap), def.key + " 入口图没进预热队列");
            Require(collected.Contains(def.exitMap), def.key + " 出口图没进预热队列");
            foreach (var stage in def.stages)
                Require(collected.Contains(stage.mapId), def.key + " 阶段图 " + stage.mapId + " 没进预热队列");
        }
        PqDirector.Reset();
        Step("分片常驻：7 个分片已 pin，PrefetchAll 覆盖 " + collected.Count + " 张图");
    }

    // Maps.json 只是索引（{ version, maps: { id -> 分片路径 } }），直接读磁盘，
    // 不用走 BotWarmup 的后台解析——回归脚本不该依赖预热线程。
    static Dictionary<string, object> LoadRawIndex()
    {
        string path = Path.Combine(Application.dataPath, "Resources/Bot/Maps.json");
        if (!File.Exists(path)) return new Dictionary<string, object>();
        var root = MiniJsonDb.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
        if (root != null && root.TryGetValue("maps", out var node) && node is Dictionary<string, object> maps) return maps;
        return new Dictionary<string, object>();
    }

    static HashSet<string> LoadMapIndex() => new HashSet<string>(LoadRawIndex().Keys);

    static void CheckDef(PqDef def, HashSet<string> maps)
    {
        string tag = def.key + "(" + def.name + ")";
        Require(!string.IsNullOrEmpty(def.key), tag + " 缺 key");
        Require(!string.IsNullOrEmpty(def.eventName), tag + " 缺事件脚本名");
        Require(def.minLevel > 0, tag + " 缺等级门槛");
        Require(def.recruitMap > 0 && def.entryMap > 0 && def.exitMap > 0, tag + " 地图 id 不完整");

        // —— 地图 ——
        if (maps.Count > 0)
            foreach (var (id, what) in new[] { (def.recruitMap, "招募图"), (def.entryMap, "入口图"), (def.exitMap, "出口图") })
                Require(maps.Contains(id.ToString()), tag + " 的" + what + " " + id + " 不在地图包里（假人会落不了地）");

        // —— 阶段 ——
        Require(def.stages.Count >= 2, tag + " 阶段太少（" + def.stages.Count + "）");
        Require(def.stages.Count > 0 && def.stages[0].mapId == def.entryMap,
            tag + " 第一阶段不是入口图（" + (def.stages.Count > 0 ? def.stages[0].mapId : 0) + " != " + def.entryMap + "）");
        var seen = new HashSet<int>();
        int lastStageNo = 0;
        foreach (var stage in def.stages)
        {
            string st = tag + " 阶段 " + stage.stageNo + "(" + stage.title + ")";
            Require(stage.mapId > 0, st + " 缺地图 id");
            Require(seen.Add(stage.mapId), st + " 地图 " + stage.mapId + " 重复出现");
            Require(stage.stageNo >= lastStageNo, st + " 阶段号倒退");
            lastStageNo = stage.stageNo;
            if (maps.Count > 0) Require(maps.Contains(stage.mapId.ToString()), st + " 地图 " + stage.mapId + " 不在地图包里");
            if (stage.npc != 0)
                Require(ScriptSource.Has("npc", stage.npc), st + " 的 NPC " + stage.npc + " 没有脚本（静默代点会直接失败）");
            if (stage.requireItem != 0)
                Require(stage.requireItem / 1000000 == 4, st + " 的 requireItem " + stage.requireItem + " 不是道具（应按 4xxxxxx 的消耗/其他栏）");
            if (stage.requireItem != 0) Require(stage.requireCount > 0, st + " 有 requireItem 但 requireCount <= 0");
            int stepsWithItems = 0;
            foreach (var step in stage.steps)
            {
                if (step.npc != 0)
                    Require(ScriptSource.Has("npc", step.npc), st + " 的步骤 NPC " + step.npc + " 没有脚本");
                if (step.action == PqAction.GrantItem)
                {
                    stepsWithItems++;
                    Require(step.itemId > 0, st + " 有 GrantItem 步骤但没写 itemId");
                    Require(step.itemCount > 0, st + " 的 GrantItem " + step.itemId + " 数量 <= 0");
                }
                if (step.action == PqAction.BreakReactors || step.action == PqAction.KillMobs)
                    Require(step.ids.Length > 0 || step.minId > 0, st + " 的 " + step.action + " 既没有 ids 也没有 id 区间");
                if (step.action == PqAction.Stand)
                    Require(step.stands != null && step.stands.Count > 0, st + " 的 Stand 没给矩形");
            }
            // 阶段要求队长持道具，却没有任何一条交道具的步骤 —— 静默点 NPC 时数量会是 0。
            if (stage.requireItem != 0 && stepsWithItems == 0 && stage.npc == 0)
                Require(false, st + " 要求持有 " + stage.requireItem + " 却没写 GrantItem 步骤（也没 NPC 兜底）");
        }
        Step(tag + " 地图/阶段/NPC/道具 共 " + def.stages.Count + " 阶段");

        // —— 关键词闭环：假人喊的话必须能触发它自己 ——
        Require(def.keywords != null && def.keywords.Length >= 5, tag + " 关键词太少（" + (def.keywords?.Length ?? 0) + "）");
        foreach (string generic in new[] { "求组", "缺一", "缺二", "q1", "q2" })
            Require(PqDirector.Matches(def, "有人吗 " + generic), tag + " 的通用关键词「" + generic + "」没有命中");
        for (int i = 0; i < 12; i++)
        {
            string line = PqChatLines.Recruit(def.key, new System.Random(i));
            Require(!string.IsNullOrEmpty(line), tag + " 第 " + i + " 条招募喊话是空的");
            Require(PqDirector.Matches(def, line), tag + " 的招募喊话「" + line + "」不会触发自己的组队");
            Require(!PqDirector.Matches(Other(def), line) || Shares(def, line),
                tag + " 的招募喊话「" + line + "」会把玩家带偏到别的副本");
            string role = PqChatLines.Role(def.key, new System.Random(i));
            Require(!string.IsNullOrEmpty(role), tag + " 第 " + i + " 条分工台词是空的");
        }
        Step(tag + " 关键词闭环 " + def.keywords.Length + " 个 / 喊话 12 条");

        Require(!string.IsNullOrEmpty(PqChatLines.Decline(new System.Random(1))), "拒绝理由为空");
        Require(!string.IsNullOrEmpty(PqChatLines.Idle(new System.Random(1))), "闲聊台词为空");
        Require(!string.IsNullOrEmpty(PqChatLines.Answer(new System.Random(1))), "接话台词为空");
        Require(PqChatLines.beforeEnter.Length > 0, "进本前喊话为空");
        Require(PqChatLines.afterClear.Length > 0, "通关喊话为空");
    }

    // 四个副本的入口图互不相同：一句话只该属于一张图。
    static PqDef Other(PqDef def)
    {
        foreach (var other in PqCatalog.All) if (!ReferenceEquals(other, def)) return other;
        return null;
    }

    // 「求组/缺一/q1」这类通用词本来就跨副本共用，只要命中的是通用词就不算串台。
    static bool Shares(PqDef def, string line)
    {
        foreach (string keyword in new[] { "求组", "缺一", "缺二", "缺三", "q1", "q2", "组队" })
            if (line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
