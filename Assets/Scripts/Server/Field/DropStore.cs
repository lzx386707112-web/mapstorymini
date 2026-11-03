using System;
using System.Collections.Generic;
using System.Linq;

// 怪物掉落表（drop_data / drop_data_global）的运行时读取与缓存。
//
// 数据分两层（对齐源端 MonsterInformationProvider.getDrops，但避免把 2.7 万行
// 灌进 JSON 快照库——那会让每次存档都重写一个几 MB 的 db 文件）：
//  1) 基线：FieldDropTable.txt（tools/export_field_drops.py 从源端迁移全量重放导出的只读表）；
//  2) 覆盖：drop_data 表里后台/游戏内改过的行，按 (怪物, 道具) 覆盖基线，
//     新道具追加，chance<=0 视为删除。改完要清缓存（源端 clearDrops()）。
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

    // JDBC NPC queries used to rely on the first-run seed. The baseline table is now
    // shipped read-only (FieldDropTable.txt), so there is nothing to seed: DB rows are
    // pure overrides. Keep the entry point so script/database callers don't change.
    public static void EnsureDatabase(DbStore db) { }

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

            // 1) 基线：源端 drop_data 全量导出（只读）
            var baseTable = JsonDrops != null ? JsonDrops() : null;
            if (baseTable != null)
                foreach (var kv in baseTable) drops[kv.Key] = new List<DropEntry>(kv.Value);
            if (JsonGlobalDrops != null) global = new List<DropEntry>(JsonGlobalDrops());

            // 怪物结晶（etc 4260xxx）禁止直接掉落：只能由原石经制作合成获得。
            ExcludeMonsterCrystals(drops, global);

            if (db == null) return drops;

            // 2) 覆盖：DB 里改过的行，按 (怪, 道具) 覆盖基线，新道具追加，chance<=0 = 删除
            if (db.HasTable("drop_data"))
            {
                var patch = new Dictionary<int, List<DropEntry>>();
                foreach (var row in db.Table("drop_data").rows)
                {
                    int dropper = (int)DbStore.Long(row, "dropperid");
                    if (!patch.TryGetValue(dropper, out var list)) patch[dropper] = list = new List<DropEntry>();
                    list.Add(FromRow(row, false));
                }
                foreach (var kv in patch)
                {
                    if (!drops.TryGetValue(kv.Key, out var rows))
                    {
                        rows = new List<DropEntry>();
                        drops[kv.Key] = rows;
                    }
                    foreach (var row in kv.Value)
                    {
                        int at = rows.FindIndex(e => e.item == row.item);
                        if (row.chance <= 0) { if (at >= 0) rows.RemoveAt(at); continue; }
                        if (at >= 0) rows[at] = row; else rows.Add(row);
                    }
                }
            }
            if (db.HasTable("drop_data_global") && db.Table("drop_data_global").rows.Count > 0)
            {
                global = new List<DropEntry>();
                foreach (var row in db.Table("drop_data_global").rows)
                    global.Add(FromRow(row, true));
            }

            // DB 覆盖层也可能误加怪物结晶掉落，按同一规则剔除。
            ExcludeMonsterCrystals(drops, global);
            return drops;
        }

        /// <summary>怪物结晶（etc 4260xxx 命名空间）禁止直接掉落：只可由原石经制作合成获得。</summary>
        static bool IsMonsterCrystal(int item) => item >= 4260000 && item < 4270000;

        static void ExcludeMonsterCrystals(Dictionary<int, List<DropEntry>> mobs, List<DropEntry> globals)
        {
            foreach (var kv in mobs)
                kv.Value.RemoveAll(e => IsMonsterCrystal(e.item));
            globals.RemoveAll(e => IsMonsterCrystal(e.item));
            foreach (var key in mobs.Keys.Where(k => mobs[k].Count == 0).ToList())
                mobs.Remove(key);
        }
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
}
