using System;
using System.Collections.Generic;

namespace Server.Bot.Environment
{
    // java.awt.Point 的值语义适配，保持地图像素坐标、Y向下。
    public readonly struct BotPoint : IEquatable<BotPoint>
    {
        public readonly int x, y;
        public BotPoint(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(BotPoint other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is BotPoint p && Equals(p);
        public override int GetHashCode() => unchecked(x * 397 ^ y);
    }

    // 原端 Environment/Platform.java；斜面生成只选录制参考点。
    public sealed class Platform
    {
        public enum Type { FLAT, SLOPED }
        public readonly int minX, maxX, baseY;
        public readonly List<BotPoint> sortedPoints;
        public readonly Type type;
        public Platform(int minX, int maxX, int baseY, List<BotPoint> sortedPoints, Type type)
        {
            this.minX = minX; this.maxX = maxX; this.baseY = baseY;
            this.sortedPoints = sortedPoints; this.type = type;
        }
        public int Width => maxX - minX;
        public bool IsFlat => type == Type.FLAT;
        public bool IsSloped => type == Type.SLOPED;
        public int GetYAtX(int x)
        {
            if (IsFlat || sortedPoints.Count == 0) return baseY;
            x = Math.Max(minX, Math.Min(maxX, x));
            BotPoint? left = null, right = null;
            foreach (var p in sortedPoints)
            {
                if (p.x <= x) left = p;
                if (p.x >= x && !right.HasValue) right = p;
            }
            if (!left.HasValue) return sortedPoints[0].y;
            if (!right.HasValue) return sortedPoints[sortedPoints.Count - 1].y;
            if (left.Value.x == right.Value.x) return left.Value.y;
            double ratio = (double)(x - left.Value.x) / (right.Value.x - left.Value.x);
            return (int)(left.Value.y + ratio * (right.Value.y - left.Value.y));
        }
    }
}
