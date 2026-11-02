using System;
using System.Collections.Generic;
using System.Globalization;

// 排行榜 —— 对照 gms-server：
//   net/server/task/RankingLoginTask.java（每小时重算 rank/rankMove/jobRank/jobRankMove）
//   net/server/task/RankingCommandTask.java（每 5 分钟刷新 top 列表缓存）
//   net/server/Server.java:635-655（getWorldPlayerRanking / reloadWorldsPlayerRanking）
//   service/CharacterService.java:276-311（getWorldsRankPlayers / getWorldRankPlayers，各区前 50）
//   net/server/guild/GuildPackets.java:314（showPlayerRanks → GUILD_OPERATION 0x49，只发 name+level）
//
// 两套排序不一样，别合并：
//   * 榜单（getWorldRankPlayers）只按 level↓ / exp↓ / lastExpGainTime↑ 排，取前 50；
//   * 名次（updateRanking）在榜单排序之后再比 fame↓ / meso↓，所以「列表里第 4 位显示第 5 名」
//     在同级同经验时是**正常**的源端行为。
//
// 单机语义：榜单只统计 characters 表里 gm<2 的角色（假人是内存对象、不落库，所以不会出现在
// 榜单里 —— 与源端「只有真角色进榜」一致）。
public enum RankingOperation
{
    Top,
}

public sealed class RankingEntry
{
    public string name = "";
    public int level, job, world;
    public int rank;        // 榜单里的序号（1 起）
    public int rankMove;    // 该角色自己记录的升降（榜单口径）
}

public sealed class RankingRequest
{
    public int characterId;
    public RankingOperation operation;
    public int jobGroup = -1;   // -1 = 综合；>=0 时按 job / 100 过滤职业群
}

public sealed class RankingResponse
{
    public bool ok = true;
    public string error = "";
    public int world = -1, jobGroup = -1, worlds = 1;
    public bool wholeServer;
    public List<RankingEntry> entries = new List<RankingEntry>();
    public int myRank, myRankMove, myJobRank, myJobRankMove, myLevel, myJob;
}

public static class NativeRankingService
{
    // 源端上限与周期
    public const int TopLimit = 50;
    public static readonly TimeSpan RankInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan TopInterval = TimeSpan.FromMinutes(5);

    /// <summary>RankingLoginTask.run 的周期重算：距离上次重算超过 1 小时就补跑一次。</summary>
    static DateTime lastRankRefresh = DateTime.MinValue;

    /// <summary>RankingLoginTask.lastUpdate：进程启动时刻，之后每次重算成功再更新。判「上一轮之后有没有变动」用它。</summary>
    static DateTime lastUpdate = DateTime.Now;

    sealed class TopCacheEntry
    {
        public DateTime at;
        public List<RankingEntry> list;
    }

    static readonly Dictionary<string, TopCacheEntry> topCache = new Dictionary<string, TopCacheEntry>();

    /// <summary>测试 / 后台改数据后调用：下次取榜单重新查库。</summary>
    public static void Invalidate()
    {
        topCache.Clear();
        lastRankRefresh = DateTime.MinValue;
        lastUpdate = DateTime.Now;
    }

    public static RankingResponse Handle(DbStore db, RankingRequest r)
    {
        var character = CharactersDao.FindById(db, r.characterId);
        if (character == null) return new RankingResponse { ok = false, error = "角色不存在" };

        RefreshIfDue(db);

        bool whole = GameConfigDao.GetBool(db, "use_whole_server_ranking", false);
        int worlds = Math.Max(1, WorldService.Worlds(db).Count);
        var response = new RankingResponse
        {
            world = character.world, jobGroup = r.jobGroup, worlds = worlds, wholeServer = whole,
            entries = Top(db, whole, character.world, r.jobGroup, worlds),
        };

        var mine = CharactersDao.FindById(db, r.characterId) ?? character;
        response.myRank = mine.rank;
        response.myRankMove = mine.rankMove;
        response.myJobRank = mine.jobRank;
        response.myJobRankMove = mine.jobRankMove;
        response.myLevel = mine.level;
        response.myJob = mine.job;
        return response;
    }

