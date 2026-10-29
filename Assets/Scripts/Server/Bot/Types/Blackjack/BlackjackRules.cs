using System.Collections.Generic;

namespace Server.Bot.Types.Blackjack
{
    // BotTypes/Blackjack/BlackjackRules.java 逐条对齐。
    // 牌面用 "2H"/"TS"/"AD" 这种两字符编码（rank+suit），rank 取 card[0]。
    public enum BlackjackOutcome { WIN, LOSE, PUSH, BLACKJACK_WIN }

    public static class BlackjackRules
    {
        /// <summary>源端 calculateHandValue：A 先按 11 计，爆了再逐张降为 1。</summary>
        public static int CalculateHandValue(List<string> hand)
        {
            int total = 0, aces = 0;
            foreach (var card in hand)
            {
                switch (card[0])
                {
                    case 'A': aces++; total += 11; break;
                    case 'K': case 'Q': case 'J': case 'T': total += 10; break;
                    default: total += card[0] - '0'; break;
                }
            }
            while (total > 21 && aces > 0) { total -= 10; aces--; }
            return total;
        }

        public static bool IsBlackjack(List<string> hand) => hand.Count == 2 && CalculateHandValue(hand) == 21;

        public static bool IsBust(List<string> hand) => CalculateHandValue(hand) > 21;

        public static bool ShouldDealerHit(int handValue) => handValue < 17;

        /// <summary>源端 getCardDisplayValue：2..10 原值，J/Q/K/A = 11/12/13/14（用于挑卡面图）。</summary>
        public static int GetCardDisplayValue(string card)
        {
            switch (card[0])
            {
                case '2': return 2;
                case '3': return 3;
                case '4': return 4;
                case '5': return 5;
                case '6': return 6;
                case '7': return 7;
                case '8': return 8;
                case '9': return 9;
                case 'T': return 10;
                case 'J': return 11;
                case 'Q': return 12;
                case 'K': return 13;
                case 'A': return 14;
                default: throw new System.ArgumentException("Invalid card rank: " + card[0]);
            }
        }

        public static BlackjackOutcome DetermineOutcome(List<string> playerHand, List<string> dealerHand)
        {
            int playerValue = CalculateHandValue(playerHand);
            int dealerValue = CalculateHandValue(dealerHand);
            bool playerBj = IsBlackjack(playerHand), dealerBj = IsBlackjack(dealerHand);

            if (playerValue > 21) return BlackjackOutcome.LOSE;
            if (dealerValue > 21) return BlackjackOutcome.WIN;
            if (playerBj && dealerBj) return BlackjackOutcome.PUSH;
            if (playerBj) return BlackjackOutcome.BLACKJACK_WIN;
            if (dealerBj) return BlackjackOutcome.LOSE;
            if (playerValue == dealerValue) return BlackjackOutcome.PUSH;
            return playerValue > dealerValue ? BlackjackOutcome.WIN : BlackjackOutcome.LOSE;
        }

        public static int CalculatePayout(int betQuantity, BlackjackOutcome outcome)
        {
            switch (outcome)
            {
                case BlackjackOutcome.BLACKJACK_WIN: return (int)(betQuantity * 1.5);
                case BlackjackOutcome.WIN: return betQuantity;
                default: return 0;
            }
        }

        public static string FormatOutcomeMessage(string playerName, BlackjackOutcome outcome,
            List<string> playerHand, List<string> dealerHand)
        {
            int playerValue = CalculateHandValue(playerHand);
            int dealerValue = CalculateHandValue(dealerHand);
            switch (outcome)
            {
                case BlackjackOutcome.LOSE:
                    if (playerValue > 21) return playerName + ": Busted.";
                    if (IsBlackjack(dealerHand)) return playerName + ": Lose. Dealer Blackjack.";
                    return playerName + ": Lose. Dealer wins.";
                case BlackjackOutcome.WIN:
                    if (dealerValue > 21) return playerName + ": Win! Dealer busts.";
                    return playerName + ": Win!";
                case BlackjackOutcome.BLACKJACK_WIN:
                    return playerName + ": Blackjack!";
                case BlackjackOutcome.PUSH:
                    return playerName + ": Push.";
                default:
                    return playerName + ": Unexpected result.";
            }
        }
    }
}
