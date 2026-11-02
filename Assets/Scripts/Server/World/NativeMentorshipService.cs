using System;
using System.Collections.Generic;
using System.Globalization;

// 源端 org.gms.service.MentorshipService.java（1141 行）的单机等价实现。
// 脚本面（3003107.js）消费 16 个 cm API：overview/create/partyMasterMenu/hasPartyMasterCandidate/
// createFromParty/cancel/claimStages/claimWeekly/startDuel/joinDuel/duelStatus/ranking/
// virtueCoins/apprenticeCoins/spendVirtue/spendApprentice。
// 关键常量（源端 L18-29）：师傅 ≥200 级；徒弟必须未转生、同家族；对决下注 ≥100；
// 周池每 100 积分一单位，师傅 8 师德币/单位、徒弟 12 师徒币/单位；
// 阶段阈值 {100,300,600,1000,1500}，阶段奖励师德 {10,20,35,55,80} / 师徒 {20,40,70,110,160}。
// 积分来源 addPoints（L601）：total_points、周池、师傅 points/10 师德币、徒弟 points/5 师徒币、
// 进行中对局加 points_a/b；GPQ 通关 recordGpqClear 每关系 +100（本端 GPQ 未迁，入口保留）。
// 自动出师（L538）：徒弟战力 > 师傅战力即出师，师傅 +50 师德币、徒弟 +100 师徒币。
// 战力（L1075-1101）：在线 = (四维和)*max(1,max(watk,magic)) + maxHp/10 + maxMp/20 + 现金/100；
// 离线（DB 值，不含攻击力）= level*1000 + 四维*10 + maxHp/2 + maxMp/2 + 现金/100。
public static class NativeMentorshipService
{
    public const int StatusActive = 0, StatusGraduated = 1, StatusCancelled = 2;
    public const int MasterMinLevel = 200;
    public const int MinDuelStake = 100;
    public const int GpqClearPoints = 100;
    const int WeeklyUnitPoints = 100, WeeklyMasterCoin = 8, WeeklyApprenticeCoin = 12;
    static readonly int[] StageThresholds = { 100, 300, 600, 1000, 1500 };
    static readonly int[] StageMasterVirtue = { 10, 20, 35, 55, 80 };
    static readonly int[] StageApprenticeCoin = { 20, 40, 70, 110, 160 };

    // mentorship_wallet 行（characterid 键）。ensureWallet 后才读。
    sealed class Wallet { public long apprenticeCoin, virtueCoin, totalPoints, weeklyPoints; }

    // —— 钱包（mentorship_wallet）——
    static Wallet EnsureWallet(DbStore db, long characterId)
    {
        var row = db.Table("mentorship_wallet").Find(DbStore.Eq("characterid", characterId));
        if (row == null)
        {
            row = new DbRow { ["characterid"] = characterId, ["apprentice_coin"] = 0L,
                ["virtue_coin"] = 0L, ["total_points"] = 0L, ["weekly_points"] = 0L };
            db.Table("mentorship_wallet").Insert(row);
        }
        return new Wallet
        {
            apprenticeCoin = DbStore.Int(row, "apprentice_coin"),
            virtueCoin = DbStore.Int(row, "virtue_coin"),
            totalPoints = DbStore.Int(row, "total_points"),
            weeklyPoints = DbStore.Int(row, "weekly_points"),
        };
    }

    static void AddWallet(DbStore db, long characterId, long apprenticeDelta, long virtueDelta, long pointsDelta)
    {
        var wallet = EnsureWallet(db, characterId);
        var row = db.Table("mentorship_wallet").Find(DbStore.Eq("characterid", characterId));
        row["apprentice_coin"] = wallet.apprenticeCoin + apprenticeDelta;
        row["virtue_coin"] = wallet.virtueCoin + virtueDelta;
        row["total_points"] = wallet.totalPoints + pointsDelta;
    }

    // getVirtueCoins/getApprenticeCoins（L488-510）：ensureWallet 后读对应列。
    public static long CoinsOf(DbStore db, long characterId, bool virtue)
        => virtue ? EnsureWallet(db, characterId).virtueCoin : EnsureWallet(db, characterId).apprenticeCoin;

