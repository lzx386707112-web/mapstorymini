using System;
using System.Collections.Generic;
using System.Linq;
using Server.Field;

namespace Server.Bot.Environment
{
    // ≡ soloMapling/Environment/EnvironmentManager.java（市场环境启动编排，1505 行）。
    // 逐条对齐的语义：
    //   · requestMarketStartup → 后台执行 marketEnvironmentStartup；startupStarted/startupComplete
    //     原子状态机（startMarket 返回 already/running/started 三态）。
    //   · 三波启动：Free Market core（FM 入口闲逛 + 商人 + 社交）→ specialty（扭蛋/21点/OPQ）→
    //     Town wanderers（城镇游荡，由 BotTownSpawner 承接）。
    //   · EnableEnvironmentBotLimit：环境 bot 限额启动，120 秒后解除。
    //   · createBotWithRetry：环境限额 + FM 入口槽位上限（marketBotMax）+ 失败重试。
    //   · spawnBotsOnMapOnPlatform：PlatformParser 平台 CSV + PlatformSpawner 找空位 + snapToGround。
    // 有意的端口差异（源端依赖未搬系统，宁缺不占位）：
    //   · ArtificialFreeMarket.populateMarketRooms 依赖 hired-merchant 商店系统（阶段 F 余量）→ 跳过；
    //   · Blackjack/OPQ/21点桌的行为类（BlackjackDealerBot/OPQ 逻辑）未搬 → 不生成无行为假人；
    //   · 椅子/技能/换装周期刷新：源端是外挂定时循环，本端口已由 MarketBotAmbient 在各
    //     状态机内建周期行为，不再重复挂调度；
    //   · 多线程 latch/CompletableFuture → 主线程时钟续接（BotTasks），波次顺序执行。
    public static class BotEnvironment
    {
        const int FMEntrance = 910000000;
        const int Henesys = 100000000;
        const int HenesysMarket = 100000100;
        const int HenesysPark = 100000200;

        static readonly object gate = new object();
        static bool startupStarted, startupComplete;
        static int marketEntranceBotCount;
        static PlatformParser platforms;

        // BotRuntime 注入（按需加载地图几何）；城镇游荡走 BotTownSpawner（BotRuntime 实例资产）
        public static Func<int, MapRuntime> GetMap;
        public static Action TownWanderers;

        static DbStore Db => BotClient.GetBotClient()?.db;

        static BotResources res;
        static BotResources Res => res ??= BotResources.Load();

        static PlatformParser Platforms => platforms ??= new PlatformParser(
            path => Res.ReadText(path),
            dir => Res.ListFileStems(dir, ".csv"));

        // 落点前确保地图几何已在 MapService（源端直接 getMapleMapById）
        static void EnsureMap(int mapId)
        {
            if (!MapService.Has(mapId)) GetMap?.Invoke(mapId);
        }

        public static bool IsMarketStartupComplete => startupComplete;
        public static bool IsMarketStartupRunning => startupStarted && !startupComplete;

        // 会话复位：BotRuntime.Dispose 会移除全部假人并停调度时钟，
        // 不复位启动状态机的话，下次进游戏 RequestMarketStartup 会因
        // startupStarted=true 直接 no-op，自由市场永远不会再有假人。
        public static void Reset()
        {
            lock (gate)
            {
                startupStarted = false;
                startupComplete = false;
                marketEntranceBotCount = 0;
            }
        }

        // ≡ requestMarketStartup：丢给调度时钟执行，状态机保证幂等
        public static void RequestMarketStartup()
        {
            if (startupStarted) return;
            BotTasks.Schedule(MarketEnvironmentStartup, 0);
        }

        // ≡ marketEnvironmentStartup（EnvironmentManager.java:113）
        public static void MarketEnvironmentStartup()
        {
            lock (gate)
            {
                if (startupStarted) return;
                startupStarted = true;
            }
            BotGeneration.EnableEnvironmentBotLimit();
            try
            {
                Wave(1, "Free Market core",
                    SpawnScaledFMEntranceBots,
                    SpawnScaledMerchantBots,
                    SpawnMarketSocialBots);

                Wave(2, "Free Market specialty",
                    SpawnGachaBotsHenesys);          // 21点桌/OPQ 待行为类搬入（见文件头差异说明）

                Wave(3, "Town wanderers", SpawnTownWanderers);

                startupComplete = true;
                BotTasks.Schedule(BotGeneration.DisableEnvironmentBotLimit, 120_000);
            }
            catch (Exception)
            {
                // ≡ catch (RuntimeException)：回滚状态，允许再次请求（allowMarketStartupRetry）
                lock (gate) { startupStarted = false; startupComplete = false; }
                throw;
            }
        }

