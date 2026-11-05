using System;
using System.Collections.Generic;
using Server.Bot;

public sealed class StarForceRequest
{
    public int characterId;
    public long inventoryItemId;
    public long sacrificeInventoryItemId;
    public bool commit, safeguard;
    public int revision;
}
public sealed class StarForceResponse
{
    public bool ok = true;
    public bool restored;
    public string error = "";
    public StarForceRules.Quote quote;
    public StarForceRules.Result result;
    public int revision, cap;
    public long meso;
    public Dictionary<string, int> derivedStats, nextStats;
    public List<InventoryEntry> inventory;
}

// Authoritative local-server endpoint. No caller-supplied attributes, probabilities, VIP level or costs.
public static class NativeStarForceService
{
    static readonly string[] Columns = { "str", "dex", "int", "luk", "hp", "mp", "watk", "matk", "wdef", "mdef", "acc", "avoid", "speed", "jump" };
    static readonly string[] WzKeys = { "incSTR", "incDEX", "incINT", "incLUK", "incMHP", "incMMP", "incPAD", "incMAD", "incPDD", "incMDD", "incACC", "incEVA", "incSpeed", "incJump" };
    static StarForceResponse Reject(string error) => new StarForceResponse { ok = false, error = error };
    public static StarForceResponse Handle(DbStore db, StarForceRequest request, StarForceRules rules = null, BotItemCatalog catalog = null, Random random = null)
    {
        if (db == null || request == null) return Reject("无效星力请求");
        lock (db)
        {
            var character = CharactersDao.FindById(db, request.characterId);
            if (character == null) return Reject("角色不存在");
            var item = db.Table("inventoryitems").Find(x => DbStore.Int(x,"characterid") == request.characterId
                && DbStore.Long(x,"inventoryitemid") == request.inventoryItemId);
            if (item == null) return Reject("没有持有这件装备");
            int inventory = DbStore.Int(item,"inventorytype"), position = DbStore.Int(item,"position");
            if (inventory != 1 && inventory != InventoryDao.Equipped || NativeAndroidService.IsApSlot(position)) return Reject("请使用背包或已穿戴的装备");
            long expiration = DbStore.Long(item,"expiration",-1);
            if (expiration > 0 && expiration <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return Reject("装备已到期");
            int itemId = DbStore.Int(item,"itemid"), category = itemId / 10000;
            if (!Upgradable(category) || category == 135) return Reject("这件装备不能进行星力强化");
            // Zero's paired weapon and restoration must be implemented as a single operation, never half-updated.
            if (category == 156 || category == 157) return Reject("零职业双武器强化链尚未接入");
            var equipment = InventoryDao.EquipmentOf(db, request.inventoryItemId);
            if (equipment == null)
            {
                equipment = InventoryDao.EnsureEquipment(db, request.inventoryItemId, catalog);
                if (equipment == null) return Reject("装备模板缺失，无法恢复装备实例");
                db.Save();
            }
            if(NativePotentialService.IsReserved(db,request.inventoryItemId))return Reject("请先选择方块候选结果");
            if (request.sacrificeInventoryItemId > 0) return Restore(db,request,item,equipment,rules,catalog);
            if (DbStore.Int(equipment,"starForceVestige") != 0) return Reject("装备痕迹需要先复原");
            if (DbStore.Int(equipment,"locked") != 0 || (DbStore.Int(item,"flag") & 1) != 0) return Reject("装备已上锁");
            if (DbStore.Int(equipment,"enhancementVersion") != 282 && (DbStore.Int(equipment,"starLevel") != 0 || !string.IsNullOrEmpty(DbStore.Str(equipment,"expandAttribute4"))))
                return Reject("旧强化装备尚未完成版本切换");
            int revision = DbStore.Int(equipment,"starForceRevision");
            if (request.commit && request.revision != revision) return Reject("装备状态已变化，请重新预览");
            catalog ??= BotItemCatalog.Shared;
            var info = catalog.Info(itemId);
            if (info == null || BotItemCatalog.Int(info,"cash") != 0 || BotItemCatalog.Int(info,"tuc") <= 0) return Reject("装备模板不支持星力强化");
            bool superior = BotItemCatalog.Int(info,"superiorEqp") != 0;
            int level = BotItemCatalog.Int(info,"reqLevel") + DbStore.Int(equipment,"iIncReq");
            int stars = DbStore.Int(equipment,"starLevel"), streak = DbStore.Int(equipment,"starCount");
            rules ??= StarForceRules.Shared;
            StarForceRules.Quote quote;
            try { quote = rules.Preview(itemId,level,stars,streak,superior,request.safeguard,DbStore.Int(equipment,"starForceProtection") != 0); }
            catch (ArgumentException error) { return Reject(error.Message); }
            var baseStats = BaseStats(equipment,info);
            var response = new StarForceResponse { quote = quote, revision = revision, meso = character.meso,
                cap = rules.Cap(itemId,level,superior),
                derivedStats = StarForceStats.Calculate(itemId,level,stars,superior,baseStats),
                nextStats = StarForceStats.Calculate(itemId,level,stars+1,superior,baseStats) };
            if (!request.commit) return response;
            if (character.meso < quote.cost) return Reject("枫币不足");
            int free = 0;
            for (int slot = 1; slot <= character.equipslots; slot++)
                if (db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Int(x,"inventorytype")==1 && DbStore.Int(x,"position")==slot) == null)
                { free = slot; break; }
            if (free == 0) return Reject("装备栏需要至少一个空位");
            var result = rules.Roll(stars,streak,superior,quote,(random ?? new Random()).Next(10000));
            db.BeginTransaction();
            try
            {
                // Preserve a pure base layer even for previously uninitialized equipment rows.
                foreach (var pair in baseStats) equipment[pair.Key] = (long)pair.Value;
                equipment["nativeStats"] = 1L;
                equipment["enhancementVersion"] = 282L;
                equipment["starLevel"] = (long)result.stars;
                equipment["starCount"] = (long)result.dropStreak;
                equipment["maxStar"] = (long)rules.Cap(itemId,level,superior);
                equipment["starForceVestige"] = result.vestige ? 1L : 0L;
                equipment["starForceRevision"] = checked((long)revision + 1);
                if (!request.safeguard) equipment["starForceProtection"] = 0L;
                if (result.vestige && inventory == InventoryDao.Equipped)
                { item["inventorytype"] = 1L; item["position"] = (long)free; }
                character.meso -= quote.cost;
                CharactersDao.Save(db,character);
                db.Save();
                db.Commit();
            }
            catch { db.Rollback(); throw; }
            response.result = result;
            response.revision = revision + 1;
            response.meso = character.meso;
            response.derivedStats = result.vestige ? new Dictionary<string,int>() : StarForceStats.Calculate(itemId,level,result.stars,superior,baseStats);
            response.inventory = CharService.Load(db,request.characterId).inventory;
            return response;
        }
    }
    // TMS282 ItemConstants.isUpgradable: BodyPart whitelist
    // (Ring1..4, Pendant/ExtendedPendant, Weapon, Belt, Hat, Face/EyeAccessory, Top(BodyPart.Top also
    // covers Overall), Bottom, Shoes, Earrings, Shoulder, Gloves, Badge, Shield, Cape, MechanicalHeart).
    // Medal(114)/PocketItem(116)/MonsterBook(117)/Emblem(119)/Totem(120)/SecondaryWeapon(135) and the
    // 161..166 machine parts are excluded by the source, so they must stay rejected here.
    static bool Upgradable(int category)
        => category >= 100 && category <= 113
        || category == 115
        || category == 118
        || category >= 121 && category <= 159 && category != 135
        || category == 167;
    // ItemHandler.TransmissionResult: ordinary vestige survives intact; consume a same-ID donor.
    static StarForceResponse Restore(DbStore db, StarForceRequest request, DbRow item, DbRow equipment, StarForceRules rules, BotItemCatalog catalog)
    {
        if (DbStore.Int(equipment,"starForceVestige") == 0) return Reject("这件装备不是装备痕迹");
        int revision = DbStore.Int(equipment,"starForceRevision");
        if (request.commit && request.revision != revision) return Reject("装备状态已变化，请重新预览");
        if (request.sacrificeInventoryItemId == request.inventoryItemId) return Reject("装备痕迹不能用来复原自身");
        var donor = db.Table("inventoryitems").Find(x=>DbStore.Int(x,"characterid")==request.characterId && DbStore.Long(x,"inventoryitemid")==request.sacrificeInventoryItemId);
        if (donor == null || DbStore.Int(donor,"itemid") != DbStore.Int(item,"itemid") || (DbStore.Int(donor,"inventorytype") != 1 && DbStore.Int(donor,"inventorytype") != InventoryDao.Equipped))
            return Reject("需要一件相同道具ID的装备进行复原");
        var donorEquipment = InventoryDao.EquipmentOf(db,request.sacrificeInventoryItemId);
        if(NativePotentialService.IsReserved(db,request.sacrificeInventoryItemId))return Reject("请先选择材料装备的方块候选结果");
        if (donorEquipment == null || DbStore.Int(donorEquipment,"starForceVestige") != 0 || DbStore.Int(donorEquipment,"locked") != 0 || (DbStore.Int(donor,"flag") & 1) != 0)
            return Reject("复原材料装备不可用");
        if (NativeAndroidService.IsApSlot(DbStore.Int(donor,"position"))) return Reject("请先取下机器人换装部件");
        long expiration=DbStore.Long(donor,"expiration",-1);
        if (expiration>0 && expiration<=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return Reject("复原材料装备已到期");
        var response = new StarForceResponse { revision=revision,meso=CharactersDao.FindById(db,request.characterId).meso };
        if (!request.commit) return response;
        db.BeginTransaction();
        try
        {
            db.Table("inventoryitems").Delete(x=>DbStore.Long(x,"inventoryitemid")==request.sacrificeInventoryItemId);
            db.Table("inventoryequipment").Delete(x=>DbStore.Long(x,"inventoryitemid")==request.sacrificeInventoryItemId);
            equipment["starForceVestige"]=0L;
            equipment["starForceRevision"]=checked((long)revision+1);
            db.Save(); db.Commit();
        }
        catch {db.Rollback();throw;}
        response.restored=true;
        response.revision=revision+1;
        response.inventory=CharService.Load(db,request.characterId).inventory;
        return response;
    }
    static Dictionary<string,int> BaseStats(DbRow row, Dictionary<string,object> info)
    {
        bool initialized = DbStore.Int(row,"nativeStats") != 0 || DbStore.Int(row,"level") != 0 || DbStore.Int(row,"vicious") != 0;
        foreach (string column in Columns) if (DbStore.Int(row,column) != 0) initialized = true;
        var result = new Dictionary<string,int>();
        for (int i=0;i<Columns.Length;i++) result[Columns[i]] = initialized ? DbStore.Int(row,Columns[i]) : BotItemCatalog.Int(info,WzKeys[i]);
        return result;
    }
}
