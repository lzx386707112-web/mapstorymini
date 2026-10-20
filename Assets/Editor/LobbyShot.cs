using System.Globalization;
using UnityEditor;
using UnityEngine;

// 大厅截图：进 Play 后停在大厅，用 ScreenCapture 抓三张标签图（OnGUI 只有这条能拍到）。
public class LobbyShot
{
    const string K_RUN = "LobbyShot.run";
    const string K_T0 = "LobbyShot.t0";
    const string K_DIR = "LobbyShot.dir";
    static double nextShot;
    static int shotIndex;

    public static void Run()
    {
        string dir = "/tmp/lobby";
        System.IO.Directory.CreateDirectory(dir);

        PlayerPrefs.SetInt("autostart", 0);   // 停在大厅
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.Save();

        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetString(K_T0, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
        EditorPrefs.SetString(K_DIR, dir);
        nextShot = 1.5;
        shotIndex = 0;

        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        if (!EditorApplication.isPlaying)
        {
            if (EditorApplication.timeSinceStartup - Parse(EditorPrefs.GetString(K_T0, "0")) > 40)
                Finish(1);
            return;
        }

        double t0 = Parse(EditorPrefs.GetString(K_T0, "0"));
        double t = EditorApplication.timeSinceStartup;

        var c = Camera.main;
        if (c != null && !Mathf.Approximately(c.aspect, 16f / 9f)) c.aspect = 16f / 9f;

        // 依次切标签抓图
        if (t - t0 >= 1.5 && shotIndex == 0) { Lobby.JumpTo(Lobby.Tab.Dress); Cap(); }
        if (t - t0 >= 3.5 && shotIndex == 1) { Lobby.JumpTo(Lobby.Tab.Class); Cap(); }
        if (t - t0 >= 5.5 && shotIndex == 2) { Lobby.JumpTo(Lobby.Tab.Shop); Cap(); }

        if (t - t0 >= 7.5) Finish(0);
    }

    static void Cap()
    {
        string dir = EditorPrefs.GetString(K_DIR, "/tmp/lobby");
        int idx = shotIndex++;
        try
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "tab" + idx.ToString("0") + ".png"), 1);
            Debug.Log("[LobbyShot] 截图 tab" + idx);
        }
        catch (System.Exception e) { Debug.LogWarning("[LobbyShot] 截图失败：" + e.Message); }
    }

    static void Finish(int code)
    {
        EditorPrefs.SetBool(K_RUN, false);
        EditorApplication.update -= Tick;
        Debug.Log("[LobbyShot] 结束");
        EditorApplication.Exit(code);
    }

    static double Parse(string s)
    {
        double v;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0.0;
    }
}
