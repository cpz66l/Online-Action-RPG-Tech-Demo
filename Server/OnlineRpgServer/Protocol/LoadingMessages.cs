﻿using System.Text.Json.Serialization;

namespace OnlineRpgServer.Protocol;

public static class LoadingMessageIds
{
    public const int LoadBattleSceneNtf = 4001;
    public const int ClientLoadProgressNtf = 4002;
    public const int ClientBattleReadyReq = 4003;
    public const int ClientBattleReadyRes = 4004;
    public const int BattleStartNtf = 4005;
}

public sealed class LoadBattleSceneNotificationPayload
{
    [JsonPropertyName("battleId")]
    public required string BattleId { get; init; }

    [JsonPropertyName("roomId")]
    public required string RoomId { get; init; }

    [JsonPropertyName("sceneKey")]
    public required string SceneKey { get; init; }

    [JsonPropertyName("requiredAssets")]
    public required IReadOnlyList<string> RequiredAssets { get; init; }
}

public sealed class ClientLoadProgressNotificationPayload
{
    [JsonPropertyName("battleId")]
    public required string BattleId { get; init; }

    [JsonPropertyName("roomId")]
    public required string RoomId { get; init; }

    [JsonPropertyName("progress")]
    public float Progress { get; init; }

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed class ClientBattleReadyRequestPayload
{
    [JsonPropertyName("battleId")]
    public required string BattleId { get; init; }

    [JsonPropertyName("roomId")]
    public required string RoomId { get; init; }
}

public sealed class ClientBattleReadyResponsePayload
{
    [JsonPropertyName("battleId")]
    public required string BattleId { get; init; }

    [JsonPropertyName("room")]
    public required RoomDto Room { get; init; }
}

public sealed class BattleStartNotificationPayload
{
    [JsonPropertyName("battleId")]
    public required string BattleId { get; init; }

    [JsonPropertyName("roomId")]
    public required string RoomId { get; init; }

    [JsonPropertyName("serverStartTime")]
    public required long ServerStartTime { get; init; }

    // 本场战斗的全体成员与各自的出生点。
    // 放进开战通知而不是单独加一条消息：客户端收到就能直接进场景摆人，不用多一次往返。
    [JsonPropertyName("players")]
    public required IReadOnlyList<BattlePlayerSpawnDto> Players { get; init; }
}

// 开战通知里的单个玩家出生信息。
// 客户端用 playerId 认出自己，用 position / yaw 摆到服务端指定的位置。
public sealed class BattlePlayerSpawnDto
{
    [JsonPropertyName("playerId")]
    public required string PlayerId { get; init; }

    [JsonPropertyName("nickname")]
    public required string Nickname { get; init; }

    [JsonPropertyName("spawnIndex")]
    public required int SpawnIndex { get; init; }

    [JsonPropertyName("position")]
    public required BattleVector3Dto Position { get; init; }

    [JsonPropertyName("yaw")]
    public required float Yaw { get; init; }
}

public sealed class BattleVector3Dto
{
    [JsonPropertyName("x")]
    public required float X { get; init; }

    [JsonPropertyName("y")]
    public required float Y { get; init; }

    [JsonPropertyName("z")]
    public required float Z { get; init; }
}