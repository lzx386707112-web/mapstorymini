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
            if (text == null)
            {
                // 资源缺失：登记一个已完成但无内容的 job，别让 Ready() 永远 false 把假人系统卡住；
                // 真有调用方取 Root 时仍按旧行为抛错。
                lock (gate) { jobs[path] = new Job { done = true }; }
                return;
            }
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

        // 后台解析是否已经完成。调用方（进图后的假人系统）必须先问这个，不能直接 Root——
        // Root 会 Join 后台线程，等于把整段解析时间搬到主线程，表现为「进游戏后卡住不能动」。
        public static bool Ready(string path)
        {
            Job job;
            lock (gate) { if (!jobs.TryGetValue(path, out job)) return false; }
            return job.done;
        }

        // 非阻塞取用：后台解析完就拿到根节点返回 true；没解析完返回 false，
        // 顺手把解析排进后台线程（重复调用无副作用），调用方下一帧再问一次。
        // 地图分片（0.3~1.3MB）走这条路：主线程只负责把 TextAsset 读成字符串。
        public static bool TryRoot(string path, out Dictionary<string, object> root)
        {
            Job job;
            root = null;
            lock (gate) { jobs.TryGetValue(path, out job); }
            if (job == null)
            {
                Start(path);
                lock (gate) { jobs.TryGetValue(path, out job); }
            }
            if (job == null || !job.done) return false;
            root = job.root;
            return root != null;
        }

        // 释放已经解析好的根节点。地图几何取走之后就没必要再驻留一份引用，
        // 否则七个城镇分片会一直挂在 jobs 里，白白占掉几十 MB。
        public static void Release(string path)
        {
            lock (gate)
            {
                if (jobs.TryGetValue(path, out var job) && job.done && job.root != null) jobs.Remove(path);
            }
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
