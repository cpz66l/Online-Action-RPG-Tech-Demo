using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnlineRpgServer.Protocol;

public sealed class ProtocolEnvelope
{
    [JsonPropertyName("msgId")]
    public int MsgId { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("clientTime")]
    public long? ClientTime { get; init; }

    [JsonPropertyName("serverTime")]
    public long? ServerTime { get; init; }

    [JsonPropertyName("code")]
    public int? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }
}
