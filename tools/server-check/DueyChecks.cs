// 快递（Duey）回归：逐条对照源端
//   client/processor/npc/DueyProcessor.java（dueySendItem / dueyClaimPackage / dueyRemovePackage /
//                                           runDueyExpireSchedule / showDueyNotification）
//   server/Trade.java getFee 六档费率
//   单机语义：收件人只可能是本地角色，到件通知退化成 DB 的 checked 标记。
using System;
using System.Collections.Generic;
using System.IO;

static class DueyChecks
{
    // 断言里的费率表（Trade.getFee）逐档取值
    static void FeeTable(Action<bool, string> Check)
    {
        bool ok = NativeDueyService.GetFee(0) == 0
               && NativeDueyService.GetFee(99999) == 0
               && NativeDueyService.GetFee(100000) == 800            // 0.8%
               && NativeDueyService.GetFee(999999) == 7999
               && NativeDueyService.GetFee(1000000) == 18000          // 1.8%
               && NativeDueyService.GetFee(4999999) == 89999
               && NativeDueyService.GetFee(5000000) == 150000         // 3%
               && NativeDueyService.GetFee(10000000) == 400000        // 4%
               && NativeDueyService.GetFee(25000000) == 1250000       // 5%
               && NativeDueyService.GetFee(100000000) == 6000000;     // 6%
        Check(ok, "GetFee 六档边界与费率");
    }

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 快递 Duey（PARCEL / dueypackages）==");
        FeeTable(Check);

