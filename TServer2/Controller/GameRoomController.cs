using TServer2.Game;
using TServer2.Logging;
using TServer2.Model;
using TServer2.Network;
using TServer2.Protocol;

namespace TServer2.Controller;

/// <summary>
/// 游戏房间控制器 - 协调网络层和游戏逻辑层
/// </summary>
public class GameRoomController : IAsyncDisposable
{
    private readonly TcpGameServer _server;
    private readonly GameStateMachine _game;
    private readonly Dictionary<string, ClientSession> _playerSessions = new(); // PlayerId -> Session
    private readonly Lock _lock = new();
    
    private CancellationTokenSource? _countdownCts;
    private const int MinPlayersToStart = 4;
    private const int MaxPlayers = 10;
    private const int CountdownSeconds = 10;

    public GameRoomController(int port)
    {
        _server = new TcpGameServer(port);
        _game = new GameStateMachine(SendToPlayerAsync, BroadcastAsync);

        // 注册服务器事件
        _server.OnClientConnected += OnClientConnectedAsync;
        _server.OnMessageReceived += OnMessageReceivedAsync;
        _server.OnClientDisconnected += OnClientDisconnectedAsync;
    }

    /// <summary>
    /// 启动控制器
    /// </summary>
    public async Task StartAsync()
    {
        Logger.Info("Starting Game Room Controller...");
        await _server.StartAsync();
    }

    /// <summary>
    /// 停止控制器
    /// </summary>
    public async Task StopAsync()
    {
        await _countdownCts?.CancelAsync()!;
        await _server.StopAsync();
        Logger.Info("Game Room Controller stopped");
    }

    #region Event Handlers

