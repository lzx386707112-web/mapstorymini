using UnityEngine;

// 全部美术用纯色方块代替：M0 阶段要验证的是手感，不是画面。
public static class SpriteFactory
{
    static Sprite _bottom;
    static Sprite _center;

    static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = c;
        t.SetPixels(px);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        return t;
    }

    // 轴心在底边中心：这样 transform.position.y 就是"脚踩在哪"，不需要每次减半高。
    public static Sprite Bottom()
    {
        if (_bottom == null)
            _bottom = Sprite.Create(MakeTex(Color.white), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0f), 4f);
        return _bottom;
    }

    public static Sprite Center()
    {
        if (_center == null)
            _center = Sprite.Create(MakeTex(Color.white), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return _center;
    }

    public static SpriteRenderer Make(string name, Transform parent, Vector2 size, Color color,
                                      int order = 0, bool bottomPivot = true)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = bottomPivot ? Bottom() : Center();
        sr.color = color;
        sr.sortingOrder = order;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        return sr;
    }
}
