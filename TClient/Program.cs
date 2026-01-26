namespace TClient;

internal abstract class Program
{
	public static async Task Main(string[] args)
	{
		Console.OutputEncoding = System.Text.Encoding.UTF8;
		Console.Title = "♠ Texas Hold'em Poker Client ♥";

		try
		{
			using var client = new GameClient();
			await client.RunAsync();
		}
		catch (Exception ex)
		{
			Console.Clear();
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"Fatal error: {ex.Message}");
			Console.ResetColor();
			Console.WriteLine("\nPress any key to exit...");
			Console.ReadKey();
		}
	}
}