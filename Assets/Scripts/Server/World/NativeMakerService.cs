using System;
using System.Collections.Generic;
using Server.Bot;
using Server.Bot.Trade;

// 制作（Maker）—— 对照 gms-server：
//   client/processor/action/MakerProcessor.java（makerAction：type 0 制作 / 3 怪物结晶合成 / 4 装备分解）
//   server/MakerItemFactory.java（getItemCreateEntry 的附加费用、trimCost、isInvalid）
//   server/ItemInformationProvider.java（getMakerItemEntry / getMakerStimulant / getMakerReagentStatUpgrade /
//                                        getMakerDisassembledFee / getMakerDisassembledItems /
//                                        improveEquipStats / scrollOptionEquipWithChaos / randomizeUpgradeStats）
//   net/server/channel/handlers/MakerSkillHandler.java（只转调 makerAction）
//
// 单机语义：
//   * 配方数据来自 Etc.wz/ItemMake.img（见 MakerTable.cs），与客户端 Maker 窗口同源；
//   * 「扣材料 → 扣金币 → 装箱」顺序照抄源端，中途失败不退还；
//   * 刺激剂 90% 判定失败时材料与金币照扣、成品不给（源端就是这样）；
//   * 分解与结晶合成这两条路径源端没有调用 trimCost，getCost() 恒为 0 —— 也就是「免费」。
//     这是源端的既有行为，不要顺手「修」成收手续费。
public enum MakerOperation { Open, Craft, Crystal, Disassemble }

public sealed class MakerRequest
{
    public int characterId;
    public MakerOperation operation;
    public int toCreate;        // type 0/3/4 的 toCreate（3 是原石 id，4 是装备 id）
    public int position;        // type 4 时装备所在格子
    public bool stimulant;      // type 0：是否使用刺激剂
    public int[] reagentIds;    // type 0：强化宝石（一个槽一个 id，可重复）
}

public sealed class MakerIngredient
{
    public int itemId, count, held;
}

public sealed class MakerRecipeView
{
    public int itemId, category, reqLevel, reqSkillLevel, itemNum, tuc, meso, catalyst;
    public List<MakerIngredient> reqItems = new List<MakerIngredient>();
}

public sealed class MakerResponse
{
    public bool ok = true;
    public string error = "";
    public short status;                 // 源端 getCreateStatus 的六个码，0 = 可以制作
    public bool craftSucceeded = true;   // addBoostedMakerItem 的返回（刺激剂 90% 判定）
    public long meso = -1;               // 操作后的金币
    public int level, makerLevel = -1;   // Open 时回传，客户端用来灰显配方
    public List<MakerRecipeView> recipes;
    public List<MakerIngredient> gained; // 本次产出（成功时给提示用）
    public List<InventoryEntry> inventory;
}

public static class NativeMakerService
{
    // MakerProcessor.getCreateStatus 的错误码
    public const short InvalidRecipe = -1;
    public const short MissingItems = 1;
    public const short MissingMeso = 2;
    public const short LowLevel = 3;
    public const short LowMakerLevel = 4;
    public const short InventoryFull = 5;

    const int ChaosScroll60 = 2049100;

    static readonly string[] StatKeys = { "STR", "DEX", "INT", "LUK", "MHP", "MMP", "PAD", "MAD", "PDD", "MDD", "ACC", "EVA", "Speed", "Jump" };
    static readonly string[] StatColumns = { "str", "dex", "int", "luk", "hp", "mp", "watk", "matk", "wdef", "mdef", "acc", "avoid", "speed", "jump" };

    /// <summary>刺激剂 / 混沌卷 / 宝石随机都用它；回归里换成固定种子保证可复现。</summary>
    public static Random Rng = new Random();

    // MakerItemFactory.MakerItemCreateEntry
    sealed class Entry
    {
        public double rawCost;
        public int reqCost;          // 只有 trimCost 之后才有值（源端 getCost 读的就是它）
        public int reqLevel, reqMakerLevel;
        public readonly List<MakerReqItem> reqItems = new List<MakerReqItem>();
        public readonly List<MakerReqItem> gainItems = new List<MakerReqItem>();
        public bool Invalid => reqLevel < 0;

