using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OnlineRpgServer.Account;
using OnlineRpgServer.Protocol;
using OnlineRpgServer.Room;
using OnlineRpgServer.Connection;

// 服务端入口：当前迭代只负责最小 WebSocket Ping / Pong 验证。
// 后续登录、大厅、战斗同步都会建立在这条“连接 -> 收包 -> 分发 -> 回包”的链路上。

// 创建一个服务端应用构建器
var builder = WebApplication.CreateBuilder(args);

// 日志配置：保留我们关心的服务端业务日志，压低 ASP.NET Core 框架日志噪音。
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);


var app = builder.Build();  //生成真正可运行的WebApplication
var logger = app.Logger;    //拿到日志对象
var accountService = new AccountService();  //实例化账号服务
var roomService = new RoomService();        //实例化房间服务
var connectionRegistry = new ConnectionRegistry(); // 记录玩家和 WebSocket 连接的绑定关系


// 固定本地开发端口，方便 Unity 客户端和 smoke test 使用同一个地址。
app.Urls.Add("http://localhost:5050");

// 启用 WebSocket 中间件。没有这一步，/ws 只能作为普通 HTTP 请求处理。
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)//服务端会周期性维护连接活性
});

// 健康检查入口：用于确认服务端进程和 HTTP 监听是否正常，不走 WebSocket。
app.MapGet("/health", () => Results.Ok(new
{
    status = "OK",
    service = "OnlineRpgServer",
    serverTime = UnixTimeMilliseconds()
}));

// WebSocket 协议入口：未来 Unity 客户端会连接 ws://localhost:5050/ws。
app.Map("/ws", async context =>
{
    // WebSocket 第一步是 HTTP Upgrade 握手；不是 WebSocket 请求就直接拒绝。
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket endpoint. Connect with ws://host:port/ws");
        return;
    }

    // 握手通过后，HTTP 连接升级成 WebSocket 双向长连接。
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("Client connected: {ConnectionId}", connectionId);

    var connection = connectionRegistry.Add(connectionId, socket);

    try
    {
        // 当前连接的主循环：持续收消息、构造响应、发回客户端。
        await HandleConnectionAsync(connection, logger, accountService, roomService, connectionRegistry, context.RequestAborted);
    }
    finally
    {
        connectionRegistry.Remove(connectionId);
        logger.LogInformation("Client disconnected: {ConnectionId}", connectionId);
    }
});

logger.LogInformation("OnlineRpgServer starting. WebSocket endpoint: ws://localhost:5050/ws");
app.Run();//阻塞当前线程，让服务端一直运行监听请求。

static async Task HandleConnectionAsync(
    ClientConnection connection,
    ILogger logger,
    AccountService accountService,
    RoomService roomService,
    ConnectionRegistry connectionRegistry,
    CancellationToken cancellationToken)
{
    var socket = connection.Socket;
    // 当前只做小包 JSON 协议验证，4KB 足够；后续大包/分片再扩展。
    var buffer = new byte[4096];

    while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
    {
        // 统一接收完整的一条 Text Message。返回 null 表示客户端主动断开。
        var result = await ReceiveTextAsync(socket, buffer, cancellationToken);//将客户端发来的数据进行处理

        if (result is null)
        {
            break;
        }

        logger.LogInformation("Recv {ConnectionId}: {Message}", connection.ConnectionId, result);

        // 业务分发点：
        var dispatchResult = BuildResponse(result,logger,accountService,roomService,connection.ConnectionId,connectionRegistry);
        //先回复请求的响应
        await connection.SendTextAsync(dispatchResult.ResponseJson, cancellationToken);

        logger.LogInformation("Send {ConnectionId}: {Message}", connection.ConnectionId, dispatchResult.ResponseJson);

        //如果是与房间相关的响应，则再进房间状态的广播
        if (dispatchResult.RoomStateToNotify is not null)
        {
            await BroadcastRoomStateAsync(
                dispatchResult.RoomStateToNotify,
                connectionRegistry,
                logger,
                cancellationToken);
        }
    }
}

