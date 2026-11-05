// 协议层：与源端 RecvOpcode 语义一一对应的请求/响应记录 + 传输抽象。
//
// 设计目标（需求 4：前后端分层，为联机预留）：
//   - 业务代码只面对这些 record，不面对字节流；将来联机时新增 SocketTransport
//     把 record 序列化即可，Server/Client 业务零改动。
//   - 单机默认 InProcessTransport：零拷贝、零网络、无权限要求。
using System;
using System.Collections.Generic;

// —— 登录态（源端 LoginServer 处理的包）——
public sealed class LoginRequest
{
    public string name;
    public string password;
}

public sealed class LoginResponse
{
    public LoginResultCode code;
    public int accountId;
    public string accountName;
    public bool justRegistered;
    /// <summary>封停到期时间（源端 getTempBan 把时间一起发下来）；空表示永久封停。</summary>
    public string banUntil;
    /// <summary>账号性别：10 = 还没选（源端 accounts.gender 默认值），客户端要弹性别框。</summary>
    public int gender = AccountsDao.GenderUnset;
}

// 与源端 SET_GENDER 包同义（SetGenderHandler）：confirmed 固定 0x01，后跟 0/1。
public sealed class SetGenderRequest { public int accountId; public int gender; }
public sealed class SetGenderResponse { public bool ok; public int gender; }

public sealed class ServerListRequest { public int accountId; }
public sealed class ServerListResponse { public List<WorldEntry> worlds = new List<WorldEntry>(); }

public sealed class CharListRequest { public int accountId; public int world; public int channel; }
public sealed class CharListResponse
{
    public int characterSlots;
    public List<CharRecord> characters = new List<CharRecord>();
}

public sealed class CheckCharNameRequest { public string name; }
public sealed class CheckCharNameResponse { public bool available; public int reason; } // 0 可用, -1 非法, -2 重名

// 与源端 CREATE_CHAR 包体同序
public sealed class CreateCharRequest
{
    public int accountId;
    public int world;
    public string name;
    public int job;          // 职业群：0 骑士团 / 1 冒险家 / 2 战神
    public int face, hair, hairColor, skinColor, top, bottom, shoes, weapon;
    public int gender;       // 0 男 / 1 女
}

public sealed class CreateCharResponse
{
    public int code;         // 0 成功；负数见 CharFactory
    public int characterId;
    public string error;
}

public sealed class DeleteCharRequest { public int accountId; public int characterId; }
public sealed class DeleteCharResponse { public bool ok; }

public sealed class CharSelectRequest { public int accountId; public int characterId; }
public sealed class CharSelectResponse
{
    public bool ok;
    public FullCharacter character;   // 源端 PLAYER_LOGGEDIN 下发的全量数据
}

// 旧存档迁移（一次性；源端没有对应包，本地单机专用）
public sealed class ImportLegacyRequest { public LegacyProfileSnapshot snapshot; }
public sealed class ImportLegacyResponse { public int characterId; }

// —— 频道态（源端 ChannelServer 处理的包，本项目用到的子集）——

// 死亡结算（源端 Character.playerDead() 由伤害处理器触发，本地由客户端在死亡动画结束后上报）
public sealed class PlayerDeadRequest
{
    public int characterId;
    public int mapId;
    // 护符数量由服务端从 inventoryitems 自己数（源端也是服务端判 haveItem），
    // 客户端不传，避免前后端状态不一致。
}

public sealed class PlayerDeadResponse
{
    public long expLost;
    public bool usedSafetyCharm;
    public bool noExpLoss;
}

public sealed class ChangeMapRequest
{
    public int characterId;
    public bool wheel;
    public int deathX,deathY;
    public bool fromDying;    // 源端 ChangeMapHandler:85  p.readByte()==1
    public bool usedSafetyCharm;   // 本次死亡是否消耗了护符（决定复活血量 30%）
    public int targetMapId;
    public string portalName;
}

public sealed class ChangeMapResponse
{
    public bool ok;
    public int mapId;
    public int spawnPoint;
    // 复活后的血量（源端 respawn 写在 Character 上，随 PLAYER_LOGGEDIN/换图包下发）
    public int hp, mp;
    public bool wheelUsed;
    public System.Collections.Generic.List<InventoryEntry> inventory;
    public bool respawned;      // true = 本次是死亡复活，客户端要播落地/无敌帧
}

