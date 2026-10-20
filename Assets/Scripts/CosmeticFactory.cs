using System.Collections.Generic;
using UnityEngine;

// 装扮：头发 / 服饰 / 武器外观
//
// 头发和服饰**不再**是叠在角色上的几何色块（那种做法画出来就是「头顶扣个镜头、
// 身上糊个方块」，跟角色完全不贴合）。现在改成直接改写角色原画的像素：
//   · 头发：在头顶 12% 高度取「出现最多的非黑白颜色」当种子，再按颜色连通性
//     向两侧/向下蔓延（flood fill），只染这一片 → 发型轮廓、发丝明暗全部保留
//   · 服饰：胸腹段整片做「保明暗换色」，跳过皮肤色 → 衣服跟随身体轮廓
// 染好的贴图与角色帧**同尺寸同轴心**，渲染时天然对齐，不会再出现悬空色块。
//
// 武器还是程序化画的，但画成真正的武器（刃/护手/握柄/杖头），并且带倾角。
public static class CosmeticFactory
{
    const int N = 96;   // 程序化武器贴图边长

    static readonly Dictionary<string, Sprite> sprCache = new Dictionary<string, Sprite>();
    static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

    // ————————————————————————— 染色 —————————————————————————

    // 头发染色：返回与 src 同尺寸、同轴心的覆盖层 Sprite（只含头发像素）
    public static Sprite DyeHair(Sprite src, Color hair)
    {
        if (src == null || src.texture == null) return null;
        string key = "h" + src.texture.GetInstanceID() + "_" + CKey(hair);
        Sprite hit;
        if (sprCache.TryGetValue(key, out hit) && hit != null) return hit;

        int w = src.texture.width, h = src.texture.height;
        Color32[] px;
        try { px = src.texture.GetPixels32(); } catch { return null; }   // 贴图不可读时放弃覆盖层

        int x0, x1, y0, y1;
        if (!Bounds(px, w, h, out x0, out x1, out y0, out y1)) return null;
        int usedH = y1 - y0 + 1;

        // 头顶 12%：取最主要颜色当「发色」种子（跳过纯黑描边和纯白高光）
        int seedBand = Mathf.Max(1, Mathf.RoundToInt(usedH * 0.12f));
        Color32 seed;
        if (!Dominant(px, w, h, y1 - seedBand, y1, out seed)) return null;

        // 连通生长：只沿着和发色相近的像素爬，不会翻过皮肤边界跑到脸上
        bool[] mask = new bool[w * h];
        var q = new Queue<int>();
        int limit = y1 - Mathf.RoundToInt(usedH * 0.62f);   // 最多染到身高 62%（长发到肩）
        const float tol = 0.34f;
        for (int y = Mathf.Max(y1 - seedBand * 2, limit); y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * w + x;
                if (px[i].a > 60 && !mask[i] && Near(px[i], seed, tol)) { mask[i] = true; q.Enqueue(i); }
            }
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % w, y = i / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < x0 || nx > x1 || ny < limit || ny > y1) continue;
                int j = ny * w + nx;
                if (mask[j] || px[j].a <= 60 || !Near(px[j], seed, tol)) continue;
                mask[j] = true;
                q.Enqueue(j);
            }
        }

        var outPx = new Color32[w * h];
        float seedL = Mathf.Max(0.05f, Lum(seed));
        for (int i = 0; i < w * h; i++)
        {
            if (!mask[i]) continue;                       // 非头发像素保持全透明
            var c = px[i];
            float rel = Mathf.Clamp(Lum(c) / seedL, 0.32f, 1.45f);   // 相对明暗
            Color nc = new Color(hair.r * rel, hair.g * rel, hair.b * rel, c.a / 255f);
            if (Lum(c) < 0.10f)                            // 原图描边（很黑）压深，别染成亮色
                nc = Color.Lerp(nc, new Color(0.07f, 0.06f, 0.10f), 0.55f);
            outPx[i] = new Color32(Byte(nc.r), Byte(nc.g), Byte(nc.b), c.a);
        }

        var tex = MakeTex(outPx, w, h, "dye_hair");
        hit = MakeSprite(tex, src);
        Cache(key, hit, tex);
        return hit;
    }

    // 服饰染色：胸腹段「保明暗换色」，跳过皮肤和极黑描边
    public static Sprite DyeCoat(Sprite src, Color outfit)
    {
        if (src == null || src.texture == null) return null;
        string key = "c" + src.texture.GetInstanceID() + "_" + CKey(outfit);
        Sprite hit;
        if (sprCache.TryGetValue(key, out hit) && hit != null) return hit;

        int w = src.texture.width, h = src.texture.height;
        Color32[] px;
        try { px = src.texture.GetPixels32(); } catch { return null; }

        int x0, x1, y0, y1;
        if (!Bounds(px, w, h, out x0, out x1, out y0, out y1)) return null;
        int usedH = y1 - y0 + 1;
        int top = y1 - Mathf.RoundToInt(usedH * 0.46f);   // 肩
        int bot = y1 - Mathf.RoundToInt(usedH * 0.96f);   // 腰胯

        var outPx = new Color32[w * h];
        for (int y = bot; y <= top; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * w + x;
                var c = px[i];
                if (c.a <= 60) continue;
                if (Skin(c)) continue;                     // 脸/手不染
                float rel = Mathf.Clamp(Lum(c) / 0.34f, 0.30f, 1.5f);
                Color nc = new Color(outfit.r * rel, outfit.g * rel, outfit.b * rel, c.a / 255f);
                nc = Color.Lerp(new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f), nc, 0.88f);
                outPx[i] = new Color32(Byte(nc.r), Byte(nc.g), Byte(nc.b), c.a);
            }

        var tex = MakeTex(outPx, w, h, "dye_coat");
        hit = MakeSprite(tex, src);
        Cache(key, hit, tex);
        return hit;
    }

    // ————————————————————————— 武器 —————————————————————————

    // 程序化武器：颜色直接烘进贴图（刃=武器色，护手=金，柄=木），精灵本身用白色不染
    public static Sprite WeaponSprite(WeaponKind kind, Color accent)
    {
        string key = "w" + (int)kind + "_" + CKey(accent);
        Sprite hit;
        if (sprCache.TryGetValue(key, out hit) && hit != null) return hit;

        var buf = new Color[N * N];
        Color bright = Color.Lerp(accent, Color.white, 0.55f);
        Color dark = new Color(accent.r * 0.45f, accent.g * 0.45f, accent.b * 0.45f, 1f);
        Color gold = new Color(1f, 0.82f, 0.36f);
        Color wood = new Color(0.46f, 0.29f, 0.17f);

        switch (kind)
        {
            case WeaponKind.sword:
                PaintBlade(buf, accent, bright, dark, gold, wood, 0.052f, 0.030f, 0.16f, 0.30f);
                break;
            case WeaponKind.dagger:
                PaintBlade(buf, accent, bright, dark, gold, wood, 0.040f, 0.022f, 0.10f, 0.36f);
                break;
            case WeaponKind.greatsword:
                PaintBlade(buf, accent, bright, dark, gold, wood, 0.088f, 0.052f, 0.21f, 0.34f);
                break;
            case WeaponKind.staff:
            {
                Rect(buf, 0.478f, 0.04f, 0.522f, 0.86f, wood);             // 杖身
                Rect(buf, 0.451f, 0.10f, 0.549f, 0.135f, gold);            // 握把缠带
                Rect(buf, 0.451f, 0.165f, 0.549f, 0.20f, gold);
                Disc(buf, 0.5f, 0.885f, 0.082f, accent);                   // 法球
                Disc(buf, 0.5f, 0.885f, 0.052f, bright);
                Ring(buf, 0.5f, 0.885f, 0.104f, 0.014f, gold);
                // 球下托爪
                Rect(buf, 0.468f, 0.80f, 0.532f, 0.82f, gold);
                break;
            }
            case WeaponKind.mace:
            {
                Rect(buf, 0.478f, 0.03f, 0.522f, 0.70f, wood);
                Disc(buf, 0.5f, 0.79f, 0.105f, dark);                      // 球体
                Disc(buf, 0.5f, 0.82f, 0.075f, accent);
                Ring(buf, 0.5f, 0.79f, 0.128f, 0.012f, gold);
                // 四根尖刺
                Rect(buf, 0.492f, 0.88f, 0.508f, 0.955f, gold);
                Rect(buf, 0.492f, 0.625f, 0.508f, 0.70f, gold);
                Rect(buf, 0.345f, 0.782f, 0.40f, 0.798f, gold);
                Rect(buf, 0.60f, 0.782f, 0.655f, 0.798f, gold);
                break;
            }
            case WeaponKind.bow:
            {
                RingPart(buf, 0.38f, 0.5f, 0.335f, 0.028f, wood, true);     // 「)」形弓臂
                Rect(buf, 0.712f, 0.165f, 0.727f, 0.835f, new Color(0.88f, 0.90f, 0.95f));  // 弓弦
                Rect(buf, 0.345f, 0.44f, 0.42f, 0.56f, gold);              // 握把
                Rect(buf, 0.352f, 0.40f, 0.365f, 0.62f, dark);
                break;
            }
        }

        var tex = MakeTex(ToColor32(buf), N, N, "wpn");
        hit = MakeSprite(tex, null);
        Cache(key, hit, tex);
        return hit;
    }

    // 剑类：握柄 + 护手 + 渐尖刀刃 + 中脊高光
    static void PaintBlade(Color[] buf, Color accent, Color bright, Color dark,
                           Color gold, Color wood, float hwBottom, float hwTop,
                           float guardHalf, float bladeFrom)
    {
        Rect(buf, 0.5f - 0.034f, 0.04f, 0.5f + 0.034f, guardHalf - 0.055f, wood);      // 握柄
        Disc(buf, 0.5f, 0.045f, 0.042f, gold);                                        // 柄头
        Rect(buf, 0.5f - guardHalf, guardHalf - 0.055f, 0.5f + guardHalf, guardHalf, gold); // 护手
        Disc(buf, 0.5f - guardHalf, guardHalf - 0.028f, 0.026f, gold);
        Disc(buf, 0.5f + guardHalf, guardHalf - 0.028f, 0.026f, gold);

        int steps = 220;
        for (int s = 0; s < steps; s++)
        {
            float t = s / (float)(steps - 1);
            float v = Mathf.Lerp(bladeFrom, 0.985f, t);
            float hw = Mathf.Lerp(hwBottom, hwTop, t);
            if (t > 0.88f) hw *= Mathf.Lerp(1f, 0.05f, (t - 0.88f) / 0.12f);          // 收尖
            Color c = accent;
            if (t > 0.55f) c = Color.Lerp(accent, bright, (t - 0.55f) / 0.45f);
            Rect(buf, 0.5f - hw, v, 0.5f + hw, v + 0.006f, c);
            Rect(buf, 0.5f - hw * 0.18f, v, 0.5f + hw * 0.18f, v + 0.006f, bright);   // 中脊
            Rect(buf, 0.5f - hw, v, 0.5f - hw + 0.010f, v + 0.006f, dark);            // 刃侧暗边
            Rect(buf, 0.5f + hw - 0.010f, v, 0.5f + hw, v + 0.006f, dark);
        }
    }

    // ————————————————————————— 画图小工具 —————————————————————————

    static void Rect(Color[] buf, float u0, float v0, float u1, float v1, Color c)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(u0 * N), 0, N - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(u1 * N), 0, N - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(v0 * N), 0, N - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(v1 * N), 0, N - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                buf[y * N + x] = c;
    }

    static void Disc(Color[] buf, float cu, float cv, float r, Color c)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt((cu - r) * N), 0, N - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((cu + r) * N), 0, N - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt((cv - r) * N), 0, N - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt((cv + r) * N), 0, N - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float du = (x + 0.5f) / N - cu, dv = (y + 0.5f) / N - cv;
                if (du * du + dv * dv <= r * r) buf[y * N + x] = c;
            }
    }

    static void Ring(Color[] buf, float cu, float cv, float r, float thick, Color c)
    {
        RingPart(buf, cu, cv, r, thick, c, false);
    }

    // rightHalf=true 时只留右半边，用来画弓臂的 C 形
    static void RingPart(Color[] buf, float cu, float cv, float r, float thick, Color c, bool rightHalf)
    {
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float du = (x + 0.5f) / N - cu, dv = (y + 0.5f) / N - cv;
                if (rightHalf && du < 0f) continue;
                float d = Mathf.Sqrt(du * du + dv * dv);
                if (Mathf.Abs(d - r) <= thick) buf[y * N + x] = c;
            }
    }

    // ————————————————————————— 基础工具 —————————————————————————

    static bool Bounds(Color32[] px, int w, int h, out int x0, out int x1, out int y0, out int y1)
    {
        x0 = w; x1 = -1; y0 = h; y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (px[y * w + x].a <= 60) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
        return x1 >= x0 && y1 >= y0;
    }

    static bool Dominant(Color32[] px, int w, int h, int yb, int yt, out Color32 c)
    {
        c = new Color32(0, 0, 0, 255);
        var hist = new Dictionary<int, int>();
        var rep = new Dictionary<int, Color32>();
        for (int y = Mathf.Max(0, yb); y <= Mathf.Min(h - 1, yt); y++)
            for (int x = 0; x < w; x++)
            {
                var p = px[y * w + x];
                if (p.a < 200) continue;
                float l = Lum(p);
                if (l < 0.09f || l > 0.93f) continue;      // 描边/高光不算发色
                int k = (p.r << 16) | (p.g << 8) | p.b;
                hist.TryGetValue(k, out int n);
                hist[k] = n + 1;
                rep[k] = p;
            }
        int bestK = -1, bestN = -1;
        foreach (var kv in hist) if (kv.Value > bestN) { bestN = kv.Value; bestK = kv.Key; }
        if (bestK < 0) return false;
        c = rep[bestK];
        return true;
    }

    static bool Near(Color32 a, Color32 b, float tol)
    {
        float dr = (a.r - b.r) / 255f, dg = (a.g - b.g) / 255f, db = (a.b - b.b) / 255f;
        return dr * dr + dg * dg + db * db <= tol * tol;
    }

    static float Lum(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

    static bool Skin(Color32 c) => c.r > 185 && c.g > 155 && c.b > 115 && c.r >= c.b - 8;

    static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

    static Color32[] ToColor32(Color[] src)
    {
        var o = new Color32[src.Length];
        for (int i = 0; i < src.Length; i++)
            o[i] = new Color32(Byte(src[i].r), Byte(src[i].g), Byte(src[i].b), Byte(src[i].a));
        return o;
    }

    static Texture2D MakeTex(Color32[] px, int w, int h, string name)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.filterMode = FilterMode.Point;      // 像素画，必须点采样，别糊
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.name = name;
        tex.Apply();
        return tex;
    }

    // 用源精灵的轴心创建覆盖层精灵 —— 轴心一致 = 和角色帧严格对齐
    static Sprite MakeSprite(Texture2D tex, Sprite src)
    {
        // 关键：Sprite.pivot 已经是「归一化」轴心（0..1），不能再去除以像素宽，
        // 否则轴心被缩成极小值，覆盖层会整体偏移成「头顶一块色块」。
        Vector2 pivot = (src != null) ? src.pivot : new Vector2(0.5f, 0.5f);
        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot,
                               Art.PPU, 0, SpriteMeshType.FullRect);
        sp.name = tex.name;
        return sp;
    }

    static void Cache(string key, Sprite sp, Texture2D tex)
    {
        // 换色是有上限的（6 头发 x 6 服饰 x 5 职业 x 若干帧），但要防呆：
        // 超过 400 条就整体清空，避免长时间来回切色把内存吃满
        if (sprCache.Count > 400) { sprCache.Clear(); texCache.Clear(); }
        sprCache[key] = sp;
        texCache[key] = tex;
    }

    static string CKey(Color c) =>
        Mathf.RoundToInt(c.r * 255) + "-" + Mathf.RoundToInt(c.g * 255) + "-" + Mathf.RoundToInt(c.b * 255);

    // ————————————————————————— 配色表 —————————————————————————
    public static readonly Dictionary<string, Color> HairColors = new Dictionary<string, Color>
    {
        { "black",  new Color(0.13f, 0.13f, 0.16f) },
        { "brown",  new Color(0.47f, 0.29f, 0.15f) },
        { "blonde", new Color(0.95f, 0.82f, 0.42f) },
        { "blue",   new Color(0.32f, 0.52f, 0.95f) },
        { "pink",   new Color(0.96f, 0.58f, 0.80f) },
        { "green",  new Color(0.42f, 0.82f, 0.47f) },
    };
    public static readonly Dictionary<string, Color> OutfitColors = new Dictionary<string, Color>
    {
        { "none",   new Color(0.85f, 0.86f, 0.90f) },
        { "red",    new Color(0.86f, 0.28f, 0.30f) },
        { "blue",   new Color(0.28f, 0.48f, 0.90f) },
        { "purple", new Color(0.64f, 0.36f, 0.88f) },
        { "white",  new Color(0.94f, 0.95f, 0.99f) },
        { "gold",   new Color(0.93f, 0.76f, 0.30f) },
    };

    public static Color HairColor(string id)   => HairColors.TryGetValue(id, out var c) ? c : HairColors["black"];
    public static Color OutfitColor(string id) => OutfitColors.TryGetValue(id, out var c) ? c : OutfitColors["none"];
}
