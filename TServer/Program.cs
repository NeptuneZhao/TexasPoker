namespace TServer;

internal abstract class Program
{
	public static async Task Main(string[] args)
	{
		Console.WriteLine("Hello, World!");
		await new Server().StartAsync();
	}
}