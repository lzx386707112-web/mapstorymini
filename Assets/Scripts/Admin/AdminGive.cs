// 后台「发放资源」模块：POST /give/v1/resource（源端 GiveController + GiveService）。
//
// GiveResourceReqDTO：worldId/playerId/type/id/quantity + 装备属性一整套（str…jump、
// upgradeSlot、expire）。type 取值与源端 GiveService.give 一致：
//   0 点券 / 1 信用点 / 2 抵用券 → accounts.nxCredit|nxPrepaid|maplePoint（0..int.Max 钳制）
//   3 金币 → characters.mesos（0..int.Max 钳制）      4 经验 → characters.exp（最低清零）
//   5 物品（非装备；宠物按 quantity 天数）             6 装备（含属性/卷位/expire）
//   7/8/9/10 倍率 → 单机版不接（exp/meso/drop_rate 只影响世界列表显示，见 docs/admin-console.md）
//   11 GM 等级（0..127）                              12 声望 fame
//   13 传送地图（910000000 特判 FREE_MARKET 的语义在单机版无自由市场，直接落 map）
// playerId=0 源端是「全服在线」；单机版只有一个会话，等价于发给当前在线角色。
//
// 约束：纯 C#，不引用 UnityEngine（实时同步经 AdminBackend.LiveInventory / LiveWarpTo）。
using System;
using System.Collections.Generic;

public static class AdminGive
{
    public static void Register(AdminApi api)
    {
        api.Post("/give/v1/resource", ctx =>
        {
            var form = AdminJson.AsDict(ctx.Body);
            if (form == null) throw new AdminApiException("缺少发放数据");
            long type = AdminJson.GetLong(form, "type");
            long id = AdminJson.GetLong(form, "id");
            long quantity = AdminJson.GetLong(form, "quantity");
            long playerId = AdminJson.GetLong(form, "playerId");

            return AdminMainThread.Run<object>(() =>
            {
                var db = AdminBackend.RequireDb();
                if (type >= 7 && type <= 10)
                    throw new AdminApiException("倍率类（type 7-10）单机版未接入：大区倍率只影响世界列表显示，游戏内经验/掉落走本地倍率");

                long characterId = ResolveCharacter(db, playerId);
                switch (type)
                {
                    case 0: AddCurrency(db, characterId, "nxCredit", quantity); break;
                    case 1: AddCurrency(db, characterId, "nxPrepaid", quantity); break;
                    case 2: AddCurrency(db, characterId, "maplePoint", quantity); break;
                    case 3: AddMesos(db, characterId, quantity); break;
                    case 4: AddExp(db, characterId, quantity); break;
                    case 5: GiveItem(db, characterId, (int)id, (int)Math.Max(1, quantity), form); break;
                    case 6: GiveEquip(db, characterId, (int)id, form); break;
                    case 11: SetGm(db, characterId, quantity); break;
                    case 12: SetFame(db, characterId, quantity); break;
                    case 13: Warp(db, characterId, id > 0 ? id : 100000000); break;
                    default: throw new AdminApiException("不支持的发放类型: " + type);
                }
                db.Save();
                return null;
            });
        });
    }

    static long ResolveCharacter(DbStore db, long playerId)
    {
        long current = 0;
        try { current = AdminBackend.CurrentCharacterId != null ? AdminBackend.CurrentCharacterId() : 0; }
        catch { current = 0; }

        long target = playerId == 0 ? current : playerId;
        if (target <= 0) throw new AdminApiException("请指定角色（playerId），或先进入游戏再发给当前角色");
        if (db.Table("characters").Find(DbStore.Eq("id", target)) == null)
            throw new AdminApiException("角色不存在: " + target);
        return target;
    }

    // —————————— 账号货币 ——————————
    static void AddCurrency(DbStore db, long characterId, string column, long quantity)
    {
        var character = db.Table("characters").Find(DbStore.Eq("id", characterId));
        long accountId = DbStore.Long(character, "accountid");
        var account = db.Table("accounts").Find(DbStore.Eq("id", accountId));
        if (account == null) throw new AdminApiException("角色所属账号不存在: " + accountId);

        long value = DbStore.Long(account, column) + quantity;
        if (value < 0) value = 0;
        if (value > int.MaxValue) value = int.MaxValue;      // 源端 0 ≤ sum ≤ Integer.MAX_VALUE
        db.Table("accounts").Update(DbStore.Eq("id", accountId), new DbRow { [column] = value });
    }

    static void AddMesos(DbStore db, long characterId, long quantity)
    {
        var character = db.Table("characters").Find(DbStore.Eq("id", characterId));
        long value = DbStore.Long(character, "mesos") + quantity;
        if (value < 0) value = 0;
        if (value > int.MaxValue) value = int.MaxValue;
        db.Table("characters").Update(DbStore.Eq("id", characterId), new DbRow { ["mesos"] = value });
        SyncCharacterField(characterId, "mesos", value);
    }

