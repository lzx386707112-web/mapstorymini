// 282 成长系统的「属性结算」层：徽章 / 星火 / 极限属性 / 内在能力 / 灵魂。
//
// 282 服务端本身不算这些属性：
//   - 极限属性只是把 800004xx 当技能给角色加等级（UserStatHandler:414-416），
//   - 内在能力只存 CharacterPotential，改都不改属性，
//   - 星火只往 exGradeOption 里打包，灵魂只写 soulSocketId/soulOptionId。
// 真正的数值由客户端读 7000xxx 的 common 公式算出来。本项目没有那套公式引擎，
// 所以结算放在派生属性层：这里按同一份公式表（HyperStatCatalog / AbilityCatalog /
// FlameCatalog / SoulCatalog）把差额算成「套装差量字典」，由 EquipLoadout.Apply
// 并进面板——和套装加成走同一条路，(base+flat)×(100+pct)/100 的语义也自然一致。
//
// 口径约定（与 282 一致）：
//   1. 只有三个「当前预设」生效（hyperPreset / abilityPreset），其它预设只在窗口里看。
//   2. 内在能力按等级解锁的行数生效（UnlockedLines），锁定的行照样生效。
//   3. 星火只认已穿戴的装备；待确认（flameChoice 非空）期间旧结果仍在生效。
//   4. 徽章的 inc 是 ARC/AUT 力量，不并进 STR/DEX，单独出字段给伤害公式用。
//   5. 282 有定义但本项目没有通道的条目（四维互转、加持时间、塔伤害…）不结算，
//      记进 unsettled 供窗口显示——用户口径：「保持和 282 一样」，即保留条目与数值。
using System;
using System.Collections.Generic;

public sealed class GrowthStatDelta
{
    // 键名沿用 AdminSetItem.SUPPORTED_STAT_KEYS（STR/DEX/INT/LUK/PAD/MAD/PDD/MDD/ACC/EVA/
    // SPD/JMP/HP/MP/Damage/BossDamage/NormalDamage/ExpRate/DropRate/MesoRate + *Pct）。
    public readonly Dictionary<string, int> set = new Dictionary<string, int>();
    // 暴击率/暴击伤害不吃「百分比叠加」，直接进 CharacterAttributes 的 float 字段。
    public float criticalChance, criticalBonus;
    public float hexaDamagePercent,hexaBossPercent,hexaIgnorePercent;
    // ARC / AUT 力量：不参与四维，走独立的伤害减免/命中口径。
    public int arcaneForce, authenticForce;
    // 有定义但本项目没有结算通道的条目（"力量轉換為敏捷 +3" 之类），窗口按原样列出。
    public readonly List<string> unsettled = new List<string>();

    public void Add(string key, int value)
    {
        if (string.IsNullOrEmpty(key) || value == 0) return;
        set[key] = (set.TryGetValue(key, out int had) ? had : 0) + value;
    }

    public int Get(string key) => set.TryGetValue(key, out int value) ? value : 0;
}

public static class NativeGrowthStats
{
    // 能力值详情窗每帧都要面板，而结算要遍历背包并查道具表。这里按「内容指纹」做一层缓存：
    // 指纹是 O(n) 的整数运算（零分配），比每次重建 Dictionary + 查道具表便宜得多，
    // 而且不需要每个改动 inventory 的调用点都记得手动失效——漏一个就会显示旧面板。
    // 返回的 delta 只读，调用方不得改写。
    static FullCharacter cachedOwner;
    static int cachedFingerprint;
    static GrowthStatDelta cachedDelta;

    public static GrowthStatDelta Accumulate(FullCharacter character, int level, int job)
    {
        if (character == null) return new GrowthStatDelta();
        int fingerprint = Fingerprint(character, level, job);
        if (ReferenceEquals(character, cachedOwner) && fingerprint == cachedFingerprint && cachedDelta != null)
            return cachedDelta;

        var delta = new GrowthStatDelta();
        var growth = character.growth ?? new GrowthState();
        AccumulateEquipment(character, delta);
        AccumulateHyperStats(character, growth, level, delta);
        AccumulatePotentials(character, growth, level, delta);
        AccumulateHexa(character,job,delta);
        AccumulateSoulWeapon(character,delta);
        cachedOwner = character; cachedFingerprint = fingerprint; cachedDelta = delta;
        return delta;
    }

