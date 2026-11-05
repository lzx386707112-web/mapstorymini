using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// ————————————————————————————————————————————————————————————
// 装备槽位模型（补齐「防具只有武器一个槽」的缺口）。
//
// 源端依据：
//   CharacterFactory.java:69-91   建号时 top=-5 / bottom=-6 / shoes=-7 / weapon=-11
//   MapleInventory EQUIPPED       负位置 = 身上穿着的装备（-1 帽 / -8 手套 / -9 披风 / -10 盾 / -11 武器）
//   EquipmentCatalogService.java:308   reqJob 位掩码（1 战士 / 2 法师 / 4 弓 / 8 飞侠 / 16 海盗）
//   ItemInformationProvider.java:581   reqJob/reqLevel/reqSTR/reqDEX/reqINT/reqLUK
//
// 数据一律直读原始 Character/<类别>/<8位id>.img/info，不落任何导出表。
// 存档边界：槽位内容由 Server 侧的 inventoryitems（inventorytype=EQUIPPED）持有，
// 客户端只在进游戏时灌进这里、存档时写回去（GameSession）。
// ————————————————————————————————————————————————————————————

public static class EquipSlot
{
    public const int Hat = -1;
    public const int FaceAccessory = -2;
    public const int EyeAccessory = -3;
    public const int Earring = -4;
    public const int Top = -5;
    public const int Bottom = -6;
    public const int Shoes = -7;
    public const int Glove = -8;
    public const int Cape = -9;
    public const int Shield = -10;
    public const int Weapon = -11;
    public const int Medal = -49;
    // 282 机器人本体（slot 53）。心臟沿用 -54，与源端 BodyPart.MechanicalHeart 同义。
    public const int Android = -32;
    public const int MechanicalHeart = -54;

    // Paper-doll layers and stat-bearing equipped slots have different consumers.
    public static readonly int[] Appearance = { Hat, Top, Bottom, Shoes, Glove, Cape, Shield, Weapon };
    public static readonly int[] Worn = { Hat, Top, Bottom, Shoes, Glove, Cape, Shield, Weapon, Medal, FaceAccessory, EyeAccessory, Earring, -12, -13, -15, -16, -17, -18, -19, -50, -20, -55, -56, MechanicalHeart, Android, -53, -51,
        -101, -102, -103, -104, -105, -106, -107, -108, -109, -110, -111, -112, -113, -115, -116, -117, -118, -119, -120, -149, -150, -151, -153, -154, -155, -156 };

    public static string NameOf(int slot)
    {
        switch (slot)
        {
            case Hat: return "帽子";
            case FaceAccessory: return "脸饰";
            case EyeAccessory: return "眼饰";
            case Earring: return "耳环";
            case Top: return "上衣";
            case Bottom: return "下装";
            case Shoes: return "鞋子";
            case Glove: return "手套";
            case Cape: return "披风";
            case Shield: return "盾牌";
            case Weapon: return "武器";
            case Medal: return "勋章";
            case Android: return "机器人";
            case MechanicalHeart: return "心臟";
            default: return "装备";
        }
    }

    /// <summary>物品 id → 装备位置（源端 Equip.inventory 的负位置）。</summary>
    public static int SlotOf(int itemId) => NativeInventoryService.EquipmentSlotOf(itemId);

    /// <summary>普通槽 → 对应的现金槽。源端现金装备一律是 slot-100（-1→-101 … -16→-116、-49→-149）。</summary>
    public static int CashOf(int slot) => slot == 0 ? 0 : slot - 100;

    /// <summary>
    /// 装备窗一格实际穿着的槽位：现金槽非空则用现金槽，否则用普通槽。
    /// 依据 BeiDou.exe 0x7FEE65..0x7FEEEC：普通/现金两组数组并行读取（基址间隔 0x1A0），
    /// 现金那组非空就覆盖普通那组。以前只按普通槽号读，现金戒指永远落在面板外。
    /// </summary>
    public static int WornSlotOf(int cellSlot)
        => cellSlot != 0 && Profile.EquipOfSlot(CashOf(cellSlot)) > 0 ? CashOf(cellSlot) : cellSlot;
}

public static class EquipCatalog
{
    static readonly Dictionary<int, ImgNode> cache = new Dictionary<int, ImgNode>();

