using UnityEngine;

// Shared drawing helpers; every visible bitmap comes from an original IMG node.
public static class NativeUi
{
    public static bool Button(Rect rect, string node)
    {
        bool hover = rect.Contains(Pointer.Pos);
        var tex = Art.Tex("wz://" + node + "/" + (hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal") + "/0");
        if (tex != null) GUI.DrawTexture(rect, tex);
        bool clicked = GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Clicked && hover;
        // 原版 UI 点击音（Sound/UI.img/BtMouseClick）；这里覆盖所有用原生 UI 资源的按钮
        if (clicked) { Pointer.ConsumeClick(); AudioManager.Play("UI/BtMouseClick", 0.7f, false); }
        return clicked;
    }
    public static void DialogPanel(Rect rect)
    {
        const string root = "wz://UI/UIWindow.img/UtilDlgEx/";
        // Native widths are 529px. Keep the 28px top / 58px bottom and corner
        // caps intact instead of scaling the entire window into a thin banner.
        Strip(new Rect(rect.x, rect.y, rect.width, 28), Art.Tex(root + "t"));
        var center = Art.Tex(root + "c");
        // Preserve native dithering: stretching a 20px row creates vertical stripes.
        for (float y = rect.y + 28; y < rect.yMax - 58; y += 20)
            Strip(new Rect(rect.x, y, rect.width, Mathf.Min(20, rect.yMax - 58 - y)), center);
        Strip(new Rect(rect.x, rect.yMax - 58, rect.width, 58), Art.Tex(root + "s"));
    }

    static void Strip(Rect rect, Texture2D texture)
    {
        if (texture == null || rect.width <= 0 || rect.height <= 0) return;
        float cap = Mathf.Min(12f, rect.width / 2f);
        float uv = 12f / texture.width;
        GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.y, cap, rect.height), texture, new Rect(0, 0, uv, 1));
        GUI.DrawTextureWithTexCoords(new Rect(rect.x + cap, rect.y, rect.width - cap * 2, rect.height), texture, new Rect(uv, 0, 1 - uv * 2, 1));
        GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - cap, rect.y, cap, rect.height), texture, new Rect(1 - uv, 0, uv, 1));
    }
    public static void Panel(Rect rect)
    {
        // Compact labels are not full Notice2 windows (native minimum height 87).
        if (rect.height < 87f) { Strip(rect, Art.Tex("wz://UI/Basic.img/Notice2/c")); return; }
        Strip(new Rect(rect.x, rect.y, rect.width, 40), Art.Tex("wz://UI/Basic.img/Notice2/t"));
        Strip(new Rect(rect.x, rect.y + 40, rect.width, rect.height - 87), Art.Tex("wz://UI/Basic.img/Notice2/c"));
        Strip(new Rect(rect.x, rect.yMax - 47, rect.width, 47), Art.Tex("wz://UI/Basic.img/Notice2/s"));
    }
}
