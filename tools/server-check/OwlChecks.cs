// 猫头鹰搜索回归：逐条对照源端
//   net/server/channel/handlers/UseCashItemHandler.java:428-444（itemType 523：搜到才扣券）
//   net/server/channel/handlers/UseOwlOfMinervaHandler.java（<5 种给固定 11 件；≥5 种按次数取前 10）
//   net/server/world/World.java:1337-1365（owlSearched 热度表；use_enforce_item_suggestion 直接空表）
//   net/server/world/World.java:1917-1940（getAvailableItemBundles：价格升序、截 200）
//   net/server/channel/handlers/OwlWarpHandler.java（跳店校验与消息码 1/3/18）
using System;
using System.Collections.Generic;

static class OwlChecks
{
    // MarketShopService 构造只要 db/trades/map；猫头鹰扫描只用 shops 表。
    static Server.Bot.MarketShopService Market(DbStore db) => new Server.Bot.MarketShopService(db, null, null);

    static Server.Bot.MarketShop Shop(int ownerId, int mapId, string name, params (int itemId, int bundles, int quantity, int price)[] stocks)
    {
        var shop = new Server.Bot.MarketShop { ownerId = ownerId, mapId = mapId, ownerName = name,
            description = name + " 的店", open = true };
        foreach (var s in stocks)
            shop.items.Add(new Server.Bot.MarketShopStock { item = new InventoryEntry { itemId = s.itemId, quantity = (short)s.quantity },
                bundles = s.bundles, price = s.price });
        return shop;
    }

    static OwlResponse Handle(DbStore db, int cid, OwlOperation op, int itemId = 0, int ownerId = 0, int mapId = 0)
        => NativeOwlService.Handle(db, new OwlRequest { characterId = cid, operation = op, itemId = itemId, ownerId = ownerId, mapId = mapId });

    static void SetConfig(DbStore db, string code, string value)
    {
        var table = db.Table("game_config");
        var row = table.Find(x => (string)x["config_code"] == code);
        if (row == null) table.Insert(new DbRow { ["config_type"] = "server", ["config_code"] = code, ["config_value"] = value });
        else row["config_value"] = value;
        db.Save();
    }

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 猫头鹰搜索 Owl（UseCashItemHandler 523 / OwlWarpHandler）==");
        NativeOwlService.Reset();

