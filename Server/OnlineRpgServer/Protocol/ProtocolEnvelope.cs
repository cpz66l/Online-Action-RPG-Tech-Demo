using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnlineRpgServer.Protocol;

// 通用协议信封：所有业务消息都先包一层 Envelope，再把具体数据放进 payload。
// 这样日志、路由、请求响应匹配、错误处理都能走统一结构。
public sealed class ProtocolEnvelope
{
    // 消息编号：便于后续做协议表、统计和二进制协议映射。
    [JsonPropertyName("msgId")]
    public int MsgId { get; init; }

    // 消息类型：MVP 阶段优先用于路由和日志阅读，例如 PingReq / PingRes。
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    // 请求唯一 ID：响应带回同一个 requestId，客户端就能匹配请求和响应。
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    // 登录后会使用的会话凭证；迭代 0 暂时不校验。
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    // 客户端时间戳：当前用于 Ping / Pong RTT 计算。
    [JsonPropertyName("clientTime")]
    public long? ClientTime { get; init; }

    // 服务端时间戳：用于日志对齐、RTT 观察和后续同步调试。
    [JsonPropertyName("serverTime")]
    public long? ServerTime { get; init; }

    // 结果码：响应包使用，0 表示成功，非 0 表示错误。
    [JsonPropertyName("code")]
    public int? Code { get; init; }

    // 面向调试和 UI 的简短提示，不承载复杂业务数据。
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    // 具体业务数据：先用 JsonElement 保持灵活，后续按 type 转成具体 payload。
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }
}
