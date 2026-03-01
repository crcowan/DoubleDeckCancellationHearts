import React from 'react';
import type { Card as CardModel } from '../models';
import { Suit, Rank } from '../models';

interface Props {
    card: CardModel;
    onClick?: (card: CardModel) => void;
    selected?: boolean;
    disabled?: boolean;
    isCancelled?: boolean;
    style?: React.CSSProperties;
}

const suitSymbols = {
    [Suit.Clubs]: '♣',
    [Suit.Diamonds]: '♦',
    [Suit.Spades]: '♠',
    [Suit.Hearts]: '♥'
};

const suitColors = {
    [Suit.Clubs]: 'text-gray-900',
    [Suit.Diamonds]: 'text-red-600',
    [Suit.Spades]: 'text-gray-900',
    [Suit.Hearts]: 'text-red-600'
};

const rankStrings: Record<Rank, string> = {
    [Rank.Two]: '2', [Rank.Three]: '3', [Rank.Four]: '4', [Rank.Five]: '5',
    [Rank.Six]: '6', [Rank.Seven]: '7', [Rank.Eight]: '8', [Rank.Nine]: '9',
    [Rank.Ten]: '10', [Rank.Jack]: 'J', [Rank.Queen]: 'Q', [Rank.King]: 'K', [Rank.Ace]: 'A'
};

export const PlayingCard: React.FC<Props> = ({ card, onClick, selected, disabled, isCancelled, style }) => {
    const symbol = suitSymbols[card.suit];
    const colorClass = suitColors[card.suit];
    const rankStr = rankStrings[card.rank];

    return (
        <div
            className={`
        relative w-24 h-36 rounded-xl bg-white shadow-xl border-2 cursor-pointer
        flex flex-col justify-between p-2 playing-card select-none
        ${colorClass}
        ${selected ? 'border-amber-400 -translate-y-4 shadow-amber-400/50' : 'border-gray-200'}
        ${disabled ? 'opacity-50 cursor-not-allowed grayscale' : ''}
        ${isCancelled ? 'animate-cancel' : ''}
      `}
            style={style}
            onClick={() => !disabled && onClick && onClick(card)}
        >
            {/* Top Left */}
            <div className="flex flex-col items-center leading-none w-6">
                <span className="text-xl font-bold">{rankStr}</span>
                <span className="text-2xl">{symbol}</span>
            </div>

            {/* Center Big Symbol */}
            <div className="absolute inset-0 flex items-center justify-center opacity-20">
                <span className="text-6xl">{symbol}</span>
            </div>

            {/* Bottom Right (Flipped) */}
            <div className="flex flex-col items-center leading-none w-6 self-end rotate-180">
                <span className="text-xl font-bold">{rankStr}</span>
                <span className="text-2xl">{symbol}</span>
            </div>
        </div>
    );
};
