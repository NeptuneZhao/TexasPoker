using TServer.Logging;
using TServer.Model;
using TServer.Protocol;

namespace TServer.Game;

public class GameStateMachine
{
	private const int BigBlindAmount = 20;
	
	private readonly Func<ServerMessage, Task> _broadcaster;
	private readonly Func<Player, ServerMessage, Task> _unicast;
	private readonly SemaphoreSlim _gameLock = new(1, 1);

	public List<Player> Players { get; } = [];
	private List<Card> CommunityCards { get; } = [];
	private PotManager PotManager { get; } = new();
	private BettingRound BettingRound { get; } = new();
	public GameStage Stage { get; private set; } = GameStage.Waiting;
	
	private Deck _deck = new();
	private int _dealerIndex;
	private int _currentPlayerIndex;
	private readonly HashSet<Player> _playersActed = [];

	public GameStateMachine(Func<ServerMessage, Task> broadcaster, Func<Player, ServerMessage, Task> unicast)
	{
		_broadcaster = broadcaster;
		_unicast = unicast;
	}

	public void AddPlayer(Player player)
	{
		if (Players.Contains(player)) return;
		Players.Add(player);
	}

	public void RemovePlayer(Player player)
	{
		if (!Players.Contains(player)) return;
		
		Players.Remove(player);
		_playersActed.Remove(player);
		
		// 如果游戏进行中，检查是否只剩一个玩家
		if (Stage != GameStage.Waiting && Stage != GameStage.Finished)
		{
			var remainingPlayers = Players.Count(p => !p.Folded);
			if (remainingPlayers <= 1)
			{
				// 异步处理剩余玩家获胜
				_ = Task.Run(async () => await HandleSinglePlayerWinAsync());
			}
		}
	}

	public async Task StartGameAsync()
	{
		if (Players.Count < 2)
		{
			Logger.Log("Not enough players to start game", LogLevel.Warn);
			return;
		}

		Stage = GameStage.PreFlop;
		_deck = new Deck();
		CommunityCards.Clear();
		PotManager.Clear();
		
		foreach (var p in Players)
		{
			p.ResetForNewRound();
			p.Hand.Add(_deck.Deal());
			p.Hand.Add(_deck.Deal());
		}

		// 移动 Button
		_dealerIndex = (_dealerIndex + 1) % Players.Count;
		
		int sbIndex, bbIndex;
		
		// 2人游戏特殊规则：庄家是小盲，另一人是大盲
		if (Players.Count == 2)
		{
			sbIndex = _dealerIndex;
			bbIndex = (_dealerIndex + 1) % Players.Count;
		}
		else
		{
			sbIndex = (_dealerIndex + 1) % Players.Count;
			bbIndex = (_dealerIndex + 2) % Players.Count;
		}
		
		BettingRound.StartRound(BigBlindAmount);
		
		// 扣除盲注
		PlaceBlind(Players[sbIndex], BigBlindAmount / 2);
		PlaceBlind(Players[bbIndex], BigBlindAmount);
		
		// PreFlop 阶段 HighestBet 应为大盲金额
		BettingRound.HighestBet = BigBlindAmount;

		// 行动从大盲后一位开始（2人时为小盲先行动）
		if (Players.Count == 2)
		{
			_currentPlayerIndex = sbIndex; // 2人时小盲先行动
		}
		else
		{
			_currentPlayerIndex = (bbIndex + 1) % Players.Count;
		}
		
		_playersActed.Clear();
		
		// 广播游戏开始
		await _broadcaster(new ServerMessage
		{
			Type = ServerMessageType.GameStart,
			PayLoad = new
			{
				DealerIndex = _dealerIndex,
				SmallBlindIndex = sbIndex,
				BigBlindIndex = bbIndex,
				Players = Players.Select((p, idx) => new { Index = idx, p.Name, p.Chips }).ToList()
			}
		});
		
		// 向每个玩家单独发送手牌
		foreach (var player in Players)
		{
			await _unicast(player, new ServerMessage
			{
				Type = ServerMessageType.DealCard,
				PayLoad = new
				{
					Hand = player.Hand.Select(c => c.ToString()).ToList()
				}
			});
		}

		await BroadcastStateAsync("Game Started. Blinds placed.");
		await RequestActionAsync();
	}

