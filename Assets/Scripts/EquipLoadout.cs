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

    // 客户端真正渲染/叠加的槽位（饰品的美术不在纸娃娃配方里，先不参与）
    public static readonly int[] Worn = { Hat, Top, Bottom, Shoes, Glove, Cape, Shield, Weapon, Medal };

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
            default: return "装备";
        }
    }

    /// <summary>物品 id → 装备位置（源端 Equip.inventory 的负位置）。</summary>
    public static int SlotOf(int itemId) => NativeInventoryService.EquipmentSlotOf(itemId);

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

    /// <summary>把一件装备的 inc* 叠加到属性上（源端 Character.reapplyLocalStats 的装备段）。</summary>
    public static void Apply(int itemId, CharacterAttributes result, ref int watk, ref int matk)
    {
        var info = Info(itemId);
        if (info == null) return;
        result.str += info.Int("incSTR"); result.dex += info.Int("incDEX");
        result.intelligence += info.Int("incINT"); result.luk += info.Int("incLUK");
        result.maxHp += info.Int("incMHP"); result.maxMp += info.Int("incMMP");
        result.weaponDefense += info.Int("incPDD"); result.magicDefense += info.Int("incMDD");
        result.accuracy += info.Int("incACC"); result.avoidability += info.Int("incEVA");
        watk += info.Int("incPAD"); matk += info.Int("incMAD");
        result.equipmentSpeed += info.Int("incSpeed");
        result.equipmentJump += info.Int("incJump");
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
    public static string Equip(int itemId, CharacterAttributes attrs, int inventoryPosition = 0)
    {
        int slot = EquipSlot.SlotOf(itemId);
        if (slot == 0) return "这不是可装备的物品";
        if (slot == EquipSlot.Weapon)
        {
            if (!WeaponDefs.TryGet(itemId.ToString(), out var weapon)) return "这件武器的穿戴尚未接入";
            string error = EquipCatalog.WhyNot(itemId, attrs, Profile.JobId);
            if (error != null) return error;
            return MoveEquipment(itemId, slot, false, inventoryPosition);
        }
        if (slot == EquipSlot.Medal) { NativeMedalWindow.Wear(itemId, false); return null; }
        if (slot != EquipSlot.Hat && slot != EquipSlot.Top && slot != EquipSlot.Bottom && slot != EquipSlot.Shoes &&
            slot != EquipSlot.Glove && slot != EquipSlot.Cape && slot != EquipSlot.Shield) return "这个装备栏位尚未接入";
        string why = EquipCatalog.WhyNot(itemId, attrs, Profile.JobId);
        if (why != null) return why;
        if (Profile.ItemCount(itemId) <= 0) return "背包里没有这件装备";
        return MoveEquipment(itemId, slot, false, inventoryPosition);
    }

    static string MoveEquipment(int itemId, int slot, bool unequip, int inventoryPosition = 0)
    {
        if (!GameSession.InGame) return "请先进入游戏";
        var response = GameSession.Net.ChangeInventory(new InventoryChangeRequest {
            characterId = GameSession.CharacterId, itemId = itemId, equipSlot = slot, sourcePosition = inventoryPosition, equip = true, quantity = unequip ? 0 : 1 });
        if (!response.ok) return response.error;
        GameSession.Character.inventory = response.inventory;
        GameSession.ApplyInventoryToProfile();
        Profile.Save();
        return null;
    }

    public static void Unequip(int slot)
    {
        if (slot == EquipSlot.Weapon) return;
        int itemId = Profile.EquipOfSlot(slot);
        if (itemId <= 0) return;
        string error = MoveEquipment(itemId, slot, true);
        if (error != null) BottomBar.Toast(error);
    }

    /// <summary>当前角色套装差量（源端 Character.setItemBonus 的取用入口，FieldDirector 消费倍率类）。</summary>
    public static System.Collections.Generic.Dictionary<string, int> CurrentSetBonus()
    {
        var wornIds = new List<int>();
        foreach (var kv in WornSlots()) wornIds.Add(kv.Value);
        return AdminSetItem.ComputeBonus(wornIds, AdminSetItem.JobIndexOf(Profile.JobId));
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
        int w = 0, m = 0;
        var result = baseAttrs.Copy();
        // 武器走原有 WeaponDefs（含攻击倍率/种类判定），防具走 Character WZ 的 inc*。
        var d = weapon.stats;
        result.str += NativeCombatData.Int(d, "incSTR"); result.dex += NativeCombatData.Int(d, "incDEX");
        result.intelligence += NativeCombatData.Int(d, "incINT"); result.luk += NativeCombatData.Int(d, "incLUK");
        result.maxHp += NativeCombatData.Int(d, "incMHP"); result.maxMp += NativeCombatData.Int(d, "incMMP");
        result.weaponDefense += NativeCombatData.Int(d, "incPDD"); result.magicDefense += NativeCombatData.Int(d, "incMDD");
        result.accuracy += NativeCombatData.Int(d, "incACC"); result.avoidability += NativeCombatData.Int(d, "incEVA");
        // The older combat export omits movement fields; read the original weapon IMG.
        if (int.TryParse(weapon.id,out int movementWeapon))
        {
            var movementInfo=EquipCatalog.Info(movementWeapon);
            result.equipmentSpeed+=movementInfo?.Int("incSpeed") ?? 0;
            result.equipmentJump+=movementInfo?.Int("incJump") ?? 0;
        }
        w += NativeCombatData.Int(d, "incPAD") + attackBuff;
        m += NativeCombatData.Int(d, "incMAD");
        foreach (var kv in WornSlots())
        {
            if (kv.Key == EquipSlot.Weapon) continue;         // 武器已单独叠加
            EquipCatalog.Apply(kv.Value, result, ref w, ref m);
        }

        // —— 套装加成（≡ Character.reapplyLocalStats 的 setItemBonus 段）——
        // 源端 equipped 含武器（position<0 且 >-100），所以套装计数也把武器算上。
        var wornIds = new List<int>();
        foreach (var kv in WornSlots()) wornIds.Add(kv.Value);
        var set = AdminSetItem.ComputeBonus(wornIds, AdminSetItem.JobIndexOf(Profile.JobId));

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
