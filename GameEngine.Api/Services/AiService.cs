using GameEngine.Api.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
// Note: In a real implementation, you'd inject an ILLamaExecutor or similar from LLamaSharp
// For this scaffolding, we simulate the interface.

namespace GameEngine.Api.Services
{
    public class AiService
    {
        private readonly GameLogicService _logic;

        public AiService(GameLogicService logic)
        {
            _logic = logic;
        }

        // Simulates the structured output from the LLM
        public class AiMoveResponse
        {
            public string Reasoning { get; set; } = string.Empty;
            public string SelectedCard { get; set; } = string.Empty; // e.g., "Queen of Spades"
        }

        public async Task<(Card PlayedCard, string Reasoning)> GenerateMoveAsync(GameState state, Player aiPlayer, string pastHistoryDbSummary)
        {
            var validCards = GetValidCards(aiPlayer.Hand, state);
            
            // If only one valid card, play it immediately. No need for AI.
            if (validCards.Count == 1)
            {
                return (validCards.First(), "I only have one legal card to play.");
            }

            // Construct Prompt
            string prompt = ConstructPrompt(state, aiPlayer, validCards, pastHistoryDbSummary);

            // --- LLamaSharp Integration goes here ---
            // In the real app, we await the local ONNX/GGUF model inference
            // var responseJson = await _llamaModel.CompleteAsync(prompt, temperature: GetTemperature(aiPlayer.DifficultyLevel));
            // var aiMove = JsonSerializer.Deserialize<AiMoveResponse>(responseJson);
            
            // Simulated response for this architecture outline:
            await Task.Delay(100); // Simulate inference time
            
            Card chosenCard;
            string reasoning;
            
            if (aiPlayer.DifficultyLevel >= 3) // Intermediate and above
            {
                // STRATEGY 1: ACTIVE CANCELLATION (Double Deck Specific)
                // If a dangerous card (High rank or Penalty points) has been played, and we hold the exact duplicate, play it immediately to cancel it out!
                if (state.CurrentTrick.Count > 0)
                {
                    var dangerousCards = state.CurrentTrick.Where(c => c.PointValue > 0 || (int)c.Rank >= (int)Rank.Jack).ToList();
                    foreach (var threat in dangerousCards)
                    {
                        var duplicate = validCards.FirstOrDefault(c => c.Suit == threat.Suit && c.Rank == threat.Rank);
                        if (duplicate != null)
                        {
                            chosenCard = duplicate;
                            reasoning = $"I see a dangerous {threat} in the trick. Since this is Double Deck, I am playing my identical {duplicate} to instantly CANCEL it out and protect myself from taking the trick/points!";
                            return (chosenCard, reasoning);
                        }
                    }
                }

                // STRATEGY 2: DUMPING POINTS
                // Simple heuristic simulating a good AI: Dump highest point card if not leading and void in suit
                if (state.CurrentTrick.Count > 0 && !validCards.Any(c => c.Suit == state.CurrentTrick.First().Suit))
                {
                    var pointCard = validCards.OrderByDescending(c => c.PointValue).FirstOrDefault(c => c.PointValue > 0);
                    if (pointCard != null)
                    {
                        chosenCard = pointCard;
                        reasoning = $"The human player {pastHistoryDbSummary}. I am void in the led suit, so I am safely dumping my {chosenCard} to avoid points.";
                        return (chosenCard, reasoning);
                    }
                }
                
                // STRATEGY 3: DUCKING
                // If following suit, try to play the highest card we have that is still LOWER than the current highest card in the trick, to duck under it safely.
                if (state.CurrentTrick.Count > 0)
                {
                    var ledSuit = state.CurrentTrick.First().Suit;
                    var highestLed = state.CurrentTrick.Where(c => c.Suit == ledSuit).OrderByDescending(c => c.Rank).FirstOrDefault();
                    if (highestLed != null)
                    {
                        var duckCard = validCards.Where(c => c.Suit == ledSuit && c.Rank < highestLed.Rank).OrderByDescending(c => c.Rank).FirstOrDefault();
                        if (duckCard != null)
                        {
                            chosenCard = duckCard;
                            reasoning = $"I am deliberately playing my {duckCard} to safely duck under the {highestLed} currently winning the trick.";
                            return (chosenCard, reasoning);
                        }
                    }
                }

                // If we are LEADING the trick
                if (state.CurrentTrick.Count == 0)
                {
                    // STRATEGY 4: LEADING DOUBLES (Double Deck Specific)
                    // If we have a pair of identical safe cards (low/mid), lead one. If it gets canceled by the other identical card someone else holds, we take no points. If we hold both, we guarantee they cancel each other if played back to back.
                    var duplicates = validCards.GroupBy(c => new { c.Suit, c.Rank }).Where(g => g.Count() > 1).Select(g => g.First()).ToList();
                    var safeDuplicateLead = duplicates.OrderBy(c => c.Rank).FirstOrDefault(c => c.Suit != Suit.Hearts && c.Rank < Rank.King);
                    if (safeDuplicateLead != null)
                    {
                        chosenCard = safeDuplicateLead;
                        reasoning = $"I am leading the trick and I have two {chosenCard}s. Leading a duplicate is a strong Double Deck strategy to flush out cards or guarantee a cancellation later.";
                        return (chosenCard, reasoning);
                    }

                    // STRATEGY 5: FLUSHING SPADES
                    // Lead low/medium spades to force opponents to play their Spades, hoping to flush out the Queens early when I don't have them.
                    var safeSpade = validCards.Where(c => c.Suit == Suit.Spades && c.Rank < Rank.Queen).OrderBy(c => c.Rank).FirstOrDefault();
                    if (safeSpade != null)
                    {
                        chosenCard = safeSpade;
                        reasoning = $"I am leading the {chosenCard} to safely flush out Spades and hopefully draw out the Queens before points accumulate.";
                        return (chosenCard, reasoning);
                    }
                    
                    // Default Lead: Just play the lowest card in a non-dangerous suit.
                    var safeLead = validCards.Where(c => c.Suit != Suit.Hearts && c.Suit != Suit.Spades).OrderBy(c => c.Rank).FirstOrDefault();
                    if (safeLead != null)
                    {
                        chosenCard = safeLead;
                        reasoning = $"I am leading my lowest safe card, the {chosenCard}.";
                        return (chosenCard, reasoning);
                    }
                }
            }

            // Fallback / Default AI: Pick lowest valid card
            chosenCard = validCards.OrderBy(c => c.Rank).First();
            reasoning = $"I am playing it safe by playing my lowest valid card, the {chosenCard}.";

            // Safety Net: Ensure the card the AI picked is actually valid (in case LLM hallucinates)
            if (!validCards.Contains(chosenCard))
            {
                chosenCard = validCards.First(); // Force valid
                reasoning += " (Engine Override: AI hallucinated)";
            }

            return (chosenCard, reasoning);
        }

