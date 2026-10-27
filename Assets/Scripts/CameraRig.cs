using UnityEngine;

// 摄像机：跟随 + 顿帧震屏 + 关卡边界。
// 震屏必须作用在摄像机偏移上，且用 LateUpdate，否则会被跟随逻辑覆盖掉。
//
// 原端依据 VR 范围限制相机中心，地图小于视野时锁在两边界的中点。
public class CameraRig : MonoBehaviour
{
    public Transform target;
    public float smooth = 7f;
    public float baseY = PlayerCtrl.GroundY + 50f / Art.PPU;
    // 原端分辨率决定可见地图像素，而不是分别缩放地图和人物。
    // 保留 768px 逻辑高度，手机全屏按宽高比扩展横向视野。
    public float baseSize = 768f / (2f * Art.PPU);
    public float freeMinX = -11f, freeMaxX = 11f;

    Camera cam;
    float x, y;

    bool  useStage;
    float stageMinX, stageMaxX, stageMinY, stageMaxY;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = gameObject.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = baseSize;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.farClipPlane = 100f;
        UpdateViewport();
        y = baseY;
    }

    void UpdateViewport()
    {
        // 保持 768 个地图像素的高度，超宽屏扩展横向视野，不再留下未清理的侧边。
        cam.rect = new Rect(0f, 0f, 1f, 1f);
        cam.aspect = cam.targetTexture != null
            ? (float)cam.targetTexture.width / cam.targetTexture.height
            : (float)Screen.width / Mathf.Max(1, Screen.height);
        cam.orthographicSize = baseSize;
    }

    public void SetStage(Art.StageInfo stage)
    {
        useStage = true;
        stageMinX = stage.hasView ? stage.WorldX(stage.view.x) : stage.LeftX;
        stageMaxX = stage.hasView ? stage.WorldX(stage.view.z) : stage.RightX;
        stageMinY = stage.hasView ? stage.WorldY(stage.view.w) : stage.BottomY;
        stageMaxY = stage.hasView ? stage.WorldY(stage.view.y) : stage.TopY;
        SnapTo(target != null ? target.position.x : x);
    }

    // 原版地图模式：直接给出 VR 换算后的世界坐标边界（单位与 SetStage 相同）
    public void SetBounds(float minX, float maxX, float minY, float maxY)
    {
        useStage = true;
        stageMinX = Mathf.Min(minX, maxX);
        stageMaxX = Mathf.Max(minX, maxX);
        stageMinY = Mathf.Min(minY, maxY);
        stageMaxY = Mathf.Max(minY, maxY);
        SnapTo(target != null ? target.position.x : x);
    }

    public void ClearStage()
    {
        useStage = false;
    }

    void LateUpdate()
    {
        UpdateViewport();
        if (target != null)
        {
            // CAnimationDisplayer camera anchor: RelMove(0, -50), 0x00437B32.
            float tx = target.position.x;
            x = Mathf.Lerp(x, tx, 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime));
            x = ClampX(x);
            float ty = target.position.y + 50f / Art.PPU;
            y = Mathf.Lerp(y, ty, 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime));
            y = ClampY(y);
        }
        else
        {
            x = ClampX(x);
            y = ClampY(baseY);
        }

        var off = HitFeel.CameraOffset;
        // 吸附到像素网格（1/PPU）：背景是 1:1 的地图像素，镜头停在半个像素上会抖
        float px = Mathf.Round((x + off.x) * Art.PPU) / Art.PPU;
        float py = Mathf.Round((y + off.y) * Art.PPU) / Art.PPU;
        transform.position = new Vector3(px, py, -10f);
    }

    float ClampX(float v)
    {
        if (!useStage) return Mathf.Clamp(v, freeMinX, freeMaxX);

        // 半屏宽随窗口比例变，每帧算，改分辨率也不会错
        // ijl15 0x10011DF5: integer logical width / 2, not a fractional pixel.
        float half = Mathf.Floor(cam.orthographicSize * Mathf.Max(0.1f, cam.aspect) * Art.PPU) / Art.PPU;
        float lo = stageMinX + half;
        float hi = stageMaxX - half;
        if (lo >= hi) return (stageMinX + stageMaxX) * 0.5f;   // 切片比屏幕还窄：锁中间
        return Mathf.Clamp(v, lo, hi);
    }

    public void SnapTo(float targetX)
    {
        // A map switch can precede LateUpdate after a resolution/targetTexture change.
        UpdateViewport();
        x = ClampX(targetX);
        y = ClampY(target != null ? target.position.y + 50f / Art.PPU : baseY);
        transform.position = new Vector3(x, y, -10f);
    }

    float ClampY(float value)
    {
        if (!useStage) return value;
        // ijl15 0x10011E2A..0x10011E8F: integer logical height / 2.
        float half = Mathf.Floor(cam.orthographicSize * Art.PPU) / Art.PPU;
        float lo = stageMinY + half;
        float hi = stageMaxY - half;
        return lo >= hi ? (stageMinY + stageMaxY) * 0.5f : Mathf.Clamp(value, lo, hi);
    }
}
