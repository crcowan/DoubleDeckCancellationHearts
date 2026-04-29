import json
import random

suits = ['C', 'D', 'H', 'S']
non_point_suits = ['C', 'D']
ranks = ['2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K', 'A']
low_ranks = ['2', '3', '4', '5', '6', '7', '8']
high_ranks = ['9', '10', 'J', 'Q', 'K', 'A']

def card(r, s):
    return f"{r}{s}"

def rand_non_point_card(exclude=None):
    """Random card that is NOT a heart and NOT QS"""
    if exclude is None:
        exclude = set()
    while True:
        s = random.choice(non_point_suits)
        r = random.choice(ranks)
        c = f"{r}{s}"
        if c not in exclude:
            return c

def rand_card_of_suit(suit, exclude=None, rank_pool=None):
    if exclude is None:
        exclude = set()
    if rank_pool is None:
        rank_pool = ranks
    while True:
        r = random.choice(rank_pool)
        c = f"{r}{suit}"
        if c not in exclude:
            return c

def base_state(**overrides):
    state = {
        "AiScore": 0, "AiHandScore": 0,
        "OpponentScores": {"Bot2": 0, "Bot3": 0, "Bot4": 0},
        "OpponentHandScores": {"Bot2": 0, "Bot3": 0, "Bot4": 0},
        "OpponentProfiles": {"Bot2": "Bal", "Bot3": "Bal", "Bot4": "Bal"}
    }
    state.update(overrides)
    return state