        public void TrimCost()
        {
            reqCost = (int)(rawCost / 1000);
            reqCost *= 1000;
        }
    }

    // —————————————————— 入口 ——————————————————

    public static MakerResponse Handle(DbStore db, MakerRequest r)
    {
        var character = CharactersDao.FindById(db, r.characterId);
        if (character == null) return Fail("角色不存在");
        switch (r.operation)
        {
            case MakerOperation.Open: return Open(db, character);
            case MakerOperation.Craft: return Craft(db, character, r, 0);
            case MakerOperation.Crystal: return Craft(db, character, r, 3);
            case MakerOperation.Disassemble: return Craft(db, character, r, 4);
        }
        return Fail("未知的制作操作");
    }

    static MakerResponse Open(DbStore db, CharRecord c)
    {
        var views = new List<MakerRecipeView>();
        // 全量下发（含 category），由客户端按「全部 / 本职业」自己过滤。
        foreach (var recipe in MakerTable.All())
        {
            var view = new MakerRecipeView
            {
                itemId = recipe.itemId, category = recipe.category, reqLevel = recipe.reqLevel,
                reqSkillLevel = recipe.reqSkillLevel, itemNum = recipe.itemNum, tuc = recipe.tuc,
                meso = recipe.meso, catalyst = recipe.catalyst,
            };
            foreach (var req in recipe.reqItems)
                view.reqItems.Add(new MakerIngredient
                {
                    itemId = req.itemId, count = req.count,
                    held = InventoryDao.CountOf(db, (int)c.id, req.itemId),
                });
            views.Add(view);
        }
        return new MakerResponse
        {
            recipes = views, level = c.level, meso = c.meso,
            makerLevel = MakerSkillLevel(db, (int)c.id, c.job),
        };
    }

    // —————————————————— makerAction ——————————————————

