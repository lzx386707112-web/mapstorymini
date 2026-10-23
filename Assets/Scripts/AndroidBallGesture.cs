using UnityEngine;

// 悬浮球贴边时，触点在系统返回手势区内。给球所在的小矩形申请手势排除，
// 这样从球上向内拖动不会被系统抢走；球移走后恢复。矩形很小，不会影响整条边。
public static class AndroidBallGesture
{
#if UNITY_ANDROID && !UNITY_EDITOR
    static int lastL = int.MinValue, lastT, lastR, lastB;
    static bool hasRect;
    static float nextRefresh;
    static bool warned, logged;

    public static void Update(Vector2 center, float radius)
    {
        if (Screen.width <= 0 || Screen.height <= 0) return;

        float s = UiKit.Scale;
        int l = Mathf.Clamp(Mathf.RoundToInt((center.x - radius) * s), 0, Screen.width);
        int t = Mathf.Clamp(Mathf.RoundToInt((center.y - radius) * s), 0, Screen.height);
        int r = Mathf.Clamp(Mathf.RoundToInt((center.x + radius) * s), 0, Screen.width);
        int b = Mathf.Clamp(Mathf.RoundToInt((center.y + radius) * s), 0, Screen.height);
        if (r - l < 8 || b - t < 8) return;

        bool moved = !hasRect
                  || Mathf.Abs(l - lastL) > 4 || Mathf.Abs(t - lastT) > 4
                  || Mathf.Abs(r - lastR) > 4 || Mathf.Abs(b - lastB) > 4;
        bool refresh = Time.unscaledTime >= nextRefresh;
        if (!moved && !refresh) return;

        lastL = l; lastT = t; lastR = r; lastB = b;
        hasRect = true;
        nextRefresh = Time.unscaledTime + 2f;

        float dp = Screen.dpi > 1f ? Screen.dpi / 160f : 1f;
        Apply(l, t, r, b, Mathf.RoundToInt(4f * dp), false);
    }

    public static void Clear()
    {
        if (!hasRect) return;
        hasRect = false;
        lastL = int.MinValue;
        Apply(0, 0, 0, 0, 0, true);
    }

    static void Apply(int l, int t, int r, int b, int pad, bool clear)
    {
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                if (version.GetStatic<int>("SDK_INT") < 29) return;
            }

            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null) return;
                bool log = !logged;
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using (var u = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        using (var a = u.GetStatic<AndroidJavaObject>("currentActivity"))
                        using (var window = a.Call<AndroidJavaObject>("getWindow"))
                        using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
                        using (var rect = new AndroidJavaObject("android.graphics.Rect"))
                        using (var list = new AndroidJavaObject("java.util.ArrayList"))
                        {
                            if (!clear)
                            {
                                rect.Call("set", l - pad, t - pad, r + pad, b + pad);
                                list.Call<bool>("add", rect);
                            }
                            decor.Call("setSystemGestureExclusionRects", list);

                            if (log && !clear)
                            {
                                logged = true;
                                Debug.Log("[SideMenu] 悬浮球手势排除 " + (r - l) + "x" + (b - t) + "px");
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        if (!warned)
                        {
                            warned = true;
                            Debug.LogWarning("[SideMenu] 悬浮球手势排除失败: " + e.Message);
                        }
                    }
                }));
            }
        }
        catch (System.Exception e)
        {
            if (!warned)
            {
                warned = true;
                Debug.LogWarning("[SideMenu] 悬浮球手势排除失败: " + e.Message);
            }
        }
    }
#else
    public static void Update(Vector2 center, float radius) { }
    public static void Clear() { }
#endif
}
