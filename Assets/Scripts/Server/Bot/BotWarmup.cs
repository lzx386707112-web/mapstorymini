using System;
using System.Collections.Generic;
using System.Threading;

namespace Server.Bot
{
    // SoloMapling 的大 JSON（Items 8.6MB / SoloMapling 3.3MB）解析放到后台线程：
    // Unity 资源只能在主线程取字符串，但 JSON 解析是纯 C#，没理由占着渲染帧。
    // 之前这些解析发生在进图后首次访问 GameServer.Bots 的那一帧，表现就是「刚进游戏卡很久」。
    public static class BotWarmup
    {
        sealed class Job
        {
            public Thread thread;
            public Dictionary<string, object> root;
            public Exception error;
            public volatile bool done;
        }

        static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        static readonly object gate = new object();

        // 主线程预读文本 + 后台解析；重复调用无副作用。
        public static void Start(string path)
        {
            string text;
            lock (gate) { if (jobs.ContainsKey(path)) return; }
            text = PlatformRes.LoadText(path);   // Unity 资源只能在主线程读
            if (text == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 不支持线程：保持原有的同步解析。
            lock (gate)
            {
                if (jobs.ContainsKey(path)) return;
                var sync = new Job { root = MiniJsonDb.Parse(text) as Dictionary<string, object> };
                sync.done = true;
                jobs[path] = sync;
            }
#else
            lock (gate)
            {
                if (jobs.ContainsKey(path)) return;
                var job = new Job();
                jobs[path] = job;
                job.thread = new Thread(() =>
                {
                    try { job.root = MiniJsonDb.Parse(text) as Dictionary<string, object>; }
                    catch (Exception e) { job.error = e; }
                    job.done = true;
                }) { IsBackground = true };
                job.thread.Start();
            }
#endif
        }

        // 取已解析的根节点；没预热过就现场解析（同步，行为与旧实现一致）。
        public static Dictionary<string, object> Root(string path)
        {
            Job job;
            lock (gate) { jobs.TryGetValue(path, out job); }
            if (job == null)
            {
                Start(path);
                lock (gate) { jobs.TryGetValue(path, out job); }
            }
            if (job == null)
            {
                var fallback = MiniJsonDb.Parse(PlatformRes.LoadText(path)) as Dictionary<string, object>;
                if (fallback == null) throw new InvalidOperationException("Bot resource missing: " + path);
                return fallback;
            }
            job.thread?.Join();
            if (job.root == null) throw new InvalidOperationException("Bot resource invalid: " + path, job.error);
            return job.root;
        }
    }
}
