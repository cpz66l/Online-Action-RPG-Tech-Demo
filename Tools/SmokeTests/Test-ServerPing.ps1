param(
    [string]$Url = "ws://localhost:5050/ws",
    [string]$RequestId = "smoke-test-001"
)

$ErrorActionPreference = "Stop"

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ct = [System.Threading.CancellationToken]::None

try {
    $startedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $ws.ConnectAsync([Uri]$Url, $ct).GetAwaiter().GetResult() | Out-Null

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

    $requestBytes = [System.Text.Encoding]::UTF8.GetBytes($request)
    $ws.SendAsync(
        [ArraySegment[byte]]::new($requestBytes),
        [System.Net.WebSockets.WebSocketMessageType]::Text,
        $true,
        $ct
    ).GetAwaiter().GetResult() | Out-Null

    $buffer = New-Object byte[] 4096
    $result = $ws.ReceiveAsync([ArraySegment[byte]]::new($buffer), $ct).GetAwaiter().GetResult()
    $receivedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $responseText = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count)
    $response = $responseText | ConvertFrom-Json

    if ($response.type -ne "PingRes") {
        throw "Expected PingRes, got '$($response.type)': $responseText"
    }

    if ($response.requestId -ne $RequestId) {
        throw "Expected requestId '$RequestId', got '$($response.requestId)'."
    }

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
    if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        $ws.CloseAsync(
            [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
            "smoke test done",
            $ct
        ).GetAwaiter().GetResult() | Out-Null
    }

    $ws.Dispose()
}