    static MakerResponse Craft(DbStore db, CharRecord c, MakerRequest r, int type)
    {
        int characterId = (int)c.id;
        int toCreate = r.toCreate;
        int toDisassemble = -1, pos = -1;
        var reagents = new List<KeyValuePair<int, int>>();   // 有序；值是份数（去重后恒为 1）
        int stimulantId = -1;
        Entry recipe;

        if (type == 3)
        {
            // 怪物结晶合成：100 份原石 → 一颗结晶，档位按掉这只原石的怪的等级。
            int fromLeftover = toCreate;
            toCreate = MakerTable.CrystalFromLeftover(toCreate);
            if (toCreate == -1) return Fail(Name(fromLeftover) + " 无法转换为怪物结晶");
            recipe = new Entry { reqLevel = 0, reqMakerLevel = 1 };
            recipe.reqItems.Add(new MakerReqItem { itemId = fromLeftover, count = 100 });
            recipe.gainItems.Add(new MakerReqItem { itemId = toCreate, count = 1 });
        }
        else if (type == 4)
        {
            // 装备分解：源端只认「那一格上的装备 id 与 toCreate 相同」。
            pos = r.position;
            int equippedId = EquipIdAt(db, characterId, pos);
            if (equippedId == 0 || equippedId != toCreate) return Fail("无法对该物品进行分解");
            toDisassemble = toCreate;

            int fee = DisassembledFee(toCreate);
            var gains = DisassembledItems(toCreate);
            if (fee <= -1 || gains.Count == 0) return Fail(Name(toCreate) + " 无法分解为怪物结晶");
            recipe = new Entry { reqLevel = 0, reqMakerLevel = 1 };
            recipe.rawCost = fee;
            recipe.reqItems.Add(new MakerReqItem { itemId = toDisassemble, count = 1 });
            recipe.gainItems.AddRange(gains);
        }
        else
        {
            if (IsEquipment(toCreate))
            {
                if (r.stimulant)
                {
                    stimulantId = StimulantOf(toCreate);
                    if (!HasItem(db, characterId, stimulantId)) stimulantId = -1;
                }

                int slots = Math.Min(r.reagentIds != null ? r.reagentIds.Length : 0, ReagentSlots(toCreate));
                for (int i = 0; i < slots; i++)
                {
                    int reagentId = r.reagentIds[i];
                    if (!IsMakerReagent(reagentId)) continue;
                    int at = reagents.FindIndex(x => x.Key == reagentId);
                    if (at < 0) reagents.Add(new KeyValuePair<int, int>(reagentId, 1));
                    else reagents[at] = new KeyValuePair<int, int>(reagentId, reagents[at].Value + 1);
                }

                // 背包里不够的按实际持有量下调，没有的直接去掉。
                var adjust = new List<KeyValuePair<int, int>>();
                foreach (var pair in reagents)
                {
                    int held = InventoryDao.CountOf(db, characterId, pair.Key);
                    if (held < pair.Value) adjust.Add(new KeyValuePair<int, int>(pair.Key, held));
                }
                foreach (var pair in adjust)
                {
                    int at = reagents.FindIndex(x => x.Key == pair.Key);
                    if (at < 0) continue;
                    if (pair.Value > 0) reagents[at] = new KeyValuePair<int, int>(pair.Key, pair.Value);
                    else reagents.RemoveAt(at);
                }

                if (reagents.Count > 0 && !RemoveOddMakerReagents(db, toCreate, reagents))
                    return Fail("攻击力/魔力强化宝石只能用在武器装备上");
            }

            recipe = GetItemCreateEntry(db, toCreate, stimulantId, reagents);
        }

        short status = GetCreateStatus(db, c, recipe);
        var response = new MakerResponse
        {
            status = status, level = c.level, makerLevel = MakerSkillLevel(db, characterId, c.job),
            gained = new List<MakerIngredient>(),
        };
        if (status != 0)
        {
            response.ok = false;
            response.error = StatusText(toCreate, recipe, status);
            response.meso = c.meso;
            return response;
        }

        // —— 通过校验：先扣料 → 再扣金币 → 最后装箱（源端顺序，中途失败不退还）——
        if (toDisassemble != -1) RemoveEquipAt(db, characterId, pos);
        else foreach (var pair in recipe.reqItems) InventoryDao.ConsumeItem(db, characterId, pair.itemId, pair.count);

        int cost = recipe.reqCost;
        bool boosted = stimulantId != -1 || reagents.Count > 0;
        bool makerSucceeded = true;
        if (!boosted)
        {
            if (cost > 0) c.meso -= cost;
            foreach (var gain in recipe.gainItems)
            {
                if (gain.count <= 0) continue;
                GiveItem(db, c, gain.itemId, gain.count);
                response.gained.Add(new MakerIngredient { itemId = gain.itemId, count = gain.count });
            }
        }
        else
        {
            int output = recipe.gainItems[0].itemId;
            if (stimulantId != -1) InventoryDao.ConsumeItem(db, characterId, stimulantId, 1);
            foreach (var pair in reagents) InventoryDao.ConsumeItem(db, characterId, pair.Key, pair.Value);
            if (cost > 0) c.meso -= cost;
            makerSucceeded = AddBoostedMakerItem(db, c, output, stimulantId, reagents);
            if (makerSucceeded)
                response.gained.Add(new MakerIngredient { itemId = output, count = recipe.gainItems[0].count });
        }

        CharactersDao.Save(db, c);
        response.craftSucceeded = makerSucceeded;
        response.meso = c.meso;
        response.inventory = CharService.Load(db, characterId).inventory;
        return response;
    }

    // —————————————————— 配方与费用 ——————————————————

    /// <summary>UI / 自检用：走完附加费用后的真实报价（含刺激剂与宝石附加费，已 trimCost）。</summary>
    public static int QuoteCost(DbStore db, int itemId, bool stimulant, int[] reagentIds)
    {
        var reagents = new List<KeyValuePair<int, int>>();
        if (reagentIds != null)
            foreach (int reagentId in reagentIds)
            {
                if (!IsMakerReagent(reagentId)) continue;
                int at = reagents.FindIndex(x => x.Key == reagentId);
                if (at < 0) reagents.Add(new KeyValuePair<int, int>(reagentId, 1));
                else reagents[at] = new KeyValuePair<int, int>(reagentId, reagents[at].Value + 1);
            }
        int stimulantId = stimulant ? StimulantOf(itemId) : -1;
        var entry = GetItemCreateEntry(db, itemId, stimulantId, reagents);
        return entry.Invalid ? -1 : entry.reqCost;
    }

