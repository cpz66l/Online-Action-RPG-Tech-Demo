using System.Text.Json.Serialization;

namespace OnlineRpgServer.Protocol;

public static class AccountMessageIds
{
    public const int RegisterReq = 1001;
    public const int RegisterRes = 1002;
    public const int LoginReq = 1003;
    public const int LoginRes = 1004;
}

public sealed class RegisterRequestPayload
{
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; init; }
}

public sealed class RegisterResponsePayload
{
    [JsonPropertyName("playerId")]
    public required string PlayerId { get; init; }

    [JsonPropertyName("nickname")]
    public required string Nickname { get; init; }
}

public sealed class LoginRequestPayload
{
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }
}

public sealed class LoginResponsePayload
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("playerId")]
    public required string PlayerId { get; init; }

    [JsonPropertyName("nickname")]
    public required string Nickname { get; init; }
}
