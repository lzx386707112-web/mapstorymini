using UnityEngine;

// 全局 UI 缩放。
// 手机上物理分辨率动辄 2400×1080，如果按真实像素排 UI，技能格只有几毫米宽。
// 这里统一按"虚拟高度 720"排版，再用 GUI.matrix 放大到实际分辨率，
// 于是同一份布局在编辑器和真机上看起来是一样的比例。
public static class UiKit
{
    public const float DesignH = 720f;

    public static float Scale { get; private set; } = 1f;
    public static float W     { get; private set; } = 1280f;   // 虚拟宽
    public static float H     { get; private set; } = DesignH; // 虚拟高

    static Font _font;
    static bool _fontTried;

    public static void RefreshMetrics()
    {
        Scale = Mathf.Clamp(Screen.height / DesignH, 0.5f, 4f);
        W = Screen.width / Scale;
        H = Screen.height / Scale;
        LogOnce();
    }

    // 真机上「按 Screen.height 算虚拟高」偶尔会对不上实际可见区域
    // （厂商 ROM 报的分辨率比屏幕大），表现是底部栏/大按钮被屏幕下缘切掉。
    // 这里把真实数值打一次日志，方便对着 adb logcat 核对。
    static bool logged;
    static void LogOnce()
    {
        if (logged) return;
        logged = true;
        var sa = Screen.safeArea;
        Debug.Log("[Ui] screen=" + Screen.width + "x" + Screen.height +
                  " dpi=" + Screen.dpi +
                  " scale=" + Scale.ToString("0.00") +
                  " virtual=" + W.ToString("0") + "x" + H.ToString("0") +
                  " safe=" + sa.x.ToString("0") + "," + sa.y.ToString("0") + " " +
                  sa.width.ToString("0") + "x" + sa.height.ToString("0") +
                  " insets(l/r/t/b)=" + sa.x.ToString("0") + "/" + (Screen.width - sa.xMax).ToString("0") +
                  "/" + (Screen.height - sa.yMax).ToString("0") + "/" + sa.y.ToString("0"));
    }

    public static void Begin()
    {
        RefreshMetrics();

        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));

        if (!_fontTried)
        {
            _fontTried = true;
            _font = Resources.Load<Font>("Fonts/ui");
        }
        ApplyFont();
    }

    public static Font Font => _font;

    // GUIStyle 不指定 font 时用的是 GUI.skin.font，而内置字体没有中文字形：
    // 于是英文/数字正常、中文整段不显示（技能格键帽能看见、按钮文字看不见就是这么来的）。
    // 安卓 IL2CPP 首帧 GUI.skin 可能是 null，所以必须判空并兜住异常。
    static void ApplyFont()
    {
        if (_font == null) return;
        try
        {
            if (GUI.skin != null && GUI.skin.font != _font) GUI.skin.font = _font;
        }
        catch { /* skin 还没准备好时忽略，下一帧会重试 */ }
    }

    // 触摸/鼠标位置转成虚拟坐标（GUI 原点在左上）
    public static Vector2 MouseGui()
    {
        return new Vector2(Input.mousePosition.x / Scale,
                           (Screen.height - Input.mousePosition.y) / Scale);
    }

    public static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }
}
