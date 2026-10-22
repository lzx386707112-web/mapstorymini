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
        if (inst != null) inst.alpha = 0f;
    }

    void OnGUI()
    {
        if (alpha <= 0.0005f) return;

        int prev = GUI.depth;
        GUI.depth = -1000;          // depth 越小越靠前：压过 GameHUD / 摇杆 / 技能格
        UiKit.Begin();
        UiKit.Fill(new Rect(0f, 0f, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, alpha));
        GUI.depth = prev;
    }
}
