using System.Collections.Generic;
using UnityEngine;

// ————————————————————————————————————————————————————————————
// 输入总入口。
//
// 原版是纯键盘操作：方向键走路、Ctrl 攻击、Alt 跳跃、快捷栏用
// Shift/Insert/Home/PgUp/Ctrl/Del/End 这一排、E/I/S/K 开面板。
// 这里把「动作」和「按键」解耦：游戏逻辑只问 InputHub.Held(Jump)，
// 不关心这个动作是物理键盘按的、还是屏幕上的虚拟键按的。
//
// ————————————————————————————————————————————————————————————

public enum GameAction
{
    Left, Right, Up, Down,
    Jump, Attack, Pickup,
    Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8,
    Equip, Inven, Stat, Skill,
    KeySet, QuickSlot,
    Menu, Shop, Shortcut, Help,
    // Append to preserve existing serialized enum values.
    Slot9, Slot10, Slot11, Slot12, Slot13, Slot14, Slot15, Slot16, Slot17,
    Slot18, Slot19, Slot20, Slot21, Slot22, Slot23, Slot24, Slot25, Slot26,
    Buddy, WorldMap, Chat, Minimap, Quest, ChatAll, ChatWhisper, ChatParty, ChatBuddy, Interface, ChatRoom, Family, ChatFamily, Party, Messenger, ChatSpouse, MonsterBook, ChatAlliance, PartySearch, School, Medal, Sit, NpcTalk, FaceHit, FaceSmile, FaceTroubled, FaceCry, FaceAngry, FaceBewildered, FaceStunned,
}

public static class KeyMap
{
    // 原版默认键位（v83）：
    //   移动 = 方向键，跳跃 = Alt，攻击 = Ctrl，拾取 = Z
    //   快捷栏八格 = Shift / Insert / Home / PgUp / Ctrl / Delete / End / PgDn
    //   装备 E、背包 I、能力值 S、技能 K
    //   功能与表情完整复用 KeymapDao 的源端 40 项默认表；F1～F7 不再被帮助等功能占用。
    // 可在键盘设定窗口拖动图标重绑。
    static readonly Dictionary<GameAction, KeyCode> defaults = BuildDefaults();
    static Dictionary<GameAction, KeyCode> BuildDefaults()
    {
        var result = new Dictionary<GameAction, KeyCode> {
            { GameAction.Left, KeyCode.LeftArrow }, { GameAction.Right, KeyCode.RightArrow },
            { GameAction.Up, KeyCode.UpArrow }, { GameAction.Down, KeyCode.DownArrow },
            { GameAction.Menu, KeyCode.Escape }, { GameAction.QuickSlot, KeyCode.None },
            { GameAction.Help, KeyCode.None }
        };
        foreach (var entry in NativeKeyActions.Entries) result[entry.action] = KeyCode.None;
        for (int i = 0; i < KeymapDao.DefaultKeys.Length; i++)
            if (NativeKeyActions.Find(KeymapDao.DefaultTypes[i], KeymapDao.DefaultActions[i], out var action))
                result[action] = NativeKeyLayout.FromScanCode(KeymapDao.DefaultKeys[i]);
        for (int i = 0; i < QuickSlots.Count; i++) result[QuickSlots.Action(i)] = KeyCode.None;
        return result;
    }

    static readonly Dictionary<GameAction, KeyCode> bind = new Dictionary<GameAction, KeyCode>();
    static bool loaded;

