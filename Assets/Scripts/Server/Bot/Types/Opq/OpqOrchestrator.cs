// ≡ BotTypes/OPQ/OPQOrchestrator.java。一次 OPQ 跑图的协调者。
//
// 职责：
//   · 登记 / 注销 OPQ 假人；
//   · 给每个假人分配**互不重复**的云朵反应堆 / 箱子反应堆（最近的未占用活体）；
//   · 盯阶段通关条件（关卡 1 = 交付后关卡的 NPC 已出现；关卡 2 = 所有已注册假人都报了完成）；
//   · 跨图时把假人跟着队长送过去（源端 warpBotToLocation）；
//   · 每次跑图结束后复位黑板。
// 不负责：任何细粒度行为。编排器从不告诉假人「你该在哪个状态」，
// 假人自己按 mapId + 黑板读值决定状态（纯黑板模式）。
//
// 队长由 BotRuntime 注入真实队伍查询；调度使用可取消的 BotTasks。
using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Field;

namespace Server.Bot.Types.Opq
{
    public static class OpqOrchestrator
    {
        /// <summary>1Hz 足够：源端也是 1000ms。</summary>
        const long TickPeriodMs = 1000;

        public static readonly OpqSharedContext Context = new OpqSharedContext();

        static readonly List<BotCharacter> registered = new List<BotCharacter>();
        /// <summary>关卡 2 箱子按 x 从右到左排序后的下标（objectId -> 0..6）。</summary>
        static readonly Dictionary<int, int> boxSortedIndex = new Dictionary<int, int>();
        static BotTasks.Handle tickHandle;
        static long tickGeneration;

        /// <summary>
        /// 队长所在地图的解析器（botId -> mapId，-1/0 表示未知）。由主流程用
        /// BotPartyService.For(botId).leader → BotRegistry/玩家存储 接线；
        /// 未接线或队长不在线时返回未知，不以假人的进度代替队长。
        /// </summary>
        public static Func<int, int> LeaderMapOf;

        // ============================ 登记 ============================

        public static void Register(BotCharacter bot)
        {
            if (bot == null) return;
            for (int i = 0; i < registered.Count; i++)
                if (registered[i] != null && registered[i].id == bot.id) { EnsureTick(); return; }
            registered.Add(bot);
            EnsureTick();
        }

        public static void Unregister(BotCharacter bot)
        {
            if (bot == null) return;
            int botId = bot.id;
            registered.RemoveAll(b => b == null || b.id == botId || BotRegistry.Get(b.id) != b);
            Context.PutCloudAssignment(botId, null);
            Context.PutBoxAssignment(botId, null);
            Context.PutPlatformAssignment(botId, null);
            Context.ClearTaskComplete(botId);
            if (registered.Count == 0) StopTick();
        }

        /// <summary>假人每次观察到自己已进队时调用（源端 noteLeaderFromBot）：顺手把跑图激活。</summary>
        public static void NoteLeaderFromBot(BotCharacter bot)
        {
            if (bot == null || PartyOf(bot.id) <= 0) return;
            if (Context.IsPqActive) return;
            Context.SetPqActive(true);
            if (Context.CurrentPhase == OpqPhase.Inactive) Context.SetCurrentPhase(OpqPhase.Recruitment);
        }

        // ============================ 关卡 1 分配 ============================

        /// <summary>
        /// 给这个假人订一个独占的云朵反应堆（最近的未占用活体），返回 objectId。
        /// 已有分配且仍然活着就沿用；死掉的分配先摘掉再重挑。
        /// </summary>
        public static int? AssignCloudReactor(BotCharacter bot)
        {
            if (bot == null) return null;
            int botId = bot.id;
            var existing = Context.MyCloudAssignment(botId);
            if (existing.HasValue)
            {
                var current = MapReactorService.Find(bot.mapId, existing.Value);
                if (current != null && current.Hittable) return existing;
                Context.PutCloudAssignment(botId, null);
            }

            var taken = Context.CloudAssignmentValues;
            MapReactor best = null;
            double bestDistance = double.MaxValue;
            foreach (var reactor in MapReactorService.Get(bot.mapId))
            {
                if (reactor.id != OpqConstants.Stage1CloudReactorId) continue;
                if (!reactor.Hittable) continue;
                if (taken.Contains(reactor.objectId)) continue;
                double dx = bot.x - reactor.x, dy = bot.y - reactor.y;
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance) { bestDistance = distance; best = reactor; }
            }
            if (best == null) return null;
            Context.PutCloudAssignment(botId, best.objectId);
            return best.objectId;
        }

