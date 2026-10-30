// 角色读档 / 存档服务。字段集合与源端 PLAYER_LOGGEDIN 下发的数据对齐：
//   角色记录        → characters 表全字段
//   技能            → skills 表（skillid/skilllevel/masterlevel）
//   背包            → inventoryitems + inventoryequipment
//   键位            → keymap
// 存档 = 源端 saveCharToDB 的字段集（CharactersDao.Save）+ 从表覆盖写。
using System.Collections.Generic;

public sealed class SkillEntry
{
    public int skillId, skillLevel, masterLevel;
    public long expiration = -1;
}

public sealed class InventoryEntry
{
    public long itemRowId;
    public long expiration = -1;
    public int itemId, inventoryType, position, quantity;
    public int flag;
    public string owner = "", giftFrom = "";
    public int petId = -1;
    public Dictionary<string, object> petData;
    // 保留原背包行的扩展列，交易转移不丢失强化、潜能等未显示字段。
    public Dictionary<string, object> itemData, equipmentData;
    public InventoryEntry Copy()
    {
        var copy = (InventoryEntry)MemberwiseClone();
        if (itemData != null) copy.itemData = new Dictionary<string, object>(itemData);
        if (equipmentData != null) copy.equipmentData = new Dictionary<string, object>(equipmentData);
        if (petData != null) copy.petData = new Dictionary<string, object>(petData);
        return copy;
    }
    public bool isEquip;
    // 装备属性（inventoryequipment 行；非装备为 0）
    public int str, dex, int_, luk, hp, mp, watk, matk, wdef, mdef, acc, avoid, hands, speed, jump, upgradeslots;
    // 星之力（源端 Equip.java 的 maxStar/starLevel/starCount，DB 列同名）：
    //   starLevel 当前星级、starCount 连续失败次数（满 5 必成）、maxStar 上限（按需求等级表算）。
    public int maxStar, starLevel, starCount;
}

public sealed class KeymapEntry
{
    public int key, type, action;
}

// 任务状态（queststatus 表）：0 未接 / 1 进行中 / 2 完成
public sealed class QuestEntry
{
    public int quest;
    public int status;
    public int completed;
    public long time;
    public long expires;
}

public sealed class FullCharacter
{
    public CharRecord record;
    // Session-local revision prevents an older client snapshot from undoing trade escrow.
    public long tradeRevision;
    public List<SkillEntry> skills = new List<SkillEntry>();
    public List<InventoryEntry> inventory = new List<InventoryEntry>();
    public List<KeymapEntry> keymap = new List<KeymapEntry>();
    public List<QuestEntry> quests = new List<QuestEntry>();
    public long quickslotKeymap;
}

