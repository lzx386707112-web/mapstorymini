// 徽章（祕法符文 ARC / 真實符文 AUT / 豪華真實符文 Grand）的权威服务。
//
// 与 282 对齐的三个操作（ItemHandler.handleUserArcaneSymbolEnchanceRequest 与
// handleUserSacredSymbolEnchanceRequest，两者逻辑同构，只有上限 20/11、费用表与 inc 封顶不同）：
//   MergeOne  type0：把背包里一件「普通徽章」（同 itemId、Lv1、exp 0 或 1）喂给已穿戴的同款 +1 exp
//   MergeAll  type2：把背包里所有符合条件的材料一次喂完，上限 = growthCost - exp
//   LevelUp   type1：exp ≥ growthCost 时升级，扣 282 客户端表的枫币，exp 余数保留，inc 重算，
//                    主属性 +100（ARC）/ +200（圣徽），恶魔复仇者 +HP、Xenon +三围
//
// 关键约束（照抄源端，别"优化"）：
//   - 1712000「通用祕法符文」不能合并也不能升级（两个 handler 都显式挡掉）
//   - 升级要求徽章处于「已穿戴」状态（源端取 getEquippedInventory），合并没有这个要求
//   - 费用用的是 282 客户端静态表（EquipSymbol.tms282MesoCost），不是 265 老公式
//   - growthCost 是「本级升下一级」的经验门槛，同时就是升级时扣掉的经验量
using System;

public enum SymbolOperation { Preview = 0, MergeOne = 1, MergeAll = 2, LevelUp = 3 }

public sealed class SymbolRequest
{
    public int characterId;
    // 目标徽章（要升级/要吃材料的那一件）
    public long inventoryItemId;
    // MergeOne 的材料；MergeAll 忽略
    public long materialInventoryItemId;
    public SymbolOperation operation;
    public bool commit;
}

public sealed class SymbolResponse
{
    public bool ok = true;
    public string error = "";
    public int itemId, level, exp, growthCost, inc, maxLevel;
    public long mesoCost, meso;
    public bool canLevelUp, equipped;
    public int mergedCount;
    public System.Collections.Generic.List<InventoryEntry> inventory;
}

public static class NativeSymbolService
{
    static SymbolResponse Reject(string error) => new SymbolResponse { ok = false, error = error };

    public static SymbolResponse Handle(DbStore db, SymbolRequest request)
    {
        if (db == null || request == null) return Reject("无效徽章请求");
        lock (db)
        {
            var character = CharactersDao.FindById(db, request.characterId);
            if (character == null) return Reject("角色不存在");
            var item = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == request.characterId
                && DbStore.Long(x, "inventoryitemid") == request.inventoryItemId);
            if (item == null) return Reject("没有持有这件徽章");
            int itemId = DbStore.Int(item, "itemid"); 
            var info = SymbolCatalog.Of(itemId);
            if (info == null) return Reject("这不是徽章");
            if (!SymbolCatalog.IsMergeable(itemId)) return Reject("通用祕法符文不能成长");
            int inventory = DbStore.Int(item, "inventorytype"), position = DbStore.Int(item, "position");
            if (NativeAndroidService.IsApSlot(position)) return Reject("请先取下机器人换装部件");
            bool equipped = position < 0;
            if (request.operation == SymbolOperation.LevelUp && !equipped) return Reject("请先穿上这件徽章再升级");
            long expiration = DbStore.Long(item, "expiration", -1);
            if (expiration > 0 && expiration <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return Reject("徽章已到期");
            if (DbStore.Int(item, "flag") != 0) return Reject("徽章已锁定");

            var equipment = InventoryDao.EquipmentOf(db, request.inventoryItemId);
            if (equipment == null)
            {
                equipment = InventoryDao.EnsureEquipment(db, request.inventoryItemId);
                if (equipment == null) return Reject("徽章模板缺失，无法恢复实例");
                db.Save();
            }
            // 282 登录归一：level==0 的徽章补成 Lv1 + inc + 起始主属性（Char.java:3058-3084）。
            if (DbStore.Int(equipment, "symbolLevel") == 0) Normalize(equipment, itemId, character.job);

            var response = Snapshot(db, character, equipment, info, equipped);
            if (request.operation == SymbolOperation.Preview) return response;

            if (request.operation == SymbolOperation.MergeOne || request.operation == SymbolOperation.MergeAll)
                return Merge(db, request, item, equipment, info, response);
            return LevelUp(db, request, character, item, equipment, info, response);
        }
    }

