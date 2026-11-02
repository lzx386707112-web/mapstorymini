// 便签服务 —— 源端 NoteService.java + NoteActionHandler.java + SendNoteSuccessPacket/ShowNotesPacket 直译。
// 源端语义（MEMO_RESULT）：
//   sub 3 ShowNotes：登录（PlayerLoggedinHandler）/ 收礼提醒时下发未读便签列表；
//       每条 = int id + string(from + " ")（源端注释：Nexon 忘了空格）+ string message + long filetime + byte fame。
//   sub 4 SendNoteSuccess：写便签成功回执。
// NoteActionHandler：
//   action 0 = 商城回复送礼便签：availableNotes > 0 才能发，sendWithFame（收件人名望 +1）+ decreaseNotes；
//   action 1 = 游戏内丢弃便签：批量 delete（源端 deleteById 真删），fame 按每条便签的 fame 累计加给角色。
// availableNotes（CashShop.notes）：源端不持久化的内存计数，收礼领取时每份 +1。单机等价放在
//   NativeNoteService.AvailableNotes（静态内存），NativeCashService.Open 自动领取礼物时累计。
// 发送来源（本项目可达的）：商城送礼附言提醒（CashOperationHandler action 4）、Fredrick 寄存提醒
//   （FredrickProcessor.runFredrickSchedule）。509 离线消息道具与婚礼/公会便签因 GMS083 素材缺失/系统未迁移不接。
using System;
using System.Collections.Generic;

public sealed class MemoRequest
{
    public int characterId;
    public int action;             // 0 回复送礼便签 / 1 丢弃 / 2 拉取未读 / 3 写便签（sendNormal 等价 509 道具）
    public string target = "";     // action 0/3：收件角色名
    public string message = "";    // action 0/3：便签正文
    public int[] discardIds;       // action 1：要丢弃的便签 id
}

public sealed class MemoResponse
{
    public bool ok;
    public string error;
    public MemoNote[] notes;       // action 2：未读列表（ShowNotes）
    public int fameGain;           // action 1：丢弃累计名望
    public bool sent;              // action 0/3：SendNoteSuccess
}

public sealed class MemoNote
{
    public int id;
    public string from;
    public string message;
    public long timestamp;
    public int fame;
}

public static class NativeNoteService
{
    // 源端 CashShop.notes 内存计数（不落库，重登重置）
    public static int AvailableNotes;

    // DueyFredrickTask：源端服务端每 1 小时跑一次 runFredrickSchedule（寄存到期提醒便签）。
    // 单机无全局调度线程，等价为惰性补跑：任意便签请求进来时距上次超过 1 小时就补跑一次。
    public static readonly TimeSpan FredrickInterval = TimeSpan.FromHours(1);
    static DateTime lastFredrick = DateTime.MinValue;