    /// <summary>Character/&lt;类别&gt;/&lt;8位id&gt;.img 的目录名；非装备返回 null。</summary>
    public static string DirOf(int itemId)
    {
        switch (itemId / 10000)
        {
            case 100: return "Cap";
            case 104: return "Coat";
            case 105: return "Longcoat";
            case 106: return "Pants";
            case 107: return "Shoes";
            case 108: return "Glove";
            case 109: return "Shield";
            case 110: return "Cape";
            case 111: return "Ring";
            case 101: case 102: case 103: return "Accessory";
            // 167 段大部分是饰品，但 01672xxx 是 282 的機器人心臟（BodyPart 54），
            // 本项目 Accessory 目录里那 56 个同名文件是 beidou 的空壳，真正有 info/像素
            // 的是迁移进来的 Character/Android/01672xxx.img。
            case 167: return itemId / 1000 == 1672 ? "Android" : "Accessory";
            // 01662xxx 机器人本体（含 1666 段的 6 件特殊款）。
            case 166: return "Android";
            // 282 徽章：1712xxx 祕法符文在 ArcaneForce，1713/1714xxx 真實符文在
            // AuthenticForce（tools/ms282/import_growth_items.py 导入的布局）。
            // 模板没有 islot，DirOf 不认 171 段的话 Node/Icon 全部取空。
            case 171:
                int sym = itemId / 1000;
                return sym == 1712 ? "ArcaneForce"
                     : (sym == 1713 || sym == 1714) ? "AuthenticForce"
                     : null;
            default:
                int t = itemId / 10000;
                if (t >= 112 && t <= 119) return "Accessory";
                // 客户端 Character/PetEquip 实际只有 180~183（180:296, 181:10, 182:1, 183:1）。
                if (t >= 180 && t <= 183) return "PetEquip";
                if (t == 190 || t == 191 || t == 193 || t == 198 || t == 199) return "TamingMob";
                // 客户端 Character/Weapon 实际含 130~151、160、169、170（170 有 1040 个文件，
                // 商城里 259 件点装武器都是 170xxxx），169/170 必须映射过来，否则图标取不到。
                if (t == 169 || t == 170) return "Weapon";
                return t >= 121 && t <= 160 ? "Weapon" : null;
        }
    }

    public static ImgNode Info(int itemId)
    {
        if (cache.TryGetValue(itemId, out var hit)) return hit;
        var dir = DirOf(itemId);
        var node = dir == null ? null : WzAssets.Node("Character/" + dir + "/" + itemId.ToString("D8") + ".img/info");
        cache[itemId] = node;
        return node;
    }

    public static bool Exists(int itemId) => Info(itemId) != null;

    /// <summary>源端 EquipmentCatalogService.matchesJob：reqJob 位掩码 + 等级 + 四维门槛。</summary>
    public static bool CanEquip(int itemId, CharacterAttributes a, int jobId)
    {
        var info = Info(itemId);
        if (info == null) return false;
        int reqJob = info.Int("reqJob");
        if (reqJob != 0 && (reqJob & JobDefs.JobBit(jobId)) == 0) return false;
        return a.level >= info.Int("reqLevel") && a.str >= info.Int("reqSTR") &&
               a.dex >= info.Int("reqDEX") && a.intelligence >= info.Int("reqINT") &&
               a.luk >= info.Int("reqLUK");
    }

    public static string WhyNot(int itemId, CharacterAttributes a, int jobId)
    {
        var info = Info(itemId);
        if (info == null) return "没有这件装备的数据";
        int reqJob = info.Int("reqJob");
        if (reqJob != 0 && (reqJob & JobDefs.JobBit(jobId)) == 0) return "职业不符（" + JobDefs.NameOf(jobId) + "）";
        if (a.level < info.Int("reqLevel")) return "等级不足 Lv." + info.Int("reqLevel");
        if (a.str < info.Int("reqSTR")) return "力量不足 " + info.Int("reqSTR");
        if (a.dex < info.Int("reqDEX")) return "敏捷不足 " + info.Int("reqDEX");
        if (a.intelligence < info.Int("reqINT")) return "智力不足 " + info.Int("reqINT");
        if (a.luk < info.Int("reqLUK")) return "运气不足 " + info.Int("reqLUK");
        return null;
    }

