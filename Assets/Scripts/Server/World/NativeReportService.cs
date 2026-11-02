// 举报服务 —— 源端 ReportHandler.java 的直译。
// 源端语义：
//   1. type：0 = 非法程序申报（无 reason）、1 = 对话举报（带 reason + chatlog）；其它类型视为改包。
//   2. 次数：possibleReports 每角色每会话 10 次，不落库（源端 Character.java:193 内存字段）；
//      用尽 → reportResponse(2)「举报次数已用完」。
//   3. 费用：meso > 299 才能举报，扣 300 金币；不足 → reportResponse(4)「金币不足」。
//   4. 成功：向 GM 频道广播 serverNotice(6, "X was reported for: Y")，
//      并写 reports 表（reporttime/reporterid/victimid/reason/chatlog/description）；
//      源端成功时不回包（客户端本地显示「已成功登记」）。victim 不存在时 victimid=-1 照写（getIdByName）。
using System;
using System.Collections.Generic;

public sealed class ReportRequest
{
    public int characterId;
    public int type;          // 0 非法程序 / 1 对话举报
    public string victim = "";
    public int reason;
    public string description = "";
    public string chatlog = "";
}

public sealed class ReportResponse
{
    public bool ok;
    public int code;          // 源端 SUE_CHARACTER_RESULT：2 次数用尽 / 4 金币不足；0 = 成功（源端不回包）
    public string error;
}

public static class NativeReportService
{
    public const int MaxReportsPerSession = 10;   // 源端 possibleReports 初值
    public const int ReportFee = 300;

    // 源端会话内存计数：不落库、重登重置
    static readonly Dictionary<int, int> remaining = new Dictionary<int, int>();

    public static void EndSession(int characterId) => remaining.Remove(characterId);

    static int Remaining(int cid) => remaining.TryGetValue(cid, out var n) ? n : MaxReportsPerSession;

    public static ReportResponse Handle(DbStore db, ReportRequest r)
    {
        if (r == null) return new ReportResponse { ok = false, code = 0, error = "无效请求" };
        if (r.type != 0 && r.type != 1)
            // 源端：未知类型向 GM 广播 "is probably packet editing"
            return new ReportResponse { ok = false, code = 0, error = "未知举报类型" };

        int left = Remaining(r.characterId);
        if (left <= 0) return new ReportResponse { ok = false, code = 2, error = "你今天已经不能再举报了。" };
        var reporter = CharactersDao.FindById(db, r.characterId);
        if (reporter == null) return new ReportResponse { ok = false, code = 0, error = "角色不存在" };
        if (reporter.meso <= 299) return new ReportResponse { ok = false, code = 4, error = "金币不足，无法举报。" };

        remaining[r.characterId] = left - 1;
        reporter.meso -= ReportFee;
        CharactersDao.Save(db, reporter);

        // GM 广播（源端 Server.broadcastGMMessage）：单机下没有 GM 在线，落一条系统日志等价物
        Console.WriteLine("[GM] " + reporter.name + " 举报: " + r.victim + " was reported for: " + r.description);

        // 源端 Character.getIdByName：查不到返回 -1，照常落库
        int victimId = -1;
        foreach (var row in db.Table("characters").Select(DbStore.Eq("name", r.victim ?? "")))
        {
            victimId = (int)ConvertId(row);
            break;
        }
        db.Table("reports").Insert(new DbRow
        {
            ["reporttime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["reporterid"] = r.characterId,
            ["victimid"] = victimId,
            ["reason"] = r.reason,
            ["chatlog"] = r.chatlog ?? "",
            ["description"] = r.description ?? "",
        });
        db.Save();
        return new ReportResponse { ok = true, code = 0 };
    }

    static long ConvertId(DbRow row)
        => row != null && row.TryGetValue("id", out var v) && v != null
            ? System.Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) : -1;
}
