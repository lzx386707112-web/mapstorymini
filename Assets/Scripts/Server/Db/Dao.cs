// 账号 / 角色 DAO。所有 SQL 语义与源端一致：
//   - accounts：按 name 唯一查询；登录即注册（automatic_register）。
//   - characters：建号事务写 characters + keymap + skills + inventoryitems(+equipment) + quickslotkeymapped，
//     与源端 Character.insertNewChar（Character.java:8136-8296）同一个事务边界。
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

public static class AccountsDao
{
    // 本地密码格式：pbkdf2$<迭代>$<saltB64>$<hashB64>。
    // 源端是 BCrypt($2a$12$…)，列结构相同（VARCHAR(128)）；接真库时替换本算法即可。
    const int Pbkdf2Iterations = 120000;
    const int SaltBytes = 16;
    const int HashBytes = 32;

    public static string HashPassword(string pwd)
    {
        var salt = new byte[SaltBytes];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
        var hash = Pbkdf2(pwd, salt);
        return "pbkdf2$" + Pbkdf2Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
    }

    public static bool VerifyPassword(string pwd, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var it)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expect = Convert.FromBase64String(parts[3]);
        var actual = Pbkdf2(pwd, salt, it, expect.Length);
        return FixedTimeEquals(actual, expect);
    }

    static byte[] Pbkdf2(string pwd, byte[] salt, int it = Pbkdf2Iterations, int len = HashBytes)
    {
        using var derive = new Rfc2898DeriveBytes(pwd, salt, it, HashAlgorithmName.SHA256);
        return derive.GetBytes(len);
    }

    static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    public static DbRow FindByName(DbStore db, string name)
        => db.Table("accounts").Find(DbStore.Eq("name", name));

    public static DbRow FindById(DbStore db, int id)
        => db.Table("accounts").Find(DbStore.Eq("id", (long)id));

    // 自动注册（源端 LoginPasswordHandler:75-114）。返回新账号 id。
    public static int AutoRegister(DbStore db, string name, string pwd)
    {
        var t = db.Table("accounts");
        var row = new DbRow
        {
            ["name"] = name,
            ["password"] = HashPassword(pwd),
            ["createdat"] = DbStore.NowText(),
        };
        return (int)t.Insert(row);
    }

    public static int Count(DbStore db) => db.Table("accounts").rows.Count;

    public static void UpdateLoginState(DbStore db, int accountId, bool loggedIn)
    {
        db.Table("accounts").Update(DbStore.Eq("id", (long)accountId), new DbRow
        {
            ["loggedin"] = loggedIn ? 1L : 0L,
            ["lastlogin"] = DbStore.NowText(),
        });
    }

    public static int CharacterSlots(DbStore db, int accountId)
    {
        var a = FindById(db, accountId);
        return a == null ? 3 : (int)Convert.ToInt64(a["characterslots"]);
    }

    // 账号性别：源端 accounts.gender TINYINT(2) NOT NULL DEFAULT '10'
    // （gms-server .../V1.0.0__create_accounts.sql:19）。10 = 还没选，客户端看到就弹性别框，
    // 选完由 SET_GENDER 包写回 0/1。自动注册的 INSERT 不写这列，所以新号天然是 10。
    public const int GenderUnset = 10;

    public static int Gender(DbStore db, int accountId)
        => DbStore.Int(FindById(db, accountId), "gender", GenderUnset);

    public static void SetGender(DbStore db, int accountId, int gender)
    {
        // 只接受 0/1：10 是「未选」哨兵值，写回去等于没选（源端 SetGenderHandler 也只读一个字节）。
        if (gender != 0 && gender != 1) return;
        db.Table("accounts").Update(DbStore.Eq("id", (long)accountId),
            new DbRow { ["gender"] = (long)gender });
    }
}

public static class KeymapDao
{
    // 源端建号默认键位（GameConstants.java:274-276 DEFAULT_KEY/TYPE/ACTION，40 项）
    public static readonly int[] DefaultKeys = { 18, 65, 2, 23, 3, 4, 5, 6, 16, 17, 19, 25, 26, 27, 31, 34, 35, 37, 38, 40, 43, 44, 45, 46, 50, 56, 59, 60, 61, 62, 63, 64, 57, 48, 29, 7, 24, 33, 41, 39 };
    public static readonly int[] DefaultTypes = { 4, 6, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 4, 4, 5, 6, 6, 6, 6, 6, 6, 5, 4, 5, 4, 4, 4, 4, 4 };
    public static readonly int[] DefaultActions = { 0, 106, 10, 1, 12, 13, 18, 24, 8, 5, 4, 19, 14, 15, 2, 17, 11, 3, 20, 16, 9, 50, 51, 6, 7, 53, 100, 101, 102, 103, 104, 105, 54, 22, 52, 21, 25, 26, 23, 27 };

