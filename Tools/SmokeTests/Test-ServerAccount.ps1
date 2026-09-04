param(
    [string]$Url = "ws://localhost:5050/ws",
    [string]$Username = "",
    [string]$Password = "123456",
    [string]$WrongPassword = "wrong-password",
    [string]$Nickname = "SmokePlayer"
)

# Smoke Test：这个脚本临时扮演“账号客户端”，用于验证服务端 Register / Login 协议。
# 运行前需要先启动服务端：dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Username)) {
    $Username = "smoke_" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
}

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None

function New-RequestId {
    return [Guid]::NewGuid().ToString("N")
}

function Send-TextMessage {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Message
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Message)
    $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(,$bytes)
    $sendTask = $Socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $script:ct)
    $sendTask.Wait()
}

function Receive-TextMessage {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket
    )

    $buffer = New-Object byte[] 4096
    $stream = New-Object System.IO.MemoryStream

    try {
        while ($true) {
            $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(,$buffer)
            $receiveTask = $Socket.ReceiveAsync($segment, $script:ct)
            $receiveTask.Wait()
            $result = $receiveTask.Result

            if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                throw "Server closed the WebSocket connection before sending a response."
            }

            if ($result.MessageType -ne [System.Net.WebSockets.WebSocketMessageType]::Text) {
                throw "Expected WebSocket text message, got '$($result.MessageType)'."
            }

            $stream.Write($buffer, 0, $result.Count)

            if ($result.EndOfMessage) {
                return [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
            }
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Invoke-AccountRequest {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [int]$MsgId,
        [string]$Type,
        [hashtable]$Payload
    )

    $requestId = New-RequestId
    $clientTime = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $requestObject = @{
        msgId = $MsgId
        type = $Type
        requestId = $requestId
        clientTime = $clientTime
        payload = $Payload
    }

    $requestJson = $requestObject | ConvertTo-Json -Compress -Depth 8
    Send-TextMessage -Socket $Socket -Message $requestJson

    $responseJson = Receive-TextMessage -Socket $Socket
    $response = $responseJson | ConvertFrom-Json

    return [pscustomobject]@{
        RequestId = $requestId
        RequestJson = $requestJson
        ResponseJson = $responseJson
        Response = $response
    }
}

function Assert-AccountResponse {
    param(
        [object]$Exchange,
        [string]$Scenario,
        [string]$ExpectedType,
        [int]$ExpectedCode
    )

    $response = $Exchange.Response


    if ($response.type -ne $ExpectedType) {
        throw "[$Scenario] Expected type '$ExpectedType', got '$($response.type)': $($Exchange.ResponseJson)"
    }

    if ($response.code -ne $ExpectedCode) {
        throw "[$Scenario] Expected code '$ExpectedCode', got '$($response.code)': $($Exchange.ResponseJson)"
    }

    if ($response.requestId -ne $Exchange.RequestId) {
        throw "[$Scenario] Expected requestId '$($Exchange.RequestId)', got '$($response.requestId)'."
    }
}

try {
    $connectTask = $ws.ConnectAsync([Uri]$Url, $ct)
    $connectTask.Wait()

    $registerPayload = @{
        username = $Username
        password = $Password
        nickname = $Nickname
    }

    $register = Invoke-AccountRequest -Socket $ws -MsgId 1001 -Type "RegisterReq" -Payload $registerPayload
    Assert-AccountResponse -Exchange $register -Scenario "register success" -ExpectedType "RegisterRes" -ExpectedCode 0

    if ([string]::IsNullOrWhiteSpace($register.Response.payload.playerId)) {
        throw "[register success] Expected non-empty playerId: $($register.ResponseJson)"
    }

    $duplicateRegister = Invoke-AccountRequest -Socket $ws -MsgId 1001 -Type "RegisterReq" -Payload $registerPayload
    Assert-AccountResponse -Exchange $duplicateRegister -Scenario "duplicate register" -ExpectedType "ErrorRes" -ExpectedCode 2001

    $duplicateRegisterNormalizedPayload = @{
        username = "  " + $Username.ToUpperInvariant() + "  "
        password = $Password
        nickname = $Nickname + "Again"
    }

    $duplicateRegisterNormalized = Invoke-AccountRequest -Socket $ws -MsgId 1001 -Type "RegisterReq" -Payload $duplicateRegisterNormalizedPayload
    Assert-AccountResponse -Exchange $duplicateRegisterNormalized -Scenario "duplicate register normalized username" -ExpectedType "ErrorRes" -ExpectedCode 2001

    $loginPayload = @{
        username = $Username
        password = $Password
    }

    $login = Invoke-AccountRequest -Socket $ws -MsgId 1003 -Type "LoginReq" -Payload $loginPayload
    Assert-AccountResponse -Exchange $login -Scenario "login success" -ExpectedType "LoginRes" -ExpectedCode 0

    if ([string]::IsNullOrWhiteSpace($login.Response.payload.token)) {
        throw "[login success] Expected non-empty token: $($login.ResponseJson)"
    }

    if ($login.Response.payload.playerId -ne $register.Response.payload.playerId) {
        throw "[login success] Expected playerId '$($register.Response.payload.playerId)', got '$($login.Response.payload.playerId)'."
    }

    $wrongLoginPayload = @{
        username = $Username
        password = $WrongPassword
    }

    $wrongLogin = Invoke-AccountRequest -Socket $ws -MsgId 1003 -Type "LoginReq" -Payload $wrongLoginPayload
    Assert-AccountResponse -Exchange $wrongLogin -Scenario "wrong password" -ExpectedType "ErrorRes" -ExpectedCode 2002

    $token = [string]$login.Response.payload.token
    $tokenPreview = $token
    if ($tokenPreview.Length -gt 18) {
        $tokenPreview = $tokenPreview.Substring(0, 18) + "..."
    }

    $resultObject = [pscustomobject]@{
        ok = $true
        url = $Url
        username = $Username
        nickname = $Nickname
        playerId = $register.Response.payload.playerId
        tokenPreview = $tokenPreview
        scenarios = @(
            [pscustomobject]@{ name = "register success"; responseType = $register.Response.type; code = $register.Response.code }
            [pscustomobject]@{ name = "duplicate register"; responseType = $duplicateRegister.Response.type; code = $duplicateRegister.Response.code }
            [pscustomobject]@{ name = "duplicate register normalized username"; responseType = $duplicateRegisterNormalized.Response.type; code = $duplicateRegisterNormalized.Response.code }
            [pscustomobject]@{ name = "login success"; responseType = $login.Response.type; code = $login.Response.code }
            [pscustomobject]@{ name = "wrong password"; responseType = $wrongLogin.Response.type; code = $wrongLogin.Response.code }
        )
    }

    $resultObject | ConvertTo-Json -Compress -Depth 8
}
catch [System.AggregateException] {
    $inner = $_.Exception.InnerException
    $innerMessage = if ($inner -ne $null) { $inner.Message } else { $_.Exception.Message }
    throw "Account smoke test failed. 请确认服务端已经启动，并且正在监听 $Url。原始错误：$innerMessage"
}
finally {
    if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        $closeTask = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "account smoke test done", $ct)
        $closeTask.Wait()
    }

    $ws.Dispose()
}

