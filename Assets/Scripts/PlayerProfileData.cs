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
    public bool noviceApMigrated;
    public int ap;                   // 可用 AP（新手升级自动分配，转职后手动消费）
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

    // —— 任务状态（对应原端 Character.quests / QuestStatus）——
    // STARTED / COMPLETED 分开存，进度键值对拉平成字符串，JsonUtility 不需要自定义序列化器。
    public List<int> questStarted = new List<int>();
    public List<int> questCompleted = new List<int>();
    public List<long> questCompletedAt = new List<long>();      // 与 questCompleted 平行（可重复任务的间隔判定）
    public List<string> questProgress = new List<string>();     // "questId|key|value"

    public const int QuestNotStarted = 0, QuestStartedState = 1, QuestCompletedState = 2;

    public int QuestStatusOf(int id)
    {
        if (questCompleted != null && questCompleted.Contains(id)) return QuestCompletedState;
        if (questStarted != null && questStarted.Contains(id)) return QuestStartedState;
        return QuestNotStarted;
    }

    public void SetQuestStarted(int id)
    {
        if (questStarted == null) questStarted = new List<int>();
        if (!questStarted.Contains(id)) questStarted.Add(id);
    }

    public void SetQuestCompleted(int id)
    {
        if (questStarted == null) questStarted = new List<int>();
        if (questCompleted == null) questCompleted = new List<int>();
        if (questCompletedAt == null) questCompletedAt = new List<long>();
        questStarted.Remove(id);
        int i = questCompleted.IndexOf(id);
        if (i < 0) { questCompleted.Add(id); questCompletedAt.Add(0); i = questCompleted.Count - 1; }
        questCompletedAt[i] = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public void ResetQuest(int id)
    {
        questStarted?.Remove(id);
        questCompleted?.Remove(id);
        if (questCompletedAt != null)
            while (questCompletedAt.Count < (questCompleted?.Count ?? 0)) questCompletedAt.Add(0);
        if (questProgress != null)
            questProgress.RemoveAll(p => p.StartsWith(id + "|", System.StringComparison.Ordinal));
    }

    public long QuestCompletedTime(int id)
    {
        if (questCompleted == null || questCompletedAt == null) return 0;
        int i = questCompleted.IndexOf(id);
        return i >= 0 && i < questCompletedAt.Count ? questCompletedAt[i] : 0;
    }

    public string GetQuestProgress(int id, string key, string fallback = "0")
    {
        if (questProgress == null) return fallback;
        string prefix = id + "|" + key + "|";
        for (int i = questProgress.Count - 1; i >= 0; i--)
            if (questProgress[i].StartsWith(prefix, System.StringComparison.Ordinal))
                return questProgress[i].Substring(prefix.Length);
        return fallback;
    }

    public void SetQuestProgress(int id, string key, string value)
    {
        if (questProgress == null) questProgress = new List<string>();
        string prefix = id + "|" + key + "|";
        for (int i = questProgress.Count - 1; i >= 0; i--)
            if (questProgress[i].StartsWith(prefix, System.StringComparison.Ordinal)) { questProgress[i] = prefix + value; return; }
        questProgress.Add(prefix + value);
    }

    public bool RemoveItem(int id, int count)
    {
        if (itemIds == null || itemCounts == null || count <= 0) return false;
        int i = itemIds.IndexOf(id);
        if (i < 0 || itemCounts[i] < count) return false;
        itemCounts[i] -= count;
        if (itemCounts[i] <= 0) { itemIds.RemoveAt(i); itemCounts.RemoveAt(i); }
        return true;
    }

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
