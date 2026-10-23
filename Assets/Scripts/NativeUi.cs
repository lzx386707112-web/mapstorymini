using UnityEngine;

// Shared drawing helpers; every visible bitmap comes from an original IMG node.
public static class NativeUi
{
    public static bool Button(Rect rect, string node)
    {
        bool hover = rect.Contains(Pointer.Pos);
        var tex = Art.Tex("wz://" + node + "/" + (hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal") + "/0");
        if (tex != null) GUI.DrawTexture(rect, tex);
        bool clicked = Event.current.type == EventType.Repaint && Pointer.Released && hover;
        // 原版 UI 点击音（Sound/UI.img/BtMouseClick）；这里覆盖所有用原生 UI 资源的按钮
        if (clicked) AudioManager.Play("UI/BtMouseClick", 0.7f, false);
        return clicked;
    }
    public static void Panel(Rect rect)
    {
        float top = Mathf.Min(40 * BottomBar.S, rect.height / 2), bottom = Mathf.Min(47 * BottomBar.S, rect.height / 2);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, top), Art.Tex("wz://UI/Basic.img/Notice2/t"));
        GUI.DrawTexture(new Rect(rect.x, rect.y + top, rect.width, Mathf.Max(0, rect.height - top - bottom)), Art.Tex("wz://UI/Basic.img/Notice2/c"));
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - bottom, rect.width, bottom), Art.Tex("wz://UI/Basic.img/Notice2/s"));
    }
}
