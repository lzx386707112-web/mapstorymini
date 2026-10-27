using System;
using System.IO;

// Container port of tool/client-video/McvFormat.cpp. This parses compressed
// payload locations and timing; it does not pretend Unity VideoPlayer decodes MCV.
public sealed class McvVideo
{
    public sealed class Frame
    {
        public uint colorOffset, colorSize, alphaOffset, alphaSize;
        public ulong startNanoseconds, delayNanoseconds;
    }

    public uint FourCc { get; private set; }
    public ushort Width { get; private set; }
    public ushort Height { get; private set; }
    public byte Flags { get; private set; }
    public ulong DurationNanoseconds { get; private set; }
    public Frame[] Frames { get; private set; }

    static InvalidDataException Invalid(string detail) => new InvalidDataException("MCV: " + detail);

    public static McvVideo Parse(byte[] data)
    {
        if (data == null || data.Length < 36) throw Invalid("shorter than fixed header");
        using (var stream = new MemoryStream(data, false))
        using (var reader = new BinaryReader(stream))
        {
            if (reader.ReadUInt32() != 0x3056434D) throw Invalid("signature");
            reader.ReadUInt16(); // Version bytes: original parser does not validate these.
            ushort headerLength = reader.ReadUInt16();
            var video = new McvVideo
            {
                FourCc = reader.ReadUInt32() ^ 0xA5A5A5A5,
                Width = reader.ReadUInt16(), Height = reader.ReadUInt16()
            };
            uint count = reader.ReadUInt32();
            video.Flags = reader.ReadByte();
            stream.Position = 24;
            ulong unit = reader.ReadUInt64();
            uint defaultDelay = reader.ReadUInt32();
            if (headerLength < 36 || headerLength > data.Length) throw Invalid("header length");
            if (video.FourCc != 0x30385056 && video.FourCc != 0x30395056) throw Invalid("codec");
            if (video.Width == 0 || video.Height == 0 || video.Width > 8192 || video.Height > 8192) throw Invalid("dimensions");
            if (count == 0 || count > 100000) throw Invalid("frame count");
            if ((video.Flags & ~7) != 0) throw Invalid("flags");
            long tableSize = (long)count * (8 + ((video.Flags & 1) != 0 ? 8 : 0) +
                ((video.Flags & 2) != 0 ? 4 : 0) + ((video.Flags & 4) != 0 ? 8 : 0));
            if (tableSize > data.Length - headerLength) throw Invalid("truncated tables");
            video.Frames = new Frame[count];
            stream.Position = headerLength;
            for (int i = 0; i < video.Frames.Length; i++)
                video.Frames[i] = new Frame { colorOffset = reader.ReadUInt32(), colorSize = reader.ReadUInt32() };
            if ((video.Flags & 1) != 0)
                foreach (var frame in video.Frames)
                { frame.alphaOffset = reader.ReadUInt32(); frame.alphaSize = reader.ReadUInt32(); }
            try
            {
                foreach (var frame in video.Frames)
                    frame.delayNanoseconds = checked(((video.Flags & 2) != 0 ? reader.ReadUInt32() : defaultDelay) * unit);
                if ((video.Flags & 4) != 0)
                    foreach (var frame in video.Frames) frame.startNanoseconds = checked(reader.ReadUInt64() * unit);
                else
                {
                    ulong start = 0;
                    foreach (var frame in video.Frames)
                    { frame.startNanoseconds = start; start = checked(start + frame.delayNanoseconds); }
                }
                uint dataStart = checked((uint)stream.Position);
                foreach (var frame in video.Frames)
                {
                    frame.colorOffset = Payload(dataStart, frame.colorOffset, frame.colorSize, data.Length);
                    if (frame.alphaSize != 0)
                        frame.alphaOffset = Payload(dataStart, frame.alphaOffset, frame.alphaSize, data.Length);
                    video.DurationNanoseconds = Math.Max(video.DurationNanoseconds,
                        checked(frame.startNanoseconds + frame.delayNanoseconds));
                }
            }
            catch (OverflowException) { throw Invalid("integer overflow"); }
            return video;
        }
    }

    static uint Payload(uint dataStart, uint offset, uint size, int length)
    {
        uint absolute = checked(dataStart + offset);
        if (absolute > length || size > length - absolute) throw Invalid("payload out of bounds");
        return absolute;
    }
}
