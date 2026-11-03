using System.Collections.Generic;
using UnityEngine;

// 流程入口；UI 不组合扣款/授予操作。原端规则依据见 docs/original-gameplay-port.md。
public static class Profile
{
    static PlayerProfileData data;
    // 对外可读：Client/GameSession 需要把整份存档写回数据库（Server 层不认识 Unity 存档）。
    // 读写后请调用 Save()，否则改动不会落到本地盘。
    public static PlayerProfileData Data { get { Load(); return data; } }
    public static long gold => Data.gold;
    public static CharacterAttributes Attributes => Data.attributes.Copy();
    public static long experience => Data.experience;
    public static int level => Data.level;
    public static int ap => Data.ap;
    public static string playerName => Data.playerName;
    public static long ExpNeeded => ExpTable.Needed(Data.level);
    public static float ExpRatio
    {
        get
        {
            long need = ExpNeeded;
            return need <= 0 ? 0f : UnityEngine.Mathf.Clamp01((float)((double)Data.experience / need));
        }
    }
    public static string cls => Data.cls;
    // 职业 id（0 新手 / 100~522 冒险家 / 1000~1512 骑士团），规则见 JobSystem.cs
    public static int JobId => Data.jobId;
    // 可用 SP（一转起为存档累计字段；新手技能免费，TotalSp 由 SkillProgress 算）
    public static int Sp => Data.sp;
    public static string equipWeapon => Data.equipWeapon;
    public static string hair => Data.hair;
    public static string outfit => Data.outfit;
    public static IReadOnlyCollection<string> OwnedWeapons => Data.owned.AsReadOnly();
    public static bool LastSaveSucceeded { get; private set; } = true;

    // 升到新等级时广播（参数为新等级）。PlayerCtrl 用它回满 HP/MP 并播源端 BasicEff/LevelUp 特效。
    // 源端升级只有「特效 + 音效」，HUD 不叠任何文字，所以这里不再给 HUD 供数据。
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
        MigrateJob(data);
        if (data.outfit == null || !CosmeticFactory.OutfitColors.ContainsKey(data.outfit)) data.outfit = "none";
        if (!WeaponDefs.CanEquip(WeaponDefs.Get(data.equipWeapon),data.attributes,data.jobId)) data.equipWeapon = "1302000";

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
        //
        // 2026-10-31：这个键必须「读一次就删」。旧写法只在 PlaySmokeTest.Finish() 里清，
        // 一旦冒烟/截图跑到一半被打断（编辑器崩溃、直接 Cmd+Q），键就永久留在 PlayerPrefs，
        // 之后每次启动都把玩家职业强改成 debug_cls（warrior→110 剑客），于是：
        //   * 技能窗按 Profile.JobId 取职业链 → 新手冒烟看到「剑士/剑客」两本转职技能书；
        //   * debugOverride=true 又让 Profile.Save() 全部静默空转 → 玩家进度压根不落盘。
        // 与 debug_level 一样一次性消费，是这种「调试覆盖」唯一安全的形式。
        string debugCls = PlayerPrefs.GetString("debug_cls", "");
        if (!string.IsNullOrEmpty(debugCls) && ClassDefs.Get(debugCls).id == debugCls)
        {
            PlayerPrefs.DeleteKey("debug_cls");
            PlayerPrefs.Save();
            data.cls = debugCls;
            data.jobId = JobDefs.JobIdOf(debugCls);
            debugOverride = true;
            UnityEngine.Debug.LogWarning("[Profile] 本次运行职业被调试键 debug_cls 覆盖 → " + debugCls +
                                         "（job " + data.jobId + "），本次不落盘；键已删除，下次启动恢复存档职业");
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

    // —— 职业迁移 ——
    // 旧存档只有 cls 字符串（beginner/knight/warrior/mage/archer/priest 六个玩具职业）。
    // jobId 按 JobDefs.LegacyCls 映射；sp 按旧公式 3*(等级-1) 播种（一转起），
    // 之后走存档累计（升级 +3、转职按 JobSystem 授予），不再由等级反推。
    static void MigrateJob(PlayerProfileData d)
    {
        if (d.jobId <= 0 || !JobDefs.IsValid(d.jobId)) d.jobId = JobDefs.JobIdOf(d.cls);
        if (!JobDefs.IsValid(d.jobId)) d.jobId = 0;
        d.cls = JobDefs.ClsOf(d.jobId);
        if (d.sp <= 0 && d.jobId > 0)
            d.sp = 3 * Mathf.Max(0, d.level - 1);
    }

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

    static int saveBatchDepth;
    static bool saveBatchPending;
    public static System.IDisposable DeferSaves() {saveBatchDepth++;return new SaveBatch();}
    sealed class SaveBatch : System.IDisposable
    {
        bool disposed;
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(--saveBatchDepth==0 && saveBatchPending) {saveBatchPending=false;Save();}
        }
    }

