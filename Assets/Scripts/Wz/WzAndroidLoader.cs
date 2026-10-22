#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

public sealed class WzAndroidLoader : MonoBehaviour
{
    Action complete;
    string manifest, message, phase = "准备加载资源";
    const string Root = "/sdcard/mapstory";
    Task loading;
    volatile int loaded, total;
    bool permissionRequested, failed, entering;
    int sdk;
    public static void Begin(Action complete)
    {
        var go = new GameObject("[ResourceLoader]"); DontDestroyOnLoad(go);
        var loader = go.AddComponent<WzAndroidLoader>(); loader.complete = complete;
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
        if (!HasPermission())
        {
            phase = "请允许访问资源目录";
            if (!permissionRequested)
                try { RequestPermission(); } catch (Exception e) { message = e.Message; }
            return;
        }
        if (loading == null && !failed) StartLoading();
        if (loading == null || !loading.IsCompleted) return;
        if (loading.IsFaulted) { failed = true; message = loading.Exception.GetBaseException().Message; }
        else { phase = "加载完成，正在进入游戏"; entering = true; }
        loading = null;
    }
    void OnGUI()
    {
        GUI.skin.font = Resources.Load<Font>("Fonts/ui");
        float width = Mathf.Min(Screen.width - 64, 720), x = (Screen.width - width) / 2, y = Screen.height * .42f;
        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, wordWrap = true };
        GUI.Label(new Rect(x, y - 70, width, 65), message ?? phase, style);
        float progress = entering ? 1f : total > 0 ? (float)loaded / total : 0f;
        GUI.DrawTexture(new Rect(x, y, width, 24), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.18f, .2f, .23f), 0, 0);
        GUI.DrawTexture(new Rect(x, y, width * progress, 24), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.25f, .7f, .4f), 0, 0);
        GUI.Label(new Rect(x, y + 32, width, 40), Mathf.FloorToInt(progress * 100) + "%", style);
        if (failed && GUI.Button(new Rect(x, y + 88, width, 55), "重试")) failed = false;
        if (permissionRequested && loading == null && !HasPermission() && GUI.Button(new Rect(x, y + 88, width, 55), "授予文件访问权限"))
            try { RequestPermission(); } catch (Exception e) { message = e.Message; }
    }
}
#endif
