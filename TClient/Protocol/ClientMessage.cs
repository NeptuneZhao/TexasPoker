namespace TClient.Protocol;

/// <summary>
/// 客户端发送的消息类型（与TServer2同步）
/// </summary>
public enum ClientMessageType
{
    JoinRoom,
    PlayerAction,
    ShowCards,
    MuckCards,
    Heartbeat
}

/// <summary>
/// 玩家行动类型
/// </summary>
public enum ActionType
{
    Fold,
    Check,
    Call,
    Bet,
    Raise,
    AllIn
}

/// <summary>
/// 客户端消息
/// </summary>
public class ClientMessage
{
    public ClientMessageType Type { get; init; }
    public string? PlayerName { get; init; }
    public ActionType? Action { get; init; }
    public int? Amount { get; init; }
}
