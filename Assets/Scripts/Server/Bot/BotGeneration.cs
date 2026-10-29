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
                ApplyIdentity(bot, profile);
                return;
            }
            var template = BotAppearanceTemplates.Take(rng);
            if (template != null) { template.ApplyTo(bot); return; }
            decorator ??= new Decorator.BotDecorate(BotResources.Load(), BotItemCatalog.Shared, BotTypeManager.Output);
            decorator.ApplyAppearance(bot, Db);
        }

        // 档案的等级/职业优先于外观模板：模板只提供脸/发型/衣服（共享纹理），
        // 身份（等级、职业）来自账号 —— 和联机一致，也才能做到「每个职业每个等级都有人」。
        // 老库/关掉覆盖模式的行 level=0，走模板自带身份（行为与改造前一致）。
        static void ApplyIdentity(BotCharacter bot, BotProfile profile)
        {
            if (profile.level <= 0) return;
            bot.level = Math.Max(1, Math.Min(200, profile.level));
            bot.job = Math.Max(0, profile.job);
            bot.tier = TierForLevel(bot.level);
            AlignWeapon(bot);
        }

        // 等级段决定档位（源端 tier 是装饰用的稀有度，不影响战斗数值）。
        static BotTier TierForLevel(int level)
            => level >= 180 ? BotTier.S : level >= 160 ? BotTier.A : level >= 140 ? BotTier.B : level >= 120 ? BotTier.C : BotTier.D;

        // 武器必须跟职业同系：BotAttack 的挥击姿势、GrindSkill 的技能回退都按武器类别分派，
        // 战士拿着弓会做出弓的挥击动作。模板的武器是随机抽的，这里按职业纠正一次。
        static void AlignWeapon(BotCharacter bot)
        {
            int want = WeaponStyleOfJob(bot.job);
            if (want == 0) return;                       // 新手：沿用模板/默认武器
            bot.equips.TryGetValue(-11, out int current);
            if (StyleOfWeapon(current) == want) return;
            int id = DefaultWeapon(want);
            if (id <= 0) return;
            try
            {
                var item = BotItemCatalog.Shared.CleanItem(id);
                item.position = -11;
                bot.equipment[-11] = item;
                bot.equips[-11] = id;
            }
            catch (Exception) { /* 物品表缺这条就保留原武器，别让生成失败 */ }
        }

        static int StyleOfWeapon(int itemId) => (itemId / 10000) switch
        {
            130 or 131 or 140 or 141 or 143 or 144 => 100,
            137 or 138 => 200,
            145 or 146 => 300,
            133 or 147 => 400,
            148 or 149 => 500,
            _ => 0,
        };

        static int WeaponStyleOfJob(int job) => (job / 100) switch
        {
            1 => 100, 2 => 200, 3 => 300, 4 => 400, 5 => 500, _ => 0,
        };

        // 同系各一把标准武器（v83 一转赠品那一批），保证纹理不因等级再翻倍。
        static int DefaultWeapon(int style) => style switch
        {
            100 => 1302000,   // 剑
            200 => 1372000,   // 短杖
            300 => 1452000,   // 弓
            400 => 1332000,   // 短刀
            500 => 1482000,   // 指虎
            _ => 0,
        };

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
