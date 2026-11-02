// GM 活动竞技场回归（雪球 / 椰子）：逐条对照源端
//   server/events/gm/Snowball.java（hits=3、snowmanhp=1000、里程碑 45/290/560、600s 定胜负）
//   net/server/channel/handlers/SnowballHandler.java（500ms 间隔、what%2==team、10/45-15 伤害）
//   server/events/gm/Coconut.java + Coconuts.java（506 椰子、80/401/20、300s+120s 加时）
//   net/server/channel/handlers/CoconutHandler.java（750ms 锁、40%/1%/5% 概率链）
// 节流/锁/时长走 SetClock 虚拟时钟，椰子用显式 id（-1 是服务端随机挑，测试不可复现）。
using System;
using System.Collections.Generic;

static class ArenaChecks
{
    static ArenaResponse Call(DbStore db, int cid, ArenaOperation op, int param = 0)
        => NativeEventArenaService.Handle(db, new ArenaRequest { characterId = cid, operation = op, param = param });

    static long now;
    static void Advance(long ms) => now += ms;

    static int MakeChar(DbStore db, int accountId, string name, int map)
    {
        var made = CharFactory.CreateCharacter(db, accountId, 0, name, CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
        var rec = CharactersDao.FindById(db, made.characterId);
        rec.map = map;
        CharactersDao.Save(db, rec);
        return made.characterId;
    }

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 活动竞技场（雪球 Snowball / 椰子 Coconut）==");
        NativeEventArenaService.ResetForTest();
        now = Environment.TickCount64;
        NativeEventArenaService.SetClock(() => now);
        SnowballChecks(Check);
        NativeEventArenaService.ResetForTest();
        now = Environment.TickCount64;
        NativeEventArenaService.SetClock(() => now);   // ResetForTest 会把时钟重置回真实时间
        CoconutChecks(Check);
        NativeEventArenaService.SetClock(null);
        NativeEventArenaService.SetRandom(null);
    }

    static void SnowballChecks(Action<bool, string> Check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_arena_sb_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "arena_sb", "pw123456");
            int p0 = MakeChar(db, login.accountId, "SnowA", NativeEventArenaService.SnowballMap);
            int p1 = MakeChar(db, login.accountId, "SnowB", NativeEventArenaService.SnowballMap);

            // 进图自动开局 + 交替分队
            var r0 = Call(db, p0, ArenaOperation.Sync);
            Check(r0.kind == 1 && r0.myTeam == 0, "雪球进图自动开局，先到者队 0");
            var r1 = Call(db, p1, ArenaOperation.Sync);
            Check(r1.myTeam == 1, "后到者交替分队（队 1）");
            Check(r0.timeLeftMs > 590_000, "开局时长 ≈600 秒");
            Check(r0.myPosition == 0 && r0.mySnowmanHp == 1000, "雪球初始 position=0 / 雪人 1000 HP");

            // what%2 必须 == 队伍（拒绝不占用 500ms 间隔）
            var bad = Call(db, p0, ArenaOperation.Hit, 1);   // 队 0 打 what=1（队 1 的球）
            Check(!bad.ok, "what%2 != team 拒绝");

