using System.Collections.Generic;
using UnityEngine;

// BeiDou.exe 0xBE27E0: each bindable key owns a 32x32 icon rectangle.
// Keep this separate from the touch keyboard's wider keycap hit regions.
public static class NativeKeyLayout
{
    // Native drawing mirrors modifier icons; hit handling folds right Shift to left Shift.
    public static KeyCode FunctionKey(KeyCode key)
    {
        if (key == KeyCode.RightShift) return KeyCode.LeftShift;
        if (key == KeyCode.RightControl) return KeyCode.LeftControl;
        if (key == KeyCode.RightAlt) return KeyCode.LeftAlt;
        return key;
    }
    public static KeyCode OtherModifier(KeyCode key)
    {
        if (key == KeyCode.LeftShift) return KeyCode.RightShift;
        if (key == KeyCode.LeftControl) return KeyCode.RightControl;
        if (key == KeyCode.LeftAlt) return KeyCode.RightAlt;
        return KeyCode.None;
    }

    // Windows VK order matters when several keys go down in the same poll.
    public static int VirtualKey(KeyCode key)
    {
        key = FunctionKey(key);
        if (key >= KeyCode.A && key <= KeyCode.Z) return (int)key - (int)KeyCode.A + 65;
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return (int)key;
        if (key >= KeyCode.F1 && key <= KeyCode.F12) return (int)key - (int)KeyCode.F1 + 112;
        switch (key)
        {
            case KeyCode.Backspace: return 8; case KeyCode.Tab: return 9;
            case KeyCode.Return: return 13; case KeyCode.LeftShift: return 16;
            case KeyCode.LeftControl: return 17; case KeyCode.LeftAlt: return 18;
            case KeyCode.Pause: return 19; case KeyCode.CapsLock: return 20;
            case KeyCode.Escape: return 27; case KeyCode.Space: return 32;
            case KeyCode.PageUp: return 33; case KeyCode.PageDown: return 34;
            case KeyCode.End: return 35; case KeyCode.Home: return 36;
            case KeyCode.LeftArrow: return 37; case KeyCode.UpArrow: return 38;
            case KeyCode.RightArrow: return 39; case KeyCode.DownArrow: return 40;
            case KeyCode.Insert: return 45; case KeyCode.Delete: return 46;
            case KeyCode.Semicolon: return 186; case KeyCode.Equals: return 187;
            case KeyCode.Comma: return 188; case KeyCode.Minus: return 189;
            case KeyCode.Period: return 190; case KeyCode.Slash: return 191;
            case KeyCode.BackQuote: return 192; case KeyCode.LeftBracket: return 219;
            case KeyCode.Backslash: return 220; case KeyCode.RightBracket: return 221;
            case KeyCode.Quote: return 222;
        }
        return 0;
    }

    public static IEnumerable<KeyCode> PhysicalKeys => scanCodes.Values;

    // 0x00836C82 maps tray indices 28..32 to function IDs 50..54.
    public static Vector2 TrayPosition(GameAction action)
    {
        var entry = NativeKeyActions.Of(action);
        if (entry == null) return Vector2.zero;
        int index = entry.id >= 100 ? entry.id - 100 + 33 : entry.id >= 50 ? entry.id - 50 + 28 : entry.id;
        return new Vector2(9 + index % 18 * 34, 267 + index / 18 * 34);
    }

