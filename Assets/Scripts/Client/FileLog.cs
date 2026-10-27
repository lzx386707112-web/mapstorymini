using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 日志落盘。
//
// 为什么要有这个：真机出问题时只能靠 adb logcat，用户自己拿不到；
// 一旦切后台/退进程，日志就没了，也没法转发给别人分析。
//
// 约束（都很重要）：
//   · 主线程只做「入队」，写盘全在后台线程。日志回调可能来自任意线程
//     （BotWarmup 等后台解析就是这么打日志的），所以必须用并发队列。
//   · 写盘线程自己绝不调用 Debug.Log —— 会再次进入回调，形成递归。
//   · 单文件 3MB 轮转，最多留 3 份历史，长期挂机不会把存储写满。
//   · 开关关闭时一行都不写（连会话头也不写），开关状态存在 PlayerPrefs。
// ————————————————————————————————————————————————————————————
public static class FileLog
{
    public const string PrefKey = "log_to_file";
    const string Folder = "logs";
    const string MainFile = "client.txt";
    const long MaxBytes = 3L * 1024 * 1024;
    const int KeepOld = 3;                 // client.1.txt ~ client.3.txt，1 最新
    const int MaxPending = 20000;          // 积压上限，超了丢弃（宁可丢日志，不能吃内存）
    const int FlushMs = 300;
    const int BatchLines = 400;

    public static bool Enabled { get; private set; }

    static readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
    static readonly object gate = new object();
    static readonly object writeGate = new object();
    static readonly StringBuilder scratch = new StringBuilder(8192);

    static Thread worker;
    static volatile bool running;
    static string dir;
    static string path;
    static long bytes;
    static int lost;
    static bool booted;
    static string device;

    /// <summary>日志目录（分享/排查时展示给用户）。</summary>
    public static string Dir => dir ?? (dir = Path.Combine(Application.persistentDataPath, Folder));

    // —————————— 启动 ——————————
    // BeforeSplashScreen：比场景加载更早，能抓到 WZ 引导/资源就位那段的日志。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void AutoBoot() => Boot();

    public static void Boot()
    {
        if (booted) return;
        booted = true;

        // 启动得比场景加载还早（Splash 之前），这时 PlayerPrefs/持久化目录虽然通常已就绪，
        // 但真出异常也不能让游戏起不来——日志是辅助功能，挂了就静默关掉。
        try
        {
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting += FlushNow;
            Enabled = PlayerPrefs.GetInt(PrefKey, 1) != 0;   // 默认打开
            if (Enabled) Open();
        }
        catch (Exception)
        {
            Enabled = false;
        }
    }

    public static void SetEnabled(bool on)
    {
        PlayerPrefs.SetInt(PrefKey, on ? 1 : 0);
        PlayerPrefs.Save();
        if (on == Enabled) return;

        if (on)
        {
            Enabled = true;
            Open();                       // Open 里会补一条会话头
            Note("日志记录已开启");
        }
        else
        {
            Note("日志记录已关闭");        // 先让这条进队，再把闸门关上
            FlushNow();
            Enabled = false;
        }
    }

    /// <summary>手动切一下（侧边栏用）。</summary>
    public static bool Toggle()
    {
        SetEnabled(!Enabled);
        return Enabled;
    }

