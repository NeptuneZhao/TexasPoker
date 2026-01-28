using Spectre.Console;
using Spectre.Console.Rendering;
using TClient.Model;

namespace TClient.UI;

/// <summary>
/// 使用Spectre.Console的游戏渲染器
/// </summary>
public class SpectreRenderer
{
    private readonly Lock _lock = new();
    private readonly List<LogEntry> _logs = [];
    private const int MaxLogs = 8;

    /// <summary>
    /// 添加日志
    /// </summary>
    public void AddLog(string message, string style = "grey")
    {
        lock (_lock)
        {
            _logs.Add(new LogEntry(DateTime.Now, message, style));
            while (_logs.Count > MaxLogs)
                _logs.RemoveAt(0);
        }
    }

    /// <summary>
    /// 构建完整的游戏界面
    /// </summary>
    public Layout BuildLayout(GameState state)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Main").SplitColumns(
                    new Layout("Left").Size(70),
                    new Layout("Right").Size(35)
                ),
                new Layout("Footer").Size(5)
            );

        // 头部标题
        layout["Header"].Update(BuildHeader(state));

        // 左侧主区域
        var leftLayout = new Layout("LeftContent")
            .SplitRows(
                new Layout("Table").Size(14),
                new Layout("Hand").Size(8),
                new Layout("Logs")
            );

        leftLayout["Table"].Update(BuildTablePanel(state));
        leftLayout["Hand"].Update(BuildHandPanel(state));
        leftLayout["Logs"].Update(BuildLogsPanel());
        
        layout["Left"].Update(leftLayout);

        // 右侧信息区域
        var rightLayout = new Layout("RightContent")
            .SplitRows(
                new Layout("Players"),
                new Layout("Actions").Size(10)
            );

        rightLayout["Players"].Update(BuildPlayersPanel(state));
        rightLayout["Actions"].Update(BuildActionsPanel(state));
        
        layout["Right"].Update(rightLayout);

        // 底部状态栏
        layout["Footer"].Update(BuildFooter(state));

        return layout;
    }

    /// <summary>
    /// 构建头部标题
    /// </summary>
    private static Panel BuildHeader(GameState state)
    {
        var title = new Rule("[bold yellow]♠ ♥ TEXAS HOLD'EM POKER ♦ ♣[/]")
        {
            Justification = Justify.Center,
            Style = Style.Parse("yellow")
        };

        var grid = new Grid();
        grid.AddColumn();
        grid.AddRow(title);

        var phaseColor = state.Phase switch
        {
            "Waiting" => "grey",
            "Countdown" => "yellow",
            "PreFlop" or "Flop" or "Turn" or "River" => "green",
            "Showdown" => "cyan",
            "Settlement" => "blue",
            "GameOver" => "red",
            _ => "white"
        };

        var statusText = state.IsCountingDown
            ? $"[{phaseColor}]● {state.Phase}[/] | [yellow]开始倒计时: {state.CountdownSeconds}s[/]"
            : $"[{phaseColor}]● {state.Phase}[/] | 第 {state.HandNumber} 手";

        if (state.IsMyTurn)
            statusText += " | [blink bold green]>>> 轮到你行动 <<<[/]";

        grid.AddRow(new Markup(statusText));

        return new Panel(grid)
            .Border(BoxBorder.Double)
            .BorderColor(Color.Yellow)
            .Padding(0, 0);
    }

    /// <summary>
    /// 构建桌面面板
    /// </summary>
    private static Panel BuildTablePanel(GameState state)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap());

        // 公共牌
        grid.AddRow(new Markup("[bold cyan]公共牌[/]"));
        grid.AddRow(BuildCommunityCards(state.CommunityCards));

        // 底池信息
        grid.AddEmptyRow();
        var potText = BuildPotText(state.Pots);
        grid.AddRow(potText);

        // 位置信息
        if (state.DealerSeatIndex >= 0)
        {
            grid.AddEmptyRow();
            var positionText = $"[dim]D: 座位{state.DealerSeatIndex}[/] | " +
                               $"[dim]SB: 座位{state.SmallBlindSeatIndex}[/] | " +
                               $"[dim]BB: 座位{state.BigBlindSeatIndex}[/]";
            grid.AddRow(new Markup(positionText));
        }

        // 当前下注信息
        if (state.CurrentBet > 0)
        {
            grid.AddRow(new Markup($"[yellow]当前最高下注: ${state.CurrentBet}[/]"));
        }

        return new Panel(grid)
            .Header("[bold green]🎴 牌桌[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green)
            .Expand();
    }

    /// <summary>
    /// 构建公共牌显示
    /// </summary>
    private static Table BuildCommunityCards(List<Card> cards)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders();

        for (var i = 0; i < 5; i++)
            table.AddColumn(new TableColumn("").Width(8));

        var row = new List<IRenderable>();
        for (var i = 0; i < 5; i++)
        {
            row.Add(i < cards.Count ? BuildCardDisplay(cards[i]) : BuildEmptyCard());
        }

        table.AddRow(row);
        return table;
    }

    /// <summary>
    /// 构建单张牌显示
    /// </summary>
    private static IRenderable BuildCardDisplay(Card card)
    {
        var color = card.IsRed ? "red" : "white";
        var cardText = $"[bold {color} on grey23] {card.Display,-3} [/]";
        return new Markup(cardText);
    }

    /// <summary>
    /// 构建空牌位
    /// </summary>
    private static IRenderable BuildEmptyCard()
    {
        return new Markup("[dim on grey15] ??? [/]");
    }

    /// <summary>
    /// 构建底池文本
    /// </summary>
    private static IRenderable BuildPotText(List<PotInfo> pots)
    {
        if (pots.Count == 0)
            return new Markup("[dim]底池: $0[/]");

        var total = pots.Sum(p => p.Amount);
        var text = $"[bold yellow]💰 总底池: ${total}[/]";

        if (pots.Count > 1)
        {
            var details = string.Join(" | ", pots.Select(p => $"{p.Name}: ${p.Amount}"));
            text += $"\n[dim]({details})[/]";
        }

        return new Markup(text);
    }

    /// <summary>
    /// 构建手牌面板
    /// </summary>
    private static IRenderable BuildHandPanel(GameState state)
    {
        var grid = new Grid();
        grid.AddColumn();

        // 手牌显示
        var handTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders();

        handTable.AddColumn(new TableColumn("").Width(10));
        handTable.AddColumn(new TableColumn("").Width(10));

        switch (state.MyHand.Count)
        {
            case >= 2:
                handTable.AddRow(
                    BuildCardDisplay(state.MyHand[0]),
                    BuildCardDisplay(state.MyHand[1])
                );
                break;
            case 1:
                handTable.AddRow(
                    BuildCardDisplay(state.MyHand[0]),
                    BuildEmptyCard()
                );
                break;
            default:
                handTable.AddRow(BuildEmptyCard(), BuildEmptyCard());
                break;
        }

        grid.AddRow(handTable);

        // 筹码信息
        grid.AddEmptyRow();
        var chipsText = $"[bold yellow]筹码: ${state.MyChips}[/]";
        if (state is { CallAmount: > 0, IsMyTurn: true })
        {
            chipsText += $"  |  [cyan]跟注需: ${state.CallAmount}[/]";
        }
        grid.AddRow(new Markup(chipsText));

        return new Panel(grid)
            .Header($"[bold blue]🎴 你的手牌 ({state.MyPlayerName}) - 座位 {state.MySeatIndex}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue)
            .Expand();
    }

    /// <summary>
    /// 构建玩家列表面板
    /// </summary>
    private static IRenderable BuildPlayersPanel(GameState state)
    {
        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[bold]座位[/]").Width(4))
            .AddColumn(new TableColumn("[bold]玩家[/]").Width(10))
            .AddColumn(new TableColumn("[bold]筹码[/]").Width(8))
            .AddColumn(new TableColumn("[bold]下注[/]").Width(6))
            .AddColumn(new TableColumn("[bold]状态[/]").Width(8));

        foreach (var player in state.Players.OrderBy(p => p.SeatIndex))
        {
            var seatText = player.SeatIndex.ToString();
            
            // 位置标记
            if (player.SeatIndex == state.DealerSeatIndex)
                seatText += "[yellow]D[/]";
            else if (player.SeatIndex == state.SmallBlindSeatIndex)
                seatText += "[dim]S[/]";
            else if (player.SeatIndex == state.BigBlindSeatIndex)
                seatText += "[dim]B[/]";

            var nameStyle = player.Id == state.MyPlayerId ? "bold cyan" : "white";
            var isActing = player.Id == state.CurrentActingPlayerId;
            if (isActing)
                nameStyle = "bold green";

            var nameText = $"[{nameStyle}]{player.Name}[/]";
            if (isActing)
                nameText = "▶ " + nameText;

            var chipsText = $"${player.Chips}";
            var betText = player.CurrentBet > 0 ? $"${player.CurrentBet}" : "-";

            string statusText;
            if (player.HasFolded)
                statusText = "[dim grey]弃牌[/]";
            else if (player.IsAllIn)
                statusText = "[bold red]ALL-IN[/]";
            else if (!player.IsConnected)
                statusText = "[dim red]离线[/]";
            else
                statusText = "[green]在场[/]";

            // 如果有亮牌
            if (player.ShownCards is { Count: > 0 })
            {
                var cardsStr = string.Join(" ", player.ShownCards.Select(c => c.Display));
                nameText += $"\n[dim]{cardsStr}[/]";
                if (!string.IsNullOrEmpty(player.HandRank))
                    statusText += $"\n[cyan]{player.HandRank}[/]";
            }

            table.AddRow(
                new Markup(seatText),
                new Markup(nameText),
                new Markup(chipsText),
                new Markup(betText),
                new Markup(statusText)
            );
        }

        return new Panel(table)
            .Header("[bold magenta]👥 玩家列表[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Fuchsia)
            .Expand();
    }

    /// <summary>
    /// 构建操作面板
    /// </summary>
    private static Panel BuildActionsPanel(GameState state)
    {
        var grid = new Grid();
        grid.AddColumn();

        if (state.IsShowdownRequest)
        {
            grid.AddRow(new Markup("[bold cyan]摊牌选择:[/]"));
            grid.AddRow(new Markup("  [bold green][S][/] 亮牌"));
            grid.AddRow(new Markup("  [bold red][M][/] 盖牌"));
        }
        else if (state is { IsMyTurn: true, AvailableActions.Count: > 0 })
        {
            grid.AddRow(new Markup("[bold green]可用操作:[/]"));
            foreach (var action in state.AvailableActions)
            {
                var key = GetActionKey(action.Type);
                var desc = GetActionDescription(action);
                grid.AddRow(new Markup($"  [bold yellow][{key}][/] {desc}"));
            }
        }
        else
        {
            grid.AddRow(new Markup("[dim]等待其他玩家...[/]"));
        }

        grid.AddEmptyRow();
        grid.AddRow(new Markup("[dim][red] 退出游戏[/][/]"));

        return new Panel(grid)
            .Header("[bold yellow]⌨️ 操作[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Expand();
    }

    /// <summary>
    /// 构建日志面板
    /// </summary>
    private Panel BuildLogsPanel()
    {
        var grid = new Grid();
        grid.AddColumn();

        lock (_lock)
        {
            foreach (var log in _logs)
            {
                var time = log.Time.ToString("HH:mm:ss");
                grid.AddRow(new Markup($"[dim]{time}[/] [{log.Style}]{Markup.Escape(log.Message)}[/]"));
            }
        }

        // 填充空行
        lock (_lock)
        {
            for (int i = _logs.Count; i < MaxLogs; i++)
            {
                grid.AddEmptyRow();
            }
        }

        return new Panel(grid)
            .Header("[bold cyan]📜 日志[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Aqua)
            .Expand();
    }

    /// <summary>
    /// 构建底部状态栏
    /// </summary>
    private static IRenderable BuildFooter(GameState state)
    {
        var grid = new Grid();
        grid.AddColumn();

        if (!string.IsNullOrEmpty(state.LastMessage))
        {
            grid.AddRow(new Markup($"[bold]{Markup.Escape(state.LastMessage)}[/]"));
        }

        grid.AddRow(new Markup("[dim]按键操作: [[F]]弃牌 [[C]]跟注 [[K]]过牌 [[B]]下注 [[R]]加注 [[A]]全下 | [[S]]亮牌 [[M]]盖牌 | [[Q]]退出[/]"));

        return new Panel(grid)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Padding(1, 0);
    }

    /// <summary>
    /// 获取操作对应的按键
    /// </summary>
    private static string GetActionKey(string actionType)
    {
        return actionType.ToLower() switch
        {
            "fold" => "F",
            "check" => "K",
            "call" => "C",
            "bet" => "B",
            "raise" => "R",
            "allin" => "A",
            _ => "?"
        };
    }

    /// <summary>
    /// 获取操作描述
    /// </summary>
    private static string GetActionDescription(AvailableActionInfo action)
    {
        return action.Type.ToLower() switch
        {
            "fold" => "弃牌",
            "check" => "过牌",
            "call" => $"跟注 (${action.MinAmount ?? 0})",
            "bet" => $"下注 (${action.MinAmount ?? 0}-${action.MaxAmount ?? 0})",
            "raise" => $"加注 (${action.MinAmount ?? 0}-${action.MaxAmount ?? 0})",
            "allin" => $"全下 (${action.MaxAmount ?? 0})",
            _ => action.Description
        };
    }

    private record LogEntry(DateTime Time, string Message, string Style);
}
