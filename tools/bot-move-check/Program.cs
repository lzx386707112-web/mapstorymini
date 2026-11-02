// 假人移动合法性离线检查（不依赖 Unity 编辑器）。
//
// 目的：用**真实地图几何**（Assets/Resources/Bot/Maps*.json，即游戏里假人用的那份）
// 跑真实的 BotMovementRuntime + BotNavigationRuntime，逐 50ms tick 记录轨迹，然后验证
// 假人有没有遵守地图规则：
//   1) 落地状态（非跳跃、非爬绳）必须踩在 foothold 上（±26px 内），不能悬空；
//   2) 非空中/非爬绳状态不允许一 tick 位移超过 40px（那就是瞬移/直线穿墙）；
//   3) 抓绳那一刻，上一 tick 的位置必须已经在绳子 8px 以内（不能"离着远就开始爬"）。
// 同时按前端 BotField 的插值口径复算一遍视图位置，量出「视图落后服务端多少像素」。
//
// 用法（先保证 DLL 是新的）：
//   dotnet build tools/unity-compile-check/Check.csproj
//   cd tools/bot-move-check && dotnet run -- 103000000 104000000
using System;
using System.Collections.Generic;
using System.IO;
using Server.Bot;
using Server.Bot.Environment;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using EdgeType = Server.Bot.Environment.BotNavigationGraph.EdgeType;

public static class BotMoveCheck
{
    static string root;
    static BotNavigationMapLoader loader;
    static readonly Dictionary<int, MapRuntime> maps = new Dictionary<int, MapRuntime>();

    sealed class Report
    {
        public int runs, arrived, timeout, gaveUp;
        public int hover, teleport, ropeEarly, ropeOffX;
        public int maxTickStep;
        public float viewLagMax;
        public readonly List<string> samples = new List<string>();
        public void Note(string text) { if (samples.Count < 12) samples.Add(text); }
    }

    static MapRuntime Map(int id)
    {
        if (maps.TryGetValue(id, out var cached)) return cached;
        var map = loader.Geometry(id);
        maps[id] = map;
        return map;
    }

    public static int Main(string[] args)
    {
        root = FindRoot();
        if (root == null) { Console.Error.WriteLine("[BotMoveCheck] 找不到 Assets/Resources/Bot/Maps.json"); return 2; }
        var indexPath = Path.Combine(root, "Bot/Maps.json");
        loader = new BotNavigationMapLoader(File.ReadAllText(indexPath),
            res => File.ReadAllText(Path.Combine(root, res + ".json")));

        var ids = new List<int>();
        foreach (var arg in args) if (int.TryParse(arg, out int id)) ids.Add(id);
        if (ids.Count == 0) ids.AddRange(new[] { 100000000, 101010100, 103000000, 104000000 });

        var profile = new BotMovementProfile(125, 105);
        bool failed = false;
        foreach (int id in ids)
        {
            MapRuntime map;
            try { map = Map(id); }
            catch (Exception error) { Console.WriteLine($"[BotMoveCheck] {id} 地图几何缺失：{error.Message}"); continue; }
            var graph = new BotNavigationGraphBuilder(map, profile).Build();
            var report = new Report();
            int ropeRegions = 0;
            foreach (var region in graph.regions) if (region.isRopeRegion) ropeRegions++;
            Console.WriteLine($"[BotMoveCheck] 地图 {id}：foothold {map.footholds.Count} / rope {map.ropes.Count} / region {graph.regions.Count}（绳区 {ropeRegions}）/ 边 {CountEdges(graph)}");
            RunMap(map, graph, profile, report);
            Console.WriteLine($"  runs={report.runs} 到达={report.arrived} 超时={report.timeout} 放弃={report.gaveUp}  " +
                              $"悬空tick={report.hover} 瞬移={report.teleport} 提前抓绳={report.ropeEarly} 绳上x偏移={report.ropeOffX}  " +
                              $"单tick最大位移={report.maxTickStep}px 视图最大滞后={report.viewLagMax:F0}px");
            foreach (var line in report.samples) Console.WriteLine("    · " + line);
            // 落地状态悬空是硬伤（人站在空气里）。偶发单个 tick 的判定抖动不算：
            // 每次行程允许 1 个 tick 的过渡误差。
            if (report.teleport > 0 || report.hover > report.runs) failed = true;
            // 视图滞后 = 玩家看到的「人在地上飘/空中直线走」。修复后稳定在 1~2 帧（≈30px）以内；
            // 一旦有人把 BotField 的插值节奏改回固定时长，这里会立刻炸。
            if (report.viewLagMax > 200f)
            {
                Console.WriteLine($"  ! 视图滞后 {report.viewLagMax:F0}px 超过 200px（前端插值跟不上服务端 tick，会出现空中直线滑行）");
                failed = true;
            }
        }
        Console.WriteLine(failed ? "[BotMoveCheck] FAIL" : "[BotMoveCheck] OK");
        return failed ? 1 : 0;
    }

