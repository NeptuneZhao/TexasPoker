using System.Text.Json;
using TClient.Model;
using TClient.Network;
using TClient.Protocol;
using TClient.UI;

namespace TClient;

/// <summary>
/// 游戏客户端主控制器
/// </summary>
public class GameClient : IDisposable
{
	private TcpGameClient? _network;
	private readonly GameState _state = new();
	private readonly RenderLoop _renderLoop;

	private bool _isRunning = true;
	private string _playerName = "";

	public GameClient()
	{
		_renderLoop = new RenderLoop(_state)
		{
			RenderIntervalMs = 40 // 20ms渲染间隔 = 50 FPS
		};
	}

	public async Task RunAsync()
	{
		// 启动渲染循环
		_renderLoop.Start();
		
		// 欢迎
		_renderLoop.AddLog("Welcome to Texas Hold'em Poker!");
		_renderLoop.AddLog("Press any key to connect...");

		// 获取服务器信息
		Console.SetCursorPosition(35, 30);
		Console.CursorVisible = true;
		Console.Write("Enter server (default: 127.0.0.1:8848): ");
		var serverInput = Console.ReadLine()?.Trim();
		Console.CursorVisible = false;
		
		var host = "127.0.0.1";
		var port = 8848;
		
		if (!string.IsNullOrEmpty(serverInput))
		{
			var parts = serverInput.Split(':');
			host = parts[0];
			if (parts.Length > 1 && int.TryParse(parts[1], out var p))
				port = p;
		}

		// 创建网络客户端
		_network = new TcpGameClient(host, port);
		RegisterNetworkEvents(_network);

		_renderLoop.AddLog($"Connecting to {host}:{port}...");

		// 连接服务器
		var connected = await _network.ConnectAsync();
		if (!connected)
		{
			_renderLoop.AddLog("Failed to connect to server!");
			Console.WriteLine("\nPress any key to exit...");
			Console.ReadKey();
			return;
		}

		// 获取玩家名称
		Console.SetCursorPosition(35, 30);
		Console.CursorVisible = true;
		Console.Write("Enter your name: ");
		_playerName = Console.ReadLine()?.Trim() ?? $"Player{Random.Shared.Next(1000)}";
		Console.CursorVisible = false;

		await _network.JoinRoomAsync(_playerName);
		_renderLoop.AddLog($"Joined as: {_playerName}");
		_renderLoop.AddLog("Waiting for game to start (need 2+ players)...");
		_renderLoop.AddLog("Commands: [F]old [C]all [P]ass [R]aise [B]et [A]ll-in [Q]uit");

		// 主输入循环 - 只处理输入，渲染由RenderLoop自动完成
		while (_isRunning)
		{
			if (Console.KeyAvailable)
			{
				var key = Console.ReadKey(true);
				await HandleInputAsync(key);
			}

			await Task.Delay(10); // 输入检测间隔
		}
		
		// 停止渲染循环
		await _renderLoop.StopAsync();
	}

	private void RegisterNetworkEvents(TcpGameClient client)
	{
		client.OnConnected += OnConnected;
		client.OnDisconnected += OnDisconnected;
		client.OnMessageReceived += OnMessageReceived;
		client.OnRawMessageReceived += OnRawMessage;
		client.OnError += OnError;
	}

	private async Task HandleInputAsync(ConsoleKeyInfo key)
	{
		if (_network == null) return;

		// Q键可以随时退出
		if (key.Key == ConsoleKey.Q)
		{
			_isRunning = false;
			return;
		}

		var isMyTurn = _renderLoop.ReadState(s => s.IsMyTurn);
		if (!isMyTurn) return;

		// ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
		switch (key.Key)
		{
			case ConsoleKey.F: // Fold
				await _network.SendActionAsync(ActionType.Fold);
				_renderLoop.AddLog("You folded.");
				_renderLoop.UpdateState(s => s.IsMyTurn = false);
				break;

			case ConsoleKey.C: // Call
				await _network.SendActionAsync(ActionType.Call);
				_renderLoop.AddLog("You called.");
				_renderLoop.UpdateState(s => s.IsMyTurn = false);
				break;

			case ConsoleKey.P: // Pass/Check
				await _network.SendActionAsync(ActionType.Pass);
				_renderLoop.AddLog("You checked/passed.");
				_renderLoop.UpdateState(s => s.IsMyTurn = false);
				break;

			case ConsoleKey.R: // Raise
				var raiseAmount = GetAmountInput("Raise amount: ");
				if (raiseAmount > 0)
				{
					await _network.SendActionAsync(ActionType.Raise, raiseAmount);
					_renderLoop.AddLog($"You raised ${raiseAmount}.");
					_renderLoop.UpdateState(s => s.IsMyTurn = false);
				}
				break;

			case ConsoleKey.B: // Bet
				var betAmount = GetAmountInput("Bet amount: ");
				if (betAmount > 0)
				{
					await _network.SendActionAsync(ActionType.Bet, betAmount);
					_renderLoop.AddLog($"You bet ${betAmount}.");
					_renderLoop.UpdateState(s => s.IsMyTurn = false);
				}
				break;

			case ConsoleKey.A: // All-in
				var chips = _renderLoop.ReadState(s => s.MyChips);
				await _network.SendActionAsync(ActionType.AllIn, chips);
				_renderLoop.AddLog($"You went ALL-IN with ${chips}!");
				_renderLoop.UpdateState(s => s.IsMyTurn = false);
				break;
		}
	}