    public static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        for (int i = 8; i < QuickSlots.Count; i++) defaults[QuickSlots.Action(i)] = KeyCode.None;
        foreach (var kv in defaults)
        {
            int saved = PlayerPrefs.GetInt("mxd.key." + kv.Key, (int)kv.Value);
            var kc = (KeyCode)saved;
            // KeyCode 里 0 是 None；存档被写坏时退回默认，避免整局按不动
            bind[kv.Key] = System.Enum.IsDefined(typeof(KeyCode), kc) ? NativeKeyLayout.FunctionKey(kc) : kv.Value;
        }
        // 开机打一行键位表：真机上排查「按键/虚拟键没反应」时，先确认动作到底绑在哪个键
        var sb = new System.Text.StringBuilder("[KeyMap] ");
        foreach (var kv in bind)
            if (kv.Value != KeyCode.None)
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
        Debug.Log(sb.ToString());
    }

    static readonly List<KeymapEntry> unmapped = new List<KeymapEntry>();

    public static void LoadCharacter(List<KeymapEntry> entries)
    {
        Ensure();
        bind.Clear();
        foreach (var pair in defaults) bind[pair.Key] = pair.Value;
        foreach (var entry in NativeKeyActions.Entries) bind[entry.action] = KeyCode.None;
        for (int i = 0; i < QuickSlots.Count; i++) bind[QuickSlots.Action(i)] = KeyCode.None;
        var skills = new int[QuickSlots.Count];
        int skillSlot = 0;
        unmapped.Clear();
        foreach (var entry in entries)
        {
            var key = NativeKeyLayout.FromScanCode(entry.key);
            if (key != KeyCode.None && NativeKeyActions.Find(entry.type, entry.action, out var action))
                Claim(action, key);
            else if (key != KeyCode.None && entry.type == 1 && skillSlot < QuickSlots.Count)
            {
                Claim(QuickSlots.Action(skillSlot), key);
                skills[skillSlot++] = entry.action;
            }
            else unmapped.Add(entry);
        }
        QuickSlots.LoadCharacter(skills);
    }

    // 一个键只服务一个动作（原端 keymap 以扫描码为唯一键）。
    // 读档时若同一扫描码出现两次，后读到的那个动作拿走键，旧主人清成 None；
    // 否则这个键会同时派发两个动作，聊天框一抢焦点后面所有动作都停摆。
    static void Claim(GameAction action, KeyCode key)
    {
        if (key == KeyCode.None) { bind[action] = key; return; }
        foreach (var other in new List<GameAction>(bind.Keys))
            if (other != action && bind[other] == key) bind[other] = KeyCode.None;
        bind[action] = key;
    }

    public static List<KeymapEntry> ExportCharacter()
    {
        Ensure();
        // 原端 keymap 以「扫描码」为唯一键：键位数组按 VK 索引（0xBED5A0），
        // 一个键只可能有一种 type/action。这里按固定顺序输出并按键去重，
        // 否则 Dictionary 的随机遍历顺序会让同一个扫描码写出两行——
        // 读档后同一个键会同时挂两个动作（例如 1 号键既开聊天又做表情）。
        var result = new List<KeymapEntry>();
        var occupied = new HashSet<int>();
        foreach (var entry in NativeKeyActions.Entries) AppendKeymap(result, occupied, entry.action);
        for (int i = 0; i < QuickSlots.Count; i++) AppendKeymap(result, occupied, QuickSlots.Action(i));
        foreach (var entry in unmapped)
        {
            int scan = NativeKeyLayout.ToScanCode(NativeKeyLayout.FromScanCode(entry.key));
            if (scan < 0 || occupied.Contains(scan)) continue;
            result.Add(entry);
            occupied.Add(scan);
        }
        return result;
    }

    static void AppendKeymap(List<KeymapEntry> result, HashSet<int> occupied, GameAction action)
    {
        if (!bind.TryGetValue(action, out var key) || key == KeyCode.None) return;
        int scan = NativeKeyLayout.ToScanCode(key);
        if (scan < 0 || occupied.Contains(scan)) return;
        var entry = NativeKeyActions.Of(action);
        int slot = QuickSlots.Index(action);
        if (entry == null && (slot < 0 || QuickSlots.Id(slot) <= 0)) return;
        result.Add(new KeymapEntry { key = scan, type = entry != null ? entry.Type : 1,
            action = entry != null ? entry.id : QuickSlots.Id(slot) });
        occupied.Add(scan);
    }

    public static KeyCode Of(GameAction a)
    {
        Ensure();
        return bind.TryGetValue(a, out var k) ? k : KeyCode.None;
    }

    public static void Bind(GameAction a, KeyCode k)
    {
        Ensure();
        k = NativeKeyLayout.FunctionKey(k);
        // 一个键只服务一个动作：绑到新动作时把旧主人清成 None，
        // 否则 Shift 同时是「快捷格 1」和「跳跃」会互相打架。
        foreach (var key in new List<GameAction>(bind.Keys))
            if (k != KeyCode.None && bind[key] == k && key != a)
            {
                bind[key] = KeyCode.None;
                PlayerPrefs.SetInt("mxd.key." + key, (int)KeyCode.None);
            }
        bind[a] = k;
        PlayerPrefs.SetInt("mxd.key." + a, (int)k);
        PlayerPrefs.Save();
    }

    public static Dictionary<GameAction, KeyCode> Defaults()
        => new Dictionary<GameAction, KeyCode>(defaults);

    public static void Apply(Dictionary<GameAction, KeyCode> values)
    {
        Ensure();
        foreach (var kv in values)
        {
            var code = NativeKeyLayout.FunctionKey(kv.Value);
            // 原版一个键只服务一个动作。整张表应用时如果新绑定抢了别人的键，
            // 必须把旧主人清成 None：否则按一下会同时触发两件事——比如表情改绑到 1 号键、
            // 而 1 号键原本是「对所有人聊天」，按下会先开聊天（抢走焦点，之后所有动作都不派发），
            // 表情就变成「看起来没反应」。
            if (code != KeyCode.None)
                foreach (var other in new List<GameAction>(bind.Keys))
                    if (other != kv.Key && bind[other] == code)
                    {
                        bind[other] = KeyCode.None;
                        PlayerPrefs.SetInt("mxd.key." + other, (int)KeyCode.None);
                    }
            bind[kv.Key] = code;
            PlayerPrefs.SetInt("mxd.key." + kv.Key, (int)code);
        }
        PlayerPrefs.Save();
    }

    public static void ResetAll()
    {
        Ensure();
        foreach (var kv in defaults)
        {
            bind[kv.Key] = NativeKeyLayout.FunctionKey(kv.Value);
            PlayerPrefs.SetInt("mxd.key." + kv.Key, (int)bind[kv.Key]);
        }
        PlayerPrefs.Save();
    }

    public static IEnumerable<KeyValuePair<GameAction, KeyCode>> All
    {
        get { Ensure(); return bind; }
    }

    // 键帽上的显示文字（和 WZ 里的键帽贴图对齐）
    public static string Label(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.LeftShift: return "Shift";
            case KeyCode.RightShift: return "Shift";
            case KeyCode.LeftControl: return "Ctrl";
            case KeyCode.RightControl: return "Ctrl";
            case KeyCode.LeftAlt: return "Alt";
            case KeyCode.RightAlt: return "Alt";
            case KeyCode.Insert: return "Ins";
            case KeyCode.Delete: return "Del";
            case KeyCode.Home: return "Home";
            case KeyCode.End: return "End";
            case KeyCode.PageUp: return "PgUp";
            case KeyCode.PageDown: return "PgDn";
            case KeyCode.Escape: return "Esc";
            case KeyCode.BackQuote: return "`";
            case KeyCode.None: return "";
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.Space: return "Space";
            default: return k.ToString();
        }
    }
}

