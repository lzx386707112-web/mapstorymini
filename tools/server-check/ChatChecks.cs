using System;
using System.IO;

// 聊天频道回归：重点锁住悄悄话的三种结果，避免再退回「无论目标是谁都报不在线」。
static class ChatChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 聊天 / 悄悄话 ==");
        string path = Path.Combine(Path.GetTempPath(), "mapledash_chat_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            int account = (int)db.Table("accounts").Insert(new DbRow { ["name"] = "ChatAccount" });
            int me = (int)db.Table("characters").Insert(new DbRow {
                ["accountid"] = (long)account, ["name"] = "Me", ["world"] = 0L, ["level"] = 30L, ["map"] = 10000L });
            db.Table("characters").Insert(new DbRow {
                ["accountid"] = (long)account, ["name"] = "Offline", ["world"] = 0L, ["level"] = 30L, ["map"] = 10000L });

            var social = new NativeSocialService(db);
            SocialResponse Chat(string target, NativeChatChannel channel, string text = "hi")
                => social.Handle(new SocialRequest { characterId = me, operation = SocialOperation.SendChat,
                    target = target, text = text, channel = channel });

            Check(Chat("Nobody", NativeChatChannel.Whisper).error == "角色不存在", "悄悄话：没有这个角色");
            Check(Chat("Offline", NativeChatChannel.Whisper).error == "对方不在线", "悄悄话：存档有但没在线");
            // 本机角色之外的在线个体只有假人 actor；Bind 之后它就相当于 getCharacterByName 能取到的人。
            social.Parties.Bind(new Server.Bot.BotActor { id = 900001, name = "Bob", world = 0, online = true });
            var bob = Chat("bob", NativeChatChannel.Whisper);
            Check(bob.ok, "悄悄话：在线角色送达（名字忽略大小写）" + (bob.ok ? "" : "：" + bob.error));
            // 源端 getCharacterByName 也能命中自己，发给自己不算失败。
            // 上一条已经消耗掉 200ms 防刷屏窗口，这里等过去再发，验证的才是在线判定而不是限频。
            System.Threading.Thread.Sleep(220);
            Check(Chat("Me", NativeChatChannel.Whisper).ok, "悄悄话：发给自己");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
