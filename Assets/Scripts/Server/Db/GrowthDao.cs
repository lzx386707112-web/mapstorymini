// 282 角色成长存档：极限属性 / 内在能力 / 灵魂能量。
//
// 源端把这几块的「当前状态」挂在不同地方：
//   - 极限属性当前预设   → 任务 QR 2498（QuestRecord.hyperstats）
//   - 内在能力当前预设   → QR CHARACTER_POTENTIAL_PRESET（potential）
//   - 荣誉值（洗练货币） → Char.honorExp
//   - 极限属性加点       → hyperstats 表（charid, index, skillid, slv），index 就是预设 0..2
//   - 内在能力三行       → characterpotentials 表（charid, preset, potkey, skillid, slv, grade）
// 本项目没有任务 QR 通道，预设号与荣誉值统一收进 character_growth 一张角色级表。
//
// 约束（见 DbStore 注释与项目铁律）：DbStore 只持久化 Schema.sql 声明过的列，
// 新增字段必须先写进 Schema，否则落盘即丢。本文件对应的三张表已在 Schema.sql 中声明。
using System.Collections.Generic;

// character_growth 行的内存镜像。缺失行时返回默认值（不主动建行，首次写才 Insert）。
public sealed class GrowthState
{
    public long honorExp;
    public int hyperPreset, abilityPreset;
    // 锁定的内在能力行号，逗号分隔（如 "0,2"）；空串 = 没锁。
    public string abilityLock = "";
    // 循环器摇出的待确认三行（json）；空串 = 没有待确认，源端两段式选择窗的 journal。
    public string abilityChoice = "";
    public int soulMp, soulMax;
    // 魔幻箭筒VI 当前箭镞（0 未开启 / 1 魔法箭 / 2 吸血箭）。
    // 箭筒本身是客户端结算，但模式属于角色状态，跟着 GrowthState 一起下发与落盘。
    public int quiverMode;

    public HashSet<int> LockedRows()
    {
        var set = new HashSet<int>();
        if (string.IsNullOrEmpty(abilityLock)) return set;
        foreach (var part in abilityLock.Split(','))
            if (int.TryParse(part.Trim(), out int row) && row >= 0 && row < 3) set.Add(row);
        return set;
    }

    public void SetLockedRows(IEnumerable<int> rows)
    {
        var list = new List<int>();
        foreach (int row in rows) if (row >= 0 && row < 3 && !list.Contains(row)) list.Add(row);
        list.Sort();
        abilityLock = string.Join(",", list);
    }
}

// hyperstats 行（源端列名为 index，SQL 保留字，本地列名用 preset）。
public sealed class HyperStatEntry
{
    public int preset, skillId, slv;
}

// characterpotentials 行：potkey 是行号 0/1/2，grade 是稀有度 0..3。
public sealed class PotentialEntry
{
    public int preset, potKey, skillId, slv, grade;
}

public static class GrowthDao
{
    public static GrowthState Load(DbStore db, int characterId)
    {
        var state = new GrowthState();
        var row = db.Table("character_growth").Find(DbStore.Eq("characterid", (long)characterId));
        // 282 的灵魂能量上限是固定 1000（Char.initSoulMP），没有「上限成长」这回事。
        // 表里 0 只可能是「还没初始化过」，一律按 1000 报，否则窗口会画出空能量条。
        if (row == null) { state.soulMax = SoulCatalog.MaxSoulMp; return state; }
        state.honorExp = DbStore.Long(row, "honorExp");
        state.hyperPreset = Clamp(DbStore.Int(row, "hyperPreset"));
        state.abilityPreset = Clamp(DbStore.Int(row, "abilityPreset"));
        state.abilityLock = DbStore.Str(row, "abilityLock") ?? "";
        state.abilityChoice = DbStore.Str(row, "abilityChoice") ?? "";
        state.soulMp = DbStore.Int(row, "soulMp");
        state.soulMax = DbStore.Int(row, "soulMax");
        if (state.soulMax <= 0) state.soulMax = SoulCatalog.MaxSoulMp;
        // 越界值（旧档/脏数据）收敛到 0，宁可当作「没开箭筒」，不要让整块成长读不回来。
        int quiver = DbStore.Int(row, "quiverMode");
        state.quiverMode = quiver < 0 || quiver > 2 ? 0 : quiver;
        return state;
    }

