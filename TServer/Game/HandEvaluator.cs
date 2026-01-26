using TServer.Model;

namespace TServer.Game;

public abstract class HandEvaluator
{
	/// <summary>
	/// 亮牌评估
	/// </summary>
	/// <param name="holeCards">手牌</param>
	/// <param name="communityCards">公开牌</param>
	/// <returns></returns>
	public static (HandRank Rank, List<Card> BestFive) Evaluate(List<Card> holeCards, List<Card> communityCards)
	{
		var allCards = new List<Card>(holeCards);
		allCards.AddRange(communityCards);

		if (allCards.Count < 5) return (HandRank.HighCard, allCards.OrderByDescending(c => c.Rank).ToList());

		var combinations = GetCombinations(allCards, 5);
		
		var bestRank = HandRank.HighCard;
		List<Card> bestHand = [];

		foreach (var hand in combinations)
		{
			var (rank, sortedHand) = EvaluateFive(hand);
			if (rank > bestRank)
			{
				bestRank = rank;
				bestHand = sortedHand;
			}
			else if (rank == bestRank)
			{
				if (bestHand.Count == 0 || CompareSameRank(sortedHand, bestHand) > 0)
					bestHand = sortedHand;
			}
		}

		return (bestRank, bestHand);
	}

	private static (HandRank, List<Card>) EvaluateFive(List<Card> cards)
	{
		var sorted = cards.OrderByDescending(c => c.Rank).ToList();
		var flush = sorted.All(c => c.Suit == sorted[0].Suit);
		var straight = IsStraight(sorted);

		if (flush && straight) return sorted[0].Rank == Rank.Ace ? (HandRank.RoyalFlush, sorted) : (HandRank.StraightFlush, sorted);

		var groups = sorted.GroupBy(c => c.Rank).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToList();

		switch (groups[0].Count())
		{
			case 4:
				return (HandRank.FourOfAKind, groups.SelectMany(g => g).ToList());
			case 3 when groups[1].Count() == 2:
				return (HandRank.FullHouse, groups.SelectMany(g => g).ToList());
		}

		if (flush) return (HandRank.Flush, sorted);
		if (straight) return (HandRank.Straight, sorted);

		return groups[0].Count() switch
		{
			3 => (HandRank.ThreeOfAKind, groups.SelectMany(g => g).ToList()),
			2 when groups[1].Count() == 2 => (HandRank.TwoPair, groups.SelectMany(g => g).ToList()),
			_ => groups[0].Count() == 2
				? (HandRank.OnePair, groups.SelectMany(g => g).ToList())
				: (HandRank.HighCard, sorted)
		};
	}

	private static bool IsStraight(List<Card> sorted)
	{
		// Ace low straight (5, 4, 3, 2, A)
		if (sorted[0].Rank == Rank.Ace && sorted[1].Rank == Rank.Five && sorted[4].Rank == Rank.Two)
		{
			// Move Ace to end for proper displaying/comparison if needed, but for rank check it is enough
			// Actually for comparison logic, we usually treat 5-high straight lower.
			// Let's keep it simple: if 5-4-3-2-A exists, it is a straight.
			// We should verify 5,4,3,2 are present.
			return sorted[1].Rank == Rank.Five && sorted[2].Rank == Rank.Four && sorted[3].Rank == Rank.Three && sorted[4].Rank == Rank.Two;
		}

		for (var i = 0; i < 4; i++)
		{
			if (sorted[i].Rank - sorted[i + 1].Rank != 1) return false;
		}
		return true;
	}

	private static int CompareSameRank(List<Card> a, List<Card> b)
	{
		for (var i = 0; i < a.Count; i++)
		{
			if (a[i].Rank > b[i].Rank) return 1;
			if (a[i].Rank < b[i].Rank) return -1;
		}
		return 0;
	}

	private static IEnumerable<List<Card>> GetCombinations(List<Card> list, int length)
	{
		if (length == 1) return list.Select(t => new List<Card> { t });
		return GetCombinations(list, length - 1)
			.SelectMany(t => list.Where(e => t.All(x => x != e) && list.IndexOf(e) > list.IndexOf(t.Last())), // Establish order to avoid duplicates
				(t1, t2) => t1.Concat(new List<Card> { t2 }).ToList());
	}
}

public enum HandRank
{
	HighCard = 1,  // 高牌
	OnePair,       // 对子
	TwoPair,       // 两对
	ThreeOfAKind,  // 三条
	Straight,      // 顺子
	Flush,         // 同花
	FullHouse,     // 葫芦
	FourOfAKind,   // 四条
	StraightFlush, // 同花顺
	RoyalFlush     // 皇家同花顺
}