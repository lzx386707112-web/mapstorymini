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
    float botWait;
    float loadingWait, stalledWait;
    int lastLoaded, lastTotal;
    string lastWorkerPhase;
    const float ResourceStallTimeout = 120f;
    // 假人预热的硬上限（秒）。BotPreload 自己有 45s 上限，这里给足余量再兜一层。
    const float MaxBotWaitSeconds = 75f;
    int sdk;
    GUIStyle titleStyle, detailStyle, buttonStyle;
    float nextProgressUpdate, visibleProgress, pulse, nextPermissionCheck;
    string progressText = "0%";
    bool hasPermission;
    bool resourceUpdateStarted, resourceUpdateFinished;
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
        try
        {
            var index = Resources.Load<TextAsset>("NativeAssetIndex");
            if (index == null) throw new FileNotFoundException("安装包缺少资源清单，请重新安装 APK。");
            loader.manifest = index.text;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                loader.sdk = version.GetStatic<int>("SDK_INT");
        }
        catch (Exception e)
        {
            loader.manifest = null;
            loader.failed = true;
            loader.phase = "加载初始化失败";
            loader.message = e.Message + " 请退出后重新打开游戏。";
        }
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
        loadingWait = stalledWait = 0f;
        lastLoaded = lastTotal = 0; lastWorkerPhase = null;
        workerPhase = "正在检查资源";
        string expected = manifest;

        loading = Task.Run(() =>
        {
            Directory.CreateDirectory(Root);
            WzResourceInstaller.CleanupRetired(Root);
            string data = Path.Combine(Root, "Data");
            string previous = Path.Combine(Root, "PreviousData");
            if (!Directory.Exists(data) && Directory.Exists(previous)) Directory.Move(previous, data);

            bool dataExists = Directory.Exists(data);
            bool receiptOk = ReceiptMatches(expected);
            string zip = Path.Combine(Root, "MapleDashResources.zip");

            string patch = Path.Combine(Root, "MapleDashResources.patch.zip");
            if (WzResourceParts.Exists(patch) && !WzResourceUpdate.InstalledThisLaunch)
            {
                // Do not skip patches via the fast receipt path, or hide patch errors.
                string targetManifest = WzResourceInstaller.ReadArchiveManifest(patch);
                WzResourceInstaller.Install(patch, Root, targetManifest, (n, all) => { total = all; loaded = n; },
                    value => workerPhase = value, null, expected);
                manifest = targetManifest;
                loaded = 1; total = 1;
                return;
            }

            // 全量包直接解压（单包或 part01..NN 分包）；已有 Data 直接加载，不再抽样/全量哈希或匹配 APK 清单。
            if (WzResourceParts.Exists(zip))
            {
                workerPhase = "正在解压资源";
                string extracted = WzResourceInstaller.ExtractFull(zip, Root,
                    (n, all) => { total = all; loaded = n; });
                if (extracted != null) manifest = extracted;
            }
            if (!Directory.Exists(data))
                throw new FileNotFoundException("未找到资源。请把 MapleDashResources.zip（或全部分包 " +
                    "MapleDashResources.part01.zip、part02.zip…）放到 /sdcard/mapstory/ 后重试。");
            loaded = 1; total = 1;
        });
    }

    // ——————————————————— 主循环 ———————————————————

    void Update()
    {
        pulse += Time.unscaledDeltaTime;

        if (starting) return;              // 收尾交由 StartGameRoutine

        RefreshProgress();
        if (failed && loading == null) return;

        if (!hasPermission)
        {
            if (Time.unscaledTime >= nextPermissionCheck)
            {
                try { hasPermission = HasPermission(); }
                catch (Exception e)
                {
                    failed = true; phase = "权限检查失败"; message = e.Message;
                    return;
                }
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

        if (!resourceUpdateFinished)
        {
            if (!resourceUpdateStarted)
            {
                resourceUpdateStarted = true;
                StartCoroutine(CheckResourceUpdate());
            }
            return;
        }

        if (loading == null && !failed) StartLoading();
        if (loading == null) return;

        // 阶段文案由后台线程单向写入，这里只在真正变化时同步一次。
        string wp = workerPhase;
        if (!failed && !string.IsNullOrEmpty(wp) && wp != phase) phase = wp;

        if (!loading.IsCompleted)
        {
            // 只累计前台等待时间；文件完成或切换阶段都会重置无进展计时。
            if (Application.isFocused)
            {
                loadingWait += Time.unscaledDeltaTime;
                stalledWait += Time.unscaledDeltaTime;
            }
            if (loaded != lastLoaded || total != lastTotal || wp != lastWorkerPhase)
            {
                stalledWait = 0f;
                lastLoaded = loaded; lastTotal = total; lastWorkerPhase = wp;
            }
            if (!failed && stalledWait >= ResourceStallTimeout)
            {
                failed = true; phase = "资源检查超时";
                message = "连续两分钟没有进展。正在等待当前读写结束，结束后可重试；若一直无响应，请退出重开，并检查存储权限和剩余空间。";
                Debug.LogWarning("[Resources] No progress for 120s: " + wp + " " + loaded + "/" + total);
            }
            else if (!failed && loadingWait >= 10f)
                message = "首次解压或完整校验可能较久，耗时取决于手机性能和存储速度，请保持游戏在前台。";
            return;
        }

        if (loading.IsFaulted)
        {
            failed = true;
            message = loading.Exception.GetBaseException().Message + " 请检查资源包、存储权限和剩余空间后重试。";
            Debug.LogException(loading.Exception.GetBaseException());
            phase = "资源加载失败";
        }
        else if (!failed && !startInvoked)
        {
            message = null;
            startInvoked = true;
            StartCoroutine(StartGameRoutine());
        }
        loading = null;
    }

    System.Collections.IEnumerator CheckResourceUpdate()
    {
        // 在版本同步前先恢复上次被中断的目录切换。
        string data = Path.Combine(Root, "Data"), previous = Path.Combine(Root, "PreviousData");
        try { if (!Directory.Exists(data) && Directory.Exists(previous)) Directory.Move(previous, data); }
        catch (Exception e) { Debug.LogWarning("[Resources] 恢复资源目录失败：" + e.Message); }
        var recovery = Task.Run(() => WzResourcePatchInstaller.Recover(Root));
        phase = "正在恢复资源补丁";
        while (!recovery.IsCompleted) yield return null;
        if (recovery.IsFaulted)
        {
            failed = true; phase = "资源恢复失败";
            message = recovery.Exception.GetBaseException().Message + " 请联系群主处理后重新启动。";
            yield break;
        }
        string fullZip = Path.Combine(Root, "MapleDashResources.zip");
        if (WzResourceParts.Exists(fullZip))
        {
            phase = "正在解压资源";
            var extract = Task.Run(() => WzResourceInstaller.ExtractFull(fullZip, Root,
                (n, all) => { total = all; loaded = n; }));
            while (!extract.IsCompleted) yield return null;
            if (extract.IsFaulted)
            {
                failed = true; phase = "资源解压失败";
                message = extract.Exception.GetBaseException().Message;
                yield break;
            }
            if (extract.Result != null) manifest = extract.Result;
        }
        phase = "正在检查资源更新";
        yield return WzResourceUpdate.Run(manifest);
        // 资源更新流程结束后再检查 APK，等待用户处理完才继续加载。
        phase = "正在检查应用更新";
        bool appUpdateFinished = false;
        AppUpdate.Begin(() => appUpdateFinished = true);
        while (!appUpdateFinished) yield return null;
        // 授权之后才访问外部 Data；更新已在任何资源缓存/假人预热之前完成。
        string installedManifest = Path.Combine(Root, "Data/.resource-manifest.json");
        try
        {
            // 手动部署补丁仍按当前 APK 的清单校验；在线补丁已在上面安装完成。
            if ((WzResourceUpdate.InstalledThisLaunch || !File.Exists(Path.Combine(Root, "MapleDashResources.patch.zip"))) && File.Exists(installedManifest))
                manifest = File.ReadAllText(installedManifest);
        }
        catch (Exception e) { Debug.LogWarning("[Resources] 读取已安装清单失败：" + e.Message); }
        resourceUpdateFinished = true;
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
        phase = "正在加载AI假人系统";
        starting = true;
        visibleProgress = 1f;
        progressText = "资源安装完成，正在准备游戏";
        yield return null;                 // 先让假人系统加载提示渲染一帧

        // 资源可读之后先启动本地服务端，把原来登录期间的预建/合成留在加载页。
        // 两个组件仍按原有帧预算推进，完成后自行销毁；此时尚未创建可交互的登录界面。
        BotPreload botPreload;
        BotAppearancePrebake botPrebake;
        try
        {
            GameSession.Boot();
            botPrebake = GetComponent<BotAppearancePrebake>();
            if (botPrebake == null) botPrebake = gameObject.AddComponent<BotAppearancePrebake>();
            botPreload = GetComponent<BotPreload>();
            if (botPreload == null) botPreload = gameObject.AddComponent<BotPreload>();
        }
        catch (Exception e)
        {
            starting = false; failed = true; startInvoked = false;
            phase = "启动失败"; message = e.Message;
            yield break;
        }
        while (botPreload != null || botPrebake != null)
        {
            // 硬上限：假人系统自己会超时放行，这里再兜一层，任何异常路径都不该把
            // 加载页钉死在这个循环里（真机上出现过 100% CPU、画面停在加载页、日志停在
            // [Art] ready 的情况）。超时就带上现有假人继续，别拦着玩家进登录页。
            botWait += Time.unscaledDeltaTime;
            if (botWait >= 10f) message = "正在准备角色资源，耗时取决于手机性能，请稍候。";
            if (botWait >= MaxBotWaitSeconds)
            {
                Debug.LogWarning("[Bot] 预热超时 " + MaxBotWaitSeconds + "s，跳过剩余预热直接启动");
                message = "角色资源准备超时，将使用已就绪的资源继续启动。";
                break;
            }
            yield return null;
        }

        message = null;
        phase = "资源就绪，正在启动游戏";
        yield return null;
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
        if (loading != null || string.IsNullOrEmpty(manifest)) return;
        botWait = 0f;
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

            float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 960 * scale) / 2, Screen.height - 540 * scale - Screen.safeArea.y, 0), Quaternion.identity, Vector3.one * scale);

            GUI.DrawTexture(new Rect((960f - Screen.width / scale) / 2f, 378, Screen.width / scale, 162 + Screen.safeArea.y / scale), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0, 0, 0, .68f), 0, 0);
            GUI.Label(new Rect(120, 440, 720, 30), phase, titleStyle);

            // 进度条
            GUI.DrawTexture(new Rect(160, 478, 640, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.18f, .2f, .23f), 0, 0);
            if (visibleProgress > 0)
                GUI.DrawTexture(new Rect(160, 478, 640 * visibleProgress, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.25f, .7f, .4f), 0, 0);
            // 启动阶段加一条来回扫动的高光，表示仍在工作（不是卡 100%）。
            if (starting)
            {
                const float seg = 90f;
                const float travel = 640f - seg;
                float x = 160f + Mathf.PingPong(pulse * 200f, travel);
                GUI.DrawTexture(new Rect(x, 478, seg, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(1f, 1f, 1f, .22f), 0, 0);
            }

            GUI.Label(new Rect(160, 492, 640, 22), progressText, detailStyle);

            if (!string.IsNullOrEmpty(message)) GUI.Label(new Rect(80, 380, 800, 58), message, detailStyle);

            if (failed && loading == null && !string.IsNullOrEmpty(manifest) && GUI.Button(new Rect(330, 516, 300, 24), "重试", buttonStyle)) Retry();
            if (!failed && !hasPermission && permissionRequested && loading == null && GUI.Button(new Rect(330, 516, 300, 24), "授予文件访问权限", buttonStyle))
                try { RequestPermission(); } catch (Exception e) { message = e.Message; }
        }
        finally { GUI.matrix = matrix; GUI.color = color; GUI.depth = depth; }
    }
}
#endif
