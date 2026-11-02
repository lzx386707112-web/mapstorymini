// 举报回归：逐条对照源端
//   net/server/channel/handlers/ReportHandler.java（次数/费用/落库/GM 广播）
//   client/Character.java:193（possibleReports=10 会话内存，不落库）、2470（decreaseReports）
//   PacketCreator.reportResponse（SUE_CHARACTER_RESULT：2 次数用尽 / 4 金币不足）
//   resources/db/migration/V1.0.36__create_reports.sql（reports 表）
using System;
using System.Collections.Generic;

static class ReportChecks
{
    static ReportResponse Handle(DbStore db, int cid, int type, string victim, int reason, string desc, string chatlog = "")
        => NativeReportService.Handle(db, new ReportRequest
        {
            characterId = cid, type = type, victim = victim,
            reason = reason, description = desc, chatlog = chatlog,
        });

    static List<DbRow> Rows(DbStore db, int reporterId)
        => db.Table("reports").Select(DbStore.Eq("reporterid", reporterId));

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 举报 Report（ReportHandler / reports 表）==");
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_report_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "report_acc", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "Reporter", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;
            var victimMade = CharFactory.CreateCharacter(db, login.accountId, 0, "Victim", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);

            // 合法对话举报：扣 300 金币 + 落库（victim 按 name 解析）
            var rec = CharactersDao.FindById(db, cid);
            rec.meso = 100000;                      // 建号金币为 0，先补足（举报要求 meso>299）
            CharactersDao.Save(db, rec);
            long mesoBefore = rec.meso;
            var res = Handle(db, cid, 1, "Victim", 2, "金币广告", "Reporter : 大家好\nVictim : 加Q123");
            Check(res.ok && res.code == 0, "合法对话举报接受");
            rec = CharactersDao.FindById(db, cid);
            Check(rec.meso == mesoBefore - 300, "举报扣 300 金币（" + (mesoBefore - rec.meso) + "）");
            var rows = Rows(db, cid);
            Check(rows.Count == 1, "reports 落 1 条");
            Check(rows[0]["victimid"] is long vid && vid == victimMade.characterId, "victimid 按角色名解析");
            Check((string)rows[0]["description"] == "金币广告" && ((string)rows[0]["chatlog"]).StartsWith("Reporter :"),
                "description/chatlog 完整落库");
            Check((long)rows[0]["reporttime"] > 0, "reporttime 已记录");

            // 非法程序申报：reason 无语义也照收（源端 type 0 不校验 reason）
            res = Handle(db, cid, 0, "Victim", 0, "开加速挂");
            Check(res.ok && Rows(db, cid).Count == 2, "非法程序申报接受并落库");

            // 金币不足 → code 4（源端 meso > 299 才放行，且不扣次数）
            rec = CharactersDao.FindById(db, cid);
            rec.meso = 200;
            CharactersDao.Save(db, rec);
            res = Handle(db, cid, 1, "Victim", 0, "穷也得举报");
            Check(!res.ok && res.code == 4, "金币 ≤299 → reportResponse(4)");
            Check(Rows(db, cid).Count == 2, "失败不落库");
            rec = CharactersDao.FindById(db, cid);
            Check(rec.meso == 200, "失败不扣金币");

            // 次数用尽 → code 2：会话 10 次，已用 2 次（第 2 次因金币不足没扣），补足金币后连报 8 次再第 11 次拒绝
            rec.meso = 100000;
            CharactersDao.Save(db, rec);
            for (int i = 0; i < 8; i++) Handle(db, cid, 0, "Victim", 0, "刷满次数 " + i);
            Check(Rows(db, cid).Count == 10, "10 次会话额度全部可用");
            res = Handle(db, cid, 0, "Victim", 0, "第 11 次");
            Check(!res.ok && res.code == 2, "超 10 次 → reportResponse(2)");
            Check(Rows(db, cid).Count == 10, "拒绝不落库");

            // 会话重置（EndSocialSession）→ 额度恢复 10（源端 possibleReports 内存字段重登重置）
            NativeReportService.EndSession(cid);
            res = Handle(db, cid, 0, "Victim", 0, "重登后第一次");
            Check(res.ok, "会话重置后额度恢复（possibleReports=10 重登重置）");

            // 未知类型拒绝（源端广播 packet editing）
            res = Handle(db, cid, 7, "Victim", 0, "改包");
            Check(!res.ok, "未知 type 拒绝");

            // 受害者不存在 → victimid=-1 照常落库（源端 getIdByName 返回 -1）
            var made2 = CharFactory.CreateCharacter(db, login.accountId, 0, "Reporter2", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var rec2 = CharactersDao.FindById(db, made2.characterId);
            rec2.meso = 100000;
            CharactersDao.Save(db, rec2);
            res = Handle(db, made2.characterId, 1, "不存在的名字", 1, "描述");
            var rows2 = Rows(db, made2.characterId);
            Check(res.ok && rows2.Count == 1 && (long)rows2[0]["victimid"] == -1, "受害者不存在 → victimid=-1 照常落库");
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }
}
