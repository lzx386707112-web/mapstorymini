using UnityEngine;

// Shared drawing helpers; every visible bitmap comes from an original IMG node.
public static class NativeUi
{
    // CCtrlTab style 2 -> Tab3 (4DE4D0), not Tab2. Positions from 4DE15F/4DD903.
    public static int TabsStyle2(string labels, int count, int selected, float x, float y, int width, Vector2 pointer, int fixedWidth = 0)
    {
        int inner = fixedWidth > 0 ? fixedWidth - 8 : (width - (count - 1) * 12 - 12) / count;
        int remainder = fixedWidth > 0 ? 0 : width - (inner + 12) * count;
        void Image(string name, float dx)
        {
            var t = Art.Tex("wz://UI/Basic.img/Tab3/" + name);
            if (t != null) GUI.DrawTexture(new Rect(dx, y, t.width, t.height), t);
        }
        Image("left" + (selected == 0 ? 1 : 0), x);
        float start = x + 6;
        for (int i = 0; i < count; i++)
        {
            int length = inner + (i < remainder ? 1 : 0);
            var fill = Art.Tex("wz://UI/Basic.img/Tab3/fill" + (selected == i ? 1 : 0));
            if (fill != null) GUI.DrawTexture(new Rect(start, y, length, fill.height), fill);
            float end = start + length;
            Image(i == count - 1 ? "right" + (selected == i ? 1 : 0) : "middle" + (selected == i ? 1 : selected == i + 1 ? 2 : 0), end);
            var label = Art.Tex("wz://" + labels + "/" + (selected == i ? "disabled/" : "enabled/") + i);
            if (label != null) GUI.DrawTexture(new Rect(start + length / 2 - label.width / 2, y + 10 - label.height / 2 + 2, label.width, label.height), label);
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && new Rect(start - 6, y, length + 12, 21).Contains(pointer))
            { Pointer.ConsumeClick(); selected = i; }
            start = end + 12;
        }
        return selected;
    }

    static readonly System.Collections.Generic.Dictionary<string, (Rect rect, System.Func<bool> visible)> windows =
        new System.Collections.Generic.Dictionary<string, (Rect, System.Func<bool>)>();
    // Bounds are in UiKit coordinates, before per-window GUI scaling/groups.
    // Keep the last geometry so the bottom bar can hit-test before painting windows.
    public static void RegisterWindow(string key, Rect rect, System.Func<bool> visible)
        => windows[key] = (rect, visible);
    public static bool WindowAtPointer
    {
        get
        {
            // Live geometry also covers the first frame after opening or resizing a matrix popup.
            if (NativeVMatrixWindow.AtPointer || NativeHexaMatrixWindow.AtPointer) return true;
            foreach (var entry in windows.Values)
                if (entry.visible() && (entry.rect.Contains(Pointer.Pos) ||
                    (Pointer.Down || Pointer.Released) && entry.rect.Contains(Pointer.PressPosition))) return true;
            return false;
        }
    }
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
        return Button(rect, node, Pointer.Pos, false);
    }
    // forceNormal：模态弹窗打开时背景仍按正常态绘制（不置灰），但点击依旧由 GUI.enabled 拦截。
    public static bool Button(Rect rect, string node, Vector2 pointer, bool forceNormal = false)
    {
        bool hover = rect.Contains(pointer);
        bool enabled = GUI.enabled && (!NativeUtilPrompt.Visible || NativeUtilPrompt.Drawing);
        bool visual = enabled || forceNormal;
        var tex = Art.Tex("wz://" + node + "/" + (!visual ? "disabled" : hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal") + "/0");
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
                else if (track.Contains(pointer))
                { value += (pointer.y < thumb.y ? -1f : 1f) * Mathf.Max(16f, Mathf.Floor((maximum / step + 1f) / 16f)) * step; Pointer.ConsumeClick(); }
            }
            if (scrollDrag == id)
            { if (Pointer.Down) value = (pointer.y-scrollGrab-track.y)/travel*maximum; else scrollDrag = 0; }
        }
        value = Mathf.Clamp(Mathf.Round(value/step)*step,0,maximum);
        GUI.DrawTexture(new Rect(bar.x,track.y+travel*value/maximum,15,25),Art.Tex(root+"thumb0"));
        return value;
    }

    // CUIShop::SetAvatar 754779 and CTrunkDlg::SetAvatar 7C70C8 use the current look.
    public static void CharacterPortrait(float x, float y)
    {
        var character = GameSession.Character;
        if (character == null) return;
        var ids = WzCharacter.CharacterIds(character.record, character.inventory);
        var sprite = WzCharacter.Appearance(string.Join("_", ids), ids, WzCharacter.AppearanceFrame(ids, Time.unscaledTime));
        if (sprite != null)
            GUI.DrawTexture(new Rect(x - sprite.pivot.x, y - sprite.rect.height + sprite.pivot.y,
                sprite.rect.width, sprite.rect.height), sprite.texture);
    }

    static Texture2D dialogPanel;
    public static void DialogPanel(Rect rect)
    {
        const string root = "wz://UI/UIWindow.img/UtilDlgEx/";
        int height = Mathf.CeilToInt(rect.height);
        if (height < 86) return;
        if (dialogPanel == null || dialogPanel.height != height)
        {
            var top = Art.Tex(root + "t");
            var center = Art.Tex(root + "c");
            var bottom = Art.Tex(root + "s");
            if (top == null || center == null || bottom == null) return;
            var pixels = new Color32[top.width * height];
            var topPixels = top.GetPixels32();
            var centerPixels = center.GetPixels32();
            var bottomPixels = bottom.GetPixels32();
            // Assemble in native pixels, bottom-up like Texture2D. Separate GUI quads
            // can expose the field at fractional screen-scale tile boundaries.
            for (int y = 0; y < height; y++)
            {
                int fromTop = height - 1 - y;
                var source = fromTop < 28 ? topPixels : y < 58 ? bottomPixels : centerPixels;
                int sourceY = fromTop < 28 ? 27 - fromTop : y < 58 ? y :
                    center.height - 1 - (fromTop - 28) % center.height;
                System.Array.Copy(source, sourceY * top.width, pixels, y * top.width, top.width);
            }
            if (dialogPanel != null) Object.Destroy(dialogPanel);
            dialogPanel = new Texture2D(top.width, height, TextureFormat.RGBA32, false)
            { name = "UtilDlgEx assembled", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            dialogPanel.SetPixels32(pixels);
            dialogPanel.Apply(false, true);
        }
        if (rect.width == dialogPanel.width) GUI.DrawTexture(rect, dialogPanel);
        else Strip(rect, dialogPanel);
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