        public void CheckAndPlayAiTurns(GameSessionManager gameManager)
        {
            var state = gameManager.GetState();
            
            // Check if game is in passing phase and AI hasn't passed yet
            if (state.Phase == GameState.GamePhase.Passing)
            {
                foreach (var ai in state.Players.Where(p => p.IsAi && !state.PendingPasses.ContainsKey(p.Id)))
                {
                    PerformAiPass(ai, state, gameManager);
                }
                return;
            }

            // Otherwise, play card if it's an AI's turn during normal play
            if (state.Phase != GameState.GamePhase.Playing) return;

            var activePlayer = state.Players[state.CurrentTurnPlayerIndex];
            if (!activePlayer.IsAi) return;

            // This method is not provided in the snippet, assuming it exists or will be added.
            // For now, we'll just call GenerateMoveAsync and then PlayCard.
            PerformAiMove(activePlayer, state);
        }

        private void PerformAiMove(Player aiPlayer, GameState state)
        {
            // This is a placeholder. The actual AI move generation logic is in GenerateMoveAsync.
            // In a real scenario, you'd likely call GenerateMoveAsync here and then _gameManager.PlayCard.
            // For the purpose of this specific edit, we're focusing on the passing phase.
            // Example:
            // var (card, reason) = await GenerateMoveAsync(state, aiPlayer, "summary");
            // _gameManager.PlayCard(aiPlayer.Id, card);
        }

