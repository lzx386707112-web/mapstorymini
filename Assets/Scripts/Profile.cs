using System.Collections.Generic;
using UnityEngine;

// 流程入口；UI 不组合扣款/授予操作。原端规则依据见 docs/original-gameplay-port.md。
public static class Profile
{
    static PlayerProfileData data;
    static PlayerProfileData Data { get { Load(); return data; } }
    public static int gold => Data.gold;
    public static CharacterAttributes Attributes => Data.attributes.Copy();
    public static long experience => Data.experience;
    public static int level => Data.level;
    public static int ap => Data.ap;
    public static string playerName => Data.playerName;
    public static int ExpNeeded => ExpTable.Needed(Data.level);
    public static float ExpRatio
    {
        get
        {
            int need = ExpNeeded;
            return need <= 0 ? 0f : UnityEngine.Mathf.Clamp01((float)((double)Data.experience / need));
        }
    }
    public static string cls => Data.cls;
    public static string equipWeapon => Data.equipWeapon;
    public static string hair => Data.hair;
    public static string outfit => Data.outfit;
    public static IReadOnlyCollection<string> OwnedWeapons => Data.owned.AsReadOnly();
    public static bool LastSaveSucceeded { get; private set; } = true;

    // 升到新等级时广播（参数为新等级）。PlayerCtrl 用它刷新 HP/MP，HUD 用它显示提示。
    public static event System.Action<int> LeveledUp;

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
        if (ClassDefs.Get(data.cls).id != data.cls) data.cls = "beginner";
        if (data.outfit == null || !CosmeticFactory.OutfitColors.ContainsKey(data.outfit)) data.outfit = "none";
        if (!WeaponDefs.CanEquip(WeaponDefs.Get(data.equipWeapon),data.attributes,data.cls)) data.equipWeapon = "1302000";

        // —— 等级 / 经验迁移 ——
        // 旧版只累加 experience、从不升级；这里按原曲线把累计值补成等级。
        // 基础 HP/MP 缺失时取存档里已有的值，绝不把玩家练出来的成长清零。
        bool missingName = string.IsNullOrEmpty(data.playerName);
        if (missingName) data.playerName = PlayerPrefs.GetString("mxd.name", DefaultName);
        LevelSystem.EnsureBaseline(data);
        bool migratedAp = LevelSystem.MigrateNoviceAp(data);

        // 自动化测试用：临时覆盖职业，只影响本次运行，绝不写回存档。
        // 注意光在这里改 data.cls 是不够的 —— 游戏过程中任何一次 Save() 都会把
        // 覆盖后的职业写进真实存档（截图脚本跑完，玩家职业就被换掉了）。
        string debugCls = PlayerPrefs.GetString("debug_cls", "");
        if (!string.IsNullOrEmpty(debugCls) && ClassDefs.Get(debugCls).id == debugCls)
        {
            data.cls = debugCls;
            debugOverride = true;
        }

        int gained = LevelSystem.Reconcile(data);
        if (gained > 0)
            UnityEngine.Debug.Log("[Profile] 旧经验补算等级 → Lv." + data.level + "（补了 " + gained + " 级）");

        // 验收用：临时把等级抬到指定值（浏览器 ?lv=60），同样不写回存档。
        // 成长按 LevelUp 的区间逐级补，等价于真练上去，不是伪造的固定值。
        int debugLevel = PlayerPrefs.GetInt("debug_level", 0);
        if (debugLevel > 0)
        {
            PlayerPrefs.DeleteKey("debug_level");
            LevelSystem.SetLevel(data, debugLevel);
            data.experience = 0;
            debugOverride = true;
            UnityEngine.Debug.Log("[Profile] 调试等级 → Lv." + data.level +
                                  "（HP " + data.baseMaxHp + " / MP " + data.baseMaxMp + "）");
        }

