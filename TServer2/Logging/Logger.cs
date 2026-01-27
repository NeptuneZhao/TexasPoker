using Spectre.Console;

namespace TServer2.Logging;

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}

/// <summary>
/// 日志接口
/// </summary>
public interface ILogger
{
    void Log(string message, LogLevel level = LogLevel.Info);
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Fatal(string message);
}

/// <summary>
/// 美观的控制台和文件日志记录器
/// </summary>
public class GameLogger : ILogger
{
    private static readonly Lock FileLock = new();
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly LogLevel _minLevel;

    public GameLogger(LogLevel minLevel = LogLevel.Debug)
    {
        _minLevel = minLevel;
        _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        _logFilePath = Path.Combine(_logDirectory, $"game_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        
        EnsureLogDirectory();
    }

    private void EnsureLogDirectory()
    {
        if (!Directory.Exists(_logDirectory))
            Directory.CreateDirectory(_logDirectory);
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (level < _minLevel) return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpper().PadRight(5);
        
        // 控制台输出（使用Spectre.Console美化）
        var color = level switch
        {
            LogLevel.Debug => "grey",
            LogLevel.Info => "white",
            LogLevel.Warn => "yellow",
            LogLevel.Error => "red",
            LogLevel.Fatal => "red bold",
            _ => "white"
        };

        var levelColor = level switch
        {
            LogLevel.Debug => "grey",
            LogLevel.Info => "green",
            LogLevel.Warn => "yellow",
            LogLevel.Error => "red",
            LogLevel.Fatal => "red bold",
            _ => "white"
        };

        try
        {
            AnsiConsole.MarkupLine($"[grey]{timestamp}[/] [[{levelColor}]{levelStr}[/]] [{color}]{Markup.Escape(message)}[/]");
        }
        catch
        {
            // 如果Spectre.Console输出失败，回退到普通Console
            Console.WriteLine($"{timestamp} [{levelStr}] {message}");
        }

        // 文件输出
        var logLine = $"{timestamp} [{levelStr}] {message}{Environment.NewLine}";
        WriteToFile(logLine);
    }

    private void WriteToFile(string logLine)
    {
        FileLock.Enter();
        try
        {
            File.AppendAllText(_logFilePath, logLine);
        }
        catch (IOException)
        {
            // 忽略文件写入错误
        }
        finally
        {
            FileLock.Exit();
        }
    }

    public void Debug(string message) => Log(message, LogLevel.Debug);
    public void Info(string message) => Log(message);
    public void Warn(string message) => Log(message, LogLevel.Warn);
    public void Error(string message) => Log(message, LogLevel.Error);
    public void Fatal(string message) => Log(message, LogLevel.Fatal);
}

/// <summary>
/// 全局日志访问器
/// </summary>
public static class Logger
{
    private static ILogger _instance = new GameLogger();

    public static void Initialize(ILogger logger)
    {
        _instance = logger;
    }

    public static void Debug(string message) => _instance.Debug(message);
    public static void Info(string message) => _instance.Info(message);
    public static void Warn(string message) => _instance.Warn(message);
    public static void Error(string message) => _instance.Error(message);
    public static void Fatal(string message) => _instance.Fatal(message);
}
