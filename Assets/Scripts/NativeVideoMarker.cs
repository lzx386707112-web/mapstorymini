// KaringSceneCompat.dll 3d2c5373: decoded A8R8G8B8 marker signature, top-down RGBA.
public static class NativeVideoMarker
{
    static bool Pixel(byte[] p, int i, int r, int g, int b)
        => p[i*4] == r && p[i*4+1] == g && p[i*4+2] == b && p[i*4+3] == 255;

    public static int Decode(byte[] rgba, int width, int height)
    {
        if (width < 7 || width > 8 || height < 5 || height > 8 || rgba == null || rgba.Length < width*height*4) return -1;
        int r = rgba[16], g = rgba[17], b = rgba[18];
        if (Pixel(rgba,0,34,17,68) && Pixel(rgba,1,68,85,119) &&
            Pixel(rgba,2,153,170,187) && Pixel(rgba,3,204,221,221))
            return r%17 == 0 && r >= 17 && r <= 238 ? r/17 : -1;
        if (Pixel(rgba,0,17,34,68) && Pixel(rgba,1,85,102,119) &&
            Pixel(rgba,2,136,153,170) && Pixel(rgba,3,187,204,238))
        {
            if (r%17 != 0 || r < 17 || g%17 != 0 || b != 221) return -1;
            if (g == 68) return 14+r/17;
            if (g == 85 && r <= 68) return 29+r/17;
        }
        if (Pixel(rgba,0,51,85,119) && Pixel(rgba,1,102,136,153) &&
            Pixel(rgba,2,170,187,204) && Pixel(rgba,3,221,238,255))
            return r%17 == 0 && r >= 17 && r <= 68 && g == 136 && b == 238 ? 33+r/17 : -1;
        return -1;
    }
}