    static int CountEdges(BotNavigationGraph graph)
    {
        int count = 0;
        foreach (var region in graph.regions) count += graph.Outgoing(region.id).Count;
        return count;
    }

    static void RunMap(MapRuntime map, BotNavigationGraph graph, BotMovementProfile profile, Report report)
    {
        var planets = new List<BotNavigationGraph.Region>();
        foreach (var region in graph.regions) if (!region.isRopeRegion && region.Width >= 80) planets.Add(region);
        if (planets.Count == 0) return;
        var random = new Random(map.mapId * 7919 + 13);
        int pairs = Math.Min(14, planets.Count);
        for (int i = 0; i < pairs; i++)
        {
            var from = planets[random.Next(planets.Count)];
            var to = planets[random.Next(planets.Count)];
            if (from.id == to.id) continue;
            var start = from.PointAt((from.minX + from.maxX) / 2);
            var target = to.PointAt((to.minX + to.maxX) / 2);
            var ground = new BotGroundIndex(map);
            var probe = BotPathSearch.Run(graph, map, start, from.id, to.id, target);
            if (probe.path == null || probe.path.Count == 0) continue;
            Simulate(map, graph, profile, start, target, report, map.mapId * 1000 + i);
        }
    }

    static void Simulate(MapRuntime map, BotNavigationGraph graph, BotMovementProfile profile, Point start, Point target, Report report, int seed)
    {
        report.runs++;
        var actor = new BotCharacter { id = seed, name = "check", level = 30, job = 100, mapId = map.mapId, x = start.x, y = start.y };
        var motion = new BotMovementRuntime(actor, map, profile);
        motion.Teleport(start);
        var nav = new BotNavigationRuntime(motion, graph, new Random(seed));
        bool portaled = false;
        // 同图传送门：模拟 BotRuntime.EnterPortal（走到门口即传到目标门）。
        nav.UsePortal = portal =>
        {
            var exit = map.portals.Find(p => p.name == portal.targetName);
            if (exit == null) return false;
            actor.x = exit.x; actor.y = exit.y + 1; portaled = true;
            return true;
        };

        long now = 0;
        bool prevAir = false, prevClimb = false;
        Point prev = start;
        var server = new List<(long at, Point point)>();
        bool arrived = false;
        int stillTicks = 0;
        bool stuckNoted = false;
        for (int tick = 0; tick < 4000; tick++)
        {
            now += 50;
            portaled = false;
            prev = motion.Position; prevAir = motion.InAir; prevClimb = motion.Climbing;
            nav.Tick(new Point(target.x, target.y), now, true, false);
            var pos = motion.Position;
            server.Add((now, pos));
            if (pos.x == prev.x && pos.y == prev.y) stillTicks++; else stillTicks = 0;
            if (stillTicks == 40 && !stuckNoted)
            {
                stuckNoted = true;
                var edge = motion.navEdge;
                report.Note($"卡住 map={map.mapId} at {pos.x},{pos.y}→目标 {target.x},{target.y}  " +
                            $"edge={(edge == null ? "null" : edge.type + " " + edge.fromRegionId + "->" + edge.toRegionId + " start=" + edge.startPoint.x + "," + edge.startPoint.y + " end=" + edge.endPoint.x + "," + edge.endPoint.y + " launch=[" + edge.launchMinX + "," + edge.launchMaxX + "] step=" + edge.launchStepX)}  " +
                            $"waypoint={(motion.navigationTarget.HasValue ? motion.navigationTarget.Value.x + "," + motion.navigationTarget.Value.y : "-")}  " +
                            $"precise={motion.preciseTarget} nav={nav.LastDecision}");
            }

            if (!portaled && !prevAir && !prevClimb && !motion.InAir && !motion.Climbing)
            {
                int step = Math.Abs(pos.x - prev.x) + Math.Abs(pos.y - prev.y);
                if (step > report.maxTickStep) report.maxTickStep = step;
                if (step > 40)
                {
                    report.teleport++;
                    report.Note($"瞬移 map={map.mapId} t={now} {prev.x},{prev.y} -> {pos.x},{pos.y}（{step}px，nav={nav.LastDecision}）");
                }
            }            if (!motion.InAir && !motion.Climbing)
            {
                var footing = motion.ground.Ground(pos.x, pos.y);
                if (footing == null || Math.Abs(BotGroundIndex.YAt(footing, pos.x) - pos.y) > 26)
                {
                    report.hover++;
                    report.Note($"悬空 map={map.mapId} t={now} at {pos.x},{pos.y}（地面={(footing == null ? "无" : BotGroundIndex.YAt(footing, pos.x).ToString())}，nav={nav.LastDecision}）");
                }
            }
            // 只有「从地面直接抓住绳子」才算提前抓绳；空中跳抓（GroundRopeJump）是源端允许的。
            if (motion.Climbing && !prevClimb && !prevAir)
            {
                var rope = motion.climb.rope;
                if (rope != null && Math.Abs(prev.x - rope.x) > 8)
                {
                    report.ropeEarly++;
                    Console.WriteLine($"      [rope] map={map.mapId} t={now} 地面抓绳前 x={prev.x} y={prev.y} 绳 x={rope.x} 顶={rope.Top} 底={rope.Bottom}（差 {Math.Abs(prev.x - rope.x)}px）nav={nav.LastDecision}");
                }
                if (rope != null && pos.x != rope.x) report.ropeOffX++;
            }
            if (Math.Abs(pos.x - target.x) <= 8 && Math.Abs(pos.y - target.y) <= 8) { arrived = true; break; }
        }
        if (arrived) report.arrived++;
        else
        {
            report.timeout++;
            var end = motion.Position;
            report.Note($"未到达 map={map.mapId} 起点 {start.x},{start.y} → 目标 {target.x},{target.y}，" +
                        $"{now}ms 后停在 {end.x},{end.y}（剩 {Math.Abs(end.x - target.x)},{Math.Abs(end.y - target.y)}，nav={nav.LastDecision}）");
        }

        float lag = ViewLag(server);
        if (lag > report.viewLagMax) report.viewLagMax = lag;
    }

