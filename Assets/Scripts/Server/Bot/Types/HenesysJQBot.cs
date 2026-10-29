using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Field;

namespace Server.Bot.Types
{
    // ≡ BotTypes/HenesysJQBot.java（308 行）。宠物公园（100000202）里反复挑战跳跃任务的假人：
    // 掷难度 → 播一段对应难度的录制 → 看是不是还站在起点决定「再来一次」还是「歇一会」，
    // 登顶后 3/4 概率继续刷、1/4 概率走出口回射手村公园并转成 HENESYS_BOT。
    //
    // 与源端的差异（都是「不能阻塞主线程」的直接后果）：
    //  1. 源端 sleepAmountSeconds 是阻塞 sleep；这里换成本地截止时间戳 sleepUntil，
    //     由下一次 tick 判断是否已过 —— 每段等待因此多出一个 tick 周期（2~6s）。
    //  2. 源端 BotMoveStream / pathFinderAware 都是阻塞调用；这里分别用
    //     BotMovementPlayback.Play(finished 回调) 与 BotMovementService.Move + IsMoving 轮询表达，
    //     播放/行走期间 FSM 不推进（等价于源端那根线程被占住）。
    //  3. 同一个状态里的两段连续 sleep（REST、NAVIGATE_TO_EXIT）合并成一段，少占一个 tick。
    public sealed class HenesysJQBot : BotStateMachine
    {
        // ≡ JQState
        public enum JQState { Reset, NavigateToJQ, AttemptJQ, Recover, Rest, NavigateToExit, Convert }

        // ≡ 常量表
        const int PetParkMap = 100000202;
        const int HenesysParkMap = 100000200;
        const int ExitPortalId = 5;
        const int ExitDestPortalId = 13;
        const int JqStartX = -1005, JqStartY = 274;
        const int NearStartThresholdX = 60;
        const int NearStartThresholdY = 20;

        // ≡ TIER_RECORDINGS：tier(1~7) → 该难度下的录制名（随机挑一个）。全是 map100000202 下的 .bin。
        static readonly string[][] TierRecordings =
        {
            new[] { "jq1fail", "jq1afail" },                                                     // Tier 1
            new[] { "jq2fail", "jq2afail" },                                                     // Tier 2
            new[] { "jq3fail", "jq3afail" },                                                     // Tier 3
            new[] { "jq4fail", "jq4afail" },                                                     // Tier 4
            new[] { "jq5fail", "jq5afail" },                                                     // Tier 5
            new[] { "jq6fail", "jq6afail", "jq6bfail", "jq6cfail", "jq6dfail", "jq6efail" },     // Tier 6
            new[] { "jq7top" },                                                                  // Tier 7
        };

        static readonly int[] TierWeights = { 20, 18, 16, 14, 12, 8, 4 };
        static readonly int[] ExperiencedTierWeights = { 0, 0, 0, 0, 15, 25, 60 };

        const double ContinueJqChance = 0.75;

        const string DialoguePack = "JQBotDialogue.yaml";
        const string DialogueType = "JQBot";

        readonly Random random;
        readonly BotDialogue dialogue;

        readonly List<BotTasks.Handle> delayed = new List<BotTasks.Handle>();
        long sleepUntil;                 // ≡ sleepAmountSeconds 的终点
        long navigateStartedAt;
        int restPhase, exitPhase;
        bool attempting, navigateArrived, converted;
        int highestCompletedTier;
        bool hasReachedTop, lastAttemptSuccess;

        public JQState jqState { get; private set; } = JQState.Reset;

        public HenesysJQBot(BotCharacter actor, BotDialogue dialogue = null, Random random = null) : base(actor)
        {
            this.dialogue = dialogue ?? BotTypeManager.Dialogue;
            this.random = random ?? new Random();
            botType = "JQBot";
        }

        public override void UpdateState()
        {
            // ≡ 源端 BotMoveStream 阻塞：录制播放期间那根线程正卡在播放里，
            // 连基类 tick 都不跑（否则基类会按 stance 4/5 发 idle 包，打断跳跃动作）。
            if (attempting) return;
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || chr == null || converted) return;
            if (BotScheduler.Now() < sleepUntil) return;        // ≡ sleepAmountSeconds

            switch (jqState)
            {
                case JQState.Reset:
                    Sleep(2000 + random.Next(15000));
                    jqState = JQState.NavigateToJQ;
                    break;
                case JQState.NavigateToJQ:
                    NavigateToJqStart();
                    break;
                case JQState.AttemptJQ:
                    AttemptJq();
                    break;
                case JQState.Recover:
                    Recover();
                    break;
                case JQState.Rest:
                    RestAndDecide();
                    break;
                case JQState.NavigateToExit:
                    NavigateToExit();
                    break;
                case JQState.Convert:
                    ConvertToHenesysBot();
                    break;
            }
        }

