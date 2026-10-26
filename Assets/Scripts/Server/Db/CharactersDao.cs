// characters 表的强类型行。字段与源端 V1.0.6__create_characters.sql + 3 个 ALTER 一一对应。
// 读档/存档/建号都走这个结构，避免字符串列名散落业务层。
using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class CharRecord
{
    public long id;
    public int accountid, world;
    public string name;
    public int level, exp, gachaexp;
    public int str, dex, luk, int_;
    public int hp, mp, maxhp, maxmp, meso, hpMpUsed;
    public int job, skincolor, gender, fame, fquest, hair, face, ap;
    public string sp = "0,0,0,0,0,0,0,0,0,0";
    public int map, spawnpoint, gm, party, buddyCapacity = 25;
    public string createdate;
    public int rank = 1, rankMove, jobRank = 1, jobRankMove, guildid, guildrank = 5;
    public int messengerid, messengerposition = 4, mountlevel = 1, mountexp, mounttiredness;
    public int omokwins, omoklosses, omokties, matchcardwins, matchcardlosses, matchcardties;
    public int MerchantMesos, HasMerchant;
    public int equipslots = 24, useslots = 24, setupslots = 24, etcslots = 24;
    public int familyId = -1, monsterbookcover, allianceRank = 5;
    public int vanquisherStage, ariantPoints, dojoPoints, lastDojoStage, finishedDojoTutorial, vanquisherKills, summonValue;
    public int partnerId, marriageItemId, reborns, PQPoints;
    public string dataString;
    public string lastLogoutTime = "2015-01-01 05:00:00", lastExpGainTime = "2015-01-01 05:00:00";
    public int partySearch = 1, jailexpire;
    public int fishLevel = 1, damageSkinId, damageCap = 19999999;

    // —————————————————— 行 ↔ 记录 ——————————————————

    public static CharRecord FromRow(DbRow r)
    {
        var c = new CharRecord();
        c.id = L(r, "id");
        c.accountid = I(r, "accountid"); c.world = I(r, "world");
        c.name = S(r, "name");
        c.level = I(r, "level"); c.exp = I(r, "exp"); c.gachaexp = I(r, "gachaexp");
        c.str = I(r, "str"); c.dex = I(r, "dex"); c.luk = I(r, "luk"); c.int_ = I(r, "int");
        c.hp = I(r, "hp"); c.mp = I(r, "mp"); c.maxhp = I(r, "maxhp"); c.maxmp = I(r, "maxmp");
        c.meso = I(r, "meso"); c.hpMpUsed = I(r, "hpMpUsed");
        c.job = I(r, "job"); c.skincolor = I(r, "skincolor"); c.gender = I(r, "gender");
        c.fame = I(r, "fame"); c.fquest = I(r, "fquest"); c.hair = I(r, "hair"); c.face = I(r, "face");
        c.ap = I(r, "ap"); c.sp = S(r, "sp");
        c.map = I(r, "map"); c.spawnpoint = I(r, "spawnpoint"); c.gm = I(r, "gm"); c.party = I(r, "party");
        c.buddyCapacity = I(r, "buddyCapacity"); c.createdate = S(r, "createdate");
        c.rank = I(r, "rank"); c.rankMove = I(r, "rankMove"); c.jobRank = I(r, "jobRank"); c.jobRankMove = I(r, "jobRankMove");
        c.guildid = I(r, "guildid"); c.guildrank = I(r, "guildrank");
        c.messengerid = I(r, "messengerid"); c.messengerposition = I(r, "messengerposition");
        c.mountlevel = I(r, "mountlevel"); c.mountexp = I(r, "mountexp"); c.mounttiredness = I(r, "mounttiredness");
        c.omokwins = I(r, "omokwins"); c.omoklosses = I(r, "omoklosses"); c.omokties = I(r, "omokties");
        c.matchcardwins = I(r, "matchcardwins"); c.matchcardlosses = I(r, "matchcardlosses"); c.matchcardties = I(r, "matchcardties");
        c.MerchantMesos = I(r, "MerchantMesos"); c.HasMerchant = I(r, "HasMerchant");
        c.equipslots = I(r, "equipslots"); c.useslots = I(r, "useslots"); c.setupslots = I(r, "setupslots"); c.etcslots = I(r, "etcslots");
        c.familyId = I(r, "familyId"); c.monsterbookcover = I(r, "monsterbookcover"); c.allianceRank = I(r, "allianceRank");
        c.vanquisherStage = I(r, "vanquisherStage"); c.ariantPoints = I(r, "ariantPoints"); c.dojoPoints = I(r, "dojoPoints");
        c.lastDojoStage = I(r, "lastDojoStage"); c.finishedDojoTutorial = I(r, "finishedDojoTutorial"); c.vanquisherKills = I(r, "vanquisherKills");
        c.summonValue = I(r, "summonValue"); c.partnerId = I(r, "partnerId"); c.marriageItemId = I(r, "marriageItemId");
        c.reborns = I(r, "reborns"); c.PQPoints = I(r, "PQPoints"); c.dataString = S(r, "dataString");
        c.lastLogoutTime = S(r, "lastLogoutTime"); c.lastExpGainTime = S(r, "lastExpGainTime");
        c.partySearch = I(r, "partySearch"); c.jailexpire = I(r, "jailexpire");
        c.fishLevel = I(r, "fishLevel"); c.damageSkinId = I(r, "damageSkinId"); c.damageCap = I(r, "damageCap");
        return c;
    }

    public DbRow ToInsertRow()
    {
        return new DbRow
        {
            ["accountid"] = (long)accountid, ["world"] = (long)world, ["name"] = name,
            ["level"] = (long)level, ["exp"] = (long)exp, ["gachaexp"] = (long)gachaexp,
            ["str"] = (long)str, ["dex"] = (long)dex, ["luk"] = (long)luk, ["int"] = (long)int_,
            ["hp"] = (long)hp, ["mp"] = (long)mp, ["maxhp"] = (long)maxhp, ["maxmp"] = (long)maxmp,
            ["meso"] = (long)meso, ["hpMpUsed"] = (long)hpMpUsed, ["job"] = (long)job,
            ["skincolor"] = (long)skincolor, ["gender"] = (long)gender, ["fame"] = (long)fame, ["fquest"] = (long)fquest,
            ["hair"] = (long)hair, ["face"] = (long)face, ["ap"] = (long)ap, ["sp"] = sp,
            ["map"] = (long)map, ["spawnpoint"] = (long)spawnpoint, ["gm"] = (long)gm, ["party"] = (long)party,
            ["buddyCapacity"] = (long)buddyCapacity, ["createdate"] = createdate ?? DbStore.NowText(),
            ["rank"] = (long)rank, ["rankMove"] = (long)rankMove, ["jobRank"] = (long)jobRank, ["jobRankMove"] = (long)jobRankMove,
            ["guildid"] = (long)guildid, ["guildrank"] = (long)guildrank,
            ["messengerid"] = (long)messengerid, ["messengerposition"] = (long)messengerposition,
            ["mountlevel"] = (long)mountlevel, ["mountexp"] = (long)mountexp, ["mounttiredness"] = (long)mounttiredness,
            ["omokwins"] = (long)omokwins, ["omoklosses"] = (long)omoklosses, ["omokties"] = (long)omokties,
            ["matchcardwins"] = (long)matchcardwins, ["matchcardlosses"] = (long)matchcardlosses, ["matchcardties"] = (long)matchcardties,
            ["MerchantMesos"] = (long)MerchantMesos, ["HasMerchant"] = (long)HasMerchant,
            ["equipslots"] = (long)equipslots, ["useslots"] = (long)useslots, ["setupslots"] = (long)setupslots, ["etcslots"] = (long)etcslots,
            ["familyId"] = (long)familyId, ["monsterbookcover"] = (long)monsterbookcover, ["allianceRank"] = (long)allianceRank,
            ["vanquisherStage"] = (long)vanquisherStage, ["ariantPoints"] = (long)ariantPoints, ["dojoPoints"] = (long)dojoPoints,
            ["lastDojoStage"] = (long)lastDojoStage, ["finishedDojoTutorial"] = (long)finishedDojoTutorial, ["vanquisherKills"] = (long)vanquisherKills,
            ["summonValue"] = (long)summonValue, ["partnerId"] = (long)partnerId, ["marriageItemId"] = (long)marriageItemId,
            ["reborns"] = (long)reborns, ["PQPoints"] = (long)PQPoints, ["dataString"] = dataString ?? "",
            ["lastLogoutTime"] = lastLogoutTime, ["lastExpGainTime"] = lastExpGainTime,
            ["partySearch"] = (long)partySearch, ["jailexpire"] = (long)jailexpire,
            ["fishLevel"] = (long)fishLevel, ["damageSkinId"] = (long)damageSkinId, ["damageCap"] = (long)damageCap,
        };
    }

    // 存档字段集：与源端 Character.saveCharToDB 的 UPDATE（Character.java:8324）同一批字段
    public DbRow ToUpdateRow()
    {
        var row = ToInsertRow();
        row.Remove("createdate");
        row.Remove("name");
        return row;
    }

    // sp 列是「按技能书分组的剩余 SP」逗号串（源端 GameConstants.getSkillBook 下标）
    public int GetSp(int bookIndex)
    {
        var parts = (sp ?? "").Split(',');
        if (bookIndex < 0 || bookIndex >= parts.Length) return 0;
        return int.TryParse(parts[bookIndex], out var v) ? v : 0;
    }

    public void SetSp(int bookIndex, int value)
    {
        var parts = (sp ?? "").Split(',');
        if (parts.Length < 10) parts = "0,0,0,0,0,0,0,0,0,0".Split(',');
        if (bookIndex < 0 || bookIndex >= parts.Length) return;
        parts[bookIndex] = value.ToString(CultureInfo.InvariantCulture);
        sp = string.Join(",", parts);
    }

    static int I(DbRow r, string k)
    {
        if (r == null || !r.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; }
    }

    static long L(DbRow r, string k)
    {
        if (r == null || !r.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return 0; }
    }

    static string S(DbRow r, string k)
    {
        if (r == null || !r.TryGetValue(k, out var v) || v == null) return "";
        return v as string ?? v.ToString();
    }
}