    // ————————————————— 名次重算（RankingLoginTask） —————————————————

    static void RefreshIfDue(DbStore db)
    {
        var now = DateTime.Now;
        if (lastRankRefresh != DateTime.MinValue && now - lastRankRefresh < RankInterval) return;
        RefreshRank(db, now);
    }

    public static void RefreshRank(DbStore db) => RefreshRank(db, DateTime.Now);

    static void RefreshRank(DbStore db, DateTime now)
    {
        // use_refresh_rank_move=true：升降按「上一次重算以来」统计，所以先全部清零。
        if (GameConfigDao.GetBool(db, "use_refresh_rank_move", true))
        {
            ResetMoveRank(db, false);
            ResetMoveRank(db, true);
        }

        // 源端 for (world 0..worldsSize-1) + for (job 0..Job.getMax())：
        // 空大区/空职业群的 updateRanking 一条也写不到，这里只跑真正有角色的组合。
        var groups = new Dictionary<int, List<int>>();
        foreach (var row in db.Table("characters").rows)
        {
            int world = DbStore.Int(row, "world");
            if (!groups.TryGetValue(world, out var list)) groups[world] = list = new List<int>();
            if (DbStore.Int(row, "gm") >= 2) continue;
            int group = DbStore.Int(row, "job") / 100;
            if (!list.Contains(group)) list.Add(group);
        }

        foreach (var pair in groups)
        {
            UpdateRanking(db, -1, pair.Key);
            foreach (int group in pair.Value) UpdateRanking(db, group, pair.Key);
        }

        db.Save();
        lastRankRefresh = now;
        lastUpdate = now;
    }

    static void ResetMoveRank(DbStore db, bool job)
    {
        string column = job ? "jobRankMove" : "rankMove";
        foreach (var row in db.Table("characters").rows) row[column] = 0L;
    }

    static void UpdateRanking(DbStore db, int jobGroup, int world)
    {
        var rows = new List<DbRow>();
        foreach (var row in db.Table("characters").rows)
        {
            if (DbStore.Int(row, "world") != world) continue;
            if (DbStore.Int(row, "gm") >= 2) continue;
            if (jobGroup != -1 && DbStore.Int(row, "job") / 100 != jobGroup) continue;
            rows.Add(row);
        }
        rows.Sort(CompareRank);
        var accounts = db.Table("accounts");

        string rankColumn = jobGroup != -1 ? "jobRank" : "rank";
        string moveColumn = jobGroup != -1 ? "jobRankMove" : "rankMove";

        int rank = 0;
        foreach (var row in rows)
        {
            rank++;
            int rankMove = 0;
            var account = FindAccount(accounts, DbStore.Long(row, "accountid"));
            // 上一轮重算之后账号没登录过、或者正在线上 → 保留上一轮的升降值
            if (account == null || IsBefore(DbStore.Str(account, "lastlogin"), lastUpdate) || DbStore.Int(account, "loggedin") > 0)
                rankMove = DbStore.Int(row, moveColumn);

            rankMove += DbStore.Int(row, rankColumn) - rank;
            row[rankColumn] = (long)rank;
            row[moveColumn] = (long)rankMove;
        }
    }

    // 源端 ORDER BY: level DESC, exp DESC, lastExpGainTime ASC, fame DESC, meso DESC
    static int CompareRank(DbRow a, DbRow b)
    {
        int c = DbStore.Int(b, "level").CompareTo(DbStore.Int(a, "level"));
        if (c != 0) return c;
        c = DbStore.Long(b, "exp").CompareTo(DbStore.Long(a, "exp"));
        if (c != 0) return c;
        c = string.CompareOrdinal(DbStore.Str(a, "lastExpGainTime", ""), DbStore.Str(b, "lastExpGainTime", ""));
        if (c != 0) return c;
        c = DbStore.Int(b, "fame").CompareTo(DbStore.Int(a, "fame"));
        if (c != 0) return c;
        c = DbStore.Long(b, "meso").CompareTo(DbStore.Long(a, "meso"));
        if (c != 0) return c;
        // 源端完全并列时顺序由 MySQL 决定；这里用 id 兜底，保证可复现。
        return DbStore.Long(a, "id").CompareTo(DbStore.Long(b, "id"));
    }