    // spendVirtueCoins/spendApprenticeCoins（L464-486）：条件扣款，余额不足返回 false。
    public static bool SpendCoins(DbStore db, long characterId, bool virtue, long cost)
    {
        var wallet = EnsureWallet(db, characterId);
        long balance = virtue ? wallet.virtueCoin : wallet.apprenticeCoin;
        if (balance < cost) return false;
        var row = db.Table("mentorship_wallet").Find(DbStore.Eq("characterid", characterId));
        row[virtue ? "virtue_coin" : "apprentice_coin"] = balance - cost;
        return true;
    }

    // —— 关系（mentorship_relation）——
    sealed class Relation
    {
        public long id, masterCid, apprenticeCid, guildid;
        public string masterName, apprenticeName;
        public int status, rewardStep;
        public long totalPoints, lastMasterPower, lastApprenticePower;
    }

    static List<Relation> RelationsOf(DbStore db, long characterId)
    {
        var result = new List<Relation>();
        foreach (var row in db.Table("mentorship_relation").rows)
        {
            if (DbStore.Int(row, "master_cid") != characterId && DbStore.Int(row, "apprentice_cid") != characterId) continue;
            int status = DbStore.Int(row, "status");
            if (status == StatusCancelled) continue;
            result.Add(new Relation
            {
                id = DbStore.Int(row, "id"),
                masterCid = DbStore.Int(row, "master_cid"),
                apprenticeCid = DbStore.Int(row, "apprentice_cid"),
                guildid = DbStore.Int(row, "guildid"),
                masterName = DbStore.Str(row, "master_name"),
                apprenticeName = DbStore.Str(row, "apprentice_name"),
                status = status,
                rewardStep = DbStore.Int(row, "reward_step"),
                totalPoints = DbStore.Int(row, "total_points"),
                lastMasterPower = DbStore.Int(row, "last_master_power"),
                lastApprenticePower = DbStore.Int(row, "last_apprentice_power"),
            });
        }
        return result;
    }

    static Relation FindRelation(DbStore db, long characterId, bool activeOnly = true)
    {
        foreach (var relation in RelationsOf(db, characterId))
            if (!activeOnly || relation.status == StatusActive) return relation;
        return null;
    }

    // —— 战力（L1075-1101）——
    // 在线战力由脚本宿主注入：NpcApi.BuildPlayer 持有含装备的全属性 Totals
    // （(四维和)*max(1,max(watk,matk)) + maxHp/10 + maxMp/20 + 现金/100）。
    public static Func<long> OnlinePowerOfActiveCharacter;

    // 离线角色（DB 值，不含攻击力）：level*1000 + 四维*10 + maxHp/2 + maxMp/2 + 现金/100。
    public static long OfflinePower(DbStore db, long characterId)
    {
        var record = CharactersDao.FindById(db, (int)characterId);
        if (record == null) return 0;
        long cash = CashPower(db, record.accountid);
        return record.level * 1000L
            + (long)record.str * 10 + record.dex * 10L + record.int_ * 10L + record.luk * 10L
            + record.maxhp / 2L + record.maxmp / 2L + cash / 100L;
    }

    static long CashPower(DbStore db, int accountId)
    {
        var row = db.Table("accounts").Find(DbStore.Eq("id", (long)accountId));
        if (row == null) return 0;
        return Math.Max(0, DbStore.Int(row, "nxCredit")) + Math.Max(0, DbStore.Int(row, "maplePoint"))
            + Math.Max(0, DbStore.Int(row, "nxPrepaid"));
    }

    // 在线战力公式的现金项（accounts 三桶，源端 CashShop.getCash(NX_CREDIT/MAPLE_POINT/NX_PREPAID)）。
    public static long CashOfActiveAccount()
    {
        if (!GameSession.InGame) return 0;
        var record = GameSession.Character?.record;
        return record == null ? 0 : CashPower(GameServer.Local.Db, record.accountid);
    }