    static void AddExp(DbStore db, long characterId, long quantity)
    {
        var character = db.Table("characters").Find(DbStore.Eq("id", characterId));
        long value = Math.Max(0, DbStore.Long(character, "exp") + quantity);   // 源端经验最低清零
        db.Table("characters").Update(DbStore.Eq("id", characterId), new DbRow { ["exp"] = value });
        SyncCharacterField(characterId, "exp", value);
    }

    static void SetGm(DbStore db, long characterId, long level)
    {
        long value = Math.Max(0, Math.Min(127, level));                        // 源端 0-127
        db.Table("characters").Update(DbStore.Eq("id", characterId), new DbRow { ["gm"] = value });
    }

    static void SetFame(DbStore db, long characterId, long fame)
    {
        db.Table("characters").Update(DbStore.Eq("id", characterId), new DbRow { ["fame"] = Math.Max(0, fame) });
    }

    static void Warp(DbStore db, long characterId, long mapId)
    {
        db.Table("characters").Update(DbStore.Eq("id", characterId), new DbRow { ["map"] = mapId });
        AdminBackend.LiveWarpTo(characterId, mapId);
    }

    // —————————— 道具 / 装备 ——————————
    static void GiveItem(DbStore db, long characterId, int itemId, int quantity, Dictionary<string, object> form)
    {
        if (itemId <= 0) throw new AdminApiException("道具 ID 不能为空");
        // 源端 type=5 是「非装备」（宠物时 quantity 作天数），不建 inventoryequipment 行；
        // 装备一律走 type=6（gainEquip 带属性/卷位/expire）。
        long rowId = InsertItem(db, characterId, itemId, quantity, form, out _);
        AdminBackend.LiveInventory(db, characterId, rowId, false);
    }

    static void GiveEquip(DbStore db, long characterId, int itemId, Dictionary<string, object> form)
    {
        if (itemId <= 0) throw new AdminApiException("装备 ID 不能为空");
        long rowId = InsertItem(db, characterId, itemId, 1, form, out _);
        InventoryDao.InsertEquipment(db, rowId,
            (int)AdminJson.GetLong(form, "str"), (int)AdminJson.GetLong(form, "dex"),
            (int)AdminJson.GetLong(form, "int"), (int)AdminJson.GetLong(form, "luk"),
            (int)AdminJson.GetLong(form, "hp"), (int)AdminJson.GetLong(form, "mp"),
            (int)AdminJson.GetLong(form, "pAtk"), (int)AdminJson.GetLong(form, "mAtk"),
            (int)AdminJson.GetLong(form, "pDef"), (int)AdminJson.GetLong(form, "mDef"),
            (int)AdminJson.GetLong(form, "acc"), (int)AdminJson.GetLong(form, "avoid"),
            (int)AdminJson.GetLong(form, "hands"), (int)AdminJson.GetLong(form, "speed"),
            (int)AdminJson.GetLong(form, "jump"), (int)AdminJson.GetLong(form, "upgradeSlot"));
        AdminBackend.LiveInventory(db, characterId, rowId, false);
    }

    static long InsertItem(DbStore db, long characterId, int itemId, int quantity, Dictionary<string, object> form, out int invType)
    {
        invType = Math.Max(1, Math.Min(5, itemId / 1000000));
        int position = NextPosition(db, (int)characterId, invType);
        long rowId = InventoryDao.InsertItem(db, (int)characterId, itemId, invType, position, quantity);

        long expire = AdminJson.GetLong(form, "expire", -1);
        if (expire != -1)
            db.Table("inventoryitems").Update(DbStore.Eq("inventoryitemid", rowId), new DbRow { ["expiration"] = expire });
        return rowId;
    }

    // 找该背包栏下一个空位（源端 gainItem 的位置分配语义，这里取当前最大位+1）
    static int NextPosition(DbStore db, int characterId, int invType)
    {
        int max = 0;
        foreach (var row in InventoryDao.ItemsByCharacter(db, characterId))
        {
            if (DbStore.Long(row, "inventorytype") != invType) continue;
            if (DbStore.Long(row, "position") < 0) continue;              // 负位=已装备，不占格
            max = Math.Max(max, (int)DbStore.Long(row, "position"));
        }
        return max + 1;
    }

    // 当前在线角色的内存记录同步（离线角色无需处理，落库即下次选角生效）
    static void SyncCharacterField(long characterId, string field, long value)
    {
#if UNITY_EDITOR || UNITY_5_3_OR_NEWER
        try
        {
            if (GameSession.InGame && GameSession.CharacterId == (int)characterId &&
                GameSession.Character != null && GameSession.Character.record != null)
            {
                if (field == "meso") GameSession.Character.record.meso = (int)value;
                if (field == "exp") GameSession.Character.record.exp = (int)value;
            }
        }
        catch (Exception) { }
#endif
    }
}
