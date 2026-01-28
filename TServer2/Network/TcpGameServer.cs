using System.Net;
using System.Net.Sockets;
using TServer2.Logging;
using TServer2.Protocol;

namespace TServer2.Network;

/// <summary>
/// TCP 服务器
/// </summary>
public class TcpGameServer(int port) : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, port);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<ClientSession> _sessions = [];
    private readonly Lock _sessionsLock = new();

    public event Func<ClientSession, Task>? OnClientConnected;
    public event Func<ClientSession, ClientMessage, Task>? OnMessageReceived;
    public event Func<ClientSession, Exception?, Task>? OnClientDisconnected;

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync()
    {
        _listener.Start();
        Logger.Info($"Server started on port {((IPEndPoint)_listener.LocalEndpoint).Port}");

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                var session = new ClientSession(client);
                
                _sessionsLock.Enter();
                try
                {
                    _sessions.Add(session);
                }
                finally
                {
                    _sessionsLock.Exit();
                }

                Logger.Info($"Client connected: {session.SessionId} from {client.Client.RemoteEndPoint}");

                // 设置事件处理
                session.OnMessageReceived += async (s, m) =>
                {
                    if (OnMessageReceived != null)
                        // s is a session, m is the message
                        await OnMessageReceived(s, m);
                };

                session.OnDisconnected += async (s, ex) =>
                {
                    RemoveSession(s);
                    if (OnClientDisconnected != null)
                        await OnClientDisconnected(s, ex);
                };

                // 通知新连接
                if (OnClientConnected != null)
                    await OnClientConnected(session);

                // 接收消息
                _ = session.StartReceivingAsync();
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Server stopping...");
        }
        catch (Exception ex)
        {
            Logger.Fatal($"Server fatal error: {ex.Message}");
            throw;
        }
    }

    private void RemoveSession(ClientSession session)
    {
        _sessionsLock.Enter();
        try
        {
            _sessions.Remove(session);
        }
        finally
        {
            _sessionsLock.Exit();
        }
    }

    /// <summary>
    /// 获取指定会话
    /// </summary>
    public ClientSession? GetSession(string sessionId)
    {
        _sessionsLock.Enter();
        try
        {
            return _sessions.FirstOrDefault(s => s.SessionId == sessionId);
        }
        finally
        {
            _sessionsLock.Exit();
        }
    }

    /// <summary>
    /// 获取所有会话
    /// </summary>
    private List<ClientSession> GetAllSessions()
    {
        _sessionsLock.Enter();
        try
        {
            return [.._sessions];
        }
        finally
        {
            _sessionsLock.Exit();
        }
    }

    /// <summary>
    /// 广播消息给所有连接的客户端
    /// </summary>
    public async Task BroadcastAsync(ServerMessage message)
    {
        var sessions = GetAllSessions();
        var tasks = sessions.Where(s => s.IsConnected).Select(s => s.SendAsync(message));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();

        var sessions = GetAllSessions();
        foreach (var session in sessions)
        {
            await session.DisposeAsync();
        }

        Logger.Info("Server stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
