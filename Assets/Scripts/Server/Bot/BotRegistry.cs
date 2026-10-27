// 全局活体 bot 注册表。对齐源端 world.getPlayerStorage() + channel.addPlayer/removePlayer：
//   源端 bot 登录后注册到 world 的 player storage；安卓后端无 player storage，此处新建。
// 按 id 与 mapId 双索引，供地图运行时查询 bot 列表（渲染/范围查询/落点）。
using System.Collections.Generic;

namespace Server.Bot
{
    public static class BotRegistry
    {
        static readonly Dictionary<int, BotCharacter> byId = new Dictionary<int, BotCharacter>();
        static readonly Dictionary<int, List<int>> byMap = new Dictionary<int, List<int>>();

        // 全局注册（对齐源端 world.getPlayerStorage().addPlayer / channel.addPlayer）
        public static void Add(BotCharacter c)
        {
            byId[c.id] = c;
        }

        // 进入地图索引（对齐源端 map.addPlayer）
        public static void EnterMap(BotCharacter c, int mapId)
        {
            LeaveMap(c);
            c.mapId = mapId;
            c.MarkMapEntry();
            if (!byMap.TryGetValue(mapId, out var list))
            {
                list = new List<int>();
                byMap[mapId] = list;
            }
            if (!list.Contains(c.id)) list.Add(c.id);
        }

        public static void LeaveMap(BotCharacter c)
        {
            Environment.BotRecordedActions.Cancel(c);
            c.arrivalTask?.Cancel();
            Environment.BotMovementPlayback.Cancel(c);
            if (byMap.TryGetValue(c.mapId, out var list))
            {
                list.Remove(c.id);
                if (list.Count == 0) byMap.Remove(c.mapId);
            }
        }

        public static void Remove(BotCharacter c)
        {
            Environment.BotMovementService.Disable(c);
            byId.Remove(c.id);
            LeaveMap(c);
        }

        public static void Remove(int id)
        {
            if (byId.TryGetValue(id, out var c)) Remove(c);
        }

        public static BotCharacter Get(int id) => byId.TryGetValue(id, out var c) ? c : null;

        public static bool Contains(int id) => byId.ContainsKey(id);

        public static List<BotCharacter> OnMap(int mapId)
        {
            var outp = new List<BotCharacter>();
            if (byMap.TryGetValue(mapId, out var list))
                foreach (var id in list)
                    if (byId.TryGetValue(id, out var c)) outp.Add(c);
            return outp;
        }

        public static IReadOnlyDictionary<int, BotCharacter> All() => byId;

        public static int Count() => byId.Count;
    }
}
