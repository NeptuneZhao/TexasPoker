using System.Text.Json;
using TServer.Logging;
using TServer.Model;
using TServer.Protocol;

namespace TServer.Network;

/// <summary>
/// 消息分发器 - 使用委托字典替代 switch 语句<br/>
/// 使用 Claude Opus 4.5 Plan 提供的设计模式!
/// </summary>
public class MessageDispatcher
{
	// 消息处理委托类型
	public delegate Task MessageHandlerDelegate(ClientHandler handler, ClientMessage message);

	// 处理器映射表
	private readonly Dictionary<ClientMessageType, MessageHandlerDelegate> _handlers = [];

	public MessageDispatcher()
	{
		// 注册所有消息处理器
		Register(ClientMessageType.JoinRoom, HandleJoinRoom);
		Register(ClientMessageType.Ready, HandleReady); // 暂时保留 Ready
		Register(ClientMessageType.PlayerAction, HandlePlayerAction);
		Register(ClientMessageType.ShowHand, HandleShowHand);
		Register(ClientMessageType.FoldAtShowdown, HandleFoldAtShowdown);
		Register(ClientMessageType.Chat, HandleChat);
	}

	/// <summary>
	/// 注册消息处理器
	/// </summary>
	public void Register(ClientMessageType type, MessageHandlerDelegate handler) => _handlers[type] = handler;

	/// <summary>
	/// 分发消息到对应处理器
	/// </summary>
	public async Task DispatchAsync(ClientHandler handler, ClientMessage message)
	{
		if (_handlers.TryGetValue(message.Type, out var messageHandler))
			await messageHandler(handler, message);
		else
		{
			Logger.Log("Unexpected Exception on message dispatching", LogLevel.Fatal);
			throw new NotSupportedException($"Unknown message type: {message.Type}");
		}
	}

	// ============ 各类消息处理方法 ============

	private static async Task HandleJoinRoom(ClientHandler handler, ClientMessage message)
	{
		// 防止重复加入
		if (handler.Server.Game.Players.Contains(handler.ThisPlayer))
		{
			await handler.SendMessageAsync(new ServerMessage
			{
				Type = ServerMessageType.Error,
				PayLoad = new { Message = "You are already in the game." }
			});
			return;
		}
		
		// 游戏进行中不允许加入
		if (handler.Server.Game.Stage != Game.GameStage.Waiting && 
		    handler.Server.Game.Stage != Game.GameStage.Finished)
		{
			await handler.SendMessageAsync(new ServerMessage
			{
				Type = ServerMessageType.Error,
				PayLoad = new { Message = "Game is in progress. Please wait for the next round." }
			});
			return;
		}
		
		var name = message.PayLoad?.ToString();
        if (message.PayLoad is JsonElement { ValueKind: JsonValueKind.String } je)
        {
            name = je.GetString();
        }

		handler.ThisPlayer.Name = name ?? $"Player{Guid.NewGuid().ToString()[..5]}";
		handler.ThisPlayer.Chips = 1000; // 初始筹码
		
		handler.Server.Game.AddPlayer(handler.ThisPlayer);
		Logger.Log($"{handler.ThisPlayer.Name} joined the game.");
		
		await handler.Server.BroadcastStringAsync($"SYSTEM: {handler.ThisPlayer.Name} joined.");

		// 自动开始游戏（当有足够玩家时）
		if (handler.Server.Game is { Stage: Game.GameStage.Waiting, Players.Count: >= 4 })
		{
			Logger.Log("Auto-starting game...");
			await handler.Server.Game.StartGameAsync();
		}
	}

	private static async Task HandleReady(ClientHandler handler, ClientMessage message)
	{
		// 或者用于重新买入
		await Task.CompletedTask;
	}

	private static async Task HandlePlayerAction(ClientHandler handler, ClientMessage message)
	{
		var amount = message.PayLoad switch
		{
			JsonElement { ValueKind: JsonValueKind.Number } je => je.GetInt32(),
			int i => i,
			string s when int.TryParse(s, out var parsed) => parsed,
			_ => 0
		};

		await handler.Server.Game.HandleActionAsync(handler.ThisPlayer, message.Action, amount);
	}

	private static Task HandleShowHand(ClientHandler handler, ClientMessage message)
	{
		// 客户端亮牌请求（通常发生在Showdown后）
		return Task.CompletedTask;
	}

	private static Task HandleFoldAtShowdown(ClientHandler handler, ClientMessage message)
	{
		return Task.CompletedTask;
	}

	private static async Task HandleChat(ClientHandler handler, ClientMessage message)
	{
		var txt = message.PayLoad?.ToString();
		await handler.Server.BroadcastStringAsync($"CHAT {handler.ThisPlayer.Name}: {txt}");
	}
}