using System.Text.Json;
using System.Text.Json.Serialization;
using TClient.Model;
using TClient.Network;
using TClient.Protocol;

namespace TClient.Game;

/// <summary>
/// 游戏会话 - 管理单个玩家的游戏连接
/// </summary>
public class GameSession : IAsyncDisposable
{
    private TcpGameClient? _network;
    private readonly GameState _state = new();
    private readonly Lock _stateLock = new();
    private readonly List<LogEntry> _logs = [];
    private const int MaxLogs = 20;

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public bool IsConnected { get; private set; }
    public event Action<string>? OnStateChanged;

    public async Task<bool> ConnectAsync(string playerName, string? host = null)
    {
        host ??= GetConnectionInfo();
        _network = new TcpGameClient(host);
        RegisterNetworkEvents();

        AddLog($"正在连接到 {host}:5000...", "yellow");

        if (!await _network.ConnectAsync())
        {
            AddLog("连接服务器失败！", "red");
            return false;
        }

        await _network.JoinRoomAsync(playerName);
        lock (_stateLock)
        {
            _state.MyPlayerName = playerName;
        }

        AddLog($"已加入房间，玩家名: {playerName}", "green");
        AddLog("等待游戏开始 (需要4名玩家)...", "yellow");
        IsConnected = true;
        return true;
    }

