import { useState, useEffect, useRef } from 'react';
import { PlayingCard } from './components/PlayingCard';
import type { Card, GameState } from './models';
import { GamePhase } from './models';
import './index.css';

// Using a generic URL that would point to the local ASP.NET Core server
const API_URL = "http://localhost:5243/api/game";

function App() {
  const [gameState, setGameState] = useState<GameState | null>(null);
  const [numPlayers, setNumPlayers] = useState(8);
  const [aiDifficulty, setAiDifficulty] = useState(3);
  const [botNames, setBotNames] = useState<string[]>(['Bot 1', 'Bot 2', 'Bot 3', 'Bot 4', 'Bot 5', 'Bot 6', 'Bot 7', 'Bot 8', 'Bot 9', 'Bot 10']);

  // Rule Variations
  const [passingStyle, setPassingStyle] = useState("Standard"); // "Standard", "None"
  const [firstLead, setFirstLead] = useState("DealersLeft"); // "2OfClubs", "DealersLeft"
  const [breakingHearts, setBreakingHearts] = useState("Standard"); // "Standard", "Guts"
  const [cancellationWinner, setCancellationWinner] = useState("PreviousWinner"); // "PreviousWinner", "TrickLeader"
  const [targetScore, setTargetScore] = useState(100); // 50, 100, 150
  const [trickPauseMs, setTrickPauseMs] = useState(2500); // Configurable trick review timer

  const [selectedCardIndex, setSelectedCardIndex] = useState<number | null>(null);
  const [selectedPassIndices, setSelectedPassIndices] = useState<number[]>([]);
  const [newlyPassedCards, setNewlyPassedCards] = useState<Card[]>([]);
  const [isShuttingDown, setIsShuttingDown] = useState(false);
  const [showQuitConfirm, setShowQuitConfirm] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  // Manual Hand Rearrangement State
  const [localHand, setLocalHand] = useState<Card[]>([]);
  const [draggedItemIndex, setDraggedItemIndex] = useState<number | null>(null);
  const [dragOverIndex, setDragOverIndex] = useState<number | null>(null);

  // Ref to track the human hand right before the pass resolves
  const previousHandRef = useRef<Card[]>([]);

  // Ref to prevent click events from firing immediately after a drag & drop operation completes
  const dragActiveRef = useRef(false);

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
    // Appended -v2 to force the new default player count (8) for existing users who previously played with 5
    const savedPlayers = localStorage.getItem('dc-hearts-players-v2');
    const savedDiff = localStorage.getItem('dc-hearts-diff-v2');
    const savedBotNames = localStorage.getItem('dc-hearts-bot-names-v2');
    const savedRules = localStorage.getItem('dc-hearts-rules-v2');

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
    localStorage.setItem('dc-hearts-players-v2', numPlayers.toString());
    localStorage.setItem('dc-hearts-diff-v2', aiDifficulty.toString());
    localStorage.setItem('dc-hearts-bot-names-v2', JSON.stringify(botNames));
    localStorage.setItem('dc-hearts-rules-v2', JSON.stringify({
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
      setErrorMsg("Failed to start game. Ensure the C# backend is running.");
    }
  };

  const playSelectedCard = async () => {
    if (selectedCardIndex === null || !gameState) return;

    const me = gameState.players.find(p => p.id === "P1");
    if (!me || selectedCardIndex < 0 || selectedCardIndex >= localHand.length) return;

    // Optimistically deselect
    const cardToPlay = localHand[selectedCardIndex];
    setSelectedCardIndex(null);

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
        setErrorMsg(await resp.text()); // Shows validation errors
      }
    } catch (e) {
      console.error(e);
      setErrorMsg("Failed to connect to the backend.");
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

    // Synchronize localHand to allow manual sorting gracefully
    const currentServerHand = gameState.players.find(p => p.id === "P1")?.hand;
    if (!currentServerHand) {
      setLocalHand([]);
    } else {
      setLocalHand(prevLocalHand => {
        if (prevLocalHand.length === 0) return currentServerHand;

        const serverHandCopy = [...currentServerHand];
        const newLocalHand: Card[] = [];

        for (const c of prevLocalHand) {
          const matchIdx = serverHandCopy.findIndex(sc => sc.suit === c.suit && sc.rank === c.rank);
          if (matchIdx !== -1) {
            newLocalHand.push(serverHandCopy[matchIdx]);
            serverHandCopy.splice(matchIdx, 1);
          }
        }
        for (const sc of serverHandCopy) {
          newLocalHand.push(sc);
        }

        const isSame = newLocalHand.length === prevLocalHand.length && newLocalHand.every((c, i) => c.suit === prevLocalHand[i].suit && c.rank === prevLocalHand[i].rank);
        return isSame ? prevLocalHand : newLocalHand;
      });
    }

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
        setSelectedPassIndices([]);
      }
    } catch (e) {
      console.error(e);
    }
  };

  const passSelectedCards = async () => {
    if (selectedPassIndices.length !== 3 || !gameState) return;

    const me = gameState.players.find(p => p.id === "P1");
    if (!me) return;

    // Resolve the indices back to Card objects using localHand mapping
    const cardsToPass = selectedPassIndices.map(index => localHand[index]);

    // Snapshot hand before we send to API (so we know what's new when state updates)
    previousHandRef.current = localHand.filter((_, idx) => !selectedPassIndices.includes(idx));

    try {
      const resp = await fetch(`${API_URL}/pass`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          playerId: "P1", // Hardcoded human ID
          cardsToPass: cardsToPass
        })
      });
      if (resp.ok) setGameState(await resp.json());
      else setErrorMsg(await resp.text());
    } catch (e) {
      console.error(e);
      setErrorMsg("Failed to connect to the backend during passing.");
    }
  };

  const quitApplication = () => {
    setShowQuitConfirm(true);
  };

  const confirmQuit = async () => {
    setShowQuitConfirm(false);
    setIsShuttingDown(true);
    try {
      await fetch(`${API_URL}/system/shutdown`, { method: 'POST' });
    } catch {
      // Fetch might fail instantly if the server shuts down fast enough, which is expected.
    }

    // Send a message to the native Photino C# wrapper to command a self-termination.
    setTimeout(() => {
      if ((window as any).external && typeof (window as any).external.sendMessage === 'function') {
        (window as any).external.sendMessage("quit");
      } else {
        window.close(); // Fallback for standard browsers if we decoupled
      }
    }, 1500);
  };

  if (isShuttingDown) {
    return (
      <div className="fixed inset-0 z-[100] flex flex-col items-center justify-center bg-black transition-opacity duration-1000">
        <h1 className="text-4xl text-white font-black mb-4 animate-pulse uppercase tracking-widest text-transparent bg-clip-text bg-gradient-to-r from-red-500 to-orange-500">Shutting Down</h1>
        <p className="text-gray-400 text-lg font-medium">Closing game engine natively...</p>
      </div>
    );
  }

  const renderErrorModal = () => {
    if (!errorMsg) return null;
    return (
      <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/60 backdrop-blur-sm animate-fade-in">
        <div className="bg-gradient-to-br from-red-900 to-gray-900 border-2 border-red-500 rounded-3xl p-8 max-w-md w-full shadow-2xl shadow-red-500/50 transform animate-pop-in">
          <h2 className="text-2xl font-black text-red-400 mb-4 uppercase tracking-widest flex items-center gap-2">
            <span>⚠️</span> Error
          </h2>
          <p className="text-gray-200 text-lg mb-8 leading-relaxed font-medium">
            {errorMsg}
          </p>
          <button
            onClick={() => setErrorMsg(null)}
            className="w-full bg-red-600 hover:bg-red-500 text-white font-bold py-3 px-6 rounded-xl transition-all transform hover:scale-105 shadow-lg shadow-red-900/50"
          >
            DISMISS
          </button>
        </div>
      </div>
    );
  };

  const renderQuitModal = () => {
    if (!showQuitConfirm) return null;
    return (
      <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/80 backdrop-blur-md animate-fade-in">
        <div className="bg-gradient-to-br from-gray-900 to-black border-2 border-red-500/50 rounded-3xl p-8 max-w-md w-full shadow-2xl shadow-red-900/50 transform animate-pop-in text-center">
          <h2 className="text-3xl font-black text-white mb-2 uppercase tracking-widest">
            Quit Game?
          </h2>
          <p className="text-gray-400 text-md mb-8">
            Are you sure you want to quit the application and completely shut down the background game engine?
          </p>
          <div className="flex gap-4">
            <button
              onClick={() => setShowQuitConfirm(false)}
              className="flex-1 bg-gray-800 hover:bg-gray-700 text-white font-bold py-3 px-6 rounded-xl transition-colors border border-gray-600"
            >
              Cancel
            </button>
            <button
              onClick={confirmQuit}
              className="flex-1 bg-red-600 hover:bg-red-500 text-white font-bold py-3 px-6 rounded-xl transition-all transform hover:scale-105 shadow-lg shadow-red-900/50"
            >
              Confirm Quit
            </button>
          </div>
        </div>
      </div>
    );
  };

  if (!gameState || gameState.phase === GamePhase.Lobby) {
    return (
      <div className="flex flex-col items-center justify-center w-full min-h-screen p-4">
        {renderErrorModal()}
        {renderQuitModal()}
        <div className="max-w-5xl mx-auto backdrop-blur-md bg-black/30 p-8 rounded-3xl border border-white/10 shadow-2xl relative w-full flex flex-col items-center">
          <button
            onClick={quitApplication}
            className="absolute top-4 right-4 bg-red-600/50 hover:bg-red-500 text-white text-xs font-bold py-1 px-3 rounded"
          >
            Quit Game
          </button>
          <h1 className="text-4xl font-black text-center mb-6 mt-2 text-transparent bg-clip-text bg-gradient-to-r from-green-400 to-emerald-600 drop-shadow-sm">
            Double Deck Cancellation Hearts
          </h1>

          <div className="w-full flex-1">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-8 w-full max-w-4xl mx-auto items-stretch">

              {/* Left Column: Players & Bots */}
              <div className="glass-panel p-6 rounded-2xl space-y-6 flex flex-col text-center">
                <div>
                  <label className="block text-sm font-medium mb-2 opacity-80">Total Players</label>
                  <input
                    type="range" min="5" max="11"
                    value={numPlayers} onChange={e => setNumPlayers(parseInt(e.target.value))}
                    className="w-full accent-green-500"
                  />
                  <div className="text-3xl font-bold mt-2 text-green-400">{numPlayers}</div>
                </div>

                <div className="text-left space-y-2 flex-1 overflow-y-auto max-h-48 pr-2 custom-scrollbar">
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

                <div className="text-left mt-auto pt-4 border-t border-white/5">
                  <label className="block text-sm font-medium mb-2 opacity-80">AI Skill Level</label>
                  <select
                    className="w-full bg-black/40 border border-white/10 rounded pt-2 pb-2 pl-4 pr-4 text-white"
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
              </div>

              {/* Right Column: Rule Variations */}
              <div className="glass-panel p-6 rounded-2xl flex flex-col justify-center bg-black/20 border border-white/5">
                <h3 className="font-bold text-green-300 text-sm tracking-wider uppercase mb-4 text-center">Rule Variations</h3>

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
            </div>
          </div>

          <button
            onClick={startGame}
            className="w-full max-w-2xl mx-auto block bg-gradient-to-r from-green-500 to-emerald-600 hover:from-green-400 hover:to-emerald-500 text-white font-black text-xl py-4 px-8 rounded-full transition-all transform hover:scale-105 shadow-[0_0_20px_rgba(52,211,153,0.4)] hover:shadow-[0_0_30px_rgba(52,211,153,0.6)] mt-8"
          >
            START GAME
          </button>
        </div>
      </div>
    );
  }

  // --- GAME VIEW ---
  const isMyTurn = gameState.players[gameState.currentTurnPlayerIndex].id === "P1";

  return (
    <div className="relative w-full h-screen flex flex-col items-center justify-between p-4 overflow-hidden">
      {renderErrorModal()}
      {renderQuitModal()}

      <button
        onClick={quitApplication}
        className="absolute top-4 right-4 z-50 bg-red-600/30 hover:bg-red-500 text-white text-xs font-bold py-1 px-3 rounded backdrop-blur transition-colors duration-200 border border-red-500/30"
      >
        Quit App
      </button>

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
      <div className="min-h-64 flex flex-col items-center justify-end pb-4 mt-auto z-10 w-full max-w-7xl">

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

                {gameState.shooterOfMoonId && (
                  <div className="mt-8 text-center p-4 bg-yellow-900 border-2 border-yellow-400 rounded-xl animate-pulse">
                    <h4 className="text-xl font-black text-yellow-300">🌕 MOON SHOT! 🌕</h4>
                    <p className="text-white text-sm mt-2">{gameState.players.find(p => p.id === gameState.shooterOfMoonId)?.name} collected all 26 Hearts and all 4 Queens!</p>
                  </div>
                )}

                <div className="flex justify-center gap-4">
                  <button
                    className="bg-purple-600 hover:bg-purple-500 text-white font-bold py-3 px-8 rounded-full shadow-[0_0_15px_rgba(147,51,234,0.5)] transition-all hover:scale-105"
                    onClick={leaveMatch}
                  >
                    Return to Lobby
                  </button>
                  {!gameState.players.some(p => p.id === "P1") && (
                    <button
                      className="bg-gray-600 hover:bg-gray-500 text-white font-bold py-3 px-8 rounded-full transition-all"
                      onClick={quitApplication}
                    >
                      Quit
                    </button>
                  )}
                </div>
              </div>
            </div>
          ) : gameState.phase === GamePhase.GameOver ? (
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm">
              <div className="bg-gradient-to-br from-indigo-900 to-purple-900 border-2 border-indigo-400 p-12 rounded-3xl text-center shadow-2xl shadow-indigo-500/50 transform scale-110">

                {gameState.shooterOfMoonId ? (
                  <div className="mb-8">
                    <h2 className="text-7xl font-black text-transparent bg-clip-text bg-gradient-to-b from-yellow-200 to-yellow-600 drop-shadow-[0_0_20px_rgba(255,215,0,1)] animate-bounce tracking-widest uppercase">
                      🌕 THE MOON HAS BEEN SHOT! 🌕
                    </h2>
                    <div className="text-3xl text-yellow-100 mt-6 font-bold uppercase tracking-widest bg-black/30 py-4 px-8 rounded-full inline-block">
                      <span className="text-white font-black">{gameState.players.find(p => p.id === gameState.shooterOfMoonId)?.name}</span> has collected all 52 Penalty Points!
                    </div>
                  </div>
                ) : (
                  <h2 className="text-5xl font-black text-white mb-6 tracking-widest uppercase">
                    Hand Over!
                  </h2>
                )}

                <div className="bg-black/40 rounded-xl p-6 mb-8 text-left max-h-64 overflow-y-auto w-full min-w-96">
                  <h3 className="text-indigo-300 font-bold uppercase tracking-widest text-sm mb-4 border-b border-indigo-500/50 pb-2">Current Standings</h3>
                  {[...gameState.players].sort((a, b) => a.score - b.score).map((p, idx) => (
                    <div key={p.id} className="flex justify-between items-center py-2 text-gray-300 border-b border-white/5 last:border-0">
                      <div className="flex items-center gap-3">
                        <span className="w-6 text-center text-sm opacity-50">#{idx + 1}</span>
                        <span className="font-medium">{p.name} {p.id === "P1" ? "(You)" : ""}</span>
                      </div>
                      <span className="font-mono bg-black/50 px-3 py-1 rounded">{p.score}</span>
                    </div>
                  ))}
                </div>

                <div className="flex justify-center flex-col gap-4">
                  <button
                    className="bg-green-600 hover:bg-green-500 text-white font-bold py-4 px-12 rounded-full shadow-[0_0_15px_rgba(34,197,94,0.5)] transition-all hover:scale-105 text-xl tracking-wider"
                    onClick={startNextHand}
                  >
                    CONTINUE TO NEXT HAND
                  </button>
                </div>
              </div>
            </div>
          ) : gameState.phase === GamePhase.Passing ? (
            <>
              <div className={`px-6 py-2 rounded-full font-bold transition-opacity bg-purple-900 text-purple-200 shadow-lg shadow-purple-900/50`}>
                {gameState.pendingPasses?.hasOwnProperty("P1") ? "Waiting for AI swaps..." : "Select 3 cards to pass"}
              </div>
              {selectedPassIndices.length === 3 && !gameState.pendingPasses?.hasOwnProperty("P1") && (
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
              {isMyTurn && selectedCardIndex !== null && gameState.phase === GamePhase.Playing && (
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
        <div className="flex flex-wrap justify-center items-end w-full px-4 gap-y-4 -space-x-8 sm:-space-x-10 hover:space-x-1 transition-all duration-300 pb-4">
          {localHand.map((card, i) => {
            const isPassingMode = gameState.phase === GamePhase.Passing;
            const isSelected = isPassingMode
              ? selectedPassIndices.includes(i)
              : selectedCardIndex === i;

            // Still comparing newly passed cards by identity since that's all the API returns during Trick update. It's fine for simple FX.
            const isNewlyPassed = newlyPassedCards.some(c => c.suit === card.suit && c.rank === card.rank);

            const isBeingDragged = draggedItemIndex === i;
            const isDragOver = dragOverIndex === i;

            return (
              <div
                key={i}
                className={`${isNewlyPassed ? "animate-slide-down-card" : ""} ${isBeingDragged ? 'opacity-30 scale-95' : 'opacity-100'} ${isDragOver ? 'border-l-4 border-yellow-400 pl-2 -ml-2 translate-x-1' : ''} cursor-grab active:cursor-grabbing transition-all duration-200`}
                draggable={true}
                onDragStart={(e) => {
                  setDraggedItemIndex(i);
                  dragActiveRef.current = true;
                  e.dataTransfer.effectAllowed = 'move';
                }}
                onDragOver={(e) => {
                  e.preventDefault();
                  e.dataTransfer.dropEffect = 'move';
                  if (dragOverIndex !== i) setDragOverIndex(i);
                }}
                onDragLeave={() => {
                  if (dragOverIndex === i) setDragOverIndex(null);
                }}
                onDrop={(e) => {
                  e.preventDefault();
                  if (draggedItemIndex === null) return;

                  if (draggedItemIndex === i) {
                    // Sloppy click detected (user dragged and dropped on the same spot)
                    // Treat as a legitimate click interaction since drag hijacked it.
                    if (isPassingMode) {
                      if (isSelected) {
                        setSelectedPassIndices(prev => prev.filter(idx => idx !== i));
                      } else if (selectedPassIndices.length < 3) {
                        setSelectedPassIndices(prev => [...prev, i]);
                      }
                    } else {
                      setSelectedCardIndex(i);
                    }
                    setDraggedItemIndex(null);
                    setDragOverIndex(null);
                    return;
                  }

                  // Reorder
                  setLocalHand(prev => {
                    const updated = [...prev];
                    const [movedCard] = updated.splice(draggedItemIndex, 1);
                    updated.splice(i, 0, movedCard);
                    return updated;
                  });

                  // Safely remap selected indices if we just shuffled the array
                  if (selectedCardIndex === draggedItemIndex) {
                    setSelectedCardIndex(i);
                  } else if (selectedCardIndex !== null) {
                    if (draggedItemIndex < selectedCardIndex && i >= selectedCardIndex) {
                      setSelectedCardIndex(selectedCardIndex - 1);
                    } else if (draggedItemIndex > selectedCardIndex && i <= selectedCardIndex) {
                      setSelectedCardIndex(selectedCardIndex + 1);
                    }
                  }

                  if (isPassingMode) {
                    setSelectedPassIndices(prev => prev.map(idx => {
                      if (idx === draggedItemIndex) return i;
                      if (draggedItemIndex < idx && i >= idx) return idx - 1;
                      if (draggedItemIndex > idx && i <= idx) return idx + 1;
                      return idx;
                    }));
                  }

                  setDraggedItemIndex(null);
                  setDragOverIndex(null);
                }}
                onDragEnd={() => {
                  setDraggedItemIndex(null);
                  setDragOverIndex(null);
                  // Add a tiny delay before re-enabling clicks so the trailing mouseUp doesn't trigger the selection toggle
                  setTimeout(() => { dragActiveRef.current = false; }, 50);
                }}
              >
                <PlayingCard
                  card={card}
                  selected={isSelected}
                  onClick={() => {
                    if (dragActiveRef.current) return; // Ignore click if we were just dragging

                    if (isPassingMode) {
                      if (isSelected) {
                        setSelectedPassIndices(prev => prev.filter(idx => idx !== i));
                      } else if (selectedPassIndices.length < 3) {
                        setSelectedPassIndices(prev => [...prev, i]);
                      }
                    } else {
                      setSelectedCardIndex(i);
                    }
                  }}
                />
              </div>
            );
          })}
        </div>
      </div>
    </div >
  );
}

export default App;