    // MakerItemFactory.getItemCreateEntry
    static Entry GetItemCreateEntry(DbStore db, int toCreate, int stimulantId, List<KeyValuePair<int, int>> reagents)
    {
        var entry = GetMakerItemEntry(toCreate);
        if (entry.Invalid) return entry;      // 不在配方表里，费用都不算

        // 刺激剂与宝石的附加费用，最后一起 trimCost 到 1000 的整数倍。
        if (stimulantId != -1) entry.rawCost += StimulantFee(db, toCreate);
        foreach (var pair in reagents)
            entry.rawCost += ReagentFee(db, toCreate, pair.Key % 10 + 1) * pair.Value;

        entry.TrimCost();
        return entry;
    }

    // ItemInformationProvider.getMakerItemEntry
    static Entry GetMakerItemEntry(int toCreate)
    {
        var recipe = MakerTable.Get(toCreate);
        var entry = new Entry();
        if (recipe == null)
        {
            entry.reqLevel = -1;
            entry.reqMakerLevel = -1;
            entry.rawCost = -1;
            entry.gainItems.Add(new MakerReqItem { itemId = toCreate, count = -1 });
            return entry;
        }
        entry.reqLevel = recipe.reqLevel;
        entry.reqMakerLevel = recipe.reqSkillLevel;
        entry.rawCost = recipe.meso;
        entry.gainItems.Add(new MakerReqItem { itemId = toCreate, count = recipe.itemNum });
        foreach (var req in recipe.reqItems) entry.reqItems.Add(req);
        return entry;
    }

    // MakerItemFactory.getMakerStimulantFee
    static double StimulantFee(DbStore db, int itemId)
    {
        if (!GameConfigDao.GetBool(db, "use_maker_fee_heuristics", true)) return 14000;
        int level = EquipLevelReq(itemId);
        switch (EquipFamily(itemId))
        {
            case 100: return 1145.736246 * Math.Exp(0.03336832546 * level);   // 帽子
            case 105: return 2117.469118 * Math.Exp(0.03355349137 * level);   // 套服
            case 107: return 1218.624674 * Math.Exp(0.0342266043 * level);    // 鞋
            case 108: return 2129.531152 * Math.Exp(0.03421778102 * level);   // 手套
            case 104: return 1770.630579 * Math.Exp(0.03359768677 * level);   // 上衣
            case 106: return 1442.98837 * Math.Exp(0.03444783295 * level);    // 裤裙
            case 109: return 6312.40136 * Math.Exp(0.0237929527 * level);     // 盾
            default: return 4313.581428 * Math.Exp(0.03147837094 * level);    // 武器
        }
    }

    // MakerItemFactory.getMakerReagentFee
    static double ReagentFee(DbStore db, int itemId, int reagentLevel)
    {
        if (!GameConfigDao.GetBool(db, "use_maker_fee_heuristics", true)) return 8000 * reagentLevel;
        int level = EquipLevelReq(itemId);
        switch (EquipFamily(itemId))
        {
            case 100: return 5592.01613 * Math.Exp(0.02914576018 * level) * reagentLevel;
            case 105: return 3405.23441 * Math.Exp(0.03413001038 * level) * reagentLevel;
            case 107: return 2115.697484 * Math.Exp(0.0354881705 * level) * reagentLevel;
            case 108: return 4684.040894 * Math.Exp(0.03166500585 * level) * reagentLevel;
            case 104: return 2955.89017 * Math.Exp(0.0339948456 * level) * reagentLevel;
            case 106: return 1774.722181 * Math.Exp(0.03854321409 * level) * reagentLevel;
            case 109: return 12014.11296 * Math.Exp(0.02185471162 * level) * reagentLevel;
            default: return 4538.650247 * Math.Exp(0.0371980303 * level) * reagentLevel;
        }
    }

    // EquipType.getEquipTypeById：武器按千位（1302 = 单手剑），其余按万位。
    static int EquipFamily(int itemId)
    {
        int val = itemId / 100000;
        return val == 13 || val == 14 ? itemId / 1000 : itemId / 10000;
    }

