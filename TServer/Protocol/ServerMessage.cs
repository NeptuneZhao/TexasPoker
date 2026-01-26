namespace TServer.Protocol;

public class ServerMessage
{
	public ServerMessageType Type;
	public object? PayLoad; // JSON Serializable object
}

public enum ServerMessageType
{
	GameStart,     // 新一轮开始
	DealCard,      // 发牌
	GameState,     // 当前公共状态
	ActionRequest, // 请求玩家行动
	StageChanged,  // 翻牌 / 转牌 / 河牌
	Showdown,      // 摊牌
	GameResult,    // 结算
	Error          // 非法操作
}
