// MapleTV 回归：逐条对照源端 MapleTVEffect.java + PacketCreator.sendTV + UseCashItemHandler case 5
//   tvType=itemId%10；sendType=tvType<=2?tvType:tvType-3；时长 15s/30s(tvType4)/60s(tvType5)；
//   同一世界同时只播一个；正在播放拒绝。
using System;

static class TvChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== MapleTV ==");
        NativeMapleTvService.ResetForTest();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_tv_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "tv_user", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "TvGuy", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            TvResponse Call(int itemId, string message = "上电视啦") =>
                NativeMapleTvService.Handle(db, new TvRequest { characterId = cid, itemId = itemId, message = message });

            Check(!Call(5071000).ok && !Call(0).ok, "非 TV 券拒绝");
            var bad = NativeMapleTvService.Handle(db, new TvRequest { characterId = 999999, itemId = 5075000 });
            Check(!bad.ok, "角色不存在拒绝");

            // 首播：tvType0 → sendType0 + 15s
            var tv = Call(5075000);
            Check(tv.ok && tv.tvType == 0 && tv.sendType == 0 && tv.durationMs == 15000 && tv.speaker == "TvGuy",
                "5075000 → tvType0/sendType0/15s");
            Check(!Call(5075001).ok, "播放中 → 拒绝（源端 ACTIVE）");

            // 时钟推进 15s → 结束可再播；各 tvType 的 sendType 与时长（每次只 Call 一遍：开播即占用）
            long clock = NativeMapleTvService.Now();
            NativeMapleTvService.Now = () => clock + 16000;
            var star = Call(5075001);
            Check(star.ok && star.sendType == 1, "时钟推进后可再播（5075001 → sendType1）");
            NativeMapleTvService.Now = () => clock + 32000;
            var heart = Call(5075002);
            Check(heart.ok && heart.sendType == 2 && heart.durationMs == 15000,
                "5075002 → sendType2（Heart）/15s");
            NativeMapleTvService.Now = () => clock + 48000;
            var mega = Call(5075003);
            Check(mega.ok && mega.sendType == 0 && mega.durationMs == 15000,
                "5075003（megassenger）→ sendType0/15s");
            NativeMapleTvService.Now = () => clock + 64000;
            var half = Call(5075004);
            Check(half.ok && half.sendType == 1 && half.durationMs == 30000,
                "5075004 → sendType1/30s");
            NativeMapleTvService.Now = () => clock + 95000;
            var full = Call(5075005);
            Check(full.ok && full.sendType == 2 && full.durationMs == 60000,
                "5075005 → sendType2/60s");

            // 时钟回拨（真实时间恢复）后旧占用仍在有效期内 → 仍拒绝
            NativeMapleTvService.Now = () => clock + 96000;
            Check(!Call(5075000).ok, "60s 播放期内再次使用拒绝");
            NativeMapleTvService.Now = () => clock + 200000;
            Check(Call(5075000).ok, "60s 到期后可再播");
        }
        finally { NativeMapleTvService.ResetForTest(); db.Save(); System.IO.File.Delete(path); }
    }
}