public static class CharactersDao
{
    public static List<CharRecord> ByAccount(DbStore db, int accountId, int world = 0)
    {
        var outp = new List<CharRecord>();
        foreach (var r in db.Table("characters").Select(DbStore.Eq("accountid", (long)accountId)))
        {
            var c = CharRecord.FromRow(r);
            if (world == 0 || c.world == world) outp.Add(c);
        }
        outp.Sort((a, b) => a.id.CompareTo(b.id));
        return outp;
    }

    public static CharRecord FindById(DbStore db, int id)
    {
        var r = db.Table("characters").Find(DbStore.Eq("id", (long)id));
        return r == null ? null : CharRecord.FromRow(r);
    }

    public static bool NameTaken(DbStore db, string name)
        => db.Table("characters").Find(DbStore.Eq("name", name)) != null;

    public static long Insert(DbStore db, CharRecord c)
        => db.Table("characters").Insert(c.ToInsertRow());

    // 存档：字段集同源端 saveCharToDB
    public static void Save(DbStore db, CharRecord c)
    {
        db.Table("characters").Update(DbStore.Eq("id", c.id), c.ToUpdateRow());
    }

    public static void Delete(DbStore db, int characterId)
    {
        new NativeCommunityService(db).DeleteCharacter(characterId);
        new NativeBossService(db).DeleteCharacter(characterId);
        db.Table("characters").Delete(DbStore.Eq("id", (long)characterId));
        db.Table("skills").Delete(DbStore.Eq("characterid", (long)characterId));
        db.Table("keymap").Delete(DbStore.Eq("characterid", (long)characterId));
        db.Table("inventoryitems").Delete(DbStore.Eq("characterid", (long)characterId));
        db.Table("queststatus").Delete(DbStore.Eq("characterid", (long)characterId));
        db.Table("questprogress").Delete(DbStore.Eq("characterid", (long)characterId));
        db.Table("buddies").Delete(r => DbStore.Int(r, "characterid") == characterId || DbStore.Int(r, "buddyid") == characterId);
        db.Table("monsterbook").Delete(DbStore.Eq("charid", (long)characterId));
        db.Table("savedlocations").Delete(DbStore.Eq("characterid", (long)characterId));
        db.Table("extend_value").Delete(r => DbStore.Str(r, "extend_id") == characterId.ToString(System.Globalization.CultureInfo.InvariantCulture) && DbStore.Int(r, "extend_type") >= 21 && DbStore.Int(r, "extend_type") <= 23);
    }

    // 源端 Character.canCreateChar：4~13 位英文数字（服务端另有敏感词过滤，本地从简）
    public static bool CanCreateChar(string name) => CharNameRules.Valid(name);
}
