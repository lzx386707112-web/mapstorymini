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
    public int level;           // 0 = 未指定（老库/关掉覆盖模式时回退模板自带等级职业）
    public int job;
}

public static class BotDirectory
{
    const string TableName = "bot_profile";
    // 档案生成种子固定：同一份安装永远生成同样的几百个假人，
    // 不依赖随机时序，重装/换设备看到的是同一批面孔。
    const int Seed = 20261027;
    public const int DefaultSize = 300;

    // —— 冒险家职业阶梯（源端 Job 树；与 BotDecorate.SelectJob 的取值范围一致）——
    // 覆盖模式按「等级 × 该等级可转职业」铺满档案册，所以这里必须是完整的四阶职业表：
    // 漏掉哪一个职业，那个职业就永远不会有假人。
    static readonly int[] BeginnerJobs = { 0 };
    static readonly int[] FirstJobs = { 100, 200, 300, 400, 500 };
    static readonly int[] SecondJobs = { 110, 120, 130, 210, 220, 230, 310, 320, 410, 420, 510, 520 };
    static readonly int[] ThirdJobs = { 111, 121, 131, 211, 221, 231, 311, 321, 411, 421, 511, 521 };
    static readonly int[] FourthJobs = { 112, 122, 132, 212, 222, 232, 312, 322, 412, 422, 512, 522 };

    /// <summary>该等级可选的全部职业（转职门槛：10 一转 / 30 二转 / 70 三转 / 120 四转）。</summary>
    public static int[] JobsForLevel(int level)
        => level < 10 ? BeginnerJobs
         : level < 30 ? FirstJobs
         : level < 70 ? SecondJobs
         : level < 120 ? ThirdJobs
         : FourthJobs;

    /// <summary>覆盖模式需要的档案条数 = Σ 每个等级的可选职业数。</summary>
    public static int CoverageCount(int maxLevel)
    {
        int n = 0;
        int cap = Math.Max(1, Math.Min(200, maxLevel));
        for (int level = 1; level <= cap; level++) n += JobsForLevel(level).Length;
        return n;
    }

    /// <summary>档案册应有条数（0 = 关闭档案）。覆盖模式下与 solo_mapling_directory_size 无关。</summary>
    public static int ExpectedCount(DbStore db)
    {
        int size = BotConfig.DirectorySize(db);
        if (size <= 0) return 0;
        if (!BotConfig.DirectoryCoverageEnabled(db)) return size;
        return CoverageCount(BotConfig.DirectoryMaxLevel(db));
    }

    // 网格第 index 条的身份：先按等级递增，再按该等级的转职表顺序。
    static bool GridAt(int index, out int level, out int job)
    {
        level = 0; job = 0;
        if (index < 0) return false;
        int i = index;
        for (int lv = 1; lv <= 200; lv++)
        {
            var jobs = JobsForLevel(lv);
            if (i < jobs.Length) { level = lv; job = jobs[i]; return true; }
            i -= jobs.Length;
        }
        return false;
    }

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
            int wanted = ExpectedCount(db);
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
            bool coverage = BotConfig.DirectoryCoverageEnabled(db);
            bool dirty = false;

            // 升级回来的旧库：表里已有若干条但没有 level/job（列是后加的）。
            // 按网格顺序把身份补上，否则这些档案生成出来会是 0 级无职业。
            if (coverage)
                for (int i = 0; i < rows.Count && i < wanted; i++)
                    if (ToLong(rows[i], "level", 0) <= 0 && GridAt(i, out int gl, out int gj))
                    {
                        rows[i]["level"] = (long)gl;
                        rows[i]["job"] = (long)gj;
                        dirty = true;
                    }

            if (rows.Count < wanted)
            {
                // 名字要唯一（联机的账号名本来就唯一）：同批次内查重，撞了就重抽；
                // 用一个只跟下标有关的种子，保证「第 i 条档案的名字」永远不变，
                // 不依赖库里已经有几条，追加/重装都不会改名。
                var usedNames = new HashSet<string>();
                for (int i = 0; i < rows.Count; i++)
                {
                    string name = ToText(rows[i], "name");
                    if (name.Length > 0) usedNames.Add(name);
                }
                for (int i = rows.Count; i < wanted; i++)
                {
                    GridAt(i, out int level, out int job);
                    var row = new DbRow
                    {
                        ["name"] = BotConfig.RandomChineseNameEnabled(db) ? UniqueName(i, usedNames) : FixedName(i, usedNames),
                        ["template"] = templateCount > 0 ? NextTemplate(i, templateCount) : 0,
                        ["map_id"] = 0,
                        ["channel"] = 1,
                        ["bound"] = 0,
                        ["enabled"] = 1,
                        ["level"] = (long)level,
                        ["job"] = (long)job,
                    };
                    table.Insert(row);
                    rows.Add(row);
                }
                dirty = true;
            }
            if (dirty) db.Save();

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
                    level = (int)ToLong(row, "level", 0),
                    job = (int)ToLong(row, "job", 0),
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

        // 模板下标只跟下标有关：同一条档案永远同一套外观，跟补到第几条无关。
        static int NextTemplate(int index, int templateCount)
            => (int)((uint)(index * 2654435761u) % (uint)templateCount);

        static string UniqueName(int index, HashSet<string> used)
        {
            for (int salt = 0; salt < 256; salt++)
            {
                var rng = new Random(unchecked(Seed * 31 + index * 7919 + salt));
                string name = BotHelpers.GetRandomChineseBotName(rng);
                if (used.Add(name)) return name;
            }
            // 极端情况（名字池抽干）：补序号，仍保证唯一。
            string fallback = "游客" + (index + 1);
            used.Add(fallback);
            return fallback;
        }

        // solo_mapling_bot_random_chinese_name=0：沿用旧的「游客N」编号名。
        static string FixedName(int index, HashSet<string> used)
        {
            string name = "游客" + (index + 1);
            while (!used.Add(name)) name += "_";
            return name;
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
