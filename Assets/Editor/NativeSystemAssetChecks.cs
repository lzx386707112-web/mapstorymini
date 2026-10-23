using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using MapleDash.Js;

public static class NativeSystemAssetChecks {
    static int passed, failed;
    static void Check(bool value, string label) { if (value) passed++; else { failed++; Debug.LogError("[NativeSystemAssetChecks] " + label); } }
    public static void Run() {
        NativeKeyboardChecks.Run();
        var catalog = new NativeCashCatalog(); int count = 0;
        foreach (var item in catalog.All) count++;
        Check(count == 9074, "原Commodity目录9074个独立SN（源XML两个9063同名节点均保留），实际 " + count);
        var board = catalog.Find(50200067);
        Check(board != null && board.itemId == 5370000 && board.price == 6000 && board.period == 7 && board.onSale == 1, "原黑板商品SN/价格/7天有效期");
        Check(catalog.Exists(board.itemId) && NativeItemSource.Icon(board.itemId).width > 0, "真实现金道具节点与图标可解码");
        var package = catalog.Package(9102232);
        Check(package != null && package.Count > 0, "原CashPackage可解析");
        foreach (int sn in package) Check(catalog.Find(sn) != null, "礼包SN可回查 " + sn);
        Check(catalog.Exists(1003050) && NativeItemSource.Icon(1003050).width > 0, "原现金装备可解码");
        Check(!catalog.Exists(1999999), "缺少的商品资源返回不存在");
        var medal = QuestCatalog.Get(29000);
        Check(medal.medalItem == 1142001 && medal.medalCategory == 2, "勋章来源为原QuestInfo");
        Check(EquipSlot.SlotOf(1142001) == -49 && EquipCatalog.Info(1142001) != null, "勋章原装备数据和-49槽位");
        Check(NativeItemSource.Icon(1142001).width > 0, "原勋章图标可解码");
        string path = Path.Combine(Path.GetTempPath(), "mapledash_asset_" + Guid.NewGuid().ToString("N") + ".db");
        try {
            var db = DbStore.Create(path);
            int account = (int)db.Table("accounts").Insert(new DbRow { ["name"] = "AssetTest", ["nxCredit"] = 10000L });
            int cid = (int)db.Table("characters").Insert(new DbRow { ["name"] = "AssetTest", ["accountid"] = (long)account, ["level"] = 30L });
            var cash = new NativeCashService(db) { Catalog = catalog };
            cash.Handle(new CashRequest { characterId = cid, operation = CashOperation.Open });
            var purchase = cash.Handle(new CashRequest { characterId = cid, operation = CashOperation.Buy, sn = board.sn });
            Check(purchase.ok && purchase.credit == 4000 && purchase.stock.Count == 1, "真实WZ商品购买扣款发货");
            var withdraw = cash.Handle(new CashRequest { characterId = cid, operation = CashOperation.Withdraw, itemId = purchase.stock[0].id });
            Check(withdraw.ok && withdraw.character.inventory.Exists(x => x.itemId == 5370000 && x.inventoryType == 5), "真实商品取出到现金栏");
            var community = new NativeCommunityService(db);
            var posted = community.Handle(new CommunityRequest { characterId = cid, operation = CommunityOperation.SetBoard, text = "真实WZ黑板" });
            Check(posted.ok && posted.board == "真实WZ黑板" && InventoryDao.CountOf(db, cid, 5370000) == 1, "真实商城道具支持黑板使用且不消耗");
        } finally { if (File.Exists(path)) File.Delete(path); }
        Debug.Log("[NativeSystemAssetChecks] 通过 " + passed + "，失败 " + failed);
        if (failed > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
