using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TClient.Protocol;

namespace TClient.Network;

/// <summary>
/// TCP网络客户端 - 处理与服务器的通信
/// </summary>
public class TcpGameClient(string host = "127.0.0.1", int port = 8848) : IDisposable
{
	private TcpClient? _client;
	private StreamReader? _reader;
	private StreamWriter? _writer;
	private CancellationTokenSource? _cts;

	public event Action<ServerMessage>? OnMessageReceived;
	public event Action<string>? OnRawMessageReceived;
	public event Action<string>? OnError;
	public event Action? OnConnected;
	public event Action? OnDisconnected;

	public async Task<bool> ConnectAsync()
	{
		try
		{
			_client = new TcpClient();
			await _client.ConnectAsync(host, port);
			
			var stream = _client.GetStream();
			_reader = new StreamReader(stream, Encoding.UTF8);
			_writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
			
			_cts = new CancellationTokenSource();
			_ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
			
			OnConnected?.Invoke();
			return true;
		}
		catch (Exception ex)
		{
			OnError?.Invoke($"Connection failed: {ex.Message}");
			return false;
		}
	}

	private async Task ReceiveLoopAsync(CancellationToken ct)
	{
		try
		{
			while (!ct.IsCancellationRequested && _reader != null)
			{
				var line = await _reader.ReadLineAsync(ct);
				if (line == null)
				{
					OnDisconnected?.Invoke();
					break;
				}

				OnRawMessageReceived?.Invoke(line);

				// 尝试解析为ServerMessage
				try
				{
					var message = JsonSerializer.Deserialize<ServerMessage>(line, new JsonSerializerOptions
					{
						PropertyNameCaseInsensitive = true
					});
					if (message != null)
					{
						OnMessageReceived?.Invoke(message);
					}
				}
				catch
				{
					// 不是JSON消息，可能是系统消息
				}
			}
		}
		catch (OperationCanceledException)
		{
			// 正常取消
		}
		catch (Exception ex)
		{
			OnError?.Invoke($"Receive error: {ex.Message}");
			OnDisconnected?.Invoke();
		}
	}

	private async Task SendAsync(ClientMessage message)
	{
		if (_writer == null) return;

		try
		{
			var json = JsonSerializer.Serialize(message);
			await _writer.WriteLineAsync(json);
		}
		catch (Exception ex)
		{
			OnError?.Invoke($"Send error: {ex.Message}");
		}
	}

	public async Task JoinRoomAsync(string playerName)
	{
		await SendAsync(new ClientMessage
		{
			Type = ClientMessageType.JoinRoom,
			PayLoad = playerName
		});
	}

	public async Task SendActionAsync(ActionType action, int amount = 0)
	{
		await SendAsync(new ClientMessage
		{
			Type = ClientMessageType.PlayerAction,
			Action = action,
			PayLoad = amount
		});
	}

	private void Disconnect()
	{
		_cts?.Cancel();
		_client?.Close();
		_client?.Dispose();
		_client = null;
		_reader = null;
		_writer = null;
	}

	public void Dispose()
	{
		Disconnect();
		_cts?.Dispose();
		GC.SuppressFinalize(this);
	}
}
