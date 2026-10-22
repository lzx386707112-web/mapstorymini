using UnityEngine;

// Shared drawing helpers; every visible bitmap comes from an original IMG node.
public static class NativeUi
{
    public static bool Button(Rect rect, string node)
    {
        bool hover = rect.Contains(Pointer.Pos);
        var tex = Art.Tex("wz://" + node + "/" + (hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal") + "/0");
        if (tex != null) GUI.DrawTexture(rect, tex);
        return Event.current.type == EventType.Repaint && Pointer.Released && hover;
    }
    public static void Panel(Rect rect)
    {
        float top = Mathf.Min(40 * BottomBar.S, rect.height / 2), bottom = Mathf.Min(47 * BottomBar.S, rect.height / 2);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, top), Art.Tex("wz://UI/Basic.img/Notice2/t"));
        GUI.DrawTexture(new Rect(rect.x, rect.y + top, rect.width, Mathf.Max(0, rect.height - top - bottom)), Art.Tex("wz://UI/Basic.img/Notice2/c"));
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - bottom, rect.width, bottom), Art.Tex("wz://UI/Basic.img/Notice2/s"));
    }
}
