using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OnlineRpgServer.Broadcast;
using OnlineRpgServer.Protocol;
using OnlineRpgServer.Room;
using static OnlineRpgServer.Protocol.ProtocolErrors;

namespace OnlineRpgServer.Connection;

// ！！！单条连接的生命周期：收消息 → 分发 → 回包 → 按需广播 → 断线清理。
//
// 它负责"顺序"和"副作用"，不负责"业务判断"（那个在 MessageRouter）和
// "怎么写 socket"（那个在 ServerBroadcaster）。
public sealed class ConnectionHandler
{
    private readonly MessageRouter _messageRouter;
    private readonly ServerBroadcaster _broadcaster;
    private readonly RoomService _roomService;
    private readonly ConnectionRegistry _connectionRegistry;
    private readonly ILogger<ConnectionHandler> _logger;

    public ConnectionHandler(
        MessageRouter messageRouter,
        ServerBroadcaster broadcaster,
        RoomService roomService,
        ConnectionRegistry connectionRegistry,
        ILogger<ConnectionHandler> logger)
    {
        _messageRouter = messageRouter;
        _broadcaster = broadcaster;
        _roomService = roomService;
        _connectionRegistry = connectionRegistry;
        _logger = logger;
    }

    // 一条连接的完整生命周期。WebSocket 端点只调这一个方法。
    public async Task ServeAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        // connected / disconnected 成对出现在同一个日志分类下，
        // 排查连接问题时不用在两个 category 之间来回看。
        _logger.LogInformation("Client connected: {ConnectionId}", connection.ConnectionId);

        try
        {
            await HandleMessageLoopAsync(connection, cancellationToken);
        }
        finally
        {
            //连接中断，清理房间状态，广播给其他玩家
            await HandleDisconnectRoomCleanupAsync(connection);
            _connectionRegistry.Remove(connection.ConnectionId);
            _logger.LogInformation("Client disconnected: {ConnectionId}", connection.ConnectionId);
        }
    }

    private async Task HandleMessageLoopAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        var socket = connection.Socket;
        // 当前只做小包 JSON 协议验证，4KB 足够；后续大包/分片再扩展。
        var buffer = new byte[4096];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            // 统一接收完整的一条 Text Message。
            ReceiveResult receive = await ReceiveTextAsync(socket, buffer, cancellationToken);//将客户端发来的数据进行处理

            if (receive.Kind == ReceiveKind.Closed)
            {
                break;
            }

            // 非文本帧的协议错误在这里直接回复，不进入业务分发，也不断开连接。
            // 之前把错误响应字符串当成“收到的消息”返回，外层会再走一次 BuildResponse，
            // 最终给客户端回一个看不懂的 "Unsupported message type: ErrorRes"。
            if (receive.Kind == ReceiveKind.UnsupportedFrame)
            {
                var unsupportedFrameError = JsonSerializer.Serialize(
                    CreateErrorResponse(null, 1001, "Only text JSON messages are supported."));

                await connection.SendTextAsync(unsupportedFrameError, cancellationToken);
                _logger.LogInformation("Send {ConnectionId}: {Message}", connection.ConnectionId, unsupportedFrameError);
                continue;
            }

            string result = receive.Text!;

            _logger.LogInformation("Recv {ConnectionId}: {Message}", connection.ConnectionId, result);

            // 业务分发点：
            var dispatchResult = _messageRouter.BuildResponse(result, connection.ConnectionId);

            // 请求类消息会有响应；通知类消息可以没有响应。
            if (!string.IsNullOrWhiteSpace(dispatchResult.ResponseJson))
            {
                await connection.SendTextAsync(dispatchResult.ResponseJson, cancellationToken);

                _logger.LogInformation("Send {ConnectionId}: {Message}", connection.ConnectionId, dispatchResult.ResponseJson);
            }

            //如果是与房间相关的响应，则再进房间状态的广播
            if (dispatchResult.RoomStateToNotify is not null)
            {
                await _broadcaster.BroadcastRoomStateAsync(
                    dispatchResult.RoomStateToNotify,
                    cancellationToken);
            }

            //如果是开始战斗的响应，则再进加载战斗场景的广播
            if (dispatchResult.LoadBattleSceneToNotify is not null)
            {
                await _broadcaster.BroadcastLoadBattleSceneAsync(
                    dispatchResult.LoadBattleSceneToNotify,
                    cancellationToken);
            }

            //如果是战斗开始的响应，则再进战斗开始的广播
            if (dispatchResult.BattleStartToNotify is not null)
            {
                await _broadcaster.BroadcastBattleStartAsync(
                    dispatchResult.BattleStartToNotify,
                    cancellationToken);
            }
        }
    }

    private static async Task<ReceiveResult> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        // WebSocket 一条消息可能被拆成多个 frame，因此先写入 MemoryStream，等 EndOfMessage 再转字符串。
        using var stream = new MemoryStream();

        while (true)
        {
            //等待客户端发一条消息
            var result = await socket.ReceiveAsync(buffer, cancellationToken);

            // 客户端主动关闭时，服务端也用正常关闭状态回应，避免连接悬挂。
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", cancellationToken);
                return new ReceiveResult(ReceiveKind.Closed, null);
            }

            // MVP 协议只接受文本 JSON；二进制包留到 MessagePack / Protobuf 阶段再考虑。
            // 这里只标记为“不支持的帧”，由调用方负责回复协议错误。
            if (result.MessageType != WebSocketMessageType.Text)
            {
                // 二进制消息可能是分片的，先把剩余 fragment 读完再返回，
                // 避免一个二进制消息触发多次错误回复。
                if (!result.EndOfMessage)
                {
                    continue;
                }

                return new ReceiveResult(ReceiveKind.UnsupportedFrame, null);
            }

            stream.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return new ReceiveResult(ReceiveKind.Message, Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }

    //处理断开连接时的房间清理逻辑
    private async Task HandleDisconnectRoomCleanupAsync(ClientConnection connection)
    {
        //如果连接没有绑定玩家身份，则无需清理房间状态
        if (!connection.HasPlayer)
        {
            return;
        }

        var result = _roomService.LeaveRoomByPlayerId(connection.PlayerId);

        if (!result.Success)
        {
            return;
        }

        if (result.Room is null)
        {
            _logger.LogInformation(
                "Player {PlayerId} disconnected and their room was destroyed.",
                connection.PlayerId);
            return;
        }

        _logger.LogInformation(
            "Player {PlayerId} disconnected; broadcasting remaining room state for {RoomId}.",
            connection.PlayerId,
            result.Room.RoomId);

        //广播房间状态给剩余的玩家
        await _broadcaster.BroadcastRoomStateAsync(
            result.Room,
            CancellationToken.None);
    }
}

// ReceiveTextAsync 的返回值：区分“收到文本消息”“连接已关闭”“收到非文本帧但连接仍可用”三种情况。
enum ReceiveKind
{
    Message,
    Closed,
    UnsupportedFrame
}

readonly record struct ReceiveResult(ReceiveKind Kind, string? Text);