    static int EquipLevelReq(int itemId) => BotItemCatalog.Int(BotItemCatalog.Shared.Info(itemId), "reqLevel");

    // MakerProcessor.getMakerReagentSlots
    static int ReagentSlots(int itemId)
    {
        if (BotItemCatalog.Shared.Info(itemId) == null) return 0;
        int level = EquipLevelReq(itemId);
        if (level < 78) return 1;
        return level < 108 ? 2 : 3;
    }

    static int StimulantOf(int itemId)
    {
        var recipe = MakerTable.Get(itemId);
        return recipe != null ? recipe.catalyst : -1;
    }

    // ItemInformationProvider.getMakerDisassembledFee：原价的 13.6363%，向下截到 1000 的整数倍。
    static int DisassembledFee(int itemId)
    {
        var recipe = MakerTable.Get(itemId);
        if (recipe == null) return -1;
        float value = (float)(recipe.meso * 0.13636363636364);
        int fee = (int)(value / 1000);
        fee *= 1000;
        return fee;
    }

    // ItemInformationProvider.getMakerDisassembledItems：配方里点名的结晶退一半。
    static List<MakerReqItem> DisassembledItems(int itemId)
    {
        var gains = new List<MakerReqItem>();
        var recipe = MakerTable.Get(itemId);
        if (recipe == null) return gains;
        foreach (var req in recipe.reqItems)
            if (req.itemId >= 4260000 && req.itemId < 4270000)
                gains.Add(new MakerReqItem { itemId = req.itemId, count = req.count / 2 });
        return gains;
    }

    // MakerProcessor.removeOddMakerReagents
    static bool RemoveOddMakerReagents(DbStore db, int toCreate, List<KeyValuePair<int, int>> reagents)
    {
        var byType = new Dictionary<int, int>();
        var toRemove = new List<int>();
        bool isWeapon = IsWeapon(toCreate) || GameConfigDao.GetBool(db, "use_maker_permissive_atk_up", false);

        foreach (var pair in reagents)
        {
            int curRid = pair.Key;
            int type = pair.Key / 100;
            if (type < 42502 && !isWeapon) return false;   // 攻/魔强化宝石只能上武器
            if (byType.TryGetValue(type, out int tableRid))
            {
                // 同类型只留效果最高的那颗
                if (tableRid < curRid) { toRemove.Add(tableRid); byType[type] = curRid; }
                else toRemove.Add(curRid);
            }
            else byType[type] = curRid;
        }

        foreach (int id in toRemove) reagents.RemoveAll(x => x.Key == id);
        // Maker 技能每种宝石只会吃一颗
        for (int i = 0; i < reagents.Count; i++) reagents[i] = new KeyValuePair<int, int>(reagents[i].Key, 1);
        return true;
    }

    // —————————————————— 校验 ——————————————————

    // MakerProcessor.getCreateStatus
    static short GetCreateStatus(DbStore db, CharRecord c, Entry recipe)
    {
        if (recipe.Invalid) return InvalidRecipe;
        if (!HasItems(db, c, recipe)) return MissingItems;
        if (c.meso < recipe.reqCost) return MissingMeso;
        if (c.level < recipe.reqLevel) return LowLevel;
        if (MakerSkillLevel(db, (int)c.id, c.job) < recipe.reqMakerLevel) return LowMakerLevel;
        if (!CanHoldAllAfterRemoving(db, c, recipe)) return InventoryFull;
        return 0;
    }

    static bool HasItems(DbStore db, CharRecord c, Entry recipe)
    {
        foreach (var pair in recipe.reqItems)
            if (InventoryDao.CountOf(db, (int)c.id, pair.itemId) < pair.count) return false;
        return true;
    }

