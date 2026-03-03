export const Suit = {
    Clubs: 0,
    Diamonds: 1,
    Spades: 2,
    Hearts: 3
} as const;

export type Suit = typeof Suit[keyof typeof Suit];

export const Rank = {
    Two: 2,
    Three: 3,
    Four: 4,
    Five: 5,
    Six: 6,
    Seven: 7,
    Eight: 8,
    Nine: 9,
    Ten: 10,
    Jack: 11,
    Queen: 12,
    King: 13,
    Ace: 14
} as const;

export type Rank = typeof Rank[keyof typeof Rank];

export interface Card {
    suit: Suit;
    rank: Rank;
    isHeart: boolean;
    isQueenOfSpades: boolean;
    pointValue: number;
}

export interface Player {
    id: string;
    name: string;
    isAi: boolean;
    difficultyLevel: number;
    hand: Card[];
    capturedCards: Card[];
    score: number;
}

export const GamePhase = {
    Lobby: 0,
    Passing: 1,
    TrickPending: 2,
    Playing: 3,
    GameOver: 4,
    MatchOver: 5
} as const;

export type GamePhase = typeof GamePhase[keyof typeof GamePhase];

export interface GameState {
    gameId: string;
    players: Player[];
    currentTurnPlayerIndex: number;
    leadingPlayerIndex: number;
    currentTrick: Card[];
    cancelledKitty: Card[];
    setupKitty: Card[];
    heartsBroken: boolean;
    isFirstTrickOfHand: boolean;
    phase: GamePhase;
    rules?: { targetScore: number };
    roundNumber: number;
    pendingPasses?: Record<string, Card[]>;
    shooterOfMoonId?: string | null;
    lastMoveReasoning?: Record<string, string>;
}