	private static void PlaceBlind(Player p, int amount)
	{
		var actual = Math.Min(p.Chips, amount);
		p.Chips -= actual;
		p.CurrentBet += actual;
		if (p.Chips == 0) p.AllIn = true;
	}

	public async Task HandleActionAsync(Player player, ActionType action, int amount = 0)
	{
		await _gameLock.WaitAsync();
		try
		{
			await HandleActionInternalAsync(player, action, amount);
		}
		finally
		{
			_gameLock.Release();
		}
	}
	
	private async Task HandleActionInternalAsync(Player player, ActionType action, int amount = 0)
	{
		if (Stage is GameStage.Waiting or GameStage.Finished)
		{
			await SendErrorAsync(player, "Game is not in progress.");
			return;
		}
		
		if (Players[_currentPlayerIndex] != player)
		{
			await SendErrorAsync(player, "It's not your turn.");
			return;
		}

		// 验证并执行
		var valid = BettingRound.HandleBet(player, amount, action);
		if (!valid)
		{
			await SendErrorAsync(player, "Invalid action or amount.");
			return;
		}

		await BroadcastStateAsync($"{player.Name} did {action}" + (amount > 0 ? $" ({amount})" : ""));

		// Track action
		_playersActed.Add(player);
		
		// 检查是否只剩一个玩家（其他人都弃牌了）
		var remainingPlayers = Players.Count(p => !p.Folded);
		if (remainingPlayers == 1)
		{
			await HandleSinglePlayerWinAsync();
			return;
		}

		// If a raise happens, others (who aren't all-in/folded) must act again
		if (action is ActionType.Raise or ActionType.Bet)
		{
			_playersActed.RemoveWhere(p => p != player && !p.Folded && !p.AllIn);
		}
		else if (action == ActionType.AllIn && player.CurrentBet > BettingRound.HighestBet)
		{
			// All-In raise
			_playersActed.RemoveWhere(p => p != player && !p.Folded && !p.AllIn);
		}

		// 检查是否这轮下注结束
		if (IsBettingRoundFinished())
		{
			await AdvanceStageAsync();
		}
		else
		{
			MoveToNextPlayer();
			await RequestActionAsync();
		}
	}
	
	private async Task SendErrorAsync(Player player, string errorMessage)
	{
		await _unicast(player, new ServerMessage
		{
			Type = ServerMessageType.Error,
			PayLoad = new { Message = errorMessage }
		});
	}
	
	private async Task HandleSinglePlayerWinAsync()
	{
		var winner = Players.FirstOrDefault(p => !p.Folded);
		if (winner == null) return;
		
		// 收集所有剩余下注
		PotManager.CollectBets(Players);
		var totalPot = PotManager.Pots.Sum(p => p.Amount);
		winner.Chips += totalPot;
		
		await _broadcaster(new ServerMessage
		{
			Type = ServerMessageType.GameResult,
			PayLoad = new
			{
				Winner = winner.Name,
				WonAmount = totalPot,
				Reason = "All other players folded"
			}
		});
		
		Stage = GameStage.Finished;
		
		// 移除筹码为0的玩家
		var eliminatedPlayers = Players.Where(p => p.Chips <= 0).ToList();
		foreach (var p in eliminatedPlayers)
		{
			Players.Remove(p);
			Logger.Log($"{p.Name} eliminated (no chips left).");
			await _broadcaster(new ServerMessage
			{
				Type = ServerMessageType.GameState,
				PayLoad = new { Message = $"{p.Name} has been eliminated." }
			});
		}
		
		// 5秒后开始下一局
		await Task.Delay(5000);
		if (Players.Count >= 2)
			await StartGameAsync();
		else
			Stage = GameStage.Waiting;
	}