    public static void Save(DbStore db, int characterId, GrowthState state)
    {
        if (state == null) return;
        var values = new DbRow
        {
            ["honorExp"] = state.honorExp,
            ["hyperPreset"] = (long)state.hyperPreset,
            ["abilityPreset"] = (long)state.abilityPreset,
            ["abilityLock"] = state.abilityLock ?? "",
            ["abilityChoice"] = state.abilityChoice ?? "",
            ["soulMp"] = (long)state.soulMp,
            ["soulMax"] = (long)state.soulMax,
            ["quiverMode"] = (long)state.quiverMode,
        };
        var table = db.Table("character_growth");
        // characterid 是主键，直接按主键 Update，命中 0 行才 Insert。
        if (table.Update(DbStore.Eq("characterid", (long)characterId), values) == 0)
        {
            values["characterid"] = (long)characterId;
            table.Insert(values);
        }
    }

    public static List<HyperStatEntry> HyperStats(DbStore db, int characterId)
    {
        var list = new List<HyperStatEntry>();
        foreach (var r in db.Table("hyperstats").Select(DbStore.Eq("characterid", (long)characterId)))
            list.Add(new HyperStatEntry
            {
                preset = Clamp(DbStore.Int(r, "preset")),
                skillId = DbStore.Int(r, "skillid"),
                slv = DbStore.Int(r, "slv"),
            });
        // 固定顺序，避免 JSON 里行顺序变化导致客户端页签刷新时抖动。
        list.Sort((a, b) => a.preset != b.preset ? a.preset.CompareTo(b.preset) : a.skillId.CompareTo(b.skillId));
        return list;
    }

    public static void SaveHyperStats(DbStore db, int characterId, int preset, IEnumerable<HyperStatEntry> rows)
    {
        var table = db.Table("hyperstats");
        // 源端 HyperStat 存档是整预设覆盖（先删该预设再写），本地同口径。
        table.Delete(r => SkillsDao.Eq64(r, "characterid", characterId) && SkillsDao.Eq64(r, "preset", preset));
        if (rows == null) return;
        foreach (var row in rows)
        {
            if (row == null || row.skillId <= 0 || row.slv <= 0) continue;
            table.Insert(new DbRow
            {
                ["characterid"] = (long)characterId,
                ["preset"] = (long)preset,
                ["skillid"] = (long)row.skillId,
                ["slv"] = (long)row.slv,
            });
        }
    }

    public static List<PotentialEntry> Potentials(DbStore db, int characterId)
    {
        var list = new List<PotentialEntry>();
        foreach (var r in db.Table("characterpotentials").Select(DbStore.Eq("characterid", (long)characterId)))
            list.Add(new PotentialEntry
            {
                preset = Clamp(DbStore.Int(r, "preset")),
                potKey = DbStore.Int(r, "potkey"),
                skillId = DbStore.Int(r, "skillid"),
                slv = DbStore.Int(r, "slv"),
                grade = DbStore.Int(r, "grade"),
            });
        list.Sort((a, b) => a.preset != b.preset ? a.preset.CompareTo(b.preset) : a.potKey.CompareTo(b.potKey));
        return list;
    }

    public static void SavePotentials(DbStore db, int characterId, int preset, IEnumerable<PotentialEntry> rows)
    {
        var table = db.Table("characterpotentials");
        table.Delete(r => SkillsDao.Eq64(r, "characterid", characterId) && SkillsDao.Eq64(r, "preset", preset));
        if (rows == null) return;
        foreach (var row in rows)
        {
            if (row == null || row.potKey < 0 || row.potKey > 2) continue;
            table.Insert(new DbRow
            {
                ["characterid"] = (long)characterId,
                ["preset"] = (long)preset,
                ["potkey"] = (long)row.potKey,
                ["skillid"] = (long)row.skillId,
                ["slv"] = (long)row.slv,
                ["grade"] = (long)row.grade,
            });
        }
    }

    // 预设号只有 0..2 三个；存档里出现越界值时收敛而不是整行拒绝，
    // 否则一次脏数据会让整块成长系统不可用。
    static int Clamp(int preset) => preset < 0 ? 0 : preset > 2 ? 2 : preset;
}
