// 轮回星火（rebirth flame）。权威服务。
//
// 282 的流程（ItemHandler + Equip.flameTms282 / applyFlameLines）：
//   1. 从星火道具的 createType 取规则（6/7/8/12/20/21/0）
//   2. 按 lineWeights 摇行数；Boss 装备固定 4 行
//   3. 每行按 tierWeights 摇阶数；Boss 装备阶数 +2 且封顶 7
//   4. 每行摇词条（21 类，distinct：同一次星火里不重复），跳过不允许的（等级要求减免需 req≥5，
//      BOSS 伤害/伤害只在武器）
//   5. 数值按 FlameCatalog.Value 算（单属性/防御用 extendedLv，双属性用 lv，攻/魔走武器倍率）
//   6. 结果按 1000 进制打包进 exGradeOption
// 暗黑(Black)/深淵(Abyssal) 星火不直接生效，先进待确认状态，由前后对比窗选保留/替换（flameChoice journal）。
using System;
using System.Collections.Generic;

public sealed class FlameRequest
{
    public int characterId;
    public long inventoryItemId, flameInventoryItemId;
    public bool commit, choose, keepNew;
    public int revision;
}

public sealed class FlameLine
{
    public int statVal, exGrade, tier, value;
    public string label = "";
}

public sealed class FlameResponse
{
    public bool ok = true, pending;
    public string error = "";
    public int revision, lineCount, createType;
    // 当前生效的星火行（拆包后的可读形式）与本次摇出的候选行。
    public List<FlameLine> current = new List<FlameLine>();
    public List<FlameLine> candidate = new List<FlameLine>();
    public long exGradeOption;
    public List<InventoryEntry> inventory;
}

public static class NativeFlameService
{
    const int MaxTier = 7;
    static FlameResponse Reject(string error) => new FlameResponse { ok = false, error = error };

    public static bool IsReserved(DbStore db, long inventoryItemId)
    {
        var row = InventoryDao.EquipmentOf(db, inventoryItemId);
        return row != null && !string.IsNullOrEmpty(DbStore.Str(row, "flameChoice"));
    }

