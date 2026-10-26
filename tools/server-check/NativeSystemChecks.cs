using System;
using System.Collections.Generic;
using System.IO;

static class NativeSystemChecks {
    sealed class Catalog : INativeCashCatalog {
        public Dictionary<int, CashCommodity> entries = new Dictionary<int, CashCommodity> {
            [100] = new CashCommodity { sn = 100, itemId = 5370000, price = 100, count = 1, period = 90, onSale = 1 },
            [101] = new CashCommodity { sn = 101, itemId = 1000000, price = 200, count = 1, period = -1, onSale = 1 },
            [102] = new CashCommodity { sn = 102, itemId = 9100000, price = 250, count = 1, period = 90, onSale = 1 },
            [103] = new CashCommodity { sn = 103, itemId = 5030000, price = 10, count = 1, period = 1, onSale = 1 },
            [104] = new CashCommodity { sn = 104, itemId = 5430000, price = 300, count = 1, period = -1, onSale = 1 }
        };
        public IEnumerable<CashCommodity> All => entries.Values;
        public CashCommodity Find(int sn) => entries.TryGetValue(sn, out var e) ? e : null;
        public IList<int> Package(int id) => id == 9100000 ? new[] { 100, 101 } : null;
        public bool Exists(int id) => id > 0;
        public bool Complete(int id) => id > 0;
        public bool IsCash(int id) => true;
        public DbRow Equipment(int id) => new DbRow { ["str"] = 3L, ["watk"] = 5L };
        public int EquipSlot(int id) => -1;
        public string CanEquip(int id, CharRecord c) => null;
    }
    public static void Run(Action<bool, string> Check) {
        Console.WriteLine("== 家族 / 学院 / 黑板 / 商城业务回归 ==");
        string path = Path.Combine(Path.GetTempPath(), "mapledash_systems_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try {
            int account = (int)db.Table("accounts").Insert(new DbRow { ["name"] = "TestAccount", ["nxCredit"] = 20000L, ["maplePoint"] = 10000L, ["nxPrepaid"] = 10000L });
            int otherAccount = (int)db.Table("accounts").Insert(new DbRow { ["name"] = "OtherAccount" });
            var ids = new List<int>();
            for (int i = 0; i < 7; i++) ids.Add((int)db.Table("characters").Insert(new DbRow { ["accountid"] = (long)(i == 6 ? otherAccount : account), ["name"] = "Player" + i, ["level"] = 30L, ["meso"] = 10000000L, ["map"] = 200000301L }));
            int leader = ids[0], junior = ids[1];
            var community = new NativeCommunityService(db);
            CommunityResponse C(int cid, CommunityOperation operation = CommunityOperation.Read, string name = "", string text = "", int target = 0, int value = 0) => community.Handle(new CommunityRequest { characterId = cid, operation = operation, name = name, text = text, targetId = target, value = value });
            community.BeginSession(leader);
            Check(!C(leader, CommunityOperation.CreateGuild, "测试家族").ok && CharactersDao.FindById(db, leader).meso == 10000000, "创建家族人数不足不扣款");
            foreach (int cid in ids.GetRange(1, 5)) community.BeginSession(cid);
            Check(!C(leader, CommunityOperation.CreateGuild, "!!").ok, "拒绝非法家族名称");
            Check(C(leader, CommunityOperation.CreateGuild, "测试家族").ok && C(leader).guildId == 0, "创建家族先等待全体成员确认");
            foreach (int cid in ids.GetRange(1, 5)) Check(C(cid, CommunityOperation.ConfirmGuild, target: leader, value: 1).ok, "符合条件成员确认 " + cid);
            var guild = C(leader);
            Check(guild.guild.Count == 6 && guild.guildId > 0 && guild.meso == 8500000, "全体确认后建立家族并仅扣一次创建费用");
            Check(!C(junior, CommunityOperation.GuildNotice, text: "无权公告").ok, "普通成员不能修改家族公告");
            Check(C(leader, CommunityOperation.GuildNotice, text: "真实公告").ok && C(junior).notice == "真实公告", "家族公告共享持久化状态");
            Check(!C(leader, CommunityOperation.GuildNotice, text: new string('x', 101)).ok && C(leader).notice == "真实公告", "超长公告拒绝且不覆盖已有公告");
            Check(C(leader, CommunityOperation.GuildRank, target: junior, value: 2).ok && C(junior, CommunityOperation.GuildNotice, text: "副族长公告").ok, "副族长职位权限生效");
            Check(!C(junior, CommunityOperation.ExpelGuild, target: leader).ok, "副族长不能驱逐族长");
            Check(C(leader, CommunityOperation.ExpandGuild).capacity == 15 && C(leader).meso == 8000000, "源端家族扩容费用和容量");
            Check(C(leader, CommunityOperation.GuildEmblem).meso == 3000000, "家族总部修改徽章扣500万");
            Check(!C(leader, CommunityOperation.InviteGuild, "Player6").ok, "离线存档角色不被当作在线成员");
            community.BeginSession(ids[6]);
            Check(C(leader, CommunityOperation.InviteGuild, "Player6").ok && C(ids[6], CommunityOperation.AcceptGuild).guild.Count == 7, "在线家族邀请接受后成员落库");
            Check(C(leader, CommunityOperation.ExpelGuild, target: ids[6]).ok && C(ids[6]).guildId == 0, "族长驱逐成员");
            Check(!C(leader, CommunityOperation.SetBoard, text: "hello").ok, "没有黑板道具不能发黑板");
            InventoryDao.InsertItem(db, leader, 5370000, 5, 1, 1);
            Check(C(leader, CommunityOperation.SetBoard, text: "hello").board == "hello" && InventoryDao.CountOf(db, leader, 5370000) == 1, "黑板展示且不消耗道具");
            Check(C(leader, CommunityOperation.CloseBoard).board == "", "黑板关闭");
            Check(C(leader, CommunityOperation.InviteFamily, "Player1").ok && C(junior, CommunityOperation.AcceptFamily).family.Count == 2, "学院登记与接受建立关系树");
            Check(!C(junior, CommunityOperation.FamilyPrecepts, text: "无权").ok, "非首领不能改家训");
            Check(C(leader, CommunityOperation.FamilyPrecepts, text: "努力冒险").ok && C(junior).precepts == "努力冒险", "学院家训共享");
            community.GiveReputation(junior, 800);
            Check(C(leader).reputation == 800 && C(leader).totalRep == 800, "同学贡献声望给学长");
            Check(C(leader, CommunityOperation.UseFamily, value: 3).ok && community.Rates(leader).expRate == 1.5 && C(leader).reputation == 0, "学院声望扣减并启动1.5倍经验计时");
            db.Table("family_character").Update(x => DbStore.Int(x, "cid") == leader, new DbRow { ["reputation"] = 10000L });
            Check(!C(leader, CommunityOperation.UseFamily, value: 3).ok, "学院特权每日限制");
            community.EndSession(leader); community.BeginSession(leader);
            Check(community.Rates(leader).expRate == 1, "退出角色清除学院战斗增益");
            community.CanFamilyTravel = (_, __) => false;
            Check(!C(leader, CommunityOperation.UseFamily, "Player1", value: 0).ok && C(leader).reputation == 10000, "地图限制拒绝学院传送且不扣声望");
            community.CanFamilyTravel = (_, __) => true;
            Check(C(leader, CommunityOperation.UseFamily, "Player1", value: 1).ok && C(junior).summonFrom == leader, "学院召集发出真实待确认请求");
            Check(C(junior, CommunityOperation.AnswerSummon, value: 1).ok, "学院召集接受后传送");
            var cash = new NativeCashService(db) { Catalog = new Catalog() };
            CashResponse S(int cid, CashOperation operation = CashOperation.Read, int sn = 100, long item = 0, int currency = 1) => cash.Handle(new CashRequest { characterId = cid, operation = operation, sn = sn, itemId = item, currency = currency });
            Check(!S(leader, CashOperation.Buy).ok, "没有进入商城不能交易");
            Check(S(leader, CashOperation.Open).ok, "进入商城读取三类账号余额");
            Check(cash.Handle(new CashRequest { characterId = junior, operation = CashOperation.AdjustBalance, currency = 1, amount = 500 }).credit == 20500,
                "源端NPC可在商城外奖励账号点券");
            Check(!cash.Handle(new CashRequest { characterId = junior, operation = CashOperation.AdjustBalance, currency = 1, amount = -99999 }).ok && S(leader).credit == 20500,
                "NPC点券扣除失败不形成负余额");
            int balance = S(leader).credit;
            Check(!S(leader, CashOperation.Buy, currency: 3).ok && S(leader).credit == balance, "无效余额类型不扣款");
            var bought = S(leader, CashOperation.Buy);
            Check(bought.ok && bought.credit == balance - 100 && bought.stock.Count == 1, "购买原子扣款并存入现金仓库");
            Check(bought.stock[0].expiration > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 89 * 86400000L, "现金道具90天有效期");
            long stock = bought.stock[0].id;
            var taken = S(leader, CashOperation.Withdraw, item: stock);
            Check(taken.ok && taken.stock.Find(x => x.id == stock).characterId == leader && taken.character.inventory.Exists(x => x.itemId == 5370000), "从账号现金仓库取出到角色道具栏");
            Check(!S(ids[6], CashOperation.Withdraw, item: stock).ok, "其他账号不能取出现金道具");
            Check(S(leader, CashOperation.Deposit, item: stock).ok && S(leader).stock.Find(x => x.id == stock).characterId == 0, "现金道具可存回仓库且不复制");
            Check(S(junior, CashOperation.Open).stock.Exists(x => x.id == stock), "同账号同职业角色共享现金仓库");
            int count = S(leader).stock.Count;
            var pack = S(leader, CashOperation.Buy, 102, currency: 2);
            Check(pack.ok && pack.points == 9750 && pack.stock.Count == count + 2, "礼包按SN展开且只扣礼包价格");
            long equipment = pack.stock.Find(x => x.itemId == 1000000).id;
            var gear = S(leader, CashOperation.Withdraw, item: equipment);
            Check(gear.character.inventory.Find(x => x.itemId == 1000000).watk == 5, "现金装备保留源端属性行");
            Check(S(leader, CashOperation.Equip, item: equipment).character.inventory.Exists(x => x.itemId == 1000000 && x.position == -1), "现金装备穿戴并持久化负槽位");
            Check(!S(leader, CashOperation.Deposit, item: equipment).ok, "穿戴中的现金装备不能存仓");
            Check(S(leader, CashOperation.Unequip, item: equipment).ok && S(leader, CashOperation.Deposit, item: equipment).ok, "卸下后可以存回现金仓库");
            // 源端 CashShopService.changeOnSale 是 deleteById 后再 insertSelective，这里同样先删后插。
            var over = db.Table("modified_cash_item"); over.Delete(x => DbStore.Int(x, "sn") == 100);
            over.Insert(new DbRow { ["sn"] = 100L, ["price"] = 0L });
            Check(cash.Commodity(100).price == 0 && cash.Commodity(100).onSale == 1 && cash.Commodity(100).count == 1, "商城nullable覆写保留未指定字段且零价有效");
            over.Update(x => DbStore.Int(x, "sn") == 100, new DbRow { ["on_sale"] = 0L });
            Check(!S(leader, CashOperation.Buy).ok, "数据库下架优先于WZ在售标志");
            over.Delete(_ => true);
            long exp = NativeCashService.Expiration(new CashCommodity { itemId = 5211048, period = 1 }, 100);
            Check(exp == 100 + 4 * 3600000L, "4小时倍率券有效期保持源端特例");
            Check(NativeCashService.Expiration(new CashCommodity { period = -1 }, 100) == -1, "永久现金道具无到期时间");
            db.Table("cash_items").Update(x => DbStore.Long(x, "id") == stock, new DbRow { ["expiration"] = 1L });
            Check(!S(leader).stock.Exists(x => x.id == stock), "过期现金道具从仓库移除");
            CashResponse Gift(int who, string recipient, int birthday = 20050511) => cash.Handle(new CashRequest { characterId = who, operation = CashOperation.Gift, sn = 100, target = recipient, message = "送给你", birthday = birthday });
            Check(!Gift(leader, "Player6", 20000101).ok, "赠送需要通过账号生日验证");
            Check(!Gift(leader, "Player1").ok, "禁止给同账号角色赠送");
            Check(Gift(leader, "Player6").ok && S(leader).prepaid == 9900, "赠送只扣预付点券并记录离线礼物");
            Check(S(ids[6], CashOperation.Open).gifts == 1, "收件人进入商城读取待领礼物");
            var gift = S(ids[6], CashOperation.ClaimGifts);
            Check(gift.ok && gift.gifts == 0 && gift.stock.Count == 1 && gift.stock[0].giftFrom == "Player0", "领取礼物保留赠送人并清除待领记录");
            Check(S(ids[6], CashOperation.ClaimGifts).stock.Count == 1, "礼物不能重复领取");
            Check(cash.Handle(new CashRequest { characterId = leader, operation = CashOperation.WishList, wishes = new[] { 100, 101 } }).wishes.Count == 2, "愿望清单落库");
            Check(!cash.Handle(new CashRequest { characterId = leader, operation = CashOperation.WishList, wishes = new[] { -99 } }).ok && S(leader).wishes.Count == 2, "无效愿望清单不覆盖旧清单");
            Check(cash.Handle(new CashRequest { characterId = leader, operation = CashOperation.ExpandInventory, inventoryType = 2, currency = 1 }).character.record.useslots == 28, "商城道具栏扩充落库");
            Check(S(leader, CashOperation.ExpandCharacters, 104).ok && AccountsDao.CharacterSlots(db, account) == 4, "商城角色栏扩充落库");
            var inventory = NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = leader, itemId = 1142000, quantity = 1 });
            Check(inventory.ok && inventory.inventory.Exists(x => x.itemId == 1142000), "任务勋章奖励落入真实背包");
            Check(NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = leader, itemId = 1142000, equip = true, equipSlot = -49, quantity = 1 }).inventory.Exists(x => x.itemId == 1142000 && x.position == -49), "勋章装备保存至源端-49槽位");
            Check(!NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = junior, itemId = 1142000, equip = true, equipSlot = -49, quantity = 1 }).ok, "不能装备未持有的勋章");
            db.Save(); var reopened = DbStore.Open(path); var reCash = new NativeCashService(reopened) { Catalog = new Catalog() };
            Check(reCash.Handle(new CashRequest { characterId = leader }).wishes.Count == 2 && new NativeCommunityService(reopened).Handle(new CommunityRequest { characterId = leader }).guild.Count == 6, "重开存档恢复商城清单与家族成员");
            Check(CharService.Load(reopened, leader).inventory.Exists(x => x.itemId == 1142000 && x.position == -49), "重开存档恢复已装备勋章");
            // A transaction must restore mutations to existing rows, not only additions/deletions.
            var original = AccountsDao.FindById(db, account); long before = DbStore.Long(original, "nxCredit");
            db.BeginTransaction(); original["nxCredit"] = -100L; db.Rollback();
            Check(DbStore.Long(AccountsDao.FindById(db, account), "nxCredit") == before, "扣款回滚恢复原行字段值");
        } finally { if (File.Exists(path)) File.Delete(path); }
    }
}
