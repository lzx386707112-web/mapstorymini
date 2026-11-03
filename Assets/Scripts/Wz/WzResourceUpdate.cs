#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public static class WzResourceUpdate
{
    public static bool InstalledThisLaunch { get; private set; }

    sealed class InstallProgress
    {
        public volatile int completed, total;
        public volatile string phase = "正在读取资源清单…";
    }

    public static IEnumerator Run(string baseManifest)
    {
        InstalledThisLaunch = false;
        using (var updater = new AndroidJavaClass("com.mapledash.game.ResourceUpdater"))
        {
            bool began = false;
            try
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                    updater.CallStatic("begin", activity);
                began = true;
            }
            catch (Exception e) { Debug.LogWarning("[Resources] 更新检查初始化失败：" + e.Message); }
            if (!began) yield break;
            while (updater.GetStatic<int>("state") != 2)
            {
                if (updater.GetStatic<int>("state") == 1)
                {
                    string version = updater.CallStatic<string>("installing");
                    var progress = new InstallProgress();
                    var install = Task.Run(() =>
                    {
                        const string root = "/sdcard/mapstory";
                        string patch = Path.Combine(root, "MapleDashResources.patch.zip");
                        string manifest = WzResourceInstaller.ReadArchiveManifest(patch);
                        WzResourceInstaller.Install(patch, root, manifest,
                            (done, total) => { progress.total = total; progress.completed = done; },
                            phase => progress.phase = phase, version, baseManifest);
                    });
                    while (!install.IsCompleted)
                    {
                        updater.CallStatic("installProgress", progress.completed, progress.total, progress.phase);
                        yield return new WaitForSecondsRealtime(.1f);
                    }
                    if (!install.IsFaulted)
                    {
                        InstalledThisLaunch = true;
                        updater.CallStatic("installProgress", 10000, 10000, "资源安装完成");
                    }
                    updater.CallStatic("installed", install.IsFaulted
                        ? install.Exception.GetBaseException().Message : "");
                }
                yield return new WaitForSecondsRealtime(.25f);
            }
        }
    }
}
#endif