    public static FlameResponse Handle(DbStore db, FlameRequest request, Random random = null)
    {
        if (db == null || request == null) return Reject("无效星火请求");
        random ??= new Random();
        lock (db)
        {
            var target = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == request.characterId
                && DbStore.Long(x, "inventoryitemid") == request.inventoryItemId);
            if (target == null) return Reject("没有持有这件装备");
            int itemId = DbStore.Int(target, "itemid");
            int inventory = DbStore.Int(target, "inventorytype"), position = DbStore.Int(target, "position");
            if (inventory != InventoryDao.Equip && inventory != InventoryDao.Equipped) return Reject("请使用背包或已穿戴的装备");
            if (NativeAndroidService.IsApSlot(position)) return Reject("请先取下机器人换装部件");
            long expiration = DbStore.Long(target, "expiration", -1);
            if (expiration > 0 && expiration <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return Reject("装备已到期");
            if (!FlameCatalog.CanFlame(itemId)) return Reject("这件装备不能使用星火");
            var equipment = InventoryDao.EquipmentOf(db, request.inventoryItemId);
            if (equipment == null) return Reject("装备模板缺失");
            if (DbStore.Int(equipment, "locked") != 0 || (DbStore.Int(target, "flag") & 1) != 0) return Reject("装备已上锁");

            int revision = DbStore.Int(equipment, "flameRevision");
            if (request.choose)
            {
                if(request.commit && request.revision != revision) return Reject("装备状态已变化，请重新预览");
                return Choose(db, request, equipment, revision);
            }
            if (request.commit && request.revision != revision) return Reject("装备状态已变化，请重新预览");
            if (IsReserved(db, request.inventoryItemId)) return Reject("请先选择上一次星火的结果");

            var flame = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == request.characterId
                && DbStore.Long(x, "inventoryitemid") == request.flameInventoryItemId);
            if (flame == null || DbStore.Int(flame, "quantity") < 1 || DbStore.Int(flame, "position") <= 0) return Reject("星火道具不存在");
            int flameId = DbStore.Int(flame, "itemid");
            int createType = FlameCatalog.CreateTypeOf(flameId);
            if (createType < 0) return Reject("这个道具不是星火");
            var rule = FlameCatalog.Rule(createType);
            if (rule == null) return Reject("这种星火还没有规则数据");

            var info = Server.Bot.BotItemCatalog.Shared.Info(itemId);
            int reqLevel = info == null ? 0 : Server.Bot.BotItemCatalog.Int(info, "reqLevel");
            int incReq = DbStore.Int(equipment, "iIncReq");
            bool weapon = itemId / 10000 >= 121 && itemId / 10000 <= 160;
            // Boss 装备（模板 bossReward）固定 4 行、阶数 +2——282 Equip.isBossReward 读的是模板，不是实例列。
            bool bossWeapon = info != null && Server.Bot.BotItemCatalog.Int(info, "bossReward") != 0;
            int baseAttack = info == null ? 0 : Math.Max(Server.Bot.BotItemCatalog.Int(info, "incPAD"), Server.Bot.BotItemCatalog.Int(info, "incMAD"));

            var response = new FlameResponse
            {
                revision = revision, createType = createType,
                exGradeOption = DbStore.Long(equipment, "exGradeOption"),
            };
            response.current = Describe(target, equipment, response.exGradeOption);
            if (!request.commit)
            {
                response.candidate = Roll(rule, reqLevel, incReq, weapon, bossWeapon, baseAttack, random);
                response.lineCount = response.candidate.Count;
                return response;
            }
            if (!FlameCatalog.IsChoice(flameId) && IsReserved(db, request.inventoryItemId)) return Reject("请先选择上一次星火的结果");

            var lines = Roll(rule, reqLevel, incReq, weapon, bossWeapon, baseAttack, random);
            bool choice = FlameCatalog.IsChoice(flameId);
            db.BeginTransaction();
            try
            {
                int quantity = DbStore.Int(flame, "quantity");
                if (quantity <= 1) db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid") == request.flameInventoryItemId);
                else flame["quantity"] = (long)quantity - 1;
                long packed = Pack(lines);
                if (choice) equipment["flameChoice"] = Serialize(lines);
                else equipment["exGradeOption"] = packed;
                equipment["flameRevision"] = checked((long)revision + 1);
                db.Save();
                db.Commit();
                response.pending = choice;
                if (!choice) response.exGradeOption = packed;
            }
            catch { db.Rollback(); throw; }
            response.revision = revision + 1;
            response.candidate = lines;
            response.lineCount = lines.Count;
            if (!response.pending) response.current = lines;
            response.inventory = CharService.Load(db, request.characterId).inventory;
            return response;
        }
    }

    static FlameResponse Choose(DbStore db, FlameRequest request, DbRow equipment, int revision)
    {
        string pending = DbStore.Str(equipment, "flameChoice");
        if (string.IsNullOrEmpty(pending)) return Reject("没有待确认的星火结果");
        var target = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == request.inventoryItemId);
        var lines = Describe(target, equipment, long.TryParse(pending, out var packed) ? packed : 0);
        var response = new FlameResponse
        {
            revision = revision, pending = true, candidate = lines, lineCount = lines.Count,
            exGradeOption = DbStore.Long(equipment, "exGradeOption"),
        };
        response.current = Describe(target, equipment, response.exGradeOption);
        if (!request.commit) return response;
        db.BeginTransaction();
        try
        {
            if (request.keepNew) equipment["exGradeOption"] = Pack(lines);
            equipment["flameChoice"] = "";
            equipment["flameRevision"] = checked((long)revision + 1);
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }
        response.revision = revision + 1;
        response.pending = false;
        response.exGradeOption = DbStore.Long(equipment, "exGradeOption");
        response.current = Describe(target, equipment, response.exGradeOption);
        response.inventory = CharService.Load(db, request.characterId).inventory;
        return response;
    }

    // 打包只保存词条和阶数，预览/恢复必须按同一装备模板补回数值。
    static List<FlameLine> Describe(DbRow target, DbRow equipment, long packed)
    {
        return NativeGrowthStats.DescribeFlame(new InventoryEntry
        {
            itemId = DbStore.Int(target, "itemid"), exGradeOption = packed,
            equipmentData = new Dictionary<string, object> { ["iIncReq"] = DbStore.Int(equipment, "iIncReq") },
        });
    }

    // 摇一次：行数 → 每行阶数 → 每行词条（不重复）→ 数值。Boss 装备 4 行、阶数 +2 封顶 7。
    public static List<FlameLine> Roll(FlameRule rule, int reqLevel, int incReq, bool weapon, bool bossWeapon, int baseAttack, Random random)
    {
        var lineWeights = new FlameCatalog.Weights(rule.lineWeights);
        var tierWeights = new FlameCatalog.Weights(rule.tierWeights);
        int lines = bossWeapon ? FlameCatalog.BossLines : lineWeights.Pick(random.Next(lineWeights.total));
        var result = new List<FlameLine>();
        var used = new HashSet<int>();
        int guard = 0;
        while (result.Count < lines && guard++ < 200)
        {
            int statVal = random.Next(FlameStats.RollableCount);
            if (!used.Add(statVal)) continue;
            if (!FlameCatalog.Allowed(statVal, weapon, reqLevel, incReq)) { used.Remove(statVal); continue; }
            int tier = tierWeights.Pick(random.Next(tierWeights.total));
            if (bossWeapon) tier = Math.Min(MaxTier, tier + FlameCatalog.BossTierShift);
            var def = FlameStats.ByVal(statVal);
            if (def == null) continue;
            result.Add(new FlameLine
            {
                statVal = statVal, exGrade = def.exGrade, tier = tier, label = def.label,
                value = FlameCatalog.Value(statVal, tier, reqLevel, incReq, weapon, bossWeapon, baseAttack),
            });
        }
        return result;
    }

    // exGradeOption：每行 exGrade*10+tier，按 1000 进制依次累加（282 Equip 同款打包，勿拆列）。
    public static long Pack(List<FlameLine> lines)
    {
        long packed = 0, factor = 1;
        foreach (var line in lines)
        {
            packed += (long)(line.exGrade * 10 + line.tier) * factor;
            factor *= 1000;
        }
        return packed;
    }

    public static List<FlameLine> Unpack(long packed)
    {
        var result = new List<FlameLine>();
        long rest = packed;
        while (rest > 0)
        {
            int token = (int)(rest % 1000);
            rest /= 1000;
            if (token == 0) continue;
            int exGrade = token / 10, tier = token % 10;
            var def = FlameStats.ByExGrade(exGrade);
            result.Add(new FlameLine
            {
                exGrade = exGrade, tier = tier, statVal = def == null ? -1 : def.val,
                label = def == null ? "" : def.label,
            });
        }
        return result;
    }

    static string Serialize(List<FlameLine> lines) => Pack(lines).ToString(System.Globalization.CultureInfo.InvariantCulture);

    static List<FlameLine> Deserialize(string packed)
        => long.TryParse(packed, out var value) ? Unpack(value) : new List<FlameLine>();
}
