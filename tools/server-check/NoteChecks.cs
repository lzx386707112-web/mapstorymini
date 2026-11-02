// 便签系统回归：逐条对照源端 NoteService.java + NoteActionHandler.java + CashShop.loadGiftsFromDb
//   sendNormal（商城送礼提醒 / Fredrick 提醒）与 sendWithFame（回复送礼，收件人名望 +1）；
//   action 0 回复须 availableNotes>0（收礼领取时每份 +1，内存态）；
//   action 1 丢弃 = 按每条便签的 fame 累计加名望（源端读库结算，真删）。
using System;
using System.Linq;

static class NoteChecks
{
    static MemoResponse Call(DbStore db, int cid, int action, string target = "", string message = "", int[] ids = null)
        => NativeNoteService.Handle(db, new MemoRequest { characterId = cid, action = action, target = target, message = message, discardIds = ids });

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 便签（Note/Memo）==");
        NativeNoteService.ResetForTest();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_note_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "note_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "NoteGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            // sendNormal + show（未读列表、收件人隔离）
            NativeNoteService.SendNormal(db, "你好呀", "Alice", "NoteGuy");
            var unread = NativeNoteService.Unread(db, "NoteGuy");
            Check(unread.Count == 1 && unread[0].message == "你好呀" && unread[0].from == "Alice" && unread[0].fame == 0,
                "sendNormal → Unread 1 条（fame=0）");
            Check(NativeNoteService.Unread(db, "Nobody").Count == 0, "收件人隔离");

            // sendWithFame：fame 标记记在便签上
            NativeNoteService.SendWithFame(db, "赞一个", "Bob", "NoteGuy");
            unread = NativeNoteService.Unread(db, "NoteGuy");
            Check(unread.Count == 2 && unread[1].fame == 1, "sendWithFame → fame=1");

            // action 3 写便签：目标不存在 / 超长 / 成功
            Check(!Call(db, cid, 3, "Ghost", "hi").ok, "收件角色不存在拒绝");
            Check(!Call(db, cid, 3, "NoteGuy", new string('a', 74)).ok, "74 字符超长拒绝");
            Check(Call(db, cid, 3, "NoteGuy", "自言自语").ok && NativeNoteService.Unread(db, "NoteGuy").Count == 3,
                "写便签成功（sent）");

            // action 0 回复送礼便签：availableNotes=0 拒绝；=1 时成功并消耗
            NativeNoteService.ResetForTest();
            Check(!Call(db, cid, 0, "NoteGuy", "回复").ok, "无可用次数 → 拒绝");
            NativeNoteService.AvailableNotes = 1;
            Check(!Call(db, cid, 0, "Ghost", "回复").ok && NativeNoteService.AvailableNotes == 1, "回复目标不存在 → 不消耗次数");
            var reply = Call(db, cid, 0, "NoteGuy", "谢谢礼物");
            Check(reply.ok && reply.sent && NativeNoteService.AvailableNotes == 0, "回复便签 → 成功 + 次数 -1");
            Check(NativeNoteService.Unread(db, "NoteGuy").Last().fame == 1, "回复便签 → 收件人 fame 标记 1");

            // action 1 丢弃：按便签 fame 累计加名望（源端读库），真删
            unread = NativeNoteService.Unread(db, "NoteGuy");
            var fameNote = unread.First(n => n.fame == 1);
            var plainNote = unread.First(n => n.fame == 0);
            int before = DbStore.Int(db.Table("characters").Find(x => DbStore.Int(x, "id") == cid), "fame");
            var discard = Call(db, cid, 1, ids: new[] { plainNote.id, fameNote.id });
            var self = db.Table("characters").Find(x => DbStore.Int(x, "id") == cid);
            Check(discard.ok && discard.fameGain == 1, "丢弃 2 条（fame 0+1）→ fameGain=1");
            Check(DbStore.Int(self, "fame") == before + 1, "角色名望 +1（" + before + "→" + DbStore.Int(self, "fame") + "）");
            Check(NativeNoteService.Unread(db, "NoteGuy").Count == 2, "丢弃真删（剩 2 条）");

