using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OnlineRpgServer.Protocol;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

var app = builder.Build();
var logger = app.Logger;
app.Urls.Add("http://localhost:5050");

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "OK",
    service = "OnlineRpgServer",
    serverTime = UnixTimeMilliseconds()
}));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket endpoint. Connect with ws://host:port/ws");
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("Client connected: {ConnectionId}", connectionId);

    await HandleConnectionAsync(socket, connectionId, logger, context.RequestAborted);

    logger.LogInformation("Client disconnected: {ConnectionId}", connectionId);
});

logger.LogInformation("OnlineRpgServer starting. WebSocket endpoint: ws://localhost:5050/ws");
app.Run();

static async Task HandleConnectionAsync(
    WebSocket socket,
    string connectionId,
    ILogger logger,
    CancellationToken cancellationToken)
{
    var buffer = new byte[4096];

    while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
    {
        var result = await ReceiveTextAsync(socket, buffer, cancellationToken);

        if (result is null)
        {
            break;
        }

        logger.LogInformation("Recv {ConnectionId}: {Message}", connectionId, result);

        var response = BuildResponse(result, logger);
        await SendTextAsync(socket, response, cancellationToken);

        logger.LogInformation("Send {ConnectionId}: {Message}", connectionId, response);
    }
}

static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
{
    using var stream = new MemoryStream();

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, cancellationToken);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", cancellationToken);
            return null;
        }

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
    var bytes = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
}

static string BuildResponse(string rawMessage, ILogger logger)
{
    try
    {
        var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(rawMessage);

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
        {
            return JsonSerializer.Serialize(CreateErrorResponse(null, 1001, "Invalid protocol envelope."));
        }

        return envelope.Type switch
        {
            "PingReq" => JsonSerializer.Serialize(CreatePingResponse(envelope)),
            _ => JsonSerializer.Serialize(CreateErrorResponse(envelope.RequestId, 1001, $"Unsupported message type: {envelope.Type}"))
        };
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "Invalid JSON message.");
        return JsonSerializer.Serialize(CreateErrorResponse(null, 1001, "Invalid JSON message."));
    }
}

static object CreatePingResponse(ProtocolEnvelope request)
{
    var clientTime = request.ClientTime ?? TryGetPayloadClientTime(request.Payload) ?? 0;
    var serverTime = UnixTimeMilliseconds();

    return new
    {
        msgId = DebugMessageIds.PingRes,
        type = "PingRes",
        requestId = request.RequestId,
        code = 0,
        message = "OK",
        serverTime,
        payload = new PingResponsePayload
        {
            ClientTime = clientTime,
            ServerTime = serverTime
        }
    };
}

static object CreateErrorResponse(string? requestId, int code, string message)
{
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
    if (payload.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    return payload.TryGetProperty("clientTime", out var clientTimeElement) && clientTimeElement.TryGetInt64(out var clientTime)
        ? clientTime
        : null;
}

static long UnixTimeMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
