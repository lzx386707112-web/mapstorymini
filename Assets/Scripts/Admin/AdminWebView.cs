using System;
using UnityEngine;

// 后台管理的「应用内浏览器」：Android 上用全屏 Dialog 承载 WebView 覆盖在游戏画面上，
// 系统返回键关闭（Dialog 的默认行为），桌面/编辑器仍走系统浏览器。
//
// 为什么不用外部浏览器切屏：Android 切走后 Unity Activity onPause，主线程停摆，
// 后台请求全部等超时（见 AdminHost 的泵线程）；留在进程内就不存在这个问题，
// 而且横竖屏直接跟游戏走，不用单独适配。
//
// Dialog 而不是往 decor view 塞 View：返回键默认 dismiss 整个后台，
// 不用自定义 OnBackPressed 处理历史栈。
public static class AdminWebView
{
#if !UNITY_WEBGL && UNITY_ANDROID
    static AndroidJavaObject dialog;   // 静态持有：关闭与防重复打开

    public static bool Supported => true;

    public static void Open(string url)
    {
        Close();
        var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
            .GetStatic<AndroidJavaObject>("currentActivity");
        if (activity == null) throw new InvalidOperationException("Unity activity 未就绪");
        activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            try
            {
                // Theme_Black_NoTitleBar_Fullscreen：全屏黑底，无标题栏
                dialog = new AndroidJavaObject("android.app.Dialog", activity,
                    new AndroidJavaClass("android.R$style").GetStatic<int>("Theme_Black_NoTitleBar_Fullscreen"));
                var web = new AndroidJavaObject("android.webkit.WebView", activity);
                var settings = web.Call<AndroidJavaObject>("getSettings");
                settings.Call("setJavaScriptEnabled", true);
                settings.Call("setDomStorageEnabled", true);
                // 关闭 Dialog 时同步销毁 WebView，防泄漏
                dialog.Call("setOnDismissListener", new DismissCallback(web));
                dialog.Call("setContentView", web);
                dialog.Call("show");
                web.Call("setBackgroundColor", unchecked((int)0xFF101418));
                web.Call("loadUrl", url);
            }
            catch (Exception e)
            {
                dialog = null;
                Debug.LogError("[Admin] 应用内 WebView 打开失败，回退系统浏览器: " + e.Message);
                Application.OpenURL(url);
            }
        }));
    }

    public static void Close()
    {
        var d = dialog;
        dialog = null;
        if (d == null) return;
        try { d.Call<bool>("isShowing"); d.Call("dismiss"); }
        catch (Exception) { /* activity 已销毁等场景忽略 */ }
    }

    sealed class DismissCallback : AndroidJavaProxy
    {
        readonly AndroidJavaObject web;
        public DismissCallback(AndroidJavaObject web) : base("android.content.DialogInterface$OnDismissListener") => this.web = web;
        void onDismiss(AndroidJavaObject _)
        {
            try { web.Call("destroy"); } catch (Exception) { }
            if (dialog != null) dialog.Dispose();
            dialog = null;
        }
    }
#else
    public static bool Supported => false;

    public static void Open(string url) => Application.OpenURL(url);

    public static void Close() { }
#endif
}