        // ≡ navigateToJQStart：源端 pathFinderAware 阻塞走到起点；这里 Move + 轮询 IsMoving
        // （走丢/被卡住时移动系统会把 target 清掉，IsMoving 变成 false，不会永远卡在这一态）。
        void NavigateToJqStart()
        {
            if (NearJqStart()) { jqState = JQState.AttemptJQ; return; }
            if (navigateStartedAt == 0)
            {
                navigateStartedAt = BotScheduler.Now();
                BotMovementService.Move(chr, JqStartX, JqStartY, () => navigateArrived = true);
                return;
            }
            if (!navigateArrived && BotMovementService.IsMoving(chr)) return;
            navigateStartedAt = 0; navigateArrived = false;
            jqState = JQState.AttemptJQ;
        }

        // ≡ attemptJQ：掷难度 → 挑录制 → 可能说话/中途说话 → 播录制 → 记最高难度
        void AttemptJq()
        {
            int tier = RollNextTier();
            string[] variants = TierRecordings[tier - 1];
            string recording = variants[random.Next(variants.Length)];
            lastAttemptSuccess = tier == 7;

            if (OneIn(3)) Delay(500 + random.Next(2000), () => ChatLine("AttemptStart"));
            if (OneIn(3)) ScheduleMidJqChat();

            // 源端在这里 sleep(200~700) 再开播；换成排一次延时任务，主线程不等。
            attempting = true;
            jqState = JQState.Recover;                          // 源端：attemptJQ() 之后直接进 RECOVER
            Delay(200 + random.Next(500), () => PlayRecording(recording, tier));
        }

        // ≡ BotMoveStream(mvr, chr)：off 偏移（录制里是地图绝对坐标），播完由回调续上 FSM。
        void PlayRecording(string recording, int tier)
        {
            bool started = false;
            try
            {
                byte[] bytes = BotResources.Load().ReadBytes(
                    BotMovementRecording.ResourcePath(PetParkMap, recording, "bin"));
                if (bytes == null)
                {
                    // 录制缺失：跳过这次尝试，绝不让异常打断调度（Server 层没有 Debug，用 BotPerf 计数）
                    BotPerf.Record("JQBot.MissingRecording", 0);
                    return;
                }
                BotMovementPlayback.Play(chr, BotMovementRecording.ReadBinary(bytes), false,
                    stopped => OnAttemptFinished(tier));
                started = true;
            }
            catch (Exception)
            {
                BotPerf.Record("JQBot.PlaybackError", 0);
            }
            finally
            {
                // Play 可能同步回调（空录制/立刻中断），此时 attempting 已由 OnAttemptFinished 清掉
                if (!started && attempting) OnAttemptFinished(tier);
            }
        }

        void OnAttemptFinished(int tier)
        {
            attempting = false;
            highestCompletedTier = tier;                        // ≡ highestCompletedTier = selectedTier（播完才记）
            Sleep(500 + random.Next(1000));
        }

        // ≡ recover：成功 → 登顶散开；失败 → 还在起点就再来一次，否则去歇着
        void Recover()
        {
            if (lastAttemptSuccess)
            {
                hasReachedTop = true;
                ChatLine("SuccessReaction");
                DoEmote();
                Sleep(500 + random.Next(1000));
                // 成功后随机散开，别让登顶的人全叠在同一个点上
                BotMovementService.Move(chr, -1810 + random.Next(693), 274);
                jqState = JQState.Rest;
                return;
            }

            if (!NearJqStart())
            {
                ChatLine("FailReaction");
                if (OneIn(3)) DoEmote();
                Sleep(1000 + random.Next(2000));
                jqState = JQState.Rest;
                return;
            }
            // 还在起点，直接再来一次
            if (OneIn(3)) ChatLine("FailReaction");
            if (OneIn(5)) DoEmote();
            jqState = JQState.AttemptJQ;
        }

        // ≡ restAndDecide：歇口气 → 登顶过就 3/4 继续刷、否则继续
        void RestAndDecide()
        {
            if (restPhase == 0)
            {
                restPhase = 1;
                if (OneIn(3)) ChatLine("RestChat");
                if (OneIn(5)) DoEmote();
                // 源端两段 sleep（500~1500 与 1000~2500）合成一段，少占一个 tick
                Sleep(500 + random.Next(1000) + 1000 + random.Next(1500));
                return;
            }

            restPhase = 0;
            if (hasReachedTop)
            {
                jqState = random.NextDouble() < ContinueJqChance ? JQState.NavigateToJQ : JQState.NavigateToExit;
                return;
            }
            jqState = JQState.NavigateToJQ;
        }

        // ≡ rollNextTier：登顶过改用「老手权重」，否则把已完成的难度权重清零（只往上爬）
        int RollNextTier()
        {
            if (hasReachedTop) return RollWeighted(ExperiencedTierWeights);

            var adjusted = new int[TierWeights.Length];
            for (int i = 0; i < TierWeights.Length; i++)
                adjusted[i] = (i + 1 > highestCompletedTier) ? TierWeights[i] : 0;
            return RollWeighted(adjusted);
        }

