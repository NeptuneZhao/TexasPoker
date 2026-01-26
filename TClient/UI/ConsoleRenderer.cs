namespace TClient.UI;

/// <summary>
/// TUI
/// </summary>
public class ConsoleRenderer
{
	// 控制台
	private const int ConsoleWidth = 100;
	private const int ConsoleHeight = 35;

	// 分区
	private static readonly Region TitleRegion = new(0, 0, ConsoleWidth, 3);
	private static readonly Region TableRegion = new(0, 3, ConsoleWidth, 12);
	private static readonly Region HandRegion = new(0, 15, 50, 6);
	private static readonly Region InfoRegion = new(50, 15, 50, 6);
	private static readonly Region LogRegion = new(0, 21, ConsoleWidth, 8);
	private static readonly Region InputRegion = new(0, 29, ConsoleWidth, 4);

	private readonly List<string> _logMessages = [];
	private const int MaxLogLines = 6;

	public static void Initialize()
	{
		Console.Clear();
		Console.CursorVisible = false;
		try
		{
			if (!OperatingSystem.IsWindows()) return;
			
			Console.SetWindowSize(ConsoleWidth, ConsoleHeight);
			Console.SetBufferSize(ConsoleWidth, ConsoleHeight);
		}
		catch
		{
			// 某些终端不支持设置窗口大小
		}
	}

	public void RenderAll(Model.GameState state, string inputPrompt = "")
	{
		Console.CursorVisible = false;
		RenderTitle();
		RenderTable(state);
		RenderHand(state);
		RenderInfo(state);
		RenderLog();
		RenderInput(inputPrompt, state);
	}

	public void AddLog(string message, ConsoleColor color = ConsoleColor.Gray)
	{
		var timestamp = DateTime.Now.ToString("HH:mm:ss");
		_logMessages.Add($"[{timestamp}] {message}");
		if (_logMessages.Count > MaxLogLines)
			_logMessages.RemoveAt(0);
	}

	#region 渲染各区域

	private static void RenderTitle()
	{
		DrawBox(TitleRegion, ConsoleColor.Yellow, "🎰 TEXAS HOLD'EM POKER 🎰");
		SetCursorInRegion(TitleRegion, 1, 1);
		WriteColored("═══════════════════════════════════════════════════════════════════════════════════════════════════", ConsoleColor.DarkYellow);
	}

	private static void RenderTable(Model.GameState state)
	{
		DrawBox(TableRegion, ConsoleColor.Green, "🃏 TABLE");

		// 显示游戏阶段
		SetCursorInRegion(TableRegion, 2, 1);
		WriteColored("Stage: ", ConsoleColor.White);
		WriteColored(state.Stage, GetStageColor(state.Stage));

		// 显示底池
		SetCursorInRegion(TableRegion, 2, 2);
		WriteColored("Pot: ", ConsoleColor.White);
		WriteColored($"${state.Pot}", ConsoleColor.Yellow);

		// 显示公共牌
		SetCursorInRegion(TableRegion, 2, 4);
		WriteColored("Community Cards: ", ConsoleColor.Cyan);
		
		SetCursorInRegion(TableRegion, 2, 5);
		if (state.CommunityCards.Count == 0)
		{
			WriteColored("[  ?  ] [  ?  ] [  ?  ] [  ?  ] [  ?  ]", ConsoleColor.DarkGray);
		}
		else
		{
			foreach (var card in state.CommunityCards)
			{
				WriteCard(card);
				Console.Write(" ");
			}
			// 填充剩余的空位
			for (var i = state.CommunityCards.Count; i < 5; i++)
			{
				WriteColored("[  ?  ] ", ConsoleColor.DarkGray);
			}
		}

		// 显示消息
		if (!string.IsNullOrEmpty(state.Message))
		{
			SetCursorInRegion(TableRegion, 2, 7);
			WriteColored($"📢 {state.Message}", ConsoleColor.Magenta);
		}

		// 显示玩家列表
		SetCursorInRegion(TableRegion, 2, 9);
		WriteColored("Players: ", ConsoleColor.White);
		var x = 11;
		foreach (var player in state.Players)
		{
			SetCursorInRegion(TableRegion, x, 9);
			var color = player.IsCurrentPlayer ? ConsoleColor.Green : 
			            player.Folded ? ConsoleColor.DarkGray : ConsoleColor.White;
			var status = player.Folded ? "[FOLD]" : player.AllIn ? "[ALL-IN]" : $"${player.CurrentBet}";
			WriteColored($"{player.Name}({status}) ", color);
			x += player.Name.Length + status.Length + 4;
		}
	}

