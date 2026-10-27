using System;
using System.Collections.Generic;
using System.IO;

namespace Server.Bot.Environment
{
    // MovementPacketConstructor / AbstractMovementPacketHandler.updatePositionBot.
    // BIN帧头为大端，但内部移动包为小端；不能直接按录制帧头字节序解析。
    public static class BotMovementPacket
    {
        static BinaryReader Open(byte[] bytes)
        {
            var r = new BinaryReader(new MemoryStream(bytes, false));
            Skip(r, 9); return r;
        }
        static void Skip(BinaryReader r, int n)
        { if (r.ReadBytes(n).Length != n) throw new EndOfStreamException(); }
        public static List<BotMovementRecording.Command> Deconstruct(byte[] bytes)
        {
            using var r = Open(bytes);
            var result = new List<BotMovementRecording.Command>();
            int count = r.ReadSByte();
            for (int i = 0; i < count; i++)
            {
                sbyte c = r.ReadSByte(), stance = 0;
                short x = 0, y = 0, vx = 0, vy = 0, fh = 0, duration = 0;
                switch (c)
                {
                    case 0: case 5: case 17:
                        x = r.ReadInt16(); y = r.ReadInt16(); vx = r.ReadInt16(); vy = r.ReadInt16();
                        fh = r.ReadInt16(); stance = r.ReadSByte(); duration = r.ReadInt16(); break;
                    case 1: case 2: case 6: case 12: case 13: case 16: case 18: case 19: case 20: case 22:
                        Skip(r, 4); stance = r.ReadSByte(); duration = r.ReadInt16(); break;
                    case 3: case 4: case 7: case 8: case 9: case 11:
                        Skip(r, 8); stance = r.ReadSByte(); break;
                    case 14: Skip(r, 9); break;
                    case 10: r.ReadByte(); continue;
                    case 15: Skip(r, 12); stance = r.ReadSByte(); r.ReadInt16(); break;
                    case 21: Skip(r, 3); break;
                    default: continue;
                }
                result.Add(new BotMovementRecording.Command(c, x, y, vx, vy, fh, stance, duration));
            }
            return result;
        }
        // 源端重建器无论command类型均写完整14字节，此处保留其行为。
        public static byte[] Reconstruct(IReadOnlyList<BotMovementRecording.Command> commands)
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(new byte[9]); w.Write(unchecked((byte)commands.Count));
            foreach (var c in commands)
            { w.Write(c.command); w.Write(c.x); w.Write(c.y); w.Write(c.vx); w.Write(c.vy); w.Write(c.foothold); w.Write(c.stance); w.Write(c.duration); }
            return stream.ToArray();
        }
        public static byte[] Offset(byte[] bytes, int x, int y, int foothold)
        {
            var commands = Deconstruct(bytes);
            if (commands.Count == 0) throw new InvalidDataException("Empty movement offset");
            int dx = commands[0].x - x, dy = commands[0].y - y;
            for (int i = 0; i < commands.Count; i++)
            {
                var c = commands[i];
                commands[i] = new BotMovementRecording.Command(c.command, unchecked((short)(c.x - dx)),
                    unchecked((short)(c.y - dy)), c.vx, c.vy, unchecked((short)foothold), c.stance, c.duration);
            }
            return Reconstruct(commands);
        }
        // 返回广播区（count+实际消费的命令），服务端位置立即更新到末条命令。
        public static byte[] Apply(byte[] bytes, BotCharacter actor)
        {
            using var r = Open(bytes);
            int count = r.ReadSByte();
            if (count < 1) throw new InvalidDataException("Empty movement");
            for (int i = 0; i < count; i++)
            {
                int c = r.ReadSByte();
                switch (c)
                {
                    case 0: case 5: case 17:
                        actor.x = r.ReadInt16(); actor.y = r.ReadInt16(); Skip(r, 6);
                        actor.stance = r.ReadSByte(); r.ReadInt16(); break;
                    case 1: case 2: case 6: case 12: case 13: case 16: case 18: case 19: case 20: case 22:
                        actor.x += r.ReadInt16(); actor.y += r.ReadInt16();
                        actor.stance = r.ReadSByte(); r.ReadInt16(); break;
                    case 3: case 4: case 7: case 8: case 9:
                        actor.x = r.ReadInt16(); actor.y = r.ReadInt16(); Skip(r, 4); actor.stance = r.ReadSByte(); break;
                    case 11: Skip(r, 8); actor.stance = r.ReadSByte(); break;
                    case 14: Skip(r, 9); break;
                    case 10: r.ReadByte(); break;
                    case 15:
                        actor.x = r.ReadInt16(); actor.y = r.ReadInt16(); Skip(r, 8);
                        actor.stance = r.ReadSByte(); r.ReadInt16(); break;
                    case 21: Skip(r, 3); break;
                    default: throw new InvalidDataException("Unknown movement command: " + c);
                }
            }
            actor.facing = (actor.stance & 1) == 0 ? 1 : -1;
            int length = (int)r.BaseStream.Position - 9;
            var broadcast = new byte[length]; Array.Copy(bytes, 9, broadcast, 0, length);
            return broadcast;
        }
    }
}
