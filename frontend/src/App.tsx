import { useState, useEffect, useRef } from 'react';
import { PlayingCard } from './components/PlayingCard';
import type { Card, GameState } from './models';
import { GamePhase } from './models';
import './index.css';

// Using a generic URL that would point to the local ASP.NET Core server
const API_URL = "http://localhost:5243/api/game";

function App() {
  const [gameState, setGameState] = useState<GameState | null>(null);
  const [numPlayers, setNumPlayers] = useState(5);
  const [aiDifficulty, setAiDifficulty] = useState(3);
  const [botNames, setBotNames] = useState<string[]>(['Bot 1', 'Bot 2', 'Bot 3', 'Bot 4', 'Bot 5', 'Bot 6', 'Bot 7', 'Bot 8', 'Bot 9', 'Bot 10']);

  // Rule Variations
  const [passingStyle, setPassingStyle] = useState("Standard"); // "Standard", "None"
  const [firstLead, setFirstLead] = useState("DealersLeft"); // "2OfClubs", "DealersLeft"
  const [breakingHearts, setBreakingHearts] = useState("Standard"); // "Standard", "Guts"
  const [cancellationWinner, setCancellationWinner] = useState("PreviousWinner"); // "PreviousWinner", "TrickLeader"
  const [targetScore, setTargetScore] = useState(100); // 50, 100, 150
  const [trickPauseMs, setTrickPauseMs] = useState(2500); // Configurable trick review timer

  const [selectedCard, setSelectedCard] = useState<Card | null>(null);
  const [selectedPassCards, setSelectedPassCards] = useState<Card[]>([]);
  const [newlyPassedCards, setNewlyPassedCards] = useState<Card[]>([]);

  // Ref to track the human hand right before the pass resolves
  const previousHandRef = useRef<Card[]>([]);

  const fetchState = async () => {
    try {
      const resp = await fetch(`${API_URL}/state`);
      if (resp.ok) {
        const data = await resp.json();
        setGameState(data);
      }
    } catch (e) {
      console.error("Backend not reachable yet", e);
    }
  };

  useEffect(() => {
    // Attempt to load settings from LocalStorage
    const savedPlayers = localStorage.getItem('dc-hearts-players');
    const savedDiff = localStorage.getItem('dc-hearts-diff');
    const savedBotNames = localStorage.getItem('dc-hearts-bot-names');
    const savedRules = localStorage.getItem('dc-hearts-rules');

    if (savedPlayers) setNumPlayers(parseInt(savedPlayers));
    if (savedDiff) setAiDifficulty(parseInt(savedDiff));
    if (savedBotNames) setBotNames(JSON.parse(savedBotNames));

    if (savedRules) {
      const parsed = JSON.parse(savedRules);
      if (parsed.passingStyle) setPassingStyle(parsed.passingStyle);
      if (parsed.firstLead) setFirstLead(parsed.firstLead);
      if (parsed.breakingHearts) setBreakingHearts(parsed.breakingHearts);
      if (parsed.cancellationWinner) setCancellationWinner(parsed.cancellationWinner);
      if (parsed.targetScore) setTargetScore(parsed.targetScore);
      if (parsed.trickPauseMs) setTrickPauseMs(parsed.trickPauseMs);
    }

    fetchState();
  }, []);

  const startGame = async () => {
    // Save choices
    localStorage.setItem('dc-hearts-players', numPlayers.toString());
    localStorage.setItem('dc-hearts-diff', aiDifficulty.toString());
    localStorage.setItem('dc-hearts-bot-names', JSON.stringify(botNames));
    localStorage.setItem('dc-hearts-rules', JSON.stringify({
      passingStyle, firstLead, breakingHearts, cancellationWinner, trickPauseMs, targetScore
    }));

    try {
      const resp = await fetch(`${API_URL}/start`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          numberOfPlayers: numPlayers,
          aiDifficulty,
          botNames: botNames.slice(0, numPlayers - 1),
          rules: { passingStyle, firstLead, breakingHearts, cancellationWinner, targetScore }
        })
      });
      if (resp.ok) setGameState(await resp.json());
    } catch (e) {
      alert("Ensure the C# backend is running.");
    }
  };

  const playSelectedCard = async () => {
    if (!selectedCard || !gameState) return;

    // Optimistically deselect
    const cardToPlay = selectedCard;
    setSelectedCard(null);

    try {
      const resp = await fetch(`${API_URL}/play`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          playerId: "P1", // Hardcoded human ID
          suit: cardToPlay.suit,
          rank: cardToPlay.rank
        })
      });
      if (resp.ok) {
        setGameState(await resp.json());
      } else {
        alert(await resp.text()); // Shows validation errors
      }
    } catch (e) {
      console.error(e);
    }
  };

  const leaveMatch = async () => {
    try {
      const resp = await fetch(`${API_URL}/reset`, { method: 'POST' });
      if (resp.ok) {
        setGameState(await resp.json());
      }
    } catch (e) {
      console.error(e);
    }
  };

  useEffect(() => {
    if (!gameState) return;

    if (gameState.phase === GamePhase.Playing) {
      const isMyTurn = gameState.players[gameState.currentTurnPlayerIndex].id === "P1";
      if (!isMyTurn) {
        const timer = setTimeout(() => {
          fetch(`${API_URL}/play-ai`, { method: 'POST' })
            .then(r => r.json())
            .then(setGameState)
            .catch(console.error);
        }, 800); // 800ms between AI plays for visual animation pacing
        return () => clearTimeout(timer);
      }
    } else if (gameState.phase === GamePhase.Passing) {
      // Trigger AI to make their pass selections
      if (gameState.players.find(p => p.id === "P1" && !gameState.pendingPasses?.hasOwnProperty("P1"))) {
        fetch(`${API_URL}/play-ai-pass`, { method: 'POST' })
          .then(r => r.json())
          .then(setGameState)
          .catch(console.error);
      }
    } else if (gameState.phase === GamePhase.TrickPending) {
      // Automatically start the next trick after the selected pause duration
      const timer = setTimeout(() => {
        fetch(`${API_URL}/resolve-trick`, { method: 'POST' })
          .then(r => r.json())
          .then(setGameState)
          .catch(console.error);
      }, trickPauseMs);
      return () => clearTimeout(timer);
    }
  }, [gameState, trickPauseMs]);

  // Detect new cards arriving after a pass
  useEffect(() => {
    if (gameState?.phase === GamePhase.Playing && previousHandRef.current.length > 0) {
      const me = gameState.players.find(p => p.id === "P1");
      if (me) {
        // Find the 3 cards in 'me.hand' that are NOT in 'previousHandRef.current'
        const arrivingCards = me.hand.filter(currentCard =>
          // We need to match on exact instance or count if there are exact duplicates (double deck)
          // For simplicity, we just diff the total counts.
          !previousHandRef.current.some((prevCard: Card) => prevCard.suit === currentCard.suit && prevCard.rank === currentCard.rank)
        );

        // Because of the double deck, a simple diff might be tricky if they had one 2C, passed it, and received another 2C.
        // A more robust way: find exactly 3 items that balance the math. 
        // We'll trust the simple diff for the visual flair for now.
        setNewlyPassedCards(arrivingCards);

        // Clear the animation flair after a few seconds
        setTimeout(() => {
          setNewlyPassedCards([]);
          previousHandRef.current = []; // Reset
        }, 3000);
      }
    }
  }, [gameState?.phase]);

  const startNextHand = async () => {
    try {
      const resp = await fetch(`${API_URL}/reset-hand`, { method: 'POST' });
      if (resp.ok) {
        setGameState(await resp.json());
        setSelectedPassCards([]);
      }
    } catch (e) {
      console.error(e);
    }
  };

  const passSelectedCards = async () => {
    if (selectedPassCards.length !== 3 || !gameState) return;

    // Snapshot hand before we send to API (so we know what's new when state updates)
    const me = gameState.players.find(p => p.id === "P1");
    if (me) {
      // We know we are losing the 3 selected cards. The rest stay.
      previousHandRef.current = me.hand.filter(c => !selectedPassCards.some(sc => sc.suit === c.suit && sc.rank === c.rank));
    }

    try {
      const resp = await fetch(`${API_URL}/pass`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          playerId: "P1", // Hardcoded human ID
          cardsToPass: selectedPassCards
        })
      });
      if (resp.ok) setGameState(await resp.json());
      else alert(await resp.text());
    } catch (e) {
      console.error(e);
    }
  };

  if (!gameState || gameState.phase === GamePhase.Lobby) {
    return (
      <div className="flex flex-col items-center justify-center w-full min-h-screen text-center p-8">
        <h1 className="text-6xl font-bold mb-4 text-transparent bg-clip-text bg-gradient-to-r from-green-300 to-green-600 drop-shadow-lg">
          Double Deck Cancellation Hearts
        </h1>

        <div className="glass-panel p-8 rounded-2xl w-full max-w-md mt-8 space-y-6">
          <div>
            <label className="block text-sm font-medium mb-2 opacity-80">Total Players</label>
            <input
              type="range" min="5" max="11"
              value={numPlayers} onChange={e => setNumPlayers(parseInt(e.target.value))}
              className="w-full accent-green-500"
            />
            <div className="text-2xl font-bold mt-2 text-green-400">{numPlayers}</div>
          </div>

          <div className="text-left space-y-2 max-h-40 overflow-y-auto pr-2 custom-scrollbar">
            <label className="block text-sm font-medium opacity-80 mb-1">Name Your Bot Opponents:</label>
            {Array.from({ length: numPlayers - 1 }).map((_, i) => (
              <input
                key={i}
                type="text"
                value={botNames[i]}
                onChange={e => {
                  const newNames = [...botNames];
                  newNames[i] = e.target.value;
                  setBotNames(newNames);
                }}
                className="w-full bg-black/30 border border-white/10 rounded px-3 py-2 text-white text-sm"
                placeholder={`Bot ${i + 1} Name`}
              />
            ))}
          </div>

          <div>
            <label className="block text-sm font-medium mb-2 opacity-80">AI Skill Level</label>
            <select
              className="w-full bg-black/30 border border-white/10 rounded pt-2 pb-2 pl-4 pr-4 text-white"
              value={aiDifficulty}
              onChange={e => setAiDifficulty(parseInt(e.target.value))}
            >
              <option value={1}>Beginner (Random)</option>
              <option value={2}>Amateur</option>
              <option value={3}>Intermediate</option>
              <option value={4}>Expert</option>
              <option value={5}>Grand Master</option>
            </select>
          </div>

          <div className="bg-black/20 p-4 rounded-xl space-y-4 text-left border border-white/5">
            <h3 className="font-bold text-green-300 text-sm tracking-wider uppercase">Rule Variations</h3>

            <div className="flex justify-between items-center">
              <label className="text-xs opacity-80">Passing Phase</label>
              <select className="bg-black/50 border border-white/10 rounded text-xs p-1" value={passingStyle} onChange={e => setPassingStyle(e.target.value)}>
                <option value="Standard">Standard (L, R, A, Hold)</option>
                <option value="None">No Passing</option>
              </select>
            </div>

            <div className="flex justify-between items-center">
              <label className="text-xs opacity-80">First Lead</label>
              <select className="bg-black/50 border border-white/10 rounded text-xs p-1" value={firstLead} onChange={e => setFirstLead(e.target.value)}>
                <option value="DealersLeft">Dealer's Left</option>
                <option value="2OfClubs">2 of Clubs</option>
              </select>
            </div>

            <div className="flex justify-between items-center">
              <label className="text-xs opacity-80">Breaking Hearts</label>
              <select className="bg-black/50 border border-white/10 rounded text-xs p-1" value={breakingHearts} onChange={e => setBreakingHearts(e.target.value)}>
                <option value="Standard">Must Break</option>
                <option value="Guts">Guts (Lead anytime)</option>
              </select>
            </div>

            <div className="flex justify-between items-center">
              <label className="text-xs opacity-80">Cancellation Win</label>
              <select className="bg-black/50 border border-white/10 rounded text-xs p-1" value={cancellationWinner} onChange={e => setCancellationWinner(e.target.value)}>
                <option value="PreviousWinner">Previous Winner</option>
                <option value="TrickLeader">Trick Leader</option>
              </select>
            </div>

            <div className="flex justify-between items-center">
              <label className="text-xs opacity-80">Match Winning Score</label>
              <select className="bg-black/50 border border-white/10 rounded text-xs p-1" value={targetScore} onChange={e => setTargetScore(parseInt(e.target.value))}>
                <option value={50}>50 Points (Fast Match)</option>
                <option value={100}>100 Points (Standard)</option>
                <option value={150}>150 Points (Long Match)</option>
                <option value={200}>200 Points (Marathon)</option>
              </select>
            </div>

            <div className="flex justify-between items-center">
              <label className="text-xs opacity-80">Trick Review Pause</label>
              <select className="bg-black/50 border border-white/10 rounded text-xs p-1" value={trickPauseMs} onChange={e => setTrickPauseMs(parseInt(e.target.value))}>
                <option value={1000}>Fast (1 second)</option>
                <option value={2500}>Normal (2.5 seconds)</option>
                <option value={5000}>Slow (5 seconds)</option>
              </select>
            </div>
          </div>

          <button
            onClick={startGame}
            className="w-full bg-gradient-to-r from-green-500 to-emerald-600 hover:from-green-400 hover:to-emerald-500 text-white font-bold py-3 px-6 rounded-xl transition-all transform hover:scale-105 shadow-xl shadow-green-900/50"
          >
            Start Game
          </button>
        </div>
      </div>
    );
  }

  // --- GAME VIEW ---
  const humanPlayer = gameState.players.find(p => p.id === "P1");
  const isMyTurn = gameState.players[gameState.currentTurnPlayerIndex].id === "P1";

  return (
    <div className="relative w-full h-screen flex flex-col justify-between p-4 overflow-hidden">

      {/* HUD (Scores & Actions) */}
      <div className="absolute top-4 left-4 flex gap-4">
        <div className="glass-panel p-4 rounded-xl text-sm flex gap-4 items-center">
          {gameState.players.map(p => (
            <div key={p.id} className={`flex flex-col items-center p-2 rounded ${p.id === "P1" ? "bg-green-900/50" : ""}`}>
              <span className="font-bold">{p.name} {p.id === gameState.players[gameState.currentTurnPlayerIndex].id ? '🎯' : ''}</span>
              <span className="text-xl font-mono text-green-300">{p.score}</span>
            </div>
          ))}
        </div>
        <button
          onClick={leaveMatch}
          className="glass-panel px-4 py-2 rounded-xl text-sm text-red-300 hover:text-red-100 hover:bg-red-900/50 transition-colors h-fit self-center"
        >
          Leave Match
        </button>
      </div>

      {/* Opponents (Top / Sides representation placeholder) */}
      <div className="flex-1 w-full flex items-center justify-center pointer-events-none">
        {/* The Table Center / Current Trick */}
        <div className="glass-panel w-96 h-96 rounded-full flex items-center justify-center relative shadow-2xl shadow-green-900/50 border-green-500/20 pointer-events-auto">
          {gameState.currentTrick.map((card, i) => {
            // Find who played this card
            const playedByIndex = (gameState.leadingPlayerIndex + i) % gameState.players.length;
            const isHuman = gameState.players[playedByIndex].id === "P1";

            // Calculate a starting coordinate for the animation based on player index
            // Assuming human is index 0 visually, and bots are distributed around
            const humanIndex = gameState.players.findIndex(p => p.id === "P1");
            const relativePos = (playedByIndex - humanIndex + gameState.players.length) % gameState.players.length;

            let startX = 0;
            let startY = 600; // default human position (bottom)

            if (!isHuman) {
              const angle = (Math.PI / gameState.players.length) * relativePos;
              const radius = 600;
              startX = -Math.cos(angle) * radius;
              startY = -Math.sin(angle) * radius - 200;
            }

            // Create a circular fan layout for trick cards around the center
            const displayAngle = (360 / gameState.players.length) * i;

            return (
              <div
                key={`${gameState.gameId}-trick-${i}`}
                className="absolute origin-center"
                style={{ transform: `rotate(${displayAngle}deg)` }}
              >
                <div
                  className="animate-slide-in-card"
                  style={{ '--startX': `${startX}px`, '--startY': `${startY}px` } as React.CSSProperties}
                >
                  <div style={{ transform: `translateY(-80px) rotate(${-displayAngle}deg)` }}>
                    <PlayingCard card={card} />
                  </div>
                </div>
              </div>
            );
          })}

          {gameState.currentTrick.length === 0 && (
            <span className="text-white/30 font-bold uppercase tracking-widest">
              {gameState.phase === GamePhase.Playing ? "Waiting for Lead" : "Trick Completed"}
            </span>
          )}
        </div>
      </div>

      {/* Human Hand */}
      <div className="h-64 flex flex-col items-center justify-end pb-8">

        {/* Play Action Bar */}
        <div className="h-16 mb-4 flex items-center justify-center gap-4">

          {gameState.phase === GamePhase.MatchOver ? (
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm">
              <div className="bg-gradient-to-br from-indigo-900 to-purple-900 border-2 border-indigo-400 p-12 rounded-3xl text-center shadow-2xl shadow-indigo-500/50 animate-bounce max-w-3xl transform scale-110">
                <h2 className="text-6xl font-black text-transparent bg-clip-text bg-gradient-to-r from-yellow-300 to-yellow-600 mb-6 drop-shadow-[0_0_15px_rgba(255,215,0,0.8)]">
                  MATCH OVER!
                </h2>
                <div className="text-2xl text-indigo-100 mb-8 font-light">
                  Target Score of <span className="font-bold text-white">{gameState.rules?.targetScore || 100}</span> Reached!
                </div>

                <div className="bg-black/40 rounded-xl p-6 mb-8 text-left max-h-64 overflow-y-auto w-full min-w-96">
                  <h3 className="text-yellow-400 font-bold uppercase tracking-widest text-sm mb-4 border-b border-white/10 pb-2">Final Standings (Lowest Wins)</h3>
                  {[...gameState.players].sort((a, b) => a.score - b.score).map((p, idx) => (
                    <div key={p.id} className={`flex justify-between items-center py-2 ${idx === 0 ? 'text-2xl font-bold text-yellow-300' : 'text-gray-300'}`}>
                      <div className="flex items-center gap-3">
                        <span className="w-6 text-center text-sm opacity-50">#{idx + 1}</span>
                        {p.name} {idx === 0 && '👑'} {p.id === "P1" && '(You)'}
                      </div>
                      <div className="font-mono">{p.score}</div>
                    </div>
                  ))}
                </div>

                <button
                  onClick={leaveMatch}
                  className="bg-yellow-500 hover:bg-yellow-400 text-black font-extrabold py-4 px-12 rounded-full text-xl transition-all hover:scale-105 hover:shadow-[0_0_30px_rgba(255,215,0,0.6)] uppercase tracking-wider mx-auto block"
                >
                  Play Again
                </button>
              </div>
            </div>
          ) : gameState.phase === GamePhase.GameOver ? (
            <button
              onClick={startNextHand}
              className="bg-indigo-500 hover:bg-indigo-400 text-white font-bold py-3 px-8 rounded-full shadow-lg shadow-indigo-500/50 animate-pulse text-lg tracking-widest uppercase border-2 border-white/20"
            >
              Start Next Hand
            </button>
          ) : gameState.phase === GamePhase.Passing ? (
            <>
              <div className={`px-6 py-2 rounded-full font-bold transition-opacity bg-purple-900 text-purple-200 shadow-lg shadow-purple-900/50`}>
                {gameState.pendingPasses?.hasOwnProperty("P1") ? "Waiting for AI swaps..." : "Select 3 cards to pass"}
              </div>
              {selectedPassCards.length === 3 && !gameState.pendingPasses?.hasOwnProperty("P1") && (
                <button
                  onClick={passSelectedCards}
                  className="bg-green-500 hover:bg-green-400 text-white font-bold py-2 px-8 rounded-full shadow-lg shadow-green-500/50 animate-bounce"
                >
                  Pass 3 Cards
                </button>
              )}
            </>
          ) : (
            <>
              {gameState.phase !== GamePhase.TrickPending && (
                <div className={`px-6 py-2 rounded-full font-bold transition-opacity ${isMyTurn ? 'bg-amber-500 text-black shadow-lg shadow-amber-500/50' : 'bg-black/50 text-white/50'}`}>
                  {isMyTurn ? "YOUR TURN" : "Waiting for AI..."}
                </div>
              )}
              {isMyTurn && selectedCard && gameState.phase === GamePhase.Playing && (
                <button
                  onClick={playSelectedCard}
                  className="bg-green-500 hover:bg-green-400 text-white font-bold py-2 px-8 rounded-full shadow-lg shadow-green-500/50 animate-bounce"
                >
                  Play Card
                </button>
              )}
            </>
          )}

        </div>

        {/* The Fanned Hand */}
        <div className="flex justify-center w-full px-12 -space-x-12 hover:space-x-1 transition-all duration-300">
          {humanPlayer?.hand.map((card, i) => {
            const isPassingMode = gameState.phase === GamePhase.Passing;
            const isSelected = isPassingMode
              ? selectedPassCards.some(c => c.suit === card.suit && c.rank === card.rank)
              : selectedCard === card;
            const isNewlyPassed = newlyPassedCards.some(c => c.suit === card.suit && c.rank === card.rank);

            return (
              <div key={i} className={isNewlyPassed ? "animate-slide-down-card" : ""}>
                <PlayingCard
                  card={card}
                  selected={isSelected}
                  onClick={(c) => {
                    if (isPassingMode) {
                      if (isSelected) {
                        setSelectedPassCards(prev => prev.filter(pc => !(pc.suit === c.suit && pc.rank === c.rank)));
                      } else if (selectedPassCards.length < 3) {
                        setSelectedPassCards(prev => [...prev, c]);
                      }
                    } else {
                      setSelectedCard(c);
                    }
                  }}
                />
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

export default App;
