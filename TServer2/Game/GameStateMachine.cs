using TServer2.Logging;
using TServer2.Model;
using TServer2.Protocol;

namespace TServer2.Game;

/// <summary>
/// 游戏状态机 - 管理单局游戏的生命周期
/// </summary>
public class GameStateMachine
{
    private readonly Lock _lock = new();
    private readonly Func<string, ServerMessage, Task> _unicast;
    private readonly Func<ServerMessage, Task> _broadcast;
    
    // 游戏配置
    private const int InitialChips = 1000;
    private const int SmallBlindAmount = 2;
    private const int BigBlindAmount = 4;
    private const int ActionTimeoutSeconds = 20;
    
    // 游戏状态
    public GamePhase Phase { get; private set; } = GamePhase.WaitingForPlayers;
    public List<Player> Players { get; } = [];
    public List<Card> CommunityCards { get; } = [];
    
    // 位置相关
    public int DealerSeatIndex { get; private set; } = -1;
    public int SmallBlindSeatIndex { get; private set; } = -1;
    public int BigBlindSeatIndex { get; private set; } = -1;
    public int CurrentActorIndex { get; private set; } = -1;
    
    // 游戏组件
    private Deck _deck = new();
    private readonly PotManager _potManager = new();
    private readonly BettingRound _bettingRound = new();
    
    // 行动追踪
    private HashSet<string> _playersActedThisRound = [];
    private string? _lastCallerPlayerId; // 用于摊牌时强制亮牌
    
    // 行动超时
    private CancellationTokenSource? _actionTimeoutCts;
    
    // 手牌计数
    private int _handNumber;

    public GameStateMachine(Func<string, ServerMessage, Task> unicast, Func<ServerMessage, Task> broadcast)
    {
        _unicast = unicast;
        _broadcast = broadcast;
    }

    #region Player Management

    /// <summary>
    /// 添加玩家
    /// </summary>
    public (bool Success, string? Error, Player? Player) AddPlayer(string name)
    {
        _lock.Enter();
        try
        {
            if (Phase != GamePhase.WaitingForPlayers && Phase != GamePhase.Countdown)
                return (false, "Game already in progress", null);

            if (Players.Count >= 10)
                return (false, "Room is full", null);

            // 处理空名字
            if (string.IsNullOrWhiteSpace(name))
                name = $"Player{Random.Shared.Next(1000, 10000)}";

            // 检查重名
            if (Players.Any(p => p.Name == name))
                name = $"{name}_{Random.Shared.Next(100, 1000)}";

            var seatIndex = GetNextAvailableSeat();
            var player = new Player(name, InitialChips)
            {
                SeatIndex = seatIndex
            };
            
            Players.Add(player);
            Logger.Info($"Player {name} joined at seat {seatIndex}");
            
            return (true, null, player);
        }
        finally
        {
            _lock.Exit();
        }
    }

    private int GetNextAvailableSeat()
    {
        var usedSeats = Players.Select(p => p.SeatIndex).ToHashSet();
        for (var i = 0; i < 10; i++)
        {
            if (!usedSeats.Contains(i))
                return i;
        }
        return Players.Count;
    }

    /// <summary>
    /// 移除玩家
    /// </summary>
    public void RemovePlayer(string playerId, string reason = "disconnected")
    {
        _lock.Enter();
        try
        {
            var player = Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return;

            player.IsConnected = false;
            
            if (Phase == GamePhase.WaitingForPlayers || Phase == GamePhase.Countdown)
            {
                Players.Remove(player);
                Logger.Info($"Player {player.Name} removed ({reason})");
            }
            else
            {
                // 游戏进行中标记为弃牌
                player.HasFolded = true;
                Logger.Info($"Player {player.Name} disconnected, marked as folded");
            }
        }
        finally
        {
            _lock.Exit();
        }
    }

    /// <summary>
    /// 获取玩家
    /// </summary>
    public Player? GetPlayer(string playerId)
    {
        _lock.Enter();
        try
        {
            return Players.FirstOrDefault(p => p.Id == playerId);
        }
        finally
        {
            _lock.Exit();
        }
    }

    #endregion

    #region Game Flow