    public static void Save()
    {
        if (debugOverride) { LastSaveSucceeded = true; return; }
        if(saveBatchDepth>0) {saveBatchPending=true;return;}
        LastSaveSucceeded = LocalProfileStore.Save(Data);
    }
    public static void AddGold(long amount) { Data.AddGold(amount); Save(); }

    public static int ItemCount(int id) => Data.ItemCount(id);
    public static void AddItem(int id, int count)
        => TryAddItem(id, count);
    public static bool TryAddItem(int id, int count)
    {
        if (GameSession.InGame && !SyncInventory(id, count)) return false;
        if (!Data.AddItem(id, count)) return false;
        Save();
        return true;
    }
    public static bool RemoveItem(int id, int count, int sourcePosition=0)
    {
        if (Data.ItemCount(id) < count) return false;
        if (GameSession.InGame && !SyncInventory(id, -count, sourcePosition)) return false;
        if (!Data.RemoveItem(id, count)) return false;
        Save();
        return true;
    }

    static bool SyncInventory(int id, int count, int sourcePosition=0)
    {
        var result = GameSession.Net.ChangeInventory(new InventoryChangeRequest { characterId = GameSession.CharacterId, itemId = id, quantity = count,sourcePosition=sourcePosition });
        if (!result.ok) { BottomBar.Toast(result.error); return false; }
        GameSession.Character.inventory = result.inventory; return true;
    }

