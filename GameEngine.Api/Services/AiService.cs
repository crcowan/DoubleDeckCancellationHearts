using GameEngine.Api.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameEngine.Api.Services
{
    public class AiService
    {
        private readonly GameLogicService _logic;
        private readonly LlmInferenceService _llmInference;
        private readonly object _aiTurnLock = new object();
        private bool _isProcessingAiTurn = false;
        private int _lastSeenRoundNumber = -1;

        public AiService(GameLogicService logic, LlmInferenceService llmInference)
        {
            _logic = logic;
            _llmInference = llmInference;
        }

        public async Task<(Card PlayedCard, string Reasoning)> GenerateMoveAsync(GameState state, Player aiPlayer, string pastHistoryDbSummary)
        {
            var validCards = GetValidCards(aiPlayer.Hand, state);
            if (validCards.Count == 1) return (validCards.First(), "[Forced] I only have one legal card to play.");

            double effectiveSkill = Math.Max(1.0, aiPlayer.DifficultyLevel + aiPlayer.SkillOffset);

            // --- Field Intel: What are others doing? ---
            var intelEntries = state.Players
                .Where(p => p.Id != aiPlayer.Id && state.MemoryTracker.PlayerStrategies.ContainsKey(p.Id))
                .Select(p => $"{p.Name}({state.MemoryTracker.PlayerStrategies[p.Id].ActiveStrategy})")
                .ToList();
            string fieldIntel = intelEntries.Any() ? string.Join(", ", intelEntries) : "Initializing...";

            // --- Skill-Based Mistakes (Determined BEFORE reasoning) ---
            Card? forcedMistakeCard = null;
            var rnd = new Random();
            double mistakeChance = Math.Max(0.01, 0.45 - (effectiveSkill * 0.1)); 
            if (rnd.NextDouble() < mistakeChance)
            {
                forcedMistakeCard = validCards[rnd.Next(validCards.Count)];
            }

            // --- Grandmaster Fast-Track Engine (Latency Optimization) ---
            string engineSuggestedIntent = null;
            Card engineSuggestedCard = null;
            string engineReasoning = null;

            if (effectiveSkill >= 4.0)
            {
                Suit? overrideLedSuit = state.CurrentTrick.FirstOrDefault()?.Suit;

                // 1. Lead Queen of Spades
                if (state.CurrentTrick.Count == 0 && validCards.Any(c => c.IsQueenOfSpades))
                {
                    engineSuggestedCard = validCards.First(c => c.IsQueenOfSpades);
                    engineSuggestedIntent = "LeadQueen";
                    engineReasoning = "[Strategic] Grandmaster lead: Forcing the Queen of Spades to smoke out opponents.";
                }
                // 2. Cancellation (Identical card in trick)
                else if (validCards.FirstOrDefault(vc => (vc.Rank >= Rank.Jack || vc.Suit == Suit.Hearts) && state.CurrentTrick.Any(tc => tc.Suit == vc.Suit && tc.Rank == vc.Rank)) != null)
                {
                    engineSuggestedCard = validCards.FirstOrDefault(vc => (vc.Rank >= Rank.Jack || vc.Suit == Suit.Hearts) && state.CurrentTrick.Any(tc => tc.Suit == vc.Suit && tc.Rank == vc.Rank));
                    engineSuggestedIntent = "Cancellation";
                    engineReasoning = $"[Strategic] Grandmaster cancellation: Matching the {engineSuggestedCard} in the trick.";
                }
                // 3. Stop Moonshot
                else if (state.Players.FirstOrDefault(p => p.Id != aiPlayer.Id && p.HandScore >= 20 && state.Players.All(other => other.Id == p.Id || other.HandScore == 0)) != null && validCards.Any(c => c.Rank == Rank.Ace))
                {
                    var moonThreat = state.Players.FirstOrDefault(p => p.Id != aiPlayer.Id && p.HandScore >= 20 && state.Players.All(other => other.Id == p.Id || other.HandScore == 0));
                    engineSuggestedCard = validCards.First(c => c.Rank == Rank.Ace);
                    engineSuggestedIntent = "StopMoon";
                    engineReasoning = $"[Strategic] Grandmaster defense: Forcing the {engineSuggestedCard} to prevent {moonThreat.Name} from shooting the moon.";
                }
                // 4. Cancel Queen
                else if (state.CurrentTrick.Any(tc => tc.IsQueenOfSpades) && validCards.Any(vc => vc.IsQueenOfSpades))
                {
                    engineSuggestedCard = validCards.First(vc => vc.IsQueenOfSpades);
                    engineSuggestedIntent = "CancelQueen";
                    engineReasoning = "[Strategic] Grandmaster maneuver: Cancelling the Queen of Spades to nullify the point penalty.";
                }
                // 5. Dump Penalty (Void in led suit)
                else if (overrideLedSuit.HasValue && !validCards.Any(c => c.Suit == overrideLedSuit.Value) && validCards.Any(c => c.PointValue > 0))
                {
                    engineSuggestedCard = validCards.Where(c => c.PointValue > 0).OrderByDescending(c => c.PointValue).ThenByDescending(c => c.Rank).First();
                    engineSuggestedIntent = "DumpPenalty";
                    engineReasoning = $"[Strategic] Grandmaster discard: Dumping the {engineSuggestedCard} while void in {overrideLedSuit}.";
                }
                // 6. Aggressive Feeding
                else if (overrideLedSuit.HasValue && state.CurrentTrick.Any(c => c.PointValue > 0))
                {
                    var suitCards = validCards.Where(c => c.Suit == overrideLedSuit.Value).OrderBy(c => c.Rank).ToList();
                    var highestInTrick = state.CurrentTrick.Where(c => c.Suit == overrideLedSuit.Value).OrderByDescending(c => c.Rank).FirstOrDefault();
                    if (highestInTrick != null)
                    {
                        var highestSafe = suitCards.LastOrDefault(c => c.Rank < highestInTrick.Rank);
                        if (highestSafe != null && (highestSafe.PointValue > 0 || highestSafe.Rank >= Rank.Ten))
                        {
                            engineSuggestedCard = highestSafe;
                            engineSuggestedIntent = "AggressiveFeeding";
                            engineReasoning = $"[Strategic] Grandmaster feeding: Dumping the {highestSafe} on a high-value trick.";
                        }
                    }
                }

                // 7. Take Control (Ace of led suit)
                if (engineSuggestedIntent == null && overrideLedSuit.HasValue && validCards.Any(c => c.Suit == overrideLedSuit.Value && c.Rank == Rank.Ace))
                {
                    engineSuggestedCard = validCards.First(c => c.Suit == overrideLedSuit.Value && c.Rank == Rank.Ace);
                    engineSuggestedIntent = "TakeControl";
                    engineReasoning = $"[Strategic] Grandmaster control: Taking the lead with the {engineSuggestedCard}.";
                }

                // 8. Shoot the Moon (If AI has all points and can win)
                bool moonshot = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0) && aiPlayer.HandScore > 0;
                if (engineSuggestedIntent == null && moonshot)
                {
                    engineSuggestedCard = validCards.OrderByDescending(c => c.Rank).First();
                    engineSuggestedIntent = "ShootTheMoon";
                    engineReasoning = $"[Strategic] Grandmaster moonshot: Playing the {engineSuggestedCard} to maintain momentum.";
                }
            }

            // --- Fast-Track Bypass: Skip LLM entirely for deterministic moves ---
            // If the Grandmaster Engine already computed the optimal card, return it instantly.
            // This saves 2-5 seconds per move on integrated GPUs.
            if (engineSuggestedCard != null && engineSuggestedIntent != null)
            {
                // Persist the strategy decision even when bypassing the LLM
                if (effectiveSkill >= 2.5)
                {
                    if (!state.MemoryTracker.PlayerStrategies.ContainsKey(aiPlayer.Id))
                        state.MemoryTracker.PlayerStrategies[aiPlayer.Id] = new AiStrategyState();

                    var strat = state.MemoryTracker.PlayerStrategies[aiPlayer.Id];
                    if (strat.ActiveStrategy != engineSuggestedIntent) strat.StrategyAge = 1;
                    else strat.StrategyAge++;
                    
                    strat.ActiveStrategy = engineSuggestedIntent;
                    strat.StrategyRationale = engineReasoning?.Length > 120 ? engineReasoning.Substring(0, 117) + "..." : engineReasoning ?? "";
                }

                return (engineSuggestedCard, engineReasoning ?? $"[{engineSuggestedIntent}] Fast-track engine play.");
            }

            // --- Nuanced Decision Mode (LLM Consultation) ---
            // No deterministic strategic override fired. Consult the LLM for reasoning and move selection.
            
            // Decide which prompt profile to use based on the Performance Toggle
            string prompt = state.ShowAiReasoning 
                ? ConstructPromptVerbose(state, aiPlayer, validCards, effectiveSkill, fieldIntel, forcedMistakeCard, engineSuggestedIntent, engineSuggestedCard)
                : ConstructPromptNano(state, aiPlayer, validCards, effectiveSkill, fieldIntel, forcedMistakeCard, engineSuggestedIntent, engineSuggestedCard);
            
            float temperature = 0.5f;
            int maxTokens = state.ShowAiReasoning ? 60 : 25; 

            if (effectiveSkill >= 4.0) { temperature = 0.2f; if (state.ShowAiReasoning) maxTokens = 80; }
            else if (effectiveSkill >= 2.5) { temperature = 0.7f; if (state.ShowAiReasoning) maxTokens = 100; }
            else { temperature = 1.0f; if (state.ShowAiReasoning) maxTokens = 40; }

            var (validTactics, validTacticDefs) = GetDynamicTactics(state, aiPlayer, validCards, effectiveSkill);

            string responseJson = "";
            string responseRaw = "";

            var grammar = state.ShowAiReasoning ? null : "JSON"; 
            responseRaw = await _llmInference.GenerateMoveIntentAsync(aiPlayer.Id, prompt, temperature, maxTokens, grammar);
            responseJson = responseRaw.Trim();

            // Extract json block if surrounded by markdown
            var matchJson = System.Text.RegularExpressions.Regex.Match(responseJson, @"\{[\s\S]*\}");
            if (matchJson.Success)
            {
                responseJson = matchJson.Value;
            }

            if (!responseJson.StartsWith("{"))
            {
                responseJson = state.ShowAiReasoning 
                    ? "{ \"Reasoning\": \"" + responseRaw.Replace("\"", "\\\"").Replace("\n", " ") + "\" }" 
                    : "{ \"Intent\": \"" + responseRaw.Trim() + "\" }";
            }
 
            string intentStr = engineSuggestedIntent ?? "PlaySafe";
            string suggestedCardId = engineSuggestedCard?.ToShortString() ?? "";
            string reasoning = "";
 
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("Intent", out var iProp)) intentStr = iProp.GetString() ?? intentStr;
                if (root.TryGetProperty("SuggestedCard", out var sProp)) suggestedCardId = sProp.GetString() ?? suggestedCardId;
                if (root.TryGetProperty("Reasoning", out var rProp)) reasoning = rProp.GetString() ?? reasoning;
            }
            catch
            {
                if (responseRaw.Contains("Intent")) 
                {
                    var match = System.Text.RegularExpressions.Regex.Match(responseRaw, "\"Intent\":\\s*\"([^\"]+)\"");
                    if (match.Success) intentStr = match.Groups[1].Value;
                }
            }

            // --- Normal Heuristics & Map Logic ---
            if (!validTactics.Contains(intentStr))
            {
                intentStr = validTactics.FirstOrDefault() ?? "PlaySafe"; 
            }

            var chosenCard = MapIntentToCard(intentStr, validCards, state, aiPlayer, effectiveSkill, forcedMistakeCard, suggestedCardId);


            if (chosenCard == null || !validCards.Contains(chosenCard))
            {
                chosenCard = validCards.OrderBy(c => c.Rank).First();
            }

            // Engine Override Clarification:
            // If the LLM hallucinates an illegal card in its monologue (e.g. suggests 9D when Hearts are led),
            // and the Engine overrides it to a valid card (e.g. 2H), dynamically patch the text to reflect reality.
            if (!string.IsNullOrWhiteSpace(suggestedCardId) && !chosenCard.ToShortString().Equals(suggestedCardId, StringComparison.OrdinalIgnoreCase))
            {
                reasoning = reasoning.Replace(suggestedCardId, chosenCard.ToShortString(), StringComparison.OrdinalIgnoreCase);
                
                // If the LLM hallucinated the full English name (e.g. 'Queen of Hearts') instead of the ID, 
                // the replace above fails. We append an explicit override note so the UI isn't confusing.
                if (!reasoning.Contains(chosenCard.ToShortString(), StringComparison.OrdinalIgnoreCase))
                {
                    reasoning += $" (Engine override: Forced to play {chosenCard.ToShortString()}).";
                }
            }

            if (state.ShowAiReasoning && (string.IsNullOrWhiteSpace(reasoning) || reasoning == "WHY"))
            {
                reasoning = $"Playing {chosenCard} because it fits the {intentStr} strategy.";
            }
            else if (state.ShowAiReasoning && !reasoning.StartsWith("["))
            {
                reasoning = $"[{intentStr}] {reasoning}";
            }

            // --- Persist Strategy Decision ---
            if (effectiveSkill >= 2.5)
            {
                if (!state.MemoryTracker.PlayerStrategies.ContainsKey(aiPlayer.Id))
                    state.MemoryTracker.PlayerStrategies[aiPlayer.Id] = new AiStrategyState();

                var strat = state.MemoryTracker.PlayerStrategies[aiPlayer.Id];
                if (strat.ActiveStrategy != intentStr) strat.StrategyAge = 1;
                else strat.StrategyAge++;
                
                strat.ActiveStrategy = intentStr;
                strat.StrategyRationale = reasoning.Length > 120 ? reasoning.Substring(0, 117) + "..." : reasoning;
            }

            return (chosenCard, reasoning);
        }

        private string ConstructPromptVerbose(GameState state, Player aiPlayer, List<Card> validCards, double effectiveSkill, string fieldIntel, Card? forcedMistake, string engineSuggestedIntent, Card engineSuggestedCard)
        {
            var (scoreboard, voids, knownInfo, strategySection, historySection, mistakeNote, trickStr, fullHandStr, counts, prevTrickStr) = GetCommonPromptData(state, aiPlayer, validCards, effectiveSkill, forcedMistake);
            string persona = effectiveSkill >= 4.0 ? "Grandmaster" : (effectiveSkill >= 2.5 ? "Experienced" : "Beginner");
            string personaLine = $"You are {aiPlayer.Name} ({persona}). Speak in the 1st person ('I'). Use specific names only.";
            if (effectiveSkill >= 4.0) personaLine += " CRITICAL: As a Grandmaster, NEVER default to PlaySafe if an aggressive or advanced tactic is available!";
            bool moonshotPossible = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0);
            Suit? ledSuit = state.CurrentTrick.FirstOrDefault()?.Suit;

            var (tactics, tacticDefs) = GetDynamicTactics(state, aiPlayer, validCards, effectiveSkill);
            string tacticsStr = string.Join(", ", tactics);
            string defsStr = string.Join(" | ", tacticDefs);
            string hintNote = (engineSuggestedIntent != null) ? $"HINT: The Engine suggests [{engineSuggestedIntent}] with {engineSuggestedCard?.ToShortString()}.\n" : "";

            return $@"**SYSTEM RULES**
