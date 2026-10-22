using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 虚拟按键层（Winlator / 盖世模拟器同款思路）：
//   元素(Element) → 绑定(Binding: GameAction) → 注入(InputHub.SetVirtual / SetVirtualAxis)
//
// - 元素类型：BUTTON（单键）、D_PAD（十字方向）、STICK（模拟摇杆）。
//   TRACKPAD 不做：本工程的 Pointer 虚拟光标本身就是轨迹板语义。
// - 坐标归一化 0~1（cx,cy 相对 UiKit.W/H，size 相对 UiKit.H），换分辨率自适应。
// - 布局持久化为 JSON（等价 Winlator 的 .icp），支持多方案、导出即拷走文件。
// - 注入通道全部复用现有架构：
//     Button → InputHub.SetVirtual(KeyMap.Of(action), down)   （按键边沿由 InputHub 维护）
//     Stick/Dpad → InputHub.SetVirtualAxis(x, y)              （模拟量，InputBridge 抄给 TouchInput）
//   所以游戏逻辑一行都不用改。
// ————————————————————————————————————————————————————————————

public enum VCElemType { Button, Dpad, Stick }

public class VCElem
{
    public VCElemType type = VCElemType.Button;
    public GameAction action = GameAction.Jump;  // 旧存档/图标推导用（key 为空时兜底）
    public KeyCode key = KeyCode.None;           // 绑定键（任意键盘键，Winlator 同款）
    public bool shiftMod;                        // Shift+ 组合（键盘面板的 Shift+ 页签）
    public float cx = 0.5f, cy = 0.5f;           // 归一化中心（×UiKit.W / ×UiKit.H）
    public float size = 0.14f;                   // 基准直径，归一化到 UiKit.H
    public float opacity = 1f;                   // 整体不透明度 0.15~1
    public string label = "";                    // 空则自动取键名/技能图标

    // —— 技能绑定（原版快捷格的屏上等价物） ——
    // 技能窗里把技能图标拖到这个按键上就绑定；0 = 没绑。
    // 绑了技能的按键按下时直接施放该技能，不再走"按键 → 快捷格"那条绕路，
    // 所以在快捷格没配好的情况下也能点一下就放（原端拖到快捷栏格子的语义）。
    public int skillId;

    // —— 基础属性（编辑面板） ——
    public float scale = 1f;                     // 整体缩放
    public float rotation = 0f;                  // 旋转角度（度）
    public float aspect = 1f;                    // 纵横比例（宽/高）

    // —— 外观样式 ——
    // shape: 0 圆形 · 1 矩形 · 2 多边形 · 3 LT · 4 LB · 5 RT · 6 RB · 7 无
    public int shape;
    public float borderW = 2.2f;                 // 描边宽度（dp）
    public float halo = 0.12f;                   // 光环宽度（相对半径）
    public Color bg = new Color(0f, 0f, 0f, 0.20f);         // #33000000
    public Color line = new Color(1f, 1f, 1f, 0.50f);       // #80FFFFFF
    public Color bgDown = new Color(1f, 1f, 1f, 0.26f);     // #42FFFFFF
    public Color lineDown = new Color(1f, 1f, 1f, 0.95f);   // #F2FFFFFF

    // —— 摇杆 / 十字方向键的输入模式 ——
    // 标准输入：摇杆直接输出模拟轴（游戏的上下左右），不碰键盘
    // 自定义：推向某个方向时，顺手把该方向绑的键盘键按下（WASD 这类）
    public bool stickStd = true;
    public KeyCode upKey = KeyCode.None, downKey = KeyCode.None;
    public KeyCode leftKey = KeyCode.None, rightKey = KeyCode.None;

    // dir: 0 上 · 1 下 · 2 左 · 3 右
    public KeyCode DirKey(int dir)
        => dir == 0 ? upKey : dir == 1 ? downKey : dir == 2 ? leftKey : rightKey;

    public void SetDirKey(int dir, KeyCode k)
    {
        if (dir == 0) upKey = k;
        else if (dir == 1) downKey = k;
        else if (dir == 2) leftKey = k;
        else rightKey = k;
    }

    public bool HasDirKey => upKey != KeyCode.None || downKey != KeyCode.None
                          || leftKey != KeyCode.None || rightKey != KeyCode.None;

    // —— 运行时状态，不序列化 ——
    public int finger = -1;                      // 占用的手指 id
    public Vector2 axis;                         // Stick/Dpad 本帧输出
    public bool held;                            // Button 是否按住

    // 实际注入的键：优先 key，旧存档没有 key 时退回动作当前键位
    public KeyCode Key => key != KeyCode.None ? key : KeyMap.Of(action);

    public float BaseR => size * 0.5f * UiKit.H;          // 基准半径（未缩放）
    public float R => BaseR * Mathf.Max(0.05f, scale);    // 屏幕像素半径（虚拟坐标）
    public float HalfW => R * Mathf.Max(0.1f, aspect);

    public Rect Rect
    {
        get
        {
            float r = R, w = HalfW;
            var c = VirtualControls.Center(this);
            return new Rect(c.x - w, c.y - r, w * 2f, r * 2f);
        }
    }

    public VCElem Clone()
    {
        return new VCElem { type = type, action = action, key = key, shiftMod = shiftMod,
            cx = cx, cy = cy, size = size, shape = shape, opacity = opacity, label = label,
            skillId = skillId,
            scale = scale, rotation = rotation, aspect = aspect,
            borderW = borderW, halo = halo, bg = bg, line = line, bgDown = bgDown, lineDown = lineDown,
            stickStd = stickStd, upKey = upKey, downKey = downKey, leftKey = leftKey, rightKey = rightKey };
    }

    public bool Hit(Vector2 p)
    {
        // 触摸命中放宽 1.35 倍，拇指定位比鼠标难；忽略旋转（近似即可）
        float r = R * 1.35f, w = HalfW * 1.35f;
        var c = VirtualControls.Center(this);
        var d = p - c;
        if (Mathf.Approximately(w, 0f) || Mathf.Approximately(r, 0f)) return false;
        float k = d.x * d.x / (w * w) + d.y * d.y / (r * r);
        return k <= 1f;
    }
}

public static class VirtualControls
{
    public static readonly Color Teal = new Color(0.06f, 0.66f, 0.60f);   // Winlator 主题色

    public static List<VCElem> elems = new List<VCElem>();
    public static string activeName = "默认";
    public static List<string> profileNames = new List<string>();
    public static bool OverlayVisible = true;

    // —————————— 系统输入法避让 ——————————
    // 聊天框/宏名字输入时，安卓会弹出输入法，屏幕下半被整体盖住：
    // 虚拟按键照常画，但用户看不见也按不到（"显示键盘时按键都没了"）。
    // 这里把整层按输入法高度上移，键盘收起后自动落回原位。
    // 只影响绘制与命中，不改存盘坐标（cx/cy 仍是用户在编辑器里摆的位置）。
    public static float LiftY { get; private set; }

    static int baseScreenH;   // 键盘没弹出时的屏幕高，用来判断系统是不是已经帮我们把视图缩小了

    static void UpdateLift()
    {
        if (!TouchScreenKeyboard.visible)
        {
            baseScreenH = Screen.height;
            LiftY = 0f;
            return;
        }

        // adjustResize 的机器上，Unity 视图本身会被压矮，按键已经跟着上移了，
        // 再叠加一次会让它们冲出屏幕上沿，所以这种情况直接不动。
        bool resized = baseScreenH > 0 && Screen.height < baseScreenH * 0.9f;
        float lift = 0f;
        if (!resized)
        {
            float h = 0f;
            try { h = TouchScreenKeyboard.area.height; } catch { /* 个别机型/版本不支持 */ }
            if (h <= 1f) h = Screen.height * 0.42f;          // 拿不到高度时按典型键盘比例估
            lift = h / Mathf.Max(0.01f, UiKit.Scale);
        }
        // 上限 55%：再高会把按键顶到状态栏外面去
        LiftY = Mathf.Clamp(lift, 0f, UiKit.H * 0.55f);
    }

    // 控件的实际屏幕中心（含键盘避让上移），且保证整个圆仍在屏内。
    // 绘制、命中、摇杆轴向计算全部走这里，三处不会各算一套。
    public static Vector2 Center(VCElem e)
    {
        float y = e.cy * UiKit.H - LiftY;
        float r = e.R;
        y = Mathf.Clamp(y, r + 4f, Mathf.Max(r + 4f, UiKit.H - r - 4f));
        return new Vector2(e.cx * UiKit.W, y);
    }


    const string DirName = "vcontrols";
    static bool loaded;

    static string Dir => Path.Combine(Application.persistentDataPath, DirName);
    static string ManifestPath => Path.Combine(Dir, "manifest.json");
    static string ProfilePath(string name) => Path.Combine(Dir, name + ".json");

    // —————————— 载入 ——————————
    // 摇杆的默认皮肤：深蓝黑底盘 + 亮蓝环。按钮那套白色描边放在摇杆上会"散"掉，
    // 手游摇杆本身也多是冷色半透明，所以单独给一组（用户仍可在样式页改）。
    public static void ApplyStickSkin(VCElem e)
    {
        e.borderW = 3.4f;
        e.halo = 0.10f;
        e.bg = new Color(0.055f, 0.085f, 0.125f, 0.82f);
        e.line = new Color(0.29f, 0.62f, 1f, 0.95f);
        e.bgDown = new Color(0.075f, 0.115f, 0.165f, 0.88f);
        e.lineDown = new Color(0.50f, 0.80f, 1f, 1f);
        e.shape = 0;
    }

