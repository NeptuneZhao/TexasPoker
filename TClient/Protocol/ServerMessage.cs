namespace TClient.Protocol;

public class ServerMessage
{
	public ServerMessageType Type { get; init; }
	public object? PayLoad { get; init; }
}

public enum ServerMessageType
{
	GameStart,
	DealCard,
	GameState,
	ActionRequest,
	StageChanged,
	Showdown,
	GameResult,
	Error
}
