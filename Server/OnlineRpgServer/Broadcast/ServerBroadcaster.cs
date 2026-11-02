using System.Text.Json;
using OnlineRpgServer.Connection;
using OnlineRpgServer.Protocol;
using OnlineRpgServer.Room;
using static OnlineRpgServer.Protocol.ServerTime;

namespace OnlineRpgServer.Broadcast;

// 服务端主动广播的唯一出口。
//
// 现在是"被分发结果驱动"：ConnectionHandler 拿到 MessageDispatchResult 后调这里。
// 迭代 05 之后 BattleService 的 Tick 也会直接调这里 —— 那条路径没有触发请求，
// 所以广播必须能脱离分发器被单独调用，这也是它独立成类的原因。
public sealed class ServerBroadcaster
{
    private readonly ConnectionRegistry _connectionRegistry;
    private readonly ILogger _logger;

    public ServerBroadcaster(ConnectionRegistry connectionRegistry, ILogger logger)
    {
        _connectionRegistry = connectionRegistry;
        _logger = logger;
    }

    //广播房间状态
    public async Task BroadcastRoomStateAsync(
        RoomSnapshot room,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Serialize(new
        {
            msgId = RoomMessageIds.RoomStateNtf,
            type = "RoomStateNtf",
            serverTime = UnixTimeMilliseconds(),
            payload = new RoomStateNotificationPayload
            {
                Room = RoomDto.FromSnapshot(room)
            }
        });

        var playerIds = room.Players.Select(player => player.PlayerId);
        var targets = _connectionRegistry.GetConnectionsByPlayerIds(playerIds);

        //给房间内的每个玩家发送广播消息
        foreach (var target in targets)
        {
            try
            {
                await target.SendTextAsync(message, cancellationToken);
                _logger.LogInformation("Broadcast RoomStateNtf to {ConnectionId}: {Message}", target.ConnectionId, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast RoomStateNtf to {ConnectionId}", target.ConnectionId);
            }
        }
    }

    //广播加载战斗场景通知
    public async Task BroadcastLoadBattleSceneAsync(
        LoadBattleSceneDispatch notification,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Serialize(new
        {
            msgId = LoadingMessageIds.LoadBattleSceneNtf,
            type = "LoadBattleSceneNtf",
            serverTime = UnixTimeMilliseconds(),
            payload = notification.Payload
        });

        var playerIds = notification.Room.Players.Select(player => player.PlayerId);
        var targets = _connectionRegistry.GetConnectionsByPlayerIds(playerIds);

        foreach (var target in targets)
        {
            try
            {
                await target.SendTextAsync(message, cancellationToken);
                _logger.LogInformation("Broadcast LoadBattleSceneNtf to {ConnectionId}: {Message}", target.ConnectionId, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast LoadBattleSceneNtf to {ConnectionId}", target.ConnectionId);
            }
        }
    }

    //广播开始战斗通知
    public async Task BroadcastBattleStartAsync(
        BattleStartDispatch notification,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Serialize(new
        {
            msgId = LoadingMessageIds.BattleStartNtf,
            type = "BattleStartNtf",
            serverTime = UnixTimeMilliseconds(),
            payload = notification.Payload
        });

        var playerIds = notification.Room.Players.Select(player => player.PlayerId);
        var targets = _connectionRegistry.GetConnectionsByPlayerIds(playerIds);

        foreach (var target in targets)
        {
            try
            {
                await target.SendTextAsync(message, cancellationToken);
                _logger.LogInformation("Broadcast BattleStartNtf to {ConnectionId}: {Message}", target.ConnectionId, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast BattleStartNtf to {ConnectionId}", target.ConnectionId);
            }
        }
    }
}
