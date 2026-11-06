using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 日志导出与分享。
//
// 目的：真机出问题时，让用户自己点一下就能把日志发出来，不用连电脑 adb。
//
// 做法：
//   1. 列出 logs/ 下的日志，由用户选择后完整导出该文件；
//   2. Android 上把 txt 发布到系统「下载」目录（MediaStore），拿到 content:// URI，
//      再用系统分享面板发出去（微信/QQ/邮件都能收）；
//   3. 任何一步失败都退化成「文件已保存到下载目录」的提示，绝不让用户一无所获。
//
// 为什么不用 FileProvider：它依赖 androidx.core，这个工程刻意不引 androidx
// （AdminWebView 都是直接用系统 API 而不是 androidx）。MediaStore 是系统自带的，
// 零依赖，而且发出去的 URI 还能带上读权限，别人能直接打开。
// ————————————————————————————————————————————————————————————
public static class LogShare
{
    static string selectedFile;
    public static void Tick()
    {
        string selected = Interlocked.Exchange(ref selectedFile, null);
        if (selected != null) ShareFile(selected);
    }
    const string Mime = "text/plain";

    public static void Share()
    {
        FileLog.FlushNow();
        var files = FileLog.HistoryFiles();
        files.Reverse();
        if (files.Count == 0) { BottomBar.Toast("还没有日志文件，先玩一会儿再分享"); return; }
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidShare.Choose(files);
#elif UNITY_EDITOR
        var menu = new UnityEditor.GenericMenu();
        foreach (string file in files)
        {
            string selected = file;
            menu.AddItem(new GUIContent(Path.GetFileName(file)), false, () => ShareFile(selected));
        }
        menu.ShowAsContext();
#else
        BottomBar.Toast("当前平台暂不支持日志选择");
#endif
    }

