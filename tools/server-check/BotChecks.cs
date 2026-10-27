using System;
using System.Collections.Generic;
using Server.Bot;
using Server.Field;
using Server.Bot.Social;
using Server.Bot.Environment;
using Server.Bot.Events;
using Server.Bot.Types;
using Server.Bot.Trade;
using Server.Bot.Decorator;

static class BotChecks
{
    sealed class ProbeBot : BotStateMachine
    {
        public int ticks;
        public int events;
        public ProbeBot(BotCharacter c) : base(c) { }
        public override void UpdateState() { ticks++; }
        public override void HandleEvent(GameEvent ev) { events++; }
    }
    sealed class FirstChoiceRandom : Random
    {
        public override double NextDouble() => .1;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }
    sealed class LifecycleBot : BotStateMachine
    {
        public LifecycleBot(BotCharacter character) : base(character) { }
        public void SetState(State value) { state = value; }
    }
    sealed class MarketProbe : BotStateMachine
    {
        public int updates;
        public override bool UsesSharedMarketTick => true;
        public MarketProbe(BotCharacter character) : base(character) { }
        public override void UpdateState() { updates++; base.UpdateState(); }
    }
    // 仅用于状态机协议测试；不伪装为背包/金币结算实现。
    sealed class TradeProbe : IBotTradeSession
    {
        public BotActor actor { get; set; }
        public IBotTradeSession partner { get; set; }
        public int meso { get; set; }
        public bool locked { get; set; }
        public IList<InventoryEntry> Items { get; } = new System.Collections.Generic.List<InventoryEntry>();
        public Action<TradeResult> ResultCallback { get; set; }
        public readonly System.Collections.Generic.List<string> chat = new System.Collections.Generic.List<string>();
        public int declined, accepted;
        public bool AddItem(InventoryEntry item, int slot)
        { foreach (var prior in Items) if (prior.position == slot) return false; item.position = slot; Items.Add(item); return true; }
        public bool SwapItem(InventoryEntry item, int slot)
        { for (int i = Items.Count - 1; i >= 0; i--) if (Items[i].position == slot) Items.RemoveAt(i); return AddItem(item, slot); }
        public void Chat(string text) => chat.Add(text);
        public void SetMesoBot(int amount) => meso = amount;
        public void Accept() => accepted++;
        public void Confirm()
        {
            locked = true;
            if (partner.locked)
            { ResultCallback?.Invoke(TradeResult.SUCCESSFUL); partner.ResultCallback?.Invoke(TradeResult.SUCCESSFUL); actor.trade = null; partner.actor.trade = null; }
        }
        public void Decline() { declined++; actor.trade = null; partner.actor.trade = null; }
    }

    static void At(long ms) { BotScheduler.Clock = ms; BotScheduler.Drive(); }

    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("== SoloMapling 核心回归 ==");
        CheckMarketData(check);
        var appearanceActor = new BotCharacter { skinColor = 2, face = 20000, hair = 30000 };
        appearanceActor.equips[-105] = 1050000;
        appearanceActor.equips[-5] = 1040000;
        appearanceActor.equips[-11] = 1302000;
        appearanceActor.equips[-111] = 1702000;
        appearanceActor.equips[-12] = 1112000;
        var appearanceIds = BotAppearance.CharacterIds(appearanceActor);
        check(appearanceIds[0] == "00002002" && appearanceIds[1] == "00012002" &&
            Array.IndexOf(appearanceIds, "01050000") >= 0 && Array.IndexOf(appearanceIds, "01040000") < 0 &&
            appearanceIds[appearanceIds.Length - 1] == "01702000" && Array.IndexOf(appearanceIds, "01112000") < 0,
            "假人纸娃娃保留肤色，现金服装遮盖普通槽，现金武器置末且排除属性戒指");
        appearanceActor.equips.Remove(-111);
        appearanceIds = BotAppearance.CharacterIds(appearanceActor);
        check(appearanceIds[appearanceIds.Length - 1] == "01302000", "取消现金武器恢复普通武器外观");
        CheckRecordedPlayback(check);
        CheckMovementRuntime(check);
        BotScheduler.Stop();
        BotScheduler.Clock = 0;
        int ticks = 0;
        BotScheduler.Register(1, () => ticks++, 0, 1000);
        BotScheduler.Register(1, () => ticks += 100, 0, 1000);
        At(249);
        check(ticks == 0, "源端250ms驱动窗口，重复注册不替换回调");
        At(250);
        check(ticks == 1, "首个驱动窗口执行一次");
        At(250);
        check(ticks == 1, "重复帧不重复调度");
        At(1250);
        check(ticks == 2, "周期到期再次执行");

        BotScheduler.Stop();
        BotScheduler.Clock = 0;
        ticks = 0;
        BotScheduler.Register(1, () => {
            ticks++;
            BotScheduler.Nudge(1, 750, 2000);
        }, 0, 2000);
        At(250);
        At(500);
        check(ticks == 1, "回调内Nudge保留未来截止时间");
        At(1000);
        check(ticks == 2, "回调内Nudge未被周期覆盖");

        BotScheduler.Stop();
        BotScheduler.Clock = 0;
        ticks = 0;
        BotScheduler.Register(1, () => {
            BotScheduler.Unregister(1);
            BotScheduler.Register(2, () => ticks++, 0, 1000);
        }, 0, 1000);
        At(250);
        check(!BotScheduler.IsRegistered(1) && BotScheduler.IsRegistered(2), "回调中增删调度不破坏遍历");
        At(500);
        check(ticks == 1, "新增回调在下一驱动窗口执行");

        BotScheduler.Stop();
        BotScheduler.Clock = 0;
        ticks = 0;
        BotScheduler.Register(1, () => { ticks++; throw new InvalidOperationException(); }, 0, 250);
        At(250);
        At(500);
        check(ticks == 2, "异常回调释放执行标记并继续调度");
        BotScheduler.Stop();

