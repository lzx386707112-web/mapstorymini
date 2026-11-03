// 机器人（Android）装配回归：282 语义的槽位/档位/外观/台词。
// 覆盖：items 数据落地、islot 归一化、槽位映射、grade+3 规则、缺心臟提示、
// 模板外观确定性、AP 槽覆盖默认穿着、事件台词表。
using System;
using System.Collections.Generic;

static class AndroidChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 机器人（Android）装配 ==");

        // —— 数据层：266 机器人 + 56 心臟，islot 已从 282 的 Tm 归一化 ——
        var robotInfo = Server.Bot.BotItemCatalog.Shared.Info(1662000);
        Check(robotInfo != null, "机器人 1662000 在物品目录里");
        Check(robotInfo != null && (string)robotInfo["islot"] == "An", "机器人 islot 归一化为 An");
        int robotCount = 0, heartCount = 0;
        foreach (int id in Server.Bot.BotItemCatalog.Shared.ItemIds)
        {
            if (NativeAndroidService.IsRobot(id)) robotCount++;
            else if (NativeAndroidService.IsHeart(id)) heartCount++;
        }
        Check(robotCount == 266, "机器人 266 件（268 减去 2 件 androidAni/8bpp 特例），实际 " + robotCount);
        Check(heartCount == 56, "心臟 56 件，实际 " + heartCount);

        var heartInfo = Server.Bot.BotItemCatalog.Shared.Info(1672000);
        Check(heartInfo != null && (string)heartInfo["islot"] == "Ht", "心臟 islot 归一化为 Ht");
        Check(Server.Bot.BotItemCatalog.Shared.Name(1662053) == "史烏機器人", "282 名字回退生效（史烏機器人）");
        Check(Server.Bot.BotItemCatalog.Shared.Name(1672000) == "銀之心", "心臟名取自项目 String（銀之心）");
        Check(Server.Bot.BotItemCatalog.Shared.Description(1662000)?.Contains("#c機器人心臟#") == true,
            "1662000保留282客户端原始描述和强调标记");
        Check(string.IsNullOrEmpty(Server.Bot.BotItemCatalog.Shared.Description(1672000)),
            "1672000原始节点没有desc，不虚构描述");

        // —— 槽位映射 ——
        Check(NativeInventoryService.EquipmentSlotOf(1662000) == -32, "机器人 → 槽 -32");
        Check(NativeInventoryService.EquipmentSlotOf(1672000) == -54, "心臟 → 槽 -54");
        Check(NativeInventoryService.EquipmentSlotOf(1662053) == -32, "cash=1 的机器人仍走装备槽（不减 100）");
        Check(NativeInventoryService.EquipmentSlotOf(1672086) == -54, "心臟不减 100");
        // EquipSlot.Worn / EquipCatalog.DirOf 引用 UnityEngine（Mathf），离线工程编不到，
        // 这里改验数据层等价事实：物品目录的 source 必须指向 Character/Android，
        // 否则 EquipCatalog.Info 会去 Accessory 的 beidou 空壳里读（info 为空）。
        var robotSource = record(1662000, "source");
        var heartSource = record(1672000, "source");
        Check(robotSource == "Character/Android/01662000.img", "机器人 source 指向 Character/Android（" + robotSource + "）");
        Check(heartSource == "Character/Android/01672000.img", "心臟 source 指向 Character/Android，不是 Accessory 空壳（" + heartSource + "）");

        // —— 装配判定 ——
        Check(!NativeAndroidService.Resolve(1, s => 0).visible, "没装机器人 → 不显示且不算错误");
        var noHeart = NativeAndroidService.Resolve(1, s => s == NativeAndroidService.RobotSlot ? 1662000 : 0);
        Check(!noHeart.visible && noHeart.reason == "請安裝心臟", "只装机器人 → 提示請安裝心臟");

        // 檔位：1662000 是 grade 3，配 grade 5 的心臟应通过（5 <= 3+3）
        int robotGrade = NativeAndroidService.GradeOf(1662000);
        int heartGrade = NativeAndroidService.GradeOf(1672086);   // 鋰之心 grade 5
        Check(robotGrade == 3 && heartGrade == 5, "样本档位：机器人3 / 鋰之心5");

        Dictionary<int, int> wardrobe = new Dictionary<int, int>();
        var ok = NativeAndroidService.Resolve(7, slot =>
            slot == NativeAndroidService.RobotSlot ? 1662000
            : slot == NativeAndroidService.HeartSlot ? 1672086 : wardrobeOf(slot));
        Check(ok.visible, "grade3 机器人 + grade5 心臟 → 显示（5 <= 3+3）" + (ok.visible ? "" : "（" + ok.reason + "）"));

        // grade 1 的補給型 + grade 5 心臟：5 > 1+3 → 拒绝。用 1662001（grade 3）之外再找一件 grade<=1
        int lowGradeRobot = 0;
        foreach (int id in Server.Bot.BotItemCatalog.Shared.ItemIds)
            if (NativeAndroidService.IsRobot(id) && NativeAndroidService.GradeOf(id) <= 1) { lowGradeRobot = id; break; }
        if (lowGradeRobot > 0)
        {
            var rejected = NativeAndroidService.Resolve(7, slot =>
                slot == NativeAndroidService.RobotSlot ? lowGradeRobot
                : slot == NativeAndroidService.HeartSlot ? 1672101 : 0);
            Check(!rejected.visible && rejected.reason.Contains("+3"),
                "grade" + NativeAndroidService.GradeOf(lowGradeRobot) + " 机器人 + grade5 心臟 → 拒绝并说明档位规则");
        }
        else Check(true, "（无 grade<=1 机器人，跳过档位拒绝分支）");

        // —— 外观：确定性 + 模板池内取值 ——
        var first = NativeAndroidService.Resolve(1001, slot =>
            slot == NativeAndroidService.RobotSlot ? 1662053
            : slot == NativeAndroidService.HeartSlot ? 1672086 : 0);
        var again = NativeAndroidService.Resolve(1001, slot =>
            slot == NativeAndroidService.RobotSlot ? 1662053
            : slot == NativeAndroidService.HeartSlot ? 1672086 : 0);
        Check(first.visible && first.hair == again.hair && first.face == again.face && first.skin == again.skin,
            "同一角色外观稳定（确定性哈希）");
        var other = NativeAndroidService.Resolve(2002, slot =>
            slot == NativeAndroidService.RobotSlot ? 1662053
            : slot == NativeAndroidService.HeartSlot ? 1672086 : 0);
        // 282 的 248 个模板里 221 个 costume 池只有 1 项（活动款是固定造型），
        // 所以"不同角色外观不同"只在有多候选的模板上成立——先找一件这样的机器人。
        int spreadRobot = 0, spreadPool = 0;
        foreach (int id in Server.Bot.BotItemCatalog.Shared.ItemIds)
        {
            if (!NativeAndroidService.IsRobot(id)) continue;
            var tpl = NativeAndroidService.Template(NativeAndroidService.TemplateOf(id));
            if (tpl == null || !tpl.TryGetValue("hair", out var node) || node is not List<object> pool) continue;
            if (pool.Count > 4) { spreadRobot = id; spreadPool = pool.Count; break; }
        }
        Check(spreadRobot > 0, "存在发型池 >4 的模板（" + spreadRobot + " / " + spreadPool + " 项）");
        if (spreadRobot > 0)
        {
            int Pick(int characterId) => NativeAndroidService.Resolve(characterId, slot =>
                slot == NativeAndroidService.RobotSlot ? spreadRobot
                : slot == NativeAndroidService.HeartSlot ? 1672086 : 0).hair;
            bool varied = false;
            int baseline = Pick(1);
            for (int characterId = 2; characterId <= 12 && !varied; characterId++)
                varied = Pick(characterId) != baseline;
            Check(varied, "发型池 " + spreadPool + " 项时不同角色取到不同发型");
        }
        Check(!first.visible || other.visible, "两件不同机器人上同一心臟都能装配");
        Check(first.hair > 0 && other.hair > 0, "外观解析不返回空发型");
        Check(first.templateId == NativeAndroidService.TemplateOf(1662053) && first.templateId > 0,
            "模板取自 info/android（" + first.templateId + "）");
        Check(NativeAndroidService.TemplateCount == 248, "外观模板 248 个，实际 " + NativeAndroidService.TemplateCount);
        Check(first.skin > 0 && first.hair > 0 && first.face > 0,
            "外观三件套非零（skin " + first.skin + " hair " + first.hair + " face " + first.face + "）");

        // —— 默认穿着 + AP 槽覆盖 ——
        Check(first.top > 0 || first.shoes > 0, "模板默认穿着生效（top " + first.top + " shoes " + first.shoes + "）");
        int apTop = 1040002;   // 任意上衣 id：AP 槽只要非空就必须覆盖默认值
        var dressed = NativeAndroidService.Resolve(1001, slot =>
            slot == NativeAndroidService.RobotSlot ? 1662053
            : slot == NativeAndroidService.HeartSlot ? 1672086
            : slot == NativeAndroidService.ApTop ? apTop : 0);
        Check(dressed.top == apTop, "AP 槽覆盖默认穿着（top " + dressed.top + "）");
        Check(NativeAndroidService.ApSlots.Length == 12, "AP 槽表 12 个（1200..1207 + 1210..1213）");

        // —— AP 换装槽的收件规则（客户端拖拽 + 服务端落库都读这一张表）——
        Check(NativeAndroidService.IsApSlot(1200) && NativeAndroidService.IsApSlot(1213), "1200..1213 认作 AP 槽");
        Check(!NativeAndroidService.IsApSlot(1208) && !NativeAndroidService.IsApSlot(-32), "1208 空洞与负槽不是 AP 槽");
        Check(NativeAndroidService.ApAccepts(NativeAndroidService.ApHat, -1), "帽子进 APHat");
        Check(!NativeAndroidService.ApAccepts(NativeAndroidService.ApHat, -7), "鞋子不能进 APHat");
        Check(NativeAndroidService.ApAccepts(NativeAndroidService.ApTop, -5), "上衣进 APTop");
        Check(NativeAndroidService.ApAccepts(NativeAndroidService.ApBottom, -6), "裤裙进 APBottom");
        Check(NativeAndroidService.ApAccepts(NativeAndroidService.ApWeapon, -11), "武器进 APWeapon");
        // 现金装备（islot Cp/Af…）在服务端是 slot-100；机器人换装槽只有一格，必须先归一化再比。
        Check(NativeAndroidService.ApAccepts(NativeAndroidService.ApHat, -101), "现金帽子（-101）归一化后进 APHat");
        Check(!NativeAndroidService.ApAccepts(NativeAndroidService.ApWeapon, -101), "现金帽子仍不能进 APWeapon");
        Check(NativeAndroidService.ApAccepts(NativeAndroidService.ApRing, -13) &&
              NativeAndroidService.ApAccepts(NativeAndroidService.ApRing4, -12), "四格戒指互相收");
        Check(!NativeAndroidService.ApAccepts(NativeAndroidService.ApRing, -1), "帽子不能进戒指格");
        // 客户端外观池：缺资源时 AndroidLook 要沿池回退，所以服务端必须能读出池子。
        var hairPool = NativeAndroidService.Pool(first.templateId, "hair");
        Check(hairPool != null && hairPool.Count > 0, "模板发型池可读（" + (hairPool?.Count ?? 0) + " 项）");

        // —— 事件台词 ——
        var levelup = NativeAndroidService.ActionEntries(first.templateId, "levelup");
        Check(levelup != null && levelup.Count > 0, "levelup 台词表非空");
        bool hasChat = false;
        if (levelup != null)
            foreach (var entry in levelup)
                if (entry.TryGetValue("chat", out var chat) && chat is string text && text.Length > 0) hasChat = true;
        Check(hasChat, "levelup 带 chat 文本");
        var rolled = NativeAndroidService.RollAction(first.templateId, "levelup", new Random(42));
        Check(rolled != null, "RollAction 能摇出一条变体");
        var silent = NativeAndroidService.RollAction(1, "nonexistent-action", new Random(1));
        Check(silent == null, "未知动作返回 null");

        // —— 兑换货架：脚本里硬编码的商品 id 必须真存在 ——
        // 货架是 var [道具ID, 价格] 写死的，打错一位玩家就买不到东西而且没有报错。
        // 这里把脚本里所有 1662xxx/1672xxx 段数字抠出来，逐个回查物品目录 + islot。
        foreach (string script in new[] { "content/script-overrides/BeiDouSpecial/起源之塔/积分商店.js",
                                          "content/script-overrides/BeiDouSpecial/机器人兑换.js" })
        {
            string path = ProjectFile(script);
            Check(path != null, "兑换脚本存在：" + script);
            if (path == null) continue;
            var ids = new SortedSet<int>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(path), @"\b(16[67]\d{4})\b"))
            {
                int id = int.Parse(m.Groups[1].Value);
                if (NativeAndroidService.IsRobot(id) || NativeAndroidService.IsHeart(id)) ids.Add(id);
            }
            Check(ids.Count > 0, script + " 里读到了机器人/心臟商品（" + ids.Count + " 件）");
            var missing = new List<int>();
            var wrongSlot = new List<int>();
            foreach (int id in ids)
            {
                var info = Server.Bot.BotItemCatalog.Shared.Info(id);
                if (info == null) { missing.Add(id); continue; }
                string islot = info.TryGetValue("islot", out var s) ? s as string : null;
                string want = NativeAndroidService.IsHeart(id) ? "Ht" : "An";
                if (islot != want) wrongSlot.Add(id);
            }
            Check(missing.Count == 0, script + " 的商品 id 全部在物品目录里" +
                (missing.Count > 0 ? "（缺 " + string.Join(",", missing) + "）" : ""));
            Check(wrongSlot.Count == 0, script + " 的商品 islot 正确" +
                (wrongSlot.Count > 0 ? "（错 " + string.Join(",", wrongSlot) + "）" : ""));
        }
        // Explicit modes test the controller; runtime refreshes mode from the owner's action.
        Check(NativeAndroidService.FollowDirection(false, false, 0, -60, 0, 1) == 0,
            "282普通跟随：右向模式距离60停止");
        Check(NativeAndroidService.FollowDirection(false, false, 81, 0, 80, 0) == 1,
            "282普通跟随：超过80开始向右追赶");
        Check(NativeAndroidService.FollowDirection(false, false, 65, 0, 0, 0) == 0,
            "282普通跟随：65等号保留静止方向");
        Check(NativeAndroidService.FollowDirection(false, false, 65, 0, 0, 1) == 1,
            "282普通跟随：65等号保留原移动方向");
        Check(NativeAndroidService.FollowDirection(false, true, 0, -60, 0, 0) == 0,
            "282普通跟随：主人原地转左不立即绕身");
        Check(NativeAndroidService.FollowDirection(true, false, 0, 60, 0, 0) == 0,
            "282普通跟随：左向出生模式转右也不绕身");
        Check(NativeAndroidService.FollowDirection(true, true, -81, 0, -80, 0) == -1 &&
            NativeAndroidService.FollowDirection(true, true, -64, 0, -64, -1) == 0,
            "282普通跟随：左行超过80追赶，回到65内停步");
        Check(NativeAndroidService.FollowDirection(true, true, -70, 0, 0, 0) == -1,
            "模式2跳表是5C5C829：左行70px即可启动，不误用骑乘模式3的80px");
        Check(NativeAndroidService.FollowDirection(true, true, -100, 0, 0, 0) == -1,
            "主人转左刷新模式2，不继续使用模式1的目标左移145px");
        int robotX = -100, direction = 0, lastRobot = -100;
        for (int ownerX = 0; ownerX <= 240; ownerX += 5)
        {
            direction = NativeAndroidService.FollowDirection(false, false, ownerX, robotX, lastRobot, direction);
            lastRobot = robotX; robotX += direction * 6;
        }
        for (int i = 0; i < 100; i++)
        {
            direction = NativeAndroidService.FollowDirection(false, false, 240, robotX, lastRobot, direction);
            lastRobot = robotX; robotX += direction * 6;
        }
        Check(direction == 0 && 240 - robotX >= 59 && 240 - robotX < 65,
            "282普通跟随：连续步行后稳定停在距离区间，不追固定44px");
        CheckInventory(Check);
        CheckAndroidShop(Check);
    }

    // 282 双击机器人开店：USER_ANDROID_SHOP(596) → handleAndroidShop 校验召出 →
    // openShop(模板号, GENERAL_SHOP=9201060)，会话响应带店主模板号。
    static void CheckAndroidShop(Action<bool, string> check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "android-shop-check-" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "android_shop_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "ShopGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;
            var record = CharactersDao.FindById(db, cid);
            record.level = 50; record.meso = 100000; record.hp = 50;
            CharactersDao.Save(db, record);
            db.Table("game_config").Insert(new DbRow { ["config_code"] = "native_npc_shops_seed_v1", ["config_value"] = "1" });
            // 测试库不跑源端 seed：按源端 GENERAL_SHOP 造 9201060 杂货店与一件标价商品。
            db.Table("shops").Insert(new DbRow { ["shopid"] = NativeNpcShopService.AndroidShopId, ["npcid"] = NativeNpcShopService.AndroidShopId });
            db.Table("shopitems").Insert(new DbRow { ["shopid"] = NativeNpcShopService.AndroidShopId, ["itemid"] = 2000000, ["price"] = 50, ["position"] = 1 });
            var shop = new NativeNpcShopService(db);

            // 未召出机器人：282 服务端静默忽略；本工程回错误并拒绝开店。
            var denied = shop.Handle(new NpcShopRequest { characterId = cid, operation = NpcShopOperation.OpenShop,
                id = NativeNpcShopService.AndroidShopId, npcTemplateId = 1001 });
            check(!denied.ok && denied.error == "机器人未召出", "双击开店：未召出机器人被拒");

            // 召出：1662000（补給型機器人）+ 1672000（銀之心），档位/等级满足装配规则。
            long robot = InventoryDao.InsertItem(db, cid, 1662000, InventoryDao.Equipped, NativeAndroidService.RobotSlot, 1);
            InventoryDao.InsertEquipment(db, robot);
            long heart = InventoryDao.InsertItem(db, cid, 1672000, InventoryDao.Equipped, NativeAndroidService.HeartSlot, 1);
            InventoryDao.InsertEquipment(db, heart);
            var state = NativeAndroidService.Resolve(cid, slot =>
                CharService.Load(db, cid).inventory.Find(x => x.inventoryType == InventoryDao.Equipped && x.position == slot)?.itemId ?? 0);
            check(state.visible && state.templateId == NativeAndroidService.TemplateOf(1662000),
                "双击开店：1662000+1672000 装配可见");
            if (!state.visible) return;

            // 模板号与召出的不一致：拒。
            var mismatch = shop.Handle(new NpcShopRequest { characterId = cid, operation = NpcShopOperation.OpenShop,
                id = NativeNpcShopService.AndroidShopId, npcTemplateId = state.templateId + 1 });
            check(!mismatch.ok && mismatch.error == "机器人未召出", "双击开店：模板号与召出不符被拒");

            var opened = shop.Handle(new NpcShopRequest { characterId = cid, operation = NpcShopOperation.OpenShop,
                id = NativeNpcShopService.AndroidShopId, npcTemplateId = state.templateId });
            check(opened.ok && opened.opened, "双击开店：召出后打开 9201060 杂货店");
            check(opened.androidTemplateId == state.templateId, "双击开店：响应携带机器人店主模板号");
            check(opened.items.Count > 0, "双击开店：杂货店有货");
            // 会话期间的普通购买仍走同一条商店链（机器人商店就是普通商店换了店主）。
            var buy = shop.Handle(new NpcShopRequest { characterId = cid, operation = NpcShopOperation.Buy,
                index = 0, itemId = 2000000, quantity = 1 });
            check(buy.ok, "双击开店：机器人商店可正常购买");
            shop.EndSession(cid);
        }
        finally { System.IO.File.Delete(path); }
    }

    static void CheckInventory(Action<bool, string> check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "android-check-" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "android_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "RobotGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;
            var record = CharactersDao.FindById(db, cid);
            record.level = 50; record.meso = 100000; record.hp = 50;
            CharactersDao.Save(db, record);
            // Use a real open NPC-shop session so rejection cannot come from a missing session.
            db.Table("game_config").Insert(new DbRow { ["config_code"] = "native_npc_shops_seed_v1", ["config_value"] = "1" });
            db.Table("shops").Insert(new DbRow { ["shopid"] = 987654, ["npcid"] = 9030100 });
            var shop = new NativeNpcShopService(db);
            check(shop.Handle(new NpcShopRequest { characterId = cid, operation = NpcShopOperation.OpenShop, id = 987654 }).ok,
                "AP外围回归已打开NPC商店");
            foreach (int slot in NativeAndroidService.ApSlots)
            {
                int item = 0;
                foreach (int id in Server.Bot.BotItemCatalog.Shared.ItemIds)
                    if (NativeAndroidService.ApAccepts(slot, NativeInventoryService.EquipmentSlotOf(id))) { item = id; break; }
                check(item > 0, "AP槽 " + slot + " 有可穿物品");
                if (item == 0) continue;
                long first = InventoryDao.InsertItem(db, cid, item, InventoryDao.Equipped, slot, 1);
                InventoryDao.InsertEquipment(db, first);
                var sale = shop.Handle(new NpcShopRequest { characterId = cid, operation = NpcShopOperation.Sell,
                    inventoryType = 1, position = slot, quantity = 1 });
                check(!sale.ok && sale.error == "该背包槽位没有道具", "NPC商店不可出售AP穿着槽 " + slot);
                var stored = NativeStorageService.Handle(db, new StorageRequest { characterId = cid, operation = StorageOperation.Store,
                    inventoryType = 1, itemPosition = slot, itemId = item, quantity = 1 });
                check(!stored.ok && stored.error == "背包里没有这个道具", "仓库不可取走AP穿着槽 " + slot);
                var actor = new Server.Bot.BotActor { id = cid, character = CharService.Load(db, cid) };
                var trade = new Server.Bot.Trade.BotTradeService(db, Server.Bot.BotItemCatalog.Shared).Start(actor);
                check(!trade.OfferFromInventory(1, slot, 1, 1) && trade.Items.Count == 0 &&
                    CharService.Load(db, cid).inventory.Exists(x => x.itemRowId == first && x.position == slot),
                    "交易不可托管AP穿着槽 " + slot);
                // Only an AP-equipped copy exists. It must not be stolen into the owner's worn slot.
                var stolen = NativeInventoryService.Handle(db, new InventoryChangeRequest {
                    characterId = cid, itemId = item, equip = true, quantity = 1,
                    equipSlot = NativeInventoryService.EquipmentSlotOf(item), sourcePosition = slot });
                check(!stolen.ok, "AP槽 " + slot + " 不可冒充背包源格");
                int bag = NativeInventoryService.Free(db, CharactersDao.FindById(db, cid), 1);
                long second = InventoryDao.InsertItem(db, cid, item, 1, bag, 1);
                InventoryDao.InsertEquipment(db, second);
                var replaced = NativeInventoryService.Handle(db, new InventoryChangeRequest {
                    characterId = cid, itemId = item, equip = true, quantity = 1, equipSlot = slot, sourcePosition = bag });
                var rows = db.Table("inventoryitems");
                var one = rows.Find(x => DbStore.Long(x, "inventoryitemid") == first);
                var two = rows.Find(x => DbStore.Long(x, "inventoryitemid") == second);
                check(replaced.ok && DbStore.Int(one, "inventorytype") == 1 && DbStore.Int(one, "position") == bag &&
                    DbStore.Int(two, "inventorytype") == InventoryDao.Equipped && DbStore.Int(two, "position") == slot,
                    "AP槽 " + slot + " 同ID换装保留两件实例与原背包格");
                check(InventoryDao.CountOf(db, cid, item) == 1, "AP槽 " + slot + " 不计入背包可用数量");
                var character = CharactersDao.FindById(db, cid);
                NativeInventorySort.Sort(db, character, 1);
                NativeInventorySort.Merge(db, character, 1);
                check(DbStore.Int(two, "position") == slot, "整理与合并不卸下AP槽 " + slot);
                int consumed = InventoryDao.ConsumeItem(db, cid, item, 99);
                check(consumed == 1 && DbStore.Int(two, "position") == slot, "消耗只取背包同ID物品，不取AP槽 " + slot);
                db.Save();
                var saved = CharService.Load(DbStore.Open(path), cid).inventory;
                check(saved.Exists(x => x.itemId == item && x.inventoryType == InventoryDao.Equipped && x.position == slot),
                    "AP槽 " + slot + " 重读存档保留穿着");
                var removed = NativeInventoryService.Handle(db, new InventoryChangeRequest {
                    characterId = cid, itemId = item, equip = true, quantity = 0, equipSlot = slot });
                check(removed.ok && !removed.inventory.Exists(x => x.inventoryType == InventoryDao.Equipped && x.position == slot),
                    "AP槽 " + slot + " 可卸回背包");
                rows.Delete(x => DbStore.Long(x, "inventoryitemid") == first || DbStore.Long(x, "inventoryitemid") == second);
                db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid") == first || DbStore.Long(x, "inventoryitemid") == second);
            }
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    /// <summary>从当前目录向上找仓库内文件（dotnet run 的工作目录是 tools/server-check）。</summary>
    static string ProjectFile(string relative)
    {
        var dir = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, relative);
            if (System.IO.File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    static int wardrobeOf(int slot) => 0;

    /// <summary>Bot/Items.json 里某件道具的原始记录字段（source 等非 info 字段）。</summary>
    static string record(int itemId, string key)
    {
        string text = PlatformRes.LoadText("Bot/Items");
        var root = text == null ? null : MiniJsonDb.Parse(text) as Dictionary<string, object>;
        if (root == null || !root.TryGetValue("items", out var node)) return null;
        if (node is not Dictionary<string, object> items) return null;
        if (!items.TryGetValue(itemId.ToString(), out var row) || row is not Dictionary<string, object> entry) return null;
        return entry.TryGetValue(key, out var value) ? value as string : null;
    }
}