    /// <summary>
    /// info 上那层「戒指附加效果」。源端数据把戒指的功能拆在 addition 下，和 inc* 是两套：
    ///   statinc  额外属性（多数还带 con 条件）
    ///   critical 暴击率 prob%、暴击伤害 damage%
    ///   boss     BOSS 伤害 damage%（prob=100 表示常驻）
    ///   skill    赋技能；本数据集只引用 90000000/90001001，而 Skill.wz 里没有这两个节点，跳过。
    /// 条件 con 支持三种：level/lv（角色等级门槛）、job（具体 jobId 列表）、weekDay（生效星期）。
    /// </summary>
    public static void ApplyAddition(int itemId, CharacterAttributes result, ref int bossDamage)
    {
        var addition = Info(itemId)?.Get("addition");
        if (addition == null) return;
        int level = result.level, job = Profile.JobId;
        var statinc = addition.Get("statinc");
        if (statinc != null && ConditionMet(statinc.Get("con"), level, job))
        {
            result.str += statinc.Int("incSTR"); result.dex += statinc.Int("incDEX");
            result.intelligence += statinc.Int("incINT"); result.luk += statinc.Int("incLUK");
            result.maxHp += statinc.Int("incMHP"); result.maxMp += statinc.Int("incMMP");
            result.weaponDefense += statinc.Int("incPDD"); result.magicDefense += statinc.Int("incMDD");
            result.accuracy += statinc.Int("incACC"); result.avoidability += statinc.Int("incEVA");
            result.equipmentSpeed += statinc.Int("incSpeed"); result.equipmentJump += statinc.Int("incJump");
        }
        var critical = addition.Get("critical");
        if (critical != null && ConditionMet(critical.Get("con"), level, job))
        {
            result.criticalChance += Mathf.Max(0, critical.Int("prob")) / 100f;
            result.criticalBonus += Mathf.Max(0, critical.Int("damage")) / 100f;
        }
        var boss = addition.Get("boss");
        if (boss != null && ConditionMet(boss.Get("con"), level, job) && boss.Int("prob", 100) >= 100)
            bossDamage += boss.Int("damage");
    }

    // con 三种判据可同时出现（如 weekDay + level），任一不满足就整段不生效。
    static bool ConditionMet(ImgNode con, int level, int job)
    {
        if (con == null) return true;
        int need = con.Int("lv", con.Int("level"));
        if (need > 0 && level < need) return false;
        var jobs = con.Get("job");
        if (jobs != null)
        {
            // 源端列的是具体 jobId（米哈尔 5111/5112、英雄 2111/2112…），不是分支位掩码。
            bool matched = false;
            foreach (var child in jobs.Children)
                if (child.Value != null && System.Convert.ToInt32(child.Value) == job) { matched = true; break; }
            if (!matched) return false;
        }
        var days = con.Get("weekDay");
        if (days != null)
        {
            string today = System.DateTime.Now.DayOfWeek.ToString().ToLowerInvariant();
            bool matched = false;
            foreach (var child in days.Children)
                if (string.Equals(child.Value as string, today, System.StringComparison.OrdinalIgnoreCase)) { matched = true; break; }
            if (!matched) return false;
        }
        return true;
    }

    /// <summary>把一件装备的 inc* 叠加到属性上（源端 Character.reapplyLocalStats 的装备段）。</summary>
    public static void Apply(int itemId, CharacterAttributes result, ref int watk, ref int matk, int slot = 0)
    {
        int ignoredBoss = 0;
        Apply(itemId, result, ref watk, ref matk, slot, ref ignoredBoss);
    }

