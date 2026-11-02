namespace OnlineRpgServer.Protocol;

// 服务端时间基准。
//
// 分发层与广播层都要给消息盖 serverTime，抽成独立类型是为了保证只有一个时钟源，
// 而不是每个类各写一遍 DateTimeOffset.UtcNow —— 那样两边的时间会来自不同的调用点。
public static class ServerTime
{
    public static long UnixTimeMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
