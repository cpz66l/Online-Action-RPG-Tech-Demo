using OnlineRpgServer.Connection;

namespace OnlineRpgServer.Battle;

// 战斗实例的容器与推进循环。
//
// 迭代 05-0 只建骨架：一个固定频率的 Tick，用来证明"服务端第一次有了自己的时间"。
// 此前服务端所有逻辑都是"收到消息才动"，没有任何与请求无关的推进。
//
// 战斗实例、玩家状态、快照广播在 05-1 / 05-2 逐步填入。
public sealed class BattleService : IDisposable
{
    // 20Hz。客户端上行意图也是这个频率，两边对齐后插值余量才好算。
    public const int TickRate = 20;

    private const int TickIntervalMs = 1000 / TickRate;//50ms1Tick

    // 每多少个 tick 打一行日志。取 TickRate 表示20个Tick打一次，也就是 1 秒一行，
    // 否则 50ms 一行的日志会把服务端控制台刷满，反而看不见业务日志。
    private const int LogEveryNTicks = TickRate;

    private readonly ILogger<BattleService> _logger;
    private readonly ConnectionRegistry _connectionRegistry;

    private Timer? _timer;
    private long _tickCount;
    private long _lastLoggedTick;

    // 战斗实例表。CreateBattle 由连接线程写，FindBattle 将来由 Tick 线程读，因此加锁。
    private readonly Dictionary<string, BattleInstance> _battlesById =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _battleGate = new();

    public BattleService(ILogger<BattleService> logger, ConnectionRegistry connectionRegistry)
    {
        _logger = logger;
        _connectionRegistry = connectionRegistry;
    }

    public long TickCount => Interlocked.Read(ref _tickCount);

    public void Start()
    {
        if (_timer != null)
        {
            return;
        }

        // Timer 回调运行在线程池线程上，不是主线程。
        // 05-2 起 Tick 里会访问玩家位置等共享状态，届时必须自己加锁，
        // 不能假设和其他请求处理是同一线程。
        _timer = new Timer(OnTick, null, TickIntervalMs, TickIntervalMs);

        _logger.LogInformation("BattleService started. TickRate={TickRate}Hz", TickRate);
    }

    public void Stop()
    {
        if (_timer == null)
        {
            return;
        }

        _timer.Dispose();
        _timer = null;

        _logger.LogInformation("BattleService stopped. TotalTicks={TickCount}", TickCount);
    }

    // 创建战斗实例并分配出生点。
    // 由"全员 BattleReady"那一刻调用；一个房间在一场战斗里只会走到这里一次。
    public BattleInstance CreateBattle(
        string battleId,
        string roomId,
        IReadOnlyList<(string PlayerId, string Nickname)> players)
    {
        List<BattlePlayerState> states = new(players.Count);

        for (int i = 0; i < players.Count; i++)
        {
            BattleSpawnTable.SpawnPoint spawn = BattleSpawnTable.Get(i);

            states.Add(new BattlePlayerState(
                players[i].PlayerId,
                players[i].Nickname,
                i,
                spawn.Position,
                spawn.Yaw));
        }

        if (players.Count > BattleSpawnTable.Count)
        {
            _logger.LogWarning(
                "Battle {BattleId} has {PlayerCount} players but only {SpawnCount} spawn points; positions will overlap.",
                battleId,
                players.Count,
                BattleSpawnTable.Count);
        }

        BattleInstance battle = new(battleId, roomId, states);

        lock (_battleGate)
        {
            _battlesById[battleId] = battle;
        }

        _logger.LogInformation(
            "Battle created. battleId={BattleId}, roomId={RoomId}, players={PlayerCount}, spawns=[{Spawns}]",
            battleId,
            roomId,
            battle.PlayerCount,
            string.Join(", ", states.Select(state => $"{state.PlayerId}->{state.SpawnIndex}")));

        return battle;
    }

    public BattleInstance? FindBattle(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
        {
            return null;
        }

        lock (_battleGate)
        {
            return _battlesById.TryGetValue(battleId, out BattleInstance? battle) ? battle : null;
        }
    }

    // 战斗结束时清理实例。迭代05 只处理"玩家全部离开"，正式结算在迭代06。
    public bool RemoveBattle(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
        {
            return false;
        }

        lock (_battleGate)
        {
            return _battlesById.Remove(battleId);
        }
    }

    private void OnTick(object? state)
    {
        long tick = Interlocked.Increment(ref _tickCount);

        // 用差值判断而不是取模，避免 tick 从 0 开始时的边界判断。
        if (tick - _lastLoggedTick < LogEveryNTicks)
        {
            return;
        }

        _lastLoggedTick = tick;

        // 用 Debug 而不是 Information：tick 心跳是运行状态，不是业务事件。
        // 控制台默认最低级别是 Information，所以它默认不出现；
        // 需要观察时用环境变量把本模块调到 Debug，不必改代码：
        //   Logging__LogLevel__OnlineRpgServer.Battle.BattleService = Debug
        _logger.LogDebug(
            "[Battle] tick={Tick} alive, connections={ConnectionCount}",
            tick,
            _connectionRegistry.Count);
    }

    public void Dispose()
    {
        Stop();
    }
}