    static readonly Dictionary<int, KeyCode> scanCodes = new Dictionary<int, KeyCode> {
        { 1, KeyCode.Escape },
        { 2, KeyCode.Alpha1 },
        { 3, KeyCode.Alpha2 },
        { 4, KeyCode.Alpha3 },
        { 5, KeyCode.Alpha4 },
        { 6, KeyCode.Alpha5 },
        { 7, KeyCode.Alpha6 },
        { 8, KeyCode.Alpha7 },
        { 9, KeyCode.Alpha8 },
        { 10, KeyCode.Alpha9 },
        { 11, KeyCode.Alpha0 },
        { 12, KeyCode.Minus },
        { 13, KeyCode.Equals },
        { 14, KeyCode.Backspace },
        { 15, KeyCode.Tab },
        { 16, KeyCode.Q },
        { 17, KeyCode.W },
        { 18, KeyCode.E },
        { 19, KeyCode.R },
        { 20, KeyCode.T },
        { 21, KeyCode.Y },
        { 22, KeyCode.U },
        { 23, KeyCode.I },
        { 24, KeyCode.O },
        { 25, KeyCode.P },
        { 26, KeyCode.LeftBracket },
        { 27, KeyCode.RightBracket },
        { 28, KeyCode.Return },
        { 29, KeyCode.LeftControl },
        { 30, KeyCode.A },
        { 31, KeyCode.S },
        { 32, KeyCode.D },
        { 33, KeyCode.F },
        { 34, KeyCode.G },
        { 35, KeyCode.H },
        { 36, KeyCode.J },
        { 37, KeyCode.K },
        { 38, KeyCode.L },
        { 39, KeyCode.Semicolon },
        { 40, KeyCode.Quote },
        { 41, KeyCode.BackQuote },
        { 42, KeyCode.LeftShift },
        { 43, KeyCode.Backslash },
        { 44, KeyCode.Z },
        { 45, KeyCode.X },
        { 46, KeyCode.C },
        { 47, KeyCode.V },
        { 48, KeyCode.B },
        { 49, KeyCode.N },
        { 50, KeyCode.M },
        { 51, KeyCode.Comma },
        { 52, KeyCode.Period },
        { 53, KeyCode.Slash },
        { 54, KeyCode.RightShift },
        { 56, KeyCode.LeftAlt },
        { 57, KeyCode.Space },
        { 58, KeyCode.CapsLock },
        { 59, KeyCode.F1 },
        { 60, KeyCode.F2 },
        { 61, KeyCode.F3 },
        { 62, KeyCode.F4 },
        { 63, KeyCode.F5 },
        { 64, KeyCode.F6 },
        { 65, KeyCode.F7 },
        { 66, KeyCode.F8 },
        { 67, KeyCode.F9 },
        { 68, KeyCode.F10 },
        { 71, KeyCode.Home },
        { 73, KeyCode.PageUp },
        { 79, KeyCode.End },
        { 81, KeyCode.PageDown },
        { 82, KeyCode.Insert },
        { 83, KeyCode.Delete },
        { 87, KeyCode.F11 },
        { 88, KeyCode.F12 },
        { 89, KeyCode.RightControl },
        { 90, KeyCode.RightAlt },
        { 157, KeyCode.RightControl },
        { 184, KeyCode.RightAlt },
        { 199, KeyCode.Home },
        { 200, KeyCode.UpArrow },
        { 201, KeyCode.PageUp },
        { 203, KeyCode.LeftArrow },
        { 205, KeyCode.RightArrow },
        { 207, KeyCode.End },
        { 208, KeyCode.DownArrow },
        { 209, KeyCode.PageDown },
        { 210, KeyCode.Insert },
        { 211, KeyCode.Delete },
    };
    public static KeyCode FromScanCode(int scan)
        => scanCodes.TryGetValue(scan, out var key) ? FunctionKey(key) : KeyCode.None;
    public static int ToScanCode(KeyCode key)
    {
        key = FunctionKey(key);
        foreach (var pair in scanCodes)
            if (pair.Value == key) return pair.Key;
        return -1;
    }

    // 原版键名字形：UIWindow.img/KeyConfig/key/<扫描码>，11~13 像素高的点阵小图。
    // 26 格快捷栏的键名就是这套字形，不再用字体画（字体那条路会糊、还会被格子裁掉）。
    // 表里没有的键返回 null —— 不拿自绘文字顶替。
    public static string NamePath(KeyCode key)
    {
        int scan = ToScanCode(key);
        return scan < 0 ? null : "wz://UI/UIWindow.img/KeyConfig/key/" + scan;
    }

    static readonly Dictionary<KeyCode, Texture2D> nameTex = new Dictionary<KeyCode, Texture2D>();

    public static Texture2D NameTexture(KeyCode key)
    {
        if (key == KeyCode.None) return null;
        if (nameTex.TryGetValue(key, out var tex)) return tex;
        var path = NamePath(key);
        tex = path == null ? null : Art.Tex(path);
        nameTex[key] = tex;
        return tex;
    }

