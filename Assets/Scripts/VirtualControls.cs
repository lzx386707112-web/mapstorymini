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
    public float cx = 0.5f, cy = 0.5f;           // 归一化中心（×UiKit.W / ×UiKit.H）
    public float size = 0.14f;                   // 直径，归一化到 UiKit.H
    public int shape = 0;                        // Button: 0 圆形 1 圆角方
    public float opacity = 0.55f;                // 0.15~1
    public string label = "";                    // 空则自动取键名/技能图标

    // —— 运行时状态，不序列化 ——
    public int finger = -1;                      // 占用的手指 id
    public Vector2 axis;                         // Stick/Dpad 本帧输出
    public bool held;                            // Button 是否按住

    // 实际注入的键：优先 key，旧存档没有 key 时退回动作当前键位
    public KeyCode Key => key != KeyCode.None ? key : KeyMap.Of(action);

    public float R => size * 0.5f * UiKit.H;     // 屏幕像素半径（虚拟坐标）
    public Rect Rect
    {
        get
        {
            float r = R;
            return new Rect(cx * UiKit.W - r, cy * UiKit.H - r, r * 2f, r * 2f);
        }
    }

    public VCElem Clone()
    {
        return new VCElem { type = type, action = action, key = key, cx = cx, cy = cy,
            size = size, shape = shape, opacity = opacity, label = label };
    }

    public bool Hit(Vector2 p)
    {
        // 触摸命中半径放宽 1.35 倍，拇指定位比鼠标难
        float r = R * 1.35f;
        var c = new Vector2(cx * UiKit.W, cy * UiKit.H);
        return (p - c).sqrMagnitude <= r * r;
    }
}

public static class VirtualControls
{
    public static readonly Color Teal = new Color(0.06f, 0.66f, 0.60f);   // Winlator 主题色

    public static List<VCElem> elems = new List<VCElem>();
    public static string activeName = "默认";
    public static List<string> profileNames = new List<string>();
    public static bool OverlayVisible = true;

    const string DirName = "vcontrols";
    static bool loaded;

    static string Dir => Path.Combine(Application.persistentDataPath, DirName);
    static string ManifestPath => Path.Combine(Dir, "manifest.json");
    static string ProfilePath(string name) => Path.Combine(Dir, name + ".json");

    // —————————— 载入 ——————————
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
            var root = MiniJson.Parse(File.ReadAllText(ProfilePath(name))) as Dictionary<string, object>;
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
                if (d.TryGetValue("x", out var x)) e.cx = ToF(x, e.cx);
                if (d.TryGetValue("y", out var y)) e.cy = ToF(y, e.cy);
                if (d.TryGetValue("s", out var s)) e.size = ToF(s, e.size);
                if (d.TryGetValue("shape", out var sh)) e.shape = Mathf.RoundToInt(ToF(sh, 0f));
                if (d.TryGetValue("op", out var op)) e.opacity = ToF(op, e.opacity);
                if (d.TryGetValue("label", out var lb)) e.label = lb as string ?? "";
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
            var sb = new StringBuilder();
            sb.Append("{\"elements\":[");
            for (int i = 0; i < elems.Count; i++)
            {
                var e = elems[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"t\":").Append(Q(e.type.ToString()))
                  .Append(",\"a\":").Append(Q(e.action.ToString()))
                  .Append(",\"k\":").Append((int)e.key)
                  .Append(",\"x\":").Append(N(e.cx))
                  .Append(",\"y\":").Append(N(e.cy))
                  .Append(",\"s\":").Append(N(e.size))
                  .Append(",\"shape\":").Append(e.shape)
                  .Append(",\"op\":").Append(N(e.opacity))
                  .Append(",\"label\":").Append(Q(e.label ?? ""))
                  .Append('}');
            }
            sb.Append("]}");
            File.WriteAllText(ProfilePath(activeName), sb.ToString());
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
        elems.Add(new VCElem { type = VCElemType.Stick, cx = 0.085f, cy = 0.70f, size = 0.40f, opacity = 0.6f });
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
        float mx = 0f, my = 0f;
        foreach (var e in elems)
            if (e.finger >= 0 && e.type != VCElemType.Button) { mx += e.axis.x; my += e.axis.y; }
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
        var act = EffectiveAction(e);
        InputHub.SetVirtualAction(act, down);          // 动作永远发，保证点了就有反应
        if (e.key != KeyCode.None && ActionOfKey(e.key) == null)
            InputHub.SetVirtual(e.key, down);          // 游戏未映射的裸键：额外发一次

        if (down)
        {
            lastPress = (e.key != KeyCode.None ? KeyLabel(e.key) + " → " : "") + ActionDisplayName(act);
            lastNote = e.key == KeyCode.None
                ? "按动作触发"
                : ActionOfKey(e.key) == null ? "键未映射 · 按动作触发" : "";
            pressMsgUntil = Time.unscaledTime + 1.2f;
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
        var c = new Vector2(e.cx * UiKit.W, e.cy * UiKit.H);
        float r = e.R;
        var off = p - c;
        float mag = off.magnitude;
        if (mag > r) off = off / mag * r;          // 夹到基座内
        float dz = 0.14f;                           // 死区
        float k = mag / r;
        k = k <= dz ? 0f : (k - dz) / (1f - dz);
        e.axis = mag < 0.001f ? Vector2.zero : off.normalized * Mathf.Clamp01(k);
    }

    static void UpdateDpad(VCElem e, Vector2 p)
    {
        var c = new Vector2(e.cx * UiKit.W, e.cy * UiKit.H);
        var off = p - c;
        float r = e.R;
        float dz = r * 0.22f;
        float dx = 0f, dy = 0f;
        if (off.x < -dz) dx = -1f; else if (off.x > dz) dx = 1f;
        if (off.y > dz) dy = 1f; else if (off.y < -dz) dy = -1f;
        e.axis = new Vector2(dx, dy);
    }

