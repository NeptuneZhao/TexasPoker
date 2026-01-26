using TServer.Model;

namespace TServer.Game;

public class Deck
{
	private readonly Stack<Card> _cards;

	public Deck()
	{
		var cards = (
			from Suit suit in Enum.GetValues<Suit>() 
			from Rank rank in Enum.GetValues<Rank>() 
			select new Card(suit, rank))
			.ToList();

		// Fisher-Yates shuffle
		var rng = new Random();
		for (var i = cards.Count - 1; i > 0; i--)
		{
			var k = rng.Next(i + 1);
			(cards[k], cards[i]) = (cards[i], cards[k]);
		}
		_cards = new Stack<Card>(cards);
	}

	/// <summary>
	/// 发牌
	/// </summary>
	/// <returns></returns>
	/// <exception cref="InvalidOperationException"></exception>
	public Card Deal()
	{
		return _cards.Count == 0 ? throw new InvalidOperationException("Deck is empty") : _cards.Pop();
	}
	
	// ReSharper disable once UnusedMember.Global
	public int Remaining => _cards.Count;
}
