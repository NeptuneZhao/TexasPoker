using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TServer.Logging;
using TServer.Model;
using TServer.Protocol;

namespace TServer.Network;

/// <summary>
/// 处理单个客户端连接
/// </summary>
/// <param name="client">客户端</param>
/// <param name="server">服务器</param>
public class ClientHandler(TcpClient client, Server server)
{
	private readonly StreamReader _reader = new(client.GetStream(), Encoding.UTF8);
	private readonly StreamWriter _writer = new(client.GetStream(), Encoding.UTF8) { AutoFlush = true };
	private readonly MessageDispatcher _dispatcher = new();

	public readonly Server Server = server;

	/// <summary>
	/// 每个客户端拥有独立的 Player 对象
	/// </summary>
	public Player ThisPlayer { get; } = new Player();

	public async Task RunAsync()
	{
		try
		{
			while (true)
			{
				var line = await _reader.ReadLineAsync();
				if (line is null)
				{
					Logger.Log($"Client disconnected: {client.Client.RemoteEndPoint}", LogLevel.Warn);
					break;
				}
				Logger.Log($"Received from {client.Client.RemoteEndPoint}: {line}");
				await HandleMessageAsync(line);
			}
		}
		catch (Exception e)
		{
			Logger.Log($"Client handler exception: {e.Message}", LogLevel.Fatal);
			throw;
		}
		finally
		{
			client.Close();
			Server.Remove(this);
			Logger.Log($"Handler terminated: {client.Client.RemoteEndPoint}", LogLevel.Error);
		}
	}

	public async Task SendAsync(string msg) => await _writer.WriteLineAsync(msg);
	
	/// <summary>
	/// 发送服务器消息对象给此客户端
	/// </summary>
	public async Task SendMessageAsync(ServerMessage msg)
	{
		var json = JsonSerializer.Serialize(msg);
		await SendAsync(json);
	}

	private async Task HandleMessageAsync(string msgJsonLike)
	{
		// Deserialize      用于静态字符串
		// DeserializeAsync 用于流
		var message = JsonSerializer.Deserialize<ClientMessage>(msgJsonLike);
		if (message is null)
		{
			Logger.Log("Received invalid message", LogLevel.Warn);
			return;
		}

		await _dispatcher.DispatchAsync(this, message);
	}
	
}