    /// <summary>
    /// swapSlot/swapEntry：把那格换成这件再结算一次（提示框「战斗力提升量」用）。
    /// 与缓存版同一个口径，只是不走指纹缓存——预览每帧都要，但换了装备就换结果，
    /// 缓存反而要额外记 swap 参数。
    /// </summary>
    public static GrowthStatDelta Accumulate(FullCharacter character, int level, int job, int swapSlot, InventoryEntry swapEntry)
    {
        if (character == null) return new GrowthStatDelta();
        if (swapSlot == 0 || swapEntry == null) return Accumulate(character, level, job);
        var delta = new GrowthStatDelta();
        var growth = character.growth ?? new GrowthState();
        AccumulateEquipment(character, delta, swapSlot, swapEntry);
        AccumulateHyperStats(character, growth, level, delta);
        AccumulatePotentials(character, growth, level, delta);
        AccumulateHexa(character, job, delta);
        AccumulateSoulWeapon(character, delta);
        return delta;
    }

    /// <summary>强制丢弃缓存（内容指纹已经能自动覆盖，这里只给「怀疑缓存错了」的场景兜底）。</summary>
    public static void Invalidate() { cachedOwner = null; cachedDelta = null; }

    // 只把「会影响结算」的字段揉进指纹：等级/职业、当前预设、背包里每件的实例号与
    // 徽章/星火/灵魂/主属性数值、两类加点行。位置也算，因为换装会改 position。
    static int Fingerprint(FullCharacter character, int level, int job)
    {
        unchecked
        {
            int h = level * 397 ^ job;
            var soul=character.soulWeapon;
            if(soul!=null){var bound=soul.Active;
                h=h*31+(int)soul.revision;h=h*31+(bound?.level??0);h=h*31+(bound?.option??0);
                h=h*31+(soul.activeUntil>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()?1:0);}
            var growth = character.growth;
            if (growth != null) h = h * 31 + growth.hyperPreset * 7 + growth.abilityPreset;
            var inventory = character.inventory;
            if (inventory != null)
            {
                h = h * 31 + inventory.Count;
                for (int i = 0; i < inventory.Count; i++)
                {
                    var item = inventory[i];
                    if (item == null) continue;
                    h = h * 31 + (int)item.itemRowId;
                    h = h * 31 + item.position;
                    h = h * 31 + item.itemId;
                    h = h * 31 + item.symbolLevel;
                    h = h * 31 + item.symbolInc;
                    h = h * 31 + item.str + item.dex * 3 + item.int_ * 5 + item.luk * 7 + item.hp * 11 + item.mp * 13;
                    long flame = item.exGradeOption;
                    h = h * 31 + (int)flame + (int)(flame >> 32);
                    h = h * 31 + item.soulOption;
                }
            }
            var hyper = character.hyperstats;
            if (hyper != null)
            {
                h = h * 31 + hyper.Count;
                for (int i = 0; i < hyper.Count; i++)
                {
                    var line = hyper[i];
                    if (line == null) continue;
                    h = h * 31 + line.preset * 7 + line.skillId * 3 + line.slv;
                }
            }
            var potentials = character.potentials;
            if (potentials != null)
            {
                h = h * 31 + potentials.Count;
                for (int i = 0; i < potentials.Count; i++)
                {
                    var row = potentials[i];
                    if (row == null) continue;
                    h = h * 31 + row.preset * 7 + row.potKey * 3 + row.skillId * 5 + row.slv;
                }
            }
            if(character.hexaStats!=null) foreach(var row in character.hexaStats)
            {
                if(row==null) continue;
                h=h*31+row.coreId+row.preset*3+(row.applied ? 7 : 0);
                h=h*31+row.stat1+row.stat2*7+row.stat3*49;
                h=h*31+row.level1+row.level2*11+row.level3*121;
            }
            return h;
        }
    }