def generate_scenarios():
    scenarios = []
 
    # ========================================================================
    # 1. PlaySafe_Lead
    # ========================================================================
    for i in range(20):
        suit = random.choice(non_point_suits)
        hand = [rand_card_of_suit(suit, rank_pool=low_ranks), rand_non_point_card()]
        hand = [c for c in hand if not c.endswith('S') and c != 'QS']
        while len(hand) < 3:
            hand.append(rand_non_point_card(set(hand)))
        scenarios.append({
            "ScenarioId": f"PlaySafe_Lead_{i+1}",
            "Description": "Experienced AI leading. No spades, no queen. Should play safe (lowest).",
            "AiSkill": 3.0,
            "ExpectedIntent": "PlaySafe",
            "ExpectedCard": "", # Will check lowest rank in tester logic if we wanted, but intent is enough
            "GameState": base_state(AiHand=hand, LedSuit="", CurrentTrick=[])
        })
 
    # ========================================================================
    # 2. ClearSpades
    # ========================================================================
    for i in range(20):
        # AI has spades but NOT QS. Should lead highest spade under Queen if possible, or just a spade.
        spade = rand_card_of_suit('S', exclude={'QS'}, rank_pool=['7','8','9','10','J'])
        hand = [spade, rand_non_point_card(), rand_non_point_card()]
        scenarios.append({
            "ScenarioId": f"ClearSpades_{i+1}",
            "Description": "AI leading. Has spades (not QS). Should clear spades.",
            "AiSkill": 5.0,
            "ExpectedIntent": "ClearSpades",
            "ExpectedCard": spade,
            "GameState": base_state(AiHand=hand, LedSuit="", CurrentTrick=[])
        })
 
    # ========================================================================
    # 3. LeadQueen
    # ========================================================================
    for i in range(20):
        # AI has QS. Override 1 should force LeadQueen regardless of other spades.
        other1 = rand_non_point_card({"QS"})
        other2 = rand_non_point_card({"QS", other1})
        hand = ["QS", other1, other2]
        scenarios.append({
            "ScenarioId": f"LeadQueen_{i+1}",
            "Description": "AI leading. Has QS. Should lead Queen.",
            "AiSkill": 5.0,
            "ExpectedIntent": "LeadQueen",
            "ExpectedCard": "QS",
            "GameState": base_state(AiHand=hand, LedSuit="", CurrentTrick=[])
        })
 
    # ========================================================================
    # 4. DuckingTrick (GM style: highest safe card)
    # ========================================================================
    for i in range(20):
        suit = random.choice(non_point_suits)
        high = 'A'
        safe_high = 'K'
        low = '2'
        hand = [f"{safe_high}{suit}", f"{low}{suit}", rand_non_point_card()]
        scenarios.append({
            "ScenarioId": f"DuckingTrick_{i+1}",
            "Description": "Safe trick. AI has K and 2. Should duck with K (highest safe).",
            "AiSkill": 5.0,
            "ExpectedIntent": "DuckingTrick",
            "ExpectedCard": f"{safe_high}{suit}",
            "GameState": base_state(AiHand=hand, LedSuit=suit, CurrentTrick=[f"{high}{suit}"])
        })
 
    # ========================================================================
    # 5. Cancellation
    # ========================================================================
    for i in range(20):
        suit = random.choice(non_point_suits)
        rank = random.choice(high_ranks)
        the_card = f"{rank}{suit}"
        hand = [the_card, rand_non_point_card({the_card}), rand_non_point_card({the_card})]
        scenarios.append({
            "ScenarioId": f"Cancellation_{i+1}",
            "Description": "AI following. Has identical card. Should cancel.",
            "AiSkill": 5.0,
            "ExpectedIntent": "Cancellation",
            "ExpectedCard": the_card,
            "GameState": base_state(AiHand=hand, LedSuit=suit, CurrentTrick=[the_card])
        })
 
    # ========================================================================
    # 6. DumpPenalty
    # ========================================================================
    for i in range(20):
        led = random.choice(non_point_suits)
        other = 'D' if led == 'C' else 'C'
        hand = ["9H", "2H", rand_card_of_suit(other)]
        scenarios.append({
            "ScenarioId": f"DumpPenalty_{i+1}",
            "Description": "AI void in led suit. Has hearts. Should dump highest heart.",
            "AiSkill": 5.0,
            "ExpectedIntent": "DumpPenalty",
            "ExpectedCard": "9H",
            "GameState": base_state(AiHand=hand, LedSuit=led, CurrentTrick=[rand_card_of_suit(led)])
        })
 
    # ========================================================================
    # 7. AggressiveFeeding (GM style: dump points on winner)
    # ========================================================================
    for i in range(20):
        suit = random.choice(non_point_suits)
        # AI has a heart and is safe to play it because an Ace is in the trick
        hand = ["KH", f"2{suit}", rand_non_point_card()]
        # Wait, AggressiveFeeding heuristic requires following suit if possible
        # So hand must have a safe suit card if led suit is not Hearts.
        # Let's make AI have a safe HIGH suit card.
        hand = [f"K{suit}", f"2{suit}", "9H"]
        scenarios.append({
            "ScenarioId": f"AggressiveFeeding_{i+1}",
            "Description": "Trick has points. AI has K (safe) and 9H. Should play K (AggFeeding) or 9H if void.",
            "AiSkill": 5.0,
            "ExpectedIntent": "AggressiveFeeding",
            "ExpectedCard": f"K{suit}",
            "GameState": base_state(AiHand=hand, LedSuit=suit, CurrentTrick=[f"A{suit}", "2H"])
        })
 
    # ========================================================================
    # 8. CancelQueen
    # ========================================================================
    for i in range(20):
        hand = ["QS", "5S", "2S"]
        scenarios.append({
            "ScenarioId": f"CancelQueen_{i+1}",
            "Description": "Spades led. QS in trick. AI has QS. Should cancel.",
            "AiSkill": 5.0,
            "ExpectedIntent": "CancelQueen",
            "ExpectedCard": "QS",
            "GameState": base_state(AiHand=hand, LedSuit="S", CurrentTrick=["QS"])
        })
 
    # ========================================================================
    # 9. TakeControl
    # ========================================================================
    for i in range(20):
        suit = random.choice(non_point_suits)
        hand = [f"A{suit}", rand_non_point_card(), rand_non_point_card()]
        scenarios.append({
            "ScenarioId": f"TakeControl_{i+1}",
            "Description": "Safe trick. AI has Ace. Should take control.",
            "AiSkill": 5.0,
            "ExpectedIntent": "TakeControl",
            "ExpectedCard": f"A{suit}",
            "GameState": base_state(AiHand=hand, LedSuit=suit, CurrentTrick=[f"2{suit}"])
        })
 
    # ========================================================================
    # 10. StopMoon
    # ========================================================================
    for i in range(20):
        suit = random.choice(non_point_suits)
        hand = [f"A{suit}", "10S", "5D"]
        scenarios.append({
            "ScenarioId": f"StopMoon_{i+1}",
            "Description": "Moon threat. AI has Ace. Should stop moon.",
            "AiSkill": 5.0,
            "ExpectedIntent": "StopMoon",
            "ExpectedCard": f"A{suit}",
            "GameState": base_state(
                AiHand=hand, LedSuit=suit,
                CurrentTrick=[f"K{suit}", "2H"],
                OpponentHandScores={"Bot2": 15, "Bot3": 0, "Bot4": 0}
            )
        })
 
    # ========================================================================
    # 11. ShootTheMoon
    # ========================================================================
    for i in range(20):
        suit = random.choice(non_point_suits)
        hand = [f"A{suit}", "2S", "3D"]
        scenarios.append({
            "ScenarioId": f"ShootTheMoon_{i+1}",
            "Description": "AI shooting moon. Must win trick.",
            "AiSkill": 5.0,
            "ExpectedIntent": "ShootTheMoon",
            "ExpectedCard": f"A{suit}",
            "GameState": base_state(
                AiHand=hand, LedSuit=suit,
                CurrentTrick=[f"K{suit}", "2H"],
                AiHandScore=10
            )
        })
 
    with open('scenarios.json', 'w') as f:
        json.dump(scenarios, f, indent=2)
    print(f"Generated {len(scenarios)} scenarios.")

if __name__ == '__main__':
    generate_scenarios()