    /// <summary>
    /// 开始新的一手牌
    /// </summary>
    public async Task StartNewHandAsync()
    {
        _lock.Enter();
        try
        {
            // 移除筹码为0的玩家
            var bustedPlayers = Players.Where(p => p.Chips <= 0).ToList();
            foreach (var p in bustedPlayers)
            {
                Players.Remove(p);
                Logger.Info($"{p.Name} eliminated (no chips)");
            }

            // 检查游戏是否结束
            if (Players.Count < 4)
            {
                await EndGameAsync();
                return;
            }

            _handNumber++;
            Logger.Info($"=== Starting Hand #{_handNumber} ===");
            
            // 重置游戏状态
            Phase = GamePhase.PreFlop;
            CommunityCards.Clear();
            _potManager.Clear();
            _deck.Reset();
            _playersActedThisRound.Clear();
            _lastCallerPlayerId = null;

            foreach (var player in Players)
                player.ResetForNewHand();

            // 确定位置
            RotatePositions();

            // 发底牌
            DealHoleCards();

            // 下盲注
            await PostBlindsAsync();
        }
        finally
        {
            _lock.Exit();
        }

        // 广播游戏开始
        await BroadcastNewHandAsync();

        // 开始第一轮行动
        await StartBettingRoundAsync();
    }

    private void RotatePositions()
    {
        // 第一手牌随机选庄，之后顺时针轮转
        if (DealerSeatIndex < 0)
        {
            DealerSeatIndex = Players[Random.Shared.Next(Players.Count)].SeatIndex;
        }
        else
        {
            // 找下一个有玩家的座位
            var currentDealerIndex = Players.FindIndex(p => p.SeatIndex == DealerSeatIndex);
            if (currentDealerIndex < 0) currentDealerIndex = 0;
            var nextDealerIndex = (currentDealerIndex + 1) % Players.Count;
            DealerSeatIndex = Players[nextDealerIndex].SeatIndex;
        }

        // 确定小盲和大盲位置
        var dealerIdx = Players.FindIndex(p => p.SeatIndex == DealerSeatIndex);
        
        if (Players.Count == 2)
        {
            // 2人游戏：庄家是小盲
            SmallBlindSeatIndex = DealerSeatIndex;
            BigBlindSeatIndex = Players[(dealerIdx + 1) % Players.Count].SeatIndex;
        }
        else
        {
            SmallBlindSeatIndex = Players[(dealerIdx + 1) % Players.Count].SeatIndex;
            BigBlindSeatIndex = Players[(dealerIdx + 2) % Players.Count].SeatIndex;
        }

        Logger.Info($"Positions - Dealer: Seat {DealerSeatIndex}, SB: Seat {SmallBlindSeatIndex}, BB: Seat {BigBlindSeatIndex}");
    }

