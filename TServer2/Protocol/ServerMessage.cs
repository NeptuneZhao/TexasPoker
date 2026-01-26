using TServer2.Model;

namespace TServer2.Protocol;

/// <summary>
/// 服务端发送的消息类型
/// </summary>
public enum ServerMessageType
{
    /// <summary>加入房间成功</summary>
    JoinSuccess,
    
    /// <summary>玩家加入通知</summary>
    PlayerJoined,
    
    /// <summary>玩家离开通知</summary>
    PlayerLeft,
    
    /// <summary>倒计时开始</summary>
    CountdownStarted,
    
    /// <summary>倒计时更新</summary>
    CountdownUpdate,
    
    /// <summary>游戏开始</summary>
    GameStarted,
    
    /// <summary>发手牌（私密消息）</summary>
    HoleCards,
    
    /// <summary>新一轮开始</summary>
    NewHandStarted,
    
    /// <summary>盲注信息</summary>
    BlindsPosted,
    
    /// <summary>请求玩家行动</summary>
    ActionRequest,
    
    /// <summary>玩家行动广播</summary>
    PlayerActed,
    
    /// <summary>阶段变化（翻牌/转牌/河牌）</summary>
    PhaseChanged,
    
    /// <summary>公共牌发出</summary>
    CommunityCards,
    
    /// <summary>摊牌请求</summary>
    ShowdownRequest,
    
    /// <summary>玩家亮牌</summary>
    PlayerShowedCards,
    
    /// <summary>底池分配结果</summary>
    PotDistribution,
    
    /// <summary>一手牌结束</summary>
    HandEnded,
    
    /// <summary>游戏结束（排行榜）</summary>
    GameOver,
    
    /// <summary>游戏状态同步</summary>
    GameState,
    
    /// <summary>错误消息</summary>
    Error,
    
    /// <summary>心跳响应</summary>
    Heartbeat
}

/// <summary>
/// 服务端消息
/// </summary>
public class ServerMessage
{
    public ServerMessageType Type { get; set; }
    public object? Payload { get; set; }
}

#region Payload Types

/// <summary>
/// 加入成功响应
/// </summary>
public class JoinSuccessPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int SeatIndex { get; set; }
    public int Chips { get; set; }
    public List<PlayerDto> ExistingPlayers { get; set; } = [];
}

/// <summary>
/// 玩家加入通知
/// </summary>
public class PlayerJoinedPayload
{
    public PlayerDto Player { get; set; } = new();
    public int CurrentPlayerCount { get; set; }
    public int MinPlayersToStart { get; set; }
    public int MaxPlayers { get; set; }
}

/// <summary>
/// 玩家离开通知
/// </summary>
public class PlayerLeftPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 倒计时开始
/// </summary>
public class CountdownStartedPayload
{
    public int Seconds { get; set; }
}

/// <summary>
/// 倒计时更新
/// </summary>
public class CountdownUpdatePayload
{
    public int SecondsRemaining { get; set; }
}

/// <summary>
/// 游戏开始
/// </summary>
public class GameStartedPayload
{
    public List<PlayerDto> Players { get; set; } = [];
    public int DealerSeatIndex { get; set; }
    public int SmallBlindSeatIndex { get; set; }
    public int BigBlindSeatIndex { get; set; }
}

/// <summary>
/// 发手牌
/// </summary>
public class HoleCardsPayload
{
    public List<CardDto> Cards { get; set; } = [];
}

/// <summary>
/// 新一手牌开始
/// </summary>
public class NewHandStartedPayload
{
    public int HandNumber { get; set; }
    public int DealerSeatIndex { get; set; }
    public int SmallBlindSeatIndex { get; set; }
    public int BigBlindSeatIndex { get; set; }
    public List<PlayerDto> Players { get; set; } = [];
}

/// <summary>
/// 盲注信息
/// </summary>
public class BlindsPostedPayload
{
    public string SmallBlindPlayerId { get; set; } = string.Empty;
    public int SmallBlindAmount { get; set; }
    public string BigBlindPlayerId { get; set; } = string.Empty;
    public int BigBlindAmount { get; set; }
}