    // AbstractPlayerInteraction.canHoldAllAfterRemoving：先把要扣的道具从背包副本里去掉，再试放成品。
    static bool CanHoldAllAfterRemoving(DbStore db, CharRecord c, Entry recipe)
    {
        var full = CharService.Load(db, (int)c.id);
        if (full == null || full.inventory == null) return false;
        var actor = new BotActor { id = (int)c.id, job = c.job, character = full };
        var inventory = new List<InventoryEntry>();
        foreach (var item in full.inventory) inventory.Add(item.Copy());

        foreach (var req in recipe.reqItems)
        {
            int remaining = req.count;
            foreach (var item in inventory)
            {
                if (remaining <= 0) break;
                if (item.position <= 0 || item.itemId != req.itemId) continue;
                int take = Math.Min(remaining, item.quantity);
                item.quantity -= take;
                remaining -= take;
            }
            inventory.RemoveAll(i => i.quantity <= 0);
        }

        var rules = new TradeItemRules(db, BotItemCatalog.Shared);
        foreach (var gain in recipe.gainItems)
        {
            if (gain.itemId <= 0 || gain.count <= 0) continue;
            if (gain.itemId / 1000000 == 1 || gain.itemId / 1000 == 5000)
            {
                for (int i = 0; i < gain.count; i++)
                {
                    var offered = BotItemCatalog.Shared.CleanItem(gain.itemId, 1);
                    if (gain.itemId / 1000 == 5000) offered.petId = 0;
                    if (!rules.Receive(actor, inventory, offered)) return false;
                }
            }
            else if (!rules.Receive(actor, inventory, BotItemCatalog.Shared.CleanItem(gain.itemId, gain.count))) return false;
        }
        return true;
    }

    static string StatusText(int toCreate, Entry recipe, short status)
    {
        switch (status)
        {
            case InvalidRecipe: return "该物品无法用制作技能制作";
            case MissingItems: return "背包里缺少制作 " + Name(toCreate) + " 所需的材料";
            case MissingMeso: return "金币不足（需要 " + recipe.reqCost.ToString("N0") + "）";
            case LowLevel: return "等级不足，无法制作";
            case LowMakerLevel: return "制作技能等级不足";
            case InventoryFull: return "背包已满";
        }
        return "";
    }

    // MakerProcessor.getMakerSkillLevel：(job / 1000) * 10000000 + 1007
    public static int MakerSkillLevel(DbStore db, int characterId, int job)
    {
        int skillId = job / 1000 * 10000000 + 1007;
        foreach (var row in SkillsDao.ByCharacter(db, characterId))
            if (DbStore.Int(row, "skillid") == skillId) return DbStore.Int(row, "skilllevel");
        return 0;
    }

    // —————————————————— 产出入包 ——————————————————

    // MakerProcessor.addBoostedMakerItem：刺激剂 90% 判定 → 饰品补 3 次 → 宝石加属性 → 刺激剂随机化 → 装箱
    static bool AddBoostedMakerItem(DbStore db, CharRecord c, int itemId, int stimulantId, List<KeyValuePair<int, int>> reagents)
    {
        if (stimulantId != -1 && !RollSuccessChance(db, 90.0)) return false;

        var info = BotItemCatalog.Shared.Info(itemId);
        if (info == null) return false;
        var stats = BotItemCatalog.Shared.EquipStats(itemId) ?? new Dictionary<string, int>();
        int slots = BotItemCatalog.Int(info, "tuc");

        // 饰品没有升级次数时补 3 次（源端只在带宝石/刺激剂这条路上补）
        if (IsAccessory(itemId) && slots <= 0) slots = 3;

        if (GameConfigDao.GetBool(db, "use_enhanced_crafting", false))
        {
            bool perfect = c.gm > 0 && GameConfigDao.GetBool(db, "use_perfect_gm_scroll", true);
            if (!perfect) slots += 1;
            // scrollEquipWithId(CHAOS_SCROLL_60)：60% 成功走混沌，失败一样掉一次升级次数。
            var scroll = BotItemCatalog.Shared.EquipStats(ChaosScroll60);
            double chance = scroll != null && scroll.TryGetValue("success", out int s) ? s : 60;
            if (RollSuccessChance(db, chance))
            {
                int range = GameConfigDao.GetInt(db, "chaos_scroll_stat_range", 5);
                foreach (string key in StatKeys) ChaosRoll(db, stats, key, range);
            }
            slots -= 1;
        }

        if (reagents.Count > 0)
        {
            var flat = new Dictionary<string, int>();
            var randStat = new List<int>();
            var randOption = new List<int>();
            foreach (var pair in reagents)
            {
                var buff = MakerTable.Reagent(pair.Key);
                if (buff == null || string.IsNullOrEmpty(buff.stat) || buff.stat.Length < 4) continue;
                if (buff.stat.Substring(0, 4).Contains("rand"))
                {
                    if (buff.stat.Substring(4) == "Stat") randStat.Add(buff.value * pair.Value);
                    else randOption.Add(buff.value * pair.Value);
                }
                else
                {
                    string stat = buff.stat.Substring(3);
                    if (stat == "ReqLevel") continue;            // incReqLevel 不改进
                    if (stat == "MaxHP") stat = "MHP";
                    else if (stat == "MaxMP") stat = "MMP";
                    flat[stat] = (flat.TryGetValue(stat, out int cur) ? cur : 0) + buff.value * pair.Value;
                }
            }

            ImproveEquipStats(stats, flat);
            foreach (int range in randStat) ChaosOption(db, stats, range, false);     // 状态类
            foreach (int range in randOption) ChaosOption(db, stats, range, true);   // 攻防速类
        }

        if (stimulantId != -1) RandomizeUpgradeStats(stats);

        InsertEquip(db, c, itemId, stats, slots);
        return true;
    }