    internal static void RunFredrickIfDue(DbStore db)
    {
        if ((DateTime.Now - lastFredrick) < FredrickInterval) return;
        lastFredrick = DateTime.Now;
        NativeMerchantService.FredrickDueReminders(db, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public static void ResetForTest() { AvailableNotes = 0; lastFredrick = DateTime.MinValue; }

    // NoteService.sendNormal
    public static void SendNormal(DbStore db, string message, string from, string to)
    {
        var row = new DbRow { ["to"] = to, ["from"] = from, ["message"] = message, ["timestamp"] = Now(), ["fame"] = 0L, ["deleted"] = 0L };
        db.Table("notes").Insert(row);
        db.Save();
    }

    // NoteService.sendWithFame（收件人名望 +1 的标记记在便签上，由丢弃时结算）
    public static void SendWithFame(DbStore db, string message, string from, string to)
    {
        var row = new DbRow { ["to"] = to, ["from"] = from, ["message"] = message, ["timestamp"] = Now(), ["fame"] = 1L, ["deleted"] = 0L };
        db.Table("notes").Insert(row);
        db.Save();
    }

    // NoteService.show：发给某角色的未读（deleted=0）便签
    public static List<MemoNote> Unread(DbStore db, string to)
    {
        var list = new List<MemoNote>();
        foreach (var row in db.Table("notes").Select(x => string.Equals(DbStore.Str(x, "to"), to, StringComparison.Ordinal) && DbStore.Int(x, "deleted") == 0))
        {
            list.Add(new MemoNote {
                id = DbStore.Int(row, "id"),
                from = DbStore.Str(row, "from"),
                message = DbStore.Str(row, "message"),
                timestamp = DbStore.Long(row, "timestamp"),
                fame = DbStore.Int(row, "fame"),
            });
        }
        return list;
    }

    // NoteService.delete：源端 deleteById 真删，返回被删便签的 fame（-1 = 不存在）
    public static int Delete(DbStore db, int noteId)
    {
        var row = db.Table("notes").Find(x => DbStore.Int(x, "id") == noteId);
        if (row == null) return -1;
        int fame = DbStore.Int(row, "fame");
        db.Table("notes").Delete(x => DbStore.Int(x, "id") == noteId);
        db.Save();
        return fame;
    }

    public static MemoResponse Handle(DbStore db, MemoRequest r)
    {
        var resp = new MemoResponse();
        var self = db.Table("characters").Find(x => DbStore.Int(x, "id") == r.characterId);
        if (self == null) { resp.error = "角色不存在"; return resp; }
        string name = DbStore.Str(self, "name");
        RunFredrickIfDue(db);   // DueyFredrickTask 惰性等价（登录自动拉取便签必经此处）

        switch (r.action)
        {
            case 0: // NoteActionHandler action 0：商城回复送礼便签（sendWithFame + decreaseNotes）
                if (AvailableNotes <= 0) { resp.error = "没有可回复的便签次数"; return resp; }
                if (string.IsNullOrEmpty(r.message) || r.message.Length > 73) { resp.error = "便签须为1～73个字符"; return resp; }
                var receiver = db.Table("characters").Find(x => string.Equals(DbStore.Str(x, "name"), r.target, StringComparison.OrdinalIgnoreCase));
                if (receiver == null) { resp.error = "收件角色不存在"; return resp; }
                SendWithFame(db, r.message, name, DbStore.Str(receiver, "name"));
                AvailableNotes--;
                resp.sent = true; resp.ok = true; return resp;

            case 1: // NoteActionHandler action 1：丢弃便签，名望按便签记录结算（源端读库而非信包字段）
                int fame = 0;
                if (r.discardIds != null)
                    foreach (int id in r.discardIds)
                    {
                        int f = Delete(db, id);
                        if (f < 0) continue; // 已丢弃/不存在，源端仅告警跳过
                        fame += f;
                    }
                if (fame > 0)
                {
                    int updated = DbStore.Int(self, "fame") + fame;
                    db.Table("characters").Update(DbStore.Eq("id", r.characterId), new DbRow { ["fame"] = (long)updated });
                    db.Save();
                }
                resp.fameGain = fame; resp.ok = true; return resp;

            case 2: // ShowNotes
                resp.notes = Unread(db, name).ToArray(); resp.ok = true; return resp;

            case 3: // 等价 509 离线消息道具的写便签（sendNormal；道具素材 GMS083 缺失，保留服务能力）
                if (string.IsNullOrEmpty(r.message) || r.message.Length > 73) { resp.error = "便签须为1～73个字符"; return resp; }
                var target = db.Table("characters").Find(x => string.Equals(DbStore.Str(x, "name"), r.target, StringComparison.OrdinalIgnoreCase));
                if (target == null) { resp.error = "收件角色不存在"; return resp; }
                SendNormal(db, r.message, name, DbStore.Str(target, "name"));
                resp.sent = true; resp.ok = true; return resp;

            default:
                resp.error = "未知操作"; return resp;
        }
    }

    // Server.getInstance().getCurrentTime()：毫秒时间戳
    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
