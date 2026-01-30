namespace TClient.Protocol;

public enum ServerMessageType
{
    JoinSuccess,       // 加入成功
    PlayerJoined,      // 玩家加入
    PlayerLeft,        // 玩家离开
    CountdownStarted,  // 倒计时开始
    CountdownUpdate,   // 倒计时更新
    GameStarted,       // 游戏开始
    HoleCards,         // 发牌到手牌
    NewHandStarted,    // 新手牌
    BlindsPosted,      // 下盲注
    ActionRequest,     // 行动请求
    PlayerActed,       // 玩家行动
    PhaseChanged,      // 阶段变化
    CommunityCards,    // 公共牌
    ShowdownRequest,   // 摊牌请求
    PlayerShowedCards, // 玩家摊牌
    PotDistribution,   // 底池分配
    HandEnded,         // 手牌结束
    GameOver,          // 游戏结束
    GameState,         // 游戏状态
    Error,             // 错误消息
    Heartbeat          // 心跳检测
}

public class ServerMessage
{
    public ServerMessageType Type { get; init; }
    public object? Payload { get; init; }
}

// ReSharper disable ClassNeverInstantiated.Global
public class JoinSuccessPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int SeatIndex { get; set; }
    public int Chips { get; set; }
    public List<PlayerDto> ExistingPlayers { get; set; } = [];
}

public class PlayerJoinedPayload
{
    public PlayerDto Player { get; set; } = new();
    public int CurrentPlayerCount { get; set; }
    public int MinPlayersToStart { get; set; }
    public int MaxPlayers { get; set; }
}

public class PlayerLeftPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class CountdownStartedPayload
{
    public int Seconds { get; set; }
}

public class CountdownUpdatePayload
{
    public int SecondsRemaining { get; set; }
}

public class GameStartedPayload
{
    public List<PlayerDto> Players { get; set; } = [];
    public int DealerSeatIndex { get; set; }
    public int SmallBlindSeatIndex { get; set; }
    public int BigBlindSeatIndex { get; set; }
}

public class HoleCardsPayload
{
    public List<CardDto> Cards { get; set; } = [];
}

public class NewHandStartedPayload
{
    public int HandNumber { get; set; }
    public int DealerSeatIndex { get; set; }
    public int SmallBlindSeatIndex { get; set; }
    public int BigBlindSeatIndex { get; set; }
    public List<PlayerDto> Players { get; set; } = [];
}

public class BlindsPostedPayload
{
    public string SmallBlindPlayerId { get; set; } = string.Empty;
    public int SmallBlindAmount { get; set; }
    public string BigBlindPlayerId { get; set; } = string.Empty;
    public int BigBlindAmount { get; set; }
}

public class ActionRequestPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public List<AvailableAction> AvailableActions { get; set; } = [];
    public int TimeoutSeconds { get; set; }
    public int CurrentBet { get; set; }
    public int CallAmount { get; set; }
    public int MinRaise { get; set; }
    public int PlayerChips { get; set; }
    public List<PotDto> Pots { get; set; } = [];
}

public class AvailableAction
{
    public ActionType Type { get; set; }
    public int? MinAmount { get; set; }
    public int? MaxAmount { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PlayerActedPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public ActionType Action { get; set; }
    public int Amount { get; set; }
    public int PlayerChipsRemaining { get; set; }
    public int TotalPot { get; set; }
}

public class PhaseChangedPayload
{
    public string Phase { get; set; } = string.Empty;
    public List<CardDto> CommunityCards { get; set; } = [];
    public List<PotDto> Pots { get; set; } = [];
}

public class CommunityCardsPayload
{
    public List<CardDto> NewCards { get; set; } = [];
    public List<CardDto> AllCards { get; set; } = [];
}

public class ShowdownRequestPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public bool MustShow { get; set; }
    public int TimeoutSeconds { get; set; }
}

public class PlayerShowedCardsPayload
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public List<CardDto> Cards { get; set; } = [];
    public HandEvaluationDto? HandEvaluation { get; set; }
    public bool Mucked { get; set; }
}

public class PotDistributionPayload
{
    public List<PotWinner> Winners { get; set; } = [];
}

public class PotWinner
{
    public string PotName { get; set; } = string.Empty;
    public int PotAmount { get; set; }
    public List<WinnerInfo> Winners { get; init; } = [];
}

public class WinnerInfo
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int AmountWon { get; set; }
    public string HandRank { get; set; } = string.Empty;
}

public class HandEndedPayload
{
    public List<PlayerDto> Players { get; set; } = [];
    public int NextDealerSeatIndex { get; set; }
}

public class GameOverPayload
{
    public List<RankingEntry> Rankings { get; set; } = [];
    public string Reason { get; set; } = string.Empty;
}

public class RankingEntry
{
    public int Rank { get; init; }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public int FinalChips { get; init; }
}

public class GameStatePayload
{
    public string Phase { get; set; } = string.Empty;
    public List<PlayerDto> Players { get; set; } = [];
    public List<CardDto> CommunityCards { get; set; } = [];
    public List<PotDto> Pots { get; set; } = [];
    public int DealerSeatIndex { get; set; }
    public string? CurrentActingPlayerId { get; set; }
}

public class ErrorPayload
{
    public string Message { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class PlayerDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SeatIndex { get; set; }
    public int Chips { get; set; }
    public int CurrentBet { get; set; }
    public bool HasFolded { get; set; }
    public bool IsAllIn { get; set; }
    public bool IsConnected { get; set; }
}

public class CardDto
{
    public int Suit { get; set; }  // 0=Clubs, 1=Diamonds, 2=Hearts, 3=Spades
    public int Rank { get; set; }  // 2-14 (14=Ace)
}

public class PotDto
{
    public string Name { get; set; } = string.Empty;
    public int Amount { get; set; }
    public List<string> EligiblePlayerIds { get; set; } = [];
}

public class HandEvaluationDto
{
    public string Rank { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}