// 猜拳小游戏回归：逐条对照源端 RockPaperScissor.java + RPSActionHandler.java
//   开局/再来一局 1000 金币；10 轮；平局可重出；赢=round+1、输=-1；
//   赢着离场才发证书 4031332+round（ETC）；金币不足 mode 6。
using System;

static class RpsChecks
{
    static RpsResponse Call(DbStore db, int cid, RpsOperation op, int answer = 0)
        => NativeRpsService.Handle(db, new RpsRequest { characterId = cid, operation = op, answer = answer });

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 猜拳小游戏（RPS 9000019）==");
        NativeRpsService.ResetForTest();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_rps_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "rps_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "RpsGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var rec = CharactersDao.FindById(db, made.characterId);
            rec.meso = 5000;
            CharactersDao.Save(db, rec);
            int cid = made.characterId;

            // 金币不足拒绝
            var poor = rec;
            poor.meso = 500; CharactersDao.Save(db, poor);
            var denied = Call(db, cid, RpsOperation.Start);
            Check(!denied.ok && denied.mode == 6, "金币不足 → mode 6 拒绝");
            poor.meso = 5000; CharactersDao.Save(db, poor);

            // 开局：扣 1000，mode 9
            var start = Call(db, cid, RpsOperation.Start);
            rec = CharactersDao.FindById(db, cid);
            Check(start.ok && start.mode == 9, "开局 → mode 9");
            Check(rec.meso == 4000, "开局扣 1000 金币（5000→" + rec.meso + "）");

            // 源端胜负表 (0,2)(1,0)(2,1) → 0=石头 / 1=布 / 2=剪刀；服务器恒出石头（0）
            NativeRpsService.SetRandom(() => 0);
            var draw = Call(db, cid, RpsOperation.Answer, 0);
            Check(draw.roundLabel == 0 && draw.selection == 0, "石头对石头 → 平局同轮可重出");
            var win = Call(db, cid, RpsOperation.Answer, 1);
            Check(win.roundLabel == 1 && win.selection == 0, "布对石头 → 赢（roundLabel=1）");

            // 赢着离场（不点继续）→ 发证书 4031332+1
            var quit = Call(db, cid, RpsOperation.Leave);
            // 源端 reward = BASE + round（round=已 Continue 完成的胜场）；赢后不点继续 → round=0
            Check(quit.closed && quit.rewardItem == 4031332, "赢 1 轮直接离场 → 证书 4031332 + 关窗");
            var rows = InventoryDao.ItemsByCharacter(db, cid);
            rows.RemoveAll(i => Convert.ToInt32(i["itemid"]) != 4031332);
            Check(rows.Count == 1 && Convert.ToInt32(rows[0]["quantity"]) == 1, "证书道具进 ETC（4031332 x1）");

            // 再开一局：赢 → 继续 → 0x0C 下一轮（win 已复位）
            Call(db, cid, RpsOperation.Start);
            Call(db, cid, RpsOperation.Answer, 1);
            var next = Call(db, cid, RpsOperation.Continue);
            Check(next.ok && next.mode == 0x0C, "继续 → mode 0x0C 下一轮");
            Call(db, cid, RpsOperation.Answer, 2);   // 剪刀对石头：输
            var afterLose = Call(db, cid, RpsOperation.Answer, 0);
            Check(!afterLose.ok && afterLose.closed, "输后再答 → 拒绝 + 关窗（源端 ableAnswer=false）");

            // 输着离场 → 无奖励
            Call(db, cid, RpsOperation.Start);
            Call(db, cid, RpsOperation.Answer, 2);   // 剪刀对石头：输
            var quitLose = Call(db, cid, RpsOperation.Leave);
            Check(quitLose.closed && quitLose.rewardItem == 0, "输着离场 → 无奖励 + 关窗");

            // 再来一局（Retry）：同样收费（源端 bug 修正，见服务注释）
            rec = CharactersDao.FindById(db, cid);
            long before = rec.meso;
            var retry = Call(db, cid, RpsOperation.Retry);
            rec = CharactersDao.FindById(db, cid);
            Check(retry.ok && retry.mode == 14 && rec.meso == before - 1000, "再来一局 → mode 14 且扣 1000");

            // 连赢 10 轮：每轮胜利后点「继续」，第 10 次「继续」结算发 4031332+10 并关窗
            NativeRpsService.SetRandom(() => 0);
            RpsResponse last = null;
            for (int i = 0; i < 10; i++)
            {
                var w = Call(db, cid, RpsOperation.Answer, 1);   // 布克石头
                Check(w.roundLabel == i + 1, "第 " + (i + 1) + " 轮获胜");
                last = Call(db, cid, RpsOperation.Continue);
            }
            Check(last.closed && last.rewardItem == 4031342, "连赢 10 轮 → 终局证书 4031342 + 关窗");

            // 无会话出手 → 直接关窗
            var ghost = Call(db, cid, RpsOperation.Answer, 0);
            Check(!ghost.ok && ghost.closed, "无会话 Answer → 拒绝 + 关窗");

            NativeRpsService.SetRandom(null);
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }
}
