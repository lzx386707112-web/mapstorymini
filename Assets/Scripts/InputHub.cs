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

    public static bool Enabled = true;

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
        var k = KeyMap.Of(a);
        if (k == KeyCode.None) return false;
        var other = NativeKeyLayout.OtherModifier(k);
        return Input.GetKey(k) || other != KeyCode.None && Input.GetKey(other) || virtualHeld.Contains(k);
    }

    public static bool Pressed(GameAction a)
    {
        if (!Enabled) return false;
        var k = KeyMap.Of(a);
        if (k == KeyCode.None) return false;
        var other = NativeKeyLayout.OtherModifier(k);
        return Input.GetKeyDown(k) || other != KeyCode.None && Input.GetKeyDown(other) || virtualPressed.Contains(k);
    }

    public static float AxisX
    {
        get
        {
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
        foreach (var k in virtualReleaseNext) virtualHeld.Remove(k);
        virtualReleaseNext.Clear();
    }

    public static void ReleaseAllVirtual()
    {
        virtualHeld.Clear();
        virtualPressed.Clear();
        virtualReleaseNext.Clear();
    }
}

// 统一指针：物理鼠标 / 手指都归一到虚拟 GUI 坐标。
// 新面板（底部栏、技能窗、键位设置）只用它，不直接读 Input.mousePosition。
public static class Pointer
{
    public static Vector2 Pos;
    public static bool Down;      // 本帧按住
    public static bool Pressed;   // 本帧按下（边沿）
    public static bool Released;  // 本帧抬起（边沿）

    static bool lastDown;
    public static bool Blocked;   // 被上层面板吃掉时不参与命中

    public static void Tick()
    {
        var p = UiKit.MouseGui();
        bool d = Input.GetMouseButton(0);
        Pos = p;
        Pressed = d && !lastDown;
        Released = !d && lastDown;
        Down = d;
        lastDown = d;
    }
}

// 把键盘/虚拟键翻译成游戏原来那套「意图」接口（TouchInput），
// 这样 PlayerCtrl 的战斗逻辑一行都不用改。
[DefaultExecutionOrder(-150)]
public class InputBridge : MonoBehaviour
{
    public static InputBridge I;

    void Awake() { I = this; KeyMap.Ensure(); }

    void Update()
    {
        var game = GameLoop.I;
        bool playing = game != null && game.state == GameLoop.GameState.Playing &&
                       game.player != null && !game.gameOver && !PlayerCtrl.InputLocked && !BottomBar.ChatFocused;
        UiKit.RefreshMetrics();

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

        if (playing && !KeyConfigWindow.Visible)
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
        if (KeyConfigWindow.Visible)
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
        Pointer.Tick();
        InputHub.EndFrame();
    }
}
