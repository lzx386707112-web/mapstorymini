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
    public long experience;          // 当前等级内已累计的经验（原端 Character.exp）

    // —— 等级 / 成长（规则见 LevelSystem.cs）——
    // baseMaxHp/baseMaxMp 是「不含装备」的裸值，对应原端 clientMaxHp/clientMaxMp；
    // 装备的 incMHP/incMMP 由 CharacterAttributes.WithWeapon 在出战时叠加。
    public int level = 1;
    public int baseMaxHp;
    public int baseMaxMp;
    public int ap;                   // 累计可用 AP（尚未做加点面板）
    public string playerName = "";

    public int equipmentSlots = 24;
    public string cls = "beginner";
    public string equipWeapon = "1302000";
    public string hair = "h0";
    public string outfit = "none";
    public List<string> owned = new List<string> { "1302000" };

    // 原版地图掉落进背包的道具（id → 数量）。用两条平行 List 以适配 JsonUtility。
    public List<int> itemIds = new List<int>();
    public List<int> itemCounts = new List<int>();

    public int ItemCount(int id)
    {
        if (itemIds == null || itemCounts == null) return 0;
        int i = itemIds.IndexOf(id);
        return i >= 0 && i < itemCounts.Count ? itemCounts[i] : 0;
    }

    public bool AddItem(int id, int count)
    {
        if (id <= 0 || count <= 0) return false;
        if (itemIds == null) itemIds = new List<int>();
        if (itemCounts == null) itemCounts = new List<int>();
        int i = itemIds.IndexOf(id);
        if (i < 0)
        {
            itemIds.Add(id);
            itemCounts.Add(count);
        }
        else
        {
            itemCounts[i] = (int)Math.Min(int.MaxValue, (long)itemCounts[i] + count);
        }
        return true;
    }

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
