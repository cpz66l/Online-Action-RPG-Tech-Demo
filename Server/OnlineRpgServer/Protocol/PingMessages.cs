using System.Text.Json.Serialization;

namespace OnlineRpgServer.Protocol;

// 调试消息编号：9000 段预留给 Ping、GM、弱网模拟等 Debug 协议。
public static class DebugMessageIds
{
    public const int PingReq = 9001;
    public const int PingRes = 9002;
    public const int ErrorRes = 9999;
}

// Ping 请求 payload：客户端发出请求时记录自己的时间戳。
public sealed class PingPayload
{
    [JsonPropertyName("clientTime")]
    public long ClientTime { get; init; }
}

// Ping 响应 payload：服务端原样带回 clientTime，并补充 serverTime。
public sealed class PingResponsePayload
{
    [JsonPropertyName("clientTime")]
    public long ClientTime { get; init; }

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; init; }
}