static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
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
            return null;
        }

        // MVP 协议只接受文本 JSON；二进制包留到 MessagePack / Protobuf 阶段再考虑。
        if (result.MessageType != WebSocketMessageType.Text)
        {
            return JsonSerializer.Serialize(CreateErrorResponse(null, 1001, "Only text JSON messages are supported."));
        }

        stream.Write(buffer, 0, result.Count);

        if (result.EndOfMessage)
        {
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}

/*static async Task SendTextAsync(WebSocket socket, string message, CancellationToken cancellationToken)
{
    // 当前协议格式是 JSON 文本，所以按 UTF-8 发送 WebSocket Text Message。
    var bytes = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
}*/

static MessageDispatchResult BuildResponse(
    string rawMessage,
    ILogger logger,
    AccountService accountService,
    RoomService roomService,
    string connectionId,
    ConnectionRegistry connectionRegistry)
{
    try
    {
        // 先反序列化通用信封，再根据 type 做业务分发。
        var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(rawMessage);

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
        {
            return ToDispatchResult(CreateErrorResponse(null, 1001, "Invalid protocol envelope."));
        }

        return envelope.Type switch
        {
            //心跳请求
            "PingReq" => ToDispatchResult(CreatePingResponse(envelope)),
            //注册请求
            "RegisterReq" => ToDispatchResult(CreateRegisterResponse(envelope, accountService)),
            //登录请求
            "LoginReq" => ToDispatchResult(CreateLoginResponse(envelope, accountService, connectionId, connectionRegistry)),
            //进入大厅请求
            "EnterLobbyReq" => ToDispatchResult(CreateEnterLobbyResponse(envelope, accountService, roomService)),
            //创建房间请求：除了给请求者回包，还要把新的房间状态广播给房间成员
            "CreateRoomReq" => CreateCreateRoomDispatchResult(envelope, accountService, roomService),
            //加入房间请求：除了给请求者回包，还要通知房间内其他玩家成员列表变了
            "JoinRoomReq" => CreateJoinRoomDispatchResult(envelope, accountService, roomService),
            //离开房间请求：除了给请求者回包，还要通知留下来的玩家房主/成员列表变了
            "LeaveRoomReq" => CreateLeaveRoomDispatchResult(envelope, accountService, roomService),
            //未定义的响应
            _ => ToDispatchResult(CreateErrorResponse(envelope.RequestId, 1001, $"Unsupported message type: {envelope.Type}"))
        };
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "Invalid JSON message.");
        return ToDispatchResult(CreateErrorResponse(null, 1001, "Invalid JSON message."));
    }
}

static MessageDispatchResult ToDispatchResult(object response, RoomSnapshot? roomStateToNotify = null)
{
    return new MessageDispatchResult(JsonSerializer.Serialize(response), roomStateToNotify);
}

//业务分发

//心跳响应
static object CreatePingResponse(ProtocolEnvelope request)
{
    // clientTime 原样带回，客户端可以用“收到响应时间 - 发出请求时间”计算 RTT。
    var clientTime = request.ClientTime ?? TryGetPayloadClientTime(request.Payload) ?? 0;
    var serverTime = UnixTimeMilliseconds();

    return new
    {
        //信封的包装内容，每个信封必备
        msgId = DebugMessageIds.PingRes,
        type = "PingRes",
        requestId = request.RequestId,
        code = 0,
        message = "OK",
        serverTime,
        //具体的业务数据
        payload = new PingResponsePayload
        {
            ClientTime = clientTime,
            ServerTime = serverTime
        }
    };
}

//注册响应
static object CreateRegisterResponse(ProtocolEnvelope request, AccountService accountService)
{
    //处理注册请求，转换Payload的数据内容
    RegisterRequestPayload? payload = request.Payload.Deserialize<RegisterRequestPayload>();
    
    if (payload is null)
    {
        return CreateErrorResponse(request.RequestId, 1001, "Invalid RegisterReq payload.");
    }

