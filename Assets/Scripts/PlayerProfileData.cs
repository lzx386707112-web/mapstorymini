using System;
using System.Collections.Generic;

// 单机状态；不依赖 Unity 或存储 API。
[Serializable]
public sealed class PlayerProfileData
{
    public int version = 1;
    public CharacterAttributes attributes = new CharacterAttributes();
    public List<string> migratedLegacyWeapons = new List<string>();
    public int gold;
    public long experience;
    public int equipmentSlots = 24;
    public string cls = "knight";
    public string equipWeapon = "1302000";
    public string hair = "h0";
    public string outfit = "none";
    public List<string> owned = new List<string> { "1302000" };

    // Character.gainMeso 的宽整数运算与范围保护。
    public void AddGold(int amount)
        => gold = (int)Math.Max(0L, Math.Min(int.MaxValue, (long)gold + amount));

    public bool TrySpend(int amount)
    {
        if (amount < 0 || gold < amount) return false;
        gold -= amount;
        return true;
    }

    public bool TryBuy(string id, int catalogPrice)
    {
        if (string.IsNullOrEmpty(id) || catalogPrice < 0 || owned.Count - 1 >= equipmentSlots) return false;
        if (!TrySpend(catalogPrice)) return false;
        owned.Add(id);
        return true;
    }

    public bool TryEquip(string id)
    {
        if (!owned.Contains(id)) return false;
        equipWeapon = id;
        return true;
    }
}
