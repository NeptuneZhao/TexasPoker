using System.Text;

namespace TClient.Model;

public class Card
{
	public Suit Suit { get; init; }
	public Rank Rank { get; init; }

	public override string ToString()
	{
		var sb = new StringBuilder();
		var suitSymbol = SuitSymbols.First(s => s.Item1 == Suit).Item2;
		sb.Append(suitSymbol);
		sb.Append(Rank switch
		{
			Rank.Two   => "2", Rank.Three => "3", Rank.Four  => "4",
			Rank.Five  => "5", Rank.Six   => "6", Rank.Seven => "7",
			Rank.Eight => "8", Rank.Nine  => "9", Rank.Ten   => "10",
			Rank.Jack  => "J", Rank.Queen => "Q", Rank.King  => "K",
			Rank.Ace   => "A",
			_ => "?"
		});
		return sb.ToString();
	}

	public ConsoleColor GetColor() => Suit is Suit.Hearts or Suit.Diamonds 
		? ConsoleColor.Red 
		: ConsoleColor.White;

	private static readonly (Suit, string)[] SuitSymbols = [
		(Suit.Clubs,    "♣"), 
		(Suit.Diamonds, "♦"),
		(Suit.Hearts,   "♥"),
		(Suit.Spades,   "♠")
	];
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
	Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Seven = 7, Eight = 8, Nine = 9, Ten = 10,
	Jack = 11, Queen = 12, King = 13, Ace = 14
}