    var result = accountService.Register(payload.Username, payload.Password, payload.Nickname);

    if (!result.Success)
    {
        return CreateErrorResponse(request.RequestId, result.Code, result.Message);
    }

    return new
    {
        //信封的包装内容，每个信封必备
        msgId = AccountMessageIds.RegisterRes,
        type = "RegisterRes",
        requestId = request.RequestId,
        code = result.Code,
        message = result.Message,
        serverTime = UnixTimeMilliseconds(),
        //具体的业务数据
        payload = new RegisterResponsePayload
        {
            PlayerId = result.PlayerId,
            Nickname = result.Nickname
        }
    };
}

//登录响应
static object CreateLoginResponse(
    ProtocolEnvelope request,
    AccountService accountService,
    string connectionId,
    ConnectionRegistry connectionRegistry)
{
    LoginRequestPayload? payload = request.Payload.Deserialize<LoginRequestPayload>();

    if (payload is null)
    {
        return CreateErrorResponse(request.RequestId, 1001, "Invalid LoginReq payload.");
    }

    var result = accountService.Login(payload.Username, payload.Password);

    if (!result.Success)
    {
        return CreateErrorResponse(request.RequestId, result.Code, result.Message);
    }

    var session = accountService.GetSession(result.Token);

    if (session is not null)
    {
        connectionRegistry.BindSession(connectionId, session);
    }

    return new
    {
        //信封的包装内容，每个信封必备
        msgId = AccountMessageIds.LoginRes,
        type = "LoginRes",
        requestId = request.RequestId,
        code = result.Code,
        message = result.Message,
        serverTime = UnixTimeMilliseconds(),
        //具体的业务数据
        payload = new LoginResponsePayload
        {
            Token = result.Token,
            PlayerId = result.PlayerId,
            Nickname = result.Nickname
        }
    };
}

//进入大厅响应
static object CreateEnterLobbyResponse(
    ProtocolEnvelope request,
    AccountService accountService,
    RoomService roomService)
{
    //拿到请求携带的令牌，就得知请求者的身份了
    var session = accountService.GetSession(request.Token);

    if (session is null)
    {
        return CreateErrorResponse(request.RequestId, 1002, "Login token is required.");
    }

    //拉取房间列表
    var rooms = roomService.GetRoomList()
        .Select(RoomDto.FromSnapshot)
        .ToList();

    return new
    {
        msgId = LobbyMessageIds.EnterLobbyRes,
        type = "EnterLobbyRes",
        requestId = request.RequestId,
        code = 0,
        message = "OK",
        serverTime = UnixTimeMilliseconds(),
        payload = new EnterLobbyResponsePayload
        {
            PlayerInfo = new RoomPlayerDto
            {
                PlayerId = session.PlayerId,
                Nickname = session.Nickname
            },
            Rooms = rooms
        }
    };
}

//创造房间分发结果
static MessageDispatchResult CreateCreateRoomDispatchResult(
    ProtocolEnvelope request,
    AccountService accountService,
    RoomService roomService)
{
    var session = accountService.GetSession(request.Token);

    if (session is null)
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
    }

    //拿到请求信封中的具体数据payload
    CreateRoomRequestPayload? payload = request.Payload.Deserialize<CreateRoomRequestPayload>();

    if (payload is null)
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid CreateRoomReq payload."));
    }

    var result = roomService.CreateRoom(session, payload.RoomName, payload.MaxPlayers);

    if (!result.Success || result.Room is null)
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
    }

    var response = new
    {
        msgId = RoomMessageIds.CreateRoomRes,
        type = "CreateRoomRes",
        requestId = request.RequestId,
        code = result.Code,
        message = result.Message,
        serverTime = UnixTimeMilliseconds(),
        payload = new CreateRoomResponsePayload
        {
            //不直接拿取result.Room，而是读取result.Room快照，避免修改服务器房间权威
            Room = RoomDto.FromSnapshot(result.Room)
        }
    };

    // 状态变更成功后，把同一份权威快照交给外层 HandleConnectionAsync 做 RoomStateNtf 广播。
    return ToDispatchResult(response, result.Room);
}

