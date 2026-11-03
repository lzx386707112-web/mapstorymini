using System;
using System.Collections.Generic;
using System.IO;

// Explicit cutover operation, not a per-login cleanup. User authorized removing all old equipment.
// Do not call until the 282 gameplay/UI switch is complete.
public static class EnhancementEquipmentReset
{
    const string Key = "equipment-282-reset";
    public static int Apply(DbStore db)
    {
        lock (db)
        {
            var migrations = db.Table("native_migrations");
            if (migrations.Find(x => DbStore.Str(x,"name") == Key) != null) return 0;
            string backup = db.FilePath + ".before-equipment-282-reset.bak";
            // Refuse to overwrite a previous pre-cutover snapshot.
            if (File.Exists(backup)) throw new InvalidOperationException("装备切换备份已存在但缺少完成标记，请核实存档状态");
            db.Save();
            File.Copy(db.FilePath, backup, false);
            var ids = new HashSet<long>();
            foreach (var row in db.Table("inventoryitems").rows)
                if (DbStore.Int(row,"itemid") / 1000000 == 1) ids.Add(DbStore.Long(row,"inventoryitemid"));
            foreach (var row in db.Table("inventoryequipment").rows) ids.Add(DbStore.Long(row,"inventoryitemid"));
            int removed = 0;
            db.BeginTransaction();
            try
            {
                removed += db.Table("inventoryitems").Delete(x=>ids.Contains(DbStore.Long(x,"inventoryitemid")));
                db.Table("inventoryequipment").Delete(_=>true);
                foreach (string table in new[] {"dueyitems", "merchantitems"})
                    db.Table(table).Delete(x=>ids.Contains(DbStore.Long(x,"inventoryitemid")));
                removed += db.Table("cash_items").Delete(x=>DbStore.Int(x,"itemid")/1000000==1 || ids.Contains(DbStore.Long(x,"inventoryitemid")));
                removed += db.Table("extrastorage").Delete(x=>DbStore.Int(x,"itemid")/1000000==1);
                migrations.Insert(new DbRow {["name"]=Key,["completedAt"]=DbStore.NowText()});
                db.Save();
                db.Commit();
            }
            catch {db.Rollback();throw;}
            return removed;
        }
    }
}
