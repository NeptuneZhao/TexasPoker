using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TServer2.Logging;
using TServer2.Protocol;

namespace TServer2.Network;

/// <summary>
/// 客户端会话 - 处理单个客户端连接
/// 协议格式：[4字节大端长度头][JSON Body]
/// </summary>
public class ClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string SessionId { get; } = Guid.NewGuid().ToString();
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsConnected => _client.Connected && !_cts.IsCancellationRequested;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    public event Func<ClientSession, ClientMessage, Task>? OnMessageReceived;
    public event Func<ClientSession, Exception?, Task>? OnDisconnected;

    public ClientSession(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _client.ReceiveTimeout = 0; // 无超时，我们用CancellationToken控制
        _client.SendTimeout = 5000;
    }

    /// <summary>
    /// 开始接收消息
    /// </summary>
    public async Task StartReceivingAsync()
    {
        Logger.Info($"[Session {SessionId}] Started receiving messages");
        
        try
        {
            while (!_cts.IsCancellationRequested && _client.Connected)
            {
                var message = await ReceiveMessageAsync(_cts.Token);
                if (message == null) continue;
                LastActivity = DateTime.UtcNow;
                    
                if (OnMessageReceived != null)
                {
                    await OnMessageReceived(this, message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug($"[Session {SessionId}] Receiving cancelled");
        }
        catch (SocketException ex)
        {
            Logger.Warn($"[Session {SessionId}] Socket exception: {ex.Message}");
            if (OnDisconnected != null)
                await OnDisconnected(this, ex);
        }
        catch (IOException ex)
        {
            Logger.Warn($"[Session {SessionId}] IO exception: {ex.Message}");
            if (OnDisconnected != null)
                await OnDisconnected(this, ex);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Session {SessionId}] Unexpected error: {ex.Message}");
            if (OnDisconnected != null)
                await OnDisconnected(this, ex);
        }
    }

    /// <summary>
    /// 接收一条消息
    /// </summary>
    private async Task<ClientMessage?> ReceiveMessageAsync(CancellationToken ct)
    {
        // 读取4字节长度头（大端序）
        var lengthBuffer = new byte[4];
        var bytesRead = 0;
        
        while (bytesRead < 4)
        {
            var read = await _stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), ct);
            if (read == 0)
            {
                Logger.Debug($"[Session {SessionId}] Connection closed by client");
                return null;
            }
            bytesRead += read;
        }

        // 大端序转换
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBuffer);
        
        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        
        if (messageLength <= 0 || messageLength > 1024 * 1024) // 最大1MB
        {
            Logger.Warn($"[Session {SessionId}] Invalid message length: {messageLength}");
            return null;
        }

        // 读取JSON Body
        var bodyBuffer = new byte[messageLength];
        bytesRead = 0;
        
        while (bytesRead < messageLength)
        {
            var read = await _stream.ReadAsync(bodyBuffer.AsMemory(bytesRead, messageLength - bytesRead), ct);
            if (read == 0)
            {
                Logger.Debug($"[Session {SessionId}] Connection closed while reading body");
                return null;
            }
            bytesRead += read;
        }

        var json = Encoding.UTF8.GetString(bodyBuffer);
        Logger.Debug($"[Session {SessionId}] Received: {json}");

        try
        {
            return JsonSerializer.Deserialize<ClientMessage>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            Logger.Warn($"[Session {SessionId}] JSON parse error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    public async Task SendAsync(ServerMessage message)
    {
        if (!IsConnected) return;

        await _sendLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);
            var bodyBytes = Encoding.UTF8.GetBytes(json);
            var lengthBytes = BitConverter.GetBytes(bodyBytes.Length);
            
            // 转换为大端序
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            await _stream.WriteAsync(lengthBytes);
            await _stream.WriteAsync(bodyBytes);
            await _stream.FlushAsync();
            
            Logger.Debug($"[Session {SessionId}] Sent: {message.Type}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Session {SessionId}] Send failed: {ex.Message}");
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    private async Task DisconnectAsync()
    {
        await _cts.CancelAsync();
        
        try
        {
            _stream.Close();
            _client.Close();
        }
        catch
        {
            // 忽略关闭错误
        }
        
        Logger.Info($"[Session {SessionId}] Disconnected");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts.Dispose();
        _sendLock.Dispose();
        await _stream.DisposeAsync();
        _client.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
