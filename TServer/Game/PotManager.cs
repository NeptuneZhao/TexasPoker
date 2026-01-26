using TServer.Model;

namespace TServer.Game;

public class PotManager
{
	private readonly List<Pot> _pots = [];
	public IReadOnlyList<Pot> Pots => _pots;

	public void Clear() => _pots.Clear();

	/// <summary>
	/// 每轮下注结束时调用，将玩家面前的 CurrentBet 收入底池，并处理边池。
	/// </summary>
	public void CollectBets(List<Player> players)
	{
		var activePlayers = players.Where(p => !p.Folded).ToList();
		if (activePlayers.Count == 0 && _pots.Count == 0) return; // 只有在游戏刚开始全部断线才可能

		if (_pots.Count == 0)
		{
			// 只有初始时需要建立第一个 Pot，所有未弃牌玩家都有资格
			_pots.Add(new Pot { EligiblePlayers = [..activePlayers] });
		}
		
		// 循环直到收完所有玩家桌面的下注
		while (players.Any(p => p.CurrentBet > 0))
		{
			var contributors = players.Where(p => p.CurrentBet > 0).ToList();
			
			// 这一层洋葱的厚度：由非弃牌玩家中最小的下注额决定
			// 如果所有人都弃牌了（理论不应发生，因为会提前结束），就取最小的弃牌下注
			var activeContributors = contributors.Where(p => !p.Folded).ToList();
			var stepAmount = activeContributors.Count > 0 
				? activeContributors.Min(p => p.CurrentBet) 
				: contributors.Min(p => p.CurrentBet);

			// 收集这一层的筹码
			var potChunk = 0;
			foreach (var p in contributors)
			{
				var contribution = Math.Min(p.CurrentBet, stepAmount);
				p.CurrentBet -= contribution;
				potChunk += contribution;
			}

			// 归入当前最新的 Pot
			var currentPot = _pots.Last();
			currentPot.Amount += potChunk;

			// 检查是否有人在这一层 All-In 并耗尽了筹码
			// 如果有 All-In 玩家筹码耗尽，意味着他们不能参与后续更高级别的下注
			// 因此当前 Pot 必须封口，并在下一轮循环创建新的 Side Pot
			var anyAllInFinished = activeContributors.Any(p => p is { AllIn: true, CurrentBet: 0 });
			var moreBetsToCollect = players.Any(p => p.CurrentBet > 0);

			if (!anyAllInFinished || !moreBetsToCollect) continue;
			{
				// 创建新的边池
				// 只有那些还没耗尽筹码（且未弃牌）的玩家有资格进入下一个池
				// 注意：这里简单的 !p.AllIn 判断是因为如果他 AllIn 了且 CurrentBet > 0，说明它是更大的 All-In，
				// 但在这个循环里，他的 CurrentBet 刚被扣除了一部分。
				// 实际上，只要还有 CurrentBet > 0 的人，就有资格进下一个池。
				var actuallyNextEligible = players.Where(p => p is { Folded: false, CurrentBet: > 0 }).ToList();
				
				_pots.Add(new Pot { Amount = 0, EligiblePlayers = actuallyNextEligible });
			}
		}
	}

	/// <summary>
	/// 分配奖池
	/// </summary>
	/// <param name="ranks">按牌力从强到弱排好序的玩家组（同一组代表平手）</param>
	/// <returns>玩家 -> 赢得金额</returns>
	public Dictionary<Player, int> Distribute(List<List<Player>> ranks)
	{
		var winnings = new Dictionary<Player, int>();

		foreach (var pot in _pots.Where(pot => pot.Amount != 0))
		{
			// 从最强牌力组开始寻找有资格的玩家
			foreach (var group in ranks)
			{
				var winners = group.Where(p => pot.EligiblePlayers.Contains(p)).ToList();
				if (winners.Count <= 0) continue;
				// 找到赢家，平分底池
				var share = pot.Amount / winners.Count;
				var remainder = pot.Amount % winners.Count; 
				// 余数通常给位置最不利者，或者随机，或第一位。简化给第一位。
					
				foreach (var winner in winners)
				{
					winnings.TryAdd(winner, 0);
					winnings[winner] += share;
				}
				// 把余数给第一个人
				winnings[winners[0]] += remainder;

				// 这个 Pot 分完了，跳出 ranks 循环，处理下一个 Pot
				break;
			}
		}
		
		return winnings;
	}
}

public class Pot // 底池
{
	public int Amount;
	public required List<Player> EligiblePlayers;
}