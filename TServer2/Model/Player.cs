namespace TServer2.Model;

/// <summary>
/// 玩家
/// </summary>
public class Player
{
    /// <summary>
    /// 玩家唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 玩家名字
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 座位索引（0-9）
    /// </summary>
    public int SeatIndex { get; set; } = -1;
    
    /// <summary>
    /// 筹码数量
    /// </summary>
    public int Chips { get; set; }
    
    /// <summary>
    /// 当前轮次已下注金额
    /// </summary>
    public int CurrentBet { get; set; }
    
    /// <summary>
    /// 本局总下注金额
    /// </summary>
    public int TotalBetThisHand { get; set; }
    
    /// <summary>
    /// 是否已弃牌
    /// </summary>
    public bool HasFolded { get; set; }
    
    /// <summary>
    /// 是否已全下
    /// </summary>
    public bool IsAllIn { get; set; }
    
    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected { get; set; } = true;
    
    /// <summary>
    /// 手牌（两张）
    /// </summary>
    public List<Card> HoleCards { get; set; } = [];
    
    /// <summary>
    /// 是否在摊牌阶段选择亮牌
    /// </summary>
    public bool WillShowCards { get; set; }

    public Player() { }

    public Player(string name, int chips = 1000)
    {
        Name = name;
        Chips = chips;
    }

    /// <summary>
    /// 重置玩家状态（新一轮开始时调用）
    /// </summary>
    public void ResetForNewHand()
    {
        HoleCards.Clear();
        CurrentBet = 0;
        TotalBetThisHand = 0;
        HasFolded = false;
        IsAllIn = false;
        WillShowCards = false;
    }

    /// <summary>
    /// 新下注轮开始时重置当前轮下注
    /// </summary>
    public void ResetBetForNewRound()
    {
        CurrentBet = 0;
    }

    /// <summary>
    /// 下注
    /// </summary>
    public void PlaceBet(int amount)
    {
        var actualAmount = Math.Min(amount, Chips);
        Chips -= actualAmount;
        CurrentBet += actualAmount;
        TotalBetThisHand += actualAmount;
        
        if (Chips == 0)
            IsAllIn = true;
    }

    /// <summary>
    /// 是否可以行动（未弃牌且未全下）
    /// </summary>
    public bool CanAct => !HasFolded && !IsAllIn && IsConnected;

    /// <summary>
    /// 是否仍在牌局中（未弃牌）
    /// </summary>
    public bool IsInHand => !HasFolded;

    public override string ToString() => $"{Name}(${Chips})";
}

/// <summary>
/// 用于JSON传输的玩家DTO
/// </summary>
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

    public PlayerDto() { }

    public PlayerDto(Player player)
    {
        Id = player.Id;
        Name = player.Name;
        SeatIndex = player.SeatIndex;
        Chips = player.Chips;
        CurrentBet = player.CurrentBet;
        HasFolded = player.HasFolded;
        IsAllIn = player.IsAllIn;
        IsConnected = player.IsConnected;
    }
}
