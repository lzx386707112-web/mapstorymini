// 假人生成。逐条对齐源端 soloMapling.ArtificialPlayer.BotGeneration：
//   reserveBotId(原子自增 BOT_BASE_ID+count) / loadBotTemplate(Character.getDefault mockClient)
//   / setBotStats(随机中文名/ID/fame) / placeBotOnMap(snapToGround 落点 + stance=5 + map.addPlayer)
//   / addBotToServer(channel+world.playerStorage) / setBotVariables(装饰钩子) / playSpawnChoreography(出生动画)
//   + 环境 bot 上限。
// 安卓适配：源端 MapleMap 运行时改为 mapId + BotRegistry 双索引；已加载地图通过 MapService 落地。
using System;
using System.Threading;

namespace Server.Bot
{
    public static class BotGeneration
    {
        // SoloMaplingConstants.GameConstants.BOT_BASE_ID
        const int BotBaseId = 20000;

        static int currentBotCount = 100;     // 源端 AtomicInteger 起始 100
        static bool environmentBotLimitActive;
        static int environmentBotLimitStartCount;

        public const long SpawnChoreographyMaxMs = 7000;

        static DbStore Db => BotClient.GetBotClient()?.db;
        static readonly Random rng = new Random();
        static Decorator.BotDecorate decorator;
        static Social.BotNamePool names;

        public static int GetBotsCreatedCount() => currentBotCount - 100;

        public static void EnableEnvironmentBotLimit()
        {
            Interlocked.CompareExchange(ref environmentBotLimitStartCount, GetBotsCreatedCount(), 0);
            environmentBotLimitActive = true;
        }

        public static bool EnvironmentBotLimitReached()
        {
            if (!environmentBotLimitActive) return false;
            int max = Db != null ? BotConfig.EnvironmentBotMax(Db) : 30;
            return GetEnvironmentLimitedBotCount() >= max;
        }

        public static void DisableEnvironmentBotLimit()
        {
            environmentBotLimitActive = false;
            environmentBotLimitStartCount = 0;
        }

        static int GetEnvironmentLimitedBotCount() => Math.Max(0, GetBotsCreatedCount() - environmentBotLimitStartCount);

        // —— 生成主流程 ——
        public static int CreateBot(int x, int y, int mapId, bool ignoreEnvironmentLimit = false, bool playArrival = true)
        {
            int botId = ReserveBotId(ignoreEnvironmentLimit);
            if (botId < 0) return -1;

            BotCharacter bot = LoadBotTemplate();
            // 档案优先：联机里每个账号唯一，所以同一个人永远是同一张脸、同一个名字。
            // 领不到（档案被占满或关掉了）才回退到现随机，行为与改造前一致。
            BotDirectory.Ensure(Db);
            var profile = BotDirectory.Take(mapId, rng);
            bot.profileId = profile?.id ?? 0;
            SetBotStats(bot, botId, profile);
            if (BotConfig.NormalEquipsEnabled(Db)) { bot.equips.Clear(); bot.equipment.Clear(); }
            AddBotToServer(bot);
            PlaceBotOnMap(bot, x, y, mapId);
            SetBotVariables(bot, profile);              // 装饰钩子 阶段E
            if (playArrival) PlaySpawnChoreography(bot);
            return botId;
        }

        // 源端线程等待转为主线程续接；完成回调用于后续业务等待完整入场。
        public static void PlaySpawnChoreography(BotCharacter bot, Action completed = null)
        {
            bot.arrivalTask?.Cancel();
            Environment.BotMovementPlayback.Cancel(bot);
            int mapId = bot.mapId;
            bool Alive() => ReferenceEquals(BotRegistry.Get(bot.id), bot) && bot.mapId == mapId;
            void Replay(string name, Action next)
            {
                if (!Alive()) return;
                var bytes = BotResources.Load().ReadBytes(Environment.BotMovementRecording.ResourcePath(0, name, "bin"));
                var frames = Environment.BotMovementRecording.ReadBinary(bytes);
                Environment.BotMovementPlayback.Play(bot, frames, true, stopped => { if (!stopped && Alive()) next(); });
            }
            void Turn()
            {
                if (!Alive()) return;
                if (bot.stance == 3 || bot.stance == 5 || bot.stance == 7) { completed?.Invoke(); return; }
                Replay("turnaroundtoleft", () => completed?.Invoke());
            }
            bot.arrivalTask = BotTasks.Schedule(() => Replay("portalenterdrop", () =>
            {
                if (rng.Next(2) == 0) { completed?.Invoke(); return; }
                bot.arrivalTask = BotTasks.Schedule(Turn, rng.Next(1000, 1501));
            }), rng.Next(500, 1201) + 1500);
        }

