using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Port of GuildOperationHandler, Guild, Family*Handler and FamilyEntry.
// No Unity dependencies. Only registered sessions count as online; saved characters do not.
public enum CommunityOperation {
    Read, CreateGuild, ConfirmGuild, InviteGuild, AcceptGuild, LeaveGuild, DisbandGuild,
    ExpelGuild, GuildRank, GuildTitles, GuildNotice, GuildEmblem, ExpandGuild,
    InviteFamily, AcceptFamily, SeparateFamily, FamilyPrecepts, UseFamily,
    SetBoard, CloseBoard, AnswerSummon, RecordKill
}
public sealed class CommunityRequest {
    public int characterId, targetId, value, logo, logoColor, background, backgroundColor;
    public CommunityOperation operation;
    public string name = "", text = "";
    public string[] titles;
}
public sealed class CommunityMember {
    public int id, level, rank, senior;
    public string name;
    public bool online;
}
public sealed class CommunityResponse {
    public bool ok = true;
    public string error = "", guildName = "", notice = "", precepts = "", board = "";
    public int guildId, capacity, gp, familyId, reputation, todayRep, totalRep, meso, map;
    public int guildInvite, familyInvite, guildConfirmation, summonFrom;
    public string[] titles = new string[5];
    public List<CommunityMember> guild = new List<CommunityMember>(), family = new List<CommunityMember>();
    public List<int> used = new List<int>();
    public double expRate = 1, dropRate = 1;
    public long buffExpires;
}
public sealed class NativeCommunityService {
    readonly DbStore db;
    readonly HashSet<int> online = new HashSet<int>();
    readonly Dictionary<int, int> guildInvites = new Dictionary<int, int>(), familyInvites = new Dictionary<int, int>();
    readonly Dictionary<int, long> inviteTimes = new Dictionary<int, long>();
    readonly Dictionary<int, int> summons = new Dictionary<int, int>();
    readonly Dictionary<int, string> boards = new Dictionary<int, string>();
    readonly Dictionary<int, GuildFormation> formations = new Dictionary<int, GuildFormation>();
    sealed class GuildFormation { public string name; public long expires; public HashSet<int> members = new HashSet<int>(), accepted = new HashSet<int>(); }
    public static readonly int[] FamilyCosts = { 300, 500, 700, 800, 1000, 1200, 1500, 2000, 2500, 4000, 5000 };
    public NativeCommunityService(DbStore db) { this.db = db; }
    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void BeginSession(int id) { online.Add(id); }
    public void EndSession(int id) {
        online.Remove(id); boards.Remove(id); guildInvites.Remove(id); familyInvites.Remove(id);
        formations.Remove(id); summons.Remove(id);
        foreach (var key in new List<int>(formations.Keys)) if (formations[key].members.Contains(id)) formations.Remove(key);
        // Source family buffs end when a character logs out.
        db.Table("family_buff").Delete(r => DbStore.Int(r, "charid") == id);
    }
    public void DeleteCharacter(int id) {
        EndSession(id);
        var c = Character(id); var g = c == null ? null : Guild(c.guildid);
        if (g != null && DbStore.Int(g, "leader") == id) {
            foreach (var row in db.Table("characters").Select(x => DbStore.Int(x, "guildid") == c.guildid)) Membership(DbStore.Int(row, "id"), 0, 5);
            db.Table("guilds").Delete(x => DbStore.Int(x, "guildid") == c.guildid);
        }
        foreach (var child in db.Table("family_character").Select(x => DbStore.Int(x, "seniorid") == id)) {
            int childId = DbStore.Int(child, "cid"); child["seniorid"] = 0L; Rehome(childId, childId);
        }
        db.Table("family_character").Delete(x => DbStore.Int(x, "cid") == id);
        db.Table("family_entitlement").Delete(x => DbStore.Int(x, "charid") == id);
        db.Table("cash_gifts").Delete(x => DbStore.Int(x, "characterid") == id);
        db.Table("cash_wishlist").Delete(x => DbStore.Int(x, "characterid") == id);
        db.Table("cash_items").Delete(x => DbStore.Int(x, "characterid") == id);
    }
    public bool IsOnline(int id) => online.Contains(id);
    CharRecord Character(int id) => CharactersDao.FindById(db, id);
    CharRecord Find(string name, int world) {
        var row = db.Table("characters").Find(r => DbStore.Int(r, "world") == world && string.Equals(DbStore.Str(r, "name"), name, StringComparison.OrdinalIgnoreCase));
        return row == null ? null : Character(DbStore.Int(row, "id"));
    }
    DbRow Guild(int id) => db.Table("guilds").Find(r => DbStore.Int(r, "guildid") == id);
    DbRow Family(int id) => db.Table("family_character").Find(r => DbStore.Int(r, "cid") == id);
    List<DbRow> FamilyMembers(int id) => db.Table("family_character").Select(r => DbStore.Int(r, "familyid") == id);
    void SetCharacter(int id, string column, int value) => db.Table("characters").Update(r => DbStore.Int(r, "id") == id, new DbRow { [column] = (long)value });
    void Meso(CharRecord c, int amount) { c.meso += amount; SetCharacter((int)c.id, "meso", c.meso); }
    void Membership(int id, int guild, int rank) { SetCharacter(id, "guildid", guild); SetCharacter(id, "guildrank", rank); }
    void ResetDaily(DbRow row) {
        long midnight = new DateTimeOffset(DateTime.Today).ToUnixTimeMilliseconds();
        if (DbStore.Long(row, "lastresettime") >= midnight) return;
        row["lastresettime"] = Now; row["todaysrep"] = 0L; row["reptosenior"] = 0L;
        int cid = DbStore.Int(row, "cid");
        db.Table("family_entitlement").Delete(r => DbStore.Int(r, "charid") == cid && DbStore.Long(r, "timestamp") < midnight);
    }
    public CommunityResponse Handle(CommunityRequest r) {
        var self = Character(r.characterId);
        if (self == null) return new CommunityResponse { ok = false, error = "角色不存在" };
        string error;
        lock (db) {
            db.BeginTransaction();
            try { error = Apply(self, r); if (error == null) db.Commit(); else db.Rollback(); }
            catch { db.Rollback(); throw; }
            db.Save();
            var result = Read(Character(r.characterId));
            result.ok = error == null; result.error = error ?? ""; return result;
        }
    }
    string Apply(CharRecord c, CommunityRequest r) {
        int cid = (int)c.id;
        var guild = Guild(c.guildid);
        var family = Family(cid);
        bool master = guild != null && c.guildrank == 1, manager = guild != null && c.guildrank <= 2;
        var target = r.targetId > 0 ? Character(r.targetId) : Find(r.name, c.world);
        switch (r.operation) {
            case CommunityOperation.Read: return null;
            case CommunityOperation.RecordKill:
                if (r.value > 1) GiveReputation(cid, GameConfigDao.GetInt(db, r.targetId == 1 ? "family_rep_per_boss_kill" : "family_rep_per_kill", r.targetId == 1 ? 20 : 4));
                return null;
            case CommunityOperation.AnswerSummon:
                if (!summons.TryGetValue(cid, out int summonerId)) return "召集请求已失效";
                var summoner = Character(summonerId); summons.Remove(cid);
                if (r.value == 0) return null;
                if (summoner == null || !online.Contains(summonerId) || c.familyId != summoner.familyId || CanFamilyTravel == null || !CanFamilyTravel(c.map, summoner.map)) return "召集条件已改变";
                SetCharacter(cid, "map", summoner.map); return null;
            case CommunityOperation.CreateGuild:
                if (c.guildid > 0) return "已经加入家族";
                if (c.meso < GameConfigDao.GetInt(db, "create_guild_cost", 1500000)) return "创建家族需要150万金币（以服务器配置为准）";
                if (!Regex.IsMatch(r.name ?? "", @"^[\u4e00-\u9fa5a-zA-Z0-9_]{3,12}$")) return "家族名称须为3～12个中文、字母、数字或下划线";
                if (db.Table("guilds").Find(x => string.Equals(DbStore.Str(x, "name"), r.name, StringComparison.OrdinalIgnoreCase)) != null) return "家族名称已存在";
                if (c.level < 10 && !GameConfigDao.GetBool(db, "use_party_for_starters")) return "达到10级才能发起创建家族";
                var formation = new GuildFormation { name = r.name, expires = Now + 60000 };
                formation.members.Add(cid); formation.accepted.Add(cid);
                foreach (int id in online) { var m = Character(id); if (m != null && m.world == c.world && m.map == c.map && m.guildid == 0 && m.party == 0) formation.members.Add(id); }
                if (formation.members.Count < GameConfigDao.GetInt(db, "create_guild_min_partners", 6)) return "同地图内符合条件的人数不足，源端默认至少6人";
                formations[cid] = formation;
                return FinishFormation(cid);
            case CommunityOperation.ConfirmGuild:
                if (!formations.TryGetValue(r.targetId, out var pending) || pending.expires < Now || !pending.members.Contains(cid)) return "创建确认已失效";
                if (r.value == 0) { formations.Remove(r.targetId); return null; }
                if (c.guildid > 0) return "已经加入家族";
                pending.accepted.Add(cid); return FinishFormation(r.targetId);
            case CommunityOperation.InviteGuild:
                if (!manager) return "只有族长和副族长可以邀请";
                if (target == null || !online.Contains((int)target.id)) return "该角色不在线";
                if (target.world != c.world || target.guildid > 0) return "对方已加入家族或不在本世界";
                guildInvites[(int)target.id] = c.guildid; inviteTimes[(int)target.id] = Now; return null;
            case CommunityOperation.AcceptGuild:
                if (!guildInvites.TryGetValue(cid, out int gid) || !inviteTimes.TryGetValue(cid, out long sent) || Now - sent > 60000) return "家族邀请已失效";
                var invitedGuild = Guild(gid);
                if (invitedGuild == null || c.guildid > 0) return "无法加入该家族";
                if (db.Table("characters").Select(x => DbStore.Int(x, "guildid") == gid).Count >= DbStore.Int(invitedGuild, "capacity")) return "家族人数已满";
                Membership(cid, gid, 5); guildInvites.Remove(cid); return null;
            case CommunityOperation.LeaveGuild:
                if (guild == null) return "尚未加入家族";
                if (master) return "族长需要解散家族";
                Membership(cid, 0, 5); return null;
            case CommunityOperation.DisbandGuild:
                if (!master) return "只有族长可以解散";
                foreach (var member in db.Table("characters").Select(x => DbStore.Int(x, "guildid") == c.guildid)) Membership(DbStore.Int(member, "id"), 0, 5);
                db.Table("guilds").Delete(x => DbStore.Int(x, "guildid") == c.guildid); return null;
            case CommunityOperation.ExpelGuild:
                if (!manager || target == null || target.guildid != c.guildid || target.guildrank <= c.guildrank) return "没有权限驱逐该成员";
                Membership((int)target.id, 0, 5); return null;
            case CommunityOperation.GuildRank:
                if (!manager || target == null || target.guildid != c.guildid || target.id == c.id || target.guildrank <= c.guildrank || r.value < 2 || r.value > 5 || (r.value == 2 && !master)) return "职位或操作权限无效";
                Membership((int)target.id, c.guildid, r.value); return null;
            case CommunityOperation.GuildNotice:
                if (!manager) return "没有修改公告的权限";
                if (r.text == null || r.text.Length > 100) return "公告最多100个字符";
                guild["notice"] = r.text; return null;
            case CommunityOperation.GuildTitles:
                if (!master || r.titles == null || r.titles.Length != 5) return "只有族长可以修改五级职位名称";
                for (int i = 0; i < 5; i++) { if (string.IsNullOrEmpty(r.titles[i]) || r.titles[i].Length > 45) return "职位名称长度无效"; }
                for (int i = 0; i < 5; i++) guild["rank" + (i + 1) + "title"] = r.titles[i]; return null;
            case CommunityOperation.GuildEmblem:
                if (!master || c.map != 200000301) return "族长需在家族总部修改徽章";
                int emblemCost = GameConfigDao.GetInt(db, "change_emblem_cost", 5000000);
                if (c.meso < emblemCost) return "金币不足";
                if (r.logo < 0 || r.logo > ushort.MaxValue || r.background < 0 || r.background > ushort.MaxValue || r.logoColor < 0 || r.logoColor > 255 || r.backgroundColor < 0 || r.backgroundColor > 255) return "徽章参数无效";
                guild["logo"] = (long)r.logo; guild["logoColor"] = (long)r.logoColor; guild["logoBG"] = (long)r.background; guild["logoBGColor"] = (long)r.backgroundColor; Meso(c, -emblemCost); return null;
            case CommunityOperation.ExpandGuild:
                if (!master) return "只有族长可以扩容";
                int capacity = DbStore.Int(guild, "capacity"); if (capacity >= 100) return "家族容量已达上限";
                int cost = GameConfigDao.GetInt(db, "expand_guild_base_cost", 500000) + Math.Max(0, (capacity - 15) / 5) * GameConfigDao.GetInt(db, "expand_guild_tier_cost", 1000000);
                if (capacity > 30) cost = Math.Min(GameConfigDao.GetInt(db, "expand_guild_max_cost", 5000000), Math.Max(cost, 5000000));
                if (c.meso < cost) return "金币不足，需要" + cost;
                guild["capacity"] = (long)(capacity + 5); Meso(c, -cost); return null;
            case CommunityOperation.InviteFamily:
                if (!GameConfigDao.GetBool(db, "use_family_system", true)) return "学院系统已关闭";
                if (target == null || !online.Contains((int)target.id)) return "该角色不在线";
                if (target.id == c.id || target.world != c.world || target.map != c.map) return "需要邀请同地图的其他角色";
                if (target.level <= 10) return "对方需要超过10级";
                if (Math.Abs(target.level - c.level) > GameConfigDao.GetInt(db, "family_add_add_level_range", 240)) return "等级差超过学院限制";
                if (family != null && FamilyMembers(DbStore.Int(family, "familyid")).FindAll(x => DbStore.Int(x, "seniorid") == cid).Count >= 2) return "最多登记两名直属同学";
                if (family != null && c.familyId == target.familyId) return "已经在同一学院";
                familyInvites[(int)target.id] = cid; inviteTimes[-(int)target.id] = Now; return null;
            case CommunityOperation.AcceptFamily:
                if (!familyInvites.TryGetValue(cid, out int seniorId) || !inviteTimes.TryGetValue(-cid, out long familySent) || Now - familySent > 60000 || !online.Contains(seniorId)) return "学院邀请已失效";
                var senior = Character(seniorId); if (senior == null) return "学长不存在";
                if (family != null && DbStore.Int(family, "seniorid") != 0) return "只有学院首领可以合并学院";
                var seniorEntry = Family(seniorId);
                int fid = seniorEntry == null ? seniorId : DbStore.Int(seniorEntry, "familyid");
                if (fid == c.familyId && family != null) return "不能形成循环学院关系";
                int generations = Depth(seniorId) + Height(cid);
                if (generations > GameConfigDao.GetInt(db, "family_max_generations", 1000)) return "学院代数超过上限";
                if (seniorEntry == null) AddFamily(seniorId, fid, 0);
                if (FamilyMembers(fid).FindAll(x => DbStore.Int(x, "seniorid") == seniorId).Count >= 2) return "学长已登记两名直属同学";
                if (family == null) AddFamily(cid, fid, seniorId);
                else { int oldId = DbStore.Int(family, "familyid"); family["seniorid"] = (long)seniorId; foreach (var m in FamilyMembers(oldId)) { m["familyid"] = (long)fid; SetCharacter(DbStore.Int(m, "cid"), "familyId", fid); } }
                familyInvites.Remove(cid); return null;
            case CommunityOperation.SeparateFamily:
                if (family == null) return "尚未加入学院";
                var fork = r.targetId == 0 || r.targetId == cid ? family : Family(r.targetId);
                if (fork == null || (DbStore.Int(fork, "cid") != cid && DbStore.Int(fork, "seniorid") != cid)) return "只能解除直属学院关系";
                var oldSenior = Character(DbStore.Int(fork, "seniorid")); if (oldSenior == null) return "没有可解除的学长关系";
                int diff = Math.Abs(c.level - oldSenior.level), separationCost = 2500 * diff + diff * diff;
                if (c.meso < separationCost) return "金币不足，需要" + separationCost;
                var junior = Character(DbStore.Int(fork, "cid")); int penalty = (junior.level / 20 + 10) * junior.level * 2;
                GainRep(Family((int)oldSenior.id), -penalty, false);
                GainRep(Family(DbStore.Int(Family((int)oldSenior.id), "seniorid")), -(penalty / 2), false);
                Meso(c, -separationCost); fork["seniorid"] = 0L; Rehome(DbStore.Int(fork, "cid"), DbStore.Int(fork, "cid")); return null;
            case CommunityOperation.FamilyPrecepts:
                if (family == null || DbStore.Int(family, "seniorid") != 0) return "只有学院首领可以修改家训";
                if (r.text == null || r.text.Length > 200) return "家训最多200个字符";
                foreach (var m in FamilyMembers(DbStore.Int(family, "familyid"))) m["precepts"] = r.text; return null;
            case CommunityOperation.UseFamily: return UseFamily(c, family, r.value, target);
            case CommunityOperation.SetBoard:
                if (c.map >= 910000001 && c.map <= 910000022) return "自由市场房间内不能使用黑板";
                bool hasBoard = false; foreach (var item in InventoryDao.ItemsByCharacter(db, cid)) if (DbStore.Int(item, "itemid") / 10000 == 537 && DbStore.Int(item, "quantity") > 0) hasBoard = true;
                if (!hasBoard) return "需要持有黑板类现金道具";
                if (r.text == null || r.text.Length > 255) return "黑板内容最多255个字符";
                boards[cid] = r.text; return null; // source intentionally does not consume item.
            case CommunityOperation.CloseBoard: boards.Remove(cid); return null;
        }
        return "无效操作";
    }
    string FinishFormation(int owner) {
        var f = formations[owner]; if (f.accepted.Count != f.members.Count) return null;
        var leader = Character(owner); int cost = GameConfigDao.GetInt(db, "create_guild_cost", 1500000);
        if (leader == null || leader.meso < cost) { formations.Remove(owner); return "创建条件已改变"; }
        foreach (int id in f.members) { var m = Character(id); if (m == null || m.guildid != 0 || m.map != leader.map || !online.Contains(id)) { formations.Remove(owner); return "创建成员状态已改变"; } }
        int gid = (int)db.Table("guilds").Insert(new DbRow { ["leader"] = (long)owner, ["name"] = f.name });
        foreach (int id in f.members) Membership(id, gid, id == owner ? 1 : 5);
        Meso(leader, -cost); formations.Remove(owner); return null;
    }
    void AddFamily(int cid, int fid, int senior) {
        db.Table("family_character").Insert(new DbRow { ["cid"] = (long)cid, ["familyid"] = (long)fid, ["seniorid"] = (long)senior }); SetCharacter(cid, "familyId", fid);
    }
    int Depth(int id) { int d = 1; var seen = new HashSet<int>(); while (seen.Add(id)) { var row = Family(id); if (row == null || DbStore.Int(row, "seniorid") == 0) break; d++; id = DbStore.Int(row, "seniorid"); } return d; }
    int Height(int id) { int h = 1; foreach (var row in db.Table("family_character").Select(x => DbStore.Int(x, "seniorid") == id)) h = Math.Max(h, 1 + Height(DbStore.Int(row, "cid"))); return h; }
    void Rehome(int id, int fid) { var row = Family(id); if (row == null) return; row["familyid"] = (long)fid; SetCharacter(id, "familyId", fid); foreach (var child in db.Table("family_character").Select(x => DbStore.Int(x, "seniorid") == id)) Rehome(DbStore.Int(child, "cid"), fid); }
    void GainRep(DbRow row, int value, bool total) { if (row == null) return; ResetDaily(row); row["reputation"] = (long)(DbStore.Int(row, "reputation") + value); row["todaysrep"] = (long)(DbStore.Int(row, "todaysrep") + value); if (total && value > 0) row["totalreputation"] = (long)(DbStore.Int(row, "totalreputation") + value); }
    public void GiveReputation(int cid, int amount) {
        var junior = Family(cid); if (junior == null) return; var senior = Family(DbStore.Int(junior, "seniorid")); if (senior == null) return;
        var s = Character(DbStore.Int(senior, "cid")); var c = Character(cid); if (s == null || c == null) return;
        if (s.level < c.level && amount > 0) amount /= 2;
        GainRep(senior, amount, true); junior["reptosenior"] = (long)(DbStore.Int(junior, "reptosenior") + Math.Max(0, amount));
        if (GameConfigDao.GetBool(db, "family_rep_per_level_up_notice", true)) GainRep(Family(DbStore.Int(senior, "seniorid")), amount, true);
        db.Save();
    }
    string UseFamily(CharRecord c, DbRow family, int type, CharRecord target) {
        if (!GameConfigDao.GetBool(db, "use_family_system", true) || family == null) return "尚未加入学院";
        if (type < 0 || type >= FamilyCosts.Length) return "无效特权";
        ResetDaily(family);
        if (DbStore.Int(family, "reputation") < FamilyCosts[type]) return "学院声望不足";
        if (type >= 2 && db.Table("family_entitlement").Find(x => DbStore.Int(x, "charid") == c.id && DbStore.Int(x, "entitlementid") == type) != null) return "此特权今天已经使用";
        if (type <= 1) {
            if (target == null || target.id == c.id || !online.Contains((int)target.id)) return "目标同学不在线";
            if (target.familyId != c.familyId) return "目标不在同一学院";
            // Map legality is supplied by the server map provider, never client-selected destinations.
            if (CanFamilyTravel == null || !CanFamilyTravel(type == 0 ? c.map : target.map, type == 0 ? target.map : c.map)) return "这些地图之间不能使用学院传送";
            if (type == 1) { if (summons.ContainsKey((int)target.id)) return "对方已有召集请求"; summons[(int)target.id] = (int)c.id; }
            else SetCharacter((int)c.id, "map", target.map);
        } else {
            if (Rates((int)c.id).buffExpires > Now) return "学院增益尚未结束";
            double exp = type == 3 ? 1.5 : type == 6 || type == 8 || type == 10 || type == 4 ? 2 : 1;
            double drop = type == 2 ? 1.5 : type == 5 || type == 7 || type == 9 || type == 4 ? 2 : 1;
            int duration = type == 2 || type == 3 || type == 5 || type == 6 ? 15 : 30;
            var recipients = new List<int> { (int)c.id };
            if (type == 4) { recipients.Clear(); foreach (var m in FamilyMembers(c.familyId)) if (online.Contains(DbStore.Int(m, "cid"))) recipients.Add(DbStore.Int(m, "cid")); }
            if (type >= 9) { if (c.party == 0) return "组队特权需要加入组队"; recipients.Clear(); foreach (int id in online) { var m = Character(id); if (m != null && m.party == c.party && m.familyId == c.familyId) recipients.Add(id); } }
            foreach (int id in recipients) { db.Table("family_buff").Delete(x => DbStore.Int(x, "charid") == id); db.Table("family_buff").Insert(new DbRow { ["charid"] = (long)id, ["expires"] = Now + duration * 60000L, ["expRate"] = exp, ["dropRate"] = drop }); }
            db.Table("family_entitlement").Insert(new DbRow { ["charid"] = c.id, ["entitlementid"] = (long)type, ["timestamp"] = Now });
        }
        GainRep(family, -FamilyCosts[type], false); return null;
    }
    public Func<int, int, bool> CanFamilyTravel;
    public CommunityResponse Rates(int cid) {
        var result = new CommunityResponse(); var row = db.Table("family_buff").Find(x => DbStore.Int(x, "charid") == cid && DbStore.Long(x, "expires") > Now);
        if (row != null) { result.expRate = DbStore.Dbl(row, "expRate", 1); result.dropRate = DbStore.Dbl(row, "dropRate", 1); result.buffExpires = DbStore.Long(row, "expires"); } return result;
    }
    CommunityResponse Read(CharRecord c) {
        var result = Rates((int)c.id); result.meso = c.meso; result.map = c.map; result.guildId = c.guildid;
        var guild = Guild(c.guildid); if (guild != null) {
            result.guildName = DbStore.Str(guild, "name"); result.notice = DbStore.Str(guild, "notice"); result.capacity = DbStore.Int(guild, "capacity"); result.gp = DbStore.Int(guild, "GP");
            for (int i = 0; i < 5; i++) result.titles[i] = DbStore.Str(guild, "rank" + (i + 1) + "title");
            foreach (var row in db.Table("characters").Select(x => DbStore.Int(x, "guildid") == c.guildid)) result.guild.Add(Member(Character(DbStore.Int(row, "id"))));
        }
        var family = Family((int)c.id); if (family != null) {
            ResetDaily(family); result.familyId = DbStore.Int(family, "familyid"); result.reputation = DbStore.Int(family, "reputation"); result.todayRep = DbStore.Int(family, "todaysrep"); result.totalRep = DbStore.Int(family, "totalreputation"); result.precepts = DbStore.Str(family, "precepts");
            foreach (var row in FamilyMembers(result.familyId)) { var m = Character(DbStore.Int(row, "cid")); if (m != null) { var member = Member(m); member.senior = DbStore.Int(row, "seniorid"); result.family.Add(member); } }
            foreach (var row in db.Table("family_entitlement").Select(x => DbStore.Int(x, "charid") == c.id)) result.used.Add(DbStore.Int(row, "entitlementid"));
        }
        summons.TryGetValue((int)c.id, out result.summonFrom);
        guildInvites.TryGetValue((int)c.id, out result.guildInvite); familyInvites.TryGetValue((int)c.id, out result.familyInvite);
        foreach (var f in formations) if (f.Value.expires > Now && f.Value.members.Contains((int)c.id) && !f.Value.accepted.Contains((int)c.id)) result.guildConfirmation = f.Key;
        if (boards.TryGetValue((int)c.id, out string board)) result.board = board; return result;
    }
    CommunityMember Member(CharRecord c) => new CommunityMember { id = (int)c.id, name = c.name, level = c.level, rank = c.guildrank, online = online.Contains((int)c.id) };
}
