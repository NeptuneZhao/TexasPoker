using TServer.Model;

namespace TServer.Game;

/// <summary>
/// 手牌评估器 - 评估玩家的最佳5张牌组合
/// </summary>
public static class HandEvaluator
{
    /// <summary>
    /// 评估玩家手牌
    /// </summary>
    /// <param name="playerId"></param>
    /// <param name="holeCards">玩家的两张底牌</param>
    /// <param name="communityCards">公共牌（3-5张）</param>
    /// <returns>评估结果</returns>
    public static HandEvaluation Evaluate(string playerId, List<Card> holeCards, List<Card> communityCards)
    {
        var allCards = new List<Card>(holeCards);
        allCards.AddRange(communityCards);

        if (allCards.Count < 5)
        {
            // 不足5张牌时返回高牌
            var sorted = allCards.OrderByDescending(c => c.Rank).ToList();
            return new HandEvaluation(playerId, HandRank.HighCard, sorted, 
                sorted.Select(c => (int)c.Rank).ToList());
        }

        // 获取所有5张牌的组合
        var combinations = GetCombinations(allCards, 5);
        
        var bestRank = HandRank.HighCard;
        List<Card> bestFive = [];
        List<int> bestKickers = [];

        foreach (var hand in combinations)
        {
            var (rank, sortedHand, kickers) = EvaluateFive(hand);
            
            if (rank > bestRank)
            {
                bestRank = rank;
                bestFive = sortedHand;
                bestKickers = kickers;
            }
            else if (rank == bestRank)
            {
                // 同等牌型比较kicker
                if (CompareKickers(kickers, bestKickers) <= 0) continue;
                bestFive = sortedHand;
                bestKickers = kickers;
            }
        }

        return new HandEvaluation(playerId, bestRank, bestFive, bestKickers);
    }

    /// <summary>
    /// 评估5张牌的牌型
    /// </summary>
    private static (HandRank Rank, List<Card> SortedHand, List<int> Kickers) EvaluateFive(List<Card> cards)
    {
        var sorted = cards.OrderByDescending(c => c.Rank).ToList();
        var isFlush = sorted.All(c => c.Suit == sorted[0].Suit);
        var isStraight = IsStraight(sorted, out var isWheel);
        
        // 按点数分组
        var groups = sorted.GroupBy(c => c.Rank)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .ToList();

        var counts = groups.Select(g => g.Count()).ToList();

        switch (isFlush)
        {
            // 皇家同花顺
            case true when isStraight && sorted[0].Rank == Rank.Ace && !isWheel:
                return (HandRank.RoyalFlush, sorted, [(int)Rank.Ace]);
            // 同花顺
            case true when isStraight:
            {
                var highCard = isWheel ? (int)Rank.Five : (int)sorted[0].Rank;
                return (HandRank.StraightFlush, sorted, [highCard]);
            }
        }

        switch (counts[0])
        {
            // 四条
            case 4:
            {
                var kickers = new List<int> { (int)groups[0].Key, (int)groups[1].Key };
                return (HandRank.FourOfAKind, groups.SelectMany(g => g).ToList(), kickers);
            }
            // 葫芦
            case 3 when counts[1] == 2:
            {
                var kickers = new List<int> { (int)groups[0].Key, (int)groups[1].Key };
                return (HandRank.FullHouse, groups.SelectMany(g => g).ToList(), kickers);
            }
        }

        // 同花
        if (isFlush)
        {
            var kickers = sorted.Select(c => (int)c.Rank).ToList();
            return (HandRank.Flush, sorted, kickers);
        }

        // 顺子
        if (isStraight)
        {
            var highCard = isWheel ? (int)Rank.Five : (int)sorted[0].Rank;
            return (HandRank.Straight, sorted, [highCard]);
        }

        switch (counts[0])
        {
            // 三条
            case 3:
            {
                var kickers = new List<int> { (int)groups[0].Key };
                kickers.AddRange(groups.Skip(1).Select(g => (int)g.Key));
                return (HandRank.ThreeOfAKind, groups.SelectMany(g => g).ToList(), kickers);
            }
            // 两对
            case 2 when counts[1] == 2:
            {
                var kickers = new List<int> { (int)groups[0].Key, (int)groups[1].Key, (int)groups[2].Key };
                return (HandRank.TwoPair, groups.SelectMany(g => g).ToList(), kickers);
            }
        }

        // 一对
        if (counts[0] == 2)
        {
            var kickers = new List<int> { (int)groups[0].Key };
            kickers.AddRange(groups.Skip(1).Select(g => (int)g.Key));
            return (HandRank.OnePair, groups.SelectMany(g => g).ToList(), kickers);
        }

        // 高牌
        var highCardKickers = sorted.Select(c => (int)c.Rank).ToList();
        return (HandRank.HighCard, sorted, highCardKickers);
    }

    /// <summary>
    /// 检查是否为顺子
    /// </summary>
    private static bool IsStraight(List<Card> sorted, out bool isWheel)
    {
        isWheel = false;
        
        // 检查 A-2-3-4-5 (Wheel/Bicycle)
        if (sorted[0].Rank == Rank.Ace && 
            sorted[1].Rank == Rank.Five && 
            sorted[2].Rank == Rank.Four && 
            sorted[3].Rank == Rank.Three && 
            sorted[4].Rank == Rank.Two)
        {
            isWheel = true;
            return true;
        }

        // 普通顺子
        for (var i = 0; i < 4; i++)
        {
            if (sorted[i].Rank - sorted[i + 1].Rank != 1)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 比较两组kicker
    /// </summary>
    public static int CompareKickers(List<int> a, List<int> b)
    {
        var minLen = Math.Min(a.Count, b.Count);
        for (var i = 0; i < minLen; i++)
        {
            if (a[i] > b[i]) return 1;
            if (a[i] < b[i]) return -1;
        }
        return 0;
    }

    /// <summary>
    /// 比较两个手牌评估结果
    /// </summary>
    /// <returns>正数表示a更大，负数表示b更大，0表示相等</returns>
    public static int Compare(HandEvaluation a, HandEvaluation b)
    {
        if (a.Rank > b.Rank) return 1;
        if (a.Rank < b.Rank) return -1;
        return CompareKickers(a.Kickers, b.Kickers);
    }

    /// <summary>
    /// 获取所有n选k的组合
    /// </summary>
    private static IEnumerable<List<Card>> GetCombinations(List<Card> list, int length)
    {
        if (length == 1)
            return list.Select(t => new List<Card> { t });
        
        return GetCombinations(list, length - 1)
            .SelectMany(t => list.Where(e => !t.Contains(e) && list.IndexOf(e) > list.IndexOf(t.Last())),
                (t1, t2) => t1.Concat([t2]).ToList());
    }
}