// 存档：客户端把整份角色状态回传，服务端落库（源端是服务端自己持有状态，
// 本地单机为了不改现有玩法代码，走「下发整份 / 回传整份」的等价协议）。
public sealed class SaveCharacterRequest
{
    public int characterId;
    public FullCharacter character;
}

public interface ITransport
{
    LoginResponse Login(LoginRequest req);
    SetGenderResponse SetGender(SetGenderRequest req);
    ServerListResponse ServerList(ServerListRequest req);
    CharListResponse CharList(CharListRequest req);
    CheckCharNameResponse CheckCharName(CheckCharNameRequest req);
    CreateCharResponse CreateChar(CreateCharRequest req);
    DeleteCharResponse DeleteChar(DeleteCharRequest req);
    CharSelectResponse CharSelect(CharSelectRequest req);
    ImportLegacyResponse ImportLegacy(ImportLegacyRequest req);
    PlayerDeadResponse PlayerDead(PlayerDeadRequest req);
    ChangeMapResponse ChangeMap(ChangeMapRequest req);
    void SaveCharacter(SaveCharacterRequest req);
    ScriptResponse Script(ScriptRequest req);
    ScriptValueResponse ScriptValue(ScriptValueRequest req);
    SocialResponse Social(SocialRequest req);
    CommunityResponse Community(CommunityRequest req);
    BossResponse Boss(BossRequest req);
    CashResponse Cash(CashRequest req);
    bool HasNpcShop(int npcId);
    NpcShopResponse NpcShop(NpcShopRequest req);
    DueyResponse Duey(DueyRequest req);
    MerchantResponse Merchant(MerchantRequest req);
    PlayerShopResponse PlayerShop(PlayerShopRequest req);
    MakerResponse Maker(MakerRequest req);
    RankingResponse Ranking(RankingRequest req);
    OwlResponse Owl(OwlRequest req);
    StorageResponse Storage(StorageRequest req);
    MacroResponse Macro(MacroRequest req);
    ReportResponse Report(ReportRequest req);
    ArenaResponse Arena(ArenaRequest req);
    RpsResponse Rps(RpsRequest req);
    MemoResponse Memo(MemoRequest req);
    TvResponse Tv(TvRequest req);
    CarnivalResponse Carnival(CarnivalRequest req);
    Server.Bot.PartyGrindResponse PartyGrind(Server.Bot.PartyGrindRequest req);
    Server.Bot.Trade.BotTradeResponse BotTrade(Server.Bot.Trade.BotTradeRequest req);
    Server.Bot.MarketResponse Market(Server.Bot.MarketRequest req);
    InventoryChangeResponse ChangeInventory(InventoryChangeRequest req);
    EquipPatchResponse UpdateEquip(EquipPatchRequest req);
    StarForceResponse StarForce(StarForceRequest req);
    PotentialResponse Potential(PotentialRequest req);
    SymbolResponse Symbol(SymbolRequest req);
    WeeklyGemstoneResponse WeeklyGemstone(WeeklyGemstoneRequest req);
    HyperStatResponse HyperStat(HyperStatRequest req);
    AbilityResponse InnerAbility(AbilityRequest req);
    FlameResponse Flame(FlameRequest req);
    SoulResponse Soul(SoulRequest req);
    SlotRemoveResponse RemoveSlot(SlotRemoveRequest req);
    VMatrixResponse VMatrix(VMatrixRequest req);
    HexaResponse Hexa(HexaRequest req);
    HexaConvertResponse HexaConvert(HexaConvertRequest req);
    void EndSocialSession(int characterId);
}

public sealed class HexaConvertRequest
{
    public int characterId, itemId, count;
}

public sealed class HexaConvertResponse
{
    public bool ok = true;
    public string error = "";
    public long solErda;
}

