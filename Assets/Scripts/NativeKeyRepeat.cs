// BeiDou.exe CInputSystem: 0059AA4D configures the Windows repeat timings;
// 0059AF77 selects the last newly pressed VK, 0059B2D2 repeats only that key.
// Android has no SystemParametersInfo: use the client's API-failure defaults.
public sealed class NativeKeyRepeat
{
    public float Delay { get; private set; }
    public float Interval { get; private set; }
    public int Key { get; private set; }
    float since;
    bool repeating;

    public NativeKeyRepeat() { Configure(3, 0); }
    public void Configure(int keyboardDelay, int keyboardSpeed)
    {
        Delay = System.Math.Clamp((keyboardDelay + 1) * 250, 250, 1000) / 1000f;
        Interval = System.Math.Clamp(400 - keyboardSpeed * 12, 33, 400) / 1000f;
        Reset();
    }
    public void Reset() { Key = 0; repeating = false; since = 0; }
    public void Press(int key, float now)
    {
        Key = key; since = now; repeating = false;
    }
    public bool Poll(bool held, float now)
    {
        if (!held) { Reset(); return false; }
        if (Key == 0 || now - since + .000001f < (repeating ? Interval : Delay)) return false;
        // The native client emits one event per poll, not a catch-up burst.
        since = now; repeating = true; return true;
    }
}
