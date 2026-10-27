// 后台管理系统 ←→ 当前单机服务端的数据桥。
//
// 源端后台（gms-ui + Spring Boot）读的是 MySQL；本工程没有数据库进程，数据在
// GameServer.Db（DbStore，表结构与源端 Schema.sql 一致）里。所以这里只做三件事：
//   1. 把 DbRow 的 snake_case 列名转成前端要的 camelCase；
//   2. 把增删改查落到对应表（accounts / characters / inventoryitems / game_config …）；
//   3. 保证所有 DB 访问都在游戏主线程执行（DbStore 不是线程安全的）。
//
// 约束：纯 C#，不引用 UnityEngine（主线程调度见 AdminMainThread）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

// 主线程调度：HTTP 请求在 ThreadPool 线程上跑，DbStore 只能在 Unity 主线程碰。
// AdminHost 每帧调 Pump() 排空队列；离线单测（tools/admin-check）里 Pumping=false，
// Run() 直接就地执行。
public static class AdminMainThread
{
    static readonly object gate = new object();
    static readonly Queue<Action> queue = new Queue<Action>();

    public static volatile bool Pumping;
    public static int MainThreadId;
    public static Action<Exception> OnError;

    public static void Post(Action action)
    {
        if (action == null) return;
        lock (gate) queue.Enqueue(action);
    }

    public static int Pump()
    {
        int n = 0;
        while (true)
        {
            Action a;
            lock (gate)
            {
                if (queue.Count == 0) break;
                a = queue.Dequeue();
            }
            try { a(); }
            catch (Exception e) { OnError?.Invoke(e); }
            n++;
        }
        return n;
    }

    public static bool OnMainThread => Thread.CurrentThread.ManagedThreadId == MainThreadId;

    public static T Run<T>(Func<T> work, int timeoutMs = 8000)
    {
        if (!Pumping || OnMainThread) return work();

        T result = default;
        Exception error = null;
        using (var done = new ManualResetEventSlim(false))
        {
            Post(() =>
            {
                try { result = work(); }
                catch (Exception e) { error = e; }
                finally { done.Set(); }
            });
            if (!done.Wait(timeoutMs))
                throw new AdminApiException("游戏主线程繁忙，请求超时（切回游戏后再试）");
        }
        if (error != null) throw error;
        return result;
    }

    public static void Run(Action work, int timeoutMs = 8000)
    {
        Run<object>(() => { work(); return null; }, timeoutMs);
    }
}

public static class AdminBackend
{
    public const int HenesysMap = 100000000;
    public const string DefaultAdminName = "admin";
    public const string DefaultAdminPassword = "admin";

    // 当前登录角色的 id（由 AdminHost 注入；单机版只有一个角色在线）。
    public static Func<long> CurrentCharacterId;
    // 游戏内广播（聊天框 / 顶部提示），由 AdminHost 注入。
    public static Action<string> Broadcast;

    // 离线自检（tools/admin-check）注入的库；运行期恒为 null，走 GameServer.Local。
    public static DbStore DbOverride;

    public static bool Ready => Db != null;

    // 运行期取库：离线自检（tools/admin-check）注入 DbOverride，否则走 GameServer.Local。
    // 各后台模块（掉落/商店/商城/天气…）统一经这里拿 DbStore。
    public static DbStore Db
    {
        get
        {
            if (DbOverride != null) return DbOverride;
#if UNITY_EDITOR || UNITY_5_3_OR_NEWER
            return GameServer.Local?.Db;
#else
            return null;
#endif
        }
    }

    public static string Version => "MapleDash 单机版 " + UnityEngineVersion();

    static string UnityEngineVersion()
    {
#if UNITY_EDITOR || UNITY_5_3_OR_NEWER
        return UnityEngine.Application.version;
#else
        return "1.0.0";
#endif
    }