public static class CharService
{
    public static FullCharacter Load(DbStore db, int characterId)
    {
        var rec = CharactersDao.FindById(db, characterId);
        if (rec == null) return null;
        if(InventoryDao.ExpireItems(db,characterId,System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) db.Save();
        var full = new FullCharacter { record = rec };

        foreach (var r in SkillsDao.ByCharacter(db, characterId))
            full.skills.Add(new SkillEntry
            {
                skillId = (int)Convert(r, "skillid"),
                skillLevel = (int)Convert(r, "skilllevel"),
                masterLevel = (int)Convert(r, "masterlevel"),
                expiration = Convert(r, "expiration"),
            });

        foreach (var r in InventoryDao.ItemsByCharacter(db, characterId))
            full.inventory.Add(ToInventoryEntry(db, r));

        foreach (var r in KeymapDao.ByCharacter(db, characterId))
            full.keymap.Add(new KeymapEntry { key = (int)Convert(r, "key"), type = (int)Convert(r, "type"), action = (int)Convert(r, "action") });

        foreach (var r in QuestStatusDao.ByCharacter(db, characterId))
            full.quests.Add(new QuestEntry
            {
                quest = (int)Convert(r, "quest"),
                status = (int)Convert(r, "status"),
                completed = (int)Convert(r, "completed"),
                time = Convert(r, "time"),
                expires = Convert(r, "expires"),
            });

        full.quickslotKeymap = QuickslotDao.Load(db, rec.accountid);
        return full;
    }

    // inventoryitems 行 → 内存背包条目（含装备扩展列）。
    // 抽出来给后台实时同步用：后台改了某个道具，内存里那一行要按同样的规则重建，
    // 否则退出游戏时 SaveToDb 会用旧值把后台改动覆盖掉。
    public static InventoryEntry ToInventoryEntry(DbStore db, DbRow r)
    {
        var e = new InventoryEntry
        {
            itemRowId = Convert(r, "inventoryitemid"),
            expiration = Convert(r, "expiration"),
            itemId = (int)Convert(r, "itemid"),
            inventoryType = (int)Convert(r, "inventorytype"),
            position = (int)Convert(r, "position"),
            quantity = (int)Convert(r, "quantity"),
            flag = (int)Convert(r, "flag"),
            owner = DbStore.Str(r, "owner"), giftFrom = DbStore.Str(r, "giftFrom"),
            petId = DbStore.Int(r, "petid", -1), itemData = new Dictionary<string, object>(r),
        };
        if(e.petId>=0 && db.HasTable("pets"))
        {
            var pet=db.Table("pets").Find(x=>DbStore.Int(x,"petid")==e.petId);
            if(pet!=null) e.petData=new Dictionary<string,object>(pet);
        }
        var eq = InventoryDao.EquipmentOf(db, e.itemRowId);
        if (eq != null)
        {
            e.isEquip = true;
            e.equipmentData = new Dictionary<string, object>(eq);
            e.str = (int)Convert(eq, "str"); e.dex = (int)Convert(eq, "dex");
            e.int_ = (int)Convert(eq, "int"); e.luk = (int)Convert(eq, "luk");
            e.hp = (int)Convert(eq, "hp"); e.mp = (int)Convert(eq, "mp");
            e.watk = (int)Convert(eq, "watk"); e.matk = (int)Convert(eq, "matk");
            e.wdef = (int)Convert(eq, "wdef"); e.mdef = (int)Convert(eq, "mdef");
            e.acc = (int)Convert(eq, "acc"); e.avoid = (int)Convert(eq, "avoid");
            e.hands = (int)Convert(eq, "hands"); e.speed = (int)Convert(eq, "speed");
            e.jump = (int)Convert(eq, "jump"); e.upgradeslots = (int)Convert(eq, "upgradeslots");
            e.maxStar = (int)Convert(eq, "maxStar");
            e.starLevel = (int)Convert(eq, "starLevel");
            e.starCount = (int)Convert(eq, "starCount");
        }
        return e;
    }

    public static void Save(DbStore db, FullCharacter full)
    {
        if (full == null || full.record == null) return;
        CharactersDao.Save(db, full.record);

        int cid = (int)full.record.id;
        KeymapDao.Save(db, cid, full.keymap);
        foreach (var s in full.skills)
            SkillsDao.Upsert(db, cid, s.skillId, s.skillLevel, s.masterLevel, s.expiration);
        foreach (var q in full.quests)
            QuestStatusDao.Upsert(db, cid, q.quest, q.status, q.completed);
    }

    // 背包增删（掉宝/消耗/穿脱都走这里）
    public static long AddItem(DbStore db, int characterId, int itemId, int inventoryType, int position, int quantity, bool equipStats = false)
    {
        long id = InventoryDao.InsertItem(db, characterId, itemId, inventoryType, position, quantity);
        if (equipStats) InventoryDao.InsertEquipment(db, id);
        return id;
    }

    public static void RemoveItem(DbStore db, long inventoryItemId)
    {
        db.Table("inventoryitems").Delete(DbStore.Eq("inventoryitemid", inventoryItemId));
        db.Table("inventoryequipment").Delete(DbStore.Eq("inventoryitemid", inventoryItemId));
    }

    public static int CountItem(DbStore db, int characterId, int itemId)
    {
        int sum = 0;
        foreach (var r in InventoryDao.ItemsByCharacter(db, characterId))
            if (Convert(r, "itemid") == itemId) sum += (int)Convert(r, "quantity");
        return sum;
    }

    static long Convert(DbRow r, string k)
    {
        if (r == null || !r.TryGetValue(k, out var v) || v == null) return 0;
        try { return System.Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0; }
    }
}
