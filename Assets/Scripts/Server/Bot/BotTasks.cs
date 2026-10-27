using System;
using System.Collections.Generic;

namespace Server.Bot
{
    // ExecutorServiceManager / MethodScheduler 的主线程适配；不阻塞Unity、不使用线程sleep。
    // FSM轮由BotScheduler管理，本队列专门处理独立延时、出生编排和固定频率任务。
    public static class BotTasks
    {
        public sealed class Handle
        {
            internal Action callback;
            internal long due, period, sequence;
            internal bool fixedRate;
            public bool Cancelled { get; private set; }
            public bool Completed { get; internal set; }
            public void Cancel() { Cancelled = true; }
        }
        static readonly List<Handle> tasks = new List<Handle>();
        static long sequence;
        public static Action<Exception> OnError;
        public static Handle Schedule(Action task, long delayMs)
            => Add(task, delayMs, 0, false);
        public static Handle ScheduleFixedRate(Action task, long initialDelayMs, long periodMs)
            => Add(task, initialDelayMs, periodMs > 0 ? periodMs : throw new ArgumentOutOfRangeException(nameof(periodMs)), true);
        public static Handle ScheduleFixedDelay(Action task, long initialDelayMs, long periodMs)
            => Add(task, initialDelayMs, periodMs > 0 ? periodMs : throw new ArgumentOutOfRangeException(nameof(periodMs)), false);
        static Handle Add(Action task, long delay, long period, bool fixedRate)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            var h = new Handle { callback = task, due = BotScheduler.Now() + Math.Max(0, delay), period = period, fixedRate = fixedRate, sequence = sequence++ };
            tasks.Add(h);
            return h;
        }
        public static int PendingCount => tasks.FindAll(t => !t.Cancelled && !t.Completed).Count;
        public static void Drive(long now)
        {
            using var perf = BotPerf.Timer("BotTasks.Drive");
            var ready = tasks.FindAll(t => !t.Cancelled && !t.Completed && now >= t.due);
            ready.Sort((a, b) => { int c = a.due.CompareTo(b.due); return c != 0 ? c : a.sequence.CompareTo(b.sequence); });
            foreach (var h in ready)
            {
                if (h.Cancelled || h.Completed) continue;
                long previousDue = h.due;
                try { h.callback(); }
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
        public static void Stop()
        {
            foreach (var h in tasks) h.Cancel();
            tasks.Clear();
        }
    }
}
