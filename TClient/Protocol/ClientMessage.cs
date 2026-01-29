namespace TClient.Protocol;

public enum ClientMessageType
{
    JoinRoom,
    PlayerAction,
    ShowCards,
    MuckCards,
    Heartbeat
}

public enum ActionType
{
    Fold,
    Check,
    Call,
    Bet,
    Raise,
    AllIn
}

public class ClientMessage
{
    public ClientMessageType Type { get; init; }
    public string? PlayerName { get; init; }
    public ActionType? Action { get; init; }
    public int? Amount { get; init; }
}
