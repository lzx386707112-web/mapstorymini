using UnityEngine;

// 预览钩子：只有编辑器预览脚本会挂 Draw，正常运行恒为 null（一次分支，零开销）。
// 调用点在 GameWindows.DrawAll 末尾、ItemTooltip.Flush 之后，保证预览内容压在所有原版窗口之上。
// 存在的意义：预览要「不依赖鼠标位置」直接画一次悬浮框，而绘制必须发生在 OnGUI 的 Repaint 阶段。
public static class UiPreviewHook
{
    public static System.Action Draw;

    // 预览底：铺一层深色，裁出来的对照图不带地图背景，也不受场景光照影响。
    public static Color Backdrop = new Color32(30, 34, 40, 255);
    public static bool SolidBackdrop;

    static Texture2D white;
    static Texture2D White
    {
        get
        {
            if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            return white;
        }
    }

    public static void Emit()
    {
        if (Draw == null) return;
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        // 虚拟按键在 InputBridge.OnGUI 里画，晚于 GameWindows.DrawAll；靠 depth 压到最上层，
        // 否则预览底铺不住，裁出来的图右下角会露出「跳跃 / 技能格」。
        int depth = GUI.depth;
        GUI.depth = -10000;
        if (SolidBackdrop)
        {
            var previous = GUI.color;
            GUI.color = Backdrop;
            // 铺一块远超画布的矩形：UiKit.W/H 在编辑器里会随 GameView 抖动（录到过 W=4824/H=80），
            // 按当时的 W/H 铺底就会只盖住一条。
            GUI.DrawTexture(new Rect(-20000, -20000, 40000, 40000), White);
            GUI.color = previous;
        }
        Draw();
        GUI.depth = depth;
    }
}
