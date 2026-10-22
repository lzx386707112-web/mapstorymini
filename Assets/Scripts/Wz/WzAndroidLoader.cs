#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

// Android 资源加载页。
//
// 设计要点（对应三个线上问题）：
//   1. 加载页没有相机时，最小化再回来会整屏黑 —— 这里常驻一台 [BootCamera]
//      只做纯色清屏；进入游戏、主相机就绪后才销毁。
//   2. 已安装却每次都要几分钟 —— 只有回执不匹配时才全量 SHA-256；回执匹配时
//      只抽样校验少量文件，且回执同时写到 /sdcard 和应用私有目录，避免
//      外置存储写入失败导致「已解压还反复解压」。
//   3. 到 100% 后卡住、文案来回跳 —— 阶段文案只由后台线程单向设置一次，
//      权限每 0.5s 才查一次（不再逐帧覆盖文案）；收尾阶段显示「正在启动游戏」
//      和一条来回扫动的进度条，明确告诉用户还在做事。
public sealed class WzAndroidLoader : MonoBehaviour
{
    const string Root = "/sdcard/mapstory";

    Texture2D background;
    Action complete;
    string manifest;
    string privateReceipt;                 // Application.persistentDataPath/...（主线程取好，后台线程只读）

    string phase = "准备加载资源";
    string message;
    Task loading;
    volatile int loaded, total;
    volatile string workerPhase;

    bool permissionRequested, failed, starting, startInvoked;
    int sdk;
    GUIStyle titleStyle, detailStyle, buttonStyle;
    float nextProgressUpdate, visibleProgress, pulse, nextPermissionCheck;
    string progressText = "0%";
    bool hasPermission;
    Camera bootCam;

    // ——————————————————— 生命周期 ———————————————————

    void Awake()
    {
        Application.targetFrameRate = 60;
        EnsureBootCamera();
    }

    // 常驻启动相机：空场景没有相机，OnGUI 画在未定义的缓冲上，恢复前台会黑屏。
    void EnsureBootCamera()
    {
        if (bootCam != null) return;
        var go = new GameObject("[BootCamera]");
        DontDestroyOnLoad(go);
        bootCam = go.AddComponent<Camera>();
        bootCam.clearFlags = CameraClearFlags.SolidColor;
        bootCam.backgroundColor = new Color(.055f, .07f, .09f, 1f);
        bootCam.cullingMask = 0;           // 只清屏，不画世界
        bootCam.orthographic = true;
        bootCam.orthographicSize = 5f;
        bootCam.depth = -1000;             // 永远在主相机之下
    }

    public static void Begin(Action complete)
    {
        var go = new GameObject("[ResourceLoader]");
        DontDestroyOnLoad(go);
        var loader = go.AddComponent<WzAndroidLoader>();
        loader.complete = complete;
        loader.privateReceipt = Path.Combine(Application.persistentDataPath, "NativeAssetIndex.sha256");
        loader.LoadBackground();
        loader.manifest = Resources.Load<TextAsset>("NativeAssetIndex").text;
        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            loader.sdk = version.GetStatic<int>("SDK_INT");
    }

    void LoadBackground()
    {
        var image = Resources.Load<TextAsset>("LoadingBackground");
        if (image == null) return;
        if (background != null) Destroy(background);
        background = new Texture2D(2, 2);
        background.LoadImage(image.bytes);
    }

