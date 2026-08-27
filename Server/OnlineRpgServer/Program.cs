using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OnlineRpgServer.Account;
using OnlineRpgServer.Protocol;

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
var accountService = new AccountService();  //实例化账号服务器

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

    // 当前连接的主循环：持续收消息、构造响应、发回客户端。
    await HandleConnectionAsync(socket, connectionId, logger, accountService,context.RequestAborted);

    logger.LogInformation("Client disconnected: {ConnectionId}", connectionId);
});

logger.LogInformation("OnlineRpgServer starting. WebSocket endpoint: ws://localhost:5050/ws");
app.Run();//阻塞当前线程，让服务端一直运行监听请求。

static async Task HandleConnectionAsync(
    WebSocket socket,
    string connectionId,
    ILogger logger,
    AccountService accountService,
    CancellationToken cancellationToken)
{
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

        logger.LogInformation("Recv {ConnectionId}: {Message}", connectionId, result);

        // 业务分发点：
        var response = BuildResponse(result, logger, accountService);//根据不同数据业务创建不同的响应

        await SendTextAsync(socket, response, cancellationToken);//将响应发回

        logger.LogInformation("Send {ConnectionId}: {Message}", connectionId, response);
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

static async Task SendTextAsync(WebSocket socket, string message, CancellationToken cancellationToken)
{
    // 当前协议格式是 JSON 文本，所以按 UTF-8 发送 WebSocket Text Message。
    var bytes = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
}

static string BuildResponse(string rawMessage, ILogger logger, AccountService accountService)
{
    try
    {
        // 先反序列化通用信封，再根据 type 做业务分发。
        var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(rawMessage);

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
        {
            return JsonSerializer.Serialize(CreateErrorResponse(null, 1001, "Invalid protocol envelope."));
        }

        return envelope.Type switch
        {
            //心跳请求
            "PingReq" => JsonSerializer.Serialize(CreatePingResponse(envelope)),
            //注册请求
            "RegisterReq" => JsonSerializer.Serialize(CreateRegisterResponse(envelope, accountService)),
            //登录请求
            "LoginReq" => JsonSerializer.Serialize(CreateLoginResponse(envelope, accountService)),
            //错误请求
            _ => JsonSerializer.Serialize(CreateErrorResponse(envelope.RequestId, 1001, $"Unsupported message type: {envelope.Type}"))
        };
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "Invalid JSON message.");
        return JsonSerializer.Serialize(CreateErrorResponse(null, 1001, "Invalid JSON message."));
    }
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
static object CreateLoginResponse(ProtocolEnvelope request, AccountService accountService)
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
