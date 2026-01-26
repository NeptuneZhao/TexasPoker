namespace TServer2.Protocol;

/// <summary>
/// 客户端发送的消息类型
/// </summary>
public enum ClientMessageType
{
    /// <summary>加入房间</summary>
    JoinRoom,
    
    /// <summary>玩家行动</summary>
    PlayerAction,
    
    /// <summary>摊牌时选择亮牌</summary>
    ShowCards,
    
    /// <summary>摊牌时选择弃牌（不亮牌）</summary>
    MuckCards,
    
    /// <summary>心跳</summary>
    Heartbeat
}

/// <summary>
/// 玩家行动类型
/// </summary>
public enum ActionType
{
    /// <summary>弃牌</summary>
    Fold,
    
    /// <summary>过牌（Check）</summary>
    Check,
    
    /// <summary>跟注</summary>
    Call,
    
    /// <summary>下注（第一个下注）</summary>
    Bet,
    
    /// <summary>加注</summary>
    Raise,
    
    /// <summary>全下</summary>
    AllIn
}

/// <summary>
/// 客户端消息
/// </summary>
public class ClientMessage
{
    public ClientMessageType Type { get; set; }
    public string? PlayerName { get; set; }  // JoinRoom时使用
    public ActionType? Action { get; set; }  // PlayerAction时使用
    public int? Amount { get; set; }         // Bet/Raise/AllIn时使用
}

/// <summary>
/// 玩家行动请求
/// </summary>
public class PlayerActionRequest
{
    public ActionType Action { get; set; }
    public int Amount { get; set; }

    public PlayerActionRequest() { }

    public PlayerActionRequest(ActionType action, int amount = 0)
    {
        Action = action;
        Amount = amount;
    }
}