public static class InputHub
{
    static readonly NativeKeyRepeat repeat = new NativeKeyRepeat();
    static readonly SortedDictionary<int, KeyCode> repeatKeys = BuildRepeatKeys();
    static int repeatedKey;
    static SortedDictionary<int, KeyCode> BuildRepeatKeys()
    {
        var result = new SortedDictionary<int, KeyCode>();
        foreach (var key in NativeKeyLayout.PhysicalKeys)
        {
            int vk = NativeKeyLayout.VirtualKey(key);
            if (vk != 0) result[vk] = NativeKeyLayout.FunctionKey(key);
        }
        return result;
    }
    static bool KeyHeld(KeyCode key)
    {
        var other = NativeKeyLayout.OtherModifier(key);
        return Input.GetKey(key) || other != KeyCode.None && Input.GetKey(other) || virtualHeld.Contains(key);
    }
    static bool KeyPressed(KeyCode key)
    {
        var other = NativeKeyLayout.OtherModifier(key);
        return Input.GetKeyDown(key) || other != KeyCode.None && Input.GetKeyDown(other) || virtualPressed.Contains(key);
    }
    static int RepeatId(GameAction action)
    {
        var key = KeyMap.Of(action);
        return key == KeyCode.None ? 256 + (int)action : NativeKeyLayout.VirtualKey(key);
    }
    public static void TickRepeat(float now, bool active)
    {
        repeatedKey = 0;
        if (!active || !Enabled) { repeat.Reset(); previousAxisKeys = 0; return; }
        int pressed = 0;
        foreach (var pair in repeatKeys)
            if (KeyPressed(pair.Value)) pressed = pair.Key;
        int axisKeys = (virtualAxis.x < -.15f ? 1 : 0) | (virtualAxis.y > .15f ? 2 : 0) |
            (virtualAxis.x > .15f ? 4 : 0) | (virtualAxis.y < -.15f ? 8 : 0);
        for (int i = 0; i < 4; i++)
            if ((axisKeys & ~previousAxisKeys & (1 << i)) != 0) pressed = Mathf.Max(pressed, 37 + i);
        previousAxisKeys = axisKeys;
        // Direct action buttons also feed the native clock, including unbound buttons.
        foreach (var action in virtualActionsPressed) pressed = Mathf.Max(pressed, RepeatId(action));
        if (pressed != 0) repeat.Press(pressed, now);
        bool held = repeatKeys.TryGetValue(repeat.Key, out var key) && KeyHeld(key);
        if (repeat.Key >= 37 && repeat.Key <= 40 && (axisKeys & (1 << (repeat.Key - 37))) != 0) held = true;
        foreach (var action in virtualActionsHeld) if (RepeatId(action) == repeat.Key) held = true;
        if (repeat.Poll(held, now)) repeatedKey = repeat.Key;
    }
    public static bool Repeated(GameAction action) => Enabled && repeatedKey != 0 && repeatedKey == RepeatId(action);
    static readonly HashSet<KeyCode> virtualHeld = new HashSet<KeyCode>();
    static readonly HashSet<KeyCode> virtualPressed = new HashSet<KeyCode>();
    static readonly HashSet<KeyCode> virtualReleaseNext = new HashSet<KeyCode>();