    public static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        ReadManifest();
        if (profileNames.Count == 0) profileNames.Add("默认");
        if (!profileNames.Contains(activeName)) activeName = profileNames[0];
        if (!LoadFromFile(activeName)) { elems.Clear(); BuildDefaultLayout(); SaveActive(); }
    }

    static void ReadManifest()
    {
        profileNames.Clear();
        try
        {
            if (!File.Exists(ManifestPath)) { activeName = "默认"; return; }
            var root = MiniJson.Parse(File.ReadAllText(ManifestPath)) as Dictionary<string, object>;
            if (root == null) return;
            if (root.TryGetValue("active", out var a) && a is string s) activeName = s;
            if (root.TryGetValue("profiles", out var list) && list is List<object> ls)
                foreach (var it in ls) if (it is string n && !profileNames.Contains(n)) profileNames.Add(n);
        }
        catch { /* 坏档当作没有 */ }
    }

    static void WriteManifest()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.Append("{\"active\":").Append(Q(activeName)).Append(",\"profiles\":[");
            for (int i = 0; i < profileNames.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Q(profileNames[i]));
            }
            sb.Append("]}");
            File.WriteAllText(ManifestPath, sb.ToString());
        }
        catch { /* 存储满等场景下静默，下次保存再试 */ }
    }

    public static bool LoadFromFile(string name)
    {
        try
        {
            if (!File.Exists(ProfilePath(name))) return false;
            return ParseElements(File.ReadAllText(ProfilePath(name)));
        }
        catch { return false; }
    }

    // 编辑器「撤回 / 还原」用：整份布局进出一趟字符串，和存档格式完全一致
    public static string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"elements\":[");
        for (int i = 0; i < elems.Count; i++)
        {
            var e = elems[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"t\":").Append(Q(e.type.ToString()))
              .Append(",\"a\":").Append(Q(e.action.ToString()))
              .Append(",\"k\":").Append((int)e.key)
              .Append(",\"sm\":").Append(e.shiftMod ? 1 : 0)
              .Append(",\"x\":").Append(N(e.cx))
              .Append(",\"y\":").Append(N(e.cy))
              .Append(",\"s\":").Append(N(e.size))
              .Append(",\"shape\":").Append(e.shape)
              .Append(",\"op\":").Append(N(e.opacity))
              .Append(",\"sc\":").Append(N(e.scale))
              .Append(",\"rot\":").Append(N(e.rotation))
              .Append(",\"asp\":").Append(N(e.aspect))
              .Append(",\"bw\":").Append(N(e.borderW))
              .Append(",\"hl\":").Append(N(e.halo))
              .Append(",\"sbg\":").Append(Q(Hex(e.bg)))
              .Append(",\"sln\":").Append(Q(Hex(e.line)))
              .Append(",\"dbg\":").Append(Q(Hex(e.bgDown)))
              .Append(",\"dln\":").Append(Q(Hex(e.lineDown)))
              .Append(",\"label\":").Append(Q(e.label ?? ""))
              .Append(",\"sk\":").Append(e.skillId)
              .Append(",\"ssd\":").Append(e.stickStd ? 1 : 0)
              .Append(",\"ku\":").Append((int)e.upKey)
              .Append(",\"kd\":").Append((int)e.downKey)
              .Append(",\"kl\":").Append((int)e.leftKey)
              .Append(",\"kr\":").Append((int)e.rightKey)
              .Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    public static void FromJson(string json)
    {
        if (!string.IsNullOrEmpty(json)) ParseElements(json);
    }

    static bool ParseElements(string json)
    {
        try
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) return false;
            var list = root.TryGetValue("elements", out var o) ? o as List<object> : null;
            if (list == null) return false;
            var parsed = new List<VCElem>();
            foreach (var it in list)
            {
                var d = it as Dictionary<string, object>;
                if (d == null) continue;
                var e = new VCElem();
                if (d.TryGetValue("t", out var t)) System.Enum.TryParse(t as string, out e.type);
                if (d.TryGetValue("a", out var a)) System.Enum.TryParse(a as string, out e.action);
                if (d.TryGetValue("k", out var kk) && kk is double kd && System.Enum.IsDefined(typeof(KeyCode), (int)kd))
                    e.key = (KeyCode)(int)kd;
                if (d.TryGetValue("sm", out var sm)) e.shiftMod = ToF(sm, 0f) > 0.5f;
                if (d.TryGetValue("x", out var x)) e.cx = ToF(x, e.cx);
                if (d.TryGetValue("y", out var y)) e.cy = ToF(y, e.cy);
                if (d.TryGetValue("s", out var s)) e.size = ToF(s, e.size);
                if (d.TryGetValue("shape", out var sh)) e.shape = Mathf.RoundToInt(ToF(sh, 0f));
                if (d.TryGetValue("op", out var op)) e.opacity = ToF(op, e.opacity);
                if (d.TryGetValue("sc", out var sc)) e.scale = ToF(sc, e.scale);
                if (d.TryGetValue("rot", out var rot)) e.rotation = ToF(rot, e.rotation);
                if (d.TryGetValue("asp", out var asp)) e.aspect = ToF(asp, e.aspect);
                if (d.TryGetValue("bw", out var bw)) e.borderW = ToF(bw, e.borderW);
                if (d.TryGetValue("hl", out var hl)) e.halo = ToF(hl, e.halo);
                if (d.TryGetValue("sbg", out var sbg)) e.bg = ParseHex(sbg as string, e.bg);
                if (d.TryGetValue("sln", out var sln)) e.line = ParseHex(sln as string, e.line);
                if (d.TryGetValue("dbg", out var dbg)) e.bgDown = ParseHex(dbg as string, e.bgDown);
                if (d.TryGetValue("dln", out var dln)) e.lineDown = ParseHex(dln as string, e.lineDown);
                if (!d.ContainsKey("sbg"))
                {
                    // 旧存档没有样式字段：套一组和旧渲染接近的高对比默认值，避免升级后按键"看不见"
                    e.bg = new Color(0f, 0f, 0f, 0.45f);
                    e.line = new Color(1f, 1f, 1f, 0.82f);
                    e.bgDown = new Color(1f, 1f, 1f, 0.26f);
                    e.lineDown = new Color(1f, 1f, 1f, 0.95f);
                }
                if (d.TryGetValue("label", out var lb)) e.label = lb as string ?? "";
                if (d.TryGetValue("sk", out var skId)) e.skillId = Mathf.Max(0, Mathf.RoundToInt(ToF(skId, 0f)));

                // 摇杆输入模式：没有 ssd 字段 = 老存档，按「标准输入」处理（和以前行为一致）
                if (d.TryGetValue("ssd", out var ssd)) e.stickStd = ToF(ssd, 1f) > 0.5f;
                if (d.TryGetValue("ku", out var ku) && ku is double kud && System.Enum.IsDefined(typeof(KeyCode), (int)kud)) e.upKey = (KeyCode)(int)kud;
                if (d.TryGetValue("kd", out var kdn) && kdn is double kdd && System.Enum.IsDefined(typeof(KeyCode), (int)kdd)) e.downKey = (KeyCode)(int)kdd;
                if (d.TryGetValue("kl", out var kl) && kl is double kld && System.Enum.IsDefined(typeof(KeyCode), (int)kld)) e.leftKey = (KeyCode)(int)kld;
                if (d.TryGetValue("kr", out var krr) && krr is double krd && System.Enum.IsDefined(typeof(KeyCode), (int)krd)) e.rightKey = (KeyCode)(int)krd;

                // 老存档的描边是 1dp：放到现在的渲染里只有一根发丝，统一抬到 2.2dp。
                // 想要更细完全可以在「样式设置 → 描边宽度」里调回去，所以这里放心迁移。
                if (e.borderW <= 1.05f) e.borderW = 2.2f;

                parsed.Add(e);
            }
            elems = parsed;
            return true;
        }
        catch { return false; }
    }

    static float ToF(object o, float fallback)
        => o is double d && !double.IsNaN(d) ? (float)d : fallback;

    public static void SaveActive()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(ProfilePath(activeName), ToJson());
            if (!profileNames.Contains(activeName)) profileNames.Add(activeName);
            WriteManifest();
        }
        catch { /* 同上 */ }
    }

    public static void NewProfile(string name)
    {
        if (string.IsNullOrEmpty(name)) name = "方案" + (profileNames.Count + 1);
        string uniq = name; int n = 2;
        while (profileNames.Contains(uniq)) uniq = name + "_" + n++;
        SaveActive();                       // 先落盘当前方案
        activeName = uniq;
        elems = new List<VCElem>();
        BuildDefaultLayout();               // 新方案给一份默认布局，用户再改
        SaveActive();
    }

    public static void DeleteProfile(string name)
    {
        if (profileNames.Count <= 1) return;
        profileNames.Remove(name);
        try { if (File.Exists(ProfilePath(name))) File.Delete(ProfilePath(name)); } catch { }
        if (activeName == name)
        {
            activeName = profileNames[0];
            if (!LoadFromFile(activeName)) { elems.Clear(); BuildDefaultLayout(); }
        }
        WriteManifest();
    }

    public static void RestoreDefaultLayout()
    {
        elems.Clear();
        BuildDefaultLayout();
        SaveActive();
    }

    // —————————— 默认布局（第一次进入游戏就能玩；尺寸按拇指触控下限放大） ——————————
    static void BuildDefaultLayout()
    {
        // 左摇杆（移动/换层/爬绳）
        var stick = new VCElem { type = VCElemType.Stick, cx = 0.085f, cy = 0.70f, size = 0.40f, opacity = 0.6f };
        ApplyStickSkin(stick);
        elems.Add(stick);
        // 右侧四格技能（对准快捷格 1-4，点了等于按对应键）
        elems.Add(new VCElem { type = VCElemType.Button, action = GameAction.Slot1, cx = 0.630f, cy = 0.76f, size = 0.19f, opacity = 0.75f });
        elems.Add(new VCElem { type = VCElemType.Button, action = GameAction.Slot2, cx = 0.755f, cy = 0.665f, size = 0.19f, opacity = 0.75f });
        elems.Add(new VCElem { type = VCElemType.Button, action = GameAction.Slot3, cx = 0.880f, cy = 0.57f, size = 0.19f, opacity = 0.75f });
        elems.Add(new VCElem { type = VCElemType.Button, action = GameAction.Slot4, cx = 0.945f, cy = 0.40f, size = 0.19f, opacity = 0.75f });
        // 跳跃（Alt 的屏上替身）
        elems.Add(new VCElem { type = VCElemType.Button, action = GameAction.Jump, cx = 0.545f, cy = 0.60f, size = 0.17f, opacity = 0.75f });
        // 拾取（Z）
        elems.Add(new VCElem { type = VCElemType.Button, action = GameAction.Pickup, cx = 0.42f, cy = 0.72f, size = 0.15f, opacity = 0.65f });
    }

    // —————————— 绑定目录（编辑器给按钮选动作用） ——————————
    public static readonly GameAction[] BindableActions =
    {
        GameAction.Jump, GameAction.Attack, GameAction.Pickup,
        GameAction.Slot1, GameAction.Slot2, GameAction.Slot3, GameAction.Slot4,
        GameAction.Slot5, GameAction.Slot6, GameAction.Slot7, GameAction.Slot8,
        GameAction.Equip, GameAction.Inven, GameAction.Stat, GameAction.Skill,
        GameAction.KeySet, GameAction.QuickSlot, GameAction.Menu,
        GameAction.Shop, GameAction.Shortcut, GameAction.Help,
    };

    public static string ActionName(VCElem e)
    {
        if (e.type != VCElemType.Button) return "移动";
        if (e.key != KeyCode.None) return KeyLabel(e.key);
        return ActionDisplayName(e.action);
    }

    // 键名显示：数字键给 "0".."9"，小键盘给 "小键盘 1"，其余沿用 KeyMap.Label
    public static string KeyLabel(KeyCode k)
    {
        if (k == KeyCode.None) return "未绑定";
        switch (k)
        {
            case KeyCode.BackQuote: return "`";
            case KeyCode.Minus: return "-";
            case KeyCode.Equals: return "=";
            case KeyCode.LeftBracket: return "[";
            case KeyCode.RightBracket: return "]";
            case KeyCode.Backslash: return "\\";
            case KeyCode.Semicolon: return ";";
            case KeyCode.Quote: return "'";
            case KeyCode.Comma: return ",";
            case KeyCode.Period: return ".";
            case KeyCode.Slash: return "/";
            case KeyCode.CapsLock: return "Caps";
            case KeyCode.Return: return "Enter";
            case KeyCode.Backspace: return "Bksp";
            case KeyCode.Tab: return "Tab";
        }
        if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
        if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "小键盘" + (int)(k - KeyCode.Keypad0);
        var s = KeyMap.Label(k);
        return string.IsNullOrEmpty(s) ? k.ToString() : s;
    }

    // 该键在当前键位表里对应哪个游戏动作（没有则 null）——编辑器用它显示"这个键会触发什么"
    public static GameAction? ActionOfKey(KeyCode k)
    {
        if (k == KeyCode.None) return null;
        foreach (var kv in KeyMap.All)
            if (kv.Value == k) return kv.Key;
        return null;
    }

    public static int SlotOfKey(KeyCode k)
    {
        if (k == KeyCode.None) return -1;
        for (int i = 0; i < QuickSlots.Count; i++)
            if (KeyMap.Of(QuickSlots.Action(i)) == k) return i;
        return -1;
    }

    public static string ActionDisplayName(GameAction action)
    {
        switch (action)
        {
            case GameAction.Jump: return "跳跃";
            case GameAction.Attack: return "攻击";
            case GameAction.Pickup: return "拾取";
            case GameAction.Equip: return "装备";
            case GameAction.Inven: return "背包";
            case GameAction.Stat: return "能力值";
            case GameAction.Skill: return "技能";
            case GameAction.KeySet: return "键盘设置";
            case GameAction.QuickSlot: return "快捷栏";
            case GameAction.Menu: return "菜单";
            case GameAction.Shop: return "商店";
            case GameAction.Shortcut: return "快捷键";
            case GameAction.Help: return "帮助";
            default:
                int slot = QuickSlots.Index(action);
                return slot >= 0 ? "技能格 " + (slot + 1) : action.ToString();
        }
    }

    // —————————— 每帧：触摸 / 鼠标 → 注入 ——————————
    // 由 InputBridge.Update 调用（早于游戏读 AxisX/Y）。
    // 桌面端没有触摸，必须同样支持鼠标：按在虚拟键上 = 按下该键，
    // 否则鼠标点击会直接漏到下面的 HUD/快捷格（"点了虚拟键却触发了鼠标点击"）。
    public static void Tick(bool feed)
    {
        Ensure();
        UpdateLift();

        // 门控关闭（大厅/暂停/编辑器/键位窗口）：全部松开，别把状态带进下一局
        if (!feed)
        {
            for (int i = elems.Count - 1; i >= 0; i--)
                if (elems[i].finger >= 0) Release(elems[i]);
            if (anyPushed)
            {
                foreach (var e in elems)
                    if (e.type == VCElemType.Button && e.held)
                        SendButton(e, false);
                anyPushed = false;
            }
            InputHub.ClearVirtualAxis();
            BlockingPointer = false;
            wasOnControl = false;
            return;
        }

        bool touched = Input.touchCount > 0;
        if (touched)
        {
            // 鼠标按住状态下改用触摸：先把鼠标那个"手指"松开，避免卡键
            var stale = FindFinger(MouseFinger);
            if (stale != null) Release(stale);

            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                var p = new Vector2(t.position.x / UiKit.Scale, (Screen.height - t.position.y) / UiKit.Scale);
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        if (FindFinger(t.fingerId) != null) break;
                        var e = HitElement(p);
                        if (e != null) { e.finger = t.fingerId; e.held = true; OnPress(e, p); }
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        var m = FindFinger(t.fingerId);
                        if (m != null) OnDrag(m, p);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        var r = FindFinger(t.fingerId);
                        if (r != null) Release(r);
                        break;
                }
            }
        }
        else
        {
            // 鼠标（桌面）：按下/拖动/抬起，同一套元素状态机。
            // 坐标直接取鼠标像素坐标——Pointer.Pos 在触摸设备上可能是轨迹板光标，不能混用。
            var mp = ToGui(Input.mousePosition);
            if (Input.GetMouseButtonDown(0))
            {
                var hit = HitElement(mp);
                if (hit != null) { hit.finger = MouseFinger; hit.held = true; OnPress(hit, mp); }
            }
            else if (Input.GetMouseButton(0))
            {
                var m = FindFinger(MouseFinger);
                if (m != null) OnDrag(m, mp);
            }
            else
            {
                // 按键抬起 / 手指全离开：兜底释放，防粘键
                for (int i = elems.Count - 1; i >= 0; i--)
                    if (elems[i].finger >= 0) Release(elems[i]);
            }
        }

        // —— 汇总移动轴（摇杆模拟量 + 十字键数字量，叠加后截断） ——
        // 自定义摇杆不参与：InputHub.AxisX/Y 一旦有模拟量就会盖掉键盘按键，
        // 那样用户选的 W/A/S/D 会被这层轴吃掉、等于没绑。让它只走键盘通道。
        float mx = 0f, my = 0f;
        foreach (var e in elems)
        {
            if (e.finger < 0 || e.type == VCElemType.Button) continue;
            if (e.type == VCElemType.Stick && !e.stickStd) continue;
            mx += e.axis.x; my += e.axis.y;
        }
        InputHub.SetVirtualAxis(Mathf.Clamp(mx, -1f, 1f), Mathf.Clamp(my, -1f, 1f));

        // 这次点击已经被虚拟控件吃掉：HUD/底部栏不要再响应同一帧的点击
        bool onControl = HitElement(touched ? Pointer.Pos : ToGui(Input.mousePosition)) != null;
        if (touched)
            for (int i = 0; i < Input.touchCount && !onControl; i++)
                onControl = HitElement(ToGui(Input.GetTouch(i).position)) != null;
        BlockingPointer = onControl || wasOnControl;
        wasOnControl = AnyHeld();
    }

    static Vector2 ToGui(Vector2 screenPos)
        => new Vector2(screenPos.x / UiKit.Scale, (Screen.height - screenPos.y) / UiKit.Scale);

    const int MouseFinger = -2;

    // 指针正停在虚拟控件上（或正按住）：HUD 的点击门控读它，避免一次点击双重触发
    public static bool BlockingPointer { get; private set; }
    static bool wasOnControl;

    // 虚拟层此刻是否真的接管输入（游戏中、没开编辑器/键位窗口、没隐藏）
    public static bool Capturing
    {
        get
        {
            var g = GameLoop.I;
            return g != null && g.state == GameLoop.GameState.Playing
                   && OverlayVisible && !VirtualControlsEditor.Visible && !KeyConfigWindow.Visible;
        }
    }

    // 屏幕坐标（像素）是否落在某个虚拟控件上——Pointer 用它把手势让给虚拟层
    public static bool HitsAtScreen(Vector2 screenPos)
    {
        if (!Capturing) return false;
        Ensure();
        var p = new Vector2(screenPos.x / UiKit.Scale, (Screen.height - screenPos.y) / UiKit.Scale);
        return HitElement(p) != null;
    }

    static bool AnyHeld()
    {
        for (int i = 0; i < elems.Count; i++)
            if (elems[i].finger >= 0) return true;
        return false;
    }

    static bool anyPushed;

    static VCElem FindFinger(int id)
    {
        for (int i = 0; i < elems.Count; i++)
            if (elems[i].finger == id) return elems[i];
        return null;
    }

    static VCElem HitElement(Vector2 p)
    {
        // 后画的优先（列表尾部优先），与绘制顺序一致
        for (int i = elems.Count - 1; i >= 0; i--)
            if (elems[i].Hit(p)) return elems[i];
        return null;
    }

    // 屏幕坐标（像素）下命中的虚拟控件；没有则 null。技能窗拖放绑定用这个。
    public static VCElem HitAtScreen(Vector2 screenPos)
    {
        Ensure();
        var p = new Vector2(screenPos.x / UiKit.Scale, (Screen.height - screenPos.y) / UiKit.Scale);
        return HitElement(p);
    }

    // 把技能绑到一个虚拟按键上（技能窗拖到按键即调用）。
    // 只接受 BUTTON 元素：摇杆/十字是方向输入，绑技能没有语义。
    public static bool TryBindSkill(Vector2 guiPos, int skillId)
    {
        Ensure();
        var e = HitElement(guiPos);
        if (e == null || e.type != VCElemType.Button) return false;
        var data = SkillBook.Get(skillId);
        if (data == null) return false;
        if (data.IsPassive) { BottomBar.Toast("被动技能不能绑定到按键"); return true; }
        if (SkillProgress.LevelOf(skillId) <= 0) { BottomBar.Toast("先给技能加点，才能绑到按键"); return true; }

        bool replaced = e.skillId > 0 && e.skillId != skillId;
        e.skillId = skillId;
        SaveActive();
        BottomBar.Toast((replaced ? "已替换为 " : "已绑定 ") + data.name + " → " + ElementCaption(e));
        return true;
    }

    public static void UnbindSkill(VCElem e)
    {
        if (e == null || e.skillId == 0) return;
        e.skillId = 0;
        SaveActive();
    }

    // 按键的短名（提示/面板用）：优先技能名，其次键名，最后动作名
    public static string ElementCaption(VCElem e)
    {
        if (e != null && e.skillId > 0)
        {
            var d = SkillBook.Get(e.skillId);
            if (d != null) return d.name;
        }
        return e == null ? "" : ActionName(e);
    }

    static void OnPress(VCElem e, Vector2 p)
    {
        switch (e.type)
        {
            case VCElemType.Button:
                anyPushed = true;
                e.held = true;                       // 状态自洽：释放一定走得到释放逻辑
                SendButton(e, true);
                break;
            case VCElemType.Stick:
                UpdateStick(e, p);
                break;
            case VCElemType.Dpad:
                UpdateDpad(e, p);
                break;
        }
    }

    // 绑了键就发键；没绑键（旧布局 / 技能格这类默认无键的动作）就直接按动作注入，
    // 否则 SetVirtual(None) 是空操作 —— 表现为"点按钮没反应，只触发了鼠标点击"。
    //
    // 实际生效的动作：按键若能映射到动作，以键为准（用户改了游戏键位也自动跟随）；
    // 否则用元素自己记录的动作。两种情况都会直接注入"动作"，不依赖键位表是否有键。
    public static GameAction EffectiveAction(VCElem e)
        => ActionOfKey(e.Key) ?? e.action;

    static void SendButton(VCElem e, bool down)
    {
        // 绑了技能的按键直接施放（原端"技能拖进快捷格，再按键出手"的屏上等价物）。
        // 这里必须 return：继续往下发动作会让快捷格那条通道再放一次，
        // 表现就是一次点击连放两个技能、第二个报"技能冷却中"。
        if (e.skillId > 0)
        {
            if (down)
            {
                string why;
                bool ok = SkillCast.TryCast(e.skillId, out why);
                var data = SkillBook.Get(e.skillId);
                lastPress = (data != null ? data.name : "技能") + (ok ? "" : " ✗");
                lastNote = ok ? "" : (why ?? "");
                pressMsgUntil = Time.unscaledTime + 1.2f;
            }
            return;
        }

        var act = EffectiveAction(e);
        InputHub.SetVirtualAction(act, down);          // 动作永远发，保证点了就有反应
        // 显式绑了键的按钮 = 那个物理键的屏上替身：无条件把键也发一遍。
        // 只靠「键 → 动作」反查是不够的——比如攻击从 Ctrl 改到 C 后，
        // 快捷格、技能栏这些按 KeyCode 判定的通道拿不到 C，就会出现
        // 「按了 C 按钮，浏览器端动作没触发」的情况。
        if (e.shiftMod) InputHub.SetVirtual(KeyCode.LeftShift, down);
        if (e.key != KeyCode.None) InputHub.SetVirtual(e.key, down);

        if (down)
        {
            lastPress = (e.key != KeyCode.None ? KeyLabel(e.key) + " → " : "") + ActionDisplayName(act);
            lastNote = e.key == KeyCode.None
                ? "按动作触发"
                : ActionOfKey(e.key) == null ? "键未映射 · 按动作触发" : "";
            pressMsgUntil = Time.unscaledTime + 1.2f;
            Debug.Log("[Virtual] 按下 " + (e.key == KeyCode.None ? "无键" : e.key.ToString()) +
                      " → " + act + "（攻击键位=" + KeyMap.Of(GameAction.Attack) + "）");
        }
    }

    // F9 调试面板用：最近一次虚拟键按下
    static string lastPress = "-";
    static string lastNote = "";

    public static bool ShowDebug;

    static void OnDrag(VCElem e, Vector2 p)
    {
        if (e.type == VCElemType.Stick) UpdateStick(e, p);
        else if (e.type == VCElemType.Dpad) UpdateDpad(e, p);
    }

    static void UpdateStick(VCElem e, Vector2 p)
    {
        var c = Center(e);
        float r = e.R;
        var off = p - c;
        float mag = off.magnitude;
        if (mag > r) off = off / mag * r;          // 夹到基座内
        float dz = 0.14f;                           // 死区
        float k = mag / r;
        k = k <= dz ? 0f : (k - dz) / (1f - dz);
        // GUI 坐标 y 轴向下，而 InputHub.AxisY 的约定是「上 = +1」，
        // 这里把 y 翻一次符号，否则摇杆上下颠倒（往上推 = 下爬/下跳）。
        var dir = new Vector2(off.x, -off.y);
        e.axis = mag < 0.001f ? Vector2.zero : dir.normalized * Mathf.Clamp01(k);
        SyncStickKeys(e);
    }

    // 自定义模式：把模拟轴翻译成四个方向的键盘按键。
    // 用「分量阈值」而不是取主方向，斜推时可以同时按下两个键（和模拟器一致）。
    // 轴本身照常输出——游戏读 AxisX/Y 的通道不受影响，读键盘的通道这才有了键。
    const float StickKeyGate = 0.38f;

    static void SyncStickKeys(VCElem e)
    {
        if (e.type != VCElemType.Stick || e.stickStd) return;
        PressDir(e.upKey, e.axis.y > StickKeyGate);
        PressDir(e.downKey, e.axis.y < -StickKeyGate);
        PressDir(e.leftKey, e.axis.x < -StickKeyGate);
        PressDir(e.rightKey, e.axis.x > StickKeyGate);
    }

    static void PressDir(KeyCode k, bool down)
    {
        if (k != KeyCode.None) InputHub.SetVirtual(k, down);
    }

    // 切回「标准输入」或正在改映射时调用：把可能还按着的四方向键全部抬起，避免粘键
    public static void ReleaseStickKeys(VCElem e)
    {
        if (e == null) return;
        PressDir(e.upKey, false);
        PressDir(e.downKey, false);
        PressDir(e.leftKey, false);
        PressDir(e.rightKey, false);
    }

    static void UpdateDpad(VCElem e, Vector2 p)
    {
        var c = Center(e);
        var off = p - c;
        float r = e.R;
        float dz = r * 0.22f;
        float dx = 0f, dy = 0f;
        if (off.x < -dz) dx = -1f; else if (off.x > dz) dx = 1f;
        // 同摇杆：GUI y 向下 → 翻符号，保证「上 = +1」
        if (off.y > dz) dy = -1f; else if (off.y < -dz) dy = 1f;
        e.axis = new Vector2(dx, dy);
    }

    static void Release(VCElem e)
    {
        e.finger = -1;
        e.axis = Vector2.zero;
        SyncStickKeys(e);                        // 轴清零后这里会把四方向的键全部抬起，不粘键
        if (e.type == VCElemType.Button && e.held)
        {
            e.held = false;
            SendButton(e, false);
        }
    }

    // 编辑器删除元素前调用，防止把按下状态带进坟墓
    public static void ReleaseForEdit(VCElem e) => Release(e);

    // —————————— 绘制（GameHUD.OnGUI 调用，UiKit.Begin 之后） ——————————
    public static void Draw()
    {
        Ensure();
        var g = GameLoop.I;
        bool inGame = g != null && g.state == GameLoop.GameState.Playing;
        if (!inGame || VirtualControlsEditor.Visible || !OverlayVisible) return;

        // 键盘设定窗口打开时不再整层消失：降到半透明照常画出来，
        // 这样用户在"显示键盘"的同时仍能看见自己的屏上按键（也才能把技能拖上去）。
        // 此时 VirtualControls.Tick 拿到的 feed=false，按键只显示、不注入。
        float dim = KeyConfigWindow.Visible ? 0.42f : 1f;

        foreach (var e in elems) DrawElement(e, dim);
        if (KeyConfigWindow.Visible) return;

        DrawEditToggle();
        DrawPressToast();

        if (Input.GetKeyDown(KeyCode.F9)) ShowDebug = !ShowDebug;
        if (ShowDebug) DrawDebug();
    }

    // —————————— 拖技能时的可放置目标 ——————————
    // 技能窗拖起图标后，每个按钮亮一圈蓝环；指针悬停的那个换成黄环。
    // 这一层要画在浮窗之后（GameHUD 在 GameWindows.DrawAll 之后调用），
    // 否则被键盘设定窗口盖住的按键就没人看得见 —— 那正是"拖不进去"的由来。
    public static void DrawDropTargets()
    {
        if (!SkillWindow.Dragging) return;
        Ensure();

        foreach (var e in elems)
        {
            if (e.type != VCElemType.Button) continue;
            var c = Center(e);
            bool hover = e.Hit(Pointer.Pos);
            float pulse = 0.55f + 0.30f * Mathf.Sin(Time.unscaledTime * 7f);
            if (hover)
                DrawAnnulus(c, e.R - 2f, e.R + 10f, new Color(1f, 0.86f, 0.32f, 0.95f), false);
            else
                DrawAnnulus(c, e.R + 1f, e.R + 7f, new Color(0.35f, 0.88f, 1f, pulse), false);
        }

        // 拖拽图标重画一遍，保证压在窗口/按键之上
        var d = SkillBook.Get(SkillWindow.DragSkillId);
        var icon = d != null && !string.IsNullOrEmpty(d.icon) ? Art.Tex(d.icon) : null;
        if (icon == null) return;
        float s = 32f;
        GUI.DrawTexture(new Rect(Pointer.Pos.x - s * 0.5f, Pointer.Pos.y - s * 0.5f, s, s),
            icon, ScaleMode.ScaleToFit);
    }

    // 按下虚拟键时在顶部提示 1.2 秒：能立刻区分"没收到按下"和"收到了但游戏没反应"
    static float pressMsgUntil;

    static void DrawPressToast()
    {
        if (Time.unscaledTime > pressMsgUntil) return;
        bool twoLine = !string.IsNullOrEmpty(lastNote);
        float w = 380f, h = twoLine ? 54f : 34f;
        var r = new Rect(UiKit.W * 0.5f - w * 0.5f, 58f, w, h);
        RoundedRect(r, Color.black, 0.72f);
        var st = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
        st.normal.textColor = new Color(0.75f, 1f, 0.95f);
        GUI.Label(new Rect(r.x, twoLine ? r.y + 6f : r.y, r.width, 24f), "虚拟键 → " + lastPress, st);
        if (twoLine)
        {
            var sub = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            sub.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(r.x, r.y + 28f, r.width, 20f), lastNote, sub);
        }
    }

    // F9：把每个虚拟键的真实状态摊在屏幕上（键名/映射动作/是否按下/游戏是否收到）
    static void DrawDebug()
    {
        float h = 28f + (elems.Count + 1) * 20f + 24f;
        var r = new Rect(10f, UiKit.H * 0.30f, 420f, h);
        UiKit.Fill(r, new Color(0f, 0f, 0f, 0.72f));
        var st = new GUIStyle { fontSize = 13 };
        st.normal.textColor = Color.white;
        GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 18f), "虚拟键调试（F9 关闭）", st);
        float y = r.y + 26f;
        foreach (var e in elems)
        {
            if (e.type != VCElemType.Button)
            {
                GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 18f),
                    (e.type == VCElemType.Stick ? "摇杆 " : "十字 ") + e.axis.ToString("0.00"), st);
                y += 20f;
                continue;
            }
            var eff = EffectiveAction(e);
            var mapped = ActionOfKey(e.Key);
            string line = KeyLabel(e.Key) +
                          " | 实际:" + ActionDisplayName(eff) +
                          (mapped.HasValue ? "(键映射)" : "(" + ActionDisplayName(e.action) + ")") +
                          (e.held ? " | 按下" : "") +
                          (InputHub.Held(eff) ? " | 游戏收到✓" : "");
            GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 18f), line, st);
            y += 20f;
        }
        GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 18f),
            "攻击键位=" + KeyLabel(KeyMap.Of(GameAction.Attack)) +
            " | 最近：" + lastPress, st);
    }

    public static void DrawElement(VCElem e) => DrawElement(e, 1f);

    public static void DrawElement(VCElem e, float dim)
    {
        bool active = e.type == VCElemType.Button ? e.held : e.finger >= 0;
        float a = Mathf.Clamp(e.opacity, 0.05f, 1f) * Mathf.Clamp01(dim);
        var bgc = active ? e.bgDown : e.bg;
        var lnc = active ? e.lineDown : e.line;
        bgc.a *= a;
        lnc.a *= a;

        var c = Center(e);
        float r = e.BaseR;

        // 旋转 / 缩放 / 纵横比例 全部交给矩阵，形状本身只用局部坐标画
        var m = GUI.matrix;
        GUI.matrix = m * Matrix4x4.TRS(new Vector3(c.x, c.y, 0f),
            Quaternion.Euler(0f, 0f, e.rotation),
            new Vector3(Mathf.Max(0.05f, e.aspect) * Mathf.Max(0.05f, e.scale), Mathf.Max(0.05f, e.scale), 1f));

        // 技能键的冷却遮罩要在形状之后、图标之前铺，才不会盖住描边
        if (e.type == VCElemType.Stick)
        {
            DrawStick(e, r, bgc, lnc, active, a);
        }
        else if (e.type == VCElemType.Dpad)
        {
            float sw = Mathf.Max(e.borderW, r * 0.032f);
            if (e.halo > 0.004f) { var g = lnc; g.a *= 0.45f; DrawAnnulus(Vector2.zero, r + sw * 0.5f, r + sw * 0.5f + e.halo * r, g, true); }
            FillDisc(Vector2.zero, Mathf.Max(0.1f, r - sw * 0.5f), bgc);
            DrawAnnulus(Vector2.zero, Mathf.Max(0f, r - sw * 0.5f), r + sw * 0.5f, lnc, false);
            float cap = r * 0.30f;
            DrawDirCap(new Vector2(0f, -r * 0.62f), cap, e.axis.y > 0f, bgc, lnc);
            DrawDirCap(new Vector2(0f, r * 0.62f), cap, e.axis.y < 0f, bgc, lnc);
            DrawDirCap(new Vector2(-r * 0.62f, 0f), cap, e.axis.x < 0f, bgc, lnc);
            DrawDirCap(new Vector2(r * 0.62f, 0f), cap, e.axis.x > 0f, bgc, lnc);
        }
        else
        {
            DrawShape(e.shape, r, bgc, lnc, e.borderW, e.halo);

            var rect = new Rect(-r, -r, r * 2f, r * 2f);
            var icon = SlotIcon(e);
            if (icon != null)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
                GUI.color = old;
                DrawCooldown(e, rect, a);
            }
            else
            {
                string text = ElementText(e);
                if (!string.IsNullOrEmpty(text))
                {
                    // 字占直径约 30%，两个以上字符按长度回缩，避免顶到圆边上
                    float fs = r * 0.60f;
                    int n = text.Length;
                    if (n >= 2) fs *= n == 2 ? 0.86f : n == 3 ? 0.68f : 0.52f;
                    ShadowLabel(rect, text, Mathf.Max(9, Mathf.RoundToInt(fs)));
                }
            }
        }

        GUI.matrix = m;
    }

    // 技能键的冷却遮罩：复用技能窗导出的 16 帧 cool_*.png，
    // 和底部快捷格、技能列表的遮罩是同一套素材，读起来一致。
    static void DrawCooldown(VCElem e, Rect rect, float a)
    {
        if (e.skillId <= 0) return;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        var sys = player != null ? player.GetComponent<SkillSystem>() : null;
        if (sys == null) return;
        float left = sys.RemainingById(e.skillId);
        if (left <= 0.001f) return;
        float total = Mathf.Max(0.01f, sys.CooldownOf(e.skillId));
        int frame = Mathf.Clamp(Mathf.RoundToInt((1f - Mathf.Clamp01(left / total)) * 15f), 0, 15);
        var cool = Art.Tex("ui/skillwin/cool_" + frame + ".png");
        if (cool == null) return;
        var old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(rect, cool);
        GUI.color = old;
    }

    // —————————— 摇杆 ——————————
    // 参考图的分层：外发光 → 深色底盘 → 稍亮的内盘 → 内圈细线 → 外环 → 中心球钮。
    // 五层都在"局部坐标"里画（调用方已经把缩放/旋转塞进 GUI.matrix），
    // 所以缩放滑块一改，整套外观等比跟着变，不用另算。
    static void DrawStick(VCElem e, float r, Color bg, Color line, bool active, float op)
    {
        float sw = Mathf.Max(e.borderW, r * 0.022f);
        float ro = r + sw * 0.5f;
        bool hasLine = line.a > 0.004f;

        // ① 外发光：从外环外沿向外 t² 渐隐
        if (e.halo > 0.004f && hasLine)
        {
            var g = line; g.a *= 0.38f;
            DrawAnnulus(Vector2.zero, ro, ro + e.halo * r, g, true);
        }

        // ② 底盘
        FillDisc(Vector2.zero, Mathf.Max(0.1f, r - sw * 0.5f), bg);

        // ③ 内盘：中心 78% 半径提一点亮度，摇杆才有纵向的"凹槽"层次
        float ri = r * 0.78f;
        FillDisc(Vector2.zero, ri, new Color(1f, 1f, 1f, 0.055f * op));

        // ④ 内圈细线（比外环淡一档，不抢主轮廓）
        if (hasLine)
        {
            var g = line; g.a *= 0.40f;
            float iw = Mathf.Max(1.2f, sw * 0.5f);
            DrawAnnulus(Vector2.zero, Mathf.Max(0f, ri - iw * 0.5f), ri + iw * 0.5f, g, false);
        }

        // ⑤ 外环
        if (hasLine) DrawAnnulus(Vector2.zero, Mathf.Max(0f, r - sw * 0.5f), ro, line, false);

        // ⑥ 中心球钮：闲置时居中，按住时跟着手指走
        float kr = r * 0.40f;
        var knob = e.finger >= 0 ? new Vector2(e.axis.x, -e.axis.y) * (r - kr) : Vector2.zero;

        // 投影先铺：球压上去只在下沿露一线，做出厚度（比纯描边像"实体"）
        FillDisc(knob + new Vector2(0f, kr * 0.12f), kr * 1.02f, new Color(0f, 0f, 0f, 0.22f * op));
        FillBall(knob, kr, active ? new Color(0.74f, 0.78f, 0.82f, op) : new Color(0.60f, 0.63f, 0.66f, op));

        if (hasLine)
        {
            var g = line; g.a *= 0.32f;
            DrawAnnulus(knob, Mathf.Max(0f, kr - 1.2f), kr + 1.2f, g, false);
        }
    }

    static void FillBall(Vector2 c, float r, Color col)
    {
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), BallTex());
        GUI.color = old;
    }

    // shape: 0 圆 · 1 矩 · 2 多边形 · 3 LT · 4 LB · 5 RT · 6 RB · 7 无
    public static void DrawShape(int shape, float r, Color bg, Color line, float bw, float halo)
    {
        if (shape == 7) return;                       // 无：只留热区，不画任何东西

        // 描边下限：1dp 在小按键上只有一根发丝，放大到 3 倍屏就彻底看不见了。
        // 这里按半径给保底（约 3.2% 半径），用户把描边调到很小也不会"消失"。
        float w = Mathf.Max(bw, r * 0.032f);
        bool hasLine = line.a > 0.004f && w > 0.05f;
        bool hasHalo = halo > 0.004f && hasLine;

        if (shape == 1) { DrawRectShape(r, bg, line, w, halo, hasLine, hasHalo); return; }
        if (shape >= 2) { DrawPolyShape(shape, r, bg, line, w, halo, hasLine, hasHalo); return; }

        // —— 圆形：光晕 / 底 / 描边环，三块几何上互不重叠（半透明色也不会叠出一圈深边）
        if (hasHalo)
        {
            var g = line; g.a *= 0.45f;
            float ri = r + w * 0.5f;
            DrawAnnulus(Vector2.zero, ri, ri + halo * r, g, true);   // 外沿渐隐，才有发光感
        }
        FillDisc(Vector2.zero, Mathf.Max(0.1f, r - w * 0.5f), bg);
        if (hasLine) DrawAnnulus(Vector2.zero, Mathf.Max(0f, r - w * 0.5f), r + w * 0.5f, line, false);
    }

    // —————————— 矩形 ——————————
    // 圆角矩形：SoftTex 自带 8% 圆角 → 圆角半径 = 边长 * 0.16
    // 描边 = 4 条直边 + 4 段圆弧。旧版只画了直边，圆角处留着四个豁口，
    // 放大后非常明显（"按钮画得糙"的一大来源）。
    static void DrawRectShape(float r, Color bg, Color line, float w, float halo, bool hasLine, bool hasHalo)
    {
        float rad = r * 0.16f;

        if (hasHalo)
        {
            var g = line; g.a *= 0.22f;
            float e = halo * r;
            SoftRect(new Rect(-r - e, -r - e, (r + e) * 2f, (r + e) * 2f), g);
            g.a *= 1.3f;
            float e2 = e * 0.45f;
            SoftRect(new Rect(-r - e2, -r - e2, (r + e2) * 2f, (r + e2) * 2f), g);
        }

        SoftRect(new Rect(-r, -r, r * 2f, r * 2f), bg);
        if (!hasLine) return;

        float h = Mathf.Max(0f, r - rad);
        DrawLine(new Vector2(-h, -r), new Vector2(h, -r), line, w);
        DrawLine(new Vector2(-h, r), new Vector2(h, r), line, w);
        DrawLine(new Vector2(-r, -h), new Vector2(-r, h), line, w);
        DrawLine(new Vector2(r, -h), new Vector2(r, h), line, w);

        int seg = Mathf.Clamp(Mathf.CeilToInt(rad * 1.2f), 4, 20);
        Arc(new Vector2(-h, -h), rad, Mathf.PI, Mathf.PI * 1.5f, seg, line, w);
        Arc(new Vector2(h, -h), rad, Mathf.PI * 1.5f, Mathf.PI * 2f, seg, line, w);
        Arc(new Vector2(h, h), rad, 0f, Mathf.PI * 0.5f, seg, line, w);
        Arc(new Vector2(-h, h), rad, Mathf.PI * 0.5f, Mathf.PI, seg, line, w);
    }

    // 圆弧（GUI 坐标 y 向下，角度按 sin 取反理解即可）
    static void Arc(Vector2 c, float rad, float a0, float a1, int seg, Color col, float w)
    {
        var prev = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rad;
        for (int i = 1; i <= seg; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)seg);
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
            DrawLine(prev, p, col, w);
            prev = p;
        }
    }

    // —————————— 多边形 ——————————
    static void DrawPolyShape(int shape, float r, Color bg, Color line, float w, float halo, bool hasLine, bool hasHalo)
    {
        var pts = ShapePts(shape, r);

        if (hasHalo)
        {
            var g = line; g.a *= 0.22f;
            FillPoly(OffsetPoly(pts, w * 0.5f + halo * r), g);
        }

        FillPoly(pts, bg);
        if (!hasLine) return;

        // 转角处补一个同色圆点当 round-join：比 miter 尖角圆润，也不会有缝
        for (int i = 0; i < pts.Length; i++)
        {
            DrawLine(pts[i], pts[(i + 1) % pts.Length], line, w);
            FillDisc(pts[i], w * 0.5f, line);
        }
    }

    // 凸多边形等距外扩（d>0）/ 内缩（d<0）
    // 法线朝向用"边中点 → 质心"判定，避免顶点顺时针/逆时针不同导致内外翻。
    public static Vector2[] OffsetPoly(Vector2[] pts, float d)
    {
        int n = pts.Length;
        var o = new Vector2[n];
        var cen = Vector2.zero;
        for (int i = 0; i < n; i++) cen += pts[i];
        cen /= n;

        for (int i = 0; i < n; i++)
        {
            var p = pts[i];
            var prev = pts[(i - 1 + n) % n];
            var next = pts[(i + 1) % n];

            var n0 = OutNormal(prev, p, cen);
            var n1 = OutNormal(p, next, cen);

            var bis = n0 + n1;
            if (bis.sqrMagnitude < 1e-4f) { o[i] = p + n0 * d; continue; }
            bis.Normalize();
            float cos = Mathf.Max(0.30f, Vector2.Dot(bis, n0));   // 限制 miter 长度
            o[i] = p + bis * (d / cos);
        }
        return o;
    }

    static Vector2 OutNormal(Vector2 a, Vector2 b, Vector2 cen)
    {
        var e = (b - a).normalized;
        var nrm = new Vector2(-e.y, e.x);
        if (Vector2.Dot(nrm, cen - a) > 0f) nrm = -nrm;   // 指向质心的是内法线
        return nrm;
    }

    // 形状顶点（局部坐标，半径 r）
    public static Vector2[] ShapePts(int shape, float r)
    {
        if (shape == 2)
        {
            var hex = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float ang = Mathf.PI * 0.5f + i * Mathf.PI / 3f;
                hex[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
            }
            return hex;
        }

        // LT / LB / RT / RB：宽扁梯形，其中一个角切 45°
        float hh = r * 0.62f, cut = r * 0.55f;
        switch (shape)
        {
            case 3: return new[] { new Vector2(-r + cut, -hh), new Vector2(r, -hh), new Vector2(r, hh), new Vector2(-r, hh), new Vector2(-r, -hh + cut) };
            case 4: return new[] { new Vector2(-r, -hh), new Vector2(r, -hh), new Vector2(r, hh), new Vector2(-r + cut, hh), new Vector2(-r, hh - cut) };
            case 5: return new[] { new Vector2(-r, -hh), new Vector2(r - cut, -hh), new Vector2(r, -hh + cut), new Vector2(r, hh), new Vector2(-r, hh) };
            default: return new[] { new Vector2(-r, -hh), new Vector2(r, -hh), new Vector2(r, hh - cut), new Vector2(r - cut, hh), new Vector2(-r, hh) };
        }
    }

    // 扫描线填充凸多边形（局部坐标，跟随 GUI.matrix）
    // 注意：步进必须 ≥ 条高。旧版 0.5 步进 + 1px 条高会重叠 50%，
    // 半透明填充叠两遍就变深，边缘还会出现一条更实的"描边感"。
    public static void FillPoly(Vector2[] pts, Color col)
    {
        if (pts == null || pts.Length < 3 || col.a <= 0.004f) return;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < pts.Length; i++)
        {
            if (pts[i].y < minY) minY = pts[i].y;
            if (pts[i].y > maxY) maxY = pts[i].y;
        }

        var old = GUI.color;
        GUI.color = col;
        int y0 = Mathf.FloorToInt(minY), y1 = Mathf.CeilToInt(maxY);
        for (int yi = y0; yi <= y1; yi++)
        {
            float y = yi + 0.5f;
            if (y < minY || y > maxY) continue;

            float xa = float.MaxValue, xb = float.MinValue;
            for (int i = 0; i < pts.Length; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % pts.Length];
                if (Mathf.Approximately(p0.y, p1.y)) continue;
                if (y < Mathf.Min(p0.y, p1.y) || y > Mathf.Max(p0.y, p1.y)) continue;
                float t = (y - p0.y) / (p1.y - p0.y);
                float x = p0.x + (p1.x - p0.x) * t;
                if (x < xa) xa = x;
                if (x > xb) xb = x;
            }
            if (xb < xa) continue;

            // 主条带 + 左右各 1px 的半透明补丁（亚像素端点，斜边不再是硬台阶）
            if (xb - xa > 2f)
            {
                GUI.DrawTexture(new Rect(xa + 1f, yi, xb - xa - 2f, 1f), Texture2D.whiteTexture);
                var half = new Color(1f, 1f, 1f, 0.5f);
                var keep = GUI.color;
                GUI.color = keep * half;
                GUI.DrawTexture(new Rect(xa, yi, 1.2f, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(xb - 1.2f, yi, 1.2f, 1f), Texture2D.whiteTexture);
                GUI.color = keep;
            }
            else GUI.DrawTexture(new Rect(xa, yi, Mathf.Max(1f, xb - xa), 1f), Texture2D.whiteTexture);
        }
        GUI.color = old;
    }

    public static void StrokePoly(Vector2[] pts, Color col, float w)
    {
        if (pts == null || pts.Length < 2) return;
        for (int i = 0; i < pts.Length; i++)
            DrawLine(pts[i], pts[(i + 1) % pts.Length], col, w);
    }

    static Texture2D SlotIcon(VCElem e)
    {
        if (e.type != VCElemType.Button) return null;
        // ① 直接绑在按键上的技能（技能窗拖进来） ② 退回到"这个键对应的快捷格"
        int id = e.skillId;
        if (id <= 0)
        {
            int slot = SlotOfKey(e.Key);
            if (slot < 0) return null;
            id = QuickSlots.Id(slot);
        }
        if (id <= 0) return null;
        var data = SkillBook.Get(id);
        return data != null && !string.IsNullOrEmpty(data.icon) ? Art.Tex(data.icon) : null;
    }

    static string ButtonLabel(VCElem e)
    {
        // 没绑键的按钮（技能格这类动作默认无键）显示动作名，而不是"未绑定"
        if (e.key == KeyCode.None) return ActionDisplayName(e.action);
        var l = KeyLabel(e.key);
        if (string.IsNullOrEmpty(l)) l = ActionName(e);
        return e.shiftMod ? "⇧" + l : l;
    }

    // 控件上显示什么：自定义文字 > 绑定的键 > 动作名（编辑时不显示动作名，保持"所见即所配"）
    static string ElementText(VCElem e)
    {
        if (!string.IsNullOrEmpty(e.label)) return e.label;
        // 技能键：有图标就不写字，图标缺失时退回技能名，至少还能认出来
        if (e.skillId > 0)
        {
            var d = SkillBook.Get(e.skillId);
            return d != null ? d.name : "";
        }
        if (e.key != KeyCode.None) return ButtonLabel(e);
        return VirtualControlsEditor.Visible ? "" : ActionDisplayName(e.action);
    }

    static void DrawDirCap(Vector2 c, float r, bool active, Color bg, Color line)
    {
        Ring(c, r, line, Mathf.Max(1f, r * 0.10f));
        FillDisc(c, Mathf.Max(1f, r - r * 0.10f), active ? line : bg);
    }

    static void FillDisc(Vector2 c, float r, Color col)
    {
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), DiscTex());
        GUI.color = old;
    }

    // 小圆角矩形（矩形形状用）：SoftTex 的 8% 圆角刚好
    static void SoftRect(Rect r, Color col)
    {
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(r, SoftTex());
        GUI.color = old;
    }

    // —————————— 文字 ——————————
    // 项目字体 Fonts/ui.ttf 是 Arial Unicode MS **Regular**（单字重、无 Bold face），
    // FontStyle.Bold 在 Android/IL2CPP 上拿不到粗体字形，行为并不可靠。
    // 所以"加粗"用同色左右各偏移半像素重绘一次实现：视觉上增重约 1px，字宽只增加 1px。
    static readonly Dictionary<int, GUIStyle> _styles = new Dictionary<int, GUIStyle>();

    static GUIStyle Style(int size, TextAnchor align)
    {
        int key = (size << 4) | ((int)align & 0xF);
        if (_styles.TryGetValue(key, out var st) && st != null) return st;
        st = new GUIStyle
        {
            alignment = align,
            fontSize = Mathf.Max(6, size),
            richText = false,
            clipping = TextClipping.Overflow,
            wordWrap = false,
        };
        _styles[key] = st;
        return st;
    }

    // weight: 0 常规 · 1 半粗（2 遍）· 2 粗（4 遍）
    public static void Label(Rect r, string s, int size, Color col, TextAnchor align, int weight = 1)
    {
        if (string.IsNullOrEmpty(s)) return;
        var st = Style(size, align);
        st.normal.textColor = col;

        if (weight <= 0) { GUI.Label(r, s, st); return; }

        float d = Mathf.Clamp(size * 0.030f, 0.6f, 1.6f);
        GUI.Label(new Rect(r.x - d * 0.5f, r.y, r.width, r.height), s, st);
        GUI.Label(new Rect(r.x + d * 0.5f, r.y, r.width, r.height), s, st);
        if (weight >= 2)
        {
            GUI.Label(new Rect(r.x, r.y - d * 0.5f, r.width, r.height), s, st);
            GUI.Label(new Rect(r.x, r.y + d * 0.5f, r.width, r.height), s, st);
        }
    }

    // 按键上的字：深色描边 + 伪粗白字，压在任意背景上都读得清
    static void ShadowLabel(Rect r, string text, int size)
    {
        var st = Style(size, TextAnchor.MiddleCenter);

        float o = Mathf.Clamp(size * 0.055f, 1f, 2.2f);
        st.normal.textColor = new Color(0f, 0f, 0f, 0.72f);
        GUI.Label(new Rect(r.x, r.y + o, r.width, r.height), text, st);
        GUI.Label(new Rect(r.x - o, r.y, r.width, r.height), text, st);
        GUI.Label(new Rect(r.x + o, r.y, r.width, r.height), text, st);
        GUI.Label(new Rect(r.x, r.y - o, r.width, r.height), text, st);

        Label(r, text, size, Color.white, TextAnchor.MiddleCenter, 1);
    }

    // —— 圆 / 圆角方 贴图（运行时生成一次） ——
    static Texture2D _disc, _round;

    public static Texture2D DiscTex()
    {
        if (_disc != null) return _disc;
        _disc = MakeAlphaTex((x, y, n) =>
        {
            float rr = n * 0.5f - 1f;
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(n * 0.5f - 0.5f, n * 0.5f - 0.5f));
            return Mathf.Clamp01(rr - d + 0.5f);
        });
        return _disc;
    }

    public static Texture2D RoundTex()
    {
        if (_round != null) return _round;
        _round = MakeAlphaTex((x, y, n) =>
        {
            float rad = n * 0.22f;
            float bx = Mathf.Clamp(x, rad, n - rad), by = Mathf.Clamp(y, rad, n - rad);
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(bx, by));
            return Mathf.Clamp01(rad - d + 0.5f);
        });
        return _round;
    }

    // 小圆角（面板/卡片/输入框用）：0.22 的圆角在大矩形上会被拉成"面包"形，这里用 0.08
    static Texture2D _soft;
    public static Texture2D SoftTex()
    {
        if (_soft != null) return _soft;
        _soft = MakeAlphaTex((x, y, n) =>
        {
            float rad = n * 0.08f;
            float bx = Mathf.Clamp(x, rad, n - rad), by = Mathf.Clamp(y, rad, n - rad);
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(bx, by));
            return Mathf.Clamp01(rad - d + 0.5f);
        });
        return _soft;
    }

    // —— 球体贴图（摇杆中心钮）——
    // 只烧明暗、不带颜色（最终由 GUI.color 着色），球体的体积感全靠这张图。
    // 光源固定在左上，和界面其他部分的"上亮下暗"一致。
    static Texture2D _ball;
    public static Texture2D BallTex()
    {
        if (_ball != null) return _ball;
        const int n = 192;
        var t = new Texture2D(n, n, TextureFormat.ARGB32, false);
        var px = new Color[n * n];
        float cc = n * 0.5f - 0.5f;
        float rr = n * 0.5f - 1f;
        var L = new Vector3(-0.42f, -0.54f, 0.73f).normalized;   // GUI 的 y 向下 → 负 y 才是"上"
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = (x - cc) / rr, dy = (y - cc) / rr;
                float dd = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(rr * (1f - dd) + 0.5f);   // 圆的 1 纹素抗锯齿
                if (a <= 0.002f) { px[y * n + x] = new Color(1f, 1f, 1f, 0f); continue; }

                float z = Mathf.Sqrt(Mathf.Max(0f, 1f - dd * dd));
                float lam = Mathf.Clamp01(dx * L.x + dy * L.y + z * L.z);
                float v = 0.56f + 0.44f * lam;                    // 环境光 + 漫反射
                v += Mathf.Pow(lam, 26f) * 0.09f;                 // 左上高光（弱一点，才像哑光）
                v *= 0.88f + 0.12f * z;                           // 轮廓压暗一点，边缘才不"发飘"
                v = Mathf.Clamp01(v);
                px[y * n + x] = new Color(v, v, v, a);
            }
        }
        t.SetPixels(px);
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        _ball = t;
        return _ball;
    }

    delegate float AlphaFn(int x, int y, int n);

    // 贴图分辨率：128 放大会糊、边缘发虚；256 在手机上又够清楚又只占 256KB/张
    const int TexN = 256;

    static Texture2D MakeAlphaTex(AlphaFn fn) => MakeAlphaTexN(TexN, fn);

    static Texture2D MakeAlphaTexN(int n, AlphaFn fn)
    {
        var t = new Texture2D(n, n, TextureFormat.ARGB32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(fn(x, y, n)));
        t.SetPixels(px);
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    // —— 描线贴图（1×16 竖向羽化）——
    // DrawLine 是把 1×1 白点拉到 (长 × 宽)，上下边缘永远是硬锯齿；
    // 换成带渐隐的竖条后，线的两侧就有了 1.5 个纹素的过渡，圆环/图标/描边都不再毛刺。
    static Texture2D _line;
    public static Texture2D LineTex()
    {
        if (_line != null) return _line;
        const int n = 16;
        var t = new Texture2D(1, n, TextureFormat.ARGB32, false);
        var px = new Color[n];
        for (int y = 0; y < n; y++)
        {
            float d = Mathf.Min(y + 0.5f, n - 0.5f - y);
            px[y] = new Color(1f, 1f, 1f, Mathf.Clamp01(d / 1.5f));
        }
        t.SetPixels(px);
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return _line = t;
    }

    static void Disc(Vector2 c, float r, Color tint, float alpha, bool outline = false)
    {
        if (outline)
        {
            // 简单描边：画一圈略大的深色，再画内部原色 —— 这里用两次 Disc 近似
            Ring(c, r, new Color(1f, 1f, 1f, 0.5f * alpha + 0.15f));
            return;
        }
        var old = GUI.color;
        GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), DiscTex());
        GUI.color = old;
    }

    // 圆环：直接用 SDF 生成的环贴图，不再用 N 段折线拼
    // （折线在粗环/大半径下能明显看出多边形，段与段之间还会有缝）
    static void Ring(Vector2 c, float r, Color col, float w = 0f)
    {
        if (w <= 0f) w = Mathf.Max(1.5f, r * 0.05f);
        DrawAnnulus(c, Mathf.Max(0f, r - w * 0.5f), r + w * 0.5f, col, false);
    }

    // —————————— 抗锯齿圆环 / 外发光 ——————————
    // 贴图按 (内半径, 外半径) 量化缓存，绘制时把贴图铺到 [-ro, ro] 的方框里。
    // 一张 160×160 的 ARGB32 ≈ 100KB，缓存上限 24 张 ≈ 2.4MB。
    static readonly Dictionary<int, Texture2D> _annulus = new Dictionary<int, Texture2D>();
    static readonly List<int> _annulusKeys = new List<int>();
    const int AnnulusN = 160;
    const int AnnulusKeep = 24;
    const float AnnulusQ = 64f;          // 半径量化精度：1/64

    public static void DrawAnnulus(Vector2 c, float ri, float ro, Color col, bool fade)
    {
        if (col.a <= 0.003f || ro <= 0.004f) return;

        int roQ = Mathf.Clamp(Mathf.RoundToInt(ro * AnnulusQ), 1, 255);
        int riQ = Mathf.Clamp(Mathf.RoundToInt(ri * AnnulusQ), 0, roQ);
        float roq = roQ / AnnulusQ;

        var tex = AnnulusTex(riQ, roQ, fade);
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - roq, c.y - roq, roq * 2f, roq * 2f), tex);
        GUI.color = old;
    }

    static Texture2D AnnulusTex(int riQ, int roQ, bool fade)
    {
        int key = (riQ << 12) | (roQ << 2) | (fade ? 1 : 0);
        if (_annulus.TryGetValue(key, out var cached) && cached != null) return cached;

        float ro = roQ / AnnulusQ;
        float ri = Mathf.Min(riQ / AnnulusQ, ro * 0.995f);
        bool inside = ri > 0.002f;
        float band = Mathf.Max(0.0001f, ro - ri);

        var tex = MakeAlphaTexN(AnnulusN, (x, y, n) =>
        {
            // 贴图坐标 → r=1 单位
            float px = ((x + 0.5f) / n - 0.5f) * 2f * ro;
            float py = ((y + 0.5f) / n - 0.5f) * 2f * ro;
            float d = Mathf.Sqrt(px * px + py * py);
            float aa = ro * 1.15f / (n * 0.5f);                 // 约一个纹素宽

            float a = Mathf.Clamp01((ro - d) / aa);             // 外沿
            if (inside) a = Mathf.Min(a, Mathf.Clamp01((d - ri) / aa));   // 内沿
            if (fade)
            {
                float t = Mathf.Clamp01((ro - d) / band);       // 越往外越淡
                a *= t * t;
            }
            return a;
        });

        if (_annulusKeys.Count >= AnnulusKeep)
        {
            int oldKey = _annulusKeys[0];
            _annulusKeys.RemoveAt(0);
            if (_annulus.TryGetValue(oldKey, out var oldTex) && oldTex != null) Object.Destroy(oldTex);
            _annulus.Remove(oldKey);
        }
        _annulus[key] = tex;
        _annulusKeys.Add(key);
        return tex;
    }

    static void RoundedRect(Rect r, Color tint, float alpha)
    {
        var old = GUI.color;
        GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
        GUI.DrawTexture(r, RoundTex());
        GUI.color = old;
    }

    public static void DrawLine(Vector2 a, Vector2 b, Color col, float width)
    {
        var d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;
        if (col.a <= 0.002f) return;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        var m = GUI.matrix;   // 当前矩阵已含 UiKit 缩放，右乘即仍在虚拟坐标系内
        GUI.matrix = m * Matrix4x4.TRS(new Vector3(a.x, a.y, 0f),
            Quaternion.Euler(0f, 0f, ang), new Vector3(len, width, 1f));
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(0f, -0.5f, 1f, 1f), LineTex());
        GUI.matrix = m;
        GUI.color = old;
    }

    // —————————— 编辑入口按钮（统一走 Pointer：桌面=鼠标，与全部 HUD 按钮同一套输入） ——————————
    static void DrawEditToggle()
    {
        var r = new Rect(UiKit.W - 60f, 12f, 48f, 48f);
        bool hover = r.Contains(Pointer.Pos);
        float a = Pointer.Down && hover ? 0.62f : hover ? 0.45f : 0.30f;
        RoundedRect(r, Color.black, a);
        ShadowLabel(r, "键", 20);
        // 按下与抬起都要在这个按钮上，且未滑动，才算一次点击（防误触）
        if (Event.current.type == EventType.Repaint && Pointer.Released && hover
            && r.Contains(Pointer.PressPosition))
            VirtualControlsEditor.Toggle();
        if (Input.GetKeyDown(KeyCode.F8))
            VirtualControlsEditor.Toggle();
    }

    // —————————— JSON 小工具 ——————————
    static string Q(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    static string N(float v)
        => v.ToString("R", CultureInfo.InvariantCulture);

    // —————————— 颜色 <-> #AARRGGBB（编辑面板显示用，和 Winlator 一致） ——————————
    public static string Hex(Color c)
    {
        uint a = (uint)Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f);
        uint r = (uint)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f);
        uint g = (uint)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f);
        uint b = (uint)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
        return "#" + ((a << 24) | (r << 16) | (g << 8) | b).ToString("X8");
    }

    public static Color ParseHex(string s, Color fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        var t = s.Trim().TrimStart('#');
        try
        {
            if (t.Length == 6)
                return new Color(Hx(t, 0) / 255f, Hx(t, 2) / 255f, Hx(t, 4) / 255f, 1f);
            if (t.Length == 8)
                return new Color(Hx(t, 2) / 255f, Hx(t, 4) / 255f, Hx(t, 6) / 255f, Hx(t, 0) / 255f);
        }
        catch { /* 非法值退回原色 */ }
        return fallback;
    }

    static int Hx(string s, int at)
        => int.Parse(s.Substring(at, 2), NumberStyles.HexNumber);
}
