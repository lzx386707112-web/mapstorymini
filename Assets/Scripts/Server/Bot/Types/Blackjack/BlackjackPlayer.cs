using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Types.Blackjack
{
    // BotTypes/Blackjack/BlackjackPlayer.java：桌上的一个座位。
    // 赌注与牌面都以「地上掉落物」的形式存在（MapItem），这里只记引用。
    public sealed class BlackjackPlayer
    {
        public enum PlayerStatus { WAITING, ACTIVE, STAND, BUST }
        public enum HandResult { PENDING, WIN, LOSE, PUSH, BLACKJACK_WIN }

        public readonly BotActor character;
        public readonly List<string> hand = new List<string>();
        public readonly List<MapItem> bets = new List<MapItem>();
        public readonly List<MapItem> cardsOnMap = new List<MapItem>();

        public int originX, originY;
        public PlayerStatus status = PlayerStatus.WAITING;
        public HandResult result = HandResult.PENDING;
        public string responseStatus = "WAITING";
        public int skippedHands;
        public bool betsProcessed;

        public BlackjackPlayer(BotActor character)
        {
            this.character = character;
        }

        public string Name => character?.name ?? "?";
        public int HandValue => BlackjackRules.CalculateHandValue(hand);
        public bool HasResponded => responseStatus == "RESPONDED";
        public bool HasBets => bets.Count > 0;
        public void AddCardToHand(string card) => hand.Add(card);

        public void SetBets(List<MapItem> newBets)
        {
            bets.Clear();
            if (newBets != null) bets.AddRange(newBets);
        }

        public void AddCardOnMap(MapItem card) { if (card != null) cardsOnMap.Add(card); }
        public void ClearCardsOnMap() => cardsOnMap.Clear();

        public void SetStatus(PlayerStatus value) { status = value; }
        public void SetResult(HandResult value) { result = value; }
        public void SetResponseStatus(string value) { responseStatus = value; }

        public void SetOriginLocation(int x, int y) { originX = x; originY = y; }

        /// <summary>源端 skipHand：连续 4 次没下注 → 该被踢下桌。</summary>
        public bool SkipHand() => ++skippedHands > 3;

        public void ResetSkippedHands() => skippedHands = 0;

        public void ResetForNewRound()
        {
            hand.Clear();
            bets.Clear();
            cardsOnMap.Clear();
            status = PlayerStatus.WAITING;
            result = HandResult.PENDING;
            responseStatus = "WAITING";
            betsProcessed = false;
        }
    }
}
