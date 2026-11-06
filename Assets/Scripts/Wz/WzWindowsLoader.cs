#if UNITY_STANDALONE && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

// Windows（桌面独立版）资源加载页。
//
// 故意不复用 WzAndroidLoader：桌面版和手机端是两条流程。
//   1) 没有联网自更新：不查 resUpdate.json，不下载 APK，也不做资源补丁比对；
//   2) 没有共享存储授权：资源固定放在 D:\mapstory（见 GamePlatform.ResourceRoot），不需要申请权限；
//   3) 不需要每台机器做全量 SHA-256 校验：Data 在，就直接进游戏。
// 剩下只做一件事：确认 Data 就绪（缺了就把 D:\mapstory 里的资源包解开），然后回调 CreateGame。
public sealed class WzWindowsLoader : MonoBehaviour
{
    Action complete;
    string phase = "正在检查资源";
    string message;
    Task loading;
    volatile int loaded, total;
    bool failed, starting;
    float nextProgress, visibleProgress;
    string progressText = "";
    Camera bootCam;
    GUIStyle titleStyle, detailStyle, buttonStyle;

    // ——————————————————— 生命周期 ———————————————————

    void Awake()
    {
        Application.targetFrameRate = 60;
        EnsureBootCamera();
    }

    // 加载页没有相机时，OnGUI 画在未定义的缓冲上会整屏黑，这里常驻一台只清屏的相机。
    void EnsureBootCamera()
    {
        if (bootCam != null) return;
        var go = new GameObject("[BootCamera]");
        DontDestroyOnLoad(go);
        bootCam = go.AddComponent<Camera>();
        bootCam.clearFlags = CameraClearFlags.SolidColor;
        bootCam.backgroundColor = new Color(.055f, .07f, .09f, 1f);
        bootCam.cullingMask = 0;
        bootCam.orthographic = true;
        bootCam.orthographicSize = 5f;
        bootCam.depth = -1000;
    }

    public static void Begin(Action complete)
    {
        var go = new GameObject("[ResourceLoader]");
        DontDestroyOnLoad(go);
        var loader = go.AddComponent<WzWindowsLoader>();
        loader.complete = complete;
    }

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        failed = false; message = null;
        loaded = total = 0; visibleProgress = 0f; progressText = "";
        phase = "正在检查资源";
        string root = GamePlatform.ResourceRoot;

        // 解压几个 GiB 不能占着主线程；进度由工作线程单向写入，主线程只读。
        loading = Task.Run(() => Prepare(root));
        while (!loading.IsCompleted) yield return null;

        if (loading.IsFaulted)
        {
            failed = true;
            phase = "资源加载失败";
            message = loading.Exception.GetBaseException().Message;
            Debug.LogException(loading.Exception.GetBaseException());
            loading = null;
            yield break;
        }
        loading = null;

        starting = true;
        phase = "资源就绪，正在启动游戏";
        progressText = "正在启动游戏";
        yield return null;

        try { complete?.Invoke(); }
        catch (Exception e)
        {
            starting = false; failed = true; phase = "启动失败"; message = e.Message;
            Debug.LogException(e);
            yield break;
        }