//加入房间分发结果
static MessageDispatchResult CreateJoinRoomDispatchResult(
    ProtocolEnvelope request,
    AccountService accountService,
    RoomService roomService)
{
    var session = accountService.GetSession(request.Token);

    if (session is null)
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
    }


    JoinRoomRequestPayload? payload = request.Payload.Deserialize<JoinRoomRequestPayload>();

    if (payload is null)
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid JoinRoomReq payload."));
    }

    //尝试加入房间，并获取行为结果
    var result = roomService.JoinRoom(session, payload.RoomId);

    if (!result.Success || result.Room is null)
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
    }

    var response = new
    {
        msgId = RoomMessageIds.JoinRoomRes,
        type = "JoinRoomRes",
        requestId = request.RequestId,
        code = result.Code,
        message = result.Message,
        serverTime = UnixTimeMilliseconds(),
        payload = new JoinRoomResponsePayload
        {
            Room = RoomDto.FromSnapshot(result.Room)
        }
    };

    return ToDispatchResult(response, result.Room);
}

//离开房间分发结果
static MessageDispatchResult CreateLeaveRoomDispatchResult(
    ProtocolEnvelope request,
    AccountService accountService,
    RoomService roomService)
{
    var session = accountService.GetSession(request.Token);

    if (session is null)
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
    }

    LeaveRoomRequestPayload? payload = request.Payload.Deserialize<LeaveRoomRequestPayload>();

    if (payload is null || string.IsNullOrWhiteSpace(payload.RoomId))
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid LeaveRoomReq payload."));
    }

    var result = roomService.LeaveRoom(session, payload.RoomId);

    if (!result.Success)
    {
        return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
    }

    var response = new
    {
        msgId = RoomMessageIds.LeaveRoomRes,
        type = "LeaveRoomRes",
        requestId = request.RequestId,
        code = result.Code,
        message = result.Message,
        serverTime = UnixTimeMilliseconds(),
        payload = new LeaveRoomResponsePayload
        {
            RoomId = payload.RoomId,
            Room = result.Room is null ? null : RoomDto.FromSnapshot(result.Room)
        }
    };

    // result.Room 为 null 代表最后一名玩家离开、房间销毁；这时已经没有房间成员需要广播。
    return ToDispatchResult(response, result.Room);
}

//错误响应
static object CreateErrorResponse(string? requestId, int code, string message)
{
    // 错误响应也保留 requestId，方便客户端把错误和原请求对应起来。
    return new
    {
        msgId = DebugMessageIds.ErrorRes,
        type = "ErrorRes",
        requestId,
        code,
        message,
        serverTime = UnixTimeMilliseconds(),
        payload = new { }
    };
}

//广播房间状态
static async Task BroadcastRoomStateAsync(
    RoomSnapshot room,
    ConnectionRegistry connectionRegistry,
    ILogger logger,
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
    var targets = connectionRegistry.GetConnectionsByPlayerIds(playerIds);

    foreach (var target in targets)
    {
        try
        {
            await target.SendTextAsync(message, cancellationToken);
            logger.LogInformation("Broadcast RoomStateNtf to {ConnectionId}: {Message}", target.ConnectionId, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast RoomStateNtf to {ConnectionId}", target.ConnectionId);
        }
    }
}

static long? TryGetPayloadClientTime(JsonElement payload)
{
    // 兼容两种写法：clientTime 可以放在信封顶层，也可以放在 payload 内。
    if (payload.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    return payload.TryGetProperty("clientTime", out var clientTimeElement) && clientTimeElement.TryGetInt64(out var clientTime)
        ? clientTime
        : null;
}

static long UnixTimeMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

sealed record MessageDispatchResult(
    string ResponseJson,
    RoomSnapshot? RoomStateToNotify);