            // 推雪球：3 次 what=0（间隔 >500ms）→ position 1（hits 3→0→重置）
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) Advance(600);
                Call(db, p0, ArenaOperation.Hit, 0);
            }
            r0 = Call(db, p0, ArenaOperation.Sync);
            Check(r0.myPosition == 1, "推雪球 3 次 → position=1（实际 " + r0.myPosition + "）");
            r1 = Call(db, p1, ArenaOperation.Sync);
            Check(r1.otherPosition == 1, "对方视角同步可见我方推进");

            // 500ms 攻击间隔：连点第 2 次不生效
            Advance(600);
            Call(db, p0, ArenaOperation.Hit, 0);   // 推进 hits→2
            var fast = Call(db, p0, ArenaOperation.Hit, 0);   // 未推进时钟 → 节流
            r0 = Call(db, p0, ArenaOperation.Sync);
            Check(r0.myPosition == 1 && fast.ok, "500ms 内连点被节流，position 保持 1");

            // 打雪人（what=2/3）：只广播不改状态（源端 hit() 只处理 what<2）
            Advance(600);
            Call(db, p0, ArenaOperation.Hit, 2);
            r0 = Call(db, p0, ArenaOperation.Sync);
            Check(r0.myPosition == 1 && r0.mySnowmanHp == 1000, "打雪人不改 position/雪人 HP（源端 hit() 只处理 what<2）");

            // 非活动图 Sync = 退场清理
            var off = MakeChar(db, login.accountId, "SnowC", 910000000);
            var rOff = Call(db, off, ArenaOperation.Sync);
            Check(rOff.kind == 0, "非活动图 Sync 返回无活动");
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }

    static void CoconutChecks(Action<bool, string> Check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_arena_cc_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "arena_cc", "pw123456");
            int p0 = MakeChar(db, login.accountId, "CocoA", NativeEventArenaService.CoconutMap);
            int p1 = MakeChar(db, login.accountId, "CocoB", NativeEventArenaService.CoconutMap);

            var r0 = Call(db, p0, ArenaOperation.Sync);
            Check(r0.kind == 2 && r0.myTeam == 0, "椰子进图自动开局");
            Call(db, p1, ArenaOperation.Sync);
            Check(r0.falling == 401 && r0.bombings == 80 && r0.stopped == 20, "初始余量 falling=401 / bombing=80 / stopped=20");
            Check(r0.timeLeftMs > 290_000, "开局时长 ≈300 秒");

            // 概率链（每次 Hit 最多掷 3 次：0.4 → 0.01 → 0.05）：
            // 前 3 击 hits 1→2→3（普通击打走 else 分支不掷随机）；第 4 击起 hits>2 才判定。
            // 恒 0.1：0.1<0.4 ✓ 进入判定；0.1<0.01？否跳过哑弹；0.1<0.05？否跳过炸弹 → 击落。
            NativeEventArenaService.SetRandom(() => 0.1);
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) Advance(800);   // 750ms 锁
                Call(db, p0, ArenaOperation.Hit, 0);
            }
            var res = Call(db, p0, ArenaOperation.Sync);
            Check(res.falling == 400 && res.mapleScore == 1, "恒 0.1 随机下第 4 击击落：falling 401→400、Maple+1（实际 " + res.mapleScore + "）");

            // 队 1 打另一个椰子（id=1）→ Story 得分
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) Advance(800);
                Call(db, p1, ArenaOperation.Hit, 1);
            }
            res = Call(db, p0, ArenaOperation.Sync);
            Check(res.storyScore == 1 && res.falling == 399, "队 1 击落 → Story+1、falling 399（实际 " + res.storyScore + "/" + res.falling + "）");

            // 哑弹路径：恒 0.001 → 0.001<0.4 ✓、0.001<0.01 ✓ → 哑弹（stopped--，falling 不动）
            NativeEventArenaService.SetRandom(() => 0.001);
            int stoppedBefore = res.stopped;
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) Advance(800);
                Call(db, p0, ArenaOperation.Hit, 2);   // 打第 3 个椰子
            }
            Advance(800);
            Call(db, p0, ArenaOperation.Hit, 2);
            res = Call(db, p0, ArenaOperation.Sync);
            Check(res.stopped == stoppedBefore - 1, "0.001 随机命中哑弹：stopped " + stoppedBefore + "→" + (stoppedBefore - 1));
            Check(res.falling == 399, "哑弹不计入击落");

            // 时间结算：当前 1:1 平分 → 先加时；再击落一枚让 Maple 领先，推过时长验证胜负传送
            now += 130_000;   // 推过 300s → Tick 触发平分加时（ended=false，endAt+120s）
            var bonus = Call(db, p0, ArenaOperation.Sync);
            Check(!bonus.ended && bonus.timeLeftMs > 100_000, "平分触发 120 秒加时");

            NativeEventArenaService.SetRandom(() => 0.1);
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) Advance(800);
                Call(db, p0, ArenaOperation.Hit, 3);
            }
            now += 200_000;   // 推过加时 → 结算
            var final = Call(db, p0, ArenaOperation.Sync);
            Check(final.ended && final.warpTo == NativeEventArenaService.WinnerMap, "超时结算，Maple 队（2:1）传送胜方图");
            var finalLoser = Call(db, p1, ArenaOperation.Sync);
            Check(finalLoser.warpTo == NativeEventArenaService.ExitMap, "败方 Story 队传送出口图");
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }
}
