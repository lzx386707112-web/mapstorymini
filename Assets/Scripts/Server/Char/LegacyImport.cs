// 旧存档迁移：PlayerPrefs 的单机进度 → 数据库角色（一次性）。
//
// 放在 Server 层的原因：写库这件事只能是服务端做的事。客户端只负责把本地进度
// 打包成一个纯数据快照发过来，不碰任何表。
using System.Collections.Generic;

public sealed class LegacySkillEntry
{
    public int skillId, skillLevel;
}

public sealed class LegacyProfileSnapshot
{
    public string name = "";
    public int level = 1;
    public long exp;
    public int str, dex, int_, luk;
    public int maxHp, maxMp;
    public int job;
    public int map;
    public int ap, sp, meso, equipSlots = 24;
    public List<LegacySkillEntry> skills = new List<LegacySkillEntry>();
}

public static class LegacyImport
{
    /// <summary>
    /// 把快照写成一个 local 账号下的角色。库里已经有任何角色/账号时什么都不做
    /// （避免覆盖新数据）。返回新建角色 id，0 = 未迁移。
    /// </summary>
    public static int Import(DbStore db, LegacyProfileSnapshot s)
    {
        if (s == null) return 0;
        if (AccountsDao.Count(db) > 0) return 0;

        foreach (var t in new[] { "characters" })
            if (db.Table(t).rows.Count > 0) return 0;

        int accId = AccountsDao.AutoRegister(db, "local", "local");
        var rec = new CharRecord
        {
            accountid = accId,
            name = string.IsNullOrEmpty(s.name) ? "Migrated" : s.name,
            level = s.level < 1 ? 1 : s.level,
            exp = (int)(s.exp > int.MaxValue ? int.MaxValue : s.exp),
            str = s.str, dex = s.dex, int_ = s.int_, luk = s.luk,
            hp = 50, maxhp = s.maxHp > 0 ? s.maxHp : 50,
            mp = 5, maxmp = s.maxMp > 0 ? s.maxMp : 5,
            job = s.job,
            map = s.map,
            hair = 30000,
            face = 20000,
            ap = s.ap,
            meso = s.meso,
            equipslots = s.equipSlots > 0 ? s.equipSlots : 24,
            useslots = 24, setupslots = 24, etcslots = 24,
            fishLevel = 1,
        };
        rec.SetSp(JobBook.Of(rec.job), s.sp);

        db.BeginTransaction();
        try
        {
            int id = (int)CharactersDao.Insert(db, rec);
            KeymapDao.SeedDefaults(db, id);
            foreach (var sk in s.skills)
                if (sk.skillLevel > 0)
                    SkillsDao.Upsert(db, id, sk.skillId, sk.skillLevel, sk.skillLevel);
            db.Commit();
            db.Save();
            return id;
        }
        catch
        {
            db.Rollback();
            return 0;
        }
    }
}