        if (gained > 0 || missingName || migratedAp) Save();
    }

    public const string DefaultName = "枫叶旅人";

    public static void SetName(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0 || value.Length > 12) return;
        Data.playerName = value;
        PlayerPrefs.SetString("mxd.name", value);
        Save();
    }

    // 覆盖期间整段禁用落盘：自动化测试不产生任何存档副作用。
    static bool debugOverride;

    public static void Save()
    {
        if (debugOverride) { LastSaveSucceeded = true; return; }
        LastSaveSucceeded = LocalProfileStore.Save(Data);
    }
    public static void AddGold(int amount) { Data.AddGold(amount); Save(); }

    public static int ItemCount(int id) => Data.ItemCount(id);
    public static void AddItem(int id, int count)
    {
        if (Data.AddItem(id, count)) Save();
    }
    public static bool RemoveItem(int id, int count)
    {
        if (!Data.RemoveItem(id, count)) return false;
        Save();
        return true;
    }

    // —— 任务（原端 Character.getQuest / updateQuestStatus）——
    public static int QuestStatus(int id) => Data.QuestStatusOf(id);
    public static bool IsQuestStarted(int id) => Data.QuestStatusOf(id) == PlayerProfileData.QuestStartedState;
    public static bool IsQuestCompleted(int id) => Data.QuestStatusOf(id) == PlayerProfileData.QuestCompletedState;
    public static long QuestCompletedTime(int id) => Data.QuestCompletedTime(id);
    public static void StartQuest(int id) { Data.SetQuestStarted(id); Save(); }
    public static void CompleteQuest(int id) { Data.SetQuestCompleted(id); Save(); }
    public static void ResetQuest(int id) { Data.ResetQuest(id); Save(); }
    public static string QuestProgress(int id, string key, string fallback = "0") => Data.GetQuestProgress(id, key, fallback);
    public static int QuestProgressInt(int id, string key, int fallback = 0)
    {
        int v;
        return int.TryParse(Data.GetQuestProgress(id, key, fallback.ToString()), out v) ? v : fallback;
    }
    // save=false 给"一次要改一批"的场景用（例如一次击杀推进多个任务），调用方收尾 Save() 一次
    public static void SetQuestProgress(int id, string key, string value, bool save = true)
    {
        Data.SetQuestProgress(id, key, value);
        if (save) Save();
    }
    public static IReadOnlyList<int> StartedQuests => Data.questStarted ?? EmptyQuests;
    public static IReadOnlyList<int> CompletedQuests => Data.questCompleted ?? EmptyQuests;
    static readonly List<int> EmptyQuests = new List<int>();

    // 原版地图模式当前所在位置（换图/重开用；不进存档事务，只记最后一张图）
    public static int LastMapId
    {
        get { return PlayerPrefs.GetInt("mxd.fieldmap", 1000000); }
        set { PlayerPrefs.SetInt("mxd.fieldmap", value); PlayerPrefs.Save(); }
    }
    // 返回本次结算升了几级（默认升级保护开启，所以通常是 0 或 1）。
    public static int GrantExperience(int amount)
    {
        if (amount <= 0) return 0;
        int before = Data.level;
        int levels = LevelSystem.AddExperience(Data, amount, LevelSystem.LevelUpProtect);
        Save();
        if (levels > 0 && Data.level > before)
            LeveledUp?.Invoke(Data.level);
        return levels;
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
    public static bool AutomaticAp => cls == "beginner" && level <= 10;
    public static bool CanAssignAp(int stat)
    {
        if (AutomaticAp || ap <= 0 || stat < 0 || stat > 3) return false;
        var a = Data.attributes;
        int value = stat == 0 ? a.str : stat == 1 ? a.dex : stat == 2 ? a.intelligence : a.luk;
        return value < 32767;
    }
    public static bool AssignAp(int stat)
    {
        if (!CanAssignAp(stat)) return false;
        if (stat == 0) Data.attributes.str++;
        else if (stat == 1) Data.attributes.dex++;
        else if (stat == 2) Data.attributes.intelligence++;
        else Data.attributes.luk++;
        Data.ap--;
        Save();
        return true;
    }
    public static int InventoryCapacity(int tab) => tab == 0 ? Data.equipmentSlots : tab == 4 ? 96 : 24;
    public static List<int> InventoryItems(int tab)
    {
        var result = new List<int>();
        if (Data.itemIds != null)
            foreach (int id in Data.itemIds)
                if (id / 1000000 - 1 == tab && Data.ItemCount(id) > 0) result.Add(id);
        return result;
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