    public static void Apply(int itemId, CharacterAttributes result, ref int watk, ref int matk, int slot, ref int bossDamage)
    {
        var info = Info(itemId);
        if (info == null) return;
        var instance=GameSession.Character?.inventory.Find(i=>i.itemId==itemId && i.position<0 && (slot==0 || i.position==slot));
        if (instance?.equipmentData!=null && instance.equipmentData.TryGetValue("starForceVestige",out var vestige) && System.Convert.ToInt32(vestige)!=0) return;
        bool native=NativeScrollService.HasStats(instance);
        int Value(string key)=>!native ? info.Int(key) : key switch {
            "incSTR"=>instance.str,"incDEX"=>instance.dex,"incINT"=>instance.int_,"incLUK"=>instance.luk,
            "incMHP"=>instance.hp,"incMMP"=>instance.mp,"incPDD"=>instance.wdef,"incMDD"=>instance.mdef,
            "incACC"=>instance.acc,"incEVA"=>instance.avoid,"incPAD"=>instance.watk,"incMAD"=>instance.matk,
            "incSpeed"=>instance.speed,"incJump"=>instance.jump,_=>info.Int(key) };
        result.str += Value("incSTR"); result.dex += Value("incDEX");
        result.intelligence += Value("incINT"); result.luk += Value("incLUK");
        result.maxHp += Value("incMHP"); result.maxMp += Value("incMMP");
        result.weaponDefense += Value("incPDD"); result.magicDefense += Value("incMDD");
        result.accuracy += Value("incACC"); result.avoidability += Value("incEVA");
        watk += Value("incPAD"); matk += Value("incMAD");
        result.equipmentSpeed += Value("incSpeed");
        result.equipmentJump += Value("incJump");
        if (instance?.equipmentData!=null && instance.equipmentData.TryGetValue("enhancementVersion",out var version) && System.Convert.ToInt32(version)==282)
        {
            var baseStats=new Dictionary<string,int> {
                {"str",Value("incSTR")},{"dex",Value("incDEX")},{"int",Value("incINT")},{"luk",Value("incLUK")},
                {"hp",Value("incMHP")},{"mp",Value("incMMP")},{"watk",Value("incPAD")},{"matk",Value("incMAD")},
                {"wdef",Value("incPDD")},{"mdef",Value("incMDD")},{"speed",Value("incSpeed")},{"jump",Value("incJump")}
            };
            int level=info.Int("reqLevel");
            if(instance.equipmentData.TryGetValue("iIncReq",out var incReq)) level+=System.Convert.ToInt32(incReq);
            var star=StarForceStats.Calculate(itemId,level,instance.starLevel,info.Int("superiorEqp")!=0,baseStats);
            int Delta(string key)=>star.TryGetValue(key,out int value)?value:0;
            result.str+=Delta("str");result.dex+=Delta("dex");result.intelligence+=Delta("int");result.luk+=Delta("luk");
            result.maxHp+=Delta("hp");result.maxMp+=Delta("mp");
            result.weaponDefense+=Delta("wdef");result.magicDefense+=Delta("mdef");
            result.equipmentSpeed+=Delta("speed");result.equipmentJump+=Delta("jump");
            watk+=Delta("watk");matk+=Delta("matk");
        }
        ApplyAddition(itemId, result, ref bossDamage);
    }
}

/// <summary>
/// 当前角色身上穿着什么。Profile.Data 是唯一真源（落本地 JSON / 将来落 DB inventoryitems），
/// 这里只按槽位读取镜像，并负责把防具的 inc* 叠加进最终面板。
/// </summary>
public static class EquipLoadout
{
    static int SlotItem(int slot) => Profile.EquipOfSlot(slot);

    public static int Get(int slot) => SlotItem(slot);

