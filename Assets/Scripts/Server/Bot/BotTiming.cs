using System;
using System.Collections.Generic;

namespace Server.Bot
{
    // server/BotTiming.java 的非阻塞时序翻译。随机延时在构建链时确定，闭区间。
    public static class BotTiming
    {
        static readonly Random random = new Random();
        static long RandomDelay(long min, long max)
        {
            long span = Math.Max(0, max - min);
            return min + (span == 0 ? 0 : (long)(random.NextDouble() * (span + 1)));
        }
        public static BotTasks.Handle AfterRandom(long minMs, long maxMs, Action task)
            => BotTasks.Schedule(task, RandomDelay(minMs, maxMs));
        public static Chain CreateChain() => new Chain();
        public sealed class Chain
        {
            readonly List<(Action task, long pause)> steps = new List<(Action, long)>();
            Func<bool> keepGoing = () => true;
            public Chain StopUnless(Func<bool> condition) { if (condition != null) keepGoing = condition; return this; }
            public Chain Pause(long ms) { steps.Add((null, Math.Max(0, ms))); return this; }
            public Chain PauseRandom(long min, long max) => Pause(RandomDelay(min, max));
            public Chain Run(Action action) { if (action != null) steps.Add((action, 0)); return this; }
            public void Start() { Advance(0); }
            void Advance(int index)
            {
                while (index < steps.Count)
                {
                    if (!keepGoing()) return;
                    var step = steps[index++];
                    if (step.task == null)
                    {
                        int next = index;
                        BotTasks.Schedule(() => Advance(next), step.pause);
                        return;
                    }
                    step.task();
                }
            }
        }
    }
}
