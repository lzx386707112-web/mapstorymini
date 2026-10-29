// 游戏会话：客户端与「服务端」之间的唯一通道。
//
// 单机模式：GameServer.Start() 起进程内服务端，InProcessTransport 直连。
// 联机模式（将来）：把 Transport 换成 SocketTransport，业务代码零改动。
//
// 同时负责 DB 角色数据 ↔ 现有 PlayerProfileData 的双向同步：
//   进游戏：FullCharacter → Profile.Data（现有玩法代码无感）
//   存档：  Profile.Data → FullCharacter → DB
using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameSession
{
    public static GameServer Server { get; private set; }
    public static ITransport Net { get; private set; }

    public static int AccountId { get; private set; }
    public static string AccountName { get; private set; }
    public static int World { get; private set; }
    public static int Channel { get; private set; }
    public static int CharacterId { get; private set; }
    public static FullCharacter Character { get; private set; }
    public static bool InGame { get; private set; }

    public static void Boot()
    {
        if (Server != null) return;
        Server = GameServer.Start();
        Server.MapInfo = new FieldMapInfo();
        Server.Community.CanFamilyTravel = NativeCommunityMaps.CanTravel;
        Server.Cash.CanEnter = NativeCommunityMaps.CanEnterCash;
        Net = Server.Transport;
        Server.TradeInventoryCommitted += ApplyTradeInventory;
        global::Server.Field.MapReactorService.ScriptSource = global::ScriptSource.LoadKey;
        global::Server.Field.MapReactorService.ScriptMessage = (map,type,text)=>
        {
            if(GameLoop.I?.field?.Map?.id==map) NativeSystemMessage.Drop(type,text);
        };
        global::Server.Field.MapReactorService.EventInstance = map=>GameLoop.I?.field?.Map?.id==map ? NativeEventScripts.Instance() : MapleDash.Js.JsNull.Value;
        global::Server.Field.MapReactorService.ScriptError = error=>UnityEngine.Debug.LogError("[ReactorScript] "+error.Message);
        global::Server.Bot.BotTypeManager.GrantTutorialAdmin = GrantTutorialAdmin;
        global::Server.Bot.BotTypeManager.ApplySkillEffect = ApplyBotSkillEffect;
        MigrateLegacyProfile();
    }

    /// <summary>
    /// 旧存档迁移：PlayerPrefs 里的单人进度 → DB 的 local 账号角色（一次性）。
    /// 只在 DB 没有任何角色时执行，避免覆盖新数据。
    /// </summary>
    public static void MigrateLegacyProfile()
    {
        Profile.Load();
        var d = Profile.Data;
        bool hasProgress = d.level > 1 || d.gold > 0 || d.experience > 0 ||
                           (d.questCompleted != null && d.questCompleted.Count > 0);
        if (!hasProgress) return;

        var snap = new LegacyProfileSnapshot
        {
            name = d.playerName != "" ? d.playerName : "Migrated",
            level = d.level,
            exp = d.experience,
            str = d.attributes.str, dex = d.attributes.dex,
            int_ = d.attributes.intelligence, luk = d.attributes.luk,
            maxHp = Mathf.Max(50, d.baseMaxHp), maxMp = Mathf.Max(5, d.baseMaxMp),
            job = d.jobId,
            map = Profile.LastMapId,
            ap = d.ap,
            sp = d.sp,
            meso = d.gold,
            equipSlots = d.equipmentSlots,
        };
        foreach (var kv in SkillProgress.AllLearned())
            snap.skills.Add(new LegacySkillEntry { skillId = kv.Key, skillLevel = kv.Value });

        var res = Net.ImportLegacy(new ImportLegacyRequest { snapshot = snap });
        if (res != null && res.characterId > 0)
            Debug.Log("[GameSession] 旧存档已迁移到 DB：角色 id=" + res.characterId);
    }

    // —————————————————— 登录态 ——————————————————

    public static LoginResponse Login(string name, string password)
    {
        var res = Net.Login(new LoginRequest { name = name, password = password });
        if (res != null && res.code == LoginResultCode.Ok) AccountGender = res.gender;
        return res;
    }

    /// <summary>
    /// 账号性别（源端 LOGIN_STATUS 里的 gender 字节）。10 = 未选，选完走 SetGender。
    /// </summary>
    public static int AccountGender { get; private set; } = 10;

    /// <summary>
    /// 源端 SET_GENDER（SetGenderHandler）。**必须传 accountId**：性别弹窗是在进选区之前弹的，
    /// 那时 BeginSession 还没跑、AccountId 仍是 0，用会话里的 id 会静默失败（性别永远写不进去，
    /// 下次登录又弹「可以使用的账号」）。只有已进会话的地方才用单参重载。
    /// </summary>
    public static bool SetGender(int accountId, int gender)
    {
        if (accountId <= 0) return false;
        var res = Net.SetGender(new SetGenderRequest { accountId = accountId, gender = gender });
        if (res != null && res.ok) AccountGender = res.gender;
        return res != null && res.ok;
    }

    public static bool SetGender(int gender) => SetGender(AccountId, gender);

    public static ServerListResponse ServerList()
        => Net.ServerList(new ServerListRequest { accountId = AccountId });

    static readonly System.Collections.Generic.Dictionary<long, string[]> appearances =
        new System.Collections.Generic.Dictionary<long, string[]>();

    public static CharListResponse CharList(int world, int channel)
    {
        var list = Net.CharList(new CharListRequest { accountId = AccountId, world = world, channel = channel });
        appearances.Clear();
        foreach (var rec in list.characters)
        {
            var full = Net.CharSelect(new CharSelectRequest { accountId = AccountId, characterId = (int)rec.id });
            if (full.ok && full.character != null)
                appearances[rec.id] = WzCharacter.CharacterIds(rec, full.character.inventory);
        }
        return list;
    }

    public static string[] PreviewAppearance(CharRecord rec) => appearances[rec.id];

    public static CheckCharNameResponse CheckCharName(string name)
        => Net.CheckCharName(new CheckCharNameRequest { name = name });

    /// <summary>只判格式（2-12 位中文、英文或数字），不查重 —— 建号界面边打字边提示用。</summary>
    public static bool NameFormatOk(string name) => CharNameRules.Valid(name);

    public static CreateCharResponse CreateChar(CreateCharRequest req)
    {
        req.accountId = AccountId;
        req.world = World;
        return Net.CreateChar(req);
    }

    public static bool DeleteChar(int characterId)
        => Net.DeleteChar(new DeleteCharRequest { accountId = AccountId, characterId = characterId }).ok;

    // —————————————————— 死亡 / 复活（需求 3）——————————————————

    /// <summary>上报死亡：服务端按 Character.playerDead 结算经验惩罚并消耗护符。</summary>
    public static PlayerDeadResponse PlayerDead(int mapId)
    {
        if (!InGame) return new PlayerDeadResponse();
        var res = Net.PlayerDead(new PlayerDeadRequest { characterId = CharacterId, mapId = mapId });
        // 同步回本地存档，避免技能窗/经验条显示旧值
        if (res != null && res.expLost > 0)
        {
            var d = Profile.Data;
            d.experience = Math.Max(0L, d.experience - res.expLost);
            Profile.Save();
        }
        return res ?? new PlayerDeadResponse();
    }

    /// <summary>只看目标地图，不落盘：给确认弹窗显示「复活地点」用。</summary>
    public static int PeekRespawnMap(int mapId)
    {
        if (Server == null || Server.MapInfo == null) return -1;
        return DeathService.RespawnTargetMap(mapId, Server.MapInfo);
    }

    /// <summary>确认复活：走 ChangeMap(fromDying=1)，血量由服务端按 respawn 规则给。</summary>
    public static ChangeMapResponse Respawn(bool usedSafetyCharm)
    {
        if (!InGame) return new ChangeMapResponse { ok = false };
        var res = Net.ChangeMap(new ChangeMapRequest
        {
            characterId = CharacterId,
            fromDying = true,
            usedSafetyCharm = usedSafetyCharm,
        });
        if (res != null && res.ok && Character != null)
        {
            Character.record.map = res.mapId;
            Character.record.hp = res.hp;
            Character.record.mp = res.mp;
        }
        return res ?? new ChangeMapResponse { ok = false };
    }

    public static void BeginSession(int accountId, string accountName, int world, int channel)
    {
        AccountId = accountId;
        AccountName = accountName;
        World = world;
        Channel = channel;
    }

    public static bool SelectChar(int characterId)
    {
        var res = Net.CharSelect(new CharSelectRequest { accountId = AccountId, characterId = characterId });
        if (!res.ok || res.character == null) return false;
        CharacterId = characterId;
        Character = res.character;
        BottomBar.ResetChat();
        // 源端进游戏后聊天窗的第一行（2026-10-27 展开面板截图）：
        //   [欢迎] 欢迎光临冒险岛ONLINE世界。
        // 服务端 Java/i18n/脚本与客户端字符串池都搜不到这条文案（三种编码 0 命中），
        // 属于源端新手引导链路的客户端侧问候；这里进游戏时按同样的文案与黄色补上。
        BottomBar.AddChat("[欢迎] 欢迎光临冒险岛ONLINE世界。", BottomBar.ChatYellow);
        NativeSocialWindow.Reset();
        NativeCommunityWindow.Reset();
        NativeMedalWindow.Visible = false;
        NativeCashWindow.Reset();
        NativeMapWindows.WorldVisible = false;
        NativeMonsterBook.Visible = false;
        InGame = true;
        ApplyToProfile();
        KeyMap.LoadCharacter(Character.keymap);
        return true;
    }

    /// <summary>只放开当前角色、保留账号登录态（退出战斗回选角用）。</summary>
    public static void DeselectChar()
    {
        if (InGame) SaveToDb();
        NativeCashWindow.Reset();
        Net?.EndSocialSession(CharacterId);
        InGame = false;
        Character = null;
        CharacterId = 0;
    }

    public static void Logout()
    {
        if (InGame) SaveToDb();
        NativeCashWindow.Reset();
        Net?.EndSocialSession(CharacterId);
        InGame = false;
        Character = null;
        CharacterId = 0;
        if (Server != null && AccountId > 0) LoginService.Logout(Server.Db, AccountId);
        AccountId = 0;
        AccountName = "";
    }

    // —————————————————— DB → 现有玩法数据 ——————————————————

    public static void ApplyToProfile()
    {
        var d = Profile.Data;
        var rec = Character.record;
        d.playerName = rec.name;
        d.level = rec.level;
        d.experience = rec.exp;
        d.attributes.str = rec.str;
        d.attributes.dex = rec.dex;
        d.attributes.intelligence = rec.int_;
        d.attributes.luk = rec.luk;
        d.baseMaxHp = rec.maxhp;
        d.baseMaxMp = rec.maxmp;
        d.attributes.maxHp = rec.maxhp;
        d.attributes.maxMp = rec.maxmp;
        d.ap = rec.ap;
        d.SetJob(rec.job);
        d.sp = rec.GetSp(JobDefs.SkillBookOf(rec.job));
        d.gold = rec.meso;
        d.equipmentSlots = rec.equipslots;
        d.hair = HairFromId(rec.hair);
        Profile.LastMapId = rec.map;

        ApplyInventoryToProfile();

        // 任务状态（服务端随角色一起下发，客户端不直接查表）
        d.questStarted.Clear();
        d.questCompleted.Clear();
        d.questCompletedAt.Clear();
        foreach (var q in Character.quests)
        {
            if (q.completed > 0 || q.status == 2) d.SetQuestCompleted(q.quest);
            else if (q.status == 1) d.SetQuestStarted(q.quest);
        }

        // 技能等级
        SkillProgress.ClearAll();
        foreach (var s in Character.skills)
            if (s.skillLevel > 0) SkillProgress.SetLearned(s.skillId, s.skillLevel);

        Profile.Save();
    }

    public static void ApplyInventoryToProfile()
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        string previousAppearance = player != null ? WzCharacter.CurrentAppearanceKey() : null;
        var d=Profile.Data;
        // 共享 Profile 只是当前角色的镜像，切角时必须清掉上一人的装备。
        foreach (int slot in EquipSlot.Worn) d.SetEquipSlot(slot, 0);
        d.owned.Clear();

        // 背包（ETC/USE 等非装备物品同步到现有 itemIds/itemCounts）
        d.itemIds.Clear();
        d.itemCounts.Clear();
        foreach (var it in Character.inventory)
        {
            // 装备：EQUIPPED 行（源端 inventoryitems 的 position 为 -1/-5/-6/-7/-8/-9/-10/-11）。
            // 以当前角色的数据库装备为准。
            if (it.inventoryType == InventoryDao.Equipped && it.position < 0)
            {
                int pos = it.position;
                if (pos == -11 || IsEquipSlot(pos)) d.SetEquipSlot(pos, it.itemId);
                if (pos == -11) d.owned.Add(it.itemId.ToString());
                continue;
            }
            if (EquipSlot.SlotOf(it.itemId) == EquipSlot.Weapon) d.owned.Add(it.itemId.ToString());
            else d.AddItem(it.itemId, it.quantity);
        }

        // 装备窗读 Profile，场景精灵则缓存合成配方；同步后必须刷新变化的外观。
        if (player != null && previousAppearance != WzCharacter.CurrentAppearanceKey())
            player.RefreshFieldAppearance();
    }
    static void ApplyTradeInventory(Server.Bot.BotActor actor,int mesoDelta)
    {
        if (!InGame || Character==null || actor.id!=CharacterId) return;
        // Preserve XP, quests and gameplay earnings; only apply the committed trade delta.
        Profile.Data.gold=checked(Profile.Data.gold+mesoDelta);
        var inventory=new List<InventoryEntry>();
        foreach (var item in actor.character.inventory) inventory.Add(item.Copy());
        Character.inventory=inventory;
        Character.tradeRevision=actor.character.tradeRevision;
        ApplyInventoryToProfile(); Profile.Save();
    }

    static bool GrantTutorialAdmin(global::Server.Bot.BotActor actor)
    {
        if (!InGame || actor.id != CharacterId || Character == null) return false;
        var d = Profile.Data;
        // TutorialBot.grantAdmin: setLevel(149), one levelUp, then changeJob(412).
        d.level = 149;
        LevelSystem.SetLevel(d, 150);
        if (!JobSystem.ChangeJobById(412)) return false;
        Character.record.gm = 6;
        d.ap += 750; d.sp += 450;
        d.baseMaxHp = 10000; d.baseMaxMp = 10000;
        d.attributes.maxHp = 10000; d.attributes.maxMp = 10000;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        player?.ApplyProgression();
        if (player != null) { player.hp = 10000; player.mp = 10000; }
        actor.level = 150; actor.job = 412;
        Profile.Save(); SaveToDb();
        return true;
    }

    static void ApplyBotSkillEffect(global::Server.Bot.BotActor actor, int skillId)
    {
        if (!InGame || actor.id != CharacterId) return;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player != null)
            NativeBuffs.For(player).ApplySkill(skillId, SkillBook.MaxLevelOf(skillId), NativeBuffs.Now);
    }

    // —————————————————— 现有玩法数据 → DB ——————————————————

    public static void SaveToDb()
    {
        if (!InGame || Character == null) return;
        var d = Profile.Data;
        var rec = Character.record;
        rec.level = d.level;
        rec.exp = (int)Mathf.Min(int.MaxValue, d.experience);
        rec.str = d.attributes.str;
        rec.dex = d.attributes.dex;
        rec.int_ = d.attributes.intelligence;
        rec.luk = d.attributes.luk;
        rec.maxhp = d.baseMaxHp;
        rec.maxmp = d.baseMaxMp;
        // 当前 HP/MP 取运行时玩家实体（PlayerCtrl.hp/mp）
        var p = GameLoop.I != null ? GameLoop.I.player : null;
        rec.hp = p != null ? Mathf.Clamp(Mathf.RoundToInt(p.hp), 1, d.baseMaxHp) : d.baseMaxHp;
        rec.mp = p != null ? Mathf.Clamp(Mathf.RoundToInt(p.mp), 0, d.baseMaxMp) : d.baseMaxMp;
        rec.ap = d.ap;
        rec.job = d.jobId;
        rec.SetSp(JobDefs.SkillBookOf(d.jobId), d.sp);
        rec.meso = d.gold;
        rec.map = Profile.LastMapId;
        // 原始发型 ID 含发色，不能由旧版 h0/h1 标签反推覆盖。
        rec.equipslots = d.equipmentSlots;

        // 技能
        Character.skills.Clear();
        foreach (var kv in SkillProgress.AllLearned())
            Character.skills.Add(new SkillEntry { skillId = kv.Key, skillLevel = kv.Value, masterLevel = SkillBook.MasterCapOf(kv.Key) });

        // 任务（status 2=完成 / 1=进行中；已完成的不再记为进行中）
        Character.quests.Clear();
        foreach (var id in d.questCompleted)
            Character.quests.Add(new QuestEntry { quest = id, status = 2, completed = 1 });
        foreach (var id in d.questStarted)
            if (!d.questCompleted.Contains(id))
                Character.quests.Add(new QuestEntry { quest = id, status = 1, completed = 0 });

        Character.keymap = KeyMap.ExportCharacter();
        Net.SaveCharacter(new SaveCharacterRequest { characterId = CharacterId, character = Character });
    }

    // 装备槽位（源端 inventoryitems 的 position 为负值）：-1帽/-5衣/-6裤/-7鞋/-8手套/-9披风/-10盾
    static bool IsEquipSlot(int pos)
        => pos == -49 || pos == -1 || pos == -5 || pos == -6 || pos == -7 || pos == -8 || pos == -9 || pos == -10;

    // 发型 id（源端 30000~39999 含发色）↔ 客户端 h0/h1… 标签
    static string HairFromId(int id)
    {
        int baseHair = id / 10 * 10;
        int color = id % 10;
        return "h" + (baseHair % 100);
    }


}
