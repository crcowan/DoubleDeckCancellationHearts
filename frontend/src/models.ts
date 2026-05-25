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
    handScore: number;
    matchTricksWon: number;
}

export const GamePhase = {
    Lobby: 0,
    DownloadingModel: 1,
    Passing: 2,
    TrickPending: 3,
    Playing: 4,
    GameOver: 5,
    MatchOver: 6
} as const;

export type GamePhase = typeof GamePhase[keyof typeof GamePhase];



export interface TrickSummary {
    trick: Card[];
    leadingPlayerIndex: number;
    winningPlayerIndex: number;
    isCancelled: boolean;
    trickPoints: number;
}

export interface GameState {
    gameId: string;
    players: Player[];
    currentTurnPlayerIndex: number;
    leadingPlayerIndex: number;
    dealerPlayerIndex: number;
    currentTrick: Card[];
    cancelledKitty: Card[];
    setupKitty: Card[];
    kittyTakenByName?: string | null;
    heartsBroken: boolean;
    isFirstTrickOfHand: boolean;
    matchTricksPlayed: number;
    phase: GamePhase;
    previousTrick?: TrickSummary;
    rules?: { targetScore: number };
    roundNumber: number;
    pendingPasses?: Record<string, Card[]>;
    shooterOfMoonId?: string | null;
    lastMoveReasoning?: Record<string, string>;
    showAiReasoning: boolean;
    llmDownloadProgress?: number;
    llmDownloadStatus?: string;
}