    public static void ShareFile(string selected)
    {
        string file;
        try { FileLog.FlushNow(); file = Export(selected); }
        catch (Exception e) { BottomBar.Toast("日志导出失败：" + e.Message); return; }
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!AndroidShare.Offer(file)) BottomBar.Toast("日志已导出：" + file);
        else BottomBar.Toast("正在打开分享…");
#elif UNITY_EDITOR
        UnityEditor.EditorUtility.RevealInFinder(file);
        BottomBar.Toast("日志已导出：" + Path.GetFileName(file));
#else
        BottomBar.Toast("日志已导出：" + file);
#endif
    }

    /// <summary>完整导出选中的单份日志，不合并、不截断；固定长度快照避免持续追加。</summary>
    public static string Export(string selected)
    {
        if (!FileLog.HistoryFiles().Contains(selected)) throw new IOException("日志文件已不存在");
        string dir = Path.Combine(FileLog.Dir, "share");
        Directory.CreateDirectory(dir);
        string outPath = Path.Combine(dir, "mapstory-log-" + Path.GetFileNameWithoutExtension(selected) +
            "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
        using (var source = new FileStream(selected, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var target = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        {
            long remaining = source.Length;
            var buffer = new byte[64 * 1024];
            while (remaining > 0)
            {
                int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) break;
                target.Write(buffer, 0, read);
                remaining -= read;
            }
        }
        Cleanup(dir);
        return outPath;
    }

    // 导出的副本会一直堆在 share/ 里，只留最近 5 份。
    static void Cleanup(string dir)
    {
        try
        {
            var found = new List<string>(Directory.GetFiles(dir, "mapstory-log-*.txt"));
            if (found.Count <= 5) return;
            found.Sort(StringComparer.Ordinal);
            for (int i = 0; i < found.Count - 5; i++) File.Delete(found[i]);
        }
        catch (Exception) { }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static class AndroidShare
    {
        public static void Choose(List<string> files)
        {
            try
            {
                var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                var names = files.ConvertAll(Path.GetFileName).ToArray();
                var listener = new Selection(files);
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        var builder = new AndroidJavaObject("android.app.AlertDialog$Builder", activity);
                        builder.Call<AndroidJavaObject>("setTitle", "选择要分享的日志");
                        builder.Call<AndroidJavaObject>("setItems", names, listener);
                        builder.Call<AndroidJavaObject>("show");
                    }
                    catch (Exception e) { Debug.LogError("[Log] 日志列表打开失败：" + e); }
                }));
            }
            catch (Exception e) { BottomBar.Toast("日志列表打开失败：" + e.Message); }
        }

        sealed class Selection : AndroidJavaProxy
        {
            readonly List<string> files;
            public Selection(List<string> files) : base("android.content.DialogInterface$OnClickListener")
            { this.files = files; }
            public void onClick(AndroidJavaObject dialog, int which)
            {
                // Android UI thread must not call Unity APIs. Dispatch to the game thread.
                selectedFile = which >= 0 && which < files.Count ? files[which] : null;
            }
        }

        // 返回 false 表示连提交都没成功（调用方退化成提示路径）
        public static bool Offer(string file)
        {
            AndroidJavaObject activity;
            try
            {
                activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
            }
            catch (Exception) { return false; }
            if (activity == null) return false;

            string name = Path.GetFileName(file);
            byte[] data;
            try { data = File.ReadAllBytes(file); }
            catch (Exception) { return false; }

            // 写 MediaStore 是 IO，别放主线程；写完整理好的 URI 再回主线程起 Intent
            var thread = new Thread(() => Publish(activity, name, data)) { IsBackground = true, Name = "LogShare" };
            thread.Start();
            return true;
        }

        static void Publish(AndroidJavaObject activity, string name, byte[] data)
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
                AndroidJavaObject uri = null;
                if (sdk >= 29) uri = PublishMediaStore(activity, name, data);
                if (uri == null) { SaveToDownloads(name, data); return; }
                Send(activity, uri);
            }
            catch (Exception e)
            {
                Debug.LogError("[Log] 日志分享失败：" + e.Message);
                try { SaveToDownloads(name, data); } catch (Exception) { }
            }
            finally { AndroidJNI.DetachCurrentThread(); }
        }

        // Android 10+：MediaStore.Downloads 是官方给「导出文件」用的入口，
        // 不需要任何存储权限，也不用 FileProvider。
        static AndroidJavaObject PublishMediaStore(AndroidJavaObject activity, string name, byte[] data)
        {
            var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
            var values = new AndroidJavaObject("android.content.ContentValues");
            values.Call("put", "display_name", name);
            values.Call("put", "mime_type", Mime);
            values.Call("put", "relative_path", "Download");

            var collection = new AndroidJavaClass("android.provider.MediaStore$Downloads")
                .GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
            var uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
            if (uri == null) return null;

            using (var os = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
            {
                if (os == null) return null;
                os.Call("write", data, 0, data.Length);
                os.Call("flush");
            }
            return uri;
        }

        // Android 9 及以下：直接写公共下载目录（工程已申请管理外部存储权限）。
        // 老系统没有 MediaStore.Downloads，也就没有免 FileProvider 的分享通道，
        // 文件落到下载目录后提示用户自己从文件管理器发。
        static void SaveToDownloads(string name, byte[] data)
        {
            var env = new AndroidJavaClass("android.os.Environment");
            string dir = env.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", "Download")
                            .Call<string>("getAbsolutePath");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), data);
            MainThread.Toast("日志已存到「下载」目录：" + name);
        }

        static void Send(AndroidJavaObject activity, AndroidJavaObject uri)
        {
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    var intent = new AndroidJavaObject("android.content.Intent");
                    intent.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND");
                    intent.Call<AndroidJavaObject>("setType", Mime);
                    intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.STREAM", uri);
                    intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.SUBJECT", "MapleDash 客户端日志");
                    intent.Call<AndroidJavaObject>("addFlags", 1);   // FLAG_GRANT_READ_URI_PERMISSION
                    var chooser = new AndroidJavaClass("android.content.Intent")
                        .CallStatic<AndroidJavaObject>("createChooser", intent, "分享日志");
                    activity.Call("startActivity", chooser);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Log] 系统分享面板打不开：" + e.Message);
                    BottomBar.Toast("分享面板打不开，日志已存到手机的「下载」目录");
                }
            }));
        }
    }

    // 后台线程不能碰 BottomBar（内部用 Time.unscaledTime），要回到主线程再提示
    static class MainThread
    {
        public static void Toast(string text)
        {
            try
            {
                var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() => BottomBar.Toast(text)));
            }
            catch (Exception) { }
        }
    }
#endif
}
