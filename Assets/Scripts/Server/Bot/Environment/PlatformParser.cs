using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Server.Bot.Environment
{
    // 原端 Environment/PlatformParser.java。磁盘/Android资源访问由纯数据入口注入。
    public sealed class PlatformParser
    {
        public const string ResourcePath = "soloMapling/ArtificialPlayer/BotMovementSystem/movementDataPackets";
        readonly Func<string, string> readText;
        readonly Func<string, IEnumerable<string>> listStems;
        readonly Dictionary<string, Platform> cache = new Dictionary<string, Platform>();
        public PlatformParser(Func<string, string> readText, Func<string, IEnumerable<string>> listStems)
        { this.readText = readText; this.listStems = listStems; }

        public List<BotPoint> ParseCoordinates(int mapId, string fileName)
        {
            var points = ReadCoordinates(readText?.Invoke(ResourcePath + "/map" + mapId + "/" + fileName + ".csv"));
            return points.Count == 0 ? FallbackCoordinates(mapId, fileName) : points;
        }
        public Platform ParsePlatform(int mapId, string fileName)
        {
            string key = mapId + ":" + fileName;
            if (!cache.TryGetValue(key, out var p)) cache[key] = p = OrganizePlatform(ParseCoordinates(mapId, fileName));
            return p;
        }
        public Platform ParsePlatform(int mapId, string fileName, Platform.Type type)
            => OrganizePlatform(ParseCoordinates(mapId, fileName), type);
        public List<string> ListPlatformIds(int mapId)
        {
            var ids = (listStems?.Invoke(ResourcePath + "/map" + mapId) ?? Array.Empty<string>())
                .Where(s => !s.Contains("/") && s != "depreciated").ToList();
            if (ids.Count == 0 && mapId == 910000000)
                ids.AddRange(new[] { "m1", "m2", "m3", "m4", "m5", "c1-2a", "c1-2b", "c1-5a", "c2-1b", "c2-3a", "c3-2a", "c3-4a", "c3-4b", "c4-3b", "c5-1a" });
            return ids;
        }
        public static List<BotPoint> ReadCoordinates(string text)
        {
            var points = new List<BotPoint>();
            if (text == null) return points;
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || !line.Contains(",")) continue;
                var parts = line.Split(',');
                if (parts.Length >= 3 && int.TryParse(parts[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x)
                    && int.TryParse(parts[2].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int y))
                    points.Add(new BotPoint(x, y));
            }
            return points;
        }
        public static Platform OrganizePlatform(List<BotPoint> coordinates)
        {
            var type = coordinates != null && coordinates.Count > 0 && coordinates.Max(p => p.y) - coordinates.Min(p => p.y) > 50
                ? Platform.Type.SLOPED : Platform.Type.FLAT;
            return OrganizePlatform(coordinates, type);
        }
        public static Platform OrganizePlatform(List<BotPoint> coordinates, Platform.Type type)
        {
            if (coordinates == null || coordinates.Count == 0)
                return new Platform(0, 0, 0, new List<BotPoint>(), type);
            var sorted = coordinates.Distinct().OrderBy(p => p.x).ToList();
            int y = type == Platform.Type.FLAT ? ModeY(coordinates) : (int)coordinates.Average(p => p.y);
            return new Platform(sorted[0].x, sorted[sorted.Count - 1].x, y, sorted, type);
        }
        static int ModeY(List<BotPoint> points)
        {
            // Java HashMap<Integer,...> 按桶遍历；同频时保留首次遇到的桶项。
            var frequency = new Dictionary<int, int>();
            foreach (var p in points) { frequency.TryGetValue(p.y, out int n); frequency[p.y] = n + 1; }
            int capacity = 16;
            while (frequency.Count > capacity * 3 / 4) capacity *= 2;
            int best = 0, count = -1;
            foreach (var pair in frequency.OrderBy(p => (p.Key ^ (int)((uint)p.Key >> 16)) & (capacity - 1)))
                if (pair.Value > count) { best = pair.Key; count = pair.Value; }
            return best;
        }
        static List<BotPoint> FallbackCoordinates(int mapId, string fileName)
        {
            var points = new List<BotPoint>();
            if (mapId != 910000000) return points;
            int min, max, y;
            switch (fileName)
            {
                case "m1": min = -400; max = 1400; y = 34; break;
                case "m2": min = -400; max = 1400; y = -266; break;
                case "m5": min = -350; max = 500; y = 34; break;
                default: return points;
            }
            for (int x = min; x <= max; x += 80) points.Add(new BotPoint(x, y));
            return points;
        }
    }
}