    private static string GetConnectionInfo()
    {
        string[] hosts = ["127.0.0.1", "www.halfcooler.cn", "mc.halfcooler.cn"];
        const int port = 5000;

        foreach (var host in hosts)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                if (client.ConnectAsync(host, port).Wait(300))
                {
                    return host;
                }
            }
            catch
            {
                // 忽略错误并尝试下一个
            }
        }

        return "www.halfcooler.cn"; // 默认返回
    }

    private void RegisterNetworkEvents()
    {
        if (_network == null) return;

        _network.OnConnected += async () =>
        {
            AddLog("已连接到服务器", "green");
            NotifyStateChanged();
            await Task.CompletedTask;
        };

        _network.OnDisconnected += async () =>
        {
            AddLog("与服务器断开连接", "red");
            IsConnected = false;
            NotifyStateChanged();
            await Task.CompletedTask;
        };

        _network.OnError += async error =>
        {
            AddLog($"错误: {error}", "red");
            NotifyStateChanged();
            await Task.CompletedTask;
        };

        _network.OnMessageReceived += ProcessServerMessageAsync;
    }

    private void AddLog(string message, string style = "grey")
    {
        lock (_stateLock)
        {
            _logs.Add(new LogEntry(DateTime.Now, message, style));
            while (_logs.Count > MaxLogs)
                _logs.RemoveAt(0);
        }
    }

    private void NotifyStateChanged()
    {
        OnStateChanged?.Invoke(SessionId);
    }

    public async Task SendActionAsync(ActionType action, int amount = 0)
    {
        if (_network == null) return;
        await _network.SendActionAsync(action, amount);

        var actionText = action switch
        {
            ActionType.Fold => "弃牌",
            ActionType.Check => "过牌",
            ActionType.Call => $"跟注 ${amount}",
            ActionType.Bet => $"下注 ${amount}",
            ActionType.Raise => $"加注 ${amount}",
            ActionType.AllIn => $"全下 ${amount}",
            _ => action.ToString()
        };
        AddLog($"你{actionText}了", GetActionStyle(action));

        lock (_stateLock)
        {
            _state.IsMyTurn = false;
        }
        NotifyStateChanged();
    }

    public async Task ShowCardsAsync()
    {
        if (_network == null) return;
        await _network.ShowCardsAsync();
        AddLog("你选择了亮牌", "cyan");
        lock (_stateLock)
        {
            _state.IsShowdownRequest = false;
        }
        NotifyStateChanged();
    }

    public async Task MuckCardsAsync()
    {
        if (_network == null) return;
        await _network.MuckCardsAsync();
        AddLog("你选择了盖牌");
        lock (_stateLock)
        {
            _state.IsShowdownRequest = false;
        }
        NotifyStateChanged();
    }

    private static string GetActionStyle(ActionType action)
    {
        return action switch
        {
            ActionType.Fold => "red",
            ActionType.Check => "cyan",
            ActionType.Call => "green",
            ActionType.Bet => "yellow",
            ActionType.Raise => "yellow",
            ActionType.AllIn => "red",
            _ => "white"
        };
    }

    public GameStateDto GetState()
    {
        lock (_stateLock)
        {
            return new GameStateDto
            {
                Phase = _state.Phase,
                HandNumber = _state.HandNumber,
                MyPlayerId = _state.MyPlayerId,
                MyPlayerName = _state.MyPlayerName,
                MySeatIndex = _state.MySeatIndex,
                MyChips = _state.MyChips,
                MyHand = _state.MyHand.Select(c => new CardViewDto
                {
                    Display = c.Display,
                    Suit = c.Suit.ToString(),
                    Rank = c.Rank.ToString(),
                    SuitSymbol = c.SuitSymbol,
                    RankSymbol = c.RankSymbol,
                    IsRed = c.IsRed
                }).ToList(),
                Players = _state.Players.OrderBy(p => p.SeatIndex).Select(p => new PlayerViewDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    SeatIndex = p.SeatIndex,
                    Chips = p.Chips,
                    CurrentBet = p.CurrentBet,
                    HasFolded = p.HasFolded,
                    IsAllIn = p.IsAllIn,
                    IsConnected = p.IsConnected,
                    IsActing = p.Id == _state.CurrentActingPlayerId,
                    IsMe = p.Id == _state.MyPlayerId,
                    IsDealer = p.SeatIndex == _state.DealerSeatIndex,
                    IsSmallBlind = p.SeatIndex == _state.SmallBlindSeatIndex,
                    IsBigBlind = p.SeatIndex == _state.BigBlindSeatIndex,
                    ShownCards = p.ShownCards?.Select(c => new CardViewDto
                    {
                        Display = c.Display,
                        Suit = c.Suit.ToString(),
                        Rank = c.Rank.ToString(),
                        SuitSymbol = c.SuitSymbol,
                        RankSymbol = c.RankSymbol,
                        IsRed = c.IsRed
                    }).ToList(),
                    HandRank = p.HandRank
                }).ToList(),
                CommunityCards = _state.CommunityCards.Select(c => new CardViewDto
                {
                    Display = c.Display,
                    Suit = c.Suit.ToString(),
                    Rank = c.Rank.ToString(),
                    SuitSymbol = c.SuitSymbol,
                    RankSymbol = c.RankSymbol,
                    IsRed = c.IsRed
                }).ToList(),
                Pots = _state.Pots.Select(p => new PotViewDto { Name = p.Name, Amount = p.Amount }).ToList(),
                TotalPot = _state.Pots.Sum(p => p.Amount),
                DealerSeatIndex = _state.DealerSeatIndex,
                SmallBlindSeatIndex = _state.SmallBlindSeatIndex,
                BigBlindSeatIndex = _state.BigBlindSeatIndex,
                IsMyTurn = _state.IsMyTurn,
                CurrentBet = _state.CurrentBet,
                CallAmount = _state.CallAmount,
                MinRaise = _state.MinRaise,
                ActionTimeout = _state.ActionTimeout,
                AvailableActions = _state.AvailableActions.Select(a => new ActionViewDto
                {
                    Type = a.Type,
                    MinAmount = a.MinAmount,
                    MaxAmount = a.MaxAmount,
                    Description = a.Description,
                    Key = GetActionKey(a.Type),
                    DisplayText = GetActionDisplayText(a)
                }).ToList(),
                IsShowdownRequest = _state.IsShowdownRequest,
                MustShowCards = _state.MustShowCards,
                CountdownSeconds = _state.CountdownSeconds,
                IsCountingDown = _state.IsCountingDown,
                Logs = GetLogs()
            };
        }
    }

    public List<LogViewDto> GetLogs()
    {
        lock (_stateLock)
        {
            return _logs.Select(l => new LogViewDto
            {
                Time = l.Time.ToString("HH:mm:ss"),
                Message = l.Message,
                Style = l.Style
            }).ToList();
        }
    }

    private static string GetActionKey(string actionType)
    {
        return actionType.ToLower() switch
        {
            "fold" => "F",
            "check" => "K",
            "call" => "C",
            "bet" => "B",
            "raise" => "R",
            "allin" => "A",
            _ => "?"
        };
    }

    private static string GetActionDisplayText(AvailableActionInfo action)
    {
        return action.Type.ToLower() switch
        {
            "fold" => "弃牌",
            "check" => "过牌",
            "call" => $"跟注 (${action.MinAmount ?? 0})",
            "bet" => $"下注 (${action.MinAmount ?? 0}-${action.MaxAmount ?? 0})",
            "raise" => $"加注 (${action.MinAmount ?? 0}-${action.MaxAmount ?? 0})",
            "allin" => $"全下 (${action.MaxAmount ?? 0})",
            _ => action.Description
        };
    }

    #region Message Handlers

    private async Task ProcessServerMessageAsync(ServerMessage message)
    {
        switch (message.Type)
        {
            case ServerMessageType.JoinSuccess:
                HandleJoinSuccess(message.Payload);
                break;
            case ServerMessageType.PlayerJoined:
                HandlePlayerJoined(message.Payload);
                break;
            case ServerMessageType.PlayerLeft:
                HandlePlayerLeft(message.Payload);
                break;
            case ServerMessageType.CountdownStarted:
                HandleCountdownStarted(message.Payload);
                break;
            case ServerMessageType.CountdownUpdate:
                HandleCountdownUpdate(message.Payload);
                break;
            case ServerMessageType.GameStarted:
                HandleGameStarted(message.Payload);
                break;
            case ServerMessageType.HoleCards:
                HandleHoleCards(message.Payload);
                break;
            case ServerMessageType.NewHandStarted:
                HandleNewHandStarted(message.Payload);
                break;
            case ServerMessageType.BlindsPosted:
                HandleBlindsPosted(message.Payload);
                break;
            case ServerMessageType.ActionRequest:
                HandleActionRequest(message.Payload);
                break;
            case ServerMessageType.PlayerActed:
                HandlePlayerActed(message.Payload);
                break;
            case ServerMessageType.PhaseChanged:
                HandlePhaseChanged(message.Payload);
                break;
            case ServerMessageType.CommunityCards:
                HandleCommunityCards(message.Payload);
                break;
            case ServerMessageType.ShowdownRequest:
                HandleShowdownRequest(message.Payload);
                break;
            case ServerMessageType.PlayerShowedCards:
                HandlePlayerShowedCards(message.Payload);
                break;
            case ServerMessageType.PotDistribution:
                HandlePotDistribution(message.Payload);
                break;
            case ServerMessageType.HandEnded:
                HandleHandEnded(message.Payload);
                break;
            case ServerMessageType.GameOver:
                HandleGameOver(message.Payload);
                break;
            case ServerMessageType.GameState:
                HandleGameState(message.Payload);
                break;
            case ServerMessageType.Error:
                HandleError(message.Payload);
                break;
            case ServerMessageType.Heartbeat:
            default:
                break;
        }

        NotifyStateChanged();
        await Task.CompletedTask;
    }

    private void HandleJoinSuccess(object? payload)
    {
        var data = DeserializePayload<JoinSuccessPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.MyPlayerId = data.PlayerId;
            _state.MyPlayerName = data.PlayerName;
            _state.MySeatIndex = data.SeatIndex;
            _state.MyChips = data.Chips;

            _state.Players.Clear();
            foreach (var p in data.ExistingPlayers)
            {
                _state.Players.Add(ConvertToPlayerInfo(p));
            }
            _state.Players.Add(new PlayerInfo
            {
                Id = data.PlayerId,
                Name = data.PlayerName,
                SeatIndex = data.SeatIndex,
                Chips = data.Chips
            });
        }

        AddLog($"加入成功！座位 {data.SeatIndex}，筹码 ${data.Chips}", "green");
    }

    private void HandlePlayerJoined(object? payload)
    {
        var data = DeserializePayload<PlayerJoinedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            var existing = _state.Players.FirstOrDefault(p => p.Id == data.Player.Id);
            if (existing != null)
                _state.Players.Remove(existing);
            
            _state.Players.Add(ConvertToPlayerInfo(data.Player));
        }

        AddLog($"{data.Player.Name} 加入了游戏 ({data.CurrentPlayerCount}/{data.MaxPlayers})", "cyan");
    }

    private void HandlePlayerLeft(object? payload)
    {
        var data = DeserializePayload<PlayerLeftPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.Players.RemoveAll(p => p.Id == data.PlayerId);
        }

        AddLog($"{data.PlayerName} 离开了游戏 ({data.Reason})", "yellow");
    }

    private void HandleCountdownStarted(object? payload)
    {
        var data = DeserializePayload<CountdownStartedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.IsCountingDown = true;
            _state.CountdownSeconds = data.Seconds;
            _state.Phase = "Countdown";
        }

        AddLog($"游戏即将开始！倒计时 {data.Seconds} 秒...", "yellow");
    }

    private void HandleCountdownUpdate(object? payload)
    {
        var data = DeserializePayload<CountdownUpdatePayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.CountdownSeconds = data.SecondsRemaining;
        }
    }

    private void HandleGameStarted(object? payload)
    {
        var data = DeserializePayload<GameStartedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.IsCountingDown = false;
            _state.Phase = "PreFlop";
            _state.DealerSeatIndex = data.DealerSeatIndex;
            _state.SmallBlindSeatIndex = data.SmallBlindSeatIndex;
            _state.BigBlindSeatIndex = data.BigBlindSeatIndex;

            _state.Players.Clear();
            foreach (var p in data.Players)
            {
                _state.Players.Add(ConvertToPlayerInfo(p));
            }
        }

        AddLog("🎮 游戏开始！", "green");
    }

    private void HandleHoleCards(object? payload)
    {
        var data = DeserializePayload<HoleCardsPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.MyHand.Clear();
            foreach (var c in data.Cards)
            {
                _state.MyHand.Add(ConvertToCard(c));
            }
        }

        var cards = string.Join(" ", data.Cards.Select(c => $"{GetSuitSymbol(c.Suit)}{GetRankSymbol(c.Rank)}"));
        AddLog($"🎴 你的手牌: {cards}", "cyan");
    }

    private void HandleNewHandStarted(object? payload)
    {
        var data = DeserializePayload<NewHandStartedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.HandNumber = data.HandNumber;
            _state.Phase = "PreFlop";
            _state.DealerSeatIndex = data.DealerSeatIndex;
            _state.SmallBlindSeatIndex = data.SmallBlindSeatIndex;
            _state.BigBlindSeatIndex = data.BigBlindSeatIndex;
            _state.CommunityCards.Clear();
            _state.Pots.Clear();
            _state.MyHand.Clear();
            _state.CurrentBet = 0;
            _state.IsMyTurn = false;

            _state.Players.Clear();
            foreach (var p in data.Players)
            {
                var info = ConvertToPlayerInfo(p);
                _state.Players.Add(info);
                if (p.Id == _state.MyPlayerId)
                    _state.MyChips = p.Chips;
            }
        }

        AddLog($"━━━ 第 {data.HandNumber} 手开始 ━━━", "yellow");
    }

    private void HandleBlindsPosted(object? payload)
    {
        var data = DeserializePayload<BlindsPostedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.CurrentBet = data.BigBlindAmount;

            var sb = _state.Players.FirstOrDefault(p => p.Id == data.SmallBlindPlayerId);
            sb?.CurrentBet = data.SmallBlindAmount;

            var bb = _state.Players.FirstOrDefault(p => p.Id == data.BigBlindPlayerId);
            bb?.CurrentBet = data.BigBlindAmount;
        }

        AddLog($"盲注已下: SB ${data.SmallBlindAmount}, BB ${data.BigBlindAmount}");
    }

    private void HandleActionRequest(object? payload)
    {
        var data = DeserializePayload<ActionRequestPayload>(payload);
        if (data == null) return;

        string myId;
        lock (_stateLock)
        {
            myId = _state.MyPlayerId;
        }

        if (data.PlayerId != myId) return;

        lock (_stateLock)
        {
            _state.IsMyTurn = true;
            _state.CurrentBet = data.CurrentBet;
            _state.CallAmount = data.CallAmount;
            _state.MinRaise = data.MinRaise;
            _state.MyChips = data.PlayerChips;
            _state.ActionTimeout = data.TimeoutSeconds;

            _state.AvailableActions.Clear();
            foreach (var a in data.AvailableActions)
            {
                _state.AvailableActions.Add(new AvailableActionInfo
                {
                    Type = a.Type.ToString(),
                    MinAmount = a.MinAmount,
                    MaxAmount = a.MaxAmount,
                    Description = a.Description
                });
            }

            _state.Pots.Clear();
            foreach (var pot in data.Pots)
            {
                _state.Pots.Add(new PotInfo { Name = pot.Name, Amount = pot.Amount });
            }
        }

        AddLog($">>> 轮到你了！({data.TimeoutSeconds}秒)", "green");
    }

    private void HandlePlayerActed(object? payload)
    {
        var data = DeserializePayload<PlayerActedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            var player = _state.Players.FirstOrDefault(p => p.Id == data.PlayerId);
            if (player != null)
            {
                player.Chips = data.PlayerChipsRemaining;
                player.CurrentBet += data.Amount;

                switch (data.Action)
                {
                    case ActionType.Fold:
                        player.HasFolded = true;
                        break;
                    case ActionType.AllIn:
                        player.IsAllIn = true;
                        break;
                }
            }

            if (data.Amount > 0 && player?.CurrentBet > _state.CurrentBet)
                _state.CurrentBet = player.CurrentBet;

            _state.CurrentActingPlayerId = null;
        }

        var actionText = data.Action switch
        {
            ActionType.Fold => "弃牌",
            ActionType.Check => "过牌",
            ActionType.Call => $"跟注 ${data.Amount}",
            ActionType.Bet => $"下注 ${data.Amount}",
            ActionType.Raise => $"加注 ${data.Amount}",
            ActionType.AllIn => $"全下 ${data.Amount}",
            _ => data.Action.ToString()
        };

        AddLog($"{data.PlayerName}: {actionText}", "white");
    }

    private void HandlePhaseChanged(object? payload)
    {
        var data = DeserializePayload<PhaseChangedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.Phase = data.Phase;
            _state.CurrentBet = 0;

            foreach (var p in _state.Players)
                p.CurrentBet = 0;

            _state.CommunityCards.Clear();
            foreach (var c in data.CommunityCards)
            {
                _state.CommunityCards.Add(ConvertToCard(c));
            }

            _state.Pots.Clear();
            foreach (var pot in data.Pots)
            {
                _state.Pots.Add(new PotInfo { Name = pot.Name, Amount = pot.Amount });
            }
        }

        var phaseName = data.Phase switch
        {
            "Flop" => "🃏 翻牌",
            "Turn" => "🃏 转牌",
            "River" => "🃏 河牌",
            "Showdown" => "🎭 摊牌",
            _ => data.Phase
        };

        AddLog($"━━━ {phaseName} ━━━", "cyan");
    }

    private void HandleCommunityCards(object? payload)
    {
        var data = DeserializePayload<CommunityCardsPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.CommunityCards.Clear();
            foreach (var c in data.AllCards)
            {
                _state.CommunityCards.Add(ConvertToCard(c));
            }
        }

        var newCards = string.Join(" ", data.NewCards.Select(c => $"{GetSuitSymbol(c.Suit)}{GetRankSymbol(c.Rank)}"));
        AddLog($"新发公共牌: {newCards}", "cyan");
    }

    private void HandleShowdownRequest(object? payload)
    {
        var data = DeserializePayload<ShowdownRequestPayload>(payload);
        if (data == null) return;

        string myId;
        lock (_stateLock)
        {
            myId = _state.MyPlayerId;
        }

        if (data.PlayerId != myId) return;

        lock (_stateLock)
        {
            _state.IsShowdownRequest = true;
            _state.MustShowCards = data.MustShow;
        }

        var msg = data.MustShow ? "你必须亮牌！" : "选择亮牌或盖牌";
        AddLog($"🎭 摊牌时间！{msg}", "cyan");
    }

    private void HandlePlayerShowedCards(object? payload)
    {
        var data = DeserializePayload<PlayerShowedCardsPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            var player = _state.Players.FirstOrDefault(p => p.Id == data.PlayerId);
            if (player != null)
            {
                if (data is { Mucked: false, Cards.Count: > 0 })
                {
                    player.ShownCards = data.Cards.Select(ConvertToCard).ToList();
                    player.HandRank = data.HandEvaluation?.Rank;
                }
            }
        }

        if (data.Mucked)
        {
            AddLog($"{data.PlayerName} 选择盖牌");
        }
        else
        {
            var cards = string.Join(" ", data.Cards.Select(c => $"{GetSuitSymbol(c.Suit)}{GetRankSymbol(c.Rank)}"));
            var rank = data.HandEvaluation?.Rank ?? "";
            AddLog($"{data.PlayerName} 亮牌: {cards} [{rank}]", "cyan");
        }
    }

    private void HandlePotDistribution(object? payload)
    {
        var data = DeserializePayload<PotDistributionPayload>(payload);
        if (data == null) return;

        foreach (var pot in data.Winners)
        {
            foreach (var winner in pot.Winners)
            {
                string myId;
                lock (_stateLock)
                {
                    myId = _state.MyPlayerId;
                }

                var style = winner.PlayerId == myId ? "green" : "yellow";
                AddLog($"🏆 {winner.PlayerName} 赢得 {pot.PotName} ${winner.AmountWon} [{winner.HandRank}]", style);

                if (winner.PlayerId != myId) continue;
                lock (_stateLock)
                {
                    _state.MyChips += winner.AmountWon;
                }
            }
        }
    }

    private void HandleHandEnded(object? payload)
    {
        var data = DeserializePayload<HandEndedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.Phase = "Waiting";
            _state.IsMyTurn = false;
            _state.IsShowdownRequest = false;

            foreach (var p in data.Players)
            {
                var player = _state.Players.FirstOrDefault(x => x.Id == p.Id);
                if (player != null)
                {
                    player.Chips = p.Chips;
                    player.CurrentBet = 0;
                    player.HasFolded = false;
                    player.IsAllIn = false;
                    player.ShownCards = null;
                    player.HandRank = null;
                }

                if (p.Id == _state.MyPlayerId)
                    _state.MyChips = p.Chips;
            }
        }

        AddLog("这一手结束了");
    }

    private void HandleGameOver(object? payload)
    {
        var data = DeserializePayload<GameOverPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.Phase = "GameOver";
        }

        AddLog($"🎮 游戏结束！原因: {data.Reason}", "red");
        AddLog("━━━ 最终排名 ━━━", "yellow");

        foreach (var entry in data.Rankings)
        {
            var medal = entry.Rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"#{entry.Rank}"
            };
            AddLog($"{medal} {entry.PlayerName}: ${entry.FinalChips}", "white");
        }
    }

    private void HandleGameState(object? payload)
    {
        var data = DeserializePayload<GameStatePayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.Phase = data.Phase;
            _state.DealerSeatIndex = data.DealerSeatIndex;
            _state.CurrentActingPlayerId = data.CurrentActingPlayerId;

            _state.Players.Clear();
            foreach (var p in data.Players)
            {
                _state.Players.Add(ConvertToPlayerInfo(p));
            }

            _state.CommunityCards.Clear();
            foreach (var c in data.CommunityCards)
            {
                _state.CommunityCards.Add(ConvertToCard(c));
            }

            _state.Pots.Clear();
            foreach (var pot in data.Pots)
            {
                _state.Pots.Add(new PotInfo { Name = pot.Name, Amount = pot.Amount });
            }
        }
    }

    private void HandleError(object? payload)
    {
        var data = DeserializePayload<ErrorPayload>(payload);
        if (data == null) return;

        AddLog($"❌ 错误 [{data.Code}]: {data.Message}", "red");
    }

    #endregion

    #region Helper Methods

    private static T? DeserializePayload<T>(object? payload) where T : class
    {
        switch (payload)
        {
            case null:
                return null;
            case T typed:
                return typed;
        }

        if (payload is not JsonElement je) return null;

        var json = je.GetRawText();
        
        // Use type-specific deserialization for AOT compatibility
        return typeof(T).Name switch
        {
            nameof(JoinSuccessPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.JoinSuccessPayload) as T,
            nameof(PlayerJoinedPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.PlayerJoinedPayload) as T,
            nameof(PlayerLeftPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.PlayerLeftPayload) as T,
            nameof(CountdownStartedPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.CountdownStartedPayload) as T,
            nameof(CountdownUpdatePayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.CountdownUpdatePayload) as T,
            nameof(GameStartedPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.GameStartedPayload) as T,
            nameof(HoleCardsPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.HoleCardsPayload) as T,
            nameof(NewHandStartedPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.NewHandStartedPayload) as T,
            nameof(BlindsPostedPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.BlindsPostedPayload) as T,
            nameof(ActionRequestPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.ActionRequestPayload) as T,
            nameof(PlayerActedPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.PlayerActedPayload) as T,
            nameof(PhaseChangedPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.PhaseChangedPayload) as T,
            nameof(CommunityCardsPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.CommunityCardsPayload) as T,
            nameof(ShowdownRequestPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.ShowdownRequestPayload) as T,
            nameof(PlayerShowedCardsPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.PlayerShowedCardsPayload) as T,
            nameof(PotDistributionPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.PotDistributionPayload) as T,
            nameof(HandEndedPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.HandEndedPayload) as T,
            nameof(GameOverPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.GameOverPayload) as T,
            nameof(GameStatePayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.GameStatePayload) as T,
            nameof(ErrorPayload) => JsonSerializer.Deserialize(json, PayloadJsonContext.Default.ErrorPayload) as T,
            _ => null
        };
    }

    private static PlayerInfo ConvertToPlayerInfo(PlayerDto dto)
    {
        return new PlayerInfo
        {
            Id = dto.Id,
            Name = dto.Name,
            SeatIndex = dto.SeatIndex,
            Chips = dto.Chips,
            CurrentBet = dto.CurrentBet,
            HasFolded = dto.HasFolded,
            IsAllIn = dto.IsAllIn,
            IsConnected = dto.IsConnected
        };
    }

    private static Card ConvertToCard(CardDto dto)
    {
        return new Card
        {
            Suit = (Suit)dto.Suit,
            Rank = (Rank)dto.Rank
        };
    }

    private static string GetSuitSymbol(int suit)
    {
        return suit switch
        {
            0 => "♣",
            1 => "♦",
            2 => "♥",
            3 => "♠",
            _ => "?"
        };
    }

    private static string GetRankSymbol(int rank)
    {
        return rank switch
        {
            2 => "2", 3 => "3", 4 => "4", 5 => "5",
            6 => "6", 7 => "7", 8 => "8", 9 => "9",
            10 => "10", 11 => "J", 12 => "Q", 13 => "K", 14 => "A",
            _ => "?"
        };
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_network != null)
        {
            await _network.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }

    private record LogEntry(DateTime Time, string Message, string Style);
}

