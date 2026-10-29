using UnityEngine;

// 打击感中枢。所有参数都可以在运行时用右上角面板拖动，即时生效。
// 四件套：顿帧 / 震屏 / 闪白 / 击退。缺任何一个，打上去都像打在棉花上。
public static class HitFeel
{
    // —— 可调参数（面板绑这三根 + 一个闪白时长）——
    public static float StopMs      = 60f;   // 顿帧时长（毫秒）。3~5 帧 ≈ 50~83ms
    public static float ShakeAmp    = 0.18f; // 震屏振幅（世界单位）。0.1~0.3 比较合适
    public static float Knockback   = 0.9f;  // 击退距离（世界单位）
    public static float FlashDur    = 0.06f; // 闪白持续（秒）

    static float _stopUntil;
    static float _shakeUntil;
    static float _shakeNow;
    static float _shakeDur = 0.14f;

    public static Vector2 CameraOffset { get; private set; }

    // 调用一次 = 打中一下。
    public static void Punch(float weight = 1f)
    {
        if (StopMs > 0f)
        {
            // 用 unscaled 计时，因为下面会把 timeScale 压到 0
            float end = Time.unscaledTime + StopMs / 1000f;
            if (end > _stopUntil) _stopUntil = end;
            Time.timeScale = 0f;
        }
        Shake(ShakeAmp * weight, 0.14f);
    }

    // 只震屏、不带顿帧。怪物 attack<N>/info/tremble 走这里
    // （BeiDou.exe 0x6671D3：非 0 时用常量 30.0 调一次震屏）。
    public static void Shake(float worldAmplitude, float seconds)
    {
        if (worldAmplitude <= 0f) return;
        _shakeNow = worldAmplitude;
        _shakeDur = Mathf.Max(0.05f, seconds);
        _shakeUntil = Time.unscaledTime + _shakeDur;
    }

    public static void Reset()
    {
        Time.timeScale = 1f;
        _stopUntil = 0f; _shakeUntil = 0f; _shakeNow = 0f;
        CameraOffset = Vector2.zero;
    }

    // 由 HitFeelRunner 每帧调用。timeScale=0 时 Update 依然会跑，所以恢复是安全的。
    public static void Tick()
    {
        if (Time.timeScale == 0f && Time.unscaledTime >= _stopUntil)
            Time.timeScale = 1f;

        if (Time.unscaledTime < _shakeUntil && _shakeNow > 0f)
        {
            // 逐帧衰减，比固定振幅更有"余震"感
            float k = Mathf.InverseLerp(_shakeUntil, _shakeUntil - _shakeDur, Time.unscaledTime);
            float a = _shakeNow * k;
            CameraOffset = new Vector2(Random.Range(-a, a), Random.Range(-a, a));
        }
        else
        {
            CameraOffset = Vector2.zero;
        }
    }
}

// 因为 Time.timeScale 被压到 0 时，普通 Update 的 deltaTime 也是 0，
// 顿帧恢复必须依赖 unscaledTime，所以需要一个只做计时的小驱动器。
public class HitFeelRunner : MonoBehaviour
{
    void Update() => HitFeel.Tick();
    void OnDisable() => HitFeel.Reset();
}
