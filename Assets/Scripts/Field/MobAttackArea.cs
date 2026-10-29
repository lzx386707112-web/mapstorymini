using MapleDash.Wz;

// Hit area of Mob/<id>.img/attack<N>/info/range. Deliberately free of UnityEngine
// types so tools/boss-check can exercise the geometry without the engine.
//
// Source data (2217 authored range nodes, full scan 2026-10-30):
//   range/{lt,rb}  rectangle .......... 1819 rows   -> attack type 0 / 3 / 4
//   range/{sp,r}   centre + radius ....  399 rows   -> attack type 1 / 2
// The two shapes are mutually exclusive (1 row has both) and `type` tracks them:
// every circle-only row is type 1 or 2, every rect row is type 0/3/4 (see
// monster-skills.md §2.7 for the cross-tab).
// BeiDou.exe parses both inside the same attack-info function:
//   lt -> 0x6805A9, rb -> 0x680621, sp -> 0x680735, r -> 0x680830
// sp is a Point offset from the mob's feet and r is a radius: authored sp values are
// tiny (-14,-32 / 6,-60) while r is 200..280, which rules out a corner reading.
public struct MobAttackArea
{
    // BeiDou.exe adds a fixed slack to the authored radius before comparing:
    //   0x66DC60  mov eax,[esi+0x30]   ; A+0x30 = r
    //   0x66DC63  add eax,0xA          ; +10 px
    //   0x66DC69  fcomp qword [ebp-0x3C]  ; vs sqrt(dx^2+dy^2)
    //   0x66DC6F  jb <miss>
    public const float HitSlack = 10f;

    public float centerX, centerY, halfWidth, halfHeight, radius;
    public bool circle;

    // Point probe. This is NOT how either shape is tested in the source: both
    // branches measure the target's whole body (see Overlaps). Kept for callers
    // that genuinely have a single point (and for the geometry self-checks).
    public bool Contains(float dx, float dy)
    {
        float x = dx - centerX, y = dy - centerY;
        if (circle)
        {
            float rr = radius + HitSlack;
            return x * x + y * y <= rr * rr;
        }
        return x >= -halfWidth && x <= halfWidth && y >= -halfHeight && y <= halfHeight;
    }

    // Both shapes test the TARGET'S BOUNDING BOX, never a single point.
    //
    // Rectangles: 0x95B734 (target collection) walks every object, takes its
    // bounding rect (0x7A38D6) and keeps it when [0xBF04A8](rect, queryRect)
    // reports an overlap. 0x66720A passes the attack's world-space rect.
    //
    // Circles: 0x66DB60..0x66DB68 fetches the target's bounding rect as well
    // (0x7A38D6 for the looked-up entity, 0x45183B for the local player), then
    // 0x66DBDC..0x66DC21 clamps the circle centre into that rect and measures
    //   0x66DC1E  sub eax,[ebp-0x3C]   ; clampedX - centreX
    //   0x66DC21  sub edi,ecx          ; clampedY - centreY
    //   0x66DC42  call 0xA622D4        ; sqrt(dx^2+dy^2)
    //   0x66DC5D/60 cmp r+10           ; 0x66DC63 add eax,0xA
    // so the value compared with r+10 is the distance from the centre to the
    // NEAREST POINT OF THE TARGET'S BODY, i.e. a circle-vs-AABB test. The X side
    // of the clamp uses the edge facing the mob (right-10 when it faces left,
    // left+10 when it faces right), which for a target on that side is the same
    // nearest point; the Y side clamps to [top,bottom] exactly.
    //
    // Coordinates are mob-relative pixels.
    public bool Overlaps(float minX, float minY, float maxX, float maxY)
    {
        if (circle)
        {
            float nx = centerX < minX ? minX - centerX : (centerX > maxX ? centerX - maxX : 0f);
            float ny = centerY < minY ? minY - centerY : (centerY > maxY ? centerY - maxY : 0f);
            float rr = radius + HitSlack;
            return nx * nx + ny * ny <= rr * rr;
        }
        return minX <= centerX + halfWidth && maxX >= centerX - halfWidth
            && minY <= centerY + halfHeight && maxY >= centerY - halfHeight;
    }

    // Wave layout for attack type 3/4 (0x66DF4F..0x66DF76):
    //   ecx = A+0x28            ; start, authored negative (-16..-1)
    //   ecx += waveIndex        ; [ebp+8]
    //   imul ecx, [A+0x34]      ; * spacing
    //   ecx += spacing/2        ; centre of that band
    //   imul ecx, dir(+1/-1)    ; mirror by facing
    //   centreX += ecx
    // The caller only materialises a wave when its bit is set in the mask
    // (0x66DF33 shl 1,waveIndex / 0x66DF35 test), which is why attackCount is
    // bounded by 32 in the parser. Returns the mob-relative pixel offset.
    public static float WaveOffsetX(int start, int waveIndex, int spacing, int dir)
    {
        int band = (start + waveIndex) * spacing + spacing / 2;
        return band * (dir >= 0 ? 1 : -1);
    }

    public static MobAttackArea Rect(int ltX, int ltY, int rbX, int rbY) => new MobAttackArea
    {
        centerX = (ltX + rbX) * 0.5f,
        centerY = (ltY + rbY) * 0.5f,
        halfWidth = (rbX - ltX) * 0.5f,
        halfHeight = (rbY - ltY) * 0.5f,
    };

    public static MobAttackArea Circle(int x, int y, int r) =>
        new MobAttackArea { centerX = x, centerY = y, radius = r, circle = true };

    // Accepts attack<N>/info (unwraps the "range" node itself) or the range node.
    public static bool TryParse(ImgNode info, out MobAttackArea area)
    {
        area = default;
        var range = info?.Get("range") ?? info;
        if (range == null) return false;
        var lt = range.Get("lt");
        var rb = range.Get("rb");
        if (lt != null && rb != null)
        {
            area = Rect(lt.X, lt.Y, rb.X, rb.Y);
            return true;
        }
        var sp = range.Get("sp");
        int r = range.Int("r");
        if (sp == null || r <= 0) return false;
        area = Circle(sp.X, sp.Y, r);
        return true;
    }
}
