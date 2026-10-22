using UnityEngine;

// 章节地图使用原始 IMG，主题配置只保留关卡出生锚点与配色。
//
// 主体与背景均保持「地图像素 1:1」，
// 摆放位置由 manifest 里的 px（窗口左上角）、ground（地面锚点）算出来：
//     worldX = (mapX - ground.x) / PPU
//     worldY = GroundY + (ground.y - mapY) / PPU
// 于是地图的地面线正好落在 PlayerCtrl.GroundY 上，角色脚底天然对齐。
//
[DefaultExecutionOrder(100)] // 原端背景以本帧 Gr2D.center 为基准，在相机跟随后更新
public class Backdrop : MonoBehaviour
{
    Transform artRoot;
    SpriteRenderer sky;
    string curTheme = "";
    public bool HasStage { get; private set; }
    public float WalkMin { get; private set; } = -13f;
    public float WalkMax { get; private set; } = 13f;

    public void Build()
    {
        var go = new GameObject("Sky");
        go.transform.SetParent(transform, false);
        sky = go.AddComponent<SpriteRenderer>();
        sky.sprite = SpriteFactory.Center();       // 居中轴心：往四周都能盖住
        sky.color = new Color(0.07f, 0.08f, 0.11f);
        sky.sortingOrder = -950;
        sky.transform.position = new Vector3(0f, 1.5f, 0f);
        sky.transform.localScale = new Vector3(140f, 52f, 1f);
    }

    public void Apply(string theme, StageTheme t)
    {
        if (t != null && sky != null && !Art.Ready) sky.color = t.bgTop;
        if (theme == curTheme) return;
        curTheme = theme;
        Rebuild(theme, t);
    }

    void Rebuild(string theme, StageTheme t)
    {
        if (artRoot != null) Destroy(artRoot.gameObject);
        var go = new GameObject("Stage_" + theme);
        go.transform.SetParent(transform, false);
        artRoot = go.transform;

        var st = Art.Stage(theme);
        if (st == null)
        {
            HasStage = false;
            WalkMin = -13f;
            WalkMax = 13f;
            Fallback(t);
            return;
        }

        HasStage = true;
        WalkMin = st.WalkMin;
        WalkMax = st.WalkMax;
        if (sky != null) sky.color = st.sky;

        artRoot.position = new Vector3(-st.ground.x / Art.PPU, PlayerCtrl.GroundY + st.ground.y / Art.PPU, 0);
        artRoot.gameObject.AddComponent<NativeMapView>().Show(int.Parse(st.map));

        // 天空底板对准切片中心（尺寸远大于切片，四周都盖得住）
        if (sky != null)
        {
            sky.transform.position = new Vector3((st.LeftX + st.RightX) * 0.5f,
                                                 (st.TopY + st.BottomY) * 0.5f, 0f);
        }
    }

    // 没有关卡切片时退回原来的纯色层次，保证任何状态下都能跑
    void Fallback(StageTheme t)
    {
        Color deep = t != null ? t.groundDeep : new Color(0.17f, 0.19f, 0.23f);
        Color mid = t != null ? t.mid : new Color(0.2f, 0.24f, 0.3f);

        for (int i = 0; i < 3; i++)
        {
            var g = new GameObject("FB" + i);
            g.transform.SetParent(artRoot, false);
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Bottom();
            sr.color = Color.Lerp(deep, mid, i / 2f);
            sr.sortingOrder = -390 + i * 40;
            g.transform.position = new Vector3(0f, PlayerCtrl.GroundY, 0f);
            g.transform.localScale = new Vector3(70f, 4f - i * 0.8f, 1f);
        }

        var ground = new GameObject("FBGround");
        ground.transform.SetParent(artRoot, false);
        var gsr = ground.AddComponent<SpriteRenderer>();
        gsr.sprite = SpriteFactory.Bottom();
        gsr.color = deep;
        gsr.sortingOrder = -60;
        ground.transform.position = new Vector3(0f, PlayerCtrl.GroundY - 1.5f, 0f);
        ground.transform.localScale = new Vector3(70f, 1.5f, 1f);
    }
}
