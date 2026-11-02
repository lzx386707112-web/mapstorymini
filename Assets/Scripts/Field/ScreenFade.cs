using UnityEngine;

// 原版换图渐变：画面渐隐到全黑 → 换图 → 渐显。
//
// 原客户端这一步是 CField::TransferField 里硬编码的（服务端源码、Map.wz、MapHelper 里都没有
// 任何「换图淡入淡出时长」字段，EXE 里的符号表也被抹掉了），所以三个时长只能是本地取值，
// 已列进 docs/original-gameplay-port.md 的适配表。表现上对齐原版的三段：
//   1. 黑场匀速加深（不是 ease，老客户端是每帧 alpha 累加）
//   2. 全黑的那一小段里完成地图替换，玩家看不到任何跳变
//   3. 新图淡出黑幕
//
// 只负责「画」：alpha 由调用方（FieldDirector 的换图状态机）推进。
// 不用协程，因为协程里的 WaitForSecondsRealtime 在 batchmode / 切图卡帧时不可靠，
// 而换图又必须在全黑那一帧精确完成。
public sealed class ScreenFade : MonoBehaviour
{
    public const float OutSeconds  = 0.32f;   // 渐隐到全黑
    public const float HoldSeconds = 0.06f;   // 全黑停留：覆盖换图那一帧
    public const float InSeconds   = 0.32f;   // 从全黑渐显

    public const float TotalSeconds = OutSeconds + HoldSeconds + InSeconds;

    static ScreenFade inst;

    float alpha;
    System.Action changeScene;
    int scenePhase;
    double sceneAt;
    bool previousInputLock;
    public static bool Busy => inst != null && inst.scenePhase != 0;

    // Present the fully black frame before synchronous IMG/catalog work starts.
    public static void Transition(MonoBehaviour host, System.Action change)
    {
        if (Busy) return;
        var fade = Attach(host);
        fade.changeScene = change;
        fade.previousInputLock = PlayerCtrl.InputLocked;
        PlayerCtrl.InputLocked = true;
        fade.scenePhase = 1;
        fade.sceneAt = Time.realtimeSinceStartupAsDouble;
    }

    void Update()
    {
        if (scenePhase == 0) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (scenePhase == 1)
        {
            alpha = Mathf.Clamp01((float)(now - sceneAt) / OutSeconds);
            if (alpha < 1f) return;
            scenePhase = 2;
            return;
        }
        if (scenePhase == 2)
        {
            var change = changeScene; changeScene = null;
            try { change?.Invoke(); }
            catch (System.Exception error) { Debug.LogException(error); }
            finally { sceneAt = Time.realtimeSinceStartupAsDouble; scenePhase = 3; }
            return;
        }
        if (now - sceneAt < HoldSeconds) return;
        alpha = 1f - Mathf.Clamp01((float)(now - sceneAt - HoldSeconds) / InSeconds);
        if (alpha > 0f) return;
        scenePhase = 0;
        PlayerCtrl.InputLocked = previousInputLock;
    }

    public static float Alpha => inst != null ? inst.alpha : 0f;

    public static ScreenFade Attach(MonoBehaviour host)
    {
        if (inst != null) return inst;
        inst = host.gameObject.AddComponent<ScreenFade>();
        return inst;
    }

    public static void Set(float a)
    {
        if (inst != null) inst.alpha = Mathf.Clamp01(a);
    }

    // 离开原版模式时抹掉黑幕，避免下一次进来还停在半黑
    public static void Dismiss()
    {
        if (inst == null) return;
        inst.alpha = 0f;
        inst.changeScene = null;
        if (inst.scenePhase != 0) PlayerCtrl.InputLocked = inst.previousInputLock;
        inst.scenePhase = 0;
    }

    void OnDestroy() { if (inst == this) Dismiss(); }

    void OnGUI()
    {
        // IMGUI retains depth per behaviour. Restoring it after drawing makes
        // the next event sort the fade alongside world labels instead of above them.
        GUI.depth = -20000;
        if (alpha <= 0.0005f) return;
        var matrix = GUI.matrix;
        var color = GUI.color;
        GUI.matrix = Matrix4x4.identity;
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = color;
    }
}
