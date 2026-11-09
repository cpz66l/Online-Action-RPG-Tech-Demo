namespace OnlineRpgServer.Battle;

// 战斗中的三维向量。
// 服务端不引用 UnityEngine，所以自带一个最小实现；只需能表达位置与速度。
public readonly record struct BattleVector3(float X, float Y, float Z)
{
    public static BattleVector3 Zero => new(0f, 0f, 0f);
}

// 战斗中的一个玩家。
//
// 05-1 只保存身份与出生信息。速度、血量、动作状态在 05-2 / 迭代06 逐步加进来。
// 位置和朝向是可变的：Tick 会推进它们，所以不能是 init-only。
public sealed class BattlePlayerState
{
    public BattlePlayerState(
        string playerId,
        string nickname,
        int spawnIndex,
        BattleVector3 position,
        float yaw)
    {
        PlayerId = playerId;
        Nickname = nickname;
        SpawnIndex = spawnIndex;
        Position = position;
        Yaw = yaw;
    }

    public string PlayerId { get; }
    public string Nickname { get; }

    // 分配到的出生点序号，用于排查"为什么两个人站在一起"。
    public int SpawnIndex { get; }

    public BattleVector3 Position { get; set; }
    public float Yaw { get; set; }
}
