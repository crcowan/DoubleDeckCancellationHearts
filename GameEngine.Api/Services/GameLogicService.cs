using GameEngine.Api.Models;
using System.Collections.Generic;
using System.Linq;

namespace GameEngine.Api.Services
{
    public class GameLogicService
    {
        public (bool IsValid, string ErrorMessage) IsValidPlay(List<Card> hand, List<Card> currentTrick, Card playedCard, bool heartsBroken, bool isFirstTrickOfHand, GameRules rules)
        {
            if (!hand.Contains(playedCard))
                return (false, "You do not have this card in your hand.");

            if (currentTrick.Count == 0)
            {
                // Leading a trick
                if (isFirstTrickOfHand)
                {
                    if (rules.FirstLead == "2OfClubs" && hand.Any(c => c.Suit == Suit.Clubs && c.Rank == Rank.Two))
                    {
                        // If they have the 2 of clubs and are leading the first trick, they MUST lead it
                        if (playedCard.Suit != Suit.Clubs || playedCard.Rank != Rank.Two)
                            return (false, "You must lead the 2 of Clubs on the first trick.");
                    }

                    // Usually you cannot lead points on the first trick
                    if (playedCard.IsHeart || playedCard.IsQueenOfSpades)
                        return (false, "You cannot lead penalty cards (Hearts or the Queen of Spades) on the first trick.");
                }
                
                if (playedCard.IsHeart && !heartsBroken && rules.BreakingHearts == "Standard")
                {
                    // Can only lead hearts if broken OR if they ONLY hold hearts
                    if (!hand.All(c => c.IsHeart))
                        return (false, "Hearts have not been broken yet. You must lead a different suit.");
                }
                
                return (true, string.Empty);
            }

            // Must follow suit if possible
            Suit ledSuit = currentTrick.First().Suit;
            bool hasSuit = hand.Any(c => c.Suit == ledSuit);
            
            if (hasSuit && playedCard.Suit != ledSuit)
            {
                return (false, $"You must follow suit. Please play a card of suit {ledSuit}.");
            }

            // If first trick, usually cannot dump points (unless no other choice)
            if (isFirstTrickOfHand && playedCard.PointValue > 0)
            {
                // Strictly speaking, if they have no other choice, they must play it, 
                // but some variations prohibit dumping points on trick 1 completely.
                // We'll allow it if they have no non-point cards.
                if (!hand.All(c => c.PointValue > 0))
                    return (false, "You cannot play penalty cards on the first trick unless you have no other choice.");
            }

            return (true, string.Empty);
        }

        public (int WinningPlayerIndex, bool IsCancelled, int TrickPoints) EvaluateTrick(List<Card> trickCards, int leadingPlayerIndex, int totalPlayers, GameRules rules)
        {
            if (trickCards.Count == 0) return (-1, false, 0);

            Suit ledSuit = trickCards.First().Suit;
            var activeCards = new List<(Card Card, int PlayerIndex)>();

            for (int i = 0; i < trickCards.Count; i++)
            {
                int pIndex = (leadingPlayerIndex + i) % totalPlayers;
                activeCards.Add((trickCards[i], pIndex));
            }

            // Cancellation Phase
            var groups = activeCards.GroupBy(c => new { c.Card.Suit, c.Card.Rank }).ToList();
            var uncancelledCards = new List<(Card Card, int PlayerIndex)>();

            foreach (var group in groups)
            {
                if (group.Count() == 1) // Only count single instances
                {
                    uncancelledCards.Add(group.First());
                }
                // If Count == 2, they perfectly cancel out.
                // If Count > 2, it shouldn't happen with 2 decks, but logic holds.
            }

            // If ALL cards of the led suit were cancelled
            var validSuitCards = uncancelledCards.Where(c => c.Card.Suit == ledSuit).ToList();
            
            if (validSuitCards.Count == 0)
            {
                // The trick is entirely cancelled. No one wins it. Set aside.
                return (-1, true, 0); 
            }

            // Highest uncancelled card of the led suit wins
            var winningCard = validSuitCards.OrderByDescending(c => c.Card.Rank).First();
            
            // Calculate points in THIS trick (and any cancelled kitty)
            int trickPoints = CalculateTricksPoints(trickCards);

            return (winningCard.PlayerIndex, false, trickPoints);
        }

        public int CalculateTricksPoints(List<Card> capturedCards)
        {
            return capturedCards.Sum(c => c.PointValue);
        }
        
        // Optional logic for "Shooting the Moon" (getting all 52 points from two decks)
        public bool DidShootTheMoon(int playerScore)
        {
            return playerScore == 52; 
        }
    }
}
