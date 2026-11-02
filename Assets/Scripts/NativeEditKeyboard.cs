using UnityEngine;

// 移动端软键盘桥：把「点击输入框 → 拉起系统键盘 → 把最终文本回灌」这一件事收在一处。
// 项目里登录页（LoginFlow.OpenKeyboard）和聊天（BottomBar.FocusChat/ReadChatKeyboard）已各自
// 验证过这套时序，这里把两条已踩过的坑原样复用，别再各处重写：
//   1) Android 上「active=false 再 Open」真会失败（open 返回非空但键盘不弹），
//      所以同一个输入框已经开着键盘时，重复点击绝不重开（LoginFlow.cs:843-846 原注释）。
//   2) 键盘刚弹出的头几帧 status 还不是 Visible，切前后台也会短暂 LostFocus；
//      按「status != Visible 就收」等于刚弹出来就被自己收走（LoginFlow.cs:194-199 原注释）。
//      因此只在用户明确 Done / Canceled 时销毁，LostFocus / Visible 一律保留。
//   3) Android 输入法最终合成文本可能与 Done/Enter 同帧到达，必须每个 GUI 事件先读键盘快照
//      再消费 Enter，Done 与 Enter 同帧只提交一次（BottomBar.DrawChat:300-302 原注释）。
// 三处共用：NativeUtilPrompt（数量/文本弹窗）、NativeDlgInput（CUtilDlgEx 文本）、
// NativeStorageWindow（仓库金币框）。owner 用字符串隔离，避免 A 输入框的键盘被 B 读走。
public static class NativeEditKeyboard
{
    public enum Signal { None, Done, Canceled }

    static TouchScreenKeyboard keyboard;
    static string owner;

#if UNITY_EDITOR
    // batchmode 回归注入：编辑器里没有真键盘对象，用一对假 (text, status) 走同一条 Sync 逻辑。
    // 真机构建不编译这段，运行期不产生任何分支开销。
    static bool injected;
    static string injectedText;
    static TouchScreenKeyboard.Status injectedStatus;
    public static void Inject(string key, string text, TouchScreenKeyboard.Status status)
    { owner = key; injected = true; injectedText = text; injectedStatus = status; }
#endif

    public static bool Active => keyboard != null && keyboard.active;
    public static bool Owns(string key) => owner == key;

    // 每个 GUI 事件在绘制输入框之前调用：先把键盘快照回灌到 value，再返回本帧信号。
    // owner 不匹配或键盘已经收起时不改 value、不产生信号。
    public static Signal Sync(string key, ref string value)
    {
#if UNITY_EDITOR
        // 编辑器无真键盘：用注入快照验证 owner 隔离与 Done/Canceled 收键盘。
        if (injected)
        {
            if (owner != key) return Signal.None;
            var injectedSignal = Apply(ref value, injectedText, injectedStatus);
            if (injectedSignal != Signal.None) Close();
            return injectedSignal;
        }
#endif
        if (keyboard == null || owner != key) return Signal.None;
        var signal = Apply(ref value, keyboard.text, keyboard.status);
        if (signal != Signal.None) Close();
        return signal;
    }

    // 纯逻辑，供 batchmode 回归直接注入 (text, status)，不依赖真机键盘对象。
    // text 为 null 表示键盘还没给出有效文本（例如刚弹出的头几帧），保留现有草稿。
    public static Signal Apply(ref string value, string text, TouchScreenKeyboard.Status status)
    {
        if (text != null) value = text;
        if (status == TouchScreenKeyboard.Status.Done) return Signal.Done;
        if (status == TouchScreenKeyboard.Status.Canceled) return Signal.Canceled;
        // Visible / LostFocus / 其它：保留键盘，绝不因状态短暂波动就收走。
        return Signal.None;
    }

    // 坑 1 抽成纯判定，供 batchmode 回归断言：同一个输入框键盘已开着时绝不重开；
    // 换成别的输入框（owner 不同）或键盘已经收起才允许重新 Open。
    public static bool ShouldReopen(string currentOwner, bool currentActive, string key)
        => !(currentActive && currentOwner == key);

    // 点击输入框命中时调用。仅在移动平台真正拉起；同框已开着则不重开（坑 1）。
    // type 由调用方按 numeric / 文本指定；characterLimit 与对应 GUI.TextField 的上限一致。
    public static void Open(string key, string value, TouchScreenKeyboardType type, int characterLimit)
    {
        if (!Application.isMobilePlatform) return;
        if (!ShouldReopen(owner, keyboard != null && keyboard.active, key)) return;
        if (keyboard != null) keyboard.active = false;
        keyboard = TouchScreenKeyboard.Open(value ?? "", type,
            false, false, false, false, "", characterLimit);
        owner = key;
    }

    // 弹窗/窗口关闭、切场景时调用。无差别收掉当前桥持有的键盘（同一时刻只会有一个 owner）。
    public static void Close()
    {
        if (keyboard != null) keyboard.active = false;
        keyboard = null;
        owner = null;
#if UNITY_EDITOR
        injected = false; injectedText = null;
#endif
    }
}
