using System;
using System.Collections.Generic;
using System.Globalization;
using Server.Field;

namespace Server.Bot.Environment
{
    // BotNavigationMapLoader.java：从打包的原IMG几何读入纯后端地图。
    //
    // 分帧要点：Maps.json 只是索引，真正的几何按分片存放（Bot/Maps/<n>，单个 0.3~1.3MB）。
    // 以前 ReadMap 里现解、而且只缓存「最后一个分片」——城镇游荡一上来要连扫七个分片，
    // 全砸在进游戏后的同一帧里，就是那段卡顿。现在：
    //   · 解析交给 BotWarmup 的后台线程，TryGeometry 永不在主线程解 JSON；
    //   · 分片保留最近 MaxCachedShards 个，同一轮巡逻不会反复解同一个分片；
    //   · 取走后释放 BotWarmup 里的引用，避免十几个分片常驻内存。
    public sealed class BotNavigationMapLoader
    {
        // 同时驻留的分片数。单个分片解出来是 5~10MB，安卓上不能多留：
        // 2 个够覆盖「当前地图 + 下一张城镇/相邻图」的切换，又不会把内存顶上去。
        const int MaxCachedShards = 2;

        readonly Dictionary<string, object> maps;
        readonly Func<string, string> readResource;
        readonly Dictionary<string, Dictionary<string, object>> shardCache =
            new Dictionary<string, Dictionary<string, object>>();
        readonly List<string> shardOrder = new List<string>();

        public BotNavigationMapLoader(string json, Func<string, string> readResource = null)
        {
            this.readResource = readResource ?? PlatformRes.LoadText;
            var root = MiniJsonDb.Parse(json) as Dictionary<string, object>;
            maps = root?["maps"] as Dictionary<string, object> ?? throw new ArgumentException("Original map geometry is missing");
        }
        // 用 BotWarmup 已解析好的根节点，避免进图那一帧再同步解 306KB 的 Maps.json。
        public BotNavigationMapLoader(Dictionary<string, object> root, Func<string, string> readResource = null)
        {
            this.readResource = readResource ?? PlatformRes.LoadText;
            maps = root?["maps"] as Dictionary<string, object> ?? throw new ArgumentException("Original map geometry is missing");
        }
        public static BotNavigationMapLoader Load() => new BotNavigationMapLoader(BotWarmup.Root("Bot/Maps"));
        public int Count => maps.Count;

        // 地图所属分片的资源路径（Bot/Maps/<n>）；索引里直接内联几何的返回 null。
        public string ShardPath(int id)
            => maps.TryGetValue(Key(id), out var entry) ? entry as string : null;

        // 阻塞版（旧行为）：没预热就现解。
        public MapRuntime Geometry(int id) => Build(id, ReadMap(id));

        // 分帧版：分片已在后台解析好就登记 MapRuntime 并返回 true；
        // 没解析完返回 false（后台线程已经在解），调用方下一帧再试，主线程一帧都不卡。
        public bool TryGeometry(int id, out MapRuntime map)
        {
            map = null;
            var node = TryReadMap(id);
            if (node == null) return false;
            map = Build(id, node);
            return true;
        }

        MapRuntime Build(int id, Dictionary<string, object> map)
        {
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

        static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

        // 分帧版读取：分片还没解析完时返回 null，不阻塞、不抛错。
        Dictionary<string, object> TryReadMap(int id)
        {
            string key = Key(id);
            if (!maps.TryGetValue(key, out var entry)) return null;
            if (entry is Dictionary<string, object> direct) return direct;
            var resource = entry as string;
            if (resource == null) return null;
            var shard = Shard(resource, false);
            if (shard == null) return null;
            return shard.TryGetValue(key, out var node) ? node as Dictionary<string, object> : null;
        }

        // 阻塞版读取（旧行为）：数据确实不在包里时按 ArgumentException 报错，调用方一直这么处理。
        Dictionary<string, object> ReadMap(int id)
        {
            string key = Key(id);
            if (!maps.TryGetValue(key, out var entry)) throw new ArgumentException("Map data not found for " + id);
            if (entry is Dictionary<string, object> direct) return direct;
            string resource = entry as string ?? throw new ArgumentException("Invalid map resource index for " + id);
            var shard = Shard(resource, true) ?? throw new ArgumentException("Map geometry group is missing: " + resource);
            return shard.TryGetValue(key, out var node) && node is Dictionary<string, object> map
                ? map : throw new ArgumentException("Map geometry is missing: " + id);
        }

        Dictionary<string, object> Shard(string resource, bool allowBlocking)
        {
            if (shardCache.TryGetValue(resource, out var cached)) { TouchShard(resource); return cached; }
            if (!allowBlocking)
            {
                // 后台线程在解就返回 null，让调用方下一帧再来（TryRoot 会顺手把任务排进去）。
                if (!BotWarmup.TryRoot(resource, out var prefetched))
                {
                    // 解析已经结束了还拿不到根节点 = 分片资源本身缺失/损坏：
                    // 按「这张图不存在」报错，别让调用方一直等下去。
                    if (BotWarmup.Ready(resource)) throw new ArgumentException("Map geometry group is missing: " + resource);
                    return null;
                }
                Adopt(resource, prefetched);
                return prefetched;
            }
            if (BotWarmup.TryRoot(resource, out var warm)) { Adopt(resource, warm); return warm; }
            Dictionary<string, object> root = null;
            try { root = BotWarmup.Root(resource); }   // 后台没预热/还在解：Join 或当场解析
            catch (InvalidOperationException) { return null; }
            Adopt(resource, root);
            return root;
        }

        void Adopt(string resource, Dictionary<string, object> root)
        {
            if (root == null) return;
            shardCache[resource] = root;
            TouchShard(resource);
            BotWarmup.Release(resource);             // 引用交给这里的 LRU，别再在预热表里挂一份
            while (shardOrder.Count > MaxCachedShards)
            {
                var oldest = shardOrder[0];
                shardOrder.RemoveAt(0);
                shardCache.Remove(oldest);
            }
        }

        void TouchShard(string resource)
        {
            shardOrder.Remove(resource);
            shardOrder.Add(resource);
        }

        static int Int(Dictionary<string, object> d, string k, int fallback = 0) => BotItemCatalog.Int(d, k, fallback);
        static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var value) ? value as string ?? "" : "";
    }
}
