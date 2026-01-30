using TServer.Logging;
using TServer.Model;
using TServer.Protocol;

namespace TServer.Game;

/// <summary>
/// 下注轮管理器
/// </summary>
public class BettingRound
{
    private readonly Lock _lock = new();
    
    /// <summary>
    /// 当前轮最高下注
    /// </summary>
    public int CurrentBet { get; private set; }
    
    /// <summary>
    /// 最小加注额
    /// </summary>
    public int MinRaise { get; private set; }
    
    /// <summary>
    /// 大盲金额
    /// </summary>
    public int BigBlindAmount { get; private set; }
    
    /// <summary>
    /// 本轮是否有有效加注
    /// </summary>
    public bool HasRaiseThisRound { get; private set; }

    /// <summary>
    /// 开始新的下注轮
    /// </summary>
    public void StartRound(int bigBlind, int initialBet = 0)
    {
        _lock.Enter();
        try
        {
            BigBlindAmount = bigBlind;
            CurrentBet = initialBet;
            MinRaise = bigBlind;
            HasRaiseThisRound = false;
        }
        finally
        {
            _lock.Exit();
        }
        
        Logger.Debug($"Betting round started. Big blind: {bigBlind}, Initial bet: {initialBet}");
    }

    /// <summary>
    /// 获取玩家可用的行动
    /// </summary>
    public List<AvailableAction> GetAvailableActions(Player player)
    {
        var actions = new List<AvailableAction>();
        var callAmount = CurrentBet - player.CurrentBet;
        var canCheck = callAmount <= 0;
        var playerChips = player.Chips;

        // 弃牌 - 总是可以
        actions.Add(new AvailableAction(ActionType.Fold, "Fold"));

        if (canCheck)
        {
            // 过牌
            actions.Add(new AvailableAction(ActionType.Check, "Check"));
        }
        else if (playerChips > 0)
        {
            // 跟注
            var actualCall = Math.Min(callAmount, playerChips);
            actions.Add(new AvailableAction(ActionType.Call, $"Call {actualCall}", actualCall, actualCall));
        }

        // 下注/加注
        if (playerChips > callAmount)
        {
            var minBetTotal = CurrentBet + MinRaise;
            var minRaiseAmount = minBetTotal - player.CurrentBet;

            if (playerChips >= minRaiseAmount)
            {
                if (CurrentBet == 0)
                {
                    // 下注
                    actions.Add(new AvailableAction(ActionType.Bet, 
                        $"Bet {MinRaise}-{playerChips}", 
                        MinRaise, playerChips));
                }
                else
                {
                    // 加注
                    actions.Add(new AvailableAction(ActionType.Raise, 
                        $"Raise to {minBetTotal}-{player.CurrentBet + playerChips}", 
                        minBetTotal, player.CurrentBet + playerChips));
                }
            }
        }

        // 全下 - 只要有筹码就可以
        if (playerChips <= 0) return actions;

        actions.Add(new AvailableAction(ActionType.AllIn, $"All-In {playerChips}", playerChips, playerChips));

        return actions;
    }

    /// <summary>
    /// 处理玩家行动
    /// </summary>
    /// <returns>行动是否有效</returns>
    public (bool Success, string? Error) HandleAction(Player player, ActionType action, int amount)
    {
        _lock.Enter();
        try
        {
            return HandleActionInternal(player, action, amount);
        }
        finally
        {
            _lock.Exit();
        }
    }

    private (bool Success, string? Error) HandleActionInternal(Player player, ActionType action, int amount)
    {
        var callAmount = CurrentBet - player.CurrentBet;
        
        switch (action)
        {
            case ActionType.Fold:
                player.HasFolded = true;
                Logger.Info($"{player.Name} folds");
                return (true, null);

            case ActionType.Check:
                if (callAmount > 0)
                    return (false, "Cannot check when there's a bet to call");
                Logger.Info($"{player.Name} checks");
                return (true, null);

            case ActionType.Call:
                if (callAmount <= 0)
                    return (false, "Nothing to call");
                    
                var actualCall = Math.Min(callAmount, player.Chips);
                player.PlaceBet(actualCall);
                Logger.Info($"{player.Name} calls {actualCall}");
                return (true, null);

            case ActionType.Bet:
                if (CurrentBet > 0)
                    return (false, "Cannot bet when there's already a bet. Use Raise.");
                    
                if (amount < MinRaise && amount < player.Chips)
                    return (false, $"Minimum bet is {MinRaise}");
                    
                if (amount > player.Chips)
                    return (false, "Not enough chips");

                player.PlaceBet(amount);
                CurrentBet = player.CurrentBet;
                MinRaise = amount;
                HasRaiseThisRound = true;
                Logger.Info($"{player.Name} bets {amount}");
                return (true, null);

            case ActionType.Raise:
                if (CurrentBet == 0)
                    return (false, "Cannot raise when there's no bet. Use Bet.");
                
                // amount 是加注到的总额
                var raiseSize = amount - CurrentBet;
                var toAdd = amount - player.CurrentBet;
                
                if (toAdd > player.Chips)
                    return (false, "Not enough chips");
                    
                // 检查是否满足最小加注（除非全下）
                if (raiseSize < MinRaise && toAdd < player.Chips)
                    return (false, $"Minimum raise is {MinRaise}. Raise to at least {CurrentBet + MinRaise}");

                player.PlaceBet(toAdd);
                
                // 只有满足最小加注才更新加注额
                if (raiseSize >= MinRaise)
                {
                    MinRaise = raiseSize;
                    HasRaiseThisRound = true;
                }
                
                CurrentBet = amount;
                Logger.Info($"{player.Name} raises to {amount}");
                return (true, null);

            case ActionType.AllIn:
                var allInAmount = player.Chips;
                var newTotal = player.CurrentBet + allInAmount;
                
                player.PlaceBet(allInAmount);
                
                if (newTotal > CurrentBet)
                {
                    var raiseAmt = newTotal - CurrentBet;
                    
                    // All-In加注：如果加注额满足最小加注，则重新打开行动
                    if (raiseAmt >= MinRaise)
                    {
                        MinRaise = raiseAmt;
                        HasRaiseThisRound = true;
                    }
                    // 否则是不完整的加注，不重新打开行动
                    
                    CurrentBet = newTotal;
                }
                
                Logger.Info($"{player.Name} goes all-in for {allInAmount} (total: {newTotal})");
                return (true, null);

            default:
                return (false, "Unknown action");
        }
    }

    /// <summary>
    /// 获取跟注金额
    /// </summary>
    public int GetCallAmount(Player player)
    {
        return Math.Max(0, CurrentBet - player.CurrentBet);
    }
}