    // checkAutoGraduation（L538-561）：徒弟战力 > 师傅战力即自动出师。
    static void CheckAutoGraduation(DbStore db, long characterId)
    {
        var relation = FindRelation(db, characterId);
        if (relation == null) return;
        var masterRow = db.Table("mentorship_relation").Find(DbStore.Eq("id", relation.id));
        long masterPower = OfflinePower(db, relation.masterCid);
        long apprenticePower = OfflinePower(db, relation.apprenticeCid);
        // 在线的真人按在线公式重算（两套公式不对称是源端特性：离线值不含攻击力）。
        if (OnlinePowerOfActiveCharacter != null && GameSession.InGame)
        {
            long online = OnlinePowerOfActiveCharacter();
            if (relation.masterCid == GameSession.CharacterId) masterPower = online;
            else if (relation.apprenticeCid == GameSession.CharacterId) apprenticePower = online;
        }
        masterRow["last_master_power"] = masterPower;
        masterRow["last_apprentice_power"] = apprenticePower;
        if (masterPower > 0 && apprenticePower > masterPower)
        {
            masterRow["status"] = (long)StatusGraduated;
            AddWallet(db, relation.masterCid, 0, 50, 0);
            AddWallet(db, relation.apprenticeCid, 100, 0, 0);
            NativeSystemMessage.Drop(5, "[师徒] 徒弟 " + relation.apprenticeName + " 战力 " + apprenticePower
                + " 已超过师傅 " + relation.masterName + " 战力 " + masterPower + "，自动出师。");
        }
    }

    // —— 脚本面 ——
    // getOverview（L150-186）：入口先结算到期对决，再查自动出师。
    public static string Overview(DbStore db, long characterId)
    {
        FinishExpiredDuels(db);
        CheckAutoGraduation(db, characterId);
        var wallet = EnsureWallet(db, characterId);
        var relations = RelationsOf(db, characterId);
        var text = new System.Text.StringBuilder("#e师徒系统#n\r\n\r\n");
        text.Append("师徒币：#b").Append(wallet.apprenticeCoin).Append("#k\r\n");
        text.Append("师德币：#b").Append(wallet.virtueCoin).Append("#k\r\n");
        text.Append("累计积分：#b").Append(wallet.totalPoints).Append("#k，本周积分：#b")
            .Append(wallet.weeklyPoints).Append("#k\r\n\r\n");
        if (relations.Count == 0) text.Append("当前没有进行中的师徒关系。");
        else
            foreach (var relation in relations)
            {
                bool isMaster = relation.masterCid == characterId;
                text.Append(isMaster ? "师傅：" : "徒弟：").Append("#b")
                    .Append(isMaster ? relation.apprenticeName : relation.masterName)
                    .Append("#k  积分：#r").Append(relation.totalPoints)
                    .Append("#k  阶段：").Append(relation.rewardStep).Append("/").Append(StageThresholds.Length).Append("\r\n");
            }
        return text.ToString();
    }

    // createRelation 校验（L563-599）：师傅 ≥200、徒弟未转生、同家族、徒弟无进行中关系。
    public static string CreateRelation(DbStore db, long actorId, string targetName)
    {
        var actor = CharactersDao.FindById(db, (int)actorId);
        if (actor == null) return "当前角色数据不存在。";
        var target = db.Table("characters").Find(DbStore.Eq("name", targetName));
        if (target == null) return "没有找到角色 #b" + targetName + "#k。";
        long targetId = DbStore.Int(target, "id");
        if (targetId == actorId) return "不能和自己建立师徒关系。";

        // 自动判断谁是师傅（L49）：等级高（≥200）者为师傅。
        bool actorIsMaster = actor.level >= MasterMinLevel && DbStore.Int(target, "level") < MasterMinLevel;
        if (!actorIsMaster && DbStore.Int(target, "level") >= MasterMinLevel && actor.level < MasterMinLevel) actorIsMaster = false;
        long masterCid = actorIsMaster ? actorId : targetId;
        long apprenticeCid = actorIsMaster ? targetId : actorId;
        var master = CharactersDao.FindById(db, (int)masterCid);
        var apprentice = CharactersDao.FindById(db, (int)apprenticeCid);
        if (master == null || apprentice == null) return "角色数据不存在。";
        string validation = ValidateCreate(db, master, apprentice);
        if (validation != null) return validation;
        return InsertRelation(db, master, apprentice);
    }