	private void MoveToNextPlayer()
	{
		var loopCount = 0;
		do
		{
			_currentPlayerIndex = (_currentPlayerIndex + 1) % Players.Count;
			loopCount++;
			if (loopCount > Players.Count) break; // Should not happen if game loop logic is correct
		} 
		while (Players[_currentPlayerIndex].Folded || Players[_currentPlayerIndex].AllIn);
	}

	private bool IsBettingRoundFinished()
	{
		var activePlayers = Players.Where(p => p is { Folded: false, AllIn: false }).ToList();
		
		// If 0 or 1 active player remains (others folded/all-in), we might be done?
		// No, if 1 active player remains, he might still need to call an All-In raise?
		// Actually, if everyone else is All-in, the active player must Call or Fold.
		// So he is technically "Active".
		
		if (activePlayers.Count == 0) return true;

		// Everyone active must match the highest bet AND have acted.
		var allMatched = activePlayers.All(p => p.CurrentBet == BettingRound.HighestBet);
		var allActed = activePlayers.All(p => _playersActed.Contains(p));

		// Special case: Big Blind option in PreFlop.
		// If PreFlop, and HighestBet == BigBlind, BB must act.
		// Our _playersActed set handles this correctly (BB starts not acted).
		
		return allMatched && allActed;
	}

	private async Task AdvanceStageAsync()
	{
		// 1. 收集筹码放入底池
		PotManager.CollectBets(Players);
		// 重置下注状态
		_playersActed.Clear();
		BettingRound.StartRound(BigBlindAmount); // 实际上转牌圈不需要Blind，只是重置 HighestBet=0
		foreach (var p in Players) p.CurrentBet = 0;

		// 2. 发牌 / 结算
		switch (Stage)
		{
			case GameStage.PreFlop:
				Stage = GameStage.Flop;
				// Flop 发3张公共牌
				CommunityCards.Add(_deck.Deal());
				CommunityCards.Add(_deck.Deal());
				CommunityCards.Add(_deck.Deal());
				break;
			case GameStage.Flop:
				Stage = GameStage.Turn;
				// Turn 发1张公共牌
				CommunityCards.Add(_deck.Deal());
				break;
			case GameStage.Turn:
				Stage = GameStage.River;
				// River 发1张公共牌
				CommunityCards.Add(_deck.Deal());
				break;
			case GameStage.River:
				Stage = GameStage.Showdown;
				await HandleShowdownAsync();
				return;
			case GameStage.Waiting:
			case GameStage.Showdown:
			case GameStage.Finished:
			default:
				throw new NotImplementedException();
		}

		// 广播新阶段和公共牌
		await _broadcaster(new ServerMessage
		{
			Type = ServerMessageType.StageChanged,
			PayLoad = new
			{
				Stage,
				CommunityCards = CommunityCards.Select(c => c.ToString()).ToList()
			}
		});

		// 3. 开始新一轮下注
		// 从 Dealer 后第一个未 Fold 玩家开始
		_currentPlayerIndex = _dealerIndex; // Start from dealer, loop will perform +1
		MoveToNextPlayer();
		
		await BroadcastStateAsync($"Stage: {Stage}");
		
		// 检查是否只剩一人没 AllIn，直接发完牌? (Auto-run)
		// Or if only one player has cards (everyone else folded) - handled in HandleAction.
		// If everyone (or all but one) is All-In?
		var active = Players.Count(p => p is { Folded: false, AllIn: false });
		// If active <= 1, it means no more betting can occur (except checking down if bet is equal).
		// But in AdvanceStage, bets are collected, so amounts are 0.
		// If active <= 1, proceed automatically.
		
		if (active <= 1)
		{
			// 自动快进到 Showdown
			// Need a small delay or loop?
			while (Stage != GameStage.Showdown && Stage != GameStage.Finished)
			{
				await Task.Delay(1000); // Small delay for UX
				await AdvanceStageAsync();
			}
		}
		else
		{
			await RequestActionAsync();
		}
	}