        static void Wave(int number, string name, params Action[] tasks)
        {
            foreach (var task in tasks) task();   // 源端并行 join；本端口主线程顺序执行，异常上抛整体失败
        }

        // ——————————— Wave 1：FM 入口 ———————————

        // ≡ spawnScaledFMEntranceBots + distributeMarketEntranceWanderers：2/3 预算平分三平台
        static void SpawnScaledFMEntranceBots()
        {
            if (!BotConfig.FmBotsEnabled(Db)) return;
            var parts = DistributeMarketEntranceWanderers(BotConfig.MarketBotMax(Db));
            SpawnFMEntranceBotsBatch(parts[0], parts[1], parts[2]);
        }

        public static int[] DistributeMarketEntranceWanderers(int cap)
        {
            int limit = Math.Max(0, cap);
            int wanderBudget = limit * 2 / 3;
            if (limit > 0 && wanderBudget < 3) wanderBudget = Math.Min(limit, 3);
            int m1 = wanderBudget / 3, m2 = wanderBudget / 3, m5 = wanderBudget - m1 - m2;
            return new[] { m1, m2, m5 };
        }

        static void SpawnFMEntranceBotsBatch(int m1Count, int m2Count, int m5Count)
        {
            if (m1Count > 0) BotTypeManager.SetAndStartBots(SpawnBotsOnMapOnPlatform(m1Count, FMEntrance, "m1"), BotType.FM_BOT);
            if (m2Count > 0) BotTypeManager.SetAndStartBots(SpawnBotsOnMapOnPlatform(m2Count, FMEntrance, "m2"), BotType.FM_BOT);
            if (m5Count > 0) BotTypeManager.SetAndStartBots(SpawnBotsOnMapOnPlatform(m5Count, FMEntrance, "m5"), BotType.FM_BOT);
        }

        // ≡ spawnScaledMerchantBots + spawnMerchBotsBatch：selling=cap/6、buying=cap/8、nx=cap/15
        static void SpawnScaledMerchantBots()
        {
            if (!BotConfig.FmMerchantsEnabled(Db)) return;
            int cap = BotConfig.MarketBotMax(Db);
            int selling = Math.Max(2, cap / 6), buying = Math.Max(1, cap / 8), nx = Math.Max(1, cap / 15);
            SpawnMerchBotsBatch("m1", Math.Max(1, selling / 2), Math.Max(1, buying / 2), 0);
            SpawnMerchBotsBatch("m2", selling - selling / 2, buying - buying / 2, 0);
            SpawnMerchBotsBatch("m5", 0, 0, nx);
        }

        static void SpawnMerchBotsBatch(string platform, int selling, int buying, int nx)
        {
            if (selling > 0) BotTypeManager.SetAndStartBots(SpawnBotsOnMapOnPlatform(selling, FMEntrance, platform), BotType.SELLING_MERCHANT_BOT);
            if (buying > 0) BotTypeManager.SetAndStartBots(SpawnBotsOnMapOnPlatform(buying, FMEntrance, platform), BotType.BUYING_MERCHANT_BOT);
            if (nx > 0) BotTypeManager.SetAndStartBots(SpawnBotsOnMapOnPlatform(nx, FMEntrance, platform), BotType.NX_MERCHANT_BOT);
        }

        // ≡ spawnMarketSocialBots：FM 入口 (-260,34)~(330,34) 三只 SOCIAL_BOT
        static void SpawnMarketSocialBots()
        {
            var ids = SpawnFillerBots(3, FMEntrance, new BotPoint(-260, 34), new BotPoint(330, 34));
            BotTypeManager.SetAndStartBots(ids, BotType.SOCIAL_BOT);
        }

        // ——————————— Wave 2 ———————————

        // ≡ spawnGachaBotsHenesys：FM m5 平台 (40,34) 半径 250 内 2 只 GACHA_BOT
        static void SpawnGachaBotsHenesys()
        {
            if (!BotConfig.GachaBotsEnabled(Db)) return;
            var ids = SpawnBotsOnMapOnPlatformInRadius(2, FMEntrance, "m5", new BotPoint(40, 34), 250);
            BotTypeManager.SetAndStartBots(ids, BotType.GACHA_BOT);
        }

        // ——————————— Wave 3 ———————————

        // ≡ spawnTownWanderers：城镇配置驱动，具体实现已在 BotTownSpawner（BotRuntime 持有）
        static void SpawnTownWanderers() => TownWanderers?.Invoke();

        // ——————————— 生成原语 ———————————

