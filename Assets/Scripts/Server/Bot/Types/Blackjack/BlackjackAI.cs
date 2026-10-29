namespace Server.Bot.Types.Blackjack
{
    // BotTypes/Blackjack/BlackjackAI.java：11 以下必补，17 以上基本停，中间按概率补。
    public static class BlackjackAI
    {
        public static bool HitOrStand(int handValue, System.Random random)
        {
            if (handValue <= 11) return true;
            if (handValue <= 16) return random.NextDouble() < (17.0 - handValue) / 10.0;
            if (handValue == 17) return random.NextDouble() < 0.1;
            return false;
        }
    }
}