        public static void WarpBotToLocation(BotCharacter bot, int x, int y, int mapId, Action completed = null)
        {
            PlaceBotOnMap(bot, x, y, mapId);
            PlaySpawnChoreography(bot, completed);
        }

        static int ReserveBotId(bool ignoreLimit = false)
        {
            while (true)
            {
                int current = currentBotCount;
                int limited = Math.Max(0, current - 100 - environmentBotLimitStartCount);
                int max = Db != null ? BotConfig.EnvironmentBotMax(Db) : 30;
                if (!ignoreLimit && environmentBotLimitActive && limited >= max) return -1;
                if (Interlocked.CompareExchange(ref currentBotCount, current + 1, current) == current)
                    return BotBaseId + current;
            }
        }

        static BotCharacter LoadBotTemplate()
        {
            BotClient source = BotClient.EnsureStandaloneClient(Db);
            var client = new BotClient { db = source.db, world = source.world, channel = source.channel,
                accId = source.accId, gmLevel = source.gmLevel };
            var bot = new BotCharacter { client = client, world = source.world, channel = source.channel,
                accId = source.accId, gmLevel = source.gmLevel };
            client.player = bot;
            return bot;
        }

        static void SetBotStats(BotCharacter bot, int botId, BotProfile profile)
        {
            // 有档案就用档案里的名字（固定不变）；否则沿用源端的现随机。
            bot.name = profile != null && !string.IsNullOrEmpty(profile.name)
                ? profile.name
                : (BotConfig.RandomChineseNameEnabled(Db) ? BotHelpers.GetRandomChineseBotName() : GetRandomCharacterIGN());
            bot.id = botId;
            bot.fame = botId;
        }

        // 已加载地图按 PlatformSpawner.snapToGround 落地；地图未注册时保留原坐标。
        static void PlaceBotOnMap(BotCharacter bot, int x, int y, int mapId)
        {
            bot.x = x;
            bot.y = Server.Field.MapService.SnapToGround(mapId, x, y);
            bot.stance = 5;                  // 源端setStance(5)：IDLE_LEFT
            bot.facing = -1;
            BotRegistry.EnterMap(bot, mapId);
        }

        static void AddBotToServer(BotCharacter bot) => BotRegistry.Add(bot);

        // 外观链已迁移；源端recalcLocalStats/套装/恢复HPMP仍需接完整角色属性服务。
        static void SetBotVariables(BotCharacter bot, BotProfile profile = null)
        {
            // 档案指定了外观模板就照模板来：同一个假人每次上线都是同一套衣服、同一张脸
            // （联机客户端能共享纹理，靠的就是「外观来自数据库、不来自运行时随机」）。
            BotAppearanceTemplates.Ensure(Db);
            var all = BotAppearanceTemplates.All;
            if (profile != null && all.Count > 0)
            {
                all[((profile.template % all.Count) + all.Count) % all.Count].ApplyTo(bot);
                return;
            }
            var template = BotAppearanceTemplates.Take(rng);
            if (template != null) { template.ApplyTo(bot); return; }
            decorator ??= new Decorator.BotDecorate(BotResources.Load(), BotItemCatalog.Shared, BotTypeManager.Output);
            decorator.ApplyAppearance(bot, Db);
        }

        public static void RemoveBotFromServer(BotCharacter bot)
        {
            if (bot == null) return;
            // 档案归还：联机的「下线」——下次这个账号可以被重新分配，但身份不变。
            if (bot.profileId != 0) { BotDirectory.Release(bot.profileId); bot.profileId = 0; }
            BotStore.RemoveActiveBot(bot.id);
            BotWander.Stop(bot);
            BotRegistry.Remove(bot.id);
        }

        // 轮询就绪（对齐源端 createBotPollReadiness，阶段A 同步返回）
        public static BotCharacter CreateBotPollReadiness(int x, int y, int mapId, bool ignoreEnvironmentLimit = false, bool playArrival = true)
        {
            int botId = CreateBot(x, y, mapId, ignoreEnvironmentLimit, playArrival);
            return botId < 0 ? null : BotRegistry.Get(botId);
        }

        static string GetRandomCharacterIGN()
        {
            names ??= new Social.BotNamePool(BotResources.Load(), rng);
            return names.CharacterName();
        }
    }
}
