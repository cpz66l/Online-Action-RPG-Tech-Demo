namespace OnlineRpgServer.Battle;

// 一个进行中的战斗实例。
//
// 它是服务端权威状态的容器：谁在这个战斗里、各自在哪。
// 05-2 起 Tick 会遍历这里的玩家推进位置，而 Tick 跑在线程池线程上，
// 请求处理跑在连接线程上，所以所有读取一律加锁。
public sealed class BattleInstance
{
    private readonly object _gate = new();

    private readonly Dictionary<string, BattlePlayerState> _playersById =
        new(StringComparer.OrdinalIgnoreCase);

    // 保留加入顺序：广播快照时玩家顺序稳定，客户端做 diff 才不会抖动。
    private readonly List<BattlePlayerState> _playerOrder = new();

    public BattleInstance(string battleId, string roomId, IEnumerable<BattlePlayerState> players)
    {
        BattleId = battleId;
        RoomId = roomId;
        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (BattlePlayerState player in players)
        {
            if (_playersById.TryAdd(player.PlayerId, player))
            {
                _playerOrder.Add(player);
            }
        }
    }

    public string BattleId { get; }
    public string RoomId { get; }
    public long CreatedAt { get; }

    public int PlayerCount
    {
        get
        {
            lock (_gate)
            {
                return _playerOrder.Count;
            }
        }
    }

    // 返回数组副本，避免调用方遍历时集合被 Tick 修改。
    // 注意元素本身是引用：位置字段仍会被 Tick 改动，读取时按需取字段即可。
    public IReadOnlyList<BattlePlayerState> GetPlayers()
    {
        lock (_gate)
        {
            return _playerOrder.ToArray();
        }
    }

    public BattlePlayerState? FindPlayer(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        lock (_gate)
        {
            return _playersById.TryGetValue(playerId, out BattlePlayerState? player) ? player : null;
        }
    }
}
