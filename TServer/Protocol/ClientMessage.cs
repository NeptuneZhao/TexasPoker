namespace TServer.Protocol;

[Serializable]
public class ClientMessage
{
	public ClientMessageType Type { get; set; }
	public int PlayerId { get; set; }
	public ActionType Action { get; set; }
	public object? PayLoad { get; set; }
}

public enum ClientMessageType
{
	JoinRoom,
	Ready,
	PlayerAction, // 跟着 ActionType
	ShowHand,
	FoldAtShowdown,
	Chat
}

public enum ActionType
{
	Fold,  // 弃牌
	Pass,  // 过牌
	Call,  // 跟注
	Bet,   // 下注
	Raise, // 加注
	AllIn, // 全下
}