using TServer.Logging;
using TServer.Model;

namespace TServer.Game;

/// <summary>
/// 底池管理器 - 处理主池和边池逻辑
/// </summary>
public class PotManager
{
    private readonly List<Pot> _pots = [];
    private readonly Lock _lock = new();
    
    public IReadOnlyList<Pot> Pots
    {
        get
        {
            _lock.Enter();
            try { return [.._pots]; }
            finally { _lock.Exit(); }
        }
    }

    public int TotalPot
    {
        get
        {
            _lock.Enter();
            try { return _pots.Sum(p => p.Amount); }
            finally { _lock.Exit(); }
        }
    }

    /// <summary>
    /// 清空所有底池
    /// </summary>
    public void Clear()
    {
        _lock.Enter();
        try { _pots.Clear(); }
        finally { _lock.Exit(); }
    }

    /// <summary>
    /// 每轮下注结束时收集玩家下注到底池
    /// </summary>
    public void CollectBets(List<Player> players)
    {
        _lock.Enter();
        try
        {
            CollectBetsInternal(players);
        }
        finally
        {
            _lock.Exit();
        }
    }

    private void CollectBetsInternal(List<Player> players)
    {
        var activePlayers = players.Where(p => !p.HasFolded).ToList();
        if (activePlayers.Count == 0 && _pots.Count == 0)
            return;

        // 初始化主池（如果还没有）
        if (_pots.Count == 0)
        {
            _pots.Add(new Pot 
            { 
                Name = "Main Pot",
                EligiblePlayerIds = activePlayers.Select(p => p.Id).ToList()
            });
        }

        // 使用洋葱算法处理边池
        while (players.Any(p => p.CurrentBet > 0))
        {
            var contributors = players.Where(p => p.CurrentBet > 0).ToList();
            
            // 找出未弃牌玩家中的最小下注额
            var activeContributors = contributors.Where(p => !p.HasFolded).ToList();
            var stepAmount = activeContributors.Count > 0
                ? activeContributors.Min(p => p.CurrentBet)
                : contributors.Min(p => p.CurrentBet);

            // 收集这一层的筹码
            var potChunk = 0;
            foreach (var player in contributors)
            {
                var contribution = Math.Min(player.CurrentBet, stepAmount);
                player.CurrentBet -= contribution;
                potChunk += contribution;
            }

            // 加入当前最新的底池
            var currentPot = _pots.Last();
            currentPot.Amount += potChunk;

            // 检查是否有All-In玩家在这一层耗尽了筹码
            var anyAllInFinished = activeContributors.Any(p => p is { IsAllIn: true, CurrentBet: 0 });
            var moreBetsToCollect = players.Any(p => p.CurrentBet > 0);

            if (anyAllInFinished && moreBetsToCollect)
            {
                // 创建新边池
                var nextEligible = players.Where(p => p is { HasFolded: false, CurrentBet: > 0 })
                    .Select(p => p.Id).ToList();
                
                var sidePotIndex = _pots.Count;
                _pots.Add(new Pot
                {
                    Name = $"Side Pot {sidePotIndex}",
                    Amount = 0,
                    EligiblePlayerIds = nextEligible
                });
                
                Logger.Debug($"Created {_pots.Last().Name} for players: {string.Join(", ", nextEligible)}");
            }
        }

        Logger.Info($"Collected bets. Total pot: {_pots.Sum(p => p.Amount)}");
    }

    /// <summary>
    /// 分配底池给获胜者
    /// </summary>
    /// <param name="rankings">按牌力从强到弱排序的玩家组（同组为平手）</param>
    /// <param name="players">所有玩家（用于查找Player对象）</param>
    /// <param name="smallBlindPlayerId">小盲位玩家ID（用于分配零头）</param>
    /// <returns>每个玩家赢得的金额</returns>
    public Dictionary<string, int> Distribute(List<List<string>> rankings, List<Player> players, string smallBlindPlayerId)
    {
        var winnings = new Dictionary<string, int>();
        var winnersInOrder = new List<string>(); // 从小盲位开始顺时针记录获胜者

        _lock.Enter();
        try
        {
            foreach (var pot in _pots.Where(p => p.Amount > 0))
            {
                Logger.Info($"Distributing {pot.Name}: {pot.Amount} chips");
                
                // 从最强牌力组开始找有资格的玩家
                foreach (var group in rankings)
                {
                    var winners = group.Where(id => pot.EligiblePlayerIds.Contains(id)).ToList();
                    if (winners.Count == 0) continue;

                    // 找到赢家，平分底池
                    var share = pot.Amount / winners.Count;
                    var remainder = pot.Amount % winners.Count;

                    foreach (var winnerId in winners)
                    {
                        winnings.TryAdd(winnerId, 0);
                        winnings[winnerId] += share;
                        
                        // 记录获胜者（用于分配零头）
                        if (!winnersInOrder.Contains(winnerId))
                            winnersInOrder.Add(winnerId);
                    }

                    // 分配零头
                    if (remainder > 0)
                    {
                        // 零头给从小盲位开始顺时针第一个赢下此局的玩家
                        var smallBlindIdx = players.FindIndex(p => p.Id == smallBlindPlayerId);
                        var remainderWinner = players.Select((_, i) => (smallBlindIdx + i) % players.Count).Select(idx => players[idx].Id).FirstOrDefault(playerId => winners.Contains(playerId));

                        // 从小盲位开始顺时针找第一个获胜者

                        if (remainderWinner != null)
                        {
                            winnings[remainderWinner] += remainder;
                            Logger.Debug($"Remainder {remainder} goes to {remainderWinner}");
                        }
                    }

                    Logger.Info($"{pot.Name} won by: {string.Join(", ", winners)} ({share} each)");
                    break; // 这个底池分配完毕
                }
            }

            // 将奖金发放给玩家
            foreach (var (playerId, amount) in winnings)
            {
                var player = players.FirstOrDefault(p => p.Id == playerId);
                if (player == null) continue;
                
                player.Chips += amount;
                Logger.Info($"{player.Name} wins {amount} chips (now has {player.Chips})");
            }
        }
        finally
        {
            _lock.Exit();
        }

        return winnings;
    }

    /// <summary>
    /// 当只剩一个玩家时分配所有底池
    /// </summary>
    public int DistributeToSingleWinner(Player winner)
    {
        _lock.Enter();
        try
        {
            var total = _pots.Sum(p => p.Amount);
            winner.Chips += total;
            Logger.Info($"{winner.Name} wins {total} chips (all others folded)");
            return total;
        }
        finally
        {
            _lock.Exit();
        }
    }
}