Respond purely with valid JSON. Do not add conversational text.
GOAL: Avoid penalty points. Giving points to others is GOOD (unless they are shooting the moon).
{personaLine}

**CONSTRAINTS**
Reasoning: ONE short sentence. MUST MATCH your Intent. Explain why you are choosing the specific TACTIC. BANNED: 'opponent', 'player'. Use 1st person 'I' and specific names.
TACTICS AVAILABLE: {tacticsStr}
DEFINITIONS: {defsStr}
JSON FORMAT:
{{
  ""Reasoning"": ""(One short sentence holding your plan)"",
  ""Intent"": ""(TACTIC_NAME)"",
  ""SuggestedCard"": ""(e.g. '9D')""
}}

**MEMORY & HISTORY**
DEDUCTIONS: {voids}
{knownInfo}{strategySection}{historySection}{prevTrickStr}
{mistakeNote}

**GAME STATE**
Pos: {state.CurrentTrick.Count + 1}/{state.Players.Count}
Scores: {scoreboard}
Cancelled Pile: {state.CancelledKitty.Sum(c => c.PointValue)}pts | You: {aiPlayer.HandScore}pts
{counts}

**CURRENT TRICK**
LED: {ledSuit?.ToString() ?? "None"}. You MUST follow the led suit IF you have it. If void, you can discard any suit.
{hintNote}
Trick: {trickStr}
HAND: {fullHandStr}";
        }

        private string ConstructPromptNano(GameState state, Player aiPlayer, List<Card> validCards, double effectiveSkill, string fieldIntel, Card? forcedMistake, string engineSuggestedIntent, Card engineSuggestedCard)
        {
            var (_, voids, knownInfo, strategySection, historySection, _, trickStr, fullHandStr, counts, prevTrickStr) = GetCommonPromptData(state, aiPlayer, validCards, effectiveSkill, forcedMistake);
            bool moonshotPossible = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0);
            Suit? ledSuit = state.CurrentTrick.FirstOrDefault()?.Suit;

            var (tactics, tacticDefs) = GetDynamicTactics(state, aiPlayer, validCards, effectiveSkill);
            string tacticsStr = string.Join(",", tactics);
            string defsStr = string.Join(" | ", tacticDefs);
            if (effectiveSkill >= 4.0) defsStr += " | GM_RULE: Never default to PlaySafe if an advanced tactic is available";
            string prefix = effectiveSkill >= 4.0 ? "GM" : (effectiveSkill >= 2.5 ? "PRO" : "NOOB");
            string hintNote = (engineSuggestedIntent != null) ? $"HINT:[{engineSuggestedIntent}:{engineSuggestedCard?.ToShortString()}]\n" : "";

            return $@"Double Deck Hearts
{prefix}
DEFS:{defsStr}
OPTS:{tacticsStr}
Output EXACTLY ONE JSON Intent and SuggestedCard:
{{ ""Intent"": ""TACTIC_NAME"", ""SuggestedCard"": ""ID"" }}

