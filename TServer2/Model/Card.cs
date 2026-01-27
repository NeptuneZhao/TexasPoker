namespace TServer2.Model;

/// <summary>
/// 扑克牌花色
/// </summary>
public enum Suit
{
    Clubs = 0,    // 梅花 ♣
    Diamonds = 1, // 方块 ♦
    Hearts = 2,   // 红心 ♥
    Spades = 3    // 黑桃 ♠
}

/// <summary>
/// 扑克牌点数
/// </summary>
public enum Rank
{
    Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Seven = 7, Eight = 8, Nine = 9, Ten = 10,
    Jack = 11, Queen = 12, King = 13, Ace = 14
}

/// <summary>
/// 扑克牌
/// </summary>
public class Card(Suit suit, Rank rank)
{
    public Suit Suit { get; } = suit;
    public Rank Rank { get; } = rank;

    private static readonly Dictionary<Suit, string> SuitSymbols = new()
    {
        { Suit.Clubs, "♣" },
        { Suit.Diamonds, "♦" },
        { Suit.Hearts, "♥" },
        { Suit.Spades, "♠" }
    };

    public override string ToString()
    {
        var suitSymbol = SuitSymbols[Suit];
        var rankStr = Rank switch
        {
            Rank.Two => "2", Rank.Three => "3", Rank.Four => "4",
            Rank.Five => "5", Rank.Six => "6", Rank.Seven => "7",
            Rank.Eight => "8", Rank.Nine => "9", Rank.Ten => "10",
            Rank.Jack => "J", Rank.Queen => "Q", Rank.King => "K",
            Rank.Ace => "A",
            _ => "?"
        };
        return $"{suitSymbol}{rankStr}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is Card other)
            return Suit == other.Suit && Rank == other.Rank;
        return false;
    }

    public override int GetHashCode() => HashCode.Combine(Suit, Rank);
}

/// <summary>
/// 用于JSON传输的卡片DTO
/// </summary>
public class CardDto(Card card)
{
    private string Suit { get; } = card.Suit.ToString();
    private int Rank { get; } = (int)card.Rank;

    public Card ToCard()
    {
        return new Card(Enum.Parse<Suit>(Suit), (Rank)Rank);
    }
}
