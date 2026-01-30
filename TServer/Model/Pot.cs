namespace TServer.Model;

/// <summary>
/// 底池
/// </summary>
public class Pot
{
    /// <summary>
    /// 底池金额
    /// </summary>
    public int Amount { get; set; }
    
    /// <summary>
    /// 有资格争夺此底池的玩家ID列表
    /// </summary>
    public List<string> EligiblePlayerIds { get; init; } = [];

    /// <summary>
    /// 底池名称（主池/边池1/边池2...）
    /// </summary>
    public string Name { get; init; } = "Main Pot";
}

/// <summary>
/// 用于JSON传输的底池DTO
/// </summary>
public class PotDto(Pot pot)
{
    public int Amount { get; set; } = pot.Amount;
    public List<string> EligiblePlayerIds { get; set; } = [..pot.EligiblePlayerIds];
    public string Name { get; set; } = pot.Name;
}
