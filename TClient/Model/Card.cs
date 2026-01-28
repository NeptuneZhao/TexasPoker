namespace TClient.Model;

/// <summary>
/// 扑克牌
/// </summary>
public class Card
{
    public Suit Suit { get; init; }
    public Rank Rank { get; init; }

    /// <summary>
    /// 获取牌面显示字符串（如 ♠A, ♥K）
    /// </summary>
    public string Display => $"{SuitSymbol}{RankSymbol}";

    /// <summary>
    /// 花色符号
    /// </summary>
    private string SuitSymbol => Suit switch
    {
        Suit.Clubs => "♣",
        Suit.Diamonds => "♦",
        Suit.Hearts => "♥",
        Suit.Spades => "♠",
        _ => "?"
    };

    /// <summary>
    /// 点数符号
    /// </summary>
    private string RankSymbol => Rank switch
    {
        Rank.Two => "2",
        Rank.Three => "3",
        Rank.Four => "4",
        Rank.Five => "5",
        Rank.Six => "6",
        Rank.Seven => "7",
        Rank.Eight => "8",
        Rank.Nine => "9",
        Rank.Ten => "10",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        Rank.Ace => "A",
        _ => "?"
    };

    /// <summary>
    /// 是否为红色花色
    /// </summary>
    public bool IsRed => Suit is Suit.Hearts or Suit.Diamonds;

    public override string ToString() => Display;
}

public enum Suit
{
    Clubs = 0,
    Diamonds = 1,
    Hearts = 2,
    Spades = 3
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
