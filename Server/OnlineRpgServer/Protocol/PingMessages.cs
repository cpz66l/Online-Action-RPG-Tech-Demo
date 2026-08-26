using System.Text.Json.Serialization;

namespace OnlineRpgServer.Protocol;

public static class DebugMessageIds
{
    public const int PingReq = 9001;
    public const int PingRes = 9002;
    public const int ErrorRes = 9999;
}

public sealed class PingPayload
{
    [JsonPropertyName("clientTime")]
    public long ClientTime { get; init; }
}

public sealed class PingResponsePayload
{
    [JsonPropertyName("clientTime")]
    public long ClientTime { get; init; }

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; init; }
}
