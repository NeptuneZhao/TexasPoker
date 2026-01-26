using System.Text;

namespace TServer.Logging;

public static class Logger
{
	private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
	private static readonly string LogFilePath = Path.Combine(LogDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}.log");
	private static readonly Lock Lock = new();
	
	private static void Log(string msg, string level = "info")
	{
		var sb = new StringBuilder();
		sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ");
		sb.Append($"[{level.ToUpper()}] ");
		sb.Append(msg);
		var logMessage = sb.ToString();
		
		Lock.Enter();
		try
		{
			Console.WriteLine(logMessage);
			
			if (!Directory.Exists(LogDirectory))
				Directory.CreateDirectory(LogDirectory);
			File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
		}
		catch (IOException ie)
		{
			throw new IOException(ie.Message, ie);
		}
		finally
		{
			Lock.Exit();
		}
	}
	
	public static void Log(string msg, LogLevel lvl = LogLevel.Info)
	{
		Log(msg, lvl.ToString());
	}
}

public enum LogLevel
{
	Info,
	Warn,
	Error,
	Fatal
}