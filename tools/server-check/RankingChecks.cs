// 排行榜回归：逐条对照源端
//   net/server/task/RankingLoginTask.java（resetMoveRank / updateRanking / lastUpdate 的「未变动保留」规则）
//   net/server/task/RankingCommandTask.java（top 列表 5 分钟一刷）
//   service/CharacterService.java（getWorldsRankPlayers / getWorldRankPlayers，前 50、gm<2、账号未封）
//   net/server/Server.java（use_whole_server_ranking 切全服榜）
using System;
using System.Collections.Generic;
using System.IO;

static class RankingChecks
{
    // 直接往 characters 里塞行：比走 CharFactory 建号快，也能精确控制 level/exp/fame/meso/lastExpGainTime。
    static void AddCharacter(DbStore db, int accountId, int world, string name, int level, long exp, int job,
        int fame = 0, long meso = 0, int gm = 0, string lastExpGain = "2015-01-01 05:00:00")
    {
        db.Table("characters").Insert(new DbRow
        {
            ["accountid"] = (long)accountId, ["world"] = (long)world, ["name"] = name,
            ["level"] = (long)level, ["exp"] = exp, ["job"] = (long)job,
            ["fame"] = (long)fame, ["meso"] = meso, ["gm"] = (long)gm,
            ["lastExpGainTime"] = lastExpGain,
            ["rank"] = 1L, ["rankMove"] = 0L, ["jobRank"] = 1L, ["jobRankMove"] = 0L,
        });
    }

    static int NewAccount(DbStore db, string name)
        => LoginService.Login(db, name, "pw123456").accountId;

    static int RealCharacter(DbStore db, string name, int job = 0)
    {
        var login = LoginService.Login(db, name.ToLowerInvariant() + "_acc", "pw123456");
        var made = CharFactory.CreateCharacter(db, login.accountId, 0, name, CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
        if (made.code != 0) throw new InvalidOperationException("建号失败：" + name);
        if (job != 0)
        {
            var record = CharactersDao.FindById(db, made.characterId);
            record.job = job;
            CharactersDao.Save(db, record);
        }
        db.Save();
        return made.characterId;
    }

    static RankingResponse Top(DbStore db, int cid, int jobGroup = -1)
        => NativeRankingService.Handle(db, new RankingRequest
            { characterId = cid, operation = RankingOperation.Top, jobGroup = jobGroup });

    static DbRow Row(DbStore db, string name)
        => db.Table("characters").Find(x => DbStore.Str(x, "name") == name);

    static void SetConfig(DbStore db, string code, string value)
    {
        var table = db.Table("game_config");
        var row = table.Find(x => (string)x["config_code"] == code);
        if (row == null) table.Insert(new DbRow { ["config_type"] = "server", ["config_code"] = code, ["config_value"] = value });
        else row["config_value"] = value;
        db.Save();
    }

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 排行榜 Ranking（RankingLoginTask / getWorldRankPlayers）==");
        NativeRankingService.Invalidate();

        OrderChecks(Check);
        FilterChecks(Check);
        LimitChecks(Check);
        RankMoveChecks(Check);
        CacheChecks(Check);
    }

    // ————————————————— 榜单排序 —————————————————
    static void OrderChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_rank_order_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            NativeRankingService.Invalidate();
            int cid = RealCharacter(db, "RankMe");
            int account = NewAccount(db, "rank_filler");

            AddCharacter(db, account, 0, "Lv60A", 60, 1000, 100);
            AddCharacter(db, account, 0, "Lv60B", 60, 5000, 100);    // 同等级、经验更高 → 排在 A 前
            AddCharacter(db, account, 0, "Lv70", 70, 0, 100);
            AddCharacter(db, account, 0, "Lv50", 50, 999999, 100);
            db.Save();

            var res = Top(db, cid);
            Check(res.ok, "榜单请求成功");
            var names = new List<string>();
            foreach (var entry in res.entries) names.Add(entry.name);
            Check(names.Count >= 4 && names[0] == "Lv70" && names[1] == "Lv60B" && names[2] == "Lv60A" && names[3] == "Lv50",
                "榜单按 level↓ / exp↓ 排序（Lv70 → Lv60B → Lv60A → Lv50）");
            Check(res.entries.Count > 0 && res.entries[0].rank == 1 && res.entries[3].rank == 4, "榜单序号从 1 连续");