    // —————————— 行 → 前端对象 ——————————
    public static string Camel(string snake)
    {
        if (string.IsNullOrEmpty(snake) || snake.IndexOf('_') < 0) return snake;
        var sb = new System.Text.StringBuilder(snake.Length);
        bool upper = false;
        foreach (var c in snake)
        {
            if (c == '_') { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return sb.ToString();
    }

    public static Dictionary<string, object> ToDto(DbRow row, params string[] omit)
    {
        var d = new Dictionary<string, object>(row.Count);
        foreach (var kv in row)
        {
            if (omit != null && Array.IndexOf(omit, kv.Key) >= 0) continue;
            d[Camel(kv.Key)] = kv.Value;
        }
        return d;
    }

    // —————————— 后台账号 ——————————
    // 第一次起服务时，如果库里没有任何 webadmin 账号，就建一个 admin/admin，
    // 让「点侧边栏 → 浏览器登录」这条链路可用（只监听 127.0.0.1，不对外）。
    public static string EnsureWebAdmin()
    {
        return AdminMainThread.Run(() =>
        {
            var db = Db;
            if (db == null) return null;
            var accounts = db.Table("accounts");
            foreach (var r in accounts.rows)
                if (DbStore.Int(r, "webadmin") != 0) return null;

            var row = AccountsDao.FindByName(db, DefaultAdminName);
            if (row == null)
            {
                AccountsDao.AutoRegister(db, DefaultAdminName, DefaultAdminPassword);
                row = AccountsDao.FindByName(db, DefaultAdminName);
            }
            if (row == null) return null;
            accounts.Update(DbStore.Eq("id", Convert.ToInt64(row["id"])), new DbRow { ["webadmin"] = 1L });
            db.Save();
            return "已创建后台管理员账号 " + DefaultAdminName + " / " + DefaultAdminPassword;
        });
    }

    public static AdminAccount Authenticate(string name, string password)
    {
        return AdminMainThread.Run(() =>
        {
            var db = Db;
            if (db == null) return null;
            if (string.IsNullOrEmpty(name)) return null;
            var row = AccountsDao.FindByName(db, name);
            if (row == null) return null;
            if (DbStore.Int(row, "webadmin") == 0) return null;
            if (DbStore.Int(row, "banned") != 0) return null;
            if (!AccountsDao.VerifyPassword(password ?? "", DbStore.Str(row, "password"))) return null;

            long id = Convert.ToInt64(row["id"]);
            db.Table("accounts").Update(DbStore.Eq("id", id), new DbRow
            {
                ["lastlogin"] = DbStore.NowText(),
            });
            db.Save();
            return new AdminAccount { Id = id, Name = DbStore.Str(row, "name"), WebAdmin = true };
        });
    }

    public static Dictionary<string, object> AccountInfo(long id)
    {
        return AdminMainThread.Run(() =>
        {
            var db = Db;
            if (db == null) return null;
            var row = AccountsDao.FindById(db, (int)id);
            if (row == null) return null;
            var dto = ToDto(row, "password");
            dto["webadmin"] = true;
            return dto;
        });
    }

    // —————————— 账号列表 / 维护 ——————————
    public static Dictionary<string, object> AccountPage(int page, int size, long id, string name,
        string lastLoginStart, string lastLoginEnd, string createdAtStart, string createdAtEnd)
    {
        return AdminMainThread.Run(() =>
        {
            var db = Db;
            var all = new List<DbRow>();
            if (db != null)
            {
                foreach (var r in db.Table("accounts").rows)
                {
                    if (id > 0 && Convert.ToInt64(r["id"]) != id) continue;
                    if (!string.IsNullOrEmpty(name) &&
                        DbStore.Str(r, "name").IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!InRange(DbStore.Str(r, "lastlogin"), lastLoginStart, lastLoginEnd)) continue;
                    if (!InRange(DbStore.Str(r, "createdat"), createdAtStart, createdAtEnd)) continue;
                    all.Add(r);
                }
            }

            all.Sort((a, b) => Convert.ToInt64(b["id"]).CompareTo(Convert.ToInt64(a["id"])));
            int p = page <= 0 ? 1 : page;
            int s = size <= 0 ? 20 : size;
            var records = new List<object>();
            for (int i = (p - 1) * s; i < all.Count && i < p * s; i++)
                records.Add(ToDto(all[i], "password"));
            return AdminJson.Page(records, p, s, all.Count);
        });
    }

    // 日期区间比较：前端传 "2026-10-27" 或 "2026-10-27 00:00:00"，库里是 "yyyy-MM-dd HH:mm:ss"。
    static bool InRange(string value, string from, string to)
    {
        if (string.IsNullOrEmpty(value)) return string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to);
        var v = value.Length >= 10 ? value.Substring(0, 10) : value;
        if (!string.IsNullOrEmpty(from) && string.CompareOrdinal(v, from.Substring(0, Math.Min(10, from.Length))) < 0) return false;
        if (!string.IsNullOrEmpty(to) && string.CompareOrdinal(v, to.Substring(0, Math.Min(10, to.Length))) > 0) return false;
        return true;
    }

    public static List<object> CharacterMaps(long accountId)
    {
        return AdminMainThread.Run(() =>
        {
            var list = new List<object>();
            var db = Db;
            if (db == null) return list;
            foreach (var r in db.Table("characters").Select(DbStore.Eq("accountid", accountId)))
            {
                long mapId = DbStore.Long(r, "map");
                list.Add(AdminJson.Obj(
                    "characterId", Convert.ToInt64(r["id"]),
                    "characterName", DbStore.Str(r, "name"),
                    "mapId", mapId,
                    "mapName", MapName(mapId)));
            }
            return list;
        });
    }

    static string MapName(long mapId)
    {
        // 地图名只有 WZ 里有；这里用「地图 ID」兜底，避免后台去解析 Map.img 造成卡顿。
        return mapId <= 0 ? "-" : mapId.ToString(CultureInfo.InvariantCulture);
    }

    public static void AddAccount(string name, string password, string birthday, long? language)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            if (string.IsNullOrEmpty(name)) throw new AdminApiException("账号不能为空");
            if (string.IsNullOrEmpty(password)) throw new AdminApiException("密码不能为空");
            if (AccountsDao.FindByName(db, name) != null) throw new AdminApiException("账号已存在: " + name);

            var row = new DbRow
            {
                ["name"] = name,
                ["password"] = AccountsDao.HashPassword(password),
                ["createdat"] = DbStore.NowText(),
            };
            if (!string.IsNullOrEmpty(birthday)) row["birthday"] = birthday;
            if (language.HasValue) row["language"] = language.Value;
            db.Table("accounts").Insert(row);
            db.Save();
        });
    }

    public static void UpdateAccount(long id, Dictionary<string, object> form)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            var t = db.Table("accounts");
            if (t.Find(DbStore.Eq("id", id)) == null) throw new AdminApiException("账号不存在: " + id);

            var values = new DbRow();
            string newPwd = AdminJson.GetString(form, "newPwd");
            if (!string.IsNullOrEmpty(newPwd)) values["password"] = AccountsDao.HashPassword(newPwd);

            Copy(form, values, "pin", "pin");
            Copy(form, values, "pic", "pic");
            Copy(form, values, "birthday", "birthday");
            Copy(form, values, "nxCredit", "nxCredit");
            Copy(form, values, "maplePoint", "maplePoint");
            Copy(form, values, "nxPrepaid", "nxPrepaid");
            Copy(form, values, "characterslots", "characterslots");
            Copy(form, values, "gender", "gender");
            Copy(form, values, "webadmin", "webadmin");
            Copy(form, values, "nick", "nick");
            Copy(form, values, "mute", "mute");
            Copy(form, values, "email", "email");
            Copy(form, values, "rewardpoints", "rewardpoints");
            Copy(form, values, "votepoints", "votepoints");
            Copy(form, values, "language", "language");

            if (values.Count == 0) return;
            t.Update(DbStore.Eq("id", id), values);
            db.Save();
        });
    }

    static void Copy(Dictionary<string, object> from, DbRow to, string fromKey, string column)
    {
        if (from == null || !from.ContainsKey(fromKey)) return;
        var v = from[fromKey];
        if (v == null) return;
        to[column] = v is bool b ? (b ? 1L : 0L) : v;
    }

    public static void DeleteAccount(long id)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            var chars = db.Table("characters");
            var charIds = new List<long>();
            foreach (var r in chars.Select(DbStore.Eq("accountid", id))) charIds.Add(Convert.ToInt64(r["id"]));
            foreach (var cid in charIds) DeleteCharacterRows(db, cid);

            chars.Delete(DbStore.Eq("accountid", id));
            db.Table("accounts").Delete(DbStore.Eq("id", id));
            db.Save();
        });
    }

    static void DeleteCharacterRows(DbStore db, long characterId)
    {
        foreach (var table in new[] { "keymap", "skills", "skillmacros", "queststatus", "savedlocations" })
            if (db.HasTable(table)) db.Table(table).Delete(DbStore.Eq("characterid", characterId));

        var items = db.Table("inventoryitems").Select(DbStore.Eq("characterid", characterId));
        foreach (var item in items)
        {
            long itemId = Convert.ToInt64(item["inventoryitemid"]);
            if (db.HasTable("inventoryequipment"))
                db.Table("inventoryequipment").Delete(DbStore.Eq("inventoryitemid", itemId));
        }
        db.Table("inventoryitems").Delete(DbStore.Eq("characterid", characterId));
    }

    public static void BanAccount(long id, string reason)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            db.Table("accounts").Update(DbStore.Eq("id", id), new DbRow
            {
                ["banned"] = 1L,
                ["banreason"] = string.IsNullOrEmpty(reason) ? "违规操作" : reason,
                ["loggedin"] = 0L,
            });
            db.Save();
            // 正在玩的账号立即踢回登录页；否则要等下次登录才被 LoginService 挡住
            LiveKick(id, reason);
        });
    }

    public static void UnbanAccount(long id)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            db.Table("accounts").Update(DbStore.Eq("id", id), new DbRow
            {
                ["banned"] = 0L,
                ["banreason"] = null,
                ["tempban"] = "2005-05-11 00:00:00",
            });
            db.Save();
        });
    }

    public static void ResetLoggedIn(long id)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            db.Table("accounts").Update(DbStore.Eq("id", id), new DbRow { ["loggedin"] = 0L });
            db.Save();
        });
    }

    public static void MoveAccountToHenesys(long accountId)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            db.Table("characters").Update(DbStore.Eq("accountid", accountId), new DbRow
            {
                ["map"] = (long)HenesysMap,
                ["spawnpoint"] = 0L,
            });
            db.Save();

            long current = CurrentCharId();
            if (current <= 0) return;
            var cur = db.Table("characters").Find(DbStore.Eq("id", current));
            if (cur != null && DbStore.Long(cur, "accountid") == accountId)
                LiveWarp(current);
        });
    }

    // —————————— 角色 ——————————
    public static Dictionary<string, object> OnlineCharacterPage(int page, int size, long id, string name, long map)
    {
        return AdminMainThread.Run(() =>
        {
            var db = Db;
            var matched = new List<DbRow>();
            if (db != null)
            {
                foreach (var r in db.Table("characters").rows)
                {
                    if (id > 0 && Convert.ToInt64(r["id"]) != id) continue;
                    if (!string.IsNullOrEmpty(name) &&
                        DbStore.Str(r, "name").IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (map > 0 && DbStore.Long(r, "map") != map) continue;
                    matched.Add(r);
                }
            }

            matched.Sort((a, b) => Convert.ToInt64(b["id"]).CompareTo(Convert.ToInt64(a["id"])));
            int p = page <= 0 ? 1 : page;
            int s = size <= 0 ? 20 : size;
            long current = 0;
            try { current = CurrentCharacterId != null ? CurrentCharacterId() : 0; } catch (Exception) { }
            var records = new List<object>();
            for (int i = (p - 1) * s; i < matched.Count && i < p * s; i++)
            {
                var r = matched[i];
                int job = DbStore.Int(r, "job");
                var dto = ToDto(r);
                dto["jobName"] = SafeJobName(job);
                dto["online"] = Convert.ToInt64(r["id"]) == current && current > 0;
                records.Add(dto);
            }
            return AdminJson.Page(records, p, s, matched.Count);
        });
    }

    static string SafeJobName(int job)
    {
#if UNITY_EDITOR || UNITY_5_3_OR_NEWER
        try { return JobDefs.NameOf(job); }
        catch (Exception) { return job.ToString(CultureInfo.InvariantCulture); }
#else
        return job.ToString(CultureInfo.InvariantCulture);
#endif
    }

    public static void MoveCharacterToHenesys(long id)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            var t = db.Table("characters");
            if (t.Find(DbStore.Eq("id", id)) == null) throw new AdminApiException("角色不存在: " + id);
            t.Update(DbStore.Eq("id", id), new DbRow { ["map"] = (long)HenesysMap, ["spawnpoint"] = 0L });
            db.Save();
            if (CurrentCharId() == id) LiveWarp(id);
        });
    }

    public static int MoveCharactersToHenesys(long id, string name)
    {
        return AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            var t = db.Table("characters");
            int n = 0;
            bool movedCurrent = false;
            long current = CurrentCharId();
            foreach (var r in t.rows)
            {
                if (id > 0 && Convert.ToInt64(r["id"]) != id) continue;
                if (!string.IsNullOrEmpty(name) &&
                    DbStore.Str(r, "name").IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                r["map"] = (long)HenesysMap;
                r["spawnpoint"] = 0L;
                if (current > 0 && Convert.ToInt64(r["id"]) == current) movedCurrent = true;
                n++;
            }
            if (n > 0) db.Save();
            if (movedCurrent) LiveWarp(current);
            return n;
        });
    }

    public static void ChangeCharacterName(long id, string newName)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            if (!CharNameRules.Valid(newName))
                throw new AdminApiException("角色名必须是 " + CharNameRules.MinLength + "~" + CharNameRules.MaxLength + " 位英文或数字");
            var t = db.Table("characters");
            if (t.Find(DbStore.Eq("id", id)) == null) throw new AdminApiException("角色不存在: " + id);
            foreach (var r in t.rows)
                if (Convert.ToInt64(r["id"]) != id &&
                    string.Equals(DbStore.Str(r, "name"), newName, StringComparison.OrdinalIgnoreCase))
                    throw new AdminApiException("角色名已被占用: " + newName);
            t.Update(DbStore.Eq("id", id), new DbRow { ["name"] = newName });
            db.Save();
            if (CurrentCharId() == id) LiveRename(id, newName);
        });
    }

    // —————————— 背包 ——————————
    public static List<object> InventoryTypes()
    {
        var list = new List<object>();
        var names = new[] { "装备", "消耗", "设置", "其他", "现金" };
        for (int i = 1; i <= names.Length; i++)
            list.Add(AdminJson.Obj("inventoryType", i, "name", names[i - 1]));
        return list;
    }

    public static Dictionary<string, object> InventoryCharacterPage(long characterId, string characterName,
        long accountId, int page, int size)
    {
        return AdminMainThread.Run(() =>
        {
            var db = Db;
            var matched = new List<DbRow>();
            if (db != null)
            {
                foreach (var r in db.Table("characters").rows)
                {
                    if (characterId > 0 && Convert.ToInt64(r["id"]) != characterId) continue;
                    if (!string.IsNullOrEmpty(characterName) &&
                        DbStore.Str(r, "name").IndexOf(characterName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (accountId > 0 && DbStore.Long(r, "accountid") != accountId) continue;
                    matched.Add(r);
                }
            }
            matched.Sort((a, b) => Convert.ToInt64(b["id"]).CompareTo(Convert.ToInt64(a["id"])));
            int p = page <= 0 ? 1 : page;
            int s = size <= 0 ? 20 : size;
            var records = new List<object>();
            for (int i = (p - 1) * s; i < matched.Count && i < p * s; i++)
            {
                var dto = ToDto(matched[i]);
                dto["jobName"] = SafeJobName(DbStore.Int(matched[i], "job"));
                records.Add(dto);
            }
            return AdminJson.Page(records, p, s, matched.Count);
        });
    }

    public static Dictionary<string, object> InventoryPage(long characterId, long accountId, int inventoryType,
        int page, int size)
    {
        return AdminMainThread.Run(() =>
        {
            var db = Db;
            var matched = new List<DbRow>();
            if (db != null)
            {
                foreach (var r in db.Table("inventoryitems").rows)
                {
                    if (characterId > 0 && DbStore.Long(r, "characterid") != characterId) continue;
                    if (characterId <= 0 && accountId > 0 && DbStore.Long(r, "accountid") != accountId) continue;
                    if (inventoryType > 0 && DbStore.Int(r, "inventorytype") != inventoryType) continue;
                    matched.Add(r);
                }
            }

            // 源端按「背包类型 → 位置」排序，界面上一眼能对上游戏里的格子。
            matched.Sort((a, b) =>
            {
                int c = DbStore.Int(a, "inventorytype").CompareTo(DbStore.Int(b, "inventorytype"));
                return c != 0 ? c : DbStore.Int(a, "position").CompareTo(DbStore.Int(b, "position"));
            });

            int p = page <= 0 ? 1 : page;
            int s = size <= 0 ? 20 : size;
            var records = new List<object>();
            for (int i = (p - 1) * s; i < matched.Count && i < p * s; i++)
                records.Add(InventoryDto(db, matched[i]));
            return AdminJson.Page(records, p, s, matched.Count);
        });
    }

    static Dictionary<string, object> InventoryDto(DbStore db, DbRow row)
    {
        long itemId = DbStore.Long(row, "inventoryitemid");
        var dto = new Dictionary<string, object>
        {
            { "id", itemId },
            { "characterId", DbStore.Long(row, "characterid") },
            { "accountId", DbStore.Long(row, "accountid") },
            { "itemId", DbStore.Long(row, "itemid") },
            { "itemType", DbStore.Int(row, "type") },
            { "inventoryType", DbStore.Int(row, "inventorytype") },
            { "position", DbStore.Int(row, "position") },
            { "quantity", DbStore.Long(row, "quantity") },
            { "owner", DbStore.Str(row, "owner") },
            { "petId", DbStore.Long(row, "petid") },
            { "flag", DbStore.Int(row, "flag") },
            { "expiration", DbStore.Long(row, "expiration") },
            { "giftFrom", DbStore.Str(row, "giftFrom") },
        };

        var equip = db.HasTable("inventoryequipment")
            ? db.Table("inventoryequipment").Find(DbStore.Eq("inventoryitemid", itemId))
            : null;
        dto["equipment"] = equip != null;
        if (equip != null)
        {
            dto["inventoryEquipment"] = AdminJson.Obj(
                "id", Convert.ToInt64(equip["inventoryequipmentid"]),
                "inventoryItemId", itemId,
                "upgradeSlots", DbStore.Int(equip, "upgradeslots"),
                "level", DbStore.Int(equip, "level"),
                "attStr", DbStore.Int(equip, "str"),
                "attDex", DbStore.Int(equip, "dex"),
                "attInt", DbStore.Int(equip, "int"),
                "attLuk", DbStore.Int(equip, "luk"),
                "hp", DbStore.Int(equip, "hp"),
                "mp", DbStore.Int(equip, "mp"),
                "patk", DbStore.Int(equip, "watk"),
                "matk", DbStore.Int(equip, "matk"),
                "pdef", DbStore.Int(equip, "wdef"),
                "mdef", DbStore.Int(equip, "mdef"),
                "acc", DbStore.Int(equip, "acc"),
                "avoid", DbStore.Int(equip, "avoid"),
                "hands", DbStore.Int(equip, "hands"),
                "speed", DbStore.Int(equip, "speed"),
                "jump", DbStore.Int(equip, "jump"),
                "locked", DbStore.Int(equip, "locked"),
                "vicious", DbStore.Int(equip, "vicious"),
                "itemLevel", DbStore.Int(equip, "itemlevel"),
                "itemExp", DbStore.Int(equip, "itemexp"),
                "ringId", DbStore.Int(equip, "ringid"));
        }
        return dto;
    }

    public static void UpdateInventory(Dictionary<string, object> form)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            long id = AdminJson.GetLong(form, "id");
            var t = db.Table("inventoryitems");
            var itemRow = t.Find(DbStore.Eq("inventoryitemid", id));
            if (itemRow == null)
                throw new AdminApiException("道具不存在: " + id);
            long ownerCharId = DbStore.Long(itemRow, "characterid");

            var values = new DbRow();
            Copy(form, values, "itemId", "itemid");
            Copy(form, values, "inventoryType", "inventorytype");
            Copy(form, values, "position", "position");
            Copy(form, values, "quantity", "quantity");
            Copy(form, values, "owner", "owner");
            Copy(form, values, "flag", "flag");
            Copy(form, values, "expiration", "expiration");
            Copy(form, values, "giftFrom", "giftFrom");
            if (values.Count > 0) t.Update(DbStore.Eq("inventoryitemid", id), values);

            var equipForm = AdminJson.GetDict(form, "inventoryEquipment");
            if (equipForm != null && db.HasTable("inventoryequipment"))
            {
                var eq = db.Table("inventoryequipment");
                var row = eq.Find(DbStore.Eq("inventoryitemid", id));
                var eqValues = new DbRow();
                Copy(equipForm, eqValues, "upgradeSlots", "upgradeslots");
                Copy(equipForm, eqValues, "level", "level");
                Copy(equipForm, eqValues, "attStr", "str");
                Copy(equipForm, eqValues, "attDex", "dex");
                Copy(equipForm, eqValues, "attInt", "int");
                Copy(equipForm, eqValues, "attLuk", "luk");
                Copy(equipForm, eqValues, "hp", "hp");
                Copy(equipForm, eqValues, "mp", "mp");
                Copy(equipForm, eqValues, "patk", "watk");
                Copy(equipForm, eqValues, "matk", "matk");
                Copy(equipForm, eqValues, "pdef", "wdef");
                Copy(equipForm, eqValues, "mdef", "mdef");
                Copy(equipForm, eqValues, "acc", "acc");
                Copy(equipForm, eqValues, "avoid", "avoid");
                Copy(equipForm, eqValues, "hands", "hands");
                Copy(equipForm, eqValues, "speed", "speed");
                Copy(equipForm, eqValues, "jump", "jump");
                Copy(equipForm, eqValues, "locked", "locked");
                Copy(equipForm, eqValues, "vicious", "vicious");
                Copy(equipForm, eqValues, "itemLevel", "itemlevel");
                Copy(equipForm, eqValues, "itemExp", "itemexp");
                if (row != null && eqValues.Count > 0)
                    eq.Update(DbStore.Eq("inventoryitemid", id), eqValues);
                else if (row == null)
                {
                    eqValues["inventoryitemid"] = id;
                    eq.Insert(eqValues);
                }
            }
            db.Save();
            if (ownerCharId > 0) LiveInventory(db, ownerCharId, id, false);
        });
    }

    public static void DeleteInventory(Dictionary<string, object> form)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            long id = AdminJson.GetLong(form, "id");
            if (id <= 0) throw new AdminApiException("缺少道具 id");
            var itemRow = db.Table("inventoryitems").Find(DbStore.Eq("inventoryitemid", id));
            long ownerCharId = itemRow != null ? DbStore.Long(itemRow, "characterid") : 0;

            if (db.HasTable("inventoryequipment"))
                db.Table("inventoryequipment").Delete(DbStore.Eq("inventoryitemid", id));
            db.Table("inventoryitems").Delete(DbStore.Eq("inventoryitemid", id));
            db.Save();
            if (ownerCharId > 0) LiveInventory(db, ownerCharId, id, true);
        });
    }

    // —————————— 服务端参数（game_config） ——————————
    public static Dictionary<string, object> ConfigTypeList()
    {
        return AdminMainThread.Run(() =>
        {
            var types = new List<object>();
            var subTypes = new List<object>();
            var db = Db;
            if (db != null)
            {
                foreach (var r in db.Table("game_config").rows)
                {
                    string t = DbStore.Str(r, "config_type");
                    if (!string.IsNullOrEmpty(t) && !types.Contains(t)) types.Add(t);
                    string st = DbStore.Str(r, "config_sub_type");
                    if (!string.IsNullOrEmpty(st) && !subTypes.Contains(st)) subTypes.Add(st);
                }
            }
            return AdminJson.Obj("types", types, "subTypes", subTypes);
        });
    }

    public static Dictionary<string, object> ConfigPage(string type, string subType, string filter, int page, int size)
    {
        return AdminMainThread.Run(() =>
        {
            var db = Db;
            var matched = new List<DbRow>();
            if (db != null)
            {
                foreach (var r in db.Table("game_config").rows)
                {
                    if (!string.IsNullOrEmpty(type) && DbStore.Str(r, "config_type") != type) continue;
                    if (!string.IsNullOrEmpty(subType) && DbStore.Str(r, "config_sub_type") != subType) continue;
                    if (!string.IsNullOrEmpty(filter))
                    {
                        bool hit = DbStore.Str(r, "config_code").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                                || DbStore.Str(r, "config_desc").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                                || DbStore.Str(r, "config_value").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!hit) continue;
                    }
                    matched.Add(r);
                }
            }
            matched.Sort((a, b) => Convert.ToInt64(a["id"]).CompareTo(Convert.ToInt64(b["id"])));

            int p = page <= 0 ? 1 : page;
            int s = size <= 0 ? 20 : size;
            var records = new List<object>();
            for (int i = (p - 1) * s; i < matched.Count && i < p * s; i++)
                records.Add(ToDto(matched[i]));
            return AdminJson.Page(records, p, s, matched.Count);
        });
    }

    public static void AddConfig(Dictionary<string, object> form)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            var row = new DbRow
            {
                ["config_type"] = AdminJson.GetString(form, "configType"),
                ["config_sub_type"] = AdminJson.GetString(form, "configSubType"),
                ["config_clazz"] = AdminJson.GetString(form, "configClazz", "java.lang.String"),
                ["config_code"] = AdminJson.GetString(form, "configCode"),
                ["config_value"] = AdminJson.GetString(form, "configValue"),
                ["config_desc"] = AdminJson.GetString(form, "configDesc"),
                ["update_time"] = DbStore.NowText(),
            };
            if (string.IsNullOrEmpty(Convert.ToString(row["config_code"])))
                throw new AdminApiException("参数名不能为空");
            db.Table("game_config").Insert(row);
            db.Save();
        });
    }

    public static void UpdateConfig(Dictionary<string, object> form)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            long id = AdminJson.GetLong(form, "id");
            var t = db.Table("game_config");
            if (t.Find(DbStore.Eq("id", id)) == null) throw new AdminApiException("参数不存在: " + id);

            var values = new DbRow();
            Copy(form, values, "configType", "config_type");
            Copy(form, values, "configSubType", "config_sub_type");
            Copy(form, values, "configClazz", "config_clazz");
            Copy(form, values, "configCode", "config_code");
            Copy(form, values, "configValue", "config_value");
            Copy(form, values, "configDesc", "config_desc");
            values["update_time"] = DbStore.NowText();
            t.Update(DbStore.Eq("id", id), values);
            db.Save();
        });
    }

    public static void DeleteConfig(long id)
    {
        AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            db.Table("game_config").Delete(DbStore.Eq("id", id));
            db.Save();
        });
    }

    public static int DeleteConfigList(IEnumerable<object> ids)
    {
        return AdminMainThread.Run(() =>
        {
            var db = RequireDb();
            int n = 0;
            foreach (var raw in ids)
            {
                long id = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                n += db.Table("game_config").Delete(DbStore.Eq("id", id));
            }
            db.Save();
            return n;
        });
    }

    public static DbStore RequireDb()
    {
        var db = Db;
        if (db == null) throw new AdminApiException("服务端未启动（请先进入游戏）");
        return db;
    }

    // —————————— 实时同步（只对「当前在线角色」有意义） ——————————
    // 后台写的是库，游戏里的角色是进游戏时读进内存、退出时才整份写回的。
    // 不动内存的话：界面上看不到变化，退出时还会被旧值覆盖。
    // AdminLive 依赖 Unity 侧类型，离线自检工程（tools/admin-check）不编译它，
    // 所以这里统一用 #if 包住，保证本文件在两种工程里都能编译。
