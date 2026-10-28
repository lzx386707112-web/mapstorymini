// 服务端离线自检：不依赖 Unity，直接跑 登录→建号→读档→死亡→复活→持久化 全链路。
// 运行：dotnet run --project tools/server-check
using System;
using System.Collections.Generic;
using System.IO;

public static class Program
{
    static int failed = 0;
    static int passed = 0;

    static void Check(bool cond, string what)
    {
        if (cond) { passed++; Console.WriteLine("  ✓ " + what); }
        else { failed++; Console.WriteLine("  ✗ " + what); }
    }

    // 测试用地图信息：10000=城镇(蘑菇村), 100020000=野外,  XXXXX=NO_EXP_DECREASE
    sealed class TestMapInfo : IMapInfo
    {
        public bool IsTown(int mapId) => mapId == 10000;
        public int ReturnMap(int mapId) => mapId == 100020000 ? 10000 : (mapId == 10000 ? 999999999 : 10000);
        public bool NoExpDecrease(int mapId) => mapId == 980000000;
    }

    public static int Main()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "mapstory_test.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);

        Console.WriteLine("== 1. Schema ==");
        var db = DbStore.Open(dbPath);
        Check(db.Table("keymap").Col("key") != null, "keymap.key 不能误识别为索引声明");
        Check(db.HasTable("accounts"), "accounts 表存在");
        Check(db.HasTable("characters"), "characters 表存在");
        Check(db.Table("characters").Col("damageCap") != null, "characters.damageCap（V2.1.68 ALTER）");
        Check(db.Table("characters").Col("fishLevel") != null, "characters.fishLevel（V1.9.9 ALTER）");
        Check(db.Table("characters").Col("sp").defaultValue as string == "0,0,0,0,0,0,0,0,0,0", "sp 默认 10 段");
        Check(db.Table("accounts").Col("characterslots").defaultValue as long? == 3, "characterslots 默认 3");
        Check(db.HasTable("inventoryequipment") && db.HasTable("inventoryitems"), "背包两表");
        Check(db.HasTable("keymap") && db.HasTable("quickslotkeymapped") && db.HasTable("skills"), "键位/快捷栏/技能表");

        Console.WriteLine("== 2. 登录 / 自动注册 ==");
        var login = LoginService.Login(db, "tester", "pw123456");
        Check(login.code == LoginResultCode.Ok && login.justRegistered, "首次登录自动注册");
        var again = LoginService.Login(db, "tester", "wrongpw");
        Check(again.code == LoginResultCode.WrongPassword, "错误密码拒绝");
        // 编号必须与源端 Client.login / LoginPasswordHandler 发出的 LOGIN_STATUS reason 对齐，
        // 客户端就是按这个字节挑 UI/Login.img/Notice/text/<n> 提示图的（密码错误 = 4，不是 6）。
        Check((int)LoginResultCode.Ok == 0, "登录码 Ok=0");
        Check((int)LoginResultCode.Banned == 2, "登录码 Banned=2（getPermBan/getTempBan）");
        Check((int)LoginResultCode.Blocked == 3, "登录码 Blocked=3（IP/MAC 封禁）");
        Check((int)LoginResultCode.WrongPassword == 4, "登录码 WrongPassword=4");
        Check((int)LoginResultCode.NotRegistered == 5, "登录码 NotRegistered=5");
        Check((int)LoginResultCode.Error == 6, "登录码 Error=6");
        Check((int)LoginResultCode.AlreadyLoggedIn == 7, "登录码 AlreadyLoggedIn=7");
        Check((int)LoginResultCode.Invalid == 14, "登录码 Invalid=14");
        var relogin = LoginService.Login(db, "tester", "pw123456");
        Check(relogin.code == LoginResultCode.Ok && !relogin.justRegistered, "再次登录成功");
        int accId = relogin.accountId;

