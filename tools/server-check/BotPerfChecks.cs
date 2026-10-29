// 假人系统离线压测：只测 Server/ 纯 C# 层（模拟 + 调度 + 事件），不含 Unity 渲染。
// 目的：给「阶段1 零行为改动优化」提供可对比的基线数字，避免凭感觉优化。
// 输出统一为 [PERF] key=value，方便脚本比对。
// 运行：dotnet run --project tools/server-check -c Release
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Bot;
using Server.Bot.Environment;
using Server.Field;

static class BotPerfChecks
{
    const int Frames = 300;         // 压测帧数（60fps ≈ 5 秒）
    const int OnlineBots = 1000;    // 全服在线（只统计在册、不在当前图）
    // 60/120 = 现状（市场 30 + 城镇 14 的 4~8 倍）；200/400 = 「所有场景全开」后的量级。
    static readonly int[] BotsPerMapCases = { 60, 120, 200, 400 };

    static MapRuntime heavyMap;     // foothold 最多的图（建卡最贵）
    static MapRuntime walkMap;      // 有最长平台的图（测稳态移动）

    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("== 假人系统离线压测（Server 层）==");
        try
        {
            var loader = BotNavigationMapLoader.Load();
            PickMaps(loader);
            if (heavyMap == null) { Console.WriteLine("[PERF] skip=no-map"); return; }
            Console.WriteLine("[PERF] heavy_map=" + heavyMap.mapId + " footholds=" + heavyMap.footholds.Count);
            Console.WriteLine("[PERF] walk_map=" + walkMap.mapId + " footholds=" + walkMap.footholds.Count);

            foreach (int count in BotsPerMapCases) TerrainIndexCost(heavyMap, count);
            foreach (int count in BotsPerMapCases) SteadyStateCost(walkMap, count);
            TimerCost();
            RegistryScanCost(walkMap);
        }
        catch (Exception e)
        {
            Console.WriteLine("[PERF] error=" + e.GetType().Name + ":" + e.Message);
        }
        finally
        {
            Cleanup();
        }
    }

    // 两张图：建卡成本看 foothold 最多的，稳态移动要一段足够长的平台。
    static void PickMaps(BotNavigationMapLoader loader)
    {
        int[] candidates = { 910000000, 100000000, 100040000, 101000000, 200000000, 240000000, 104040000, 120000000, 240010000, 211000000 };
        float bestPlatform = 0;
        foreach (int id in candidates)
        {
            try
            {
                var map = loader.Geometry(id);
                if (map == null) continue;
                float len = 0;
                foreach (var f in map.footholds) if (!f.Wall) len = Math.Max(len, Math.Abs(f.x2 - f.x1));
                if (heavyMap == null || map.footholds.Count > heavyMap.footholds.Count) heavyMap = map;
                if (len > bestPlatform) { bestPlatform = len; walkMap = map; }
            }
            catch (ArgumentException) { }
        }
        if (walkMap == null) walkMap = heavyMap;
    }

    // —— A. 地形索引构建成本（BotGroundIndex/BotWalkRegions/BotAirCollision）——
    static void TerrainIndexCost(MapRuntime map, int count)
    {
        var bots = new List<BotCharacter>(count);
        for (int i = 0; i < count; i++)
            bots.Add(new BotCharacter { id = 50000 + i, mapId = map.mapId, x = 0, y = 0, facing = 1 });

        var watch = Stopwatch.StartNew();
        var runtimes = new List<BotMovementRuntime>(count);
        foreach (var bot in bots) runtimes.Add(new BotMovementRuntime(bot, map, BotMovementProfile.Base));
        watch.Stop();
        Console.WriteLine("[PERF] terrain_build bots=" + count +
            " total_ms=" + watch.Elapsed.TotalMilliseconds.ToString("F2") +
            " per_bot_ms=" + (watch.Elapsed.TotalMilliseconds / count).ToString("F3"));
        GC.KeepAlive(runtimes);
    }

    // —— B. 稳态每帧成本：同图 N 个假人全在移动，且都「在视野内」 ——
    static void SteadyStateCost(MapRuntime map, int count)
    {
        // 时钟必须先归位：BotTasks 的 due 是在注册瞬间用 BotScheduler.Now() 算的，
        // 若沿用上一轮的大时钟，这一轮所有 tick 都要等到那个时刻才开始，会测成 0。
        BotScheduler.Clock = 1000;
        MapService.Register(map);
        BotMovementService.IsObserved = _ => true;
        BotMovementService.ResolveProfile = (_, __) => BotMovementProfile.Base;

        FootholdSegment walk = null;
        foreach (var f in map.footholds)
            if (!f.Wall && (walk == null || Math.Abs(f.x2 - f.x1) > Math.Abs(walk.x2 - walk.x1))) walk = f;
        if (walk == null || Math.Abs(walk.x2 - walk.x1) < 200) { Console.WriteLine("[PERF] steady=skip-no-platform"); return; }
        int leftX = (int)Math.Min(walk.x1, walk.x2) + 20, rightX = (int)Math.Max(walk.x1, walk.x2) - 40;
        int GroundY(int x) => (int)(map.SnapToGroundRaw(x, walk.y1) ?? walk.y1);

        var bots = new List<BotCharacter>(count);
        for (int i = 0; i < count; i++)
        {
            int x = leftX + (i % 7) * 10;
            var bot = new BotCharacter { id = 51000 + i, mapId = map.mapId, facing = 1, x = x, y = GroundY(x) };
            BotRegistry.Add(bot);
            BotMovementService.Enable(bot);
            BotMovementService.Move(bot, rightX, GroundY(rightX));
            bots.Add(bot);
        }

        var samples = new List<double>(Frames);
        long now = 1000;
        bool toRight = true;        for (int frame = 0; frame < 120; frame++) { now += 17; BotScheduler.Clock = now; BotScheduler.Drive(); }
        for (int frame = 0; frame < Frames; frame++)
        {
            now += 17; BotScheduler.Clock = now;
            // 来回走：到站就掉头，保证整段压测都在移动（站着不动只会命中快照去重）
            if (frame % 60 == 0)
            {
                toRight = !toRight;
                int target = toRight ? rightX : leftX;
                foreach (var bot in bots) BotMovementService.Move(bot, target, GroundY(target));
            }
            var watch = Stopwatch.StartNew();
            BotScheduler.Drive();
            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        Report("steady bots=" + count, samples);

        // 单次移动 tick 的耗时与分配：位置真的在变，不会命中快照去重
        var probe = bots[0];
        var motion = BotMovementService.Get(probe);
        int probeTarget = Math.Abs(probe.x - rightX) > 100 ? rightX : leftX;
        const int probeTicks = 2000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var tickWatch = Stopwatch.StartNew();
        for (int i = 0; i < probeTicks; i++)
        {
            BotMovementService.Move(probe, probeTarget, GroundY(probeTarget));
            motion.Tick(new BotNavigationGraph.Point(probeTarget, GroundY(probeTarget)), 900000L + i * 50);
        }
        tickWatch.Stop();
        long after = GC.GetAllocatedBytesForCurrentThread();
        Console.WriteLine("[PERF] move_tick bots=" + count +
            " us_per_tick=" + (tickWatch.Elapsed.TotalMilliseconds * 1000 / probeTicks).ToString("F2") +
            " alloc_bytes_per_tick=" + ((after - before) / (double)probeTicks).ToString("F2"));

        BotMovementService.Shutdown();
        foreach (var bot in bots) BotRegistry.Remove(bot);
        BotMovementService.IsObserved = null; BotMovementService.ResolveProfile = null;
        MapService.Unregister(map.mapId);
        BotTasks.Stop();
    }

    // —— C. 定时器成本：1000 个周期任务，跑 300 帧 ——
    static void TimerCost()
    {
        BotTasks.Stop();
        BotScheduler.Clock = 0;
        int fired = 0;
        for (int i = 0; i < OnlineBots; i++) BotTasks.ScheduleFixedDelay(() => fired++, 0, 2000);
        var samples = new List<double>(Frames);
        long now = 0;
        for (int i = 0; i < 30; i++) { now += 17; BotScheduler.Clock = now; BotTasks.Drive(now); }
        for (int frame = 0; frame < Frames; frame++)
        {
            now += 17; BotScheduler.Clock = now;
            var watch = Stopwatch.StartNew();
            BotTasks.Drive(now);
            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        Report("timers tasks=" + OnlineBots, samples);
        Console.WriteLine("[PERF] timers_fired=" + fired + " frames=" + (Frames + 30));
        BotTasks.Stop();
    }

    // —— D. 每帧遍历全部在册假人（BotWander.AdvanceAll）——
    static void RegistryScanCost(MapRuntime map)
    {
        BotScheduler.Clock = 1000;
        var bots = new List<BotCharacter>(OnlineBots);
        for (int i = 0; i < OnlineBots; i++)
        {
            var bot = new BotCharacter { id = 60000 + i, mapId = map.mapId, x = 0, y = 0 };
            BotRegistry.Add(bot);
            bots.Add(bot);
        }
        var samples = new List<double>(Frames);
        long now = 1000;
        for (int frame = 0; frame < Frames; frame++)
        {
            now += 17; BotScheduler.Clock = now;
            var watch = Stopwatch.StartNew();
            BotScheduler.Drive();
            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        Report("registry_scan online=" + OnlineBots, samples);
        foreach (var bot in bots) BotRegistry.Remove(bot);
    }

    static void Report(string key, List<double> samples)
    {
        if (samples.Count == 0) return;
        samples.Sort();
        double sum = 0; foreach (double s in samples) sum += s;
        Console.WriteLine("[PERF] " + key +
            " avg_ms=" + (sum / samples.Count).ToString("F3") +
            " p50_ms=" + samples[samples.Count / 2].ToString("F3") +
            " p95_ms=" + samples[(int)(samples.Count * 0.95)].ToString("F3") +
            " p99_ms=" + samples[Math.Min(samples.Count - 1, (int)(samples.Count * 0.99))].ToString("F3") +
            " max_ms=" + samples[samples.Count - 1].ToString("F3"));
    }

    static void Cleanup()
    {
        try
        {
            foreach (var bot in new List<BotCharacter>(BotRegistry.All().Values)) BotRegistry.Remove(bot);
            BotMovementService.Shutdown();
            BotMovementService.IsObserved = null;
            BotMovementService.ResolveProfile = null;
            BotTasks.Stop();
            BotScheduler.Stop();
            BotScheduler.Clock = 0;
            if (heavyMap != null) MapService.Unregister(heavyMap.mapId);
            if (walkMap != null) MapService.Unregister(walkMap.mapId);
        }
        catch (Exception) { }
    }
}