    // ————————————————— 榜单（getWorldRankPlayers） —————————————————

    static List<RankingEntry> Top(DbStore db, bool whole, int world, int jobGroup, int worlds)
    {
        string key = (whole ? "A" : "W" + world) + "/" + jobGroup + "/" + worlds;
        var now = DateTime.Now;
        if (topCache.TryGetValue(key, out var cached) && now - cached.at < TopInterval) return cached.list;

        var accounts = db.Table("accounts");
        var rows = new List<DbRow>();
        foreach (var row in db.Table("characters").rows)
        {
            if (DbStore.Int(row, "gm") >= 2) continue;
            int w = DbStore.Int(row, "world");
            if (whole)
            {
                if (w < 0 || w > worlds - 1) continue;
            }
            else if (w != world) continue;
            if (jobGroup != -1 && DbStore.Int(row, "job") / 100 != jobGroup) continue;

            var account = FindAccount(accounts, DbStore.Long(row, "accountid"));
            // 源端 LEFT JOIN accounts 的条件：banned = 0 OR tempban IS NULL
            if (account != null && DbStore.Int(account, "banned") != 0 && !IsNullTempban(account)) continue;
            rows.Add(row);
        }
        rows.Sort(whole ? (Comparison<DbRow>)CompareWholeTop : CompareTop);

        var list = new List<RankingEntry>();
        for (int i = 0; i < rows.Count && i < TopLimit; i++)
        {
            var row = rows[i];
            list.Add(new RankingEntry
            {
                name = DbStore.Str(row, "name"), level = DbStore.Int(row, "level"),
                job = DbStore.Int(row, "job"), world = DbStore.Int(row, "world"),
                rank = i + 1, rankMove = DbStore.Int(row, "rankMove"),
            });
        }
        topCache[key] = new TopCacheEntry { at = now, list = list };
        return list;
    }

    // 源端 getWorldRankPlayers: level DESC, exp DESC, lastExpGainTime ASC, LIMIT 50
    static int CompareTop(DbRow a, DbRow b)
    {
        int c = DbStore.Int(b, "level").CompareTo(DbStore.Int(a, "level"));
        if (c != 0) return c;
        c = DbStore.Long(b, "exp").CompareTo(DbStore.Long(a, "exp"));
        if (c != 0) return c;
        c = string.CompareOrdinal(DbStore.Str(a, "lastExpGainTime", ""), DbStore.Str(b, "lastExpGainTime", ""));
        if (c != 0) return c;
        return DbStore.Long(a, "id").CompareTo(DbStore.Long(b, "id"));
    }

    // 源端全服榜在前面多一个 world ASC
    static int CompareWholeTop(DbRow a, DbRow b)
    {
        int c = DbStore.Int(a, "world").CompareTo(DbStore.Int(b, "world"));
        return c != 0 ? c : CompareTop(a, b);
    }

    static DbRow FindAccount(DbTable accounts, long accountId)
        => accounts.Find(x => DbStore.Long(x, "id") == accountId);

    static bool IsNullTempban(DbRow account)
        => !account.TryGetValue("tempban", out var value) || value == null;

    /// <summary>accounts.lastlogin 是 TEXT；空值/解析失败都当「早于 lastUpdate」（源端 lastlogin==null 同理）。</summary>
    static bool IsBefore(string lastlogin, DateTime reference)
    {
        if (string.IsNullOrEmpty(lastlogin)) return true;
        return DateTime.TryParse(lastlogin, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) && at < reference;
    }
}
