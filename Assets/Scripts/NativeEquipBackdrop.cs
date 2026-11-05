using System;
using UnityEngine;
using MapleDash.Wz;

// ————————————————————————————————————————————————————————————
// 折叠态装备窗背景（UI/UIWindow.img/Equip/backgrnd，175×304）。
//
// 「副手 / 图腾 / 心脏 / 徽章 / 纹章」5 格不是 GMS 原版槽位，是后来 DLL 补画后烘进位图的，
// 和自带格（帽子/武器…）比只有两处文字缺陷：
//   1) 文字的白描边发糊：描边色散成 #88AABB 一带（自带格是干净的 #DDEEEE）；
//   2) 字形本身被描边吃得残缺，无法从位图恢复。
//
// 几何不能动！这 5 格本来就比自带格矮 1～3px（实测：副手/图腾内容 y=200..228、
// 心脏/徽章/纹章 y=231..261），格间/格底的白行（y=229..230、262..264）是源端烘死的
// 分隔线——把它们当「残缺」补成底纹会把格子间的缝填没（第三版翻车原因）。
//
// 源端文字参数（逐格量自同图自带格，20 个标签全部一致）：
//   · 标签墨迹 23×11px，左上角钉在格内 (3,3)，核心色 #7788AA，1-bit 无抗锯齿；
//   · 核心外扩 1px（4 邻域，含字内空腔）填描边色 #DDEEEE（实测 (221,238,238)，
//     「武器」格核心/描边二值化后与该规则 IoU=0.93、零多余像素）；
//   · 12px 宋体点阵（与文泉驿点阵宋体 9pt@12 同源，「勋章/指环」逐位 IoU≈0.96）。
//
// 底纹是「逐行不同、横向 4px 周期」的噪声（5 格干净行 y=18..28 实测 1408/1408
// 像素满足 p[x]==p[x+4]），所以补底纹 = 用同一格的干净行整行复制，x 相位天然一致；
// 文字行严禁用本行做种子——本行的种子列被字形污染，铺出来就是竖条纹（第一版翻车原因）。
//
// 处理：加载时把 5 个格子的文字带整行换成本格干净行，再把点阵字形按 (3,3) 盖回去
//（先 #DDEEEE 描边、后 #7788AA 核心）。位图其余像素（含分隔线）一律不动。
// ————————————————————————————————————————————————————————————
public static class NativeEquipBackdrop
{
    public const int Width = 175, Height = 304;

    // 与 EquipWindow 的格子网格保持一致（GridX / GridY / Step / Cell）。
    const int GridX = 5, GridY = 35, Step = 33, Cell = 32;

    // 自带格标签的核心色（#7788AA）与描边色（#DDEEEE）。
    public static readonly Color32 LabelColor = new Color32(119, 136, 170, 255);
    public static readonly Color32 OutlineColor = new Color32(221, 238, 238, 255);

    // 文字区：自带格墨迹在 y=3..13，白描边向外扩 1px，取 y=2..17 整段换底纹。
    const int BandTop = 2, BandBottom = 17;

    // 干净行种子区：5 格的 y=18..28 实测无文字、无白行、严格 4px 周期。
    const int SeedLow = 18, SeedHigh = 28;

    public struct Gap
    {
        public readonly int row, col, slot, glyph;
        public readonly string label;

        public Gap(int row, int col, int slot, string label, int glyph)
        {
            this.row = row; this.col = col; this.slot = slot;
            this.label = label; this.glyph = glyph;
        }
    }

    /// <summary>保留的后补格子；心脏和符文徽章使用各自的专用面板。</summary>
    public static readonly Gap[] Gaps =
    {
        new Gap(5, 3, -51, "副手", 0),
        new Gap(5, 4, -53, "图腾", 1),
        new Gap(6, 4, -56, "纹章", 2),
    };

    public static string LabelOf(int slot)
    {
        foreach (var gap in Gaps) if (gap.slot == slot) return gap.label;
        return null;
    }

    // 12px 宋体点阵字形（文泉驿点阵宋体 9pt@12px，1-bit）。每条 = 墨迹宽 + 11 行位图，
    // bit0 为最左列；排版与自带格完全一致：墨迹左上角钉在格内 (3,3)。
    struct LabelGlyph { public int width; public uint[] rows; }

    static readonly LabelGlyph[] Glyphs =
    {
        new LabelGlyph { width = 23, rows = new uint[] {   // 副手
            0x3C047F, 0x03E500, 0x02053E, 0x3FE522, 0x02053E, 0x020500,
            0x7FF57F, 0x020549, 0x02047F, 0x020449, 0x03077F } },
        new LabelGlyph { width = 22, rows = new uint[] {   // 图腾
            0x1573FF, 0x3FD209, 0x0252F9, 0x3FF24D, 0x115233, 0x2FD249,
            0x097397, 0x3F5221, 0x205211, 0x2F4A21, 0x106BFF } },
        new LabelGlyph { width = 23, rows = new uint[] {   // 纹章
            0x020044, 0x3FE084, 0x0887F2, 0x7FF22A, 0x104227, 0x1FC144,
            0x104142, 0x1FC08F, 0x020150, 0x7FF22C, 0x020413 } },
    };