	private static void RenderHand(Model.GameState state)
	{
		DrawBox(HandRegion, ConsoleColor.Blue, "🎴 YOUR HAND");

		SetCursorInRegion(HandRegion, 2, 2);
		if (state.MyHand.Count == 0)
		{
			WriteColored("[  ?  ] [  ?  ]", ConsoleColor.DarkGray);
		}
		else
		{
			foreach (var card in state.MyHand)
			{
				WriteCard(card);
				Console.Write("  ");
			}
		}

		SetCursorInRegion(HandRegion, 2, 4);
		WriteColored($"Chips: ${state.MyChips}", ConsoleColor.Yellow);
		Console.Write("  ");
		WriteColored($"Bet: ${state.MyCurrentBet}", ConsoleColor.Cyan);
	}

	private static void RenderInfo(Model.GameState state)
	{
		DrawBox(InfoRegion, ConsoleColor.Magenta, "📊 INFO");

		SetCursorInRegion(InfoRegion, 2, 1);
		WriteColored("Available Actions:", ConsoleColor.White);

		SetCursorInRegion(InfoRegion, 2, 2);
		if (state.IsMyTurn)
		{
			WriteColored("[F]old  ", ConsoleColor.Red);
			WriteColored("[C]all  ", ConsoleColor.Green);
			WriteColored("[R]aise  ", ConsoleColor.Yellow);
			SetCursorInRegion(InfoRegion, 2, 3);
			WriteColored("[P]ass  ", ConsoleColor.Cyan);
			WriteColored("[A]ll-in  ", ConsoleColor.Magenta);
			WriteColored("[B]et", ConsoleColor.Blue);
		}
		else
		{
			WriteColored("Waiting for other players...", ConsoleColor.DarkGray);
		}
	}

	private void RenderLog()
	{
		DrawBox(LogRegion, ConsoleColor.DarkCyan, "📜 LOG");

		for (var i = 0; i < MaxLogLines; i++)
		{
			SetCursorInRegion(LogRegion, 2, i + 1);
			ClearLine(LogRegion.Width - 4);
			if (i >= _logMessages.Count) continue;
			
			var msg = _logMessages[i];
			if (msg.Length > LogRegion.Width - 4)
				msg = msg[..(LogRegion.Width - 7)] + "...";
			WriteColored(msg, ConsoleColor.Gray);
		}
	}

	private static void RenderInput(string prompt, Model.GameState state)
	{
		DrawBox(InputRegion, ConsoleColor.White, "⌨️  INPUT");

		SetCursorInRegion(InputRegion, 2, 1);
		ClearLine(InputRegion.Width - 4);
		
		if (state.IsMyTurn)
		{
			WriteColored(">> Your turn! Enter command: ", ConsoleColor.Green);
		}
		else
		{
			WriteColored(">> ", ConsoleColor.DarkGray);
		}
		
		if (!string.IsNullOrEmpty(prompt))
		{
			WriteColored(prompt, ConsoleColor.Yellow);
		}
	}

	#endregion

	#region 辅助方法

	private static void DrawBox(Region region, ConsoleColor borderColor, string title)
	{
		Console.ForegroundColor = borderColor;

		// 顶部边框
		Console.SetCursorPosition(region.X, region.Y);
		Console.Write("╔" + new string('═', region.Width - 2) + "╗");

		// 标题
		if (!string.IsNullOrEmpty(title))
		{
			Console.SetCursorPosition(region.X + 2, region.Y);
			Console.Write($" {title} ");
		}

		// 侧边框
		for (var y = 1; y < region.Height - 1; y++)
		{
			Console.SetCursorPosition(region.X, region.Y + y);
			Console.Write("║");
			Console.Write(new string(' ', region.Width - 2));
			Console.Write("║");
		}

		// 底部边框
		Console.SetCursorPosition(region.X, region.Y + region.Height - 1);
		Console.Write("╚" + new string('═', region.Width - 2) + "╝");

		Console.ResetColor();
	}

	private static void SetCursorInRegion(Region region, int localX, int localY)
	{
		Console.SetCursorPosition(region.X + localX, region.Y + localY);
	}

	private static void WriteColored(string text, ConsoleColor color)
	{
		Console.ForegroundColor = color;
		Console.Write(text);
		Console.ResetColor();
	}

	private static void WriteCard(Model.Card card)
	{
		Console.Write("[");
		Console.ForegroundColor = card.GetColor();
		var cardStr = card.ToString().PadRight(4);
		Console.Write($" {cardStr}");
		Console.ResetColor();
		Console.Write("]");
	}

	private static void ClearLine(int width)
	{
		Console.Write(new string(' ', width));
		Console.CursorLeft -= width;
	}

	private static ConsoleColor GetStageColor(string stage) => stage switch
	{
		"Waiting" => ConsoleColor.DarkGray,
		"PreFlop" => ConsoleColor.Yellow,
		"Flop" => ConsoleColor.Green,
		"Turn" => ConsoleColor.Cyan,
		"River" => ConsoleColor.Blue,
		"Showdown" => ConsoleColor.Magenta,
		"Finished" => ConsoleColor.Red,
		_ => ConsoleColor.White
	};

	#endregion
}

public record Region(int X, int Y, int Width, int Height);