    // ItemInformationProvider.improveEquipStats（只做到 short 上限截断）
    static void ImproveEquipStats(Dictionary<string, int> stats, Dictionary<string, int> deltas)
    {
        foreach (var pair in deltas)
        {
            if (Array.IndexOf(StatKeys, pair.Key) < 0) continue;   // afterImage 等无属性字段直接跳过
            stats.TryGetValue(pair.Key, out int cur);
            stats[pair.Key] = (int)Math.Min(short.MaxValue, cur + pair.Value);
        }
    }

    // ItemInformationProvider.scrollOptionEquipWithChaos：option=true 只滚攻防速类，false 只滚状态类。
    static void ChaosOption(DbStore db, Dictionary<string, int> stats, int range, bool option)
    {
        string[] keys = option
            ? new[] { "PAD", "PDD", "MAD", "MDD", "Speed", "Jump", "MHP", "MMP" }
            : new[] { "STR", "DEX", "INT", "LUK", "ACC", "EVA" };
        foreach (string key in keys) ChaosRoll(db, stats, key, range);
    }

    static void ChaosRoll(DbStore db, Dictionary<string, int> stats, string key, int range)
    {
        if (!stats.TryGetValue(key, out int cur) || cur <= 0) return;
        int next = cur + Rng.Next(-range, range + 1);
        // 未开 use_enhanced_chaos_scroll 时负值截到 0（源端 getMaximumShortMaxIfOverflow(0, ...)）
        if (!GameConfigDao.GetBool(db, "use_enhanced_chaos_scroll", false) && next < 0) next = 0;
        stats[key] = (int)Math.Min(short.MaxValue, next);
    }

    // ItemInformationProvider.randomizeUpgradeStats
    static void RandomizeUpgradeStats(Dictionary<string, int> stats)
    {
        string[] small = { "STR", "DEX", "INT", "LUK", "MAD", "PAD", "ACC", "EVA", "Jump" };
        string[] large = { "PDD", "MDD", "MHP", "MMP" };
        foreach (string key in small) RandUpgradedStat(stats, key, 2);
        foreach (string key in large) RandUpgradedStat(stats, key, 5);
    }

    static void RandUpgradedStat(Dictionary<string, int> stats, string key, int maxRange)
    {
        if (!stats.TryGetValue(key, out int value) || value == 0) return;
        stats[key] = (int)(value + Math.Floor(Rng.NextDouble() * (maxRange + 1)));
    }

    // ItemInformationProvider.rollSuccessChance：按 scroll_chance_rolls 次数取最好的一次
    static bool RollSuccessChance(DbStore db, double percent)
        => Rng.NextDouble() >= Math.Pow(1 - percent / 100.0, GameConfigDao.GetInt(db, "scroll_chance_rolls", 1));

