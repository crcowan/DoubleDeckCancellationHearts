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
        private static bool _isProcessingAiTurn = false;
        private static readonly object _aiTurnLock = new object();

        private readonly GameLogicService _logic;
        private readonly LlmInferenceService _llmInference;

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
            if (effectiveSkill >= 4.0)
            {
                Suit? overrideLedSuit = state.CurrentTrick.FirstOrDefault()?.Suit;

                // 1. Lead Queen of Spades
                if (state.CurrentTrick.Count == 0 && validCards.Any(c => c.IsQueenOfSpades))
                {
                    var card = validCards.First(c => c.IsQueenOfSpades);
                    return (card, "[Strategic] Grandmaster lead: Forcing the Queen of Spades to smoke out opponents.");
                }

                // 2. Cancellation (Identical card in trick)
                var cancelTarget = validCards.FirstOrDefault(vc =>
                    (vc.Rank >= Rank.Jack || vc.Suit == Suit.Hearts) &&
                    state.CurrentTrick.Any(tc => tc.Suit == vc.Suit && tc.Rank == vc.Rank));
                if (cancelTarget != null)
                {
                    return (cancelTarget, $"[Strategic] Grandmaster cancellation: Matching the {cancelTarget} in the trick.");
                }

                // 3. Stop Moonshot
                var moonThreat = state.Players.FirstOrDefault(p => p.Id != aiPlayer.Id && p.HandScore >= 6 && state.Players.All(other => other.Id == p.Id || other.HandScore == 0));
                if (moonThreat != null && validCards.Any(c => c.Rank == Rank.Ace))
                {
                    var ace = validCards.First(c => c.Rank == Rank.Ace);
                    return (ace, $"[Strategic] Grandmaster defense: Forcing the {ace} to prevent {moonThreat.Name} from shooting the moon.");
                }

                // 4. Cancel Queen
                if (state.CurrentTrick.Any(tc => tc.IsQueenOfSpades) && validCards.Any(vc => vc.IsQueenOfSpades))
                {
                    var qs = validCards.First(vc => vc.IsQueenOfSpades);
                    return (qs, "[Strategic] Grandmaster maneuver: Cancelling the Queen of Spades to nullify the point penalty.");
                }

                // 5. Dump Penalty (Void in led suit)
                if (overrideLedSuit.HasValue && !validCards.Any(c => c.Suit == overrideLedSuit.Value))
                {
                    var penaltyCards = validCards.Where(c => c.PointValue > 0).OrderByDescending(c => c.PointValue).ThenByDescending(c => c.Rank).ToList();
                    if (penaltyCards.Any())
                    {
                        var dump = penaltyCards.First();
                        return (dump, $"[Strategic] Grandmaster discard: Dumping the {dump} while void in {overrideLedSuit}.");
                    }
                }

                // 6. Aggressive Feeding
                if (overrideLedSuit.HasValue && state.CurrentTrick.Any(c => c.PointValue > 0))
                {
                    var suitCards = validCards.Where(c => c.Suit == overrideLedSuit.Value).OrderBy(c => c.Rank).ToList();
                    var highestInTrick = state.CurrentTrick.Where(c => c.Suit == overrideLedSuit.Value).OrderByDescending(c => c.Rank).FirstOrDefault();
                    if (highestInTrick != null)
                    {
                        var highestSafe = suitCards.LastOrDefault(c => c.Rank < highestInTrick.Rank);
                        if (highestSafe != null && (highestSafe.PointValue > 0 || highestSafe.Rank >= Rank.Ten))
                        {
                            return (highestSafe, $"[Strategic] Grandmaster feeding: Dumping the {highestSafe} on a high-value trick.");
                        }
                    }
                }

                // 7. Take Control (Ace of led suit)
                if (overrideLedSuit.HasValue && validCards.Any(c => c.Suit == overrideLedSuit.Value && c.Rank == Rank.Ace))
                {
                    var ace = validCards.First(c => c.Suit == overrideLedSuit.Value && c.Rank == Rank.Ace);
                    return (ace, $"[Strategic] Grandmaster control: Taking the lead with the {ace}.");
                }

                // 8. Shoot the Moon (If AI has all points and can win)
                bool moonshot = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0) && aiPlayer.HandScore > 0;
                if (moonshot)
                {
                    // Basic heuristic: play highest possible to keep the lead
                    var highest = validCards.OrderByDescending(c => c.Rank).First();
                    return (highest, $"[Strategic] Grandmaster moonshot: Playing the {highest} to maintain momentum.");
                }
            }

            // --- Nuanced Decision Mode (LLM Consultation) ---
            // If no deterministic strategic override fired, consult the LLM for reasoning and move selection.
            
            // Decide which prompt profile to use based on the Performance Toggle
            string prompt = state.ShowAiReasoning 
                ? ConstructPromptVerbose(state, aiPlayer, validCards, effectiveSkill, fieldIntel, forcedMistakeCard)
                : ConstructPromptNano(state, aiPlayer, validCards, effectiveSkill, fieldIntel, forcedMistakeCard);
            
            float temperature = 0.5f;
            int maxTokens = state.ShowAiReasoning ? 60 : 25; 

            if (effectiveSkill >= 4.0) { temperature = 0.2f; if (state.ShowAiReasoning) maxTokens = 80; }
            else if (effectiveSkill >= 2.5) { temperature = 0.7f; if (state.ShowAiReasoning) maxTokens = 100; }
            else { temperature = 1.0f; if (state.ShowAiReasoning) maxTokens = 40; }

            var grammar = state.ShowAiReasoning ? null : "JSON"; 
            string responseRaw = await _llmInference.GenerateMoveIntentAsync(prompt, state.SelectedAiModel, temperature, maxTokens, grammar);
            
            string responseJson = responseRaw.Trim();
            if (!responseJson.StartsWith("{"))
            {
                responseJson = state.ShowAiReasoning 
                    ? "{ \"Reasoning\": \"" + responseRaw.Replace("\"", "\\\"").Replace("\n", " ") + "\" }" 
                    : "{ \"Intent\": \"" + responseRaw.Trim() + "\" }";
            }
 
            string intentStr = "PlaySafe";
            string suggestedCardId = "";
            string reasoning = "";
 
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("Intent", out var iProp)) intentStr = iProp.GetString() ?? "PlaySafe";
                if (root.TryGetProperty("SuggestedCard", out var sProp)) suggestedCardId = sProp.GetString() ?? "";
                if (root.TryGetProperty("Reasoning", out var rProp)) reasoning = rProp.GetString() ?? "";
            }
            catch
            {
                if (responseRaw.Contains("Intent")) 
                {
                    var match = System.Text.RegularExpressions.Regex.Match(responseRaw, "\"Intent\":\\s*\"([^\"]+)\"");
                    if (match.Success) intentStr = match.Groups[1].Value;
                }
            }

            var (validTactics, validTacticDefs) = GetDynamicTactics(state, aiPlayer, validCards, effectiveSkill);
            
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

        private string ConstructPromptVerbose(GameState state, Player aiPlayer, List<Card> validCards, double effectiveSkill, string fieldIntel, Card? forcedMistake)
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

            return $@"<bos><start_of_turn>user
{personaLine}
Respond purely with valid JSON. Do not add conversational text.

