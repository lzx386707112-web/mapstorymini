using System;
using System.Collections.Generic;

namespace Server.Bot.Types.Blackjack
{
    // BotTypes/Blackjack/Deck.java：52 张，抽空自动重洗。
    public sealed class Deck
    {
        static readonly char[] Suits = { 'H', 'D', 'C', 'S' };
        static readonly char[] Ranks = { '2', '3', '4', '5', '6', '7', '8', '9', 'T', 'J', 'Q', 'K', 'A' };

        readonly Random random;
        readonly List<string> cards;

        public Deck(Random random = null)
        {
            this.random = random ?? new Random();
            cards = CreateAndShuffle();
        }

        List<string> CreateAndShuffle()
        {
            var deck = new List<string>(52);
            foreach (char suit in Suits)
                foreach (char rank in Ranks)
                    deck.Add(new string(new[] { rank, suit }));
            // Fisher-Yates：源端用 Collections.shuffle（也是 Fisher-Yates）。
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            return deck;
        }

        public string Draw()
        {
            if (cards.Count == 0) cards.AddRange(CreateAndShuffle());
            string card = cards[0];
            cards.RemoveAt(0);
            return card;
        }

        public int Remaining => cards.Count;

        public void Reshuffle()
        {
            cards.Clear();
            cards.AddRange(CreateAndShuffle());
        }
    }
}
