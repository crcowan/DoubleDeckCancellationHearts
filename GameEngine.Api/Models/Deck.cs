using System;
using System.Collections.Generic;
using System.Linq;

namespace GameEngine.Api.Models
{
    public class Deck
    {
        private readonly List<Card> _cards;
        private readonly Random _rng = new();

        public Deck()
        {
            _cards = GenerateDoubleDeck();
            Shuffle();
        }

        private List<Card> GenerateDoubleDeck()
        {
            var deck = new List<Card>();
            // Two standard 52-card decks
            for (int d = 0; d < 2; d++)
            {
                foreach (Suit suit in Enum.GetValues(typeof(Suit)))
                {
                    foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                    {
                        deck.Add(new Card(suit, rank));
                    }
                }
            }
            return deck;
        }

        public void Shuffle()
        {
            int n = _cards.Count;
            while (n > 1)
            {
                n--;
                int k = _rng.Next(n + 1);
                Card value = _cards[k];
                _cards[k] = _cards[n];
                _cards[n] = value;
            }
        }

        /// <summary>
        /// Deals the deck out to a specified number of hands.
        /// Any remaining cards (the "kitty") are returned separately.
        /// </summary>
        public (List<List<Card>> Hands, List<Card> Kitty) Deal(int numPlayers)
        {
            var hands = new List<List<Card>>();
            for (int i = 0; i < numPlayers; i++)
            {
                hands.Add(new List<Card>());
            }

            int cardsPerPlayer = _cards.Count / numPlayers;
            int totalDealt = cardsPerPlayer * numPlayers;

            for (int i = 0; i < totalDealt; i++)
            {
                hands[i % numPlayers].Add(_cards[i]);
            }

            // Cards remaining go to the kitty
            var kitty = _cards.Skip(totalDealt).ToList();

            // Sort hands by suit then rank for convenience
            foreach (var hand in hands)
            {
                hand.Sort((c1, c2) => 
                {
                    int suitComparison = c1.Suit.CompareTo(c2.Suit);
                    if (suitComparison != 0) return suitComparison;
                    return c1.Rank.CompareTo(c2.Rank);
                });
            }

            return (hands, kitty);
        }
    }
}