            // 同等级同经验时按 lastExpGainTime 升序（先升级的在前）
            AddCharacter(db, account, 0, "SameOld", 80, 100, 100, lastExpGain: "2015-01-01 00:00:00");
            AddCharacter(db, account, 0, "SameNew", 80, 100, 100, lastExpGain: "2020-01-01 00:00:00");
            db.Save();
            NativeRankingService.Invalidate();
            var res2 = Top(db, cid);
            int atOld = res2.entries.FindIndex(e => e.name == "SameOld");
            int atNew = res2.entries.FindIndex(e => e.name == "SameNew");
            Check(atOld >= 0 && atNew >= 0 && atOld < atNew, "同级同经验按 lastExpGainTime 升序（先升级的在前）");

            // 职业页只收本职业群
            AddCharacter(db, account, 0, "Mage200", 90, 0, 200);
            AddCharacter(db, account, 0, "Warrior110", 90, 0, 110);
            db.Save();
            NativeRankingService.Invalidate();
            var mageView = Top(db, cid, 2);
            Check(mageView.entries.Count > 0 && mageView.entries.TrueForAll(e => e.job / 100 == 2)
                  && mageView.entries.Exists(e => e.name == "Mage200"),
                "职业页按 job / 100 过滤（法师页只有 2xx）");
        }
        finally { File.Delete(path); }
    }

    // ————————————————— 过滤（gm / 封号 / 大区） —————————————————
    static void FilterChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_rank_filter_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            NativeRankingService.Invalidate();
            int cid = RealCharacter(db, "RankFilter");
            int account = NewAccount(db, "filter_filler");

            AddCharacter(db, account, 0, "Normal", 100, 0, 300);
            AddCharacter(db, account, 0, "Gm2", 110, 0, 300, gm: 2);
            AddCharacter(db, account, 0, "OtherWorld", 120, 0, 300);
            Row(db, "OtherWorld")["world"] = 1L;
            int bannedAccount = NewAccount(db, "banned_acc");
            AddCharacter(db, bannedAccount, 0, "Banned", 130, 0, 300);
            var bannedRow = db.Table("accounts").Find(x => DbStore.Long(x, "id") == bannedAccount);
            bannedRow["banned"] = 1L;
            bannedRow["tempban"] = "2099-01-01 00:00:00";
            db.Save();

            var res = Top(db, cid);
            Check(res.entries.Exists(e => e.name == "Normal"), "普通角色在榜");
            Check(!res.entries.Exists(e => e.name == "Gm2"), "gm >= 2 的角色不进榜");
            Check(!res.entries.Exists(e => e.name == "Banned"), "封禁账号的角色不进榜");
            Check(!res.entries.Exists(e => e.name == "OtherWorld"), "默认按本大区统计（别的大区不进榜）");

            // 全服榜：先补第二个大区，再开 use_whole_server_ranking
            db.Table("world_prop").Insert(new DbRow { ["id"] = 1L, ["flag"] = 0L, ["channel_size"] = 1L, ["enabled"] = 1L });
            SetConfig(db, "use_whole_server_ranking", "true");
            NativeRankingService.Invalidate();
            var whole = Top(db, cid);
            Check(whole.wholeServer && whole.entries.Exists(e => e.name == "OtherWorld"),
                "use_whole_server_ranking=true 时全服榜含别的大区");
            int firstWorld0 = whole.entries.FindIndex(e => e.world == 0);
            int firstWorld1 = whole.entries.FindIndex(e => e.world == 1);
            Check(firstWorld1 >= 0 && firstWorld0 >= 0 && firstWorld0 < firstWorld1,
                "全服榜按 world 升序分段（源端 ORDER BY world ASC）");
            SetConfig(db, "use_whole_server_ranking", "false");
            NativeRankingService.Invalidate();
        }
        finally { File.Delete(path); }
    }

    // ————————————————— 前 50 上限 —————————————————
    static void LimitChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_rank_limit_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            NativeRankingService.Invalidate();
            int cid = RealCharacter(db, "RankLimit");
            int account = NewAccount(db, "limit_filler");
            for (int i = 0; i < 55; i++) AddCharacter(db, account, 0, "Filler" + i.ToString("00"), 10 + i, 0, 100);
            db.Save();

            var res = Top(db, cid);
            Check(res.entries.Count == NativeRankingService.TopLimit, "榜单最多 50 条");
            Check(res.entries[0].name == "Filler54" && res.entries[49].name == "Filler05",
                "取等级最高的 50 人（Filler54 … Filler05）");
        }
        finally { File.Delete(path); }
    }

    // ————————————————— 名次与升降 —————————————————
    static void RankMoveChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_rank_move_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            NativeRankingService.Invalidate();
            int cid = RealCharacter(db, "RankMove");
            int account = NewAccount(db, "move_filler");
            // lastlogin 早于进程启动 → 源端会把上一轮的升降值保留下来
            var accountRow = db.Table("accounts").Find(x => DbStore.Long(x, "id") == account);
            accountRow["lastlogin"] = "2015-01-01 05:00:00";
            accountRow["loggedin"] = 0L;

            AddCharacter(db, account, 0, "Alpha", 60, 1000, 100);
            AddCharacter(db, account, 0, "Bravo", 50, 1000, 100);
            db.Save();

            NativeRankingService.RefreshRank(db);
            Check(DbStore.Int(Row(db, "Alpha"), "rank") == 1 && DbStore.Int(Row(db, "Bravo"), "rank") == 2,
                "首轮重算：Alpha 第 1、Bravo 第 2");
            Check(DbStore.Int(Row(db, "Alpha"), "rankMove") == 0 && DbStore.Int(Row(db, "Bravo"), "rankMove") == -1,
                "首轮升降 = 旧名次 - 新名次（1→1 = 0，1→2 = -1）");

            // 第二人反超：默认 use_refresh_rank_move=true（先清零再算）
            Row(db, "Bravo")["level"] = 70L;
            db.Save();
            NativeRankingService.RefreshRank(db);
            Check(DbStore.Int(Row(db, "Bravo"), "rank") == 1 && DbStore.Int(Row(db, "Bravo"), "rankMove") == 1,
                "反超后 Bravo 升 1 位（清零模式下只看本轮）");
            Check(DbStore.Int(Row(db, "Alpha"), "rankMove") == -1, "Alpha 降 1 位");

            // use_refresh_rank_move=false + 账号未登录 → 在上一轮的值上继续累加
            SetConfig(db, "use_refresh_rank_move", "false");
            Row(db, "Alpha")["level"] = 90L;
            db.Save();
            NativeRankingService.RefreshRank(db);
            Check(DbStore.Int(Row(db, "Alpha"), "rank") == 1 && DbStore.Int(Row(db, "Alpha"), "rankMove") == 0,
                "关掉 use_refresh_rank_move：旧值 -1 累加 +1 得 0");
            Check(DbStore.Int(Row(db, "Bravo"), "rankMove") == 0, "Bravo 旧值 +1 累加 -1 得 0");
            SetConfig(db, "use_refresh_rank_move", "true");

            // 名次口径会再比 fame / meso（榜单口径不比）
            AddCharacter(db, account, 0, "TwinLow", 75, 500, 200, fame: 1, meso: 1000);
            AddCharacter(db, account, 0, "TwinHigh", 75, 500, 200, fame: 50, meso: 1000);
            db.Save();
            NativeRankingService.Invalidate();
            NativeRankingService.RefreshRank(db);
            Check(DbStore.Int(Row(db, "TwinHigh"), "rank") < DbStore.Int(Row(db, "TwinLow"), "rank"),
                "同级同经验时名气高的名次更前（updateRanking 的 fame DESC）");

            // 职业名次按 job / 100 分组
            Check(DbStore.Int(Row(db, "TwinHigh"), "jobRank") == 1,
                "jobRank 按职业群单独排（200 群里第一）");
            Check(DbStore.Int(Row(db, "Alpha"), "jobRank") == 1, "100 群里 Alpha 第一");

            var mine = Top(db, cid);
            Check(mine.ok && mine.myRank >= 1 && mine.myLevel >= 1, "回传「我的名次」与等级");
        }
        finally { File.Delete(path); }
    }

    // ————————————————— 榜单缓存（RankingCommandTask 5 分钟） —————————————————
    static void CacheChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_rank_cache_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            NativeRankingService.Invalidate();
            int cid = RealCharacter(db, "RankCache");
            int account = NewAccount(db, "cache_filler");
            AddCharacter(db, account, 0, "Cached", 40, 0, 100);
            db.Save();

            var first = Top(db, cid);
            AddCharacter(db, account, 0, "Later", 200, 0, 100);
            db.Save();
            var cached = Top(db, cid);
            Check(first.entries.Count == cached.entries.Count && !cached.entries.Exists(e => e.name == "Later"),
                "榜单 5 分钟缓存：期间新角色不出现");

            NativeRankingService.Invalidate();
            var refreshed = Top(db, cid);
            Check(refreshed.entries.Exists(e => e.name == "Later") && refreshed.entries[0].name == "Later",
                "清缓存后重新查库，新角色立刻上榜");

            var missing = NativeRankingService.Handle(db, new RankingRequest { characterId = 999999, operation = RankingOperation.Top });
            Check(!missing.ok, "角色不存在时报错");
        }
        finally { File.Delete(path); }
    }
}
