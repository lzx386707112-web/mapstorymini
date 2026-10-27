// 后台图鉴图标编码：WZ 画布的 RGBA 字节 → PNG。
//
// 源端 gms-server 的图标是预生成 atlas（ImageIO 切图缓存），本项目改为运行时直读 WZ，
// 从 ImgNode.Rgba() 拿到 RGBA 后就地编码成 PNG 给前端 <img src="/assets/.../*.png">。
//
// 为什么手写编码器：
//   - UnityEngine.Texture2D.EncodeToPNG 要先建贴图并依赖 Unity 类型，Admin 模块想保持纯 C#；
//   - IL2CPP/Android 没有 System.Drawing / ImageSharp；
//   - PNG 结构本身极小：签名 + IHDR + IDAT + IEND，压缩直接用 System.IO.Compression.DeflateStream
//     （它只输出裸 deflate 流，zlib 的 2 字节头与 adler32 尾、以及 PNG 块的 crc32 都要自己拼）。
// 滤波统一用 0（None）：图标都是 32x32 级别的小图，压缩率差异可忽略，实现最简单、最稳。
//
// 纯 C#、不引用 UnityEngine，可离线单测（tools/ 下的离线工程可直接编译本文件）。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

public static class AdminPng
{
    // RGBA（每像素 4 字节、行连续、上→下）→ PNG 字节。宽高与 rgba 长度不符直接抛异常。
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("PNG 尺寸非法: " + width + "x" + height);
        if (rgba == null || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA 长度与尺寸不符");

        var png = new List<byte>(rgba.Length / 2 + 256);
        // PNG 签名
        png.AddRange(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // IHDR：宽高、8bit、colortype 6（RGBA）、压缩 0、滤波 0、非隔行
        var ihdr = new byte[13];
        WriteInt(ihdr, 0, width);
        WriteInt(ihdr, 4, height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(png, "IHDR", ihdr);

        // 图像数据：每行前置滤波字节 0，再跟该行 RGBA
        int stride = checked(width * 4);
        var filtered = new byte[checked(height * (stride + 1))];
        for (int y = 0; y < height; y++)
        {
            int dst = y * (stride + 1);
            filtered[dst] = 0;
            Buffer.BlockCopy(rgba, y * stride, filtered, dst + 1, stride);
        }
        Chunk(png, "IDAT", Zlib(filtered));

        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    // zlib 格式 = 0x78 0x9C（CMF/FLG，(0x78<<8|0x9C) % 31 == 0）+ 裸 deflate + adler32（大端）。
    static byte[] Zlib(byte[] raw)
    {
        using (var output = new MemoryStream(raw.Length / 2 + 32))
        {
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                deflate.Write(raw, 0, raw.Length);
            uint adler = Adler32(raw);
            output.WriteByte((byte)(adler >> 24));
            output.WriteByte((byte)(adler >> 16));
            output.WriteByte((byte)(adler >> 8));
            output.WriteByte((byte)adler);
            return output.ToArray();
        }
    }

    static void Chunk(List<byte> png, string type, byte[] data)
    {
        int len = data.Length;
        png.Add((byte)(len >> 24)); png.Add((byte)(len >> 16));
        png.Add((byte)(len >> 8)); png.Add((byte)len);
        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        png.AddRange(typeBytes);
        png.AddRange(data);
        // crc32 覆盖「块类型 + 块数据」，一次性算完再取反（中途不能异或）
        uint crc = Crc32Continue(0xFFFFFFFFu, typeBytes, 0, 4);
        crc = Crc32Continue(crc, data, 0, data.Length) ^ 0xFFFFFFFFu;
        png.Add((byte)(crc >> 24)); png.Add((byte)(crc >> 16));
        png.Add((byte)(crc >> 8)); png.Add((byte)crc);
    }

    // —————————— crc32（IEEE 802.3，反射多项式 0xEDB88320） ——————————
    static readonly uint[] crcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Crc32(byte[] data, int offset, int length)
        => Crc32Continue(0xFFFFFFFFu, data, offset, length) ^ 0xFFFFFFFFu;

    static uint Crc32Continue(uint crc, byte[] data, int offset, int length)
    {
        for (int i = 0; i < length; i++)
            crc = crcTable[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    // —————————— adler32（zlib 校验和） ——————————
    public static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        for (int i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }

    static void WriteInt(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }
}