    // 虚拟键直接按「动作」注入：技能格 1-4 这类动作默认没绑键盘键（KeyMap.Of = None），
    // 只发 KeyCode 会变成空操作。屏上按钮因此有第二条通道。
    static readonly HashSet<GameAction> virtualActionsHeld = new HashSet<GameAction>();
    static readonly HashSet<GameAction> virtualActionsPressed = new HashSet<GameAction>();

    public static bool Enabled = true;

    static Vector2 virtualAxis;
    static bool virtualAxisActive;
    static int previousAxisKeys;

    // 屏上虚拟摇杆给模拟量（VirtualControls 每帧调用）；
    // 有模拟量时 AxisX/Y 直接采用，盖过数字方向键——
    // 游戏逻辑仍然只读 AxisX/Y，一层都不知道摇杆的存在。
    public static void SetVirtualAxis(float x, float y)
    {
        virtualAxis = new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
        virtualAxisActive = virtualAxis.sqrMagnitude > 0.0001f;
    }

    public static void ClearVirtualAxis()
    {
        virtualAxis = Vector2.zero;
        virtualAxisActive = false;
    }

    // 屏幕快捷格通过虚拟键接口发送按下/抬起；这里只记录"物理键码"，
    // 于是重绑一个虚拟键 = 换一个 KeyCode，游戏逻辑一行都不用改。
    public static void SetVirtual(KeyCode k, bool down)
    {
        k = NativeKeyLayout.FunctionKey(k);
        if (k == KeyCode.None) return;
        if (down)
        {
            if (virtualHeld.Add(k)) virtualPressed.Add(k);
            virtualReleaseNext.Remove(k);
        }
        else
        {
            virtualHeld.Remove(k);
        }
    }