        int RollWeighted(int[] weights)
        {
            int total = 0;
            foreach (int w in weights) total += w;
            if (total == 0) return 7;

            int roll = random.Next(total), cumulative = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative) return i + 1;
            }
            return 7;
        }

        bool NearJqStart()
            => Math.Abs(chr.x - JqStartX) <= NearStartThresholdX && Math.Abs(chr.y - JqStartY) <= NearStartThresholdY;

        // ≡ scheduleMidJQChat：一次尝试中途最多插 2 句自言自语（20~30s 后）
        void ScheduleMidJqChat()
        {
            int count = random.Next(3);
            for (int i = 0; i < count; i++)
                Delay(20000 + random.Next(10000), () => ChatLine("MidJQ"));
        }

        // ≡ navigateToExit：走到出口门 → 等一会 → botWarpMapOnPortal(100000200, portal 13)
        // 源端是一次阻塞调用里连做三段，这里拆成三个相位，每相位结束排一段 Sleep。
        void NavigateToExit()
        {
            switch (exitPhase)
            {
                case 0:
                    ChatLine("LeavingJQ");
                    var portal = FindPortal(ExitPortalId);
                    if (portal == null) { exitPhase = 0; jqState = JQState.Convert; return; }
                    exitPhase = 1; navigateArrived = false;
                    BotMovementService.Move(chr, portal.x, portal.y, () => navigateArrived = true);
                    return;
                case 1:
                    if (!navigateArrived && BotMovementService.IsMoving(chr)) return;
                    BotMovementService.Stop(chr);
                    // 源端到门口后有两段 sleep(1000~2000)，这里合成一段
                    Sleep(2000 + random.Next(1000) + random.Next(1000));
                    exitPhase = 2;
                    return;
                case 2:
                    WarpToExit();
                    Sleep(1000);
                    exitPhase = 3;
                    return;
                default:
                    exitPhase = 0;
                    jqState = JQState.Convert;
                    return;
            }
        }

        // ≡ botWarpMapOnPortal(chr, map100000200, 13)。本工程没有独立的 WarpMap 入口，
        // 换图能力挂在 BotMovementService.EnterPortal（BotRuntime 注入，按 portal.targetName 落点）——
        // 100000202 的 portal 5 是 out00 → tm=100000200 / tn=in01，落点正是 100000200 的 portal 13。
        void WarpToExit()
        {
            var portal = FindPortal(ExitPortalId);
            var enter = BotMovementService.EnterPortal;
            // 目的地图 = 射手村公园（≡ HENESYS_PARK_MAP）；落点由 portal.targetName(in01) 解析成目的地
            // 的 portal 13（≡ EXIT_DEST_PORTAL_ID），两张图的门名/门号已在 map 数据里核对过。
            if (portal == null || enter == null || portal.targetMap != HenesysParkMap)
            {
                BotPerf.Record("JQBot.WarpUnavailable", 0);     // 没有传送能力：留在原地转 HENESYS_BOT
                return;
            }
            try { enter(chr, portal); }
            catch (Exception) { BotPerf.Record("JQBot.WarpError", 0); }
        }

        // ≡ convertToHenesysBot
        void ConvertToHenesysBot()
        {
            converted = true;
            BotTypeManager.ConvertBotType(chr, BotType.HENESYS_BOT);
            if (BotStore.GetBotById(chr.id) is HenesysBot henesys) henesys.SetLastJqConversionTime(BotScheduler.Now());
        }

        MapPortal FindPortal(int id)
        {
            var map = MapService.Get(chr.mapId);
            if (map == null) return null;
            foreach (var portal in map.portals) if (portal.id == id) return portal;
            return null;
        }

        // ≡ chatLine：对白节点缺失/解析失败时静默跳过（源端同样吞异常）
        void ChatLine(string node)
        {
            try
            {
                string line = dialogue.RandomLine(DialoguePack, DialogueType, node);
                if (line != null) output.Speak(chr, line);
            }
            catch (Exception) { }
        }

        // ≡ doEmote
        void DoEmote() => output.Emote(chr, 1 + random.Next(7));

        // 源端 rollChanceInverse(n) = 1/n 命中
        bool OneIn(int n) => random.Next(n) == 0;

        void Sleep(long ms) => sleepUntil = BotScheduler.Now() + Math.Max(0, ms);

        // ≡ MethodScheduler.runAfterDelay：BotTasks.Schedule 排一次可取消的延时回调。
        // 回调前先确认这个 bot 还是当前注册的那个（可能已经转换类型/下线），避免转换后继续说 JQ 的话。
        void Delay(long ms, Action action)
        {
            BotTasks.Handle handle = null;
            handle = BotTasks.Schedule(() => { delayed.Remove(handle); if (Alive()) action(); }, ms);
            delayed.Add(handle);
        }

        bool Alive() => running && chr.online && BotRegistry.Contains(chr.id) && ReferenceEquals(BotStore.GetBotById(chr.id), this);

        public override void StopScheduledTask()
        {
            foreach (var handle in delayed) handle.Cancel();
            delayed.Clear();
            BotMovementPlayback.Cancel(chr);
            BotMovementService.Stop(chr);
            attempting = false;
            navigateStartedAt = 0; navigateArrived = false;
            base.StopScheduledTask();
        }

        public override void DisplayCommands(BotActor player) => output.ShowOptions(player, new[] { chr.name });
    }
}
