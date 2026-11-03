#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

// 更新是启动门禁；Java 在沙盒 files 中保存已确认安装的 JSON 版本。
public sealed class AppUpdate : MonoBehaviour
{
    public static void Begin(Action complete)
    {
        var go = new GameObject("[AppUpdate]");
        DontDestroyOnLoad(go);
        go.AddComponent<AppUpdate>().StartCoroutine(Run(complete, go));
    }

    static IEnumerator Run(Action complete, GameObject owner)
    {
        AndroidJavaClass updater = null;
        try
        {
            updater = new AndroidJavaClass("com.mapledash.game.AppUpdater");
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                updater.CallStatic("begin", activity);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Update] 初始化失败，继续启动：" + e.Message);
            updater?.Dispose(); updater = null;
        }
        if (updater != null)
        {
            while (!updater.GetStatic<bool>("finished"))
                yield return new WaitForSecondsRealtime(.25f);
            updater.Dispose();
        }
        complete();
        Destroy(owner);
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) return;
        using (var updater = new AndroidJavaClass("com.mapledash.game.AppUpdater"))
            updater.CallStatic("resume");
    }
}
#endif
