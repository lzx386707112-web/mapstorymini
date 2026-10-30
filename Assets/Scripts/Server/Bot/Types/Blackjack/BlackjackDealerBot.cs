using System;
using System.Collections.Generic;
using Server.Bot.Social;
using Server.Field;

namespace Server.Bot.Types.Blackjack
{
    // BotTypes/Blackjack/BlackjackDealerBot.java（860 行）对齐移植。
    //
    // 与源端的差异（都源于「没有独立线程」这一条）：
    //  1. 源端在 handleDealerTurn 里 while + sleepAmountSeconds(500) 阻塞自己；
    //     这里改成「每 tick 补一张牌」，靠 2s 的固定 tick 周期推进，主线程不阻塞。
    //  2. 源端 dealCardsToAllPlayers 每张牌 sleep 500ms；这里同一 tick 内一次发完。
    //     地上出现的牌面完全相同，只是没有逐张的动画节奏。
    //  3. 源端 payWinner 在异步线程里逐枚 sleep 投放；这里一次按 x 错开投放，
    //     只有「AI 捡回押注」那一步用 BotTasks 延时。
    //  4. 源端 startScheduledTask 里 updateScheduleDelay(2000) 会被随后每 tick 的
    //     checkPrioritySpeed 覆盖（无人时 10s），牌桌会卡；这里固定 2s。
    public sealed class BlackjackDealerBot : BotStateMachine
    {
        // ≡ BlackjackDealerBot.BET_STAMPS
        static readonly int[] BetStamps = { 4002000, 4002001, 4002002, 4002003, 4031558, 4031559, 4031560, 4031561 };
        const int AiBetItem = 4031560;   // 黑骑士图章
        const int AiBetQty = 5;
        static readonly int[] DealerOutfit = { 1042129, 1060001, 1072010, 1702099 };

        readonly BlackjackTable table;
        readonly Random random = new Random();
        readonly BotMessages messages = BotTypeManager.Messages;

        BlackjackTable.Phase lastPhase;
        long bettingDeadline;
        bool finalDecision;
        bool payoutsIssued;
        bool dealerTurnAnnounced;
        long playerTurnStart;

        public BlackjackDealerBot(BotCharacter chr, BotMessages messagesOverride = null) : base(chr)
        {
            table = new BlackjackTable(chr, random);
            botType = "BLACKJACK_DEALER";
            if (messagesOverride != null) messages = messagesOverride;
            lastPhase = table.phase;
            DressDealer();
        }

        public BlackjackTable Table => table;

        void DressDealer()
        {
            try
            {
                var customization = new Decorator.BotCustomization(BotItemCatalog.Shared, BotTypeManager.Output);
                foreach (int itemId in DealerOutfit) customization.Equip(chr, itemId);
            }
            catch (Exception)
            {
                // 外观道具缺失不影响牌局（源端同样直接 EquipBot，缺了就跳过）
            }
        }

        // 源端 2s 基础 tick
        protected override long DefaultPeriodMs() => 2000;

        // 源端靠 startScheduledTask 里 updateScheduleDelay(2000)，但随后每 tick 的
        // checkPrioritySpeed 会把它改成 2~6s 或 10s；牌桌需要稳定节拍，这里钉死 2s。
        public override void CheckPrioritySpeed() => UpdateScheduleDelay(2000);

        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused()) return;