        var map = new MapRuntime { mapId = -101 };
        map.AddFoothold(-100, 100, 100, 100);
        map.AddFoothold(0, 0, 0, 200);
        MapService.Register(map);
        check(MapService.SnapToGround(-101, 0, 10) == 99, "落点跳过竖墙且保留脚下-1");
        check(MapService.SnapToGround(-101, 200, 10) == 10, "无脚下平台保留原坐标");
        var c = BotGeneration.CreateBotPollReadiness(20, 10, -101, true, false);
        check(c.id == 20100 && c.y == 99 && BotRegistry.OnMap(-101).Count == 1,
            "源端ID基数20000、首个20100，生成接通落点与注册");
        BotRegistry.EnterMap(c, -102);
        check(BotRegistry.OnMap(-101).Count == 0 && BotRegistry.OnMap(-102).Count == 1,
            "换图清除旧地图索引");
        var old = new ProbeBot(c);
        BotStore.AddActiveBot(c.id, old);
        old.StartScheduledTask(0);
        var replacement = new ProbeBot(c);
        BotStore.AddActiveBot(c.id, replacement);
        check(!old.running && !BotScheduler.IsRegistered(c.id), "替换状态机释放旧调度");
        replacement.StartScheduledTask(0);
        BotWander.Start(c, false, 0, 0);
        BotGeneration.RemoveBotFromServer(c);
        check(!replacement.running && !BotScheduler.IsRegistered(c.id) &&
            !BotStore.BotLoggedIn(c.id) && !BotRegistry.Contains(c.id) &&
            BotRegistry.OnMap(-102).Count == 0 && c.wander == null,
            "移除假人完整清理状态机、调度、地图与移动状态");
        MapService.Unregister(-101);
        BotScheduler.Stop();
        BotWander.Start(c, false, 0, 0);
        c.mapId = -103;
        BotWander.Tick(c, 0);
        check(c.wander == null && !c.moving, "换图终止旧地图游荡任务");
        CheckSocialRouting(check);
        CheckTasksAndPlatforms(check);
        CheckProfilesAndEvents(check);
        CheckRecordings(check);
        CheckTradeLifecycle(check);
        CheckMarketDirector(check);
        CheckEquipment(check);
        CheckRealTrade(check);
        CheckOriginalGeometryAndGroundPhysics(check);
        CheckWorldObservationAndGround(check);
        CheckRuntimeSession(check);
        CheckTownWanderer(check);
        CheckAmbientMap(check);
        CheckTownPresence(check);
        CheckMarketAmbient(check);
    }

    static void CheckMarketData(Action<bool, string> check)
    {
        var resources = BotResources.Load(); var pool = new MarketItemPool(resources, new FirstChoiceRandom());
        var hat = pool.Pick("common.yaml", "Hat", "A");
        check(pool.Variant(hat) == 1002026 && pool.Price(1002026) == 1000 && pool.Item(263190) == null,
            "原YAMLBeans保留零前缀字符串，商品ID按十进制读取而非PyYAML八进制");
        pool.Version = 38;
        check(pool.Price(2040301) == 2500000 && pool.Scrolls("A", true).Count == 0,
            "市场估价按版本区间读取，v39前不生成诅咒卷轴");
        pool.Version = 39;
        check(pool.Price(2040301) == 1700000 && pool.Scrolls("A", true).Count > 0,
            "v39边界切换市场价格和诅咒卷轴商品池");
        var stock = pool.Scrolls("A");
        check(stock.Count == 1 && stock[0].itemId == 2040301 && stock[0].quantity == 1,
            "原卷轴四次抽样后保留首个重复ID，数量取实际YAML范围");
        check(pool.Quantity("Potion", "S") == 5 && pool.Quantity("unknown", "S") == 5,
            "商品数量读取原itemQuantities且缺失类型回退5");
        check(MarketEconomy.CleanNumber(1999999) == 1900000 && MarketEconomy.CleanNumber(99999) == 99999,
            "原clean number保留Java整数除法截断，不误改为四舍五入");
        check(MarketEconomy.TrailingDigit(1200000) == 1222222 && MarketEconomy.Shorthand(1050) == "1.1k" &&
            MarketEconomy.Shorthand(1500000) == "1.5m" && MarketEconomy.Shorthand(1000000) == "1m",
            "市场尾数风格与中文叫卖使用的k/m缩写保留半数向上和去零语义");
        check(MarketEconomy.DayPrice(1000, DayOfWeek.Monday) == 950 && MarketEconomy.DayPrice(1000, DayOfWeek.Friday) == 1100,
            "市场星期涨跌按原周一二-5%、周五六日+10%");
        check(MarketEconomy.DoorPremium(910000001, 300, -80, 1000) == 1200 &&
            MarketEconomy.DoorPremium(910000001, 299, -80, 1000) == 1000 &&
            MarketEconomy.Quantity(1, 910000001, 300, 34) == 1 && MarketEconomy.Quantity(10, 910000001, 300, 34) == 14,
            "市场房间和门口溢价逐步截断，门口闭区间，单份商品数量不涨");
        var economy = new MarketEconomy(new FirstChoiceRandom(), () => new DateTime(2026, 10, 28, 0, 0, 0));
        check(economy.MarketIndices.Count == 12 && economy.MarketIndices[3] == 1.13 && economy.MarketIndices[9] == .85,
            "市场指数为原12个两小时正弦噪声槽并钳制.85到1.15");
        check(economy.Price(1000000) == 880000 && economy.TierForRoom(910000001) == "S" &&
            MarketEconomy.JavaInt(1e20) == int.MaxValue && MarketEconomy.WzPriceByJob(1000, 400) == 2250,
            "市场价格按指数、日、随机、风格顺序执行并保留Java饱和转换");
        pool.Version = 55;
        var pricing = new MarketScrollPricing(pool, BotItemCatalog.Shared, new FirstChoiceRandom());
        check(pricing.Find(1472026, "watt").First == 2044702 && pricing.Find(1302000, "watt") == null,
            "卷轴映射仅迁移原端支持的装备，不替未支持的武器编造卷轴");
        pool.Version = 38;
        check(pricing.Find(1002026, "int") == null && pricing.Find(1032000, "int") != null,
            "卷轴类型按原物品池最早版本门控，普通卷轴不被诅咒卷轴版本阻塞");
        pool.Version = 55;
        var firstPattern = MarketScrollPricing.Pattern(1, 55, new FirstChoiceRandom());
        var oldPattern = MarketScrollPricing.Pattern(7, 39, new FirstChoiceRandom());
        check(firstPattern[0] == 4 && firstPattern[1] == 0 && oldPattern[0] == 3 && oldPattern[1] == 4,
            "卷轴模式保留v40概率切换及第一组不按槽数裁剪的源端行为");
        var clean = BotItemCatalog.Shared.CleanEquip(1032000);
        var changed = clean.Copy(); changed.watk += 3; changed.luk += 3;
        check(MarketScrollPricing.HighestStat(changed, clean, out int bonus) == "luk" && bonus == 3,
            "最高强化属性同值时保留Java按差值建Map的后写覆盖顺序");
        var options = new List<MarketScrollPricing.Scroll> { new MarketScrollPricing.Scroll(50, 50, 1) };
        var costs = MarketScrollPricing.MinimumCosts(options, 100, 2, 2, 1);
        var prices = MarketScrollPricing.CleanCosts(costs);
        check(costs[0,0] == 100 && costs[1,1] == 300 && costs[2,2] == 700 &&
            double.IsPositiveInfinity(costs[1,0]) && prices[0] == 100 && prices[1] == 180 && prices[2] == 420,
            "强化估价按每步材料总价除成功率，去重最低成本后只扣非首档60%");
        var anomalies = new double[,] { {100, 900, 200, 300} };
        var corrected = MarketScrollPricing.CleanCosts(anomalies);
        check(corrected[1] == 96 && corrected[2] == 120 && corrected[3] == 180,
            "强化价格异常先用相邻档60%修正，再统一卖出折扣");
        check(pricing.IsClean(clean) && pricing.ItemValue(clean) == pool.Price(clean.itemId),
            "干净装备估价直接读原市场池，缺失价格保留null");
        changed = clean.Copy(); changed.upgradeslots = 0; changed.luk += 123;
        check(pricing.EquipValue(changed) == (pool.Price(changed.itemId) ?? 100),
            "强化主属性没有对应卷轴时回退原基础价格");
        var db = DbStore.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_bot_market_test.json"));
        var claw = BotItemCatalog.Shared.CleanEquip(1472026); int originalAttack = claw.watk;
        int[] pattern = MarketScrollPricing.Pattern(claw.upgradeslots, 55, new FirstChoiceRandom());
        int attackGain = pattern[0] * BotItemCatalog.Int(BotItemCatalog.Shared.Info(2044702), "incPAD")
            + pattern[1] * BotItemCatalog.Int(BotItemCatalog.Shared.Info(2044701), "incPAD");
        pricing.ScrollEquip(claw, db);
        check(claw.watk == originalAttack + attackGain && claw.upgradeslots == 0 &&
            BotItemCatalog.Int(claw.equipmentData, "level") == pattern[0] + pattern[1],
            "市场装备按源默认GM必成功叠加原IMG属性，最后清零槽数并记录强化次数");
        check(pricing.EquipValue(claw) > 0 && pricing.ItemValue(claw) == pricing.EquipValue(claw),
            "真实原IMG强化装备进入成本估价而非使用干净装备价格");
        var cache = new EquipMetadataCache(BotItemCatalog.Shared, EquipMetadataCache.LoadPools());
        var equipment = new MarketEquipmentPool(pool, BotItemCatalog.Shared, cache, db, new FirstChoiceRandom());
        var hotPattern = MarketEquipmentPool.Pattern("s", true);
        check(hotPattern.Length == 4 && hotPattern[0] == 2 && hotPattern[1] == 2 &&
            MarketEquipmentPool.Pattern("a").Length == 2 && equipment.Level("S") == 80 && equipment.Level("s") == 0,
            "市场装备模式忽略大小写而等级区间按源端区分大小写");
        var selected = equipment.Categories(new[] {"Weapon","Hat","Top","Pants","Shield"}, new[] {2,2});
        check(selected.Count == 4 && selected[0] == selected[1] && selected[2] == selected[3] && selected[0] != selected[2],
            "市场类别先无放回抽索引再恢复原顺序，每类按模式重复且不去重装备");
        var yamlEquips = equipment.FromYaml("thief.yaml",new[] {"Weapon","Hat","Top","Pants","Shield"},"S",true);
        check(yamlEquips.Count > 0 && yamlEquips.TrueForAll(e => e.Item.isEquip && e.Price >= 0),
            "原市场YAML装备生成接入干净IMG、卷轴模拟和最终估价");
        var cachedEquips = equipment.FromCache("S",400);
        bool validCached = cachedEquips.Count > 0 && cachedEquips.Count <= 4;
        foreach (var e in cachedEquips)
        {
            var info = BotItemCatalog.Shared.Info(e.Item.itemId);
            int req = BotItemCatalog.Int(info,"reqJob"), level = BotItemCatalog.Int(info,"reqLevel");
            validCached &= (req == 0 || req == 8) && level >= 48 && level <= 80 &&
                BotItemCatalog.Int(info,"cash") == 0 && BotItemCatalog.Int(info,"tradeBlock") == 0 && e.Price >= 0;
        }
        check(validCached,"市场缓存备货遵循职业/可交易/等级窗口，15次尝试最多四件");
    }

    static void CheckOriginalGeometryAndGroundPhysics(Action<bool, string> check)
    {
        var loader = BotNavigationMapLoader.Load();
        var fm = loader.Geometry(910000000);
        check(loader.Count == 6336 && fm.footholds.Count > 0 && fm.ropes.Count > 0 && fm.portals.Count > 0,
            "原始6336张地图几何可加载，市场保留平台绳梯传送门");
        var ids = new HashSet<int>(); foreach (var fh in fm.footholds) ids.Add(fh.id);
        check(ids.Count == fm.footholds.Count && !ids.Contains(0), "导航地图保留原始foothold身份，不用无编号线段代替");
        var physics = new BotGroundPhysics();
        var state = new BotGroundPhysics.Travel(0);
        state = physics.Advance(state, 1, 0, 1, BotMovementProfile.Base);
        double terminal = 16.667 * .008 * 3 / .4;
        double expectedSpeed = terminal * (1 - Math.Pow(13.0 / 15, 6));
        check(Math.Abs(state.Speed - expectedSpeed) < 1e-10 && state.CarryMs == 2,
            "原地面50ms按6个8ms子步加速并保留2ms余数");
        state = physics.Advance(state, 1, 0, 1, BotMovementProfile.Base);
        state = physics.Advance(state, 1, 0, 1, BotMovementProfile.Base);
        state = physics.Advance(state, 1, 0, 1, BotMovementProfile.Base);
        check(state.CarryMs == 0 && physics.WalkStep(BotMovementProfile.Base) == 6,
            "地面200ms累计25个子步，源端基础50ms步长为6像素");
        check(physics.SlipScale(.2f, new BotMovementProfile(100, 100, true)) == 1
            && physics.CounterStrafe(.2f, BotMovementProfile.Base, 1) == -1,
            "雪鞋取消冰面滑动，冰面反向制动按原端阈值");
        check(BotGroundPhysics.JavaRound(-1.5) == -1 && BotGroundPhysics.WalkableEndpoint(12, -26)
            && !BotGroundPhysics.WalkableEndpoint(13, 0) && !BotGroundPhysics.WalkableEndpoint(0, 17),
            "物理保留Java负数舍入及端点落差边界");
    }

    static void CheckRealTrade(Action<bool, string> check)
    {
        var db = DbStore.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_bot_trade_test.json"));
        long playerId = db.Table("characters").Insert(new DbRow { ["name"] = "TradePlayer", ["level"] = 120,
            ["meso"] = 2000000L, ["equipslots"] = 24L, ["useslots"] = 24L, ["setupslots"] = 24L, ["etcslots"] = 24L });
        var player = new BotActor { id = (int)playerId, level = 120, character = CharService.Load(db, (int)playerId) };
        var seller = new BotCharacter { id = 20999, level = 120 };
        var sellerMachine = new LifecycleBot(seller) { tradeLogic = new BotTradeLogic() };
        BotStore.AddActiveBot(seller.id, sellerMachine);
        var service = new BotTradeService(db, BotItemCatalog.Shared);
        check(service.Invite(player, seller), "真实交易服务建立玩家与假人双边会话");
        var buy = (BotTradeService.Session)player.trade; var sell = (BotTradeService.Session)seller.trade;
        check(sellerMachine.tradeLogic.queue.Get(seller) == player && sellerMachine.tradeLogic.CheckQueue(seller) && sell.FullTrade,
            "真实交易邀请进入假人请求队列，由业务轮接受并打开双边交易");
        check(!buy.AddItem(BotItemCatalog.Shared.CleanEquip(1302000), 1) && !buy.SwapItem(BotItemCatalog.Shared.CleanEquip(1302000), 1),
            "玩家不能调用假人装备生成入口绕过真实背包托管");
        buy.SetMesoBot(1000000);
        check(buy.meso == 0 && player.character.record.meso == 2000000, "玩家不能调用假人报价入口凭空制造托管金币");
        sell.Accept(); int callbacks = 0; sell.ResultCallback = result => { if (result == TradeResult.SUCCESSFUL) callbacks++; };
        var sword = BotItemCatalog.Shared.CleanEquip(1302000); sword.watk = 99; sword.flag = 16;
        sword.owner = "原物主"; sword.equipmentData = new Dictionary<string, object> { ["level"] = 7L };
        sell.AddItem(sword, 1); buy.SetMeso(1000000);
        check(player.character.record.meso == 1000000 && CharactersDao.FindById(db, player.id).meso == 1000000,
            "真实报价立即托管金币并更新数据库");
        buy.Confirm();
        check(callbacks == 0 && player.character.inventory.Count == 0 && buy.locked, "单方锁定不提前发物品或成功回调");
        sell.Confirm();
        var reloaded = CharService.Load(db, player.id);
        check(callbacks == 1 && player.trade == null && seller.trade == null && reloaded.inventory.Count == 1,
            "双边确认后真实物品落库，清会话并回调一次");
        var received = reloaded.inventory[0];
        check(received.watk == 99 && received.owner == "原物主" && received.flag == 8
            && Convert.ToInt64(received.equipmentData["level"]) == 7,
            "成交保留装备属性物主扩展列，剪刀标记转不可交易");
        check(seller.meso == 982000 && player.character.record.meso == 1000000,
            "百万金币成交按源端18‰税率结算");
        service.Invite(player, seller); buy = (BotTradeService.Session)player.trade; sell = (BotTradeService.Session)seller.trade;
        buy.SetMeso(10); buy.SetMeso(20); sell.Accept(); sell.Decline();
        check(player.character.record.meso == 1000000 && CharactersDao.FindById(db, player.id).meso == 1000000,
            "报价为累计托管，取消后完整退还30金币");
        // 让刚收到的剪刀装备按源端可丢弃配置参与转回，测试实际库存托管/退款。
        db.Table("game_config").Insert(new DbRow { ["config_code"] = "trade_limit_item_nodrop", ["config_value"] = "true" });
        service.Invite(player, seller); buy = (BotTradeService.Session)player.trade; sell = (BotTradeService.Session)seller.trade;
        check(buy.OfferFromInventory(1, received.position, 1, 1) && CharService.Load(db, player.id).inventory.Count == 0,
            "玩家放入物品时从实际背包托管，避免只显示不扣物品");
        buy.Decline();
        check(CharService.Load(db, player.id).inventory.Count == 1 && player.character.inventory.Count == 1,
            "玩家取消托管物品返还并落库");
        service.Invite(player, seller); buy = (BotTradeService.Session)player.trade; sell = (BotTradeService.Session)seller.trade;
        player.character.record.equipslots = 1;
        sell.AddItem(BotItemCatalog.Shared.CleanEquip(1302000), 1); buy.SetMeso(50); buy.Confirm(); sell.Confirm();
        check(player.character.record.meso == 1000000 && CharService.Load(db, player.id).inventory.Count == 1 && player.trade == null,
            "背包不足拒绝成交并退回托管金币，无半边结算");
        check(BotTradeService.Fee(99999) == 0 && BotTradeService.Fee(100000) == 800
            && BotTradeService.Fee(5000000) == 150000 && BotTradeService.Fee(100000000) == 6000000,
            "交易税阶梯使用源端边界及整数除法");
        db.Save();
        var reopened = DbStore.Open(db.FilePath);
        check(CharService.Load(reopened, player.id).inventory[0].watk == 99 && CharactersDao.FindById(reopened, player.id).meso == 1000000,
            "真实成交与取消后的物品金币可跨数据库重开恢复");
        System.IO.File.Delete(db.FilePath);
        BotStore.RemoveActiveBot(seller.id);
    }

    static void CheckEquipment(Action<bool, string> check)
    {
        var catalog = BotItemCatalog.Shared;
        var resources = BotResources.Load();
        var data = EquipMetadataCache.LoadPools();
        var cache = new EquipMetadataCache(catalog, data);
        var output = new BotOutput();
        var custom = new BotCustomization(catalog, output);
        check(catalog.ItemCount == 25324 && catalog.Name(1302000) != null, "原始IMG物品目录包含25324条属性及武器名称");
        var sword = catalog.CleanEquip(1302000);
        check(sword.watk == BotItemCatalog.Int(catalog.Info(1302000), "incPAD") && sword.quantity == 1 && sword.isEquip,
            "假人干净装备使用源端IMG攻击属性与装备数量");
        check(BotCustomization.EquipSlotType(1189999) == 53 && BotCustomization.EquipSlotType(1342000) == 51,
            "源端狂热图腾与副手不误入普通武器槽");
        check(custom.DestinationSlot(1702022) == -111, "现金武器使用独立-111覆盖槽");
        var bot = new BotCharacter { level = 200, gender = 0, tier = BotTier.S, job = 112 };
        custom.Equip(bot, 1302000); custom.Equip(bot, 1702022);
        custom.Equip(bot, 1000000, -160); custom.ClearCash(bot);
        check(bot.equips.ContainsKey(-11) && !bot.equips.ContainsKey(-111) && bot.equips.ContainsKey(-160),
            "清点装保留普通装备及源端排除边界-160");
        var generic = new GenericEquipPool(resources, catalog, new FirstChoiceRandom());
        bool valid = true;
        for (int level = 1; level <= 200; level += 7)
            foreach (string category in new[] { "tops", "bottoms", "overalls", "weapons", "caps", "shoes", "gloves", "capes" })
                for (int gender = 0; gender <= 1; gender++)
                {
                    int? id = generic.Pick(category, level, gender);
                    if (id.HasValue) valid &= BotItemCatalog.Int(catalog.Info(id.Value), "reqLevel") <= level
                        && (id.Value / 1000 % 10 == 2 || id.Value / 1000 % 10 == gender);
                }
        check(valid, "快速装备原始清单严格遵守等级上限与未归一化性别位");
        var nx = new NXItemPool(resources, cache, data, new FirstChoiceRandom());
        var nxDecorator = new BotDecorateNX(nx, custom, new FirstChoiceRandom());
        var nxBot = new BotCharacter { tier = BotTier.S };
        nxDecorator.Enabled = false; nxDecorator.Apply(nxBot, true);
        check(nxBot.equips.Count == 0, "点装关闭时不修改装备");
        nxDecorator.Apply(nxBot, false, true);
        check(nxBot.equips.ContainsKey(-111) && nxBot.equips.ContainsKey(-105), "强制点装忽略开关并保证原端武器和服装层");
        var selection = new EquipSelection(cache, new FirstChoiceRandom());
        check(EquipSelection.JobStyle(new BotCharacter { job = 322 }) == 320
            && EquipSelection.JobStyle(new BotCharacter { job = 522 }) == 510,
            "职业风格保留弩手特殊值及海盗原端基础力量敏捷判定");
        bot.level = 100;
        int? chosen = selection.ForWearing("SWORD", bot);
        check(chosen.HasValue && BotItemCatalog.Int(catalog.Info(chosen.Value), "reqLevel") >= 75
            && BotItemCatalog.Int(catalog.Info(chosen.Value), "reqLevel") <= 100
            && BotItemCatalog.Int(catalog.Info(chosen.Value), "cash") == 0,
            "完整配装使用原端25%等级窗口并排除现金装备");
        check(selection.Weighted(new[] { 1, 2, 3, 4, 5 }) == 1, "经典装备权重先抽区间再在区间内抽取");
        var decorate = new BotDecorate(resources, catalog, output, new FirstChoiceRandom());
        check(decorate.GenerateLevel(BotTier.S, 120, 200) == 182
            && decorate.GenerateLevel(BotTier.A, 120, 200) == 162
            && decorate.GenerateLevel(BotTier.B, 120, 200) == 142
            && decorate.GenerateLevel(BotTier.D, 120, 200) == 122,
            "假人等级按原端含端点区间分四段，保留整数截断");
        var generated = BotGeneration.CreateBotPollReadiness(0, 0, -999, true, false);
        check(generated.level >= 120 && generated.level <= 200 && generated.job % 10 == 2
            && generated.equips.ContainsKey(-111) && (generated.equips.ContainsKey(-5) || generated.equips.ContainsKey(-6)),
            "真实生成入口接通原端等级职业、现金武器与普通服装保障");
        BotGeneration.RemoveBotFromServer(generated);
    }

    static void CheckSocialRouting(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Clock = 100;
        var player = new BotActor { id = 7, name = "玩家" };
        var character = new BotCharacter { id = 20201, name = "测试假人" };
        var bot = new ProbeBot(character);
        BotStore.AddActiveBot(character.id, bot);
        var messages = new BotMessages(); var output = new BotOutput(); int options = 0;
        using (var dispatcher = new BotDispatcher(messages, output,
            _ => new BotActor[] { player, character }, (_, __) => options++))
        {
            messages.Add(new ChatMessage(player, "测试假人你好"), QueueType.PRIMARY);
            dispatcher.ProcessOne();
            check(bot.running && bot.interactors.Respondant == player, "叫名启动假人并记录当前对话者");
            messages.Add(new ChatMessage(player, "1"), QueueType.PRIMARY);
            dispatcher.ProcessOne();
            check(messages.Count(QueueType.SECONDARY) == 1 && output.Count == 2,
                "无名字的对话回复转入secondary并清除选项提示");
            messages.Add(new ChatMessage(player, "测试假人"), QueueType.PRIMARY);
            dispatcher.ProcessOne();
            check(options == 1 && bot.interactors.IsInquirer(player), "运行中的普通假人走inquirer选项路径");
            bot.interactors.ResetRespondants();
            messages.Add(new ChatMessage(player, "2"), QueueType.PRIMARY);
            dispatcher.ProcessOne();
            check(messages.Count(QueueType.TERTIARY) == 1, "只有inquirer的回复转入tertiary");
            messages.Add(new ChatMessage(player, "玩家和测试假人"), QueueType.PRIMARY);
            dispatcher.ProcessOne();
            check(options == 1, "地图角色顺序匹配真人名字后不再向后匹配假人");
            BotScheduler.Clock = 10100; messages.Clean(BotScheduler.Now());
            check(messages.Count(QueueType.SECONDARY) == 1, "消息正好10000ms时保留");
            BotScheduler.Clock = 10101; messages.Clean(BotScheduler.Now());
            check(messages.Count(QueueType.SECONDARY) == 0 && messages.Count(QueueType.TERTIARY) == 0,
                "消息超过10000ms才过期");
            dispatcher.Start(); dispatcher.Start();
            check(BotTasks.PendingCount == 2, "重复启动分派器不重复注册定时任务");
        }
        check(BotTasks.PendingCount == 0, "销毁分派器取消分派与清理任务");
        BotStore.RemoveActiveBot(character.id); BotScheduler.Stop(); BotTasks.Stop();
    }

    static void CheckTasksAndPlatforms(Action<bool, string> check)
    {
        BotScheduler.Clock = 0; var order = new System.Collections.Generic.List<int>();
        BotTasks.Schedule(() => { order.Add(1); BotTasks.Schedule(() => order.Add(3), 0); }, 50);
        BotTasks.Schedule(() => order.Add(2), 50);
        BotScheduler.Clock = 50; BotTasks.Drive(50);
        check(string.Join(",", order) == "1,2", "同截止时间按注册顺序执行，嵌套任务延后一个驱动");
        BotTasks.Drive(50);
        check(string.Join(",", order) == "1,2,3", "嵌套零延时任务在下一驱动执行");
        int runs = 0;
        BotTasks.ScheduleFixedRate(() => { runs++; throw new InvalidOperationException(); }, 0, 10);
        BotTasks.Drive(50); BotTasks.Drive(100);
        check(runs == 1, "独立重复任务抛异常后取消重复执行");
        var points = PlatformParser.ReadCoordinates("0,-10,0\n1,10,51\nbad,row\n2,10,51");
        var platform = PlatformParser.OrganizePlatform(points);
        check(platform.IsSloped && platform.sortedPoints.Count == 2 && platform.GetYAtX(0) == 25,
            "录制CSV去重、超过50像素为斜面，插值按整数截断");
        check(platform.GetYAtX(-20) == 0 && platform.GetYAtX(20) == 51, "斜面查询在两端夹紧");
        var parser = new PlatformParser(_ => null, _ => Array.Empty<string>());
        check(parser.ParsePlatform(910000000, "m2").baseY == -266 && parser.ListPlatformIds(910000000).Count == 15,
            "市场录制缺失使用源端平台回退数据");
        BotTasks.Stop();
    }

    static void CheckProfilesAndEvents(Action<bool, string> check)
    {
        var character = new BotCharacter { id = 20202, job = 410, level = 54, world = 0, channel = 1, mapId = 100 };
        var young = BotMovementProfile.FromCharacter(character, false);
        character.level = 55;
        var haste = BotMovementProfile.FromCharacter(character, false);
        check(young.speed == 150 && young.jump == 115 && haste.speed == 155 && haste.jump == 123,
            "盗贼55级急速边界与属性分桶保持源端");
        character.job = 100; character.level = 9;
        var party = new BotActor { id = 7, job = 420, level = 60, online = true };
        var boosted = BotMovementProfile.FromCharacter(character, false, new[] { party }, true);
        check(boosted.speed == 155 && boosted.jump == 100 && boosted.snowShoes,
            "队伍急速只提升速度不提升跳跃，雪鞋标志保留");
        check(BotMovementProfile.FromCharacter(character, true, new[] { party }, true).Equals(BotMovementProfile.Base),
            "禁移动技能地图强制基础属性且忽略雪鞋");
        check(new BotMovementProfile(201, 124).Equals(new BotMovementProfile(200, 123)) &&
            new BotMovementProfile(200, 123).GetHashCode() == new BotMovementProfile(200, 123).GetHashCode(),
            "移动属性值相等，避免无变化时反复重建导航图");
        var bus = new EventBus(); var bot = new ProbeBot(character);
        bot.Subscribe(bus, EventType.LEVEL_UP);
        var player = new BotActor { id = 7, world = 0, channel = 1, mapId = 101 };
        bus.Publish(EventFactory.LevelUp(player));
        check(!bot.HasQueuedEvents, "假人事件过滤不同地图");
        player.mapId = 100;
        bus.Publish(EventFactory.LevelUp(player)); bus.Publish(EventFactory.LevelUp(player));
        bot.ProcessQueuedEvents();
        check(bot.events == 1 && bot.HasQueuedEvents, "每次事件处理只消费一条");
        bot.ProcessQueuedEvents(); bot.StopScheduledTask();
        bus.Publish(EventFactory.LevelUp(player));
        check(bot.events == 2 && !bot.HasQueuedEvents, "停用假人解除事件订阅");
    }

    static void CheckRecordings(Action<bool, string> check)
    {
        // timestamp=1, length=2, payload=AB CD，末尾截断帧按原端EOF规则忽略。
        var binary = new byte[] { 0,0,0,0,0,0,0,1, 0,0,0,2, 0xab,0xcd, 0,0,0 };
        var packets = BotMovementRecording.ReadBinary(binary);
        check(packets.Count == 1 && packets[0].timestamp == 1 && packets[0].data[1] == 0xcd,
            "移动录制大端帧头与原始封包字节不混用端序");
        var raw = BotMovementRecording.ReadCsv("123\n9\n0,-12,34,1,-2,5,4,100\n124\n1\n1,0,0,0,0,0,-1,20\n");
        check(raw.Count == 2 && raw[0].declaredCommands == 9 && raw[0].commands.Count == 1 &&
            raw[1].timestamp == 124 && raw[1].commands[0].stance == -1,
            "录制CSV命令数偏大时回退一行，保留下一帧时间戳");
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
            "../../../../../Assets/Resources/Bot/SoloMapling.json"));
        var resources = new BotResources(System.IO.File.ReadAllText(path));
        var files = resources.ListFileStems(PlatformParser.ResourcePath, ".bin");
        int frames = 0;
        foreach (var file in files)
            frames += BotMovementRecording.ReadBinary(resources.ReadBytes(PlatformParser.ResourcePath + "/" + file + ".bin")).Count;
        check(files.Count == 440 && frames == 7441, "全部440份源端BIN录制可读取，共7441帧");
        var original = resources.ReadBytes("soloMapling/ArtificialPlayer/BotDialoguePack/TownWandererDialogue.yaml");
        check(original != null && original.Length > 8 && original[0] == 0x84 && original[1] == 0x4c,
            "无法解析的城镇对话仍可取回源文件原始字节");
        CheckSocialConversation(check, resources);
    }

    static void CheckSocialConversation(Action<bool, string> check, BotResources resources)
    {
        BotTasks.Stop(); BotScheduler.Clock = 100;
        var character = new BotCharacter { id = 20203, name = "聊天假人", chair = 3010000, x = 100 };
        var player = new BotActor { id = 8, x = 0 };
        var output = new BotOutput(); var messages = new BotMessages();
        var bot = new SocialBot(character, new BotDialogue(resources, new FirstChoiceRandom()), output,
            messages, () => 3010001, new FirstChoiceRandom());
        BotRegistry.Add(character); BotRegistry.EnterMap(character, 0); BotStore.AddActiveBot(character.id, bot);
        bot.running = true; bot.interactors.SetRespondant(player); bot.OnFirstInteraction(player);
        check(character.chair == 0 && output.Poll()?.kind == BotOutput.Kind.Chair,
            "社交假人先从原椅子起身");
        BotScheduler.Clock = 700; BotTasks.Drive(700);
        check(character.facing == -1 && output.Count == 0, "起身600ms后朝向玩家，尚未提前发言");
        BotScheduler.Clock = 2700; BotTasks.Drive(2700);
        check(output.Poll()?.text == "你好呀。" && output.Count == 0, "使用源端问候台词，等候2000ms后发言");
        BotScheduler.Clock = 3100; BotTasks.Drive(3100);
        var emote = output.Poll(); var options = output.Poll();
        check(emote?.kind == BotOutput.Kind.Emote && emote.value == 2 &&
            options?.kind == BotOutput.Kind.Hint && options.text.Contains("1. 最近在忙什么？\r\n"),
            "问候后400ms发原端表情与四个选项");
        output.Clear();
        BotScheduler.Clock = 3200; messages.Add(new ChatMessage(player, "1")); bot.UpdateState();
        check(output.Count == 0, "选择回复保持源端思考延时");
        BotScheduler.Clock = 5200; BotTasks.Drive(5200);
        check(output.Poll()?.text == "没什么，在等朋友。", "第一选项读取源端WhatsUp节点");
        output.Clear();
        BotScheduler.Clock = 38200; bot.UpdateState();
        check(bot.hasActiveRespondant, "社交超时35000ms边界仍保留对话");
        BotScheduler.Clock = 38201; bot.UpdateState();
        check(!bot.hasActiveRespondant && output.Count >= 2, "超过35000ms发超时台词并清除交互");
        BotGeneration.RemoveBotFromServer(character); BotTasks.Stop(); BotScheduler.Stop();
    }

    static void CheckWorldObservationAndGround(Action<bool, string> check)
    {
        var graph = new BotWorldGraph("{\"neighbors\":{\"1\":[2],\"2\":[3],\"3\":[]}}");
        check(graph.Route(1, 3, 1) == null && graph.Route(1, 3, 2).Count == 2 && graph.Route(1, 1, 0).Count == 0,
            "世界路线BFS遵守跳数上限，路径不包含出发地图");
        check(BotWorldGraph.TaxiNpc(100000000, 104000000) == 1012000 &&
            graph.Route(100000000, 104000000, 1)[0] == 104000000 && BotWorldGraph.TaxiNpc(100000000, 100000000) == 0,
            "原六城出租车构成单跳边且不允许原地出租车路线");
        var real = new BotActor { id = 1, mapId = 1, x = 350, y = 0 };
        var players = new List<BotActor> { real, new BotCharacter { id = 20100, mapId = 3 } };
        var map = new MapRuntime { mapId = 1 };
        map.portals.Add(new MapPortal { targetMap = 2, x = 0, y = 0 });
        BotScheduler.Clock = 1000;
        var observer = new BotObserverTracker(() => players, id => id == 1 ? map : null, graph);
        observer.Refresh();
        check(observer.IsFull(1) && observer.IsHalo(2) && !observer.IsFull(3), "观察者排除假人，真人距传送门350px边界激活邻图");
        real.x = 351; BotScheduler.Clock = 2000; observer.Refresh();
        check(!observer.IsHalo(2) && observer.IsActive(2), "离开传送门范围立即取消halo但保留四秒驻留");
        BotScheduler.Clock = 5000; observer.Refresh();
        check(!observer.IsActive(2), "四秒驻留到期等号边界移除邻图");
        observer.MarkObservedNow(3); BotScheduler.Clock = 7000;
        check(observer.IsFull(3), "强制观察两秒等号边界仍有效");
        BotScheduler.Clock = 7001; check(!observer.IsActive(3), "强制观察严格超过两秒过期");
        var geometry = new MapRuntime();
        geometry.footholds.Add(new FootholdSegment(-100, 100, 100, 100) { id = 1 });
        geometry.footholds.Add(new FootholdSegment(-100, 0, 100, 0) { id = 2 });
        geometry.footholds.Add(new FootholdSegment(100, -100, -100, -100) { id = 3 });
        geometry.footholds.Add(new FootholdSegment(0, -200, 0, 200) { id = 4 });
        var ground = new BotGroundIndex(geometry);
        check(ground.Below(0, -200)?.id == 2 && ground.Below(101, 0) == null && ground.Below(-101, 0) == null,
            "原64像素地面桶排序，反向底边与竖墙不成为落脚面");
        check(ground.Ground(0, 20)?.id == 2 && ground.Ground(0, 27)?.id == 1,
            "脚下检测比较当前点及上探26px，超过上探范围不吸附上层");
        check(ground.GroundAbove(0, 100, 100)?.y == 0 && ground.GroundAbove(0, 100, 99) == null,
            "向上查询严格排除同高度并保留最大上升距离边界");
        var a = new FootholdSegment(0, 0, 10, 0);
        check(BotGroundIndex.CanWalkAcross(a, new FootholdSegment(11, 1, 20, 1)) &&
            !BotGroundIndex.CanWalkAcross(a, new FootholdSegment(12, 1, 20, 1)), "平台连接只允许原端曼哈顿2px以内端点缝隙");
        var solid = new MapRuntime();
        AddRectangle(solid, 1, 0, 0, 100, 100);
        AddRectangle(solid, 5, 20, 20, 80, 80);
        AddRectangle(solid, 9, 24, 24, 76, 76);
        AddRectangle(solid, 13, 40, 40, 60, 60);
        var collision = BotFootholdCollision.FromBelow(solid.footholds);
        check(collision.Contains(3) && !collision.Contains(7) && !collision.Contains(11) && collision.Contains(15),
            "闭环实体与孔洞交替，8px内薄壳继承父实体分类");
        solid.footholds[0].next = 0;
        check(!BotFootholdCollision.FromBelow(solid.footholds).Contains(3), "断开next的轮廓不误判为封闭实体天花板");
        var walk = new MapRuntime();
        walk.footholds.Add(new FootholdSegment(0, 0, 10, 0) { id = 1, next = 2 });
        walk.footholds.Add(new FootholdSegment(11, 1, 20, 1) { id = 2, previous = 1 });
        walk.footholds.Add(new FootholdSegment(21, 1, 30, 1) { id = 3 });
        walk.ropes.Add(new MapRope { x = 5, y1 = -20, y2 = 0, ladder = true });
        var regions = new BotWalkRegions(walk);
        check(regions.regions.Count == 3 && regions.byFoothold[1] == regions.byFoothold[2] && regions.byFoothold[2] != regions.byFoothold[3],
            "可行走区域仅沿prev/next合并合法接缝，不自动连接相邻孤立平台");
        check(regions.GroundSample(walk.footholds[0], 10, 0)?.point.y == 0 &&
            regions.GroundSample(walk.footholds[0], 22, 1)?.point.x == 20 &&
            regions.GroundSample(walk.footholds[0], 23, 1) == null,
            "区域地面采样优先包含x的平台，缝隙只允许2px且不跨越区域");
        var field = new MapRuntime { left = -1000, right = 1000, top = -1000, bottom = 1000 };
        field.AddFoothold(-100, 0, 100, 0); field.footholds[0].id = 1;
        var fieldGround = new BotGroundIndex(field); var airCollision = new BotAirCollision(field, fieldGround);
        var physics = new BotGroundPhysics(); var fieldRegions = new BotWalkRegions(field);
        var motion = new BotGroundMotion(field, fieldGround, fieldRegions, airCollision, physics);
        var point = new BotNavigationGraph.Point(0, 0); var travel = new BotGroundPhysics.Travel(0);
        BotGroundMotion.Result moved = default;
        for (int i = 0; i < 12; i++) { moved = motion.Step(point, field.footholds[0], 1, travel, BotMovementProfile.Base); point = moved.point; travel = moved.travel; }
        check(point.x > 0 && point.x < 100 && point.y == 0 && !moved.lostGround, "原地面加速接真实平台采样，连续行走保持脚底高度");
        moved = motion.Step(new BotNavigationGraph.Point(100, 0), field.footholds[0], 1, new BotGroundPhysics.Travel(100, 1), BotMovementProfile.Base);
        check(moved.lostGround && moved.point.x > 100 && moved.point.y == 0, "走出平台后返回离地状态，不吸附不存在的地面");
        var air = new BotAirMotion(field, airCollision, physics, BotMovementProfile.Base);
        var jump = air.Launch(new BotNavigationGraph.Point(0, 0), 0); int airTicks = 0;
        while (jump.airborne && airTicks++ < 40) { BotAirMotion.AdvanceTimers(jump); air.Step(jump); }
        check(!jump.airborne && jump.Position.y == 0 && jump.fallDistance > 70 && jump.fallDistance < 80,
            "原50ms普通跳经过重力顶点后落地，记录真实坠落距离");
        var flash = air.Launch(new BotNavigationGraph.Point(0, -100), 6); flash.pendingFlash = true;
        for (int i = 0; i < 12 && !flash.flashFired; i++) air.Step(flash);
        check(flash.flashFired && flash.fixedArc && flash.velocityX == 28 && flash.velocityY < 0,
            "二段跳在原速度阈值触发，550px/s转换每tick并按Java舍入");
        var ropeMotion = new BotRopeMotion(fieldGround);
        var rope = new MapRope { x = 0, y1 = 0, y2 = 100 };
        check(!BotRopeMotion.CanGrab(new BotNavigationGraph.Point(0, 0), rope) &&
            BotRopeMotion.CanGrab(new BotNavigationGraph.Point(8, 1), rope) &&
            !BotRopeMotion.CanGrab(new BotNavigationGraph.Point(9, 1), rope), "抓绳范围保留首可攀爬高度top+1及8px横向边界");
        var climb = BotRopeMotion.Attach(rope, 10); climb.direction = -1;
        check(ropeMotion.Step(climb) == BotRopeMotion.Result.Climb && climb.position.y == 5 &&
            ropeMotion.Step(climb) == BotRopeMotion.Result.Land && climb.position.y == 0 && !climb.climbing,
            "绳梯50ms攀爬5px，到顶按原三探针选择真实平台退出");
        climb = BotRopeMotion.Attach(rope, 100); climb.direction = 1;
        check(ropeMotion.Step(climb) == BotRopeMotion.Result.Fall && !climb.climbing, "绳梯下端越界返回自由落体");
        var swim = new BotSwimMotion(airCollision, physics, BotMovementProfile.Base);
        var swimming = new BotSwimMotion.State { position = new BotNavigationGraph.Point(0, -100), direction = 1 };
        swim.Step(swimming);
        check(Math.Abs(swimming.horizontalSpeed - 23.685) < .0001 && Math.Abs(swimming.verticalSpeed - 29.5) < .0001,
            "游泳水平加速与4.21摩擦、590重力使用原端px/s单位");
        swimming.jumpRequested = true; swim.Step(swimming);
        check(swimming.verticalSpeed < -700 && !swimming.jumpRequested && swimming.facing == 1,
            "游泳跳跃施加原1000px/s向上脉冲并消费单次请求");
        var trajectory = new BotTrajectory(field, airCollision, BotMovementProfile.Base);
        var prediction = trajectory.Jump(new BotNavigationGraph.Point(0, 0), 6);
        check(prediction != null && prediction.point.x > 0 && prediction.point.y == 0 && prediction.TimeMs == prediction.ticks * 50,
            "导航跳跃预测保持固定水平步长并返回原50ms落地时刻");
        var grab = trajectory.Grab(new BotNavigationGraph.Point(0, 10), 6, rope);
        check(grab != null && grab.ticks == 0 && grab.point.y == 10, "导航抓绳预测在物理推进前检测当前抓绳范围");
        CheckPathSearch(check);
        var navigationMap = new MapRuntime { mapId = 42, left = -200, right = 200, top = -200, bottom = 200 };
        navigationMap.AddFoothold(-50, 30, 50, 30); navigationMap.footholds[0].id = 1;
        navigationMap.AddFoothold(-150, 100, 150, 100); navigationMap.footholds[1].id = 2;
        var built = new BotNavigationGraphBuilder(navigationMap).Build();
        bool jumpUp = false, dropDown = false;
        foreach (var edge in built.Outgoing(2)) if (edge.type == BotNavigationGraph.EdgeType.Jump && edge.toRegionId == 1) jumpUp = true;
        foreach (var edge in built.Outgoing(1)) if (edge.type == BotNavigationGraph.EdgeType.Drop && edge.toRegionId == 2) dropDown = true;
        check(jumpUp && dropDown, "原导航图从实际几何与落地后三tick稳定性生成上跳和下落边");
        navigationMap.ropes.Add(new MapRope { id = 1, x = 0, y1 = 30, y2 = 100, ladder = true });
        built = new BotNavigationGraphBuilder(navigationMap).Build();
        bool climbIn = false, climbOut = false;
        foreach (var edge in built.Outgoing(2)) if (edge.type == BotNavigationGraph.EdgeType.Climb && edge.toRegionId == 3) climbIn = true;
        foreach (var edge in built.Outgoing(3)) if (edge.type == BotNavigationGraph.EdgeType.Climb && edge.toRegionId == 1) climbOut = true;
        check(climbIn && climbOut, "原绳梯图生成地面进入与顶部平台退出的双向路径");
        var cache = new BotNavigationGraphs();
        var cacheMap = new MapRuntime { mapId = 0 };
        var firstGraph = cache.Get(cacheMap);
        check(ReferenceEquals(firstGraph, cache.Get(cacheMap, new BotMovementProfile(100, 100, true))),
            "非冰面雪鞋属性归一化，避免重复构建相同导航图");
        for (int id = 1; id <= 511; id++) cache.Get(new MapRuntime { mapId = id });
        cache.Get(cacheMap); cache.Get(new MapRuntime { mapId = 512 });
        check(cache.Count == 512 && ReferenceEquals(cache.PeekAny(cacheMap), firstGraph) && cache.PeekAny(new MapRuntime { mapId = 1 }) == null,
            "导航缓存超过512只淘汰最旧条目，访问过的地图不会因整表清空丢失");
        var originalMarket = BotNavigationMapLoader.Load().Geometry(910000000);
        var marketGraph = new BotNavigationGraphBuilder(originalMarket, BotMovementProfile.Base).Build();
        int marketEdges = 0, marketClimbs = 0;
        foreach (var list in marketGraph.outgoing.Values) foreach (var edge in list)
        { marketEdges++; if (edge.type == BotNavigationGraph.EdgeType.Climb) marketClimbs++; }
        check(marketGraph.regions.Count > 1 && marketEdges > 0 && marketClimbs > 0,
            "原自由市场910000000完整IMG几何可构建多区域及攀爬导航边");
        BotScheduler.Stop();
    }

    static void CheckPathSearch(Action<bool, string> check)
    {
        var p0 = new BotNavigationGraph.Point(0, 0); var p10 = new BotNavigationGraph.Point(10, 0);
        var p20 = new BotNavigationGraph.Point(20, 0); var p30 = new BotNavigationGraph.Point(30, 0);
        var r1 = new BotNavigationGraph.Region(1, new List<BotNavigationGraph.Segment> { new BotNavigationGraph.Segment(new FootholdSegment(0, 0, 10, 0)) });
        var r2 = new BotNavigationGraph.Region(2, new List<BotNavigationGraph.Segment> { new BotNavigationGraph.Segment(new FootholdSegment(10, 0, 20, 0)) });
        var r3 = new BotNavigationGraph.Region(3, new List<BotNavigationGraph.Segment> { new BotNavigationGraph.Segment(new FootholdSegment(20, 0, 30, 0)) });
        var direct = new BotNavigationGraph.Edge(1, 3, BotNavigationGraph.EdgeType.Jump, p0, p30, 6, 0, 0, 0, 0, 1000);
        var portal = new BotNavigationGraph.Edge(1, 2, BotNavigationGraph.EdgeType.Portal, p0, p10, 0, 1, 0, 0, 0, 100);
        var finish = new BotNavigationGraph.Edge(2, 3, BotNavigationGraph.EdgeType.Jump, p10, p20, 6, 0, 0, 0, 0, 100);
        var outgoing = new Dictionary<int, List<BotNavigationGraph.Edge>> { [1] = new List<BotNavigationGraph.Edge> { direct, portal }, [2] = new List<BotNavigationGraph.Edge> { finish } };
        var graph = new BotNavigationGraph(1, 61, BotMovementProfile.Base, new List<BotNavigationGraph.Region> { r1, r2, r3 },
            new Dictionary<int, BotNavigationGraph.Region> { [1] = r1, [2] = r2, [3] = r3 }, new Dictionary<int, int>(), outgoing, new HashSet<int>());
        var map = new MapRuntime(); var runtimePortal = new MapPortal { id = 1 }; map.portals.Add(runtimePortal);
        var result = BotPathSearch.Run(graph, map, p0, 1, 3, p30);
        check(result.path.Count == 2 && result.path[0] == portal && result.cost == 280 && result.usesPortal,
            "原导航搜索累加到起跳点与最终目标步行成本，选取更低成本portal路线");
        runtimePortal.enabled = false; result = BotPathSearch.Run(graph, map, p0, 1, 3, p30);
        check(result.path.Count == 1 && result.path[0] == direct && !result.usesPortal, "关闭的传送门边不参与路线搜索");
        result = BotPathSearch.Run(graph, map, p0, 1, 1, p10);
        check(result.path.Count == 0 && result.cost == 80, "同区域路线保留最终步行成本，不构造虚假跨区边");
        long seed = BotPathSearch.RouteSeed(20100);
        result = BotPathSearch.Run(graph, map, p0, 1, 3, p30, true, seed);
        var repeated = BotPathSearch.Run(graph, map, p0, 1, 3, p30, true, seed);
        check(seed != 0 && result.cost == repeated.cost && result.path.Count == repeated.path.Count,
            "假人ID派生路线扰动保持可复现的原64位混合和随机成本");
    }

    static void AddRectangle(MapRuntime map, int id, int left, int top, int right, int bottom)
    {
        map.footholds.Add(new FootholdSegment(left, top, right, top) { id = id, next = id + 1 });
        map.footholds.Add(new FootholdSegment(right, top, right, bottom) { id = id + 1, next = id + 2 });
        map.footholds.Add(new FootholdSegment(right, bottom, left, bottom) { id = id + 2, next = id + 3 });
        map.footholds.Add(new FootholdSegment(left, bottom, left, top) { id = id + 3, next = id });
    }

    static TradeProbe PairTrade(BotActor actor, BotActor player)
    {
        var botSide = new TradeProbe { actor = actor }; var playerSide = new TradeProbe { actor = player };
        botSide.partner = playerSide; playerSide.partner = botSide; actor.trade = botSide; player.trade = playerSide;
        return botSide;
    }
    static void CheckTradeLifecycle(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Clock = 1000;
        var wants = new BotTradeWants();
        wants.mesoWanted = 1000000;
        check(wants.VerifySufficientMeso(900000) && !wants.VerifySufficientMeso(899999), "百万交易容忍10%差价，边界不多放宽1金币");
        wants.SetWants(0, new[] { 4000000, 4000000 }); wants.AddItemWanted(4000001, 3);
        var offered = new[] { new InventoryEntry { itemId = 4000000, quantity = 1 },
            new InventoryEntry { itemId = 4000001, quantity = 1 }, new InventoryEntry { itemId = 4000001, quantity = 2 } };
        check(wants.VerifyTrade(0, offered), "交易需求累计分堆数量，重复需求保持源端逐条核验语义");
        wants.Reset();
        check(wants.mesoWanted == 4206969 && wants.itemsWanted.Count == 0, "交易需求重置保留源端特殊缺省价");
        var character = new BotCharacter { id = 20204, mapId = 100 };
        var player = new BotActor { id = 9, mapId = 100 };
        var bot = new LifecycleBot(character) { output = new BotOutput(), tradeLogic = new BotTradeLogic(), ItemName = id => "源端物品" };
        BotStore.AddActiveBot(character.id, bot); bot.running = true;
        bot.UpdateState(); check(bot.state == BotStateMachine.State.RUNNING, "基类IDLE验证在线并转RUNNING");
        var session = PairTrade(character, player);
        bot.tradeHandler.confirmedPartner = player; bot.tradeMode = TradeMode.SELLING; bot.tradeWants.mesoWanted = 1000000;
        bot.tradeInventory.SetMain(new InventoryEntry { itemId = 1302000, quantity = 1, isEquip = true, watk = 17 });
        bot.UpdateState(); check(bot.state == BotStateMachine.State.TRADING, "基类确认同地图交易伙伴后进入TRADING");
        bot.UpdateState(); bot.UpdateState();
        check(session.Items.Count == 1 && session.Items[0].watk == 17 && session.chat.Contains("我想要1m"),
            "出售流程先展示装备属性，再报需求金币");
        ((TradeProbe)session.partner).meso = 900000; ((TradeProbe)session.partner).locked = true;
        bot.UpdateState(); bot.UpdateState();
        check(character.trade == null && bot.tradeStateMachine.OfferAccepted, "满足差价容忍后确认并接收交易服务回调");
        bot.UpdateState(); bot.UpdateState();
        check(bot.output.Poll()?.value == 2 && bot.output.Poll()?.text == "谢谢，下次再来。", "成功交易清理发原端表情与感谢台词");
        BotScheduler.Clock = 2999; bot.UpdateState();
        check(bot.lastTradeResult == null, "交易结束等待2000ms期间不提前完成");
        BotScheduler.Clock = 3000; bot.UpdateState(); bot.UpdateState(); bot.UpdateState();
        check(bot.lastTradeResult == TradeResult.SUCCESSFUL && bot.state == BotStateMachine.State.RUNNING &&
            bot.tradeStateMachine == null && bot.CheckIfNotRunningOrPaused(), "交易状态清理完成后恢复RUNNING并保留结束停顿");
        BotScheduler.Clock = 5000;
        check(!bot.CheckIfNotRunningOrPaused(), "结束停顿到期后恢复业务行为");
        session = PairTrade(character, player); bot.tradeHandler.confirmedPartner = player;
        bot.tradeWants.mesoWanted = 100; ((TradeProbe)session.partner).locked = true;
        var declined = new BotTradeStateMachine(bot, TradeMode.SELLING);
        declined.Update(); declined.Update(); declined.Update();
        check(bot.tradeLogic.blocked.IsBlocked(character.id, player.id) && session.declined == 0,
            "不足价且对方锁定时加入300秒黑名单，拒绝保留原端延时");
        BotScheduler.Clock = 7000; declined.Update();
        check(session.declined == 1, "拒绝交易延时后关闭交易会话");
        session = PairTrade(character, player); bot.tradeHandler.confirmedPartner = player;
        var buying = new BotTradeStateMachine(bot, TradeMode.BUYING);
        bot.tradeWants.SetWants(0, 4000001); bot.tradeWants.mesoOffering = 250000;
        buying.Update(); buying.Update();
        check(buying.state == BotTradeStateMachine.State.WAITING_RESPONSE, "收购物品未到位时不先放出金币");
        session.partner.Items.Add(new InventoryEntry { itemId = 4000001, quantity = 1 });
        buying.Update(); buying.Update();
        check(session.meso == 250000 && buying.state == BotTradeStateMachine.State.CONFIRMING, "收购物品符合需求后报价并进入确认");
        session.Decline();
        session = PairTrade(character, player);
        var timeout = new BotTradeStateMachine(bot, TradeMode.NULL); timeout.Update();
        BotScheduler.Clock = 67000; timeout.Update();
        check(timeout.state == BotTradeStateMachine.State.WAITING_RESPONSE, "交易60秒边界未超时");
        BotScheduler.Clock = 67001; timeout.Update(); timeout.Update();
        BotScheduler.Clock = 69001; timeout.Update();
        check(timeout.TradeComplete && session.declined == 1, "交易严格超过60秒后等待2秒并关闭");
        bot.SetState(BotStateMachine.State.PAUSE); bot.HasPlayersOnMap = () => false; bot.UpdateState();
        check(bot.CheckIfNotRunningOrPaused(), "PAUSE状态不继续子类业务");
        bot.HasPlayersOnMap = () => true; bot.UpdateState();
        check(bot.state == BotStateMachine.State.RUNNING, "真人进图恢复PAUSE状态");
        bot.running = false; bot.UpdateState(); bot.UpdateState();
        check(bot.state == BotStateMachine.State.IDLE && !bot.running, "离线状态经过FINISHED释放并回IDLE");
        BotStore.RemoveActiveBot(character.id); BotScheduler.Stop(); BotTasks.Stop();
        check(BotTradeStateMachine.FormatPrice(1250) == "1.3k" && BotTradeStateMachine.FormatPrice(1000000) == "1m", "市场价格缩写保留Java HALF_UP舍入与去尾零");
    }
    static void CheckMarketDirector(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Clock = 10000;
        var director = new MarketBotDirector(() => null, new FirstChoiceRandom());
        var character = new BotCharacter { id = 20205, mapId = 100 };
        var bot = new MarketProbe(character) { marketDirector = director, HasPlayersOnMap = () => false };
        BotStore.AddActiveBot(character.id, bot); bot.StartScheduledTask(0);
        check(!BotScheduler.IsRegistered(character.id) && director.ActiveCount == 1, "市场假人注册共享驱动，避免同时进入普通调度轮");
        BotScheduler.Clock = 10250; BotTasks.Drive(10250);
        check(bot.updates == 0, "市场空图按原端休眠而非持续跑AI");
        bot.HasPlayersOnMap = () => true; BotScheduler.Clock = 18250; BotTasks.Drive(18250);
        check(bot.updates == 1, "市场休眠截止后有真人恢复业务");
        check(director.TryPathfind() && director.TryPathfind() && director.TryPathfind() && !director.TryPathfind(), "市场共享每轮最多3次寻路预算");
        check(director.TryChat(character) && !director.TryChat(character), "同一市场假人发言受共享冷却约束");
        BotScheduler.Clock = 18750;
        check(director.TryChat(character) && director.TryMegaphone() && !director.TryMegaphone(), "发言500ms边界恢复，喇叭全局8秒去重");
        BotStore.RemoveActiveBot(character.id); director.Stop(); BotTasks.Stop(); BotScheduler.Stop();
        check(director.ActiveCount == 0, "移除市场假人清除共享驱动登记");
    }
    static void CheckRecordedPlayback(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Clock = 100;
        var actor = new BotCharacter { id = 20990, mapId = 0, x = 300, y = 400 };
        BotRegistry.Add(actor);
        var first = new BotMovementRecording.Command(0, 10, 20, 0, 0, 1, 2, 50);
        var second = new BotMovementRecording.Command(0, 30, 25, 0, 0, 1, 4, 50);
        byte[] packet = BotMovementPacket.Reconstruct(new[] { first, second });
        byte[] shifted = BotMovementPacket.Offset(packet, 300, 400, 7);
        var commands = BotMovementPacket.Deconstruct(shifted);
        check(commands.Count == 2 && commands[0].x == 300 && commands[1].x == 320 && commands[1].y == 405 && commands[1].foothold == 7,
            "录制包按首命令偏移且保持相对位移与当前平台");
        byte[] broadcast = BotMovementPacket.Apply(shifted, actor);
        check(actor.x == 320 && actor.y == 405 && actor.stance == 4 && broadcast.Length == shifted.Length - 9,
            "移动包立即应用末命令并只广播实际消费区域");
        bool completed = false;
        var frames = new[] { new BotMovementRecording.Packet { timestamp = 1000, data = packet },
            new BotMovementRecording.Packet { timestamp = 1075, data = packet } };
        BotMovementPlayback.Play(actor, frames, true, stopped => completed = !stopped);
        check(!completed && actor.x == 340, "录制回放首包立即执行并以当前位置逐包偏移");
        BotScheduler.Clock = 174; BotTasks.Drive(174);
        check(!completed && actor.x == 340, "录制相邻包等待完整timestamp差值");
        BotScheduler.Clock = 175; BotTasks.Drive(175);
        check(completed && actor.x == 360, "录制末包后立即完成且不额外等待duration");
        var playback = BotMovementPlayback.Play(actor, frames, false);
        playback.Interrupt(); BotScheduler.Clock = 250; BotTasks.Drive(250);
        check(actor.stance == 4, "录制中断注入两条原端停止命令");
        completed = false;
        BotMovementPlayback.Play(actor, frames, false, stopped => completed = true);
        BotRegistry.EnterMap(actor, 1); BotScheduler.Clock = 325; BotTasks.Drive(325);
        check(!completed && actor.mapId == 1, "换图取消旧录制且不触发旧业务续接");
        BotRegistry.EnterMap(actor, 0); actor.x = 1000; actor.y = 1000;
        bool stoppedAtPoint = false;
        BotMovementPlayback.Play(actor, frames, true, interrupted => stoppedAtPoint = !interrupted,
            stopPoint: new BotNavigationGraph.Point(10, 20));
        check(stoppedAtPoint && actor.x == 1020 && actor.stance == 4,
            "录制停止点检查原始坐标，命中后注入停止包并正常完成");
        BotRegistry.Remove(actor); BotTasks.Stop();
    }

    static void CheckMovementRuntime(Action<bool, string> check)
    {
        var map = new MapRuntime { mapId = 77, left = -300, right = 300, top = -300, bottom = 300 };
        map.AddFoothold(-250, 100, 250, 100); map.footholds[0].id = 1;
        var bot = new BotCharacter { id = 20777, mapId = 77, x = 0, y = 100, facing = 1 };
        var motion = new BotMovementRuntime(bot, map);
        check(motion.CalcStep(80) == 0 && motion.CalcStep(81) > 0 && motion.CalcStep(31) > 0 && motion.CalcStep(30) == 0,
            "原地面停止30/启动80像素迟滞边界通过执行器保持");
        int broadcasts = 0; motion.Broadcast = (_, bytes) => broadcasts++;
        motion.Tick(new BotNavigationGraph.Point(200, 100), 50);
        check(bot.x > 0 && bot.stance == 2 && motion.travel.Speed > 0, "实际移动执行器接8ms加速并更新角色行走状态");
        motion.Teleport(new BotNavigationGraph.Point(0, 100)); motion.Publish(100); int before = broadcasts;
        motion.Publish(150);
        check(broadcasts == before, "实际移动广播按位置速度姿态平台去重");
        motion.Jump(0); bool airborne = motion.InAir;
        for (int i = 0; i < 50 && motion.InAir; i++) motion.Tick(null, 200 + i * 50);
        check(airborne && !motion.InAir && bot.y == 100, "实际移动执行器普通跳完整离地落地而非横向插值");
        map.ropes.Add(new MapRope { id = 1, x = 0, y1 = -20, y2 = 100, ladder = true });
        motion = new BotMovementRuntime(bot, map); motion.Attach(map.ropes[0], 80);
        motion.Tick(new BotNavigationGraph.Point(0, 0), 1000);
        check(motion.Climbing && bot.y == 75 && bot.stance == 14, "实际绳梯执行器按每50ms五像素攀爬且使用梯子姿态");
        var navMap = new MapRuntime { mapId = 78, left = -200, right = 200, top = -200, bottom = 200 };
        navMap.AddFoothold(-50, 30, 50, 30); navMap.footholds[0].id = 1;
        navMap.AddFoothold(-150, 100, 150, 100); navMap.footholds[1].id = 2;
        var graph = new BotNavigationGraphBuilder(navMap).Build();
        bot = new BotCharacter { id = 20778, mapId = 78, x = 0, y = 100, facing = 1 };
        motion = new BotMovementRuntime(bot, navMap);
        var nav = new BotNavigationRuntime(motion, graph, new FirstChoiceRandom());
        for (int i = 0; i < 120 && bot.y != 30; i++) nav.Tick(new BotNavigationGraph.Point(0, 30), i * 50, i % 2 == 1, true);
        check(bot.y == 30, "实际导航与物理执行器走通不同平台上跳路线");
        var water = new MapRuntime { mapId = 79, swim = true, left = -300, right = 300, top = -300, bottom = 300 };
        bot = new BotCharacter { id = 20779, mapId = 79, x = 0, y = 0 };
        motion = new BotMovementRuntime(bot, water); motion.Fall(0); motion.Tick(new BotNavigationGraph.Point(0, -150), 1000);
        check(motion.Swimming && motion.swim.verticalSpeed < 0 && motion.nextSwimJumpMs == 1500,
            "游泳运行意图上方目标触发500ms冷却跳跃脉冲");
        BotTasks.Stop(); BotMovementService.Shutdown(); BotScheduler.Clock = 1000;
        BotMovementService.IsObserved = _ => true;
        BotMovementService.ResolveProfile = (_, __) => BotMovementProfile.Base;
        MapService.Register(map);
        bot = new BotCharacter { id = 20780, mapId = 77, x = -150, y = 100 };
        BotRegistry.Add(bot); bool arrived = false;
        BotMovementService.Move(bot, 150, 100, () => arrived = true);
        for (int i = 0; i < 150 && !arrived; i++) { BotScheduler.Clock = 1000 + i * 50; BotTasks.Drive(BotScheduler.Clock); }
        check(arrived && Math.Abs(bot.x - 150) <= 8, "50ms移动服务与100ms导航AI调度走通精确目标并回调到达");
        BotMovementService.Move(bot, -150, 100); BotRegistry.Remove(bot);
        float removedX = bot.x; BotScheduler.Clock += 1000; BotTasks.Drive(BotScheduler.Clock);
        check(BotMovementService.Get(bot) == null && bot.x == removedX, "移除角色取消实际移动服务回调与导航状态");
        var picker = new BotGroundSpotPicker(new FirstChoiceRandom());
        var spots = picker.PickMany(map, new BotNavigationGraph.Point(0, 100), 3, -100, 100);
        check(spots.Count == 3 && spots.TrueForAll(p => p.x >= -100 && p.x <= 100 && p.y == 100),
            "原可达平台选点器严格按限制区间返回真实平台点");
        BotMovementService.Shutdown(); BotTasks.Stop(); BotMovementService.IsObserved = null; BotMovementService.ResolveProfile = null;
        MapService.Unregister(77);

    }

    static void CheckRuntimeSession(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Stop(); BotScheduler.Clock = 1000;
        var db = DbStore.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_bot_runtime_test.json"));
        using var runtime = new BotRuntime(db);
        var character = new FullCharacter { record = new CharRecord { id = 1, name = "RuntimePlayer", level = 120 } };
        var actor = runtime.SynchronizePlayer(character, 100000000, 0, 0);
        check(runtime.observers.IsFull(100000000) && BotMovementService.Observed(100000000) && actor.character == character,
            "本地假人会话把真实角色地图同步到观察者和移动门控");
        var map = runtime.GetMap(100000000);
        check(ReferenceEquals(map, MapService.Get(100000000)) && map.footholds.Count > 0,
            "本地假人会话注册原始IMG几何供运行时落点与导航共享");
        runtime.Drive(3001); runtime.SynchronizePlayer(character, 101000000, 100, 100);
        check(runtime.observers.IsFull(101000000) && !runtime.observers.IsFull(100000000) && ReferenceEquals(actor, new List<BotActor>(runtime.Players)[0]),
            "真人换图复用同一交互身份并更新观察地图集合");
        var portalMap = new MapRuntime { mapId = -889, left = -100, right = 100, top = -100, bottom = 200 };
        portalMap.AddFoothold(-100, 100, 100, 100); portalMap.footholds[0].id = 1;
        portalMap.portals.Add(new MapPortal { id = 0, name = "sp", x = 20, y = 100 }); MapService.Register(portalMap);
        var bot = new BotCharacter { id = 20887, mapId = 100000000 };
        BotRegistry.Add(bot); BotMovementService.Enable(bot);
        bool entered = runtime.EnterPortal(bot, new MapPortal { targetMap = -889, targetName = "missing" });
        runtime.Drive(3100); runtime.Drive(3150);
        check(entered && bot.mapId == -889 && BotRegistry.OnMap(-889).Contains(bot) && BotMovementService.Get(bot)?.map.mapId == -889,
            "假人传送门按源端回退portal0并重建目标地图移动执行器");
        var chatBot = new BotCharacter { id = 20886, name = "RuntimeChatBot" };
        BotRegistry.Add(chatBot); BotRegistry.EnterMap(chatBot, 101000000);
        var social = BotTypeManager.CreateAndSetBot(chatBot, BotType.SOCIAL_BOT);
        check(runtime.ReceiveChat(actor.id, "RuntimeChatBot 你好") && !runtime.ReceiveChat(actor.id, "/ignored"),
            "实际会话聊天入口接primary队列并排除斜线命令");
        runtime.Drive(5001);
        check(social.running && ReferenceEquals(social.interactors.Respondant, actor),
            "实际会话每两秒分派地图名字匹配消息，真人身份进入社交假人交互");
        runtime.SynchronizePlayer(character, 101000000, 100, 100, 2);
        check(actor.channel == 2 && ReferenceEquals(actor, new List<BotActor>(runtime.Players)[0]),
            "同图换频道在发布入图事件前更新真人身份，避免跨频道分派和显示");
        runtime.RemovePlayer(actor.id);
        check(!runtime.observers.HasRealPlayerNow(101000000), "真人离开假人会话后不再充当地图观察者");
        runtime.Dispose(); MapService.Unregister(-889);
        check(BotTasks.PendingCount == 0 && BotScheduler.Size() == 0 && BotMovementService.IsObserved == null,
            "本地服务端退出释放假人观察调度与移动输出连接");
    }

    static void CheckTownWanderer(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Clock = 0;
        var map = new MapRuntime { mapId = -888, left = -300, right = 300, top = -200, bottom = 200 };
        map.AddFoothold(-250, 100, 250, 100); map.footholds[0].id = 1; MapService.Register(map);
        var actor = new BotCharacter { id = 20888, mapId = -888, x = 0, y = 100 };
        BotRegistry.Add(actor);
        var output = new BotOutput(); var flavor = new BotFlavor(new FirstChoiceRandom()); var events = new EventBus();
        var town = new TownWandererBot(actor, new BotDialogue(BotResources.Load()), flavor, events, new FirstChoiceRandom())
            { output = output, running = true, HasPlayersOnMap = () => true };
        BotStore.AddActiveBot(actor.id, town); BotMovementService.IsObserved = _ => true;
        town.UpdateState();
        check(town.wanderState == TownWandererBot.WanderState.Idle && BotWander.IsWandering(actor),
            "城镇游荡具体类型初始化后启动原版地图游荡服务");
        town.UpdateState(); town.UpdateState();
        bool chat = false, emote = false; BotOutput.Message message;
        while ((message = output.Poll()) != null) { chat |= message.kind == BotOutput.Kind.Chat; emote |= message.kind == BotOutput.Kind.Emote; }
        check(emote && !chat, "城镇源对话解析失败沿用原端跳过发言，表情和游荡继续且不编造台词");
        var player = new BotActor { id = 1, mapId = -888, x = -100, y = 100 };
        town.HandleEvent(EventFactory.LevelUp(player));
        BotScheduler.Clock = 9000; BotTasks.Drive(9000);
        BotScheduler.Clock = 9700; BotTasks.Drive(9700);
        BotScheduler.Clock = 10100; BotTasks.Drive(10100);
        emote = false; while ((message = output.Poll()) != null) emote |= message.kind == BotOutput.Kind.Emote;
        check(actor.facing == -1 && emote, "城镇升级祝贺执行5至9秒等待、转向、300至700毫秒思考及400毫秒表情时序");
        town.StopScheduledTask();
        check(!BotWander.IsWandering(actor) && BotMovementService.Get(actor) == null, "停用城镇假人释放游荡与实际移动执行器");
        BotStore.RemoveActiveBot(actor.id);
        var mapped = BotTypeManager.CreateAndSetBot(actor, BotType.TOWN_WANDERER_BOT);
        check(mapped is TownWandererBot && mapped.botType == "TownWandererBot", "城镇假人类型映射具体实现并保留源端对话类型名");
        BotGeneration.RemoveBotFromServer(actor); MapService.Unregister(-888);
        BotMovementService.Shutdown(); BotTasks.Stop(); BotMovementService.IsObserved = null;
    }

    static void CheckAmbientMap(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Clock = 0;
        var map = new MapRuntime { mapId = -887 };
        map.AddFoothold(-300, 100, 300, 100); map.footholds[0].id = 1; MapService.Register(map);
        var actor = new BotCharacter { id = 20887, mapId = -887, x = 0, y = 100 };
        BotRegistry.Add(actor);
        var ambient = new AmbientMapBot(actor, null, () => Array.Empty<BotActor>(), new FirstChoiceRandom())
            { running = true, HasPlayersOnMap = () => true };
        BotStore.AddActiveBot(actor.id, ambient);
        BotMovementService.IsObserved = _ => false;
        ambient.UpdateState(); BotScheduler.Clock = 801; ambient.UpdateState();
        check(BotMovementService.Get(actor) == null, "环境路人未被观察时不启动随机行走");
        BotMovementService.IsObserved = _ => true;
        BotScheduler.Clock = 1602; ambient.UpdateState();
        check(BotMovementService.Get(actor) != null && BotMovementService.IsMoving(actor), "环境路人原权重移动分支接实际物理导航执行器");
        ambient.StopScheduledTask();
        check(BotMovementService.Get(actor) == null, "环境路人停用清理移动执行器");
        BotGeneration.RemoveBotFromServer(actor); MapService.Unregister(-887);
        BotMovementService.Shutdown(); BotTasks.Stop(); BotMovementService.IsObserved = null;
    }

    static void CheckTownPresence(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Clock = 0;
        var config = new BotTownPresenceConfig(BotResources.Load());
        check(config.Towns.Count > 0 && config.Towns[0].name == "Kerning City" && config.Towns[0].MainMapId == 103000000,
            "城镇存在配置直接读取源YAML并保留主地图顺序");
        var ov = new BotTownOverrides();
        ov.ban.Add(new BotTownOverrides.Zone(10, 20, -10, -20, 1));
        ov.boost.Add(new BotTownOverrides.Zone(-50, -50, 50, 50, 2));
        ov.boost.Add(new BotTownOverrides.Zone(-50, -50, 50, 50, 3));
        check(ov.IsBanned(-10, 20) && !ov.IsBanned(11, 20) && ov.Boost(0, 0) == 6,
            "城镇禁区保留反向端点和闭区间，重叠boost按原端相乘");
        var map = new MapRuntime { mapId = -886 }; map.AddFoothold(-100, 100, 100, 100); map.footholds[0].id = 1;
        var sampler = new BotTownPresenceSampler(new FirstChoiceRandom());
        ov.pins.Add(new BotNavigationGraph.Point(25, 0)); ov.pins.Add(new BotNavigationGraph.Point(200, 0));
        var pins = sampler.Sample(map, new BotNavigationGraph.Point(0, 100), 2, null, ov);
        check(pins.Count == 2 && pins[0].x == 25 && pins[0].y == 100 && pins[1].x == 200 && pins[1].y == 0,
            "城镇固定点优先且落地下方平台，找不到地面保留原坐标");
        var slightlyBelow = new BotTownOverrides(); slightlyBelow.pins.Add(new BotNavigationGraph.Point(25, 110));
        check(sampler.Sample(map, new BotNavigationGraph.Point(0, 100), 1, null, slightlyBelow)[0].y == 100,
            "城镇固定点复用原findGroundPoint上探26px选择更近平台");
        var sampled = sampler.Sample(map, new BotNavigationGraph.Point(0, 100), 3, new[] { new BotNavigationGraph.Point(0, 100) });
        check(sampled.Count == 3 && sampled.TrueForAll(p => p.x >= -100 && p.x <= 100 && p.y == 100),
            "城镇加权选点从可达地面采样并保留原数量");
        var spawnMap = new MapRuntime { mapId = 103000000 }; spawnMap.AddFoothold(-100, 100, 100, 100);
        spawnMap.footholds[0].id = 1; spawnMap.portals.Add(new MapPortal { id = 0, x = 0, y = 100 });
        MapService.Register(spawnMap); bool complete = false;
        BotGeneration.EnableEnvironmentBotLimit();
        using (var spawner = new BotTownSpawner(config, id => id == 103000000 ? spawnMap : null, sampler))
            spawner.Spawn(() => complete = true);
        var created = BotRegistry.OnMap(103000000);
        check(complete && created.Count == 2 && created.TrueForAll(b => BotStore.GetBotById(b.id) is TownWandererBot),
            "城镇生成按原端每城最多两人并映射具体游荡类型，缺portal地图跳过");
        foreach (var bot in created) BotGeneration.RemoveBotFromServer(bot);
        BotGeneration.DisableEnvironmentBotLimit(); MapService.Unregister(103000000);
        BotTasks.Stop(); BotTypeManager.Output.Clear();

    }

    static void CheckMarketAmbient(Action<bool, string> check)
    {
        BotTasks.Stop(); BotScheduler.Clock = 0;
        var map = new MapRuntime { mapId = 910000000 }; map.AddFoothold(-500, 34, 1500, 34); map.footholds[0].id = 1;
        MapService.Register(map);
        var actor = new BotCharacter { id = 20885, mapId = 910000000, x = 0, y = 33 };
        BotRegistry.Add(actor); BotRegistry.EnterMap(actor, map.mapId);
        var ambient = new MarketBotAmbient(BotResources.Load(), () => null, new FirstChoiceRandom());
        var fm = new FMBot(actor, ambient, new FirstChoiceRandom()) { running = true, HasPlayersOnMap = () => true, output = new BotOutput() };
        BotStore.AddActiveBot(actor.id, fm);
        fm.UpdateState();
        check(fm.UsesSharedMarketTick && fm.marketState == FMBot.MarketState.Ambient && fm.Room == 1,
            "自由市场具体类型按原端进入共享环境循环，不启用旧购物分支");
        BotScheduler.Clock = 1600; fm.UpdateState(); BotTasks.Drive(1600);
        check(BotRecordedActions.IsMoving(actor) && actor.x == 10,
            "市场散步按原端10px步行封包启动，不用临时速度替代");
        for (int i = 1; i <= 18; i++) { BotScheduler.Clock = 1600 + i * 160; BotTasks.Drive(BotScheduler.Now()); }
        check(!BotRecordedActions.IsMoving(actor) && actor.x == 100 && actor.stance == 4,
            "市场散步每160毫秒一步，距终点20px结束并注入双停止命令");
        BotRecordedActions.WalkAlongX(actor, -200, 18, null); float before = actor.x;
        BotRegistry.LeaveMap(actor); BotScheduler.Clock += 160; BotTasks.Drive(BotScheduler.Now());
        check(!BotRecordedActions.IsMoving(actor) && actor.x == before, "假人离图取消封包步行且不运行旧回调");
        check(!FMBot.ShouldPurchaseItem(121, 100, new FirstChoiceRandom()) && FMBot.ShouldPurchaseItem(70, 100, new FirstChoiceRandom()),
            "保留原市场估价辅助函数的1.2拒买边界和0.7必买档");
        BotGeneration.RemoveBotFromServer(actor); BotTasks.Stop(); MapService.Unregister(map.mapId);
        var mappedActor = new BotCharacter { id = 20884 };
        check(BotTypeManager.CreateAndSetBot(mappedActor, BotType.FM_BOT) is FMBot, "FM_BOT已替换通用占位类型");
        BotStore.RemoveActiveBot(mappedActor.id); BotTypeManager.Output.Clear();
    }

}
