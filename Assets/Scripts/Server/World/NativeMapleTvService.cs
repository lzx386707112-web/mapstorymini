// MapleTV —— 源端 MapleTVEffect.java + UseCashItemHandler itemType 507 case 5 直译。
// 源端语义：
//   道具 5075000..5075005（tvType = itemId % 10）：
//     tvType 0/1/2 = 普通 TV（type 0 Normal / 1 Star / 2 Heart），无/有 victim；
//     tvType >= 3 = megassenger（大 TV 带滚动字幕）+ ear 广播一条 serverNotice(3)；
//     tvType 3/5 读 victim（TV 上的搭档），tvType 4 无 victim。
//   时长：tvType 4 → 30s、tvType 5 → 60s、其它 15s；同一世界同一时刻只播一个（ACTIVE[world]），
//   正在播放时使用 → 拒绝且不消耗道具。
//   第 5 条消息限 15 字符（PacketCreator.sendTV 截断）；sendTV type = tvType<=2 ? tvType : tvType-3。
// 单机等价：唯一真人，victim/partner 无意义（无搭档外观渲染，注释保留包语义）；
//   ear 的 serverNotice(3) 广播等价聊天框白字；结束用惰性时间判（测试可注入时钟）。
using System;

public sealed class TvRequest
{
    public int characterId;
    public int itemId;          // 5075000..5075005
    public string message = ""; // 单条消息（源端 5 条；单机输入一条，其余置空由客户端轮播跳过）
}

public sealed class TvResponse
{
    public bool ok;
    public string error;
    public int tvType;          // itemId % 10
    public int sendType;        // 0 Normal / 1 Star / 2 Heart
    public long durationMs;     // 15000/30000/60000
    public string message;
    public string speaker;
}

public static class NativeMapleTvService
{
    // 源端 ACTIVE[world]（单机单世界）：TV 占用标志
    public static bool Active;
    public static long ActiveUntil;

    public static Func<long> Now = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static void ResetForTest() { Active = false; ActiveUntil = 0; Now = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

    // PacketCreator.sendTV：type<=2 ? type : type-3
    static int SendType(int tvType) => tvType <= 2 ? tvType : tvType - 3;

    public static TvResponse Handle(DbStore db, TvRequest r)
    {
        var resp = new TvResponse();
        var self = db.Table("characters").Find(x => DbStore.Int(x, "id") == r.characterId);
        if (self == null) { resp.error = "角色不存在"; return resp; }
        int itemId = r.itemId;
        if (itemId < 5075000 || itemId > 5075005) { resp.error = "这不是 MapleTV 券"; return resp; }
        int tvType = itemId % 10;

        // MapleTVEffect.broadcastMapleTVIfNotActive：正在播放 → 拒绝（源端 dropMessage 不消耗）
        long now = Now();
        if (Active && now < ActiveUntil) { resp.error = "MapleTV 正在播放中"; return resp; }

        // broadcastTV：15s，type4 30s，type5 60s
        long duration = tvType == 4 ? 30000 : tvType == 5 ? 60000 : 15000;
        Active = true;
        ActiveUntil = now + duration;

        resp.ok = true;
        resp.tvType = tvType;
        resp.sendType = SendType(tvType);
        resp.durationMs = duration;
        resp.message = r.message ?? "";
        resp.speaker = DbStore.Str(self, "name");
        return resp;
    }
}
