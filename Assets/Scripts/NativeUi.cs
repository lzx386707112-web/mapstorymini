using UnityEngine;

// Shared drawing helpers; every visible bitmap comes from an original IMG node.
public static class NativeUi
{
    // Mobile presentation constraint, in the window's own IMG coordinates.
    // Keep native geometry intact; move the whole window back into the viewport.
    public static Vector2 ClampWindowPosition(Vector2 position, Vector2 size, float scale)
    {
        return new Vector2(
            Mathf.Clamp(position.x, 0f, Mathf.Max(0f, UiKit.W / scale - size.x)),
            Mathf.Clamp(position.y, 0f, Mathf.Max(0f, UiKit.H / scale - size.y)));
    }

    public static bool Button(Rect rect, string node)
    {
        return Button(rect, node, Pointer.Pos);
    }
    public static bool Button(Rect rect, string node, Vector2 pointer)
    {
        bool hover = rect.Contains(pointer);
        bool enabled = GUI.enabled && (!NativeUtilPrompt.Visible || NativeUtilPrompt.Drawing);
        var tex = Art.Tex("wz://" + node + "/" + (!enabled ? "disabled" : hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal") + "/0");
        if (tex != null) GUI.DrawTexture(rect, tex);
        bool clicked = enabled && Event.current.type == EventType.Repaint && Pointer.Clicked && hover;
        // 原版 UI 点击音（Sound/UI.img/BtMouseClick）；这里覆盖所有用原生 UI 资源的按钮
        if (clicked) { Pointer.ConsumeClick(); AudioManager.Play("UI/BtMouseClick", 0.7f, false); }
        return clicked;
    }
    static int scrollDrag;
    static float scrollGrab;
    public static float Scroll4(Rect bar, Rect view, float value, float maximum, float step, Vector2 pointer)
    {
        value = Mathf.Clamp(value, 0, maximum);
        bool active = maximum > 0;
        string root = "wz://UI/Basic.img/VScr4/" + (active ? "enabled/" : "disabled/");
        var track = new Rect(bar.x, bar.y + 13, 15, bar.height - 26);
        var texture = Art.Tex(root + "base");
        for (float y = track.y; y < track.yMax; y += 13)
        {
            float height = Mathf.Min(13, track.yMax - y);
            if (texture != null) GUI.DrawTextureWithTexCoords(new Rect(bar.x,y,15,height),texture,new Rect(0,1-height/13,1,height/13));
        }
        GUI.DrawTexture(new Rect(bar.x,bar.y,15,13),Art.Tex(root + (active ? "prev0" : "prev")));
        GUI.DrawTexture(new Rect(bar.x,bar.yMax-13,15,13),Art.Tex(root + (active ? "next0" : "next")));
        if (!active) return 0;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        float travel = Mathf.Max(1,track.height - 25);
        var thumb = new Rect(bar.x,track.y+travel*value/maximum,15,25);
        if (GUI.enabled && Event.current.type == EventType.Repaint)
        {
            if (view.Contains(pointer)) value -= Input.mouseScrollDelta.y * step * 2;
            if (Pointer.Pressed)
            {
                if (thumb.Contains(pointer)) { scrollDrag = id; scrollGrab = pointer.y-thumb.y; Pointer.ConsumeClick(); }
                else if (new Rect(bar.x,bar.y,15,13).Contains(pointer)) { value -= step; Pointer.ConsumeClick(); }
                else if (new Rect(bar.x,bar.yMax-13,15,13).Contains(pointer)) { value += step; Pointer.ConsumeClick(); }
            }
            if (scrollDrag == id)
            { if (Pointer.Down) value = (pointer.y-scrollGrab-track.y)/travel*maximum; else scrollDrag = 0; }
        }
        value = Mathf.Clamp(Mathf.Round(value/step)*step,0,maximum);
        GUI.DrawTexture(new Rect(bar.x,track.y+travel*value/maximum,15,25),Art.Tex(root+"thumb0"));
        return value;
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
        {
            float height = Mathf.Min(20, rect.yMax - 58 - y);
            if (center != null && rect.width == center.width)
                GUI.DrawTextureWithTexCoords(new Rect(rect.x, y, rect.width, height), center,
                    new Rect(0f, 1f - height / center.height, 1f, height / center.height));
            else Strip(new Rect(rect.x, y, rect.width, height), center);
        }
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
