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
    //   · ArtificialFreeMarket.populateMarketRooms 通过权威商店服务逐店排队；
    //   · 椅子/技能/换装周期刷新：源端是外挂定时循环，本端口已由 MarketBotAmbient 在各
    //     状态机内建周期行为，不再重复挂调度；
    //   · 多线程 latch/CompletableFuture → 主线程时钟续接（BotTasks），波次顺序执行。
    //   · FM 入口名额上限不再直接用 market_bot_max：源端该值（默认 10）比 Wave1 自己的计划量还小，
    //     默认配置下会静默丢掉 3 只社交假人，Wave2 的扭蛋/21点/OPQ 全部生成失败。这里按「计划量」
    //     算名额（见 FmEntranceCapacity），保证计划多少就能生成多少。
    //
    // 分帧（本端口新增，不改源端语义）：源端整段跑在后台线程里，主线程不卡；
    // 本端口在主线程跑，所以把每一波再拆成「算座位 → 一个一个生成假人」的步骤队列，
    // 由 BotWorkPump 的每帧毫秒预算驱动。波次顺序、限额、重试全部保持原样。
    public static class BotEnvironment
    {
        public static Action PopulateMarketRooms;
        const int FMEntrance = 910000000;
        const int Henesys = 100000000;
        const int HenesysMarket = 100000100;
        const int HenesysPark = 100000200;

        // 等地图分片后台解析最多让出多少帧（≈3 秒）；超时说明分片确实不在包里，退回同步加载。
        public const int MapWaitFrames = 180;

        static readonly object gate = new object();
        static bool startupStarted, startupComplete, limitDisableScheduled;
        static int marketEntranceBotCount;
        static int fmEntranceCapacity = -1;
        static readonly Random rng = new Random();
        static PlatformParser platforms;

        // BotRuntime 注入（按需加载地图几何）；城镇游荡走 BotTownSpawner（BotRuntime 实例资产）
        public static Func<int, MapRuntime> GetMap;
        // 非阻塞版地图加载：分片已在后台解析好就立刻可用，否则报告「还在解析」。
        public static BotMapLoad TryGetMap;
        public static Action TownWanderers;

        static DbStore Db => BotClient.GetBotClient()?.db;

        static BotResources res;
        static BotResources Res => res ??= BotResources.Load();

        static PlatformParser Platforms => platforms ??= new PlatformParser(
            path => Res.ReadText(path),
            dir => Res.ListFileStems(dir, ".csv"));

        public static bool IsMarketStartupComplete => startupComplete;
        public static bool IsMarketStartupRunning => startupStarted && !startupComplete;

        // ——————————— 分帧泵 ———————————
        //
        // 每一次 Post = 一件小事（生成 1 个假人 / 算一张座位表 / 等一个地图分片）。
        // PumpStep 每帧跑一次，边跑边看 BotWorkPump 的剩余预算，超了就留到下一帧。
        static readonly Queue<Action> steps = new Queue<Action>();
        static BotTasks.Handle pumpTask;
        static bool yieldRequested;

        // 追加一小步到假人分帧泵（供城镇游荡等下游模块续接自己的流程）。
        public static void Post(Action step)
        {
            if (step == null) return;
            lock (steps) steps.Enqueue(step);
            EnsurePump();
        }

        // 等待类步骤用这个：排到队尾，并且本帧别再回头取它。
        // 否则「等地图分片」的步骤会被「出队 → 重新入队 → 立刻又出队」空转，白烧一整帧预算。
        public static void PostNextFrame(Action step)
        {
            if (step == null) return;
            lock (steps) steps.Enqueue(step);
            yieldRequested = true;
            EnsurePump();
        }

        // 用 Handle 判断泵还在不在跑：BotTasks.Stop() 会取消所有任务，
        // 只用一个 bool 标记的话会永远以为「泵已排期」，整个假人生成卡死。
        static void EnsurePump()
        {
            if (pumpTask != null && !pumpTask.Cancelled && !pumpTask.Completed) return;
            pumpTask = BotTasks.Schedule(PumpStep, 0);
        }

        static void PumpStep()
        {
            pumpTask = null;
            yieldRequested = false;
            int executed = 0;
            while (true)
            {
                Action step;
                lock (steps) step = steps.Count > 0 ? steps.Dequeue() : null;
                if (step == null) { FinishStartup(); return; }
                try { using (Server.Bot.BotPerf.Timer("BotEnvironment.Step")) step(); }
                catch (Exception)
                {
                    // ≡ catch (RuntimeException)：回滚状态，允许再次请求（allowMarketStartupRetry）
                    AbortStartup();
                    return;
                }
                if (yieldRequested) { yieldRequested = false; EnsurePump(); return; }
                // 预算用完就让位给渲染：剩下的步骤下一帧继续，绝不把主线程一次吃干。
                // 兜底的 executed 上限防的是「步骤互相续接」时的意外死循环。
                if (!BotWorkPump.Has(0.75f) || ++executed > 512) { EnsurePump(); return; }
            }
        }

        static void FinishStartup()
        {
            if (!startupStarted || startupComplete) return;
            startupComplete = true;
            if (limitDisableScheduled) return;
            limitDisableScheduled = true;
            BotTasks.Schedule(BotGeneration.DisableEnvironmentBotLimit, 120_000);
        }

        static void AbortStartup()
        {
            lock (gate) { startupStarted = false; startupComplete = false; }
            lock (steps) steps.Clear();
        }

        // 会话复位：BotRuntime.Dispose 会移除全部假人并停调度时钟，
        // 不复位启动状态机的话，下次进游戏 RequestMarketStartup 会因
        // startupStarted=true 直接 no-op，自由市场永远不会再有假人。
        public static void Reset()
        {
            lock (gate)
            {
                startupStarted = false;
                startupComplete = false;
                limitDisableScheduled = false;
                marketEntranceBotCount = 0;
            }
            // 分步生成还没走完就退服时，残留的步骤必须在下次进游戏前清掉。
            lock (steps) steps.Clear();
            pumpTask = null;
            yieldRequested = false;
        }

        // ≡ requestMarketStartup：丢给调度时钟执行，状态机保证幂等
        public static void RequestMarketStartup()
        {
            if (startupStarted) return;
            BotTasks.Schedule(MarketEnvironmentStartup, 0);
        }

        // ≡ marketEnvironmentStartup（EnvironmentManager.java:113）
        // 三波按顺序串起来：前一波的座位/假人步骤排完，才把下一波的计划步骤排到队尾。
        public static void MarketEnvironmentStartup()
        {
            lock (gate)
            {
                if (startupStarted) return;
                startupStarted = true;
            }
            BotGeneration.EnableEnvironmentBotLimit();
            lock (steps) steps.Clear();
            Post(() => PopulateMarketRooms?.Invoke());
            Post(PlanWave1);
        }

        // ——————————— Wave 1：FM 入口 + 商人 + 社交 ———————————
        static void PlanWave1() => EnsureMapThen(FMEntrance, () =>
        {
            PlanScaledFMEntranceSeats();
            PlanMerchantSeats();
            PlanSocialSeats();
            Post(PlanWave2);            // 排在本波所有座位步骤之后
        });

        // ≡ spawnScaledFMEntranceBots + distributeMarketEntranceWanderers：2/3 预算平分三平台
        static void PlanScaledFMEntranceSeats()
        {
            if (!BotConfig.FmBotsEnabled(Db)) return;
            var parts = DistributeMarketEntranceWanderers(BotConfig.MarketBotMax(Db));
            PlanPlatformSeats(FMEntrance, "m1", parts[0], BotType.FM_BOT);
            PlanPlatformSeats(FMEntrance, "m2", parts[1], BotType.FM_BOT);
            PlanPlatformSeats(FMEntrance, "m5", parts[2], BotType.FM_BOT);
        }

        public static int[] DistributeMarketEntranceWanderers(int cap)
        {
            int limit = Math.Max(0, cap);
            int wanderBudget = limit * 2 / 3;
            if (limit > 0 && wanderBudget < 3) wanderBudget = Math.Min(limit, 3);
            int m1 = wanderBudget / 3, m2 = wanderBudget / 3, m5 = wanderBudget - m1 - m2;
            return new[] { m1, m2, m5 };
        }

        // ≡ spawnScaledMerchantBots + spawnMerchBotsBatch：selling=cap/6、buying=cap/8、nx=cap/15
        static void PlanMerchantSeats()
        {
            if (!BotConfig.FmMerchantsEnabled(Db)) return;
            int cap = BotConfig.MarketBotMax(Db);
            int selling = Math.Max(2, cap / 6), buying = Math.Max(1, cap / 8), nx = Math.Max(1, cap / 15);
            PlanMerchantBatch("m1", Math.Max(1, selling / 2), Math.Max(1, buying / 2), 0);
            PlanMerchantBatch("m2", selling - selling / 2, buying - buying / 2, 0);
            PlanMerchantBatch("m5", 0, 0, nx);
        }

        static void PlanMerchantBatch(string platform, int selling, int buying, int nx)
        {
            PlanPlatformSeats(FMEntrance, platform, selling, BotType.SELLING_MERCHANT_BOT);
            PlanPlatformSeats(FMEntrance, platform, buying, BotType.BUYING_MERCHANT_BOT);
            PlanPlatformSeats(FMEntrance, platform, nx, BotType.NX_MERCHANT_BOT);
        }

        // ≡ spawnMarketSocialBots：FM 入口 (-260,34)~(330,34) 三只 SOCIAL_BOT
        static void PlanSocialSeats()
            => EnqueueBotSpawns(FillerSeats(3, FMEntrance, new BotPoint(-260, 34), new BotPoint(330, 34)), FMEntrance, BotType.SOCIAL_BOT);

        // ——————————— Wave 2：扭蛋 + 21 点桌 + OPQ 大厅 ———————————
        // ≡ runWave(2, "Free Market specialty", [spawnGachaBotsHenesys, spawnBlackjackTables, spawnOPQBotsInLobby])
        static void PlanWave2() => EnsureMapThen(FMEntrance, () =>
        {
            // ≡ spawnGachaBotsHenesys：FM m5 平台 (40,34) 半径 250 内 2 只 GACHA_BOT
            if (BotConfig.GachaBotsEnabled(Db))
                EnqueueBotSpawns(RadiusSeats(2, FMEntrance, "m5", new BotPoint(40, 34), 250), FMEntrance, BotType.GACHA_BOT);
            PlanBlackjackTable();
            PlanOpqLobbyBots();
            Post(PlanHenesys);
        });

        // ≡ spawnBlackjackTables：FM 入口一张 21 点桌（荷官 1 + AI 玩家 2）。
        // 座位按源端 calculateTablePositions 的矩形等分算：上边 y=-266、下边 y=4，x 从 -320 到 260。
        static void PlanBlackjackTable()
        {
            if (!BotConfig.BlackjackTablesEnabled(Db)) return;
            var seats = TableSeats(FMEntrance,
                new BotPoint(-320, -266), new BotPoint(260, -266),
                new BotPoint(-320, 4), new BotPoint(260, 4));
            Post(() => SpawnBlackjackTable(seats));
        }

        // ≡ calculateTablePositions + jitter：6 个座位，[0] 是荷官（上方中点，无抖动）。
        static List<BotPoint> TableSeats(int mapId, BotPoint topP1, BotPoint topP2, BotPoint botP1, BotPoint botP2)
        {
            int topMinX = Math.Min(topP1.x, topP2.x), topMaxX = Math.Max(topP1.x, topP2.x), topY = topP1.y;
            int topThird = (topMaxX - topMinX) / 3;
            int botMinX = Math.Min(botP1.x, botP2.x), botMaxX = Math.Max(botP1.x, botP2.x), botY = botP1.y;
            int botThird = (botMaxX - botMinX) / 3;
            int Jitter() => rng.Next(125) - 50;

            var raw = new List<BotPoint>
            {
                new BotPoint(topMinX + topThird + topThird / 2, topY),                  // [0] 荷官
                new BotPoint(topMinX + topThird / 2 + Jitter(), topY),                  // [1] 上左
                new BotPoint(topMinX + topThird * 2 + topThird / 2 + Jitter(), topY),   // [2] 上右
                new BotPoint(botMinX + botThird / 2 + Jitter(), botY),                  // [3] 下左
                new BotPoint(botMinX + botThird + botThird / 2 + Jitter(), botY),       // [4] 下中
                new BotPoint(botMinX + botThird * 2 + botThird / 2 + Jitter(), botY),   // [5] 下右
            };
            for (int i = 0; i < raw.Count; i++) raw[i] = PlatformSpawner.SnapToGround(mapId, raw[i]);
            return raw;
        }

        // ≡ spawnBlackjackTable：荷官单独 setAndStartBots(BLACKJACK_DEALER)，AI 玩家不挂状态机
        // （源端也是如此 —— 它们的下注和要牌/停牌全由荷官驱动）。
        // 生成拆成三步排队，避免一帧里连建 3 只假人。
        static void SpawnBlackjackTable(List<BotPoint> seats)
        {
            if (seats == null || seats.Count < 6) return;
            var playerSeatOrder = new List<int> { 1, 2, 3, 4, 5 };
            Shuffle(playerSeatOrder);

            BotCharacter dealer = null;
            var players = new List<BotCharacter>();
            int stage = 0;

            void Step()
            {
                switch (stage++)
                {
                    case 0:
                        dealer = CreateBotWithRetry(seats[0], FMEntrance, 5);
                        if (dealer == null) return;
                        Post(Step);
                        break;
                    case 1:
                    case 2:
                    {
                        var seat = seats[playerSeatOrder[stage - 2]];
                        var player = CreateBotWithRetry(seat, FMEntrance, 5);
                        if (player != null) players.Add(player);
                        Post(Step);
                        break;
                    }
                    default:
                        WireTable(dealer, players, seats[0]);
                        break;
                }
            }
            Post(Step);
        }

        static void WireTable(BotCharacter dealer, List<BotCharacter> players, BotPoint dealerSeat)
        {
            if (dealer == null) return;
            BotTypeManager.SetAndStartBots(new List<int> { dealer.id }, BotType.BLACKJACK_DEALER);
            if (BotStore.GetBotById(dealer.id) is not Types.Blackjack.BlackjackDealerBot table) return;

            // 玩家先入桌（牌局逻辑），朝向等出生编排播完再摆（否则会被编舞覆盖）
            foreach (var player in players)
            {
                table.Table.AddPlayer(player);
                table.interactors.SetRespondant(player);
            }
            BotTasks.Schedule(() =>
            {
                foreach (var player in players)
                    player.facing = dealerSeat.x >= player.x ? 1 : -1;
            }, BotGeneration.SpawnChoreographyMaxMs + 500);
        }

        // ≡ spawnOPQBotsInLobby：FM 入口 m3/m4 上共 3 只，等级 50~70。
        static void PlanOpqLobbyBots()
        {
            if (!BotConfig.OpqLobbyBotsEnabled(Db)) return;
            var platformIds = new[] { "m3", "m4" };      // ≡ List.of("m3", "m4")
            const int totalBots = 3;
            int perPlatform = totalBots / platformIds.Length;
            int remainder = totalBots % platformIds.Length;
            for (int i = 0; i < platformIds.Length; i++)
            {
                int count = perPlatform + (i < remainder ? 1 : 0);
                if (count <= 0) continue;
                string platformId = platformIds[i];
                Post(() => EnqueueBotSpawns(PlatformSeats(FMEntrance, platformId, count), FMEntrance, BotType.OPQ_BOT, 50, 70));
            }
        }

        static void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ——————————— Henesys 人群 ———————————
        // ≡ spawnHenesysBots。源端只挂在 GM 命令（EnvironmentCommand）上，不在启动流程里；
        // 本工程按「全部打开」并入启动流程，仍受三个 feature 开关控制。
        // 注：源端 m4/m5/m6_social 三组只创建不 setAndStartBots（漏了），这里按意图启动。
        static void PlanHenesys()
        {
            if (!BotConfig.HenesysCrowdEnabled(Db) && !BotConfig.HenesysMarketCrowdEnabled(Db)
                && !BotConfig.HenesysParkCrowdEnabled(Db))
            { Post(PlanWave3); return; }

            EnsureMapThen(Henesys, () =>
            {
                if (BotConfig.HenesysCrowdEnabled(Db))
                {
                    PlanPlatformSeats(Henesys, "m1", 30, BotType.HENESYS_BOT);
                    PlanPlatformSeats(Henesys, "m4_social", 3, BotType.HENESYS_BOT);
                    PlanPlatformSeats(Henesys, "m5_social", 3, BotType.HENESYS_BOT);
                    PlanPlatformSeats(Henesys, "m6_social", 3, BotType.HENESYS_BOT);
                }
                PlanHenesysMarket();
            });
        }
        static void PlanHenesysMarket()
        {
            if (!BotConfig.HenesysMarketCrowdEnabled(Db) && !BotConfig.HenesysParkCrowdEnabled(Db))
            { Post(PlanWave3); return; }
            EnsureMapThen(HenesysMarket, () =>
            {
                if (BotConfig.HenesysMarketCrowdEnabled(Db))
                {
                    PlanPlatformSeats(HenesysMarket, "m1", 10, BotType.HENESYS_BOT);
                    PlanPlatformSeats(HenesysMarket, "m2", 10, BotType.HENESYS_BOT);
                }
                PlanHenesysPark();
            });
        }
        static void PlanHenesysPark()
        {
            if (!BotConfig.HenesysParkCrowdEnabled(Db)) { Post(PlanWave3); return; }
            EnsureMapThen(HenesysPark, () =>
            {
                PlanPlatformSeats(HenesysPark, "m1", 10, BotType.HENESYS_BOT);
                Post(PlanWave3);
            });
        }

        // ——————————— Wave 3：城镇游荡 ———————————
        // ≡ spawnTownWanderers：城镇配置驱动，具体实现已在 BotTownSpawner（BotRuntime 持有，
        // 内部同样按「一个城镇一步 / 一个假人一步」续接）。
        static void PlanWave3() => TownWanderers?.Invoke();

        // ——————————— 座位 → 逐人生成 ———————————

        // 算座位表本身只是纯数学（毫秒级），并且要求地图几何已登记；
        // 真正常开销的是后面的 CreateBot（随机外观/装备池/注册状态机），所以拆成两步。
        static void PlanPlatformSeats(int mapId, string platformId, int count, BotType type)
        {
            if (count <= 0) return;
            Post(() => EnqueueBotSpawns(PlatformSeats(mapId, platformId, count), mapId, type));
        }

        // 一次只生成一个假人，剩下的排到队尾；全部生成完才启动这一批的状态机。
        // 第一条也排队（不在这里直接跑），这样「算座位」全部走完才开始逐个生成，波次顺序不乱。
        static void EnqueueBotSpawns(List<BotPoint> seats, int mapId, BotType type,
            int minLevel = 0, int maxLevel = 0)
        {
            var ids = new List<int>();
            int index = 0;
            void NextBot()
            {
                if (index >= seats.Count) { BotTypeManager.SetAndStartBots(ids, type); return; }
                var bot = CreateBotWithRetry(seats[index++], mapId, 5);
                if (bot != null)
                {
                    // ≡ setBotsLevelRange：OPQ 大厅要求 50~70 级
                    if (maxLevel >= minLevel && minLevel > 0)
                        bot.level = minLevel + rng.Next(maxLevel - minLevel + 1);
                    ids.Add(bot.id);
                }
                Post(NextBot);
            }
            Post(NextBot);
        }

        static List<BotPoint> PlatformSeats(int mapId, string platformId, int count)
        {
            var platform = Platforms.ParsePlatform(mapId, platformId);
            var occupied = new List<BotPoint>();
            var seats = new List<BotPoint>();
            var spawner = new PlatformSpawner();
            for (int i = 0; i < count; i++)
            {
                var spawn = PlatformSpawner.SnapToGround(mapId, spawner.FindUnoccupiedPoint(platform, occupied));
                occupied.Add(spawn);
                seats.Add(spawn);
            }
            return seats;
        }

        // ≡ findUnoccupiedPointInRadius（EnvironmentManager.java:1191）：100 次采样限制在半径内
        static List<BotPoint> RadiusSeats(int numBots, int mapId, string platformId, BotPoint center, int radius)
        {
            var platform = Platforms.ParsePlatform(mapId, platformId);
            var occupied = new List<BotPoint>();
            var seats = new List<BotPoint>();
            var spawner = new PlatformSpawner();
            for (int i = 0; i < numBots; i++)
            {
                var spawn = FindUnoccupiedPointInRadius(spawner, platform, occupied, center, radius);
                if (spawn == null) continue;
                var grounded = PlatformSpawner.SnapToGround(mapId, spawn.Value);
                occupied.Add(grounded);
                seats.Add(grounded);
            }
            return seats;
        }

        // ≡ spawnFillerBots（EnvironmentManager.java:1058）：临时平线平台 + 找空位
        static List<BotPoint> FillerSeats(int numBots, int mapId, BotPoint p1, BotPoint p2)
        {
            int minX = Math.Min(p1.x, p2.x), maxX = Math.Max(p1.x, p2.x);
            var platform = new Platform(minX, maxX, p1.y, new List<BotPoint> { p1, p2 }, Platform.Type.FLAT);
            var occupied = new List<BotPoint>();
            var seats = new List<BotPoint>();
            var spawner = new PlatformSpawner();
            foreach (var raw in spawner.FindUnoccupiedPoints(platform, occupied, numBots))
            {
                var spawn = PlatformSpawner.SnapToGround(mapId, raw);
                occupied.Add(spawn);
                seats.Add(spawn);
            }
            return seats;
        }

        // 等一帧地图几何就绪：分片没解完就把这一步重新排队（主线程不碰 JSON），
        // 地图确实不在包里就立刻放弃（原端同样跳过），等够 MapWaitFrames 才退回同步加载兜底。
        static void EnsureMapThen(int mapId, Action next, int attempt = 0)
        {
            if (MapService.Has(mapId)) { next(); return; }
            var state = TryGetMap != null ? TryGetMap(mapId, out _, false) : BotMapState.Missing;
            if (state == BotMapState.Ready) { next(); return; }
            if (state == BotMapState.Missing) { next(); return; }
            if (attempt >= MapWaitFrames)
            {
                try { GetMap?.Invoke(mapId); } catch (ArgumentException) { /* 地图不在包里：后续步骤自然跳过 */ }
                next();
                return;
            }
            PostNextFrame(() => EnsureMapThen(mapId, next, attempt + 1));
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

        // ≡ reserveMarketEntranceBotSlot：FM 入口 bot 总量上限。
        // 源端直接用 market_bot_max（默认 10），但 Wave1 自己的计划量就已经是
        //   闲逛 2/3 + 商人 1/6 + 1/8 + 1/15 + 社交 3 ≈ 13 只
        // 于是一到 10 就拒绝，社交剩一半、Wave2 的扭蛋/21点/OPQ 全部生成失败（源端同样如此）。
        // 「全部打开」要求计划多少就能生成多少，所以名额按计划量算，market_bot_max 仍然决定市场规模。
        static bool ReserveMarketEntranceBotSlot(int mapId)
        {
            if (mapId != FMEntrance) return false;
            int capacity = FmEntranceCapacity();
            while (true)
            {
                int current = marketEntranceBotCount;
                if (current >= capacity) return false;
                if (System.Threading.Interlocked.CompareExchange(ref marketEntranceBotCount, current + 1, current) == current)
                    return true;
            }
        }

        public static int FmEntranceCapacity()
        {
            if (fmEntranceCapacity > 0) return fmEntranceCapacity;
            int cap = BotConfig.MarketBotMax(Db);
            var parts = DistributeMarketEntranceWanderers(cap);
            int wanderers = parts[0] + parts[1] + parts[2];
            // 商人：selling 拆两批各 max(1, ·)，所以实际落地量可能比 selling 多 1
            int selling = Math.Max(2, cap / 6), buying = Math.Max(1, cap / 8), nx = Math.Max(1, cap / 15);
            int merchants = Math.Max(1, selling / 2) + (selling - selling / 2)
                          + Math.Max(1, buying / 2) + (buying - buying / 2) + nx;
            int social = 3;
            int gacha = BotConfig.GachaBotsEnabled(Db) ? 2 : 0;
            int blackjack = BotConfig.BlackjackTablesEnabled(Db) ? 3 : 0;   // 1 荷官 + 2 AI 玩家
            int opq = BotConfig.OpqLobbyBotsEnabled(Db) ? 3 : 0;
            fmEntranceCapacity = wanderers + merchants + social + gacha + blackjack + opq;
            return fmEntranceCapacity;
        }

        static void ReleaseMarketEntranceBotSlot(int mapId, bool reserved)
        {
            if (mapId == FMEntrance && reserved)
                Math.Max(0, System.Threading.Interlocked.Decrement(ref marketEntranceBotCount));
        }

        // ——————————— 同步版生成原语（保留给后台/自检直接调用）———————————

        // ≡ spawnBotsOnMapOnPlatform（EnvironmentManager.java:937）
        public static List<int> SpawnBotsOnMapOnPlatform(int numBots, int mapId, string platformId)
        {
            EnsureMap(mapId);
            var ids = new List<int>();
            foreach (var seat in PlatformSeats(mapId, platformId, numBots))
            {
                var bot = CreateBotWithRetry(seat, mapId, 5);
                if (bot != null) ids.Add(bot.id);
            }
            return ids;
        }

        // ≡ spawnBotsOnMapOnPlatformInRadius（EnvironmentManager.java:996）
        public static List<int> SpawnBotsOnMapOnPlatformInRadius(int numBots, int mapId, string platformId, BotPoint center, int radius)
        {
            EnsureMap(mapId);
            var ids = new List<int>();
            foreach (var seat in RadiusSeats(numBots, mapId, platformId, center, radius))
            {
                var bot = CreateBotWithRetry(seat, mapId, 5);
                if (bot != null) ids.Add(bot.id);
            }
            return ids;
        }

        // ≡ spawnFillerBots（EnvironmentManager.java:1058）
        public static List<int> SpawnFillerBots(int numBots, int mapId, BotPoint p1, BotPoint p2)
        {
            EnsureMap(mapId);
            var ids = new List<int>();
            foreach (var seat in FillerSeats(numBots, mapId, p1, p2))
            {
                var bot = CreateBotWithRetry(seat, mapId, 5);
                if (bot != null) ids.Add(bot.id);
            }
            return ids;
        }

        // 落点前确保地图几何已在 MapService（源端直接 getMapleMapById）——同步版用。
        static void EnsureMap(int mapId)
        {
            if (!MapService.Has(mapId)) GetMap?.Invoke(mapId);
        }

        // ≡ findUnoccupiedPointInRadius：100 次采样限制在半径内
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
    }

    // 非阻塞地图加载委托：
    //   Ready   —— 几何已登记（分片后台解析好或本来就有），可以立刻用；
    //   Pending —— 分片正在后台解析，调用方下一帧再问，主线程一帧都不等；
    //   Missing —— 包里根本没有这张图，当作无法生成（原端同样跳过）。
    public delegate BotMapState BotMapLoad(int mapId, out MapRuntime map, bool allowBlocking);

    public enum BotMapState { Ready, Pending, Missing }
}