    static void Release(VCElem e)
    {
        e.finger = -1;
        e.axis = Vector2.zero;
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
        if (KeyConfigWindow.Visible) return;

        foreach (var e in elems) DrawElement(e);
        DrawEditToggle();
        DrawPressToast();

        if (Input.GetKeyDown(KeyCode.F9)) ShowDebug = !ShowDebug;
        if (ShowDebug) DrawDebug();
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

    public static void DrawElement(VCElem e)
    {
        // Winlator 键帽风格：深色半透明底 + 白描边 + 白字；按住时描边变青色
        float o = Mathf.Clamp(e.opacity, 0.15f, 1f);
        bool active = e.type == VCElemType.Button ? e.held : e.finger >= 0;
        var c = new Vector2(e.cx * UiKit.W, e.cy * UiKit.H);
        var border = active ? Teal : new Color(1f, 1f, 1f, 0.85f * o);
        var fill = new Color(0f, 0f, 0f, 0.45f * o);
        const float bw = 2.5f;

        if (e.type == VCElemType.Stick)
        {
            Ring(c, e.R, border);
            FillDisc(c, e.R - bw, fill);
            var knob = c + (e.finger >= 0 ? e.axis * e.R : Vector2.zero);
            float kr = e.R * 0.42f;
            Ring(knob, kr, active ? Teal : new Color(1f, 1f, 1f, 0.9f * o));
            FillDisc(knob, kr - bw, fill);
            return;
        }
        if (e.type == VCElemType.Dpad)
        {
            Ring(c, e.R, border);
            FillDisc(c, e.R - bw, fill);
            float cap = e.R * 0.30f;
            DrawDirCap(c + Vector2.up * e.R * 0.62f, cap, e.axis.y > 0f, o);
            DrawDirCap(c + Vector2.down * e.R * 0.62f, cap, e.axis.y < 0f, o);
            DrawDirCap(c + Vector2.left * e.R * 0.62f, cap, e.axis.x < 0f, o);
            DrawDirCap(c + Vector2.right * e.R * 0.62f, cap, e.axis.x > 0f, o);
            return;
        }

        // Button
        var rect = e.Rect;
        if (e.shape == 1)
        {
            RoundedRect(rect, Color.white, border.a);   // 外层当描边
            var inner = new Rect(rect.x + bw, rect.y + bw, rect.width - bw * 2f, rect.height - bw * 2f);
            RoundedRect(inner, Color.black, fill.a);
        }
        else
        {
            Ring(c, e.R, border);
            FillDisc(c, e.R - bw, fill);
        }

        // 技能图标优先（绑定到快捷格且已放技能时）
        var icon = SlotIcon(e);
        if (icon != null) GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        else
        {
            string text = !string.IsNullOrEmpty(e.label) ? e.label : ButtonLabel(e);
            if (!string.IsNullOrEmpty(text)) ShadowLabel(rect, text, Mathf.RoundToInt(e.R * 0.5f));
        }
    }

    static Texture2D SlotIcon(VCElem e)
    {
        if (e.type != VCElemType.Button) return null;
        int slot = SlotOfKey(e.Key);
        if (slot < 0) return null;
        int id = QuickSlots.Id(slot);
        if (id <= 0) return null;
        var data = SkillBook.Get(id);
        return data != null && !string.IsNullOrEmpty(data.icon) ? Art.Tex(data.icon) : null;
    }

    static string ButtonLabel(VCElem e)
    {
        // 没绑键的按钮（技能格这类动作默认无键）显示动作名，而不是"未绑定"
        if (e.key == KeyCode.None) return ActionDisplayName(e.action);
        var l = KeyLabel(e.key);
        return string.IsNullOrEmpty(l) ? ActionName(e) : l;
    }

    static void DrawDirCap(Vector2 c, float r, bool active, float o)
    {
        Ring(c, r, active ? Teal : new Color(1f, 1f, 1f, 0.8f * o));
        FillDisc(c, Mathf.Max(1f, r - 2f), new Color(0f, 0f, 0f, 0.4f * o));
    }

    static void FillDisc(Vector2 c, float r, Color col)
    {
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), DiscTex());
        GUI.color = old;
    }

    static void ShadowLabel(Rect r, string text, int size)
    {
        var st = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(9, size) };
        st.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        var r2 = new Rect(r.x + 1, r.y + 1, r.width, r.height);
        GUI.Label(r2, text, st);
        st.normal.textColor = Color.white;
        GUI.Label(r, text, st);
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

    delegate float AlphaFn(int x, int y, int n);

    static Texture2D MakeAlphaTex(AlphaFn fn)
    {
        int n = 128;
        var t = new Texture2D(n, n, TextureFormat.ARGB32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = new Color(1f, 1f, 1f, fn(x, y, n));
        t.SetPixels(px);
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
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

    static void Ring(Vector2 c, float r, Color col)
    {
        int seg = 40;
        float w = Mathf.Max(1.5f, r * 0.05f);
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
            var p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * (r - w * 0.5f);
            var p1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * (r - w * 0.5f);
            DrawLine(p0, p1, col, w);
        }
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
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        var m = GUI.matrix;   // 当前矩阵已含 UiKit 缩放，右乘即仍在虚拟坐标系内
        GUI.matrix = m * Matrix4x4.TRS(new Vector3(a.x, a.y, 0f),
            Quaternion.Euler(0f, 0f, ang), new Vector3(len, width, 1f));
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(0f, -0.5f, 1f, 1f), Texture2D.whiteTexture);
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
}
