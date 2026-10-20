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

    public static void Begin()
    {
        Scale = Mathf.Clamp(Screen.height / DesignH, 0.5f, 4f);
        W = Screen.width / Scale;
        H = Screen.height / Scale;

        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));

        if (!_fontTried)
        {
            _fontTried = true;
            _font = Resources.Load<Font>("Fonts/ui");
            // 注意：这里刻意不写 GUI.skin.font。
            // 安卓 IL2CPP 下 GUI.skin 在首帧可能为 null，赋值会把整个 OnGUI 打断。
            // 字体在需要的地方通过 GUIStyle.font 显式指定。
        }
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
