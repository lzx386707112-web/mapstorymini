using System;
using Server.Field;

// BeiDou.exe 957B74: direction keys, level range, ±80px horizontal landing search.
public static class NativeTeleportMovement
{
    public static bool IsSkill(int id) => id == 2101002 || id == 2201002 || id == 2301001 ||
        id == 12101003 || id == 22101001;

    public static bool Destination(MapRuntime map, int x, int y, int horizontal, int vertical, int range,
        out int targetX, out int targetY)
    {
        targetX = x; targetY = y;
        if (map == null || (map.fieldLimit & 2) != 0 || range <= 0) return false;
        horizontal = Math.Sign(horizontal); vertical = Math.Sign(vertical); // WZ y points down.
        int tx = x + horizontal * range;
        if (horizontal == 0 && vertical == 0 || tx < map.left || tx > map.right) return false;
        FootholdSegment original = null, chosen = null;
        int originalDistance = int.MaxValue, best = int.MaxValue, result = y;
        foreach (var f in map.footholds)
        {
            if (f.x1 >= f.x2 || x < f.x1 || x > f.x2) continue;
            int fy = Height(f, x), distance = Math.Abs(fy - y);
            if (distance < originalDistance) { original = f; originalDistance = distance; }
        }
        foreach (var f in map.footholds)
        {
            if (f.x1 >= f.x2 || tx < f.x1 || tx > f.x2) continue;
            int fy = Height(f, tx), score;
            if (horizontal != 0)
            {
                score = Math.Abs(fy - y);
                if (score > 80) continue;
            }
            else if (vertical < 0)
            {
                if (fy < y - range || fy >= y || ReferenceEquals(f, original)) continue;
                score = fy - (y - range);
            }
            else
            {
                if (fy <= y || fy > y + range * 2 || ReferenceEquals(f, original)) continue;
                // FindAbove(target), then FindBelow(target) if the former is the support segment.
                score = fy <= y + range ? y + range - fy : range + fy - (y + range);
            }
            if (score < best) { chosen = f; best = score; result = fy; }
        }
        if (chosen == null) return false;
        targetX = tx; targetY = result - 1;
        return true;
    }
    static int Height(FootholdSegment f, int x)
        => (int)(f.y1 + (f.y2 - f.y1) * ((x - f.x1) / (double)(f.x2 - f.x1)));
}