/// <summary>
/// 进程内传输：把协议直接路由到 GameServer（本地单机模式）。
/// 联机时替换为实现同接口的 SocketTransport。
/// </summary>
public sealed class InProcessTransport : ITransport
{
    readonly GameServer server;
    public InProcessTransport(GameServer s) { server = s; }
    public ScriptValueResponse ScriptValue(ScriptValueRequest r) => server.ScriptValues.Handle(r);
    public ScriptResponse Script(ScriptRequest r) => server.Scripts.Handle(r);
    public SocialResponse Social(SocialRequest r) => server.Social.Handle(r);
    public Server.Bot.PartyGrindResponse PartyGrind(Server.Bot.PartyGrindRequest r) => Server.Bot.BotPartyGrindProtocol.Handle(server.Bots,r);
    public Server.Bot.Trade.BotTradeResponse BotTrade(Server.Bot.Trade.BotTradeRequest r) => server.Bots.tradeProtocol.Handle(r);
    public Server.Bot.MarketResponse Market(Server.Bot.MarketRequest r) => server.Bots.marketProtocol.Handle(r);
    public CommunityResponse Community(CommunityRequest r) => server.Community.Handle(r);
    public BossResponse Boss(BossRequest r) => server.Bosses.Handle(r);
    public InventoryChangeResponse ChangeInventory(InventoryChangeRequest r) => NativeInventoryService.Handle(server.Db, r);
    public EquipPatchResponse UpdateEquip(EquipPatchRequest r) => NativeEquipService.Patch(server.Db, r);
    public StarForceResponse StarForce(StarForceRequest r)
    {
        lock(server.Db)
        {
            if(r!=null&&server.ActiveBotPlayer(r.characterId)?.trade!=null)
                return new StarForceResponse {ok=false,error="交易中不能强化装备"};
            var response=NativeStarForceService.Handle(server.Db,r);
            if(response.ok&&r.commit)RefreshEnhancementMirror(r.characterId);
            return response;
        }
    }
    public PotentialResponse Potential(PotentialRequest r)
    {
        lock(server.Db)
        {
            if(r!=null&&server.ActiveBotPlayer(r.characterId)?.trade!=null)
                return new PotentialResponse {ok=false,error="交易中不能变更潜能"};
            var response=NativePotentialService.Handle(server.Db,r);
            if(response.ok&&r.commit)RefreshEnhancementMirror(r.characterId);
            return response;
        }
    }
    public WeeklyGemstoneResponse WeeklyGemstone(WeeklyGemstoneRequest r) => NativeWeeklyGemstoneService.Handle(server.Db,r);
    public SymbolResponse Symbol(SymbolRequest r)
    {
        lock(server.Db)
        {
            if(r!=null&&server.ActiveBotPlayer(r.characterId)?.trade!=null)
                return new SymbolResponse {ok=false,error="交易中不能强化徽章"};
            var response=NativeSymbolService.Handle(server.Db,r);
            if(response.ok&&r.commit)RefreshEnhancementMirror(r.characterId);
            return response;
        }
    }
    public HyperStatResponse HyperStat(HyperStatRequest r)
    {
        lock(server.Db)
        {
            var response=NativeHyperStatService.Handle(server.Db,r);
            if(response.ok&&r.commit)RefreshGrowthMirror(r.characterId);
            return response;
        }
    }
    public AbilityResponse InnerAbility(AbilityRequest r)
    {
        lock(server.Db)
        {
            var response=NativeInnerAbilityService.Handle(server.Db,r);
            if(response.ok&&r.commit)RefreshGrowthMirror(r.characterId);
            return response;
        }
    }
    public FlameResponse Flame(FlameRequest r)
    {
        lock(server.Db)
        {
            if(r!=null&&server.ActiveBotPlayer(r.characterId)?.trade!=null)
                return new FlameResponse {ok=false,error="交易中不能使用星火"};
            var response=NativeFlameService.Handle(server.Db,r);
            if(response.ok&&r.commit)RefreshEnhancementMirror(r.characterId);
            return response;
        }
    }
    public SoulResponse Soul(SoulRequest r)
    {
        lock(server.Db)
        {
            if(r!=null&&server.ActiveBotPlayer(r.characterId)?.trade!=null)
                return new SoulResponse {ok=false,error="交易中不能变更灵魂"};
            var response=NativeSoulService.Handle(server.Db,r);
            if(response.ok&&r.commit)RefreshEnhancementMirror(r.characterId);
            return response;
        }
    }
    // 成长三表不挂在 inventory 上，单独把服务端内存里的角色刷一遍，
    // 否则客户端窗口读的还是旧预设/旧行（下次 SaveToDb 也会用旧值）。
    // 这里必须走 ActiveBotPlayer 而不是 GameSession —— Protocol 会被 tools/server-check
    // 直接编译（Client/ 不在工程里），碰 GameSession 立刻 CS0103。
    void RefreshGrowthMirror(int characterId)
    {
        var actor = server.ActiveBotPlayer(characterId);
        if (actor?.character == null) return;
        var fresh = CharService.Load(server.Db, characterId);
        if (fresh == null) return;
        actor.character.growth = fresh.growth;
        actor.character.hyperstats = fresh.hyperstats;
        actor.character.potentials = fresh.potentials;
        NativeGrowthStats.Invalidate();
    }
    void RefreshEnhancementMirror(int characterId)    {
        var actor=server.ActiveBotPlayer(characterId);
        if(actor?.character==null)return;
        var current=CharService.Load(server.Db,characterId);
        actor.character.inventory=current.inventory;
        actor.character.soulWeapon=current.soulWeapon;
        actor.character.record.meso=current.record.meso;
        NativeGrowthStats.Invalidate();
        // Reuse the existing inventory/economy revision gate in SynchronizePlayer
        // and SaveCharacter so an older client snapshot cannot refund a cube.
        actor.character.tradeRevision++;
    }
    public SlotRemoveResponse RemoveSlot(SlotRemoveRequest r) => NativeEquipService.RemoveSlot(server.Db, r);
    public VMatrixResponse VMatrix(VMatrixRequest r)
    {
        lock(server.Db) return NativeVMatrixService.Handle(server.Db,r);
    }
    public HexaResponse Hexa(HexaRequest r)
    {
        lock(server.Db) return NativeHexaMatrixService.Handle(server.Db,r);
    }
    public HexaConvertResponse HexaConvert(HexaConvertRequest r)
    {
        lock(server.Db)
        {
            if(r!=null&&server.ActiveBotPlayer(r.characterId)?.trade!=null)
                return new HexaConvertResponse {ok=false,error="交易中不能转换灵魂艾尔达"};
            var res=NativeHexaMatrixService.Convert(server.Db,r.characterId,r.itemId,r.count);
            if(res.ok)RefreshEnhancementMirror(r.characterId);
            return new HexaConvertResponse {ok=res.ok,error=res.error,solErda=res.solErda};
        }
    }
    public bool HasNpcShop(int npcId) => server.NpcShops.HasNpc(npcId);
    public NpcShopResponse NpcShop(NpcShopRequest r) => server.NpcShops.Handle(r);
    public DueyResponse Duey(DueyRequest r) => NativeDueyService.Handle(server.Db, r);
    public MerchantResponse Merchant(MerchantRequest r) => NativeMerchantService.Handle(server.Db, r);
    public PlayerShopResponse PlayerShop(PlayerShopRequest r) => NativePlayerShopService.Handle(server.Db, r);
    public MakerResponse Maker(MakerRequest r) => NativeMakerService.Handle(server.Db, r);
    public RankingResponse Ranking(RankingRequest r) => NativeRankingService.Handle(server.Db, r);
    public OwlResponse Owl(OwlRequest r) => NativeOwlService.Handle(server.Db, r);
    public StorageResponse Storage(StorageRequest r) => NativeStorageService.Handle(server.Db, r);
    public MacroResponse Macro(MacroRequest r) => NativeSkillMacroService.Handle(server.Db, r);
    public ReportResponse Report(ReportRequest r) => NativeReportService.Handle(server.Db, r);
    public ArenaResponse Arena(ArenaRequest r) => NativeEventArenaService.Handle(server.Db, r);
    public RpsResponse Rps(RpsRequest r) => NativeRpsService.Handle(server.Db, r);
    public MemoResponse Memo(MemoRequest r) => NativeNoteService.Handle(server.Db, r);
    public TvResponse Tv(TvRequest r) => NativeMapleTvService.Handle(server.Db, r);
    public CarnivalResponse Carnival(CarnivalRequest r) => NativeCarnivalService.Handle(server.Db, r);
    public CashResponse Cash(CashRequest r) => server.Cash.Handle(r);
    public void EndSocialSession(int characterId) { NativePetItems.EndSession(server.Db,characterId); NativePlayerShopService.Logout(server.Db,characterId); NativeReportService.EndSession(characterId); server.EndBotSession(characterId); server.Scripts.EndSession(characterId); server.Social.EndSession(characterId); server.Community.EndSession(characterId); server.Cash.EndSession(characterId); server.NpcShops.EndSession(characterId); }

