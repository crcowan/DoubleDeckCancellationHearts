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
        public int Score { get; set; }
    }

    public class AiMemoryTracker
    {
        public int QueensOfSpadesPlayed { get; set; } = 0;
        public int PenaltyHeartsPlayed { get; set; } = 0;
        
        // Dictionary mapping PlayerId -> List of Suits they are void in
        public Dictionary<string, HashSet<Suit>> PlayerVoids { get; set; } = new();
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

    public class GameState
    {
        public string GameId { get; set; } = string.Empty;
        public List<Player> Players { get; set; } = new();
        public int CurrentTurnPlayerIndex { get; set; }
        public int LeadingPlayerIndex { get; set; }
        
        public List<Card> CurrentTrick { get; set; } = new();
        
        // Cards from a trick where every card was cancelled
        public List<Card> CancelledKitty { get; set; } = new(); 
        
        // The leftover cards from the deal in a 5, 7, etc. player game
        public List<Card> SetupKitty { get; set; } = new(); 
        
        public bool HeartsBroken { get; set; }
        public bool IsFirstTrickOfHand { get; set; } = true;

        public GameRules Rules { get; set; } = new();

        // Passing Phase Tracking
        public int RoundNumber { get; set; } = 1;
        public Dictionary<string, List<Card>> PendingPasses { get; set; } = new();
        public string? ShooterOfMoonId { get; set; }

        public AiMemoryTracker MemoryTracker { get; set; } = new();
        public Dictionary<string, string> LastMoveReasoning { get; set; } = new();

        public enum GamePhase { Lobby, Passing, TrickPending, Playing, GameOver, MatchOver }
        public GamePhase Phase { get; set; } = GamePhase.Lobby;
    }
}