    public static bool Held(GameAction a)
    {
        if (!Enabled) return false;
        if (virtualActionsHeld.Contains(a)) return true;
        var k = KeyMap.Of(a);
        if (k == KeyCode.None) return false;
        var other = NativeKeyLayout.OtherModifier(k);
        return Input.GetKey(k) || other != KeyCode.None && Input.GetKey(other) || virtualHeld.Contains(k);
    }

    public static bool Pressed(GameAction a)
    {
        if (!Enabled) return false;
        if (virtualActionsPressed.Contains(a)) return true;
        var k = KeyMap.Of(a);
        if (k == KeyCode.None) return false;
        var other = NativeKeyLayout.OtherModifier(k);
        return Input.GetKeyDown(k) || other != KeyCode.None && Input.GetKeyDown(other) || virtualPressed.Contains(k);
    }

    // 屏上按钮绑的是「动作」而不是某个键时走这里（技能格、装备、背包…都一样）
    public static void SetVirtualAction(GameAction a, bool down)
    {
        if (down)
        {
            if (virtualActionsHeld.Add(a)) virtualActionsPressed.Add(a);
        }
        else
        {
            virtualActionsHeld.Remove(a);
        }
    }

    public static float AxisX
    {
        get
        {
            if (virtualAxisActive) return virtualAxis.x;
            float v = 0f;
            if (Held(GameAction.Left)) v -= 1f;
            if (Held(GameAction.Right)) v += 1f;
            return v;
        }
    }

    public static float AxisY
    {
        get
        {
            if (virtualAxisActive) return virtualAxis.y;
            float v = 0f;
            if (Held(GameAction.Down)) v -= 1f;
            if (Held(GameAction.Up)) v += 1f;
            return v;
        }
    }

    // 一帧结束时清"本帧按下"标记：虚拟键没有 GetKeyDown，
    // 只能靠这里把边沿信号维持恰好一帧。
    public static void EndFrame()
    {
        virtualPressed.Clear();
        virtualActionsPressed.Clear();
        foreach (var k in virtualReleaseNext) virtualHeld.Remove(k);
        virtualReleaseNext.Clear();
    }

    public static void ReleaseAllVirtual()
    {
        repeat.Reset(); repeatedKey = 0; previousAxisKeys = 0;
        virtualHeld.Clear();
        virtualPressed.Clear();
        virtualReleaseNext.Clear();
        virtualActionsHeld.Clear();
        virtualActionsPressed.Clear();
        ClearVirtualAxis();
    }
}

// 统一指针：物理鼠标 / 手指都归一到虚拟 GUI 坐标。
// 新面板（底部栏、技能窗、键位设置）只用它，不直接读 Input.mousePosition。
public static class Pointer
{
    public static Vector2 Pos;
#if UNITY_EDITOR
    public static Vector2? TestPos;
    public static bool TestClick;
    static bool testHeld;
#endif
    public static bool Down;      // 本帧按住
    public static bool Pressed;   // 本帧按下（边沿）
    public static bool Released;  // 本帧抬起（边沿）

    public static bool RightDown, RightPressed, RightReleased;
    public static bool Clicked, DoubleClicked, Dragging, DragStarted, DragEnded;
    public static Vector2 PressPosition;
    public static bool Blocked;

    // 模态面板（侧滑菜单）接管期间：冻结所有 Pointer 事件，避免抬手穿透到底层 HUD。
    public static bool Suppressed;
    public static void ConsumeClick()
    {
        Clicked = DoubleClicked = Released = false;
    }
    static float lastClickTime = -10f;
    static Vector2 lastClickPosition;
    static bool doubleCandidate;
    static int frame = -1;

    public static bool UsesVirtualCursor { get; private set; }
    static bool touchGesture, touchMoved, touchSecondary, touchHold, touchOnControl;
    static bool mouseOnControl;   // 鼠标按下点落在虚拟控件上：整段手势不发鼠标事件
    static int touchId;
    static float touchSince;
    static Vector2 touchStart;
    static bool initialized;

