// BotTypes/DiceBot.java（326 行）对齐移植：Cho（双）/ Han（单）双骰赌桌荷官。
// 流程：喊「请下注」→ 等玩家说 "bet han/cho" → 扫描玩家脚下的赌注图章 → 3 秒后掷双骰
//       → 2 秒后判定 → 赢则按数量把押注物铺开扔回玩家脚下，输则把地上的押注收走。
//
// 与源端的差异（全部源于「没有独立线程 / 没有阻塞 sleep」和源端自身的两处笔误）：
//   1. 源端 sleepAmountSeconds / MethodScheduler.runAfterDelay / TimeUnit.sleep(100) 全部换成
//      BotScheduler.Now() 时间戳 + BotTasks.Schedule 延时续接（≡ DropGameBot / TutorialBot 的写法）。
//      派彩逐枚 100ms 的间隔改成同一串错峰的单次任务，先后与间隔语义一致。
//   2. 源端 waitForState(BET, 25) 的判定写成
//      `if (!diceBotState.equals(targetState) && now < endTime) processMessages(); else {收摊}`，
//      即「等到了 BET」反而会走进收摊分支——这会让赌桌永远掷不出骰子，属于源端笔误。
//      这里按方法名与日志（"Waiting for state: BET"）还原为正常语义：
//      到达 BET → 返回继续；未到达且未超时 → 收消息；超时 → 收摊。
//   3. 源端等待下注靠 synchronized + waitingForBet 双检锁防重入（因为那条线程会被长时间挂住）。
//      C# 侧单线程 tick，重入不可能发生，该字段与双检逻辑整体去掉。
//   4. 源端 calculateIfPlayerWins 在 currentBet == NONE 时抛 IllegalArgumentException（会炸掉整轮 tick）。
//      这里改成「记日志 + 判负」，不让一次异常把假人打死。
//   5. 源端 respondant 由外部脚本/交互系统灌进来；本端口的入口是 BotDispatcher 写入的 inquirer
//      队列，故照 GameZoneHostBot.checkForInquirer 的写法在 WAIT_FOR_BET 里把 inquirer 提升为 respondant。
//   6. 源端 handlePlayerStealsBets 的 dropMessage(5, ...) 用黄字广播（≡ BotOutput.Kind.YellowMessage）。
//      掉名声是纯内存写入，客户端是权威进程，改了也不会同步，但仍按源端照做（与 Blackjack 一致）。
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

            if (currentBet == BetType.None)
            {
                // 源端 waitForState 每轮都重算 endTime（startTime 会被 handleIfListIsEmpty 推后）
                endTime = startTime + BetTimeoutMs;
                if (BotScheduler.Now() < endTime)
                {
                    if (!optionsShown) { optionsShown = true; DisplayCommands(player); }
                    ProcessMessages();
                    return;
                }
                ExpireOptions();
                output.Speak(chr, "Please place your bets when you are ready!");
                state = State.FINISHED;
                ResetDiceBotState();
                return;
            }
            // 已收到 "bet han/cho"（≡ waitForState 等到目标状态后返回）
            SetState(DiceBotState.Bet);
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

            bool playerWins;
            if (currentBet == BetType.None)
            {
                // 降级点 4：源端在这里抛 IllegalArgumentException，这里记日志后判负
                DebugLog("[DiceBot] no bet type selected, treating round as a loss");
                playerWins = false;
            }
            else playerWins = BotDealerDice.PlayerWins(currentBet, rolled);

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
                        if (!Alive()) return;
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
            sequenceTask?.Cancel();
            sequenceTask = null;
            base.StopScheduledTask();
        }
    }
}
