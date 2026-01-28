using Spectre.Console;

namespace TClient;

internal abstract class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "♠ Texas Hold'em Poker Client ♥";

        try
        {
            await using var client = new GameClient();
            await client.RunAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.Clear();
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
            AnsiConsole.MarkupLine("\n[dim]按任意键退出...[/]");
            Console.ReadKey(true);
        }
    }
}