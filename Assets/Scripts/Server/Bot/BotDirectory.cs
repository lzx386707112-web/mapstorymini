// 假人档案册 —— 对齐联机的「玩家账号表」。
//
// 联机为什么不卡、为什么稳定：每个玩家账号在数据库里唯一，别人看到他永远是同一张脸、
// 同一个名字、同一套装备。客户端只是把这些「已经确定的数据」画出来，不在运行时现造。
// 假人以前是反过来的 —— 进一张图现随机一批，人走茶凉，下次进来又是新面孔，
// 而且每个新外观在纸娃娃那边都是一份全新的 CPU 合成结果（缓存一条都命中不了）。
//
// 现在照联机来：
//   · 档案一次性预置几百条（默认 300），存 bot_profile 表，之后只读不改；
//   · 每条档案 = 固定名字 + 固定外观模板（模板本身也固定，见 BotAppearanceTemplates）；
//   · 上线时从档案里领一个（Take），下线归还（Release），跟联机的登录/下线同构。
//
// 这样「几百个假人」是数据库里几百条记录，实际同时在场的是「当前地图需要的那几十个」，
// 与联机里几百个账号分布在几十张图上是同一个模型。
using System;
using System.Collections.Generic;

namespace Server.Bot
{
    public sealed class BotProfile
    {
        public int id;
        public string name = "";
        public int template;        // BotAppearanceTemplates 下标
        public int mapId;           // >0 = 只出现在该图；0 = 自由分配
        public int channel = 1;
    }

    public static class BotDirectory
    {
        const string TableName = "bot_profile";
        // 档案生成种子固定：同一份安装永远生成同样的几百个假人，
        // 不依赖随机时序，重装/换设备看到的是同一批面孔。
        const int Seed = 20261027;
        public const int DefaultSize = 300;

        static readonly List<BotProfile> profiles = new List<BotProfile>();
        static readonly HashSet<int> inUse = new HashSet<int>();
        // 按图绑定的档案下标（map_id > 0）
        static readonly Dictionary<int, List<int>> byMap = new Dictionary<int, List<int>>();
        static int loadedSize = -1;

        public static int Count => profiles.Count;
        public static int InUseCount => inUse.Count;
        public static bool Enabled => profiles.Count > 0;
        public static IReadOnlyList<BotProfile> All => profiles;

        // 会话复位：档案本身留在库里，只清运行期的「谁在场」。
        public static void Reset()
        {
            profiles.Clear(); inUse.Clear(); byMap.Clear(); loadedSize = -1;
        }

        public static int Size(DbStore db) => BotConfig.DirectorySize(db);

        // 幂等：库里不足就补齐。补齐用固定种子，且是按「已有多少条」继续往后生成，
        // 所以改了 solo_mapling_directory_size 之后，老档案不会被改写，只是后面追加。
        public static void Ensure(DbStore db)
        {
            int wanted = Size(db);
            if (loadedSize == wanted && profiles.Count > 0) return;
            if (wanted <= 0 || db == null) { loadedSize = 0; profiles.Clear(); inUse.Clear(); byMap.Clear(); return; }
            // 表不存在（旧库/资源没同步）就别动，也别记住 loadedSize —— 下次还要再试。
            if (!db.HasTable(TableName)) return;
            loadedSize = wanted;
            profiles.Clear(); inUse.Clear(); byMap.Clear();

            // 外观模板要先就绪：档案存的是模板下标，模板没了档案就落不到脸上。
            BotAppearanceTemplates.Ensure(db);
            int templateCount = BotAppearanceTemplates.All.Count;

            var table = db.Table(TableName);
            if (table == null) { loadedSize = -1; return; }
            var rows = table.Select(null);
            if (rows.Count < wanted)
            {
                // 先按已有条数推进随机序列，保证「新增的那些」也是确定性的。
                var rng = new Random(Seed);
                for (int i = 0; i < rows.Count; i++) rng.Next();
                for (int i = rows.Count; i < wanted; i++)
                {
                    string name = BotConfig.RandomChineseNameEnabled(db)
                        ? BotHelpers.GetRandomChineseBotName(rng)
                        : "游客" + (i + 1);
                    var row = new DbRow
                    {
                        ["name"] = name,
                        ["template"] = templateCount > 0 ? rng.Next(templateCount) : 0,
                        ["map_id"] = 0,
                        ["channel"] = 1,
                        ["bound"] = 0,
                        ["enabled"] = 1,
                    };
                    table.Insert(row);
                    rows.Add(row);
                }
                db.Save();
            }

            for (int i = 0; i < rows.Count && profiles.Count < wanted; i++)
            {
                var row = rows[i];
                if (ToLong(row, "enabled", 1) == 0) continue;
                var profile = new BotProfile
                {
                    id = (int)ToLong(row, "id", 0),
                    name = ToText(row, "name"),
                    template = (int)ToLong(row, "template", 0),
                    mapId = (int)ToLong(row, "map_id", 0),
                    channel = (int)ToLong(row, "channel", 1),
                };
                int index = profiles.Count;
                profiles.Add(profile);
                if (profile.mapId > 0)
                {
                    if (!byMap.TryGetValue(profile.mapId, out var list)) byMap[profile.mapId] = list = new List<int>();
                    list.Add(index);
                }
            }
        }

        // 领一份档案：优先本图绑定的，其次未绑定的；全被占用返回 null（调用方回退随机生成）。
        // 返回的档案由调用方负责 Release —— 和联机的「上线/下线」一一对应。
        public static BotProfile Take(int mapId, Random random)
        {
            if (profiles.Count == 0) return null;
            // 本图专属档案先来：这些「常驻本图」的人每次进来看见的都是同一批。
            if (byMap.TryGetValue(mapId, out var candidates))
                for (int i = 0; i < candidates.Count; i++)
                {
                    var p = profiles[candidates[i]];
                    if (inUse.Contains(p.id)) continue;
                    inUse.Add(p.id);
                    return p;
                }
            // 自由档案：从没被占用的里挑一个。起点用 random 打散，避免所有人都挤在前几条。
            int start = random != null && profiles.Count > 0 ? random.Next(profiles.Count) : 0;
            for (int n = 0; n < profiles.Count; n++)
            {
                var p = profiles[(start + n) % profiles.Count];
                if (p.mapId > 0 || inUse.Contains(p.id)) continue;
                inUse.Add(p.id);
                return p;
            }
            return null;
        }

        public static void Release(int profileId)
        {
            if (profileId != 0) inUse.Remove(profileId);
        }

        public static BotProfile Find(int profileId)
        {
            for (int i = 0; i < profiles.Count; i++) if (profiles[i].id == profileId) return profiles[i];
            return null;
        }

        static long ToLong(DbRow row, string key, long fallback)
        {
            if (row == null || !row.TryGetValue(key, out var v) || v == null) return fallback;
            try { return Convert.ToInt64(v); } catch (Exception) { return fallback; }
        }

        static string ToText(DbRow row, string key)
            => row != null && row.TryGetValue(key, out var v) && v != null ? v.ToString() : "";
    }
}
