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
                settings.Call("setDatabaseEnabled", true);
                // 后台 UI 打包在 adminui.bytes 里，重装后哈希变了；WebView 按 URL 缓存会继续喂旧页面，
                // 所以每次打开都不要缓存（本地回环，代价只有几毫秒）。
                settings.Call("setCacheMode", 2);                    // WebSettings.LOAD_NO_CACHE
                settings.Call("setUseWideViewPort", true);           // 让页面按设备宽度排版（mobile.less 的断点才生效）
                settings.Call("setLoadWithOverviewMode", true);
                settings.Call("setSupportZoom", false);              // 横屏手指误触缩放会把布局搞乱
                settings.Call("setBuiltInZoomControls", false);
                web.Call("setBackgroundColor", unchecked((int)0xFF101418));   // 先铺底色，避免加载前白闪
                web.Call("setWebViewClient", new ErrorClient());
                // 关闭 Dialog 时同步销毁 WebView，防泄漏
                dialog.Call("setOnDismissListener", new DismissCallback(web));
                dialog.Call("setContentView", web);
                dialog.Call("show");
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

    // 加载失败时换成一张能读懂的错误页（默认错误页是纯黑背景，看着像「黑屏没反应」）。
    // 只实现 API 23+ 的主框架回调：AndroidJavaProxy 按方法名派发，同名重载会二义，不能两个都写。
    sealed class ErrorClient : AndroidJavaProxy
    {
        public ErrorClient() : base("android.webkit.WebViewClient") { }

        void onReceivedError(AndroidJavaObject view, AndroidJavaObject request, AndroidJavaObject error)
        {
            try
            {
                if (request == null || view == null) return;
                if (!request.Call<bool>("isForMainFrame")) return;      // 子资源失败不动整页
                int code = error != null ? error.Call<int>("getErrorCode") : 0;
                string desc = error != null ? error.Call<string>("getDescription") : "";
                string url = request.Call<AndroidJavaObject>("getUrl").Call<string>("toString");
                Debug.LogError("[Admin] WebView 加载失败 code=" + code + " " + desc + " url=" + url);
                // 用 loadData 而不是 loadDataWithBaseURL：后者要传 Java null，Unity 的 JNI 参数
                // 推导对 null 不友好；错误页是纯内联样式，不需要 baseUrl。
                view.Call("loadData", ErrorHtml(url, code, desc), "text/html", "utf-8");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Admin] 错误页渲染失败: " + e.Message);
            }
        }

        static string ErrorHtml(string url, int code, string desc)
        {
            string hint = code == -1 && desc != null && desc.ToLowerInvariant().Contains("cleartext")
                ? "当前安装包没有放行本地明文 HTTP（Android 9+ 默认禁止），需要用最新构建重新出包。"
                : "本地后台服务可能还没起来，返回后再点一次「后台管理」即可重试。";
            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                   "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                   "<style>body{margin:0;padding:24px;background:#101418;color:#e6ebf2;" +
                   "font:15px/1.7 -apple-system,'PingFang SC','Microsoft YaHei',sans-serif}" +
                   "h1{font-size:17px;margin:0 0 14px;color:#ffb454}code{color:#7fd1ff;word-break:break-all}" +
                   "p{margin:0 0 10px;color:#9fb0c3}</style></head><body>" +
                   "<h1>后台页面加载失败</h1>" +
                   "<p>地址：<code>" + Html(url) + "</code></p>" +
                   "<p>原因：<code>" + Html(desc) + "</code>（code " + code + "）</p>" +
                   "<p>" + Html(hint) + "</p>" +
                   "<p>返回键关闭本页。</p></body></html>";
        }

        static string Html(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
#else
    public static bool Supported => false;

    public static void Open(string url) => Application.OpenURL(url);

    public static void Close() { }
#endif
}