            ProcessJoinInquiries();
            // 源端用 lastLoggedPhase 判断「本阶段的第一帧」，这里同样在 switch 之前取快照
            bool firstTickOfPhase = lastPhase != table.phase;
            switch (table.phase)
            {
                case BlackjackTable.Phase.WAITING: HandleWaiting(firstTickOfPhase); break;
                case BlackjackTable.Phase.BETTING: HandleBetting(); break;
                case BlackjackTable.Phase.PROCESSING_BETS: HandleProcessingBets(); break;
                case BlackjackTable.Phase.DEALING: HandleDealing(); break;
                case BlackjackTable.Phase.PLAYER_TURNS: HandlePlayerTurn(); break;
                case BlackjackTable.Phase.PLAYER_RESPONSE: HandlePlayerResponse(); break;
                case BlackjackTable.Phase.DEALER_TURN: HandleDealerTurn(); break;
                case BlackjackTable.Phase.RESOLVING: HandleResolving(); break;
                case BlackjackTable.Phase.PAYOUT: HandlePayout(); break;
                default:
                    state = State.FINISHED;
                    break;
            }
            lastPhase = table.phase;
        }

        // ——————————————— 阶段处理 ———————————————

        void HandleWaiting(bool firstTickOfPhase)
        {
            if (firstTickOfPhase)
            {
                table.ResetForNewRound();
                if (!table.HasEnoughPlayers) ChatBubble("Waiting for Players to join table..");
            }
            if (table.HasEnoughPlayers)
            {
                bettingDeadline = 0;
                table.SetPhase(BlackjackTable.Phase.BETTING);
            }
        }

        void HandleBetting()
        {
            if (bettingDeadline == 0)
            {
                ChatBubble("Please Place your Bets!");
                TriggerArtificialPlayerBets();
                bettingDeadline = BotScheduler.Now() + 10000;
                return;
            }
            if (BotScheduler.Now() >= bettingDeadline)
            {
                bettingDeadline = 0;
                table.SetPhase(BlackjackTable.Phase.PROCESSING_BETS);
            }
        }

        void HandleProcessingBets()
        {
            table.IncrementWaitCount();
            ProcessAllPlayersBets();

            if (table.PlayerCount <= 1)
            {
                table.SetPhase(BlackjackTable.Phase.WAITING);
                return;
            }
            if (table.HasAtLeastOneBet() && table.waitCount > 1)
            {
                ChatBubble("All bets are closed. Let's Begin.");
                table.SetPhase(BlackjackTable.Phase.DEALING);
            }
            else
            {
                table.SetPhase(BlackjackTable.Phase.BETTING);
            }
        }

        void HandleDealing()
        {
            // 源端逐张 sleep 500ms；这里同一 tick 发完（状态相同，只少动画节奏）
            int size = table.PlayerCount;
            for (int round = 0; round < 2; round++)
            {
                for (int i = 0; i < size; i++)
                {
                    int seqIndex = BlackjackTable.GetDealSequenceIndex(i, size);
                    var player = table.PlayerAt(seqIndex);
                    if (player == null) continue;
                    if (seqIndex != 0 && !player.HasBets) continue;      // 没下注的人不发牌
                    if (seqIndex == 0 && round == 1) continue;           // 荷官首轮只亮一张
                    DealCardToPlayer(player);
                }
            }
            Speak("My card: " + table.Dealer.HandValue + ".");
            table.SetPhase(BlackjackTable.Phase.PLAYER_TURNS);
        }

        void HandlePlayerTurn()
        {
            playerTurnStart = BotScheduler.Now();
            finalDecision = false;

            var current = table.CurrentPlayer;
            if (current == null) { table.IncrementToNextPlayer(); return; }

            int handValue = current.HandValue;
            if (handValue == 0)
            {
                table.IncrementToNextPlayer();
                if (table.IsCurrentPlayerDealer) table.SetPhase(BlackjackTable.Phase.DEALER_TURN);
                return;
            }
            if (handValue > 21) { HandleBust(current, handValue); return; }
            if (handValue == 21) { HandleAutoStand(current); return; }

            FaceTowards(current.character);
            Speak(current.Name + ": " + handValue + ". What will you do?");
            if (!BotHelpers.IsBot(current.character)) ShowActionHint(current.character);
            TriggerArtificialPlayerResponse(current);
            table.SetPhase(BlackjackTable.Phase.PLAYER_RESPONSE);
        }

        void HandleBust(BlackjackPlayer player, int handValue)
        {
            player.SetResponseStatus("RESPONDED");
            player.SetStatus(BlackjackPlayer.PlayerStatus.BUST);
            Speak(player.Name + ": " + handValue + " Too Many.");
            TriggerAiPlayerReaction(player, "PlayerBust");

            var kicked = LootLoserBets(player);
            if (player.cardsOnMap.Count > 0)
            {
                DropCommands.LootItemListOnFloor(chr, new List<MapItem>(player.cardsOnMap));
                player.ClearCardsOnMap();
            }

            if (kicked != null) KickPlayer(kicked);   // removePlayer 已把下标移到下一位
            else table.IncrementToNextPlayer();

            table.SetPhase(table.IsCurrentPlayerDealer ? BlackjackTable.Phase.DEALER_TURN : BlackjackTable.Phase.PLAYER_TURNS);
        }

        void HandleAutoStand(BlackjackPlayer player)
        {
            player.SetResponseStatus("RESPONDED");
            player.SetStatus(BlackjackPlayer.PlayerStatus.STAND);
            Speak(player.Name + ": 21!");
            table.IncrementToNextPlayer();
            table.SetPhase(table.IsCurrentPlayerDealer ? BlackjackTable.Phase.DEALER_TURN : BlackjackTable.Phase.PLAYER_TURNS);
        }

        void HandlePlayerResponse()
        {
            var current = table.CurrentPlayer;
            if (current == null) { table.SetPhase(BlackjackTable.Phase.DEALER_TURN); return; }
            if (!current.HasResponded) WaitForResponse(current);
            if (!current.HasResponded) return;
            ProcessPlayerAction(current);
        }

        void ProcessPlayerAction(BlackjackPlayer player)
        {
            switch (player.status)
            {
                case BlackjackPlayer.PlayerStatus.ACTIVE:   // 要牌
                    DealCardToPlayer(player);
                    player.SetResponseStatus("WAITING");
                    player.SetStatus(BlackjackPlayer.PlayerStatus.WAITING);
                    table.SetPhase(BlackjackTable.Phase.PLAYER_TURNS);
                    break;
                case BlackjackPlayer.PlayerStatus.STAND:
                case BlackjackPlayer.PlayerStatus.BUST:
                    table.IncrementToNextPlayer();
                    player.SetResponseStatus("WAITING");
                    table.SetPhase(table.IsCurrentPlayerDealer ? BlackjackTable.Phase.DEALER_TURN : BlackjackTable.Phase.PLAYER_TURNS);
                    break;
            }
        }

        void HandleDealerTurn()
        {
            var dealer = table.Dealer;
            if (!dealerTurnAnnounced)
            {
                dealerTurnAnnounced = true;
                Speak("My Turn.");
            }

            int handValue = dealer.HandValue;
            if (!BlackjackRules.ShouldDealerHit(handValue))
            {
                Speak(handValue + ".");
                if (handValue > 21) Speak("Too many. I Bust.");
                else if (handValue == 21) { Speak("21 for me :P"); output.Emote(chr, 3); }
                dealerTurnAnnounced = false;
                table.SetPhase(BlackjackTable.Phase.RESOLVING);
                return;
            }
            // 源端 while 循环里先说点数再补牌，这里每 tick 走一轮
            Speak(handValue + ".");
            DealCardToPlayer(dealer);
        }

        void HandleResolving()
        {
            table.ResolveAllOutcomes();

            var dealerHand = table.Dealer.hand;
            var lines = new List<string>();
            for (int i = 1; i < table.PlayerCount; i++)
            {
                var player = table.PlayerAt(i);
                if (player == null || player.hand.Count == 0) continue;
                lines.Add(BlackjackRules.FormatOutcomeMessage(player.Name,
                    BlackjackRules.DetermineOutcome(player.hand, dealerHand), player.hand, dealerHand));
            }
            if (lines.Count > 0) Speak(string.Join(" | ", lines));

            for (int i = 1; i < table.PlayerCount; i++)
            {
                var player = table.PlayerAt(i);
                if (player == null || player.hand.Count == 0) continue;
                string node = OutcomeToDialogueNode(BlackjackRules.DetermineOutcome(player.hand, dealerHand));
                if (node != null) TriggerAiPlayerReaction(player, node);
            }

            table.SetPhase(BlackjackTable.Phase.PAYOUT);
        }

        void HandlePayout()
        {
            if (!payoutsIssued)
            {
                var toKick = new List<BotActor>();
                int staggerDelay = 0;
                for (int i = 1; i < table.PlayerCount; i++)
                {
                    var player = table.PlayerAt(i);
                    if (player == null || player.result == BlackjackPlayer.HandResult.PENDING) continue;
                    if (player.betsProcessed) continue;

                    if (player.result == BlackjackPlayer.HandResult.WIN ||
                        player.result == BlackjackPlayer.HandResult.BLACKJACK_WIN)
                    {
                        bool bonus = player.result == BlackjackPlayer.HandResult.BLACKJACK_WIN;
                        int delay = staggerDelay;
                        BotTasks.Schedule(() => PayWinner(player, bonus), delay);
                        staggerDelay += 2000 + random.Next(1500);
                    }
                    else
                    {
                        var kicked = PayoutOrCollect(player);
                        if (kicked != null) toKick.Add(kicked);
                    }
                }
                foreach (var actor in toKick) KickPlayer(actor);
                payoutsIssued = true;
                return;
            }

            // 等所有异步派彩落地（AI 捡回押注那一步也在这之前完成 betsProcessed）
            for (int i = 1; i < table.PlayerCount; i++)
            {
                var player = table.PlayerAt(i);
                if (player == null || player.result == BlackjackPlayer.HandResult.PENDING) continue;
                if (!player.betsProcessed) return;
            }

            CleanupAllCards();
            ChatBubble("Let's go again.");
            payoutsIssued = false;
            table.SetPhase(BlackjackTable.Phase.WAITING);
        }

        void KickPlayer(BotActor actor)
        {
            table.RemovePlayer(actor);
            interactors.RemoveRespondant(actor);
        }

        // ——————————————— 发牌 ———————————————

        void DealCardToPlayer(BlackjackPlayer player)
        {
            string card = table.DrawCard();
            int cardX = player.originX + (player.hand.Count + 1) * 35;
            int cardY = player.originY;
            int itemId = CardVisuals.GetCardItemId(card);
            var dropped = DropCommands.BotThrowItemNoExpireOwnerOnly(chr, itemId, cardX, cardY);
            table.DealCardToPlayer(player, card);
            player.AddCardOnMap(dropped);
        }

        // ——————————————— 押注扫描 ———————————————

        void ProcessAllPlayersBets()
        {
            var toKick = new List<BotActor>();
            var betLines = new List<string>();
            for (int i = 0; i < table.PlayerCount; i++)
            {
                var player = table.PlayerAt(i);
                if (player == null) continue;
                if (i == 0)
                {
                    player.SetOriginLocation((int)chr.x, (int)chr.y);
                    continue;
                }
                var who = player.character;
                if (who.mapId != chr.mapId)
                {
                    Speak(who.name + " has left the map.");
                    toKick.Add(who);
                    continue;
                }
                player.SetOriginLocation((int)who.x, (int)who.y);

                // 源端 readPlayersBetsStamps(chr, 5000)：范围参数是「距离平方」
                var items = BotLogic.ReadPlayersBetsStamps(who, 5000);
                if (items.Count == 0)
                {
                    ChatBubble("No Bet detected for: " + who.name);
                    if (player.SkipHand()) toKick.Add(who);
                }
                else
                {
                    player.ResetSkippedHands();
                    player.SetBets(items);
                    betLines.Add(BotLogic.AnnounceBetString(who, items));
                }
            }
            if (betLines.Count > 0) Speak(string.Join(" | ", betLines));
            foreach (var actor in toKick) KickPlayer(actor);
        }

        // ——————————————— 派彩 ———————————————

        BotActor PayoutOrCollect(BlackjackPlayer player)
        {
            switch (player.result)
            {
                case BlackjackPlayer.HandResult.BLACKJACK_WIN: PayWinner(player, true); return null;
                case BlackjackPlayer.HandResult.WIN: PayWinner(player, false); return null;
                case BlackjackPlayer.HandResult.LOSE: return LootLoserBets(player);
                case BlackjackPlayer.HandResult.PUSH: player.betsProcessed = true; return null;
                default: player.betsProcessed = true; return null;
            }
        }

        void PayWinner(BlackjackPlayer player, bool blackjackBonus)
        {
            if (player.character == null) { player.betsProcessed = true; return; }
            bool isAi = BotHelpers.IsBot(player.character);

            foreach (var item in new List<MapItem>(player.bets))
            {
                int quantity = blackjackBonus ? (int)(item.Quantity * 1.5) : item.Quantity;
                for (int i = 0; i < quantity && i < 60; i++)   // 上限防单次爆量
                {
                    var position = BotHelpers.AdjustCenterPositionXAxis(
                        player.originX, player.originY, i, 2, 4, 20);
                    DropCommands.BotThrowItemToOwner(chr, item.ItemId, position.x, position.y, player.character);
                }
            }
            player.betsProcessed = true;

            if (isAi)
            {
                // AI 赢完自己把地上的押注捡回来（源端 sleep 后扫 1500 范围）
                BotTasks.Schedule(() => LootBackForAi(player), 2000 + random.Next(1500));
            }
        }

        void LootBackForAi(BlackjackPlayer player)
        {
            var who = player.character;
            if (who == null) return;
            var onFloor = BotLogic.ReadPlayersBetsStamps(who, player.originX, player.originY, 1500);
            foreach (var item in new List<MapItem>(onFloor))
                DropCommands.LootItemListOnFloor(who, new List<MapItem> { item });
        }

        BotActor LootLoserBets(BlackjackPlayer player)
        {
            var who = player.character;
            if (who == null) return null;
            double lootRadius = BotHelpers.IsBot(who) ? 1500 : 5000;

            bool potCollectible = BotLogic.CheckIfItemsOnFloorStill(player.bets,
                BotLogic.ReadPlayersBetsStamps(who, player.originX, player.originY, lootRadius));

            player.betsProcessed = true;

            if (potCollectible)
            {
                DropCommands.LootItemListOnFloor(chr, new List<MapItem>(player.bets));
                return null;
            }

            // 偷回自己的押注 → 掉名声
            who.fame -= 10;
            Speak("Player has stolen back his bet.");
            LootPlayerCards(player);
            return who;
        }

        // ——————————————— 收牌 ———————————————

        void CleanupAllCards()
        {
            foreach (var player in table.Players) LootPlayerCards(player);
        }

        void LootPlayerCards(BlackjackPlayer player)
        {
            try
            {
                if (player.cardsOnMap.Count == 0) return;
                DropCommands.LootItemListOnFloor(chr, new List<MapItem>(player.cardsOnMap));
                player.ClearCardsOnMap();
            }
            catch (Exception) { /* 源端同样吞掉收牌异常 */ }
        }

        // ——————————————— 假人下注 / 决策 ———————————————

        void TriggerArtificialPlayerBets()
        {
            int stagger = 0;
            for (int i = 1; i < table.PlayerCount; i++)
            {
                var player = table.PlayerAt(i);
                if (player == null || !BotHelpers.IsBot(player.character)) continue;
                if (player.HasBets) continue;
                var who = player.character;
                BotTasks.Schedule(() => DropCommands.BotDropItemQtyOwnerOnly(who, AiBetItem, AiBetQty), 1250 + stagger);
                stagger += 1500 + random.Next(2000);
            }
        }

        void TriggerArtificialPlayerResponse(BlackjackPlayer player)
        {
            if (!BotHelpers.IsBot(player.character)) return;
            var who = player.character;
            var botChr = who as BotCharacter;   // 只有假人才有渲染实体（真人走自己的会话）
            int handValue = player.HandValue;
            BotTasks.Schedule(() =>
            {
                bool hit = BlackjackAI.HitOrStand(handValue, random);
                string node = hit ? "HitResponse" : "StandResponse";
                if (botChr != null)
                {
                    string line = DialogueLine(node);
                    output.Speak(botChr, line ?? (hit ? "hit" : "stand"));
                }
                if (hit)
                {
                    player.SetResponseStatus("RESPONDED");
                    player.SetStatus(BlackjackPlayer.PlayerStatus.ACTIVE);
                }
                else
                {
                    player.SetResponseStatus("RESPONDED");
                    player.SetStatus(BlackjackPlayer.PlayerStatus.STAND);
                }
            }, 1250 + random.Next(1500));
        }

        static string OutcomeToDialogueNode(BlackjackOutcome outcome)
        {
            switch (outcome)
            {
                case BlackjackOutcome.WIN: return "PlayerWin";
                case BlackjackOutcome.BLACKJACK_WIN: return "PlayerBlackjack";
                case BlackjackOutcome.LOSE: return "PlayerLose";
                case BlackjackOutcome.PUSH: return "PlayerPush";
                default: return null;
            }
        }

        void TriggerAiPlayerReaction(BlackjackPlayer player, string dialogueNode)
        {
            if (!BotHelpers.IsBot(player.character)) return;
            if (player.character is not BotCharacter botChr) return;
            int emote = DialogueEmote(dialogueNode);
            if (emote > 0) output.Emote(botChr, emote);
            if (random.NextDouble() < 0.30)
            {
                string line = DialogueLine(dialogueNode);
                if (line != null) output.Speak(botChr, line);
            }
        }

        // 对话资源缺失时返回 null（源端 YAML 不在包里也会退化）
        static string DialogueLine(string node)
        {
            try { return BotTypeManager.Dialogue?.RandomLine("BlackjackDealerBotDialogue.yaml", "BLACKJACK_DEALER", node); }
            catch (Exception) { return null; }
        }

        static int DialogueEmote(string node)
        {
            try { return BotTypeManager.Dialogue?.RandomEmote("BlackjackDealerBotDialogue.yaml", "BLACKJACK_DEALER", node) ?? -1; }
            catch (Exception) { return -1; }
        }

        // ——————————————— 玩家响应 ———————————————

        void WaitForResponse(BlackjackPlayer player)
        {
            var who = player.character;
            if (who == null) return;
            if (!BotHelpers.IsBot(who) && who.mapId != chr.mapId)
            {
                Speak(player.Name + " has left. Automatic Stand.");
                player.SetResponseStatus("RESPONDED");
                player.SetStatus(BlackjackPlayer.PlayerStatus.STAND);
                return;
            }

            long endTime = playerTurnStart + 15000;   // 15s 提醒，30s 强制停牌
            if (BotScheduler.Now() >= endTime)
            {
                if (finalDecision)
                {
                    Speak("No response from " + player.Name + ". Automatic Stand.");
                    player.SetResponseStatus("RESPONDED");
                    player.SetStatus(BlackjackPlayer.PlayerStatus.STAND);
                    finalDecision = false;
                }
                else
                {
                    Speak("Please Make a decision " + player.Name);
                    if (!BotHelpers.IsBot(who)) ShowActionHint(who);
                    playerTurnStart = BotScheduler.Now();
                    finalDecision = true;
                }
                return;
            }
            ProcessBlackjackMessages(player);
        }

        void ProcessBlackjackMessages(BlackjackPlayer player)
        {
            var message = messages.Poll(QueueType.SECONDARY);
            if (message == null) return;
            if (!interactors.IsRespondant(message.sender))
            {
                messages.Add(message, QueueType.SECONDARY);
                return;
            }
            if (!ReferenceEquals(message.sender, player.character)) return;

            string content = message.content.ToLowerInvariant();
            if (content.Contains("stand") || content.Contains("stay") ||
                content.Contains("停牌") || content.Contains("不要") || content.Contains("停"))
            {
                player.SetResponseStatus("RESPONDED");
                player.SetStatus(BlackjackPlayer.PlayerStatus.STAND);
            }
            else if (content.Contains("hit") || content.Contains("要牌") || content.Contains("补牌"))
            {
                player.SetResponseStatus("RESPONDED");
                player.SetStatus(BlackjackPlayer.PlayerStatus.ACTIVE);
            }
        }

        void ProcessJoinInquiries()
        {
            var message = messages.Poll(QueueType.TERTIARY);
            if (message == null) return;
            if (!interactors.IsInquirer(message.sender))
            {
                messages.Add(message, QueueType.TERTIARY);
                return;
            }
            string content = message.content.ToLowerInvariant();
            if (!content.Contains("join") && !content.Contains("加入")) return;

            var sender = message.sender;
            if (table.IsAlreadyAtTable(sender))
            {
                output.Send(BotOutput.Kind.YellowMessage, sender, "You are already at the table.", targetId: sender.id);
                return;
            }
            if (table.AddPlayer(sender))
            {
                interactors.RemoveInquirer(sender);
                interactors.SetRespondant(sender);
                ChatBubble(sender.name + " has joined the table.");
                output.Send(BotOutput.Kind.YellowMessage, sender,
                    "You have joined " + chr.name + "'s Blackjack table. You will be in the next round.", targetId: sender.id);
            }
            else
            {
                output.Send(BotOutput.Kind.YellowMessage, sender, "Table is full.", targetId: sender.id);
            }
        }

        // ——————————————— 对外接口 ———————————————

        /// <summary>源端 joinNearestTable：找当前地图上最近的荷官并加入。</summary>
        public static string JoinNearestTable(BotActor player)
        {
            BlackjackDealerBot nearest = null;
            double nearestDistance = double.MaxValue;
            foreach (var bot in BotStore.GetAllBots().Values)
            {
                if (bot is not BlackjackDealerBot dealer) continue;
                if (dealer.chr.mapId != player.mapId) continue;
                double dx = dealer.chr.x - player.x, dy = dealer.chr.y - player.y;
                double distance = dx * dx + dy * dy;
                if (distance < nearestDistance) { nearestDistance = distance; nearest = dealer; }
            }
            if (nearest == null) return "当前地图没有可加入的 21 点荷官。请确认 21 点桌功能已开启，并重新进入自由市场。";
            return nearest.JoinPlayerFromNpc(player);
        }

        public string JoinPlayerFromNpc(BotActor player)
        {
            const string howTo = "\r\n\r\n下注方式：把赌场筹码丢在自己附近。轮到你行动时，输入“要牌”或“停牌”。";
            if (table.IsAlreadyAtTable(player)) return "你已经在这张 21 点桌里了。" + howTo;
            if (!table.AddPlayer(player)) return "这张 21 点桌已经满了。";
            interactors.SetRespondant(player);
            FaceTowards(player);
            ChatBubble(player.name + " 加入了 21 点桌。");
            return "已加入 " + chr.name + " 的 21 点桌。" + howTo;
        }

        public override void DisplayCommands(BotActor player)
            => output.ShowOptions(player, new[] { "加入" });

        void ShowActionHint(BotActor player) => output.ShowOptions(player, new[] { "停牌", "要牌" });

        void Speak(string text) { if (!string.IsNullOrEmpty(text)) output.Speak(chr, text); }
        void ChatBubble(string text) { if (!string.IsNullOrEmpty(text)) output.Send(BotOutput.Kind.Bubble, chr, text); }
        void FaceTowards(BotActor target) { if (target != null) chr.facing = target.x >= chr.x ? 1 : -1; }
    }
}