        Console.WriteLine("== 3. 建号事务 ==");
        Check(CharNameRules.Valid("中文"), "两字中文名允许");
        Check(CharNameRules.Valid("中文Ab12"), "中英文数字混合名允许");
        Check(CharNameRules.Valid("一二三四五六七八九十一二"), "12 字中文名允许");
        Check(!CharNameRules.Valid("一二三四五六七八九十一二三"), "13 字中文名拒绝");
        Check(!CharNameRules.Valid("名字！"), "中文标点拒绝");
        // 默认外观：男 + 脸20000 + 发30300 + 发色0 + 肤0 + 衣1040002 + 裤1060002 + 鞋1072001 + 剑1302000
        var create = CharFactory.CreateCharacter(db, accId, 0, "HeroOne", CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
        Check(create.code == 0, "冒险家建号成功");
        var chars = CharactersDao.ByAccount(db, accId, 0);
        Check(chars.Count == 1, "角色列表 1 个");
        var c0 = chars[0];
        Check(c0.job == 0 && c0.level == 1, "job=BEGINNER, level=1");
        Check(c0.str == 12 && c0.dex == 5 && c0.int_ == 4 && c0.luk == 4, "四维 12/5/4/4");
        Check(c0.hp == 50 && c0.mp == 5, "HP50/MP5");
        Check(c0.map == 10000, "初始地图 蘑菇村 10000");
        Check(c0.hair == 30300, "hair=发型+发色");
        Check(KeymapDao.ByCharacter(db, (int)c0.id).Count == 40, "默认键位已写入(40 项)");
        var items = InventoryDao.ItemsByCharacter(db, (int)c0.id);
        Check(items.Count == 5, "背包 5 件（衣/裤/鞋/武器/指南）");
        bool hasGuide = false, hasSword = false;
        foreach (var it in items)
        {
            if (Convert.ToInt64(it["itemid"]) == 4161001) hasGuide = true;
            if (Convert.ToInt64(it["itemid"]) == 1302000 && Convert.ToInt64(it["position"]) == -11) hasSword = true;
        }
        Check(hasGuide, "新手指南 4161001 在 ETC");
        Check(hasSword, "武器 1302000 在位置 -11");

        Console.WriteLine("== 4. 建号校验 ==");
        Check(CharFactory.CreateCharacter(db, accId, 0, "BadFace", CharFactory.JobGroupAdventurer,
            99999, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0).code != 0, "非法脸型拒绝");
        Check(CharFactory.CreateCharacter(db, accId, 0, "Bad Name!", CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0).code != 0, "中文、英文、数字以外拒绝");
        Check(CharFactory.CreateCharacter(db, accId, 0, "A", CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0).code != 0, "少于 2 位拒绝");
        Check(CharFactory.CreateCharacter(db, accId, 0, "HeroOne", CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0).code != 0, "重名拒绝");
        // 第 2、3 个角色（女性：脸 21001 / 发 31010 / 发色 2）
        for (int i = 2; i <= 3; i++)
        {
            var r = CharFactory.CreateCharacter(db, accId, 0, "Hero" + i, CharFactory.JobGroupAdventurer,
                21001, 31040, 2, 1, 1041006, 1061008, 1072005, 1322005, 1);
            Check(r.code == 0, "建号 " + i + " 成功");
        }
        Check(CharFactory.CreateCharacter(db, accId, 0, "HeroFour", CharFactory.JobGroupAdventurer,
            21001, 31040, 2, 1, 1041006, 1061008, 1072005, 1322005, 1).code == CharFactory.ErrNoSlot, "第 4 个角色位已满");
        // 骑士团
        var cyg = CharFactory.CreateCharacter(db, accId, 0, "CygnusA", CharFactory.JobGroupCygnus,
            21000, 31000, 0, 0, 1041002, 1061002, 1072001, 1302000, 1);
        Check(cyg.code == CharFactory.ErrNoSlot, "骑士团也占角色位（已满）");

        Console.WriteLine("== 5. 读档全量 ==");
        var full = CharService.Load(db, (int)c0.id);
        Check(full != null && full.record.name == "HeroOne", "CharSelect 读档");
        Check(full.skills.Count == 0, "新号无技能");
        Check(full.inventory.Count == 5, "背包 5 件读回");
        Check(full.keymap.Count == 40, "键位读回(40 项)");
        Check(full.record.sp == "0,0,0,0,0,0,0,0,0,0", "sp 串默认");

        var originalKeys = new List<KeymapEntry>(full.keymap);
        full.keymap.RemoveAll(k => k.type == 6 && k.action == 101);
        full.keymap.Add(new KeymapEntry { key = 30, type = 6, action = 101 });
        full.keymap.RemoveAll(k => k.type == 4 && k.action == 8);
        full.keymap.Add(new KeymapEntry { key = 16, type = 1, action = 1001004 });
        CharService.Save(db, full);
        var customKeys = CharService.Load(db, (int)c0.id).keymap;
        Check(customKeys.Count == 40, "键位保存覆盖原表，不累积旧行");
        Check(customKeys.Exists(k => k.key == 30 && k.type == 6 && k.action == 101), "表情改绑保存/重读");
        Check(!customKeys.Exists(k => k.key == 60), "表情原 F2 绑定已移除");
        Check(customKeys.Exists(k => k.key == 16 && k.type == 1 && k.action == 1001004), "技能绑定保存/重读");
        var secondKeys = CharService.Load(db, (int)CharactersDao.ByAccount(db, accId, 0)[1].id).keymap;
        Check(secondKeys.Exists(k => k.key == 60 && k.type == 6 && k.action == 101), "其他角色仍保留默认表情绑定");
        var damagedId = (int)CharactersDao.ByAccount(db, accId, 0)[1].id;
        var damagedRows = db.Table("keymap").Select(DbStore.Eq("characterid", (long)damagedId));
        foreach (var row in damagedRows) row.Remove("key");
        Check(CharService.Load(db, damagedId).keymap.Exists(k => k.key == 60 && k.type == 6 && k.action == 101),
            "旧存档缺失 key 列时修复为源端默认键位");
        KeymapDao.Save(db, damagedId, new List<KeymapEntry>());
        Check(CharService.Load(db, damagedId).keymap.Count == 0, "主动清空的键位表不被修复逻辑重置");
        full.keymap = originalKeys;
        CharService.Save(db, full);

        Console.WriteLine("== 6. 技能 / SP ==");
        SkillsDao.Upsert(db, (int)c0.id, 1001003, 5, 20);
        SkillsDao.Upsert(db, (int)c0.id, 1001003, 6, 20);   // 覆盖写，不重复行
        var sk = SkillsDao.ByCharacter(db, (int)c0.id);
        Check(sk.Count == 1 && Convert.ToInt64(sk[0]["skilllevel"]) == 6, "技能 upsert");
        c0.SetSp(1, 61);
        Check(c0.GetSp(1) == 61 && c0.sp.Split(',').Length == 10, "sp 按技能书分组");

        Console.WriteLine("== 7. 死亡 / 复活规则 ==");
        var mapInfo = new TestMapInfo();
        var lowbie = CharactersDao.FindById(db, (int)c0.id); lowbie.exp = 1000; lowbie.level = 10;
        // 7.1 新手不扣
        var d1 = DeathService.PlayerDead(lowbie, 100020000, mapInfo, 0);
        Check(d1.expLost == 0 && d1.noExpLoss, "新手不扣经验");
        // 7.2 城镇 1%
        var adv = CharactersDao.FindById(db, (int)c0.id); adv.job = 100; adv.exp = 1000; adv.level = 10;
        int need = ServerExpTable.GetExpNeededForLevel(adv.level);
        var d2 = DeathService.PlayerDead(adv, 10000, mapInfo, 0);
        Check(d2.expLost == need / 100, "城镇扣 1%（" + need / 100 + "）");
        // 7.3 野外 LUK<50 扣 10%
        var d3 = DeathService.PlayerDead(adv, 100020000, mapInfo, 0);
        Check(d3.expLost == need / 10, "野外扣 10%（LUK<50）");
        // 7.4 野外 LUK>=50 扣 20%
        adv.luk = 50;
        var d4 = DeathService.PlayerDead(adv, 100020000, mapInfo, 0);
        Check(d4.expLost == need / 20, "野外扣 20%（LUK>=50）");
        // 7.5 护符免扣
        var d5 = DeathService.PlayerDead(adv, 100020000, mapInfo, 1);
        Check(d5.usedSafetyCharm && d5.expLost == 0, "安全护符免扣");
        // 7.6 经验不足扣全部
        adv.exp = 3; adv.luk = 4;
        var d6 = DeathService.PlayerDead(adv, 100020000, mapInfo, 0);
        Check(d6.expLost == 3, "经验不足扣全部");
        // 7.7 NO_EXP_DECREASE 地图
        adv.exp = 5000;
        var d7 = DeathService.PlayerDead(adv, 980000000, mapInfo, 0);
        Check(d7.expLost == 0, "NO_EXP_DECREASE 地图不扣");
        // 7.8 复活血量
        Check(DeathService.RespawnHp(10000, false, 500) == 50, "普通复活 HP=50");
        Check(DeathService.RespawnHp(262030300, false, 500) == 500, "特殊图满血");
        Check(DeathService.RespawnHp(10000, true, 500) == 150, "护符复活 30%");
        // 7.9 复活目标地图
        Check(DeathService.RespawnTargetMap(100020000, mapInfo) == 10000, "野外死亡回 10000");
        Check(DeathService.RespawnTargetMap(10000, mapInfo) == 10000, "无 returnMap 时原地");

        Console.WriteLine("== 8. 持久化 round-trip ==");
        CharactersDao.Save(db, adv);
        db.Save();
        var db2 = DbStore.Open(dbPath);
        var back = CharactersDao.FindById(db2, (int)c0.id);
        Check(back != null && back.name == "HeroOne", "重开库后角色在");
        Check(back.exp == adv.exp && back.luk == adv.luk, "存档字段一致");
        Check(AccountsDao.FindByName(db2, "tester") != null, "账号在");
        var pwOk = AccountsDao.VerifyPassword("pw123456", (string)AccountsDao.FindByName(db2, "tester")["password"]);
        Check(pwOk, "密码哈希可校验");
        Check(SkillsDao.ByCharacter(db2, (int)c0.id).Count == 1, "技能持久化");

        Console.WriteLine("== 9. 删除角色 ==");
        var third = CharactersDao.ByAccount(db2, accId, 0).Find(x => x.name == "Hero3");
        CharactersDao.Delete(db2, (int)third.id);
        db2.Save();
        Check(CharactersDao.FindById(db2, (int)third.id) == null, "角色删除");
        Check(CharactersDao.ByAccount(db2, accId, 0).Count == 2, "剩 2 个角色");

        // ——— 10. 走协议层的全链路（登录→选区→建号→选角→死亡→复活）———
        Console.WriteLine("== 10. ITransport 全链路 ==");
        string e2ePath = Path.Combine(Path.GetTempPath(), "mapstory_e2e.db");
        if (File.Exists(e2ePath)) File.Delete(e2ePath);
        var edb = DbStore.Open(e2ePath);
        var srv = GameServer.StartWith(edb);
        srv.MapInfo = new TestMapInfo();
        ITransport net = srv.Transport;

        var lg = net.Login(new LoginRequest { name = "e2e", password = "pw123456" });
        Check(lg.code == LoginResultCode.Ok && lg.accountId > 0, "登录（首次自动注册）");

        var worlds = net.ServerList(new ServerListRequest { accountId = lg.accountId }).worlds;
        Check(worlds.Count >= 1 && worlds[0].channelSize >= 1, "选区列表来自 world_prop");

        var mk = net.CreateChar(new CreateCharRequest
        {
            accountId = lg.accountId, world = worlds[0].id, name = "E2EHero",
            job = CharFactory.JobGroupAdventurer,
            face = 20000, hair = 30300, hairColor = 0, skinColor = 0,
            top = 1040002, bottom = 1060002, shoes = 1072001, weapon = 1302000, gender = 0,
        });
        Check(mk.code == 0, "建号成功");
        Check(net.CharList(new CharListRequest { accountId = lg.accountId, world = worlds[0].id }).characters.Count == 1,
            "角色列表 1 个");

        var sel = net.CharSelect(new CharSelectRequest { accountId = lg.accountId, characterId = mk.characterId });
        Check(sel.ok && sel.character != null, "选角下发全量数据");
        Check(sel.character.record.map == CharFactory.MapMushroomTown, "初始地图 10000（蘑菇村）");
        Check(sel.character.inventory.Count >= 5, "初始装备+新手指南已下发");

        // 死亡：先放到野外图再死
        var rec0 = CharactersDao.FindById(edb, mk.characterId);
        rec0.map = 100020000; rec0.level = 20; rec0.luk = 60; rec0.exp = 5000; rec0.job = 100;
        rec0.maxhp = 500; rec0.hp = 500;   // 让护符 30% 分支有可观测差异（30%*500=150）
        CharactersDao.Save(edb, rec0); edb.Save();

        var dead = net.PlayerDead(new PlayerDeadRequest { characterId = mk.characterId, mapId = 100020000 });
        Check(dead.expLost > 0 && !dead.noExpLoss, "野外死亡扣经验");
        var afterDead = CharactersDao.FindById(edb, mk.characterId);
        Check(afterDead.exp == 5000 - dead.expLost, "经验已落库");

        var raise = net.ChangeMap(new ChangeMapRequest { characterId = mk.characterId, fromDying = true, usedSafetyCharm = false });
        Check(raise.ok && raise.mapId == 10000, "复活回 returnMap=10000");
        Check(raise.respawned && raise.hp == 50, "普通复活 HP=50（源端 respawn）");
        Check(CharactersDao.FindById(edb, mk.characterId).map == 10000, "复活地图已落库");

        // 护符：先塞 1 个护符再死一次
        InventoryDao.InsertItem(edb, mk.characterId, DeathService.SafetyCharm, InventoryDao.Use, 1, 1);
        edb.Save();
        Check(InventoryDao.CountOf(edb, mk.characterId, DeathService.SafetyCharm) == 1, "背包里有 1 个护符");
        var dead2 = net.PlayerDead(new PlayerDeadRequest
        { characterId = mk.characterId, mapId = 100020000 });
        Check(dead2.usedSafetyCharm && dead2.expLost == 0, "护符免扣经验");
        Check(InventoryDao.CountOf(edb, mk.characterId, DeathService.SafetyCharm) == 0, "护符已消耗 1 个");
        var raise2 = net.ChangeMap(new ChangeMapRequest { characterId = mk.characterId, fromDying = true, usedSafetyCharm = true });
        Check(raise2.hp > 50, "护符复活按 30% 给血（" + raise2.hp + "）");

        edb.Save();
        File.Delete(e2ePath);

        Console.WriteLine("== 默认键盘功能服务 ==");
        string socialPath = Path.Combine(Path.GetTempPath(), "mapstory_social_" + Guid.NewGuid().ToString("N") + ".db");
        var socialDb = DbStore.Create(socialPath);
        var socialLogin = LoginService.Login(socialDb, "socialtest", "password123");
        var makeFirst = CharFactory.CreateCharacter(socialDb, socialLogin.accountId, 0, "HeroOne", CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
        var social = new NativeSocialService(socialDb);
        int first = makeFirst.characterId;
        var makeBuddy = CharFactory.CreateCharacter(socialDb, socialLogin.accountId, 0, "BuddyTwo", CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
        int second = makeBuddy.characterId;
        Check(makeBuddy.code == 0, "创建好友申请目标");
        SocialResponse Social(int id, SocialOperation op, string target = "", int cardId = 0)
            => social.Handle(new SocialRequest { characterId = id, operation = op, target = target, cardId = cardId });
        Check(!Social(first, SocialOperation.AddBuddy, "HeroOne").ok, "拒绝添加自己");
        Check(!Social(first, SocialOperation.AddBuddy, "MissingName").ok, "拒绝不存在的好友");
        Check(Social(first, SocialOperation.AddBuddy, "BuddyTwo").ok, "给离线角色发送好友申请");
        var pendingBuddy = Social(second, SocialOperation.Read);
        Check(pendingBuddy.buddies.Count == 1 && pendingBuddy.buddies[0].pending, "对方收到持久化待接受申请");
        Check(Social(second, SocialOperation.AcceptBuddy, "HeroOne").ok, "接受好友申请");
        Check(!Social(second, SocialOperation.Read).buddies[0].pending, "接受后显示正式好友");
        Check(!Social(second, SocialOperation.AcceptBuddy, "HeroOne").ok, "不能重复接受过期申请");
        Check(!Social(second, SocialOperation.CreateParty).ok, "新手不能绕过源端10级组队条件");
        var partyChar = CharactersDao.FindById(socialDb, second); partyChar.level = 10; CharactersDao.Save(socialDb, partyChar);
        Check(Social(second, SocialOperation.CreateParty).party, "10级可创建组队");
        Check(!Social(second, SocialOperation.CreateParty).ok, "不能重复创建组队");
        Check(!Social(second, SocialOperation.InviteParty, "HeroOne").ok, "离线角色不会被伪装成在线队员");
        Check(!social.Handle(new SocialRequest { characterId = second, operation = SocialOperation.SearchParty, minLevel = 1, maxLevel = 32 }).ok,
            "搜索范围不能超过30级");
        Check(!social.Handle(new SocialRequest { characterId = second, operation = SocialOperation.SearchParty, minLevel = 11, maxLevel = 30 }).ok,
            "搜索范围必须包含自己");
        Check(social.Handle(new SocialRequest { characterId = second, operation = SocialOperation.SearchParty, minLevel = 1, maxLevel = 30 }).searching,
            "队长可发起有效搜索");
        social.EndSession(second);
        Check(!Social(second, SocialOperation.Read).party && !Social(second, SocialOperation.Read).searching, "退出角色释放组队和搜索状态");
        Check(!social.Handle(new SocialRequest { characterId = first, operation = SocialOperation.SendChat, text = new string('a', 128) }).ok,
            "聊天长度上限127");
        Check(!social.Handle(new SocialRequest { characterId = first, operation = SocialOperation.SendChat, channel = NativeChatChannel.Spouse, text = "hi" }).ok,
            "没有配偶不能发送夫妻消息");
        Check(!social.Handle(new SocialRequest { characterId = first, operation = SocialOperation.SendChat, channel = NativeChatChannel.Party, text = "hi" }).ok,
            "没有队伍不能发送组队消息");
        Check(social.Handle(new SocialRequest { characterId = first, operation = SocialOperation.SendChat, text = "hello" }).ok, "所有人频道可发送消息");
        Check(!social.Handle(new SocialRequest { characterId = first, operation = SocialOperation.SendChat, text = "spam" }).ok, "聊天200毫秒防刷屏");
        for (int i = 0; i < 8; i++) Social(first, SocialOperation.AddMonsterCard, cardId: 2380000);
        Check(Social(first, SocialOperation.Read).cards[2380000] == 5, "同一怪物卡片最多5张");
        Check(Social(first, SocialOperation.Read).bookLevel == 1, "重复卡片不会增加手册等级");
        for (int i = 1; i <= 10; i++) Social(first, SocialOperation.AddMonsterCard, cardId: 2380000 + i);
        Check(Social(first, SocialOperation.Read).bookLevel == 2, "11种独立卡片升到手册2级");
        Check(!Social(first, SocialOperation.AddMonsterCard, cardId: 2000000).ok, "拒绝非怪物卡片ID");
        Check(Social(first, SocialOperation.SetBookCover, cardId: 2380000).bookCover == 2380000, "设置封面写回角色");
        socialDb.Save();
        var reopenedSocial = new NativeSocialService(DbStore.Open(socialPath));
        var persisted = reopenedSocial.Handle(new SocialRequest { characterId = first });
        Check(persisted.cards[2380000] == 5 && persisted.bookCover == 2380000 && persisted.buddies.Count > 0,
            "重新开库恢复好友、卡片和封面");
        CharactersDao.Delete(socialDb, first);
        Check(socialDb.Table("buddies").Select(r => DbStore.Int(r, "characterid") == first || DbStore.Int(r, "buddyid") == first).Count == 0,
            "删除角色清理双向好友关系");
        Check(socialDb.Table("monsterbook").Select(r => DbStore.Int(r, "charid") == first).Count == 0,
            "删除角色清理怪物手册");
        File.Delete(socialPath);

        Console.WriteLine();
        NativeSystemChecks.Run(Check);
        BotChecks.Run(Check);
        Console.WriteLine("通过 " + passed + " / 失败 " + failed);
        File.Delete(dbPath);
        return failed == 0 ? 0 : 1;
    }
}
