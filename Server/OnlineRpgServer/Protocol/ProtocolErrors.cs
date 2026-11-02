namespace OnlineRpgServer.Protocol;

// 统一的错误响应构造。
//
// 分发层（未知消息类型、payload 非法）与连接层（非文本帧）都要用同一种错误信封，
// 放在这里避免两处各写一份导致字段慢慢长歪。
public static class ProtocolErrors
{
    public static object CreateErrorResponse(string? requestId, int code, string message)
    {
        // 错误响应也保留 requestId，方便客户端把错误和原请求对应起来。
        return new
        {
            msgId = DebugMessageIds.ErrorRes,
            type = "ErrorRes",
            requestId,
            code,
            message,
            serverTime = ServerTime.UnixTimeMilliseconds(),
            payload = new { }
        };
    }
}