        /// <summary>这张图还有没有「活着且没被别的假人订走」的云朵；askingBotId 自己的那份照样算。</summary>
        public static bool HasUnclaimedLiveCloudReactor(int mapId, int askingBotId)
        {
            var taken = Context.CloudAssignmentValues;
            var mine = Context.MyCloudAssignment(askingBotId);
            foreach (var reactor in MapReactorService.Get(mapId))
            {
                if (reactor.id != OpqConstants.Stage1CloudReactorId) continue;
                if (!reactor.Hittable) continue;
                if (taken.Contains(reactor.objectId) && (!mine.HasValue || mine.Value != reactor.objectId)) continue;
                return true;
            }
            return false;
        }

        // ============================ 关卡 2 分配 ============================

        /// <summary>
        /// 给这个假人订一个独占的箱子反应堆。和源端一样跳过「被别人打过」的箱子
        /// （state &gt; 0 且不是自己订的），避免和真人抢同一个箱子。
        /// </summary>
        public static int? AssignBoxReactor(BotCharacter bot)
        {
            if (bot == null) return null;
            int botId = bot.id;
            BuildBoxSortedIndex(bot.mapId);

            var existing = Context.MyBoxAssignment(botId);
            if (existing.HasValue)
            {
                var current = MapReactorService.Find(bot.mapId, existing.Value);
                if (current != null && current.Hittable) return existing;
                Context.PutBoxAssignment(botId, null);
            }

            var taken = Context.BoxAssignmentValues;
            MapReactor best = null;
            double bestDistance = double.MaxValue;
            foreach (var reactor in MapReactorService.Get(bot.mapId))
            {
                if (!IsBoxReactor(reactor.id)) continue;
                if (!reactor.Hittable) continue;
                if (reactor.state > 0 && !taken.Contains(reactor.objectId)) continue;
                if (taken.Contains(reactor.objectId)) continue;
                double dx = bot.x - reactor.x, dy = bot.y - reactor.y;
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance) { bestDistance = distance; best = reactor; }
            }
            if (best == null) return null;
            Context.PutBoxAssignment(botId, best.objectId);
            return best.objectId;
        }

        public static bool HasUnclaimedLiveBoxReactor(int mapId, int askingBotId)
        {
            var taken = Context.BoxAssignmentValues;
            foreach (var reactor in MapReactorService.Get(mapId))
            {
                if (!IsBoxReactor(reactor.id)) continue;
                if (!reactor.Hittable) continue;
                if (reactor.state > 0 && !taken.Contains(reactor.objectId)) continue;
                if (taken.Contains(reactor.objectId)) continue;
                return true;
            }
            return false;
        }

        static bool IsBoxReactor(int reactorId)
            => reactorId >= OpqConstants.Stage2BoxReactorFirst && reactorId <= OpqConstants.Stage2BoxReactorLast;

        /// <summary>按 x 从右到左给箱子编号（最右 = 第 1 个），源端 buildBoxSortedIndex 同序。</summary>
        static void BuildBoxSortedIndex(int mapId)
        {
            if (boxSortedIndex.Count > 0) return;
            var boxes = new List<MapReactor>();
            foreach (var reactor in MapReactorService.Get(mapId))
                if (IsBoxReactor(reactor.id)) boxes.Add(reactor);
            boxes.Sort((a, b) => b.x.CompareTo(a.x));
            for (int i = 0; i < boxes.Count; i++) boxSortedIndex[boxes[i].objectId] = i;
        }

        /// <summary>箱子的第几个（喊话用）；编号表还没建起来/找不到 → "?"。</summary>
        public static string GetBoxOrdinal(int reactorObjectId)
        {
            if (boxSortedIndex.TryGetValue(reactorObjectId, out int index) &&
                index >= 0 && index < OpqConstants.Stage2BoxOrdinals.Length)
                return OpqConstants.Stage2BoxOrdinals[index];
            return "?";
        }

        /// <summary>这个箱子该吐哪张唱片：编号 i 的箱子固定吐 Stage2Items[i]。</summary>
        public static int GetBoxItemId(int reactorObjectId)
        {
            if (boxSortedIndex.TryGetValue(reactorObjectId, out int index) &&
                index >= 0 && index < OpqConstants.Stage2Items.Length)
                return OpqConstants.Stage2Items[index];
            return OpqConstants.Stage2Items[0];
        }

        // ============================ 通关判定 ============================

        /// <summary>
        /// 源端 isNpcPresent(leader.getMap(), CHAMBERLAIN_EAK)：关卡 1 交完云朵后 NPC 会被生成。
        /// 仅 NPC 已出现才能通关；云朵全破不等于已交付祭坛。
        /// </summary>
        public static bool IsChamberlainSpawned()
        {
            var map = MapService.Get(OpqConstants.Stage1);
            if (map == null) return false;
            foreach (var npc in map.npcs)
                if (npc.id == OpqConstants.ChamberlainEak) return true;
            return false;
        }

        // ============================ 跨图 / 相位 ============================

        /// <summary>
        /// 源端 followLeaderWarp(fakechar, pt)：把假人送到「队长所在地图」的这个点上。
        /// 队长地图解析不出来时返回 false，假人下一 tick 再试。
        /// </summary>
        public static bool FollowLeaderWarp(BotCharacter bot, int x, int y)
        {
            int mapId = LeaderMap(bot);
            if (mapId <= 0) return false;
            return WarpTo(bot, mapId, x, y);
        }

        /// <summary>
        /// 源端 warpBotToLocation：落点前先把目标图的 MapRuntime 装好（没有几何就不能传送），
        /// 再改 mapId + 坐标。地图加载失败按「这张图暂时去不了」处理，不抛给调度器。
        /// </summary>
        public static bool WarpTo(BotCharacter bot, int mapId, int x, int y)
        {
            if (bot == null || mapId <= 0) return false;
            if (bot.mapId != mapId)
            {
                var load = BotEnvironment.GetMap;
                try
                {
                    var runtime = load != null ? load(mapId) : MapService.Get(mapId);
                    if (runtime == null) return false;
                }
                catch (ArgumentException) { return false; }
                BotRegistry.EnterMap(bot, mapId);
            }
            BotMovementService.Stop(bot);
            bot.x = x;
            bot.y = MapService.SnapToGround(mapId, x, y);
            return true;
        }

        /// <summary>假人观测到自己处在某阶段时，把相位镜像进黑板（源端 mirrorPhase）。</summary>
        public static void MirrorPhase(OpqPhase phase)
        {
            if (Context.CurrentPhase == phase) return;
            Context.SetCurrentPhase(phase);
        }

        /// <summary>新一轮跑图（源端 resetForNewRun）：清黑板、激活、回到招募相位。</summary>
        public static void ResetForNewRun()
        {
            Context.Reset();
            Context.SetPqActive(true);
            Context.SetCurrentPhase(OpqPhase.Recruitment);
            boxSortedIndex.Clear();
        }

        /// <summary>硬停（源端 shutdownRun）：黑板全清，假人下一 tick 回 RECRUITMENT。</summary>
        public static void ShutdownRun()
        {
            Context.Reset();
            boxSortedIndex.Clear();
        }

        // ============================ 队长解析 ============================

        /// <summary>队长现在在哪张图；解析不出来返回 -1。</summary>
        public static int LeaderMap(BotCharacter bot)
        {
            if (bot == null) return -1;
            return LeaderMapOf?.Invoke(bot.id) ?? -1;
        }

        public static int PartyOf(int botId) => MapItemService.PartyOf != null ? MapItemService.PartyOf(botId) : -1;

        // ============================ 内部 tick ============================

        static void EnsureTick()
        {
            if (tickHandle != null && !tickHandle.Cancelled && !tickHandle.Completed) return;
            tickGeneration++;
            long generation = tickGeneration;
            tickHandle = BotTasks.ScheduleFixedDelay(() => SafeTick(generation), TickPeriodMs, TickPeriodMs);
        }

        static void StopTick()
        {
            tickGeneration++;
            tickHandle?.Cancel();
            tickHandle = null;
        }

        static void SafeTick(long generation)
        {
            // 停表后残留的回调一律丢弃（BotTasks 的 Cancel 只保证不再排下一轮）。
            if (generation != tickGeneration) return;
            Tick();
        }

        static void Tick()
        {
            Prune();
            if (registered.Count == 0) { StopTick(); return; }
            if (!Context.IsPqActive) return;

            if (AllBotsLostParty())
            {
                ResetForNewRun();
                return;
            }

            switch (Context.CurrentPhase)
            {
                case OpqPhase.Stage1:
                    if (!Context.IsStage1Complete && IsChamberlainSpawned())
                    {
                        Context.SetStage1Complete(true);
                        ClearCompletionFlags();
                    }
                    break;
                case OpqPhase.Stage2:
                    if (!Context.IsStage2Complete && AllRegisteredBotsComplete())
                    {
                        Context.SetStage2Complete(true);
                        ClearCompletionFlags();
                    }
                    break;
            }
        }

        /// <summary>丢掉已经下线/换过实例的假人（源端靠 unregisterBot，这里再兜一层）。</summary>
        static void Prune()
        {
            for (int i = registered.Count - 1; i >= 0; i--)
            {
                var bot = registered[i];
                if (bot == null || BotRegistry.Get(bot.id) != bot) registered.RemoveAt(i);
            }
        }

        static bool AllBotsLostParty()
        {
            if (registered.Count == 0) return false;
            foreach (var bot in registered) if (PartyOf(bot.id) > 0) return false;
            return true;
        }

        static bool AllRegisteredBotsComplete()
        {
            if (registered.Count == 0) return false;
            foreach (var bot in registered) if (!Context.IsMyTaskComplete(bot.id)) return false;
            return true;
        }

        static void ClearCompletionFlags()
        {
            foreach (var bot in registered) Context.ClearTaskComplete(bot.id);
        }
    }
}
