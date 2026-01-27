using Spectre.Console;
using TServer2.Controller;
using TServer2.Logging;

namespace TServer2;

internal abstract class Program
{
    private const int DefaultPort = 5000;

    public static async Task Main(string[] args)
    {
        // 初始化日志
        Logger.Initialize(new GameLogger());

        // 显示启动横幅
        DisplayBanner();

        // 解析端口参数
        var port = DefaultPort;
        if (args.Length > 0 && int.TryParse(args[0], out var customPort))
        {
            port = customPort;
        }

        Logger.Info("Texas Hold'em Poker Server v1.0");
        Logger.Info($"Starting on port {port}...");
        Logger.Info("Waiting for players to join...");
        Logger.Info("  - Minimum players to start: 4");
        Logger.Info("  - Maximum players: 10");
        Logger.Info("  - Initial chips: 1000");
        Logger.Info("  - Small blind: 2, Big blind: 4");
        Logger.Info("  - Action timeout: 20 seconds");

        // 创建并启动游戏控制器
        await using var controller = new GameRoomController(port);
        
        // 设置优雅关闭
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Logger.Info("Shutdown signal received...");
            cts.Cancel();
        };

        try
        {
            // 启动服务器（这会阻塞直到取消）
            var serverTask = controller.StartAsync();
            
            // 等待取消信号
            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }

            Logger.Info("Shutting down server...");
            await controller.StopAsync();
        }
        catch (Exception ex)
        {
            Logger.Fatal($"Server crashed: {ex.Message}");
            Logger.Error(ex.StackTrace ?? "No stack trace");
            Environment.Exit(1);
        }

        Logger.Info("Server stopped. Goodbye!");
    }

    private static void DisplayBanner()
    {
        try
        {
            AnsiConsole.Write(
                new FigletText("Texas Poker")
                    .LeftJustified()
                    .Color(Color.Green));

            AnsiConsole.Write(new Rule("[yellow]Server[/]").LeftJustified());
            AnsiConsole.WriteLine();
        }
        catch
        {
            // 如果 Spectre.Console 无法渲染，使用简单的文本
            Console.WriteLine("================================");
            Console.WriteLine("   TEXAS HOLD'EM POKER SERVER   ");
            Console.WriteLine("================================");
            Console.WriteLine();
        }
    }
}

