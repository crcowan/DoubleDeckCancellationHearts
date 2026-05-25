using System.Collections.Generic;

namespace GameEngine.Api.Models
{
    public class Player
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsAi { get; set; }
        public int DifficultyLevel { get; set; } // 1 (Beginner) to 5 (Grand Master)
        public double SkillOffset { get; set; } // -0.5 to +0.5 random variance
        public List<Card> Hand { get; set; } = new();
        public List<Card> CapturedCards { get; set; } = new();
        public int Score { get; set; } // Running total score for the match
        public int HandScore { get; set; } // Score specific to the current hand
        public int MatchTricksWon { get; set; } = 0;

        public string GetPlayStyleProfile(int matchTricksPlayed, int playerCount)
        {
            // Require at least a full hand's worth of data before jumping to conclusions
            if (matchTricksPlayed < 10) return "Bal"; 
            
            double averageTricks = (double)matchTricksPlayed / playerCount;
            
            if (MatchTricksWon > averageTricks * 1.5) return "Agg";
            if (MatchTricksWon < averageTricks * 0.5) return "Def";
            return "Bal";
        }
    }

    public class AiStrategyState
    {
        // The bot's current high-level strategy (e.g., "ShootTheMoon", "PlaySafe", "DumpPoints")
        public string ActiveStrategy { get; set; } = "PlaySafe";
        
        // Why the bot chose this strategy (carried forward for context)
        public string StrategyRationale { get; set; } = "";
        
        // Compact log of recent trick outcomes (rolling window, max 5)
        // Format: "T3: You led 4C→Bot2 won(6pts)" or "T4: Led QS→Cancelled"
        public List<string> RecentTrickLog { get; set; } = new();
        
        // How many consecutive tricks this strategy has been active
        public int StrategyAge { get; set; } = 0;
    }

    public class AiMemoryTracker
    {
        public int QueensOfSpadesPlayed { get; set; } = 0;
        public int PenaltyHeartsPlayed { get; set; } = 0;
        
        // Dictionary mapping PlayerId -> List of Suits they are void in
        public Dictionary<string, HashSet<Suit>> PlayerVoids { get; set; } = new();

        // PerceiverPlayerId -> (TargetPlayerId -> Set of cards known to be in that target's hand)
        public Dictionary<string, Dictionary<string, HashSet<Card>>> KnownCards { get; set; } = new();

        // Per-bot strategic memory persisted across tricks within a hand
        public Dictionary<string, AiStrategyState> PlayerStrategies { get; set; } = new();
    }

    public class GameRules
    {
        // Allowed Values: "Standard" (Pass Left, Right, Across, Hold), "None" (No passing)
        public string PassingStyle { get; set; } = "Standard";
        
        // Allowed Values: "2OfClubs", "DealersLeft"
        public string FirstLead { get; set; } = "DealersLeft"; // Simplifying first lead to Dealer's Left as default for now
        
        // Allowed Values: "Standard" (Must break), "Guts" (Can lead anytime)
        public string BreakingHearts { get; set; } = "Standard";
        
        // Allowed Values: "PreviousWinner", "TrickLeader"
        public string CancellationWinner { get; set; } = "PreviousWinner";
        
        // Target score to end the match. Default is 100.
        public int TargetScore { get; set; } = 100;
    }

    public class TrickSummary
    {
        public List<Card> Trick { get; set; } = new();
        public int LeadingPlayerIndex { get; set; }
        public int WinningPlayerIndex { get; set; } // -1 if cancelled
        public bool IsCancelled { get; set; }
        public int TrickPoints { get; set; }
    }

    public class GameState
    {
        public string GameId { get; set; } = string.Empty;
        public List<Player> Players { get; set; } = new();
        public int CurrentTurnPlayerIndex { get; set; }
        public int LeadingPlayerIndex { get; set; }
        public int DealerPlayerIndex { get; set; }
        
        public List<Card> CurrentTrick { get; set; } = new();
        
        // Cards from a trick where every card was cancelled
        public List<Card> CancelledKitty { get; set; } = new(); 
        
        // The leftover cards from the deal in a 5, 7, etc. player game
        public List<Card> SetupKitty { get; set; } = new(); 
        public string? KittyTakenByName { get; set; } 
        
        public bool HeartsBroken { get; set; }
        public bool IsFirstTrickOfHand { get; set; } = true;
        public int MatchTricksPlayed { get; set; } = 0;

        public TrickSummary? PreviousTrick { get; set; }

        public GameRules Rules { get; set; } = new();

        // Passing Phase Tracking
        public int RoundNumber { get; set; } = 1;
        public Dictionary<string, List<Card>> PendingPasses { get; set; } = new();
        public string? ShooterOfMoonId { get; set; }

        public AiMemoryTracker MemoryTracker { get; set; } = new();
        public Dictionary<string, string> LastMoveReasoning { get; set; } = new();
        public bool ShowAiReasoning { get; set; } = true;



        public enum GamePhase { Lobby, DownloadingModel, Passing, TrickPending, Playing, GameOver, MatchOver }
        public GamePhase Phase { get; set; } = GamePhase.Lobby;

        public double LlmDownloadProgress { get; set; } = 0.0;
        public string LlmDownloadStatus { get; set; } = string.Empty;
    }
}
