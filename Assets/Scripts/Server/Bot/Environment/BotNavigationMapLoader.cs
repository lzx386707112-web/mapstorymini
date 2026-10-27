using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Environment
{
    // BotNavigationMapLoader.java：从打包的原IMG几何读入纯后端地图。
    public sealed class BotNavigationMapLoader
    {
        readonly Dictionary<string, object> maps;
        readonly Func<string, string> readResource;
        string cachedShard;
        Dictionary<string, object> cachedMaps;
        public BotNavigationMapLoader(string json, Func<string, string> readResource = null)
        {
            this.readResource = readResource ?? PlatformRes.LoadText;
            var root = MiniJsonDb.Parse(json) as Dictionary<string, object>;
            maps = root?["maps"] as Dictionary<string, object> ?? throw new ArgumentException("Original map geometry is missing");
        }
        public static BotNavigationMapLoader Load() => new BotNavigationMapLoader(PlatformRes.LoadText("Bot/Maps"));
        public int Count => maps.Count;
        public MapRuntime Geometry(int id)
        {
            var map = ReadMap(id);
            var info = (Dictionary<string, object>)map["info"];
            string link = info.TryGetValue("link", out var l) ? Convert.ToString(l) : "";
            if (!string.IsNullOrEmpty(link))
            {
                map = ReadMap(int.Parse(link)); info = (Dictionary<string, object>)map["info"];
            }
            var result = new MapRuntime { mapId = id, returnMap = Int(info, "returnMap", id), fieldLimit = Int(info, "fieldLimit"),
                swim = Int(info, "swim") != 0, top = Int(info, "VRTop"), bottom = Int(info, "VRBottom"),
                left = Int(info, "VRLeft"), right = Int(info, "VRRight"), minX = 0, maxX = 0, minY = 0, maxY = 0 };
            if (result.top == result.bottom)
            {
                var mini = (Dictionary<string, object>)map["miniMap"];
                if (mini.Count > 0)
                {
                    result.left = -Int(mini, "centerX"); result.top = -Int(mini, "centerY");
                    result.right = result.left + Int(mini, "width"); result.bottom = result.top + Int(mini, "height");
                }
                else { result.left = result.top = -131072; result.right = result.bottom = 131072; }
            }
            if (info.TryGetValue("fs", out var fs)) result.footholdSpeed = Convert.ToSingle(fs);
            foreach (Dictionary<string, object> f in (List<object>)map["footholds"])
            {
                result.AddFoothold(Int(f, "x1"), Int(f, "y1"), Int(f, "x2"), Int(f, "y2"));
                var segment = result.footholds[result.footholds.Count - 1];
                segment.id = Int(f, "id"); segment.previous = Int(f, "prev"); segment.next = Int(f, "next");
                segment.forbidFallDown = Int(f, "forbidFallDown") != 0; segment.layer = Int(f, "layer"); segment.group = Int(f, "group");
            }
            foreach (Dictionary<string, object> r in (List<object>)map["ropes"])
                result.ropes.Add(new MapRope { id = Int(r, "id"), x = Int(r, "x"), y1 = Int(r, "y1"), y2 = Int(r, "y2"),
                    ladder = Int(r, "l") == 1, upperExit = Int(r, "uf") != 0 });
            foreach (Dictionary<string, object> p in (List<object>)map["portals"])
                result.portals.Add(new MapPortal { id = Int(p, "id"), x = Int(p, "x"), y = Int(p, "y"), type = Int(p, "pt"),
                    targetMap = Int(p, "tm"), name = Str(p, "pn"), targetName = Str(p, "tn"), script = Str(p, "script") });
            if (map.TryGetValue("npcs", out var npcs) && npcs is List<object> entries)
                foreach (Dictionary<string, object> n in entries)
                    result.npcs.Add(new MapNpcPosition { id = Int(n, "id"), x = Int(n, "x"), y = Int(n, "y") });
            return result;
        }
        Dictionary<string, object> ReadMap(int id)
        {
            string key = id.ToString();
            if (!maps.TryGetValue(key, out var entry)) throw new ArgumentException("Map data not found for " + id);
            if (entry is Dictionary<string, object> direct) return direct;
            string resource = entry as string ?? throw new ArgumentException("Invalid map resource index for " + id);
            if (cachedShard != resource)
            {
                cachedMaps = MiniJsonDb.Parse(readResource(resource)) as Dictionary<string, object>
                    ?? throw new ArgumentException("Map geometry group is missing: " + resource);
                cachedShard = resource;
            }
            return cachedMaps[key] as Dictionary<string, object> ?? throw new ArgumentException("Map geometry is missing: " + id);
        }
        static int Int(Dictionary<string, object> d, string k, int fallback = 0) => BotItemCatalog.Int(d, k, fallback);
        static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var value) ? value as string ?? "" : "";
    }
}
