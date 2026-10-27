using System;
using System.Collections.Generic;

// 怪物掉落表（drop_data / drop_data_global）的运行时读取与缓存。
//
// 对齐源端 MonsterInformationProvider.getDrops：DB 是唯一数据源，改完要清缓存
// （源端 dropService.modifyDropData 之后 clearDrops()）。本项目的差异只有一处：
// FieldDrops.json（源端 drop_data SQL 的导出快照）当「首启种子」导入 DB——
// 1) 有库且表为空 → 把 JSON 快照灌进表（一次性），之后以表为准；
// 2) 有库且表非空 → 直接读表（后台/游戏内改掉落都落在表里）；
// 3) 无库（编辑器自检、离线抽样）→ 回退 JSON 快照，行为与改造前一致。
//
// 约束：纯 C#，不引用 UnityEngine（FieldLoot/后台/admin-check 共用）。
public sealed class DropEntry
{
    public long id;                 // drop_data.id（JSON 兜底时为 0，仅供后台展示）
    public int item;                // 0 = 金币
    public int min = 1, max = 1;
    public int quest;
    public int chance;
    public long continent = -1;     // 仅 drop_data_global
    public string comments;         // 仅 drop_data_global / 后台备注
}

public static class DropStore
{
    /// <summary>与源端 Randomizer 上限一致（FieldCatalog.ChanceDenominator）。</summary>
    public const int ChanceDenominator = 999999;

    /// <summary>注入数据库（GameServer.Local?.Db / AdminBackend.DbOverride）。</summary>
    public static Func<DbStore> DbProvider;

    /// <summary>FieldCatalog 注入的 JSON 快照（首启种子 + 无库兜底）。</summary>
    public static Func<Dictionary<int, List<DropEntry>>> JsonDrops;
    public static Func<List<DropEntry>> JsonGlobalDrops;

    static readonly object gate = new object();
    static Dictionary<int, List<DropEntry>> drops = new Dictionary<int, List<DropEntry>>();
    static List<DropEntry> global = new List<DropEntry>();
    static bool loaded;
    static DbStore loadedDb;

    /// <summary>改表后调用：下次取用重新加载（对应源端 clearDrops）。</summary>
    public static void Invalidate()
    {
        lock (gate) { loaded = false; loadedDb = null; }
    }

    public static List<DropEntry> ForMob(int mobId)
    {
        var all = Snapshot();
        return all.TryGetValue(mobId, out var rows) ? rows : new List<DropEntry>();
    }

    public static List<DropEntry> Global()
    {
        Snapshot();
        lock (gate) return new List<DropEntry>(global);
    }

    public static Dictionary<int, List<DropEntry>> All()
    {
        var snap = Snapshot();
        var copy = new Dictionary<int, List<DropEntry>>();
        foreach (var kv in snap) copy[kv.Key] = new List<DropEntry>(kv.Value);
        return copy;
    }

    static Dictionary<int, List<DropEntry>> Snapshot()
    {
        var db = DbProvider != null ? DbProvider() : null;
        lock (gate)
        {
            if (loaded && ReferenceEquals(db, loadedDb)) return drops;
            loaded = true;
            loadedDb = db;

            drops = new Dictionary<int, List<DropEntry>>();
            global = new List<DropEntry>();

            if (db != null && db.HasTable("drop_data"))
            {
                SeedIfEmpty(db);
                foreach (var row in db.Table("drop_data").rows)
                {
                    int dropper = (int)DbStore.Long(row, "dropperid");
                    if (!drops.TryGetValue(dropper, out var list)) drops[dropper] = list = new List<DropEntry>();
                    list.Add(FromRow(row, false));
                }
                if (db.HasTable("drop_data_global"))
                    foreach (var row in db.Table("drop_data_global").rows)
                        global.Add(FromRow(row, true));
                return drops;
            }

            // 无库兜底：直接用 JSON 快照
            var json = JsonDrops != null ? JsonDrops() : null;
            if (json != null) foreach (var kv in json) drops[kv.Key] = new List<DropEntry>(kv.Value);
            if (JsonGlobalDrops != null) global = new List<DropEntry>(JsonGlobalDrops());
            return drops;
        }
    }

    static void SeedIfEmpty(DbStore db)
    {
        var table = db.Table("drop_data");
        if (table.rows.Count > 0) return;
        var json = JsonDrops != null ? JsonDrops() : null;
        if (json == null || json.Count == 0) return;

        foreach (var kv in json)
            foreach (var e in kv.Value)
                table.Insert(ToRow(kv.Key, e, false));
        if (db.HasTable("drop_data_global") && db.Table("drop_data_global").rows.Count == 0 && JsonGlobalDrops != null)
            foreach (var e in JsonGlobalDrops())
                db.Table("drop_data_global").Insert(ToRow(0, e, true));
        db.Save();
    }

    static DropEntry FromRow(DbRow row, bool isGlobal)
    {
        return new DropEntry
        {
            id = DbStore.Long(row, "id"),
            item = (int)DbStore.Long(row, "itemid"),
            min = (int)DbStore.Long(row, "minimum_quantity", 1),
            max = (int)DbStore.Long(row, "maximum_quantity", 1),
            quest = (int)DbStore.Long(row, "questid"),
            chance = (int)DbStore.Long(row, "chance"),
            continent = isGlobal ? DbStore.Long(row, "continent", -1) : -1,
            comments = DbStore.Str(row, "comments"),
        };
    }

    static DbRow ToRow(int dropper, DropEntry e, bool isGlobal)
    {
        var row = new DbRow
        {
            ["itemid"] = (long)e.item,
            ["minimum_quantity"] = (long)e.min,
            ["maximum_quantity"] = (long)e.max,
            ["questid"] = (long)e.quest,
            ["chance"] = (long)e.chance,
        };
        if (isGlobal)
        {
            row["continent"] = e.continent;
            if (!string.IsNullOrEmpty(e.comments)) row["comments"] = e.comments;
        }
        else
        {
            row["dropperid"] = (long)dropper;
            if (!string.IsNullOrEmpty(e.comments)) row["comments"] = e.comments;
        }
        return row;
    }
}
