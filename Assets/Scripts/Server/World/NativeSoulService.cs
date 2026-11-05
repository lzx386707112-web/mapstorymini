// 灵魂武器（开孔 + 镶魂）。权威服务，照 282 ItemHandler 两个 handler：
//   开孔 UserItemSkillSocketUpgradeItemUseRequest：
//     必须是武器，且 rLevel + iIncReq ≥ 75；消耗 2590xxx，按模板 ScrollStat.success 判成败；
//     成功写 soulSocketId = itemId % 2590000。
//   镶魂 UserItemSkillOptionUpdateItemUseRequest：
//     必须是武器且已有插槽；消耗 2591xxx；soulID = 1 + itemId % 2591000；
//     soulOptionId 存 soulID，soulOption 存属性代码（SoulCatalog 的代码表）。
// 灵魂能量（SoulMP）由 80001805 之类技能消耗，存在 character_growth.soulMp，上限 1000。
using System;
using System.Collections.Generic;

public enum SoulOperation { Preview = 0, Socket = 1, Enchant = 2, ReadModern = 10, ActivateModern, DeactivateModern, LevelModern, GradeModern, RollModern, CastModern }

public sealed class SoulRequest
{
    public int characterId;
    public long inventoryItemId, soulInventoryItemId;
    public SoulOperation operation;
    public bool commit;
    public long revision;
    public int amount;
}

public sealed class SoulResponse
{
    public bool ok = true, success;
    public string error = "", optionKey = "";
    public int socketId, soulId, soulItemId, optionCode, optionValue, soulMp, soulMax;
    public List<InventoryEntry> inventory;
    public SoulWeaponWz2State modern;
}

public static class NativeSoulService
{
    static SoulResponse Reject(string error) => new SoulResponse { ok = false, error = error };

