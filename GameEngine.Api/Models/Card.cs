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

        public string ToShortString()
        {
            string r = Rank switch
            {
                Rank.Two => "2", Rank.Three => "3", Rank.Four => "4", Rank.Five => "5",
                Rank.Six => "6", Rank.Seven => "7", Rank.Eight => "8", Rank.Nine => "9",
                Rank.Ten => "10", Rank.Jack => "J", Rank.Queen => "Q", Rank.King => "K", Rank.Ace => "A",
                _ => "?"
            };
            string s = Suit switch
            {
                Suit.Clubs => "C", Suit.Diamonds => "D", Suit.Spades => "S", Suit.Hearts => "H",
                _ => "?"
            };
            return r + s;
        }

        public static Card? TryParseShortId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length < 2) return null;
            id = id.Trim().ToUpperInvariant();

            string rankPart = id.Length == 3 ? id[..2] : id[..1]; // "10C" vs "KC"
            string suitPart = id[^1..]; // last char

            Rank? rank = rankPart switch
            {
                "2" => Rank.Two, "3" => Rank.Three, "4" => Rank.Four, "5" => Rank.Five,
                "6" => Rank.Six, "7" => Rank.Seven, "8" => Rank.Eight, "9" => Rank.Nine,
                "10" => Rank.Ten, "J" => Rank.Jack, "Q" => Rank.Queen, "K" => Rank.King, "A" => Rank.Ace,
                _ => null
            };
            Suit? suit = suitPart switch
            {
                "C" => Suit.Clubs, "D" => Suit.Diamonds, "S" => Suit.Spades, "H" => Suit.Hearts,
                _ => null
            };

            if (rank == null || suit == null) return null;
            return new Card(suit.Value, rank.Value);
        }
    }
}
