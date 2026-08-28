using System.Net.WebSockets;
using System.Text;

namespace OnlineRpgServer.Connection;

// 表示一个已连接到服务端的 WebSocket 客户端。
// 它只保存“连接层”信息：connectionId、socket，以及登录后绑定的玩家身份。
public sealed class ClientConnection : IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    //同一个 WebSocket 不能被多个异步发送同时抢着写。以后如果 A、B 几乎同时触发房间变化，
    //服务端可能并发给同一个 socket 发通知，所以每个连接自己维护一个发送锁，让变化依次写入，而不是混合写入
    public ClientConnection(string connectionId, WebSocket socket)
    {
        ConnectionId = connectionId;
        Socket = socket;
    }

    public string ConnectionId { get; }
    public WebSocket Socket { get; }

    public string PlayerId { get; private set; } = string.Empty;
    public string Nickname { get; private set; } = string.Empty;

    public bool HasPlayer => !string.IsNullOrWhiteSpace(PlayerId);

    public void BindPlayer(string playerId, string nickname)
    {
        PlayerId = playerId;
        Nickname = nickname;
    }

    public async Task SendTextAsync(string message, CancellationToken cancellationToken)
    {
        if (Socket.State != WebSocketState.Open)
        {
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(message);

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _sendLock.Dispose();
    }
}