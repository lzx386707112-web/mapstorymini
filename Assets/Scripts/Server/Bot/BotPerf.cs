using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Server.Bot
{
    // 假人系统的耗时统计。Server/ 是纯 C#（不引用 UnityEngine），所以这里只累计，
    // 输出交给前端注入（Sink），由 FieldDirector 每 2 秒 Flush 一次到 logcat。
    //
    // 用途：先量再改。之前的问题就是凭感觉判断「加载卡」，实际大头在别处。
    // 打开方式：PlayerPrefs debug_bot_perf=1（一次有效），然后看
    //   adb logcat -s Unity | grep Bot/Perf
    public static class BotPerf
    {
        public static bool Enabled;
        public static Action<string> Sink;

        sealed class Stat
        {
            public long count;
            public double sum, max;
        }

        static readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        static readonly List<string> order = new List<string>();

        // 单次采样（毫秒）。
        public static void Record(string name, double ms)
        {
            if (!Enabled) return;
            if (!stats.TryGetValue(name, out var stat))
            {
                stat = new Stat();
                stats[name] = stat;
                order.Add(name);
            }
            stat.count++;
            stat.sum += ms;
            if (ms > stat.max) stat.max = ms;
        }

        public static Scope Timer(string name) => new Scope(name);

        // 把一段代码包起来的计时器：using (BotPerf.Timer("x")) { ... }
        public struct Scope : IDisposable
        {
            readonly string name;
            readonly long start;
            public Scope(string name)
            {
                this.name = name;
                start = Enabled ? Stopwatch.GetTimestamp() : 0;
            }
            public void Dispose()
            {
                if (!Enabled) return;
                Record(name, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }
        }

        // 输出并清零；调用方控制节奏（前端每 2 秒一次）。
        public static void Flush()
        {
            if (!Enabled || order.Count == 0) return;
            foreach (string key in order)
            {
                var stat = stats[key];
                if (stat.count == 0) continue;
                Sink?.Invoke("[Bot/Perf] " + key + " n=" + stat.count +
                             " avg=" + (stat.sum / stat.count).ToString("F2") +
                             " max=" + stat.max.ToString("F2") +
                             " total=" + stat.sum.ToString("F1") + "ms");
            }
            stats.Clear(); order.Clear();
        }

        public static void Clear() { stats.Clear(); order.Clear(); }
    }
}
