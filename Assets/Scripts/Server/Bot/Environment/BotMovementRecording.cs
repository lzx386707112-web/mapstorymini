using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Server.Bot.Environment
{
    // InPacketReader.java。录制文件帧头是Java DataOutputStream的大端序，内部封包原样保存。
    public static class BotMovementRecording
    {
        public sealed class Packet
        {
            public long timestamp;
            public byte[] data;
        }
        public readonly struct Command
        {
            public readonly sbyte command, stance;
            public readonly short x, y, vx, vy, foothold, duration;
            public Command(sbyte command, short x, short y, short vx, short vy, short foothold, sbyte stance, short duration)
            { this.command = command; this.x = x; this.y = y; this.vx = vx; this.vy = vy; this.foothold = foothold; this.stance = stance; this.duration = duration; }
        }
        public sealed class RawPacket
        {
            public long timestamp;
            public int declaredCommands;
            public List<Command> commands;
        }
        public static string ResourcePath(int mapId, string name, string extension)
        {
            if (name.StartsWith("/", StringComparison.Ordinal)) name = name.Substring(1);
            return PlatformParser.ResourcePath + "/map" + mapId + "/" + name + "." + extension;
        }
        public static List<Packet> ReadBinary(byte[] bytes)
        {
            if (bytes == null) throw new FileNotFoundException("Movement recording missing");
            var result = new List<Packet>();
            int offset = 0;
            while (bytes.Length - offset >= 12)
            {
                long timestamp = 0;
                for (int i = 0; i < 8; i++) timestamp = unchecked((timestamp << 8) | bytes[offset++]);
                int length = 0;
                for (int i = 0; i < 4; i++) length = unchecked((length << 8) | bytes[offset++]);
                if (length < 0) throw new InvalidDataException("Negative movement packet length");
                // 源端EOFException结束读取，最后一帧截断时不加入结果。
                if (length > bytes.Length - offset) break;
                var data = new byte[length]; Array.Copy(bytes, offset, data, 0, length); offset += length;
                result.Add(new Packet { timestamp = timestamp, data = data });
            }
            return result;
        }
        public static List<RawPacket> ReadCsv(string text)
        {
            if (text == null) throw new FileNotFoundException("Movement recording missing");
            using var reader = new StringReader(text);
            var result = new List<RawPacket>();
            string previous = null, line;
            while ((line = previous ?? reader.ReadLine()) != null)
            {
                previous = null;
                long timestamp = long.Parse(line.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                line = reader.ReadLine();
                if (line == null) break;
                int count = int.Parse(line.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                var commands = new List<Command>();
                for (int i = 0; i < count; i++)
                {
                    line = reader.ReadLine();
                    if (line == null) break;
                    if (!line.Contains(",")) { previous = line; break; }
                    var fields = line.Split(',');
                    if (fields.Length != 8) throw new FormatException("Invalid movement packet: " + line);
                    commands.Add(new Command(ParseByte(fields[0]), ParseShort(fields[1]), ParseShort(fields[2]),
                        ParseShort(fields[3]), ParseShort(fields[4]), ParseShort(fields[5]), ParseByte(fields[6]), ParseShort(fields[7])));
                }
                result.Add(new RawPacket { timestamp = timestamp, declaredCommands = count, commands = commands });
            }
            return result;
        }
        static sbyte ParseByte(string value) => sbyte.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        static short ParseShort(string value) => short.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }
}
