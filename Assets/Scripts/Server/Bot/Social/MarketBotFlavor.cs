using System;
using System.Collections.Generic;

namespace Server.Bot.Social
{
    // MarketBotFlavor.java；共享最近12句记录，最多重抽16次。
    public sealed partial class MarketBotFlavor
    {
        readonly Random random;
        readonly Queue<string> recent = new Queue<string>();
        public MarketBotFlavor(Random random = null) { this.random = random ?? new Random(); }
        string Pick(string[] pool) => pool[random.Next(pool.Length)];
        string Fresh(string[] pool)
        {
            string line = Pick(pool);
            if (pool.Length > 1)
                for (int i = 0; i < 16 && recent.Contains(line); i++) line = Pick(pool);
            recent.Enqueue(line);
            while (recent.Count > 12) recent.Dequeue();
            return line;
        }
        public string Selling(string name) => Fresh(SELL_PREFIX) + " " + name + " " + Pick(SELL_SUFFIX);
        public string Buying(string name, string price)
            => Fresh(BUY_PREFIX) + " " + name + (string.IsNullOrWhiteSpace(price) ? "" : " " + price) + " " + Pick(BUY_SUFFIX);
        public string Wander() => Fresh(WANDER);
        public string Browse() => Fresh(BROWSE);
        public string Idle() => Fresh(IDLE);
        public string Reply() => Fresh(REPLY);
        public string Megaphone() => Fresh(MEGA);
        public string TradeOpen() => Fresh(TRADE_OPEN);
        public string TradeReply() => Fresh(TRADE_REPLY);
        public string Chat()
        {
            int roll = random.Next(100);
            return Fresh(roll < 50 ? CHAT : roll < 68 ? WANDER : roll < 80 ? FUNNY : roll < 88 ? SCAM : roll < 94 ? WARN : BEG);
        }
        public static int LineCount => WANDER.Length + BROWSE.Length + IDLE.Length + CHAT.Length + REPLY.Length
            + MEGA.Length + TRADE_OPEN.Length + TRADE_REPLY.Length + FUNNY.Length + BEG.Length + WARN.Length + SCAM.Length;
    }
}