    static string ValidateCreate(DbStore db, CharRecord master, CharRecord apprentice)
    {
        if (master.level < MasterMinLevel)
            return "师傅必须达到 200 级以上。点券会参与战力计算，但不能绕过等级条件。";
        if (apprentice.reborns != 0) return "徒弟必须是未转生角色。";
        if (master.guildid == 0 || master.guildid != apprentice.guildid)
            return "师傅和徒弟必须在同一个家族。";
        if (FindRelation(db, apprentice.id) != null) return "该徒弟已经有进行中的师徒关系。";
        return null;
    }

    static string InsertRelation(DbStore db, CharRecord master, CharRecord apprentice)
    {
        long startMaster = OfflinePower(db, master.id), startApprentice = OfflinePower(db, apprentice.id);
        db.Table("mentorship_relation").Insert(new DbRow
        {
            ["master_cid"] = master.id, ["master_accountid"] = (long)master.accountid, ["master_name"] = master.name,
            ["apprentice_cid"] = apprentice.id, ["apprentice_accountid"] = (long)apprentice.accountid,
            ["apprentice_name"] = apprentice.name,
            ["guildid"] = (long)master.guildid, ["status"] = (long)StatusActive,
            ["start_master_power"] = startMaster, ["start_apprentice_power"] = startApprentice,
            ["last_master_power"] = startMaster, ["last_apprentice_power"] = startApprentice,
            ["reward_step"] = 0L, ["total_points"] = 0L,
        });
        EnsureWallet(db, master.id); EnsureWallet(db, apprentice.id);
        return "师徒关系建立成功：#b" + master.name + "#k 收 #b" + apprentice.name + "#k 为徒。";
    }

    // getPartyMasterMenu（L63-84）：菜单列出队伍里 ≥200 级的成员，selection = 角色 cid。
    public static string PartyMasterMenu(DbStore db, long actorId, List<long> onlinePartyMemberIds)
    {
        if (onlinePartyMemberIds == null || onlinePartyMemberIds.Count < 2)
            return "请先和要建立师徒关系的角色组队。";
        var text = new System.Text.StringBuilder("#e确认师傅#n\r\n\r\n请选择本队伍中的师傅：\r\n\r\n");
        int candidates = 0;
        foreach (long memberId in onlinePartyMemberIds)
        {
            var member = CharactersDao.FindById(db, (int)memberId);
            if (member == null || member.level < MasterMinLevel) continue;
            candidates++;
            text.Append("#L").Append(member.id).Append("##b").Append(member.name)
                .Append("#k  等级 ").Append(member.level).Append("#l\r\n");
        }
        if (candidates == 0) return "队伍中没有达到 200 级的师傅候选。";
        text.Append("选择后，队伍中其他符合条件的成员会作为徒弟建立关系。");
        return text.ToString();
    }

    public static bool HasPartyMasterCandidate(DbStore db, List<long> onlinePartyMemberIds)
    {
        if (onlinePartyMemberIds == null) return false;
        foreach (long memberId in onlinePartyMemberIds)
        {
            var member = CharactersDao.FindById(db, (int)memberId);
            if (member != null && member.level >= MasterMinLevel) return true;
        }
        return false;
    }

    // createRelationsFromParty（L95-148）。
    public static string CreateFromParty(DbStore db, long actorId, int masterCid, List<long> onlinePartyMemberIds)
    {
        var members = onlinePartyMemberIds ?? new List<long>();
        if (!members.Contains(masterCid)) return "选择的师傅不在当前队伍中。";
        var master = CharactersDao.FindById(db, masterCid);
        if (master == null) return "选择的Link角色不存在。";
        if (master.level < MasterMinLevel)
            return "师傅必须达到 200 级以上。点券会参与战力计算，但不能绕过等级条件。";
        var text = new System.Text.StringBuilder("#e师徒关系建立结果#n\r\n\r\n");
        int success = 0;
        foreach (long memberId in members)
        {
            if (memberId == masterCid) continue;
            var apprentice = CharactersDao.FindById(db, (int)memberId);
            string message = apprentice == null ? "角色数据不存在。"
                : ValidateCreate(db, master, apprentice) ?? InsertRelation(db, master, apprentice);
            if (message.StartsWith("师徒关系建立成功", StringComparison.Ordinal)) success++;
            text.Append(apprentice?.name ?? ("角色" + memberId)).Append("：").Append(message).Append("\r\n");
        }
        if (success == 0) text.Append("\r\n没有成功建立新的师徒关系。");
        return text.ToString();
    }