    // The screen acts as a relative trackpad: touching never teleports the cursor.
    static void ReadDevice(out bool left, out bool right)
    {
        left = right = false;
        if (!initialized)
        {
            initialized = true;
            Pos = new Vector2(UiKit.W * 0.5f, UiKit.H * 0.5f);
            UsesVirtualCursor = Application.isMobilePlatform;
        }
        if (!Application.isFocused)
        {
            touchGesture = touchHold = touchOnControl = false;
            mouseOnControl = false;
            return;
        }
        if (Input.touchCount > 0)
        {
            UsesVirtualCursor = true;
            if (!touchGesture)
            {
                var first = Input.GetTouch(0);
                // Do not restart a cancelled gesture while a finger remains on screen.
                if (first.phase != TouchPhase.Began) return;
                touchGesture = true;
                touchId = first.fingerId;
                touchStart = first.position;
                touchSince = Time.unscaledTime;
                touchMoved = touchSecondary = touchHold = false;
                // 手指按在虚拟按键/摇杆上：整段手势归虚拟层，光标不跟随、也不产生点击
                touchOnControl = VirtualControls.HitsAtScreen(first.position);
            }
            if (touchOnControl)
            {
                bool any = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var tt = Input.GetTouch(i);
                    if (tt.phase != TouchPhase.Ended && tt.phase != TouchPhase.Canceled) any = true;
                }
                if (!any) { touchGesture = false; touchOnControl = false; }
                return;
            }
            int active = 0;
            bool cancelled = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Canceled) cancelled = true;
                if (t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled) active++;
                if (t.fingerId != touchId) continue;
                if ((t.position - touchStart).sqrMagnitude > 64f * UiKit.Scale * UiKit.Scale)
                    touchMoved = true;
                if (!touchSecondary && t.phase != TouchPhase.Began && t.phase != TouchPhase.Canceled)
                    Pos += new Vector2(t.deltaPosition.x, -t.deltaPosition.y) / UiKit.Scale;
            }
            // A second finger cancels any pending left tap. Hold stays left until released.
            if (Input.touchCount >= 2 && !touchHold) touchSecondary = true;
            if (cancelled)
            {
                touchGesture = touchHold = touchOnControl = false;
                return;
            }
            if (active == 0)
            {
                if (touchHold) left = false;
                else if (!touchMoved && Time.unscaledTime - touchSince <= 0.35f)
                {
                    right = touchSecondary;
                    left = !touchSecondary;
                }
                touchGesture = touchHold = false;
            }
            else
            {
                if (!touchSecondary && !touchMoved && Time.unscaledTime - touchSince >= 0.35f)
                    touchHold = true;
                left = touchHold;
            }
        }
        else
        {
            touchGesture = touchHold = false;
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1) ||
                Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0f)
                UsesVirtualCursor = false;
            if (!UsesVirtualCursor)
            {
                Pos = new Vector2(Input.mousePosition.x / UiKit.Scale,
                                  (Screen.height - Input.mousePosition.y) / UiKit.Scale);
                // 鼠标按下的位置落在虚拟按键上：这次按下到抬起整段都归虚拟层，
                // 不产生任何鼠标左键事件（HUD 不响应、光标也不做点击动画）。
                bool raw = Input.GetMouseButton(0);
                if (!raw) mouseOnControl = false;
                else if (!mouseOnControl && VirtualControls.HitsAtScreen(Input.mousePosition))
                    mouseOnControl = true;
                left = raw && !mouseOnControl;
                right = Input.GetMouseButton(1);
            }
        }
        Pos = new Vector2(Mathf.Clamp(Pos.x, 0f, UiKit.W - 1f),
                          Mathf.Clamp(Pos.y, 0f, UiKit.H - 1f));
    }

    // Sample once per frame before any UI reads the pointer.
    public static void Tick()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;

        if (Suppressed)
        {
            Pressed = Released = RightPressed = RightReleased = false;
            Clicked = DoubleClicked = DragStarted = DragEnded = false;
            Down = RightDown = false;
            Dragging = false;
            doubleCandidate = false;
            touchGesture = touchHold = touchOnControl = false;
            mouseOnControl = false;
            lastClickTime = -10f;
            return;
        }

        ReadDevice(out bool left, out bool right);
        bool focused = Application.isFocused;
        Pressed = left && !Down;
        Released = !left && Down;
        RightPressed = right && !RightDown;
        RightReleased = !right && RightDown;
        Down = left;
        RightDown = right;
        Clicked = DoubleClicked = DragStarted = DragEnded = false;
        if (Pressed)
        {
            PressPosition = Pos;
            // 5 个虚拟像素的容差在手机上几乎不可能点中（等于 7~8 个物理像素），
            // 技能窗双击施放、键位窗双击取回图标都会被判成两次单击。放到 16。
            doubleCandidate = Time.unscaledTime - lastClickTime <= 0.5f &&
                              (Pos - lastClickPosition).sqrMagnitude <= 256f;
        }
        if ((Down || Released) && !Dragging && (Pos - PressPosition).sqrMagnitude > 16f)
            DragStarted = Dragging = true;
        if (Released)
        {
            DragEnded = Dragging;
            Clicked = focused && !Dragging;
            DoubleClicked = Clicked && doubleCandidate;
            lastClickTime = Clicked && !DoubleClicked ? Time.unscaledTime : -10f;
            lastClickPosition = Pos;
            Dragging = false;
        }
        if (!focused)
        {
            // Focus loss cancels a gesture; it must never activate a release button.
            Released = RightReleased = false;
            Dragging = false;
            lastClickTime = -10f;
        }