    public static SoulResponse Handle(DbStore db, SoulRequest request, Random random = null)
    {
        if (db == null || request == null) return Reject("无效灵魂请求");
        if(request.operation>=SoulOperation.ReadModern)return NativeSoulWeaponWz2Service.Handle(db,request,random);
        if(NativeSoulWeaponWz2Service.Read(db,request.characterId).unlocked)return Reject("已切换为新版灵魂武器，请使用等级、升阶和烙印功能");
        random ??= new Random();
        lock (db)
        {
            var target = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == request.characterId
                && DbStore.Long(x, "inventoryitemid") == request.inventoryItemId);
            if (target == null) return Reject("没有持有这件装备");
            int itemId = DbStore.Int(target, "itemid"), position = DbStore.Int(target, "position");
            if (NativeAndroidService.IsApSlot(position)) return Reject("请先取下机器人换装部件");
            long expiration = DbStore.Long(target, "expiration", -1);
            if (expiration > 0 && expiration <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return Reject("装备已到期");
            var equipment = InventoryDao.EquipmentOf(db, request.inventoryItemId);
            if (equipment == null) return Reject("装备模板缺失");
            if (DbStore.Int(equipment, "locked") != 0 || (DbStore.Int(target, "flag") & 1) != 0) return Reject("装备已上锁");
            if (!IsWeapon(itemId)) return Reject("只有武器可以开孔或镶魂");

            var state = GrowthDao.Load(db, request.characterId);
            var response = Snapshot(equipment, state);
            if (request.operation == SoulOperation.Preview) return response;

            var soul = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == request.characterId
                && DbStore.Long(x, "inventoryitemid") == request.soulInventoryItemId);
            if (soul == null || DbStore.Int(soul, "quantity") < 1 || DbStore.Int(soul, "position") <= 0) return Reject("道具不存在");
            int soulItemId = DbStore.Int(soul, "itemid");

            return request.operation == SoulOperation.Socket
                ? Socket(db, request, equipment, soul, soulItemId, itemId, response, random)
                : Enchant(db, request, equipment, soul, soulItemId, ResolveOption(soulItemId, random), response);
        }
    }

    static SoulResponse Socket(DbStore db, SoulRequest request, DbRow equipment, DbRow soul, int soulItemId, int itemId, SoulResponse response, Random random)
    {
        if (soulItemId / 1000 != SoulCatalog.EnchanterBase / 1000) return Reject("这不是灵魂开孔器");
        if (DbStore.Int(equipment, "soulSocketId") != 0) return Reject("这件武器已经有灵魂插槽了");
        int reqLevel = Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(itemId) ?? new Dictionary<string, object>(), "reqLevel");
        int level = reqLevel + DbStore.Int(equipment, "iIncReq");
        if (level < SoulCatalog.MinLevel) return Reject("武器等级不足 " + SoulCatalog.MinLevel + " 级，无法开孔");
        response.success = false;
        if (!request.commit)
        {
            response.ok = true;
            return response;
        }
        // 成功率来自道具模板的 ScrollStat.success（282 ii.getScrollStats().get(ScrollStat.success)）。
        int chance = ScrollSuccess(soulItemId);
        bool success = chance >= 100 || random.Next(100) < chance;
        db.BeginTransaction();
        try
        {
            Consume(db, soul, request.soulInventoryItemId);
            if (success) equipment["soulSocketId"] = (long)(soulItemId % SoulCatalog.EnchanterBase);
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }
        response.success = success;
        response.socketId = DbStore.Int(equipment, "soulSocketId");
        response.inventory = CharService.Load(db, request.characterId).inventory;
        return response;
    }

    static SoulResponse Enchant(DbStore db, SoulRequest request, DbRow equipment, DbRow soul, int soulItemId, int optionOverride, SoulResponse response)
    {
        if (soulItemId / 1000 != SoulCatalog.SoulBase / 1000) return Reject("这不是灵魂石");
        if (DbStore.Int(equipment, "soulSocketId") == 0) return Reject("这件武器还没有灵魂插槽");
        int option = optionOverride;
        if (option <= 0 || SoulCatalog.Of(option) == null) return Reject("这颗灵魂石还没有属性数据");
        int soulId = SoulCatalog.SoulIdOf(soulItemId);
        if (!request.commit)
        {
            response.soulId = soulId; response.soulItemId = soulItemId; response.optionCode = option;
            ApplyOptionPreview(response, option);
            return response;
        }
        db.BeginTransaction();
        try
        {
            Consume(db, soul, request.soulInventoryItemId);
            equipment["soulOptionId"] = (long)soulId;
            equipment["soulItemId"] = (long)soulItemId;
            equipment["soulOption"] = (long)option;
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }
        response.soulId = soulId; response.soulItemId = soulItemId; response.optionCode = option;
        ApplyOptionPreview(response, option);
        response.inventory = CharService.Load(db, request.characterId).inventory;
        return response;
    }

    static SoulResponse Snapshot(DbRow equipment, GrowthState state)
    {
        int option = DbStore.Int(equipment, "soulOption");
        var response = new SoulResponse
        {
            socketId = DbStore.Int(equipment, "soulSocketId"),
            soulId = DbStore.Int(equipment, "soulOptionId"),
            soulItemId = DbStore.Int(equipment, "soulItemId"),
            optionCode = option,
            soulMp = state.soulMp, soulMax = state.soulMax,
        };
        ApplyOptionPreview(response, option);
        return response;
    }

    static void ApplyOptionPreview(SoulResponse response, int code)
    {
        var option = SoulCatalog.Of(code);
        if (option == null) return;
        response.optionKey = option.key;
        response.optionValue = option.value;
    }

    static void Consume(DbStore db, DbRow row, long inventoryItemId)
    {
        int quantity = DbStore.Int(row, "quantity");
        if (quantity <= 1) CharService.RemoveItem(db, inventoryItemId);
        else row["quantity"] = (long)quantity - 1;
    }

    // 灵魂石 → 属性代码：先查离线导出的名字推导表（472 条），查不到再按 282 的 soulPotList 兜底随机。
    // 282 对「名字没有属性前缀」的 Boss 灵魂珠就是这么处理的（ItemConstants.getSoulOptionFromSoul 末尾）。
    static int ResolveOption(int soulItemId, Random random)
    {
        int code = SoulCatalog.OptionFor(soulItemId);
        return code > 0 ? code : SoulCatalog.FallbackOption(random.Next(SoulCatalog.FallbackPotList.Length));
    }

    // 开孔成功率：2590xxx 的模板 success。取不到时按 100 处理（282 里成功的开孔器都是 100%）。
    static int ScrollSuccess(int soulItemId)
    {
        var info = Server.Bot.BotItemCatalog.Shared.Info(soulItemId);
        if (info == null) return 100;
        int success = Server.Bot.BotItemCatalog.Int(info, "success");
        return success <= 0 ? 100 : success;
    }

    public static bool IsWeapon(int itemId)
    {
        int prefix = itemId / 10000;
        return prefix >= 121 && prefix <= 160;
    }
}
