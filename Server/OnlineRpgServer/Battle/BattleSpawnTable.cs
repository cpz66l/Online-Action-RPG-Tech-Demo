namespace OnlineRpgServer.Battle;

// 出生点表。
//
// 坐标必须与客户端场景 BattleArena_Training 里的 SpawnPoint_Local_01 / _02 对齐。
// 改场景出生点后要同步改这里 —— 这是迭代05 已知的一处双份维护，
// 与"移动参数在 Prefab 一份、服务端一份"是同一类债。
//
// 之所以让服务端持有坐标而不是只下发序号：05-2 的 Tick 要积分位置，
// 服务端必须知道每个玩家的初始位置，否则第一帧就算不出来。
public static class BattleSpawnTable
{
    public readonly record struct SpawnPoint(BattleVector3 Position, float Yaw);

    private static readonly SpawnPoint[] Points =
    [
        // spawn_local_01：场景原点附近，面朝 +Z（木桩方向）。
        new(new BattleVector3(0f, 1f, 0f), 0f),

        // spawn_local_02：右侧 1.5 米，略微朝向中心，避免两人正面重叠。
        new(new BattleVector3(1.5f, 1f, 0f), -20f)
    ];

    public static int Count => Points.Length;

    // 索引越界时绕回而不是抛异常：
    // 人数多于出生点时会两人共用一点，由调用方记警告，不在这里静默处理。
    public static SpawnPoint Get(int index)
    {
        if (Points.Length == 0)
        {
            return new SpawnPoint(BattleVector3.Zero, 0f);
        }

        int safe = index % Points.Length;

        if (safe < 0)
        {
            safe += Points.Length;
        }

        return Points[safe];
    }
}
