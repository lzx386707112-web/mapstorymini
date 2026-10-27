using System;
using System.Collections.Generic;
using System.Globalization;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    public sealed class BotTownPresenceConfig
    {
        public sealed class MapShare
        {
            public int mapId, count;
            public BotTownOverrides overrides;
        }
        public sealed class TownEntry
        {
            public string name, dialogueOverride;
            public int levelLo, levelHi, wanderers;
            public readonly List<MapShare> maps = new List<MapShare>();
            public int MainMapId => maps.Count == 0 ? -1 : maps[0].mapId;
        }
        readonly BotResources resources;
        List<TownEntry> cached;
        public BotTownPresenceConfig(BotResources resources) { this.resources = resources; }
        public IReadOnlyList<TownEntry> Towns => cached ??= Load();
        public IReadOnlyList<TownEntry> Reload() => cached = Load();
        public HashSet<int> AllTownMapIds()
        { var ids = new HashSet<int>(); foreach (var t in Towns) foreach (var m in t.maps) ids.Add(m.mapId); return ids; }
        public BotTownOverrides OverridesFor(int mapId)
        {
            foreach (var t in Towns) foreach (var m in t.maps) if (m.mapId == mapId) return m.overrides;
            // 原TownPinsStore.load/forMap均返回空集合，addPin为空方法。
            return new BotTownOverrides();
        }
        List<TownEntry> Load()
        {
            var result = new List<TownEntry>();
            try
            {
                var root = resources.ReadData("soloMapling/ArtificialPlayer/BotTownSystem/TownPresence.yaml") as Dictionary<string, object>;
                foreach (var node in List(Get(root, "towns")))
                {
                    if (!(node is Dictionary<string, object> t)) continue;
                    int lo = Int(Get(t, "level_lo"), 10);
                    var town = new TownEntry { name = Str(Get(t, "name"), "town"), levelLo = lo,
                        levelHi = Int(Get(t, "level_hi"), lo), wanderers = Int(Get(t, "wanderers"), 0), dialogueOverride = Str(Get(t, "dialogue"), null) };
                    foreach (var entry in List(Get(t, "maps")))
                    {
                        if (!(entry is Dictionary<string, object> m)) continue;
                        int id = Int(Get(m, "map"), -1), count = Int(Get(m, "count"), 0);
                        if (id <= 0 || count <= 0) continue;
                        var ov = new BotTownOverrides();
                        Zones(Get(m, "ban"), 1, ov.ban); Zones(Get(m, "boost"), 2, ov.boost);
                        foreach (var pin in List(Get(m, "pins"))) if (pin is Dictionary<string, object> p)
                        {
                            int x = Int(Get(p, "x"), int.MinValue), y = Int(Get(p, "y"), int.MinValue);
                            if (x != int.MinValue && y != int.MinValue) ov.pins.Add(new Point(x, y));
                        }
                        town.maps.Add(new MapShare { mapId = id, count = count, overrides = ov });
                    }
                    if (town.maps.Count > 0) result.Add(town);
                }
            }
            catch (Exception) { return new List<TownEntry>(); }
            return result;
        }
        static void Zones(object node, double multiplier, List<BotTownOverrides.Zone> result)
        {
            foreach (var item in List(node)) if (item is Dictionary<string, object> z)
            {
                double mult = double.TryParse(Str(Get(z, "mult"), ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : multiplier;
                result.Add(new BotTownOverrides.Zone(Int(Get(z, "x1"), int.MinValue), Int(Get(z, "y1"), int.MinValue),
                    Int(Get(z, "x2"), int.MaxValue), Int(Get(z, "y2"), int.MaxValue), mult));
            }
        }
        static object Get(Dictionary<string, object> node, string key) => node != null && node.TryGetValue(key, out var value) ? value : null;
        static IEnumerable<object> List(object node) => node as List<object> ?? (IEnumerable<object>)Array.Empty<object>();
        static string Str(object value, string fallback) => value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture);
        static int Int(object value, int fallback)
        {
            if (value is long l) return unchecked((int)l);
            if (value is double d) return (int)d;
            return int.TryParse(Str(value, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
        }
    }
}
