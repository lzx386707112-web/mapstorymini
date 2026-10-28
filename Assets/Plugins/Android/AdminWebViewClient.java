package com.mapledash.game;

import android.webkit.WebView;
import android.webkit.WebViewClient;

// 后台管理 WebView 的导航拦截（AdminWebView 用）。
//
// 为什么必须有它：不给 WebView 设 WebViewClient 时，页面里的 window.location
// 整页导航会被 WebView 派发给系统（ActionView Intent），直接跳到外部浏览器。
// 后台前端一旦逃逸出游戏，Unity Activity 切后台被厂商冻结（nubia 免冻/CpuFreezer），
// 本地 HTTP 服务停摆，外部浏览器里的后台接口全部超时。
//
// 为什么不是 AndroidJavaProxy：WebViewClient 是抽象类，AndroidJavaProxy 只能代理
// 接口（抛 "not an interface"）。所以放一个真实 Java 类进 Plugins/Android，
// 由 Gradle 编进 APK，运行时 new 出来 setWebViewClient。
//
// 策略：所有导航一律内部加载；非 http(s)（intent:/market: 等）直接吞掉。
// 任何情况下都不允许离开应用内 WebView。
public class AdminWebViewClient extends WebViewClient {
    @Override
    public boolean shouldOverrideUrlLoading(WebView view, String url) {
        if (url != null && (url.startsWith("http://") || url.startsWith("https://"))) {
            view.loadUrl(url);
        }
        // 返回 true = 已处理，WebView 不再做默认派发（外部浏览器/应用选择器）
        return true;
    }
}