#if UNITY_EDITOR
        // UI 截图自动化：编辑器脚本把指针钉在指定位置，只保留悬停、不产生点击
        if (TestPos.HasValue)
        {
            Pos = TestPos.Value;
            bool click = TestClick;
            Pressed = click && !testHeld;
            Released = !click && testHeld;
            Down = click;
            testHeld = click;
            RightDown = RightPressed = RightReleased = false;
            Clicked = DoubleClicked = DragStarted = DragEnded = false;
        }
#endif
    }
}

// 把键盘/虚拟键翻译成游戏原来那套「意图」接口（TouchInput），
// 这样 PlayerCtrl 的战斗逻辑一行都不用改。
[DefaultExecutionOrder(-150)]
public class InputBridge : MonoBehaviour
{
    public static InputBridge I;

    void Awake()
    {
        I = this;
        KeyMap.Ensure();
        Input.simulateMouseWithTouches = false;
        if (GetComponent<NativeCursor>() == null) gameObject.AddComponent<NativeCursor>();
    }

    void Update()
    {
        var game = GameLoop.I;
        UiKit.RefreshMetrics();
        // 侧滑菜单先读原始触摸：它决定本帧要不要冻结下面的游戏输入。
        GameSideMenu.Tick();
        Pointer.Tick();

        bool playing = game != null && game.state == GameLoop.GameState.Playing &&
                       game.player != null && !game.gameOver && !PlayerCtrl.InputLocked && !BottomBar.ChatFocused &&
                       !GameSideMenu.Blocking && !NpcSession.Blocking;

        // 屏上虚拟按键（摇杆/十字/按钮）：先把触摸翻译成注入，
        // 后面读 AxisX/Pressed 时已经包含虚拟键的量。
        bool dialogInput = NpcSession.Blocking && !GameSideMenu.Blocking && !BottomBar.ChatFocused;
        VirtualControls.Tick((playing || dialogInput) && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible);
        // 在打开 NPC 的分派之前处理已存在的会话，同一按下不能打开又翻页。
        if (dialogInput && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible &&
            InputHub.Pressed(GameAction.NpcTalk)) NpcDialogUi.ContinueWithTalkKey();
        InputHub.TickRepeat(Time.unscaledTime, playing && Application.isFocused && Time.timeScale > 0 &&
            !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible);

        if (playing)
        {
            TouchInput.MoveH = InputHub.AxisX;
            TouchInput.MoveV = InputHub.AxisY;
            if (InputHub.Pressed(GameAction.Jump) || InputHub.Repeated(GameAction.Jump)) TouchInput.PushJump();
        }
        else
        {
            TouchInput.MoveH = 0f;
            TouchInput.MoveV = 0f;
        }

        if ((playing || dialogInput) && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible)
        {
            foreach (var entry in NativeKeyActions.Entries)
            {
                if (entry.action == GameAction.Jump || entry.action == GameAction.Attack || entry.action == GameAction.Pickup) continue;
                if ((playing || NativeKeyActions.AvailableDuringNpcDialog(entry.action)) &&
                    InputHub.Pressed(entry.action)) NativeKeyActions.Execute(entry.action);
            }
            foreach (var action in new[] { GameAction.Help, GameAction.QuickSlot })
                if (InputHub.Pressed(action)) NativeKeyActions.Execute(action);
        }

        // Esc（系统菜单动作）不能被「角色此刻能不能动」卡住：面板开着、角色倒地、
        // 正在读图之后都得能关窗口，否则玩家会被自己的面板锁在游戏里。
        // 已经各自处理 Esc 的层（键位窗、虚拟键编辑器、侧滑菜单、聊天输入、频道下拉、
        // NPC 对话）在这里让出去，避免一次按键关掉两层。
        bool escapeOwnedElsewhere = KeyConfigWindow.Visible || VirtualControlsEditor.Visible ||
            GameSideMenu.Blocking || BottomBar.ChatFocused || BottomBar.ChannelListOpen || NpcSession.Blocking;
        if (!escapeOwnedElsewhere && InputHub.Pressed(GameAction.Menu))
            NativeKeyActions.Execute(GameAction.Menu);
        if (KeyConfigWindow.Visible || VirtualControlsEditor.Visible || NpcSession.Blocking)
        {
            TouchInput.MoveH = TouchInput.MoveV = 0f;
            TouchInput.TakeJump();
            return;
        }

        // 快捷栏：按下哪个键就放哪一格的技能（原版 QuickSlot 的语义）
        if (playing)
            for (int i = 0; i < QuickSlots.Count; i++)
                if (QuickSlots.Id(i) > 0 && InputHub.Pressed(QuickSlots.Action(i)))
                    QuickSlots.Cast(i);

        // BeiDou 0094F5EA is the sole call to the basic-attack handler 0094E256:
        // dispatch on key-down/repeat, rather than polling a held key every frame.
        // A later direction key replaces the keyboard repeat owner as in DirectInput.
        bool skillOwnsAttack = false;
        for (int i = 0; i < QuickSlots.Count; i++)
            if (QuickSlots.Id(i) > 0 && KeyMap.Of(QuickSlots.Action(i)) == KeyMap.Of(GameAction.Attack))
                skillOwnsAttack = true;
        if (playing && !skillOwnsAttack &&
            (InputHub.Pressed(GameAction.Attack) || InputHub.Repeated(GameAction.Attack))) game.player.RequestAttack();

        if (playing && (InputHub.Pressed(GameAction.Pickup) || InputHub.Repeated(GameAction.Pickup)))
        {
            GroundItem.TryPickupNearest();
        }
    }

    void LateUpdate()
    {
        InputHub.EndFrame();
    }

    // 全局悬浮球/菜单覆盖层：加载页在 InputBridge 创建之前，所以这里天然不画；
    // 悬浮球仅在游戏内显示。depth 低于 GameHUD/LoginFlow，画在最上层。
    void OnGUI()
    {
        if (GameLoop.I == null) return;
        // 同一个 depth 内按调用顺序叠：后画的在上面。
        GUI.depth = -300;
        UiKit.Begin();

        // 屏上虚拟按键垫最底，侧边栏（含整屏压暗蒙版）盖在它上面。
        // 悬浮球与按键层不会同时出现（球在面板收起时才画），所以球仍然在最上层。
        // 编辑器预览（UiPreviewHook.Draw 非空）时让位：虚拟按键在 GameHUD 之后绘制，
        // 靠 GUI.depth 压不住，会把预览的深色底戳出一堆洞（截图右下角露出「跳跃/技能格」）。
        if (!GameHUD.InterfaceHidden && UiPreviewHook.Draw == null)
        {
            VirtualControls.Draw();
            VirtualControls.DrawDropTargets();
        }
        GameSideMenu.Draw();
        VirtualControlsEditor.Draw();
    }
}