        // ≡ spawnBotsOnMapOnPlatform（EnvironmentManager.java:937）
        public static List<int> SpawnBotsOnMapOnPlatform(int numBots, int mapId, string platformId)
        {
            EnsureMap(mapId);
            var platform = Platforms.ParsePlatform(mapId, platformId);
            var occupied = new List<BotPoint>();
            var ids = new List<int>();
            var spawner = new PlatformSpawner();
            for (int i = 0; i < numBots; i++)
            {
                var spawn = PlatformSpawner.SnapToGround(mapId, spawner.FindUnoccupiedPoint(platform, occupied));
                occupied.Add(spawn);
                var bot = CreateBotWithRetry(spawn, mapId, 5);
                if (bot != null) ids.Add(bot.id);
            }
            return ids;
        }

        // ≡ spawnBotsOnMapOnPlatformInRadius（EnvironmentManager.java:996）
        public static List<int> SpawnBotsOnMapOnPlatformInRadius(int numBots, int mapId, string platformId, BotPoint center, int radius)
        {
            EnsureMap(mapId);
            var platform = Platforms.ParsePlatform(mapId, platformId);
            var occupied = new List<BotPoint>();
            var ids = new List<int>();
            var spawner = new PlatformSpawner();
            for (int i = 0; i < numBots; i++)
            {
                var spawn = FindUnoccupiedPointInRadius(spawner, platform, occupied, center, radius);
                if (spawn == null) continue;
                var grounded = PlatformSpawner.SnapToGround(mapId, spawn.Value);
                occupied.Add(grounded);
                var bot = CreateBotWithRetry(grounded, mapId, 5);
                if (bot != null) ids.Add(bot.id);
            }
            return ids;
        }

        // ≡ spawnFillerBots（EnvironmentManager.java:1058）：临时平线平台 + 找空位
        public static List<int> SpawnFillerBots(int numBots, int mapId, BotPoint p1, BotPoint p2)
        {
            EnsureMap(mapId);
            int minX = Math.Min(p1.x, p2.x), maxX = Math.Max(p1.x, p2.x);
            var platform = new Platform(minX, maxX, p1.y, new List<BotPoint> { p1, p2 }, Platform.Type.FLAT);
            var occupied = new List<BotPoint>();
            var ids = new List<int>();
            var spawner = new PlatformSpawner();
            foreach (var raw in spawner.FindUnoccupiedPoints(platform, occupied, numBots))
            {
                var spawn = PlatformSpawner.SnapToGround(mapId, raw);
                occupied.Add(spawn);
                var bot = CreateBotWithRetry(spawn, mapId, 5);
                if (bot != null) ids.Add(bot.id);
            }
            return ids;
        }

        // ≡ findUnoccupiedPointInRadius（EnvironmentManager.java:1191）：100 次采样限制在半径内
        static BotPoint? FindUnoccupiedPointInRadius(PlatformSpawner spawner, Platform platform, List<BotPoint> occupied, BotPoint center, int radius)
        {
            for (int i = 0; i < 100; i++)
            {
                var candidate = spawner.FindUnoccupiedPoint(platform, occupied);
                if (Math.Abs(candidate.x - center.x) <= radius && Math.Abs(candidate.y - center.y) <= radius)
                    return candidate;
            }
            return null;
        }

        // ≡ createBotWithRetry（EnvironmentManager.java:1229）：环境限额 + FM 入口槽位 + 重试。
        // 源端重试间 Thread.sleep 等待异步就绪；本端口 CreateBotPollReadiness 同步完成，立即重试。
        static BotCharacter CreateBotWithRetry(BotPoint spawn, int mapId, int maxRetries)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (BotGeneration.EnvironmentBotLimitReached()) return null;
                bool slot = ReserveMarketEntranceBotSlot(mapId);
                if (mapId == FMEntrance && !slot) return null;
                try
                {
                    var bot = BotGeneration.CreateBotPollReadiness(spawn.x, spawn.y, mapId);
                    if (bot != null) return bot;
                }
                catch (Exception)
                {
                    // 源端记录失败继续重试
                }
                ReleaseMarketEntranceBotSlot(mapId, slot);
                if (BotGeneration.EnvironmentBotLimitReached()) return null;
            }
            return null;
        }

        // ≡ reserveMarketEntranceBotSlot：FM 入口 bot 总量受 market_bot_max 限制
        static bool ReserveMarketEntranceBotSlot(int mapId)
        {
            if (mapId != FMEntrance) return false;
            while (true)
            {
                int current = marketEntranceBotCount;
                if (current >= BotConfig.MarketBotMax(Db)) return false;
                if (System.Threading.Interlocked.CompareExchange(ref marketEntranceBotCount, current + 1, current) == current)
                    return true;
            }
        }

        static void ReleaseMarketEntranceBotSlot(int mapId, bool reserved)
        {
            if (mapId == FMEntrance && reserved)
                Math.Max(0, System.Threading.Interlocked.Decrement(ref marketEntranceBotCount));
        }
    }
}