            // 重复丢弃同 id：不存在的跳过、名望不再累计
            var again = Call(db, cid, 1, ids: new[] { plainNote.id, fameNote.id });
            Check(again.ok && again.fameGain == 0, "重复丢弃 → fameGain=0");
            Check(DbStore.Int(db.Table("characters").Find(x => DbStore.Int(x, "id") == cid), "fame") == before + 1, "名望不重复累计");

            // 商城链：送礼提醒便签 + LoadGifts（进商城自动领礼物 + availableNotes 累计）
            var login2 = LoginService.Login(db, "note_user2", "pw123456");
            var made2 = CharFactory.CreateCharacter(db, login2.accountId, 0, "NotePal", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int palId = made2.characterId;
            var cash = new NativeCashService(db) { Catalog = new NativeSystemChecks.Catalog() };
            CashResponse S(int cid2, CashOperation op = CashOperation.Read, int sn = 100, string target = "", string message = "", int birthday = 0) =>
                cash.Handle(new CashRequest { characterId = cid2, operation = op, sn = sn, currency = 1, target = target, message = message, birthday = birthday });
            Check(S(cid, CashOperation.Open).ok, "NoteGuy 进商城");
            cash.Handle(new CashRequest { characterId = cid, operation = CashOperation.AdjustBalance, currency = 4, amount = 5000 });
            int bday = int.Parse(DbStore.Str(db.Table("accounts").Find(x => DbStore.Int(x, "id") == login.accountId), "birthday").Replace("-", ""));
            Check(S(cid, CashOperation.Gift, 106, "NotePal", "送你礼物", bday).ok, "送礼 5222000 给 NotePal");
            var palNotes = NativeNoteService.Unread(db, "NotePal");
            Check(palNotes.Any(n => n.from == "NoteGuy" && n.message.Contains("礼物")), "送礼 → 收件人收到商城提醒便签");

            // NotePal 进商城 → cash_gifts 自动装进现金仓库 + 可用回复次数 +1
            int preNotes = NativeNoteService.AvailableNotes;
            Check(S(palId, CashOperation.Open).ok, "NotePal 进商城（LoadGifts）");
            var palStock = S(palId).stock;
            Check(palStock.Any(x => x.itemId == 5222000 && x.giftFrom == "NoteGuy"), "礼物进现金仓库（giftFrom=NoteGuy）");
            Check(NativeNoteService.AvailableNotes == preNotes + 1, "每份礼物 +1 回复便签次数");
            Check(S(palId, CashOperation.Open).ok && NativeNoteService.AvailableNotes == preNotes + 1, "重复进商城不重复累计（礼物已领）");

            // Fredrick 提醒接线：DueyFredrickTask 每小时惰性等价（Handle 入口补跑）
            NativeNoteService.ResetForTest();
            db.Table("fredstorage").Insert(new DbRow { ["cid"] = (long)cid, ["daynotes"] = 0L,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 3L * 24 * 60 * 60 * 1000 });   // 关店 3 天 ≥ 首档 2 天
            Check(Call(db, cid, 2).ok, "拉取便签（触发惰性调度）");
            palNotes = NativeNoteService.Unread(db, "NoteGuy");
            Check(palNotes.Any(n => n.from == "FREDRICK" && n.message.Contains("2 天")), "寄存 3 天 → FREDRICK 首档（2 天）提醒便签");
            Check(DbStore.Int(db.Table("fredstorage").Find(x => DbStore.Int(x, "cid") == cid), "daynotes") == 1, "提醒档位推进 daynotes 0→1");
            Check(Call(db, cid, 2).ok && NativeNoteService.Unread(db, "NoteGuy").Count(n => n.from == "FREDRICK") == 1,
                "1 小时闸门内不重复提醒");
            NativeNoteService.ResetForTest();
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }
}