**GAME STATE**
Pos: {state.CurrentTrick.Count + 1}/{state.Players.Count}
Scores: {scoreboard}
Cancelled Pile: {state.CancelledKitty.Sum(c => c.PointValue)}pts | You: {aiPlayer.HandScore}pts
{counts}

**MEMORY & HISTORY**
DEDUCTIONS: {voids}
{knownInfo}{strategySection}{historySection}{prevTrickStr}
{mistakeNote}

**CURRENT TRICK**
LED: {ledSuit?.ToString() ?? "None"}. You MUST follow the led suit IF you have it. If void, you can discard any suit.
Trick: {trickStr}
HAND: {fullHandStr}

**CONSTRAINTS**
GOAL: Avoid penalty points. Giving points to others is GOOD (unless they are shooting the moon).
Reasoning: ONE short sentence. MUST MATCH your Intent. Explain why you are choosing the specific TACTIC. BANNED: 'opponent', 'player'. Use 1st person 'I' and specific names.
TACTICS: {tacticsStr}
DEFS: {defsStr}

JSON FORMAT:
{{
  ""Reasoning"": ""(One short sentence holding your plan)"",
  ""Intent"": ""(TACTIC_NAME)"",
  ""SuggestedCard"": ""(e.g. '9D')""
}}<end_of_turn>
<start_of_turn>model
{{ ""Reasoning"": """;
        }

        private string ConstructPromptNano(GameState state, Player aiPlayer, List<Card> validCards, double effectiveSkill, string fieldIntel, Card? forcedMistake)
        {
            var (_, voids, knownInfo, strategySection, historySection, _, trickStr, fullHandStr, counts, prevTrickStr) = GetCommonPromptData(state, aiPlayer, validCards, effectiveSkill, forcedMistake);
            bool moonshotPossible = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0);
            Suit? ledSuit = state.CurrentTrick.FirstOrDefault()?.Suit;

            var (tactics, tacticDefs) = GetDynamicTactics(state, aiPlayer, validCards, effectiveSkill);
            string tacticsStr = string.Join(",", tactics);
            string defsStr = string.Join(" | ", tacticDefs);
            if (effectiveSkill >= 4.0) defsStr += " | GM_RULE: Never default to PlaySafe if an advanced tactic is available";
            string prefix = effectiveSkill >= 4.0 ? "GM" : (effectiveSkill >= 2.5 ? "PRO" : "NOOB");

            // Hyper-compressed Nano prompt
            // V:Voids, K:Known, S:Strategy, H:History, PT:PrevTrick, L:Led, HND:Hand, C:Counts, TR:Trick
            return $@"<bos><start_of_turn>user
[{prefix}:{aiPlayer.Name}]
V:{voids}
{knownInfo}{strategySection}{historySection}{prevTrickStr}
L:{ledSuit?.ToString()?[0] ?? 'N'}
HND:{fullHandStr}
{counts}
TR:{trickStr}
OPTS:{tacticsStr}
DEFS:{defsStr}
JSON:{{""Intent"":""TACTIC_NAME"",""SuggestedCard"":""CARD_ID""}}<end_of_turn>
<start_of_turn>model
{{ ""Intent"": """;
        }

        private (List<string> Tactics, List<string> TacticDefs) GetDynamicTactics(GameState state, Player aiPlayer, List<Card> validCards, double effectiveSkill)
        {
            List<string> tactics = new List<string> { "PlaySafe" };
            List<string> tacticDefs = new List<string> { "PlaySafe: Play lowest card" };
            bool moonshotPossible = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0);
            Suit? ledSuit = state.CurrentTrick.FirstOrDefault()?.Suit;

            if (state.CurrentTrick.Count == 0) // Leading
            {
                if (validCards.Any(c => c.Suit == Suit.Spades)) { tactics.Add("ClearSpades"); tacticDefs.Add("ClearSpades: Lead Spades to drain them"); }
                if (validCards.Any(c => c.IsQueenOfSpades)) { tactics.Add("LeadQueen"); tacticDefs.Add("LeadQueen: Force Queen out"); }
            }
            else // Following
            {
                tactics.Add("DuckingTrick"); tacticDefs.Add("DuckingTrick: Play under the highest card");
                tactics.Add("Cancellation"); tacticDefs.Add("Cancellation: Play identical card to cancel");
                if (state.CurrentTrick.Any(c => c.PointValue > 0) || validCards.Any(c => c.PointValue > 0)) { tactics.Add("AggressiveFeeding"); tacticDefs.Add("AggressiveFeeding: Dump points on winner"); }
                if (state.CurrentTrick.Any(c => c.IsQueenOfSpades) && validCards.Any(c => c.IsQueenOfSpades)) { tactics.Add("CancelQueen"); tacticDefs.Add("CancelQueen: Cancel the Queen"); }
                if (ledSuit.HasValue && validCards.Any(c => c.Suit != ledSuit.Value)) { tactics.Add("DumpPenalty"); tacticDefs.Add("DumpPenalty: Discard high points when void"); }
                
                // If the trick is currently completely safe, consider taking it to gain the lead
                if (state.CurrentTrick.Sum(c => c.PointValue) == 0 && validCards.Any(c => c.PointValue == 0)) { 
                    tactics.Add("TakeControl"); tacticDefs.Add("TakeControl: Win safe trick to gain lead"); 
                }
            }
            
            moonshotPossible = state.Players.All(p => p.Id == aiPlayer.Id || p.HandScore == 0) && aiPlayer.HandScore > 0;
            if (moonshotPossible) { tactics.Add("ShootTheMoon"); tacticDefs.Add("ShootTheMoon: Take all penalty points"); }
            
            // If an opponent has a significant number of points and no one else has any, they are a moon threat!
            var moonThreat = state.Players.FirstOrDefault(p => p.Id != aiPlayer.Id && p.HandScore >= 6 && state.Players.All(other => other.Id == p.Id || other.HandScore == 0));
            if (moonThreat != null) {
                tactics.Add("StopMoon"); tacticDefs.Add("StopMoon: Sacrifice to take points from moon-shooter");
            }
            
            // --- Skill Gradation Engine ---
            if (effectiveSkill >= 4.0 && tactics.Count > 1) 
            {
                tactics.Remove("PlaySafe");
                tacticDefs.RemoveAll(d => d.StartsWith("PlaySafe:"));
            }
            else if (effectiveSkill < 2.5) 
            {
                tactics.Remove("Cancellation");
                tactics.Remove("CancelQueen");
                tactics.Remove("ClearSpades");
                tactics.Remove("DuckingTrick");
                tactics.Remove("TakeControl");
                tactics.Remove("StopMoon");
                tacticDefs.RemoveAll(d => d.StartsWith("Cancellation:") || d.StartsWith("CancelQueen:") || d.StartsWith("ClearSpades:") || d.StartsWith("DuckingTrick:") || d.StartsWith("TakeControl:") || d.StartsWith("StopMoon:"));
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

                case "TakeControl":
                    if (ledSuit.HasValue)
                    {
                        var suitCards = validCards.Where(c => c.Suit == ledSuit.Value && c.PointValue == 0).OrderByDescending(c => c.Rank).ToList();
                        if (suitCards.Any()) return suitCards.First(); // Play highest safe card to win
                    }
                    // If void, we can't win. Just play lowest card.
                    return validCards.OrderBy(c => c.Rank).First();

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
                    // Leading the trick: play lowest
                    return validCards.OrderBy(c => c.Rank).First();

                case "DumpPenalty":
                case "DiscardPoints":
                case "AggressiveFeeding":
                    if (!ledSuit.HasValue) 
                    {
                        // We are leading the trick. NEVER blindly lead the highest point validCard just to Discard!
                        // Instead, secretly short a non-penalty suit so we can safely discard LATER.
                        var safeLeads = validCards.Where(c => c.PointValue == 0).GroupBy(c => c.Suit).OrderBy(g => g.Count()).ToList();
                        if (safeLeads.Any()) {
                            return safeLeads.First().OrderBy(c => c.Rank).First(); // Play lowest of shortest suit
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

        public void CheckAndPlayAiTurns(GameSessionManager gameManager)
        {
            var state = gameManager.GetState();
            if (state.Phase == GameState.GamePhase.Passing) {
                foreach (var ai in state.Players.Where(p => p.IsAi && !state.PendingPasses.ContainsKey(p.Id))) {
                    PerformAiPass(ai, state, gameManager);
                }
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

        private void PerformAiPass(Player ai, GameState state, GameSessionManager gameManager)
        {
            var cardsToPass = new List<Card>();
            if (state.RoundNumber % 4 == 0) {
                state.LastMoveReasoning[ai.Id] = "Hold round.";
                gameManager.PassCards(ai.Id, cardsToPass);
                return;
            }

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
            state.LastMoveReasoning[ai.Id] = reasoning.Trim();
            gameManager.PassCards(ai.Id, cardsToPass);
        }
    }
}