    static void InsertEquip(DbStore db, CharRecord c, int itemId, Dictionary<string, int> stats, int slots)
    {
        int position = NativeInventoryService.Free(db, c, 1);
        if (position == 0) throw new InvalidOperationException("Inventory capacity preflight mismatch");
        long id = InventoryDao.InsertItem(db, (int)c.id, itemId, 1, position, 1, -1);
        InventoryDao.InsertEquipment(db, id);
        var row = InventoryDao.EquipmentOf(db, id);
        for (int i = 0; i < StatColumns.Length; i++)
            row[StatColumns[i]] = (long)(short)(stats.TryGetValue(StatKeys[i], out int v) ? v : 0);
        row["upgradeslots"] = (long)slots;
        row["level"] = 0L;
        row["nativeStats"] = 1L;
    }

    // 普通过路（不带刺激剂/宝石）：装备按 IMG 基础属性新建，其余走通用装箱。
    static void GiveItem(DbStore db, CharRecord c, int itemId, int count)
    {
        if (itemId / 1000000 == 1)
        {
            var stats = BotItemCatalog.Shared.EquipStats(itemId) ?? new Dictionary<string, int>();
            InsertEquip(db, c, itemId, stats, BotItemCatalog.Int(BotItemCatalog.Shared.Info(itemId), "tuc"));
            return;
        }
        NativeInventoryService.AddItems(db, c, itemId, count, -1);
    }

    static bool HasItem(DbStore db, int characterId, int itemId)
        => itemId > 0 && InventoryDao.CountOf(db, characterId, itemId) > 0;

    static int EquipIdAt(DbStore db, int characterId, int position)
    {
        foreach (var row in InventoryDao.ItemsByCharacter(db, characterId))
            if (DbStore.Int(row, "inventorytype") == 1 && DbStore.Int(row, "position") == position && position > 0)
                return DbStore.Int(row, "itemid");
        return 0;
    }

    static void RemoveEquipAt(DbStore db, int characterId, int position)
    {
        foreach (var row in InventoryDao.ItemsByCharacter(db, characterId))
        {
            if (DbStore.Int(row, "inventorytype") != 1 || DbStore.Int(row, "position") != position || position <= 0) continue;
            long id = DbStore.Long(row, "inventoryitemid");
            db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == id);
            db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid", -1) == id);
            return;
        }
    }

    static string Name(int itemId) => BotItemCatalog.Shared.Name(itemId) ?? itemId.ToString();

    static MakerResponse Fail(string error) => new MakerResponse { ok = false, error = error };

    // —————————————————— 物品类别判定（ItemConstants） ——————————————————

    // isEquipment：装备 id 都小于 2000000 且不为 0
    public static bool IsEquipment(int itemId) => itemId < 2000000 && itemId != 0;

    // isWeapon：1302000 <= id < 1493000
    public static bool IsWeapon(int itemId) => itemId >= 1302000 && itemId < 1493000;

    // isAccessory：1110000 <= id < 1140000（戒指/吊坠/腰带）
    public static bool IsAccessory(int itemId) => itemId >= 1110000 && itemId < 1140000;

    // isMakerReagent：id / 10000 == 425
    public static bool IsMakerReagent(int itemId) => itemId / 10000 == 425;

    /// <summary>UI 用：这件装备能不能分解（源端要求「有分解费 + 配方里点了结晶」）。</summary>
    public static bool CanDisassemble(int itemId)
        => DisassembledFee(itemId) > -1 && DisassembledItems(itemId).Count > 0;

    /// <summary>UI 用：分解能拿回的结晶（数量已减半）。</summary>
    public static List<MakerReqItem> DisassemblyGains(int itemId) => DisassembledItems(itemId);

    /// <summary>UI 用：宝石槽位数（需求等级 &lt;78 → 1、&lt;108 → 2、否则 3）。</summary>
    public static int ReagentSlotsFor(int itemId) => ReagentSlots(itemId);

    /// <summary>UI 用：这件装备的刺激剂 id（ItemMake.img 的 catalyst，没有回 -1）。</summary>
    public static int StimulantFor(int itemId) => StimulantOf(itemId);
}