#region View DTOs

public class GameStateDto
{
    public string Phase { get; init; } = string.Empty;
    public int HandNumber { get; init; }
    public string MyPlayerId { get; init; } = string.Empty;
    public string MyPlayerName { get; init; } = string.Empty;
    public int MySeatIndex { get; init; }
    public int MyChips { get; init; }
    public List<CardViewDto> MyHand { get; init; } = [];
    public List<PlayerViewDto> Players { get; init; } = [];
    public List<CardViewDto> CommunityCards { get; init; } = [];
    public List<PotViewDto> Pots { get; init; } = [];
    public int TotalPot { get; init; }
    public int DealerSeatIndex { get; init; }
    public int SmallBlindSeatIndex { get; init; }
    public int BigBlindSeatIndex { get; init; }
    public bool IsMyTurn { get; init; }
    public int CurrentBet { get; init; }
    public int CallAmount { get; init; }
    public int MinRaise { get; init; }
    public int ActionTimeout { get; init; }
    public List<ActionViewDto> AvailableActions { get; init; } = [];
    public bool IsShowdownRequest { get; init; }
    public bool MustShowCards { get; init; }
    public int CountdownSeconds { get; init; }
    public bool IsCountingDown { get; init; }
    public List<LogViewDto> Logs { get; init; } = [];
}

