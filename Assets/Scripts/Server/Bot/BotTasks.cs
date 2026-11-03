using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Server.Bot
{
    // ExecutorServiceManager / MethodScheduler 的主线程适配；不阻塞Unity、不使用线程sleep。
    // FSM轮由BotScheduler管理，本队列专门处理独立延时、出生编排和固定频率任务。
    public static class BotTasks
    {
        public sealed class Handle
        {
            internal Action callback;
            internal string perfName;
            internal long due, period, sequence;
            internal bool fixedRate;
            public bool Cancelled { get; private set; }
            public bool Completed { get; internal set; }
            public void Cancel() { Cancelled = true; }
        }
        static readonly List<Handle> tasks = new List<Handle>();
        static readonly Stack<List<Handle>> readyPool = new Stack<List<Handle>>();
        static readonly Comparison<Handle> DueOrder = (a, b) =>
        { int c = a.due.CompareTo(b.due); return c != 0 ? c : a.sequence.CompareTo(b.sequence); };
        static long sequence;
        public static Action<Exception> OnError;
        public static Handle Schedule(Action task, long delayMs,
            [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
            => Add(task, delayMs, 0, false, caller, file);
        public static Handle ScheduleFixedRate(Action task, long initialDelayMs, long periodMs,
            [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
            => Add(task, initialDelayMs, periodMs > 0 ? periodMs : throw new ArgumentOutOfRangeException(nameof(periodMs)), true, caller, file);
        public static Handle ScheduleFixedDelay(Action task, long initialDelayMs, long periodMs,
            [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
            => Add(task, initialDelayMs, periodMs > 0 ? periodMs : throw new ArgumentOutOfRangeException(nameof(periodMs)), false, caller, file);
        static Handle Add(Action task, long delay, long period, bool fixedRate, string caller, string file)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            var h = new Handle { callback = task, due = BotScheduler.Now() + Math.Max(0, delay), period = period, fixedRate = fixedRate, sequence = sequence++,
                perfName = "BotTask." + System.IO.Path.GetFileNameWithoutExtension(file) + "." + caller };
            tasks.Add(h);
            return h;
        }
        public static int PendingCount
        {
            get
            {
                int count = 0;
                foreach (var task in tasks) if (!task.Cancelled && !task.Completed) count++;
                return count;
            }
        }
        public static void Drive(long now)
        {
            using var perf = BotPerf.Timer("BotTasks.Drive");
            // Snapshot first: callbacks may register/cancel tasks. Reuse storage, including nested drives.
            var ready = readyPool.Count > 0 ? readyPool.Pop() : new List<Handle>();
            try
            {
                foreach (var task in tasks)
                    if (!task.Cancelled && !task.Completed && now >= task.due) ready.Add(task);
                ready.Sort(DueOrder);
                foreach (var h in ready)
                {
                    if (h.Cancelled || h.Completed) continue;
                    long previousDue = h.due;
                    try { using var callbackPerf = BotPerf.Timer(h.perfName); h.callback(); }
                    catch (Exception e)
                    {
                        // ScheduledExecutorService的重复任务发生异常后不再重复。
                        h.Cancel();
                        OnError?.Invoke(e);
                    }
                    if (h.period == 0) h.Completed = true;
                    else if (!h.Cancelled) h.due = (h.fixedRate ? previousDue : BotScheduler.Now()) + h.period;
                }
                tasks.RemoveAll(t => t.Cancelled || t.Completed);
            }
            finally { ready.Clear(); readyPool.Push(ready); }
        }
        public static void Stop()
        {
            foreach (var h in tasks) h.Cancel();
            tasks.Clear();
        }
    }
}
