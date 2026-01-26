namespace TClient.Model;

/// <summary>
/// 客户端游戏状态
/// </summary>
public class GameState
{
	public string Stage { get; set; } = "Waiting";
	public int Pot { get; set; }
	public string Message { get; set; } = "";
	public List<Card> CommunityCards { get; set; } = [];
	public List<Card> MyHand { get; set; } = [];
	public int MyChips { get; set; } = 1000;
	public int MyCurrentBet { get; set; }
	public bool IsMyTurn { get; set; }
	public int CurrentBet { get; set; } // 当前最高下注
	public List<PlayerInfo> Players { get; set; } = [];
}

public class PlayerInfo
{
	public string Name { get; set; } = "";
	public int Chips { get; set; }
	public int CurrentBet { get; set; }
	public bool Folded { get; set; }
	public bool AllIn { get; set; }
	public bool IsCurrentPlayer { get; set; }
}
