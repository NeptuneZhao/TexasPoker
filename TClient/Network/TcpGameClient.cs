using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TClient.Protocol;

namespace TClient.Network;

/// <summary>
/// TCP网络客户端 - 与TServer2通信
/// 协议格式：[4字节大端长度头][JSON Body]
/// </summary>
public class TcpGameClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public event Func<ServerMessage, Task>? OnMessageReceived;
    public event Func<string, Task>? OnError;
    public event Func<Task>? OnConnected;
    public event Func<Task>? OnDisconnected;

    public bool IsConnected => _client?.Connected ?? false;

    public TcpGameClient(string host = "127.0.0.1", int port = 5000)
    {
        _host = host;
        _port = port;
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();

            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));

            if (OnConnected != null)
                await OnConnected();
            
            return true;
        }
        catch (Exception ex)
        {
            if (OnError != null)
                await OnError($"连接失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 接收消息循环
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                var message = await ReceiveMessageAsync(ct);
                if (message == null) continue;

                if (OnMessageReceived != null)
                    await OnMessageReceived(message);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            if (OnError != null)
                await OnError($"接收错误: {ex.Message}");
            if (OnDisconnected != null)
                await OnDisconnected();
        }
    }

    /// <summary>
    /// 接收一条消息
    /// </summary>
    private async Task<ServerMessage?> ReceiveMessageAsync(CancellationToken ct)
    {
        if (_stream == null) return null;

        // 读取4字节长度头（大端序）
        var lengthBuffer = new byte[4];
        var bytesRead = 0;

        while (bytesRead < 4)
        {
            var read = await _stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), ct);
            if (read == 0)
            {
                if (OnDisconnected != null)
                    await OnDisconnected();
                throw new IOException("连接已关闭");
            }
            bytesRead += read;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (length <= 0 || length > 1024 * 1024) // 最大1MB
        {
            throw new InvalidDataException($"无效的消息长度: {length}");
        }

        // 读取JSON Body
        var bodyBuffer = new byte[length];
        bytesRead = 0;

        while (bytesRead < length)
        {
            var read = await _stream.ReadAsync(bodyBuffer.AsMemory(bytesRead, length - bytesRead), ct);
            if (read == 0)
            {
                throw new IOException("读取消息体时连接断开");
            }
            bytesRead += read;
        }

        var json = Encoding.UTF8.GetString(bodyBuffer);
        return JsonSerializer.Deserialize<ServerMessage>(json, JsonOptions);
    }

    /// <summary>
    /// 心跳循环
    /// </summary>
    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                await SendMessageAsync(new ClientMessage { Type = ClientMessageType.Heartbeat });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 忽略心跳错误
            }
        }
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    public async Task SendMessageAsync(ClientMessage message)
    {
        if (_stream == null) return;

        await _sendLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);
            var bodyBytes = Encoding.UTF8.GetBytes(json);
            
            var lengthBuffer = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, bodyBytes.Length);

            await _stream.WriteAsync(lengthBuffer);
            await _stream.WriteAsync(bodyBytes);
            await _stream.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 加入房间
    /// </summary>
    public async Task JoinRoomAsync(string playerName)
    {
        await SendMessageAsync(new ClientMessage
        {
            Type = ClientMessageType.JoinRoom,
            PlayerName = playerName
        });
    }

    /// <summary>
    /// 发送玩家行动
    /// </summary>
    public async Task SendActionAsync(ActionType action, int amount = 0)
    {
        await SendMessageAsync(new ClientMessage
        {
            Type = ClientMessageType.PlayerAction,
            Action = action,
            Amount = amount
        });
    }

    /// <summary>
    /// 选择亮牌
    /// </summary>
    public async Task ShowCardsAsync()
    {
        await SendMessageAsync(new ClientMessage
        {
            Type = ClientMessageType.ShowCards
        });
    }

    /// <summary>
    /// 选择盖牌
    /// </summary>
    public async Task MuckCardsAsync()
    {
        await SendMessageAsync(new ClientMessage
        {
            Type = ClientMessageType.MuckCards
        });
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        
        if (_receiveTask != null)
        {
            try { await _receiveTask; } catch { /* ignored */ }
        }
        
        if (_heartbeatTask != null)
        {
            try { await _heartbeatTask; } catch { /* ignored */ }
        }

        _stream?.Close();
        _client?.Close();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