	private async Task HandleShowdownAsync()
	{
		var finalPlayers = Players.Where(p => !p.Folded).ToList();
		Dictionary<Player, int> winnings;
		
		if (finalPlayers.Count == 1)
		{
			// 剩下的赢
			winnings = PotManager.Distribute([finalPlayers]);
		}
		else
		{
			// 计算牌型
			var evaluations = finalPlayers.Select(p => 
			{
				var (rank, bestFive) = HandEvaluator.Evaluate(p.Hand, CommunityCards);
				return new { Player = p, Rank = rank, BestFive = bestFive };
			}).ToList();
			
			// Use the new Comparers
			var rankedGroups = evaluations
				.OrderByDescending(x => x.Rank)
				.ThenByDescending(x => x.BestFive, new HandComparer())
				.GroupBy(x => x.BestFive, new HandEqualityComparer())
				.Select(g => g.Select(x => x.Player).ToList())
				.ToList();

			winnings = PotManager.Distribute(rankedGroups);
		}
		
		foreach (var kvp in winnings)
		{
			kvp.Key.Chips += kvp.Value;
		}
		
		// 广播所有玩家的手牌（Showdown时公开）
		await _broadcaster(new ServerMessage 
		{ 
			Type = ServerMessageType.Showdown, 
			PayLoad = finalPlayers.Select(p => new 
			{ 
				p.Name, 
				Hand = p.Hand.Select(c => c.ToString()).ToList(),
				Evaluation = HandEvaluator.Evaluate(p.Hand, CommunityCards).Rank.ToString()
			}).ToList() 
		});
		
		// Notify results
		await BroadcastStateAsync("Showdown Results");
		await _broadcaster(new ServerMessage 
		{ 
			Type = ServerMessageType.GameResult, 
			PayLoad = winnings.Select(w => new { w.Key.Name, Won = w.Value }).ToList() 
		});
		
		// Next Game
		Stage = GameStage.Finished;
		
		// 移除筹码为0的玩家
		var eliminatedPlayers = Players.Where(p => p.Chips <= 0).ToList();
		foreach (var p in eliminatedPlayers)
		{
			Players.Remove(p);
			Logger.Log($"{p.Name} eliminated (no chips left).");
			await _broadcaster(new ServerMessage
			{
				Type = ServerMessageType.GameState,
				PayLoad = new { Message = $"{p.Name} has been eliminated." }
			});
		}
		
		// Reset in 5 seconds...
		await Task.Delay(5000);
		if (Players.Count >= 2)
		    await StartGameAsync();
		else
		    Stage = GameStage.Waiting;
	}

	private async Task BroadcastStateAsync(string msg)
	{
		await _broadcaster(new ServerMessage 
		{ 
			Type = ServerMessageType.GameState, 
			PayLoad = new 
			{ 
				Stage, 
				Pot = PotManager.Pots.Sum(p => p.Amount), 
				Message = msg, 
				CommunityCards = CommunityCards.Select(c => c.ToString()).ToList(),
				CurrentBets = Players.Select(p => new { p.Name, p.CurrentBet, p.Chips, p.Folded, p.AllIn }).ToList()
			} 
		});
	}

	private async Task RequestActionAsync()
	{
		// 通知当前玩家行动
		var player = Players[_currentPlayerIndex];
		
		// 计算可用的操作
		var neededToCall = BettingRound.HighestBet - player.CurrentBet;
		var canCheck = neededToCall <= 0;
		var canCall = neededToCall > 0 && player.Chips >= neededToCall;
		var canRaise = player.Chips > neededToCall;
		
		// 广播谁在行动
		await _broadcaster(new ServerMessage
		{
			Type = ServerMessageType.ActionRequest,
			PayLoad = new 
			{ 
				PlayerId = Players.IndexOf(player),
				PlayerName = player.Name,
				NeededToCall = neededToCall,
				CanCheck = canCheck,
				CanCall = canCall,
				CanRaise = canRaise,
				MinRaise = BettingRound.HighestBet + 20, // BigBlindAmount
				PlayerChips = player.Chips
			}
		});
	}
}

public enum GameStage
{
	Waiting,
	PreFlop,
	Flop,
	Turn,
	River,
	Showdown,
	Finished
}