Intel: {fieldIntel}
{knownInfo}{strategySection}{historySection}{prevTrickStr}
{counts}
L:{ledSuit?.ToString()?[0] ?? 'N'}
{hintNote}
TR:{trickStr}
HND:{fullHandStr}";
        }



        private (List<string> Tactics, List<string> TacticDefs) GetDynamicTactics(GameState state, Player aiPlayer, List<Card> validCards, double effectiveSkill)
        {
            List<string> tactics = new List<string> { "PlaySafe" };
            List<string> tacticDefs = new List<string> { "PlaySafe: Play the lowest valid card to avoid points" };
            bool moonshotPossible = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0);
            Suit? ledSuit = state.CurrentTrick.FirstOrDefault()?.Suit;

            // --- Endgame Safety ---
            if (state.MemoryTracker.QueensOfSpadesPlayed == 2 && state.MemoryTracker.PenaltyHeartsPlayed == 26)
            {
                tactics.Add("EndgameSafety"); tacticDefs.Add("EndgameSafety: All points are gone. Win tricks safely and quickly.");
            }

            if (state.CurrentTrick.Count == 0) // Leading
            {
                if (validCards.Any(c => c.Suit == Suit.Spades)) { tactics.Add("ClearSpades"); tacticDefs.Add("ClearSpades: Lead Spades to drain opponents of them"); }
                if (validCards.Any(c => c.IsQueenOfSpades)) { tactics.Add("LeadQueen"); tacticDefs.Add("LeadQueen: Lead the Queen to force points on someone"); }
                
                // Bleed Spades
                if (state.MemoryTracker.QueensOfSpadesPlayed < 2 && !aiPlayer.Hand.Any(c => c.IsQueenOfSpades) && validCards.Any(c => c.Suit == Suit.Spades && (c.Rank == Rank.Ace || c.Rank == Rank.King)))
                {
                    tactics.Add("BleedSpades"); tacticDefs.Add("BleedSpades: Lead low Spades to force opponents to play their Queens");
                }

                // Avoid Void Lead
                var opponentVoids = state.MemoryTracker.PlayerVoids.Where(kvp => kvp.Key != aiPlayer.Id && kvp.Value.Any()).SelectMany(kvp => kvp.Value).Distinct().ToList();
                if (opponentVoids.Any() && validCards.Any(c => !opponentVoids.Contains(c.Suit)))
                {
                    tactics.Add("AvoidVoidLead"); tacticDefs.Add($"AvoidVoidLead: Do not lead {string.Join(",", opponentVoids)} to avoid being dumped on");
                }
            }
            else // Following
            {
                tactics.Add("DuckingTrick"); tacticDefs.Add("DuckingTrick: Play a card lower than the current highest to avoid winning");
                tactics.Add("Cancellation"); tacticDefs.Add("Cancellation: Play an identical card to cancel the current highest card");
                if (state.CurrentTrick.Any(c => c.PointValue > 0) || validCards.Any(c => c.PointValue > 0)) { tactics.Add("AggressiveFeeding"); tacticDefs.Add("AggressiveFeeding: Dump penalty points on the current winner"); }
                if (state.CurrentTrick.Any(c => c.IsQueenOfSpades) && validCards.Any(c => c.IsQueenOfSpades)) { tactics.Add("CancelQueen"); tacticDefs.Add("CancelQueen: Play your Queen to cancel the played Queen"); }
                if (ledSuit.HasValue && validCards.Any(c => c.Suit != ledSuit.Value)) { tactics.Add("DumpPenalty"); tacticDefs.Add("DumpPenalty: You are void, so discard your highest penalty card"); }
                
                // If the trick is currently completely safe, consider taking it to gain the lead
                if (state.CurrentTrick.Sum(c => c.PointValue) == 0 && validCards.Any(c => c.PointValue == 0)) { 
                    tactics.Add("TakeControl"); tacticDefs.Add("TakeControl: Win this safe trick with a high card to gain the lead"); 
                }
                
                // Avoid Kitty on Trick 1
                if (state.IsFirstTrickOfHand && state.SetupKitty.Count > 0)
                {
                    tactics.Add("AvoidKitty"); tacticDefs.Add("AvoidKitty: The winner gets the Kitty. Play lowest card to duck at all costs.");
                    tactics.Remove("TakeControl"); tactics.Remove("PlaySafe");
                    tacticDefs.RemoveAll(d => d.StartsWith("TakeControl:") || d.StartsWith("PlaySafe:"));
                }
                
                // Guard Queen
                if (aiPlayer.Hand.Any(c => c.IsQueenOfSpades) && ledSuit == Suit.Spades && validCards.Any(c => c.Rank >= Rank.King))
                {
                    tactics.Add("GuardQueen"); tacticDefs.Add("GuardQueen: Hold high spades to protect your Queen of Spades.");
                }

                // Establish Void Early Game
                if (aiPlayer.Hand.Count > 18) // First ~8 tricks
                {
                    if (aiPlayer.Hand.Count(c => c.Suit == Suit.Clubs) <= 3 && validCards.Any(c => c.Suit == Suit.Clubs)) { tactics.Add("EstablishVoid"); tacticDefs.Add("EstablishVoid: Actively play high Clubs to run out of them."); }
                    else if (aiPlayer.Hand.Count(c => c.Suit == Suit.Diamonds) <= 3 && validCards.Any(c => c.Suit == Suit.Diamonds)) { tactics.Add("EstablishVoid"); tacticDefs.Add("EstablishVoid: Actively play high Diamonds to run out of them."); }
                }
            }
            
            moonshotPossible = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0) && aiPlayer.HandScore > 0;
            if (moonshotPossible) { tactics.Add("ShootTheMoon"); tacticDefs.Add("ShootTheMoon: Take all penalty points"); }
            
            // If an opponent has a significant number of points and no one else has any, they are a moon threat!
            // In double deck (52 total points), a threshold of 26 (half the points) is a realistic trigger for a moonshot threat.
            var moonThreat = state.Players.FirstOrDefault(p => p.Id != aiPlayer.Id && p.HandScore >= 26 && state.Players.All(other => other.Id == p.Id || other.HandScore == 0));
            if (moonThreat != null) {
                tactics.Add("StopMoon"); tacticDefs.Add($"StopMoon: {moonThreat.Name} is shooting the moon! Intentionally take points to stop them");
                if (effectiveSkill >= 3.0) 
                {
                    tactics.Remove("PlaySafe");
                    tacticDefs.RemoveAll(d => d.StartsWith("PlaySafe:"));
                }
            }
            
            // --- Skill Gradation Engine ---
            if (effectiveSkill >= 4.0 && tactics.Count > 1) 
            {
                tactics.Remove("PlaySafe");
                tacticDefs.RemoveAll(d => d.StartsWith("PlaySafe:"));
            }

            return (tactics, tacticDefs);
        }

        private (string Scoreboard, string Voids, string KnownInfo, string StrategySection, string HistorySection, string MistakeNote, string TrickStr, string FullHandStr, string Counts, string PrevTrickStr) GetCommonPromptData(GameState state, Player aiPlayer, List<Card> validCards, double effectiveSkill, Card? forcedMistake)
        {
            // Inline calculation - extremely fast naturally, prevents stale data bugs
            string scoreboard = string.Join(", ", state.Players.OrderByDescending(p => p.Score).Select(p => $"{p.Name}:{p.Score} (Hand:{p.HandScore})"));
            
            var opponentVoids = state.MemoryTracker.PlayerVoids
                .Where(kvp => kvp.Key != aiPlayer.Id && kvp.Value.Any())
                .Select(kvp => {
                    var name = state.Players.FirstOrDefault(p => p.Id == kvp.Key)?.Name;
                    var suitShorts = string.Join("", kvp.Value.Select(s => s.ToString()[0]));
                    return $"{name} void:{suitShorts}";
                })
                .ToList();
            string voidsBlock = opponentVoids.Any() ? string.Join("; ", opponentVoids) : "None";

            int queensPlayed = state.MemoryTracker.QueensOfSpadesPlayed;
            int heartsPlayed = state.MemoryTracker.PenaltyHeartsPlayed;
            string countsBlock = $"{queensPlayed}/2 QS, {heartsPlayed}/26 H.";

            var profiles = state.Players.Where(p => p.Id != aiPlayer.Id)
                .Select(p => $"{p.Name}({p.GetPlayStyleProfile(state.MatchTricksPlayed, state.Players.Count)})");
            string knownInfo = $"PROF:{string.Join(",", profiles)}\n";

            var myKnowledge = state.MemoryTracker.KnownCards.ContainsKey(aiPlayer.Id) ? state.MemoryTracker.KnownCards[aiPlayer.Id] : null;
            if (effectiveSkill >= 3.0 && myKnowledge != null) {
                var lines = myKnowledge.Where(kvp => kvp.Value.Any()).Select(kvp => {
                    var name = state.Players.FirstOrDefault(p => p.Id == kvp.Key)?.Name;
                    var cardShorts = string.Join(",", kvp.Value.Select(c => c.ToShortString()));
                    return $"{name}:{cardShorts}";
                });
                if (lines.Any()) knownInfo += "K:" + string.Join(" | ", lines) + "\n";
            }

            string strategySection = "";
            if (effectiveSkill >= 2.5 && state.MemoryTracker.PlayerStrategies.TryGetValue(aiPlayer.Id, out var strat)) {
                if (!string.IsNullOrEmpty(strat.ActiveStrategy) && strat.StrategyAge > 0)
                    strategySection = $"STRAT:{strat.ActiveStrategy}({strat.StrategyAge})\n";
            }

            string historySection = "";
            if (effectiveSkill >= 2.5 && state.MemoryTracker.PlayerStrategies.TryGetValue(aiPlayer.Id, out var s2) && s2.RecentTrickLog.Any())
                historySection = "H:" + string.Join("|", s2.RecentTrickLog) + "\n";

            string mistakeNote = (forcedMistake != null) ? $"MISTAKE:{forcedMistake.ToShortString()}\n" : "";
            string trickStr = state.CurrentTrick.Count == 0 ? "Empty" : string.Join(",", state.CurrentTrick.Select(c => c.ToShortString()));
            var handWithLegal = aiPlayer.Hand.OrderBy(c => c.Suit).ThenBy(c => c.Rank).Select(c => c.ToShortString());
            string fullHandStr = string.Join(",", handWithLegal);
            
            string prevTrickStr = state.PreviousTrick != null 
                ? $"PT: {string.Join(",", state.PreviousTrick.Trick.Select(c => c.ToShortString()))} ({(state.PreviousTrick.IsCancelled ? "Cancelled" : state.Players[state.PreviousTrick.WinningPlayerIndex].Name + " won")})\n"
                : "PT: None\n";

            return (scoreboard, voidsBlock, knownInfo, strategySection, historySection, mistakeNote, trickStr, fullHandStr, countsBlock, prevTrickStr);
        }

        private Card MapIntentToCard(string intent, List<Card> validCards, GameState state, Player aiPlayer, double skill, Card? forcedMistake, string suggestedCardId)
        {
            if (forcedMistake != null && validCards.Contains(forcedMistake)) return forcedMistake;

            // Universal Cancellation Override: If we can legally cancel a HIGH card already in the trick, DO IT!
            // This prevents AI from throwing away a lower card when it could safely cancel a penalty card or A/K/Q.
            if (intent != "ShootTheMoon") 
            {
                var highCancelMatch = validCards.FirstOrDefault(vc => 
                    (vc.Rank >= Rank.Jack || vc.Suit == Suit.Hearts) && 
                    state.CurrentTrick.Any(tc => tc.Suit == vc.Suit && tc.Rank == vc.Rank));
                    
                if (highCancelMatch != null) return highCancelMatch;
            }

            // Robust SuggestedCard parsing
            if (!string.IsNullOrWhiteSpace(suggestedCardId))
            {
                // Strip common model hallucinations like "AC(L)" or "KH (High)"
                suggestedCardId = System.Text.RegularExpressions.Regex.Replace(suggestedCardId, @"\(.*?\)", "").Trim();
                
                var match = validCards.FirstOrDefault(c => c.ToShortString().Equals(suggestedCardId, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            // Fallback Logic: The LLM provided a strategy name (Intent) but maybe not a valid card ID
            // We use heuristics to fulfill the intent.
            Suit? ledSuit = state.CurrentTrick.FirstOrDefault()?.Suit;

            switch (intent)
            {
                case "Cancellation":
                    // Find a card in hand that perfectly matches a card already in the trick to cancel it out
                    var perfectMatch = validCards.FirstOrDefault(vc => state.CurrentTrick.Any(tc => tc.Suit == vc.Suit && tc.Rank == vc.Rank));
                    if (perfectMatch != null) return perfectMatch;
                    
                    // Fallback if no matching card can be found
                    return validCards.OrderByDescending(c => c.PointValue).ThenByDescending(c => c.Rank).First();

                case "EndgameSafety":
                case "TakeControl":
                    if (ledSuit.HasValue)
                    {
                        var suitCards = validCards.Where(c => c.Suit == ledSuit.Value && c.PointValue == 0).OrderByDescending(c => c.Rank).ToList();
                        if (suitCards.Any()) return suitCards.First(); // Play highest safe card to win
                    }
                    if (intent == "EndgameSafety") return validCards.OrderByDescending(c => c.Rank).First();
                    // If void, we can't win. Just play lowest card.
                    return validCards.OrderBy(c => c.Rank).First();

                case "BleedSpades":
                    var lowSpade = validCards.Where(c => c.Suit == Suit.Spades).OrderBy(c => c.Rank).FirstOrDefault();
                    if (lowSpade != null) return lowSpade;
                    return validCards.First();

                case "EstablishVoid":
                    var targetCards = validCards.Where(c => c.Suit == Suit.Clubs || c.Suit == Suit.Diamonds).OrderByDescending(c => c.Rank).ToList();
                    if (targetCards.Any()) return targetCards.First();
                    return validCards.OrderByDescending(c => c.Rank).First();

                case "AvoidKitty":
                case "GuardQueen":
                    // Play the absolute lowest card to avoid taking the kitty, or to protect the Queen of Spades
                    return validCards.OrderBy(c => c.Rank).First();

                case "AvoidVoidLead":
                    var oppVoids = state.MemoryTracker.PlayerVoids.Where(kvp => kvp.Key != aiPlayer.Id && kvp.Value.Any()).SelectMany(kvp => kvp.Value).Distinct().ToList();
                    var safeLeads = validCards.Where(c => !oppVoids.Contains(c.Suit)).ToList();
                    return safeLeads.Any() ? safeLeads.OrderBy(c => c.Rank).First() : validCards.OrderBy(c => c.Rank).First();

                case "StopMoon":
                    // To stop a moon, we want to play the HIGHEST card possible to try and steal the trick and the points
                    if (ledSuit.HasValue)
                    {
                        var suitCards = validCards.Where(c => c.Suit == ledSuit.Value).OrderByDescending(c => c.Rank).ToList();
                        if (suitCards.Any()) return suitCards.First();
                    }
                    return validCards.OrderByDescending(c => c.Rank).First();

                case "PlaySafe":
                case "AvoidingPoints":
                case "DuckingTrick":
                    if (ledSuit.HasValue)
                    {
                        var suitCards = validCards.Where(c => c.Suit == ledSuit.Value).OrderBy(c => c.Rank).ToList();
                        if (suitCards.Any()) 
                        {
                            // Opportunistic Cancellation: If a card can be safely cancelled, take the free discard
                            var cancelMatch = suitCards.FirstOrDefault(vc => state.CurrentTrick.Any(tc => tc.Suit == vc.Suit && tc.Rank == vc.Rank));
                            if (cancelMatch != null) return cancelMatch;

                            // Play the highest card that won't win the trick
                            var highestInTrick = state.CurrentTrick.Where(c => c.Suit == ledSuit.Value).OrderByDescending(c => c.Rank).FirstOrDefault();
                            if (highestInTrick != null) 
                            {
                                var highestSafe = suitCards.LastOrDefault(c => c.Rank < highestInTrick.Rank);
                                if (highestSafe != null) return highestSafe;
                            }
                            
                            // If we must take the trick (no lower cards), play lowest to minimize damage from future tricks
                            return suitCards.First(); 
                        }
                        
                        // We are VOID in the led suit and cannot possibly win. Throw away highest non-point "loser" card safely.
                        var nonPoints = validCards.Where(c => c.PointValue == 0).OrderByDescending(c => c.Rank).ToList();
                        if (nonPoints.Any()) return nonPoints.First();
                        
                        // If forced to play points on a duck, play the lowest point card possible (e.g. 2H over QS)
                        return validCards.OrderBy(c => c.PointValue).ThenBy(c => c.Rank).First();
                    }
                    // Leading the trick: Find the suit with the fewest known opponent voids to minimize being dumped on.
                    var safeLead = validCards
                        .GroupBy(c => c.Suit)
                        .Select(g => new { 
                            Suit = g.Key, 
                            VoidCount = state.MemoryTracker.PlayerVoids.Values.Count(v => v.Contains(g.Key)),
                            LowestCard = g.OrderBy(c => c.Rank).First()
                        })
                        .OrderBy(x => x.VoidCount)
                        .ThenBy(x => x.LowestCard.Rank)
                        .First();
                    return safeLead.LowestCard;

                case "DumpPenalty":
                case "DiscardPoints":
                case "AggressiveFeeding":
                    if (!ledSuit.HasValue) 
                    {
                        // We are leading the trick. NEVER blindly lead the highest point validCard just to Discard!
                        // Instead, secretly short a non-penalty suit so we can safely discard LATER.
                        // Priority: Avoid suits where others are void, then pick shortest suit to void ourselves.
                        var strategicLeads = validCards.Where(c => c.PointValue == 0)
                            .GroupBy(c => c.Suit)
                            .Select(g => new {
                                Suit = g.Key,
                                VoidCount = state.MemoryTracker.PlayerVoids.Values.Count(v => v.Contains(g.Key)),
                                Cards = g.OrderBy(c => c.Rank).ToList()
                            })
                            .OrderBy(x => x.VoidCount)
                            .ThenBy(x => x.Cards.Count)
                            .ToList();

                        if (strategicLeads.Any()) {
                            return strategicLeads.First().Cards.First(); // Play lowest of best strategic suit
                        }
                    }
                    // Dump the highest point cards or highest ranks
                    return validCards.OrderByDescending(c => c.PointValue).ThenByDescending(c => c.Rank).First();

                case "CancelQueen":
                    var qsMatch = validCards.FirstOrDefault(vc => vc.Suit == Suit.Spades && vc.Rank == Rank.Queen && state.CurrentTrick.Any(tc => tc.Suit == Suit.Spades && tc.Rank == Rank.Queen));
                    if (qsMatch != null) return qsMatch;
                    return validCards.OrderByDescending(c => c.PointValue).ThenByDescending(c => c.Rank).First();

                case "LeadQueen":
                    var queen = validCards.FirstOrDefault(c => c.Suit == Suit.Spades && c.Rank == Rank.Queen);
                    if (queen != null) return queen;
                    break;
                
                case "ClearSpades":
                    // Safely bleed spades by playing the highest spade under the Queen.
                    var safeSpades = validCards.Where(c => c.Suit == Suit.Spades && c.Rank < Rank.Queen).OrderByDescending(c => c.Rank).ToList();
                    if (safeSpades.Any()) return safeSpades.First();
                    // Fallback to lowest spade if only Ace/King are left to try and drop them safely
                    var otherSpade = validCards.Where(c => c.Suit == Suit.Spades).OrderBy(c => c.Rank).FirstOrDefault();
                    if (otherSpade != null) return otherSpade;
                    break;

                case "ShootTheMoon":
                    if (ledSuit.HasValue)
                    {
                        bool hasLedSuit = validCards.Any(c => c.Suit == ledSuit.Value);
                        if (!hasLedSuit)
                        {
                            // Void in led suit! We CANNOT win this trick. Do NOT throw away points, otherwise our moonshot fails.
                            var moonSafeDiscards = validCards.Where(c => c.PointValue == 0).OrderBy(c => c.Rank).ToList();
                            if (moonSafeDiscards.Any()) return moonSafeDiscards.First();
                        }
                    }
                    // Take the trick! Play highest.
                    return validCards.OrderByDescending(c => c.Rank).First();
            }

            // Default: Play lowest card to stay safe
            return validCards.OrderBy(c => c.Rank).First();
        }

        private List<Card> GetValidCards(List<Card> hand, GameState state)
        {
            return hand.Where(c => _logic.IsValidPlay(hand, state.CurrentTrick, c, state.HeartsBroken, state.IsFirstTrickOfHand, state.Rules).IsValid).ToList();
        }

        public void ClearAllCaches()
        {
            _llmInference.ClearAllBotCaches();
        }

        public void CheckAndProcessAiPasses(GameSessionManager gameManager)
        {
            var state = gameManager.GetState();
            
            // Clear KV caches if we are starting a new hand (RoundNumber changed)
            if (_lastSeenRoundNumber != state.RoundNumber)
            {
                _llmInference.ClearAllBotCaches();
                _lastSeenRoundNumber = state.RoundNumber;
            }
            
            if (state.Phase == GameState.GamePhase.Passing) {
                var ai = state.Players.FirstOrDefault(p => p.IsAi && !state.PendingPasses.ContainsKey(p.Id));
                if (ai != null) {
                    lock (_aiTurnLock) {
                        if (_isProcessingAiTurn) return;
                        _isProcessingAiTurn = true;
                    }
                    _ = Task.Run(async () => {
                        try {
                            await PerformAiPassAsync(ai, state, gameManager);
                        }
                        finally {
                            lock (_aiTurnLock) { _isProcessingAiTurn = false; }
                        }
                    });
                }
            }
        }

        public void CheckAndPlayAiTurns(GameSessionManager gameManager)
        {
            var state = gameManager.GetState();
            
            // Clear KV caches if we are starting a new hand (RoundNumber changed)
            if (_lastSeenRoundNumber != state.RoundNumber)
            {
                _llmInference.ClearAllBotCaches();
                _lastSeenRoundNumber = state.RoundNumber;
            }
            
            if (state.Phase == GameState.GamePhase.Passing) {
                CheckAndProcessAiPasses(gameManager);
                return;
            }
            if (state.Phase != GameState.GamePhase.Playing) return;
            var activePlayer = state.Players[state.CurrentTurnPlayerIndex];
            if (!activePlayer.IsAi) return;

            lock (_aiTurnLock) {
                if (_isProcessingAiTurn) return;
                _isProcessingAiTurn = true;
            }

            _ = Task.Run(async () => {
                try {
                    var (card, reason) = await GenerateMoveAsync(state, activePlayer, "Empty");
                    state.LastMoveReasoning[activePlayer.Id] = reason;
                    gameManager.PlayCard(activePlayer.Id, card);
                }
                finally {
                    lock (_aiTurnLock) { _isProcessingAiTurn = false; }
                }
            });
        }

        private async Task PerformAiPassAsync(Player ai, GameState state, GameSessionManager gameManager)
        {
            var cardsToPass = new List<Card>();
            if (state.RoundNumber % 4 == 0) {
                state.LastMoveReasoning[ai.Id] = "Hold round.";
                gameManager.PassCards(ai.Id, cardsToPass);
                return;
            }

            double effectiveSkill = Math.Max(1.0, ai.DifficultyLevel + ai.SkillOffset);
            
            if (effectiveSkill >= 2.5) 
            {
                bool passingLeft = (state.RoundNumber % 4) == 1;
                bool passingRight = (state.RoundNumber % 4) == 2;
                string dir = passingLeft ? "Left" : passingRight ? "Right" : "Across";
                
                string prompt = $@"You are {ai.Name} (Grandmaster).
Respond purely with valid JSON.
**PASSING PHASE**{dir}.
Hand: {string.Join(", ", ai.Hand.OrderBy(c => c.Suit).ThenBy(c => c.Rank).Select(c => c.ToShortString()))}

Strategies:
- Void a suit: Pass all cards of a suit to become void.
- Break pairs: Pass one card from a pair to setup cancellations.
- Dump: Pass Ace/King of Spades or high Hearts if unprotected.
- Pass Queens: Pass Queen of Spades if you have < 3 lower spades.

Provide exactly 3 valid card IDs from your hand in a JSON array.
JSON:{{
  ""Reasoning"": ""..."",
  ""Pass"": [""Card1"", ""Card2"", ""Card3""]
}}";
                
                var grammar = state.ShowAiReasoning ? null : "JSON";
                string responseRaw = await _llmInference.GenerateMoveIntentAsync(ai.Id, prompt, 0.4f, 60, grammar);
                string responseJson = responseRaw.Trim();
                if (!responseJson.StartsWith("{")) responseJson = "{ \"Pass\": [\"" + responseRaw;
                
                var matchJson = System.Text.RegularExpressions.Regex.Match(responseJson, @"\{[\s\S]*\}");
                if (matchJson.Success) responseJson = matchJson.Value;

                string reasoning = "Used LLM passing logic.";
                try 
                {
                    using var doc = JsonDocument.Parse(responseJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Pass", out var passProp) && passProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in passProp.EnumerateArray())
                        {
                            string cid = elem.GetString()?.Trim() ?? "";
                            var match = ai.Hand.FirstOrDefault(c => c.ToShortString().Equals(cid, StringComparison.OrdinalIgnoreCase) && !cardsToPass.Contains(c));
                            if (match != null) cardsToPass.Add(match);
                        }
                    }
                    if (root.TryGetProperty("Reasoning", out var rProp)) reasoning = rProp.GetString() ?? reasoning;
                }
                catch { }

                if (cardsToPass.Count == 3)
                {
                    state.LastMoveReasoning[ai.Id] = reasoning;
                    gameManager.PassCards(ai.Id, cardsToPass);
                    return;
                }
                cardsToPass.Clear(); // Fallback if hallucinated
            }

            var (finalCards, finalReason) = GetAiPassFallbackCards(ai, state);
            state.LastMoveReasoning[ai.Id] = finalReason;
            gameManager.PassCards(ai.Id, finalCards);
        }

        private (List<Card> Cards, string Reasoning) GetAiPassFallbackCards(Player ai, GameState state)
        {
            var cardsToPass = new List<Card>();

            int passCount = 3;
            string reasoning = "Passing highest cards.";

            if (ai.DifficultyLevel >= 4) {
                var hand = new List<Card>(ai.Hand);
                bool passingLeft = (state.RoundNumber % 4) == 1;

                // Priority 1: Pass Doubles (One from a pair, especially high cards, especially if passing left)
                var groupedCards = hand.GroupBy(c => new { c.Suit, c.Rank }).Where(g => g.Count() > 1).ToList();
                if (groupedCards.Any()) {
                    var bestPairs = groupedCards.OrderByDescending(g => g.Key.Suit == Suit.Spades && g.Key.Rank == Rank.Queen ? 100 : (int)g.Key.Rank).ToList();
                    foreach (var pair in bestPairs) {
                        // Breaking high pairs is excellent. If passing left, it's brutal. If not, it's still good.
                        if (pair.Key.Rank >= Rank.Jack || pair.Key.Suit == Suit.Hearts) {
                            var toPass = pair.First();
                            cardsToPass.Add(toPass);
                            hand.Remove(toPass);
                            reasoning = "Passing double for cancellation setup.";
                            if (cardsToPass.Count == passCount) break;
                        }
                    }
                }

                // Priority 2: Strategic Voiding (Diamonds, Clubs)
                if (cardsToPass.Count < passCount) {
                    var safeSuitsToVoid = new[] { Suit.Diamonds, Suit.Clubs };
                    foreach (var s in safeSuitsToVoid) {
                        var suitCards = hand.Where(c => c.Suit == s).ToList();
                        if (suitCards.Any() && cardsToPass.Count + suitCards.Count <= passCount) {
                            cardsToPass.AddRange(suitCards);
                            foreach(var c in suitCards) hand.Remove(c);
                            if (reasoning == "Passing highest cards.") reasoning = "Voiding a suit.";
                            else reasoning += " Voiding a suit.";
                            if (cardsToPass.Count == passCount) break;
                        }
                    }
                }

                // Priority 3: Spades Mitigation (Queen of Spades and High Spades)
                if (cardsToPass.Count < passCount) {
                    var spades = hand.Where(c => c.Suit == Suit.Spades).OrderBy(c => c.Rank).ToList();
                    var qsMatches = spades.Where(c => c.Rank == Rank.Queen).ToList();
                    if (qsMatches.Any()) {
                        // Are we padded?
                        int lowerSpades = spades.Count(c => c.Rank < Rank.Queen);
                        if (lowerSpades < 3) {
                            // Unsafe Queen. Pass it.
                            foreach(var qs in qsMatches.Take(passCount - cardsToPass.Count)) {
                                cardsToPass.Add(qs);
                                hand.Remove(qs);
                                if (reasoning == "Passing highest cards.") reasoning = "Passing unsafe Queen.";
                                else reasoning += " Bleeding Queen.";
                            }
                        }
                    }
                    
                    if (cardsToPass.Count < passCount) {
                        // Unprotected Aces/Kings of spades
                        int lowerSpades = spades.Count(c => c.Rank < Rank.Queen);
                        var highSpades = spades.Where(c => c.Rank > Rank.Queen).OrderByDescending(c => c.Rank).ToList();
                        if (lowerSpades < 4 && highSpades.Any()) {
                            foreach (var hs in highSpades.Take(passCount - cardsToPass.Count)) {
                                cardsToPass.Add(hs);
                                hand.Remove(hs);
                                if (reasoning == "Passing highest cards.") reasoning = "Bleeding unsafe high spade.";
                                else reasoning += " Bleeding high spade.";
                            }
                        }
                    }
                }

                // Priority 4: Dump High Cards
                if (cardsToPass.Count < passCount) {
                    var remaining = hand.OrderByDescending(c => c.PointValue > 0 ? 100 + c.PointValue : (int)c.Rank).Take(passCount - cardsToPass.Count).ToList();
                    cardsToPass.AddRange(remaining);
                    if (reasoning == "Passing highest cards.") reasoning = "Discarding high potential winners.";
                }
            } 
            else 
            {
                // Diff < 4: Naive passing logic
                var shortSuits = ai.Hand.GroupBy(c => c.Suit).OrderBy(g => g.Count()).ToList();
                foreach (var group in shortSuits) {
                    if (cardsToPass.Count + group.Count() <= passCount) cardsToPass.AddRange(group);
                }
                if (cardsToPass.Count < passCount) {
                    var remaining = ai.Hand.Except(cardsToPass).OrderByDescending(c => c.Suit == Suit.Spades && c.Rank == Rank.Queen ? 100 : (int)c.Rank).Take(passCount - cardsToPass.Count);
                    cardsToPass.AddRange(remaining);
                }
            }

            cardsToPass = cardsToPass.Take(passCount).ToList();
            return (cardsToPass, reasoning.Trim());
        }
    }
}