        private void PerformAiPass(Player ai, GameState state, GameSessionManager gameManager)
        {
            // Simple logic: sort hand and pass highest cards, prioritizing points depending on difficulty
            var cardsToPass = new List<Card>();
            var sortedHand = ai.Hand
                .OrderByDescending(c => c.Rank == Rank.Queen && c.Suit == Suit.Spades ? 100 : (int)c.Rank)
                .ToList();

            // At higher difficulties, AI is smarter about passing Hearts vs High Spades
            int passCount = 3;
            if (ai.DifficultyLevel > 3 && state.Rules.PassingStyle != "None")
            {
                // Expert AI passes Queen of Spades always, then highest Hearts, then highest other cards
                cardsToPass.AddRange(ai.Hand.Where(c => c.Suit == Suit.Spades && c.Rank == Rank.Queen));
                if (cardsToPass.Count < passCount)
                {
                    cardsToPass.AddRange(ai.Hand.Where(c => c.IsHeart).OrderByDescending(c => (int)c.Rank).Take(passCount - cardsToPass.Count));
                }
                if (cardsToPass.Count < passCount)
                {
                    cardsToPass.AddRange(ai.Hand.Except(cardsToPass).OrderByDescending(c => (int)c.Rank).Take(passCount - cardsToPass.Count));
                }
            }
            else
            {
                // Beginner/Intermediate just passes the three highest value cards
                cardsToPass = sortedHand.Take(3).ToList();
            }

            gameManager.PassCards(ai.Id, cardsToPass.Take(3).ToList());
        }

        private string ConstructPrompt(GameState state, Player aiPlayer, List<Card> validCards, string historySummary)
        {
            // The magic happens here: We dynamically build a prompt teaching the AI Double Deck Cancellation Hearts
            string prompt = $@"
You are playing Double Deck Cancellation Hearts.
Difficulty: {aiPlayer.DifficultyLevel}/5.
Human Player History: {historySummary}

CRITICAL STRATEGY RULES:
1. DOUBLE DECK CANCELLATION: There are two of every card. If an opponent plays an Ace (or a high card/penalty card) and you have the IDENTICAL duplicate, YOU MUST PLAY IT to cancel their card out! This is the most crucial defensive strategy.
2. If you are void in the led suit, aggressively dump your Queen of Spades or highest Hearts.
3. If you must follow suit, try to play a card just underneath the current highest card to 'duck' the trick.
4. If you are LEADING the trick, lead a low/mid card that you have TWO of (a duplicate). Or, lead low Spades to flush out the Queens.

Your Hand: {string.Join(", ", aiPlayer.Hand.Select(c => c.ToString()))}
Valid Moves: {string.Join(", ", validCards.Select(c => c.ToString()))}
Current Trick: {string.Join(", ", state.CurrentTrick.Select(c => c.ToString()))}
Hearts Broken: {state.HeartsBroken}

Output JSON with 'Reasoning' (your internal monologue) and 'SelectedCard' (must be exactly from Valid Moves).
";
            return prompt;
        }

        private List<Card> GetValidCards(List<Card> hand, GameState state)
        {
            return hand.Where(c => _logic.IsValidPlay(hand, state.CurrentTrick, c, state.HeartsBroken, state.IsFirstTrickOfHand, state.Rules).IsValid).ToList();
        }

        private float GetTemperature(int difficulty)
        {
            // Beginner = highly random (High Temp). Grandmaster = highly deterministic (Low Temp).
            return difficulty switch
            {
                1 => 0.9f, 
                2 => 0.7f,
                3 => 0.5f,
                4 => 0.3f,
                5 => 0.1f,
                _ => 0.5f
            };
        }
    }
}