    public static List<DbRow> ByCharacter(DbStore db, int characterId)
    {
        var rows = db.Table("keymap").Select(DbStore.Eq("characterid", (long)characterId));
        // 旧 Schema 解析器曾忽略 key 列。仅恢复这种无法表示有效键位的整表，
        // 保留正常自定义表以及用户主动清空的空表。
        if (rows.Count > 0 && rows.TrueForAll(r => !r.ContainsKey("key") || Convert.ToInt64(r["key"]) == 0))
        {
            db.Table("keymap").Delete(DbStore.Eq("characterid", (long)characterId));
            SeedDefaults(db, characterId);
            rows = db.Table("keymap").Select(DbStore.Eq("characterid", (long)characterId));
        }
        return rows;
    }

    public static void Save(DbStore db, int characterId, List<KeymapEntry> entries)
    {
        var table = db.Table("keymap");
        table.Delete(DbStore.Eq("characterid", (long)characterId));
        foreach (var entry in entries)
            table.Insert(new DbRow { ["characterid"] = (long)characterId, ["key"] = (long)entry.key,
                ["type"] = (long)entry.type, ["action"] = (long)entry.action });
    }

    public static void SeedDefaults(DbStore db, int characterId)
    {
        var t = db.Table("keymap");
        int n = Math.Min(Math.Min(DefaultKeys.Length, DefaultTypes.Length), DefaultActions.Length);
        for (int i = 0; i < n; i++)
            t.Insert(new DbRow { ["characterid"] = (long)characterId, ["key"] = (long)DefaultKeys[i], ["type"] = (long)DefaultTypes[i], ["action"] = (long)DefaultActions[i] });
    }
}

public static class QuickslotDao
{
    public static void Save(DbStore db, int accountId, long keymap)
    {
        var t = db.Table("quickslotkeymapped");
        t.Update(DbStore.Eq("accountid", (long)accountId), new DbRow { ["keymap"] = keymap });
        if (t.Find(DbStore.Eq("accountid", (long)accountId)) == null)
            t.Insert(new DbRow { ["accountid"] = (long)accountId, ["keymap"] = keymap });
    }

    public static long Load(DbStore db, int accountId)
    {
        var r = db.Table("quickslotkeymapped").Find(DbStore.Eq("accountid", (long)accountId));
        return r == null ? 0L : Convert.ToInt64(r["keymap"]);
    }
}

public static class SkillsDao
{
    public static List<DbRow> ByCharacter(DbStore db, int characterId)
        => db.Table("skills").Select(DbStore.Eq("characterid", (long)characterId));

    public static void Upsert(DbStore db, int characterId, int skillId, int skillLevel, int masterLevel, long expiration = -1)
    {
        var t = db.Table("skills");
        int n = t.Update(r => Eq64(r, "characterid", characterId) && Eq64(r, "skillid", skillId), new DbRow
        {
            ["skilllevel"] = (long)skillLevel,
            ["masterlevel"] = (long)masterLevel,
            ["expiration"] = expiration,
        });
        if (n == 0)
            t.Insert(new DbRow { ["skillid"] = (long)skillId, ["characterid"] = (long)characterId, ["skilllevel"] = (long)skillLevel, ["masterlevel"] = (long)masterLevel, ["expiration"] = expiration });
    }

    internal static bool Eq64(DbRow r, string col, int v)
        => r.TryGetValue(col, out var o) && o != null && Convert.ToInt64(o) == v;
}

public static class InventoryDao
{
    public const int Equipped = 1;
    public const int Equip = 1;
    public const int Use = 2;
    public const int Setup = 3;
    public const int Etc = 4;
    public const int Cash = 5;

    public static long InsertItem(DbStore db, int characterId, int itemId, int inventoryType, int position, int quantity)
    {
        return db.Table("inventoryitems").Insert(new DbRow
        {
            ["type"] = (long)(itemId / 1000000),
            ["characterid"] = (long)characterId,
            ["itemid"] = (long)itemId,
            ["inventorytype"] = (long)inventoryType,
            ["position"] = (long)position,
            ["quantity"] = (long)quantity,
            ["owner"] = "",
            ["petid"] = -1L,
            ["flag"] = 0L,
            ["expiration"] = -1L,
            ["giftFrom"] = "",
        });
    }

    public static void InsertEquipment(DbStore db, long inventoryItemId, int str = 0, int dex = 0, int intl = 0, int luk = 0,
        int hp = 0, int mp = 0, int watk = 0, int matk = 0, int wdef = 0, int mdef = 0, int acc = 0, int avoid = 0,
        int hands = 0, int speed = 0, int jump = 0, int upgradeslots = 0)
    {
        db.Table("inventoryequipment").Insert(new DbRow
        {
            ["inventoryitemid"] = inventoryItemId,
            ["upgradeslots"] = (long)upgradeslots,
            ["str"] = (long)str, ["dex"] = (long)dex, ["int"] = (long)intl, ["luk"] = (long)luk,
            ["hp"] = (long)hp, ["mp"] = (long)mp, ["watk"] = (long)watk, ["matk"] = (long)matk,
            ["wdef"] = (long)wdef, ["mdef"] = (long)mdef, ["acc"] = (long)acc, ["avoid"] = (long)avoid,
            ["hands"] = (long)hands, ["speed"] = (long)speed, ["jump"] = (long)jump,
        });
    }

