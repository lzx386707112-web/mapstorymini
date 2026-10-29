using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Bot.Types.Opq;
using Server.Field;

namespace Server.Bot.Types
{
    // ≡ BotTypes/OPQ/OPQBot.java（995 行）。
    //
    // 设计（照源端）：
    //  · 感知驱动：假人只根据自己看得到的东西（当前 mapId / 坐标 / 背包 / 地面掉落）
    //    加黑板读值决定下一步，从不接受别的假人的命令。
    //  · 换图是权威信号：被游戏传送后 mapId 一变，detectPhaseFromMap 把状态重新对齐到该阶段的入口状态；
    //    但同一阶段内部不倒退（STAGE_1_* 不会因为还在 920010000 就被拽回 NAVIGATE）。
    //  · 每个阶段是「导航 → 砸反应堆 → 捡 → 回交 → 投放 → 等待」这套循环；
    //    等 orchestrator 或换图把阶段推走。
    //  · 所有等待都有 STAGE_WAIT_TIMEOUT_MS 上限，卡住的假人最终落到 LOOP_CHECK 而不是挂死。
    //
    // 与源端的差异（本工程缺少的依赖，逐条对应源端调用点）：
    //  1. 源端 sleepAmountSeconds / Thread.sleep 是阻塞；这里换成本地截止时间戳 + BotTasks 的延时链
    //     （砸 4 下 = 700ms 间隔的 BotTasks 链，FSM 不阻塞）。代价是每段等待至少多一个 tick。
    //  2. 源端 BotAttack.basicSwing / CustomReactor.hitReactor 直接改 Map 上的 Reactor；
    //     这里改为 MapReactorService.Hit（同样的「state+1，到 4 破坏并掉落」语义）。
    //  3. 源端 warpBotToLocation 由 ChannelServer.getMapFactory 提供；这里走 OpqOrchestrator.WarpTo
    //     （BotEnvironment.GetMap 装图 + BotRegistry.EnterMap）。
    //  4. 队伍邀请通过 BotRuntime.Parties 接受，队长地图由真实队伍服务注入。
    public sealed class OpqBot : BotStateMachine
    {
        // ≡ OPQBot.OPQBotState。中文注释对应源端枚举项。
        public enum OpqState
        {
            Reset,                    // RESET：清空一次跑图的暂存
            Recruitment,              // RECRUITMENT：大厅喊话招募
            InPartyIdle,              // IN_PARTY_IDLE：已进队，等队长开副本
            Stage1Navigate,           // STAGE_1_NAVIGATE：走向分到的云朵反应堆
            Stage1HitReactor,         // STAGE_1_HIT_REACTOR：砸 4 下云朵
            Stage1Loot,               // STAGE_1_LOOT：捡云朵碎片
            Stage1Return,             // STAGE_1_RETURN：走回交任务点
            Stage1DropItems,          // STAGE_1_DROP_ITEMS：把云朵碎片扔在地上
            Stage1Wait,               // STAGE_1_WAIT：等关卡 1 通关标志
            Stage1Transition,         // STAGE_1_TRANSITION：跟队长去中转塔
            Stage1TransitionPt2,      // STAGE_1_TRANSITION_PT_2：在中转塔等队长进关卡 2
            Stage2Navigate,           // STAGE_2_NAVIGATE：走向分到的箱子
            Stage2HitBox,             // STAGE_2_HIT_BOX：砸 4 下箱子
            Stage2Loot,               // STAGE_2_LOOT：捡唱片
            Stage2Return,             // STAGE_2_RETURN：走回音乐盒投放点
            Stage2DropItems,          // STAGE_2_DROP_ITEMS：把唱片扔在音乐盒
            Stage2Wait,               // STAGE_2_WAIT：等关卡 2 通关/队长离开
            ExitDetect,               // EXIT_DETECT：判断该去哪（出口大厅 / 直接回大厅）
            ExitLobby,                // EXIT_LOBBY：出口大厅 → 回招募大厅
            LoopCheck,                // LOOP_CHECK：一轮结束，按是否还在队伍决定下一轮
        }

        const long NavigateSettleMs = OpqConstants.NavigateSettleMs;
        const int HitRange = OpqConstants.ReactorHitRangePx;

        readonly Random random;
        public OpqState opqState { get; private set; } = OpqState.Reset;
        public string lastTransitionReason { get; private set; } = "";

        // 每次跑图的暂存（≡ 源端同名私有字段）
        long stageWaitStartTime;
        long lastRecruitAt;
        int reactorHitsThisTarget;
        int cloudPiecesLooted;
        int lootedRecordItemId = -1;
        int announcedBoxOid;