	private static int GetAmountInput(string prompt)
	{
		Console.SetCursorPosition(35, 30);
		Console.Write(new string(' ', 40));
		Console.SetCursorPosition(35, 30);
		Console.CursorVisible = true;
		Console.Write(prompt);
		
		var input = Console.ReadLine();
		Console.CursorVisible = false;
		
		if (int.TryParse(input, out var amount) && amount > 0)
			return amount;
		
		return 0;
	}

	// 网络事件处理

	private void OnConnected()
	{
		_renderLoop.AddLog("Connected to server!");
	}

	private void OnDisconnected()
	{
		_renderLoop.AddLog("Disconnected from server.");
		_isRunning = false;
	}

	private void OnMessageReceived(ServerMessage message)
	{
		ProcessServerMessage(message);
	}

	private void OnRawMessage(string message)
	{
		// 处理系统消息（非JSON格式）
		if (!message.StartsWith("SYSTEM:")) return;
		_renderLoop.AddLog(message);
	}

	private void OnError(string error)
	{
		_renderLoop.AddLog($"ERROR: {error}");
	}

	private void ProcessServerMessage(ServerMessage message)
	{
		switch (message.Type)
		{
			case ServerMessageType.GameState:
				ParseGameState(message.PayLoad);
				break;

			case ServerMessageType.ActionRequest:
				ParseActionRequest(message.PayLoad);
				break;

			case ServerMessageType.DealCard:
				ParseDealCard(message.PayLoad);
				break;

			case ServerMessageType.GameResult:
				ParseGameResult(message.PayLoad);
				break;

			case ServerMessageType.GameStart:
				_renderLoop.AddLog("Game starting!");
				_renderLoop.UpdateState(s => s.Stage = "PreFlop");
				break;

			case ServerMessageType.StageChanged:
				_renderLoop.AddLog($"Stage changed: {message.PayLoad}");
				break;

			case ServerMessageType.Showdown:
				_renderLoop.AddLog("SHOWDOWN!");
				_renderLoop.UpdateState(s => s.Stage = "Showdown");
				break;

			case ServerMessageType.Error:
				_renderLoop.AddLog($"Server Error: {message.PayLoad}");
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(message));
		}
	}

	private void ParseGameState(object? payload)
	{
		if (payload is not JsonElement je) return;

		_renderLoop.UpdateState(state =>
		{
			if (je.TryGetProperty("Stage", out var stageProp))
				state.Stage = stageProp.GetString() ?? "Waiting";

			if (je.TryGetProperty("Pot", out var potProp))
				state.Pot = potProp.GetInt32();

			if (je.TryGetProperty("Message", out var msgProp))
			{
				var msg = msgProp.GetString();
				if (!string.IsNullOrEmpty(msg))
					state.Message = msg;
			}

			if (!je.TryGetProperty("CommunityCards", out var cardsProp)) return;
			
			state.CommunityCards.Clear();
			foreach (var card in cardsProp.EnumerateArray().Select(ParseCard).OfType<Card>())
			{
				state.CommunityCards.Add(card);
			}
		});

		// 日志在锁外添加
		if (je.TryGetProperty("Message", out var msgProp2))
		{
			var msg = msgProp2.GetString();
			if (!string.IsNullOrEmpty(msg))
				_renderLoop.AddLog(msg);
		}
	}

	private void ParseActionRequest(object? payload)
	{
		if (payload is not JsonElement) return;

		_renderLoop.UpdateState(s => s.IsMyTurn = true);
		_renderLoop.AddLog(">>> YOUR TURN! Press action key...");
	}

	private void ParseDealCard(object? payload)
	{
		if (payload is not JsonElement cardArray) return;

		_renderLoop.UpdateState(state =>
		{
			state.MyHand.Clear();
			foreach (var card in cardArray.EnumerateArray().Select(ParseCard).OfType<Card>())
			{
				state.MyHand.Add(card);
			}
		});
		
		var count = _renderLoop.ReadState(s => s.MyHand.Count);
		_renderLoop.AddLog($"You received {count} cards.");
	}

	private void ParseGameResult(object? payload)
	{
		if (payload is not JsonElement je) return;

		_renderLoop.AddLog("=== GAME RESULT ===");
		foreach (var resultElem in je.EnumerateArray())
		{
			if (!resultElem.TryGetProperty("Name", out var nameProp) ||
			    !resultElem.TryGetProperty("Won", out var wonProp)) continue;
			var name = nameProp.GetString();
			var won = wonProp.GetInt32();
			if (won <= 0) continue;
			_renderLoop.AddLog($"{name} won ${won}!");
			if (name == _playerName)
				_renderLoop.UpdateState(s => s.MyChips += won);
		}
		
		_renderLoop.UpdateState(s =>
		{
			s.Stage = "Finished";
			s.IsMyTurn = false;
		});
	}

	private static Card? ParseCard(JsonElement elem)
	{
		try
		{
			if (elem.TryGetProperty("Suit", out var suitProp) &&
			    elem.TryGetProperty("Rank", out var rankProp))
			{
				return new Card
				{
					Suit = (Suit)suitProp.GetInt32(),
					Rank = (Rank)rankProp.GetInt32()
				};
			}
		}
		catch
		{
			// 解析失败
		}
		return null;
	}

	public void Dispose()
	{
		_renderLoop.Dispose();
		_network?.Dispose();
		GC.SuppressFinalize(this);
	}
}