public class CardViewDto
{
    public string Display { get; init; } = string.Empty;
    public string Suit { get; init; } = string.Empty;
    public string Rank { get; init; } = string.Empty;
    public string SuitSymbol { get; init; } = string.Empty;
    public string RankSymbol { get; init; } = string.Empty;
    public bool IsRed { get; init; }
}

public class PlayerViewDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SeatIndex { get; init; }
    public int Chips { get; init; }
    public int CurrentBet { get; init; }
    public bool HasFolded { get; init; }
    public bool IsAllIn { get; init; }
    public bool IsConnected { get; init; }
    public bool IsActing { get; init; }
    public bool IsMe { get; init; }
    public bool IsDealer { get; init; }
    public bool IsSmallBlind { get; init; }
    public bool IsBigBlind { get; init; }
    public List<CardViewDto>? ShownCards { get; init; }
    public string? HandRank { get; init; }
}

public class PotViewDto
{
    public string Name { get; init; } = string.Empty;
    public int Amount { get; init; }
}

public class ActionViewDto
{
    public string Type { get; init; } = string.Empty;
    public int? MinAmount { get; init; }
    public int? MaxAmount { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string DisplayText { get; init; } = string.Empty;
}

public class LogViewDto
{
    public string Time { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Style { get; init; } = string.Empty;
}

#endregion

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JoinSuccessPayload))]
[JsonSerializable(typeof(PlayerJoinedPayload))]
[JsonSerializable(typeof(PlayerLeftPayload))]
[JsonSerializable(typeof(CountdownStartedPayload))]
[JsonSerializable(typeof(CountdownUpdatePayload))]
[JsonSerializable(typeof(GameStartedPayload))]
[JsonSerializable(typeof(HoleCardsPayload))]
[JsonSerializable(typeof(NewHandStartedPayload))]
[JsonSerializable(typeof(BlindsPostedPayload))]
[JsonSerializable(typeof(ActionRequestPayload))]
[JsonSerializable(typeof(PlayerActedPayload))]
[JsonSerializable(typeof(PhaseChangedPayload))]
[JsonSerializable(typeof(CommunityCardsPayload))]
[JsonSerializable(typeof(ShowdownRequestPayload))]
[JsonSerializable(typeof(PlayerShowedCardsPayload))]
[JsonSerializable(typeof(PotDistributionPayload))]
[JsonSerializable(typeof(HandEndedPayload))]
[JsonSerializable(typeof(GameOverPayload))]
[JsonSerializable(typeof(GameStatePayload))]
[JsonSerializable(typeof(ErrorPayload))]
internal partial class PayloadJsonContext : JsonSerializerContext;