    static void Open()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            lock (gate)
            {
                path = Path.Combine(Dir, MainFile);
                var info = new FileInfo(path);
                bytes = info.Exists ? info.Length : 0;   // 续写：先按已有长度算，否则永远触发不了轮转
            }
            if (worker == null || !running)
            {
                running = true;
                worker = new Thread(Loop) { IsBackground = true, Name = "FileLog" };
                worker.Start();
            }
            // 会话头同步写一次：文件立刻存在（刚进游戏就能导出/分享），
            // 也不怕进程被系统杀掉时连会话头都没落盘。几百字节，主线程代价可忽略。
            Append(SessionHeader() + "\n");
        }
        catch (Exception)
        {
            // 目录建不出来（存储被拔/存储只读）先记着，下次有日志进来再试。
            // 这里不把 Enabled 置 false：开关是「用户想记」，写不了是环境问题，
            // 一旦环境恢复（比如用户清理完存储）还能自己接上。
            path = null;
        }
    }

    // 启动早期目录可能还没就绪：日志回调里补一次，5 秒最多试一次，
    // 免得磁盘真不可写时每条日志都去建一次目录。
    static int nextOpenTry;

    static void EnsureOpen()
    {
        if (path != null) return;
        int now = Environment.TickCount;
        if (now < nextOpenTry) return;
        nextOpenTry = now + 5000;
        Open();
    }

    // —————————— 采集 ——————————
    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (!Enabled) return;
        if (path == null) EnsureOpen();
        if (pending.Count >= MaxPending) { Interlocked.Increment(ref lost); return; }

        var sb = new StringBuilder(condition?.Length ?? 0 + 96);
        sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ');
        sb.Append(Level(type)).Append(' ');
        sb.Append(condition);
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            if (!string.IsNullOrEmpty(stackTrace))
            {
                sb.Append('\n');
                // 栈太长的按 2000 字截断，日志是给人看的，不是给调试器看的
                sb.Append(stackTrace.Length > 2000 ? stackTrace.Substring(0, 2000) + "…" : stackTrace);
            }
        }
        pending.Enqueue(sb.ToString());
    }

    static char Level(LogType t)
    {
        switch (t)
        {
            case LogType.Warning: return 'W';
            case LogType.Error:
            case LogType.Exception:
            case LogType.Assert: return 'E';
            default: return 'I';
        }
    }

    /// <summary>插一条带分隔线的备注（会话头、开关变化）。</summary>
    public static void Note(string text)
    {
        if (!Enabled || string.IsNullOrEmpty(text)) return;
        pending.Enqueue("\n===== " + text + " =====\n");
    }

    // 设备/CPU/GPU 调一次要过 native，缓存起来；屏幕尺寸不能缓存
    // （启动早期 UiKit 还没按真实分辨率算过，缓存下来会一直是 1280×720）。
    static string DeviceStatic()
    {
        if (device != null) return device;
        var sb = new StringBuilder(320);
        sb.Append("MapleDash ").Append(Application.version)
          .Append(" · ").Append(Application.platform)
          .Append(" · ").Append(Application.identifier).Append('\n');
        try
        {
            sb.Append("设备 ").Append(SystemInfo.deviceModel)
              .Append(" / ").Append(SystemInfo.operatingSystem).Append('\n');
            sb.Append("CPU ").Append(SystemInfo.processorType).Append(" ×").Append(SystemInfo.processorCount)
              .Append(" / 内存 ").Append(SystemInfo.systemMemorySize).Append("MB\n");
            sb.Append("GPU ").Append(SystemInfo.graphicsDeviceName)
              .Append(" / 显存 ").Append(SystemInfo.graphicsMemorySize).Append("MB\n");
        }
        catch (Exception) { }
        return device = sb.ToString();
    }

    public static string DeviceSummary()
    {
        var sb = new StringBuilder(DeviceStatic(), 460);
        try
        {
            UiKit.RefreshMetrics();
            sb.Append("屏幕 ").Append(Screen.width).Append('×').Append(Screen.height)
              .Append(" dpi=").Append(Mathf.RoundToInt(Screen.dpi))
              .Append(" 虚拟 ").Append(Mathf.RoundToInt(UiKit.W)).Append('×').Append(Mathf.RoundToInt(UiKit.H))
              .Append('\n');
        }
        catch (Exception) { }
        return sb.ToString();
    }

    static string SessionHeader()
        => "会话开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + DeviceSummary();

    // —————————— 写入 ——————————
    static void Loop()
    {
        while (running)
        {
            Thread.Sleep(FlushMs);
            Drain();
        }
    }

    static void Drain()
    {
        if (!Enabled && pending.IsEmpty) return;
        string text;
        lock (writeGate)
        {
            scratch.Length = 0;
            int n = 0;
            while (n < BatchLines && pending.TryDequeue(out string line))
            {
                scratch.Append(line).Append('\n');
                n++;
            }
            int dropped = Interlocked.Exchange(ref lost, 0);
            if (dropped > 0) scratch.Append("（日志积压，丢弃 ").Append(dropped).Append(" 行）\n");
            if (scratch.Length == 0) return;
            text = scratch.ToString();
        }
        Append(text);
    }

    static void Append(string text)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            lock (gate)
            {
                if (path == null) return;
                // 按剩余空间切块写：一轮 Drain 可能攒出几 MB 文本（几百行大日志），
                // 整段写进去会让单文件直接冲破上限，轮转就形同虚设。
                int offset = 0;
                while (offset < data.Length)
                {
                    if (bytes >= MaxBytes) Rotate();
                    long room = MaxBytes - bytes;
                    if (room <= 0) { Rotate(); continue; }
                    int take = (int)Math.Min(data.Length - offset, room);
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        fs.Write(data, offset, take);
                    bytes += take;
                    offset += take;
                }
            }
        }
        catch (Exception)
        {
            // 磁盘满 / 目录被删 / 权限变更：静默丢弃。写盘失败绝不能反过来影响游戏。
        }
    }

    static void Rotate()
    {
        try
        {
            string oldest = OldPath(KeepOld);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = KeepOld - 1; i >= 1; i--)
            {
                string from = OldPath(i), to = OldPath(i + 1);
                if (File.Exists(from)) { if (File.Exists(to)) File.Delete(to); File.Move(from, to); }
            }
            string main = Path.Combine(Dir, MainFile);
            if (File.Exists(main)) { if (File.Exists(OldPath(1))) File.Delete(OldPath(1)); File.Move(main, OldPath(1)); }
        }
        catch (Exception) { }
        bytes = 0;
    }

    static string OldPath(int index) => Path.Combine(Dir, "client." + index + ".txt");

    /// <summary>把队列里剩下的立刻写盘（分享前 / 退进程前）。</summary>
    public static void FlushNow()
    {
        try
        {
            while (!pending.IsEmpty) Drain();
        }
        catch (Exception) { }
    }

    // —————————— 给分享模块用 ——————————
    /// <summary>按「旧 → 新」返回现存日志文件。</summary>
    public static List<string> HistoryFiles()
    {
        var list = new List<string>();
        try
        {
            for (int i = KeepOld; i >= 1; i--)
            {
                string p = OldPath(i);
                if (File.Exists(p)) list.Add(p);
            }
            string main = Path.Combine(Dir, MainFile);
            if (File.Exists(main)) list.Add(main);
        }
        catch (Exception) { }
        return list;
    }

    /// <summary>读一个正在被写的日志文件：必须共享读，否则会被写线程的排他句柄挡掉。</summary>
    public static string ReadShared(string file, int maxChars)
    {
        try
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string all = reader.ReadToEnd();
                if (maxChars > 0 && all.Length > maxChars) return all.Substring(all.Length - maxChars);
                return all;
            }
        }
        catch (Exception) { return ""; }
    }
}