    static Texture2D patched;
    static bool tried;

    /// <summary>修好的背景图；素材缺失或尺寸不符时返回 null（调用方回退原图）。</summary>
    public static Texture2D Texture()
    {
        if (tried) return patched;
        tried = true;
        try
        {
            var frame = WzAssets.Node("UI/UIWindow.img/Equip/backgrnd");
            if (frame == null) return null;
            var canvas = WzAssets.Canvas(frame);
            int w = canvas.Width, h = canvas.Height;
            if (w != Width || h != Height) return null;
            // Pixels() 返回的是按 canvas 缓存的那份数组，必须复制后再改。
            var pixels = (byte[])WzAssets.Pixels(canvas).Clone();
            foreach (var gap in Gaps) Repair(pixels, w, h, gap);
            // 用户要求移除旧心脏/徽章格，分别使用机器人/符文徽章独立面板。
            // 旧格烘在原图 y=231..262、x=71..102/104..135；
            // 用同图帽子右侧无格、无文字、无人物轮廓的32×32背景原像素覆盖，不自行绘制。
            const int blankX = GridX + 2 * Step, blankY = GridY;
            foreach (int col in new[] { 2, 3 })
                for (int y = 0; y < Cell; y++)
                    Buffer.BlockCopy(pixels, ((blankY + y) * w + blankX) * 4,
                        pixels, ((231 + y) * w + GridX + col * Step) * 4, Cell * 4);
            patched = WzAssets.Upload(pixels, w, h, "EquipBackgrnd");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[EquipBackdrop] 背景修补失败，回退原图：" + e.Message);
        }
        return patched;
    }

    // 行序是 WZ 原始的「自顶向下」，Upload 内部再翻成 Unity 的底向上，这里不用管。
    static void Repair(byte[] px, int w, int h, Gap gap)
    {
        int x0 = GridX + gap.col * Step, y0 = GridY + gap.row * Step;
        if (x0 < 1 || y0 < 1 || x0 + Cell > w || y0 + Cell > h) return;
        // 1) 文字带整行换成本格干净行（种子行落在 y=18..28，绝不用被字污染的本行）。
        //    只修文字——格底/格间的白行是源端烘死的分隔线（这 5 格本来就比自带格矮
        //    1～3px，白行就是格与格的缝），填掉它缝隙就没了（第三版翻车原因）。
        for (int y = BandTop; y <= BandBottom; y++) CopyRow(px, w, x0, y0, y, SeedFor(y));
        // 2) 槽位名：与自带格同一套排版参数（墨迹 23×11、格内 (3,3)、1-bit 点阵）。
        //    先铺描边（核心外扩 1px、4 邻域、含字内空腔），再盖核心，两层同源自带格。
        var glyph = Glyphs[gap.glyph];
        int gx = x0 + 3, gy = y0 + 3;
        for (int j = 0; j < glyph.rows.Length; j++)
        {
            uint bits = glyph.rows[j];
            for (int i = 0; i < glyph.width; i++)
            {
                if ((bits >> i & 1) == 0) continue;
                if (i > 0) Put(px, w, gx + i - 1, gy + j, OutlineColor);
                if (i < glyph.width - 1) Put(px, w, gx + i + 1, gy + j, OutlineColor);
                Put(px, w, gx + i, gy + j - 1, OutlineColor);
                Put(px, w, gx + i, gy + j + 1, OutlineColor);
            }
        }
        for (int j = 0; j < glyph.rows.Length; j++)
        {
            uint bits = glyph.rows[j];
            for (int i = 0; i < glyph.width; i++)
            {
                if ((bits >> i & 1) != 0) Put(px, w, gx + i, gy + j, LabelColor);
            }
        }
    }

    static void Put(byte[] px, int w, int x, int y, Color32 c)
    {
        int di = (y * w + x) * 4;
        px[di] = c.r; px[di + 1] = c.g; px[di + 2] = c.b; px[di + 3] = 255;
    }

    // 文字行往干净区借种子行：y=2..17 → 18..28，越界截到 28。
    static int SeedFor(int y) => Math.Min(SeedHigh, y + SeedLow - BandTop + 2);

    static void CopyRow(byte[] px, int w, int x0, int y0, int y, int seed)
    {
        int src = (y0 + seed) * w, dst = (y0 + y) * w;
        for (int i = 1; i <= Cell - 2; i++)   // x=0 是格线，不动
        {
            int si = (src + x0 + i) * 4, di = (dst + x0 + i) * 4;
            px[di] = px[si]; px[di + 1] = px[si + 1];
            px[di + 2] = px[si + 2]; px[di + 3] = px[si + 3];
        }
    }
}