    // cancelActiveRelation（L188-208）。
    public static string Cancel(DbStore db, long characterId)
    {
        var relation = FindRelation(db, characterId);
        if (relation == null) return "当前没有可解除的师徒关系。";
        db.Table("mentorship_relation").Update(DbStore.Eq("id", relation.id),
            new DbRow { ["status"] = (long)StatusCancelled });
        return "已解除当前进行中的师徒关系。";
    }

    // claimStageRewards（L210-254）。
    public static string ClaimStages(DbStore db, long characterId)
    {
        CheckAutoGraduation(db, characterId);
        bool any = false;
        foreach (var relation in RelationsOf(db, characterId))
        {
            bool isMaster = relation.masterCid == characterId;
            int nextStep = relation.rewardStep;
            while (nextStep < StageThresholds.Length && relation.totalPoints >= StageThresholds[nextStep])
            {
                string rewardKey = "stage_" + (nextStep + 1);
                var logs = db.Table("mentorship_reward_log");
                if (logs.Find(r => DbStore.Int(r, "relationid") == relation.id && DbStore.Str(r, "reward_key") == rewardKey) != null)
                { nextStep++; continue; }
                logs.Insert(new DbRow { ["relationid"] = relation.id, ["reward_key"] = rewardKey });
                if (isMaster) AddWallet(db, characterId, 0, StageMasterVirtue[nextStep], 0);
                else AddWallet(db, characterId, StageApprenticeCoin[nextStep], 0, 0);
                db.Table("mentorship_relation").Update(DbStore.Eq("id", relation.id),
                    new DbRow { ["reward_step"] = (long)(nextStep + 1) });
                any = true;
                nextStep++;
            }
        }
        return any ? "已领取阶段奖励，具体数额见钱包变化。"
            : "当前没有可领取的阶段奖励。";
    }

    // claimWeeklyPool（L256-303）：每 100 积分一单位，师傅 8 师德币 / 徒弟 12 师徒币，
    // 每人每周每关系一次。
    public static string ClaimWeekly(DbStore db, long characterId)
    {
        CheckAutoGraduation(db, characterId);
        string weekKey = WeekKey();
        bool any = false;
        foreach (var relation in RelationsOf(db, characterId))
        {
            if (relation.status == StatusCancelled) continue;
            bool isMaster = relation.masterCid == characterId;
            var pool = db.Table("mentorship_weekly_pool")
                .Find(r => DbStore.Int(r, "relationid") == relation.id && DbStore.Str(r, "week_key") == weekKey);
            if (pool == null || DbStore.Int(pool, "points") <= 0) continue;
            string claimColumn = isMaster ? "claim_master" : "claim_apprentice";
            if (DbStore.Int(pool, claimColumn) != 0) continue;
            long units = Math.Max(1, DbStore.Int(pool, "points") / WeeklyUnitPoints);
            long coin = units * (isMaster ? WeeklyMasterCoin : WeeklyApprenticeCoin);
            pool[claimColumn] = 1L;
            if (isMaster) AddWallet(db, characterId, 0, coin, 0);
            else AddWallet(db, characterId, coin, 0, 0);
            any = true;
        }
        return any ? "已领取本周历练池奖励，数额见钱包变化。"
            : "本周暂无可领取历练池奖励。";
    }

    // —— 对决积分赛（L305-417）——
    public static string StartDuel(DbStore db, long characterId, long stake)
    {
        if (stake < MinDuelStake) return "对决下注最少需要 100 枚师德币。";
        var relation = FindRelation(db, characterId);
        if (relation == null) return "需要先建立师徒关系，才能参加对决积分赛。";
        foreach (var row in db.Table("mentorship_duel").rows)
        {
            int status = DbStore.Int(row, "status");
            if ((status == 0 || status == 1) &&
                (DbStore.Int(row, "relation_a") == relation.id || DbStore.Int(row, "relation_b") == relation.id))
                return "你的师徒组合已经有进行中或排队中的对决。";
        }
        if (!SpendCoins(db, characterId, true, stake)) return "师德币不足，无法下注。";
        db.Table("mentorship_duel").Insert(new DbRow
        {
            ["relation_a"] = relation.id, ["relation_b"] = 0L, ["stake"] = stake,
            ["points_a"] = 0L, ["points_b"] = 0L, ["status"] = 0L, ["winner_relation"] = 0L,
            ["created_at"] = Now(), ["ends_at"] = 0L, ["finished_at"] = 0L,
        });
        return "已报名 24 小时对决积分赛，等待另一组师徒加入。";
    }

