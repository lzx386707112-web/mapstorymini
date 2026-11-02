using System;
using System.Collections.Generic;

// 快递（Duey）—— 对照 gms-server：
//   client/processor/npc/DueyProcessor.java（dueySendItem / dueyClaimPackage / dueyRemovePackage /
//                                           dueySendTalk / runDueyExpireSchedule）
//   server/DueyPackage.java（setSentTime 的 quick 分支、isDeliveringTime）
//   server/Trade.java:93-108（getFee 分档费率）
//   util/PacketCreator.java:6626-6688（PARCEL 包：0x08 列表 / 0x17 取件 / 0x19 到件通知）
//   resources/db/migration/V1.0.9__create_dueyi.sql
//
// 单机语义：收件人只可能是本地角色，所以「通知在线收件人」退化成同一次调用里的到件标记；
// 其余校验顺序、费率、错误码全部照源端，不改写。
public enum DueyOperation { Open, Quick, Send, Claim, Remove, Close, Notify }

public sealed class DueyRequest
{
    public int characterId;
    public DueyOperation operation;
    public int packageId;
    public int inventoryType, itemPosition, itemQuantity, mesos;
    public string message = "", recipient = "";
    public bool quick;
}

public sealed class DueyPackageData
{
    public int packageId, mesos;
    public string sender = "", message;
    public bool quick;
    public long sendTime;          // 源端 sentTimeInMilliseconds()（月份 +1 的展示编码由客户端做）
    public InventoryEntry item;
}

public sealed class DueyResponse
{
    public bool ok = true;
    public string error = "";
    public byte code;              // DueyProcessor.Actions 的码，客户端按它选提示
    public bool quick;
    public int fee;
    public long meso = -1;         // 发生变化后的金币（-1 = 本操作没动金币）
    public List<DueyPackageData> packages;
    public List<InventoryEntry> inventory;
    public DueyPackageData pending; // 到件通知（Notify）
}

public static class NativeDueyService
{
    // ItemId.QUICK_DELIVERY_TICKET
    public const int QuickDeliveryTicket = 5330000;
    const int UnTradeable = 0x08, KarmaUse = 0x02, KarmaEqp = 0x10;

    // DueyProcessor.Actions
    public const byte SendEnableActions = 0x09;
    public const byte NotEnoughMesos = 0x0A;
    public const byte IncorrectRequest = 0x0B;
    public const byte NameDoesNotExist = 0x0C;
    public const byte SameAccError = 0x0D;
    public const byte SuccessfullySent = 0x12;
    public const byte RecvUnknownError = 0x13;
    public const byte RecvNoFreeSlots = 0x15;
    public const byte RecvReceiverWithUnique = 0x16;

    // Trade.getFee：按金额分档，整数除法向零截断。
    public static int GetFee(long meso)
    {
        long fee = 0;
        if (meso >= 100000000) fee = meso * 6 / 100;
        else if (meso >= 25000000) fee = meso * 5 / 100;
        else if (meso >= 10000000) fee = meso * 4 / 100;
        else if (meso >= 5000000) fee = meso * 3 / 100;
        else if (meso >= 1000000) fee = meso * 18 / 1000;
        else if (meso >= 100000) fee = meso * 8 / 1000;
        return unchecked((int)fee);
    }

    static bool Rechargeable(int itemId) => itemId / 10000 == 207;

    // Item.isUntradeable = (flag & UNTRADEABLE) != 0 || (isDropRestricted() && !hasKarma())
    static bool IsUntradeable(DbStore db, InventoryEntry item)
    {
        if ((item.flag & UnTradeable) != 0) return true;
        int karma = item.isEquip ? KarmaEqp : KarmaUse;
        if ((item.flag & karma) == karma) return false;   // hasKarma → 允许交易
        return IsDropRestricted(item);
    }