    // 材料合并：源端 type0（单件按装备格位置）与 type2（按 itemId 扫全背包）共用同一套材料判定。
    static SymbolResponse Merge(DbStore db, SymbolRequest request, DbRow item, DbRow equipment, SymbolInfo info, SymbolResponse response)
    {
        int level = DbStore.Int(equipment, "symbolLevel"), exp = DbStore.Int(equipment, "symbolExp");
        if (level < 1 || level >= info.maxLevel) return Reject("徽章已满级");
        int growth = info.GrowthCost(level);
        if (exp >= growth) return Reject("经验已满，请先升级");

        var materials = new System.Collections.Generic.List<DbRow>();
        if (request.operation == SymbolOperation.MergeOne)
        {
            if (request.materialInventoryItemId == request.inventoryItemId) return Reject("不能把徽章喂给自己");
            var material = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == request.characterId
                && DbStore.Long(x, "inventoryitemid") == request.materialInventoryItemId);
            if (material == null) return Reject("材料徽章不在背包里");
            if (!IsMaterial(db, request.characterId, material, DbStore.Int(item, "itemid"))) return Reject("材料必须是同款、1 级、未使用的徽章");
            materials.Add(material);
        }
        else
        {
            int need = growth - exp;
            foreach (var row in db.Table("inventoryitems").Select(x => DbStore.Int(x, "characterid") == request.characterId))
            {
                if (materials.Count >= need) break;
                if (DbStore.Long(row, "inventoryitemid") == request.inventoryItemId) continue;
                if (IsMaterial(db, request.characterId, row, DbStore.Int(item, "itemid"))) materials.Add(row);
            }
            if (materials.Count == 0) return Reject("背包里没有可用的同款材料徽章");
        }

