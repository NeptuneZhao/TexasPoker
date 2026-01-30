using TServer.Model;

namespace TServer.Game;

/// <summary>
/// 牌组
/// </summary>
public class Deck
{
    private readonly List<Card> _cards = [];
    private int _currentIndex;

    public Deck()
    {
        Initialize();
        Shuffle();
    }

    private void Initialize()
    {
        _cards.Clear();
        foreach (var suit in Enum.GetValues<Suit>())
        {
            foreach (var rank in Enum.GetValues<Rank>())
            {
                _cards.Add(new Card(suit, rank));
            }
        }
        _currentIndex = 0;
    }

    /// <summary>
    /// 洗牌
    /// </summary>
    private void Shuffle()
    {
        var random = Random.Shared;
        for (var i = _cards.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
        _currentIndex = 0;
    }

    /// <summary>
    /// 发一张牌
    /// </summary>
    public Card Deal() => _currentIndex >= _cards.Count ?
        throw new InvalidOperationException("No more cards in deck") :
        _cards[_currentIndex++];

    /// <summary>
    /// 烧牌（弃掉顶牌）
    /// </summary>
    public void Burn()
    {
        if (_currentIndex >= _cards.Count)
            throw new InvalidOperationException("No more cards to burn");
        
        _currentIndex++;
    }

    /// <summary>
    /// 剩余牌数
    /// </summary>
    public int RemainingCards => _cards.Count - _currentIndex;

    /// <summary>
    /// 重置牌组
    /// </summary>
    public void Reset()
    {
        Initialize();
        Shuffle();
    }
}
