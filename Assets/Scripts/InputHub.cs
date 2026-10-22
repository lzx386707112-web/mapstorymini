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
}

public static class KeyMap
{
    // 原版默认键位（v83）：
    //   移动 = 方向键，跳跃 = Alt，攻击 = Ctrl，拾取 = Z
    //   快捷栏八格 = Shift / Insert / Home / PgUp / Ctrl / Delete / End / PgDn
    //   装备 E、背包 I、能力值 S、技能 K
    //   菜单 Esc、快捷键 Tab、帮助 F1、键位设置反斜杠
    // 可在键盘设定窗口拖动图标重绑。
    static readonly Dictionary<GameAction, KeyCode> defaults = new Dictionary<GameAction, KeyCode>
    {
        { GameAction.Left,  KeyCode.LeftArrow },
        { GameAction.Right, KeyCode.RightArrow },
        { GameAction.Up,    KeyCode.UpArrow },
        { GameAction.Down,  KeyCode.DownArrow },
        { GameAction.Jump,  KeyCode.LeftAlt },
        { GameAction.Attack,KeyCode.LeftControl },
        { GameAction.Pickup,KeyCode.Z },

        { GameAction.Slot1, KeyCode.None },
        { GameAction.Slot2, KeyCode.None },
        { GameAction.Slot3, KeyCode.None },
        { GameAction.Slot4, KeyCode.None },
        { GameAction.Slot5, KeyCode.None },
        { GameAction.Slot6, KeyCode.None },
        { GameAction.Slot7, KeyCode.None },
        { GameAction.Slot8, KeyCode.None },

        { GameAction.Equip, KeyCode.E },
        { GameAction.Inven, KeyCode.I },
        { GameAction.Stat,  KeyCode.S },
        { GameAction.Skill, KeyCode.K },
        { GameAction.KeySet, KeyCode.Backslash },
        { GameAction.QuickSlot, KeyCode.BackQuote },

        { GameAction.Menu,  KeyCode.Escape },
        { GameAction.Shop,  KeyCode.None },
        { GameAction.Shortcut, KeyCode.RightBracket },
        { GameAction.Help,  KeyCode.None },
    };

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
            bind[kv.Key] = NativeKeyLayout.FunctionKey(kv.Value);
            PlayerPrefs.SetInt("mxd.key." + kv.Key, (int)bind[kv.Key]);
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
        bool playing = game != null && game.state == GameLoop.GameState.Playing &&
                       game.player != null && !game.gameOver && !PlayerCtrl.InputLocked && !BottomBar.ChatFocused;
        UiKit.RefreshMetrics();
        Pointer.Tick();

        // 屏上虚拟按键（摇杆/十字/按钮）：先把触摸翻译成注入，
        // 后面读 AxisX/Pressed 时已经包含虚拟键的量。
        VirtualControls.Tick(playing && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible);

        if (playing)
        {
            TouchInput.MoveH = InputHub.AxisX;
            TouchInput.MoveV = InputHub.AxisY;
            if (InputHub.Pressed(GameAction.Jump)) TouchInput.PushJump();
        }
        else
        {
            TouchInput.MoveH = 0f;
            TouchInput.MoveV = 0f;
        }

        if (playing && !KeyConfigWindow.Visible && !VirtualControlsEditor.Visible)
        {
            if (InputHub.Pressed(GameAction.Equip)) EquipWindow.Toggle();
            if (InputHub.Pressed(GameAction.Inven)) InvenWindow.Toggle();
            if (InputHub.Pressed(GameAction.Stat)) StatWindow.Toggle();
            if (InputHub.Pressed(GameAction.Skill)) SkillWindow.Toggle();
            if (InputHub.Pressed(GameAction.KeySet)) KeyConfigWindow.Toggle();
            if (InputHub.Pressed(GameAction.QuickSlot)) BottomBar.QuickCollapsed = !BottomBar.QuickCollapsed;
            if (InputHub.Pressed(GameAction.Menu)) GameMenuWindow.Toggle();
            if (InputHub.Pressed(GameAction.Shop)) ShopWindow.Toggle();
            if (InputHub.Pressed(GameAction.Shortcut)) ShortcutWindow.Toggle();
            if (InputHub.Pressed(GameAction.Help)) HelpWindow.Toggle();
        }
        if (KeyConfigWindow.Visible || VirtualControlsEditor.Visible)
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

        // Ctrl 的技能绑定优先；未绑技能时按住请求普攻，攻击间隔仍由角色控制。
        bool skillOwnsAttack = false;
        for (int i = 0; i < QuickSlots.Count; i++)
            if (QuickSlots.Id(i) > 0 && KeyMap.Of(QuickSlots.Action(i)) == KeyMap.Of(GameAction.Attack))
                skillOwnsAttack = true;
        if (playing && !skillOwnsAttack && InputHub.Held(GameAction.Attack)) game.player.RequestAttack();

        if (playing && InputHub.Pressed(GameAction.Pickup))
        {
            if (game.field != null) GroundItem.TryPickupNearest();
            else MesoDrop.TryPickupNearest();
        }
    }

    void LateUpdate()
    {
        InputHub.EndFrame();
    }
}