    static void AccumulateSoulWeapon(FullCharacter character,GrowthStatDelta delta)
    {
        var soul=character.soulWeapon;if(soul==null || !soul.unlocked || !soul.Equipped(character))return;
        var bound=soul.Active??new SoulGrowth();
        int attack=SoulWeaponWz2Catalog.PassiveAttack(bound.level);delta.Add("PAD",attack);delta.Add("MAD",attack);
        foreach(var stat in PotentialStats.CalculateValues(SoulWeaponWz2Catalog.OptionStats(bound.option)))
            if(stat.Key=="CriticalChance")delta.criticalChance+=stat.Value/100f;
            else delta.Add(stat.Key,stat.Value);
        if(soul.activeUntil>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {int rate=SoulWeaponWz2Catalog.ActiveAttackPercent(bound.grade);delta.Add("PADPct",rate);delta.Add("MADPct",rate);}
    }

    static void AccumulateHexa(FullCharacter character,int job,GrowthStatDelta delta)
    {
        if(character.hexaStats==null) return;
        foreach(var row in character.hexaStats)
        {
            if(row==null || !row.applied || row.level!=row.level1+row.level2+row.level3) continue;
            ApplyHexa(row.stat1,row.level1,true,job,delta);
            ApplyHexa(row.stat2,row.level2,false,job,delta);
            ApplyHexa(row.stat3,row.level3,false,job,delta);
        }
    }
    static void ApplyHexa(int type,int level,bool main,int job,GrowthStatDelta delta)
    {
        int value=HexaCatalog.StatValue(type,level,main);
        switch(type)
        {
            case 0: delta.criticalBonus+=value/10000f; break;
            case 1: delta.hexaBossPercent+=value/100f; break;
            case 2: delta.hexaIgnorePercent+=value/100f; break;
            case 3: delta.hexaDamagePercent+=value/100f; break;
            case 4: delta.Add("PAD",value); break;
            case 5: delta.Add("MAD",value); break;
            // 2026-11-04 用户批准的职业换算；不是已核实 EXE 公式。
            case 6:
                if(job/100==36) { delta.Add("STR",value*40); delta.Add("DEX",value*40); delta.Add("LUK",value*40); }
                else delta.Add(HexaCatalog.MainStat(job),HexaCatalog.MainStatValue(value,job));
                break;
        }
    }

    // ——— 徽章 / 星火 / 灵魂：都挂在 inventoryequipment 行上 ———
    static void AccumulateEquipment(FullCharacter character, GrowthStatDelta delta, int swapSlot = 0, InventoryEntry swapEntry = null)
    {
        if (character.inventory != null)
            foreach (var item in character.inventory)
            {
                if (item == null || item.position >= 0 || item.quantity <= 0) continue;
                // 换装预览：这一格原来的那件被换掉了，它的星火/灵魂/徽章加成不能算。
                if (swapSlot != 0 && item.position == swapSlot) continue;
                AccumulateWornItem(item, character, delta, item.position);
            }
        // 悬停的那件按「已穿上」的语义补进来（背包里 position>=0，上面的循环收不到）。
        if (swapEntry != null && swapEntry.quantity > 0) AccumulateWornItem(swapEntry, character, delta, swapSlot);
    }

    static void AccumulateWornItem(InventoryEntry item, FullCharacter character, GrowthStatDelta delta, int position)
    {
        // 徽章：槽位本身按负数段区分（-201..-222），不吃 Weapons/防具那套 Worn 表。
        if (position < 0 && SymbolCatalog.IsSymbolSlot(position))
        {
            var symbol = SymbolCatalog.Of(item.itemId);
            if (symbol == null) return;
            if (symbol.IsArcane) delta.arcaneForce += item.symbolInc;
            else delta.authenticForce += item.symbolInc;
            if (position == SymbolCatalog.SlotOf(item.itemId))
                foreach (string rate in new[] { "ExpRate", "MesoRate", "DropRate" })
                    delta.Add(rate, SymbolCatalog.GrandBonus(item.itemId, item.symbolLevel, rate));
            // 主属性/HP 是升级时直接写进装备行的（EquipSymbol.setInc 同口径），按 flat 叠加。
            delta.Add("STR", item.str); delta.Add("DEX", item.dex);
            delta.Add("INT", item.int_); delta.Add("LUK", item.luk);
            delta.Add("HP", item.hp); delta.Add("MP", item.mp);
            return;
        }

        // 星火：exGradeOption 里每行只有 exGrade+tier，数值要按装备等级现算。
        if (item.exGradeOption > 0) AccumulateFlame(item, delta);

        // 灵魂：soulOption 是属性代码，SoulCatalog 给出键与值。
        if (item.soulOption > 0 && (character.soulWeapon == null || !character.soulWeapon.unlocked))
            ApplySoul(item.soulOption, delta);
    }

    static void AccumulateFlame(InventoryEntry item, GrowthStatDelta delta)
    {
        foreach (var line in DescribeFlame(item))
        {
            if (line.statVal < 0 || line.value <= 0) continue;
            ApplyFlameStat(line.statVal, line.value, delta);
        }
    }

    /// <summary>
    /// 拆出这件装备身上的星火行，并把数值按装备等级算好（tooltip 与结算层共用同一口径）。
    /// exGradeOption 只存 exGrade+tier，数值永远是「按当前装备等级现算」，不落库。
    /// 悬停 tooltip 每帧都会要一次，所以按件缓存；返回的列表只读。
    /// </summary>
    public static List<FlameLine> DescribeFlame(InventoryEntry item)
    {
        if (item == null || item.exGradeOption <= 0) return EmptyFlame;
        int incReq = 0;
        if (item.equipmentData != null && item.equipmentData.TryGetValue("iIncReq", out var raw))
            int.TryParse(Convert.ToString(raw), out incReq);
        if (ReferenceEquals(item, cachedFlameItem) && item.exGradeOption == cachedFlameCode
            && incReq == cachedFlameIncReq) return cachedFlameLines;

        var lines = new List<FlameLine>();
        var info = Server.Bot.BotItemCatalog.Shared.Info(item.itemId);
        int reqLevel = info == null ? 0 : Server.Bot.BotItemCatalog.Int(info, "reqLevel");
        bool weapon = NativeSoulService.IsWeapon(item.itemId);
        bool boss = info != null && Server.Bot.BotItemCatalog.Int(info, "bossReward") != 0;
        int baseAttack = info == null ? 0 : Math.Max(Server.Bot.BotItemCatalog.Int(info, "incPAD"),
            Server.Bot.BotItemCatalog.Int(info, "incMAD"));
        foreach (var line in NativeFlameService.Unpack(item.exGradeOption))
        {
            line.value = line.statVal < 0 ? 0
                : FlameCatalog.Value(line.statVal, line.tier, reqLevel, incReq, weapon, boss, baseAttack);
            lines.Add(line);
        }
        cachedFlameItem = item; cachedFlameCode = item.exGradeOption; cachedFlameIncReq = incReq;
        cachedFlameLines = lines;
        return lines;
    }

    static readonly List<FlameLine> EmptyFlame = new List<FlameLine>();
    static InventoryEntry cachedFlameItem;
    static long cachedFlameCode;
    static int cachedFlameIncReq;
    static List<FlameLine> cachedFlameLines;

    // 词条 0..9 是四维/双属性，10..20 见 FlameStats.All 的标签。
    static void ApplyFlameStat(int statVal, int value, GrowthStatDelta delta)
    {
        switch (statVal)
        {
            case 0: delta.Add("STR", value); break;
            case 1: delta.Add("DEX", value); break;
            case 2: delta.Add("INT", value); break;
            case 3: delta.Add("LUK", value); break;
            case 4: delta.Add("STR", value); delta.Add("DEX", value); break;
            case 5: delta.Add("STR", value); delta.Add("INT", value); break;
            case 6: delta.Add("STR", value); delta.Add("LUK", value); break;
            case 7: delta.Add("DEX", value); delta.Add("INT", value); break;
            case 8: delta.Add("DEX", value); delta.Add("LUK", value); break;
            case 9: delta.Add("INT", value); delta.Add("LUK", value); break;
            case 10: delta.Add("PAD", value); break;
            case 11: delta.Add("MAD", value); break;
            case 12: delta.Add("PDD", value); break;
            case 13: delta.Add("HP", value); break;
            case 14: delta.Add("MP", value); break;
            case 15: delta.Add("SPD", value); break;
            case 16: delta.Add("JMP", value); break;
            case 17: // 全屬性%：四维同时乘
                delta.Add("STRPct", value); delta.Add("DEXPct", value);
                delta.Add("INTPct", value); delta.Add("LUKPct", value);
                break;
            case 18: delta.Add("BossDamage", value); break;
            case 19: delta.Add("Damage", value); break;
            default: break;   // 20 = 等級要求減免，没有属性面板通道
        }
    }

    static void ApplySoul(int optionCode, GrowthStatDelta delta)
    {
        var option = SoulCatalog.Of(optionCode);
        if (option == null || string.IsNullOrEmpty(option.key)) return;
        ApplyAbilityKey(option.key, option.value, 0, delta);
    }

    // ——— 极限属性：只算当前预设 ———
    static void AccumulateHyperStats(FullCharacter character, GrowthState growth, int level, GrowthStatDelta delta)
    {
        if (character.hyperstats == null) return;
        foreach (var line in character.hyperstats)
        {
            if (line.preset != growth.hyperPreset || line.slv <= 0) continue;
            var info = HyperStatCatalog.Of(line.skillId);
            if (info == null || string.IsNullOrEmpty(info.key)) continue;
            int value = info.Value(line.slv);
            if (value == 0) continue;
            // 極限屬性把 80000421「神祕力量」当 ARC 加，不是百分比。
            if (info.key == "Arcane") { delta.arcaneForce += value; continue; }
            ApplyAbilityKey(info.key, value, level, delta);
        }
    }

    // ——— 内在能力：当前预设 + 已解锁行 ———
    static void AccumulatePotentials(FullCharacter character, GrowthState growth, int level, GrowthStatDelta delta)
    {
        if (character.potentials == null) return;
        int unlocked = NativeInnerAbilityService.UnlockedLines(level);
        foreach (var row in character.potentials)
        {
            if (row.preset != growth.abilityPreset || row.potKey >= unlocked || row.slv <= 0) continue;
            var info = AbilityCatalog.Of(row.skillId);
            if (info == null || info.keys == null || info.formulas == null) continue;
            for (int i = 0; i < info.keys.Length && i < info.formulas.Length; i++)
            {
                int value = GrowthFormula.Evaluate(info.formulas[i], row.slv);
                if (value == 0) continue;
                ApplyAbilityKey(info.keys[i], value, level, delta);
            }
        }
    }

    /// <summary>
    /// 键名 → 面板。三个目录（HyperStat / Ability / Soul）共用同一套键名，
    /// 所以集中在这里翻译一次；认不出的键进 unsettled，不静默吞掉。
    /// </summary>
    static void ApplyAbilityKey(string key, int value, int level, GrowthStatDelta delta)
    {
        switch (key)
        {
            case "STR": delta.Add("STR", value); return;
            case "DEX": delta.Add("DEX", value); return;
            case "INT": delta.Add("INT", value); return;
            case "LUK": delta.Add("LUK", value); return;
            case "MHP": delta.Add("HP", value); return;
            case "MMP": delta.Add("MP", value); return;
            case "PDD": delta.Add("PDD", value); return;
            case "MDD": delta.Add("MDD", value); return;
            case "PAD": delta.Add("PAD", value); return;
            case "MAD": delta.Add("MAD", value); return;
            case "ACC": delta.Add("ACC", value); return;
            case "EVA": delta.Add("EVA", value); return;
            case "Speed": delta.Add("SPD", value); return;
            case "Jump": delta.Add("JMP", value); return;
            // 百分比类：AdminSetItem.ApplyStat 会按 (base+flat)×(100+pct)/100 结算。
            case "HPR": case "MHPR": delta.Add("HPPct", value); return;
            case "MPR": case "MMPR": delta.Add("MPPct", value); return;
            case "PDDR": delta.Add("PDDPct", value); return;
            case "Damage": delta.Add("Damage", value); return;
            case "BossDamage": delta.Add("BossDamage", value); return;
            case "NormalDamage": delta.Add("NormalDamage", value); return;
            case "IgnoreDefense": delta.Add("IgnoreDefense", value); return;
            case "ExpRate": delta.Add("ExpRate", value); return;
            case "DropRate": delta.Add("DropRate", value); return;
            case "MesoRate": delta.Add("MesoRate", value); return;
            case "CriticalChance": delta.criticalChance += value / 100f; return;
            case "CriticalDamage": delta.criticalBonus += value / 100f; return;
            // 「每一定等級攻擊力 +1」：公式算出的是「每 N 级」，除以当前等级才是加值数量。
            case "LV2PAD":
                if (level > 0 && value > 0) delta.Add("PAD", level / value);
                return;
            case "LV2MAD":
                if (level > 0 && value > 0) delta.Add("MAD", level / value);
                return;
            case "AllStats": // 灵魂的「所有属性 +x」
                delta.Add("STR", value); delta.Add("DEX", value); delta.Add("INT", value); delta.Add("LUK", value);
                return;
            default:
                // 四维互转 / 攻擊速度 / 加持時間 / 塔傷害 / 補給模式 / 狀態異常追加傷害 /
                // 防禦轉傷害 / 無冷卻 / 被動等級 / 目标数 —— 282 客户端有公式，本项目没有对应通道。
                var label = string.IsNullOrEmpty(key) ? "?" : key;
                if (!delta.unsettled.Contains(label)) delta.unsettled.Add(label);
                return;
        }
    }
}