    public static string JoinDuel(DbStore db, long characterId, long stakeLimit)
    {
        var relation = FindRelation(db, characterId);
        if (relation == null) return "需要先建立师徒关系，才能参加对决积分赛。";
        foreach (var row in db.Table("mentorship_duel").rows)
        {
            if (DbStore.Int(row, "status") != 0 || DbStore.Int(row, "relation_a") == relation.id) continue;
            long stake = DbStore.Int(row, "stake");
            if (stake < MinDuelStake || stake > stakeLimit) continue;
            var masterRow = db.Table("mentorship_relation").Find(DbStore.Eq("id", DbStore.Int(row, "relation_a")));
            long masterCid = masterRow != null ? DbStore.Int(masterRow, "master_cid") : 0;
            // 源端 joinDuel 扣的是排队方已下注的 stake（L343-385 里由对方负担），匹配方不再扣。
            row["relation_b"] = relation.id;
            row["status"] = 1L;
            row["ends_at"] = Now() + 24L * 3600 * 1000;
            return "匹配成功，24 小时内获得的师徒积分会计入对决。";
        }
        return "暂时没有可匹配的对决队列。";
    }

    public static string DuelStatus(DbStore db, long characterId)
    {
        var relation = FindRelation(db, characterId);
        if (relation == null) return "当前没有排队中或进行中的对决。";
        foreach (var row in db.Table("mentorship_duel").rows)
        {
            if (DbStore.Int(row, "relation_a") != relation.id && DbStore.Int(row, "relation_b") != relation.id) continue;
            int status = DbStore.Int(row, "status");
            if (status != 0 && status != 1) continue;
            return "对决状态：" + (status == 0 ? "排队中" : "进行中") + "\r\n"
                + "A 组积分：" + DbStore.Int(row, "points_a") + "\r\n"
                + "B 组积分：" + DbStore.Int(row, "points_b") + "\r\n"
                + "下注：" + DbStore.Int(row, "stake") + " 师德币";
        }
        return "当前没有排队中或进行中的对决。";
    }

    // getRankingText（L419-462）。
    public static string Ranking(DbStore db, long characterId, string type)
    {
        bool weekly = type == "weekly";
        var rows = new List<(string name, long points)>();
        if (weekly)
        {
            string weekKey = WeekKey();
            foreach (var pool in db.Table("mentorship_weekly_pool").rows)
            {
                if (DbStore.Str(pool, "week_key") != weekKey || DbStore.Int(pool, "points") <= 0) continue;
                var relation = db.Table("mentorship_relation").Find(DbStore.Eq("id", DbStore.Int(pool, "relationid")));
                if (relation == null) continue;
                int status = DbStore.Int(relation, "status");
                if (status != StatusActive && status != StatusGraduated) continue;
                rows.Add((DbStore.Str(relation, "master_name") + " / " + DbStore.Str(relation, "apprentice_name"),
                    DbStore.Int(pool, "points")));
            }
        }
        else
        {
            foreach (var relation in db.Table("mentorship_relation").rows)
            {
                int status = DbStore.Int(relation, "status");
                if (status != StatusActive && status != StatusGraduated) continue;
                long points = DbStore.Int(relation, "total_points");
                if (points <= 0) continue;
                rows.Add((DbStore.Str(relation, "master_name") + " / " + DbStore.Str(relation, "apprentice_name"), points));
            }
        }
        rows.Sort((a, b) => b.points.CompareTo(a.points));
        var text = new System.Text.StringBuilder(weekly ? "#e本周师徒榜#n\r\n\r\n" : "#e累计师徒榜#n\r\n\r\n");
        if (rows.Count == 0) return text.Append("暂无排行数据。").ToString();
        for (int i = 0; i < rows.Count && i < 10; i++)
            text.Append(i + 1).Append(". #b").Append(rows[i].name).Append("#k  ").Append(rows[i].points).Append("\r\n");
        return text.ToString();
    }

