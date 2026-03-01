using System;

namespace GameEngine.Api.Models
{
    public enum Suit
    {
        Clubs,
        Diamonds,
        Spades,
        Hearts
    }

    public enum Rank
    {
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13,
        Ace = 14
    }

    public record Card(Suit Suit, Rank Rank) : IComparable<Card>
    {
        public bool IsHeart => Suit == Suit.Hearts;
        public bool IsQueenOfSpades => Suit == Suit.Spades && Rank == Rank.Queen;

        public int PointValue
        {
            get
            {
                if (IsQueenOfSpades) return 13;
                if (IsHeart) return 1;
                return 0;
            }
        }

        public int CompareTo(Card? other)
        {
            if (other == null) return 1;
            
            // In Hearts, we only care about comparing ranks within the same suit during a trick
            if (Suit != other.Suit)
            {
                // This shouldn't normally be used directly to determine trick winners, 
                // but needed for basic IComparable
                return Suit.CompareTo(other.Suit);
            }
            return Rank.CompareTo(other.Rank);
        }

        public override string ToString() => $"{Rank} of {Suit}";
    }
}
