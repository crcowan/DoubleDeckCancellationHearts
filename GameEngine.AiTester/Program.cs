using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameEngine.Api.Models;
using GameEngine.Api.Services;


namespace GameEngine.AiTester
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=======================================");
            Console.WriteLine("   AI GRANDMASTER VALIDATION MODULE");
            Console.WriteLine("=======================================\n");

            Console.WriteLine("Select Gemma Model:");
            Console.WriteLine("1. Fast2B");
            Console.WriteLine("2. Balanced4B (Default)");
            Console.Write("Choice: ");
            var input = Console.ReadLine();
            
            AiModelSize selectedModel = AiModelSize.Balanced4B;
            if (input == "1") selectedModel = AiModelSize.Fast2B;

            Console.WriteLine($"\nLoading model {selectedModel}...");

            var llmInference = new LlmInferenceService();
            
            // Dummy preload to ensure weights are in memory
            try {
                // This triggers lazy loading inside LlmInferenceService
                await llmInference.GenerateMoveIntentAsync("<bos><start_of_turn>user\ntest\n<end_of_turn><start_of_turn>model\n", selectedModel, 0.1f, 10, "JSON");
                Console.WriteLine("Model successfully loaded.");
            } catch (Exception ex) {
                Console.WriteLine("Failed to load model: " + ex.Message);
                return;
            }

            var gameLogic = new GameLogicService();
            var aiService = new AiService(gameLogic, llmInference);

            string jsonPath = "scenarios.json";
            if (!File.Exists(jsonPath))
            {
                Console.WriteLine($"Error: {jsonPath} not found.");
                return;
            }

            var scenariosJson = File.ReadAllText(jsonPath);
            var scenarios = JsonSerializer.Deserialize<List<TestScenario>>(scenariosJson) ?? new List<TestScenario>();

            Console.WriteLine($"Found {scenarios.Count} scenarios to run.\n");

            string logPath = "test_results.log";
            using var logFile = new StreamWriter(logPath, append: false);
            logFile.WriteLine($"--- AI Validation Run: {DateTime.Now} using {selectedModel} ---");

            int passed = 0;
            int failed = 0;

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("ScenarioId,Description,ExpectedIntent,ActualIntent,ExpectedCard,ActualCard,ExecutionTimeMs,Result");

            foreach (var scenario in scenarios)
            {
                Console.WriteLine($"Running Scenario: {scenario.ScenarioId}");
                logFile.WriteLine($"\nScenario: {scenario.ScenarioId}");
                logFile.WriteLine($"Description: {scenario.Description}");

                var (gameState, aiPlayer) = BuildMockGameState(scenario);

                // Run the AI
                // ShowAiReasoning MUST be true so the intent JSON is returned nicely
                gameState.ShowAiReasoning = true;
                
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var result = await aiService.GenerateMoveAsync(gameState, aiPlayer, "");
                    sw.Stop();
                    
                    // The reasoning string is usually prefixed with [Intent], let's extract it
                    string actualIntent = "Unknown";
                    var match = System.Text.RegularExpressions.Regex.Match(result.Reasoning, @"\[(.*?)\]");
                    if (match.Success) actualIntent = match.Groups[1].Value;

                    string actualCard = result.PlayedCard.ToShortString();

                    if (actualIntent == "Unknown" && result.Reasoning.Contains("[Forced]"))
                    {
                        actualIntent = "Forced";
                    }

                    bool intentPass = actualIntent.Equals(scenario.ExpectedIntent, StringComparison.OrdinalIgnoreCase) || (actualIntent == "Forced");
                    bool cardPass = string.IsNullOrEmpty(scenario.ExpectedCard) || actualCard.Equals(scenario.ExpectedCard, StringComparison.OrdinalIgnoreCase);

                    // User requested: "checked against the expected card. That defines a test pass or fail."
                    bool isPass = !string.IsNullOrEmpty(scenario.ExpectedCard) ? cardPass : intentPass;

                    logFile.WriteLine($"Expected Intent: {scenario.ExpectedIntent} | Actual Intent: {actualIntent}");
                    logFile.WriteLine($"Expected Card: {scenario.ExpectedCard} | Actual Card: {actualCard}");
                    logFile.WriteLine($"AI Reasoning: {result.Reasoning}");
                    logFile.WriteLine($"Execution Time: {sw.ElapsedMilliseconds} ms");

                    string resultStr = isPass ? "PASS" : "FAIL";
                    csv.AppendLine($"{scenario.ScenarioId},{EscapeCsv(scenario.Description)},{scenario.ExpectedIntent},{actualIntent},{scenario.ExpectedCard},{actualCard},{sw.ElapsedMilliseconds},{resultStr}");

                    if (isPass)
                    {
                        Console.WriteLine("  -> PASS");
                        logFile.WriteLine("RESULT: PASS");
                        passed++;
                    }
                    else
                    {
                        Console.WriteLine("  -> FAIL");
                        logFile.WriteLine("RESULT: FAIL");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  -> ERROR: {ex.Message}");
                    logFile.WriteLine($"RESULT: ERROR ({ex.Message})");
                    csv.AppendLine($"{scenario.ScenarioId},{EscapeCsv(scenario.Description)},{scenario.ExpectedIntent},ERROR,{scenario.ExpectedCard},ERROR,0,FAIL");
                    failed++;
                }
            }

            string csvPath = "test_results.csv";
            File.WriteAllText(csvPath, csv.ToString());

            Console.WriteLine("\n=======================================");
            Console.WriteLine($"RESULTS: {passed} Passed | {failed} Failed");
            Console.WriteLine($"Detailed logs written to {logPath}");
            Console.WriteLine($"Spreadsheet written to {csvPath}");
            Console.WriteLine("=======================================");
            
            logFile.WriteLine($"\nFINAL SUMMARY: {passed} Passed, {failed} Failed");
        }

        static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Contains(",") || text.Contains("\"") || text.Contains("\n"))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return text;
        }

        static (GameState state, Player aiPlayer) BuildMockGameState(TestScenario scenario)
        {
            var state = new GameState();
            
            // Build AI Player
            var ai = new Player
            {
                Id = "AI_Tester",
                Name = "Bot_Tester",
                IsAi = true,
                DifficultyLevel = (int)Math.Floor(scenario.AiSkill),
                SkillOffset = scenario.AiSkill - Math.Floor(scenario.AiSkill),
                Score = scenario.GameState.AiScore,
                HandScore = scenario.GameState.AiHandScore
            };

            foreach (var cardStr in scenario.GameState.AiHand)
            {
                var card = Card.TryParseShortId(cardStr);
                if (card != null) ai.Hand.Add(card);
            }

            state.Players.Add(ai);

            // Build Opponents
            foreach (var oppKvp in scenario.GameState.OpponentScores)
            {
                var p = new Player
                {
                    Id = oppKvp.Key,
                    Name = oppKvp.Key,
                    IsAi = true,
                    Score = oppKvp.Value,
                    HandScore = scenario.GameState.OpponentHandScores.ContainsKey(oppKvp.Key) ? scenario.GameState.OpponentHandScores[oppKvp.Key] : 0,
                    // To force "Agg" or "Def", we mock MatchTricksWon
                    // GetPlayStyleProfile checks if MatchTricksWon > avg * 1.5
                    MatchTricksWon = 0
                };

                if (scenario.GameState.OpponentProfiles.TryGetValue(oppKvp.Key, out var profile))
                {
                    if (profile == "Agg") p.MatchTricksWon = 100; // Will be Agg
                    else if (profile == "Def") p.MatchTricksWon = 0; // Will be Def
                    else p.MatchTricksWon = 3; // Will be Bal
                }
                state.Players.Add(p);
            }

            // Mock MatchTricksPlayed to ensure GetPlayStyleProfile works
            state.MatchTricksPlayed = 10;
            state.HeartsBroken = true; // Allow hearts to be played freely in test scenarios
            state.IsFirstTrickOfHand = false; // Allow penalty cards to be played in test scenarios

            // Build Current Trick
            foreach (var cardStr in scenario.GameState.CurrentTrick)
            {
                var card = Card.TryParseShortId(cardStr);
                if (card != null) state.CurrentTrick.Add(card);
            }

            return (state, ai);
        }
    }
}
