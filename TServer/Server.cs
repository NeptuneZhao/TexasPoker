using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using TServer.Game;
using TServer.Logging;
using TServer.Model;
using TServer.Network;
using TServer.Protocol;

namespace TServer;

/// <summary>
/// 服务器的主要逻辑类
/// </summary>
public class Server
{
	private readonly int _port;
	private readonly TcpListener _listener;
	private readonly List<ClientHandler> _clients = [];
	private readonly Lock _lock = new();

	public GameStateMachine Game { get; }

	public Server(int port = 8848)
	{
		_port = port;
		_listener = new TcpListener(IPAddress.Any, port);
		Game = new GameStateMachine(BroadcastMessageAsync, SendToPlayerAsync);
	}

	public async Task StartAsync()
	{
		_listener.Start();
		Logger.Log($"Server started on port {_port}.");

		while (true)
		{
			var tcpClient = await _listener.AcceptTcpClientAsync();
			Logger.Log($"New client connected: {tcpClient.Client.RemoteEndPoint}");
			
			var handler = new ClientHandler(tcpClient, this);
			lock (_lock)
				_clients.Add(handler);
			
			
			// Fire and forget handler
			_ = Task.Run(async () =>
			{
				try
				{
					await handler.RunAsync();
				}
				catch (Exception ex)
				{
					Logger.Log($"Client handler error: {ex.Message}", LogLevel.Error);
				}
				finally
				{
					Remove(handler);
				}
			});
		}
		// ReSharper disable once FunctionNeverReturns
		// Method never exits...
	}

	private async Task BroadcastMessageAsync(ServerMessage msg)
	{
		var json = JsonSerializer.Serialize(msg);
		await BroadcastStringAsync(json);
	}
	
	/// <summary>
	/// 向单个玩家发送消息
	/// </summary>
	private async Task SendToPlayerAsync(Player player, ServerMessage msg)
	{
		ClientHandler? handler;
		lock (_lock)
		{
			handler = _clients.FirstOrDefault(c => c.ThisPlayer == player);
		}
		if (handler != null)
		{
			await handler.SendMessageAsync(msg);
		}
	}

	public async Task BroadcastStringAsync(string msg)
	{
		List<ClientHandler> clients;
		lock (_lock)
		{ 
			clients = _clients.ToList();
		}
		
		// Safe broadcast
		await Task.WhenAll(clients.Select(client => client.SendAsync(msg)));
		// Logger.Log($"Broadcast: {msg}"); // Verbose
	}

	public void Remove(ClientHandler client)
	{
		lock (_lock)
		{
			if (!_clients.Remove(client)) return;
			
			Logger.Log($"Client removed. Remaining: {_clients.Count}");
			// Also remove from GameStateMachine if needed? 
			// The Handler should likely handle "Leave" logic before disconnecting.
			// But if connection drops, we should clean up!
			Game.RemovePlayer(client.ThisPlayer);
		}
	}
}