        SuggestionChecks(Check);
        ScanChecks(Check);
        WarpChecks(Check);
    }

    // ————————————————— 推荐位 —————————————————
    static void SuggestionChecks(Action<bool, string> Check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_owl_sugg_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            NativeOwlService.Reset();
            var login = LoginService.Login(db, "owl_sugg_acc", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "OwlSugg", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            var res = Handle(db, cid, OwlOperation.Open);
            Check(res.suggestions != null && res.suggestions.Count == NativeOwlService.DefaultSuggestions.Length,
                "搜索种类 < 5 → 固定推荐 " + NativeOwlService.DefaultSuggestions.Length + " 件");
            Check(res.suggestions != null && res.suggestions.Count > 0 && res.suggestions[0] == NativeOwlService.DefaultSuggestions[0],
                "固定推荐首件 = 工作手套 1082002");

            // 记 5 种不同热度（2/5/1/3/4 次），≥5 种 → 按次数降序前 10
            var service = Market(db);
            NativeOwlService.MarketProvider = () => service;
            foreach (var (id, times) in new[] { (4000000, 2), (4000001, 5), (4000002, 1), (4000003, 3), (4000004, 4) })
                for (int i = 0; i < times; i++) Handle(db, cid, OwlOperation.Search, id);
            res = Handle(db, cid, OwlOperation.Open);
            Check(res.suggestions.Count == 5, "≥5 种 → 按次数降序取前 10（这里只有 5 种）");
            Check(res.suggestions[0] == 4000001 && res.suggestions[1] == 4000004 && res.suggestions[4] == 4000002,
                "降序 5/4/3/2/1：首位 4000001(5次)，同次数补 itemId 升序");
            Check(NativeOwlService.Searched[4000001] == 5, "搜索热度按次累加");

            SetConfig(db, "use_enforce_item_suggestion", "true");
            int before = NativeOwlService.Searched.Count;
            Handle(db, cid, OwlOperation.Search, 4000005);
            Check(NativeOwlService.Searched.Count == before, "use_enforce_item_suggestion=true 不记热度");
            res = Handle(db, cid, OwlOperation.Open);
            Check(res.suggestions.Count == 0, "use_enforce_item_suggestion=true 推荐位恒空");
            SetConfig(db, "use_enforce_item_suggestion", "false");
        }
        finally { NativeOwlService.Reset(); db.Save(); }
    }

    // ————————————————— 扫描与扣券 —————————————————
    static void ScanChecks(Action<bool, string> Check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_owl_scan_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            NativeOwlService.Reset();
            var login = LoginService.Login(db, "owl_scan_acc", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "OwlScan", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            var service = Market(db);
            NativeOwlService.MarketProvider = () => service;
            // 两家店都有同一物品：价格 300 / 100 → 升序；另有卖光（exists=false）与 bundles=0 的格子要被跳过
            service.UpsertPlayerStore(Shop(800001, 910000001, "甲", (2070005, 3, 1, 300), (2070006, 2, 1, 50)));
            service.UpsertPlayerStore(Shop(800002, 910000002, "乙", (2070005, 1, 1, 100), (4000000, 5, 10, 20)));
            var soldOut = Shop(800003, 910000003, "丙", (2070005, 1, 1, 10));
            soldOut.items[0].exists = false;
            service.UpsertPlayerStore(soldOut);

            var res = Handle(db, cid, OwlOperation.Search, 2070005);
            Check(res.hits.Count == 2, "卖光 / 关店格子不进结果（3 家店 4 格 → 2 条）");
            Check(res.hits[0].price == 100 && res.hits[1].price == 300, "结果按单价升序（100 在 300 前）");
            Check(res.hits[0].ownerName == "乙" && res.hits[1].ownerName == "甲", "结果带店主名");
            Check(res.hits[0].bundles == 1 && res.hits[1].bundles == 3, "组合列 = 份数 bundles");
            Check(res.charged, "搜到结果 → charged=true（客户端扣 1 个猫头鹰）");

            res = Handle(db, cid, OwlOperation.Search, 4100000);
            Check(res.hits.Count == 0 && !res.charged, "搜不到 → hits 空 + charged=false（源端：空手而归不扣券）");

            // 源端 subList 截 200（注意源端这行其实没赋值——结果里 bundle 多于 200 时不截断也算复刻，
            // 但 hmsAvailable.sort 后 subList 是明确意图，这里按意图验证 ≤200）
            var many = Market(db);
            NativeOwlService.MarketProvider = () => many;
            for (int i = 0; i < 220; i++) many.UpsertPlayerStore(Shop(810000 + i, 910000001, "店" + i, (4000009, 1, 1, i + 1)));
            res = Handle(db, cid, OwlOperation.Search, 4000009);
            Check(res.hits.Count == NativeOwlService.MaxResults, "结果截到 " + NativeOwlService.MaxResults + " 条");
            NativeOwlService.MarketProvider = () => service;
        }
        finally { NativeOwlService.Reset(); db.Save(); }
    }

    // ————————————————— 跳店 —————————————————
    static void WarpChecks(Action<bool, string> Check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_owl_warp_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            NativeOwlService.Reset();
            var login = LoginService.Login(db, "owl_warp_acc", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "OwlWarp", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            var service = Market(db);
            NativeOwlService.MarketProvider = () => service;
            service.UpsertPlayerStore(Shop(800101, 910000001, "目标店", (2070005, 3, 1, 100)));
            var closed = Shop(800102, 910000001, "关门的店", (2070005, 3, 1, 100));
            closed.open = false;
            service.UpsertPlayerStore(closed);

            var res = Handle(db, cid, OwlOperation.Warp, itemId: 2070005, ownerId: cid, mapId: 910000001);
            Check(!res.ok && res.code == NativeOwlService.ErrorNoShop, "访问自己的店 → 拒绝（源端 serverNotice）");

            res = Handle(db, cid, OwlOperation.Warp, itemId: 2070005, ownerId: 999999, mapId: 910000001);
            Check(!res.ok && res.code == NativeOwlService.ErrorNoShop, "店铺不存在 → 消息码 1");

            res = Handle(db, cid, OwlOperation.Warp, itemId: 2070005, ownerId: 800102, mapId: 910000001);
            Check(!res.ok && res.code == NativeOwlService.ErrorMaintenance, "店已关门 → 消息码 18");

            res = Handle(db, cid, OwlOperation.Warp, itemId: 2070005, ownerId: 800101, mapId: 910000002);
            Check(!res.ok && res.code == NativeOwlService.ErrorMismatch, "图对不上（店已搬家）→ 消息码 3");

            res = Handle(db, cid, OwlOperation.Warp, itemId: 2070006, ownerId: 800101, mapId: 910000001);
            Check(!res.ok && res.code == NativeOwlService.ErrorMismatch, "店里已没有该道具 → 消息码 3");

            res = Handle(db, cid, OwlOperation.Warp, itemId: 2070005, ownerId: 800101, mapId: 910000001);
            Check(res.ok && res.map == 910000001, "校验通过 → 返回目标地图");
        }
        finally { NativeOwlService.Reset(); db.Save(); }
    }
}
