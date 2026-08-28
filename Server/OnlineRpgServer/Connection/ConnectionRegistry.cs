using System.Net.WebSockets;
using OnlineRpgServer.Account;

namespace OnlineRpgServer.Connection;

// 连接注册表：负责记录 connectionId、playerId 和 WebSocket 连接之间的关系。
// 广播目标查询放在这里。
public sealed class ConnectionRegistry
{
    private readonly object _gate = new();

    //一个连接id与其客户端的映射表
    private readonly Dictionary<string, ClientConnection> _connectionsById =
        new(StringComparer.OrdinalIgnoreCase);
    //一个玩家id与其连接id的映射表
    private readonly Dictionary<string, string> _connectionIdByPlayerId =
        new(StringComparer.OrdinalIgnoreCase);

    public ClientConnection Add(string connectionId, WebSocket socket)
    {
        ClientConnection connection = new(connectionId, socket);

        lock (_gate)
        {
            _connectionsById[connectionId] = connection;
        }

        return connection;
    }

    public void Remove(string connectionId)
    {
        lock (_gate)
        {
            //先判断该客户端是否存在映射表中
            if (!_connectionsById.TryGetValue(connectionId, out ClientConnection? connection))
            {
                return;
            }

            if (connection.HasPlayer)
            {
                _connectionIdByPlayerId.Remove(connection.PlayerId);
            }

            _connectionsById.Remove(connectionId);
            connection.Dispose();
        }
    }

    public void BindSession(string connectionId, PlayerSession session)
    {
        lock (_gate)
        {
            if (!_connectionsById.TryGetValue(connectionId, out ClientConnection? connection))
            {
                return;
            }

            connection.BindPlayer(session.PlayerId, session.Nickname);
            _connectionIdByPlayerId[session.PlayerId] = connectionId;
        }
    }

    //根据玩家id拉取相应的客户端连接列表，用于同房间广播或大厅广播。
    public IReadOnlyList<ClientConnection> GetConnectionsByPlayerIds(IEnumerable<string> playerIds)
    {
        List<ClientConnection> result = new();

        lock (_gate)
        {
            foreach (string playerId in playerIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_connectionIdByPlayerId.TryGetValue(playerId, out string? connectionId))
                {
                    continue;
                }

                if (!_connectionsById.TryGetValue(connectionId, out ClientConnection? connection))
                {
                    continue;
                }

                if (connection.Socket.State == WebSocketState.Open)
                {
                    result.Add(connection);
                }
            }
        }

        return result;
    }
}