    public static List<DbRow> ItemsByCharacter(DbStore db, int characterId)
        => db.Table("inventoryitems").Select(DbStore.Eq("characterid", (long)characterId));

    public static DbRow EquipmentOf(DbStore db, long inventoryItemId)
        => db.Table("inventoryequipment").Find(DbStore.Eq("inventoryitemid", inventoryItemId));

    /// <summary>背包里某个 itemid 的总数量（死亡护符判定 / 任务道具校验用）。</summary>
    public static int CountOf(DbStore db, int characterId, int itemId)
    {
        int sum = 0;
        foreach (var r in ItemsByCharacter(db, characterId))
        {
            if (DbStore.Long(r, "itemid", 0) == itemId && DbStore.Long(r, "position", 0) > 0)
                sum += (int)DbStore.Long(r, "quantity", 0);
        }
        return sum;
    }

    /// <summary>
    /// 消耗 n 个道具（源端 gainItem(itemid, -n)）。返回实际消耗数量。
    /// 护符 / 任务交道具都走这里，避免各处自己改 quantity 导致负数。
    /// </summary>
    public static int ConsumeItem(DbStore db, int characterId, int itemId, int count)
    {
        if (count <= 0) return 0;
        var table = db.Table("inventoryitems");
        int left = count;
        foreach (var r in ItemsByCharacter(db, characterId))
        {
            if (left <= 0) break;
            if (DbStore.Long(r, "itemid", 0) != itemId) continue;
            if (DbStore.Long(r, "position", 0) < 0) continue;
            int q = (int)DbStore.Long(r, "quantity", 0);
            int take = Math.Min(q, left);
            long id = DbStore.Long(r, "inventoryitemid", 0);
            if (q - take <= 0)
            {
                table.Delete(x => DbStore.Long(x, "inventoryitemid", -1) == id);
                db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == id);
            }
            else r["quantity"] = (long)(q - take);
            left -= take;
        }
        return count - left;
    }
}

public static class QuestStatusDao
{
    public static List<DbRow> ByCharacter(DbStore db, int characterId)
        => db.Table("queststatus").Select(DbStore.Eq("characterid", (long)characterId));

    public static void Upsert(DbStore db, int characterId, int questId, int status, int completed = 0)
    {
        var t = db.Table("queststatus");
        int n = t.Update(r => SkillsDao.Eq64(r, "characterid", characterId) && SkillsDao.Eq64(r, "quest", questId), new DbRow
        {
            ["status"] = (long)status,
            ["completed"] = (long)completed,
        });
        if (n == 0)
            t.Insert(new DbRow { ["characterid"] = (long)characterId, ["quest"] = (long)questId, ["status"] = (long)status, ["completed"] = (long)completed });
    }

    public static int StatusOf(DbStore db, int characterId, int questId)
    {
        var r = db.Table("queststatus").Find(x => SkillsDao.Eq64(x, "characterid", characterId) && SkillsDao.Eq64(x, "quest", questId));
        return r == null ? 0 : (int)Convert.ToInt64(r["status"]);
    }
}

public static class SavedLocationsDao
{
    public static void Save(DbStore db, int characterId, string locationType, int map, int portal)
    {
        var t = db.Table("savedlocations");
        int n = t.Update(r => SkillsDao.Eq64(r, "characterid", characterId) && (string)r["locationtype"] == locationType, new DbRow
        {
            ["map"] = (long)map,
            ["portal"] = (long)portal,
        });
        if (n == 0)
            t.Insert(new DbRow { ["characterid"] = (long)characterId, ["locationtype"] = locationType, ["map"] = (long)map, ["portal"] = (long)portal });
    }

    public static DbRow Find(DbStore db, int characterId, string locationType)
        => db.Table("savedlocations").Find(r => SkillsDao.Eq64(r, "characterid", characterId) && (string)r["locationtype"] == locationType);
}

public static class GameConfigDao
{
    public static string Get(DbStore db, string code, string fallback = "")
    {
        var r = db.Table("game_config").Find(x => (string)x["config_code"] == code);
        return r == null ? fallback : (string)r["config_value"];
    }

    public static bool GetBool(DbStore db, string code, bool fallback = false)
    {
        var r = db.Table("game_config").Find(x => (string)x["config_code"] == code);
        if (r == null) return fallback;
        var v = ((string)r["config_value"]).Trim();
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public static int GetInt(DbStore db, string code, int fallback = 0)
    {
        var r = db.Table("game_config").Find(x => (string)x["config_code"] == code);
        return r == null ? fallback : int.TryParse((string)r["config_value"], out var v) ? v : fallback;
    }

    public static double GetFloat(DbStore db, string code, double fallback = 0)
    {
        var r = db.Table("game_config").Find(x => (string)x["config_code"] == code);
        return r == null ? fallback : double.TryParse((string)r["config_value"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