    public static readonly Dictionary<KeyCode, Vector2> Positions = new Dictionary<KeyCode, Vector2>
    {
        { KeyCode.Alpha1, new Vector2(48, 66) },
        { KeyCode.Alpha2, new Vector2(82, 66) },
        { KeyCode.Alpha3, new Vector2(116, 66) },
        { KeyCode.Alpha4, new Vector2(150, 66) },
        { KeyCode.Alpha5, new Vector2(184, 66) },
        { KeyCode.Alpha6, new Vector2(218, 66) },
        { KeyCode.Alpha7, new Vector2(252, 66) },
        { KeyCode.Alpha8, new Vector2(286, 66) },
        { KeyCode.Alpha9, new Vector2(320, 66) },
        { KeyCode.Alpha0, new Vector2(354, 66) },
        { KeyCode.Minus, new Vector2(388, 66) },
        { KeyCode.Equals, new Vector2(422, 66) },
        { KeyCode.Q, new Vector2(64, 99) },
        { KeyCode.W, new Vector2(98, 99) },
        { KeyCode.E, new Vector2(132, 99) },
        { KeyCode.R, new Vector2(166, 99) },
        { KeyCode.T, new Vector2(200, 99) },
        { KeyCode.Y, new Vector2(234, 99) },
        { KeyCode.U, new Vector2(268, 99) },
        { KeyCode.I, new Vector2(302, 99) },
        { KeyCode.O, new Vector2(336, 99) },
        { KeyCode.P, new Vector2(370, 99) },
        { KeyCode.LeftBracket, new Vector2(404, 99) },
        { KeyCode.RightBracket, new Vector2(438, 99) },
        { KeyCode.LeftControl, new Vector2(22, 198) },
        { KeyCode.A, new Vector2(81, 132) },
        { KeyCode.S, new Vector2(115, 132) },
        { KeyCode.D, new Vector2(149, 132) },
        { KeyCode.F, new Vector2(183, 132) },
        { KeyCode.G, new Vector2(217, 132) },
        { KeyCode.H, new Vector2(251, 132) },
        { KeyCode.J, new Vector2(285, 132) },
        { KeyCode.K, new Vector2(319, 132) },
        { KeyCode.L, new Vector2(353, 132) },
        { KeyCode.Semicolon, new Vector2(387, 132) },
        { KeyCode.Quote, new Vector2(421, 132) },
        { KeyCode.BackQuote, new Vector2(14, 66) },
        { KeyCode.LeftShift, new Vector2(38, 165) },
        { KeyCode.Backslash, new Vector2(472, 99) },
        { KeyCode.Z, new Vector2(98, 165) },
        { KeyCode.X, new Vector2(132, 165) },
        { KeyCode.C, new Vector2(166, 165) },
        { KeyCode.V, new Vector2(200, 165) },
        { KeyCode.B, new Vector2(234, 165) },
        { KeyCode.N, new Vector2(268, 165) },
        { KeyCode.M, new Vector2(302, 165) },
        { KeyCode.Comma, new Vector2(336, 165) },
        { KeyCode.Period, new Vector2(370, 165) },
        { KeyCode.RightShift, new Vector2(457, 165) },
        { KeyCode.LeftAlt, new Vector2(122, 198) },
        { KeyCode.Space, new Vector2(233, 198) },
        { KeyCode.F1, new Vector2(82, 27) },
        { KeyCode.F2, new Vector2(116, 27) },
        { KeyCode.F3, new Vector2(150, 27) },
        { KeyCode.F4, new Vector2(184, 27) },
        { KeyCode.F5, new Vector2(226, 27) },
        { KeyCode.F6, new Vector2(260, 27) },
        { KeyCode.F7, new Vector2(294, 27) },
        { KeyCode.F8, new Vector2(328, 27) },
        { KeyCode.F9, new Vector2(370, 27) },
        { KeyCode.F10, new Vector2(404, 27) },
        { KeyCode.Home, new Vector2(548, 66) },
        { KeyCode.PageUp, new Vector2(582, 66) },
        { KeyCode.End, new Vector2(548, 99) },
        { KeyCode.PageDown, new Vector2(582, 99) },
        { KeyCode.Insert, new Vector2(514, 66) },
        { KeyCode.Delete, new Vector2(514, 99) },
        { KeyCode.F11, new Vector2(438, 27) },
        { KeyCode.F12, new Vector2(472, 27) },
        { KeyCode.RightControl, new Vector2(461, 198) },
        { KeyCode.RightAlt, new Vector2(348, 198) },
    };
}