        // 未提交时只报「会吃掉几件」，不动物品（与星力/潜能的预览口径一致）。
        if (!request.commit)
        {
            int preview = Math.Min(materials.Count, growth - exp);
            response.mergedCount = preview;
            response.growthCost = growth;
            response.canLevelUp = info.CanLevelUp(level, exp + preview);
            return response;
        }
        db.BeginTransaction();
        try
        {
            foreach (var material in materials) CharService.RemoveItem(db, DbStore.Long(material, "inventoryitemid"));
            int gained = Math.Min(materials.Count, growth - exp);
            equipment["symbolExp"] = (long)(exp + gained);
            db.Save();
            db.Commit();
            response.exp = exp + gained;
            response.mergedCount = gained;
        }
        catch { db.Rollback(); throw; }
        response.growthCost = growth;
        response.canLevelUp = info.CanLevelUp(level, response.exp);
        response.inventory = CharService.Load(db, request.characterId).inventory;
        return response;
    }

    static SymbolResponse LevelUp(DbStore db, SymbolRequest request, CharRecord character, DbRow item, DbRow equipment, SymbolInfo info, SymbolResponse response)
    {
        int level = DbStore.Int(equipment, "symbolLevel"), exp = DbStore.Int(equipment, "symbolExp");
        int growth = info.GrowthCost(level);
        if (!info.CanLevelUp(level, exp)) return Reject("经验不足，无法升级");
        long cost = SymbolCatalog.MesoCost(info.itemId, level);
        if (cost <= 0) return Reject("这件徽章没有可用的升级费用表");
        long meso = character.meso;
        response.mesoCost = cost;
        response.meso = meso;
        if (!request.commit) return response;
        if (meso < cost) return Reject("枫币不足");

        int nextLevel = level + 1;
        db.BeginTransaction();
        try
        {
            equipment["symbolLevel"] = (long)nextLevel;
            equipment["symbolExp"] = (long)Math.Max(0, exp - growth);
            equipment["symbolInc"] = (long)info.IncAt(nextLevel);
            ApplyMainStat(equipment, info, character.job);
            character.meso = meso - cost;
            CharactersDao.Save(db, character);
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }

        response.level = nextLevel;
        response.exp = Math.Max(0, exp - growth);
        response.inc = info.IncAt(nextLevel);
        response.meso = meso - cost;
        response.growthCost = info.GrowthCost(nextLevel);
        response.canLevelUp = info.CanLevelUp(nextLevel, response.exp);
        response.inventory = CharService.Load(db, request.characterId).inventory;
        return response;
    }

    static SymbolResponse Snapshot(DbStore db, CharRecord character, DbRow equipment, SymbolInfo info, bool equipped)
    {
        int level = DbStore.Int(equipment, "symbolLevel"), exp = DbStore.Int(equipment, "symbolExp");
        return new SymbolResponse
        {
            itemId = info.itemId, level = level, exp = exp, maxLevel = info.maxLevel,
            growthCost = info.GrowthCost(level), mesoCost = SymbolCatalog.MesoCost(info.itemId, level),
            inc = DbStore.Int(equipment, "symbolInc"), meso = character.meso,
            canLevelUp = info.CanLevelUp(level, exp), equipped = equipped,
        };
    }

    // 材料判定：同款、1 级、exp 为 0 或 1（源端 isPlainMergeMaterial）、在背包里（正位置）、未锁未过期。
    static bool IsMaterial(DbStore db, int characterId, DbRow row, int itemId)
    {
        if (DbStore.Int(row, "itemid") != itemId) return false;
        int position = DbStore.Int(row, "position");
        if (position <= 0 || NativeAndroidService.IsApSlot(position)) return false;
        if (DbStore.Long(row, "quantity") <= 0 || DbStore.Int(row, "flag") != 0) return false;
        long expiration = DbStore.Long(row, "expiration", -1);
        if (expiration > 0 && expiration <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return false;
        var equipment = InventoryDao.EquipmentOf(db, DbStore.Long(row, "inventoryitemid"));
        // 没有装备行的材料视为 1 级 0 经验（源端 plain merge material 的默认形态）。
        if (equipment == null) return true;
        return SymbolCatalog.IsMergeMaterial(DbStore.Int(equipment, "symbolLevel"), DbStore.Int(equipment, "symbolExp"));
    }

    // 282 Char.java:3058-3084 的首次归一：Lv1、inc 30/10、起始主属性 300/500；
    // 恶魔复仇者 4200（通用 1712000）/ 5250 / 10500 HP；Xenon 117 三项 / 240 三项。
    public static void Normalize(DbRow equipment, int itemId, int job)
    {
        var info = SymbolCatalog.Of(itemId);
        if (info == null) return;
        bool arcane = info.IsArcane;
        equipment["symbolLevel"] = 1L;
        if (DbStore.Int(equipment, "symbolExp") < 0) equipment["symbolExp"] = 0L;
        equipment["symbolInc"] = (long)info.initialInc;
        if (IsDemonAvenger(job))
            equipment["hp"] = (long)(arcane ? (itemId == 1712000 ? 4200 : 5250) : 10500);
        else if (IsXenon(job))
        {
            long value = arcane ? 117 : 240;
            equipment["str"] = value; equipment["dex"] = value; equipment["luk"] = value;
        }
        else
            equipment[MainStatColumn(job)] = (long)(arcane ? 300 : 500);
    }

    // 升级时的属性成长：普通职业只加主属性，恶魔复仇者加 HP，Xenon 三项同加。
    static void ApplyMainStat(DbRow equipment, SymbolInfo info, int job)
    {
        if (IsDemonAvenger(job))
            equipment["hp"] = DbStore.Long(equipment, "hp") + info.demonAvengerHpPerLevel;
        else if (IsXenon(job))
        {
            long gain = info.xenonPerLevel;
            equipment["str"] = DbStore.Long(equipment, "str") + gain;
            equipment["dex"] = DbStore.Long(equipment, "dex") + gain;
            equipment["luk"] = DbStore.Long(equipment, "luk") + gain;
        }
        else
        {
            string column = MainStatColumn(job);
            equipment[column] = DbStore.Long(equipment, column) + info.mainStatPerLevel;
        }
    }

    // 恶魔复仇者 = 3101（Demon Avenger），Xenon = 3600 系。两者都有独立的属性口径，
    // 不能按「职业群推主属性」处理。
    public static bool IsDemonAvenger(int job) => job == 3101 || job == 3102;
    public static bool IsXenon(int job) => job >= 3600 && job <= 3612;

    // 主属性列：job/100%10 → 1 战士 STR / 2 法师 INT / 3 弓手 DEX / 4 盗贼 LUK / 5 海盗 STR。
    // 冒险家、骑士团、英雄、反抗军都落在这里；认不出来按 STR 处理（宁可给错也不写坏存档列）。
    public static string MainStatColumn(int job)
    {
        switch (job / 100 % 10)
        {
            case 2: return "int";
            case 3: return "dex";
            case 4: return "luk";
            default: return "str";
        }
    }
}
