using TServer.Model;

namespace TServer.Game;

public class HandComparer : IComparer<List<Card>>
{
    public int Compare(List<Card>? x, List<Card>? y)
    {
        switch (x)
        {
            case null when y == null:
                return 0;
            case null:
                return -1;
        }

        if (y == null) return 1;

        for (var i = 0; i < Math.Min(x.Count, y.Count); i++)
        {
            var cmp = x[i].Rank.CompareTo(y[i].Rank);
            if (cmp != 0) return cmp;
        }
        return 0;
    }
}

public class HandEqualityComparer : IEqualityComparer<List<Card>>
{
    public bool Equals(List<Card>? x, List<Card>? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        if (x.Count != y.Count) return false;

        return !x.Where((t, i) => t.Rank != y[i].Rank).Any();
    }

    public int GetHashCode(List<Card> obj)
    {
        var hash = new HashCode();
        
        foreach (var card in obj) hash.Add(card.Rank);
        return hash.ToHashCode();
    }
}
