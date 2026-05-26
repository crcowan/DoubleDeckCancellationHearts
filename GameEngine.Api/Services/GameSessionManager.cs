using GameEngine.Api.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameEngine.Api.Services
{
    public class GameSessionManager
    {
        private GameState _state = new();
        private readonly GameLogicService _logic;
        private readonly LlmModelManager _llmModelManager;
        private readonly LlmInferenceService _llmInferenceService;
        private readonly object _stateLock = new object();

        public GameSessionManager(GameLogicService logic, LlmModelManager llmModelManager, LlmInferenceService llmInferenceService)
        {
            _logic = logic;
            _llmModelManager = llmModelManager;
            _llmInferenceService = llmInferenceService;
        }

        public GameState GetState() 
        {
            lock (_stateLock) { return _state; }
        }

        public void ResetState()
        {
            lock (_stateLock) { _state = new GameState(); }
        }

        public void InitializeGame(List<Player> players, GameRules rules, bool showAiReasoning = true, bool heuristicBypassEnabled = true)
        {
            lock (_stateLock)
            {
            var initialPhase = rules.PassingStyle == "None" ? GameState.GamePhase.Playing : GameState.GamePhase.Passing;

            _state = new GameState
            {
                GameId = Guid.NewGuid().ToString(),
                Players = players,
                Rules = rules,
                Phase = initialPhase,
                ShowAiReasoning = showAiReasoning,
                HeuristicBypassEnabled = heuristicBypassEnabled
            };

            // Hook into LLM downloading on the very first start if it's missing (and not using Ollama)
            if (players.Any(p => p.IsAi) && !_llmInferenceService.UseOllama && !_llmModelManager.IsServerDownloaded())
            {
                _state.Phase = GameState.GamePhase.DownloadingModel;
                _state.LlmDownloadStatus = "Initializing AI Download...";
                
                // Detach download task to run in background
                _ = Task.Run(async () => {
                    _llmModelManager.OnDownloadProgressChanged += HandleDownloadProgress;
                    try {
                        await _llmModelManager.DownloadServerAsync();
                        lock (_stateLock) {
                            _state.Phase = initialPhase;
                        }
                    } 
                    catch (Exception ex) {
                        lock (_stateLock) {
                            _state.LlmDownloadStatus = "Failed: " + ex.Message;
                        }
                    }
                    finally {
                        _llmModelManager.OnDownloadProgressChanged -= HandleDownloadProgress;
                    }
                });
            }

            var deck = new Deck();
            var dealResult = deck.Deal(players.Count);

            for (int i = 0; i < players.Count; i++)
            {
                _state.Players[i].Hand = dealResult.Hands[i];
            }

            _state.SetupKitty = dealResult.Kitty;

            // Randomly select the first dealer
            var rnd = new Random();
            _state.DealerPlayerIndex = rnd.Next(players.Count);

            if (rules.FirstLead == "2OfClubs")
            {
                // Find the first player holding a 2 of Clubs
                var twoOfClubsHolder = _state.Players.FindIndex(p => p.Hand.Any(c => c.Suit == Suit.Clubs && c.Rank == Rank.Two));
                if (twoOfClubsHolder != -1)
                {
                    _state.CurrentTurnPlayerIndex = twoOfClubsHolder;
                    _state.LeadingPlayerIndex = twoOfClubsHolder;
                }
                else
                {
                    // Fallback if no 2 of clubs somehow (e.g. in Kitty), left of dealer
                    int nextLeader = (_state.DealerPlayerIndex + 1) % _state.Players.Count;
                    _state.CurrentTurnPlayerIndex = nextLeader;
                    _state.LeadingPlayerIndex = nextLeader;
                }
            }
            else
            {
                // Simple first lead logic: The player to the left of the dealer starts
                int nextLeader = (_state.DealerPlayerIndex + 1) % _state.Players.Count;
                _state.CurrentTurnPlayerIndex = nextLeader;
                _state.LeadingPlayerIndex = nextLeader;
            }
            }
        }

        private void HandleDownloadProgress(double percentage, string message)
        {
            lock (_stateLock)
            {
                _state.LlmDownloadProgress = percentage;
                _state.LlmDownloadStatus = message;
            }
        }

        public void DealNewHand()
        {
            lock (_stateLock)
            {
                if (_state == null) return;

            var rnd = new Random();
            var deck = new Deck();
            var dealResult = deck.Deal(_state.Players.Count);

            for (int i = 0; i < _state.Players.Count; i++)
            {
                _state.Players[i].Hand = dealResult.Hands[i];
                _state.Players[i].CapturedCards.Clear();
                _state.Players[i].HandScore = 0; // Reset hand score for the new hand
                if (_state.Players[i].IsAi) 
                {
                    _state.Players[i].SkillOffset = rnd.NextDouble() - 0.5;
                }
            }

            _state.SetupKitty = dealResult.Kitty;
            _state.CancelledKitty.Clear();
            _state.KittyTakenByName = null; // Reset kitty notification
            _state.CurrentTrick.Clear();
            _state.HeartsBroken = false;
            _state.IsFirstTrickOfHand = true;
            _state.MemoryTracker = new AiMemoryTracker(); // Reset memory every new hand
            _state.LastMoveReasoning.Clear();

            // Rotate Dealer Left:
            _state.DealerPlayerIndex = (_state.DealerPlayerIndex + 1) % _state.Players.Count;
            int nextLeader = (_state.DealerPlayerIndex + 1) % _state.Players.Count;

            if (_state.Rules.FirstLead == "2OfClubs")
            {
                var twoOfClubsHolder = _state.Players.FindIndex(p => p.Hand.Any(c => c.Suit == Suit.Clubs && c.Rank == Rank.Two));
                if (twoOfClubsHolder != -1) nextLeader = twoOfClubsHolder;
            }

            _state.CurrentTurnPlayerIndex = nextLeader;
            _state.LeadingPlayerIndex = nextLeader;
            _state.Phase = _state.Rules.PassingStyle == "None" ? GameState.GamePhase.Playing : GameState.GamePhase.Passing;
            _state.RoundNumber++;
            }
        }

        public (bool Success, string ErrorMessage) PassCards(string playerId, List<Card> cards)
        {
            lock (_stateLock)
            {
                if (_state.Phase != GameState.GamePhase.Passing) return (false, "Not currently in the passing phase.");
            
            var player = _state.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return (false, "Player not found");
            if (_state.PendingPasses.ContainsKey(playerId)) return (false, "You have already passed your cards.");
            
            bool isKeepRound = _state.RoundNumber % 4 == 0;
            if (isKeepRound && cards.Count != 0) return (false, "You cannot pass cards during a Keep round.");
            if (!isKeepRound && cards.Count != 3) return (false, "You must pass exactly 3 cards.");
            
            // Verify cards were in hand
            foreach (var c in cards)
            {
                if (!player.Hand.Contains(c)) return (false, "You do not own the cards you are trying to pass.");
            }

            // Stage cards
            _state.PendingPasses[playerId] = cards;
            
            // Remove from hand immediately
            foreach (var c in cards)
            {
                player.Hand.Remove(c);
            }

                // Check if everyone has passed
                if (_state.PendingPasses.Count == _state.Players.Count)
                {
                    ExecutePassSwaps();
                }

                return (true, string.Empty);
            }
        }

        private void ExecutePassSwaps()
        {
            int playerCount = _state.Players.Count;
            // Determine direction based on RoundNumber: 1=Left, 2=Right, 3=Across, 4=Hold
            // If Double Deck, maybe they just loop L/R/A
            int offset = 0;
            int passCycle = _state.RoundNumber % 4; // 1, 2, 3, 0(4)

            if (passCycle == 1) offset = 1; // Pass Left
            else if (passCycle == 2) offset = playerCount - 1; // Pass Right
            else if (passCycle == 3) offset = playerCount / 2; // Pass Across
            else if (passCycle == 0) // Hold (No passing)
            {
                // Give them back their own cards
                foreach (var p in _state.Players)
                {
                    p.Hand.AddRange(_state.PendingPasses[p.Id]);
                }
                _state.PendingPasses.Clear();
                _state.LastMoveReasoning.Clear(); // Clear passing reasoning before trick 1 starts
                _state.Phase = GameState.GamePhase.Playing;
                return;
            }

            for (int i = 0; i < playerCount; i++)
            {
                int receiverIndex = (i + offset) % playerCount;
                var giverId = _state.Players[i].Id;
                var receiver = _state.Players[receiverIndex];

                var passedCards = _state.PendingPasses[giverId];
                receiver.Hand.AddRange(passedCards);

                // Memory Tracking: Giver knows exactly what cards they passed to the Receiver
                if (!_state.MemoryTracker.KnownCards.ContainsKey(giverId))
                    _state.MemoryTracker.KnownCards[giverId] = new Dictionary<string, HashSet<Card>>();
                
                if (!_state.MemoryTracker.KnownCards[giverId].ContainsKey(receiver.Id))
                    _state.MemoryTracker.KnownCards[giverId][receiver.Id] = new HashSet<Card>();

                foreach (var c in passedCards)
                {
                    _state.MemoryTracker.KnownCards[giverId][receiver.Id].Add(c);
                }
            }

            _state.PendingPasses.Clear();
            _state.LastMoveReasoning.Clear(); // Clear passing reasoning before trick 1 starts
            _state.Phase = GameState.GamePhase.Playing;
        }

        public (bool Success, string ErrorMessage) PlayCard(string playerId, Card card)
        {
            lock (_stateLock)
            {
                // Verify it's the player's turn
                var activePlayer = _state.Players[_state.CurrentTurnPlayerIndex];
            if (activePlayer.Id != playerId) return (false, "It is not your turn.");

            // Validate Move
            var validation = _logic.IsValidPlay(
                activePlayer.Hand, 
                _state.CurrentTrick, 
                card, 
                _state.HeartsBroken, 
                _state.IsFirstTrickOfHand,
                _state.Rules
            );

            if (!validation.IsValid) return (false, validation.ErrorMessage);

            // Apply Move
            activePlayer.Hand.Remove(card);
            _state.CurrentTrick.Add(card);

            if (card.IsHeart) 
            {
                _state.HeartsBroken = true;
                _state.MemoryTracker.PenaltyHeartsPlayed++;
            }
            if (card.IsQueenOfSpades) 
            {
                _state.HeartsBroken = true;
                _state.MemoryTracker.QueensOfSpadesPlayed++;
            }

            // Memory Tracking: Remove this card from all 'KnownCards' memory as it is now played
            foreach (var perceiverHandKnowledge in _state.MemoryTracker.KnownCards.Values)
            {
                foreach (var knownSet in perceiverHandKnowledge.Values)
                {
                    knownSet.Remove(card);
                }
            }

            // Check for Suit Voids (If this is not the led card, and suit doesn't match led suit)
            if (_state.CurrentTrick.Count > 1) 
            {
                var ledSuit = _state.CurrentTrick.First().Suit;
                if (card.Suit != ledSuit) 
                {
                    if (!_state.MemoryTracker.PlayerVoids.ContainsKey(playerId)) 
                    {
                        _state.MemoryTracker.PlayerVoids[playerId] = new HashSet<Suit>();
                    }
                    _state.MemoryTracker.PlayerVoids[playerId].Add(ledSuit);
                }
            }

            // Check if trick is complete
            if (_state.CurrentTrick.Count == _state.Players.Count)
            {
                _state.Phase = GameState.GamePhase.TrickPending;
            }
            else
            {
                // Move to next player
                _state.CurrentTurnPlayerIndex = (_state.CurrentTurnPlayerIndex + 1) % _state.Players.Count;
            }

                return (true, string.Empty);
            }
        }

        public void CompleteTrick()
        {
            lock (_stateLock)
            {
                if (_state.Phase != GameState.GamePhase.TrickPending) return;

            var result = _logic.EvaluateTrick(_state.CurrentTrick, _state.LeadingPlayerIndex, _state.Players.Count, _state.Rules);

            int totalTrickPoints = 0;
            if (!result.IsCancelled)
            {
                totalTrickPoints = result.TrickPoints;
                if (_state.CancelledKitty.Count > 0) totalTrickPoints += _logic.CalculateTricksPoints(_state.CancelledKitty);
                if (_state.IsFirstTrickOfHand && _state.SetupKitty.Count > 0) totalTrickPoints += _logic.CalculateTricksPoints(_state.SetupKitty);
            }

            _state.PreviousTrick = new TrickSummary
            {
                Trick = new List<Card>(_state.CurrentTrick),
                LeadingPlayerIndex = _state.LeadingPlayerIndex,
                IsCancelled = result.IsCancelled,
                WinningPlayerIndex = result.IsCancelled ? -1 : result.WinningPlayerIndex,
                TrickPoints = totalTrickPoints
            };

            if (result.IsCancelled)
            {
                // All cards cancelled. Keep them in the "cancelled kitty" for the next winner.
                _state.CancelledKitty.AddRange(_state.CurrentTrick);
                
                if (_state.Rules.CancellationWinner == "TrickLeader")
                {
                     // Trick Leader wins the cancelled pile
                     _state.CurrentTurnPlayerIndex = _state.LeadingPlayerIndex;
                }
                else
                {
                     // Previous winner wins the trick (or whoever led this trick if it's the first one)
                     _state.CurrentTurnPlayerIndex = _state.LeadingPlayerIndex;
                }
            }
            else
            {
                // Normal Trick Winner
                int winnerIndex = result.WinningPlayerIndex;
                var winner = _state.Players[winnerIndex];

                winner.CapturedCards.AddRange(_state.CurrentTrick);
                winner.CapturedCards.AddRange(_state.CancelledKitty);
                
                // If this is the first trick, they also get the setup kitty
                if (_state.IsFirstTrickOfHand && _state.SetupKitty.Count > 0)
                {
                    winner.CapturedCards.AddRange(_state.SetupKitty);
                    _state.KittyTakenByName = winner.Name; // Track who took it for UI
                    _state.SetupKitty.Clear();
                }

                // Apply points immediately for real-time scoring
                winner.Score += totalTrickPoints;
                winner.HandScore += totalTrickPoints;
                winner.MatchTricksWon++;
                _state.MatchTricksPlayed++;

                _state.CancelledKitty.Clear();
                _state.CurrentTurnPlayerIndex = winnerIndex;
                _state.LeadingPlayerIndex = winnerIndex;
            }

            // --- Record Trick Outcome into AI Strategy State ---
            // Build a compact summary before we clear the trick
            int trickNumber = _state.Players.Max(p => p.CapturedCards.Count / Math.Max(1, _state.Players.Count));
            string ledCardShort = _state.CurrentTrick.Count > 0
                ? $"{(int)_state.CurrentTrick.First().Rank}{_state.CurrentTrick.First().Suit.ToString()[0]}"
                : "?";

            string trickSummary;
            if (result.IsCancelled)
            {
                trickSummary = $"T{trickNumber}: Led {ledCardShort}, Cancelled.";
            }
            else
            {
                var winnerName = _state.Players[result.WinningPlayerIndex].Name;
                int pts = result.TrickPoints;
                trickSummary = $"T{trickNumber}: Led {ledCardShort}, {winnerName} won({pts}pts).";
            }

            foreach (var p in _state.Players.Where(p => p.IsAi))
            {
                if (!_state.MemoryTracker.PlayerStrategies.ContainsKey(p.Id))
                    _state.MemoryTracker.PlayerStrategies[p.Id] = new AiStrategyState();
                
                var strat = _state.MemoryTracker.PlayerStrategies[p.Id];
                strat.RecentTrickLog.Add(trickSummary);
                // Rolling window: keep only the 5 most recent to stay within token budget
                while (strat.RecentTrickLog.Count > 5)
                    strat.RecentTrickLog.RemoveAt(0);
            }
            // --- End Trick Outcome Recording ---

            _state.CurrentTrick.Clear();
            _state.IsFirstTrickOfHand = false;
            _state.LastMoveReasoning.Clear(); // Clear reasoning for the next trick

            // Check Hand End
            if (_state.Players[0].Hand.Count == 0)
            {
                CheckMoonShooting();
                
                // Check Match Over Condition
                if (_state.Players.Any(p => p.Score >= _state.Rules.TargetScore))
                {
                    _state.Phase = GameState.GamePhase.MatchOver;
                }
                else
                {
                    _state.Phase = GameState.GamePhase.GameOver;
                }
            }
            else
            {
                // We keep the state as TrickPending. The frontend will hit 'resolve-trick' again or a new 'next-trick' endpoint to continue.
                // Wait, actually, CompleteTrick is called BY the frontend when it's done reviewing the trick.
                    // So now we transition from TrickPending back to Playing!
                    _state.Phase = GameState.GamePhase.Playing;
                }
            }
        }

        private void CheckMoonShooting()
        {
            foreach (var p in _state.Players)
            {
                if (_logic.DidShootTheMoon(p.CapturedCards))
                {
                    // Special Double Deck Moon: usually score goes down or others go up massively.
                    // For now, reverse the 52 points and subtract 52 instead.
                    p.Score -= 104;
                    p.HandScore -= 104;
                    _state.ShooterOfMoonId = p.Id;
                }
            }
        }
    }
}
