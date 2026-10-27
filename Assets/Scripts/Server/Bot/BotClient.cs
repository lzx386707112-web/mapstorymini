// 单例 standalone mock client。对齐源端 BotClientHandler.ensureStandaloneClient()：
//   standalone host保留共享上下文；每个bot另建mock client并复制world/channel/accId/gmLevel。
// 安卓单机无真实连接，此处为占位；db 由 GameServer 初始化时注入，供 BotConfig 读取配置。

namespace Server.Bot
{
    public sealed class BotClient
    {
        public const string ClientIp = "127.0.0.1";
        public static long SessionId = 6969;

        static BotClient botClient;

        public DbStore db;
        public int world = 0;          // WORLD_SCANIA
        public int channel = 1;        // CHANNEL_1
        public int accId;
        public int gmLevel;
        public BotCharacter player;    // host 角色（id=0）

        public static BotClient EnsureStandaloneClient(DbStore db)
        {
            if (botClient != null)
            {
                if (db != null) botClient.db = db;
                return botClient;
            }
            var mock = new BotClient { db = db, world = 0, channel = 1 };
            var host = new BotCharacter { id = 0, name = "Host", client = mock };
            mock.player = host;
            botClient = mock;
            return botClient;
        }

        // 取已有单例（db 可为 null，仅在已初始化后调用）
        public static BotClient GetBotClient() => botClient;

        public static void Init(DbStore db) => EnsureStandaloneClient(db);
    }
}