        string path = Path.Combine(Path.GetTempPath(), "mapledash_duey_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var loginA = LoginService.Login(db, "dueysender", "pw123456");
            var madeA = CharFactory.CreateCharacter(db, loginA.accountId, 0, "DueySender", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            Check(madeA.code == 0, "寄件人建号成功");
            int cidA = madeA.characterId;

            var loginB = LoginService.Login(db, "dueyrecv", "pw123456");
            var madeB = CharFactory.CreateCharacter(db, loginB.accountId, 0, "DueyRecv", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            Check(madeB.code == 0, "收件人建号成功");
            int cidB = madeB.characterId;

            // 同账号的第二个角色，用来验 SAMEACC_ERROR
            var madeA2 = CharFactory.CreateCharacter(db, loginA.accountId, 0, "DueySelf", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            Check(madeA2.code == 0, "同账号第二角色建号成功");

            void SetMeso(int cid, long meso)
            {
                var rec = CharactersDao.FindById(db, cid);
                rec.meso = meso;
                CharactersDao.Save(db, rec);
            }

            long MesoOf(int cid) => CharactersDao.FindById(db, cid).meso;

            void Clear(int cid)
            {
                var ids = new HashSet<long>();
                foreach (var r in db.Table("inventoryitems").Select(x => DbStore.Int(x, "characterid") == cid))
                    ids.Add(DbStore.Long(r, "inventoryitemid"));
                db.Table("inventoryitems").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
                db.Table("inventoryequipment").Delete(x => ids.Contains(DbStore.Long(x, "inventoryitemid", -1)));
                db.Save();
            }

            void SetConfig(string code, string value)
            {
                var row = db.Table("game_config").Find(x => (string)x["config_code"] == code);
                if (row == null)
                    db.Table("game_config").Insert(new DbRow {
                        ["config_type"] = "server", ["config_sub_type"] = "Game Mechanics",
                        ["config_clazz"] = "java.lang.Boolean", ["config_code"] = code, ["config_value"] = value });
                else row["config_value"] = value;
                db.Save();
            }

            void DropPackages()
            {
                db.Table("dueyitems").Delete(x => true);
                db.Table("dueypackages").Delete(x => true);
                db.Save();
            }

            DueyResponse Send(int cid, int mesos, string to, int invType = 0, int pos = 0, int qty = 0, bool quick = false, string msg = "")
                => NativeDueyService.Handle(db, new DueyRequest {
                    characterId = cid, operation = DueyOperation.Send, mesos = mesos, recipient = to,
                    inventoryType = invType, itemPosition = pos, itemQuantity = qty, quick = quick, message = msg });

            List<DueyPackageData> Open(int cid)
                => NativeDueyService.Handle(db, new DueyRequest { characterId = cid, operation = DueyOperation.Open }).packages;

            DueyResponse Claim(int cid, int packageId)
                => NativeDueyService.Handle(db, new DueyRequest { characterId = cid, operation = DueyOperation.Claim, packageId = packageId });

            SetMeso(cidA, 1000000000L);
            SetMeso(cidB, 0);

            // 1. 纯金币寄件：fee = 800 + 5000（非快速），包裹落到收件人
            var sent = Send(cidA, 100000, "DueyRecv");
            Check(sent.ok && sent.code == NativeDueyService.SuccessfullySent, "纯金币寄件成功");
            Check(MesoOf(cidA) == 1000000000L - 105800L, "寄件扣除 金币+手续费+5000 邮费");
            var boxB = Open(cidB);
            Check(boxB.Count == 1 && boxB[0].mesos == 100000 && boxB[0].sender == "DueySender",
                "收件人列表出现包裹（金额与寄件人正确）");

            // 2. 取件：金币到账、包裹删除
            long beforeB = MesoOf(cidB);
            var claimed = Claim(cidB, boxB[0].packageId);
            Check(claimed.ok, "取件回包 ok");
            Check(MesoOf(cidB) == beforeB + 100000, "取件金币到账");
            Check(Open(cidB).Count == 0, "取件后包裹从列表移除");

            // 3. 取件鉴权：不是本人的包裹
            DropPackages();
            Send(cidA, 1000, "DueyRecv");
            int pkgB = Open(cidB)[0].packageId;
            var stolen = Claim(cidA, pkgB);
            Check(!stolen.ok && stolen.code == NativeDueyService.RecvUnknownError, "非收件人取件被拒绝");
            Check(Open(cidB).Count == 1, "越权取件不删包裹");

            // 4. 投递中（sendtime 在未来）不可取
            var row = db.Table("dueypackages").Find(x => DbStore.Int(x, "packageid") == pkgB);
            row["sendtime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000;
            db.Save();
            var delivering = Claim(cidB, pkgB);
            Check(!delivering.ok && delivering.error.Contains("投递中"), "future sendtime 视为投递中");

            // 5. Remove 主动删除包裹；删除不存在的包裹也返回 ok
            var removed = NativeDueyService.Handle(db, new DueyRequest { characterId = cidB, operation = DueyOperation.Remove, packageId = pkgB });
            Check(removed.ok && Open(cidB).Count == 0, "Remove 删除包裹");
            Check(NativeDueyService.Handle(db, new DueyRequest { characterId = cidB, operation = DueyOperation.Remove, packageId = 999999 }).ok,
                "Remove 不存在的包裹按幂等返回 ok");

            // 6. 寄物品：数量从背包扣、包裹带同一件物品，取件后进收件人背包
            DropPackages(); Clear(cidA); Clear(cidB);
            SetMeso(cidA, 1000000L); SetMeso(cidB, 0);
            InventoryDao.InsertItem(db, cidA, 4000000, InventoryDao.Etc, 1, 10);
            var itemSent = Send(cidA, 0, "DueyRecv", InventoryDao.Etc, 1, 10);
            Check(itemSent.ok, "寄物品成功");
            Check(MesoOf(cidA) == 1000000L - 5000L, "寄物品只收 5000 邮费");
            var invA = CharService.Load(db, cidA).inventory.FindAll(i => i.itemId == 4000000);
            Check(invA.Count == 0, "寄出的物品从背包扣掉");
            var itemBox = Open(cidB);
            Check(itemBox.Count == 1 && itemBox[0].item != null && itemBox[0].item.itemId == 4000000
                  && itemBox[0].item.quantity == 10, "包裹里带上寄出的物品与数量");
            var itemClaim = Claim(cidB, itemBox[0].packageId);
            Check(itemClaim.ok, "取物品成功");
            var invB = CharService.Load(db, cidB).inventory.FindAll(i => i.itemId == 4000000);
            Check(invB.Count == 1 && invB[0].quantity == 10, "物品进入收件人背包");

            // 7. 部分寄出：只扣请求数量，余量留在原格
            Clear(cidA); DropPackages();
            SetMeso(cidA, 1000000L);
            InventoryDao.InsertItem(db, cidA, 4000000, InventoryDao.Etc, 1, 10);
            Send(cidA, 0, "DueyRecv", InventoryDao.Etc, 1, 4);
            var rest = CharService.Load(db, cidA).inventory.Find(i => i.itemId == 4000000 && i.position == 1);
            Check(rest != null && rest.quantity == 6, "部分寄出后原格留余量");
            Check(Open(cidB)[0].item.quantity == 4, "包裹数量 = 请求数量");

            // 8. 数量不足 → INVALID（-2）
            var tooMany = Send(cidA, 0, "DueyRecv", InventoryDao.Etc, 1, 99);
            Check(!tooMany.ok && tooMany.code == NativeDueyService.IncorrectRequest, "数量超过持有量被拒绝");

            // 9. 不可交易物品（flag 0x08）→ 拒绝，且物品仍留在背包
            DropPackages(); Clear(cidA);
            long untrade = InventoryDao.InsertItem(db, cidA, 4000000, InventoryDao.Etc, 1, 5);
            db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid", -1) == untrade)["flag"] = 8L;
            db.Save();
            var blocked = Send(cidA, 0, "DueyRecv", InventoryDao.Etc, 1, 5);
            Check(!blocked.ok && blocked.error.Contains("不能寄送"), "UNTRADEABLE 物品禁止寄送");
            var kept = CharService.Load(db, cidA).inventory.Find(i => i.itemId == 4000000);
            Check(kept != null && kept.quantity == 5, "被拒绝时物品不离开背包");

            // 10. 现金道具（use_enforce_unmerchable_cash）禁止寄送
            Clear(cidA); DropPackages();
            InventoryDao.InsertItem(db, cidA, 5330000, InventoryDao.Cash, 2, 1);
            var cashBlocked = Send(cidA, 0, "DueyRecv", InventoryDao.Cash, 2, 1);
            Check(!cashBlocked.ok, "现金道具默认禁止寄送");

            // 11. 快速投递：无票拒绝；有票成功、扣票、type=1、不加 5000
            DropPackages(); Clear(cidA);
            SetMeso(cidA, 1000000L);
            var noTicket = Send(cidA, 100000, "DueyRecv", quick: true);
            Check(!noTicket.ok && noTicket.error.Contains("快速投递券"), "无快速投递券不能走快速投递");
            InventoryDao.InsertItem(db, cidA, NativeDueyService.QuickDeliveryTicket, InventoryDao.Cash, 1, 1);
            var quick = Send(cidA, 100000, "DueyRecv", quick: true);
            Check(quick.ok && quick.fee == 800, "快速投递费率为纯 getFee（不加 5000）");
            Check(MesoOf(cidA) == 1000000L - 100800L, "快速投递扣 金币+800");
            Check(InventoryDao.CountOf(db, cidA, NativeDueyService.QuickDeliveryTicket) == 0, "快速投递券被消耗");
            var quickBox = Open(cidB);
            Check(quickBox.Count == 1 && quickBox[0].quick, "包裹标记为快速投递（type=1）");

            // 12. 收件人不存在 / 同账号 / 留言过长 / 负数金额
            DropPackages();
            Check(Send(cidA, 0 + 1, "NoSuchHero").code == NativeDueyService.NameDoesNotExist, "收件人不存在");
            Check(Send(cidA, 1, "DueySelf").code == NativeDueyService.SameAccError, "同账号寄件被拒绝");
            Check(Send(cidA, 1, "DueyRecv", msg: new string('m', 101)).code == NativeDueyService.IncorrectRequest, "留言超 100 字被拒绝");
            Check(Send(cidA, -1, "DueyRecv").code == NativeDueyService.IncorrectRequest, "负数金额被拒绝");
            Check(Send(cidA, 0, "DueyRecv", qty: 0).code == NativeDueyService.IncorrectRequest, "空包裹（无物品无金币）被拒绝");

            // 13. 金币不足
            SetMeso(cidA, 1000);
            var poor = Send(cidA, 100000, "DueyRecv");
            Check(!poor.ok && poor.code == NativeDueyService.NotEnoughMesos, "金币不足返回 NOT_ENOUGH_MESOS");

            // 14. 收件人背包满 → NO_FREE_SLOTS
            DropPackages(); Clear(cidA); Clear(cidB);
            SetMeso(cidA, 1000000L);
            InventoryDao.InsertItem(db, cidA, 4000000, InventoryDao.Etc, 1, 1);
            Send(cidA, 0, "DueyRecv", InventoryDao.Etc, 1, 1);
            int fullPkg = Open(cidB)[0].packageId;
            var recB = CharactersDao.FindById(db, cidB);
            var actorB = new Server.Bot.BotActor { id = cidB, job = recB.job, character = CharService.Load(db, cidB) };
            int slotMax = new Server.Bot.Trade.TradeItemRules(db, Server.Bot.BotItemCatalog.Shared).SlotMax(4000000, actorB);
            Check(slotMax > 0, "测试道具 slotMax > 0（前置）");
            for (int slot = 1; slot <= recB.etcslots; slot++)
                InventoryDao.InsertItem(db, cidB, 4000000, InventoryDao.Etc, slot, slotMax);
            db.Save();
            var full = Claim(cidB, fullPkg);
            Check(!full.ok && full.code == NativeDueyService.RecvNoFreeSlots, "收件人背包满返回 NO_FREE_SLOTS");
            Check(Open(cidB).Count == 1, "取件失败不删包裹");
            Clear(cidB);

            // 15. 30 天过期清包连物品
            DropPackages(); Clear(cidA); Clear(cidB);
            long expRow = InventoryDao.InsertItem(db, -1, 4000000, InventoryDao.Etc, 1, 3);
            int expPkg = (int)db.Table("dueypackages").Insert(new DbRow {
                ["receiverid"] = (long)cidB, ["sendername"] = "DueySender", ["mesos"] = 0L,
                ["sendtime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 31L * 24 * 60 * 60 * 1000,
                ["message"] = "", ["checked"] = 1L, ["type"] = 0L });
            db.Table("dueyitems").Insert(new DbRow { ["packageid"] = (long)expPkg, ["inventoryitemid"] = expRow });
            db.Save();
            Check(Open(cidB).Count == 1, "过期前包裹可见（前置）");
            int expired = NativeDueyService.Expire(db, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Check(expired >= 1, "Expire 清掉 30 天前的包裹");
            Check(CharService.Load(db, cidB).inventory.Find(i => i.itemId == 4000000) == null, "过期包裹里的物品一并删除");

            // 16. 到件通知（checked 置 1 → 发一次 → 置 0）
            DropPackages();
            SetMeso(cidA, 1000000L);
            Send(cidA, 100, "DueyRecv");
            var notify = NativeDueyService.TakePendingNotification(db, cidB);
            Check(notify != null && notify.sender == "DueySender", "到件通知取到寄件人");
            Check(NativeDueyService.TakePendingNotification(db, cidB) == null, "同一条通知只发一次（checked 清零）");

            // 17. use_duey=false 整体关闭
            SetConfig("use_duey", "false");
            var off = NativeDueyService.Handle(db, new DueyRequest { characterId = cidA, operation = DueyOperation.Open });
            Check(!off.ok, "use_duey=false 时快递接口关闭");
            SetConfig("use_duey", "true");
        }
        finally
        {
            db.Save();
            File.Delete(path);
        }
    }
}
