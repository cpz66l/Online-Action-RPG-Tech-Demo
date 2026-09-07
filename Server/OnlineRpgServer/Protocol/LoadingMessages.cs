using System.Text.Json.Serialization;

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
}