    private void DealHoleCards()
    {
        // 从小盲开始顺时针发牌，每人两张
        var sbIdx = Players.FindIndex(p => p.SeatIndex == SmallBlindSeatIndex);
        
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < Players.Count; i++)
            {
                var playerIdx = (sbIdx + i) % Players.Count;
                Players[playerIdx].HoleCards.Add(_deck.Deal());
            }
        }

        foreach (var p in Players)
            Logger.Debug($"Dealt to {p.Name}: {string.Join(", ", p.HoleCards)}");
    }

    private async Task PostBlindsAsync()
    {
        var sbPlayer = Players.First(p => p.SeatIndex == SmallBlindSeatIndex);
        var bbPlayer = Players.First(p => p.SeatIndex == BigBlindSeatIndex);

        sbPlayer.PlaceBet(Math.Min(SmallBlindAmount, sbPlayer.Chips));
        bbPlayer.PlaceBet(Math.Min(BigBlindAmount, bbPlayer.Chips));

        _bettingRound.StartRound(BigBlindAmount, BigBlindAmount);

        Logger.Info($"Blinds: {sbPlayer.Name} posts SB {sbPlayer.CurrentBet}, {bbPlayer.Name} posts BB {bbPlayer.CurrentBet}");

        await _broadcast(new ServerMessage
        {
            Type = ServerMessageType.BlindsPosted,
            Payload = new BlindsPostedPayload
            {
                SmallBlindPlayerId = sbPlayer.Id,
                SmallBlindAmount = sbPlayer.CurrentBet,
                BigBlindPlayerId = bbPlayer.Id,
                BigBlindAmount = bbPlayer.CurrentBet
            }
        });
    }

    private async Task BroadcastNewHandAsync()
    {
        // 广播游戏开始
        await _broadcast(new ServerMessage
        {
            Type = ServerMessageType.NewHandStarted,
            Payload = new NewHandStartedPayload
            {
                HandNumber = _handNumber,
                DealerSeatIndex = DealerSeatIndex,
                SmallBlindSeatIndex = SmallBlindSeatIndex,
                BigBlindSeatIndex = BigBlindSeatIndex,
                Players = Players.Select(p => new PlayerDto(p)).ToList()
            }
        });

        // 向每个玩家发送其手牌
        foreach (var player in Players)
        {
            await _unicast(player.Id, new ServerMessage
            {
                Type = ServerMessageType.HoleCards,
                Payload = new HoleCardsPayload
                {
                    Cards = player.HoleCards.Select(c => new CardDto(c)).ToList()
                }
            });
        }
    }

    private async Task StartBettingRoundAsync()
    {
        _playersActedThisRound.Clear();
        
        // 确定第一个行动者
        DetermineFirstActor();

        // 如果只剩一个可行动玩家，跳过
        var activePlayers = Players.Where(p => p.CanAct).ToList();
        if (activePlayers.Count <= 1)
        {
            // 所有人都All-In了或弃牌了
            await AdvanceToNextPhaseAsync();
            return;
        }

        await RequestActionAsync();
    }

    private void DetermineFirstActor()
    {
        if (Phase == GamePhase.PreFlop)
        {
            // PreFlop: 从大盲后一位开始（UTG）
            var bbIdx = Players.FindIndex(p => p.SeatIndex == BigBlindSeatIndex);
            CurrentActorIndex = (bbIdx + 1) % Players.Count;
            
            // 2人游戏：小盲先行动
            if (Players.Count == 2)
            {
                var sbIdx = Players.FindIndex(p => p.SeatIndex == SmallBlindSeatIndex);
                CurrentActorIndex = sbIdx;
            }
        }
        else
        {
            // Flop/Turn/River: 从庄家后第一个未弃牌玩家开始
            var dealerIdx = Players.FindIndex(p => p.SeatIndex == DealerSeatIndex);
            for (var i = 1; i <= Players.Count; i++)
            {
                var idx = (dealerIdx + i) % Players.Count;
                if (!Players[idx].HasFolded)
                {
                    CurrentActorIndex = idx;
                    break;
                }
            }
        }

        // 确保找到可行动的玩家
        SkipToNextActivePlayer();
    }

    private void SkipToNextActivePlayer()
    {
        var count = 0;
        while (!Players[CurrentActorIndex].CanAct && count < Players.Count)
        {
            CurrentActorIndex = (CurrentActorIndex + 1) % Players.Count;
            count++;
        }
    }

    private async Task RequestActionAsync()
    {
        var player = Players[CurrentActorIndex];
        
        if (!player.CanAct)
        {
            // 跳过不能行动的玩家
            await MoveToNextPlayerAsync();
            return;
        }

        var actions = _bettingRound.GetAvailableActions(player);
        var callAmount = _bettingRound.GetCallAmount(player);

        var request = new ActionRequestPayload
        {
            PlayerId = player.Id,
            AvailableActions = actions,
            TimeoutSeconds = ActionTimeoutSeconds,
            CurrentBet = _bettingRound.CurrentBet,
            CallAmount = callAmount,
            MinRaise = _bettingRound.MinRaise,
            PlayerChips = player.Chips,
            Pots = _potManager.Pots.Select(p => new PotDto(p)).ToList()
        };

        Logger.Info($"Requesting action from {player.Name}. Available: {string.Join(", ", actions.Select(a => a.Type))}");

        await _unicast(player.Id, new ServerMessage
        {
            Type = ServerMessageType.ActionRequest,
            Payload = request
        });

        // 启动超时计时器
        StartActionTimeout(player.Id);
    }

    private void StartActionTimeout(string playerId)
    {
        _actionTimeoutCts?.Cancel();
        _actionTimeoutCts = new CancellationTokenSource();
        
        var cts = _actionTimeoutCts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ActionTimeoutSeconds), cts.Token);
                
                // 超时自动弃牌
                Logger.Warn($"Player {playerId} timed out, auto-folding");
                await HandlePlayerActionAsync(playerId, ActionType.Fold, 0);
            }
            catch (TaskCanceledException)
            {
                // 正常取消
            }
        });
    }

    /// <summary>
    /// 处理玩家行动
    /// </summary>
    public async Task HandlePlayerActionAsync(string playerId, ActionType action, int amount)
    {
        _actionTimeoutCts?.Cancel();

        Player? player;
        bool shouldAdvance;
        bool singleWinner;

        _lock.Enter();
        try
        {
            if (Phase is GamePhase.WaitingForPlayers or GamePhase.Countdown or GamePhase.GameOver)
            {
                await SendErrorAsync(playerId, "Game not in progress");
                return;
            }

            player = Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                await SendErrorAsync(playerId, "Player not found");
                return;
            }

            if (Players[CurrentActorIndex].Id != playerId)
            {
                await SendErrorAsync(playerId, "Not your turn");
                return;
            }

            // 执行行动
            var (success, error) = _bettingRound.HandleAction(player, action, amount);
            if (!success)
            {
                await SendErrorAsync(playerId, error ?? "Invalid action");
                return;
            }

            // 记录跟注者（用于摊牌）
            if (action is ActionType.Call or ActionType.AllIn)
                _lastCallerPlayerId = playerId;
            
            // 如果是加注/下注，重新打开行动（清除除了加注者外的已行动标记）
            if (action is ActionType.Raise or ActionType.Bet || 
                (action == ActionType.AllIn && player.CurrentBet > _bettingRound.CurrentBet))
            {
                _playersActedThisRound.RemoveWhere(id => id != playerId);
            }

            _playersActedThisRound.Add(playerId);

            // 检查是否只剩一人
            var remaining = Players.Count(p => !p.HasFolded);
            singleWinner = remaining == 1;
            shouldAdvance = !singleWinner && IsBettingRoundComplete();
        }
        finally
        {
            _lock.Exit();
        }

        // 广播行动
        await _broadcast(new ServerMessage
        {
            Type = ServerMessageType.PlayerActed,
            Payload = new PlayerActedPayload
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Action = action,
                Amount = action switch
                {
                    ActionType.Fold or ActionType.Check => 0,
                    ActionType.Call => _bettingRound.GetCallAmount(player),
                    _ => amount
                },
                PlayerChipsRemaining = player.Chips,
                TotalPot = _potManager.TotalPot + Players.Sum(p => p.CurrentBet)
            }
        });

        if (singleWinner)
        {
            await HandleSingleWinnerAsync();
        }
        else if (shouldAdvance)
        {
            await AdvanceToNextPhaseAsync();
        }
        else
        {
            await MoveToNextPlayerAsync();
        }
    }

    private bool IsBettingRoundComplete()
    {
        var activePlayers = Players.Where(p => p.CanAct).ToList();
        
        // 所有活跃玩家都已行动
        if (!activePlayers.All(p => _playersActedThisRound.Contains(p.Id)))
            return false;

        // 所有活跃玩家的下注都相等
        var bets = activePlayers.Select(p => p.CurrentBet).Distinct().ToList();
        if (bets.Count > 1)
            return false;

        // 如果有All-In玩家，检查是否有人未匹配
        var maxBet = Players.Where(p => !p.HasFolded).Max(p => p.CurrentBet);
        if (activePlayers.Any(p => p.CurrentBet < maxBet))
            return false;

        return true;
    }

    private async Task MoveToNextPlayerAsync()
    {
        _lock.Enter();
        try
        {
            var startIdx = CurrentActorIndex;
            do
            {
                CurrentActorIndex = (CurrentActorIndex + 1) % Players.Count;
            } while (!Players[CurrentActorIndex].CanAct && CurrentActorIndex != startIdx);
        }
        finally
        {
            _lock.Exit();
        }

        await RequestActionAsync();
    }

    private async Task AdvanceToNextPhaseAsync()
    {
        // 收集下注到底池
        _lock.Enter();
        try
        {
            _potManager.CollectBets(Players);
            foreach (var p in Players)
                p.ResetBetForNewRound();
        }
        finally
        {
            _lock.Exit();
        }

        // 确定下一阶段
        var nextPhase = Phase switch
        {
            GamePhase.PreFlop => GamePhase.Flop,
            GamePhase.Flop => GamePhase.Turn,
            GamePhase.Turn => GamePhase.River,
            GamePhase.River => GamePhase.Showdown,
            _ => GamePhase.Showdown
        };

        _lock.Enter();
        try
        {
            Phase = nextPhase;
        }
        finally
        {
            _lock.Exit();
        }

        Logger.Info($"=== Phase: {nextPhase} ===");

        if (nextPhase == GamePhase.Showdown)
        {
            await HandleShowdownAsync();
        }
        else
        {
            await DealCommunityCardsAsync();
        }
    }

    private async Task DealCommunityCardsAsync()
    {
        _lock.Enter();
        try
        {
            // 烧牌
            _deck.Burn();

            // 发公共牌
            var cardCount = Phase switch
            {
                GamePhase.Flop => 3,
                _ => 1
            };

            for (var i = 0; i < cardCount; i++)
            {
                var card = _deck.Deal();
                CommunityCards.Add(card);
            }
        }
        finally
        {
            _lock.Exit();
        }

        Logger.Info($"Community cards: {string.Join(", ", CommunityCards)}");

        // 广播公共牌
        await _broadcast(new ServerMessage
        {
            Type = ServerMessageType.PhaseChanged,
            Payload = new PhaseChangedPayload
            {
                Phase = Phase.ToString(),
                CommunityCards = CommunityCards.Select(c => new CardDto(c)).ToList(),
                Pots = _potManager.Pots.Select(p => new PotDto(p)).ToList()
            }
        });

        // 重置下注轮
        _bettingRound.StartRound(BigBlindAmount, 0);
        await StartBettingRoundAsync();
    }

    private async Task HandleSingleWinnerAsync()
    {
        Player? winner;
        int amount;
        
        _lock.Enter();
        try
        {
            _potManager.CollectBets(Players);
            winner = Players.FirstOrDefault(p => !p.HasFolded);
            if (winner == null) return;
            
            amount = _potManager.DistributeToSingleWinner(winner);
            Phase = GamePhase.Settlement;
        }
        finally
        {
            _lock.Exit();
        }

        await _broadcast(new ServerMessage
        {
            Type = ServerMessageType.PotDistribution,
            Payload = new PotDistributionPayload
            {
                Winners =
                [
                    new PotWinner
                    {
                        PotName = "All Pots",
                        PotAmount = amount,
                        Winners =
                        [
                            new WinnerInfo
                            {
                                PlayerId = winner.Id,
                                PlayerName = winner.Name,
                                AmountWon = amount,
                                HandRank = "Others Folded"
                            }
                        ]
                    }
                ]
            }
        });

        await EndHandAsync();
    }

    private async Task HandleShowdownAsync()
    {
        // 收集下注
        _lock.Enter();
        try
        {
            _potManager.CollectBets(Players);
        }
        finally
        {
            _lock.Exit();
        }

        // 处理亮牌流程
        await ProcessShowdownAsync();
    }

    private async Task ProcessShowdownAsync()
    {
        var playersInHand = Players.Where(p => !p.HasFolded).ToList();
        
        // 最后跟注者必须先亮牌
        var firstToShow = playersInHand.FirstOrDefault(p => p.Id == _lastCallerPlayerId) 
                          ?? playersInHand.First();
        
        // 按顺序请求亮牌或盖牌
        var showOrder = new List<Player>();
        var startIdx = Players.IndexOf(firstToShow);
        for (var i = 0; i < Players.Count; i++)
        {
            var idx = (startIdx + i) % Players.Count;
            if (!Players[idx].HasFolded)
                showOrder.Add(Players[idx]);
        }

        // 评估所有玩家手牌
        var evaluations = new Dictionary<string, HandEvaluation>();
        foreach (var p in playersInHand)
        {
            var eval = HandEvaluator.Evaluate(p.Id, p.HoleCards, CommunityCards);
            evaluations[p.Id] = eval;
        }

        // 按牌力排序
        var rankings = evaluations.Values
            .GroupBy(e => (e.Rank, string.Join(",", e.Kickers)))
            .OrderByDescending(g => g.First().Rank)
            .ThenByDescending(g => g.First(), Comparer<HandEvaluation>.Create(
                (a, b) => HandEvaluator.CompareKickers(a.Kickers, b.Kickers)))
            .Select(g => g.Select(e => e.PlayerId).ToList())
            .ToList();

        // 广播亮牌
        foreach (var player in showOrder)
        {
            var isFirst = player == firstToShow;
            var eval = evaluations[player.Id];

            await _broadcast(new ServerMessage
            {
                Type = ServerMessageType.PlayerShowedCards,
                Payload = new PlayerShowedCardsPayload
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    Cards = player.HoleCards.Select(c => new CardDto(c)).ToList(),
                    HandEvaluation = new HandEvaluationDto(eval),
                    Mucked = false
                }
            });

            await Task.Delay(500); // 给客户端时间显示
        }

        // 分配底池
        var sbPlayer = Players.First(p => p.SeatIndex == SmallBlindSeatIndex);
        var winnings = _potManager.Distribute(rankings, Players, sbPlayer.Id);

        // 广播结果
        var potWinners = new List<PotWinner>();
        foreach (var pot in _potManager.Pots)
        {
            var potWinner = new PotWinner
            {
                PotName = pot.Name,
                PotAmount = pot.Amount,
                Winners = []
            };

            foreach (var group in rankings)
            {
                var winners = group.Where(id => pot.EligiblePlayerIds.Contains(id)).ToList();
                if (winners.Count > 0)
                {
                    foreach (var winnerId in winners)
                    {
                        var player = Players.First(p => p.Id == winnerId);
                        var eval = evaluations[winnerId];
                        potWinner.Winners.Add(new WinnerInfo
                        {
                            PlayerId = winnerId,
                            PlayerName = player.Name,
                            AmountWon = winnings.GetValueOrDefault(winnerId, 0) / _potManager.Pots.Count(p => 
                                p.EligiblePlayerIds.Contains(winnerId)),
                            HandRank = eval.Rank.ToString()
                        });
                    }
                    break;
                }
            }

            potWinners.Add(potWinner);
        }

        await _broadcast(new ServerMessage
        {
            Type = ServerMessageType.PotDistribution,
            Payload = new PotDistributionPayload { Winners = potWinners }
        });

        await EndHandAsync();
    }

    private async Task EndHandAsync()
    {
        _lock.Enter();
        try
        {
            Phase = GamePhase.Settlement;
        }
        finally
        {
            _lock.Exit();
        }

        await _broadcast(new ServerMessage
        {
            Type = ServerMessageType.HandEnded,
            Payload = new HandEndedPayload
            {
                Players = Players.Select(p => new PlayerDto(p)).ToList(),
                NextDealerSeatIndex = DealerSeatIndex
            }
        });

        Logger.Info($"=== Hand #{_handNumber} ended ===");

        // 等待几秒后开始下一手
        await Task.Delay(3000);
        await StartNewHandAsync();
    }

    private async Task EndGameAsync()
    {
        _lock.Enter();
        try
        {
            Phase = GamePhase.GameOver;
        }
        finally
        {
            _lock.Exit();
        }

        // 生成排行榜
        var rankings = Players.OrderByDescending(p => p.Chips)
            .Select((p, i) => new RankingEntry
            {
                Rank = i + 1,
                PlayerId = p.Id,
                PlayerName = p.Name,
                FinalChips = p.Chips
            })
            .ToList();

        Logger.Info("=== GAME OVER ===");
        foreach (var r in rankings)
            Logger.Info($"#{r.Rank} {r.PlayerName}: {r.FinalChips} chips");

        await _broadcast(new ServerMessage
        {
            Type = ServerMessageType.GameOver,
            Payload = new GameOverPayload
            {
                Rankings = rankings,
                Reason = $"Less than 4 players remaining ({Players.Count})"
            }
        });
    }

    private async Task SendErrorAsync(string playerId, string message)
    {
        await _unicast(playerId, new ServerMessage
        {
            Type = ServerMessageType.Error,
            Payload = new ErrorPayload { Message = message }
        });
    }

    #endregion

    #region State Queries

    /// <summary>
    /// 获取当前游戏状态
    /// </summary>
    public GameStatePayload GetGameState()
    {
        _lock.Enter();
        try
        {
            return new GameStatePayload
            {
                Phase = Phase.ToString(),
                Players = Players.Select(p => new PlayerDto(p)).ToList(),
                CommunityCards = CommunityCards.Select(c => new CardDto(c)).ToList(),
                Pots = _potManager.Pots.Select(p => new PotDto(p)).ToList(),
                DealerSeatIndex = DealerSeatIndex,
                CurrentActingPlayerId = CurrentActorIndex >= 0 && CurrentActorIndex < Players.Count 
                    ? Players[CurrentActorIndex].Id 
                    : null
            };
        }
        finally
        {
            _lock.Exit();
        }
    }

    #endregion
}
