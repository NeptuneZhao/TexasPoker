using System.Text.Json;
using Spectre.Console;
using TClient.Model;
using TClient.Network;
using TClient.Protocol;
using TClient.UI;

namespace TClient;

/// <summary>
/// 游戏客户端主控制器
/// </summary>
public class GameClient : IAsyncDisposable
{
    private TcpGameClient? _network;
    private readonly GameState _state = new();
    private readonly SpectreRenderer _renderer = new();
    private readonly Lock _stateLock = new();

    private bool _isRunning = true;
    private LiveDisplayContext? _liveContext;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 运行客户端
    /// </summary>
    public async Task RunAsync()
    {
        Console.Title = "Texas Hold'em Poker Client";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 显示欢迎界面
        ShowWelcome();

        // 获取连接信息
        var (host, port) = GetConnectionInfo();
        var playerName = GetPlayerName();

        // 创建网络客户端
        _network = new TcpGameClient(host, port);
        RegisterNetworkEvents();

        _renderer.AddLog($"正在连接到 {host}:{port}...", "yellow");

        // 连接服务器
        if (!await _network.ConnectAsync())
        {
            AnsiConsole.MarkupLine("[red]连接服务器失败！按任意键退出...[/]");
            Console.ReadKey(true);
            return;
        }

        // 加入房间
        await _network.JoinRoomAsync(playerName);
        lock (_stateLock)
        {
            _state.MyPlayerName = playerName;
        }

        _renderer.AddLog($"已加入房间，玩家名: {playerName}", "green");
        _renderer.AddLog("等待游戏开始 (需要4名玩家)...", "yellow");

        // 启动带Live显示的主循环
        var live = AnsiConsole.Live(BuildCurrentLayout())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top);
        
