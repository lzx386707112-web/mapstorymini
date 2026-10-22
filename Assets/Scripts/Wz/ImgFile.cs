using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

// Standalone GMS IMG reader, based on the source client's tool/wz-python/wzpy.
// No PNG/JSON intermediate files or native Windows DLLs are needed.
namespace MapleDash.Wz
{
    public sealed class ImgNode
    {
        public string Name, Kind;
        public object Value;
        public ImgNode Parent;
        public readonly List<ImgNode> Children = new List<ImgNode>();
        public int Width, Height, Format, X, Y;
        internal byte[] Pixels;
        public ImgNode Get(string path)
        {
            var n = this;
            foreach (var part in path.Split('/'))
            {
                if (part == "" || part == ".") continue;
                if (part == "..") { n = n == null ? null : n.Parent; continue; }
                n = n == null ? null : n.Resolve().Children.Find(c => c.Name == part);
            }
            return n;
        }
        public int Int(string path, int fallback = 0)
        {
            var v = Get(path)?.Value;
            return v == null ? fallback : Convert.ToInt32(v);
        }
        public string Text(string path, string fallback = "") => Get(path)?.Value as string ?? fallback;
        public ImgNode Resolve()
        {
            var n = this;
            for (int i = 0; i < 64; i++)
            {
                if (n == null) throw new InvalidDataException("Missing UOL target: " + Name);
                if (n.Kind != "UOL") return n;
                n = n.Parent?.Get((string)n.Value);
            }
            throw new InvalidDataException("IMG UOL cycle: " + Name);
        }
        public byte[] Rgba()
        {
            if (Kind != "Canvas") throw new InvalidDataException("Not a Canvas: " + Name);
            var compressed = Pixels;
            if (compressed.Length < 2) throw new InvalidDataException("Empty Canvas");
            if (compressed[0] != 0x78 || ((compressed[0] << 8) + compressed[1]) % 31 != 0)
            {
                using (var input = new BinaryReader(new MemoryStream(compressed)))
                using (var output = new MemoryStream())
                {
                    while (input.BaseStream.Position < input.BaseStream.Length)
                    {
                        int length = input.ReadInt32();
                        if (length <= 0 || length > input.BaseStream.Length - input.BaseStream.Position)
                            throw new InvalidDataException("Invalid listWz chunk");
                        var block = input.ReadBytes(length);
                        for (int i = 0; i < length; i++) block[i] ^= ImgFile.KeyByte(i);
                        output.Write(block, 0, block.Length);
                    }
                    compressed = output.ToArray();
                }
            }
            byte[] raw;
            using (var input = new MemoryStream(compressed, 2, compressed.Length - 2))
            using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream()) { inflate.CopyTo(output); raw = output.ToArray(); }
            int count = checked(Width * Height);
            int stride = Format == 2 ? 4 : 2;
            if (Format != 1 && Format != 2 && Format != 513)
                throw new NotSupportedException("Canvas format " + Format);
            // Some original blobs have trailing decoded bytes; consume the declared pixel rectangle (wzpy behavior).
            if (raw.Length < checked(count * stride)) throw new InvalidDataException("Canvas pixel length mismatch " + Name + ": " + raw.Length + " != " + count * stride);
            var rgba = new byte[checked(count * 4)];
            for (int i = 0; i < count; i++)
            {
                int s = i * stride, d = i * 4;
                if (Format == 2)
                {
                    rgba[d] = raw[s + 2]; rgba[d + 1] = raw[s + 1]; rgba[d + 2] = raw[s]; rgba[d + 3] = raw[s + 3];
                }
                else if (Format == 1)
                {
                    rgba[d] = (byte)((raw[s + 1] & 15) * 17); rgba[d + 1] = (byte)((raw[s] >> 4) * 17);
                    rgba[d + 2] = (byte)((raw[s] & 15) * 17); rgba[d + 3] = (byte)((raw[s + 1] >> 4) * 17);
                }
                else
                {
                    int v = raw[s] | raw[s + 1] << 8;
                    rgba[d] = (byte)(((v >> 11) & 31) * 8); rgba[d + 1] = (byte)(((v >> 5) & 63) * 4);
                    rgba[d + 2] = (byte)((v & 31) * 8); rgba[d + 3] = 255;
                }
            }
            return rgba; // top-to-bottom; Unity upload flips rows in the renderer.
        }
    }

    public sealed class ImgFile : IDisposable
    {
        static readonly List<byte> key = new List<byte>();
        static readonly object keyLock = new object();
        internal static byte KeyByte(int index)
        {
            lock (keyLock)
            {
                if (index >= key.Count)
                {
                    var aesKey = new byte[32];
                    // Canonical user key at offsets 0,16,...,112 (see wzpy.crypto).
                    byte[] seed = { 0x13, 0x08, 0x06, 0xb4, 0x1b, 0x0f, 0x33, 0x52 };
                    for (int i = 0; i < 8; i++) aesKey[i * 4] = seed[i];
                    using (var aes = Aes.Create())
                    {
                        aes.Key = aesKey; aes.Mode = CipherMode.ECB; aes.Padding = PaddingMode.None;
                        using (var cipher = aes.CreateEncryptor())
                        {
                            var block = new byte[16];
                            byte[] iv = { 0x4d, 0x23, 0xc7, 0x2b };
                            for (int j = 0; j < 16; j++) block[j] = key.Count == 0 ? iv[j % 4] : key[key.Count - 16 + j];
                            while (key.Count <= index)
                            {
                                block = cipher.TransformFinalBlock(block, 0, 16);
                                key.AddRange(block);
                            }
                        }
                    }
                }
                return key[index];
            }
        }
        // IL2CPP/Android 不内置 1252 代码页，Encoding.GetEncoding(1252) 会抛
        // NotSupportedException，导致地图/角色读取中断、进游戏时相机没建起来（黑屏）。
        // 这里直接按 CP1252 表解码：0x80-0x9F 是 Windows-1252 标点区，其余字节一一对应。
        static readonly char[] Cp1252High =
        {
            '\u20AC','\u0081','\u201A','\u0192','\u201E','\u2026','\u2020','\u2021',
            '\u02C6','\u2030','\u0160','\u2039','\u0152','\u008D','\u017D','\u008F',
            '\u0090','\u2018','\u2019','\u201C','\u201D','\u2022','\u2013','\u2014',
            '\u02DC','\u2122','\u0161','\u203A','\u0153','\u009D','\u017E','\u0178'
        };
        static string DecodeCp1252(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                chars[i] = b >= 0x80 && b <= 0x9F ? Cp1252High[b - 0x80] : (char)b;
            }
            return new string(chars);
        }
        readonly BinaryReader reader;
        public readonly ImgNode Root;
        public ImgFile(string path) : this(File.ReadAllBytes(path), path) { }
        public ImgFile(byte[] bytes, string path)
        {
            reader = new BinaryReader(new MemoryStream(bytes));
            try
            {
                if (reader.ReadByte() != 0x73 || ReadString() != "Property")
                    throw new InvalidDataException("Expected GMS standalone IMG: " + path);
                reader.ReadUInt16();
                Root = new ImgNode { Name = Path.GetFileName(path), Kind = "Property" };
                Properties(Root);
            }
            catch { reader.Dispose(); throw; }
        }
        int Int() { int n = reader.ReadSByte(); return n == -128 ? reader.ReadInt32() : n; }
        string ReadString()
        {
            int sign = reader.ReadSByte();
            if (sign == 0) return "";
            bool unicode = sign > 0;
            int length = sign == -128 || sign == 127 ? reader.ReadInt32() : Math.Abs(sign);
            if (length < 0 || length > reader.BaseStream.Length) throw new InvalidDataException("IMG string length");
            var bytes = reader.ReadBytes(checked(length * (unicode ? 2 : 1)));
            if (bytes.Length != length * (unicode ? 2 : 1)) throw new EndOfStreamException();
            for (int i = 0; i < length; i++)
            {
                if (unicode)
                {
                    int mask = (0xaaaa + i) & 65535;
                    bytes[2 * i] ^= (byte)((mask & 255) ^ KeyByte(2 * i));
                    bytes[2 * i + 1] ^= (byte)((mask >> 8) ^ KeyByte(2 * i + 1));
                }
                else bytes[i] ^= (byte)(((0xaa + i) & 255) ^ KeyByte(i));
            }
            return unicode ? Encoding.Unicode.GetString(bytes) : DecodeCp1252(bytes);
        }
        string StringBlock()
        {
            byte marker = reader.ReadByte();
            if (marker == 0 || marker == 0x73) return ReadString();
            if (marker != 1 && marker != 0x1b) throw new InvalidDataException("IMG string marker " + marker);
            uint offset = reader.ReadUInt32(); long keep = reader.BaseStream.Position;
            reader.BaseStream.Position = offset;
            string value = ReadString(); reader.BaseStream.Position = keep; return value;
        }
        void Properties(ImgNode parent)
        {
            int count = Int();
            if (count < 0 || count > reader.BaseStream.Length) throw new InvalidDataException("IMG property count");
            for (int i = 0; i < count; i++)
            {
                var n = new ImgNode { Name = StringBlock(), Parent = parent };
                byte tag = reader.ReadByte();
                switch (tag)
                {
                    case 0: break;
                    case 2: case 11: n.Value = reader.ReadInt16(); break;
                    case 3: case 19: n.Value = Int(); break;
                    case 20: int v = reader.ReadSByte(); n.Value = v == -128 ? reader.ReadInt64() : (long)v; break;
                    case 4: n.Value = reader.ReadByte() == 128 ? reader.ReadSingle() : 0f; break;
                    case 5: n.Value = reader.ReadDouble(); break;
                    case 8: n.Value = StringBlock(); break;
                    case 9:
                        uint size = reader.ReadUInt32(); long end = reader.BaseStream.Position + size;
                        if (end > reader.BaseStream.Length) throw new EndOfStreamException();
                        Extended(n, end);
                        if (reader.BaseStream.Position > end) throw new InvalidDataException("IMG block overflow");
                        reader.BaseStream.Position = end;
                        break;
                    default: throw new NotSupportedException("IMG property tag " + tag);
                }
                parent.Children.Add(n);
            }
        }
        void Extended(ImgNode n, long end)
        {
            n.Kind = StringBlock();
            switch (n.Kind)
            {
                case "Property": reader.ReadUInt16(); Properties(n); break;
                case "Shape2D#Convex2D":
                    int count = Int();
                    if (count < 0 || count > reader.BaseStream.Length) throw new InvalidDataException("Convex point count");
                    for (int i = 0; i < count; i++)
                    {
                        var point = new ImgNode { Name = i.ToString(), Parent = n, Kind = StringBlock() };
                        if (point.Kind != "Shape2D#Vector2D") throw new NotSupportedException("Convex child " + point.Kind);
                        point.X = Int(); point.Y = Int(); n.Children.Add(point);
                    }
                    break;
                case "Shape2D#Vector2D": n.X = Int(); n.Y = Int(); break;
                case "UOL": reader.ReadByte(); n.Value = StringBlock(); break;
                case "Canvas":
                    reader.ReadByte();
                    if (reader.ReadByte() == 1) { reader.ReadUInt16(); Properties(n); }
                    n.Width = Int(); n.Height = Int(); n.Format = Int() + reader.ReadByte();
                    reader.ReadInt32(); reader.ReadInt32(); reader.ReadByte();
                    n.Pixels = reader.ReadBytes(checked((int)(end - reader.BaseStream.Position)));
                    break;
                // Audio/video are opaque in this map-rendering prototype.
                case "Sound_DX8": case "Sound": case "Canvas#Video": case "RawData": break;
                default: throw new NotSupportedException("IMG extended type " + n.Kind);
            }
        }
        public void Dispose() { reader.Dispose(); }
    }
}
