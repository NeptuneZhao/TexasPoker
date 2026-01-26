using TClient.Model;

namespace TClient.UI;

/// <summary>
/// 渲染循环管理器 - 将渲染逻辑与业务逻辑分离
/// 在独立线程中以固定间隔进行渲染
/// </summary>
public class RenderLoop(GameState state) : IDisposable
{
	private readonly ConsoleRenderer _renderer = new();
	private readonly Lock _stateLock = new();
	
	private CancellationTokenSource? _cts;
	private Task? _renderTask;
	
	/// <summary>
	/// 渲染间隔（毫秒）
	/// </summary>
	public int RenderIntervalMs { get; init; } = 100;
	
	/// <summary>
	/// 是否正在运行
	/// </summary>
	private bool IsRunning => _renderTask is { IsCompleted: false };

	/// <summary>
	/// 启动渲染循环
	/// </summary>
	public void Start()
	{
		if (IsRunning) return;
		
		ConsoleRenderer.Initialize();
		_cts = new CancellationTokenSource();
		_renderTask = Task.Run(() => RenderLoopAsync(_cts.Token));
	}

	/// <summary>
	/// 停止渲染循环
	/// </summary>
	public async Task StopAsync()
	{
		if (_cts == null) return;
		
		await _cts.CancelAsync();
		if (_renderTask != null)
		{
			try
			{
				await _renderTask;
			}
			catch (OperationCanceledException)
			{
				// 正常取消
			}
		}
		
		_cts.Dispose();
		_cts = null;
		_renderTask = null;
	}

	/// <summary>
	/// 添加日志消息（线程安全）
	/// </summary>
	public void AddLog(string message, ConsoleColor color = ConsoleColor.Gray)
	{
		lock (_stateLock)
		{
			_renderer.AddLog(message, color);
		}
	}

	/// <summary>
	/// 更新游戏状态（线程安全）
	/// </summary>
	public void UpdateState(Action<GameState> updateAction)
	{
		lock (_stateLock)
		{
			updateAction(state);
		}
	}

	/// <summary>
	/// 读取游戏状态（线程安全）
	/// </summary>
	public T ReadState<T>(Func<GameState, T> readFunc)
	{
		lock (_stateLock)
		{
			return readFunc(state);
		}
	}

	/// <summary>
	/// 强制立即渲染一帧
	/// </summary>
	public void RenderNow()
	{
		lock (_stateLock)
		{
			_renderer.RenderAll(state);
		}
	}

	private async Task RenderLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				lock (_stateLock)
				{
					_renderer.RenderAll(state);
				}
				
				await Task.Delay(RenderIntervalMs, ct);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	public void Dispose()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		GC.SuppressFinalize(this);
	}
}
