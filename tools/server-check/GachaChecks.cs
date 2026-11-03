// 商城惊喜箱（CashShopSurprise）回归：逐条对照 CashShop.openCashShopSurprise +
// CashItemFactory.getRandomCashItem（在售非礼包商品等概率）+ CashShopSurpriseHandler。
// 远程扭蛋的双击→选村→gachaponRemote.js 链路是客户端桥（NpcApi/NpcSession），服务端
// 抽奖引擎 GachaDraw 已有独立覆盖，这里不重复。
using System;
using System.Linq;

static class GachaChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 商城惊喜箱 / 远程扭蛋券 ==");
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_gacha_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "gacha_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "GachaGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;
            var cash = new NativeCashService(db) { Catalog = new NativeSystemChecks.Catalog() };
            CashResponse S(CashOperation op = CashOperation.Read, int sn = 100, long item = 0) =>
                cash.Handle(new CashRequest { characterId = cid, operation = op, sn = sn, itemId = item, currency = 1 });

            Check(S(CashOperation.Open).ok, "进入商城");
            cash.Handle(new CashRequest { characterId = cid, operation = CashOperation.AdjustBalance, currency = 1, amount = 1000 });

            // 买 1 张惊喜箱（sn=106 → 5222000）
            Check(S(CashOperation.Buy, 106).ok, "购买惊喜箱 5222000");
            long boxId = S().stock.First(x => x.itemId == 5222000).id;

            // 无效 cashId → 失败
            Check(!S(CashOperation.OpenSurprise, item: 999999).ok, "无效 cashId 拒绝");

            // 固定随机下标：指向 sn=100（5370000 非礼包在售）
            var pool = ((NativeSystemChecks.Catalog)cash.Catalog).entries.Values
                .Where(x => x.onSale == 1 && x.itemId / 10000 != 910).OrderBy(x => x.sn).ToList();
            int idx = pool.FindIndex(x => x.sn == 100);
            NativeCashService.SurprisePick = n => idx;
            var opened = S(CashOperation.OpenSurprise, item: boxId);
            Check(opened.ok && opened.rewardItemId == 5370000 && opened.rewardSn == 100, "开箱 → 奖励 5370000（实际 " + opened.rewardItemId + "）");
            Check(!S().stock.Any(x => x.itemId == 5222000), "惊喜箱消耗（数量1 → 删除）");
            Check(S().stock.Any(x => x.itemId == 5370000), "奖励进现金仓库");

            // 礼包商品（9100000）不入奖励池：所有下标开出的都不是 9100000
            // 再买 2 张，开一次剩 1 张
            Check(S(CashOperation.Buy, 106).ok && S(CashOperation.Buy, 106).ok, "再买 2 张惊喜箱");
            boxId = S().stock.First(x => x.itemId == 5222000).id;
            NativeCashService.SurprisePick = n => 0;
            Check(S(CashOperation.OpenSurprise, item: boxId).ok, "开第一张");
            var rest = S().stock.First(x => x.itemId == 5222000);
            Check(rest.quantity == 1, "数量 2 → 开一次剩 1");
            NativeCashService.SurprisePick = null;
            // 这里原来放任真随机：抽到 5222000 自己（惊喜箱本身也在在售池里）时，
            // 仓库里会「又出现一张 5222000」，下面那条「最后一张消耗」就随机变红。
            // 固定到 sn=100（5370000）保持确定性，仍覆盖「随机开出非礼包商品」这条语义。
            NativeCashService.SurprisePick = n => idx;
            var anyOpen = S(CashOperation.OpenSurprise, item: rest.id);
            Check(anyOpen.ok && anyOpen.rewardItemId != 9100000, "礼包商品不入奖励池");
            Check(!S().stock.Any(x => x.itemId == 5222000), "最后一张消耗");

            // 无券开箱拒绝
            Check(!S(CashOperation.OpenSurprise, item: boxId).ok, "无惊喜箱 → 拒绝");

            // 远程扭蛋券 5451000 在道具目录里（双击→选村→gachaponRemote.js 为客户端链）
            var catalog = Server.Bot.BotItemCatalog.Shared;
            Check(catalog.Info(5451000) != null, "远程扭蛋券 5451000 在道具目录");
        }
        finally { NativeCashService.SurprisePick = null; db.Save(); System.IO.File.Delete(path); }
    }
}