        // TODO: why it is BUG
        await live.StartAsync(async ctx =>
        {
            _liveContext = ctx;
            await MainLoopAsync(ctx);
        });

        
        // Game over
    }

    /// <summary>
    /// 显示欢迎界面
    /// </summary>
    private static void ShowWelcome()
    {
        AnsiConsole.Clear();
        
        var title = new FigletText("Texas Poker")
            .Centered()
            .Color(Color.Yellow);
        
        AnsiConsole.Write(title);
        AnsiConsole.WriteLine();
        
        var panel = new Panel(
            new Markup("[bold]欢迎来到德州扑克！[/]\n\n" +
                       "[dim]• 最少4人开始游戏\n" +
                       "• 初始筹码1000\n" +
                       "• 盲注2/4[/]"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green)
            .Header("[yellow]游戏说明[/]")
            .Padding(1, 1);
        
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// 获取连接信息
    /// </summary>
    private static (string host, int port) GetConnectionInfo()
    {
        var input = AnsiConsole.Ask<string>(
            "[cyan]服务器地址[/] [dim](默认: 127.0.0.1:5000)[/]:",
            "127.0.0.1:5000");

        var parts = input.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5000;

        return (host, port);
    }

    /// <summary>
    /// 获取玩家名称
    /// </summary>
    private static string GetPlayerName()
    {
        return AnsiConsole.Ask<string>(
            "[cyan]你的名字:[/]",
            $"Player{Random.Shared.Next(1000, 9999)}");
    }

    /// <summary>
    /// 注册网络事件
    /// </summary>
    private void RegisterNetworkEvents()
    {
        if (_network == null) return;

        _network.OnConnected += async () =>
        {
            _renderer.AddLog("已连接到服务器", "green");
            RefreshDisplay();
            await Task.CompletedTask;
        };

        _network.OnDisconnected += async () =>
        {
            _renderer.AddLog("与服务器断开连接", "red");
            _isRunning = false;
            RefreshDisplay();
            await Task.CompletedTask;
        };

        _network.OnError += async error =>
        {
            _renderer.AddLog($"错误: {error}", "red");
            RefreshDisplay();
            await Task.CompletedTask;
        };

        _network.OnMessageReceived += ProcessServerMessageAsync;
    }

    /// <summary>
    /// 主循环
    /// </summary>
    private async Task MainLoopAsync(LiveDisplayContext ctx)
    {
        // 启动输入处理任务
        var inputTask = Task.Run(async () =>
        {
            while (_isRunning)
            {
                try
                {
                    // 使用 ReadKey 会阻塞，但在单独的任务中运行不会影响 UI
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        await HandleInputAsync(key);
                    }
                    else
                    {
                        await Task.Delay(50);
                    }
                }
                catch
                {
                    // 忽略输入错误
                }
            }
        });

        while (_isRunning)
        {
            try
            {
                // 刷新显示
                ctx.UpdateTarget(BuildCurrentLayout());
            }
            catch (ArgumentOutOfRangeException)
            {
                // 终端尺寸太小导致的渲染错误，忽略
            }
            catch
            {
                // 忽略其他渲染错误
            }

            await Task.Delay(100); // 10 FPS，降低刷新率以减少资源占用
        }

        // 等待输入任务结束
        await inputTask;

        // 显示退出信息
        try
        {
            ctx.UpdateTarget(BuildCurrentLayout());
            _renderer.AddLog("游戏已结束", "yellow");
            ctx.UpdateTarget(BuildCurrentLayout());
        }
        catch
        {
            // 忽略最终渲染错误
        }
    }

    /// <summary>
    /// 构建当前布局
    /// </summary>
    private Layout BuildCurrentLayout()
    {
        lock (_stateLock)
        {
            return _renderer.BuildLayout(_state);
        }
    }

    /// <summary>
    /// 刷新显示
    /// </summary>
    private void RefreshDisplay()
    {
        try
        {
            _liveContext?.UpdateTarget(BuildCurrentLayout());
        }
        catch (ArgumentOutOfRangeException)
        {
            // 终端尺寸太小导致的渲染错误，忽略
        }
        catch
        {
            // 忽略其他渲染错误
        }
    }

    /// <summary>
    /// 处理键盘输入
    /// </summary>
    private async Task HandleInputAsync(ConsoleKeyInfo key)
    {
        if (_network == null) return;

        // Q键退出
        if (key.Key == ConsoleKey.Q)
        {
            _isRunning = false;
            return;
        }

        // 处理摊牌选择
        bool isShowdown;
        lock (_stateLock)
        {
            isShowdown = _state.IsShowdownRequest;
        }

        if (isShowdown)
        {
            switch (key.Key)
            {
                case ConsoleKey.S:
                    await _network.ShowCardsAsync();
                    _renderer.AddLog("你选择了亮牌", "cyan");
                    lock (_stateLock)
                    {
                        _state.IsShowdownRequest = false;
                    }
                    RefreshDisplay();
                    return;
                case ConsoleKey.M:
                    await _network.MuckCardsAsync();
                    _renderer.AddLog("你选择了盖牌");
                    lock (_stateLock)
                    {
                        _state.IsShowdownRequest = false;
                    }
                    RefreshDisplay();
                    return;
            }
        }

        // 处理游戏行动
        bool isMyTurn;
        lock (_stateLock)
        {
            isMyTurn = _state.IsMyTurn;
        }

        if (!isMyTurn) return;

        switch (key.Key)
        {
            case ConsoleKey.F: // Fold
                await _network.SendActionAsync(ActionType.Fold);
                _renderer.AddLog("你弃牌了", "red");
                SetMyTurnFalse();
                break;

            case ConsoleKey.K: // Check
                await _network.SendActionAsync(ActionType.Check);
                _renderer.AddLog("你过牌了", "cyan");
                SetMyTurnFalse();
                break;

            case ConsoleKey.C: // Call
                int callAmount;
                lock (_stateLock)
                {
                    callAmount = _state.CallAmount;
                }
                await _network.SendActionAsync(ActionType.Call, callAmount);
                _renderer.AddLog($"你跟注了 ${callAmount}", "green");
                SetMyTurnFalse();
                break;

            case ConsoleKey.B: // Bet
                var betAmount = await GetAmountInputAsync("下注金额");
                if (betAmount > 0)
                {
                    await _network.SendActionAsync(ActionType.Bet, betAmount);
                    _renderer.AddLog($"你下注了 ${betAmount}", "yellow");
                    SetMyTurnFalse();
                }
                break;

            case ConsoleKey.R: // Raise
                var raiseAmount = await GetAmountInputAsync("加注金额");
                if (raiseAmount > 0)
                {
                    await _network.SendActionAsync(ActionType.Raise, raiseAmount);
                    _renderer.AddLog($"你加注到 ${raiseAmount}", "yellow");
                    SetMyTurnFalse();
                }
                break;

            case ConsoleKey.A: // All-In
                int chips;
                lock (_stateLock)
                {
                    chips = _state.MyChips;
                }
                await _network.SendActionAsync(ActionType.AllIn, chips);
                _renderer.AddLog($"你全下了 ${chips}！", "bold red");
                SetMyTurnFalse();
                break;
        }

        RefreshDisplay();
    }

    private void SetMyTurnFalse()
    {
        lock (_stateLock)
        {
            _state.IsMyTurn = false;
        }
    }

    /// <summary>
    /// 获取金额输入
    /// </summary>
    private static async Task<int> GetAmountInputAsync(string prompt)
    {
        // 暂时使用简单的控制台输入
        // 在实际中可以使用更复杂的UI
        Console.CursorVisible = true;
        Console.SetCursorPosition(0, Console.WindowHeight - 2);
        Console.Write($"{prompt}: ");
        
        var input = "";
        while (true)
        {
            if (!Console.KeyAvailable)
            {
                await Task.Delay(10);
                continue;
            }

            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter)
                break;
            switch (k.Key)
            {
                case ConsoleKey.Escape:
                    Console.CursorVisible = false;
                    return 0;
                case ConsoleKey.Backspace when input.Length > 0:
                    input = input[..^1];
                    Console.Write("\b \b");
                    break;
                default:
                {
                    if (char.IsDigit(k.KeyChar))
                    {
                        input += k.KeyChar;
                        Console.Write(k.KeyChar);
                    }

                    break;
                }
            }
        }

        Console.CursorVisible = false;
        return int.TryParse(input, out var amount) ? amount : 0;
    }

    /// <summary>
    /// 处理服务器消息
    /// </summary>
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
                // 忽略心跳
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(message));
        }

        RefreshDisplay();
        await Task.CompletedTask;
    }

    #region Message Handlers

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
            // 添加自己
            _state.Players.Add(new PlayerInfo
            {
                Id = data.PlayerId,
                Name = data.PlayerName,
                SeatIndex = data.SeatIndex,
                Chips = data.Chips
            });
        }

        _renderer.AddLog($"加入成功！座位 {data.SeatIndex}，筹码 ${data.Chips}", "green");
    }

    private void HandlePlayerJoined(object? payload)
    {
        var data = DeserializePayload<PlayerJoinedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            // 检查是否已存在
            var existing = _state.Players.FirstOrDefault(p => p.Id == data.Player.Id);
            if (existing != null)
                _state.Players.Remove(existing);
            
            _state.Players.Add(ConvertToPlayerInfo(data.Player));
        }

        _renderer.AddLog($"{data.Player.Name} 加入了游戏 ({data.CurrentPlayerCount}/{data.MaxPlayers})", "cyan");
    }

    private void HandlePlayerLeft(object? payload)
    {
        var data = DeserializePayload<PlayerLeftPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.Players.RemoveAll(p => p.Id == data.PlayerId);
        }

        _renderer.AddLog($"{data.PlayerName} 离开了游戏 ({data.Reason})", "yellow");
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

        _renderer.AddLog($"游戏即将开始！倒计时 {data.Seconds} 秒...", "yellow");
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

        _renderer.AddLog("🎮 游戏开始！", "bold green");
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
        _renderer.AddLog($"🎴 你的手牌: {cards}", "cyan");
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

            // 更新玩家状态
            _state.Players.Clear();
            foreach (var p in data.Players)
            {
                var info = ConvertToPlayerInfo(p);
                _state.Players.Add(info);
                if (p.Id == _state.MyPlayerId)
                    _state.MyChips = p.Chips;
            }
        }

        _renderer.AddLog($"━━━ 第 {data.HandNumber} 手开始 ━━━", "bold yellow");
    }

    private void HandleBlindsPosted(object? payload)
    {
        var data = DeserializePayload<BlindsPostedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.CurrentBet = data.BigBlindAmount;

            // 更新玩家下注
            var sb = _state.Players.FirstOrDefault(p => p.Id == data.SmallBlindPlayerId);
            sb?.CurrentBet = data.SmallBlindAmount;

            var bb = _state.Players.FirstOrDefault(p => p.Id == data.BigBlindPlayerId);
            bb?.CurrentBet = data.BigBlindAmount;
        }

        _renderer.AddLog($"盲注已下: SB ${data.SmallBlindAmount}, BB ${data.BigBlindAmount}", "dim");
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

            // 更新底池
            _state.Pots.Clear();
            foreach (var pot in data.Pots)
            {
                _state.Pots.Add(new PotInfo { Name = pot.Name, Amount = pot.Amount });
            }
        }

        _renderer.AddLog($">>> 轮到你了！({data.TimeoutSeconds}秒)", "bold green");
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

            // 更新当前下注
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

        _renderer.AddLog($"{data.PlayerName}: {actionText}", "white");
    }

    private void HandlePhaseChanged(object? payload)
    {
        var data = DeserializePayload<PhaseChangedPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.Phase = data.Phase;
            _state.CurrentBet = 0;

            // 重置玩家当前下注
            foreach (var p in _state.Players)
                p.CurrentBet = 0;

            // 更新公共牌
            _state.CommunityCards.Clear();
            foreach (var c in data.CommunityCards)
            {
                _state.CommunityCards.Add(ConvertToCard(c));
            }

            // 更新底池
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

        _renderer.AddLog($"━━━ {phaseName} ━━━", "bold cyan");
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
        _renderer.AddLog($"新发公共牌: {newCards}", "cyan");
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

        var msg = data.MustShow ? "你必须亮牌！[S]亮牌" : "选择: [S]亮牌 [M]盖牌";
        _renderer.AddLog($"🎭 摊牌时间！{msg}", "bold cyan");
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
            _renderer.AddLog($"{data.PlayerName} 选择盖牌", "dim");
        }
        else
        {
            var cards = string.Join(" ", data.Cards.Select(c => $"{GetSuitSymbol(c.Suit)}{GetRankSymbol(c.Rank)}"));
            var rank = data.HandEvaluation?.Rank ?? "";
            _renderer.AddLog($"{data.PlayerName} 亮牌: {cards} [{rank}]", "cyan");
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

                var style = winner.PlayerId == myId ? "bold green" : "yellow";
                _renderer.AddLog($"🏆 {winner.PlayerName} 赢得 {pot.PotName} ${winner.AmountWon} [{winner.HandRank}]", style);

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

            // 更新玩家筹码
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

        _renderer.AddLog("这一手结束了", "dim");
    }

    private void HandleGameOver(object? payload)
    {
        var data = DeserializePayload<GameOverPayload>(payload);
        if (data == null) return;

        lock (_stateLock)
        {
            _state.Phase = "GameOver";
        }

        _renderer.AddLog($"🎮 游戏结束！原因: {data.Reason}", "bold red");
        _renderer.AddLog("━━━ 最终排名 ━━━", "yellow");

        foreach (var entry in data.Rankings)
        {
            var medal = entry.Rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"#{entry.Rank}"
            };
            _renderer.AddLog($"{medal} {entry.PlayerName}: ${entry.FinalChips}", "white");
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

        _renderer.AddLog($"❌ 错误 [{data.Code}]: {data.Message}", "red");
    }

    #endregion

    #region Helper Methods

    private static T? DeserializePayload<T>(object? payload) where T : class
    {
        return payload switch
        {
            null => null,
            T typed => typed,
            JsonElement je => JsonSerializer.Deserialize<T>(je.GetRawText(), JsonOptions),
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
}