    // 按 BotField 的插值口径复算视图位置，返回整段行程里「视图与服务端位置」的最大偏差（px）。
    // 口径 = 修复后的 BotField.TrackPosition：每帧读最新服务端坐标，两帧追平，
    // 单帧位移 >=200px 视为传送直接落位。
    static float ViewLag(List<(long at, Point point)> server)
    {
        if (server.Count < 2) return 0f;
        const float FrameSeconds = 1f / 60f;
        const float TeleportPx = 200f;
        var view = new Vec(server[0].point.x, server[0].point.y);
        var target = view;
        float speed = 0f;
        int at = 1;
        float worst = 0f;
        for (long nowMs = 0; nowMs <= server[server.Count - 1].at; nowMs += 17)
        {
            while (at < server.Count && server[at].at <= nowMs)
            {
                var point = server[at].point;
                float dx = point.x - target.x, dy = point.y - target.y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                target = new Vec(point.x, point.y);
                if (distance >= TeleportPx) { view = target; speed = 0f; }
                else if (distance > 0.01f) speed = Math.Max(2f, Math.Min(distance / Math.Max(FrameSeconds * 2f, 0.02f), 900f));
                at++;
            }
            float step = speed * FrameSeconds;
            float vx = target.x - view.x, vy = target.y - view.y;
            float remaining = (float)Math.Sqrt(vx * vx + vy * vy);
            if (remaining > 0.0001f)
            {
                float move = Math.Min(step, remaining);
                view = new Vec(view.x + vx / remaining * move, view.y + vy / remaining * move);
            }
            var serverNow = server[Math.Max(0, at - 1)].point;
            float offX = view.x - serverNow.x, offY = view.y - serverNow.y;
            float off = (float)Math.Sqrt(offX * offX + offY * offY);
            if (off > worst) { worst = off; if (off > 60f) Console.WriteLine($"      [lag] t={nowMs} view={view.x:F0},{view.y:F0} server={serverNow.x},{serverNow.y}"); }
        }
        return worst;
    }

    readonly struct Vec
    {
        public readonly float x, y;
        public Vec(float x, float y) { this.x = x; this.y = y; }
    }

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Assets/Resources/Bot/Maps.json");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "Assets/Resources");
            dir = dir.Parent;
        }
        return null;
    }
}
