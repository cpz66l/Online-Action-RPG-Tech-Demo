param(
    [string]$Url = "ws://localhost:5050/ws",
    [string]$RequestId = "smoke-test-001"
)

# Smoke Test：这个脚本临时扮演“客户端”，用于在 Unity 接入前验证服务端 WebSocket 链路。
# 运行前需要先启动服务端：dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
$ErrorActionPreference = "Stop"

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ct = [System.Threading.CancellationToken]::None

try {
    # 记录开始时间，收到 PingRes 后用于计算一次完整往返耗时。
    $startedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()

    # 标准 WebSocket 握手：连接 ws://localhost:5050/ws。
    $ws.ConnectAsync([Uri]$Url, $ct).GetAwaiter().GetResult() | Out-Null

    # 构造符合 ProtocolEnvelope 的 PingReq。requestId 用于验证响应是否匹配原请求。
    $clientTime = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $request = @{
        msgId = 9001
        type = "PingReq"
        requestId = $RequestId
        clientTime = $clientTime
        payload = @{
            clientTime = $clientTime
        }
    } | ConvertTo-Json -Compress

    # 以 UTF-8 文本形式发送 JSON；这和 Unity 未来要做的发送方式一致。
    $requestBytes = [System.Text.Encoding]::UTF8.GetBytes($request)
    $ws.SendAsync(
        [ArraySegment[byte]]::new($requestBytes),
        [System.Net.WebSockets.WebSocketMessageType]::Text,
        $true,
        $ct
    ).GetAwaiter().GetResult() | Out-Null

    # 等待服务端响应，并把 WebSocket Text Message 还原成 JSON 对象。
    $buffer = New-Object byte[] 4096
    $result = $ws.ReceiveAsync([ArraySegment[byte]]::new($buffer), $ct).GetAwaiter().GetResult()
    $receivedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $responseText = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count)
    $response = $responseText | ConvertFrom-Json

    # 核心断言 1：服务端必须返回 PingRes，不能返回 ErrorRes 或其他类型。
    if ($response.type -ne "PingRes") {
        throw "Expected PingRes, got '$($response.type)': $responseText"
    }

    # 核心断言 2：requestId 必须原样带回，证明请求/响应可以匹配。
    if ($response.requestId -ne $RequestId) {
        throw "Expected requestId '$RequestId', got '$($response.requestId)'."
    }

    # 输出给人看的验证结果：ok、协议类型、结果码、RTT、时间戳。
    [pscustomobject]@{
        ok = $true
        url = $Url
        requestId = $response.requestId
        responseType = $response.type
        code = $response.code
        rttMs = $receivedAt - $startedAt
        clientTime = $response.payload.clientTime
        serverTime = $response.payload.serverTime
    } | ConvertTo-Json -Compress
}
finally {
    # 无论验证成功或失败，都尽量正常关闭 WebSocket，避免服务端连接悬挂。
    if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        $ws.CloseAsync(
            [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
            "smoke test done",
            $ct
        ).GetAwaiter().GetResult() | Out-Null
    }

    $ws.Dispose()
}
