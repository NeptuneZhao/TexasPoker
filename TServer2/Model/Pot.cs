namespace TServer2.Model;

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
    public List<string> EligiblePlayerIds { get; set; } = [];

    /// <summary>
    /// 底池名称（主池/边池1/边池2...）
    /// </summary>
    public string Name { get; set; } = "Main Pot";
}

/// <summary>
/// 用于JSON传输的底池DTO
/// </summary>
public class PotDto
{
    public int Amount { get; set; }
    public List<string> EligiblePlayerIds { get; set; } = [];
    public string Name { get; set; } = string.Empty;

    public PotDto() { }

    public PotDto(Pot pot)
    {
        Amount = pot.Amount;
        EligiblePlayerIds = [..pot.EligiblePlayerIds];
        Name = pot.Name;
    }
}