    private static async Task OnClientConnectedAsync(ClientSession session)
    {
        Logger.Info($"New connection: {session.SessionId}");
        // 连接后等待 JoinRoom 消息
        await Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(ClientSession session, ClientMessage message)
    {
        Logger.Debug($"Message from {session.SessionId}: {message.Type}");

        try
        {
            switch (message.Type)
            {
                case ClientMessageType.JoinRoom:
                    await HandleJoinRoomAsync(session, message.PlayerName);
                    break;

                case ClientMessageType.PlayerAction:
                    if (session.PlayerId != null && message.Action.HasValue)
                    {
                        await _game.HandlePlayerActionAsync(
                            session.PlayerId, 
                            message.Action.Value, 
                            message.Amount ?? 0);
                    }
                    break;

                case ClientMessageType.Heartbeat:
                    await session.SendAsync(new ServerMessage { Type = ServerMessageType.Heartbeat });
                    break;

                case ClientMessageType.ShowCards:
                case ClientMessageType.MuckCards:
                default:
                    Logger.Warn($"Unknown message type: {message.Type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error handling message: {ex.Message}");
            await session.SendAsync(new ServerMessage
            {
                Type = ServerMessageType.Error,
                Payload = new ErrorPayload { Message = "Internal server error" }
            });
        }
    }

    private async Task OnClientDisconnectedAsync(ClientSession session, Exception? ex)
    {
        if (session.PlayerId != null)
        {
            var reason = ex is System.Net.Sockets.SocketException 
                ? "Socket error" 
                : "Disconnected";
            
            Logger.Warn($"Player {session.PlayerName} disconnected: {reason}");
            
            // 从房间移除
            _game.RemovePlayer(session.PlayerId, reason);
            
            _lock.Enter();
            try
            {
                _playerSessions.Remove(session.PlayerId);
            }
            finally
            {
                _lock.Exit();
            }

            // 广播玩家离开
            await BroadcastAsync(new ServerMessage
            {
                Type = ServerMessageType.PlayerLeft,
                Payload = new PlayerLeftPayload
                {
                    PlayerId = session.PlayerId,
                    PlayerName = session.PlayerName ?? "Unknown",
                    Reason = reason
                }
            });

            // 检查是否需要取消倒计时
            var playerCount = _game.Players.Count;
            if (playerCount < MinPlayersToStart && _countdownCts != null)
            {
                await _countdownCts.CancelAsync();
                _countdownCts = null;
                Logger.Info("Countdown cancelled - not enough players");
            }
        }
    }

    #endregion

    #region Message Handlers

    private async Task HandleJoinRoomAsync(ClientSession session, string? playerName)
    {
        var (success, error, player) = _game.AddPlayer(playerName ?? "");

        if (!success || player == null)
        {
            await session.SendAsync(new ServerMessage
            {
                Type = ServerMessageType.Error,
                Payload = new ErrorPayload { Message = error ?? "Failed to join room" }
            });
            return;
        }

        // 关联 session 和 player
        session.PlayerId = player.Id;
        session.PlayerName = player.Name;
        
        _lock.Enter();
        try
        {
            _playerSessions[player.Id] = session;
        }
        finally
        {
            _lock.Exit();
        }

        // 发送加入成功消息
        await session.SendAsync(new ServerMessage
        {
            Type = ServerMessageType.JoinSuccess,
            Payload = new JoinSuccessPayload
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                SeatIndex = player.SeatIndex,
                Chips = player.Chips,
                ExistingPlayers = _game.Players
                    .Where(p => p.Id != player.Id)
                    .Select(p => new PlayerDto(p))
                    .ToList()
            }
        });

        // 广播新玩家加入
        await BroadcastAsync(new ServerMessage
        {
            Type = ServerMessageType.PlayerJoined,
            Payload = new PlayerJoinedPayload
            {
                Player = new PlayerDto(player),
                CurrentPlayerCount = _game.Players.Count,
                MinPlayersToStart = MinPlayersToStart,
                MaxPlayers = MaxPlayers
            }
        });

        Logger.Info($"Player {player.Name} joined at seat {player.SeatIndex}. Total: {_game.Players.Count}/{MaxPlayers}");

        // 检查是否可以开始倒计时
        if (_game.Players.Count >= MinPlayersToStart && _game.Phase == GamePhase.WaitingForPlayers)
        {
            await StartCountdownAsync();
        }
    }

    private async Task StartCountdownAsync()
    {
        if (_countdownCts != null) return;

        _countdownCts = new CancellationTokenSource();
        var token = _countdownCts.Token;

        Logger.Info($"Starting {CountdownSeconds}s countdown...");

        await BroadcastAsync(new ServerMessage
        {
            Type = ServerMessageType.CountdownStarted,
            Payload = new CountdownStartedPayload { Seconds = CountdownSeconds }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = CountdownSeconds; i > 0; i--)
                {
                    token.ThrowIfCancellationRequested();
                    
                    await BroadcastAsync(new ServerMessage
                    {
                        Type = ServerMessageType.CountdownUpdate,
                        Payload = new CountdownUpdatePayload { SecondsRemaining = i }
                    });

                    Logger.Info($"Countdown: {i}...");
                    await Task.Delay(1000, token);
                }

                // 倒计时结束，开始游戏
                Logger.Info("Countdown finished, starting game!");
                await BroadcastAsync(new ServerMessage
                {
                    Type = ServerMessageType.GameStarted,
                    Payload = new GameStartedPayload
                    {
                        Players = _game.Players.Select(p => new PlayerDto(p)).ToList()
                    }
                });

                await _game.StartNewHandAsync();
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Countdown cancelled");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during countdown: {ex.Message}");
            }
            finally
            {
                _countdownCts = null;
            }
        }, _countdownCts.Token);
    }

    #endregion

    #region Communication

    private async Task SendToPlayerAsync(string playerId, ServerMessage message)
    {
        ClientSession? session;
        _lock.Enter();
        try
        {
            _playerSessions.TryGetValue(playerId, out session);
        }
        finally
        {
            _lock.Exit();
        }

        if (session?.IsConnected == true)
        {
            try
            {
                await session.SendAsync(message);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to send to player {playerId}: {ex.Message}");
            }
        }
    }

    private async Task BroadcastAsync(ServerMessage message)
    {
        List<ClientSession> sessions;
        _lock.Enter();
        try
        {
            sessions = [.._playerSessions.Values];
        }
        finally
        {
            _lock.Exit();
        }

        var tasks = sessions.Where(s => s.IsConnected).Select(s => SafeSendAsync(s, message));
        await Task.WhenAll(tasks);
    }

    private static async Task SafeSendAsync(ClientSession session, ServerMessage message)
    {
        try
        {
            await session.SendAsync(message);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Broadcast failed for session {session.SessionId}: {ex.Message}");
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _server.DisposeAsync();
        
        GC.SuppressFinalize(this);
    }
}