    bool HasPermission()
    {
        if (sdk < 30) return Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite);
        using (var environment = new AndroidJavaClass("android.os.Environment"))
            return environment.CallStatic<bool>("isExternalStorageManager");
    }

    void RequestPermission()
    {
        permissionRequested = true;
        if (sdk < 30) { Permission.RequestUserPermission(Permission.ExternalStorageWrite); return; }
        using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var uriClass = new AndroidJavaClass("android.net.Uri"))
        using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
        using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION", uri))
            activity.Call("startActivity", intent);
    }

    // ——————————————————— 后台加载 ———————————————————

    void StartLoading()
    {
        failed = false; message = null; loaded = 0; total = 0;
        workerPhase = "正在检查资源";
        string expected = manifest;

        loading = Task.Run(() =>
        {
            Directory.CreateDirectory(Root);
            string data = Path.Combine(Root, "Data");
            string previous = Path.Combine(Root, "PreviousData");
            if (!Directory.Exists(data) && Directory.Exists(previous)) Directory.Move(previous, data);

            bool dataExists = Directory.Exists(data);
            bool receiptOk = ReceiptMatches(expected);
            string zip = Path.Combine(Root, "MapleDashResources.zip");

            // 已安装且回执匹配：只抽样校验，正常情况几百毫秒内完成。
            if (receiptOk && dataExists)
            {
                workerPhase = "正在检查资源";
                try
                {
                    WzResourceInstaller.VerifySample(Root, expected, 32);
                }
                catch
                {
                    // 抽样发现异常：退回到完整校验，交由真正的错误提示。
                    workerPhase = "正在校验资源";
                    WzResourceInstaller.Verify(Root, expected, (n, all) => { total = all; loaded = n; });
                }
                WriteReceipt(expected);
                loaded = 1; total = 1;
                return;
            }

            // 需要安装：优先用 ZIP；ZIP 与当前 APK 不匹配时退回校验已有 Data。
            if (File.Exists(zip))
            {
                try
                {
                    workerPhase = "首次安装：解压并校验资源";
                    WzResourceInstaller.Install(zip, Root, expected, (n, all) => { total = all; loaded = n; });
                    WriteReceipt(expected);
                    loaded = 1; total = 1;
                    return;
                }
                catch (Exception) when (dataExists)
                {
                    // 旧 ZIP / 版本不符：Data 还在，改走校验路径。
                }
            }

            if (!dataExists)
                throw new FileNotFoundException("未找到资源。请把 MapleDashResources.zip 放到 /sdcard/mapstory/ 后重试。");

            workerPhase = "正在校验资源";
            WzResourceInstaller.Verify(Root, expected, (n, all) => { total = all; loaded = n; });
            WriteReceipt(expected);
            loaded = 1; total = 1;
        });
    }

    // ——————————————————— 主循环 ———————————————————

    void Update()
    {
        pulse += Time.unscaledDeltaTime;

        if (starting) return;              // 收尾交由 StartGameRoutine

        RefreshProgress();

        if (!hasPermission)
        {
            if (Time.unscaledTime >= nextPermissionCheck)
            {
                hasPermission = HasPermission();
                nextPermissionCheck = Time.unscaledTime + .5f;
            }
            if (!hasPermission)
            {
                // 文案保持稳定，不要逐帧覆盖导致「来回切换」。
                phase = "请允许访问资源目录";
                if (!permissionRequested)
                    try { RequestPermission(); } catch (Exception e) { message = e.Message; }
                return;
            }
        }

        if (loading == null && !failed) StartLoading();
        if (loading == null) return;

        // 阶段文案由后台线程单向写入，这里只在真正变化时同步一次。
        string wp = workerPhase;
        if (!string.IsNullOrEmpty(wp) && wp != phase) phase = wp;

        if (!loading.IsCompleted) return;

        if (loading.IsFaulted)
        {
            failed = true;
            message = loading.Exception.GetBaseException().Message;
            phase = "资源加载失败";
        }
        else if (!startInvoked)
        {
            startInvoked = true;
            StartCoroutine(StartGameRoutine());
        }
        loading = null;
    }

    void RefreshProgress()
    {
        if (Time.unscaledTime < nextProgressUpdate) return;
        nextProgressUpdate = Time.unscaledTime + .1f;
        int count = loaded, countTotal = total;
        float target = countTotal > 0 ? Mathf.Clamp01((float)count / countTotal) : 0f;
        visibleProgress = Mathf.Max(visibleProgress, target);   // 只进不退，避免回跳
        progressText = countTotal > 0
            ? Mathf.FloorToInt(visibleProgress * 100f) + "%   " + count + "/" + countTotal
            : Mathf.FloorToInt(visibleProgress * 100f) + "%";
    }

    System.Collections.IEnumerator StartGameRoutine()
    {
        phase = "资源就绪，正在启动游戏";
        starting = true;
        visibleProgress = 1f;
        progressText = "100%";
        yield return null;                 // 先让「正在启动游戏」渲染一帧

        try { complete?.Invoke(); }
        catch (Exception e)
        {
            starting = false; failed = true; startInvoked = false;
            phase = "启动失败"; message = e.Message;
            yield break;
        }

        // 等主相机就绪；这期间 [BootCamera] 保证画面不黑。
        float wait = 0f;
        while (Camera.main == null && wait < 3f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }
        if (Camera.main == null)
        {
            starting = false; failed = true; startInvoked = false;
            phase = "启动超时"; message = "游戏相机未就绪，请点重试";
            yield break;
        }

        if (bootCam != null) { Destroy(bootCam.gameObject); bootCam = null; }
        Destroy(gameObject);
    }

    void Retry()
    {
        failed = false; message = null; starting = false; startInvoked = false;
        loading = null; workerPhase = null;
        loaded = 0; total = 0; visibleProgress = 0f; progressText = "0%";
        phase = "准备加载资源"; nextProgressUpdate = 0f;
        EnsureBootCamera();
    }

    // ——————————————————— 回执 ———————————————————

    static string ManifestHash(string text) => WzResourceInstaller.Hash(Encoding.UTF8.GetBytes(text));

    IEnumerable<string> ReceiptPaths()
    {
        yield return Path.Combine(Root, "NativeAssetIndex.sha256");
        if (!string.IsNullOrEmpty(privateReceipt)) yield return privateReceipt;
    }

    bool ReceiptMatches(string expected)
    {
        string hash = ManifestHash(expected);
        foreach (var path in ReceiptPaths())
        {
            try { if (File.Exists(path) && File.ReadAllText(path).Trim() == hash) return true; }
            catch { }
        }
        return false;
    }

    void WriteReceipt(string expected)
    {
        string hash = ManifestHash(expected);
        foreach (var path in ReceiptPaths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, hash + "\n");
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch { }
        }
    }

    // ——————————————————— 返回前台 ———————————————————

    void OnApplicationPause(bool paused)
    {
        if (!paused) OnResumed();
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused) OnResumed();
    }

    void OnResumed()
    {
        nextProgressUpdate = 0f;
        nextPermissionCheck = 0f;
        if (background == null) LoadBackground();
        if (!starting) EnsureBootCamera();   // 恢复前台时确保有相机清屏
    }

    void OnDestroy()
    {
        if (background != null) Destroy(background);
        if (bootCam != null) { Destroy(bootCam.gameObject); bootCam = null; }
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
            buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 20 };
        }
        var matrix = GUI.matrix; var color = GUI.color; int depth = GUI.depth;
        try
        {
            GUI.depth = -1000;
            GUI.color = Color.white;
            GUI.matrix = Matrix4x4.identity;
            // 不透明底色，避免旧字形残留；有背景图时再盖一层。
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.055f, .07f, .09f, 1), 0, 0);
            if (background != null) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), background, ScaleMode.ScaleAndCrop);
            GUI.DrawTexture(new Rect(0, Screen.height * .74f, Screen.width, Screen.height * .26f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0, 0, 0, .68f), 0, 0);
            float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 960 * scale) / 2, Screen.height - 540 * scale - Screen.safeArea.y, 0), Quaternion.identity, Vector3.one * scale);

            GUI.Label(new Rect(120, 412, 720, 30), phase, titleStyle);

            // 进度条
            GUI.DrawTexture(new Rect(160, 454, 640, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.18f, .2f, .23f), 0, 0);
            if (visibleProgress > 0)
                GUI.DrawTexture(new Rect(160, 454, 640 * visibleProgress, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.25f, .7f, .4f), 0, 0);
            // 启动阶段加一条来回扫动的高光，表示仍在工作（不是卡 100%）。
            if (starting)
            {
                const float seg = 90f;
                const float travel = 640f - seg;
                float x = 160f + Mathf.PingPong(pulse * 200f, travel);
                GUI.DrawTexture(new Rect(x, 454, seg, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(1f, 1f, 1f, .22f), 0, 0);
            }

            GUI.Label(new Rect(160, 470, 640, 22), progressText, detailStyle);

            if (!string.IsNullOrEmpty(message)) GUI.Label(new Rect(80, 320, 800, 70), message, detailStyle);

            if (failed && GUI.Button(new Rect(330, 386, 300, 26), "重试", buttonStyle)) Retry();
            if (!hasPermission && permissionRequested && loading == null && GUI.Button(new Rect(330, 386, 300, 26), "授予文件访问权限", buttonStyle))
                try { RequestPermission(); } catch (Exception e) { message = e.Message; }
        }
        finally { GUI.matrix = matrix; GUI.color = color; GUI.depth = depth; }
    }
}
#endif
