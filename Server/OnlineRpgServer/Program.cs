using OnlineRpgServer.Account;
using OnlineRpgServer.Battle;
using OnlineRpgServer.Broadcast;
using OnlineRpgServer.Connection;
using OnlineRpgServer.Protocol;
using OnlineRpgServer.Room;
using static OnlineRpgServer.Protocol.ServerTime;

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

// 业务日志按模块分类：每个类注入自己的 ILogger<T>，控制台会显示到命名空间一级。
// BattleService 的 Tick 心跳用 LogDebug 打，默认（Information）不输出。
// 需要观察推进循环时用环境变量临时打开，不必改代码：
//   Logging__LogLevel__OnlineRpgServer.Battle.BattleService = Debug
// 反过来，如果某个模块太吵，也可以单独压到 Warning。


var app = builder.Build();  //生成真正可运行的WebApplication
var accountService = new AccountService();  //实例化账号服务
var roomService = new RoomService();        //实例化房间服务
var connectionRegistry = new ConnectionRegistry(); // 记录玩家和 WebSocket 连接的绑定关系

// 每个模块注入自己的 ILogger<T>，日志分类才会带上模块名。
// 这样控制台能分辨"这条是连接层打的"还是"战斗层打的"，
// 也能按分类单独调级别，不必为了去看 tick 而全局降到 Debug。
var battleService = new BattleService(
    app.Services.GetRequiredService<ILogger<BattleService>>(),
    connectionRegistry);

// 分发层：只把消息变成"回什么包 + 要顺带广播什么"，不直接写 socket。
var messageRouter = new MessageRouter(
    accountService,
    roomService,
    connectionRegistry,
    battleService,
    app.Services.GetRequiredService<ILogger<MessageRouter>>());

// 广播层：唯一往连接上写业务通知的地方。迭代05 起，BattleService 的 Tick 也从这里广播。
var broadcaster = new ServerBroadcaster(
    connectionRegistry,
    app.Services.GetRequiredService<ILogger<ServerBroadcaster>>());

// 连接层：串起收消息、分发、回包、按需广播和断线清理。
var connectionHandler = new ConnectionHandler(
    messageRouter,
    broadcaster,
    roomService,
    connectionRegistry,
    app.Services.GetRequiredService<ILogger<ConnectionHandler>>());


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

    var connection = connectionRegistry.Add(connectionId, socket);

    // 收消息、分发、断线清理都在 ConnectionHandler 里，端点只负责握手与注册。
    await connectionHandler.ServeAsync(connection, context.RequestAborted);     //！！！业务服务入口，阻塞当前线程直到连接断开
    //！！！每条连接一个独立 ServeAsync 循环
});

app.Logger.LogInformation("OnlineRpgServer starting. WebSocket endpoint: ws://localhost:5050/ws");

// 在开始监听前启动推进循环，保证第一个客户端进来时 Tick 已经在跑。
battleService.Start();

// 进程收到停止信号时关掉 Timer，避免退出阶段还在打日志。
app.Lifetime.ApplicationStopping.Register(battleService.Stop);

app.Run();//阻塞当前线程，让服务端一直运行监听请求。
