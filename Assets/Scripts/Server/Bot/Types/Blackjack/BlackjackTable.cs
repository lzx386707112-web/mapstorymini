using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Types.Blackjack
{
    // BotTypes/Blackjack/BlackjackTable.java：牌桌状态机（阶段、座位、发牌、结算）。
    public sealed class BlackjackTable
    {
        public enum Phase
        {
            WAITING, BETTING, PROCESSING_BETS, DEALING, PLAYER_TURNS,
            PLAYER_RESPONSE, DEALER_TURN, RESOLVING, PAYOUT
        }

        public const int MaxPlayers = 8; // 含 index 0 的荷官

        readonly Deck deck;
        readonly List<BlackjackPlayer> players = new List<BlackjackPlayer>(MaxPlayers);
        public Phase phase { get; private set; }
        public int currentPlayerIndex { get; private set; }
        public int waitCount { get; private set; }

        public BlackjackTable(BotActor dealerCharacter, System.Random random = null)
        {
            deck = new Deck(random);
            players.Add(new BlackjackPlayer(dealerCharacter)); // index 0 = 荷官
            phase = Phase.WAITING;
            currentPlayerIndex = 1;
        }

        public void SetPhase(Phase value) => phase = value;

        public IReadOnlyList<BlackjackPlayer> Players => players;
        public int PlayerCount => players.Count;
        public BlackjackPlayer Dealer => players[0];
        public BlackjackPlayer PlayerAt(int index) => index >= 0 && index < players.Count ? players[index] : null;
        public BlackjackPlayer CurrentPlayer => PlayerAt(currentPlayerIndex);
        public bool IsCurrentPlayerDealer => currentPlayerIndex == 0;
        public bool HasEnoughPlayers => players.Count > 1;

        public bool AddPlayer(BotActor character)
        {
            if (players.Count >= MaxPlayers) return false;
            players.Add(new BlackjackPlayer(character));
            return true;
        }

        public bool RemovePlayer(BotActor character)
        {
            for (int i = 1; i < players.Count; i++) // 永远不移除 index 0 的荷官
            {
                if (players[i].character != character) continue;
                players.RemoveAt(i);
                // 保持 currentPlayerIndex 指向「同一个位置」：
                //   移除的人在当前位置之前 → 下标前移
                //   在当前位置或之后 → 不变（后一个顶上来了）
                //   越界 → 回绕到荷官（0），表示该荷官行动
                if (currentPlayerIndex > i) currentPlayerIndex--;
                if (currentPlayerIndex >= players.Count) currentPlayerIndex = 0;
                return true;
            }
            return false;
        }

        public void IncrementToNextPlayer()
        {
            currentPlayerIndex++;
            if (currentPlayerIndex >= players.Count) currentPlayerIndex = 0;
        }

        public void ResetForNewRound()
        {
            foreach (var player in players) player.ResetForNewRound();
            currentPlayerIndex = 1;
            waitCount = 0;
        }

        public void IncrementWaitCount() => waitCount++;

        public bool HasAtLeastOneBet()
        {
            for (int i = 1; i < players.Count; i++) if (players[i].HasBets) return true;
            return false;
        }

        /// <summary>源端 getDealSequenceIndex：先发给所有玩家，最后一张给荷官。</summary>
        public static int GetDealSequenceIndex(int index, int totalPlayers)
        {
            if (index == totalPlayers - 1) return 0;
            return index % (totalPlayers - 1) + 1;
        }

        public string DrawCard() => deck.Draw();

        public void DealCardToPlayer(BlackjackPlayer player, string card) => player.AddCardToHand(card);

        public bool ShouldDealerHit() => BlackjackRules.ShouldDealerHit(Dealer.HandValue);

        public void ResolveAllOutcomes()
        {
            var dealerHand = Dealer.hand;
            for (int i = 1; i < players.Count; i++)
            {
                var player = players[i];
                if (player.hand.Count == 0) continue;
                switch (BlackjackRules.DetermineOutcome(player.hand, dealerHand))
                {
                    case BlackjackOutcome.WIN: player.SetResult(BlackjackPlayer.HandResult.WIN); break;
                    case BlackjackOutcome.LOSE: player.SetResult(BlackjackPlayer.HandResult.LOSE); break;
                    case BlackjackOutcome.PUSH: player.SetResult(BlackjackPlayer.HandResult.PUSH); break;
                    case BlackjackOutcome.BLACKJACK_WIN: player.SetResult(BlackjackPlayer.HandResult.BLACKJACK_WIN); break;
                }
            }
        }

        public bool IsAlreadyAtTable(BotActor character)
        {
            foreach (var player in players) if (player.character == character) return true;
            return false;
        }
    }
}
