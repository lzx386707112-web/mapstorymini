using UnityEngine;

// 摄像机：跟随 + 顿帧震屏 + 关卡边界。
// 震屏必须作用在摄像机偏移上，且用 LateUpdate，否则会被跟随逻辑覆盖掉。
//
// 边界：关卡背景是「真实地图切片」，切片之外什么都没有。
// 所以镜头不能像自由滚动那样乱跑，必须夹在 [切片左端+半屏, 切片右端-半屏] 内，
// 这样任何机位都只会看到切片内部的画面，不会露黑边。
public class CameraRig : MonoBehaviour
{
    public Transform target;
    public float smooth = 7f;
    public float forwardOffset = 1.1f;
    public float baseY = 0.9f;
    // 正交尺寸：视野高度 = baseSize * 2 * PPU = 7.2*2*40 = 576 地图像素。
    // 原版 800x600 的窗口能看到约 600px 高的地图，角色（~110px）占屏 15%。
    // 之前 4.6 只有 368px 高，等于把画面放大了 1.6 倍，所以「整个景像被拉近」。
    public float baseSize = 7.2f;
    public float freeMinX = -11f, freeMaxX = 11f;

    Camera cam;
    float x;

    bool  useStage;
    float stageMinX, stageMaxX;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = gameObject.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = baseSize;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.farClipPlane = 100f;
    }

    // 关卡切片的世界左右端（不是可走范围，是画面边界）
    public void SetStage(float minX, float maxX)
    {
        useStage = true;
        stageMinX = minX;
        stageMaxX = maxX;
    }

    public void ClearStage()
    {
        useStage = false;
    }

    void LateUpdate()
    {
        if (target != null)
        {
            float tx = target.position.x + forwardOffset;
            x = Mathf.Lerp(x, tx, 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime));
            x = ClampX(x);
        }

        var off = HitFeel.CameraOffset;
        // 吸附到像素网格（1/PPU）：背景是 1:1 的地图像素，镜头停在半个像素上会抖
        float px = Mathf.Round((x + off.x) * Art.PPU) / Art.PPU;
        transform.position = new Vector3(px, baseY + off.y, -10f);
    }

    float ClampX(float v)
    {
        if (!useStage) return Mathf.Clamp(v, freeMinX, freeMaxX);

        // 半屏宽随窗口比例变，每帧算，改分辨率也不会错
        float half = cam.orthographicSize * Mathf.Max(0.1f, cam.aspect);
        float lo = stageMinX + half;
        float hi = stageMaxX - half;
        if (lo >= hi) return (stageMinX + stageMaxX) * 0.5f;   // 切片比屏幕还窄：锁中间
        return Mathf.Clamp(v, lo, hi);
    }

    public void SnapTo(float targetX)
    {
        x = ClampX(targetX);
        transform.position = new Vector3(x, baseY, -10f);
    }
}
