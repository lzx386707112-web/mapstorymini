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
    // 职业 id（源端 Job id：0 新手，100/200/300/400/500 一转，…112/212 四转，
    // 1000+ 骑士团）。cls 保留给旧存档与美术兼容，新逻辑一律读 jobId。
    public int jobId;
    // 可用 SP（存档累计字段，对应源端 remainingSp[skillbook]）。
    // 新手恒 0（新手技能免费），一转起重排/升级逐步累计。
    public int sp;
    public string equipWeapon = "1302000";
    // 防具槽（与 EquipSlot 常量对应，-1 帽/-5 衣/-6 裤/-7 鞋/-8 手套/-9 披风/-10 盾）。
    // 0 表示空。源端装备落在 inventoryitems(EQUIPPED)，单机沿用武器同路，存本地 JSON，
    // 不单独拆表；DB 表结构仍 1:1 对齐源端（见 Schema.sql 的 inventoryitems/inventoryequipment）。
    public int equipHat, equipTop, equipBottom, equipShoes, equipGlove, equipCape, equipShield;
    // Remaining native negative slots; parallel lists survive JsonUtility serialization.
    public List<int> accessorySlots = new List<int>();
    public List<int> accessoryItems = new List<int>();
    public string hair = "h0";
    public string outfit = "none";
    public List<string> owned = new List<string> { "1302000" };

    // 已学技能（id → 等级）。与 DB skills 表对应；进游戏时由 GameSession 灌入。
    public List<int> skillIds = new List<int>();
    public List<int> skillLevels = new List<int>();

    public int SkillLevelOf(int id)
    {
        if (skillIds == null) return 0;
        int i = skillIds.IndexOf(id);
        return i >= 0 && i < skillLevels.Count ? skillLevels[i] : 0;
    }

    public void SetSkillLevel(int id, int lv)
    {
        if (skillIds == null) skillIds = new List<int>();
        if (skillLevels == null) skillLevels = new List<int>();
        int i = skillIds.IndexOf(id);
        if (i < 0) { skillIds.Add(id); skillLevels.Add(Math.Max(0, lv)); }
        else if (i < skillLevels.Count) skillLevels[i] = Math.Max(0, lv);
    }

    public void ClearSkills()
    {
        skillIds.Clear();
        skillLevels.Clear();
    }

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

    // —— 防具槽（与 EquipSlot 常量对应）——
    public int EquipOfSlot(int slot)
    {
        switch (slot)
        {
            case -1: return equipHat;
            case -5: return equipTop;
            case -6: return equipBottom;
            case -7: return equipShoes;
            case -8: return equipGlove;
            case -9: return equipCape;
            case -10: return equipShield;
            case -11: return int.TryParse(equipWeapon, out var w) ? w : 0;
            default:
                int i = accessorySlots == null ? -1 : accessorySlots.IndexOf(slot);
                return i >= 0 && accessoryItems != null && i < accessoryItems.Count ? accessoryItems[i] : 0;
        }
    }

    public void SetEquipSlot(int slot, int itemId)
    {
        switch (slot)
        {
            case -1: equipHat = itemId; break;
            case -5: equipTop = itemId; break;
            case -6: equipBottom = itemId; break;
            case -7: equipShoes = itemId; break;
            case -8: equipGlove = itemId; break;
            case -9: equipCape = itemId; break;
            case -10: equipShield = itemId; break;
            case -11: equipWeapon = itemId.ToString(); break;
            default:
                if (accessorySlots == null) accessorySlots = new List<int>();
                if (accessoryItems == null) accessoryItems = new List<int>();
                int i = accessorySlots.IndexOf(slot);
                if (i < 0) { accessorySlots.Add(slot); accessoryItems.Add(itemId); }
                else accessoryItems[i] = itemId;
                break;
        }
    }

    // —— 职业成长（规则见 JobSystem.cs，逐条对齐源端 Character.changeJob）——

    /// <summary>切换职业。同步刷新 cls（美术/旧 UI 兼容字段）。</summary>
    public void SetJob(int id)
    {
        jobId = id;
        cls = JobDefs.ClsOf(id);
    }

    public void AddSp(int delta) => sp = Math.Max(0, sp + delta);

    public void AddAp(int delta) => ap = Math.Max(0, ap + delta);

    public void AddEquipSlots(int delta) => equipmentSlots = Math.Max(4, equipmentSlots + delta);

    /// <summary>转职/重排时直接写四维与剩余 AP/SP（对应源端 updateStrDexIntLukSp）。</summary>
    public void SetBaseStats(int str, int dex, int intl, int luk, int remainingAp, int remainingSp)
    {
        attributes.str = str;
        attributes.dex = dex;
        attributes.intelligence = intl;
        attributes.luk = luk;
        ap = remainingAp;
        sp = remainingSp;
    }

    /// <summary>转职 HP/MP 成长（源端 addMaxMPMaxHP）。</summary>
    public void AddBaseHpMp(int addHp, int addMp)
    {
        if (addHp > 0) baseMaxHp = Math.Max(1, baseMaxHp + addHp);
        if (addMp > 0) baseMaxMp = Math.Max(1, baseMaxMp + addMp);
        attributes.maxHp = baseMaxHp;
        attributes.maxMp = baseMaxMp;
    }
}
