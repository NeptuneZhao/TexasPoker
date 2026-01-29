using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TClient.Protocol;

namespace TClient.Network;

/// <summary>
/// TCP 网络客户端
/// 协议格式 - [4字节大端长度头][Body]
/// </summary>
public class TcpGameClient(string host = "127.0.0.1", int port = 5000) : IAsyncDisposable
{
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
    
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(host, port);
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
            if (OnError != null) await OnError($"连接失败: {ex.Message}");
            return false;
        }
    }
    
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
            // 正常
        }
        catch (Exception ex)
        {
            if (OnError != null)
                await OnError($"接收错误: {ex.Message}");
            if (OnDisconnected != null)
                await OnDisconnected();
        }
    }
    
    private async Task<ServerMessage?> ReceiveMessageAsync(CancellationToken ct)
    {
        if (_stream == null) return null;

        // 读取 4 字节长度头
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
        if (length <= 0) throw new InvalidDataException($"无效的消息长度: {length}");

        // 读取 JSON Body
        var bodyBuffer = new byte[length];
        bytesRead = 0;

        while (bytesRead < length)
        {
            var read = await _stream.ReadAsync(bodyBuffer.AsMemory(bytesRead, length - bytesRead), ct);
            if (read == 0) throw new IOException("读取消息体时连接断开");
            
            bytesRead += read;
        }

        var json = Encoding.UTF8.GetString(bodyBuffer);
        return JsonSerializer.Deserialize<ServerMessage>(json, JsonOptions);
    }
    
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
            catch (Exception ex)
            {
                if (OnError != null) await OnError($"心跳错误: {ex.Message}");
            }
        }
    }
    
    private async Task SendMessageAsync(ClientMessage message)
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
    
    public async Task JoinRoomAsync(string playerName)
    {
        await SendMessageAsync(new ClientMessage
        {
            Type = ClientMessageType.JoinRoom,
            PlayerName = playerName
        });
    }
    
    public async Task SendActionAsync(ActionType action, int amount = 0)
    {
        await SendMessageAsync(new ClientMessage
        {
            Type = ClientMessageType.PlayerAction,
            Action = action,
            Amount = amount
        });
    }
    
    public async Task ShowCardsAsync()
    {
        await SendMessageAsync(new ClientMessage
        {
            Type = ClientMessageType.ShowCards
        });
    }
    
    public async Task MuckCardsAsync()
    {
        // 弃牌
        await SendMessageAsync(new ClientMessage
        {
            Type = ClientMessageType.MuckCards
        });
    }
    
    // TODO: ignored fix
    private async Task DisconnectAsync()
    {
        await _cts?.CancelAsync()!;
        
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
