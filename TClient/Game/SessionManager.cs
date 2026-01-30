using System.Collections.Concurrent;

namespace TClient.Game;

/// <summary>
/// 管理所有游戏会话
/// </summary>
public class SessionManager
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();

    public GameSession CreateSession()
    {
        var session = new GameSession();
        _sessions[session.SessionId] = session;
        return session;
    }

    public GameSession? GetSession(string sessionId)
    {
        return _sessions.GetValueOrDefault(sessionId);
    }

    public async Task RemoveSessionAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.DisposeAsync();
        }
    }
}