    public LoginResponse Login(LoginRequest r)
    {
        var res = LoginService.Login(server.Db, r.name, r.password);
        return new LoginResponse { code = res.code, accountId = res.accountId, accountName = res.accountName,
            justRegistered = res.justRegistered, banUntil = res.banUntil, gender = res.gender };
    }
    public SetGenderResponse SetGender(SetGenderRequest r)
    {
        int g = LoginService.SetGender(server.Db, r.accountId, r.gender);
        if (g == r.gender) server.Db.Save();
        return new SetGenderResponse { ok = g == r.gender, gender = g };
    }
    public ServerListResponse ServerList(ServerListRequest r) => new ServerListResponse { worlds = WorldService.Worlds(server.Db) };
    public CharListResponse CharList(CharListRequest r) => new CharListResponse
    {
        characterSlots = AccountsDao.CharacterSlots(server.Db, r.accountId),
        characters = CharactersDao.ByAccount(server.Db, r.accountId, r.world),
    };
    public CheckCharNameResponse CheckCharName(CheckCharNameRequest r)
    {
        if (!CharactersDao.CanCreateChar(r.name)) return new CheckCharNameResponse { available = false, reason = -1 };
        if (CharactersDao.NameTaken(server.Db, r.name)) return new CheckCharNameResponse { available = false, reason = -2 };
        return new CheckCharNameResponse { available = true, reason = 0 };
    }
    public CreateCharResponse CreateChar(CreateCharRequest r)
    {
        var res = CharFactory.CreateCharacter(server.Db, r.accountId, r.world, r.name, r.job,
            r.face, r.hair, r.hairColor, r.skinColor, r.top, r.bottom, r.shoes, r.weapon, r.gender);
        if (res.code == 0) server.Db.Save();
        return new CreateCharResponse { code = res.code, characterId = res.characterId, error = res.error };
    }
    public DeleteCharResponse DeleteChar(DeleteCharRequest r)
    {
        // 源端 DeleteCharHandler 会校验 accountid == character.accountid（防止删别人角色）
        var rec = CharactersDao.FindById(server.Db, r.characterId);
        if (rec == null || rec.accountid != r.accountId) return new DeleteCharResponse { ok = false };
        CharactersDao.Delete(server.Db, r.characterId);
        server.Db.Save();
        return new DeleteCharResponse { ok = true };
    }
    public CharSelectResponse CharSelect(CharSelectRequest r)
    {
        var full = CharService.Load(server.Db, r.characterId);
        if (full == null) return new CharSelectResponse { ok = false };
        server.Community.BeginSession(r.characterId);
        return new CharSelectResponse { ok = true, character = full };
    }
    public ImportLegacyResponse ImportLegacy(ImportLegacyRequest r)
        => new ImportLegacyResponse { characterId = LegacyImport.Import(server.Db, r.snapshot) };

