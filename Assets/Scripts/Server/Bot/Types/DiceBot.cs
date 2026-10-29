// BotTypes/DiceBot.java（326 行）对齐移植：Cho（双）/ Han（单）双骰赌桌荷官。
// 流程：喊「请下注」→ 等玩家说 "bet han/cho" → 扫描玩家脚下的赌注图章 → 3 秒后掷双骰
//       → 2 秒后判定 → 赢则按数量把押注物铺开扔回玩家脚下，输则把地上的押注收走。
//
// 原端 waitForState 只轮询一次；消息回调将状态改成 BET，下一 tick 直接进入 BET。
// 延时以可取消任务接续，不阻塞地图线程；不改写原端判胜或等待条件。
using System;
using System.Collections.Generic;
using Server.Bot.Social;
using Server.Field;

namespace Server.Bot.Types
{
    // ≡ DiceBot.BetType：复用 BotDealerDice 的同名枚举（CHO=双 / HAN=单 / NONE=未下注）
    using BetType = BotDealerDice.BetType;

    public sealed class DiceBot : BotStateMachine
    {
        // ≡ DiceBot.DiceBotState（7 态）
        public enum DiceBotState { Reset, WaitForBet, Bet, Roll, RollPending, ProcessBet, BetPayout }

        // ≡ 源端各处常量
        const long BetTimeoutMs = 25_000;
        const double BetScanRangeSq = 15_000;
        const double LoseRecheckRangeSq = 75_000;
        const long RollDelayMs = 3000;
        const long ProcessDelayMs = 2000;
        const long PayoutStaggerMs = 100;

        static readonly string[] BetHint = { "Bet Cho (even)", "Bet Han (odd)" };

        readonly Random random;
        readonly BotMessages messages;

        public DiceBotState diceBotState { get; private set; } = DiceBotState.Reset;

        BetType currentBet = BetType.None;
        int[] rolls;
        List<MapItem> currentPot;
        long startTime, endTime;
        bool betsProcessed;
        bool optionsShown;
        int generation;
        BotTasks.Handle sequenceTask;

        public DiceBot(BotCharacter c, BotMessages messages = null, Random random = null) : base(c)
        {
            this.messages = messages ?? BotTypeManager.Messages;
            this.random = random ?? new Random();
            botType = "DiceBot";
            startTime = BotScheduler.Now();
        }

        // 带着玩家时把 tick 钉快一点，别让「说 bet han」等一个 10 秒周期。
        public override void CheckPrioritySpeed()
        {
            if (interactors.Respondant != null) UpdateScheduleDelay(1000);
            else base.CheckPrioritySpeed();
        }

        // =========================================================================
        // MAIN STATE MACHINE
        // =========================================================================
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused()) return;
            DebugLog(chr.name + " DiceBotState: " + diceBotState);

