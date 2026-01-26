using TServer.Logging;
using TServer.Model;
using TServer.Protocol;

namespace TServer.Game;

public class BettingRound
{
	public int HighestBet; // 本轮当前最高下注
	private int _minRaise; // 最小加注

	public void StartRound(int bigBlind)
	{
		HighestBet = 0;
		_minRaise = bigBlind; 
	}
	
	/// <summary>
	/// 验证并执行玩家下注逻辑
	/// </summary>
	public bool HandleBet(Player player, int amount, ActionType type)
	{
		// amount: Protocol usually sends "Total Bet Amount" for Raise/Bet.
		
		var neededToCall = HighestBet - player.CurrentBet;
        var chipsAvailable = player.Chips;

		switch (type)
		{
			case ActionType.Fold:
				player.Folded = true;
				return true;

			case ActionType.Pass: // Check
				return neededToCall <= 0; // 只有在平注时才能 Check

			case ActionType.Call:
				// 跟注：投入差额，或者 All In
				var actualCall = Math.Min(neededToCall, chipsAvailable);
				player.Chips -= actualCall;
				player.CurrentBet += actualCall;
				
				if (player.Chips == 0) player.AllIn = true;
				return true;

			case ActionType.Raise:
			case ActionType.Bet:
				// 假设 amount 是总下注额 (Total CurrentBet)
				
				if (amount > player.Chips + player.CurrentBet) return false; // 钱不够
				if (amount < HighestBet + _minRaise && amount < player.Chips + player.CurrentBet) 
				{
					// 加注必须满足最小加注额
					// 除非 All-In
					return false;
				}

				var increment = amount - player.CurrentBet;
				var raiseSize = amount - HighestBet;
				
				player.Chips -= increment;
				player.CurrentBet = amount;
				
				if (raiseSize > 0)
				{
					// 更新最小加注额 (Raise - ReRaise)
					if (raiseSize > _minRaise) _minRaise = raiseSize;
				}

				HighestBet = amount;
				if (player.Chips == 0) player.AllIn = true;
				
				return true;
				
			case ActionType.AllIn:
				// 全下
				var allInAmt = player.Chips;
				var newTotal = player.CurrentBet + allInAmt;
				
				player.Chips = 0;
				player.CurrentBet = newTotal;
				player.AllIn = true;
				
				// 检查是否构成了有效加注
				if (newTotal <= HighestBet) return true;
				
				var actualRaise = newTotal - HighestBet;
				if (actualRaise >= _minRaise) _minRaise = actualRaise; // 只有完整加注才更新权重
				HighestBet = newTotal;
				return true;
			
			default:
				Logger.Log("Unknown action type in HandleBet", LogLevel.Error);
				throw new ArgumentOutOfRangeException(nameof(type), "Unknown action type");
		}
	}
}
