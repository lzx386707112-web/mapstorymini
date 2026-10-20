using UnityEngine;

// 关卡背景：直接用 tools/wz_stage.py 从客户端地图渲染出来的真实切片。
//
// 每个主题三张图（back/mid/front），都是「地图像素 1:1」的，
// 摆放位置由 manifest 里的 px（窗口左上角）、ground（地面锚点）算出来：
//     worldX = (mapX - ground.x) / PPU
//     worldY = GroundY + (ground.y - mapY) / PPU
// 于是地图的地面线正好落在 PlayerCtrl.GroundY 上，角色脚底天然对齐。
//
// 这里刻意没有任何「视差配方」：原版背景的视差已经烘进切片里了
// （渲染时按摄像机中心换算过），再叠一层动态视差反而会和瓦片对不上。
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
        if (st == null || (string.IsNullOrEmpty(st.fileBack) && string.IsNullOrEmpty(st.fileMid)))
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

        // 三层按 manifest 给的 sortingOrder 铺。front 层（原版 front=1）挡在角色前面，
        // 走到树/柱子后面会被遮住——这是原版的深度感来源，不要去掉。
        Place(st.fileBack, st.orderBack, st);
        Place(st.fileMid, st.orderMid, st);
        Place(st.fileFront, st.orderFront, st);

        // 天空底板对准切片中心（尺寸远大于切片，四周都盖得住）
        if (sky != null)
        {
            sky.transform.position = new Vector3((st.LeftX + st.RightX) * 0.5f,
                                                 (st.TopY + st.BottomY) * 0.5f, 0f);
        }
    }

    void Place(string rel, int order, Art.StageInfo st)
    {
        if (string.IsNullOrEmpty(rel) || st == null) return;

        // FrameBottom：轴心取图片左下角，于是 transform.position 就是切片的左下角，
        // 精灵世界尺寸 = 像素 / PPU（40），和地图 1:1，不用任何缩放。
        var sp = Art.FrameBottom(rel);
        if (sp == null)
        {
            Debug.LogWarning("[Backdrop] 缺少切片 " + rel);
            return;
        }

        var g = new GameObject("Layer" + order);
        g.transform.SetParent(artRoot, false);
        var sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingOrder = order;
        sr.color = Color.white;
        g.transform.localScale = Vector3.one;
        g.transform.position = new Vector3(st.LeftX, st.BottomY, 0f);
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