    // ItemInformationProvider.isDropRestricted：IMG info/tradeBlock == 1。
    static bool IsDropRestricted(InventoryEntry item)
        => Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(item.itemId), "tradeBlock") == 1;

    // ItemInformationProvider.isPickupRestricted：IMG info/only == 1（与 TradeItemRules.Unique 同源）。
    static bool IsPickupRestricted(int itemId)
        => Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(itemId), "only") == 1;

    // ItemInformationProvider.isUnmerchable：受 use_enforce_unmerchable_cash / _pet 控制。
    static bool IsUnmerchable(DbStore db, int itemId)
    {
        if (GameConfigDao.GetBool(db, "use_enforce_unmerchable_cash", true) && itemId / 1000000 == 5) return true;
        return GameConfigDao.GetBool(db, "use_enforce_unmerchable_pet", true) && itemId / 1000 == 5000;
    }

    // KarmaManipulator.toggleKarmaFlagToUntradeable：karma 位换成 UNTRADEABLE。
    static void ToggleKarmaFlagToUntradeable(InventoryEntry item)
    {
        int karma = item.isEquip ? KarmaEqp : KarmaUse;
        if ((item.flag & karma) == karma) item.flag = (item.flag ^ karma) | UnTradeable;
    }

    public static DueyResponse Handle(DbStore db, DueyRequest r)
    {
        if (!GameConfigDao.GetBool(db, "use_duey", true))
            return new DueyResponse { ok = false, error = "快递系统未开启" };
        var c = CharactersDao.FindById(db, r.characterId);
        if (c == null) return new DueyResponse { ok = false, error = "角色不存在" };

        switch (r.operation)
        {
            case DueyOperation.Open: return new DueyResponse { packages = Load(db, (int)c.id) };
            case DueyOperation.Quick: return new DueyResponse { quick = true };
            case DueyOperation.Send: return Send(db, c, r);
            case DueyOperation.Claim: return Claim(db, c, r);
            case DueyOperation.Remove: return Remove(db, c, r);
            case DueyOperation.Notify: return new DueyResponse { pending = TakePendingNotification(db, (int)c.id) };
            default: return new DueyResponse { ok = true };
        }
    }

    static List<DueyPackageData> Load(DbStore db, int characterId)
    {
        var list = new List<DueyPackageData>();
        foreach (var row in db.Table("dueypackages").Select(x => DbStore.Int(x, "receiverid") == characterId))
        {
            var item = ItemOf(db, DbStore.Int(row, "packageid"));
            list.Add(new DueyPackageData
            {
                packageId = DbStore.Int(row, "packageid"),
                mesos = DbStore.Int(row, "mesos"),
                sender = DbStore.Str(row, "sendername"),
                message = row.TryGetValue("message", out var msg) ? msg as string : null,
                sendTime = DbStore.Long(row, "sendtime"),
                quick = DbStore.Int(row, "type") == 1,
                item = item,
            });
        }
        return list;
    }

    // dueyitems → inventoryitems（包裹里最多一件，源端 getPackageFromDB 也是取第一条）。
    static InventoryEntry ItemOf(DbStore db, int packageId)
    {
        var link = db.Table("dueyitems").Find(x => DbStore.Int(x, "packageid") == packageId);
        if (link == null) return null;
        long rowId = DbStore.Long(link, "inventoryitemid");
        var row = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid", -1) == rowId);
        return row == null ? null : CharService.ToInventoryEntry(db, row);
    }

    static DueyResponse Send(DbStore db, CharRecord c, DueyRequest r)
    {
        int cid = (int)c.id;
        // GM 等级不足不能寄件（minimum_gm_level_to_use_duey 默认 4，非 GM 直接放行）。
        int minGm = GameConfigDao.GetInt(db, "minimum_gm_level_to_use_duey", 4);
        if (c.gm > 0 && c.gm < minGm)
            return new DueyResponse { ok = false, code = IncorrectRequest, error = "当前 GM 等级不能使用快递" };
        if (r.mesos < 0) return new DueyResponse { ok = false, code = IncorrectRequest, error = "金额不合法" };
        if (!string.IsNullOrEmpty(r.message) && r.message.Length > 100)
            return new DueyResponse { ok = false, code = IncorrectRequest, error = "留言过长" };

        int fee = GetFee(r.mesos);
        if (!r.quick) fee += 5000;
        else if (CountOf(db, cid, QuickDeliveryTicket) < 1)
            return new DueyResponse { ok = false, code = IncorrectRequest, error = "缺少快速投递券" };

        long finalcost = (long)r.mesos + fee;
        if (finalcost < 0 || finalcost > int.MaxValue || (r.itemQuantity < 1 && r.mesos == 0))
            return new DueyResponse { ok = false, code = IncorrectRequest, error = "请求不合法" };
        if (c.meso < finalcost)
            return new DueyResponse { ok = false, code = NotEnoughMesos, error = "金币不足", fee = fee };

        var targetRow = db.Table("characters").Find(x => string.Equals(DbStore.Str(x, "name"), r.recipient, StringComparison.OrdinalIgnoreCase));
        var target = targetRow == null ? null : CharRecord.FromRow(targetRow);
        if (target == null)
            return new DueyResponse { ok = false, code = NameDoesNotExist, error = "收件人不存在" };
        if ((int)target.accountid == c.accountid)
            return new DueyResponse { ok = false, code = SameAccError, error = "不能寄给自己账号" };

        if (r.quick) InventoryDao.ConsumeItem(db, cid, QuickDeliveryTicket, 1);

        int packageId = (int)db.Table("dueypackages").Insert(new DbRow
        {
            ["receiverid"] = target.id,
            ["sendername"] = c.name,
            ["mesos"] = (long)r.mesos,
            ["sendtime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["message"] = r.message ?? "",
            ["checked"] = 1L,
            ["type"] = r.quick ? 1L : 0L,
        });
        if (packageId <= 0) return new DueyResponse { ok = false, code = SendEnableActions, error = "创建包裹失败" };

        // 源端顺序就是「先建包 → 扣费 → 再装箱」，这里照抄（装箱失败不回滚金币）。
        c.meso -= finalcost;
        CharactersDao.Save(db, c);

        int res = AddPackageItemFromInventory(db, c, packageId, r);
        db.Save();
        if (res == 0)
            return new DueyResponse { ok = true, code = SuccessfullySent, fee = fee, meso = c.meso, inventory = CharService.Load(db, cid).inventory };
        if (res > 0)
            return new DueyResponse { ok = false, code = SendEnableActions, error = "包裹写入失败", fee = fee, meso = c.meso, inventory = CharService.Load(db, cid).inventory };
        return new DueyResponse { ok = false, code = IncorrectRequest, error = res == -1 ? "这件物品不能寄送" : "指定数量不足", fee = fee, meso = c.meso, inventory = CharService.Load(db, cid).inventory };
    }

    // DueyProcessor.addPackageItemFromInventory：0 成功 / 1 物品入库失败 / -1 不可寄 / -2 数量不足。
    static int AddPackageItemFromInventory(DbStore db, CharRecord c, int packageId, DueyRequest r)
    {
        if (r.inventoryType <= 0) return 0;
        int cid = (int)c.id;
        var row = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid
            && DbStore.Int(x, "inventorytype") == r.inventoryType
            && DbStore.Int(x, "position") == r.itemPosition
            && DbStore.Int(x, "position") > 0);
        if (row == null) return -2;

        var item = CharService.ToInventoryEntry(db, row);
        if (item.quantity < r.itemQuantity) return -2;
        if (IsUntradeable(db, item) || IsUnmerchable(db, item.itemId)) return -1;

        // 可充能弹药整组从背包扣走；但装箱数量仍取请求里的 amount（照抄源端
        // dueySendItem → addPackageItemFromInventory 的 setQuantity(amount) 行为）。
        int removeCount = Rechargeable(item.itemId) ? item.quantity : r.itemQuantity;
        if (removeCount >= item.quantity)
        {
            long rowId = DbStore.Long(row, "inventoryitemid");
            db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == rowId);
            db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == rowId);
        }
        else row["quantity"] = (long)(item.quantity - removeCount);

        var packed = item.Copy();
        ToggleKarmaFlagToUntradeable(packed);
        packed.quantity = r.itemQuantity;
        packed.position = 1;

        long newRow = InventoryDao.InsertItem(db, -1, packed.itemId, packed.inventoryType, 1, packed.quantity, packed.petId);
        var inserted = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid", -1) == newRow);
        if (inserted != null)
        {
            inserted["owner"] = packed.owner ?? "";
            inserted["flag"] = (long)packed.flag;
            inserted["expiration"] = packed.expiration;
        }
        if (packed.isEquip) InventoryDao.InsertEquipment(db, newRow);
        db.Table("dueyitems").Insert(new DbRow { ["packageid"] = (long)packageId, ["inventoryitemid"] = newRow });
        return 0;
    }

    static DueyResponse Claim(DbStore db, CharRecord c, DueyRequest r)
    {
        int cid = (int)c.id;
        var row = db.Table("dueypackages").Find(x => DbStore.Int(x, "packageid") == r.packageId);
        if (row == null) return new DueyResponse { ok = false, code = RecvUnknownError, error = "包裹不存在" };
        if (DbStore.Int(row, "receiverid") != cid)
            return new DueyResponse { ok = false, code = RecvUnknownError, error = "不是本人的包裹" };
        // isDeliveringTime：时间戳晚于当前 = 还在投递途中。
        if (DbStore.Long(row, "sendtime") > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            return new DueyResponse { ok = false, code = RecvUnknownError, error = "包裹还在投递中" };

        int mesos = DbStore.Int(row, "mesos");
        var item = ItemOf(db, r.packageId);
        if (item != null)
        {
            if (c.meso > int.MaxValue - mesos)
                return new DueyResponse { ok = false, code = RecvUnknownError, error = "金币已达上限" };
            var check = new InventoryChangeRequest { checkItems = new[] { item.itemId }, checkQuantities = new[] { item.quantity } };
            if (!NativeInventoryService.CanReceive(db, c, check))
            {
                // 源端判定：唯一道具（info/pickupRestricted）且背包里已有一件 → 专用提示。
                bool duplicate = IsPickupRestricted(item.itemId) && CountOf(db, cid, item.itemId) > 0;
                return new DueyResponse { ok = false, code = duplicate ? RecvReceiverWithUnique : RecvNoFreeSlots, error = duplicate ? "背包已有同一件唯一道具" : "背包空间不足" };
            }
            NativeInventoryService.AddItems(db, c, item.itemId, item.quantity, item.expiration, item.petId);
        }

        c.meso += mesos;
        CharactersDao.Save(db, c);
        DeletePackage(db, r.packageId);
        db.Save();
        return new DueyResponse { ok = true, meso = c.meso, packages = Load(db, cid), inventory = CharService.Load(db, cid).inventory };
    }

    static DueyResponse Remove(DbStore db, CharRecord c, DueyRequest r)
    {
        var row = db.Table("dueypackages").Find(x => DbStore.Int(x, "packageid") == r.packageId);
        if (row == null) return new DueyResponse { ok = true, packages = Load(db, (int)c.id) };
        if (DbStore.Int(row, "receiverid") != (int)c.id)
            return new DueyResponse { ok = false, code = RecvUnknownError, error = "不是本人的包裹" };
        DeletePackage(db, r.packageId);
        db.Save();
        return new DueyResponse { ok = true, packages = Load(db, (int)c.id) };
    }

    static void DeletePackage(DbStore db, int packageId)
    {
        var link = db.Table("dueyitems").Find(x => DbStore.Int(x, "packageid") == packageId);
        if (link != null)
        {
            long rowId = DbStore.Long(link, "inventoryitemid");
            db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == rowId);
            db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == rowId);
            db.Table("dueyitems").Delete(x => DbStore.Int(x, "packageid") == packageId);
        }
        db.Table("dueypackages").Delete(x => DbStore.Int(x, "packageid") == packageId);
    }

    // runDueyExpireSchedule：30 天前的包裹连同物品一起清掉。
    public static int Expire(DbStore db, long now)
    {
        long cutoff = now - 30L * 24 * 60 * 60 * 1000;
        var stale = db.Table("dueypackages").Select(x => DbStore.Long(x, "sendtime") < cutoff);
        foreach (var row in stale) DeletePackage(db, DbStore.Int(row, "packageid"));
        if (stale.Count > 0) db.Save();
        return stale.Count;
    }

    // 到件通知：源端 showDueyNotification 只对在线收件人发，并把 Checked 清零。
    public static DueyPackageData TakePendingNotification(DbStore db, int characterId)
    {
        var row = db.Table("dueypackages")
            .Select(x => DbStore.Int(x, "receiverid") == characterId && DbStore.Int(x, "checked") == 1)
            .Find(x => true);
        if (row == null) return null;
        db.Table("dueypackages").Update(x => DbStore.Int(x, "receiverid") == characterId, new DbRow { ["checked"] = 0L });
        db.Save();
        return new DueyPackageData
        {
            sender = DbStore.Str(row, "sendername"),
            quick = DbStore.Int(row, "type") == 1,
            packageId = DbStore.Int(row, "packageid"),
        };
    }

    static int CountOf(DbStore db, int characterId, int itemId)
        => InventoryDao.CountOf(db, characterId, itemId);
}