    // —— 积分入口 ——
    // addPoints（L601-620）：eventKey 幂等 → 关系总分 / 周池 / 双方钱包 / 进行中对局。
    public static void AddPoints(DbStore db, long relationId, int points, string eventKey)
    {
        var relationRow = db.Table("mentorship_relation").Find(DbStore.Eq("id", relationId));
        if (relationRow == null || DbStore.Int(relationRow, "status") != StatusActive) return;
        var logs = db.Table("mentorship_event_log");
        if (logs.Find(r => DbStore.Str(r, "event_key") == eventKey && DbStore.Int(r, "relationid") == relationId) != null) return;
        logs.Insert(new DbRow { ["event_key"] = eventKey, ["relationid"] = relationId, ["points"] = (long)points });

        relationRow["total_points"] = DbStore.Int(relationRow, "total_points") + points;
        long masterCid = DbStore.Int(relationRow, "master_cid");
        long apprenticeCid = DbStore.Int(relationRow, "apprentice_cid");

        string weekKey = WeekKey();
        var pool = db.Table("mentorship_weekly_pool")
            .Find(r => DbStore.Int(r, "relationid") == relationId && DbStore.Str(r, "week_key") == weekKey);
        if (pool == null)
            db.Table("mentorship_weekly_pool").Insert(new DbRow
            { ["relationid"] = relationId, ["week_key"] = weekKey, ["points"] = (long)points,
                ["claim_master"] = 0L, ["claim_apprentice"] = 0L });
        else pool["points"] = DbStore.Int(pool, "points") + points;

        AddWallet(db, masterCid, 0, points / 10, points);
        AddWallet(db, apprenticeCid, points / 5, 0, points);
        foreach (var duel in db.Table("mentorship_duel").rows)
        {
            if (DbStore.Int(duel, "status") != 1) continue;
            if (DbStore.Int(duel, "relation_a") == relationId)
                duel["points_a"] = DbStore.Int(duel, "points_a") + points;
            else if (DbStore.Int(duel, "relation_b") == relationId)
                duel["points_b"] = DbStore.Int(duel, "points_b") + points;
        }
    }

    // recordGpqClear（L512-536）：GPQ 通关给每个参与关系 +100。
    public static void RecordGpqClear(DbStore db, List<long> participantIds)
    {
        string eventKey = "gpq:" + Now() + ":" + string.Join(",", participantIds ?? new List<long>());
        foreach (var participant in participantIds ?? new List<long>())
            foreach (var relation in RelationsOf(db, participant))
                if (relation.status == StatusActive) AddPoints(db, relation.id, GpqClearPoints, eventKey);
    }

    // finishExpiredDuels（L767-806）：到点结算，胜组师傅拿 stake*2 师德币。
    public static void FinishExpiredDuels(DbStore db)
    {
        long now = Now();
        foreach (var duel in db.Table("mentorship_duel").rows)
        {
            if (DbStore.Int(duel, "status") != 1 || (DbStore.Int(duel, "ends_at") > now && DbStore.Int(duel, "ends_at") != 0)) continue;
            long winner = DbStore.Int(duel, "points_a") >= DbStore.Int(duel, "points_b")
                ? DbStore.Int(duel, "relation_a") : DbStore.Int(duel, "relation_b");
            long stake = DbStore.Int(duel, "stake");
            var relation = db.Table("mentorship_relation").Find(DbStore.Eq("id", winner));
            if (stake > 0 && relation != null)
                AddWallet(db, DbStore.Int(relation, "master_cid"), 0, stake * 2, 0);
            duel["status"] = 2L;
            duel["winner_relation"] = winner;
            duel["finished_at"] = now;
        }
    }

    static string WeekKey()
    {
        var now = DateTime.UtcNow;
        // ISO 周键（源端 WeekFields.ISO，格式 %04dW%02d）。
        int week = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        int year = now.Month == 1 && week >= 52 ? now.Year - 1 : now.Year;
        return string.Format(CultureInfo.InvariantCulture, "{0:D4}W{1:D2}", year, week);
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
