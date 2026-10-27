// 假人调度核心（时间轮 + 自适应 throttle）。逐条对齐源端：
//   BotTickService.java（DRIVER_PERIOD_MS=250 驱动 + governor 自适应节流）
//   ExecutorServiceManager.java（线程池；安卓单机改为主线程 tick，去掉多线程池）
// 外部（GameServer/FieldDirector）每帧用当前毫秒时钟调用 Drive(nowMs)，
// 复用 MapSpawner.Clock 的 Time.realtimeSinceStartup 注入范式。
using System;
using System.Collections.Generic;

namespace Server.Bot
{
    public static class BotScheduler
    {
        const long DriverPeriodMs = 250;
        const long GovernorWindowMs = 5000;
        const long LagHiMs = 1000;
        const long LagLoMs = 300;
        const double FactorMax = 4.0;
        const double FactorStep = 1.5;
        // 一轮至少放行几个 tick：太少会让假人动作整体变慢；剩下的按帧预算顺延到下一帧。
        const int MinTicksPerRound = 4;

        sealed class Entry
        {
            public Action tick;
            public long periodMs;
            public long nextDueMs;
            public long pendingDueMs;
            public bool noThrottle;
            public bool ticking;
        }

        static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        static double throttleFactor = 1.0;
        static long governorWindowStart;
        static long windowLagSum;
        static long windowDispatches;
        static bool started;
        static long nextDriverMs;
        static long previousFrameMs;
        static bool hasFrame;
        static float deltaSeconds;

        public static double ThrottleFactor() => throttleFactor;

        public static void Register(int botId, Action tick, long initialDelayMs, long periodMs)
        {
            EnsureDriver();
            if (entries.ContainsKey(botId)) return;
            entries[botId] = new Entry
            {
                tick = tick,
                periodMs = periodMs,
                nextDueMs = Now() + initialDelayMs,
                pendingDueMs = 0,
                noThrottle = false,
                ticking = false,
            };
        }

        public static void Unregister(int botId) => entries.Remove(botId);

        public static bool IsRegistered(int botId) => entries.ContainsKey(botId);

        public static void SetNoThrottle(int botId, bool on)
        {
            if (entries.TryGetValue(botId, out var e)) e.noThrottle = on;
        }

        public static int Size() => entries.Count;

        public static void Reschedule(int botId, long periodMs)
        {
            if (!entries.TryGetValue(botId, out var e)) return;
            e.periodMs = periodMs;
            RequestFireAt(e, Now() + periodMs);
        }

        public static void Nudge(int botId, long initialDelayMs, long periodMs)
        {
            if (!entries.TryGetValue(botId, out var e)) return;
            e.periodMs = periodMs;
            RequestFireAt(e, Now() + initialDelayMs);
        }

        static void RequestFireAt(Entry e, long at)
        {
            e.pendingDueMs = at;
            if (e.nextDueMs != long.MaxValue) e.nextDueMs = at;
        }

        public static void Stop()
        {
            entries.Clear();
            started = false;
            throttleFactor = 1.0;
            governorWindowStart = windowLagSum = windowDispatches = 0;
            hasFrame = false;
            deltaSeconds = 0;
        }

        // —— 时钟注入点（前端每帧注入，对齐 MapSpawner.Clock 范式；后端保持纯 C# 不引用 UnityEngine）——
        public static long Clock;
        public static long Now() => Clock;
        public static float Dt() => deltaSeconds;

        static void EnsureDriver()
        {
            // 驱动由外部时钟调用 Drive()，此处仅标记已启动（无独立线程）
            if (started) return;
            started = true;
            nextDriverMs = Now() + DriverPeriodMs;
        }

        public static void Drive()
        {
            using var perf = BotPerf.Timer("BotScheduler.Drive");
            long now = Now();
            deltaSeconds = hasFrame ? Math.Max(0, now - previousFrameMs) / 1000f : 0;
            previousFrameMs = now;
            hasFrame = true;
            // 帧推进与源端 250ms 决策轮分离；未接前端时不会自行启动。
            BotTasks.Drive(now);
            BotWander.AdvanceAll(now);
            if (!IsOperating() || now < nextDriverMs) return;
            nextDriverMs = now + DriverPeriodMs;
            long lagSum = 0;
            int dispatched = 0;
            bool backlog = false;
            // 主线程回调可注册、停用或转换类型；不遍历可变字典。
            var snapshot = new List<KeyValuePair<int, Entry>>(entries);
            foreach (var pair in snapshot)
            {
                if (!entries.TryGetValue(pair.Key, out var e) || !ReferenceEquals(e, pair.Value)) continue;
                if (now >= e.nextDueMs && !e.ticking)
                {
                    // 进游戏那几帧会有几十个假人同时到点，一次全跑就把帧撑爆了：
                    // 放行够 MinTicksPerRound 个之后看本帧预算，超了就顺延（nextDueMs 不动，下一帧还due）。
                    if (dispatched >= MinTicksPerRound && !BotWorkPump.Has(0.5f)) { backlog = true; break; }
                    e.ticking = true;
                    long lag = now - e.nextDueMs;
                    e.nextDueMs = long.MaxValue;
                    if (e.pendingDueMs > 0 && now >= e.pendingDueMs) e.pendingDueMs = 0;
                    lagSum += Math.Max(0, lag);
                    dispatched++;
                    RunTick(e, now);
                }
            }
            // 还有到点没跑完的：下一帧接着发，别等下一个 250ms 轮（否则动作会明显发迟）。
            if (backlog) nextDriverMs = now;
            GovernorTick(now, lagSum, dispatched);
        }

        static void RunTick(Entry e, long now)
        {
            try
            {
                e.tick();
            }
            catch
            {
                // 单次 tick 异常不中断调度（对齐源端 catch(Throwable)）
            }
            finally
            {
                e.ticking = false;
            }
            if (!IsOperating()) return;
            long due = now + (e.noThrottle ? e.periodMs : (long)(e.periodMs * throttleFactor));
            if (e.pendingDueMs > 0)
            {
                long pending = e.pendingDueMs;
                e.pendingDueMs = 0;
                due = Math.Min(due, Math.Max(pending, now));
            }
            e.nextDueMs = due;
        }

        static void GovernorTick(long now, long lagSumMs, int dispatches)
        {
            windowLagSum += lagSumMs;
            windowDispatches += dispatches;
            if (governorWindowStart == 0) { governorWindowStart = now; return; }
            if (now - governorWindowStart < GovernorWindowMs) return;

            long avg = windowDispatches > 0 ? windowLagSum / windowDispatches : 0;
            windowLagSum = 0;
            windowDispatches = 0;
            governorWindowStart = now;

            if (avg > LagHiMs && throttleFactor < FactorMax)
                throttleFactor = Math.Min(FactorMax, throttleFactor * FactorStep);
            else if (avg < LagLoMs && throttleFactor > 1.0)
                throttleFactor = Math.Max(1.0, throttleFactor / FactorStep);
        }

        // 源端 runAsync：阶段A 主线程同步执行（后续如需异步再改 Task；Unity 渲染需主线程）。
        public static void RunAsync(Action task) => task();

        static bool IsOperating() => started;
    }
}