#if UNITY_EDITOR || UNITY_5_3_OR_NEWER
    static void Live(Action action)
    {
        try { action(); }
        catch (Exception e) { UnityEngine.Debug.LogWarning("[Admin] 实时同步失败: " + e.Message); }
    }

    static void LiveWarp(long characterId) => Live(() => AdminLive.WarpToHenesys(characterId));
    public static void LiveWarpTo(long characterId, long mapId) => Live(() => AdminLive.WarpTo(characterId, mapId));
    static void LiveRename(long characterId, string newName) => Live(() => AdminLive.Rename(characterId, newName));
    public static void LiveInventory(DbStore db, long characterId, long itemRowId, bool deleted)
        => Live(() => AdminLive.SyncInventory(db, characterId, itemRowId, deleted));
    static void LiveKick(long accountId, string reason) => Live(() => AdminLive.KickAccount(accountId, reason));
#else
    static void LiveWarp(long characterId) { }
    public static void LiveWarpTo(long characterId, long mapId) { }
    static void LiveRename(long characterId, string newName) { }
    public static void LiveInventory(DbStore db, long characterId, long itemRowId, bool deleted) { }
    static void LiveKick(long accountId, string reason) { }
#endif

    static long CurrentCharId()
    {
        try { return CurrentCharacterId != null ? CurrentCharacterId() : 0; }
        catch (Exception) { return 0; }
    }
}
