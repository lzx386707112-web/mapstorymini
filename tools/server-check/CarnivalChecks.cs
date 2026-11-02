// 怪物嘉年华回归：逐条对照源端 MonsterCarnival.java + Character.gainCP + finish 传送表
//   CP：gain>0 涨 totalCP、扣减不回退 totalCP、可花费 CP 不为负；
//   计时 600s（timeDefault 数据缺失兜底，见服务端头注）；平局延长一次 180s；
//   finish：胜队 gainFestivalPoints(totalCP) + 传送 map+3（980000201 → 980000203 胜 / 980000204 负）；
//   单机化：蓝队 CP 由 BotTick 模拟（注入 BotTickMs）。
using System;

static class CarnivalChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 怪物嘉年华（CPQ）==");
        NativeCarnivalService.ResetForTest();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_carnival_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "cpq_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "CpqGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            CarnivalResponse Call(int op, int mapId = 0, int cp = 0) =>
                NativeCarnivalService.Handle(db, new CarnivalRequest { characterId = cid, operation = op, mapId = mapId, cp = cp });

            // 进入校验
            Check(!Call(0, 910000000).ok, "非 CPQ 战斗图拒绝");
            var low = db.Table("characters").Find(x => DbStore.Int(x, "id") == cid);
            low["level"] = 20L;
            Check(!Call(0, 980000201).ok, "低于 30 级拒绝（脚本 cpqMinLvl）");
            low["level"] = 51L;
            Check(!Call(0, 980000201).ok, "高于 50 级拒绝（脚本 cpqMaxLvl）");
            low["level"] = 35L;

            var enter = Call(0, 980000201);
            Check(enter.ok && enter.inCarnival && enter.secondsLeft > 590 && enter.team == 0, "进入 980000201 → 计时 600s、红队");
            Check(!Call(0, 980000301).ok, "已有战斗 → 重复进入拒绝");

            // CP 记账（Character.gainCP：totalCP 只涨不减；可花费 CP 不为负）
            Check(Call(1, cp: 3).ok && Call(1, cp: 2).ok, "杀怪 CP 3+2");
            var s = Call(2);
            Check(s.redCp == 5 && s.redTotal == 5, "redCp=5 total=5");
            Check(Call(1, cp: -8).ok, "花费 8 CP（召唤语义）");
            s = Call(2);
            Check(s.redCp == 0 && s.redTotal == 5, "CP 扣到 0 截断、totalCP 仍 5");

            // 蓝队模拟：注入 BotTickMs 恒 0（roll<40 → 每秒 +1）
            NativeCarnivalService.BotTickMs = () => 0;
            long clock = NativeCarnivalService.Now();
            NativeCarnivalService.Now = () => clock + 10000;
            s = Call(2);
            Check(s.blueCp >= 9, "蓝队 10 秒模拟 CP ≥9（实际 " + s.blueCp + "）");
            Check(!s.finished && s.secondsLeft <= 590, "未到时限不结算");

            // 到时结算：红 5 < 蓝 → 判负 → 传送 980000204（map+3）
            NativeCarnivalService.Now = () => clock + 601000;
            var fin = Call(2);
            Check(fin.finished && !fin.win && fin.warpTo == 980000204, "判负 → 传送 980000204（源端 map+3）");
            Check(NativeCarnivalService.FestivalPoints == 0, "败队不得名望分（源端 finish 只给胜队）");

            // 胜局：红队大比分 → 传送 980000203（map+2）+ FestivalPoints += totalCP
            var enter2 = Call(0, 980000201);
            Check(enter2.ok, "结算后再进（ACTIVE 已清）");
            NativeCarnivalService.BotTickMs = () => 99;   // roll≥60 → 每秒 +1
            NativeCarnivalService.Now = () => clock + 602000;
            for (int i = 0; i < 30; i++) Call(1, cp: 3);   // 红 +90
            NativeCarnivalService.Now = () => clock + 1202000;
            var fin2 = Call(2);
            Check(fin2.finished && fin2.win && fin2.warpTo == 980000203, "判胜 → 传送 980000203（源端 map+2）");
            Check(fin2.festivalPoints == fin2.redTotal && fin2.redTotal == 90, "名望分 = totalCP 90");

            // 平局延长：进第三场，蓝队全程空过（roll≥90）→ 双 0 平局 → 延长一次（180s）不结算
            var enter3 = Call(0, 980000201);
            NativeCarnivalService.BotTickMs = () => 95;
            NativeCarnivalService.Now = () => clock + 1203000;
            var ext = Call(2);
            NativeCarnivalService.Now = () => clock + 1802000;   // 第一时限到
            var tie = Call(2);
            Check(tie.ok && !tie.finished && tie.inCarnival, "平局 → 延长不结算");
            Check(tie.secondsLeft > 0 && tie.secondsLeft <= 180, "延长后剩余 ≤180s");
            NativeCarnivalService.Now = () => clock + 1985000;   // 延长期也到且仍平 → 源端 timeUp 平局走 extendTime 循环，单机收敛为结算
            var fin3 = Call(2);
            Check(fin3.finished, "延长期到仍平局 → 结算收敛");
            NativeCarnivalService.ResetForTest();
        }
        finally { NativeCarnivalService.ResetForTest(); db.Save(); System.IO.File.Delete(path); }
    }
}
