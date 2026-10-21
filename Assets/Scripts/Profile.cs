using System.Collections.Generic;

// 流程入口；UI 不组合扣款/授予操作。原端规则依据见 docs/original-gameplay-port.md。
public static class Profile
{
    static PlayerProfileData data;
    static PlayerProfileData Data { get { Load(); return data; } }
    public static int gold => Data.gold;
    public static CharacterAttributes Attributes => Data.attributes.Copy();
    public static long experience => Data.experience;
    public static string cls => Data.cls;
    public static string equipWeapon => Data.equipWeapon;
    public static string hair => Data.hair;
    public static string outfit => Data.outfit;
    public static IReadOnlyCollection<string> OwnedWeapons => Data.owned.AsReadOnly();
    public static bool LastSaveSucceeded { get; private set; } = true;

    public static void Load()
    {
        if (data != null) return;
        data = LocalProfileStore.Load();
        data.gold = System.Math.Max(0, data.gold);
        data.experience = System.Math.Max(0, data.experience);
        if (data.equipmentSlots <= 0) data.equipmentSlots = 24;
        if (data.attributes == null) data.attributes = new CharacterAttributes();
        if (data.migratedLegacyWeapons == null) data.migratedLegacyWeapons = new List<string>();
        for (int i = 0; i < data.owned.Count; i++)
        {
            string old = data.owned[i];
            data.owned[i] = WeaponDefs.Canonical(old);
            if (old != data.owned[i]) data.migratedLegacyWeapons.Add(old);
        }
        data.equipWeapon = WeaponDefs.Canonical(data.equipWeapon);
        data.owned.RemoveAll(id => !WeaponDefs.TryGet(id, out _));
        if (!data.owned.Contains("1302000")) data.owned.Add("1302000");
        if (!data.owned.Contains(data.equipWeapon)) data.equipWeapon = "1302000";
        if (!HairStyles.Contains(data.hair)) data.hair = "h0";
        if (ClassDefs.Get(data.cls).id != data.cls) data.cls = "knight";
        if (data.outfit == null || !CosmeticFactory.OutfitColors.ContainsKey(data.outfit)) data.outfit = "none";
        if (!WeaponDefs.CanEquip(WeaponDefs.Get(data.equipWeapon),data.attributes,data.cls)) data.equipWeapon = "1302000";
    }

    public static void Save() => LastSaveSucceeded = LocalProfileStore.Save(Data);
    public static void AddGold(int amount) { Data.AddGold(amount); Save(); }
    public static void GrantExperience(int amount)
    {
        if (amount <= 0) return;
        Data.experience = Data.experience > long.MaxValue - amount ? long.MaxValue : Data.experience + amount;
        Save();
    }
    public static bool Spend(int amount)
    {
        if (!Data.TrySpend(amount)) return false;
        Save();
        return true;
    }
    public static bool OwnsWeapon(string id) => Data.owned.Contains(WeaponDefs.Canonical(id));

    public static bool TryBuyWeapon(string id)
    {
        if (!WeaponDefs.TryGet(id, out var weapon) || weapon.price < 0 || !Data.TryBuy(weapon.id, weapon.price)) return false;
        Save();
        return true;
    }

    // 购买放入背包，不自动装备；装备不能凭空授予物品。
    public static void EquipWeapon(string id)
    {
        if (!WeaponDefs.TryGet(id,out var weapon)) return;
        if (WeaponDefs.CanEquip(weapon, Attributes, cls) && Data.TryEquip(weapon.id)) Save();
    }
    public static bool TryBuyWeapon(int shop, int slot, string expectedId)
    {
        var catalog = WeaponDefs.Shops;
        var selected = System.Linq.Enumerable.FirstOrDefault(catalog, s => s.id == shop);
        if (selected == null || slot < 0 || slot >= selected.items.Count) return false;
        var item = selected.items[slot];
        if (item.id != expectedId || item.price < 0 || !Data.TryBuy(item.id, item.price)) return false;
        Save(); return true;
    }
    public static void SetClass(string id)
    {
        if (ClassDefs.Get(id).id != id) return;
        Data.cls = id;
        if (!WeaponDefs.CanEquip(WeaponDefs.Get(Data.equipWeapon),Data.attributes,id)) Data.equipWeapon = "1302000";
        Save();
    }
    public static void SetHair(string id)
    {
        if (!HairStyles.Contains(id)) return;
        Data.hair = id; Save();
    }
    public static void SetOutfit(string id)
    {
        if (!CosmeticFactory.OutfitColors.ContainsKey(id)) return;
        Data.outfit = id; Save();
    }
}