        // 非阻塞版 sleepAmountSeconds：截止时间戳；phaseStep 用来把「说一句 → 等一下 → 再动手」拆成多个 tick。
        long sleepUntil;
        int phaseStep;
        long navigateStartedAt;

        // 4 连击的延时链（源端是一个 for + Thread.sleep 的阻塞循环）
        BotTasks.Handle swingTask;
        int swingToken;

        public OpqBot(BotCharacter chr, Random random = null) : base(chr)
        {
            this.random = random ?? new Random();
            botType = "OPQBot";
            OpqOrchestrator.Register(chr);
        }

        public static bool IsInsidePq(int mapId)
            => mapId == OpqConstants.Stage1 || mapId == OpqConstants.Tower
            || mapId == OpqConstants.Stage2 || mapId == OpqConstants.ExitLobby;

        public override bool IsAvailableForAmbientActions() => opqState == OpqState.Recruitment;

        // PQ 有节奏要求（砸一下 700ms、等待 800ms），不能因为图上没真人被底类降到 10s 一轮。
        protected override long DefaultPeriodMs() => 1000;
        public override void CheckPrioritySpeed() { }

        public override void StartScheduledTask(long initialDelayMs)
        {
            OpqOrchestrator.Register(chr);
            base.StartScheduledTask(initialDelayMs);
        }

        public override void StopScheduledTask()
        {
            CancelSwing();
            BotMovementService.Stop(chr);
            BotMovementPlayback.Cancel(chr);
            OpqOrchestrator.Unregister(chr);
            base.StopScheduledTask();
        }

        /// <summary>≡ setStateForDebug（源端 !opq forcestate）。</summary>
        public void SetStateForDebug(OpqState state) => TransitionTo(state, "debug forced");

        // =========================================================================
        // 主 tick
        // =========================================================================

        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || chr == null) return;

            // 队伍没了还留在副本里 = PQ 作废（源端 handlePQAbandoned）
            if (IsInsidePqState() && !IsInParty())
            {
                HandlePqAbandoned();
                return;
            }

            // 换图是权威信号：被传送到别的阶段就重新对齐入口状态
            var derived = DetectPhaseFromMap();
            if (derived.HasValue && derived.Value != opqState && !InSameStageFamily(derived.Value, opqState))
                TransitionTo(derived.Value, "map-derived rehome (mapId=" + chr.mapId + ")");

