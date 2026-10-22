#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

public sealed class WzAndroidLoader : MonoBehaviour
{
    Texture2D background;
    Action complete;
    string manifest, message, phase = "准备加载资源";
    const string Root = "/sdcard/mapstory";
    Task loading;
    volatile int loaded, total;
    bool permissionRequested, failed, entering;
    int sdk;
    GUIStyle titleStyle, detailStyle, buttonStyle;
    float nextProgressUpdate, visibleProgress;
    string progressText = "0%";
    bool hasPermission;

    void RefreshProgress()
    {
        if (Time.unscaledTime < nextProgressUpdate && !entering) return;
        nextProgressUpdate = Time.unscaledTime + .1f;
        int count = loaded, countTotal = total;
        visibleProgress = entering ? 1f : countTotal > 0 ? Mathf.Clamp01((float)count / countTotal) : 0f;
        progressText = Mathf.FloorToInt(visibleProgress * 100) + "%";
    }

    public static void Begin(Action complete)
    {
        var go = new GameObject("[ResourceLoader]"); DontDestroyOnLoad(go);
        var loader = go.AddComponent<WzAndroidLoader>(); loader.complete = complete;
        var image = Resources.Load<TextAsset>("LoadingBackground");
        loader.background = new Texture2D(2, 2);
        loader.background.LoadImage(image.bytes);
        loader.manifest = Resources.Load<TextAsset>("NativeAssetIndex").text;
        using (var version = new AndroidJavaClass("android.os.Build$VERSION")) loader.sdk = version.GetStatic<int>("SDK_INT");
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
    void StartLoading()
    {
        failed = false; message = null; loaded = 0; total = 0;
        string expected = manifest;
        loading = Task.Run(() =>
        {
            Directory.CreateDirectory(Root);
            string data = Path.Combine(Root, "Data"), previous = Path.Combine(Root, "PreviousData");
            if (!Directory.Exists(data) && Directory.Exists(previous)) Directory.Move(previous, data);
            string receipt = Path.Combine(Root, "NativeAssetIndex.sha256");
            bool installed = File.Exists(receipt) && File.ReadAllText(receipt).Trim() == WzResourceInstaller.Hash(Encoding.UTF8.GetBytes(expected));
            string zip = Path.Combine(Root, "MapleDashResources.zip");
            if (!installed && File.Exists(zip))
            {
                phase = "正在解压并校验资源";
                WzResourceInstaller.Install(zip, Root, expected, (n, all) => { total = all; loaded = n; });
            }
            else
            {
                phase = "正在校验并加载资源";
                if (!Directory.Exists(data)) throw new FileNotFoundException("请将资源包放到 /sdcard/mapstory/MapleDashResources.zip 后重试。");
                WzResourceInstaller.Verify(Root, expected, (n, all) => { total = all; loaded = n; });
                File.WriteAllText(receipt, WzResourceInstaller.Hash(Encoding.UTF8.GetBytes(expected)) + "\n");
            }
        });
    }
    void Update()
    {
        if (entering) { complete(); Destroy(gameObject); return; }
        RefreshProgress();
        hasPermission = HasPermission();
        if (!hasPermission)
        {
            phase = "请允许访问资源目录";
            if (!permissionRequested)
                try { RequestPermission(); } catch (Exception e) { message = e.Message; }
            return;
        }
        if (loading == null && !failed) StartLoading();
        if (loading == null || !loading.IsCompleted) return;
        if (loading.IsFaulted) { failed = true; message = loading.Exception.GetBaseException().Message; }
        else { phase = "加载完成，正在进入游戏"; entering = true; visibleProgress = 1f; progressText = "100%"; }
        loading = null;
    }
    void OnDestroy() { if (background != null) Destroy(background); }

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
            // There is no game camera yet. Repaint an opaque background to clear old glyphs.
            GUI.matrix = Matrix4x4.identity;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.055f, .07f, .09f, 1), 0, 0);
            if (background != null) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), background, ScaleMode.ScaleAndCrop);
            GUI.DrawTexture(new Rect(0, Screen.height * .74f, Screen.width, Screen.height * .26f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0, 0, 0, .68f), 0, 0);
            float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 960 * scale) / 2, Screen.height - 540 * scale - Screen.safeArea.y, 0), Quaternion.identity, Vector3.one * scale);
            GUI.Label(new Rect(120, 412, 720, 30), failed ? "资源加载失败" : phase, titleStyle);
            GUI.DrawTexture(new Rect(160, 454, 640, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.18f, .2f, .23f), 0, 0);
            if (visibleProgress > 0)
                GUI.DrawTexture(new Rect(160, 454, 640 * visibleProgress, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.25f, .7f, .4f), 0, 0);
            GUI.Label(new Rect(160, 470, 640, 22), progressText, detailStyle);
            // Error/instruction text has its own region, separate from title and percentage.
            if (!string.IsNullOrEmpty(message)) GUI.Label(new Rect(80, 324, 800, 65), message, detailStyle);
            if (failed && GUI.Button(new Rect(330, 386, 300, 26), "重试", buttonStyle)) failed = false;
            if (permissionRequested && loading == null && !hasPermission && GUI.Button(new Rect(330, 386, 300, 26), "授予文件访问权限", buttonStyle))
                try { RequestPermission(); } catch (Exception e) { message = e.Message; }
        }
        finally { GUI.matrix = matrix; GUI.color = color; GUI.depth = depth; }
    }
}
#endif
