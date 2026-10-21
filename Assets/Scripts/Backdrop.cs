using UnityEngine;

// 关卡背景：直接用 tools/wz_stage.py 从客户端地图渲染出来的真实切片。
//
// 每个主题三张图（back/mid/front），都是「地图像素 1:1」的，
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
    readonly System.Collections.Generic.List<NativeBack> nativeBacks =
        new System.Collections.Generic.List<NativeBack>();

    void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null) return;
        foreach (var back in nativeBacks) back.Update(camera);
    }

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
        nativeBacks.Clear();
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
        if (st.backgrounds.Count > 0)
            foreach (var entry in st.backgrounds)
                nativeBacks.Add(new NativeBack(artRoot, st, entry));
        else
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

    // CMapLoadable::MakeBack / MakeGrid：每个 WZ back 独立定位和平铺，
    // 不镜像补景、不 smear_fill，也不把 rx/ry 烘进固定背景图。
    class NativeBack
    {
        readonly Art.StageInfo stage;
        readonly Art.StageBack entry;
        readonly Transform root;
        readonly Sprite sprite;
        readonly System.Collections.Generic.List<SpriteRenderer> cells =
            new System.Collections.Generic.List<SpriteRenderer>();
        readonly float started = Time.time;

        public NativeBack(Transform parent, Art.StageInfo stage, Art.StageBack entry)
        {
            this.stage = stage;
            this.entry = entry;
            root = new GameObject("Back_" + entry.no).transform;
            root.SetParent(parent, false);
            sprite = Art.FrameBottom(entry.file);
        }

        public void Update(Camera camera)
        {
            if (sprite == null) return;
            float mapX = camera.transform.position.x * Art.PPU + stage.ground.x;
            float mapY = stage.ground.y - (camera.transform.position.y - PlayerCtrl.GroundY) * Art.PPU;
            float halfY = camera.orthographicSize * Art.PPU;
            float halfX = halfY * camera.aspect;
            int type = entry.type;
            bool scrollX = type == 4 || type == 6;
            bool scrollY = type == 5 || type == 7;
            bool repeatX = type == 1 || type == 3 || type == 4 || type == 6 || type == 7;
            bool repeatY = type == 2 || type == 3 || type == 5 || type == 6 || type == 7;
            float elapsed = Time.time - started;
            // rx/ry 是相对相机的百分比；自动滚动轴则是 5*|r| px/s。
            float bx = entry.x - entry.origin.x + mapX * (1f + (scrollX ? 0f : entry.rx / 100f));
            float by = entry.y - entry.origin.y + mapY * (1f + (scrollY ? 0f : entry.ry / 100f));
            if (scrollX) bx -= elapsed * 5f * entry.rx;
            if (scrollY) by -= elapsed * 5f * entry.ry;
            float stepX = entry.cx > 0 ? entry.cx : entry.size.x;
            float stepY = entry.cy > 0 ? entry.cy : entry.size.y;
            int left = repeatX ? Mathf.FloorToInt((mapX - halfX - bx - entry.size.x) / stepX) + 1 : 0;
            int right = repeatX ? Mathf.CeilToInt((mapX + halfX - bx) / stepX) - 1 : 0;
            int top = repeatY ? Mathf.FloorToInt((mapY - halfY - by - entry.size.y) / stepY) + 1 : 0;
            int bottom = repeatY ? Mathf.CeilToInt((mapY + halfY - by) / stepY) - 1 : 0;
            int count = 0;
            for (int j = top; j <= bottom; j++)
                for (int i = left; i <= right; i++)
                {
                    if (count == cells.Count)
                    {
                        var go = new GameObject("Tile");
                        go.transform.SetParent(root, false);
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = sprite;
                        sr.sortingOrder = (entry.front != 0 ? 600 : -800) + entry.no;
                        sr.color = new Color(1f, 1f, 1f, entry.alpha / 255f);
                        cells.Add(sr);
                    }
                    var cell = cells[count++];
                    cell.enabled = true;
                    cell.transform.position = new Vector3(stage.WorldX(bx + i * stepX),
                        stage.WorldY(by + j * stepY + entry.size.y), 0f);
                }
            for (int i = count; i < cells.Count; i++) cells[i].enabled = false;
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