            switch (opqState)
            {
                case OpqState.Reset: ResetOpqState(); TransitionTo(OpqState.Recruitment, "RESET 完成，开始招募"); break;
                case OpqState.Recruitment: HandleRecruitment(); break;
                case OpqState.InPartyIdle: HandleInPartyIdle(); break;
                case OpqState.Stage1Navigate: HandleStage1Navigate(); break;
                case OpqState.Stage1HitReactor: HandleStage1HitReactor(); break;
                case OpqState.Stage1Loot: HandleStage1Loot(); break;
                case OpqState.Stage1Return: HandleStage1Return(); break;
                case OpqState.Stage1DropItems: HandleStage1DropItems(); break;
                case OpqState.Stage1Wait: HandleStage1Wait(); break;
                case OpqState.Stage1Transition: HandleStage1Transition(); break;
                case OpqState.Stage1TransitionPt2: HandleStage1TransitionPart2(); break;
                case OpqState.Stage2Navigate: HandleStage2Navigate(); break;
                case OpqState.Stage2HitBox: HandleStage2HitBox(); break;
                case OpqState.Stage2Loot: HandleStage2Loot(); break;
                case OpqState.Stage2Return: HandleStage2Return(); break;
                case OpqState.Stage2DropItems: HandleStage2DropItems(); break;
                case OpqState.Stage2Wait: HandleStage2Wait(); break;
                case OpqState.ExitDetect: HandleExitDetect(); break;
                case OpqState.ExitLobby: HandleExitLobby(); break;
                case OpqState.LoopCheck: HandleLoopCheck(); break;
                default:
                    // 源端 default：记日志、置 FINISHED、复位
                    CancelSwing();
                    state = State.FINISHED;
                    ResetOpqState();
                    break;
            }
        }

        // =========================================================================
        // 状态机管道
        // =========================================================================

        void TransitionTo(OpqState next, string reason)
        {
            if (opqState == next) return;
            opqState = next;
            lastTransitionReason = reason;
            phaseStep = 0;
            navigateStartedAt = 0;
            CancelSwing();
        }

        void ResetOpqState()
        {
            opqState = OpqState.Reset;
            lastRecruitAt = 0;
            ResetScratch();
        }

        void ResetScratch()
        {
            stageWaitStartTime = 0;
            reactorHitsThisTarget = 0;
            cloudPiecesLooted = 0;
            lootedRecordItemId = -1;
            announcedBoxOid = 0;
            navigateStartedAt = 0;
            phaseStep = 0;
            sleepUntil = 0;
        }

        // =========================================================================
        // 阶段：招募 / 待命
        // =========================================================================

        void HandleRecruitment()
        {
            // 源端 BotPartyLogic.checkPartyQueue：招募阶段先处理队伍邀请。
            var parties = BotTypeManager.Parties;
            var invitation = parties?.Pending(chr.id);
            if (invitation != null) parties.Answer(chr, invitation.party, true);
            if (IsInParty())
            {
                OpqOrchestrator.NoteLeaderFromBot(chr);
                OpqOrchestrator.MirrorPhase(OpqPhase.InPartyIdle);
                TransitionTo(OpqState.InPartyIdle, "已经进队");
                return;
            }

            long now = BotScheduler.Now();
            if (now - lastRecruitAt < OpqConstants.RecruitMessageIntervalMs) return;
            lastRecruitAt = now;

            output.Speak(chr, OpqRecruitMessages.Generate(chr, random));
            WanderToRandomSpot();
        }

        void HandleInPartyIdle()
        {
            if (!IsInParty())
            {
                TransitionTo(OpqState.Recruitment, "开副本前队伍就散了");
                return;
            }
            OpqOrchestrator.NoteLeaderFromBot(chr);

            // 队长已经进关卡 1：等 3 秒跟着进（源端 sleepAmountSeconds(3000) + followLeaderWarp）
            if (LeaderMap() != OpqConstants.Stage1) return;

            if (phaseStep == 0)
            {
                ResetStageScratch();
                Sleep(3000);
                phaseStep = 1;
                return;
            }
            if (StillSleeping()) return;

            if (OpqOrchestrator.FollowLeaderWarp(chr, OpqConstants.Stage1EntryX, OpqConstants.Stage1EntryY))
                TransitionTo(OpqState.Stage1Navigate, "跟着队长进关卡 1");
        }

        // =========================================================================
        // 阶段：关卡 1（云朵）
        // =========================================================================

        void HandleStage1Navigate()
        {
            OpqOrchestrator.MirrorPhase(OpqPhase.Stage1);
            if (StillSleeping()) return;

            int? reactorOid = OpqOrchestrator.AssignCloudReactor(chr);

            // 云朵已经全破（或全被别的假人订走）：不再抢，回投放点
            if (!OpqOrchestrator.HasUnclaimedLiveCloudReactor(chr.mapId, chr.id))
            {
                OpqOrchestrator.Context.PutCloudAssignment(chr.id, null);
                TransitionTo(OpqState.Stage1Return, "没有剩余云朵 → 回交");
                return;
            }
            if (!reactorOid.HasValue) return;   // 还没分到，下一 tick 再试

            var reactor = MapReactorService.Find(chr.mapId, reactorOid.Value);
            if (reactor == null || !reactor.Hittable)
            {
                OpqOrchestrator.Context.PutCloudAssignment(chr.id, null);
                return;   // 陈旧分配：摘掉重新挑
            }

            if (navigateStartedAt == 0)
            {
                MoveTo(reactor.x, reactor.y);
                navigateStartedAt = BotScheduler.Now();
                return;
            }
            if (BotScheduler.Now() - navigateStartedAt < NavigateSettleMs) return;

            if (Math.Abs(chr.x - reactor.x) <= HitRange)
            {
                reactorHitsThisTarget = 0;
                TransitionTo(OpqState.Stage1HitReactor, "已到云朵射程内 (oid=" + reactorOid.Value + ")");
            }
            else
            {
                MoveTo(reactor.x, reactor.y);   // 还没到，继续走
                navigateStartedAt = BotScheduler.Now();
            }
        }

        void HandleStage1HitReactor()
        {
            if (swingTask != null) return;   // 4 连击的延时链还在跑
            reactorHitsThisTarget = 0;
            StartSwingChain(false);
        }

        void HandleStage1Loot()
        {
            if (StillSleeping()) return;

            var reactorOid = OpqOrchestrator.Context.MyCloudAssignment(chr.id);
            var reactor = reactorOid.HasValue ? MapReactorService.Find(chr.mapId, reactorOid.Value) : null;

            // 1) 主扫：假人脚下（刚破的那个云朵位置）
            var found = BotLogic.CheckForItemsOnFloor(chr.mapId, (int)chr.x, (int)chr.y,
                OpqConstants.LootScanRangeSq, OpqConstants.Stage1Items);

            // 2) 兜底：云朵可能穿透好几层平台，按反应堆 x 逐层往下探
            if (found.Count == 0 && reactor != null)
            {
                for (int step = 1; step <= OpqConstants.LootFallbackSteps; step++)
                {
                    int probeY = reactor.y + step * OpqConstants.LootFallbackStepPx;
                    var hits = BotLogic.CheckForItemsOnFloor(chr.mapId, reactor.x, probeY,
                        OpqConstants.LootScanRangeSq, OpqConstants.Stage1Items);
                    if (hits.Count > 0) { found = hits; break; }
                }
            }

            if (found.Count > 0)
            {
                DropCommands.LootItemListOnFloor(chr, found);
                cloudPiecesLooted += 1;
                output.Send(BotOutput.Kind.Bubble, chr, "Cloud Pieces: " + cloudPiecesLooted);
            }

            Sleep(800);
            TransitionTo(OpqState.Stage1Navigate, "捡完一轮，找下一个云朵");
        }

        void HandleStage1Return()
        {
            if (StillSleeping()) return;
            if (phaseStep == 0)
            {
                MoveTo(OpqConstants.Stage1DropX, OpqConstants.Stage1DropY);
                Sleep(NavigateSettleMs);
                phaseStep = 1;
                return;
            }
            if (Math.Abs(chr.x - OpqConstants.Stage1DropX) > 20)
            {
                MoveTo(OpqConstants.Stage1DropX, OpqConstants.Stage1DropY);
                Sleep(NavigateSettleMs);
                return;
            }
            TransitionTo(OpqState.Stage1DropItems, "到达回交点");
        }

        void HandleStage1DropItems()
        {
            if (StillSleeping()) return;

            if (cloudPiecesLooted > 0)
            {
                if (phaseStep == 0)
                {
                    OutputSpeak("Dropping " + cloudPiecesLooted + " cloud" + (cloudPiecesLooted == 1 ? "" : "s") + "!");
                    Sleep(400);
                    phaseStep = 1;
                    return;
                }
                DropCommands.BotThrowItemQty(chr, OpqConstants.CloudPiece, cloudPiecesLooted, (int)chr.x, (int)chr.y);
            }

            OpqOrchestrator.Context.MarkTaskComplete(chr.id);
            StartStageWaitTimer();
            TransitionTo(OpqState.Stage1Wait, "云朵已投放，等关卡 1 通关");
        }

        void HandleStage1Wait()
        {
            if (WaitTimedOut()) { TransitionTo(OpqState.LoopCheck, "关卡 1 等待超时"); return; }
            if (!OpqOrchestrator.Context.IsStage1Complete || !OpqOrchestrator.IsChamberlainSpawned()) return;

            if (phaseStep == 0)
            {
                OpqOrchestrator.FollowLeaderWarp(chr, OpqConstants.Stage1CompleteX, OpqConstants.Stage1CompleteY);
                Sleep(1000);
                phaseStep = 1;
                return;
            }
            if (StillSleeping()) return;

            // 走向通往中转塔的门（≡ moveToPortal(4)）
            MoveToPortal(OpqConstants.Stage1ExitPortalId);
            TransitionTo(OpqState.Stage1Transition, "关卡 1 通关标志已置位");
        }

        void HandleStage1Transition()
        {
            if (LeaderMap() != OpqConstants.Tower) return;

            if (phaseStep == 0)
            {
                OpqOrchestrator.FollowLeaderWarp(chr, OpqConstants.TowerSpawnX, OpqConstants.TowerSpawnY);
                Sleep(1000);
                phaseStep = 1;
                return;
            }
            if (StillSleeping()) return;

            MoveTo(OpqConstants.TowerWalkX, OpqConstants.TowerWalkY);
            TransitionTo(OpqState.Stage1TransitionPt2, "在中转塔等队长进关卡 2");
        }

        void HandleStage1TransitionPart2()
        {
            if (LeaderMap() != OpqConstants.Stage2) return;

            if (phaseStep == 0)
            {
                OpqOrchestrator.FollowLeaderWarp(chr, OpqConstants.Stage2SpawnX, OpqConstants.Stage2SpawnY);
                Sleep(1000);
                phaseStep = 1;
                return;
            }
            if (StillSleeping()) return;

            TransitionTo(OpqState.Stage2Navigate, "已进关卡 2");
        }

        // =========================================================================
        // 阶段：关卡 2（唱片箱）
        // =========================================================================

        void HandleStage2Navigate()
        {
            OpqOrchestrator.MirrorPhase(OpqPhase.Stage2);
            if (StillSleeping()) return;

            if (LeaderLeftStage2())
            {
                OpqOrchestrator.Context.PutBoxAssignment(chr.id, null);
                FollowLeaderOut();
                return;
            }

            int? reactorOid = OpqOrchestrator.AssignBoxReactor(chr);
            bool moreBoxes = OpqOrchestrator.HasUnclaimedLiveBoxReactor(chr.mapId, chr.id);

            if (!moreBoxes && !reactorOid.HasValue)
            {
                if (lootedRecordItemId > 0)
                {
                    TransitionTo(OpqState.Stage2Return, "没箱子了，先回去把唱片扔了");
                    return;
                }
                OpqOrchestrator.Context.MarkTaskComplete(chr.id);
                StartStageWaitTimer();
                TransitionTo(OpqState.Stage2Wait, "箱子清完，等关卡 2 通关");
                return;
            }
            if (!reactorOid.HasValue) return;   // 等别的假人释放

            var reactor = MapReactorService.Find(chr.mapId, reactorOid.Value);
            if (reactor == null || !reactor.Hittable)
            {
                OpqOrchestrator.Context.PutBoxAssignment(chr.id, null);
                return;
            }

            if (announcedBoxOid != reactor.objectId)
            {
                announcedBoxOid = reactor.objectId;
                OutputSpeak("I'll get the " + OpqOrchestrator.GetBoxOrdinal(reactor.objectId) + " box!");
            }

            if (navigateStartedAt == 0)
            {
                MoveTo(reactor.x, reactor.y);
                navigateStartedAt = BotScheduler.Now();
                return;
            }
            if (BotScheduler.Now() - navigateStartedAt < NavigateSettleMs) return;

            if (Math.Abs(chr.x - reactor.x) <= HitRange)
            {
                reactorHitsThisTarget = 0;
                TransitionTo(OpqState.Stage2HitBox, "已到箱子射程内 (oid=" + reactorOid.Value + ")");
            }
            else
            {
                MoveTo(reactor.x, reactor.y);
                navigateStartedAt = BotScheduler.Now();
            }
        }

        void HandleStage2HitBox()
        {
            if (swingTask != null) return;

            // 面朝箱子再挥（≡ microTurnAroundToLeft/Right）
            var reactorOid = OpqOrchestrator.Context.MyBoxAssignment(chr.id);
            var reactor = reactorOid.HasValue ? MapReactorService.Find(chr.mapId, reactorOid.Value) : null;
            if (reactor != null) chr.facing = reactor.x >= chr.x ? 1 : -1;

            reactorHitsThisTarget = 0;
            StartSwingChain(true);
        }

        void HandleStage2Loot()
        {
            if (StillSleeping()) return;

            var reactorOid = OpqOrchestrator.Context.MyBoxAssignment(chr.id);
            var reactor = reactorOid.HasValue ? MapReactorService.Find(chr.mapId, reactorOid.Value) : null;
            int scanX = reactor != null ? reactor.x : (int)chr.x;
            int scanY = reactor != null ? reactor.y : (int)chr.y;

            var found = BotLogic.CheckForItemsOnFloor(chr.mapId, scanX, scanY,
                OpqConstants.LootScanRangeSq, OpqConstants.Stage2Items);
            if (found.Count > 0) DropCommands.LootItemListOnFloor(chr, found);
            if (lootedRecordItemId > 0) output.Send(BotOutput.Kind.Bubble, chr, "Got a record!");

            Sleep(800);
            OpqOrchestrator.Context.PutBoxAssignment(chr.id, null);
            TransitionTo(OpqState.Stage2Return, "捡完唱片，回音乐盒");
        }

        void HandleStage2Return()
        {
            if (StillSleeping()) return;
            if (phaseStep == 0)
            {
                MoveTo(OpqConstants.Stage2DropX, OpqConstants.Stage2DropY);
                Sleep(NavigateSettleMs);
                phaseStep = 1;
                return;
            }
            if (Math.Abs(chr.x - OpqConstants.Stage2DropX) > 20)
            {
                MoveTo(OpqConstants.Stage2DropX, OpqConstants.Stage2DropY);
                Sleep(NavigateSettleMs);
                return;
            }
            TransitionTo(OpqState.Stage2DropItems, "到达音乐盒投放点");
        }

        void HandleStage2DropItems()
        {
            if (StillSleeping()) return;

            if (lootedRecordItemId > 0 && phaseStep == 0)
            {
                OutputSpeak("Dropping my record!");
                Sleep(400);
                phaseStep = 1;
                return;
            }
            if (lootedRecordItemId > 0)
            {
                DropCommands.BotThrowItem(chr, lootedRecordItemId, (int)chr.x, (int)chr.y, out _);
                lootedRecordItemId = -1;
                Sleep(600);
                phaseStep = 2;
                return;
            }
            if (phaseStep < 2)
            {
                Sleep(600);
                phaseStep = 2;
                return;
            }

            // 队长已经清完走了就别再追剩下的箱子
            if (LeaderLeftStage2()) { FollowLeaderOut(); return; }

            if (OpqOrchestrator.HasUnclaimedLiveBoxReactor(chr.mapId, chr.id))
            {
                TransitionTo(OpqState.Stage2Navigate, "还有箱子，再来一个");
                return;
            }
            OpqOrchestrator.Context.MarkTaskComplete(chr.id);
            StartStageWaitTimer();
            TransitionTo(OpqState.Stage2Wait, "箱子全破，等关卡 2 通关");
        }

        void HandleStage2Wait()
        {
            int leaderMap = LeaderMap();
            if (leaderMap == OpqConstants.ExitLobby || leaderMap == OpqConstants.OpqLobby)
            {
                FollowLeaderOut();
                return;
            }
            if (OpqOrchestrator.Context.IsStage2Complete)
            {
                TransitionTo(OpqState.ExitDetect, "关卡 2 通关标志已置位");
                return;
            }
            if (WaitTimedOut()) TransitionTo(OpqState.LoopCheck, "关卡 2 等待超时");
        }

        // =========================================================================
        // 阶段：出口
        // =========================================================================

        void HandleExitDetect()
        {
            OpqOrchestrator.MirrorPhase(OpqPhase.Exit);
            if (chr.mapId == OpqConstants.ExitLobby)
            {
                TransitionTo(OpqState.ExitLobby, "已到出口大厅");
                return;
            }
            int leaderMap = LeaderMap();
            if (leaderMap == OpqConstants.ExitLobby)
            {
                if (phaseStep == 0)
                {
                    OpqOrchestrator.FollowLeaderWarp(chr, OpqConstants.ExitLobbyX, OpqConstants.ExitLobbyY);
                    Sleep(1000);
                    phaseStep = 1;
                    return;
                }
                if (StillSleeping()) return;
                TransitionTo(OpqState.ExitLobby, "跟着队长去出口大厅");
            }
            else if (leaderMap == OpqConstants.OpqLobby)
            {
                if (phaseStep == 0)
                {
                    OpqOrchestrator.FollowLeaderWarp(chr, OpqConstants.LobbyX, OpqConstants.LobbyY);
                    Sleep(1000);
                    phaseStep = 1;
                    return;
                }
                if (StillSleeping()) return;
                TransitionTo(OpqState.LoopCheck, "跟着队长直接回招募大厅");
            }
        }

        void HandleExitLobby()
        {
            if (StillSleeping()) return;
            int leaderMap = LeaderMap();

            if (phaseStep == 0)
            {
                Sleep(leaderMap == OpqConstants.OpqLobby ? 300 : 2000);
                phaseStep = 1;
                return;
            }
            // 出口大厅没有出口门数据（只有 sp），只能服务端直接送回去
            if (!OpqOrchestrator.WarpTo(chr, OpqConstants.OpqLobby, OpqConstants.LobbyX, OpqConstants.LobbyY))
            {
                phaseStep = 0;   // 大厅图还没装好，下一 tick 再来
                return;
            }
            WanderToRandomSpot();
            TransitionTo(OpqState.LoopCheck, "已回到招募大厅");
        }

        void HandleLoopCheck()
        {
            CancelSwing();
            OpqOrchestrator.Context.ClearTaskComplete(chr.id);
            OpqOrchestrator.Context.PutCloudAssignment(chr.id, null);
            OpqOrchestrator.Context.PutBoxAssignment(chr.id, null);
            OpqOrchestrator.Context.ClearPlatformAssignment(chr.id);
            ResetScratch();

            if (IsInParty()) TransitionTo(OpqState.InPartyIdle, "还在队伍里，准备下一轮");
            else TransitionTo(OpqState.Recruitment, "没队伍了，回大厅喊话");
        }

        // =========================================================================
        // PQ 作废
        // =========================================================================

        void HandlePqAbandoned()
        {
            if (StillSleeping()) return;
            int botId = chr.id;
            OpqOrchestrator.Context.PutCloudAssignment(botId, null);
            OpqOrchestrator.Context.PutBoxAssignment(botId, null);
            OpqOrchestrator.Context.ClearPlatformAssignment(botId);
            OpqOrchestrator.Context.ClearTaskComplete(botId);

            if (phaseStep == 0)
            {
                OpqOrchestrator.WarpTo(chr, OpqConstants.ExitLobby, OpqConstants.ExitLobbyX, OpqConstants.ExitLobbyY);
                Sleep(1500);
                phaseStep = 1;
                return;
            }
            OpqOrchestrator.WarpTo(chr, OpqConstants.OpqLobby, OpqConstants.LobbyX, OpqConstants.LobbyY);
            ResetOpqState();
            TransitionTo(OpqState.Recruitment, "队伍丢了，PQ 作废，已回大厅");
        }

        // =========================================================================
        // 感知辅助
        // =========================================================================

        /// <summary>当前 mapId 应该对应哪个入口状态（≡ detectPhaseFromMap）。</summary>
        OpqState? DetectPhaseFromMap()
        {
            int mapId = chr.mapId;
            if (mapId == OpqConstants.OpqLobby) return IsInParty() ? OpqState.InPartyIdle : OpqState.Recruitment;
            if (mapId == OpqConstants.Stage1) return OpqState.Stage1Navigate;
            if (mapId == OpqConstants.Tower) return OpqState.Stage1Transition;
            if (mapId == OpqConstants.Stage2) return OpqState.Stage2Navigate;
            if (mapId == OpqConstants.ExitLobby) return OpqState.ExitDetect;
            return null;
        }

        /// <summary>同一阶段内部不倒退（≡ inSameStageFamily）。</summary>
        static bool InSameStageFamily(OpqState a, OpqState b)
        {
            string fa = Family(a), fb = Family(b);
            return fa != null && fa == fb;
        }

        static string Family(OpqState s)
        {
            switch (s)
            {
                case OpqState.Stage1Navigate:
                case OpqState.Stage1HitReactor:
                case OpqState.Stage1Loot:
                case OpqState.Stage1Return:
                case OpqState.Stage1DropItems:
                case OpqState.Stage1Wait:
                case OpqState.Stage1Transition:
                case OpqState.Stage1TransitionPt2:
                    return "S1";
                case OpqState.Stage2Navigate:
                case OpqState.Stage2HitBox:
                case OpqState.Stage2Loot:
                case OpqState.Stage2Return:
                case OpqState.Stage2DropItems:
                case OpqState.Stage2Wait:
                    return "S2";
                case OpqState.ExitDetect:
                case OpqState.ExitLobby:
                    return "EX";
                case OpqState.Recruitment:
                case OpqState.InPartyIdle:
                    return "LOBBY";
                default:
                    return null;
            }
        }

        bool IsInsidePqState()
        {
            switch (opqState)
            {
                case OpqState.Reset:
                case OpqState.Recruitment:
                case OpqState.InPartyIdle:
                case OpqState.LoopCheck:
                    return false;
                default:
                    return true;
            }
        }

        bool IsInParty() => OpqOrchestrator.PartyOf(chr.id) > 0;

        int LeaderMap() => OpqOrchestrator.LeaderMap(chr);

        bool LeaderLeftStage2()
        {
            int leaderMap = LeaderMap();
            return leaderMap == OpqConstants.ExitLobby || leaderMap == OpqConstants.OpqLobby;
        }

        void FollowLeaderOut()
        {
            if (phaseStep == 0) { Sleep(1000); phaseStep = 1; return; }
            if (StillSleeping()) return;

            int leaderMap = LeaderMap();
            if (leaderMap == OpqConstants.ExitLobby)
            {
                OpqOrchestrator.FollowLeaderWarp(chr, OpqConstants.ExitLobbyX, OpqConstants.ExitLobbyY);
                TransitionTo(OpqState.ExitLobby, "队长已经离开关卡 2，跟去出口大厅");
            }
            else
            {
                OpqOrchestrator.FollowLeaderWarp(chr, OpqConstants.LobbyX, OpqConstants.LobbyY);
                TransitionTo(OpqState.LoopCheck, "队长已经在招募大厅，跳过出口");
            }
        }

        void ResetStageScratch()
        {
            cloudPiecesLooted = 0;
            lootedRecordItemId = -1;
            reactorHitsThisTarget = 0;
        }

        void StartStageWaitTimer() => stageWaitStartTime = BotScheduler.Now();

        bool WaitTimedOut()
            => stageWaitStartTime > 0 && BotScheduler.Now() - stageWaitStartTime > OpqConstants.StageWaitTimeoutMs;

        // =========================================================================
        // 动作封装
        // =========================================================================

        void CancelSwing()
        {
            swingToken++;
            swingTask?.Cancel();
            swingTask = null;
        }

        /// <summary>
        /// 源端 hitReactor4Times：连挥 4 下、每下间隔 700ms。这里换成 BotTasks 延时链，FSM 不阻塞。
        /// </summary>
        void StartSwingChain(bool stage2)
        {
            int token = ++swingToken;
            swingTask = BotTasks.Schedule(() => SwingStep(token, stage2), 0);
        }

        void SwingStep(int token, bool stage2)
        {
            if (token != swingToken) return;
            if (chr == null || CheckIfNotRunningOrPaused()) { swingTask = null; return; }

            var context = OpqOrchestrator.Context;
            int? reactorOid = stage2 ? context.MyBoxAssignment(chr.id) : context.MyCloudAssignment(chr.id);
            if (!reactorOid.HasValue)
            {
                swingTask = null;
                TransitionTo(stage2 ? OpqState.Stage2Navigate : OpqState.Stage1Navigate, "HIT 状态下丢了分配");
                return;
            }

            var reactor = MapReactorService.Find(chr.mapId, reactorOid.Value);
            if (reactor == null || !reactor.Hittable)
            {
                swingTask = null;
                TransitionTo(stage2 ? OpqState.Stage2Loot : OpqState.Stage1Loot, "到场时反应堆已被打破");
                return;
            }

            BotAttack.BasicSwing(chr, random, output);

            // 源端 CustomReactor.dropItemAtReactor：假人没有真实客户端，反应堆掉落由这里补
            int recordItemId = -1;
            var drops = new List<InventoryEntry>();
            if (stage2)
            {
                recordItemId = OpqOrchestrator.GetBoxItemId(reactorOid.Value);
                drops.Add(BotLogic.GenerateCleanItem(recordItemId));
            }
            else drops.Add(BotLogic.GenerateCleanItem(OpqConstants.CloudPiece));

            var hit = MapReactorService.Hit(chr.mapId, reactorOid.Value, drops, chr.id, OpqOrchestrator.PartyOf(chr.id));
            reactorHitsThisTarget++;
            bool broken = hit == null || !hit.Hittable;
            if (broken && recordItemId > 0) lootedRecordItemId = recordItemId;

            if (broken || reactorHitsThisTarget >= OpqConstants.MaxReactorHits)
            {
                swingTask = null;
                TransitionTo(stage2 ? OpqState.Stage2Loot : OpqState.Stage1Loot,
                    "反应堆已破 (hits=" + reactorHitsThisTarget + ")");
                return;
            }
            swingTask = BotTasks.Schedule(() => SwingStep(token, stage2), OpqConstants.SwingIntervalMs);
        }

        void MoveTo(int x, int y)
        {
            int groundY = (int)MapService.SnapToGround(chr.mapId, x, y);
            BotMovementService.Move(chr, x, groundY);
        }

        bool MoveToPortal(int portalId)
        {
            var map = MapService.Get(chr.mapId);
            if (map == null) return false;
            MapPortal portal = null;
            foreach (var p in map.portals) if (p.id == portalId) { portal = p; break; }
            if (portal == null) return false;
            MoveTo(portal.x, portal.y);
            return true;
        }

        void OutputSpeak(string text) => output.Speak(chr, text);

        // ≡ sleepAmountSeconds 的非阻塞版：Sleep 排一个截止时间，StillSleeping 判断是否还没到。
        void Sleep(long ms) => sleepUntil = BotScheduler.Now() + ms;
        bool StillSleeping() => BotScheduler.Now() < sleepUntil;

        // ≡ botMoveToPlatformAnyUnoccupiedSpot 的近似：在地图主地面上随机挑一个点走过去。
        // 源端按平台名（m3/m4…）挑空位，我们这里没有平台名表，用整图地面 + 夹取替代。
        void WanderToRandomSpot()
        {
            var map = MapService.Get(chr.mapId);
            if (map == null) return;
            int minX = map.left + 80, maxX = map.right - 80;
            if (maxX <= minX) return;
            int x = minX + random.Next(maxX - minX);
            int y = (int)MapService.SnapToGround(chr.mapId, x, chr.y);
            BotMovementService.Move(chr, x, y);
        }

        public override void DisplayCommands(BotActor player) => output.ShowOptions(player, new[] { chr.name });
    }
}
