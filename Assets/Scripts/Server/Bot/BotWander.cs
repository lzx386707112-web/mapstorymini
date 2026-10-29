// BotWanderSystem：400ms决策轮、初始0..1499ms停留、可达ledge选点。
// 坐标统一由BotMovementService推进，不在游荡决策里插值或吸附地面。
using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot
{
    public sealed class BotWander
    {
        const float ArriveX = 25f;
        const float ArriveY = 60f;
        const float ProgressEps = 12f;
        const long StuckMs = 6000;
        const long DwellMinMs = 2500;
        const long DwellMaxMs = 6000;
        const float MinStroll = 60f;

        public enum State { Dwell, Walk }
        public State state = State.Dwell;
        public float targetX, targetY;
        public long dwellUntilMs;
        public float bestDist = float.MaxValue;
        public long progressAtMs;
        public int mapId;
        public bool banded;
        long nextPollMs;
        public float bandLo, bandHi;

        static readonly Random rng = new Random();

        public static void Start(BotCharacter bot, bool banded, float lo, float hi)
        {
            if (bot == null || MapService.Get(bot.mapId) == null) return;
            Environment.BotMovementService.Enable(bot);
            if (!banded)
            {
                var map = MapService.Get(bot.mapId); var graph = Environment.BotMovementService.Graphs.Get(map);
                var reachable = Environment.BotMovementService.Reachable(graph, new Environment.BotGroundIndex(map),
                    new Environment.BotNavigationGraph.Point((int)bot.x, (int)bot.y));
                int min = int.MaxValue, max = int.MinValue;
                foreach (var region in graph.regions)
                    if (!region.isRopeRegion && (reachable.Count == 0 || reachable.Contains(region.id)))
                    { min = Math.Min(min, region.minX); max = Math.Max(max, region.maxX); }
                if (max > min)
                {
                    int inset = Math.Max(30, (int)(.075 * (max - min)));
                    if (max - inset > min + inset) { banded = true; lo = min + inset; hi = max - inset; }
                }
            }
            bot.wander = new BotWander { mapId = bot.mapId, banded = banded, bandLo = lo, bandHi = hi, state = State.Dwell, dwellUntilMs = 0, progressAtMs = BotScheduler.Now(), nextPollMs = BotScheduler.Now() + rng.Next(1500) };
            // 只把真正在游荡的假人放进推进表：以前每帧遍历全服在册假人（市场商人、组队伴侣等
            // 没有 wander 的也一起扫），在线人数越多这笔固定开销越大。
            if (activeIds.Add(bot.id)) active.Add(bot);
        }

        public static void Stop(BotCharacter bot) { bot.wander = null; bot.moving = false; }

        public static bool IsWandering(BotCharacter bot) => bot.wander != null;

        // 每帧（或每调度 tick）推进：移动 + 到站切换 + 卡住检测。
        public static void Tick(BotCharacter bot, long nowMs)
        {
            var w = bot.wander;
            if (w == null) return;
            if (bot.mapId != w.mapId) { Stop(bot); return; }
            if (nowMs < w.nextPollMs) return;
            w.nextPollMs += 400;
            if (!Environment.BotMovementService.Observed(bot.mapId)) return;
            var rt = MapService.Get(bot.mapId);
            if (rt == null || rt.footholds.Count == 0) { bot.moving = false; return; }

            if (w.state == State.Dwell)
            {
                if (nowMs < w.dwellUntilMs) { bot.moving = false; return; }
                var spot = PickStroll(bot, rt, w);
                if (spot == null) { w.dwellUntilMs = nowMs + DwellMinMs; bot.moving = false; return; }
                w.targetX = spot.Value.x; w.targetY = spot.Value.y;
                w.state = State.Walk; w.bestDist = float.MaxValue; w.progressAtMs = nowMs;
                Environment.BotMovementService.Move(bot, (int)w.targetX, (int)w.targetY);
                return;
            }

            // Walk
            float dx = w.targetX - bot.x;
            float dy = w.targetY - bot.y;
            if (Math.Abs(dx) <= ArriveX && Math.Abs(dy) <= ArriveY && !Environment.BotMovementService.IsMoving(bot))
            {
                w.state = State.Dwell;
                w.dwellUntilMs = nowMs + DwellMinMs + (long)(rng.NextDouble() * (DwellMaxMs - DwellMinMs));
                bot.moving = false;
                return;
            }
            float dist = Math.Abs(dx) + Math.Abs(dy);
            if (dist < w.bestDist - ProgressEps) { w.bestDist = dist; w.progressAtMs = nowMs; }
            else if (nowMs - w.progressAtMs > StuckMs)
            {
                var spot2 = PickStroll(bot, rt, w);
                if (spot2 != null) { w.targetX = spot2.Value.x; w.targetY = spot2.Value.y; w.bestDist = float.MaxValue; w.progressAtMs = nowMs; Environment.BotMovementService.Move(bot, (int)w.targetX, (int)w.targetY); }
                else { w.state = State.Dwell; w.dwellUntilMs = nowMs + DwellMinMs; bot.moving = false; }
                return;
            }
            if (!Environment.BotMovementService.IsMoving(bot))
                Environment.BotMovementService.Move(bot, (int)w.targetX, (int)w.targetY);
        }

        // 每帧推进所有带 wander 的 bot（后端驱动、前端渲染）。
        // 只遍历游荡中的假人，并且顺手清理已下线/已停止的条目（swap-remove，O(1)）。
        public static void AdvanceAll(long nowMs)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var bot = active[i];
                // 换图由 Tick 内部 Stop（与原来一致）；这里只清理已停止/已下线的条目。
                if (bot == null || bot.wander == null || !BotRegistry.Contains(bot.id))
                {
                    if (bot != null) activeIds.Remove(bot.id);
                    active[i] = active[active.Count - 1];
                    active.RemoveAt(active.Count - 1);
                    continue;
                }
                Tick(bot, nowMs);
            }
        }

        static readonly List<BotCharacter> active = new List<BotCharacter>();
        static readonly HashSet<int> activeIds = new HashSet<int>();

        static readonly Environment.BotGroundSpotPicker picker = new Environment.BotGroundSpotPicker(rng);
        static (float x, float y)? PickStroll(BotCharacter bot, MapRuntime rt, BotWander w)
        {
            Environment.BotNavigationGraph.Point? last = null;
            var from = new Environment.BotNavigationGraph.Point((int)bot.x, (int)bot.y);
            for (int i = 0; i < 6; i++)
            {
                var point = w.banded ? picker.Pick(rt, from, (int)w.bandLo, (int)w.bandHi) : picker.Pick(rt, from);
                if (!point.HasValue) break;
                last = point;
                if (Math.Abs(point.Value.x - from.x) >= MinStroll) break;
            }
            return last.HasValue ? ((float)last.Value.x, (float)last.Value.y) : ((float, float)?)null;
        }
    }
}