    /// <summary>穿上一件（槽位由物品 id 决定）。返回 null 表示成功，否则是原因。</summary>
    /// <param name="preferredSlot">装备窗上被拖到的普通槽位（-12/-13…）。四格戒指不指定时按第一个空槽，
    /// 指定了就落那一格；那一格已有戒指时由服务端做互换（源端 InventoryManipulator.equip 的 prior 分支）。</param>
    public static string Equip(int itemId, CharacterAttributes attrs, int inventoryPosition = 0, int preferredSlot = 0)
    {
        if(NativePetItems.IsEquipment(itemId))
        {
            if(!GameSession.InGame) return "请先进入游戏";
            int index=EquipWindow.SelectedPet;
            var pet=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,getPet=true,petSlot=index}).pet;
            if(pet==null || !NativePetItems.EquipmentFits(itemId,pet.itemId)) return "请先召唤并选择可以穿戴此装备的宠物";
            int target=NativePetItems.EquipmentSlot(itemId,index);
            if(target==0) return "此宠物装备的槽位尚未核实";
            return MoveEquipment(itemId,target,false,inventoryPosition,index);
        }
        int slot = EquipSlot.SlotOf(itemId);
        if (slot == 0) return "这不是可装备的物品";
        // 机器人换装槽（1200..1213）：道具不改自己的身体装备位，只挂到机器人身上。
        // 源端这里也不查 reqJob/reqLevel——机器人时装纯外观，谁都能穿。
        if (NativeAndroidService.IsApSlot(preferredSlot))
        {
            if (!NativeAndroidService.ApAccepts(preferredSlot, slot))
                return "这件装备不能放进机器人的这个换装槽";
            return MoveEquipment(itemId, preferredSlot, false, inventoryPosition);
        }
        string mountError=NativeMount.WearError(itemId,slot);
        if(mountError!=null) return mountError;
        if (slot == -12 || slot == -112)
        {
            bool cash = slot == -112;
            int[] group = cash ? new[] { -112, -113, -115, -116 } : new[] { -12, -13, -15, -16 };
            int wanted = preferredSlot == 0 ? 0 : cash ? preferredSlot - 100 : preferredSlot <= -100 ? preferredSlot + 100 : preferredSlot;
            if (wanted != 0 && System.Array.IndexOf(group, wanted) >= 0) slot = wanted;
            else foreach (int ringSlot in group)
                if (Profile.EquipOfSlot(ringSlot) == 0) { slot = ringSlot; break; }
        }
        if (slot == EquipSlot.Weapon)
        {
            if (!WeaponDefs.TryGet(itemId.ToString(), out var weapon)) return "这件武器的穿戴尚未接入";
            string error = EquipCatalog.WhyNot(itemId, attrs, Profile.JobId);
            if (error != null) return error;
            return MoveEquipment(itemId, slot, false, inventoryPosition);
        }
        if (slot == EquipSlot.Medal) { NativeMedalWindow.Wear(itemId, false); return null; }
        string why = EquipCatalog.WhyNot(itemId, attrs, Profile.JobId);
        if (why != null) return why;
        // 背包窗直接显示会话库存；发放道具后 Profile 数量镜像可能尚未同步。
        // 穿戴按实际来源格校验，不能用镜像把真实存在的装备提前拦掉。
        bool held = GameSession.InGame
            ? GameSession.Character.inventory.Exists(x => x.inventoryType == InventoryDao.Equip &&
                InventoryDao.IsBagPosition(x.position) && x.itemId == itemId && x.quantity > 0 &&
                (inventoryPosition == 0 || x.position == inventoryPosition))
            : Profile.ItemCount(itemId) > 0;
        if (!held) return "背包里没有这件装备";
        return MoveEquipment(itemId, slot, false, inventoryPosition);
    }

    static string MoveEquipment(int itemId, int slot, bool unequip, int inventoryPosition = 0, int petSlot = 0, int destinationPosition = 0)
    {
        if (!GameSession.InGame) return "请先进入游戏";
        var response = GameSession.Net.ChangeInventory(new InventoryChangeRequest {
            characterId = GameSession.CharacterId, itemId = itemId, equipSlot = slot, sourcePosition = inventoryPosition, petSlot=petSlot, equip = true, quantity = unequip ? 0 : 1, moveTo = destinationPosition });
        if (NativeAndroidService.IsApSlot(slot))
            Debug.Log("[Android/Drag] request item=" + itemId + " source=" + inventoryPosition +
                " target=" + slot + " unequip=" + unequip + " ok=" + response.ok + " error=" + response.error);
        if (!response.ok) return response.error;
        if(slot==-18 || slot==-19 || slot==-118 || slot==-119)
        {
            var player=GameLoop.I?.player;
            player?.GetComponent<NativeBuffs>()?.CancelStat("MONSTER_RIDING");
        }
        GameSession.Character.inventory = response.inventory;
        GameSession.ApplyInventoryToProfile();
        // 机器人装配提示（源端 Char.initAndroid 在装备后弹一次）：缺心臟、心臟档位过高都在这里报。
        if (!unequip && (slot == NativeAndroidService.RobotSlot || slot == NativeAndroidService.HeartSlot))
        {
            string problem = NativeAndroidService.Resolve(GameSession.CharacterId, Profile.EquipOfSlot).reason;
            if (!string.IsNullOrEmpty(problem)) BottomBar.Toast(problem);
        }
        if (!unequip && itemId == 1189999 && SkillProgress.LevelOf(1016) == 0) SkillProgress.SetLearned(1016, 1);
        Profile.Save();
        return null;
    }

    public static void Unequip(int slot)
    {
        if (slot == EquipSlot.Weapon) return;
        string error = UnequipToInventory(slot);
        if (error != null) BottomBar.Toast(error);
    }

    public static string UnequipToInventory(int slot, int destinationPosition = 0)
    {
        if (slot == EquipSlot.Weapon) return "普通武器请通过换装替换";
        int itemId = Profile.EquipOfSlot(slot);
        if (itemId <= 0) return "该格没有装备";
        return MoveEquipment(itemId, slot, true, destinationPosition: destinationPosition);
    }

    /// <summary>当前角色套装差量（源端 Character.setItemBonus 的取用入口，FieldDirector 消费倍率类）。</summary>
    public static System.Collections.Generic.Dictionary<string, int> CurrentSetBonus()
    {
        var wornIds = new List<int>();
        foreach (var kv in WornSlots()) wornIds.Add(kv.Value);
        var source=AdminSetItem.ComputeBonus(wornIds, AdminSetItem.JobIndexOf(Profile.JobId));
        var bonus=source==null?new Dictionary<string,int>():new Dictionary<string,int>(source);
        AddPotentialBonus(bonus,new CharacterAttributes {level=GameSession.Character?.record.level??1});
        var growth = NativeGrowthStats.Accumulate(GameSession.Character, Profile.level, Profile.JobId);
        foreach (string rate in new[] { "ExpRate", "MesoRate", "DropRate" })
            bonus[rate] = (bonus.TryGetValue(rate, out int value) ? value : 0) + growth.Get(rate);
        return bonus;
    }

    static void AddPotentialBonus(Dictionary<string,int> set,CharacterAttributes result)
    {
        var inventory=GameSession.Character?.inventory;
        if(inventory!=null)
        foreach(var item in inventory)
        {
            if(item.position>=0||System.Array.IndexOf(EquipSlot.Worn,item.position)<0||item.equipmentData==null)continue;
            var data=item.equipmentData;
            if(data.TryGetValue("starForceVestige",out var vestige)&&System.Convert.ToInt32(vestige)!=0)continue;
            var options=new List<int>();
            foreach(string prefix in new[]{"potentialOption","bonusOption"})
                for(int line=1;line<=3;line++)if(data.TryGetValue(prefix+line,out var option))options.Add(System.Convert.ToInt32(option));
            if(!options.Exists(id=>id>0))continue;
            int level=EquipCatalog.Info(item.itemId)?.Int("reqLevel")??0;
            if(data.TryGetValue("iIncReq",out var incReq))level+=System.Convert.ToInt32(incReq);
            var effects=PotentialStats.Calculate(PotentialCatalog.Shared,level,options,result.level);
            foreach(var pair in effects)
                if(pair.Key=="CriticalChance")result.criticalChance+=pair.Value/100f;
                else if(pair.Key=="CriticalDamage")result.criticalBonus+=pair.Value/100f;
                else if(pair.Key=="Speed")result.equipmentSpeed+=pair.Value;
                else if(pair.Key=="Jump")result.equipmentJump+=pair.Value;
                else PotentialStats.Add(set,pair.Key,pair.Value);
        }
    }

    static IEnumerable<KeyValuePair<int, int>> WornSlots()
    {
        foreach (int slot in EquipSlot.Worn)
        {
            int id = SlotItem(slot);
            if (id > 0) yield return new KeyValuePair<int, int>(slot, id);
        }
    }

    /// <summary>基础属性 + 武器 + 全部防具 + 套装加成的最终面板（出战与能力值窗共用同一条路）。</summary>
    public static CharacterAttributes Apply(CharacterAttributes baseAttrs, WeaponDef weapon, int attackBuff, out int watk, out int matk)
    {
        int w = attackBuff, m = 0, additionBoss = 0;
        var result = baseAttrs.Copy();
        if(int.TryParse(weapon.id,out int weaponId)) EquipCatalog.Apply(weaponId,result,ref w,ref m,EquipSlot.Weapon,ref additionBoss);
        foreach (var kv in WornSlots())
        {
            if (kv.Key == EquipSlot.Weapon) continue;         // 武器已单独叠加
            EquipCatalog.Apply(kv.Value, result, ref w, ref m,kv.Key,ref additionBoss);
        }

        // —— 套装加成（≡ Character.reapplyLocalStats 的 setItemBonus 段）——
        // 源端 equipped 含武器（position<0 且 >-100），所以套装计数也把武器算上。
        var wornIds = new List<int>();
        foreach (var kv in WornSlots()) wornIds.Add(kv.Value);
        var set = AdminSetItem.ComputeBonus(wornIds, AdminSetItem.JobIndexOf(Profile.JobId));
        set = set == null ? new Dictionary<string,int>() : new Dictionary<string,int>(set);
        AddPotentialBonus(set,result);

        // —— 282 成长系统（徽章 / 星火 / 极限属性 / 内在能力 / 灵魂）——
        // 282 服务端只存状态、属性由客户端 7000xxx 公式算；本项目把同一份公式表在派生层结算，
        // 结果并进同一张差量表，于是 (base+flat)×(100+pct)/100 的语义与套装完全一致。
        var growth = NativeGrowthStats.Accumulate(GameSession.Character, result.level, Profile.JobId);
        foreach (var stat in growth.set)
            set[stat.Key] = (set.TryGetValue(stat.Key, out int hadStat) ? hadStat : 0) + stat.Value;
        // —— 称号试炼统一加成（NativeTitleTrials：佩戴系列已点亮档，差量并进同一张表）——
        foreach (var kv in NativeTitleTrials.BonusSet())
            set[kv.Key] = (set.TryGetValue(kv.Key, out int hadTitle) ? hadTitle : 0) + kv.Value;
        result.criticalChance += growth.criticalChance;
        result.criticalBonus += growth.criticalBonus;
        result.hexaDamagePercent = growth.hexaDamagePercent;
        result.hexaBossPercent = growth.hexaBossPercent;
        result.hexaIgnorePercent = growth.hexaIgnorePercent;
        result.arcaneForce = growth.arcaneForce;
        result.authenticForce = growth.authenticForce;
        // info/addition/boss 的 BOSS 伤害并进同一张差量表，交给 AdminSetItem.ApplyDamage。
        // ComputeBonus 可能是缓存实例，先复制再改。
        if (additionBoss != 0)
        {
            set = set == null ? new Dictionary<string,int>() : new Dictionary<string,int>(set);
            set["BossDamage"] = (set.TryGetValue("BossDamage", out int hadBoss) ? hadBoss : 0) + additionBoss;
        }

        // HP/MP/STR/DEX/LUK：apply(key, base) = (base+flat) + (base+flat)×pct%
        result.maxHp = AdminSetItem.ApplyStat(set, "HP", result.maxHp);
        result.maxMp = AdminSetItem.ApplyStat(set, "MP", result.maxMp);
        result.str = AdminSetItem.ApplyStat(set, "STR", result.str);
        result.dex = AdminSetItem.ApplyStat(set, "DEX", result.dex);
        result.luk = AdminSetItem.ApplyStat(set, "LUK", result.luk);
        // 源端 INT 特殊：先 apply INT，魔法值吃「INT 差量」再整体 apply MAD。
        // 这里 matk = intelligence + m，等价表达为：MAD 先乘到 (intelligence+m) 上——顺序一致。
        result.intelligence = AdminSetItem.ApplyStat(set, "INT", result.intelligence);
        // 源端 PDD/MDD/ACC/EVA 走 SetItemNativeStats.mergedValue：总值 = apply(key, 装备+技能)，
        // 本工程无对应技能 buff 通道，装备值即基线，apply 语义等价。
        result.weaponDefense = AdminSetItem.ApplyStat(set, "PDD", result.weaponDefense);
        result.magicDefense = AdminSetItem.ApplyStat(set, "MDD", result.magicDefense);
        result.accuracy = AdminSetItem.ApplyStat(set, "ACC", result.accuracy);
        result.avoidability = AdminSetItem.ApplyStat(set, "EVA", result.avoidability);
        w = AdminSetItem.ApplyStat(set, "PAD", w);
        m = AdminSetItem.ApplyStat(set, "MAD", result.intelligence + m) - result.intelligence;
        // SPD/JMP are merged with active temporary stats by PlayerCtrl, as SetItemNativeStats does.

        result.maxHp = Mathf.Max(50, result.maxHp);
        result.maxMp = Mathf.Max(5, result.maxMp);
        result.setItemBonus = set;    // CombatRules.Attack 的 Damage% 消费（源端 Character.setItemBonus）
        watk = w;
        matk = System.Math.Min(2000, result.intelligence + m);
        return result;
    }
}
