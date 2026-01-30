namespace TServer.Model;

/// <summary>
/// 游戏阶段
/// </summary>
public enum GamePhase
{
    /// <summary>等待玩家加入</summary>
    WaitingForPlayers,
    
    /// <summary>倒计时中（4人已到，等待10秒）</summary>
    Countdown,
    
    /// <summary>翻牌前</summary>
    PreFlop,
    
    /// <summary>翻牌（3张公共牌）</summary>
    Flop,
    
    /// <summary>转牌（第4张公共牌）</summary>
    Turn,
    
    /// <summary>河牌（第5张公共牌）</summary>
    River,
    
    /// <summary>摊牌阶段</summary>
    Showdown,
    
    /// <summary>结算阶段</summary>
    Settlement,
    
    /// <summary>游戏结束（玩家少于4人）</summary>
    GameOver
}

/// <summary>
/// 玩家位置类型
/// </summary>
public enum PositionType
{
    Dealer,     // 庄家（Button）
    SmallBlind, // 小盲
    BigBlind,   // 大盲
    UTG,        // 枪口位（Under the Gun）
    Middle,     // 中间位
    Cutoff,     // 切牌位
    Normal      // 普通位置
}

/// <summary>
/// 牌型等级
/// </summary>
public enum HandRank
{
    HighCard = 1,      // 高牌
    OnePair = 2,       // 对子
    TwoPair = 3,       // 两对
    ThreeOfAKind = 4,  // 三条
    Straight = 5,      // 顺子
    Flush = 6,         // 同花
    FullHouse = 7,     // 葫芦
    FourOfAKind = 8,   // 四条
    StraightFlush = 9, // 同花顺
    RoyalFlush = 10    // 皇家同花顺
}

/// <summary>
/// 玩家手牌评估结果
/// </summary>
public class HandEvaluation(string playerId, HandRank rank, List<Card> bestFive, List<int> kickers)
{
    public string PlayerId { get; } = playerId;
    public HandRank Rank { get; } = rank;
    public List<Card> BestFive { get; } = bestFive;
    public List<int> Kickers { get; } = kickers; // 用于比较同等牌型
}

/// <summary>
/// 玩家手牌评估DTO
/// </summary>
public class HandEvaluationDto
{
    public string PlayerId { get; set; }
    public string Rank { get; set; }
    public List<CardDto> BestFive { get; set; }

    public HandEvaluationDto(HandEvaluation eval)
    {
        PlayerId = eval.PlayerId;
        Rank = eval.Rank.ToString();
        BestFive = eval.BestFive.Select(c => new CardDto(c)).ToList();
    }
}
