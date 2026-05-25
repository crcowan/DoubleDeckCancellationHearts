using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameEngine.AiTester
{
    public class TestScenario
    {
        public string ScenarioId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double AiSkill { get; set; } = 5.0; // Default to GM
        public string ExpectedIntent { get; set; } = string.Empty;
        public string ExpectedCard { get; set; } = string.Empty; // Optional, can be empty if testing intent only
        public GameStateMock GameState { get; set; } = new();
    }

    public class GameStateMock
    {
        public List<string> AiHand { get; set; } = new();
        public string LedSuit { get; set; } = string.Empty; // If empty, it means AI is leading
        public List<string> CurrentTrick { get; set; } = new();
        
        public int AiScore { get; set; } = 0;
        public int AiHandScore { get; set; } = 0;

        // PlayerId -> Score
        public Dictionary<string, int> OpponentScores { get; set; } = new();
        public Dictionary<string, int> OpponentHandScores { get; set; } = new();
        
        // PlayerId -> Profile ("Agg", "Def", "Bal")
        public Dictionary<string, string> OpponentProfiles { get; set; } = new();

        // PlayerId -> List of Void Suits ("Clubs", "Diamonds", "Hearts", "Spades")
        public Dictionary<string, List<string>> OpponentVoids { get; set; } = new();
    }
}