    public static bool ConsumeProjectile(int id,int count,int position)
    {
        if (count==0) return true;
        if (count>0 && Data.ItemCount(id)<count) return false;
        if (GameSession.InGame)
        {
            var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {
                characterId=GameSession.CharacterId,itemId=id,quantity=-count,sourcePosition=position,projectile=true });
            if (!result.ok) return false;
            GameSession.Character.inventory=result.inventory;
        }
        if (count>0) Data.RemoveItem(id,count); else Data.AddItem(id,-count);
        Save(); return true;
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
    public static int GrantExperience(long amount)
    {
        // !toggleexp（源端 ToggleExpCommand → Character.gainExp 直接 return）。
        if (GameServer.Local != null && !GameConfigDao.GetBool(GameServer.Local.Db, "exp_enabled", true)) return 0;
        // 经验是 long：300 级需求已到 1.6e17，不能再钳到 int.MaxValue
        amount+=NativeEquipmentEffects.PendantBonus(amount);
        amount+=NativeEquipmentEffects.RingExpBonus(amount);
        if (GameLoop.I?.player?.GetComponent<NativeMobDebuffs>()?.Has(124) == true) amount /= 2;
        if (amount <= 0) return 0;
        int before = Data.level;
        int levels = LevelSystem.AddExperience(Data, amount, LevelSystem.LevelUpProtect);
        NativeGainNotice.Experience(amount);
        Save();
        if (levels > 0 && Data.level > before)
            LeveledUp?.Invoke(Data.level);
        return levels;
    }
    public static bool Spend(long amount)
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
        Data.jobId = JobDefs.JobIdOf(id);
        if (!WeaponDefs.CanEquip(WeaponDefs.Get(Data.equipWeapon),Data.attributes,id)) Data.equipWeapon = "1302000";
        Save();
    }

    // 转职/能力值重排等成长操作统一走 JobSystem（规则层），这里只做落盘
    public static void SetJob(int id)
    {
        if (!JobDefs.IsValid(id)) return;
        Data.SetJob(id);
        if (!WeaponDefs.CanEquip(WeaponDefs.Get(Data.equipWeapon), Data.attributes, Data.jobId)) Data.equipWeapon = "1302000";
        Save();
    }
    public static void AddSp(int delta) { Data.AddSp(delta); Save(); }
    public static void AddAp(int delta) { Data.AddAp(delta); Save(); }
    public static void AddBaseHpMp(int addHp, int addMp) { Data.AddBaseHpMp(addHp, addMp); Save(); }
    public static void AddEquipSlots(int delta) { Data.AddEquipSlots(delta); Save(); }
    public static void SetBaseStats(int str, int dex, int intl, int luk, int ap, int sp)
    { Data.SetBaseStats(str, dex, intl, luk, ap, sp); Save(); }

    // 防具槽位（与 EquipSlot 常量对应；源端在 inventoryitems 表里，单机走本地 JSON，DB 表结构仍对齐）
    public static int EquipOfSlot(int slot)
    {
        if(GameSession.InGame && NativePetItems.IsPetSlot(slot))
            return GameSession.Character.inventory.Find(x=>x.inventoryType==InventoryDao.Equipped && x.position==slot)?.itemId ?? 0;
        return Data.EquipOfSlot(slot);
    }
    public static void SetEquipSlot(int slot, int itemId) { Data.SetEquipSlot(slot, itemId); Save(); }

    public static bool AutomaticAp => JobId == 0 && level <= 10;
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
    // CUIStat::AutoAp (8C8692) -> CBasicStat plan (7939BA), default server client-plan mode.
    // Array order is STR, DEX, INT, LUK; the secondary goal is filled before the primary.
    public static int[] AutoApPlan(bool brawler = false)
    {
        if (AutomaticAp || ap <= 0) return null;
        return BuildAutoApPlan(JobId, level, ap, Data.attributes, brawler);
    }
    public static int[] BuildAutoApPlan(int jobId, int lv, int points, CharacterAttributes attributes, bool brawler = false)
    {
        if (points <= 0 || attributes == null) return null;
        int job = jobId % 1000, secondary, primary, goal, mainGoal;
        switch (job / 100)
        {
            case 0:
                if (jobId != 0 && jobId != 2001) return null;
                secondary = 1; primary = 0; goal = lv; mainGoal = lv * 4 + 12; break;
            case 1:
                secondary = 1; primary = 0; goal = lv <= 30 ? lv * 2 : lv + 30;
                mainGoal = lv * 5 + 12 - goal; break;
            case 2:
                secondary = 3; primary = 2; goal = lv + 3; mainGoal = lv * 5 + 12 - goal; break;
            case 3:
                if (job == 300 || job >= 310 && job <= 312) goal = lv + 5;
                else if (job >= 320 && job <= 322) goal = lv;
                else return null;
                secondary = 0; primary = 1; mainGoal = lv * 5 + 12 - goal; break;
            case 4:
                secondary = 1; primary = 3; goal = lv <= 40 ? lv * 2 : lv + 40;
                mainGoal = lv * 5 + 12 - goal; break;
            case 5:
                bool fighter = job / 10 == 51 || jobId == 1500 || job == 500 && brawler;
                if (fighter)
                {
                    secondary = 1; primary = 0; goal = lv <= 20 ? lv * 2 : lv + 20;
                    mainGoal = lv * 5 + 12 - goal;
                }
                else if (job / 10 == 52 || job == 500)
                { secondary = 0; primary = 1; goal = lv; mainGoal = lv * 4 + 12; }
                else return null;
                break;
            default: return null;
        }
        var a = attributes;
        int[] values = { a.str, a.dex, a.intelligence, a.luk }, gain = new int[4];
        int first = System.Math.Min(points, System.Math.Max(0, goal - values[secondary]));
        int second = System.Math.Min(points - first, System.Math.Max(0, mainGoal - values[primary]));
        second += points - first - second;
        gain[secondary] = System.Math.Min(first, System.Math.Max(0, 32767 - values[secondary]));
        gain[primary] = System.Math.Min(second, System.Math.Max(0, 32767 - values[primary]));
        return gain;
    }
    public static bool AssignAutoAp(int[] plan)
    {
        if (plan == null || plan.Length != 4 || AutomaticAp) return false;
        var a = Data.attributes;
        int[] values = { a.str, a.dex, a.intelligence, a.luk };
        long spent = 0;
        for (int i = 0; i < 4; i++)
        {
            if (plan[i] < 0 || values[i] + (long)plan[i] > 32767) return false;
            spent += plan[i];
        }
        if (spent <= 0 || spent > ap) return false;
        a.str += plan[0]; a.dex += plan[1]; a.intelligence += plan[2]; a.luk += plan[3];
        Data.ap -= (int)spent; Save();
        GameLoop.I?.player?.RefreshBuffAttributes();
        return true;
    }
    // —— 能力值洗点（源端 AssignAPProcessor.APResetAction / UseCashItemHandler175..235）——
    // APFrom/APTo 是原端编码：STR 64 / DEX 128 / INT 256 / LUK 512 / HP 2048 / MP 8192。
    // 原端先按 APFrom 扣一点（STR/DEX/INT/LUK 需 >=5），再用 addStat(APTo, usedAPReset=true) 加一点。
    // 本工程 StatWindow 的 HP/MP 加点箭头常驻禁用，hpMpUsed 恒为 0，
    // 所以 HP/MP 两行按源端 hpMpUsed<1 分支拒绝，不伪造洗血结果。
    public static string ResetAp(int from, int to)
    {
        var a = Data.attributes;
        switch (from)
        {
            case 64: if (a.str < 5) return "你的力量(STR)不足，无法进行重置。"; break;
            case 128: if (a.dex < 5) return "你的敏捷(DEX)不足，无法进行重置。"; break;
            case 256: if (a.intelligence < 5) return "你的智力(INT)不足，无法进行重置。"; break;
            case 512: if (a.luk < 5) return "你的运气(LUK)不足，无法进行重置。"; break;
            case 2048: case 8192: return "你没有足够的HP、MP属性点用于AP重置。";
            default: return "AP重置操作执行失败。";
        }
        switch (to)
        {
            case 64: case 128: case 256: case 512: break;
            case 2048: case 8192: return "你没有足够的HP、MP属性点用于AP重置。";
            default: return "AP重置操作执行失败。";
        }
        switch (from)
        {
            case 64: a.str--; break;
            case 128: a.dex--; break;
            case 256: a.intelligence--; break;
            default: a.luk--; break;
        }
        switch (to)
        {
            case 64: a.str++; break;
            case 128: a.dex++; break;
            case 256: a.intelligence++; break;
            default: a.luk++; break;
        }
        Save();
        GameLoop.I?.player?.RefreshBuffAttributes();
        return null;
    }
    // —— 技能点洗点（源端 UseCashItemHandler 175..235 的 SP 分支 / AssignSPProcessor.canSPAssign）——
    // 原端请求先 SPTo 后 SPFrom（窗口 NativeSpResetWindow 已按此序准备）；规则：
    // 目标技能 curLevel < maxLevel（四转受 masterLevel，本工程 SkillBook.MasterCapOf 已合并处理）、
    // 来源技能 curLevel > 0，双向各 ±1；来源归 0 时原端还会清理技能宏引用并重发宏包
    // （UseCashItemHandler:199-227），这里由 NativeSkillMacros.ClearSkillRefs 完成。
    // canSPAssign 只校验技能在本职业树；洗点窗列表本就来自 LineForJob，天然满足。
    public static string ResetSp(int spFromId, int spToId)
    {
        var from = SkillBook.Get(spFromId);
        var to = SkillBook.Get(spToId);
        if (from == null || to == null) return "技能数据缺失";
        if (spFromId == spToId) return "AP/SP重置操作执行失败。";
        int fromLv = SkillProgress.LevelOf(spFromId);
        int toLv = SkillProgress.LevelOf(spToId);
        int cap = SkillBook.MasterCapOf(spToId);
        if (fromLv <= 0) return "你没有可以恢复的技能点。";
        if (toLv >= cap) return "目标技能等级已达到上限。";
        SkillProgress.SetLevel(spFromId, fromLv - 1);
        SkillProgress.SetLevel(spToId, toLv + 1);
        if (fromLv - 1 == 0) NativeSkillMacros.ClearSkillRefs(spFromId);
        return null;
    }
    public static int InventoryCapacity(int tab)
    {
        if (tab == 0) return Data.equipmentSlots;
        var rec = GameSession.InGame ? GameSession.Character.record : null;
        return tab == 4 ? 96 : rec == null ? 24 : tab == 1 ? rec.useslots : tab == 2 ? rec.setupslots : rec.etcslots;
    }
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