            switch (diceBotState)
            {
                case DiceBotState.Reset:
                    ResetDiceBotState();
                    StartGame();
                    SetState(DiceBotState.WaitForBet);
                    break;
                case DiceBotState.WaitForBet:
                    WaitForBet();
                    break;
                case DiceBotState.Bet:
                    SetState(DetectBet() ? DiceBotState.Roll : DiceBotState.WaitForBet);
                    break;
                case DiceBotState.Roll:
                    CalculateRolls();
                    SetState(DiceBotState.RollPending);
                    break;
                case DiceBotState.RollPending:
                    if (rolls != null) SetState(DiceBotState.ProcessBet);
                    break;
                case DiceBotState.ProcessBet:
                    CalculateBet();
                    SetState(DiceBotState.BetPayout);
                    break;
                case DiceBotState.BetPayout:
                    if (betsProcessed) SetState(DiceBotState.Reset);
                    break;
                default:
                    DebugLog("Unexpected DiceBot state: " + diceBotState);
                    state = State.FINISHED;
                    ResetDiceBotState();
                    break;
            }
        }

        public override void DisplayCommands(BotActor player)
        {
            if (player == null) return;
            output.ShowOptions(player, BetHint);
        }

        // =========================================================================
        // STATE HELPERS
        // =========================================================================
        BotActor Player => interactors.Respondant;

        void SetState(DiceBotState next) => diceBotState = next;
        void SetBet(BetType bet) => currentBet = bet;

        bool Alive() => running && chr.online && ReferenceEquals(BotStore.GetBotById(chr.id), this);

        void ResetDiceBotState()
        {
            SetState(DiceBotState.Reset);
            SetBet(BetType.None);
            currentPot = null;
            rolls = null;
            startTime = BotScheduler.Now();
            endTime = 0;
            betsProcessed = false;
            optionsShown = false;
            sequenceTask?.Cancel();
            sequenceTask = null;
        }

        // ≡ 源端 sleepAmountSeconds / runAfterDelay 的可取消等价物
        void Later(long ms, Action next)
        {
            sequenceTask?.Cancel();
            sequenceTask = BotTasks.Schedule(() =>
            {
                if (!Alive()) return;
                try { next(); }
                catch (Exception e) { DebugLog(chr.name + " sequence error: " + e.Message); }
            }, ms);
        }

        // ——— RESET ———
        void StartGame()
        {
            // ≡ SocialCommands.BotChatbubble(getChr(), "Please Place your bets!")
            output.Send(BotOutput.Kind.Bubble, chr, "Please Place your bets!");
        }

        // ——— WAIT_FOR_BET ———
        void WaitForBet()
        {
            if (Player == null) CheckForInquirer();
            var player = Player;
            if (player == null) return;

            endTime = startTime + BetTimeoutMs;
            if (diceBotState != DiceBotState.Bet && BotScheduler.Now() < endTime)
            {
                if (!optionsShown) { optionsShown = true; DisplayCommands(player); }
                ProcessMessages();
                return;
            }
            ExpireOptions();
            output.Speak(chr, "Please place your bets when you are ready!");
            state = State.FINISHED;
            ResetDiceBotState();
        }

        // ≡ GameZoneHostBot.checkForInquirer：把排队询问的玩家提升为本局对话对象
        void CheckForInquirer()
        {
            if (interactors.inquirers.Count == 0) return;
            var inquirer = interactors.inquirers[0];
            interactors.RemoveInquirer(inquirer);
            interactors.SetRespondant(inquirer);
        }

        // ——— BET ———
        bool DetectBet()
        {
            var player = Player;
            if (player == null) return false;

            // ≡ BotLogic.readPlayersBetsStamps(getRespondant(), 15000)
            var items = BotLogic.ReadPlayersBetsStamps(player, BetScanRangeSq);
            if (items.Count == 0)
            {
                // ≡ handleIfListIsEmpty：给玩家更多时间（把超时窗口整体后移）
                output.Speak(chr, "No Bet detected. Please Place your bets!");
                startTime = BotScheduler.Now();
                return false;
            }
            // ≡ handleBetList
            output.Speak(chr, BotLogic.AnnounceBetString(player, items));
            currentPot = items;
            return true;
        }

        // ——— ROLL / ROLL_PENDING ———
        void CalculateRolls()
        {
            // ≡ MethodScheduler.runAfterDelay(3000) → rolls = botDealerRollDoubleDice(getChr())
            Later(RollDelayMs, () => rolls = BotDealerDice.Roll(chr, random, output));
        }

        // ——— PROCESS_BET ———
        void CalculateBet()
        {
            // ≡ MethodScheduler.runAfterDelay(2000) → processBet(rolls)
            Later(ProcessDelayMs, () => ProcessBet(rolls));
        }

        void ProcessBet(int[] rolled)
        {
            if (rolled == null || rolled.Length < 2) { betsProcessed = true; return; }

            if (currentBet == BetType.None) throw new ArgumentException("Invalid bet: " + currentBet);
            bool playerWins = BotDealerDice.PlayerWins(currentBet, rolled);

            output.Speak(chr, BotDealerDice.ResultText(rolled));
            output.Emote(chr, 2);

            if (playerWins) PayoutWinnersBets();
            else HandlePlayerLose();
            betsProcessed = true;
        }

        // ——— 赢：按数量铺开扔回玩家脚下 ———
        void PayoutWinnersBets()
        {
            if (currentPot == null) return;
            var player = Player;
            if (player == null) return;

            int token = generation;
            int throwIndex = 0;
            foreach (var mapItem in currentPot)
            {
                if (mapItem == null) continue;
                int itemId = mapItem.ItemId;
                int quantity = mapItem.Quantity;
                for (int x = 0; x < quantity; x++, throwIndex++)
                {
                    // ≡ adjustCenterPositionXAxis(center, x, 2, 4, 20)
                    var pos = BotHelpers.AdjustCenterPositionXAxis((int)player.x, (int)player.y, x, 2, 4, 20);
                    int delay = throwIndex * (int)PayoutStaggerMs;
                    int capturedItemId = itemId;
                    int px = pos.x, py = pos.y;
                    BotTasks.Schedule(() =>
                    {
                        if (!Alive() || token != generation) return;
                        // ≡ DropCommands.botThrowItem(getChr(), mapItem.getItemId(), center)
                        DropCommands.BotThrowItem(chr, capturedItemId, px, py, out _);
                    }, delay);
                }
            }
        }

        // ——— 输：收走地上的押注；被偷回则掉名声 ———
        void HandlePlayerLose()
        {
            var player = Player;
            if (player == null || currentPot == null || currentPot.Count == 0) return;

            var anchor = currentPot[0];
            var stillOnFloor = BotLogic.ReadPlayersBetsStamps(player, (int)anchor.x, (int)anchor.y, LoseRecheckRangeSq);
            bool potCollectible = BotLogic.CheckIfItemsOnFloorStill(currentPot, stillOnFloor);

            if (potCollectible) DropCommands.LootItemListOnFloor(chr, currentPot);
            else HandlePlayerStealsBets();
        }

        void HandlePlayerStealsBets()
        {
            var player = Player;
            if (player != null)
            {
                // ≡ getRespondant().setFame(fame - 10)
                player.fame -= 10;
                // ≡ getRespondant().dropMessage(5, ...)
                output.Send(BotOutput.Kind.YellowMessage, player,
                    "[Mushroom Casino] You have stolen your bets. You have been defamed by the Mushroom Casino.",
                    targetId: player.id);
            }
            output.Speak(chr, "Player has stolen back his bet. Ceasing Game.");
            state = State.FINISHED;
        }

        void ExpireOptions()
        {
            var player = Player;
            if (player != null) output.ExpireOptions(player);
        }

        // =========================================================================
        // MESSAGES
        // =========================================================================
        // ≡ 源端 processMessages()：共享队列取一条、超时 1 秒返回。本端口玩家聊天走 PRIMARY，
        //   照 TutorialBot 的写法先试 SECONDARY 再回落 PRIMARY。
        void ProcessMessages()
        {
            Consume(QueueType.SECONDARY);
            Consume(QueueType.PRIMARY);
        }

        void Consume(QueueType type)
        {
            var message = messages.Poll(type);
            if (message == null) return;
            if (!interactors.IsMessageFromRespondant(message)) { messages.Add(message, type); return; }
            HandleBetCommand(message);
        }

        void HandleBetCommand(ChatMessage message)
        {
            string content = message.content == null ? "" : message.content.ToLowerInvariant();
            if (!content.Contains("bet")) return;
            if (content.Contains("han")) ProcessBetCommand(BetType.Han, message);
            else if (content.Contains("cho")) ProcessBetCommand(BetType.Cho, message);
            else HandleInvalidBet();
        }

        void ProcessBetCommand(BetType betType, ChatMessage message)
        {
            SetState(DiceBotState.Bet);
            // ≡ String.format("%s bets on %s!", sender.getName(), betType.name())
            output.Speak(chr, BotDealerDice.BetAnnouncement(message.sender.name, betType));
            SetBet(betType);
        }

        void HandleInvalidBet()
        {
            output.Speak(chr, "Please Select Han or Cho!");
            startTime = BotScheduler.Now();
            SetBet(BetType.None);
        }

        public override void StopScheduledTask()
        {
            generation++;
            sequenceTask?.Cancel();
            sequenceTask = null;
            base.StopScheduledTask();
        }
    }
}