    public PlayerDeadResponse PlayerDead(PlayerDeadRequest r)
    {
        var rec = CharactersDao.FindById(server.Db, r.characterId);
        if (rec == null) return new PlayerDeadResponse();
        int charms = InventoryDao.CountOf(server.Db, (int)rec.id, DeathService.SafetyCharm);
        var res = DeathService.PlayerDead(rec, r.mapId, server.MapInfo, charms);
        if (res.expLost > 0)
        {
            rec.exp = Math.Max(0, rec.exp - res.expLost);
            CharactersDao.Save(server.Db, rec);
        }
        if (res.usedSafetyCharm)
        {
            // 护符消耗 1 个（源端 7497-7511：haveItem(SAFETY_CHARM) → gainItem(-1)）
            InventoryDao.ConsumeItem(server.Db, (int)rec.id, DeathService.SafetyCharm, 1);
        }
        server.Db.Save();
        return new PlayerDeadResponse
        {
            expLost = res.expLost,
            usedSafetyCharm = res.usedSafetyCharm,
            noExpLoss = res.noExpLoss,
        };
    }

    public ChangeMapResponse ChangeMap(ChangeMapRequest r)
    {
        var rec = CharactersDao.FindById(server.Db, r.characterId);
        if (rec == null) return new ChangeMapResponse { ok = false };
        int target = r.targetMapId;
        if (r.fromDying)
        {
            if(rec.hp<=0 && r.wheel && InventoryDao.CountOf(server.Db,r.characterId,DeathService.WheelOfFortune)>0)
            {
                InventoryDao.ConsumeItem(server.Db,r.characterId,DeathService.WheelOfFortune,1);
                rec.hp=50;
                rec.spawnpoint=(server.MapInfo as IRespawnMapInfo)?.ClosestSpawn(rec.map,r.deathX,r.deathY) ?? 0;
                CharactersDao.Save(server.Db,rec);server.Db.Save();
                return new ChangeMapResponse {ok=true,mapId=rec.map,spawnPoint=rec.spawnpoint,hp=rec.hp,mp=rec.mp,respawned=true,wheelUsed=true,
                    inventory=CharService.Load(server.Db,r.characterId).inventory};
            }
            // 源端 ChangeMapHandler:99-118：死亡确认 → respawn(returnMap)
            target = DeathService.RespawnTargetMap(rec.map, server.MapInfo);
            int maxHp = rec.maxhp, maxMp = rec.maxmp;
            rec.hp = DeathService.RespawnHp(target, r.usedSafetyCharm, maxHp);
            rec.mp = DeathService.RespawnMp(target, r.usedSafetyCharm, maxMp);
            rec.map = target;
            rec.spawnpoint = 0;
            CharactersDao.Save(server.Db, rec);
            server.Db.Save();
            return new ChangeMapResponse
            {
                ok = true, mapId = target, spawnPoint = rec.spawnpoint,
                hp = rec.hp, mp = rec.mp, respawned = true,
            };
        }

        rec.map = target;
        CharactersDao.Save(server.Db, rec);
        server.Db.Save();
        return new ChangeMapResponse
        {
            ok = true, mapId = target, spawnPoint = rec.spawnpoint,
            hp = rec.hp, mp = rec.mp,
        };
    }
    public void SaveCharacter(SaveCharacterRequest r)
    {
        if (r.character != null && r.character.record != null)
        {
            var stored = CharactersDao.FindById(server.Db, r.characterId);
            if (stored != null) {
                // Community state is server-owned and may have changed since the last snapshot.
                var incoming = r.character.record;
                incoming.guildid = stored.guildid; incoming.guildrank = stored.guildrank;
                incoming.familyId = stored.familyId; incoming.allianceRank = stored.allianceRank;
                incoming.party = stored.party; incoming.monsterbookcover = stored.monsterbookcover;
                var actor=server.ActiveBotPlayer(r.characterId);
                if (actor?.character!=null && (actor.trade!=null || r.character.tradeRevision<actor.character.tradeRevision))
                {
                    incoming.meso=stored.meso;
                    r.character.inventory=CharService.Load(server.Db,r.characterId).inventory;
                    r.character.tradeRevision=actor.character.tradeRevision;
                }
                if (incoming.level > stored.level) server.Community.GiveReputation(r.characterId,
                    (incoming.level - stored.level) * GameConfigDao.GetInt(server.Db, "family_rep_per_level_up", 200));
            }
            CharService.Save(server.Db, r.character);
            server.Db.Save();
            return;
        }
        // 只给 id 的兼容路径：重读一份再存（等价于「服务端持状态」）
        var full = CharService.Load(server.Db, r.characterId);
        if (full == null) return;
        CharService.Save(server.Db, full);
        server.Db.Save();
    }
}
