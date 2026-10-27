// 激活 bot 状态机存储。对齐源端 BotMessagingSystem.CharacterStorage：
//   ConcurrentHashMap<Integer, BotSM> activeBotMap；addActiveBot/removeActiveBot/getBotById/getAllBots
//   + updateAllBots() 遍历 running 的 bot 调 updateState()（被 BotScheduler 驱动）。
// 阶段A 单线程（Unity 主线程），用 Dictionary；后续若异步 tick 再换 ConcurrentDictionary。
using System.Collections.Generic;

namespace Server.Bot
{
    public static class BotStore
    {
        static readonly Dictionary<int, BotStateMachine> activeBotMap = new Dictionary<int, BotStateMachine>();

        static readonly List<BotActor> respondants = new List<BotActor>();
        static readonly List<BotActor> inquirers = new List<BotActor>();
        public static void AddRespondant(BotActor actor) => respondants.Add(actor);
        public static void RemoveRespondant(BotActor actor) => respondants.Remove(actor);
        public static bool IsRespondant(BotActor actor) => respondants.Contains(actor);
        public static IReadOnlyList<BotActor> Respondants => respondants;
        public static void AddInquirer(BotActor actor) => inquirers.Add(actor);
        public static void RemoveInquirer(BotActor actor) => inquirers.Remove(actor);
        public static bool IsInquirer(BotActor actor) => inquirers.Contains(actor);
        public static bool IsInvisibleBot(int id) => id == 100;

        public static void AddActiveBot(int id, BotStateMachine character)
        {
            if (activeBotMap.TryGetValue(id, out var previous) && !ReferenceEquals(previous, character))
                previous.StopScheduledTask();
            activeBotMap[id] = character;
        }

        public static void RemoveActiveBot(int id)
        {
            if (activeBotMap.TryGetValue(id, out var bot)) bot.StopScheduledTask();
            activeBotMap.Remove(id);
        }

        public static bool BotLoggedIn(int id) => activeBotMap.ContainsKey(id);

        public static BotStateMachine GetBotById(int id) => activeBotMap.TryGetValue(id, out var b) ? b : null;

        public static Dictionary<int, BotStateMachine> GetAllBots() => activeBotMap;

        public static void UpdateAllBots()
        {
            foreach (var bot in new List<BotStateMachine>(activeBotMap.Values))
                if (bot.running && ReferenceEquals(GetBotById(bot.chr.id), bot)) bot.UpdateState();
        }
    }
}