        // 等主相机就绪再撤掉清屏相机，避免中间有一帧黑屏。
        float wait = 0f;
        while (Camera.main == null && wait < 3f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }
        if (bootCam != null) { Destroy(bootCam.gameObject); bootCam = null; }
        Destroy(gameObject);
    }

    // ——————————————————— 资源准备（工作线程） ———————————————————

    void Prepare(string root)
    {
        Directory.CreateDirectory(root);
        if (Ready(root)) return;

        // Data 不在时，接受整包和分包两种放法（与 Android 同名同结构，资源包可以直接复用）。
        string zip = Path.Combine(root, "MapleDashResources.zip");
        if (!WzResourceParts.Exists(zip))
            throw new FileNotFoundException("未找到资源。请把 MapleDashResources.zip（或全部分包 part01、part02…）" +
                                            "或已展开的 Data 目录放到 " + root + " 后点「重试」。");
        phase = "正在解压资源";
        WzResourceInstaller.ExtractFull(zip, root, (n, all) => { total = all; loaded = n; });
        if (!Ready(root))
            throw new InvalidDataException("资源解压后仍然缺少 Data 目录：" + Path.Combine(root, "Data"));
    }

    static bool Ready(string root) => Directory.Exists(Path.Combine(root, "Data"));

    // ——————————————————— 主循环 ———————————————————

    void Update()
    {
        if (starting) return;
        if (loading == null) return;
        if (Time.unscaledTime < nextProgress) return;
        nextProgress = Time.unscaledTime + .1f;
        int count = loaded, countTotal = total;
        if (countTotal <= 0) return;
        float target = Mathf.Clamp01((float)count / countTotal);
        visibleProgress = Mathf.Max(visibleProgress, target);      // 只进不退
        progressText = Mathf.FloorToInt(visibleProgress * 100f) + "%   " + count + "/" + countTotal;
        phase = "正在解压资源";
    }

    void Retry()
    {
        if (loading != null) return;
        StopAllCoroutines();
        StartCoroutine(Run());
    }

    void OpenResourceFolder()
    {
        try { System.Diagnostics.Process.Start("explorer.exe", "\"" + GamePlatform.ResourceRoot + "\""); }
        catch (Exception e) { message = "打开资源目录失败：" + e.Message; }
    }

    // ——————————————————— 绘制 ———————————————————

    void OnGUI()
    {
        if (titleStyle == null)
        {
            var font = Resources.Load<Font>("Fonts/ui");
            titleStyle = new GUIStyle(GUI.skin.label) { font = font, alignment = TextAnchor.MiddleCenter, fontSize = 19, wordWrap = true };
            titleStyle.normal.textColor = Color.white;
            detailStyle = new GUIStyle(titleStyle) { fontSize = 14 };
            detailStyle.normal.textColor = new Color(.75f, .79f, .84f);
            buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16 };
        }
        var matrix = GUI.matrix; var color = GUI.color; int depth = GUI.depth;
        try
        {
            GUI.depth = -1000;
            GUI.color = Color.white;
            GUI.matrix = Matrix4x4.identity;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, new Color(.055f, .07f, .09f, 1), 0, 0);

            float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 960 * scale) / 2, (Screen.height - 540 * scale) / 2, 0),
                Quaternion.identity, Vector3.one * scale);

            GUI.Label(new Rect(120, 200, 720, 30), phase, titleStyle);

            GUI.DrawTexture(new Rect(160, 250, 640, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(.18f, .2f, .23f), 0, 0);
            if (visibleProgress > 0f)
                GUI.DrawTexture(new Rect(160, 250, 640 * visibleProgress, 10), Texture2D.whiteTexture,
                    ScaleMode.StretchToFill, true, 0, new Color(.25f, .7f, .4f), 0, 0);
            if (string.IsNullOrEmpty(progressText) == false)
                GUI.Label(new Rect(160, 264, 640, 22), progressText, detailStyle);

            // 资源目录一直显示出来：这台机器上到底读的是哪个路径，一眼能看见。
            GUI.Label(new Rect(80, 300, 800, 40), "资源目录：" + GamePlatform.ResourceRoot, detailStyle);
            if (!string.IsNullOrEmpty(message)) GUI.Label(new Rect(80, 330, 800, 120), message, detailStyle);

            if (failed)
            {
                if (GUI.Button(new Rect(300, 470, 170, 30), "重试", buttonStyle)) Retry();
                if (GUI.Button(new Rect(490, 470, 170, 30), "打开资源目录", buttonStyle)) OpenResourceFolder();
            }
        }
        finally { GUI.matrix = matrix; GUI.color = color; GUI.depth = depth; }
    }
}
#endif
