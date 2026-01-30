namespace TClient.Model;

public class GameState
{
    // 基本状态
    public string Phase { get; set; } = "Waiting";
    public int HandNumber { get; set; }
    
    // 玩家信息
    public string MyPlayerId { get; set; } = string.Empty;
    public string MyPlayerName { get; set; } = string.Empty;
    public int MySeatIndex { get; set; }
    public int MyChips { get; set; } = 1000;
    public List<Card> MyHand { get; } = [];
    
    // 桌面状态
    public List<PlayerInfo> Players { get; } = [];
    public List<Card> CommunityCards { get; } = [];
    public List<PotInfo> Pots { get; } = [];
    
    // 位置信息
    public int DealerSeatIndex { get; set; } = -1;
    public int SmallBlindSeatIndex { get; set; } = -1;
    public int BigBlindSeatIndex { get; set; } = -1;
    
    // 行动状态
    public bool IsMyTurn { get; set; }
    public string? CurrentActingPlayerId { get; set; }
    public int CurrentBet { get; set; }
    public int CallAmount { get; set; }
    public int MinRaise { get; set; }
    public int ActionTimeout { get; set; }
    public List<AvailableActionInfo> AvailableActions { get; } = [];
    
    // 摊牌状态
    public bool IsShowdownRequest { get; set; }
    public bool MustShowCards { get; set; }
    
    // 倒计时
    public int CountdownSeconds { get; set; }
    public bool IsCountingDown { get; set; }
    
    // 消息
    public string LastMessage { get; set; } = string.Empty;
}

public class PlayerInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SeatIndex { get; init; }
    public int Chips { get; set; }
    public int CurrentBet { get; set; }
    public bool HasFolded { get; set; }
    public bool IsAllIn { get; set; }
    public bool IsConnected { get; init; } = true;
    public List<Card>? ShownCards { get; set; }
    public string? HandRank { get; set; }
}

public class PotInfo
{
    public string Name { get; init; } = string.Empty;
    public int Amount { get; init; }
}

public class AvailableActionInfo
{
    public string Type { get; init; } = string.Empty;
    public int? MinAmount { get; init; }
    public int? MaxAmount { get; init; }
    public string Description { get; init; } = string.Empty;
}
