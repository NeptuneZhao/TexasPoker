namespace TServer.Model;

public class Player
{
	public string Id { get; } = Guid.NewGuid().ToString();
	public string Name { get; set; }
	public int Chips { get; set; }
	public int CurrentBet { get; set; }
	public bool Folded { get; set; }
	public bool AllIn { get; set; }
	public List<Card> Hand { get; } = [];

	public Player(string name = "", int chips = 0)
	{
		Name = name;
		Chips = chips;
		CurrentBet = 0;
		Folded = false;
		AllIn = false;
	}

	/// <summary>
	/// 重置玩家状态（新一轮开始时调用）
	/// </summary>
	public void ResetForNewRound()
	{
		Hand.Clear();
		CurrentBet = 0;
		Folded = false;
		AllIn = false;
	}
}