/// <summary>
/// 请求玩家行动
/// </summary>
public class ActionRequestPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public List<AvailableAction> AvailableActions { get; set; } = [];
    public int TimeoutSeconds { get; set; }
    public int CurrentBet { get; set; }       // 当前最高下注
    public int CallAmount { get; set; }       // 跟注需要的金额
    public int MinRaise { get; set; }         // 最小加注额
    public int PlayerChips { get; set; }      // 玩家当前筹码
    public List<PotDto> Pots { get; set; } = [];
}

/// <summary>
/// 可用行动
/// </summary>
public class AvailableAction
{
    public ActionType Type { get; set; }
    public int? MinAmount { get; set; }
    public int? MaxAmount { get; set; }
    public string Description { get; set; } = string.Empty;

    public AvailableAction() { }

    public AvailableAction(ActionType type, string description, int? minAmount = null, int? maxAmount = null)
    {
        Type = type;
        Description = description;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
    }
}

/// <summary>
/// 玩家行动广播
/// </summary>
public class PlayerActedPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public ActionType Action { get; set; }
    public int Amount { get; set; }
    public int PlayerChipsRemaining { get; set; }
    public int TotalPot { get; set; }
}

/// <summary>
/// 阶段变化
/// </summary>
public class PhaseChangedPayload
{
    public string Phase { get; set; } = string.Empty;
    public List<CardDto> CommunityCards { get; set; } = [];
    public List<PotDto> Pots { get; set; } = [];
}

/// <summary>
/// 公共牌
/// </summary>
public class CommunityCardsPayload
{
    public List<CardDto> NewCards { get; set; } = [];
    public List<CardDto> AllCards { get; set; } = [];
}

/// <summary>
/// 摊牌请求
/// </summary>
public class ShowdownRequestPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public bool MustShow { get; set; }  // 是否必须亮牌（最后跟注者）
    public int TimeoutSeconds { get; set; }
}

/// <summary>
/// 玩家亮牌
/// </summary>
public class PlayerShowedCardsPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public List<CardDto> Cards { get; set; } = [];
    public HandEvaluationDto? HandEvaluation { get; set; }
    public bool Mucked { get; set; }  // 是否盖牌
}

/// <summary>
/// 底池分配
/// </summary>
public class PotDistributionPayload
{
    public List<PotWinner> Winners { get; set; } = [];
}

/// <summary>
/// 底池获胜者
/// </summary>
public class PotWinner
{
    public string PotName { get; set; } = string.Empty;
    public int PotAmount { get; set; }
    public List<WinnerInfo> Winners { get; set; } = [];
}

/// <summary>
/// 获胜者信息
/// </summary>
public class WinnerInfo
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int AmountWon { get; set; }
    public string HandRank { get; set; } = string.Empty;
}

/// <summary>
/// 一手牌结束
/// </summary>
public class HandEndedPayload
{
    public List<PlayerDto> Players { get; set; } = [];
    public int NextDealerSeatIndex { get; set; }
}

/// <summary>
/// 游戏结束
/// </summary>
public class GameOverPayload
{
    public List<RankingEntry> Rankings { get; set; } = [];
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 排名条目
/// </summary>
public class RankingEntry
{
    public int Rank { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int FinalChips { get; set; }
}

/// <summary>
/// 游戏状态
/// </summary>
public class GameStatePayload
{
    public string Phase { get; set; } = string.Empty;
    public List<PlayerDto> Players { get; set; } = [];
    public List<CardDto> CommunityCards { get; set; } = [];
    public List<PotDto> Pots { get; set; } = [];
    public int DealerSeatIndex { get; set; }
    public string? CurrentActingPlayerId { get; set; }
}

/// <summary>
/// 错误消息
/// </summary>
public class ErrorPayload
{
    public string Message { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

#endregion
