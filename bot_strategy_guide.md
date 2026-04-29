# 🧠 Bot Strategy & Terms Guide: Double Deck Cancellation Hearts

This guide explains how the AI thinks and defines the special terms used in the game's "Internal Monologues."

---

## 📖 Key Terms

To understand why a bot makes a move, you first need to know these core game concepts:

| Term | Definition |
| :--- | :--- |
| **Trick** | One round of play where every player contributes one card. The "Winner" of the trick takes all cards played. |
| **Led Suit** | The suit of the very first card played in a trick. All other players **must** play this suit if they have it. |
| **Void** | When a player has ZERO cards of a certain suit. Being "Void" is a major advantage because it lets you play any card (like a penalty card) safely. |
| **Breaking Hearts** | You cannot lead a trick with a Heart until someone has played one on a different suit first. This "breaks" the seal on Hearts. |
| **Cancellation** | **(Unique to this game)** Since there are two decks, two identical cards (e.g., two Kings of Diamonds) can appear in one trick. If they do, they "Cancel" each other out. Neither can win the trick! |
| **The Kitty** | A small pile of extra cards dealt face-down at the start. The winner of the first trick takes these cards (and any penalty points hidden inside). |

---

## 🛡️ Defensive Strategies

Most bot moves are designed to avoid taking points.

### **1. Ducking the Trick**
*   **The Move:** Playing a card that is as high as possible **but still lower** than the current winning card.
*   **Why:** You want to get rid of a medium-rank card (like a 10 or Jack) that might win a later trick, but you want to ensure someone else takes the current one.

### **2. Discarding Points (Dumping)**
*   **The Move:** Throwing away a Heart or the Queen of Spades when you are **Void** in the led suit.
*   **Why:** Since you aren't playing the led suit, you can't win the trick. This is a "free" chance to give your penalty points to the person winning the trick.

### **3. Avoiding Points**
*   **The Move:** Playing the lowest possible card of the led suit.
*   **Why:** The simplest way to stay safe. If you play a 2 or 3, it is very unlikely you will be forced to take the trick.

### **4. Cancellation & Cancel Queen**
*   **The Move:** Purposefully playing an identical card to one already in the trick.
*   **Why:** If a bot sees a King of Clubs is winning, and it also has a King of Clubs, it will play it to "Cancel" both. This forces the trick win to fall to a lower card or even cancels the trick entirely!

---

## ⚔️ Offensive Strategies

Advanced bots (Amateur and above) will sometimes try to trap you.

### **1. Clear Spades (Bleeding)**
*   **The Move:** Leading high Spades early in the game.
*   **Why:** The goal is to force everyone to play their Spades. If you run out of Spades early, you'll be forced to take the **Queen of Spades** (13 points) later when someone else leads it.

### **2. Lead Queen**
*   **The Move:** Leading the Queen of Spades itself.
*   **Why:** Dangerous, but effective if the bot knows other players are low on Spades. It forces you to play a King or Ace, meaning *you* take the 13 points instead of the bot.

### **3. Aggressive Feeding**
*   **The Move:** Playing a high-value card into a trick that a bot knows an opponent is guaranteed to win.
*   **Why:** "Feeding" the leader points to ensure they stay ahead in the score (and thus lose the match).

---

## 🌕 The Ultimate Move: Shooting the Moon

*   **The Strategy:** Trying to capture **all 26 Hearts and all 4 Queens of Spades** (a total of 52 points).
*   **The Result:** If a bot succeeds, they don't get 52 points—instead, their score **decreases by 52** (or everyone else's increases). 
*   **Bot Logic:** If a bot sees they have already accidentally taken 2 or 3 Queens, they might suddenly switch from "Defensive" to "Aggressive" to try and finish the set!

---

### **Skill Level Summary**
*   **Beginner:** Mostly uses *Avoiding Points*. Often makes mistakes and wins tricks accidentally.
*   **Intermediate:** Uses *Ducking* and *Discarding* effectively. Starts monitoring *Voids*.
*   **Grandmaster:** Tracks exactly which cards have been played, uses *Cancellation* to trap opponents, and will actively try to *Shoot the Moon* if the opportunity arises.
