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
                // Theme_Black_NoTitleBar（不是 _Fullscreen）：全屏主题下系统会忽略
                // SOFT_INPUT_ADJUST_RESIZE，横屏弹键盘会直接盖住登录框。
                // 状态栏/导航栏改成 show() 之后用 systemUiVisibility 沉浸式隐藏（见下）。
                dialog = new AndroidJavaObject("android.app.Dialog", activity,
                    new AndroidJavaClass("android.R$style").GetStatic<int>("Theme_Black_NoTitleBar"));
                var web = new AndroidJavaObject("android.webkit.WebView", activity);
                var settings = web.Call<AndroidJavaObject>("getSettings");
                settings.Call("setJavaScriptEnabled", true);
                settings.Call("setDomStorageEnabled", true);
                settings.Call("setDatabaseEnabled", true);
                // 后台 UI 打包在 adminui.bytes 里，重装后哈希变了；WebView 按 URL 缓存会继续喂旧页面，
                // 所以每次打开都不要缓存（本地回环，代价只有几毫秒）。
                settings.Call("setCacheMode", 2);                    // WebSettings.LOAD_NO_CACHE
                settings.Call("setUseWideViewPort", true);           // 认 <meta viewport> 里的 device-width
                // 关键：OVERVIEW 模式会把整页「缩小到一屏可见」，页面一旦有横向溢出
                // （登录页/表格都比视口宽一点）整页就会被缩到 0.65 左右，字和提示条全被压扁。
                // 关掉它，按 viewport meta 的 initial-scale=1 渲染，宽了就横向滚动。
                settings.Call("setLoadWithOverviewMode", false);
                settings.Call("setSupportZoom", false);              // 横屏手指误触缩放会把布局搞乱
                settings.Call("setBuiltInZoomControls", false);
                settings.Call("setTextZoom", 100);                   // 别被系统「字体大小」设置二次缩放
                web.Call("setBackgroundColor", unchecked((int)0xFF101418));   // 先铺底色，避免加载前白闪
                // 不设 WebViewClient：它是抽象类不是接口，AndroidJavaProxy 代理不了
                // （会抛 "android.webkit.WebViewClient is not an interface" 并让整个打开流程回退）。
                // 不设 client 时 WebView 自己接管导航，加载失败也会显示系统自带的错误页。
                // 关闭 Dialog 时同步销毁 WebView，防泄漏
                dialog.Call("setOnDismissListener", new DismissCallback(web));
                dialog.Call("setContentView", web);
                dialog.Call("show");

                // show() 之后再设：Dialog 显示时会被主题的 windowSoftInputMode 覆盖一次。
                var win = dialog.Call<AndroidJavaObject>("getWindow");
                win.Call("setLayout", -1, -1);                 // MATCH_PARENT × MATCH_PARENT
                win.Call("setSoftInputMode", 0x10);            // SOFT_INPUT_ADJUST_RESIZE：键盘顶起页面而不是盖住
                HideSystemBars(win);

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

    // 沉浸式全屏：把状态栏/导航栏藏掉，内容铺满整屏。
    // 游戏主窗口本来就是全屏，但 Dialog 是独立窗口，得自己藏一遍。
    // 这里只用 systemUiVisibility / InsetsController，不设 FLAG_FULLSCREEN，
    // 所以上面设的 SOFT_INPUT_ADJUST_RESIZE 仍然生效（弹键盘时页面会被顶起）。
    static void HideSystemBars(AndroidJavaObject win)
    {
        // LAYOUT_STABLE | LAYOUT_HIDE_NAVIGATION | LAYOUT_FULLSCREEN
        // | HIDE_NAVIGATION | FULLSCREEN | IMMERSIVE_STICKY
        const int flags = 0x100 | 0x200 | 0x400 | 0x2 | 0x4 | 0x1000;
        try { win.Call<AndroidJavaObject>("getDecorView").Call("setSystemUiVisibility", flags); }
        catch (Exception) { }

        try
        {
            // Android 11+：隐藏 状态栏(1)|导航栏(2)，BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE = 2
            var controller = win.Call<AndroidJavaObject>("getInsetsController");
            if (controller != null)
            {
                controller.Call("hide", 3);
                controller.Call("setSystemBarsBehavior", 2);
            }
        }
        catch (Exception) { }   // API < 30 没有 getInsetsController，忽略
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

    // 注：这里没有 WebViewClient。它是抽象类，AndroidJavaProxy 只能代理接口，
    // 设了会抛 "android.webkit.WebViewClient is not an interface"，把整个打开流程拱去系统浏览器。
    // 想自绘错误页就得写 Java 插件（Assets/Plugins/Android/*.java 里的子类），暂不值得。
    // 不设 client 时 WebView 自己处理导航，失败也会显示系统错误页（内容可读，够定位问题）。
#else
    public static bool Supported => false;

    public static void Open(string url) => Application.OpenURL(url);

    public static void Close() { }
#endif
}
